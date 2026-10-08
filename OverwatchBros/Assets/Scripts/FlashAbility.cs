using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Viktoruv pulzni granat (prave tlacitko mysi, jako Soldierovy Helix rakety): vystreli se z hlavne granatometu,
// leti rychle rovne (rychlost 'speed', nejdal 'range' m) a o prvni prekazku nebo nepritele vybuchne. Nepratele
// v okruhu 'radius' zrani ('power' uprostred, na okraji 40 %) a na 'duration' sekund omraci (nemuzou se hybat,
// utocit ani pouzivat schopnosti). Neprochazi zdmi a spoluhrace ani Viktora nezasahne.
public class FlashAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    public static readonly Color PulseColor = new Color(0.55f, 0.85f, 1f);

    FirstPersonController fpc;

    float nextUseTime;

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
        if (!HeroInput.Pressed(this, HeroInput.Key.RightMouse) || !HeroInput.Locked(this)) return;
        if (fpc.InputBlocked || Time.time < nextUseTime) return;

        nextUseTime = Time.time + ability.Cooldown;

        var eye = fpc.playerCamera.transform;
        // granat vyleti z hlavne (stejne misto jako stopy strel), miri se ale ze stredu obrazovky
        Vector3 muzzle = eye.position + eye.right * 0.15f - eye.up * 0.12f + eye.forward * 0.4f;
        GetComponent<PlayerHero>().SayAbility(ability);
        ThrowServerRpc(eye.position, eye.forward, muzzle);
    }

    // ---------------- server ----------------

    [ServerRpc]
    void ThrowServerRpc(Vector3 origin, Vector3 direction, Vector3 muzzle)
    {
        var match = MatchManager.Instance;
        if (ability == null || direction.sqrMagnitude < 0.01f || (match != null && (match.IsOver || match.IsLobby))) return;

        direction.Normalize();

        // Granat leti rovne a vybuchne o prvni prekazku nebo nepritele, nejdal po 'range' metrech.
        float distance = ability.range;
        var hits = Physics.SphereCastAll(origin, 0.12f, direction, ability.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && (owner == NetworkObject || Combat.SameTeam(gameObject, owner.gameObject))) continue;

            distance = Mathf.Max(0.3f, hit.distance - 0.1f);
            break;
        }

        Vector3 point = origin + direction * distance;
        float flight = distance / Mathf.Max(1f, ability.speed);
        if ((muzzle - origin).sqrMagnitude > 1f) muzzle = origin + direction * 0.5f;
        ThrownClientRpc(muzzle, point, flight);
        StartCoroutine(Detonate(point, flight));
    }

    IEnumerator Detonate(Vector3 point, float delay)
    {
        yield return new WaitForSeconds(delay);

        var match = MatchManager.Instance;
        if (match == null || (!match.IsOver && !match.IsLobby))
        {
            var hit = new HashSet<Health>();
            foreach (var col in Physics.OverlapSphere(point, ability.radius, ~0, QueryTriggerInteraction.Ignore))
            {
                // poskozeni podle vzdalenosti od stredu vybuchu
                float near = Vector3.Distance(point, col.ClosestPoint(point));
                float damage = ability.power * Mathf.Lerp(1f, 0.4f, near / Mathf.Max(0.1f, ability.radius));

                var dummy = col.GetComponentInParent<Target>();
                if (dummy != null)
                    dummy.TakeDamage(damage);

                var victim = col.GetComponentInParent<Health>();
                if (victim == null || !hit.Add(victim) || victim.currentHealth.Value <= 0f) continue;
                if (victim.gameObject == gameObject || Combat.SameTeam(gameObject, victim.gameObject)) continue;
                if (!Combat.HasLineOfSight(point, col)) continue;

                Combat.DamagePlayer(gameObject, victim, damage, Combat.AbilitySource(ability));

                var controller = victim.GetComponent<FirstPersonController>();
                if (controller != null && victim.currentHealth.Value > 0f)
                {
                    controller.ServerStun(ability.duration, gameObject);

                    var recorder = GetComponent<PotgRecorder>();
                    if (recorder != null)
                        recorder.ServerAddStun();
                }
            }
        }

        DetonatedClientRpc(point);
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void ThrownClientRpc(Vector3 from, Vector3 to, float seconds)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(ball.GetComponent<Collider>());
        ball.name = "PulseGrenade";
        ball.transform.localScale = Vector3.one * 0.14f;
        Fx.Paint(ball, new Color(0.92f, 0.96f, 1f));
        ball.AddComponent<FlashGrenadeVisual>().Init(from, to, seconds);

        // vystrel z granatometu: tupe bouchnuti a zablesk u hlavne
        ProceduralSfx.Play(ProceduralSfx.Gunshot, from, 0.9f);
        ProceduralSfx.Play(ProceduralSfx.Dash, from, 0.6f);
        Fx.Sparks(from, PulseColor);
    }

    [ClientRpc]
    void DetonatedClientRpc(Vector3 point)
    {
        Fx.Explosion(point, (ability != null ? ability.radius : 3f) * 0.6f);
        Fx.BulletImpact(point, PulseColor, 4f);
        Fx.Sparks(point, Color.white);
        ProceduralSfx.Play(ProceduralSfx.Explosion, point, 0.7f);
        ProceduralSfx.Play(ProceduralSfx.Hit, point, 1f);

        // Bily zablesk.
        var lightObject = new GameObject("FlashLight");
        lightObject.transform.position = point;
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Color.white;
        light.range = (ability != null ? ability.radius : 4f) * 2.5f;
        light.intensity = 9f;
        Destroy(lightObject, 0.18f);
    }
}

// Letici granat (jen efekt): doleti z bodu do bodu za dany cas a zmizi.
public class FlashGrenadeVisual : MonoBehaviour
{
    Vector3 from, to;
    float seconds, age;

    public void Init(Vector3 start, Vector3 end, float flight)
    {
        from = start;
        to = end;
        seconds = Mathf.Max(0.02f, flight);
        transform.position = start;

        // svitici stopa za granatem
        var trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.minVertexDistance = 0.1f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.12f), new Keyframe(1f, 0f));
        trail.material = Fx.ParticleMaterial;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(FlashAbility.PulseColor, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = gradient;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = FlashAbility.PulseColor;
        glow.range = 3f;
        glow.intensity = 3f;
    }

    void Update()
    {
        age += Time.deltaTime;
        transform.position = Vector3.Lerp(from, to, age / seconds);
        if (age >= seconds)
            Destroy(gameObject);
    }
}
