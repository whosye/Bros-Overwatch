using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Honzova past na medvedy (klavesa E): polozi past na zem tam, kam miri zamerovac (nejdal 'range' m, max 20 m).
// Prvni nepritel, ktery na ni slapne, dostane 'power' poskozeni a na 'duration' sekund se nemuze hybat (strilet muze).
// Venku muze byt jen jedna past. Nepratele ji muzou rozstrilet (ma 'TrapHealth' zivotu).
public class TrapAbility : NetworkBehaviour
{
    public AbilityDefinition ability;
    public float armDelay = 0.6f;
    public const float TrapHealth = 50f;
    const float MaxPlaceDistance = 20f;

    static readonly Color Steel = new Color(0.55f, 0.57f, 0.60f);

    FirstPersonController fpc;
    Health health;

    // vlastnik
    float nextUseTime;
    bool placed;

    // server
    bool trapActive;
    Vector3 trapPosition;
    float armedAt;
    float trapHealth;
    int throwId;   // zvysi se pri kazdem hodu / zruseni (pozdni dopad stareho hodu se zahodi)

    float PlaceRange => ability != null && ability.range > 0f ? Mathf.Min(ability.range, MaxPlaceDistance) : 12f;

    // vizual
    GameObject visual;
    TrapJaws jaws;

    // vyska modelu pasti vuci jeho sirce (prefab HonzaTrap ma sirku 1) a kolik z ni je polozene pasti v zemi
    const float TrapModelHeight = 0.28f, TrapSink = 0.4f;

    // model pasti ze Sketchfabu (Editor/TrapSetup: sirka 1, spodek v pocatku, celisti JawFront / JawBack na ose pantu)
    static GameObject trapPrefab;
    static bool trapPrefabLoaded;

    public float CooldownRemaining => Mathf.Max(0f, nextUseTime - Time.time);

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        health = GetComponent<Health>();
    }

    public override void OnNetworkDespawn()
    {
        if (visual != null)
            Destroy(visual);
    }

    void OnDisable()
    {
        Cancel();
    }

    void Update()
    {
        if (IsOwner)
            OwnerUpdate();

        if (IsServer && trapActive && Time.time >= armedAt)
            ServerWatch();
    }

    void OwnerUpdate()
    {
        if (ability == null) return;
        if (!HeroInput.Pressed(this, HeroInput.Key.E) || !HeroInput.Locked(this)) return;
        if (fpc.InputBlocked || Time.time < nextUseTime) return;
        if (!FindGround(out Vector3 point)) return;

        nextUseTime = Time.time + ability.Cooldown;
        placed = true;
        ProceduralSfx.PlayForActor(this, ProceduralSfx.Reload, transform.position, 0.6f);
        GetComponent<PlayerHero>().SayAbility(ability);
        PlaceServerRpc(point);
    }

    // Misto na zemi tam, kam miri zamerovac. Kdyz zamerovac miri na zed, past spadne k jejimu patu;
    // kdyz miri do vzduchu, polozi se pod konec dosahu.
    bool FindGround(out Vector3 point)
    {
        var eye = fpc.playerCamera.transform;
        Vector3 aimPoint = eye.position + eye.forward * PlaceRange;

        var hits = Physics.RaycastAll(eye.position, eye.forward, PlaceRange, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
            if (hit.collider.GetComponentInParent<TrapHitbox>() != null) continue;

            if (hit.normal.y > 0.5f)
            {
                point = hit.point;
                return true;
            }
            aimPoint = hit.point + hit.normal * 0.4f;
            break;
        }

        var down = Physics.RaycastAll(aimPoint + Vector3.up * 0.2f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(down, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in down)
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
            if (hit.collider.GetComponentInParent<BoulderHitbox>() != null) continue;

            point = hit.point;
            return hit.normal.y > 0.5f;
        }

        point = default;
        return false;
    }

    // Vola PlayerRespawn (vlastnik): po smrti / restartu past zmizi.
    public void Cancel()
    {
        if (!placed) return;

        placed = false;
        if (IsSpawned && IsOwner && NetworkManager != null && !NetworkManager.ShutdownInProgress)
            RemoveServerRpc();
    }

    // ---------------- server ----------------

    [ServerRpc]
    void PlaceServerRpc(Vector3 point)
    {
        var match = MatchManager.Instance;
        if (ability == null || (match != null && (match.IsOver || match.IsLobby))) return;
        if (Vector3.Distance(point, transform.position) > PlaceRange + 6f) return;

        // Past se hodi obloukem z ruky; polozi se az po dopadu.
        Vector3 from = transform.position + Vector3.up * 1.45f + transform.right * -0.25f + transform.forward * 0.35f;
        float flight = Mathf.Clamp(0.25f + Vector3.Distance(from, point) * 0.025f, 0.3f, 0.7f);
        trapActive = false;
        int id = ++throwId;
        ThrownClientRpc(from, point, flight);
        StartCoroutine(Land(id, point, flight));
    }

    IEnumerator Land(int id, Vector3 point, float flight)
    {
        yield return new WaitForSeconds(flight);
        if (id != throwId || health.currentHealth.Value <= 0f) yield break;

        trapActive = true;
        trapPosition = point;
        trapHealth = TrapHealth;
        armedAt = Time.time + armDelay;
        PlacedClientRpc(point);
    }

    [ServerRpc]
    void RemoveServerRpc()
    {
        throwId++;
        if (!trapActive) return;

        trapActive = false;
        RemovedClientRpc();
    }

    void ServerWatch()
    {
        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;

        foreach (var col in Physics.OverlapSphere(trapPosition + Vector3.up * 0.4f, ability.radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var dummy = col.GetComponentInParent<Target>();
            if (dummy != null)
            {
                dummy.TakeDamage(ability.power);
                Snap("Terč");
                return;
            }

            var victim = col.GetComponentInParent<Health>();
            if (victim == null || victim == health || victim.currentHealth.Value <= 0f) continue;
            if (Combat.SameTeam(gameObject, victim.gameObject)) continue;

            Combat.DamagePlayer(gameObject, victim, ability.power, Combat.AbilitySource(ability));

            var controller = victim.GetComponent<FirstPersonController>();
            if (controller != null && victim.currentHealth.Value > 0f)
                controller.ServerRoot(ability.duration);

            var caughtHero = victim.GetComponent<PlayerHero>();
            Snap(caughtHero != null ? caughtHero.DisplayName : "");
            return;
        }
    }

    // Zasah do pasti (zbran, vybuch, uder) - jen server; spoluhraci ji nezniceji.
    public void ServerDamage(GameObject attacker, float amount)
    {
        if (!IsServer || !trapActive || amount <= 0f) return;
        if (attacker == gameObject || Combat.SameTeam(attacker, gameObject)) return;

        trapHealth -= amount;
        if (trapHealth > 0f) return;

        trapActive = false;
        DestroyedClientRpc(trapPosition);
    }

    [ClientRpc]
    void DestroyedClientRpc(Vector3 point)
    {
        placed = false;
        Fx.Sparks(point + Vector3.up * 0.2f, Steel);
        Fx.BulletImpact(point + Vector3.up * 0.2f, Steel, 2f);
        ProceduralSfx.Play(ProceduralSfx.Hit, point, 1f);
        if (visual != null)
            Destroy(visual);
        visual = null;
    }

    void Snap(string caught)
    {
        trapActive = false;
        SnappedClientRpc(trapPosition, ability.duration);

        // majitel pasti se ozve - slysi to jen on (Resources/Banter/<hrdina>_past, napr. honza_past)
        var owner = GetComponent<PlayerHero>();
        if (owner != null && owner.Hero != null)
            owner.ServerSayFolderLine(owner.Hero.name.ToLowerInvariant() + "_past", true);

        // znacka "chycen v pasti" jen tomu, kdo past polozil (bot nic)
        if (BotBrain.IsBot(gameObject) || (owner != null && owner.isBot.Value)) return;
        var target = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } } };
        CaughtClientRpc(caught ?? "", target);
    }

    [ClientRpc]
    void CaughtClientRpc(string caught, ClientRpcParams rpcParams = default)
    {
        if (IsOwner) HudUI.NotifyTrap(caught);
    }

    // ---------------- vizual ----------------

    // Letici past: zavrena se otaci v oblouku z ruky na misto dopadu (pak ji nahradi polozena past).
    [ClientRpc]
    void ThrownClientRpc(Vector3 from, Vector3 to, float flight)
    {
        var held = GetComponent<HeldWeapons>();
        if (held != null)
            held.PlayThrow();
        ProceduralSfx.PlayForActor(this, ProceduralSfx.Dash, from, 0.4f);

        var flying = BuildVisual(out var flyingJaws);
        flying.name = "TrapThrow";
        flyingJaws.Close(true);
        flying.transform.localScale = Vector3.one * 0.8f;
        flying.AddComponent<TrapFlight>().Begin(from, to, flight);
    }

    [ClientRpc]
    void PlacedClientRpc(Vector3 point)
    {
        if (visual != null)
            Destroy(visual);

        visual = BuildVisual(out jaws);
        visual.transform.position = point + Vector3.up * 0.02f;
        // polozena past je vizualne zapustena do zeme (ven koukaji zuby a vrsek kruhu); poloha pasti, zasahova
        // zona i chyceni zustavaji, kde byly (jen model jde dolu)
        var trapModel = visual.transform.Find("TrapModel");
        if (trapModel != null)
        {
            float radius = ability != null ? Mathf.Clamp(ability.radius * 0.6f, 0.35f, 0.7f) : 0.5f;
            trapModel.localPosition = Vector3.down * (radius * 2.2f * TrapModelHeight * TrapSink + 0.02f);
        }

        // Zasahova zona pasti (aby ji slo rozstrilet); nizka, takze se pres ni da prejit.
        float size = ability != null ? Mathf.Clamp(ability.radius * 1.2f, 0.7f, 1.4f) : 1f;
        var box = visual.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.12f, 0f);
        box.size = new Vector3(size, 0.24f, size);
        visual.AddComponent<TrapHitbox>().Owner = this;
        visual.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        ProceduralSfx.Play(ProceduralSfx.Hit, point, 0.4f);
    }

    [ClientRpc]
    void RemovedClientRpc()
    {
        if (visual != null)
            Destroy(visual);
        visual = null;
    }

    // Past sklapne: celisti se zavrou a po dobu znehybneni zustane lezet.
    [ClientRpc]
    void SnappedClientRpc(Vector3 point, float holdSeconds)
    {
        placed = false;
        Fx.Sparks(point + Vector3.up * 0.3f, Steel);
        ProceduralSfx.Play(CollectedAudioBank.Pick("honza_trap", ProceduralSfx.Hit), point, 0.9f, 25f);

        if (visual == null) return;

        var box = visual.GetComponent<BoxCollider>();
        if (box != null)
            Destroy(box);
        if (jaws != null) jaws.Close(false);   // rychle sklapnuti
        Destroy(visual, Mathf.Max(0.5f, holdSeconds));
        visual = null;
    }

    GameObject BuildVisual(out TrapJaws trapJaws)
    {
        var root = new GameObject("TrapVisual");
        float radius = ability != null ? Mathf.Clamp(ability.radius * 0.6f, 0.35f, 0.7f) : 0.5f;
        trapJaws = root.AddComponent<TrapJaws>();

        if (!trapPrefabLoaded)
        {
            trapPrefabLoaded = true;
            trapPrefab = Resources.Load<GameObject>("Weapons/HonzaTrap");
        }
        if (trapPrefab != null)
        {
            // model: kruh celisti zhruba o prumeru 2 * radius (sirka i s naboji o kus vetsi)
            var model = Instantiate(trapPrefab, root.transform);
            model.name = "TrapModel";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * radius * 2.2f;
            foreach (var collider in model.GetComponentsInChildren<Collider>()) Destroy(collider);
            Transform front = null, back = null;
            foreach (var part in model.GetComponentsInChildren<Transform>(true))
            {
                if (front == null && part.name.StartsWith("JawFront")) front = part;
                else if (back == null && part.name.StartsWith("JawBack")) back = part;
            }
            // celisti se zavrou otocenim kolem osy pantu (lokalni X) nahoru proti sobe
            trapJaws.Init(front, Quaternion.Euler(-80f, 0f, 0f), back, Quaternion.Euler(80f, 0f, 0f));
            return root;
        }

        var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        DestroyImmediate(plate.GetComponent<Collider>());
        plate.transform.SetParent(root.transform, false);
        plate.transform.localScale = new Vector3(radius * 0.9f, 0.015f, radius * 0.9f);
        Fx.Paint(plate, new Color(0.20f, 0.21f, 0.23f));

        var jawLeft = BuildJaw(root.transform, radius, -1f);
        var jawRight = BuildJaw(root.transform, radius, 1f);
        trapJaws.Init(jawLeft, Quaternion.Euler(0f, 0f, -78f), jawRight, Quaternion.Euler(0f, 0f, 78f));
        return root;
    }

    // Jedna celist: pulkruh zubu, otaci se kolem osy Z ve stredu pasti.
    static Transform BuildJaw(Transform parent, float radius, float side)
    {
        var jaw = new GameObject("Jaw").transform;
        jaw.SetParent(parent, false);

        const int teeth = 7;
        for (int i = 0; i < teeth; i++)
        {
            float angle = Mathf.Lerp(-80f, 80f, i / (teeth - 1f)) * Mathf.Deg2Rad;
            var tooth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(tooth.GetComponent<Collider>());
            tooth.transform.SetParent(jaw, false);
            tooth.transform.localPosition = new Vector3(side * Mathf.Cos(angle) * radius, 0.05f, Mathf.Sin(angle) * radius);
            tooth.transform.localRotation = Quaternion.Euler(0f, -side * angle * Mathf.Rad2Deg, side * 35f);
            tooth.transform.localScale = new Vector3(0.05f, 0.14f, 0.07f);
            Fx.Paint(tooth, Steel);
        }

        return jaw;
    }
}

// Celisti pasti: otevrene (jak jsou v modelu) a zavrene (otoceni 'closed' navic); Close sklapne rychle nebo hned.
public class TrapJaws : MonoBehaviour
{
    const float CloseSeconds = 0.07f;

    Transform a, b;
    Quaternion aOpen, bOpen, aClosed, bClosed;
    float t = -1f;

    public void Init(Transform jawA, Quaternion closeA, Transform jawB, Quaternion closeB)
    {
        a = jawA; b = jawB;
        if (a != null) { aOpen = a.localRotation; aClosed = aOpen * closeA; }
        if (b != null) { bOpen = b.localRotation; bClosed = bOpen * closeB; }
        enabled = false;
    }

    public void Close(bool instant)
    {
        t = instant ? CloseSeconds : 0f;
        Apply();
        enabled = !instant;
    }

    void Update()
    {
        t += Time.deltaTime;
        Apply();
        if (t >= CloseSeconds) enabled = false;
    }

    void Apply()
    {
        float u = Mathf.Clamp01(t / CloseSeconds);
        u = u * u;   // zrychluje az do sklapnuti
        if (a != null) a.localRotation = Quaternion.Slerp(aOpen, aClosed, u);
        if (b != null) b.localRotation = Quaternion.Slerp(bOpen, bClosed, u);
    }
}

// Let hozene pasti (jen vizual): parabola z ruky na misto dopadu s rotaci, pak zmizi.
public class TrapFlight : MonoBehaviour
{
    Vector3 from, to;
    float duration, elapsed;
    float apex;

    public void Begin(Vector3 start, Vector3 end, float seconds)
    {
        from = start;
        to = end + Vector3.up * 0.05f;
        duration = Mathf.Max(0.05f, seconds);
        apex = 0.5f + Vector3.Distance(start, end) * 0.08f;
        transform.position = from;
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        transform.position = Vector3.Lerp(from, to, t) + Vector3.up * (4f * apex * t * (1f - t));
        Vector3 flat = Vector3.ProjectOnPlane(to - from, Vector3.up);
        Vector3 axis = flat.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
        transform.rotation = Quaternion.AngleAxis(t * 540f, axis);
        if (t >= 1f)
            Destroy(gameObject);
    }
}

// Znacka na Honzove pasti: podle ni zbrane a vybuchy poznaji, ze trefily past, a komu patri.
public class TrapHitbox : MonoBehaviour
{
    public TrapAbility Owner;

    public bool IsFriendly(GameObject attacker)
    {
        return Owner == null || attacker == Owner.gameObject || Combat.SameTeam(attacker, Owner.gameObject);
    }

    // Jen server.
    public void Damage(GameObject attacker, float amount)
    {
        if (Owner != null)
            Owner.ServerDamage(attacker, amount);
    }
}
