using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Max - Shift, E a prave tlacitko v jedne komponente:
//   Shift (Rozpolceni): vypad 'range' metru ve smeru pohledu a na konci otocka kosou - 'power' vsem v okruhu
//     'radius' a omraceni na 0.2 s. Ma-li Max animaci skoku s uderem
//     (animations.shiftAbility), misto rychleho vypadu skoci (LeapTime) a kosou udere shora do zeme pri dopadu.
//   E (Stinova forma): az 'duration' s je Max stin - neviditelny (jen fialovy kour), nezranitelny, bez kolize
//     a chodi skrz zdi. Forma skonci utokem (LMB, PTM, Shift, Q), dalsim E nebo vyprsenim casu; kdyz konci ve zdi,
//     vystrci ho to na nejblizsi volne misto. Cooldown bezi az od konce formy.
//   Odolnost pri vypadu: behem Shiftu a GuardAfter s po nem dostava Max o GuardReduction mene poskozeni.
//   PTM (Otocka kosou): Max se s kosou roztoci o 360 st. - 'power' poskozeni vsem nepratelum v okruhu 'radius'
//     kolem sebe (ne pres zdi). Behem otocky jde pomaleji.
public class MaxAbility : NetworkBehaviour
{
    public AbilityDefinition reap;    // Shift
    public AbilityDefinition step;    // E
    public AbilityDefinition slash;   // PTM

    public static readonly Color ShadowColor = new Color(0.55f, 0.18f, 0.85f, 1f);

    const float DashTime = 0.18f;
    public const float GuardReduction = 0.4f;
    const float GuardAfter = 1f;
    const float ReapStun = 0.2f;          // zasazene Rozpolcenim omraci
    // Skok s uderem: usek animace od odrazu (0,73 s) po podrep po uderu; uder do zeme (1,67 s) prijde pri dopadu.
    const float LeapTime = 0.5f;
    const float LeapHeight = 2.2f;   // vrchol skoku nad mistem odrazu
    // vychozi casy puvodniho klipu; jiny klip ma sve casy v sade animaci (CharacterAnimSet.shiftFrom/Impact/To)
    const float LeapClipFrom = 0.73f, LeapClipImpact = 1.67f, LeapClipTo = 2.19f;

    void LeapClip(out float from, out float impact, out float to)
    {
        var set = hero != null && hero.Hero != null ? hero.Hero.animations : null;
        if (set != null && set.shiftImpact > set.shiftFrom + 0.05f && set.shiftTo >= set.shiftImpact)
        {
            from = set.shiftFrom; impact = set.shiftImpact; to = set.shiftTo;
            return;
        }
        from = LeapClipFrom; impact = LeapClipImpact; to = LeapClipTo;
    }

    FirstPersonController fpc;
    PlayerHero hero;
    Health health;
    HeldWeapons held;
    float nextReap, nextStep, nextSlash;
    bool busy;

    public float ReapRemaining => Mathf.Max(0f, nextReap - Time.time);
    public float StepRemaining => inShadow ? 0f : Mathf.Max(0f, nextStep - Time.time);
    public float SlashRemaining => Mathf.Max(0f, nextSlash - Time.time);
    public bool Busy => busy;

    public void Configure(AbilityDefinition shift, AbilityDefinition e, AbilityDefinition rmb)
    {
        reap = shift;
        step = e;
        slash = rmb;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
        health = GetComponent<Health>();
        held = GetComponent<HeldWeapons>();
    }

    void Update()
    {
        if (!IsOwner) return;
        if (inShadow)
        {
            ShadowTick();
            return;
        }
        if (busy || !HeroInput.Locked(this) || fpc.InputBlocked || health.currentHealth.Value <= 0f) return;

        if (reap != null && HeroInput.Pressed(this, HeroInput.Key.Shift) && Time.time >= nextReap && !fpc.Rooted)
            StartCoroutine(Reap());
        else if (step != null && HeroInput.Pressed(this, HeroInput.Key.E) && Time.time >= nextStep)
            EnterShadow();
        else if (slash != null && HeroInput.Pressed(this, HeroInput.Key.RightMouse) && Time.time >= nextSlash)
            StartCoroutine(Slash());
    }

    Vector3 LookFlat()
    {
        Vector3 forward = fpc.playerCamera.transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.001f ? forward.normalized : transform.forward;
    }

    // ---------------- Shift: Rozpolceni ----------------

    IEnumerator Reap()
    {
        busy = true;
        nextReap = Time.time + reap.Cooldown;
        hero.SayAbility(reap);
        ReapGuardServerRpc(0.6f);   // odolnost uz behem vypadu (prodlouzi se pri dopadu)
        ProceduralSfx.PlayForActor(this, CollectedAudioBank.Pick("max_reap", ProceduralSfx.Dash), transform.position, 0.7f);
        ReapAudioServerRpc();

        // smer pohledu; pri pohledu nahoru i kousek do vyse
        Vector3 look = fpc.playerCamera.transform.forward;
        Vector3 direction = LookFlat() + Vector3.up * Mathf.Clamp(look.y, 0f, 0.5f);
        var visual = GetComponent<CharacterVisual>();
        bool leap = visual != null && visual.HasAbilityAnimation(CharacterVisual.AbilityAnim.Shift);
        float time = leap ? LeapTime : DashTime;
        if (leap)
        {
            PlayLeap(visual);
            LeapServerRpc();
            if (held != null) held.PlayReach(LeapTime);   // v prvni osobe napraah kosou nad hlavu, sek pri dopadu
        }

        float speed = (reap.range > 0f ? reap.range : 6f) / time;
        var controller = GetComponent<CharacterController>();
        float elapsed = 0f;
        NetworkObject struck = null;   // nepritel v ceste: u nej se vypad zastavi
        while (elapsed < time && !fpc.CannotAct)
        {
            struck = EnemyInPath();
            if (struck != null) break;
            float before = elapsed / time;
            elapsed += Time.deltaTime;
            float after = Mathf.Min(1f, elapsed / time);
            // skok: oblouk nahoru a dolu (parabola), dopad presne na konci
            float rise = leap ? LeapHeight * 4f * (after * (1f - after) - before * (1f - before)) : 0f;
            controller.Move(direction * speed * (after - before) * time + Vector3.up * rise);
            yield return null;
        }
        if (struck == null) struck = EnemyInPath();
        fpc.ResetVertical();
        if ((!leap || struck != null) && held != null) held.Swing();
        if (struck != null) ReapServerRpc(transform.position, struck, true);
        else ReapServerRpc(transform.position, default, false);
        busy = false;
    }

    // Zivy nepritel tesne pred Maxem (ve smeru vypadu) - neviditelne (stin, prsten) a spoluhrace ignoruje.
    NetworkObject EnemyInPath()
    {
        Vector3 center = transform.position + Vector3.up * 1f + LookFlat() * 0.7f;
        NetworkObject best = null;
        float bestDistance = float.MaxValue;
        foreach (var col in Physics.OverlapSphere(center, 0.9f, ~0, QueryTriggerInteraction.Ignore))
        {
            var victim = col.GetComponentInParent<Health>();
            if (victim == null || victim.gameObject == gameObject || victim.currentHealth.Value <= 0f) continue;
            if (Combat.SameTeam(gameObject, victim.gameObject)) continue;
            var victimHero = victim.GetComponent<PlayerHero>();
            if (victimHero != null && (victimHero.IsUntargetable || victimHero.HiddenFromLocal)) continue;
            var networkObject = victim.GetComponent<NetworkObject>();
            if (networkObject == null || !networkObject.IsSpawned) continue;
            float distance = (victim.transform.position - transform.position).sqrMagnitude;
            if (distance < bestDistance) { bestDistance = distance; best = networkObject; }
        }
        return best;
    }

    [ServerRpc]
    void ReapAudioServerRpc() => ReapAudioClientRpc();

    [ClientRpc]
    void ReapAudioClientRpc()
    {
        if (!IsOwner && !ReplayPlayer.Active)
            ProceduralSfx.PlayForActor(this, CollectedAudioBank.Pick("max_reap", ProceduralSfx.Dash), transform.position, 0.7f);
    }

    // usek od odrazu po konec; dopad (uder) prijde presne na konci skoku (LeapTime)
    void PlayLeap(CharacterVisual visual)
    {
        LeapClip(out float from, out float impact, out float to);
        visual.PlayAbility(CharacterVisual.AbilityAnim.Shift, from, to, LeapTime * (to - from) / (impact - from));
    }

    [ServerRpc]
    void LeapServerRpc() => LeapClientRpc();

    [ClientRpc]
    void LeapClientRpc()
    {
        if (IsOwner) return;   // majitel uz prehral sam
        var visual = GetComponent<CharacterVisual>();
        if (visual != null) PlayLeap(visual);
    }

    // ---------------- server: odolnost pri vypadu ----------------

    float guardUntil = -1f;    // server: do kdy dostava mene poskozeni

    [ServerRpc]
    void ReapGuardServerRpc(float seconds) => guardUntil = Mathf.Max(guardUntil, Time.time + Mathf.Clamp(seconds, 0f, 1f) + GuardAfter);

    // Nasobek poskozeni, ktere 'target' prave dostava (odolnost pri vypadu).
    public static float IncomingScale(GameObject target)
    {
        if (target == null || !target.TryGetComponent<MaxAbility>(out var max) || !max.isActiveAndEnabled) return 1f;
        return Time.time < max.guardUntil ? 1f - GuardReduction : 1f;
    }

    // Vystup ze stinu utokem: temne nasati (vlastnik hned, ostatni pres server).
    [ClientRpc]
    void EmergeClientRpc()
    {
        if (IsOwner) return;
        ProceduralSfx.Play(ProceduralSfx.ShadowEmerge, transform.position + Vector3.up, 0.9f, 35f);
    }

    [ServerRpc]
    void ReapServerRpc(Vector3 position, NetworkObjectReference struckRef, bool hasStruck)
    {
        guardUntil = Mathf.Max(guardUntil, Time.time + GuardAfter);
        if (reap == null || (position - transform.position).sqrMagnitude > 25f) position = transform.position;
        var match = MatchManager.Instance;
        int hits = 0;
        if (match == null || (!match.IsOver && !match.IsLobby))
        {
            var done = new System.Collections.Generic.HashSet<Health>();
            Vector3 center = position + Vector3.up * 1f;
            // nepritel, o ktereho se vypad zastavil: zasah a omraceni vzdy (i kdyby stal tesne za hranou okruhu)
            if (hasStruck && struckRef.TryGet(out NetworkObject struckObject))
            {
                var victim = struckObject.GetComponent<Health>();
                if (victim != null && victim.gameObject != gameObject && victim.currentHealth.Value > 0f
                    && !Combat.SameTeam(gameObject, victim.gameObject)
                    && (victim.transform.position - position).sqrMagnitude < 3.5f * 3.5f)
                {
                    done.Add(victim);
                    Combat.DamagePlayer(gameObject, victim, reap.power, Combat.AbilitySource(reap));
                    if (victim.currentHealth.Value > 0f && victim.TryGetComponent<FirstPersonController>(out var stunnedTarget))
                        stunnedTarget.ServerStun(ReapStun, gameObject, false);
                    hits++;
                }
            }
            foreach (var col in Physics.OverlapSphere(center, reap.radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var victim = col.GetComponentInParent<Health>();
                if (victim == null || victim.gameObject == gameObject || !done.Add(victim) || victim.currentHealth.Value <= 0f) continue;
                if (Combat.SameTeam(gameObject, victim.gameObject) || !Combat.HasLineOfSight(center, col)) continue;
                Combat.DamagePlayer(gameObject, victim, reap.power, Combat.AbilitySource(reap));
                if (victim.currentHealth.Value > 0f && victim.TryGetComponent<FirstPersonController>(out var stunned))
                    stunned.ServerStun(ReapStun, gameObject, false);
                hits++;
            }
        }
        ReapClientRpc(position, hits);
    }

    [ClientRpc]
    void ReapClientRpc(Vector3 position, int hits)
    {
        // otocka kosou: fialovy kruh jisker
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            Fx.Sparks(position + new Vector3(Mathf.Cos(a), 1f, Mathf.Sin(a)) * (reap != null ? reap.radius * 0.7f : 2f), ShadowColor);
        }
        ProceduralSfx.PlayForActor(this, ProceduralSfx.Hit, position, 0.9f);
    }

    // ---------------- E: Stinova forma ----------------

    // Ve stinu je Max jen o malo rychlejsi nez bezny strelec (chuze, rychlost hrdiny 1; sprint ma jen hrdina
    // bez schopnosti na Shiftu): walkSpeed * 1,25 = 6,25 m/s (strelec 5, Max normalne 6). Odvozeno od chuze,
    // takze se prizpusobi, kdyz se zmeni rychlost postav.
    const float ShadowSpeedFactor = 1.25f;
    float ShadowSpeed => fpc.walkSpeed * fpc.SpeedMultiplier * ShadowSpeedFactor;
    const float FloorProbe = 1.1f;          // jak vysoko nad chodidly hleda podlahu (schody nahoru)
    bool inShadow;
    float shadowEnd;
    Vector3 shadowEntry;

    // Kour stinu (vidi vsichni): zapina server pri vstupu do formy.
    readonly NetworkVariable<bool> shadowForm = new NetworkVariable<bool>(false);
    ParticleSystem shadowSmoke;

    public bool InShadow => inShadow;

    public override void OnNetworkSpawn()
    {
        shadowForm.OnValueChanged += OnShadowChanged;
        if (shadowForm.Value) OnShadowChanged(false, true);
    }

    public override void OnNetworkDespawn()
    {
        shadowForm.OnValueChanged -= OnShadowChanged;
    }

    public bool ShadowFormActive => shadowForm.Value;
    static readonly Color ShadowTint = new Color(0.16f, 0.02f, 0.26f, 0.45f);

    // Vzhled stinove formy (jako Kaynuv Stinovy krok): Max je pruhledny tmave fialovy stin, za nim tahne
    // kourova stopa s jiskrami; vlastnik ma tmave fialovy nadech obrazovky. Vstup a vystup = vyron stinu.
    void OnShadowChanged(bool previous, bool current)
    {
        var visual = GetComponent<CharacterVisual>();
        if (visual != null) visual.SetShadow(current);

        // cizi klienti: stin nema kolizi (vlastnik si ji ridi sam)
        if (!IsOwner)
        {
            var body = GetComponent<CharacterController>();
            if (body != null) body.enabled = !current;
        }

        if (shadowSmoke == null) shadowSmoke = CreateShadowTrail();
        if (current) shadowSmoke.Play();
        else shadowSmoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        ShadowBurst();

        if (IsOwner && BotBrain.IsLocalHuman(this))
        {
            if (current) HudUI.NotifyTint(ShadowTint, step != null && step.duration > 0.2f ? step.duration : 3f);
            else HudUI.EndOverlay();
        }
    }

    void ShadowBurst()
    {
        Vector3 chest = transform.position + Vector3.up * 1.1f;
        if (shadowSmoke != null) shadowSmoke.Emit(40);
        Fx.Sparks(chest, ShadowColor);
        Fx.Sparks(chest + Vector3.up * 0.4f, new Color(0.9f, 0.5f, 1f));
        ProceduralSfx.PlayForActor(this, CollectedAudioBank.Pick("max_shadow", ProceduralSfx.Dash), transform.position, 0.75f, 25f);
    }

    // Kourova stopa: tmave fialove chuchvalce ve svete (zustavaji za Maxem) a svetle fialove jiskricky.
    ParticleSystem CreateShadowTrail()
    {
        var go = new GameObject("FX_StinovaForma");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 1f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.08f, 0.0f, 0.14f, 0.75f), new Color(0.35f, 0.08f, 0.6f, 0.6f));
        main.gravityModifier = -0.15f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;
        var emission = ps.emission;
        emission.rateOverTime = 45f;
        emission.rateOverDistance = 6f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.35f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.4f)));
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(new Color(0.55f, 0.2f, 0.9f), 0f), new GradientColorKey(new Color(0.05f, 0f, 0.1f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.15f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        go.GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;
        return ps;
    }

    const float ShadowGravity = 20f;
    float shadowFall;

    // Nejvyssi pevna podlaha pod mistem (nejvys FloorProbe nad chodidly - schody), hleda az 80 m dolu.
    // Postavy a jine sitove objekty (a Glum) se nepocitaji.
    static bool ShadowGround(Vector3 feet, out float groundY)
    {
        groundY = 0f;
        var hits = Physics.RaycastAll(feet + Vector3.up * FloorProbe, Vector3.down, FloorProbe + 80f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() != null || hit.collider.GetComponentInParent<GollumHitbox>() != null) continue;
            groundY = hit.point.y;
            return true;
        }
        return false;
    }

    void EnterShadow()
    {
        inShadow = true;
        shadowFall = 0f;
        shadowEntry = transform.position;
        shadowEnd = Time.time + (step.duration > 0.2f ? step.duration : 3f);
        GetComponent<CharacterController>().enabled = false;   // skrz zdi
        fpc.AbilityActive = true;                              // bezny pohyb stoji, ridi ho stin
        fpc.ResetVertical();
        hero.SayAbility(step);
        EnterShadowServerRpc(shadowEnd - Time.time);
    }

    void ShadowTick()
    {
        // konec: cas, utok (LMB, PTM, Shift, Q), dalsi E, smrt nebo omraceni
        bool attack = HeroInput.Pressed(this, HeroInput.Key.LeftMouse) || HeroInput.Pressed(this, HeroInput.Key.RightMouse)
            || HeroInput.Pressed(this, HeroInput.Key.Shift) || HeroInput.Pressed(this, HeroInput.Key.Q);
        bool again = HeroInput.Pressed(this, HeroInput.Key.E) && Time.time > shadowEnd - (step.duration > 0.2f ? step.duration : 3f) + 0.2f;
        if (Time.time >= shadowEnd || attack || again || fpc.IsDead || fpc.Stunned || fpc.MatchOver)
        {
            ExitShadow(attack && !fpc.IsDead && !fpc.Stunned);
            return;
        }

        // pohyb ve stinu: WASD podle smeru kamery, skrz vsechno; vyska podle podlahy pod sebou
        Vector2 input = Vector2.zero;
        if (HeroInput.MoveKey(this, 'w')) input.y += 1f;
        if (HeroInput.MoveKey(this, 's')) input.y -= 1f;
        if (HeroInput.MoveKey(this, 'd')) input.x += 1f;
        if (HeroInput.MoveKey(this, 'a')) input.x -= 1f;
        Vector3 forward = LookFlat();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 move = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f) * ShadowSpeed * Time.deltaTime;

        Vector3 next = transform.position + move;
        if (ShadowGround(next, out float groundY))
        {
            if (next.y - groundY <= 0.35f)
            {
                // schody nahoru / mirne dolu: plynule po podlaze
                next.y = Mathf.MoveTowards(transform.position.y, groundY, 6f * Time.deltaTime);
                shadowFall = 0f;
            }
            else
            {
                // pod nim neni podlaha (okno z patra, okraj strechy): pada jako normalne (zrychluje), na zem
                shadowFall = Mathf.Min(shadowFall + ShadowGravity * Time.deltaTime, 30f);
                next.y = Mathf.Max(groundY, transform.position.y - shadowFall * Time.deltaTime);
            }
        }
        transform.position = next;

        // ve stinu bez zbrane a rukou (HeldWeapons skryva pri IsHidden), mys se otaci normalne (FirstPersonController)
    }

    void ExitShadow(bool byAttack = false)
    {
        inShadow = false;
        var controller = GetComponent<CharacterController>();
        Vector3 spot = FreeSpot(transform.position, controller);
        transform.position = spot;
        controller.enabled = true;
        fpc.AbilityActive = false;
        fpc.ResetVertical();
        fpc.OwnerSpeedBoost(1.5f, 1.3f);
        nextStep = Time.time + (step != null ? step.Cooldown : 12f);
        if (byAttack) ProceduralSfx.PlayForActor(this, ProceduralSfx.ShadowEmerge, transform.position + Vector3.up, 0.9f, 35f);
        ExitShadowServerRpc(byAttack);
    }

    // Volne misto na stani co nejbliz (forma muze skoncit ve zdi): kruhy do 5 m, jinak zpet tam, kde zacala.
    Vector3 FreeSpot(Vector3 around, CharacterController controller)
    {
        if (Fits(around, controller, out Vector3 here)) return here;
        for (float r = 0.5f; r <= 5f; r += 0.5f)
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI * 2f / 16f;
                if (Fits(around + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r, controller, out Vector3 spot))
                    return spot;
            }
        return shadowEntry;
    }

    static bool Fits(Vector3 position, CharacterController controller, out Vector3 standing)
    {
        standing = position;
        if (!Physics.Raycast(position + Vector3.up * FloorProbe, Vector3.down, out RaycastHit floor, FloorProbe + 4f, ~0, QueryTriggerInteraction.Ignore))
            return false;
        Vector3 feet = floor.point + Vector3.up * 0.03f;
        Vector3 low = feet + Vector3.up * (controller.radius + 0.1f);
        Vector3 high = feet + Vector3.up * (controller.height - controller.radius);
        if (Physics.CheckCapsule(low, high, controller.radius, ~0, QueryTriggerInteraction.Ignore)) return false;
        standing = feet;
        return true;
    }

    public void CancelShadow()
    {
        if (inShadow) ExitShadow();
    }

    [ServerRpc]
    void EnterShadowServerRpc(float seconds)
    {
        health.ServerInvulnerable(Mathf.Clamp(seconds, 0f, 5f) + 0.5f);
        shadowForm.Value = true;   // viditelny stin, ale necilitelny (bez kolize, nezranitelny)
    }

    [ServerRpc]
    void ExitShadowServerRpc(bool byAttack)
    {
        shadowForm.Value = false;
        health.ServerClearInvulnerable();
        if (byAttack)
            EmergeClientRpc();
    }

    // ---------------- PTM: Otocka kosou ----------------

    // Usek animace otocky (Mixamo, 30 fps): od rozmachu po dotoceni; kosa projede kolem Maxe zhruba v SpinHitAt.
    const float SpinClipFrom = 0.1f, SpinClipTo = 1.9f;
    const float SpinTime = 0.65f;
    const float SpinHitAt = 0.25f;
    const float SpinMoveScale = 0.6f;
    const float SpinFallbackRadius = 3.6f;
    // Behem otocky je kamera za Maxem, at hrac vidi svoji animaci (jako u Modreho plamene).
    static readonly Vector3 SpinCameraOffset = new Vector3(0f, 2.0f, -4.5f);
    Vector3 spinCameraLocal;
    bool spinCamera;

    float SpinRadius => slash.radius > 0.5f ? slash.radius : SpinFallbackRadius;

    IEnumerator Slash()
    {
        busy = true;
        nextSlash = Time.time + slash.Cooldown;
        hero.SayAbility(slash);
        PlaySpin();
        SpinServerRpc();
        fpc.AbilitySpeedScale = SpinMoveScale;
        var visual = GetComponent<CharacterVisual>();
        if (fpc.Bot == null && visual != null && visual.HasAbilityAnimation(CharacterVisual.AbilityAnim.RightMouse))
        {
            spinCameraLocal = fpc.playerCamera.transform.localPosition;
            spinCamera = fpc.ThirdPersonView = true;
        }

        float start = Time.time;
        bool struck = false;
        while (Time.time - start < SpinTime && !fpc.CannotAct)
        {
            if (!struck && Time.time - start >= SpinHitAt)
            {
                struck = true;
                if (held != null) held.Swing();
                SlashServerRpc(transform.position);
            }
            if (spinCamera)
                ThirdPersonCamera.Place(transform, NetworkObject, fpc.playerCamera, spinCameraLocal, SpinCameraOffset);
            yield return null;
        }

        EndSpinCamera();
        fpc.AbilitySpeedScale = 1f;
        busy = false;
    }

    void EndSpinCamera()
    {
        if (!spinCamera) return;
        spinCamera = false;
        fpc.ThirdPersonView = false;
        if (fpc.playerCamera != null) fpc.playerCamera.transform.localPosition = spinCameraLocal;
    }

    void OnDisable()
    {
        if (fpc != null) EndSpinCamera();
    }

    void PlaySpin()
    {
        var visual = GetComponent<CharacterVisual>();
        if (visual != null) visual.PlayAbility(CharacterVisual.AbilityAnim.RightMouse, SpinClipFrom, SpinClipTo, SpinTime);
    }

    [ServerRpc]
    void SpinServerRpc() => SpinClientRpc();

    [ClientRpc]
    void SpinClientRpc()
    {
        ProceduralSfx.PlayForActor(this, CollectedAudioBank.Pick("max_slash", ProceduralSfx.Dash), transform.position, 0.8f);
        if (!IsOwner) PlaySpin();   // majitel uz prehral sam
    }

    [ServerRpc]
    void SlashServerRpc(Vector3 position)
    {
        if (slash == null) return;
        if ((position - transform.position).sqrMagnitude > 9f) position = transform.position;

        var match = MatchManager.Instance;
        if (match == null || (!match.IsOver && !match.IsLobby))
        {
            var done = new System.Collections.Generic.HashSet<Health>();
            Vector3 center = position + Vector3.up * 1f;
            foreach (var col in Physics.OverlapSphere(center, SpinRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var victim = col.GetComponentInParent<Health>();
                if (victim == null || victim.gameObject == gameObject || !done.Add(victim) || victim.currentHealth.Value <= 0f) continue;
                if (Combat.SameTeam(gameObject, victim.gameObject) || !Combat.HasLineOfSight(center, col)) continue;
                Combat.DamagePlayer(gameObject, victim, slash.power, Combat.AbilitySource(slash));
            }
        }
        SlashClientRpc(position);
    }

    [ClientRpc]
    void SlashClientRpc(Vector3 position)
    {
        ProceduralSfx.PlayForActor(this, ProceduralSfx.Hit, position, 0.8f);
        SpinRing(position + Vector3.up * 1f, SpinRadius * 0.85f);
    }

    // Fialovy kruh rezu kolem Maxe (vybledne) a jiskry po obvodu.
    static void SpinRing(Vector3 center, float radius)
    {
        const int Points = 33;
        var line = new GameObject("FX_OtockaKosou").AddComponent<LineRenderer>();
        line.positionCount = Points;
        line.material = Fx.ParticleMaterial;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.alignment = LineAlignment.View;
        line.widthMultiplier = 0.22f;
        for (int i = 0; i < Points; i++)
        {
            float a = i * Mathf.PI * 2f / (Points - 1);
            line.SetPosition(i, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
        }
        line.startColor = new Color(0.75f, 0.35f, 1f, 0.9f);
        line.endColor = new Color(0.35f, 0.05f, 0.6f, 0.8f);
        line.gameObject.AddComponent<FadeLine>().Init(line, 0.35f);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            Fx.Sparks(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius, ShadowColor);
        }
    }
}

// Cara, ktera behem 'seconds' vybledne a zmizi (efekt rezu).
public class FadeLine : MonoBehaviour
{
    LineRenderer line;
    float seconds, age;
    Color start, end;

    public void Init(LineRenderer target, float duration)
    {
        line = target;
        seconds = duration;
        start = line.startColor;
        end = line.endColor;
    }

    void Update()
    {
        age += Time.deltaTime;
        float k = 1f - age / seconds;
        if (k <= 0f) { Destroy(gameObject); return; }
        line.startColor = new Color(start.r, start.g, start.b, start.a * k);
        line.endColor = new Color(end.r, end.g, end.b, end.a * k);
    }
}
