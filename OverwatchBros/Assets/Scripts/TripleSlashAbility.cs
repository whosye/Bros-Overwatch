using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Povuv Trojsek (podle Aatroxova Q) jako ZAKLADNI UTOK (leve tlacitko, misto obycejnych seku sekyrkami): kazde kliknuti
// (nebo drzeni) udela dalsi uder rotace, po tretim zase od zacatku. Kdyz chvili (RotationReset s) neutoci, zacne od 1.
//   1. Sek pravou - dlouhy uzky obdelnik pred sebe, maly krok dopredu.
//   2. Sek levou (zrcadlova animace) - kuzel na blizko, zasazene kratce zpomali.
//   3. Drtivy dopad - poskok a uder do zeme v kruhu kolem sebe, zasazene vyhodi do vzduchu.
// Kombo Shift -> LMB: kliknuti behem Drtiveho skoku (nebo hned po dopadu) = hned po dopadu aktualni uder rotace
// s dlouhym vypadem dopredu (bez cekani na dalsi uder).
// Sladky bod: kdo stoji na kraji zasahu (posledni ~30 % dosahu), dostane o SweetBonus vic. Poskozeni ze zbrane
// (WeaponDefinition.damage = 1. uder); leceni z poskozeni dela Povuv lifesteal (Combat). Zapina ho
// WeaponDefinition.comboRotation (WeaponShooting pak nesekne). Animace postavy: CharacterAnimSet combo1-3.
public class TripleSlashAbility : NetworkBehaviour
{
    public WeaponDefinition weapon;

    const float RotationReset = 1.2f;   // po tolika s bez utoku zacne rotace znovu od 1. uderu
    const float SweetBonus = 1.4f;
    const float SweetStart = 0.7f;
    static readonly Color FireColor = new Color(1f, 0.5f, 0.15f, 1f);

    // per uder: nasobek poskozeni, dosah, polovicni uhel (0 = pas), sirka pasu, krok dopredu, doba uderu (do dalsiho),
    // delka animace (muze byt delsi nez doba uderu - dalsi uder ji plynule prerusi)
    struct Step { public float damage, reach, halfAngle, width, lunge, recover, anim; }
    static readonly Step[] Steps =
    {
        new Step { damage = 1.0f, reach = 4.2f, width = 0.8f, lunge = 0.5f, recover = 0.42f, anim = 0.6f },      // uzky obdelnik pred sebe
        new Step { damage = 1.15f, reach = 2.8f, halfAngle = 50f, lunge = 0.6f, recover = 0.42f, anim = 0.6f },  // kuzel, blizsi
        new Step { damage = 1.6f, reach = 3f, lunge = 0.3f, recover = 0.7f, anim = 0.85f },                       // kruh kolem sebe
    };
    const float ComboLunge = 2.2f;      // Shift -> LMB: uder hned po dopadu skoku a s delsim vypadem dopredu
    const float ComboBuffer = 0.45f;    // LMB zmacknute az tolik s po dopadu se jeste pocita jako kombo
    bool comboQueued;
    float landedAt = -100f;
    const float SlowSeconds = 0.6f, SlowFactor = 0.7f;
    const float Knockup = 5f;
    const float HopTime = 0.3f, HopHeight = 0.6f, LungeTime = 0.12f;

    // ---- Drtivy skok (Shift, AbilityKind.GroundSlam): vyskok na misto v zamerovaci (do 'range' m) a uder do zeme -
    // 'power' vsem nepratelum v 'radius' kolem dopadu, zpomaleni a maly poskok. Cooldown 'cooldown'.
    public AbilityDefinition slam;
    float nextSlam;
    public float SlamRemaining => Mathf.Max(0f, nextSlam - Time.time);
    public bool Slamming => slamming;
    bool slamming;
    const float SlamSlowSeconds = 1.2f, SlamSlowFactor = 0.6f, SlamKnockup = 3.5f;

    public void ConfigureSlam(AbilityDefinition definition)
    {
        slam = definition;
        slamming = false;
    }

    FirstPersonController fpc;
    Health health;
    int stage;            // ktery uder je na rade (0-2)
    float nextAttack, lastAttack = -100f;
    bool busy;

    public int Stage => stage;

    public void Configure(WeaponDefinition definition)
    {
        weapon = definition;
        stage = 0;
        busy = false;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        health = GetComponent<Health>();
    }

    void OnDisable()
    {
        stage = 0;
        busy = false;
        slamming = false;
        comboQueued = false;
        if (area != null) area.SetActive(false);
    }

    void Update()
    {
        if (!IsOwner) return;
        if (slam != null && !slamming && !busy && Time.time >= nextSlam && HeroInput.Pressed(this, HeroInput.Key.Shift) && HeroInput.Locked(this)
            && !fpc.InputBlocked && !fpc.CannotAct && !fpc.Rooted && health.currentHealth.Value > 0f)
        {
            StartCoroutine(DoSlam());
            return;
        }
        if (weapon == null) return;
        if (slamming)
        {
            // kombo: LMB behem skoku = hned po dopadu aktualni uder rotace s vypadem dopredu
            if (HeroInput.Pressed(this, HeroInput.Key.LeftMouse) && HeroInput.Locked(this)) comboQueued = true;
            return;
        }
        if (stage > 0 && !busy && Time.time - lastAttack > RotationReset) stage = 0;
        if (busy) return;

        bool combo = comboQueued || (Time.time - landedAt < ComboBuffer && HeroInput.Pressed(this, HeroInput.Key.LeftMouse));
        if (!combo && Time.time < nextAttack) return;
        if (!combo && !HeroInput.Held(this, HeroInput.Key.LeftMouse)) return;
        if (!HeroInput.Locked(this)) return;
        if (fpc.ShootBlocked || fpc.RushActive || fpc.BlockActive || fpc.CannotAct || health.currentHealth.Value <= 0f) { comboQueued = false; return; }

        comboQueued = false;
        if (combo) landedAt = -100f;
        StartCoroutine(DoStep(stage, combo));
    }

    // ---------------- Drtivy skok ----------------

    // Misto dopadu: kam miri zamerovac (do 'range' m); kdyz miri do vzduchu, bod v dosahu a pod nim zem.
    Vector3 SlamTarget()
    {
        var eye = fpc.playerCamera.transform;
        float range = slam.range > 1f ? slam.range : 16f;
        Vector3 point;
        if (Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<FirstPersonController>() != fpc)
            point = hit.point + hit.normal * 0.2f;
        else
            point = eye.position + eye.forward * range;
        // vodorovne nejvys 'range' od hrace, a na zem pod bodem
        Vector3 flat = point - transform.position; flat.y = 0f;
        if (flat.magnitude > range) point = transform.position + flat.normalized * range + Vector3.up * (point.y - transform.position.y);
        if (Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out RaycastHit ground, 25f, ~0, QueryTriggerInteraction.Ignore))
            point = ground.point;
        return point;
    }

    IEnumerator DoSlam()
    {
        slamming = true;
        comboQueued = false;
        nextSlam = Time.time + slam.Cooldown;
        GetComponent<PlayerHero>()?.SayAbility(slam);
        Vector3 from = transform.position, to = SlamTarget();
        float distance = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(to.x, 0f, to.z));
        float time = Mathf.Clamp(distance / 16f, 0.35f, 0.7f);
        float arc = 1.8f + distance * 0.15f;
        SlamStartServerRpc(time);
        PlaySlamStart(time);

        var controller = fpc.Controller;
        float t = 0f;
        Vector3 last = from;
        while (t < time && !fpc.CannotAct)
        {
            t = Mathf.Min(time, t + Time.deltaTime);
            float k = t / time;
            Vector3 want = Vector3.Lerp(from, to, k) + Vector3.up * arc * 4f * k * (1f - k);
            controller.Move(want - transform.position);
            last = want;
            yield return null;
        }
        fpc.ResetVertical();
        var held = GetComponent<HeldWeapons>();
        if (held != null) held.Swing(2f);
        SlamServerRpc(transform.position);
        slamming = false;
        landedAt = Time.time;
        lastAttack = Time.time;   // rotace Trojseku po skoku pokracuje (nezacne od 1.)
    }

    void PlaySlamStart(float flight)
    {
        // animace: vlastni skok s uderem (sada animaci, policko shiftAbility), jinak 3. uder komba - nacasovano na dopad
        var visual = GetComponent<CharacterVisual>();
        var which = visual != null && visual.HasAbilityAnimation(CharacterVisual.AbilityAnim.Shift)
            ? CharacterVisual.AbilityAnim.Shift : CharacterVisual.AbilityAnim.Combo3;
        if (visual != null && visual.HasAbilityAnimation(which))
        {
            float length = visual.AbilityClipLength(which);
            var set = visual.HeroClips;
            if (which == CharacterVisual.AbilityAnim.Shift && set != null && set.shiftImpact > set.shiftFrom + 0.05f && set.shiftTo >= set.shiftImpact)
                // usek od odrazu po konec, uder do zeme presne pri dopadu (casy zmerene z klipu)
                visual.PlayAbility(which, set.shiftFrom, set.shiftTo, flight * (set.shiftTo - set.shiftFrom) / (set.shiftImpact - set.shiftFrom));
            else
                visual.PlayAbility(which, 0f, length, flight + 0.25f);
        }
        ProceduralSfx.PlayForActor(this, ProceduralSfx.LeapStart, transform.position, 0.6f);
    }

    [ServerRpc]
    void SlamStartServerRpc(float flight) => SlamStartClientRpc(Mathf.Clamp(flight, 0.2f, 1f));

    [ClientRpc]
    void SlamStartClientRpc(float flight)
    {
        if (!IsOwner) PlaySlamStart(flight);
    }

    [ServerRpc]
    void SlamServerRpc(Vector3 position)
    {
        if (slam == null) return;
        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;
        if (health.currentHealth.Value <= 0f) return;
        if ((position - transform.position).sqrMagnitude > 9f) position = transform.position;
        float radius = slam.radius > 0.5f ? slam.radius : 3.5f;
        Vector3 center = position + Vector3.up * 0.5f;
        var hit = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var dummy = col.GetComponentInParent<Target>();
            if (dummy != null) dummy.TakeDamage(slam.power);
            var victim = col.GetComponentInParent<Health>();
            if (victim == null || victim == health || !hit.Add(victim) || victim.currentHealth.Value <= 0f) continue;
            if (Combat.SameTeam(gameObject, victim.gameObject)) continue;
            if (!Combat.HasLineOfSight(center, col)) continue;
            Combat.DamagePlayer(gameObject, victim, slam.power, Combat.AbilitySource(slam));
            if (victim.currentHealth.Value > 0f && victim.TryGetComponent<FirstPersonController>(out var controller))
            {
                controller.ServerSlow(SlamSlowSeconds, SlamSlowFactor);
                Vector3 away = victim.transform.position - position; away.y = 0f;
                controller.ServerKnockback(Vector3.up * SlamKnockup + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.zero) * 2f);
            }
            var victimHero = victim.GetComponent<PlayerHero>();
            if (victimHero != null) victimHero.ServerNoteAttacker(gameObject);
            HitFxClientRpc(col.bounds.center, false);
        }
        SlamFxClientRpc(position, radius);
    }

    [ClientRpc]
    void SlamFxClientRpc(Vector3 position, float radius)
    {
        // dopad: dve razove vlny, zhave praskliny do vsech stran, mensi ohnivy vybuch
        Vector3 center = SlashGlow.Ground(position, 0.08f);
        SlashGlow.Shockwave(center, radius + 0.6f, 0.5f, 0.5f, 0.9f);
        SlashGlow.Shockwave(center, radius * 0.55f, 0.3f, 0.2f, 0.45f);
        int seed = Random.Range(0, 100000);
        float turn = Random.Range(0f, 45f);
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(turn + i * 45f + Random.Range(-10f, 10f), Vector3.up) * Vector3.forward;
            SlashGlow.Crack(center + dir * 0.4f, dir, radius * Random.Range(0.8f, 1.15f), 0.12f, 0.14f, 0.9f, seed + i);
        }
        Fx.Explosion(position + Vector3.up * 0.2f, radius * 0.3f);
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * Mathf.PI * 2f;
            Fx.Sparks(position + new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) * radius * 0.85f, FireColor);
        }
        ProceduralSfx.Play(ProceduralSfx.Explosion, position, 0.6f);
    }

    Vector3 LookFlat()
    {
        Vector3 forward = fpc.playerCamera.transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.001f ? forward.normalized : transform.forward;
    }

    IEnumerator DoStep(int step, bool combo = false)
    {
        busy = true;
        var s = Steps[step];
        if (combo) s.lunge = Mathf.Max(s.lunge, ComboLunge);
        lastAttack = Time.time;
        nextAttack = Time.time + s.recover;
        PlayStep(step);
        StepServerRpc(step);
        var held = GetComponent<HeldWeapons>();
        if (held != null) held.Swing(step == 2 ? 1.6f : 1f);

        Vector3 dir = LookFlat();
        var controller = fpc.Controller;
        if (step < 2)
        {
            float t = 0f, lungeTime = combo ? 0.2f : LungeTime;
            while (t < lungeTime && !fpc.CannotAct)
            {
                float dt = Mathf.Min(Time.deltaTime, lungeTime - t);
                t += dt;
                controller.Move(dir * (s.lunge / lungeTime) * dt);
                yield return null;
            }
            HitServerRpc(step, transform.position + Vector3.up * 1f, dir);
        }
        else
        {
            float t = 0f;
            while (t < HopTime && !fpc.CannotAct)
            {
                float before = t / HopTime;
                t = Mathf.Min(HopTime, t + Time.deltaTime);
                float after = t / HopTime;
                float rise = HopHeight * 4f * (after * (1f - after) - before * (1f - before));
                controller.Move(dir * s.lunge * (after - before) + Vector3.up * rise);
                yield return null;
            }
            fpc.ResetVertical();
            HitServerRpc(step, transform.position + Vector3.up * 0.3f, dir);
        }

        stage = (step + 1) % Steps.Length;
        busy = false;
    }

    // animace postavy (vidi vsichni) - vlastnik hned, ostatni pres server
    void PlayStep(int step)
    {
        var visual = GetComponent<CharacterVisual>();
        if (visual != null)
        {
            var which = CharacterVisual.AbilityAnim.Combo1 + step;
            if (visual.HasAbilityAnimation(which))
            {
                float length = visual.AbilityClipLength(which);
                visual.PlayAbility(which, 0f, length, Mathf.Min(length, Steps[step].anim));
            }
        }
        // zvuk seku jako u sekyrek (nahravka zbrane), u dopadu i svist vyskoku
        var clip = weapon != null && weapon.fireSound != null ? weapon.fireSound
            : CollectedAudioBank.ForActor(this, CollectedAudioBank.WeaponCue(weapon) + "_weapon.swing", ProceduralSfx.Dash);
        ProceduralSfx.PlayForActor(this, clip, transform.position, 0.6f, 25f);
        if (step == 2) ProceduralSfx.PlayForActor(this, ProceduralSfx.Dash, transform.position, 0.4f);
    }

    [ServerRpc]
    void StepServerRpc(int step) => StepClientRpc(step);

    [ClientRpc]
    void StepClientRpc(int step)
    {
        if (IsOwner) return;
        if (step >= 0 && step < Steps.Length) PlayStep(step);
    }

    // ---------------- server: zasah ----------------

    [ServerRpc]
    void HitServerRpc(int step, Vector3 origin, Vector3 forward)
    {
        if (weapon == null || step < 0 || step >= Steps.Length) return;
        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;
        if (health.currentHealth.Value <= 0f) return;
        if ((origin - transform.position).sqrMagnitude > 9f) origin = transform.position + Vector3.up;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.01f ? forward.normalized : transform.forward;

        var s = Steps[step];
        var hit = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(origin, s.reach + 0.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            var dummy = col.GetComponentInParent<Target>();
            if (dummy != null) dummy.TakeDamage(weapon.damage * s.damage);

            var victim = col.GetComponentInParent<Health>();
            if (victim == null || victim == health || !hit.Add(victim) || victim.currentHealth.Value <= 0f) continue;
            if (Combat.SameTeam(gameObject, victim.gameObject)) continue;

            Vector3 to = col.bounds.center - origin;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            float along = Vector3.Dot(flat, forward);
            float distance;
            if (step == 2)
            {
                distance = flat.magnitude;
                if (distance > s.reach + 0.4f || Mathf.Abs(to.y) > 2.5f) continue;
            }
            else if (s.halfAngle > 0f)
            {
                distance = flat.magnitude;
                if (distance > s.reach + 0.4f || (distance > 0.5f && Vector3.Angle(forward, flat) > s.halfAngle)) continue;
            }
            else
            {
                distance = along;
                float side = (flat - forward * along).magnitude;
                if (along < -0.3f || along > s.reach + 0.4f || side > s.width) continue;
            }
            if (!Combat.HasLineOfSight(origin, col)) continue;

            bool sweet = distance >= s.reach * SweetStart;
            Combat.DamagePlayer(gameObject, victim, weapon.damage * s.damage * (sweet ? SweetBonus : 1f), "weapon:melee");
            if (victim.currentHealth.Value > 0f)
            {
                var controller = victim.GetComponent<FirstPersonController>();
                if (controller != null)
                {
                    if (step == 1) controller.ServerSlow(SlowSeconds, SlowFactor);
                    if (step == 2)
                    {
                        Vector3 away = flat.sqrMagnitude > 0.01f ? flat.normalized : forward;
                        controller.ServerKnockback(Vector3.up * Knockup + away * 2.5f);
                    }
                }
            }
            HitFxClientRpc(col.bounds.center, sweet);
        }
        SwingFxClientRpc(step, origin, forward);
    }

    // ---------------- ukazatel plochy dalsiho uderu (vidi jen sam Pova) ----------------
    // Jako u Aatroxe: slaba vypln, "sladky bod" (posledni ~30 % dosahu, kde rana dava vic) vyrazne, zarivy okraj.
    // Plocha kopiruje teren (kazdy vrchol se polozi na zem). Mimo boj je tlumena, behem komba (a pri drzeni LMB)
    // se rozsviti a jemne pulzuje. Jen u vlastniho hrace (ne u botu), ostatnim se nic nevytvori.

    static readonly Color AreaColor = new Color(1f, 0.5f, 0.12f, 1f);
    static readonly float[] AreaRows = { 0f, 0.3f, 0.6f, 0.69f, 0.71f, 0.86f, 0.955f, 1f };
    GameObject area;
    Mesh areaMesh;
    Material areaMaterial;
    int areaStep = -1;
    Vector2[] areaUV;
    Vector3[] areaVertices;
    float areaAlpha;

    void LateUpdate()
    {
        bool show = IsOwner && fpc != null && fpc.Bot == null && weapon != null && weapon.comboRotation
            && !slamming && health != null && health.currentHealth.Value > 0f && fpc.playerCamera != null;
        bool fighting = show && (busy || comboQueued || Time.time - lastAttack < RotationReset
            || (HeroInput.Held(this, HeroInput.Key.LeftMouse) && HeroInput.Locked(this)));
        float target = !show ? 0f : fighting ? 1f : 0.5f;
        areaAlpha = Mathf.MoveTowards(areaAlpha, target, Time.deltaTime * (show ? 5f : 12f));
        if (areaAlpha <= 0.001f)
        {
            if (area != null && area.activeSelf) area.SetActive(false);
            return;
        }
        if (area == null) BuildArea();
        if (!area.activeSelf) area.SetActive(true);

        int next = stage > 0 && !busy && Time.time - lastAttack > RotationReset ? 0 : stage;
        if (next != areaStep) BuildAreaGrid(next);

        var rotation = Quaternion.LookRotation(LookFlat(), Vector3.up);
        Vector3 feet = transform.position;
        for (int i = 0; i < areaVertices.Length; i++)
            areaVertices[i] = SlashGlow.Ground(feet + rotation * AreaLocal(areaStep, areaUV[i].x, areaUV[i].y), 0.05f);
        areaMesh.vertices = areaVertices;
        areaMesh.RecalculateBounds();

        float pulse = fighting ? 0.82f + 0.18f * Mathf.Sin(Time.time * 8f) : 1f;
        areaMaterial.color = new Color(1f, 1f, 1f, areaAlpha * pulse);
    }

    void BuildArea()
    {
        area = new GameObject("Trojsek_Plocha");
        areaMesh = new Mesh { name = "Trojsek_Plocha" };
        areaMesh.MarkDynamic();
        area.AddComponent<MeshFilter>().sharedMesh = areaMesh;
        var renderer = area.AddComponent<MeshRenderer>();
        areaMaterial = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3100 };
        renderer.sharedMaterial = areaMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // mistni bod tvaru uderu: u = napric (-1..1, u kruhu uhel), v = od hrace ven (0..1)
    static Vector3 AreaLocal(int step, float u, float v)
    {
        var s = Steps[step];
        if (step != 2 && s.halfAngle <= 0f) return new Vector3(u * s.width, 0f, v * s.reach);
        float a = u * (step == 2 ? 180f : s.halfAngle) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (v * s.reach);
    }

    void BuildAreaGrid(int step)
    {
        areaStep = step;
        bool circle = step == 2;
        bool rect = !circle && Steps[step].halfAngle <= 0f;
        var columns = new System.Collections.Generic.List<float>();
        if (circle)
            for (int i = 0; i <= 36; i++) columns.Add(Mathf.Lerp(-1f, 1f, i / 36f));
        else
        {
            int inner = rect ? 3 : 11;
            columns.Add(-1f); columns.Add(-0.94f);
            for (int i = 0; i < inner; i++) columns.Add(Mathf.Lerp(-0.85f, 0.85f, inner == 1 ? 0.5f : i / (float)(inner - 1)));
            columns.Add(0.94f); columns.Add(1f);
        }
        int cols = columns.Count, rows = AreaRows.Length;
        areaUV = new Vector2[cols * rows];
        areaVertices = new Vector3[cols * rows];
        var colors = new Color[cols * rows];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                float u = columns[c], v = AreaRows[r];
                areaUV[r * cols + c] = new Vector2(u, v);
                float a = v < 0.7f ? Mathf.Lerp(0.04f, 0.1f, v / 0.7f) : 0.26f;   // sladky bod vyrazne
                if (v >= 0.999f) a = 0.8f;                                        // vnejsi okraj
                if (!circle && Mathf.Abs(u) >= 0.999f) a = Mathf.Max(a, 0.6f);    // bocni okraje
                if (v <= 0.001f && rect) a = Mathf.Max(a, 0.3f);
                var color = AreaColor;
                color.a = a;
                colors[r * cols + c] = color;
            }
        var triangles = new int[(cols - 1) * (rows - 1) * 6];
        int t = 0;
        for (int r = 0; r < rows - 1; r++)
            for (int c = 0; c < cols - 1; c++)
            {
                int i = r * cols + c;
                triangles[t++] = i; triangles[t++] = i + cols; triangles[t++] = i + 1;
                triangles[t++] = i + 1; triangles[t++] = i + cols; triangles[t++] = i + cols + 1;
            }
        areaMesh.Clear();
        areaMesh.vertices = areaVertices;
        areaMesh.colors = colors;
        areaMesh.triangles = triangles;
    }

    public override void OnDestroy()
    {
        if (area != null) Destroy(area);
        if (areaMesh != null) Destroy(areaMesh);
        if (areaMaterial != null) Destroy(areaMaterial);
        base.OnDestroy();
    }

    // ---------------- vizual uderu (vidi vsichni) ----------------
    //   1. sek: zhavy oblouk cepele shora dolu a ohniva puklina v zemi pres cely obdelnik
    //   2. sek: siroky srpek pres kuzel zleva doprava (zhavy hrot cepele na okraji = sladky bod)
    //   3. uder: razova vlna po zemi a praskliny do stran

    [ClientRpc]
    void SwingFxClientRpc(int step, Vector3 origin, Vector3 forward)
    {
        step = Mathf.Clamp(step, 0, Steps.Length - 1);
        var s = Steps[step];
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 feet = origin - Vector3.up * (step == 2 ? 0.3f : 1f);
        int seed = Random.Range(0, 100000);

        if (step == 2)
        {
            Vector3 center = SlashGlow.Ground(feet, 0.08f);
            SlashGlow.Shockwave(center, s.reach + 0.2f, 0.4f, 0.4f, 0.7f);
            SlashGlow.Shockwave(center, s.reach * 0.6f, 0.25f, 0.2f, 0.35f);
            float turn = Random.Range(0f, 60f);
            for (int i = 0; i < 6; i++)
            {
                Vector3 dir = Quaternion.AngleAxis(turn + i * 60f + Random.Range(-12f, 12f), Vector3.up) * forward;
                SlashGlow.Crack(center + dir * 0.35f, dir, s.reach * Random.Range(0.7f, 1f), 0.09f, 0.12f, 0.75f, seed + i);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = (i / 4f + Random.value * 0.2f) * Mathf.PI * 2f;
                Fx.Sparks(center + new Vector3(Mathf.Cos(a), 0.2f, Mathf.Sin(a)) * s.reach * 0.9f, FireColor);
            }
            ProceduralSfx.Play(ProceduralSfx.Explosion, origin, 0.3f);
            return;
        }

        if (s.halfAngle <= 0f)
        {
            // oblouk cepele ve svisle rovine: zezadu nad hlavou dopredu az k zemi
            Vector3 pivot = feet + Vector3.up * 1.55f + forward * 0.35f;
            SlashGlow.Arc(pivot, Vector3.up, forward, 1.6f, -35f, 150f, 0.32f, new Color(1f, 0.35f, 0.08f, 0.45f), new Color(1f, 0.2f, 0f, 0f), 0.08f, 0.3f);
            SlashGlow.Arc(pivot, Vector3.up, forward, 1.75f, -35f, 150f, 0.07f, SlashGlow.Hot, new Color(1f, 0.4f, 0.1f, 0f), 0.08f, 0.26f);
            // puklina pres cely zasah
            SlashGlow.Crack(feet + forward * 0.9f, forward, s.reach - 0.7f, 0.13f, 0.12f, 0.7f, seed);
            Fx.Sparks(SlashGlow.Ground(feet + forward * s.reach * 0.9f, 0.2f), FireColor);
        }
        else
        {
            // srpek pres kuzel (leva ruka: zleva doprava), lehce sikmo shora dolu
            Vector3 pivot = feet + Vector3.up * 1.05f;
            float from = -s.halfAngle - 12f, to = s.halfAngle + 12f;
            SlashGlow.Arc(pivot, forward, right, s.reach * 0.6f, from, to, s.reach * 0.38f,
                new Color(1f, 0.38f, 0.08f, 0.4f), new Color(1f, 0.2f, 0f, 0f), 0.09f, 0.3f, 0.35f, -0.25f);
            SlashGlow.Arc(pivot, forward, right, s.reach * 0.9f, from, to, 0.08f,
                SlashGlow.Hot, new Color(1f, 0.4f, 0.1f, 0f), 0.09f, 0.28f, 0.35f, -0.25f);
            Fx.Sparks(pivot + (forward * Mathf.Cos(to * Mathf.Deg2Rad) + right * Mathf.Sin(to * Mathf.Deg2Rad)) * s.reach * 0.9f, FireColor);
        }
    }

    [ClientRpc]
    void HitFxClientRpc(Vector3 at, bool sweet)
    {
        Fx.BulletImpact(at, FireColor, sweet ? 1.7f : 1.1f);
        ProceduralSfx.Play(ProceduralSfx.Hit, at, sweet ? 0.9f : 0.6f);
        if (sweet) Fx.Sparks(at + Vector3.up * 0.3f, new Color(1f, 0.85f, 0.3f));
    }
}
