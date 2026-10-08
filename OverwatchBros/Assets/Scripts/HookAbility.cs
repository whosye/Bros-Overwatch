using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Ayranova sekera na retezu (klavesa E, jako Roadhoguv hak): hodi sekeru na retezu rovne dopredu (nejdal 'range' m).
// Prvniho nepritele, ktereho trefi, zrani ('power'), pritahne tesne pred Ayrana a na chvili omraci
// ('duration' s po dotazeni). Zed sekeru zastavi, spoluhrace proleti.
public class HookAbility : NetworkBehaviour, ICooldownCut
{
    public AbilityDefinition ability;

    public const float PullSeconds = 0.35f;
    const float HookRadius = 0.35f;
    const float PullDistance = 2.2f;   // jak daleko pred Ayranem pritazeny skonci

    FirstPersonController fpc;

    float nextUseTime;

    public float CooldownRemaining => Mathf.Max(0f, nextUseTime - Time.time);

    public void CutCooldown(float fraction)
    {
        if (nextUseTime > Time.time)
            nextUseTime = Time.time + (nextUseTime - Time.time) * (1f - Mathf.Clamp01(fraction));
    }

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
    }

    void Update()
    {
        if (!IsOwner || ability == null) return;
        if (!HeroInput.Pressed(this, HeroInput.Key.E) || !HeroInput.Locked(this)) return;
        if (fpc.InputBlocked || fpc.RushActive || fpc.BlockActive || Time.time < nextUseTime) return;

        nextUseTime = Time.time + ability.Cooldown;

        var eye = fpc.playerCamera.transform;
        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.6f);
        GetComponent<PlayerHero>().SayAbility(ability);
        ThrowServerRpc(eye.position, eye.forward);
    }

    // ---------------- server ----------------

    [ServerRpc]
    void ThrowServerRpc(Vector3 origin, Vector3 direction)
    {
        var match = MatchManager.Instance;
        if (ability == null || direction.sqrMagnitude < 0.01f || (match != null && (match.IsOver || match.IsLobby))) return;

        direction.Normalize();
        Vector3 end = origin + direction * ability.range;
        Health victim = null;

        var hits = Physics.SphereCastAll(origin, HookRadius, direction, ability.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && (owner == NetworkObject || Combat.SameTeam(gameObject, owner.gameObject))) continue;
            if (hit.collider.GetComponentInParent<BoulderHitbox>() != null) continue;

            end = hit.distance > 0f ? hit.point : origin;

            var health = hit.collider.GetComponentInParent<Health>();
            if (health != null && health.currentHealth.Value > 0f)
                victim = health;
            break;
        }

        if (victim == null)
        {
            HookFxClientRpc(end, default, false);
            return;
        }

        Combat.DamagePlayer(gameObject, victim, ability.power, Combat.AbilitySource(ability));

        var controller = victim.GetComponent<FirstPersonController>();
        if (controller != null && victim.currentHealth.Value > 0f)
        {
            // Cil: kousek pred Ayranem ve smeru, kterym se diva (ve vysce jeho nohou).
            Vector3 forward = direction;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : transform.forward;
            Vector3 destination = transform.position + forward * PullDistance;

            controller.ServerPull(destination, PullSeconds);
            controller.ServerStun(PullSeconds + ability.duration, gameObject, false);
        }

        HookFxClientRpc(end, victim.NetworkObject, true);
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void HookFxClientRpc(Vector3 end, NetworkObjectReference victimRef, bool hit)
    {
        Transform victim = null;
        if (hit && victimRef.TryGet(out NetworkObject victimObject))
            victim = victimObject.transform;

        // Na retezu leti stejna sekera, jakou hrdina drzi v ruce.
        var playerHero = GetComponent<PlayerHero>();
        var weapon = playerHero != null && playerHero.Hero != null ? playerHero.Hero.weapon : null;
        HookVisual.Spawn(transform, end, victim, weapon != null ? weapon.heldPrefab : null);
        if (hit)
        {
            ProceduralSfx.Play(ProceduralSfx.Hit, end, 1f);
            ProceduralSfx.Play(ProceduralSfx.Empty, end, 1f);
        }
    }
}

// Sekera na retezu (jen efekt): tocici se sekera vyleti k cili, a kdyz nekoho zasekla, drzi se ho, dokud ho nepritahne.
public class HookVisual : MonoBehaviour
{
    const float OutSeconds = 0.12f;
    const float BackSeconds = 0.2f;

    Transform owner, victim;
    Vector3 end;
    float age;
    LineRenderer line;
    Transform tip;
    Transform axe;

    public static void Spawn(Transform owner, Vector3 end, Transform victim, GameObject axePrefab = null)
    {
        var go = new GameObject("HookVisual");
        var visual = go.AddComponent<HookVisual>();
        visual.owner = owner;
        visual.end = end;
        visual.victim = victim;

        visual.line = go.AddComponent<LineRenderer>();
        visual.line.positionCount = 2;
        visual.line.startWidth = visual.line.endWidth = 0.05f;
        visual.line.material = Fx.NewLit(new Color(0.55f, 0.56f, 0.60f));
        visual.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        visual.tip = new GameObject("Tip").transform;
        visual.tip.SetParent(go.transform, false);

        if (axePrefab != null)
        {
            var model = Instantiate(axePrefab, visual.tip);
            foreach (var collider in model.GetComponentsInChildren<Collider>())
                DestroyImmediate(collider);
            visual.axe = model.transform;
        }
        else
        {
            // Nahradni sekera z kostek: topurko a hlava.
            visual.axe = new GameObject("Axe").transform;
            visual.axe.SetParent(visual.tip, false);
            Block(visual.axe, new Vector3(0f, 0.25f, 0f), new Vector3(0.045f, 0.56f, 0.045f), new Color(0.45f, 0.28f, 0.12f));
            Block(visual.axe, new Vector3(0f, 0.47f, 0.10f), new Vector3(0.03f, 0.20f, 0.20f), new Color(0.78f, 0.80f, 0.84f));
        }

        // Sekera drzi retez koncem topurka; pri letu se toci hlavou napred.
        visual.axe.localPosition = Vector3.zero;
        visual.axe.localRotation = Quaternion.Euler(90f, 0f, 0f);

        visual.Place();
    }

    void Update()
    {
        age += Time.deltaTime;

        float lifetime = OutSeconds + (victim != null ? HookAbility.PullSeconds + 0.05f : BackSeconds);
        if (owner == null || age >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        Place();
    }

    void Place()
    {
        Vector3 hand = owner.position + Vector3.up * 1.25f + owner.forward * 0.4f;
        Vector3 hookPoint;

        if (age < OutSeconds)
            hookPoint = Vector3.Lerp(hand, end, age / OutSeconds);
        else if (victim != null)
            hookPoint = victim.position + Vector3.up * 1.2f;
        else
            hookPoint = Vector3.Lerp(end, hand, (age - OutSeconds) / BackSeconds);

        line.SetPosition(0, hand);
        line.SetPosition(1, hookPoint);
        tip.position = hookPoint;
        if ((hookPoint - hand).sqrMagnitude > 0.001f)
            tip.rotation = Quaternion.LookRotation(hookPoint - hand);

        // Cestou k cili se sekera toci, zaseknuta (nebo pri navratu) uz ne.
        float spin = age < OutSeconds ? age * 1800f : 0f;
        axe.localRotation = Quaternion.Euler(90f + spin, 0f, 0f);
    }

    static void Block(Transform parent, Vector3 position, Vector3 scale, Color color)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(block.GetComponent<Collider>());
        block.transform.SetParent(parent, false);
        block.transform.localPosition = position;
        block.transform.localScale = scale;
        Fx.Paint(block, color);
    }
}
