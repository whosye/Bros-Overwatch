using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Bard (inspirace: Lucio) - podpurce s aurou. Vsechny tri schopnosti v jedne komponente:
//   pasivne: aura v okruhu 'radius' (z Shift schopnosti) - bud leci spoluhrace (a slabeji sebe), nebo je zrychluje;
//   Shift (Prepnuti rytmu): prepina leceni / zrychleni;
//   E (Zesileni): na 'duration' s zesili auru (leceni x 'power', zrychleni vic);
//   Q (Drop the Beat): vyskok a pri dopadu uder - nepratele do SlamRadius m odleti, jsou na SlamStun s omraceni
//     a pak SlamSlowSeconds s zpomaleni; potom Mosino na miste tancuje (animace ultimatky) az 'duration' s - spoluhraci
//     v okruhu 'radius' dostanou stit 'power' HP a dokud tancuje, davaji v jeho okruhu DanceDamageScale x poskozeni;
//     tanec (i stit a bonus) skonci driv, kdyz Mosino zemre;
//   rytmus: hra ma takt BeatBpm; davka vystrelena do taktu dava BeatDamageScale x poskozeni a leci spoluhrace v aure;
//   jizda po zdech: ve vzduchu u zdi s drzenym mezernikem (FirstPersonController.WallRide);
//   prave tlacitko (Basovy uder): zvukova vlna pred sebe odhodi nepratele ('knockback', 'power' dmg, dosah 'range');
//     kdo pritom narazi do zdi, je WallSlamStun s omraceny;
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
    const float SlamSlowSeconds = 3f;
    const float SlamSlowFactor = 0.5f;
    public const float DanceDamageScale = 2f;   // +100 % poskozeni spoluhracu behem tance
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
    public float AuraRadius => Radius;
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
        if (landingMarker != null) Destroy(landingMarker);   // (preruseny vyskok)
        landingMarker = null;
        dropping = false;
        if (dancingLocal && fpc != null) fpc.AbilityActive = false;   // (preruseny tanec)
        if (dancingLocal && ProceduralSfx.IsLocalPlayer(this)) HudUI.DanceRemaining = 0f;
        dancingLocal = false;
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
        {
            ServerDanceTick();
            ServerTick();
        }

        // tanec preruseny pohybem nebo schopnosti: ovladani hned zpet, at se schopnost pouzije jeste v tomto snimku
        if (dancingLocal && IsOwner && fpc.Bot == null && HeroInput.Locked(this) && DanceInterrupted())
        {
            danceInterrupted = true;
            fpc.AbilityActive = false;
        }

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

    // Drop the Beat (vlastnik): vyskok, uder az pri dopadu na zem (tanec nikdy ve vzduchu). Za letu je na zemi
    // videt znacka dopadu (kruh dosahu uderu). Kdyz je ve vzduchu dlouho (seskok z vysky), prudce spadne dolu;
    // kdyz ani pak nedopadne (voda, propast), ultimatka se zrusi bez uderu.
    bool dropping;
    GameObject landingMarker;
    bool dancingLocal;   // vlastnik prave tancuje (ma zamceny pohyb)
    const float FallAfter = 1.2f;      // s ve vzduchu, pak prudky pad
    const float FallSpeed = 30f;
    const float GiveUpAfter = 6f;

    System.Collections.IEnumerator DropTheBeat()
    {
        dropping = true;
        fpc.AddImpulse(Vector3.up * 10f);
        ProceduralSfx.PlayForActor(this, ProceduralSfx.Dash, transform.position, 0.8f);
        float start = Time.time;
        var body = GetComponent<CharacterController>();
        var marker = landingMarker = ProceduralSfx.IsLocalPlayer(this) ? CreateLandingMarker() : null;
        bool landed = false;
        while (health.currentHealth.Value > 0f && Time.time - start < GiveUpAfter)
        {
            UpdateLandingMarker(marker);
            if (Time.time - start >= 0.25f && body != null && body.isGrounded) { landed = true; break; }
            if (Time.time - start >= FallAfter) fpc.ForceFall(FallSpeed);
            yield return null;
        }
        if (marker != null) Destroy(marker);
        landingMarker = null;
        dropping = false;
        if (!landed || health.currentHealth.Value <= 0f) yield break;
        ConcertServerRpc();

        // tanec: na miste, bez strelby, kamera ve 3. osobe, nejdyl 'duration' s; ukonci ho driv pohyb, skok,
        // jina schopnost (DanceInterrupted v Update - schopnost se hned pouzije) nebo smrt
        fpc.AbilityActive = dancingLocal = true;
        danceInterrupted = false;
        danceMoveArmed = false;
        bool local = ProceduralSfx.IsLocalPlayer(this);
        float danceEnd = Time.time + concert.duration;
        while (Time.time < danceEnd && health.currentHealth.Value > 0f && !danceInterrupted)
        {
            if (local)
            {
                HudUI.DanceSeconds = concert.duration;
                HudUI.DanceRemaining = Mathf.Clamp01((danceEnd - Time.time) / concert.duration);
            }
            yield return null;
        }
        if (local) HudUI.DanceRemaining = 0f;
        if (Time.time < danceEnd && health.currentHealth.Value > 0f)
            EndDanceServerRpc();   // preruseny tanec - stit a bonus spoluhracu konci driv
        fpc.AbilityActive = dancingLocal = false;
    }

    // Znacka dopadu (jen vlastnik): cerchovany kruh dosahu uderu na zemi pod Mosinem (kresli se i pres prekazky)
    // a maly terc uprostred.
    GameObject CreateLandingMarker()
    {
        var marker = new GameObject("BardLandingMarker");
        marker.transform.localScale = new Vector3(SlamRadius * 2f, 0.02f, SlamRadius * 2f);
        EzekielCircleVisual.AddDashedOutline(marker.transform);
        var center = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        DestroyImmediate(center.GetComponent<Collider>());
        center.transform.SetParent(marker.transform, false);
        center.transform.localScale = new Vector3(0.08f, 1f, 0.08f);
        Fx.Paint(center, SpeedColor);
        marker.SetActive(false);
        return marker;
    }

    void UpdateLandingMarker(GameObject marker)
    {
        if (marker == null) return;
        // zem pod Mosinem (bez vlastni kapsle a ostatnich hracu)
        var hits = Physics.RaycastAll(transform.position + Vector3.up * 0.5f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<PlayerHero>() != null) continue;
            marker.transform.position = hit.point + Vector3.up * 0.03f;
            marker.SetActive(true);
            return;
        }
        marker.SetActive(false);
    }

    // ---------------- tanec (server) ----------------

    float danceUntil;   // server: do kdy Mosino tancuje (0 = netancuje)
    readonly List<Health> danceShielded = new List<Health>();

    bool Dancing => danceUntil > Time.time && health != null && health.currentHealth.Value > 0f;
    // (server) Mosino tanci - spoluhraci v okruhu davaji dvojnasobne poskozeni; pro boty (drzet se dal)
    public bool IsDancing => Dancing;
    public float DanceRadius => concert != null ? concert.radius : 0f;

    // Nasobek poskozeni utocnika od tancicich Mosinu (spoluhrac v okruhu tance). Vola server (Combat.DamagePlayer).
    public static float DamageScaleFor(GameObject attacker)
    {
        if (attacker == null) return 1f;
        foreach (var bard in bards)
        {
            if (bard == null || !bard.Dancing || bard.concert == null) continue;
            if (!Combat.SameTeam(bard.gameObject, attacker)) continue;
            if ((bard.transform.position - attacker.transform.position).sqrMagnitude > bard.concert.radius * bard.concert.radius) continue;
            return DanceDamageScale;
        }
        return 1f;
    }

    void ServerDanceTick()
    {
        if (danceUntil <= 0f || Dancing) return;

        // konec tance (cas nebo smrt): stit spoluhracu rychle vyprcha
        danceUntil = 0f;
        foreach (var target in danceShielded)
            if (target != null && target.currentHealth.Value > 0f)
                target.ServerHoldShield(0f, 0f);
        danceShielded.Clear();
        EndDanceClientRpc();
    }

    // Preruseni tance hracem (bot tancuje celou dobu): pohyb (az po prvnim pusteni pohybovych klaves - kdo pri
    // dopadu jeste drzi W, tanec hned nezrusi), skok nebo jina schopnost.
    bool danceInterrupted, danceMoveArmed;

    bool DanceInterrupted()
    {
        Vector2 move = HeroInput.Move(this);
        if (move.sqrMagnitude < 0.01f) danceMoveArmed = true;
        if (danceMoveArmed && move.sqrMagnitude > 0.09f) return true;
        return HeroInput.Pressed(this, HeroInput.Key.Space) || HeroInput.Pressed(this, HeroInput.Key.Shift)
            || HeroInput.Pressed(this, HeroInput.Key.E) || HeroInput.Pressed(this, HeroInput.Key.RightMouse);
    }

    [ServerRpc]
    void EndDanceServerRpc()
    {
        if (danceUntil > Time.time) danceUntil = Time.time;   // ServerDanceTick tanec ukonci
    }

    [ClientRpc]
    void EndDanceClientRpc()
    {
        var visual = GetComponent<CharacterVisual>();
        if (visual != null) visual.StopUltimate();
        var music = GetComponent<BardAuraMusic>();
        if (music != null) music.StopDance();
        fpc.SetCrouchedHitbox(false);
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

    // ---------------- gesto pri zesileni (E) ----------------
    // Jako Lucio: na zacatku zesileni leva ruka nahoru, pest s natazenym ukazovackem dvakrat pumpne nahoru-dolu
    // a ruka zase dolu (cele ~1,3 s). Kazdy klient ho spusti sam, kdyz se zesileni zapne (NetworkVariable amped).
    // Ve 3. osobe po animaci (svaly Humanoid avatara, nize), v 1. osobe HeldWeapons.UpdateAmpGesture.
    const float GestureRaise = 0.18f;
    const int GesturePumps = 2;
    const float GesturePumpSeconds = 0.42f;
    const float GestureLower = 0.25f;
    float gestureStart = -100f;
    bool wasAmped;

    float GestureTime => Time.time - gestureStart;
    static float GestureLength => GestureRaise + GesturePumps * GesturePumpSeconds + GestureLower;

    // 0 = ruka dole, 1 = zdvizena
    public float AmpGestureWeight
    {
        get
        {
            float t = GestureTime;
            if (t < 0f || t > GestureLength) return 0f;
            if (t < GestureRaise) return Mathf.SmoothStep(0f, 1f, t / GestureRaise);
            if (t > GestureLength - GestureLower) return Mathf.SmoothStep(0f, 1f, (GestureLength - t) / GestureLower);
            return 1f;
        }
    }

    // 1 = pest nahore, 0 = dole (dvakrat dolu a zpet nahoru)
    public float AmpGesturePump
    {
        get
        {
            float cycles = Mathf.Clamp((GestureTime - GestureRaise) / GesturePumpSeconds, 0f, GesturePumps);
            return 0.5f + 0.5f * Mathf.Cos(cycles * Mathf.PI * 2f);
        }
    }

    CharacterVisual gestureVisual;
    Animator gestureAnimator;
    HumanPoseHandler gestureHandler;
    HumanPose gesturePose;
    float gestureWeight;
    static System.Collections.Generic.Dictionary<string, int> muscleIndex;

    void LateUpdate()
    {
        // zesileni se prave zaplo: gesto od zacatku
        bool isAmped = amped.Value;
        if (isAmped && !wasAmped) gestureStart = Time.time;
        wasAmped = isAmped;

        bool alive = health != null && health.currentHealth.Value > 0f;
        gestureWeight = alive ? AmpGestureWeight : 0f;
        if (gestureWeight <= 0f) return;

        if (gestureVisual == null) gestureVisual = GetComponent<CharacterVisual>();
        var animator = gestureVisual != null ? gestureVisual.BodyAnimator : null;
        if (animator == null || gestureVisual.UltimateActive) return;
        if (gestureHandler == null || gestureAnimator != animator)
        {
            gestureHandler?.Dispose();
            gestureHandler = new HumanPoseHandler(animator.avatar, gestureVisual.ModelRoot);
            gestureAnimator = animator;
        }
        if (muscleIndex == null)
        {
            muscleIndex = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < HumanTrait.MuscleCount; i++) muscleIndex[HumanTrait.MuscleName[i]] = i;
        }

        gestureHandler.GetHumanPose(ref gesturePose);
        float pump = AmpGesturePump;
        SetMuscle("Left Shoulder Down-Up", 0.6f);
        SetMuscle("Left Arm Down-Up", Mathf.Lerp(0.7f, 1f, pump));          // paze vzhuru
        SetMuscle("Left Arm Front-Back", 0.4f);                             // lehce dopredu
        SetMuscle("Left Arm Twist In-Out", 0f);
        SetMuscle("Left Forearm Stretch", Mathf.Lerp(-0.45f, 0.35f, pump)); // loket: na uder vystreli nahoru
        SetMuscle("Left Forearm Twist In-Out", 0f);
        SetMuscle("Left Hand Down-Up", 0f);
        SetMuscle("Left Hand In-Out", 0f);
        // pest s natazenym ukazovackem
        for (int i = 0; i < HumanTrait.MuscleCount && i < gesturePose.muscles.Length; i++)
        {
            string name = HumanTrait.MuscleName[i];
            if (!name.StartsWith("LeftHand.") || !name.Contains("Stretched")) continue;
            float target = name.Contains("Index") ? 0.7f : name.Contains("Thumb") ? -0.4f : -0.9f;
            gesturePose.muscles[i] = Mathf.Lerp(gesturePose.muscles[i], target, gestureWeight);
        }
        gestureHandler.SetHumanPose(ref gesturePose);
    }

    void SetMuscle(string name, float value)
    {
        if (muscleIndex.TryGetValue(name, out int i) && i < gesturePose.muscles.Length)
            gesturePose.muscles[i] = Mathf.Lerp(gesturePose.muscles[i], value, gestureWeight);
    }

    public override void OnDestroy()
    {
        gestureHandler?.Dispose();
        gestureHandler = null;
        base.OnDestroy();
    }

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
                target.Heal(BeatHeal * SelfHealScale * Combat.HealingScale(gameObject));
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
            {
                controller.ServerKnockback((flat.normalized + Vector3.up * 0.35f).normalized * wave.knockback);
                // narazi do zdi: omraceni ve chvili narazu
                if (WallBehind(victim, flat.normalized, out float distance, out Vector3 point))
                    StartCoroutine(WallSlam(victim, controller, point, distance / Mathf.Max(1f, wave.knockback)));
            }

            var victimHero = victim.GetComponent<PlayerHero>();
            if (victimHero != null)
                victimHero.ServerNoteAttacker(gameObject);
        }

        WaveClientRpc(origin, forward, wave.range);
    }

    // Basova vlna do zdi: odhozeny nepritel, kteremu v draze odhozeni (do WallSlamDistance m) stoji zed, je po narazu
    // WallSlamStun s omraceny. Pohyb odhozeneho pocita jeho klient, proto server zed hleda dopredu tvarem jeho kapsle.
    const float WallSlamDistance = 10f;
    const float WallSlamStun = 1f;

    static bool WallBehind(Health victim, Vector3 direction, out float distance, out Vector3 point)
    {
        distance = 0f;
        point = Vector3.zero;
        var body = victim.GetComponent<CharacterController>();
        if (body == null || direction.sqrMagnitude < 0.01f) return false;

        Vector3 center = body.transform.TransformPoint(body.center);
        float half = Mathf.Max(0f, body.height * 0.5f - body.radius);
        Vector3 top = center + Vector3.up * half;
        Vector3 bottom = center - Vector3.up * half + Vector3.up * 0.3f;   // (nad schody a nerovnosti zeme)
        var hits = Physics.CapsuleCastAll(top, bottom, body.radius * 0.9f, direction, WallSlamDistance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<PlayerHero>() != null || hit.distance <= 0f) continue;
            if (Mathf.Abs(hit.normal.y) > 0.5f) continue;   // podlaha / strop, ne zed
            distance = hit.distance;
            point = hit.point;
            return true;
        }
        return false;
    }

    System.Collections.IEnumerator WallSlam(Health victim, FirstPersonController controller, Vector3 point, float delay)
    {
        yield return new WaitForSeconds(Mathf.Clamp(delay, 0f, 1f));
        if (victim == null || controller == null || victim.currentHealth.Value <= 0f) yield break;
        controller.ServerStun(WallSlamStun, gameObject, false);
        WallSlamClientRpc(point);
    }

    [ClientRpc]
    void WallSlamClientRpc(Vector3 point)
    {
        Fx.Sparks(point, Color.white);
        Fx.Sparks(point + Vector3.up * 0.3f, new Color(0.75f, 0.5f, 1f));
        ProceduralSfx.Play(ProceduralSfx.StunConfirm, point, 0.8f);
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
                target.Heal(amount * SelfHealScale * Combat.HealingScale(gameObject));
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

        // tanec: spoluhraci v okruhu (i Mosino) maji stit, dokud tancuje
        danceUntil = Time.time + concert.duration;
        danceShielded.Clear();
        foreach (var col in Physics.OverlapSphere(transform.position + Vector3.up, concert.radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var target = col.GetComponentInParent<Health>();
            if (target == null || target.currentHealth.Value <= 0f || target.GetComponent<PlayerHero>() == null) continue;
            if (target.gameObject != gameObject && !Combat.SameTeam(gameObject, target.gameObject)) continue;
            if (danceShielded.Contains(target)) continue;
            target.ServerHoldShield(concert.power, concert.duration);
            danceShielded.Add(target);
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
            controller.ServerSlow(SlamStun + SlamSlowSeconds, SlamSlowFactor);   // (po omraceni jeste zpomaleny)
            var victimHero = victim.GetComponent<PlayerHero>();
            if (victimHero != null) victimHero.ServerNoteAttacker(gameObject);
        }

        var recorder = GetComponent<PotgRecorder>();
        if (recorder != null)
            recorder.ServerNoteUltimate();

        ConcertClientRpc(transform.position, concert.radius, concert.duration);
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
    void ConcertClientRpc(Vector3 position, float radius, float danceSeconds)
    {
        // Mosino tancuje (animace ultimatky pres cele telo, vidi ji vsichni)
        var visual = GetComponent<CharacterVisual>();
        if (visual != null) visual.PlayUltimate(danceSeconds);
        var music = GetComponent<BardAuraMusic>();
        if (music != null) music.PlayDance(danceSeconds);
        fpc.SetCrouchedHitbox(true);   // pri tanci je u zeme - zasahova kapsle jako v drepu (u vsech hracu)

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
// On sam ji slysi vystredene; ostatni (spoluhraci i nepratele) jen v dosahu jeho aury (prostorove, k okraji zeslabne).
// Pri prepnuti se skladby plynule prolnou; po smrti, mimo zapas a v POTG se ztisi (a pak kazda pokracuje, kde skoncila).
// Behem tance (Q) hraje od zacatku tanecni pisnicka (Resources/Audio/bard_dance), aura se zatim ztisi.
public class BardAuraMusic : MonoBehaviour
{
    BardAbility bard;
    Health health;
    AudioSource heal, speed, dance;
    static AudioClip healClip, speedClip, danceClip;
    float danceUntil = -1f;
    const float DanceRange = 30f;   // pisnicku tance slyset dal nez auru

    // Tanec zacal (vsichni klienti, ConcertClientRpc): pisnicka od zacatku.
    public void PlayDance(float seconds)
    {
        danceUntil = Time.time + seconds;
        if (danceClip == null) danceClip = Resources.Load<AudioClip>("Audio/bard_dance");
        if (dance == null && danceClip != null)
        {
            var go = new GameObject("BardDanceMusic");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            dance = go.AddComponent<AudioSource>();
            dance.clip = danceClip;
            dance.loop = true;
            dance.playOnAwake = false;
            dance.rolloffMode = AudioRolloffMode.Linear;
            dance.dopplerLevel = 0f;
            dance.minDistance = DanceRange * 0.4f;
            dance.maxDistance = DanceRange;
        }
        if (dance == null) return;
        dance.volume = 0.12f;
        dance.time = 0f;
        dance.Play();
    }

    // Tanec skoncil driv (preruseni, smrt).
    public void StopDance()
    {
        danceUntil = -1f;
    }

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
        bool dancing = active && danceUntil > Time.time;
        bool speedOn = active && !dancing && bard.SpeedMode;
        bool healOn = active && !dancing && !bard.SpeedMode;
        UpdateDance(dancing);

        if (healClip == null) healClip = Resources.Load<AudioClip>("Audio/bard_heal");
        if (speedClip == null) speedClip = Resources.Load<AudioClip>("Audio/bard_speed");
        // (ostatni ji slysi jen v dosahu aury - hlida rolloff do jejiho okraje)
        Fade(ref heal, healClip, healOn, 0.08f);
        Fade(ref speed, speedClip, speedOn, 0.06f);   // (nahravka je o 2,6 dB hlasitejsi nez leciva)
    }

    void UpdateDance(bool on)
    {
        if (dance == null) return;
        dance.spatialBlend = ProceduralSfx.IsLocalPlayer(bard) ? 0f : 1f;
        if (on || !dance.isPlaying) return;
        // konec tance: rychle zeslabne a zastavi se
        dance.volume = Mathf.MoveTowards(dance.volume, 0f, Time.deltaTime * 0.4f);
        if (dance.volume <= 0.001f) dance.Stop();
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
            source.dopplerLevel = 0f;
        }
        if (source == null) return;

        // slyset jen v dosahu aury (od poloviny dosahu slabne k nule na okraji)
        source.maxDistance = Mathf.Max(2f, bard.AuraRadius);
        source.minDistance = source.maxDistance * 0.5f;

        source.spatialBlend = ProceduralSfx.IsLocalPlayer(bard) ? 0f : 1f;
        if (on && !source.isPlaying) source.Play();
        source.volume = Mathf.MoveTowards(source.volume, on ? volume : 0f, Time.deltaTime * (on ? 1f : 2f));
        if (!on && source.volume <= 0.001f && source.isPlaying) source.Pause();
    }
}
