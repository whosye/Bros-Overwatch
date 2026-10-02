using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Honzova past na medvedy (klavesa E): polozi past na zem pred sebe. Prvni nepritel, ktery na ni slapne,
// dostane 'power' poskozeni a na 'duration' sekund se nemuze hybat (strilet muze). Venku muze byt jen jedna past.
public class TrapAbility : NetworkBehaviour
{
    public AbilityDefinition ability;
    public float placeDistance = 1.8f;
    public float armDelay = 0.6f;

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

        nextUseTime = Time.time + ability.cooldown;
        placed = true;
        ProceduralSfx.Play(ProceduralSfx.Reload, transform.position, 0.6f);
        GetComponent<PlayerHero>().SayAbility(ability);
        PlaceServerRpc(point);
    }

    // Misto na zemi kousek pred hracem (ne za zdi).
    bool FindGround(out Vector3 point)
    {
        Vector3 chest = transform.position + Vector3.up * 1.1f;
        Vector3 forward = transform.forward;
        float reach = placeDistance;

        foreach (var hit in Physics.RaycastAll(chest, forward, placeDistance + 0.4f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
            reach = Mathf.Min(reach, Mathf.Max(0.3f, hit.distance - 0.4f));
        }

        var down = Physics.RaycastAll(chest + forward * reach, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
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
        if (Vector3.Distance(point, transform.position) > placeDistance + 5f) return;

        trapActive = true;
        trapPosition = point;
        armedAt = Time.time + armDelay;
        PlacedClientRpc(point);
    }

    [ServerRpc]
    void RemoveServerRpc()
    {
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

    void Snap()
    {
        trapActive = false;
        SnappedClientRpc(trapPosition, ability.duration);
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void PlacedClientRpc(Vector3 point)
    {
        if (visual != null)
            Destroy(visual);

        visual = BuildVisual();
        visual.transform.position = point + Vector3.up * 0.02f;
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

        if (jawA != null) jawA.localRotation = Quaternion.Euler(0f, 0f, -78f);
        if (jawB != null) jawB.localRotation = Quaternion.Euler(0f, 0f, 78f);
        Destroy(visual, Mathf.Max(0.5f, holdSeconds));
        visual = null;
    }

    GameObject BuildVisual()
    {
        var root = new GameObject("TrapVisual");
        float radius = ability != null ? Mathf.Clamp(ability.radius * 0.6f, 0.35f, 0.7f) : 0.5f;

        var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(plate.GetComponent<Collider>());
        plate.transform.SetParent(root.transform, false);
        plate.transform.localScale = new Vector3(radius * 0.9f, 0.015f, radius * 0.9f);
        Fx.Paint(plate, new Color(0.20f, 0.21f, 0.23f));

        jawA = BuildJaw(root.transform, radius, -1f);
        jawB = BuildJaw(root.transform, radius, 1f);
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
