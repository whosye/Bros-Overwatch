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

    // Stred podlahy stodoly (kam si bot dojde lecit).
    public Vector3 Center => floor.TransformPoint(floorBounds.center + Vector3.up * floorBounds.extents.y) + Vector3.up * 0.1f;

    // Muze se tam hrac tymu 'team' dostat? (bot si nevybira cil v nepratelske spawn stodole)
    public static bool BlockedFor(int team, Vector3 position)
    {
        foreach (var barn in all)
            if (barn != null && barn.owner >= 0 && barn.owner != team && barn.Contains(position)) return true;
        return false;
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

        // Otvory se hledaji ve skutecnych zdech (i kdyz je nekdo posunul / pridal): po obvodu podlahy se kazdych
        // 25 cm ve vysce 1,2 m zkusi, jestli je ve zdi stena; souvisly usek bez steny (aspon 0,8 m) = otvor -> dvere
        // od podlahy po preklad (nebo 3,6 m).
        Physics.SyncTransforms();
        Vector3 c = floorBounds.center, e = floorBounds.extents;

        // 1) pod kazdym prekladem ("Preklad...") dvere pres celou sirku otvoru (puvodni otvory stodoly)
        foreach (var lintel in GetComponentsInChildren<Transform>())
        {
            if (!lintel.name.StartsWith("Preklad") || !lintel.TryGetComponent<Renderer>(out var lr)) continue;
            Bounds lb = lr.bounds;
            Vector3 lc = floor.InverseTransformPoint(lb.center);
            // ktera strana: nejblizsi okraj podlahy
            float dz = e.z - Mathf.Abs(lc.z - c.z), dx = e.x - Mathf.Abs(lc.x - c.x);
            bool lAlongX = dz < dx;
            float lSign = lAlongX ? Mathf.Sign(lc.z - c.z) : Mathf.Sign(lc.x - c.x);
            // sirka = delsi vodorovny rozmer site prekladu (nezavisle na natoceni stodoly)
            var lf = lintel.GetComponent<MeshFilter>();
            Vector3 ls = lf != null && lf.sharedMesh != null ? Vector3.Scale(lf.sharedMesh.bounds.size, lintel.lossyScale) : lb.size;
            float width = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z));
            float mid = lAlongX ? (lc.x - c.x) / e.x : (lc.z - c.z) / e.z;
            float half = width * 0.5f / (lAlongX ? e.x : e.z);
            AddDoor(lAlongX, lSign, mid - half, mid + half, c, e, floorTop);
        }

        for (int side = 0; side < 4; side++)
        {
            // strana: osa podel zdi, smer ven, poloha zdi (lokalne v podlaze)
            bool alongX = side < 2;
            float sign = side % 2 == 0 ? -1f : 1f;
            float length = alongX ? e.x * 2f : e.z * 2f;
            int steps = Mathf.Max(2, Mathf.RoundToInt(length / 0.25f));
            float gapStart = -1f;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                bool open = i < steps && IsGap(alongX, sign, Mathf.Lerp(-1f, 1f, t), c, e, floorTop);
                float at = Mathf.Lerp(-1f, 1f, t);
                if (open && gapStart < 0f) gapStart = at;
                if (!open && gapStart >= 0f)
                {
                    // 2) mezera ve zdi bez prekladu (posunute / pridane zdi): jen kdyz tam uz dvere nejsou
                    if (!Covered(alongX, sign, (gapStart + at) * 0.5f, c, e))
                        AddDoor(alongX, sign, gapStart, at, c, e, floorTop);
                    gapStart = -1f;
                }
            }
        }
        var widths = new System.Text.StringBuilder();
        foreach (var d in doors) widths.Append($" {d.transform.lossyScale.x:0.0}m");
        Debug.Log($"[Stodola] {name}: dveri {doors.Count} (sirky:{widths})");
    }

    // Je misto na zdi uz zakryte nekterymi dvermi?
    bool Covered(bool alongX, float sign, float along, Vector3 c, Vector3 e)
    {
        Vector3 p = WallPoint(alongX, sign, along, 0.15f, c, e, 0f);
        foreach (var d in doors)
        {
            Vector3 local = d.transform.InverseTransformPoint(new Vector3(p.x, d.transform.position.y, p.z));
            if (Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.z) <= 8f) return true;
        }
        return false;
    }

    Vector3 WallPoint(bool alongX, float sign, float along, float inward, Vector3 c, Vector3 e, float height)
    {
        Vector3 local = alongX
            ? new Vector3(c.x + along * e.x, 0f, c.z + sign * (e.z - inward))
            : new Vector3(c.x + sign * (e.x - inward), 0f, c.z + along * e.z);
        Vector3 world = floor.TransformPoint(local);
        world.y = height;
        return world;
    }

    Vector3 Outward(bool alongX, float sign) => (alongX ? floor.forward : floor.right) * sign;

    bool IsGap(bool alongX, float sign, float along, Vector3 c, Vector3 e, float floorTop)
    {
        if (Mathf.Abs(along) > 0.97f) return false;   // rohy
        // zacatek tesne pred vnitrni stranou zdi (zed ~0,3 m) - seno a bedny kousek od zdi otvor nezakryji
        Vector3 from = WallPoint(alongX, sign, along, 0.42f, c, e, floorTop + 1.2f);
        foreach (var hit in Physics.RaycastAll(from, Outward(alongX, sign), 0.8f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.collider.GetComponentInParent<PlayerHero>() == null) return false;
        return true;
    }

    void AddDoor(bool alongX, float sign, float a0, float a1, Vector3 c, Vector3 e, float floorTop)
    {
        float halfLength = alongX ? e.x : e.z;
        float width = (a1 - a0) * halfLength;
        if (width < 0.8f) return;
        float mid = (a0 + a1) * 0.5f;
        // vyska: od podlahy po spodek prekladu nad otvorem (nebo 3,6 m)
        Vector3 bottom = WallPoint(alongX, sign, mid, 0.15f, c, e, floorTop + 0.3f);
        float height = 3.6f;
        if (Physics.Raycast(bottom, Vector3.up, out var top, 6f, ~0, QueryTriggerInteraction.Ignore))
            height = Mathf.Clamp(top.point.y - floorTop, 1.2f, 6f);

        var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = "DvereSpawnu";
        Destroy(door.GetComponent<Collider>());   // strely i hraci (podle pravidel nize) prochazi
        door.transform.SetParent(transform, true);
        door.transform.position = WallPoint(alongX, sign, mid, 0.15f, c, e, floorTop + height * 0.5f);
        door.transform.rotation = Quaternion.LookRotation(Outward(alongX, sign), Vector3.up);
        door.transform.localScale = Vector3.one;
        var parentScale = transform.lossyScale;
        door.transform.localScale = new Vector3((width + 0.05f) / Mathf.Max(0.01f, parentScale.x), height / Mathf.Max(0.01f, parentScale.y), 0.06f / Mathf.Max(0.01f, parentScale.z));
        var renderer = door.GetComponent<Renderer>();
        renderer.sharedMaterial = doorMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        doors.Add(renderer);
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
        int phase = match.roundPhase.Value;
        // priprava: zavrena jen stodola utocniku (obranci se rozestavuji);
        // mezi body (po dobyti, nez se dalsi bod odemkne): zavrene obe
        bool setup = phase == MatchManager.RoundSetup && owner == match.attackTeam.Value;
        bool betweenPoints = match.pointIndex.Value > 0 && match.lockSecondsLeft.Value > 0
            && (phase == MatchManager.RoundAttack || phase == MatchManager.RoundOvertime);
        setupLock = owner >= 0 && match.IsAttackMode && (setup || betweenPoints);
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
