using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Annina ultimatni schopnost (Q): Posileni. Spoluhrac v zamerovaci (do 'range' m, s primym vyhledem) dostane na
// 'duration' sekund posileni: dava o 50 % vetsi poskozeni, dostava o polovinu mensi a hned se vyleci o 'power' HP.
// Kdyz v zamerovaci neni spoluhrac, ultimatka se nespotrebuje. Na sebe ji Anna pouzit nemuze (jako v Overwatchi).
public class NanoBoostAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    public const float DamageMultiplier = 1.5f;
    public const float DamageTakenMultiplier = 0.5f;

    FirstPersonController fpc;
    PlayerHero hero;
    float nextUseTime;
    float nextHint;

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

        var target = FindAlly();
        if (target == null)
        {
            // Nikdo v zamerovaci: kratky "prazdny" zvuk, ultimatka zustava.
            if (Time.time >= nextHint)
            {
                nextHint = Time.time + 0.5f;
                ProceduralSfx.Play(ProceduralSfx.Empty, transform.position, 0.6f);
            }
            return;
        }

        nextUseTime = Time.time + ability.Cooldown;
        hero.SpendUlt();
        hero.SayAbility(ability);
        BoostServerRpc(target.NetworkObject);
    }

    // Spoluhrac nejblize stredu zamerovace (siroky paprsek, at se netrefuje pixelove presne).
    PlayerHero FindAlly()
    {
        var eye = fpc.playerCamera.transform;
        var hits = Physics.SphereCastAll(eye.position, 0.9f, eye.forward, ability.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var other = hit.collider.GetComponentInParent<PlayerHero>();
            if (other == null || other == hero) continue;
            if (!Combat.SameTeam(gameObject, other.gameObject)) continue;

            var health = other.GetComponent<Health>();
            if (health == null || health.currentHealth.Value <= 0f) continue;
            if (!Combat.HasLineOfSight(eye.position, hit.collider)) continue;
            return other;
        }
        return null;
    }

    // ---------------- server ----------------

    [ServerRpc]
    void BoostServerRpc(NetworkObjectReference targetRef)
    {
        var match = MatchManager.Instance;
        if (ability == null || (match != null && (match.IsOver || match.IsLobby))) return;
        if (!targetRef.TryGet(out NetworkObject targetObject)) return;

        var target = targetObject.GetComponent<PlayerHero>();
        var health = targetObject.GetComponent<Health>();
        if (target == null || health == null || target == hero || health.currentHealth.Value <= 0f) return;
        if (!Combat.SameTeam(gameObject, target.gameObject)) return;
        if (Vector3.Distance(transform.position, target.transform.position) > ability.range + 5f) return;

        target.ServerBoost(ability.duration);
        Combat.HealPlayer(gameObject, health, ability.power);

        var recorder = GetComponent<PotgRecorder>();
        if (recorder != null)
            recorder.ServerNoteUltimate();

        BoostedClientRpc(target.transform.position);
    }

    [ClientRpc]
    void BoostedClientRpc(Vector3 position)
    {
        // Ultimatka je slyset pres celou mapu.
        Fx.PlayGlobal(ProceduralSfx.UltCharge, 0.5f);
        Fx.Sparks(position + Vector3.up * 1.2f, NanoAura.AuraColor);
    }
}

// Viditelna aura posileneho hrace: modre svetlo a stoupajici jiskry kolem tela.
public class NanoAura : MonoBehaviour
{
    public static readonly Color AuraColor = new Color(0.45f, 0.8f, 1f, 1f);

    Light glow;
    ParticleSystem particles;

    public static void Set(Transform player, bool on)
    {
        ReplayLog.NanoAura(player, on);
        var aura = player.GetComponentInChildren<NanoAura>(true);
        if (!on)
        {
            if (aura != null)
                Destroy(aura.gameObject);
            return;
        }
        if (aura != null) return;

        var go = new GameObject("NanoAura");
        go.transform.SetParent(player, false);
        go.transform.localPosition = new Vector3(0f, 1f, 0f);
        aura = go.AddComponent<NanoAura>();
        aura.Build();
    }

    void Build()
    {
        glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = AuraColor;
        glow.range = 4f;
        glow.intensity = 3f;

        particles = gameObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(AuraColor, Color.white);
        main.gravityModifier = -0.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.rateOverTime = 45f;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.45f;
        shape.radiusThickness = 0f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;
        particles.Play();
    }

    void Update()
    {
        if (glow != null)
            glow.intensity = 2.6f + Mathf.Sin(Time.time * 9f) * 0.8f;
    }
}
