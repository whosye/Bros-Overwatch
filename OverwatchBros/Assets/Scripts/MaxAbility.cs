using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Max - Shift, E a prave tlacitko v jedne komponente:
//   Shift (Rozpolceni): vypad 'range' metru ve smeru pohledu a na konci otocka kosou - 'power' vsem v okruhu
//     'radius'. Za kazdeho zasazeneho nepritele se cooldown zkrati o 1 s.
//   E (Stinova forma): az 'duration' s je Max stin - neviditelny (jen fialovy kour), nezranitelny, bez kolize
//     a chodi skrz zdi. Forma skonci utokem (LMB, PTM, Shift, Q), dalsim E nebo vyprsenim casu; kdyz konci ve zdi,
//     vystrci ho to na nejblizsi volne misto. Cooldown bezi az od konce formy.
//   PTM (Dlouhy rez, jako Kaynovo W): drzenim se nabiji (kosa v napraahu, Max jde pomalu): vlna dosahne od 4 m
//     az po 'range' m. Pustenim mohutny sek dopredu a vyjede vlna rezu v pruhu dosah x 2,4 m: 'power' poskozeni, vsechny v ceste vyhodi do vzduchu
//     a zpomali. Nejde pres zdi, nezrani vlastni tym.
public class MaxAbility : NetworkBehaviour
{
    public AbilityDefinition reap;    // Shift
    public AbilityDefinition step;    // E
    public AbilityDefinition slash;   // PTM

    public static readonly Color ShadowColor = new Color(0.55f, 0.18f, 0.85f, 1f);

    const float DashTime = 0.18f;
    const float SlashWidth = 2.4f;       // sirka pruhu Dlouheho rezu
    const float KnockupSpeed = 9f;       // zasazene vyhodi do vzduchu (asi 4 m vysoko)
    const float KnockupPush = 2.5f;
    const float SlowFactor = 0.6f;       // zpomaleni o 40 %
    const float SlowSeconds = 1.5f;

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
        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.7f);

        // smer pohledu; pri pohledu nahoru i kousek do vyse
        Vector3 look = fpc.playerCamera.transform.forward;
        Vector3 direction = LookFlat() + Vector3.up * Mathf.Clamp(look.y, 0f, 0.5f);
        float speed = (reap.range > 0f ? reap.range : 6f) / DashTime;
        var controller = GetComponent<CharacterController>();
        float elapsed = 0f;
        while (elapsed < DashTime && !fpc.CannotAct)
        {
            controller.Move(direction * speed * Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }
        fpc.ResetVertical();
        if (held != null) held.Swing();
        ReapServerRpc(transform.position);
        busy = false;
    }

    [ServerRpc]
    void ReapServerRpc(Vector3 position)
    {
        if (reap == null || (position - transform.position).sqrMagnitude > 25f) position = transform.position;
        var match = MatchManager.Instance;
        int hits = 0;
        if (match == null || (!match.IsOver && !match.IsLobby))
        {
            var done = new System.Collections.Generic.HashSet<Health>();
            Vector3 center = position + Vector3.up * 1f;
            foreach (var col in Physics.OverlapSphere(center, reap.radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var victim = col.GetComponentInParent<Health>();
                if (victim == null || victim.gameObject == gameObject || !done.Add(victim) || victim.currentHealth.Value <= 0f) continue;
                if (Combat.SameTeam(gameObject, victim.gameObject) || !Combat.HasLineOfSight(center, col)) continue;
                Combat.DamagePlayer(gameObject, victim, reap.power, Combat.AbilitySource(reap));
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
        ProceduralSfx.Play(ProceduralSfx.Hit, position, 0.9f);
        // za kazdeho zasazeneho o sekundu kratsi cooldown
        if (IsOwner && hits > 0)
            nextReap = Mathf.Max(Time.time, nextReap - hits);
    }

    // ---------------- E: Stinova forma ----------------

    const float ShadowSpeed = 8f;           // m/s ve stinu (o neco rychleji nez chuze)
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
        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.9f);
        ProceduralSfx.Play(ProceduralSfx.LeapStart, transform.position, 0.35f);
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

    void EnterShadow()
    {
        inShadow = true;
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
            ExitShadow();
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
        if (Physics.Raycast(next + Vector3.up * FloorProbe, Vector3.down, out RaycastHit floor, FloorProbe + 3f, ~0, QueryTriggerInteraction.Ignore)
            && floor.collider.GetComponentInParent<NetworkObject>() == null)
            next.y = Mathf.MoveTowards(transform.position.y, floor.point.y, 6f * Time.deltaTime);
        transform.position = next;

        // ve stinu bez zbrane a rukou (HeldWeapons skryva pri IsHidden), mys se otaci normalne (FirstPersonController)
    }

    void ExitShadow()
    {
        inShadow = false;
        var controller = GetComponent<CharacterController>();
        Vector3 spot = FreeSpot(transform.position, controller);
        transform.position = spot;
        controller.enabled = true;
        fpc.AbilityActive = false;
        fpc.ResetVertical();
        fpc.OwnerSpeedBoost(1.5f, 1.3f);
        nextStep = Time.time + (step != null ? step.Cooldown : 9f);
        ExitShadowServerRpc();
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
    void ExitShadowServerRpc()
    {
        shadowForm.Value = false;
        health.ServerClearInvulnerable();
    }

    // ---------------- PTM: Dlouhy rez ----------------

    // Nabijeni: cim dele drzi PTM, tim dal vlna dosahne (MinReach -> 'range' za ChargeTime s). Behem nabijeni
    // drzi kosu v napraahu, jde jen pomalu a na zemi vidi, kam vlna dosahne. Pustenim (nebo po MaxHold s) sekne.
    const float MinReach = 4f;
    const float ChargeTime = 1f;
    const float MaxHold = 2.5f;
    const float MinWindup = 0.2f;        // aspon kratky napraah, i pri rychlem kliknuti
    const float ChargeMoveScale = 0.4f;

    IEnumerator Slash()
    {
        busy = true;
        hero.SayAbility(slash);
        if (held != null) held.BeginReachHold();
        SlashWindupServerRpc();
        float maxReach = slash.range > MinReach ? slash.range : 10f;
        float start = Time.time;
        var preview = CreateReachPreview();
        fpc.AbilitySpeedScale = ChargeMoveScale;

        float reach = MinReach;
        while (true)
        {
            float held01 = Mathf.Clamp01((Time.time - start) / ChargeTime);
            reach = Mathf.Lerp(MinReach, maxReach, held01);
            UpdateReachPreview(preview, reach);
            bool released = !HeroInput.Held(this, HeroInput.Key.RightMouse) && Time.time - start >= MinWindup;
            if (released || Time.time - start >= MaxHold || fpc.CannotAct) break;
            yield return null;
        }

        fpc.AbilitySpeedScale = 1f;
        if (preview != null) Destroy(preview.gameObject);
        nextSlash = Time.time + slash.Cooldown;
        if (fpc.CannotAct)
        {
            busy = false;
            yield break;
        }
        if (held != null) held.ReleaseReach();
        var cam = fpc.playerCamera.transform;
        SlashServerRpc(cam.position, LookFlat(), reach);
        busy = false;
    }

    // Nahled dosahu (jen vlastnik): fialovy pruh na zemi pred Maxem.
    LineRenderer CreateReachPreview()
    {
        var go = new GameObject("NahledDosahu");
        var line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.material = Fx.ParticleMaterial;
        line.alignment = LineAlignment.TransformZ;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        line.widthMultiplier = SlashWidth;
        line.startColor = new Color(0.55f, 0.18f, 0.85f, 0.15f);
        line.endColor = new Color(0.75f, 0.4f, 1f, 0.45f);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return line;
    }

    void UpdateReachPreview(LineRenderer line, float reach)
    {
        if (line == null) return;
        Vector3 direction = LookFlat();
        Vector3 feet = transform.position + Vector3.up * 0.06f;
        line.SetPosition(0, feet + direction * 0.6f);
        line.SetPosition(1, feet + direction * reach);
    }

    [ServerRpc]
    void SlashWindupServerRpc() => SlashWindupClientRpc();

    [ClientRpc]
    void SlashWindupClientRpc()
    {
        // napraah: slysitelne a videt (fialova jiskra u Maxe), at se da utect
        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.5f);
        Fx.Sparks(transform.position + Vector3.up * 1.8f, ShadowColor);
    }

    [ServerRpc]
    void SlashServerRpc(Vector3 origin, Vector3 direction, float reach)
    {
        if (slash == null) return;
        if ((origin - transform.position).sqrMagnitude > 9f) origin = transform.position + Vector3.up * 1.6f;
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
        float length = Mathf.Clamp(reach, MinReach, slash.range > MinReach ? slash.range : 10f);

        var match = MatchManager.Instance;
        if (match == null || (!match.IsOver && !match.IsLobby))
        {
            var done = new System.Collections.Generic.HashSet<Health>();
            // dlouhy pruh pred Maxem (jako Kaynovo W): 'range' m dopredu, 'SlashWidth' m siroky
            Vector3 center = origin + direction * (length * 0.5f) - Vector3.up * 0.6f;
            var rotation = Quaternion.LookRotation(direction, Vector3.up);
            foreach (var col in Physics.OverlapBox(center, new Vector3(SlashWidth * 0.5f, 1.4f, length * 0.5f), rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                var victim = col.GetComponentInParent<Health>();
                if (victim == null || victim.gameObject == gameObject || done.Contains(victim) || victim.currentHealth.Value <= 0f) continue;
                Vector3 to = col.ClosestPoint(origin) - origin;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                done.Add(victim);
                if (Combat.SameTeam(gameObject, victim.gameObject) || !Combat.HasLineOfSight(origin, col)) continue;
                Combat.DamagePlayer(gameObject, victim, slash.power, Combat.AbilitySource(slash));
                var movement = victim.GetComponent<FirstPersonController>();
                if (movement != null && victim.currentHealth.Value > 0f)
                {
                    // vyhozeni do vzduchu (knockup), kousek od Maxe, a po dopadu zpomaleni
                    Vector3 away = flat.sqrMagnitude > 0.01f ? flat.normalized : direction;
                    movement.ServerKnockback(away * KnockupPush + Vector3.up * KnockupSpeed);
                    movement.ServerSlow(SlowSeconds, SlowFactor);
                }
            }
        }
        SlashClientRpc(origin, direction, length);
    }

    [ClientRpc]
    void SlashClientRpc(Vector3 origin, Vector3 direction, float length)
    {
        ProceduralSfx.Play(ProceduralSfx.Dash, origin, 1f);
        ProceduralSfx.Play(ProceduralSfx.LeapStart, origin, 0.4f);
        var wave = new GameObject("FX_DlouhyRez").AddComponent<ReachWave>();
        wave.Init(origin - Vector3.up * 1.55f, direction, length, SlashWidth);   // (origin = oci, vlna jede po zemi)
    }
}

// Vlna Dlouheho rezu: od Maxe dopredu vyjede rada fialovych srpovitych seku (napric pruhem), po zemi tmava stopa
// a jiskry; vse behem chvilky vybledne.
public class ReachWave : MonoBehaviour
{
    const float Travel = 0.22f;      // jak rychle vlna dojede na konec pruhu
    const int Arcs = 7;
    Vector3 origin, direction;
    float length, width, age;
    int spawned;

    public void Init(Vector3 groundOrigin, Vector3 forward, float reach, float stripWidth)
    {
        origin = groundOrigin;
        direction = forward;
        length = reach;
        width = stripWidth;

        // tmava stopa po zemi pres cely pruh
        var streak = NewLine("Stopa", 2);
        streak.SetPosition(0, origin + direction * 0.8f + Vector3.up * 0.05f);
        streak.SetPosition(1, origin + direction * length + Vector3.up * 0.05f);
        streak.alignment = LineAlignment.TransformZ;
        streak.transform.rotation = Quaternion.LookRotation(Vector3.down, direction);
        streak.widthCurve = new AnimationCurve(new Keyframe(0f, width * 0.5f), new Keyframe(1f, width * 0.9f));
        streak.startColor = new Color(0.12f, 0.02f, 0.2f, 0.75f);
        streak.endColor = new Color(0.45f, 0.12f, 0.8f, 0.5f);
        streak.gameObject.AddComponent<FadeLine>().Init(streak, 0.6f);
    }

    LineRenderer NewLine(string name, int points)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.positionCount = points;
        line.material = Fx.ParticleMaterial;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return line;
    }

    void Update()
    {
        age += Time.deltaTime;
        int due = Mathf.Min(Arcs, Mathf.FloorToInt(age / Travel * Arcs) + 1);
        while (spawned < due)
        {
            float along = Mathf.Lerp(1.2f, length, spawned / (Arcs - 1f));
            SpawnArc(origin + direction * along);
            spawned++;
        }
        if (age > Travel + 0.8f) Destroy(gameObject);
    }

    // Srpovity sek napric pruhem: oblouk ze zeme nahoru a zpet, sviti fialove.
    void SpawnArc(Vector3 center)
    {
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        var arc = NewLine("Sek", 14);
        for (int i = 0; i < 14; i++)
        {
            float u = i / 13f;
            float height = Mathf.Sin(u * Mathf.PI) * 1.6f;
            Vector3 p = center + side * (u - 0.5f) * width + Vector3.up * (0.1f + height) + direction * Mathf.Sin(u * Mathf.PI) * 0.35f;
            arc.SetPosition(i, p);
        }
        arc.alignment = LineAlignment.View;
        arc.widthCurve = new AnimationCurve(new Keyframe(0f, 0.02f), new Keyframe(0.5f, 0.28f), new Keyframe(1f, 0.02f));
        arc.startColor = new Color(0.75f, 0.35f, 1f, 0.95f);
        arc.endColor = new Color(0.35f, 0.05f, 0.6f, 0.8f);
        arc.gameObject.AddComponent<FadeLine>().Init(arc, 0.35f);
        Fx.Sparks(center + Vector3.up * 0.6f, MaxAbility.ShadowColor);
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
