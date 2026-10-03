using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Mirkova ultimatni schopnost (Q): Smrst. Vystreli pevne tocici se teleso (koule o polomeru 'radius' obehnana
// prstenci), ktere leti rovne dopredu rychlosti 'speed' po dobu 'duration' sekund a prochazi i zdmi.
// Nepratelum, kterych se dotyka, ubira 'power' zivotu za sekundu.
public class StormAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    const float TickInterval = 0.2f;

    // Delka valce smrsti jako nasobek polomeru (radius 3 -> 18 m). Valec zacina v bode vystrelu a miri dopredu.
    public const float LengthFactor = 6f;
    static readonly Color StormColor = new Color(0.55f, 0.75f, 1f, 1f);

    FirstPersonController fpc;
    PlayerHero hero;

    float nextUseTime;

    // server
    bool stormActive;
    Vector3 stormPosition, stormDirection;
    float stormEnd;
    float nextTick;

    // Server: vir prave leti (kvuli nabijeni ultimatky a hodnoceni akci).
    public bool IsStormActive => stormActive;

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
        if (IsOwner)
            OwnerUpdate();

        if (IsServer && stormActive)
            ServerTick();
    }

    const float WindupSeconds = 2f;
    readonly UltWindup windup = new UltWindup();
    WeaponShooting shooting;

    void OwnerUpdate()
    {
        if (ability == null) return;

        if (shooting == null)
            shooting = GetComponent<WeaponShooting>();

        // Priprava: 2 s se sam natahuje luk az na maximum (bez drzeni tlacitka), pak vyleti smrst.
        if (windup.Active)
        {
            bool done = windup.Tick(fpc);
            if (shooting != null)
                shooting.ForcedCharge = windup.Active ? windup.Progress : 0f;
            if (done)
                Fire();
            return;
        }

        if (!Keyboard.current.qKey.wasPressedThisFrame || !GameSettings.CursorLocked) return;
        if (fpc.InputBlocked || !CanUse) return;

        windup.Begin(fpc, WindupSeconds);
        hero.SayAbility(ability);
        Fx.PlayGlobal(ProceduralSfx.UltCharge, 0.5f);
        WindupServerRpc();
    }

    void Fire()
    {
        if (shooting != null)
            shooting.ForcedCharge = 0f;

        nextUseTime = Time.time + ability.Cooldown;
        hero.SpendUlt();

        // Smrst leti presne tam, kam miri zamerovac (i nahoru nebo dolu).
        var eye = fpc.playerCamera.transform;
        Vector3 direction = eye.forward;
        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.9f);
        LaunchServerRpc(eye.position + direction * 2.5f, direction);
    }

    // Vola PlayerRespawn / reset kola.
    public void Cancel()
    {
        if (!windup.Active) return;

        windup.Cancel(fpc);
        if (shooting != null)
            shooting.ForcedCharge = 0f;
    }

    void OnDisable()
    {
        if (windup.Active && fpc != null)
            Cancel();
    }

    [ServerRpc]
    void WindupServerRpc()
    {
        WindupClientRpc();
    }

    [ClientRpc]
    void WindupClientRpc()
    {
        if (!IsOwner)
            Fx.PlayGlobal(ProceduralSfx.UltCharge, 0.5f);
    }

    // ---------------- server ----------------

    [ServerRpc]
    void LaunchServerRpc(Vector3 origin, Vector3 direction)
    {
        var match = MatchManager.Instance;
        if (ability == null || direction.sqrMagnitude < 0.01f || (match != null && (match.IsOver || match.IsLobby))) return;

        stormActive = true;
        stormPosition = origin;
        stormDirection = direction.normalized;
        stormEnd = Time.time + ability.duration;
        nextTick = Time.time;

        var recorder = GetComponent<PotgRecorder>();
        if (recorder != null)
            recorder.ServerNoteUltimate();

        LaunchedClientRpc(origin, stormDirection, ability.speed, ability.duration, ability.radius);
    }

    void ServerTick()
    {
        stormPosition += stormDirection * ability.speed * Time.deltaTime;

        if (Time.time >= stormEnd)
        {
            stormActive = false;
            return;
        }

        if (Time.time < nextTick) return;
        nextTick = Time.time + TickInterval;

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;

        // Zamerne bez kontroly vyhledu: smrst prochazi zdmi.
        var hit = new HashSet<Health>();
        Vector3 center = stormPosition;
        float length = ability.radius * LengthFactor;
        Vector3 tail = center + stormDirection * ability.radius;
        Vector3 head = center + stormDirection * Mathf.Max(ability.radius, length - ability.radius);
        foreach (var col in Physics.OverlapCapsule(tail, head, ability.radius * 1.1f, ~0, QueryTriggerInteraction.Ignore))
        {
            var boulder = col.GetComponentInParent<BoulderHitbox>();
            if (boulder != null)
                boulder.Damage(gameObject, ability.power * TickInterval);

            var victim = col.GetComponentInParent<Health>();
            if (victim == null || !hit.Add(victim) || victim.currentHealth.Value <= 0f) continue;
            if (victim.gameObject == gameObject || Combat.SameTeam(gameObject, victim.gameObject)) continue;

            Combat.DamagePlayer(gameObject, victim, ability.power * TickInterval);
        }
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void LaunchedClientRpc(Vector3 origin, Vector3 direction, float speed, float seconds, float radius)
    {
        var go = new GameObject("Storm");
        go.transform.position = origin;
        go.AddComponent<StormVisual>().Init(direction, speed, seconds, radius, StormColor);
        ProceduralSfx.Play(ProceduralSfx.LeapStart, origin, 1f);
    }
}

// Letici smrst (jen efekt): pevne teleso viditelne z dalky - svitici jadro, kolem nej tri prstence z hranolu,
// ktere se toci proti sobe, a za nim stopa castic. Pohybuje se stejne jako serverova smrst a prochazi zdmi.
public class StormVisual : MonoBehaviour
{
    Vector3 direction;
    float speed, seconds, age;
    Transform body;
    readonly Transform[] rings = new Transform[5];
    ParticleSystem trail;
    Light glow;
    float fullScale = 1f;

    public void Init(Vector3 moveDirection, float moveSpeed, float lifetime, float radius, Color color)
    {
        direction = moveDirection;
        speed = moveSpeed;
        seconds = lifetime;

        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction);

        float length = radius * StormAbility.LengthFactor;

        // Telo zacina v bode vystrelu a tahne se dopredu (stejne jako zasahova oblast na serveru).
        body = new GameObject("Body").transform;
        body.SetParent(transform, false);
        body.localPosition = new Vector3(0f, 0f, length * 0.5f);

        // Jadro: dlouhy valec se zakulacenymi konci, osou ve smeru letu.
        var core = Solid(PrimitiveType.Capsule, body, Vector3.zero, new Vector3(radius * 1.25f, length * 0.5f, radius * 1.25f),
            Color.Lerp(color, Color.white, 0.55f));
        core.localRotation = Quaternion.Euler(90f, 0f, 0f);
        core.name = "Core";

        // Prstence z hranolu kolem jadra.
        var dark = new Color(color.r * 0.45f, color.g * 0.5f, color.b * 0.75f);
        for (int r = 0; r < rings.Length; r++)
        {
            var ring = new GameObject("Ring").transform;
            ring.SetParent(body, false);
            ring.localPosition = new Vector3(0f, 0f, (r / (rings.Length - 1f) - 0.5f) * (length - radius * 1.2f));
            rings[r] = ring;

            const int pieces = 10;
            float ringRadius = radius * (r % 2 == 0 ? 0.9f : 1.05f);
            for (int i = 0; i < pieces; i++)
            {
                float angle = i * Mathf.PI * 2f / pieces;
                var piece = Solid(PrimitiveType.Cube, ring, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * ringRadius,
                    new Vector3(radius * 0.42f, radius * 0.16f, radius * 0.30f), i % 2 == 0 ? color : dark);
                piece.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
            }
        }

        // Stopa castic za telesem.
        var trailObject = new GameObject("Trail");
        trailObject.transform.SetParent(transform, false);
        trail = trailObject.AddComponent<ParticleSystem>();
        trail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = trail.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.3f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, color);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 500;

        var emission = trail.emission;
        emission.rateOverTime = 160f;

        var shape = trail.shape;
        // Castice se sypou po cele delce valce.
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(radius * 1.4f, radius * 1.4f, length);
        shape.position = new Vector3(0f, 0f, length * 0.5f);
        emission.rateOverTime = 260f;

        var fade = trail.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        trailObject.GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;
        trail.Play();

        var lightObject = new GameObject("Glow");
        lightObject.transform.SetParent(transform, false);
        glow = lightObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = color;
        glow.range = radius * 9f;
        lightObject.transform.localPosition = new Vector3(0f, 0f, length * 0.5f);
        glow.intensity = 6f;

        body.localScale = Vector3.zero;
    }

    static Transform Solid(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        Fx.Paint(go, color);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    void Update()
    {
        age += Time.deltaTime;
        transform.position += direction * speed * Time.deltaTime;

        // Rychly nabeh na plnou velikost, na konci se smrskne.
        float grow = Mathf.Clamp01(age / 0.25f);
        float shrink = Mathf.Clamp01((seconds - age) / 0.4f);
        body.localScale = Vector3.one * fullScale * Mathf.Min(grow, shrink);

        // Prstence se toci kolem osy letu, prostredni proti smeru krajnich; jadro pulzuje.
        for (int r = 0; r < rings.Length; r++)
            rings[r].Rotate(0f, 0f, (r % 2 == 1 ? -300f : 220f) * Time.deltaTime, Space.Self);
        glow.intensity = 5f + Mathf.Sin(age * 14f) * 1.5f;

        if (age >= seconds && trail.isEmitting)
        {
            trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            glow.enabled = false;
        }

        if (age >= seconds + 1.2f)
            Destroy(gameObject);
    }
}
