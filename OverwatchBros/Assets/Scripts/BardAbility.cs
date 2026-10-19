using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Bard (inspirace: Lucio) - podpurce s aurou. Vsechny tri schopnosti v jedne komponente:
//   pasivne: aura v okruhu 'radius' (z Shift schopnosti) - bud leci spoluhrace (a slabeji sebe), nebo je zrychluje;
//   Shift (Prepnuti rytmu): prepina leceni / zrychleni;
//   E (Zesileni): na 'duration' s zesili auru (leceni x 'power', zrychleni vic);
//   Q (Drop the Beat): vyskok a pri dopadu uder - spoluhraci v okruhu dostanou docasny stit 'power' HP (behem 'duration' s
//     vyprcha), nepratele do SlamRadius m odleti a jsou na SlamStun s omraceni;
//   rytmus: hra ma takt BeatBpm; davka vystrelena do taktu dava BeatDamageScale x poskozeni a leci spoluhrace v aure;
//   jizda po zdech: ve vzduchu u zdi s drzenym mezernikem (FirstPersonController.WallRide);
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
    const float SlamRadius = 7f;       // Drop the Beat: dosah uderu na nepratele
    const float SlamStun = 1f;
    const float SlamKnockback = 13f;
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
        if (GetComponent<BardAuraMusic>() == null)
            gameObject.AddComponent<BardAuraMusic>();
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

        if (!IsOwner || crossfade == null || !HeroInput.Locked(this) || fpc.InputBlocked) return;
        bool alive = health.currentHealth.Value > 0f;
        if (!alive) return;

        if (HeroInput.Pressed(this, HeroInput.Key.Shift) && Time.time >= nextToggle)
        {
            nextToggle = Time.time + crossfade.Cooldown;
            ProceduralSfx.PlayForActor(this, ProceduralSfx.CaptureTick, transform.position, 0.7f);
            hero.SayAbility(crossfade);
            ToggleServerRpc();
        }

        if (amp != null && HeroInput.Pressed(this, HeroInput.Key.E) && Time.time >= nextAmp)
        {
            nextAmp = Time.time + amp.Cooldown;
            hero.SayAbility(amp);
            AmpServerRpc();
        }

        if (wave != null && Mouse.current != null && HeroInput.Pressed(this, HeroInput.Key.RightMouse) && Time.time >= nextWave)
        {
            nextWave = Time.time + wave.Cooldown;
            Soundwave();
        }

        if (concert != null && HeroInput.Pressed(this, HeroInput.Key.Q) && !dropping)
        {
            bool ready = UltUsesCharge ? hero.UltReady : Time.time >= nextUlt;
            if (!ready) return;
            nextUlt = Time.time + concert.Cooldown;
            hero.SpendUlt();
            hero.SayAbility(concert);
            StartCoroutine(DropTheBeat());
        }
    }

    // Drop the Beat (vlastnik): vyskok, pri dopadu (nejpozdeji po 1,6 s) uder.
    bool dropping;

    System.Collections.IEnumerator DropTheBeat()
    {
        dropping = true;
        fpc.AddImpulse(Vector3.up * 10f);
        ProceduralSfx.PlayForActor(this, ProceduralSfx.Dash, transform.position, 0.8f);
        float start = Time.time;
        var body = GetComponent<CharacterController>();
        yield return new WaitForSeconds(0.25f);
        while (Time.time - start < 1.6f && body != null && !body.isGrounded)
            yield return null;
        dropping = false;
        if (health.currentHealth.Value > 0f)
            ConcertServerRpc();
    }

    // ---------------- rytmus ----------------

    public const float BeatBpm = 120f;
    const float BeatWindow = 0.08f;          // tolerance kolem uderu (s)
    public const float BeatDamageScale = 1.5f;
    const float BeatHeal = 8f;               // lecivy puls spoluhracum v aure za davku do taktu
    public static float LastLocalBeatShot = -10f;   // HUD: kdy mistni hrac naposled trefil takt

    // Spolecne hodiny vsech (cas serveru), at takt sedi u vsech hracu stejne.
    public static double BeatClock
    {
        get
        {
            var network = NetworkManager.Singleton;
            return network != null && network.IsListening ? network.ServerTime.Time : Time.unscaledTimeAsDouble;
        }
    }

    // 0 = prave uder, roste k 1 = dalsi uder
    public static float BeatPhase => (float)(BeatClock * BeatBpm / 60.0 % 1.0);

    public static bool OnBeat
    {
        get
        {
            float period = 60f / BeatBpm;
            float t = BeatPhase * period;
            return t < BeatWindow || t > period - BeatWindow;
        }
    }

    public static bool ShotOnBeat(GameObject shooter)
    {
        var bard = shooter.GetComponent<BardAbility>();
        return bard != null && bard.isActiveAndEnabled && bard.crossfade != null && OnBeat;
    }

    public static void NoteLocalBeatShot(Vector3 position)
    {
        LastLocalBeatShot = Time.time;
        ProceduralSfx.Play(ProceduralSfx.CaptureTick, position, 0.7f, 20f, spatial: false);
    }

    float nextBeatHeal;

    // Davka do taktu (server): lecivy puls spoluhracum v aure (jednou za davku).
    public void ServerBeatShot()
    {
        if (!IsServer || Time.time < nextBeatHeal || !CanAct()) return;
        nextBeatHeal = Time.time + 0.3f;
        foreach (var target in Allies())
        {
            if (target.gameObject == gameObject)
                target.Heal(BeatHeal * SelfHealScale);
            else
                Combat.HealPlayer(gameObject, target, BeatHeal);
        }
        BeatClientRpc(transform.position + Vector3.up * 1.2f);
    }

    [ClientRpc]
    void BeatClientRpc(Vector3 point)
    {
        Fx.Sparks(point, new Color(1f, 0.85f, 0.3f));
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
            if (Mathf.Abs(surface.normal.y) < 0.5f)
                // Zed: silnejsi odraz od ni a sikmo nahoru - trik na strechu nebo unik.
                push = (surface.normal + Vector3.up * 0.8f).normalized * wave.speed * 1.4f;
            // Pri mireni pod sebe je odraz hlavne nahoru (at to neni jen poskok).
            else if (push.y > 0f) push.y = Mathf.Max(push.y, wave.speed * 0.75f);
            fpc.AddImpulse(push);
        }

        ProceduralSfx.PlayForActor(this, ProceduralSfx.Explosion, transform.position, 0.35f);
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

            Combat.DamagePlayer(gameObject, victim, wave.power, Combat.AbilitySource(wave));
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

        // Uder: nepratele v okoli odleti a jsou chvili omraceni.
        var slammed = new HashSet<Health>();
        Vector3 center = transform.position + Vector3.up;
        foreach (var col in Physics.OverlapSphere(center, SlamRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            var victim = col.GetComponentInParent<Health>();
            if (victim == null || !slammed.Add(victim) || victim.currentHealth.Value <= 0f) continue;
            if (victim.gameObject == gameObject || Combat.SameTeam(gameObject, victim.gameObject)) continue;
            if (victim.GetComponent<PlayerHero>() == null || !Combat.HasLineOfSight(center, col)) continue;
            var controller = victim.GetComponent<FirstPersonController>();
            if (controller == null) continue;
            Vector3 away = Vector3.ProjectOnPlane(victim.transform.position - transform.position, Vector3.up);
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            controller.ServerStun(SlamStun, gameObject, false);   // (omraceni pred odhozenim - jinak by ho zrusilo)
            controller.ServerKnockback((away.normalized + Vector3.up * 0.5f).normalized * SlamKnockback);
            var victimHero = victim.GetComponent<PlayerHero>();
            if (victimHero != null) victimHero.ServerNoteAttacker(gameObject);
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
            ProceduralSfx.PlayForActor(this, ProceduralSfx.CaptureUnlock, point, 0.9f);
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
        Fx.Explosion(position + Vector3.up * 0.2f, SlamRadius * 0.4f);
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

// Hudba z Barda podle zapnute aury: lecivy rytmus (Resources/Audio/bard_heal) a zrychlujici (Resources/Audio/bard_speed).
// Ostatni ji slysi prostorove z nej, on sam vystredene. Pri prepnuti se skladby plynule prolnou; po smrti, mimo zapas
// a v POTG se ztisi (a pak kazda pokracuje, kde skoncila).
public class BardAuraMusic : MonoBehaviour
{
    BardAbility bard;
    Health health;
    AudioSource heal, speed;
    static AudioClip healClip, speedClip;

    void Awake()
    {
        bard = GetComponent<BardAbility>();
        health = GetComponent<Health>();
    }

    void Update()
    {
        var match = MatchManager.Instance;
        bool active = bard != null && bard.enabled && bard.IsSpawned
            && health != null && health.currentHealth.Value > 0f
            && match != null && !match.IsLobby && !match.IsOver && !ReplayPlayer.Active;
        bool speedOn = active && bard.SpeedMode;
        bool healOn = active && !bard.SpeedMode;

        if (healClip == null) healClip = Resources.Load<AudioClip>("Audio/bard_heal");
        if (speedClip == null) speedClip = Resources.Load<AudioClip>("Audio/bard_speed");
        Fade(ref heal, healClip, healOn, 0.15f);
        Fade(ref speed, speedClip, speedOn, 0.11f);   // (nahravka je o 2,6 dB hlasitejsi nez leciva)
    }

    void Fade(ref AudioSource source, AudioClip clip, bool on, float volume)
    {
        if (on && source == null && clip != null)
        {
            var go = new GameObject("BardAuraMusic_" + clip.name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 4f;
            source.maxDistance = 35f;
            source.dopplerLevel = 0f;
        }
        if (source == null) return;

        source.spatialBlend = ProceduralSfx.IsLocalPlayer(bard) ? 0f : 1f;
        if (on && !source.isPlaying) source.Play();
        source.volume = Mathf.MoveTowards(source.volume, on ? volume : 0f, Time.deltaTime * (on ? 1f : 2f));
        if (!on && source.volume <= 0.001f && source.isPlaying) source.Pause();
    }
}
