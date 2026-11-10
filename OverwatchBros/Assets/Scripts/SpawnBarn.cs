using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Stodola u mista oziveni (kazdy objekt "Stodola..." s podlahou "Podlaha"; najde se sam pri nacteni sceny):
//  - uvnitr se hraci regeneruje zdravi 2x rychleji (Health se pta SpawnBarn.RegenScale),
//  - kdyz je to prave spawn nektereho tymu, maji vsechny otvory pod preklady ("Preklad...") zlute pruhledne
//    dvere (jako v Overwatchi): vlastni tym prochazi, nepritel dovnitr nesmi (kdo uz uvnitr je, smi ven),
//  - behem odpoctu pred zacatkem zapasu (utok/obrana) jsou dvere utocniku zavrene uplne (obranci ven smi),
// Dvere nemaji kolizi (strely jimi prolétaji): pruchod hlida ten, kdo postavu ridi (hrac svou, server boty) -
// kdyz by postava nepovolene prekrocila hranu stodoly, vrati se na posledni povolene misto.
// Kdo vlastni stodolu, se pocita z mist oziveni (SpawnZone) - nic dalsiho se neposila po siti.
public class SpawnBarn : MonoBehaviour
{
    public const float RegenMultiplier = 2f;
    const float Inset = 0.3f;          // tloustka sten - uvnitr = za stenou
    const float InsideHeight = 4.5f;   // nad tim (strecha) uz neni uvnitr
    const float SpawnMargin = 2.5f;    // spawn tesne pred stodolou se pocita taky

    static readonly List<SpawnBarn> all = new List<SpawnBarn>();

    Transform floor;
    Bounds floorBounds;   // lokalne v 'floor'
    readonly List<Renderer> doors = new List<Renderer>();
    Material doorMaterial;
    int owner = -1;
    bool setupLock;
    float nextOwnerCheck;
    readonly Dictionary<FirstPersonController, (bool inside, Vector3 position)> tracked = new Dictionary<FirstPersonController, (bool, Vector3)>();

    // ---------------- vyhledani stodol ----------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Attach();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach();

    static void Attach()
    {
        foreach (var t in Object.FindObjectsByType<Transform>())
        {
            if (!t.name.StartsWith("Stodola") || t.GetComponent<SpawnBarn>() != null) continue;
            var podlaha = t.Find("Podlaha");
            if (podlaha == null || podlaha.GetComponent<MeshFilter>() == null) continue;
            t.gameObject.AddComponent<SpawnBarn>();
        }
    }

    void Awake()
    {
        floor = transform.Find("Podlaha");
        floorBounds = floor.GetComponent<MeshFilter>().sharedMesh.bounds;
        BuildDoors();
    }

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    void OnDestroy()
    {
        if (doorMaterial != null) Destroy(doorMaterial);
    }

    // ---------------- dotazy ----------------

    // Je bod uvnitr stodoly (za stenami, pod strechou)?
    public bool Contains(Vector3 world, float margin = -Inset, float below = 0.6f)
    {
        Vector3 p = floor.InverseTransformPoint(world);
        Vector3 c = floorBounds.center, e = floorBounds.extents;
        float top = c.y + e.y;
        return Mathf.Abs(p.x - c.x) <= e.x + margin && Mathf.Abs(p.z - c.z) <= e.z + margin
            && p.y >= top - below && p.y <= top + InsideHeight;
    }

    // Stodola, ve ktere (nebo tesne pred kterou) je dane misto oziveni; null = spawn neni u stodoly.
    public static SpawnBarn AtSpawn(Vector3 spawn)
    {
        foreach (var barn in all)
            if (barn != null && barn.Contains(spawn, SpawnMargin, 2f)) return barn;
        return null;
    }

    // Nasobek regenerace zdravi pro hrace na danem miste (Health, server).
    public static float RegenScale(Vector3 position)
    {
        foreach (var barn in all)
            if (barn != null && barn.Contains(position)) return RegenMultiplier;
        return 1f;
    }

    // ---------------- dvere ----------------

    void BuildDoors()
    {
        float floorTop = floor.TransformPoint(floorBounds.center + Vector3.up * floorBounds.extents.y).y;
        var template = Resources.Load<Material>("Fx/Particle");
        doorMaterial = template != null ? new Material(template) : new Material(Shader.Find("Sprites/Default"));
        doorMaterial.mainTexture = null;
        doorMaterial.color = new Color(1f, 0.82f, 0.1f, 0.3f);

        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("Preklad")) continue;
            var filter = child.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            Bounds b = filter.sharedMesh.bounds;
            float bottom = child.TransformPoint(b.center - Vector3.up * b.extents.y).y;
            float height = bottom - floorTop;
            if (height < 1f) continue;

            var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = "DvereSpawnu";
            Destroy(door.GetComponent<Collider>());   // strely i hraci (podle pravidel nize) prochazi
            door.transform.SetParent(child, false);
            door.transform.localPosition = b.center - Vector3.up * (b.extents.y + height * 0.5f / Mathf.Max(0.01f, child.lossyScale.y));
            door.transform.localRotation = Quaternion.identity;
            var scale = child.lossyScale;
            door.transform.localScale = new Vector3(b.size.x * 0.98f, height / Mathf.Max(0.01f, scale.y), Mathf.Min(b.size.x, b.size.z) * 0.25f);
            // tenka strana podel tloustky steny
            if (b.size.x < b.size.z)
                door.transform.localScale = new Vector3(b.size.x * 0.25f, height / Mathf.Max(0.01f, scale.y), b.size.z * 0.98f);
            var renderer = door.GetComponent<Renderer>();
            renderer.sharedMaterial = doorMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            doors.Add(renderer);
        }
    }

    // ---------------- prubeh ----------------

    void Update()
    {
        if (Time.time >= nextOwnerCheck)
        {
            nextOwnerCheck = Time.time + 0.2f;
            UpdateOwner();
        }

        bool shown = owner >= 0;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f);
        if (shown)
            doorMaterial.color = setupLock
                ? new Color(1f, 0.6f, 0.05f, 0.5f + 0.1f * pulse)
                : new Color(1f, 0.82f, 0.1f, 0.24f + 0.08f * pulse);
        foreach (var door in doors)
            if (door != null && door.enabled != shown) door.enabled = shown;
    }

    void UpdateOwner()
    {
        owner = -1;
        setupLock = false;
        var match = MatchManager.Instance;
        if (match == null || !match.IsSpawned || match.IsLobby) return;
        for (int team = 0; team < PlayerTeam.TeamCount; team++)
            if (SpawnZone.TryGetCenter(team, out Vector3 spawn) && Contains(spawn, SpawnMargin, 2f))
            {
                owner = team;
                break;
            }
        setupLock = owner >= 0 && match.IsAttackMode && match.roundPhase.Value == MatchManager.RoundSetup
            && owner == match.attackTeam.Value;   // jen stodola utocniku; obranci se pri priprave rozestavuji
    }

    // Po pohybu postav (LateUpdate): nepovoleny prechod hranou stodoly = zpet.
    void LateUpdate()
    {
        foreach (var fpc in FirstPersonController.All)
        {
            if (fpc == null || !fpc.IsSpawned || !fpc.IsOwner) continue;
            Vector3 position = fpc.transform.position;
            bool inside = Contains(position);
            if (!tracked.TryGetValue(fpc, out var last) || owner < 0 || (position - last.position).sqrMagnitude > 9f)
            {
                // novy, stodola neni spawn, nebo teleport/oziveni (velky skok) - jen zapamatovat
                tracked[fpc] = (inside, position);
                continue;
            }
            if (inside != last.inside && !Allowed(fpc, inside))
            {
                PutBack(fpc, last.position);
                continue;
            }
            tracked[fpc] = (inside, position);
        }
    }

    bool Allowed(FirstPersonController fpc, bool entering)
    {
        if (setupLock) return false;
        if (!entering) return true;   // ven smi kazdy
        var team = fpc.GetComponent<PlayerTeam>();
        return team != null && team.teamId.Value == owner;
    }

    static void PutBack(FirstPersonController fpc, Vector3 position)
    {
        var body = fpc.GetComponent<CharacterController>();
        if (body != null) body.enabled = false;
        fpc.transform.position = position;
        if (body != null) body.enabled = true;
    }
}
