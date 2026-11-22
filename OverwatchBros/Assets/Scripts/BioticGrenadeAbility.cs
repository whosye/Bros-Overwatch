using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Annin biotický granat (E): hod dopredu (nejdal 'range' m), po dopadu vybuchne. Spoluhrace v okruhu 'radius'
// (i Annu) vyleci o 'power' HP, nepratele o 'power' zrani a na 'duration' sekund jim zablokuje leceni.
// Zasazeni spoluhraci (i David sam) navic dostanou slabsi posileni nez z Q: o PowerScale vetsi poskozeni
// na PowerSeconds s; nepratele zpomali na SlowSeconds s (bez snizeni prijimaneho poskozeni; Q = +50 % poskozeni, -50 % prijimaneho, 12 s).
// Neprochazi zdmi.
public class BioticGrenadeAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    static readonly Color GrenadeColor = new Color(0.55f, 1f, 0.65f, 1f);
    static readonly Color HarmColor = new Color(0.75f, 0.35f, 1f, 1f);

    public const float PowerScale = 1.2f;
    public const float PowerSeconds = 4f;
    // nepratele v okruhu navic zpomali
    public const float SlowSeconds = 2f;
    public const float SlowFactor = 0.6f;

    FirstPersonController fpc;
    float nextUseTime;

    // Do kdy ma mistni hrac posileni z granatu (ikona v BuffUI).
    static float localPowerUntil;
    public static float LocalPowerUntil => Time.time < localPowerUntil ? localPowerUntil : 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => localPowerUntil = 0f;

    // Obrazek buffu = ikona granatu.
    public static Texture BuffIcon()
    {
        foreach (var hero in HeroRegistry.All)
            foreach (var a in new[] { hero.ability, hero.secondaryAbility, hero.altAbility, hero.rmbAbility, hero.blockAbility })
                if (a != null && a.name == "BioticGrenade_Data" && a.icon != null) return a.icon;
        return null;
    }

    public float CooldownRemaining => Mathf.Max(0f, nextUseTime - Time.time);

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
        if (fpc.InputBlocked || Time.time < nextUseTime) return;

        nextUseTime = Time.time + ability.Cooldown;

        var eye = fpc.playerCamera.transform;
        ProceduralSfx.PlayForActor(this, CollectedAudioBank.ForActor(this, "david_grenade", ProceduralSfx.Dash), transform.position, 0.5f);
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

        // Granat leti rovne a vybuchne o prvni prekazku nebo hrace (i spoluhrace), nejdal po 'range' metrech.
        float distance = ability.range;
        var hits = Physics.SphereCastAll(origin, 0.14f, direction, ability.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && owner == NetworkObject) continue;

            distance = Mathf.Max(0.3f, hit.distance - 0.1f);
            break;
        }

        Vector3 point = origin + direction * distance;
        float flight = distance / Mathf.Max(1f, ability.speed);
        ThrownClientRpc(origin + direction * 0.5f, point, flight);
        StartCoroutine(Detonate(point, flight));
    }

    IEnumerator Detonate(Vector3 point, float delay)
    {
        yield return new WaitForSeconds(delay);

        var match = MatchManager.Instance;
        if (match == null || (!match.IsOver && !match.IsLobby))
        {
            var touched = new HashSet<Health>();
            foreach (var col in Physics.OverlapSphere(point, ability.radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var dummy = col.GetComponentInParent<Target>();
                if (dummy != null)
                    dummy.TakeDamage(ability.power);

                var target = col.GetComponentInParent<Health>();
                if (target == null || !touched.Add(target) || target.currentHealth.Value <= 0f) continue;
                if (!Combat.HasLineOfSight(point + Vector3.up * 0.3f, col)) continue;

                bool friend = target.gameObject == gameObject || Combat.SameTeam(gameObject, target.gameObject);
                if (friend)
                {
                    Combat.HealPlayer(gameObject, target, ability.power);
                    if (target.TryGetComponent<PlayerHero>(out var ally))
                    {
                        ally.ServerDamageBuff(PowerScale, PowerSeconds);
                        PoweredClientRpc(ally.NetworkObjectId, PowerSeconds);
                    }
                }
                else
                {
                    Combat.DamagePlayer(gameObject, target, ability.power, Combat.AbilitySource(ability));
                    target.ServerBlockHealing(ability.duration);
                    if (target.TryGetComponent<FirstPersonController>(out var slowed))
                        slowed.ServerSlow(SlowSeconds, SlowFactor);
                }
            }
        }

        DetonatedClientRpc(point);
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void PoweredClientRpc(ulong targetId, float seconds)
    {
        var network = NetworkManager.Singleton;
        if (network == null || network.SpawnManager == null || !network.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var target)) return;
        Fx.Sparks(target.transform.position + Vector3.up * 1.2f, GrenadeColor);
        if (target.IsOwner && !BotBrain.IsBot(target.gameObject))
            localPowerUntil = Time.time + seconds;
    }

    [ClientRpc]
    void ThrownClientRpc(Vector3 from, Vector3 to, float seconds)
    {
        if (!IsOwner)
            ProceduralSfx.PlayForActor(this, CollectedAudioBank.ForActor(this, "david_grenade", ProceduralSfx.Dash), from, 0.5f);
        // hazi zelenou plechovku Monsteru (Q = bila)
        var can = new GameObject("BioticGrenade_Monster");
        MonsterCan.Build(can.transform, true);
        can.AddComponent<GreenCanFlight>().Init(from, to, seconds, GrenadeColor);
    }

    [ClientRpc]
    void DetonatedClientRpc(Vector3 point)
    {
        float radius = ability != null ? ability.radius : 4f;
        Fx.BulletImpact(point, GrenadeColor, radius);
        Fx.Sparks(point, GrenadeColor);
        Fx.Sparks(point + Vector3.up * 0.4f, HarmColor);
        ProceduralSfx.Play(ProceduralSfx.Hit, point, 1f);

        var lightObject = new GameObject("BioticLight");
        lightObject.transform.position = point + Vector3.up * 0.5f;
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = GrenadeColor;
        light.range = radius * 2.5f;
        light.intensity = 6f;
        Destroy(lightObject, 0.35f);
    }
}

// Let zelene plechovky (biotický granat): obloukem k mistu dopadu, toci se, za sebou zelena stopa.
public class GreenCanFlight : MonoBehaviour
{
    Vector3 from, to;
    float seconds, age;

    public void Init(Vector3 start, Vector3 end, float flight, Color color)
    {
        from = start;
        to = end;
        seconds = Mathf.Max(0.02f, flight);
        transform.position = start;

        var trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.minVertexDistance = 0.1f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.12f), new Keyframe(1f, 0f));
        trail.material = Fx.ParticleMaterial;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = gradient;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / seconds);
        float arc = Mathf.Sin(t * Mathf.PI) * Mathf.Min(1.5f, Vector3.Distance(from, to) * 0.12f);
        transform.position = Vector3.Lerp(from, to, t) + Vector3.up * arc;
        transform.rotation = Quaternion.Euler(age * 720f, age * 260f, 0f);
        if (age >= seconds)
            Destroy(gameObject);
    }
}
