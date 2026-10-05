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
    Transform jawA, jawB;

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
        if (!Keyboard.current.eKey.wasPressedThisFrame || !GameSettings.CursorLocked) return;
        if (fpc.InputBlocked || Time.time < nextUseTime) return;
        if (!FindGround(out Vector3 point)) return;

        nextUseTime = Time.time + ability.Cooldown;
        placed = true;
        ProceduralSfx.Play(ProceduralSfx.Reload, transform.position, 0.6f);
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
                Snap();
                return;
            }

            var victim = col.GetComponentInParent<Health>();
            if (victim == null || victim == health || victim.currentHealth.Value <= 0f) continue;
            if (Combat.SameTeam(gameObject, victim.gameObject)) continue;

            Combat.DamagePlayer(gameObject, victim, ability.power);

            var controller = victim.GetComponent<FirstPersonController>();
            if (controller != null && victim.currentHealth.Value > 0f)
                controller.ServerRoot(ability.duration);

            Snap();
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

    void Snap()
    {
        trapActive = false;
        SnappedClientRpc(trapPosition, ability.duration);
    }

    // ---------------- vizual ----------------

    // Letici past: zavrena se otaci v oblouku z ruky na misto dopadu (pak ji nahradi polozena past).
    [ClientRpc]
    void ThrownClientRpc(Vector3 from, Vector3 to, float flight)
    {
        var held = GetComponent<HeldWeapons>();
        if (held != null)
            held.PlayThrow();
        ProceduralSfx.Play(ProceduralSfx.Dash, from, 0.4f);

        var flying = BuildVisual(out var a, out var b);
        flying.name = "TrapThrow";
        if (a != null) a.localRotation = Quaternion.Euler(0f, 0f, -78f);
        if (b != null) b.localRotation = Quaternion.Euler(0f, 0f, 78f);
        flying.transform.localScale = Vector3.one * 0.8f;
        flying.AddComponent<TrapFlight>().Begin(from, to, flight);
    }

    [ClientRpc]
    void PlacedClientRpc(Vector3 point)
    {
        if (visual != null)
            Destroy(visual);

        visual = BuildVisual(out jawA, out jawB);
        visual.transform.position = point + Vector3.up * 0.02f;

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
        ProceduralSfx.Play(ProceduralSfx.Hit, point, 1f);
        ProceduralSfx.Play(ProceduralSfx.Empty, point, 1f);

        if (visual == null) return;

        var box = visual.GetComponent<BoxCollider>();
        if (box != null)
            Destroy(box);
        if (jawA != null) jawA.localRotation = Quaternion.Euler(0f, 0f, -78f);
        if (jawB != null) jawB.localRotation = Quaternion.Euler(0f, 0f, 78f);
        Destroy(visual, Mathf.Max(0.5f, holdSeconds));
        visual = null;
    }

    GameObject BuildVisual(out Transform jawLeft, out Transform jawRight)
    {
        var root = new GameObject("TrapVisual");
        float radius = ability != null ? Mathf.Clamp(ability.radius * 0.6f, 0.35f, 0.7f) : 0.5f;

        var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(plate.GetComponent<Collider>());
        plate.transform.SetParent(root.transform, false);
        plate.transform.localScale = new Vector3(radius * 0.9f, 0.015f, radius * 0.9f);
        Fx.Paint(plate, new Color(0.20f, 0.21f, 0.23f));

        jawLeft = BuildJaw(root.transform, radius, -1f);
        jawRight = BuildJaw(root.transform, radius, 1f);
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
            Destroy(tooth.GetComponent<Collider>());
            tooth.transform.SetParent(jaw, false);
            tooth.transform.localPosition = new Vector3(side * Mathf.Cos(angle) * radius, 0.05f, Mathf.Sin(angle) * radius);
            tooth.transform.localRotation = Quaternion.Euler(0f, -side * angle * Mathf.Rad2Deg, side * 35f);
            tooth.transform.localScale = new Vector3(0.05f, 0.14f, 0.07f);
            Fx.Paint(tooth, Steel);
        }

        return jaw;
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
