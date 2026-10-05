using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Bard (inspirace: Lucio) - podpurce s aurou. Vsechny tri schopnosti v jedne komponente:
//   pasivne: aura v okruhu 'radius' (z Shift schopnosti) - bud leci spoluhrace (a slabeji sebe), nebo je zrychluje;
//   Shift (Prepnuti rytmu): prepina leceni / zrychleni;
//   E (Zesileni): na 'duration' s zesili auru (leceni x 'power', zrychleni vic);
//   Q (Koncert): vsichni spoluhraci v okruhu dostanou docasny stit 'power' HP, ktery behem 'duration' s vyprchava;
//   prave tlacitko (Basovy uder): zvukova vlna pred sebe odhodi nepratele ('knockback', 'power' dmg, dosah 'range');
//     kdyz je pred nim do 'radius' m zem nebo zed (mireni pod sebe / do zdi), vlna ho odrazi opacnym smerem ('speed').
// Leceni a stit pocita server, zrychleni si kazdy hrac aplikuje sam (pohyb ridi vlastnik).
public class BardAbility : NetworkBehaviour
{
    public AbilityDefinition crossfade;   // Shift
    public AbilityDefinition amp;         // E
    public AbilityDefinition concert;     // Q
    public AbilityDefinition wave;        // prave tlacitko

    public const float SpeedBonus = 1.3f;
    public const float AmpedSpeedBonus = 1.6f;
    const float SelfHealScale = 0.5f;
    const float TickInterval = 0.25f;

    public static readonly Color HealColor = new Color(0.45f, 1f, 0.45f, 1f);
    public static readonly Color SpeedColor = new Color(1f, 0.85f, 0.25f, 1f);

    public NetworkVariable<bool> speedMode = new NetworkVariable<bool>(false);
    public NetworkVariable<bool> amped = new NetworkVariable<bool>(false);

    static readonly List<BardAbility> bards = new List<BardAbility>();

    FirstPersonController fpc;
    PlayerHero hero;
    Health health;
    float nextToggle, nextAmp, nextUlt, nextWave;
    float ampUntil;
    float nextTick;

    ParticleSystem notes;
    Light glow;

    float Radius => crossfade != null ? crossfade.radius : 12f;
    bool UltUsesCharge => hero != null && hero.UsesUltCharge;

    public float ToggleRemaining => Mathf.Max(0f, nextToggle - Time.time);
    public float AmpRemaining => Mathf.Max(0f, nextAmp - Time.time);
    public float WaveRemaining => Mathf.Max(0f, nextWave - Time.time);
    public float UltRemaining => UltUsesCharge ? 0f : Mathf.Max(0f, nextUlt - Time.time);
    public bool SpeedMode => speedMode.Value;
    public bool Amped => amped.Value;

    public void Configure(AbilityDefinition shift, AbilityDefinition e, AbilityDefinition q, AbilityDefinition rmb)
    {
        crossfade = shift;
        amp = e;
        concert = q;
        wave = rmb;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
        health = GetComponent<Health>();
    }

    void OnEnable()
    {
        if (!bards.Contains(this)) bards.Add(this);
    }

    void OnDisable()
    {
        bards.Remove(this);
        if (notes != null) Destroy(notes.gameObject);
        notes = null;
        glow = null;
        if (IsServer && IsSpawned)
        {
            amped.Value = false;
            speedMode.Value = false;
        }
    }

    // Kolikrat rychleji se hrac pohybuje diky aure spratelenych bardu (1 = bez bonusu). Vola vlastnik hrace.
    public static float SpeedScaleFor(GameObject player)
    {
        float best = 1f;
        foreach (var bard in bards)
        {
            if (bard == null || !bard.isActiveAndEnabled || !bard.speedMode.Value || bard.crossfade == null) continue;
            if (bard.health == null || bard.health.currentHealth.Value <= 0f) continue;
            if (bard.gameObject != player && !Combat.SameTeam(bard.gameObject, player)) continue;
            if ((bard.transform.position - player.transform.position).sqrMagnitude > bard.Radius * bard.Radius) continue;
            best = Mathf.Max(best, bard.amped.Value ? AmpedSpeedBonus : SpeedBonus);
        }
        return best;
    }

    void Update()
    {
        UpdateVisual();

        if (IsServer)
            ServerTick();

        if (!IsOwner || crossfade == null || !GameSettings.CursorLocked || fpc.InputBlocked) return;
        bool alive = health.currentHealth.Value > 0f;
        if (!alive) return;

        if (Keyboard.current.leftShiftKey.wasPressedThisFrame && Time.time >= nextToggle)
        {
            nextToggle = Time.time + crossfade.Cooldown;
            ProceduralSfx.Play(ProceduralSfx.CaptureTick, transform.position, 0.7f);
            hero.SayAbility(crossfade);
            ToggleServerRpc();
        }

        if (amp != null && Keyboard.current.eKey.wasPressedThisFrame && Time.time >= nextAmp)
        {
            nextAmp = Time.time + amp.Cooldown;
            hero.SayAbility(amp);
            AmpServerRpc();
        }

        if (wave != null && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame && Time.time >= nextWave)
        {
            nextWave = Time.time + wave.Cooldown;
            Soundwave();
        }

        if (concert != null && Keyboard.current.qKey.wasPressedThisFrame)
        {
            bool ready = UltUsesCharge ? hero.UltReady : Time.time >= nextUlt;
            if (!ready) return;
            nextUlt = Time.time + concert.Cooldown;
            hero.SpendUlt();
            hero.SayAbility(concert);
            ConcertServerRpc();
        }
    }

    // Basovy uder (vlastnik): odraz od zeme / zdi pred sebou a pozadavek na odhozeni nepratel.
    void Soundwave()
    {
        var eye = fpc.playerCamera.transform;
        Vector3 forward = eye.forward;

        // Odraz: jen kdyz je na co "zatlacit" (zem pod sebou, zed pred sebou).
        if (Physics.Raycast(eye.position, forward, out var surface, wave.radius, ~0, QueryTriggerInteraction.Ignore)
            && surface.collider.GetComponentInParent<PlayerHero>() == null)
        {
            Vector3 push = -forward * wave.speed;
            // Pri mireni pod sebe je odraz hlavne nahoru (at to neni jen poskok).
            if (push.y > 0f) push.y = Mathf.Max(push.y, wave.speed * 0.75f);
            fpc.AddImpulse(push);
        }

        ProceduralSfx.Play(ProceduralSfx.Explosion, transform.position, 0.35f);
        hero.SayAbility(wave);
        SoundwaveServerRpc(eye.position, forward);
    }

    // ---------------- server ----------------

    [ServerRpc]
    void SoundwaveServerRpc(Vector3 origin, Vector3 forward)
    {
        if (wave == null || !CanAct()) return;
        if ((origin - transform.position).sqrMagnitude > 9f) origin = transform.position + Vector3.up * 1.6f;
        forward = forward.sqrMagnitude > 0.01f ? forward.normalized : transform.forward;

        var hit = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(origin + forward * (wave.range * 0.5f), wave.range * 0.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            var victim = col.GetComponentInParent<Health>();
            if (victim == null || !hit.Add(victim) || victim.currentHealth.Value <= 0f) continue;
            if (victim.gameObject == gameObject || Combat.SameTeam(gameObject, victim.gameObject)) continue;

            Vector3 to = col.bounds.center - origin;
            if (to.magnitude > wave.range + 0.6f) continue;
            if (to.sqrMagnitude > 0.04f && Vector3.Angle(forward, to) > 40f) continue;
            if (!Combat.HasLineOfSight(origin, col)) continue;

            Combat.DamagePlayer(gameObject, victim, wave.power);
            if (victim.currentHealth.Value <= 0f) continue;

            Vector3 flat = Vector3.ProjectOnPlane(to, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            var controller = victim.GetComponent<FirstPersonController>();
            if (controller != null)
                controller.ServerKnockback((flat.normalized + Vector3.up * 0.35f).normalized * wave.knockback);

            var victimHero = victim.GetComponent<PlayerHero>();
            if (victimHero != null)
                victimHero.ServerNoteAttacker(gameObject);
        }

        WaveClientRpc(origin, forward, wave.range);
    }

    void ServerTick()
    {
        if (amped.Value && Time.time >= ampUntil)
            amped.Value = false;

        if (Time.time < nextTick || crossfade == null) return;
        nextTick = Time.time + TickInterval;

        if (speedMode.Value || health.currentHealth.Value <= 0f) return;
        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;

        float amount = crossfade.power * TickInterval * (amped.Value && amp != null ? amp.power : 1f);
        foreach (var target in Allies())
        {
            if (target.gameObject == gameObject)
                target.Heal(amount * SelfHealScale);
            else
                Combat.HealPlayer(gameObject, target, amount);
        }
    }

    // Zivi spoluhraci v aure (vcetne sebe).
    List<Health> Allies()
    {
        var result = new List<Health>();
        foreach (var col in Physics.OverlapSphere(transform.position + Vector3.up, Radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var target = col.GetComponentInParent<Health>();
            if (target == null || result.Contains(target) || target.currentHealth.Value <= 0f) continue;
            if (target.GetComponent<PlayerHero>() == null) continue;
            if (target.gameObject != gameObject && !Combat.SameTeam(gameObject, target.gameObject)) continue;
            result.Add(target);
        }
        return result;
    }

    bool CanAct()
    {
        var match = MatchManager.Instance;
        return health.currentHealth.Value > 0f && !(match != null && (match.IsOver || match.IsLobby));
    }

    [ServerRpc]
    void ToggleServerRpc()
    {
        if (health.currentHealth.Value <= 0f) return;
        speedMode.Value = !speedMode.Value;
        PulseClientRpc(transform.position + Vector3.up, speedMode.Value, false);
    }

    [ServerRpc]
    void AmpServerRpc()
    {
        if (amp == null || !CanAct()) return;
        amped.Value = true;
        ampUntil = Time.time + amp.duration;
        PulseClientRpc(transform.position + Vector3.up, speedMode.Value, true);
    }

    [ServerRpc]
    void ConcertServerRpc()
    {
        if (concert == null || !CanAct()) return;

        foreach (var col in Physics.OverlapSphere(transform.position + Vector3.up, concert.radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var target = col.GetComponentInParent<Health>();
            if (target == null || target.currentHealth.Value <= 0f || target.GetComponent<PlayerHero>() == null) continue;
            if (target.gameObject != gameObject && !Combat.SameTeam(gameObject, target.gameObject)) continue;
            target.ServerAddShield(concert.power, concert.duration);
        }

        var recorder = GetComponent<PotgRecorder>();
        if (recorder != null)
            recorder.ServerNoteUltimate();

        ConcertClientRpc(transform.position, concert.radius);
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void PulseClientRpc(Vector3 point, bool speed, bool strong)
    {
        var color = speed ? SpeedColor : HealColor;
        Fx.Sparks(point, color);
        if (strong)
        {
            Fx.Sparks(point + Vector3.up * 0.5f, Color.white);
            ProceduralSfx.Play(ProceduralSfx.CaptureUnlock, point, 0.9f);
        }
        else if (!IsOwner)
        {
            ProceduralSfx.Play(ProceduralSfx.CaptureTick, point, 0.6f);
        }
    }

    [ClientRpc]
    void WaveClientRpc(Vector3 origin, Vector3 forward, float range)
    {
        // Jiskry podel vlny.
        var color = new Color(0.75f, 0.5f, 1f);
        for (int i = 1; i <= 4; i++)
            Fx.Sparks(origin + forward * (range * i / 4f), i % 2 == 0 ? color : Color.white);
        if (!IsOwner)
            ProceduralSfx.Play(ProceduralSfx.Explosion, origin, 0.35f);
    }

    [ClientRpc]
    void ConcertClientRpc(Vector3 position, float radius)
    {
        // Ultimatka je slyset pres celou mapu.
        Fx.PlayGlobal(ProceduralSfx.CaptureWon, 0.6f);
        Fx.Explosion(position + Vector3.up * 0.3f, radius * 0.5f);
        for (int i = 0; i < 10; i++)
        {
            float a = i / 10f * Mathf.PI * 2f;
            Fx.Sparks(position + new Vector3(Mathf.Cos(a), 0.4f, Mathf.Sin(a)) * 2.5f, i % 2 == 0 ? Health.ShieldColor : Color.white);
        }
    }

    // Noty a svetlo kolem barda v barve rezimu (zelena = leceni, zluta = zrychleni); zesileni je jasnejsi.
    void UpdateVisual()
    {
        bool show = health != null && health.currentHealth.Value > 0f && crossfade != null;
        if (!show)
        {
            if (notes != null && notes.isEmitting) notes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (glow != null) glow.enabled = false;
            return;
        }

        if (notes == null) BuildVisual();

        var color = speedMode.Value ? SpeedColor : HealColor;
        var main = notes.main;
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
        var emission = notes.emission;
        emission.rateOverTime = amped.Value ? 40f : 12f;
        if (!notes.isEmitting) notes.Play();

        glow.enabled = true;
        glow.color = color;
        glow.intensity = (amped.Value ? 3.5f : 1.4f) + Mathf.Sin(Time.time * 8f) * 0.4f;
        glow.range = amped.Value ? 6f : 4f;
    }

    void BuildVisual()
    {
        var go = new GameObject("FX_BardAura");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.2f, 0f);

        notes = go.AddComponent<ParticleSystem>();
        notes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = notes.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
        main.gravityModifier = -0.25f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = notes.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.7f;
        shape.radiusThickness = 0f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        notes.GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;

        glow = go.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.shadows = LightShadows.None;
    }
}
