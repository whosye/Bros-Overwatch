using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Flankerova ultimatni schopnost (Q): pulzni bomba. Hodi lepivou bombu obloukem; prilepi se na prvniho nepritele,
// ktereho trefi (nebo na zem / zed), a po 'duration' sekundach vybuchne: okruh 'radius' m, 'power' poskozeni
// ve stredu (u kraje 30 %). Prilepena na hraci jde s nim. Nabiji se hrou, zvuk je slyset pres celou mapu.
public class PulseBombAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    public static readonly Color BombColor = new Color(0.35f, 0.75f, 1f, 1f);
    const float Gravity = 14f;

    FirstPersonController fpc;
    PlayerHero hero;
    float nextUseTime;

    bool UsesCharge => hero != null && hero.UsesUltCharge;
    bool CanUse => UsesCharge ? hero.UltReady : Time.time >= nextUseTime;
    public float CooldownRemaining => UsesCharge ? 0f : Mathf.Max(0f, nextUseTime - Time.time);

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
    }

    void Update()
    {
        if (!IsOwner || ability == null) return;
        if (!Keyboard.current.qKey.wasPressedThisFrame || !GameSettings.CursorLocked) return;
        if (fpc.InputBlocked || !CanUse) return;

        nextUseTime = Time.time + ability.Cooldown;
        hero.SpendUlt();
        hero.SayAbility(ability);

        var eye = fpc.playerCamera.transform;
        ThrowServerRpc(eye.position + eye.forward * 0.6f, eye.forward);
    }

    [ServerRpc]
    void ThrowServerRpc(Vector3 origin, Vector3 direction)
    {
        var match = MatchManager.Instance;
        if (ability == null || direction.sqrMagnitude < 0.01f || (match != null && (match.IsOver || match.IsLobby))) return;

        Vector3 velocity = direction.normalized * Mathf.Max(5f, ability.speed);
        var go = new GameObject("PulseBomb");
        go.transform.position = origin;
        go.AddComponent<PulseBombSim>().Init(this, velocity);

        var recorder = GetComponent<PotgRecorder>();
        if (recorder != null)
            recorder.ServerNoteUltimate();

        ThrownClientRpc(origin, velocity);
    }

    // ---------------- volano simulaci bomby (server) ----------------

    public void ServerStuck(NetworkObject target, Vector3 point)
    {
        if (target != null)
            StuckToPlayerClientRpc(target, target.transform.InverseTransformPoint(point));
        else
            StuckClientRpc(point);
    }

    public void ServerExplode(Vector3 point)
    {
        Combat.Explode(gameObject, point, ability.radius, ability.power, 0.3f);
        ExplodedClientRpc(point);
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void ThrownClientRpc(Vector3 origin, Vector3 velocity)
    {
        Fx.PlayGlobal(ProceduralSfx.UltCharge, 0.5f);
        PulseBombVisual.Spawn(this, origin, velocity);
    }

    [ClientRpc]
    void StuckToPlayerClientRpc(NetworkObjectReference targetRef, Vector3 localPoint)
    {
        if (targetRef.TryGet(out NetworkObject target))
            PulseBombVisual.StickTo(this, target.transform, localPoint);
        ProceduralSfx.Play(ProceduralSfx.Hit, transform.position, 0.5f);
    }

    [ClientRpc]
    void StuckClientRpc(Vector3 point)
    {
        PulseBombVisual.StickAt(this, point);
    }

    [ClientRpc]
    void ExplodedClientRpc(Vector3 point)
    {
        PulseBombVisual.Remove(this);
        Fx.Explosion(point, ability != null ? ability.radius : 3f);
        Fx.Sparks(point, BombColor);
    }

    public static Vector3 Step(ref Vector3 velocity, float dt)
    {
        velocity.y -= Gravity * dt;
        return velocity * dt;
    }
}

// Serverova simulace bomby: let obloukem, prilepeni, odpocet a vybuch.
public class PulseBombSim : MonoBehaviour
{
    PulseBombAbility owner;
    Vector3 velocity;
    Transform stuckTo;
    Vector3 stuckLocal;
    bool stuck;
    float explodeAt;
    float lifetime = 6f;

    public void Init(PulseBombAbility thrower, Vector3 startVelocity)
    {
        owner = thrower;
        velocity = startVelocity;
    }

    void FixedUpdate()
    {
        if (owner == null || !owner.IsSpawned)
        {
            Destroy(gameObject);
            return;
        }

        if (stuck)
        {
            if (stuckTo != null)
                transform.position = stuckTo.TransformPoint(stuckLocal);
            if (Time.time >= explodeAt)
            {
                owner.ServerExplode(transform.position);
                Destroy(gameObject);
            }
            return;
        }

        lifetime -= Time.fixedDeltaTime;
        Vector3 start = transform.position;
        Vector3 step = PulseBombAbility.Step(ref velocity, Time.fixedDeltaTime);
        float distance = step.magnitude;

        var hits = Physics.SphereCastAll(start, 0.15f, step / Mathf.Max(0.0001f, distance), distance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var network = hit.collider.GetComponentInParent<NetworkObject>();
            if (network != null && (network == owner.NetworkObject || Combat.SameTeam(owner.gameObject, network.gameObject))) continue;

            Vector3 point = hit.distance > 0f ? hit.point : start;
            bool player = network != null && network.GetComponent<Health>() != null;
            Stick(player ? network : null, point);
            return;
        }

        transform.position = start + step;
        if (lifetime <= 0f)
            Stick(null, transform.position);
    }

    void Stick(NetworkObject target, Vector3 point)
    {
        stuck = true;
        transform.position = point;
        if (target != null)
        {
            stuckTo = target.transform;
            stuckLocal = stuckTo.InverseTransformPoint(point);
        }
        explodeAt = Time.time + owner.ability.duration;
        owner.ServerStuck(target, point);
    }
}

// Vizual bomby na klientech: let obloukem (stejna fyzika jako server), pak pripevneni a blikani.
public class PulseBombVisual : MonoBehaviour
{
    static readonly System.Collections.Generic.Dictionary<PulseBombAbility, PulseBombVisual> active =
        new System.Collections.Generic.Dictionary<PulseBombAbility, PulseBombVisual>();

    Vector3 velocity;
    bool stuck;
    Transform follow;
    Vector3 local;
    Light glow;
    float age;

    public static void Spawn(PulseBombAbility owner, Vector3 origin, Vector3 startVelocity)
    {
        Remove(owner);
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "PulseBombVisual";
        go.transform.position = origin;
        go.transform.localScale = Vector3.one * 0.22f;
        Fx.Paint(go, PulseBombAbility.BombColor);
        var visual = go.AddComponent<PulseBombVisual>();
        visual.velocity = startVelocity;
        visual.glow = go.AddComponent<Light>();
        visual.glow.type = LightType.Point;
        visual.glow.color = PulseBombAbility.BombColor;
        visual.glow.range = 3f;
        visual.glow.intensity = 2f;
        active[owner] = visual;
    }

    public static void StickTo(PulseBombAbility owner, Transform target, Vector3 localPoint)
    {
        if (!active.TryGetValue(owner, out var visual) || visual == null) return;
        visual.stuck = true;
        visual.follow = target;
        visual.local = localPoint;
    }

    public static void StickAt(PulseBombAbility owner, Vector3 point)
    {
        if (!active.TryGetValue(owner, out var visual) || visual == null) return;
        visual.stuck = true;
        visual.transform.position = point;
    }

    public static void Remove(PulseBombAbility owner)
    {
        if (active.TryGetValue(owner, out var visual) && visual != null)
            Destroy(visual.gameObject);
        active.Remove(owner);
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age > 10f)
        {
            Destroy(gameObject);
            return;
        }

        if (!stuck)
            transform.position += PulseBombAbility.Step(ref velocity, Time.deltaTime);
        else if (follow != null)
            transform.position = follow.TransformPoint(local);

        // Po prilepeni blika cim dal rychleji.
        if (stuck && glow != null)
            glow.intensity = 1.5f + Mathf.Abs(Mathf.Sin(age * age * 3f)) * 5f;
    }
}
