using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// Bot pro trenink a testovani: normalni postava hrace (stejny prefab), kterou na hostu misto klavesnice a mysi ridi
// tenhle "mozek". Pridava ho host v lobby (MatchManager.ServerAddBot). Komponenta existuje jen na hostu - ostatni
// hraci vidi bota jako kohokoli jineho.
// Chodi po mape (navigacni sit), hleda nepratele, miri (s reakcni dobou a nepresnosti), strili, uhyba do stran,
// pri malo zivotech jde pro lekarnicku a pouziva schopnosti hrdiny (virtualni klavesy pres HeroInput).
public class BotBrain : MonoBehaviour
{
    public static readonly List<BotBrain> All = new List<BotBrain>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        All.Clear();
        trails.Clear();
        trailFrame = -1;
        nextTrailSample = 0f;
    }

    public static bool IsBot(GameObject go) => go != null && go.TryGetComponent<BotBrain>(out _);

    // Postava, kterou ovlada hrac na tomhle pocitaci (ne bot, ktery na hostu taky "patri" hostovi).
    public static bool IsLocalHuman(NetworkBehaviour behaviour) => behaviour != null && behaviour.IsOwner && !IsBot(behaviour.gameObject);

    // ---------------- obtiznost (1. etapa: jedna stredni) ----------------

    public float reactionTime = 0.6f;     // s od spatreni do prvni strely
    public float turnSpeed = 180f;         // stupnu za sekundu
    public float aimError = 3f;          // stupnu (nahodna odchylka, meni se kazdou pul sekundu)
    public float viewDistance = 55f;
    public float memorySeconds = 11f;      // jak dlouho si pamatuje, kde naposledy videl nepritele
    public float burstSeconds = 0.7f;      // strili v davkach: tak dlouho strili, pak chvili ne
    public float burstPause = 0.6f;

    // ---------------- vystup pro FirstPersonController a WeaponShooting ----------------

    public Vector2 Move { get; private set; }
    public bool Run { get; private set; }
    public bool FireHeld { get; private set; }
    public bool FirePressedThisFrame => FireHeld && Time.frameCount - firePressedFrame <= 1;
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }

    bool jumpRequest;
    public bool ConsumeJump()
    {
        bool j = jumpRequest;
        jumpRequest = false;
        return j;
    }

    int firePressedFrame = -10;

    void SetFire(bool held)
    {
        if (held && !FireHeld) firePressedFrame = Time.frameCount;
        FireHeld = held;
    }

    // ---------------- stav ----------------

    FirstPersonController fpc;
    WeaponShooting shooting;
    PlayerHero hero;
    PlayerTeam team;
    Health health;

    PlayerHero target;
    bool targetVisible;
    float seenSince = -1f;
    Vector3 targetLastPos, targetVelocity;
    float nextThink;

    readonly List<Vector3> path = new List<Vector3>();
    int corner;
    Vector3 pathGoal;
    float nextRepath;

    Vector2 aimOffset;
    float nextAimJitter;
    float strafeSign = 1f, nextStrafeSwitch;
    float chargeRelease = 0.8f;
    float burstUntil, pauseUntil;
    float lastSeenTime = -100f;
    Vector3 lastSeenPos;
    float lastHealth = -1f;

    Vector3 stuckCheckPos;
    float nextStuckCheck;
    int stuckCount;
    Vector3 wanderGoal;
    float nextWander;

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        shooting = GetComponent<WeaponShooting>();
        hero = GetComponent<PlayerHero>();
        team = GetComponent<PlayerTeam>();
        health = GetComponent<Health>();
        if (fpc != null) fpc.Bot = this;
        if (shooting != null) shooting.Bot = this;
        Yaw = transform.eulerAngles.y;

        // kazdy bot je trochu jiny: agresivita, reakce, presnost
        aggression = Random.Range(0.5f, 1f);
        reactionTime *= Random.Range(0.8f, 1.35f);
        aimError *= Random.Range(0.8f, 1.35f);
        nextHunch = Time.time + Random.Range(8f, 20f);
    }

    // ---------------- "lidske" chovani ----------------

    // 0 = opatrny (drzi pozici, strili z dalky), 1 = dravec (jde po nepritelich)
    float aggression;
    const int MaxChasers = 3;            // za jednim hracem se aktivne zenou nejvys tri boti
    const float HearDistance = 35f;

    // tusena poloha nepritele (slysel strelbu / tuseni) - jde se tam podivat
    Vector3 investigatePos;
    float investigateUntil = -1f;
    float nextHunch;

    // Strelba slysitelna pro boty (vola server pri kazdem vystrelu).
    public static void HearShot(Vector3 position, GameObject shooter)
    {
        foreach (var bot in All)
        {
            if (bot == null || bot.gameObject == shooter || Combat.SameTeam(bot.gameObject, shooter)) continue;
            if (bot.target != null && bot.targetVisible) continue;
            if (Vector3.Distance(bot.transform.position, position) > HearDistance) continue;
            bot.Investigate(position, 6f);
        }
    }

    void Investigate(Vector3 position, float seconds)
    {
        Vector2 jitter = Random.insideUnitCircle * 4f;
        investigatePos = position + new Vector3(jitter.x, 0f, jitter.y);
        investigateUntil = Time.time + seconds;
    }

    // Kolik jinych botu se ted aktivne zene za timhle hracem.
    static int ChasersOf(PlayerHero victim, BotBrain except)
    {
        int count = 0;
        foreach (var bot in All)
            if (bot != null && bot != except && bot.target == victim && bot.chasing) count++;
        return count;
    }

    bool chasing;

    // Obcas "tuseni", kde je nepritel (jako hrac, ktery ví, odkud prisli): jde do jeho oblasti, ne primo na nej.
    void Hunch()
    {
        if (Time.time < nextHunch) return;
        nextHunch = Time.time + Mathf.Lerp(28f, 12f, aggression) * Random.Range(0.7f, 1.3f);
        if (target != null || Time.time < investigateUntil || Random.value > 0.35f + aggression * 0.5f) return;

        PlayerHero pick = null;
        float best = float.MaxValue;
        foreach (var other in FindObjectsByType<PlayerHero>())
        {
            if (other == hero || !other.IsSpawned || other.IsJoining || other.IsUntargetable || Combat.SameTeam(gameObject, other.gameObject)) continue;
            float d = Vector3.Distance(transform.position, other.transform.position) * Random.Range(0.7f, 1.3f);
            if (d < best) { best = d; pick = other; }
        }
        if (pick == null) return;
        Vector2 jitter = Random.insideUnitCircle * 10f;
        investigatePos = pick.transform.position + new Vector3(jitter.x, 0f, jitter.y);
        investigateUntil = Time.time + 12f;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    // ---------------- schopnosti (virtualni klavesy) ----------------

    readonly float[] pressAt = { -10f, -10f, -10f, -10f, -10f, -10f };
    readonly float[] holdUntil = new float[6];
    float nextAbilityThink;

    // Schopnost se pta, jestli bot "zmackl" klavesu (stisk plati chvili, dokud si ho schopnost nevezme).
    public bool ConsumePress(HeroInput.Key key)
    {
        int i = (int)key;
        if (Time.time - pressAt[i] > 0.25f) return false;
        pressAt[i] = -10f;
        return true;
    }

    public bool IsHeld(HeroInput.Key key) => Time.time < holdUntil[(int)key];

    void Press(HeroInput.Key key, float hold = 0.12f)
    {
        pressAt[(int)key] = Time.time;
        holdUntil[(int)key] = Time.time + hold;
    }

    static float Reach(AbilityDefinition ability, float fallback) =>
        ability != null && ability.range > 0.5f && ability.range < 60f ? ability.range : fallback;


    // ---------------- role: healer / strelec na dalku / ostatni ----------------

    enum Role { Fighter, Ranged, Support }

    // Healer: zbran leci spoluhrace (Anna) nebo hrdina s aurou (Bard). Na dalku: odstrel, luk, puska s priblizenim.
    Role CurrentRole(WeaponDefinition weapon)
    {
        var def = hero != null ? hero.Hero : null;
        if (def == null || weapon == null) return Role.Fighter;
        if (weapon.allyHeal > 0f || def.secondaryAbilityKind == AbilityKind.Crossfade) return Role.Support;
        if (!weapon.IsMelee && (weapon.HasScopedShot || weapon.IsCharged || weapon.scopeFov > 0f)) return Role.Ranged;
        return Role.Fighter;
    }

    static void RangedBand(WeaponDefinition weapon, out float min, out float max)
    {
        min = Mathf.Clamp(weapon.falloffStart > 0f ? weapon.falloffStart : 15f, 12f, 25f);
        max = min + 20f;
    }

    // spoluhraci (3x za sekundu): kdo potrebuje leceni nejvic (zraneni + blizkost)
    readonly List<PlayerHero> allies = new List<PlayerHero>();
    PlayerHero healTarget;
    bool healVisible;

    static bool IsSupportHero(PlayerHero p)
    {
        var def = p != null ? p.Hero : null;
        return def != null && ((def.weapon != null && def.weapon.allyHeal > 0f) || def.secondaryAbilityKind == AbilityKind.Crossfade);
    }

    // Je s kym hrat jako healer: nekdo zraneny, nebo spoluhrac, ktery neni healer.
    bool HasSupportAnchor()
    {
        if (healTarget != null) return true;
        foreach (var ally in allies)
            if (!IsSupportHero(ally)) return true;
        return false;
    }

    static float HealthFraction(PlayerHero p)
    {
        var h = p != null ? p.GetComponent<Health>() : null;
        return h != null && h.maxHealth > 0f ? h.currentHealth.Value / h.maxHealth : 1f;
    }

    void ScanAllies(Vector3 eye)
    {
        allies.Clear();
        healTarget = null;
        float best = float.MaxValue;
        foreach (var other in FindObjectsByType<PlayerHero>())
        {
            if (other == hero || !other.IsSpawned || other.IsJoining || !Combat.SameTeam(gameObject, other.gameObject)) continue;
            var otherHealth = other.GetComponent<Health>();
            if (otherHealth == null || otherHealth.currentHealth.Value <= 0f) continue;
            allies.Add(other);
            float fraction = HealthFraction(other);
            float distance = Vector3.Distance(transform.position, other.transform.position);
            if (fraction >= 0.92f || distance > 45f) continue;
            float score = fraction + distance / 120f;
            if (score < best)
            {
                best = score;
                healTarget = other;
            }
        }
        healVisible = healTarget != null && CanSee(eye, healTarget);
    }

    // Healer: drzi se kousek za spoluhracem, ktery leceni potrebuje (jinak za tim, kdo je nejbliz boji / bodu).
    Vector3 supportSpot;
    Vector3 supportAnchorPos;
    float nextSupportSpot;

    Vector3 SupportSpot(MatchManager match, bool aura)
    {
        PlayerHero anchor = healTarget;
        if (anchor == null)
        {
            Vector3 front = target != null ? (targetVisible ? target.transform.position : lastSeenPos)
                : match != null && match.IsAttackMode ? match.pointPosition.Value : transform.position;
            float best = float.MaxValue;
            foreach (var ally in allies)
            {
                if (IsSupportHero(ally)) continue;   // healeri by se drzeli jeden druheho a stali by
                float score = Vector3.Distance(ally.transform.position, front) + Vector3.Distance(ally.transform.position, transform.position) * 0.5f;
                if (score < best) { best = score; anchor = ally; }
            }
        }
        if (anchor == null) return transform.position;

        Vector3 anchorPos = anchor.transform.position;
        if (Time.time >= nextSupportSpot || Vector3.Distance(anchorPos, supportAnchorPos) > 3f)
        {
            nextSupportSpot = Time.time + Random.Range(1f, 2f);
            supportAnchorPos = anchorPos;
            Vector3 threat = target != null ? (targetVisible ? target.transform.position : lastSeenPos) : anchorPos + anchor.transform.forward * 10f;
            Vector3 away = anchorPos - threat;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : -anchor.transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, away) * Random.Range(-2f, 2f);
            Vector3 candidate = anchorPos + away * (aura ? 2.5f : 5f) + side;
            supportSpot = NavMesh.SamplePosition(candidate, out var hit, 2.5f, BotNavigation.Filter) ? hit.position : anchorPos;
        }
        return supportSpot;
    }

    // Strelec na dalku: misto s vyhledem na cil (nepritel / bod), v pasmu dosahu zbrane, radeji vys,
    // nedaleko od bota a ne na stejnem miste jako jini boti.
    class Spot { public Vector3 position, focus; public float next, arrivedAt = -1f, hold; public bool valid; }
    readonly Spot combatSpot = new Spot();
    readonly Spot watchSpot = new Spot();

    Vector3 FiringSpot(Spot cache, Vector3 focus, float minR, float maxR)
    {
        // na miste chvili vyckat; kdyz odtud nikoho nevidi, jit vykouknout jinam (ne kempit porad na jednom miste)
        bool atSpot = cache.valid && Vector3.Distance(transform.position, cache.position) < 1.8f;
        if (atSpot && cache.arrivedAt < 0f)
        {
            cache.arrivedAt = Time.time;
            cache.hold = Random.Range(3.5f, 7f);
        }
        bool stale = atSpot && !targetVisible && Time.time - cache.arrivedAt > cache.hold;
        lookFocus = focus;
        lookFocusUntil = Time.time + 0.5f;
        if (!stale && cache.valid && Time.time < cache.next && Vector3.Distance(focus, cache.focus) < 6f) return cache.position;
        Vector3 previous = cache.valid ? cache.position : Vector3.one * 1e6f;
        cache.arrivedAt = -1f;
        cache.next = Time.time + Random.Range(4f, 7f);
        cache.focus = focus;

        Vector3 focusEye = focus + Vector3.up * 1.2f;
        Vector3 ourSide = transform.position - focus;
        ourSide.y = 0f;
        float baseAngle = ourSide.sqrMagnitude > 0.01f ? Mathf.Atan2(ourSide.x, ourSide.z) * Mathf.Rad2Deg : Random.Range(0f, 360f);
        float mid = (minR + maxR) * 0.5f;

        bool found = false;
        float bestScore = float.MinValue;
        Vector3 best = transform.position;
        for (int i = 0; i < 14; i++)
        {
            Vector3 candidate;
            if (i == 0)
            {
                if (stale) continue;
                candidate = transform.position;   // zustat, kde je (kdyz je to dobre misto)
            }
            else
            {
                float angle = (baseAngle + Random.Range(-85f, 85f)) * Mathf.Deg2Rad;
                candidate = focus + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * Random.Range(minR, maxR);
            }
            if (!NavMesh.SamplePosition(candidate, out var hit, 3f, BotNavigation.Filter)) continue;
            Vector3 p = hit.position;
            float d = Vector3.Distance(p, focus);
            if (d < minR * 0.7f || d > maxR * 1.25f) continue;
            if (Physics.Linecast(p + Vector3.up * 1.6f, focusEye, out RaycastHit block, ~0, QueryTriggerInteraction.Ignore)
                && block.collider.GetComponentInParent<PlayerHero>() == null) continue;   // bez vyhledu

            float score = (p.y - focus.y) * 1.5f - Vector3.Distance(p, transform.position) * 0.35f - Mathf.Abs(d - mid) * 0.1f;
            foreach (var bot in All)
                if (bot != this && bot.combatSpot.valid && Vector3.Distance(p, bot.combatSpot.position) < 4f) score -= 5f;
            if (Vector3.Distance(p, previous) < 6f) score -= stale ? 25f : 0f;   // vykouknout odjinud
            if (score > bestScore)
            {
                bestScore = score;
                best = p;
                found = true;
            }
        }
        cache.valid = found;
        cache.position = found ? best : focus;
        if (!found) cache.next = Time.time + 1.5f;   // nic s vyhledem: jit bliz a brzy zkusit znovu
        return cache.position;
    }

    // Kam se divat, kdyz strelec stoji na miste a ceka (oblast nepritele / bod), s pomalym rozhlizenim.
    Vector3 lookFocus;
    float lookFocusUntil;

    // Schopnost mirena na spoluhrace (Annino posileni, granat): chvili na nej mirit, pak zmacknout.
    PlayerHero aimAlly;
    HeroInput.Key aimAllyKey;
    float aimAllyUntil;

    void AimAllyAndPress(PlayerHero ally, HeroInput.Key key)
    {
        aimAlly = ally;
        aimAllyKey = key;
        aimAllyUntil = Time.time + 1f;
    }

    // Healer: leceni a podpora maji prednost pred utokem.
    void SupportAbilities(HeroDefinition def, float hp, bool combat, Vector3 eye)
    {
        if (def.secondaryAbilityKind == AbilityKind.Crossfade)
        {
            var bard = GetComponent<BardAbility>();
            if (bard == null) return;
            float radius = def.secondaryAbility != null && def.secondaryAbility.radius > 0.5f ? def.secondaryAbility.radius : 10f;
            int inAura = 0, hurt = 0;
            float lowest = hp, sum = hp;
            foreach (var ally in allies)
            {
                if (Vector3.Distance(ally.transform.position, transform.position) > radius) continue;
                float f = HealthFraction(ally);
                inAura++;
                sum += f;
                if (f < 0.9f) hurt++;
                lowest = Mathf.Min(lowest, f);
            }
            if (hp < 0.9f) hurt++;

            // rytmus: leceni, kdyz je nekdo zraneny nebo se bojuje; jinak zrychleni (presun)
            bool wantSpeed = hurt == 0 && !combat && healTarget == null;
            if (bard.SpeedMode != wantSpeed) Press(HeroInput.Key.Shift);
            else if (!wantSpeed && (hurt >= 2 || lowest < 0.45f)) Press(HeroInput.Key.E);   // zesileni leceni

            // koncert (stit): vic spoluhracu v boji a zranenych
            if (hero.UltReady && combat && inAura >= 1 && sum / (inAura + 1) < 0.65f)
                Press(HeroInput.Key.Q, 0.15f);

            // basovy uder: nepritel tesne u nej
            if (combat && target != null && def.rmbAbility != null && Vector3.Distance(transform.position, target.transform.position) < Reach(def.rmbAbility, 8f) * 0.8f)
                Press(HeroInput.Key.RightMouse);
            return;
        }

        // Anna
        if (def.abilityKind == AbilityKind.NanoBoost && def.ability != null && hero.UltReady && aimAlly == null)
        {
            // posileni: na spoluhrace, ktery bojuje (nebo je zraneny), s vyhledem a v dosahu
            PlayerHero pick = null;
            foreach (var ally in allies)
            {
                float d = Vector3.Distance(ally.transform.position, transform.position);
                if (d > Mathf.Max(8f, def.ability.range * 0.9f) || !CanSee(eye, ally)) continue;
                if (combat || HealthFraction(ally) < 0.6f) { pick = ally; break; }
            }
            if (pick != null)
            {
                AimAllyAndPress(pick, HeroInput.Key.Q);
                return;
            }
        }

        if (def.altAbilityKind == AbilityKind.BioticGrenade && def.altAbility != null && aimAlly == null)
        {
            float reach = Reach(def.altAbility, 20f);
            // granat na zraneneho spoluhrace (leci i okoli)
            if (healTarget != null && healVisible && HealthFraction(healTarget) < 0.6f
                && Vector3.Distance(healTarget.transform.position, transform.position) < reach)
            {
                AimAllyAndPress(healTarget, HeroInput.Key.E);
                return;
            }
            // jinak na nepritele, kdyz na nej miri
            if (combat && target != null && Vector3.Distance(target.transform.position, transform.position) < reach && Random.value < 0.35f)
            {
                Vector3 forward = Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward;
                if (Vector3.Angle(forward, target.transform.position + Vector3.up * 1.2f - eye) < 12f) Press(HeroInput.Key.E);
            }
        }

        // uspavaci sipka: obrana, kdyz je nepritel blizko
        if (def.secondaryAbilityKind == AbilityKind.SleepDart && combat && target != null
            && Vector3.Distance(target.transform.position, transform.position) < 12f)
        {
            Vector3 forward = Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward;
            if (Vector3.Angle(forward, target.transform.position + Vector3.up * 1.2f - eye) < 8f) Press(HeroInput.Key.Shift);
        }
    }

    // Anna: strelba do spoluhrace (leci ho).
    bool healing;
    bool healPhase;
    float nextHealSwitch;

    void HealShoot(Vector3 eye)
    {
        Vector3 forward = Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward;
        bool aligned = Vector3.Angle(forward, healTarget.transform.position + Vector3.up * 1.2f - eye) < 3f + aimError * 0.5f;
        SetFire(aligned && !FireHeld);   // po jedne rane
    }

    // Kdy co zmacknout: ultimatku na nepritele v dosahu, E a PTM v boji na zamereneho nepritele, Shift pri pribliazeni.
    // (Kdyz je schopnost na cooldownu, stisk prosté nic neudela.)
    void UseAbilities(WeaponDefinition weapon, bool combat, bool chase)
    {
        if (Time.time < nextAbilityThink || hero == null || hero.Hero == null) return;
        nextAbilityThink = Time.time + Random.Range(0.35f, 0.75f);
        var def = hero.Hero;
        float hp = health != null && health.maxHealth > 0f ? health.currentHealth.Value / health.maxHealth : 1f;

        if (CurrentRole(weapon) == Role.Support)
        {
            Vector3 supportEye = fpc.playerCamera != null ? fpc.playerCamera.transform.position : transform.position + Vector3.up * 1.6f;
            SupportAbilities(def, hp, combat, supportEye);
            return;
        }

        // leceni (Viktorovo pole) i bez boje, kdyz je zraneny
        if (def.altAbility != null && def.altAbilityKind == AbilityKind.HealField && hp < 0.6f)
            Press(HeroInput.Key.E);

        if (!combat || target == null) return;
        Vector3 eye = fpc.playerCamera != null ? fpc.playerCamera.transform.position : transform.position + Vector3.up * 1.6f;
        Vector3 toTarget = target.transform.position + Vector3.up * 1.2f - eye;
        float distance = toTarget.magnitude;
        Vector3 forward = Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward;
        bool aligned = Vector3.Angle(forward, toTarget) < 15f;
        if (!aligned || Time.time - seenSince < reactionTime) return;

        // ultimatka: nepritel v dosahu, bot neni skoro mrtvy
        if (def.ability != null && hero.UltReady && distance < Reach(def.ability, 20f) && hp > 0.25f && Random.value < 0.5f + aggression * 0.3f)
        {
            Press(HeroInput.Key.Q, 0.15f);
            return;
        }

        // E (utocna)
        if (def.altAbility != null && def.altAbilityKind != AbilityKind.HealField && distance < Reach(def.altAbility, 18f) && Random.value < 0.45f)
            Press(HeroInput.Key.E);

        // PTM (u Maxova rezu drzet, at se vlna nabije podle vzdalenosti)
        if (def.rmbAbility != null && distance < Reach(def.rmbAbility, 15f) && Random.value < 0.4f)
            Press(HeroInput.Key.RightMouse, def.rmbAbilityKind == AbilityKind.MaxSlash ? Mathf.Clamp(distance / 10f, 0.25f, 1f) : 0.15f);

        // Shift: priblizit se k nepriteli (vypady, skoky)
        if (def.secondaryAbility != null && chase && distance > PreferredRange(weapon) + 4f && Random.value < 0.45f)
            Press(HeroInput.Key.Shift);
    }

    // ---------------- rozhodovani ----------------

    void Update()
    {
        if (fpc == null || !fpc.IsSpawned || !fpc.IsServer) return;

        var match = MatchManager.Instance;
        bool active = match != null && !match.IsLobby && !match.IsOver && !fpc.CannotAct;
        if (!active)
        {
            Move = Vector2.zero;
            Run = false;
            SetFire(false);
            return;
        }

        if (Time.time >= nextThink)
        {
            nextThink = Time.time + 0.2f;
            var thinkWeapon = hero != null && hero.Hero != null ? hero.Hero.weapon : null;
            viewDistance = thinkWeapon != null && CurrentRole(thinkWeapon) == Role.Ranged ? 95f : 55f;
            PickTarget();
            ScanAllies(fpc.playerCamera != null ? fpc.playerCamera.transform.position : transform.position + Vector3.up * 1.6f);
        }
        TrackTarget();

        var weapon = hero != null && hero.Hero != null ? hero.Hero.weapon : null;
        Vector3 feet = transform.position;

        // kam jit
        Vector3 goal;
        bool combat = target != null && targetVisible;
        HealthGoal(out bool wantsHealth, out Vector3 healthPos);

        // Utok a obrana: hlavni je bod. Bot strili na kazdeho, koho vidi, ale za nepritelem daleko od bodu
        // nebezi (jen do 'leash' metru od bodu); kdyz je na bodu potreba (zabirani / obrana), drzi se na nem.
        bool hasObjective = Objective(match, out Vector3 objective, out float leash, out bool holdPoint);
        bool chase = target != null;
        if (hasObjective && target != null && Vector3.Distance(target.transform.position, match.pointPosition.Value) > leash)
            chase = false;
        if (holdPoint)
            chase = false;
        // nezenou se vsichni: kdyz uz jdou dva jini, opatrny bot zustane a strili, kdyz vidi
        bool melee = weapon != null && weapon.IsMelee;   // na blizko musi dojit, jinak nic neudela
        if (chase && !melee && !chasing && ChasersOf(target, this) >= MaxChasers && aggression < 0.85f)
            chase = false;
        // opatrny bot za nepritelem, ktereho uz nevidi, nejde (jen dravec ho hleda)
        if (chase && !targetVisible && aggression < 0.6f && !melee)
            chase = false;
        Role role = CurrentRole(weapon);
        bool supporting = role == Role.Support && HasSupportAnchor();
        // healer se za nepratelem nezene - drzi se u svych (strili, kdyz vidi)
        // healer se drzi u svych; za nepritelem jde jen kousek (do 18 m), kdyz ho vidi a nikdo nepotrebuje leceni
        if (supporting)
            chase = chase && healTarget == null && targetVisible && Vector3.Distance(transform.position, target.transform.position) < 18f;
        chasing = chase;
        chaseTarget = chase ? target : null;
        RecordTrails();
        Hunch();
        float minR = 0f, maxR = 0f;
        if (role == Role.Ranged) RangedBand(weapon, out minR, out maxR);

        if (wantsHealth)
            goal = healthPos;
        else if (supporting)
            goal = SupportSpot(match, role == Role.Support && weapon.allyHeal <= 0f);
        else if (chase && role == Role.Ranged)
            goal = FiringSpot(combatSpot, targetVisible ? target.transform.position : lastSeenPos, minR, maxR);
        else if (chase)
            goal = targetVisible ? target.transform.position : lastSeenPos;
        else if (hasObjective)
            goal = objective;
        else if (combat)
            goal = transform.position;              // drzi pozici a strili (opatrny / uz jdou jini)
        else if (Time.time < investigateUntil)
        {
            // slysel strelbu / tusi, kde nepritel je (strelec na dalku si najde misto s vyhledem tam)
            goal = role == Role.Ranged ? FiringSpot(watchSpot, investigatePos, minR, maxR) : investigatePos;
            if (Vector3.Distance(transform.position, investigatePos) < 3f) investigateUntil = -1f;
        }
        else
            goal = IdleGoal(match);

        if (Time.time >= nextStrafeSwitch)
        {
            nextStrafeSwitch = Time.time + Random.Range(0.8f, 2f);
            strafeSign = Random.value < 0.5f ? -1f : 1f;
        }

        // zaseknuty: chvili objizdka
        if (Time.time < detourUntil)
            goal = detourGoal;

        Vector3 moveDir = Vector3.zero;
        if (combat && chase && role == Role.Ranged && !wantsHealth && Time.time >= detourUntil)
        {
            // na dalku: dojit na misto s vyhledem a odtud strilet, jen lehce uhybat; kdyz je nepritel moc blizko, couvat
            float distance = Vector3.Distance(feet, target.transform.position);
            Vector3 toTarget = target.transform.position - feet;
            toTarget.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, toTarget.normalized);
            Vector3 toSpot = goal - feet;
            toSpot.y = 0f;
            bool atSpot = toSpot.magnitude <= 1.2f;
            Vector3 approach = atSpot ? Vector3.zero : FollowPath(goal);
            if (distance < minR * 0.5f) approach = -toTarget.normalized;
            moveDir = approach + side * strafeSign * (atSpot ? 0.45f : 0.15f);
        }
        else if (combat && chase && !wantsHealth && weapon != null && Time.time >= detourUntil)
        {
            // v boji: drzet vzdalenost podle zbrane a uhybat do stran
            float distance = Vector3.Distance(feet, target.transform.position);
            float preferred = PreferredRange(weapon);
            Vector3 toTarget = target.transform.position - feet;
            toTarget.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, toTarget.normalized);
            Vector3 approach = distance > preferred + 2f ? FollowPath(goal) : distance < preferred - 3f ? -toTarget.normalized : Vector3.zero;
            moveDir = approach + side * strafeSign * (weapon.IsMelee ? 0.3f : 0.8f);
        }
        else if (combat && weapon != null)
        {
            // strili, ale jde (nebo stoji) na bodu: k cili cesty a uhyby do stran
            Vector3 toTarget = target.transform.position - feet;
            toTarget.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, toTarget.normalized);
            Vector3 toGoal = goal - feet;
            toGoal.y = 0f;
            Vector3 path = toGoal.magnitude > 1.5f ? FollowPath(goal) : Vector3.zero;
            moveDir = path + side * strafeSign * 0.6f;
        }
        else
        {
            moveDir = FollowPath(goal);
        }

        moveDir.y = 0f;
        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

        // natoceni: na cil, jinak ve smeru chuze
        Vector3 eye = fpc.playerCamera != null ? fpc.playerCamera.transform.position : feet + Vector3.up * 1.6f;
        if (aimAlly != null && (Time.time > aimAllyUntil || !aimAlly.IsSpawned)) aimAlly = null;
        // Anna leci strelbou: zraneny spoluhrac na dohled ma prednost (pokud ji nepritel neni tesne u tela)
        // Anna: leci i strili. Hodne zraneny spoluhrac (pod 60 %) ma prednost; jinak v boji strida leceni a strelbu
        // (cim je spoluhrac zranenejsi, tim dele leci). Bez boje jen leci. Nepritel tesne u ni = brani se.
        bool canHeal = role == Role.Support && weapon != null && weapon.allyHeal > 0f && healTarget != null && healVisible
            && Vector3.Distance(feet, healTarget.transform.position) < 40f;
        if (canHeal && combat && Time.time >= nextHealSwitch)
        {
            float need = Mathf.InverseLerp(0.92f, 0.6f, HealthFraction(healTarget));   // 0 = skoro zdravy, 1 = hodne zraneny
            healPhase = !healPhase;
            nextHealSwitch = Time.time + (healPhase ? Mathf.Lerp(0.8f, 2.2f, need) : Mathf.Lerp(2f, 0.8f, need));
        }
        healing = canHeal && (!combat || HealthFraction(healTarget) < 0.6f || healPhase)
            && !(combat && Vector3.Distance(feet, target.transform.position) < 6f);
        Vector3 lookAt;
        if (aimAlly != null)
        {
            lookAt = aimAlly.transform.position + Vector3.up * 1.1f;
            TurnTowards(lookAt - eye, true, true);
            Vector3 forward = Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward;
            if (Vector3.Angle(forward, lookAt - eye) < 4f)
            {
                Press(aimAllyKey, 0.15f);
                aimAlly = null;
            }
        }
        else if (healing)
        {
            lookAt = healTarget.transform.position + Vector3.up * 1.2f;
            TurnTowards(lookAt - eye, true);
        }
        else
        {
            if (combat)
                lookAt = AimPoint(weapon, eye);
            else if (Time.time < lookFocusUntil && (moveDir.sqrMagnitude < 0.3f || Vector3.Distance(feet, lookFocus) < 45f))
            {
                // hlida oblast: diva se tam a pomalu se rozhlizi do stran
                Vector3 toFocus = lookFocus + Vector3.up * 1.2f - eye;
                lookAt = eye + Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.7f + aggression * 5f) * 28f, 0f) * toFocus;
            }
            else
                lookAt = moveDir.sqrMagnitude > 0.01f ? eye + moveDir * 10f : eye + transform.forward * 10f;
            TurnTowards(lookAt - eye, combat);
        }

        // pohyb v mistnich osach tela
        Vector3 local = Quaternion.Euler(0f, -Yaw, 0f) * moveDir;
        Move = new Vector2(local.x, local.z);
        Run = !combat && moveDir.sqrMagnitude > 0.25f;

        CheckStuck(moveDir);
        CheckLoiter(combat || holdPoint || wantsHealth || supporting);
        if (aimAlly != null) SetFire(false);
        else if (healing) HealShoot(eye);
        else Shoot(weapon, eye, combat);
        UseAbilities(weapon, combat, chase);
    }

    void PickTarget()
    {
        // kdyz ho neco zasahne, rozhlizi se i za sebe
        float hp = health != null ? health.currentHealth.Value : 0f;
        bool hurt = lastHealth >= 0f && hp < lastHealth - 0.5f;
        lastHealth = hp;

        PlayerHero best = null;
        bool bestVisible = false;
        float bestScore = float.MaxValue;
        Vector3 eye = fpc.playerCamera != null ? fpc.playerCamera.transform.position : transform.position + Vector3.up * 1.6f;

        foreach (var other in FindObjectsByType<PlayerHero>())
        {
            if (other == hero || !other.IsSpawned || other.IsJoining || other.IsUntargetable) continue;
            var otherHealth = other.GetComponent<Health>();
            if (otherHealth == null || otherHealth.currentHealth.Value <= 0f) continue;
            if (Combat.SameTeam(gameObject, other.gameObject)) continue;

            float distance = Vector3.Distance(transform.position, other.transform.position);
            bool visible = distance <= viewDistance && CanSee(eye, other);
            // bot vidi jen pred sebe (zorne pole ~140 st.) a nevi, kde jsou nepratele, ktere nevidi
            Vector3 toOther = other.transform.position - transform.position;
            toOther.y = 0f;
            if (visible && !hurt && Vector3.Angle(Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward, toOther) > 70f && distance > 6f)
                visible = false;
            if (!visible && other != target) continue;
            float score = distance + (visible ? 0f : 1000f);
            if (score < bestScore)
            {
                bestScore = score;
                best = other;
                bestVisible = visible;
            }
        }

        if (best != target)
        {
            target = best;
            seenSince = -1f;
            targetVisible = false;   // novy cil: reakcni doba znovu od spatreni
            targetVelocity = Vector3.zero;
            if (target != null) targetLastPos = target.transform.position;
        }
        if (bestVisible && !targetVisible) seenSince = Time.time;
        if (!bestVisible) seenSince = -1f;
        targetVisible = bestVisible;
        if (bestVisible)
        {
            lastSeenTime = Time.time;
            lastSeenPos = target.transform.position;
        }
        else if (target != null && Time.time - lastSeenTime > memorySeconds)
        {
            target = null;   // zapomnel
        }
    }

    bool CanSee(Vector3 eye, PlayerHero other)
    {
        Vector3 chest = other.transform.position + Vector3.up * 1.3f;
        if (!Physics.Linecast(eye, chest, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)) return true;
        var owner = hit.collider.GetComponentInParent<PlayerHero>();
        return owner == other;
    }

    void TrackTarget()
    {
        if (target == null) return;
        Vector3 now = target.transform.position;
        if (Time.deltaTime > 0f)
            targetVelocity = Vector3.Lerp(targetVelocity, (now - targetLastPos) / Time.deltaTime, 0.2f);
        targetLastPos = now;
    }

    static float PreferredRange(WeaponDefinition weapon)
    {
        if (weapon.IsMelee) return 1.6f;
        if (weapon.falloffStart > 0f) return Mathf.Clamp(weapon.falloffStart * 0.8f, 6f, 25f);
        return 15f;
    }

    static float EffectiveRange(WeaponDefinition weapon)
    {
        if (weapon.IsMelee) return 2.6f;
        if (weapon.maxDamageRange > 0f) return weapon.maxDamageRange * 0.9f;
        return Mathf.Min(weapon.range, 80f);
    }

    // Kam mirit: hrudnik cile, u projektilu s predstihem podle jeho pohybu, plus nahodna odchylka.
    Vector3 AimPoint(WeaponDefinition weapon, Vector3 eye)
    {
        Vector3 point = target.transform.position + Vector3.up * 1.25f;
        if (weapon != null && weapon.IsProjectile && weapon.projectileSpeed > 1f)
        {
            float flight = Vector3.Distance(eye, point) / weapon.projectileSpeed;
            point += new Vector3(targetVelocity.x, 0f, targetVelocity.z) * flight;
            point += Vector3.up * (0.5f * weapon.projectileGravity * flight * flight);   // padajici strela: mirit vys
        }
        return point;
    }

    void TurnTowards(Vector3 direction, bool combat, bool precise = false)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        if (combat && Time.time >= nextAimJitter)
        {
            nextAimJitter = Time.time + 0.5f;
            aimOffset = Random.insideUnitCircle * aimError * (scoping ? 0.3f : 1f);
        }
        Vector2 jitter = combat && !precise ? aimOffset : Vector2.zero;
        float wantYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + jitter.x;
        float flat = new Vector2(direction.x, direction.z).magnitude;
        float wantPitch = combat ? -Mathf.Atan2(direction.y, flat) * Mathf.Rad2Deg + jitter.y : 0f;
        Yaw = Mathf.MoveTowardsAngle(Yaw, wantYaw, turnSpeed * Time.deltaTime);
        Pitch = Mathf.Clamp(Mathf.MoveTowardsAngle(Pitch, wantPitch, turnSpeed * Time.deltaTime), -80f, 80f);
    }

    bool scoping;

    void Shoot(WeaponDefinition weapon, Vector3 eye, bool combat)
    {
        if (!combat || weapon == null || seenSince < 0f || Time.time - seenSince < reactionTime)
        {
            SetFire(false);
            scoping = false;
            return;
        }

        float distance = Vector3.Distance(eye, target.transform.position + Vector3.up * 1.25f);
        Vector3 forward = Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward;
        Vector3 toAim = AimPoint(weapon, eye) - eye;
        bool aligned = Vector3.Angle(forward, toAim) < (weapon.IsMelee ? 25f : 5f + aimError);
        bool inRange = distance <= EffectiveRange(weapon);

        // sniper na dalku: drzet pribliseni (PTM), nechat ranu nabit a vystrelit
        scoping = weapon.HasScopedShot && distance > 14f && distance < 95f;
        if (scoping)
        {
            holdUntil[(int)HeroInput.Key.RightMouse] = Time.time + 0.3f;
            if (FireHeld)
            {
                SetFire(false);
                return;
            }
            if (aligned && shooting.ChargeFraction >= chargeRelease)
            {
                SetFire(true);
                chargeRelease = Random.Range(0.7f, 1f);
            }
            return;
        }

        if (weapon.IsCharged)
        {
            // luk: natahovat a pustit pri nahodne mire natazeni
            if (FireHeld && shooting.ChargeFraction >= chargeRelease)
            {
                SetFire(false);
                chargeRelease = Random.Range(0.6f, 1f);
                return;
            }
            SetFire(aligned && inRange);
            return;
        }

        // davky: chvili strili, chvili ne
        bool want = aligned && inRange;
        if (want && Time.time >= pauseUntil && Time.time >= burstUntil && !FireHeld)
            burstUntil = Time.time + burstSeconds * Random.Range(0.6f, 1.4f);
        if (FireHeld && Time.time >= burstUntil)
        {
            pauseUntil = Time.time + burstPause * Random.Range(0.6f, 1.6f);
            SetFire(false);
            return;
        }
        SetFire(want && Time.time >= pauseUntil);
    }

    // ---------------- cile cesty ----------------

    void HealthGoal(out bool wants, out Vector3 position)
    {
        wants = false;
        position = Vector3.zero;
        if (health == null || health.currentHealth.Value > health.maxHealth * 0.4f) return;
        var match = MatchManager.Instance;
        if (match == null) return;

        float best = 45f;
        var spots = PickupSpot.Sorted;
        for (int i = 0; i < spots.Count; i++)
        {
            var spot = spots[i];
            if (spot == null || !match.PickupAvailable(i)) continue;
            if (spot.kind != PickupKind.Health && spot.kind != PickupKind.BigHealth && spot.kind != PickupKind.Tramal) continue;
            float d = Vector3.Distance(transform.position, spot.transform.position);
            if (d < best)
            {
                best = d;
                position = spot.transform.position;
                wants = true;
            }
        }
    }

    // ---------------- utok a obrana ----------------

    Vector3 objectiveSpot;
    float nextObjectiveSpot;
    bool objectiveSpotOnPoint;

    // Cil podle role. Utocnik: na bod (zabirat), na nem zustat. Obrance: stat kolem bodu; kdyz jsou utocnici
    // na bodu, vlezt na nej (bez obrance na bodu se zabira). 'leash' = jak daleko od bodu jeste pronasleduje.
    bool Objective(MatchManager match, out Vector3 goal, out float leash, out bool holdPoint)
    {
        goal = Vector3.zero;
        leash = 0f;
        holdPoint = false;
        if (match == null || !match.IsAttackMode || team == null) return false;

        bool attacker = team.teamId.Value == match.attackTeam.Value;
        Vector3 point = match.pointPosition.Value;

        // priprava: utocnici cekaji ve spawnu (bod je zamceny), obranci se rozestavuji kolem bodu
        if (attacker && match.roundPhase.Value == MatchManager.RoundSetup && SpawnZone.TryGetCenter(team.teamId.Value, out Vector3 spawn))
        {
            leash = 6f;
            if (Time.time >= nextObjectiveSpot || Vector3.Distance(objectiveSpot, spawn) > SpawnZone.Radius)
            {
                nextObjectiveSpot = Time.time + Random.Range(3f, 6f);
                objectiveSpot = AroundPoint(spawn, SpawnZone.Radius * 0.6f);
            }
            goal = objectiveSpot;
            return true;
        }
        bool contested = attacker || EnemyOnPoint(match);
        bool inside = OnPoint(match, transform.position);

        if (attacker)
        {
            leash = 14f;
            holdPoint = inside;   // na bodu zustat a branit si ho
        }
        else
        {
            leash = contested ? 12f : 22f;
            holdPoint = contested && inside;
        }

        // strelec na dalku: kdyz uz na bodu stoji nekdo z jeho tymu, kryje ho z mista s vyhledem na bod
        var weapon = hero != null && hero.Hero != null ? hero.Hero.weapon : null;
        if (weapon != null && CurrentRole(weapon) == Role.Ranged && (AlliesOnPoint(match) > 0 || (!attacker && !contested)))
        {
            RangedBand(weapon, out float minR, out float maxR);
            leash = maxR + 5f;
            holdPoint = false;
            goal = FiringSpot(watchSpot, point, Mathf.Min(minR, 12f), Mathf.Min(maxR, 28f));
            return true;
        }

        // misto: utocnik a obrance pri zabirani uvnitr bodu, obrance jinak v okoli (kryti, vyhled na bod)
        bool wantInside = attacker || contested;
        if (Time.time >= nextObjectiveSpot || objectiveSpotOnPoint != wantInside
            || Vector3.Distance(transform.position, objectiveSpot) < 1.5f || Vector3.Distance(objectiveSpot, point) > 20f)
        {
            nextObjectiveSpot = Time.time + Random.Range(4f, 8f);
            objectiveSpotOnPoint = wantInside;
            objectiveSpot = wantInside ? InsidePoint(match) : AroundPoint(point, Mathf.Max(match.pointSize.Value.x, match.pointSize.Value.y) * 0.5f + 6f);
        }
        goal = objectiveSpot;
        return true;
    }

    int AlliesOnPoint(MatchManager match)
    {
        int count = 0;
        foreach (var ally in allies)
            if (OnPoint(match, ally.transform.position)) count++;
        return count;
    }

    // Je misto uvnitr obdelniku bodu (natoceni bodu, vyska do 4 m)?
    static bool OnPoint(MatchManager match, Vector3 position)
    {
        Vector3 local = Quaternion.Euler(0f, -match.pointYaw.Value, 0f) * (position - match.pointPosition.Value);
        Vector2 size = match.pointSize.Value;
        return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.y * 0.5f && local.y > -1.5f && local.y < 4f;
    }

    // Stoji na bodu nekdo z utocniku (pro obrance: je potreba bod branit primo)? Zjistuje se 3x za sekundu.
    bool enemyOnPoint;
    float nextEnemyOnPointCheck;

    bool EnemyOnPoint(MatchManager match)
    {
        if (Time.time < nextEnemyOnPointCheck) return enemyOnPoint;
        nextEnemyOnPointCheck = Time.time + 0.3f;
        enemyOnPoint = CheckEnemyOnPoint(match);
        return enemyOnPoint;
    }

    bool CheckEnemyOnPoint(MatchManager match)
    {
        foreach (var other in FindObjectsByType<PlayerHero>())
        {
            if (other == hero || !other.IsSpawned || other.IsJoining || Combat.SameTeam(gameObject, other.gameObject)) continue;
            var otherHealth = other.GetComponent<Health>();
            if (otherHealth == null || otherHealth.currentHealth.Value <= 0f) continue;
            if (OnPoint(match, other.transform.position)) return true;
        }
        return match.progress.Value > 0.01f;
    }

    // Nahodne misto uvnitr bodu (kousek od okraje), na navigacni siti.
    Vector3 InsidePoint(MatchManager match)
    {
        Vector2 size = match.pointSize.Value;
        var rotation = Quaternion.Euler(0f, match.pointYaw.Value, 0f);
        for (int tries = 0; tries < 6; tries++)
        {
            Vector3 local = new Vector3(Random.Range(-0.35f, 0.35f) * size.x, 0f, Random.Range(-0.35f, 0.35f) * size.y);
            Vector3 candidate = match.pointPosition.Value + rotation * local;
            if (NavMesh.SamplePosition(candidate, out var hit, 2f, BotNavigation.Filter) && OnPoint(match, hit.position))
                return hit.position;
        }
        return match.pointPosition.Value;
    }

    // Bez nepritele: v utoku a obrane kolem aktivniho bodu (prochazi ho a okoli). V deathmatchi body nehraji roli:
    // bot jde lovit - do oblasti, kde je nektery nepritel (jako hrac, ktery vi, odkud strilelo), jinak po mape.
    Vector3 IdleGoal(MatchManager match)
    {
        bool arrived = Vector3.Distance(transform.position, wanderGoal) < 2.5f;
        if (match.IsAttackMode)
        {
            Vector3 point = match.pointPosition.Value;
            if (Time.time >= nextWander || arrived || Vector3.Distance(wanderGoal, point) > 12f)
            {
                nextWander = Time.time + Random.Range(5f, 9f);
                wanderGoal = AroundPoint(point, 7f);
            }
            return wanderGoal;
        }

        if (Time.time >= nextHotspotSwitch || hotspot == Vector3.zero || Vector3.Distance(transform.position, hotspot) < 6f)
        {
            var huntWeapon = hero != null && hero.Hero != null ? hero.Hero.weapon : null;
            bool rangedHunter = huntWeapon != null && CurrentRole(huntWeapon) == Role.Ranged;
            nextHotspotSwitch = Time.time + (rangedHunter ? Random.Range(5f, 8f) : Mathf.Lerp(16f, 8f, aggression) * Random.Range(0.8f, 1.2f));
            hotspot = HuntSpot();
            nextWander = 0f;
        }
        if (Time.time >= nextWander || arrived)
        {
            nextWander = Time.time + Random.Range(4f, 7f);
            wanderGoal = AroundPoint(hotspot, 6f);
        }
        // strelec na dalku: v blizkosti oblasti nepritele si najde misto s vyhledem na ni
        var weapon = hero != null && hero.Hero != null ? hero.Hero.weapon : null;
        if (weapon != null && CurrentRole(weapon) == Role.Ranged)
        {
            RangedBand(weapon, out float minR, out float maxR);
            if (Vector3.Distance(transform.position, hotspot) < maxR + 15f)
                return FiringSpot(watchSpot, hotspot, minR, maxR);
        }
        return wanderGoal;
    }

    Vector3 hotspot;
    float nextHotspotSwitch;

    // Deathmatch: oblast nahodneho nepritele (s nepresnosti), kdyz zadny neni, klicove misto mapy.
    Vector3 HuntSpot()
    {
        var enemies = new List<PlayerHero>();
        foreach (var other in FindObjectsByType<PlayerHero>())
        {
            if (other == hero || !other.IsSpawned || other.IsJoining || other.IsUntargetable || Combat.SameTeam(gameObject, other.gameObject)) continue;
            var otherHealth = other.GetComponent<Health>();
            if (otherHealth != null && otherHealth.currentHealth.Value > 0f) enemies.Add(other);
        }
        // kdyz o nikom nevi nebo obcas (at prochazi i chaty): nahodne misto pod strechou
        if (enemies.Count == 0 || Random.value < 0.3f)
        {
            var indoor = BotNavigation.RandomIndoorSpot();
            if (indoor.HasValue) return indoor.Value;
        }
        if (enemies.Count == 0) return RandomHotspot();
        var weapon = hero != null && hero.Hero != null ? hero.Hero.weapon : null;
        float spread = weapon != null && CurrentRole(weapon) == Role.Ranged ? 5f : Mathf.Lerp(14f, 6f, aggression);
        Vector2 jitter = Random.insideUnitCircle * spread;
        return enemies[Random.Range(0, enemies.Count)].transform.position + new Vector3(jitter.x, 0f, jitter.y);
    }

    // Klicova mista mapy = mista bodu A, B, C (CapturePoint_1..3 ve scene).
    Vector3 RandomHotspot()
    {
        var spots = new List<Vector3>();
        for (int i = 1; i <= MatchManager.CapturePointCount; i++)
        {
            var marker = GameObject.Find($"CapturePoint_{i}");
            if (marker != null) spots.Add(marker.transform.position);
        }
        if (spots.Count == 0) return transform.position;
        // nejdriv jine misto nez to, kde prave je
        for (int tries = 0; tries < 4; tries++)
        {
            Vector3 pick = spots[Random.Range(0, spots.Count)];
            if (spots.Count == 1 || Vector3.Distance(pick, hotspot) > 5f) return pick;
        }
        return spots[0];
    }

    // Nahodne misto na navigacni siti v okoli bodu.
    Vector3 AroundPoint(Vector3 center, float radius)
    {
        for (int tries = 0; tries < 6; tries++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(candidate, out var hit, 3f, BotNavigation.Filter))
                return hit.position;
        }
        return NavMesh.SamplePosition(center, out var near, 8f, BotNavigation.Filter) ? near.position : center;
    }

    // ---------------- chuze po navigacni siti ----------------

    Vector3 FollowPath(Vector3 goal)
    {
        Vector3 feet = transform.position;
        if (Time.time >= nextRepath || Vector3.Distance(goal, pathGoal) > 3f)
        {
            nextRepath = Time.time + 1f;
            pathGoal = goal;
            Repath(feet, goal);
        }

        while (corner < path.Count)
        {
            Vector3 to = path[corner] - feet;
            to.y = 0f;
            if (to.magnitude > 0.7f) break;
            corner++;
        }

        // navigace k pronasledovanemu nevede (dvere, prah, je v chate): po jeho stope
        if (pathIncomplete && chaseTarget != null && (corner >= path.Count || Vector3.Distance(feet, path[path.Count - 1]) < 2.5f)
            && TrailDirection(chaseTarget, out Vector3 trailDir))
            return trailDir;

        if (corner >= path.Count)
        {
            // bez cesty: primo k cili, jen kdyz je blizko a nic mezi tim neni (jinak by sel do zdi)
            Vector3 direct = goal - feet;
            direct.y = 0f;
            if (direct.magnitude <= 1f) return Vector3.zero;
            // (bot mimo navigacni sit, napr. na spawnu, jde rovne, dokud pred nim nic neni)
            if (Physics.Raycast(feet + Vector3.up * 0.8f, direct.normalized, Mathf.Min(direct.magnitude, 1.5f), ~0, QueryTriggerInteraction.Ignore))
            {
                // cesta nevede a primo to nejde: za chvili jiny cil (ne kazdy snimek - hledani cesty neco stoji)
                if (Time.time >= nextNoPathReset)
                {
                    nextNoPathReset = Time.time + 1f;
                    ForgetGoal();
                    nextRepath = 0f;
                }
                return Vector3.zero;
            }
            return direct.normalized;
        }

        Vector3 next = path[corner];
        Vector3 dir = next - feet;
        // schod nebo vyvyseni kousek pred botem: vyskocit
        if (dir.y > 0.25f && new Vector2(dir.x, dir.z).magnitude < 1.6f)
            jumpRequest = true;
        dir.y = 0f;
        return dir.normalized;
    }

    void Repath(Vector3 from, Vector3 to)
    {
        path.Clear();
        corner = 0;
        pathIncomplete = true;
        if (!BotNavigation.Ready) return;
        if (!NavMesh.SamplePosition(from, out var start, 3f, BotNavigation.Filter))
        {
            if (!reportedOffMesh)
            {
                reportedOffMesh = true;
                Debug.LogWarning($"[Boti] {name} ({(hero != null && hero.Hero != null ? hero.Hero.heroName : "?")}) stoji mimo navigacni sit na {from} - jde rovne k cili.");
            }
            return;
        }
        var navPath = new NavMeshPath();
        if (!NavMesh.SamplePosition(to, out var end, 6f, BotNavigation.Filter)
            || !NavMesh.CalculatePath(start.position, end.position, BotNavigation.Filter, navPath))
        {
            ForgetGoal();
            return;
        }
        path.AddRange(navPath.corners);
        corner = path.Count > 1 ? 1 : 0;
        // cil musi byt opravdu na konci cesty (SamplePosition muze cil "pritahnout" ven z chaty)
        pathIncomplete = navPath.status != NavMeshPathStatus.PathComplete || Vector3.Distance(end.position, to) > 1.5f;
        // cil, kam nevede cela cesta (nedosazitelne misto, napr. strecha): vybrat jiny
        if (navPath.status != NavMeshPathStatus.PathComplete)
            ForgetGoal();
    }

    // Cil je nedosazitelny: pri toulani, hledani i na bodu si vybrat jine misto.
    void ForgetGoal()
    {
        nextWander = 0f;
        nextHotspotSwitch = 0f;
        investigateUntil = -1f;
        nextObjectiveSpot = 0f;
    }

    // Kdyz chce jit, ale skoro se nehne: vyskocit; opakovane - zkusit jinou cestu kousek vedle.
    void CheckStuck(Vector3 moveDir)
    {
        if (Time.time < nextStuckCheck) return;
        nextStuckCheck = Time.time + 1f;
        bool wantsMove = moveDir.sqrMagnitude > 0.25f;
        Vector3 delta = transform.position - stuckCheckPos;
        delta.y = 0f;
        stuckCheckPos = transform.position;
        // postup ve smeru, kam chce jit (a ve smeru z minule kontroly) - kdyz se jen tre o zed, je to malo
        Vector3 wanted = (moveDir.normalized + stuckLastDir).normalized;
        stuckLastDir = moveDir.normalized;
        float progress = wanted.sqrMagnitude > 0.1f ? Vector3.Dot(delta, wanted) : delta.magnitude;
        if (!wantsMove || progress > 0.6f)
        {
            stuckCount = 0;
            return;
        }
        stuckCount++;
        jumpRequest = true;
        if (stuckCount >= 2)
        {
            // objizdka: kousek zpet nebo do strany (na siti), chvili tam, pak znovu k cili jinou cestou
            stuckCount = 0;
            Vector3 back = -transform.forward;
            Vector3 aside = transform.position + Quaternion.Euler(0f, Random.Range(-100f, 100f), 0f) * back * Random.Range(3f, 6f);
            detourGoal = NavMesh.SamplePosition(aside, out var hit, 3f, BotNavigation.Filter) ? hit.position : aside;
            detourUntil = Time.time + 2.5f;
            nextObjectiveSpot = 0f;   // na bodu si vybrat jine misto
            nextWander = 0f;
            nextRepath = 0f;
        }
    }

    // Mimo boj (a mimo drzeni bodu) nema bot stat dlouho na jednom miste: kdyz se 7 s nepohne o 4 m,
    // jde jinam - nejcasteji prozkoumat nejakou chatu.
    Vector3 loiterPos;
    float loiterSince = -1f;

    void CheckLoiter(bool busy)
    {
        if (busy || loiterSince < 0f || Vector3.Distance(transform.position, loiterPos) > 4f)
        {
            loiterPos = transform.position;
            loiterSince = Time.time;
            return;
        }
        if (Time.time - loiterSince < 7f) return;

        loiterPos = transform.position;
        loiterSince = Time.time;
        ForgetGoal();
        var indoor = BotNavigation.RandomIndoorSpot();
        if (indoor.HasValue && Random.value < 0.7f)
        {
            investigatePos = indoor.Value;   // (investigace = jit se tam podivat)
            investigateUntil = Time.time + 15f;
        }
        nextRepath = 0f;
    }

    // ---------------- stopy hracu (bot jde za nepritelem tam, kam navigacni sit nevede - dvere, prahy) ----------------

    const float TrailStep = 0.6f;       // novy bod stopy po tolika metrech
    const int TrailLength = 60;         // ~ poslednich 15-20 s chuze
    static readonly Dictionary<PlayerHero, List<Vector3>> trails = new Dictionary<PlayerHero, List<Vector3>>();
    static int trailFrame = -1;
    static float nextTrailSample;

    static void RecordTrails()
    {
        if (trailFrame == Time.frameCount || Time.time < nextTrailSample) return;
        trailFrame = Time.frameCount;
        nextTrailSample = Time.time + 0.2f;
        foreach (var player in FindObjectsByType<PlayerHero>())
        {
            if (!player.IsSpawned) continue;
            if (!trails.TryGetValue(player, out var trail)) trails[player] = trail = new List<Vector3>();
            Vector3 feet = player.transform.position;
            var health = player.GetComponent<Health>();
            if (health != null && health.currentHealth.Value <= 0f) { trail.Clear(); continue; }   // (po smrti nova stopa)
            if (trail.Count > 0 && (trail[trail.Count - 1] - feet).sqrMagnitude < TrailStep * TrailStep) continue;
            if (trail.Count > 0 && (trail[trail.Count - 1] - feet).sqrMagnitude > 64f) trail.Clear();   // teleport / oziveni
            trail.Add(feet);
            if (trail.Count > TrailLength) trail.RemoveAt(0);
        }
    }

    // Smer po stope cile: nejnovejsi bod stopy, ktery bot primo vidi (a neni moc daleko).
    bool TrailDirection(PlayerHero who, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (who == null || !trails.TryGetValue(who, out var trail) || trail.Count == 0) return false;
        Vector3 feet = transform.position;
        Vector3 eye = feet + Vector3.up * 1.0f;
        for (int i = trail.Count - 1; i >= 0; i--)
        {
            Vector3 point = trail[i];
            Vector3 flat = point - feet;
            flat.y = 0f;
            if (flat.magnitude > 20f) continue;
            if (flat.magnitude < 0.5f) return false;   // uz stoji na nejnovejsim viditelnem bode
            if (Physics.Linecast(eye, point + Vector3.up * 1.0f, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<PlayerHero>() == null) continue;
            // prah / schod: vyskocit
            if (point.y - feet.y > 0.25f && flat.magnitude < 1.6f) jumpRequest = true;
            direction = flat.normalized;
            return true;
        }
        return false;
    }

    PlayerHero chaseTarget;   // koho prave pronasleduje (pro stopu)
    bool pathIncomplete;      // navigace k cili nevede cela

    Vector3 detourGoal;
    float detourUntil;
    Vector3 stuckLastDir;
    float nextNoPathReset;
    bool reportedOffMesh;
}

// Navigacni sit pro boty: postavi se za behu na hostu (z koliznich tvaru mapy), poprve pri pridani bota.
// Mapa se tak muze libovolne upravovat - sit je vzdy aktualni.
public static class BotNavigation
{
    static NavMeshSurface surface;
    public static bool Ready => surface != null && surface.navMeshData != null;

    // Vlastni typ agenta: jako postava hrace (polomer 0,4 m - projde dvermi, vyska 1,85 m, schod jen 0,3 m, svah 42 st.).
    // Vychozi "Humanoid" pocita se schodem 0,75 m - sit pak vedla i pres hrany, ktere postava nevyjde.
    static int agentType = -1;
    public static NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = agentType >= 0 ? agentType : 0, areaMask = NavMesh.AllAreas };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        surface = null;
        agentType = -1;
        indoorSpots.Clear();
    }

    public static void EnsureBuilt()
    {
        if (Ready) return;

        // postavy hracu do site nepatri (byly by v ni diry)
        var controllers = Object.FindObjectsByType<CharacterController>();
        var states = new bool[controllers.Length];
        for (int i = 0; i < controllers.Length; i++)
        {
            states[i] = controllers[i].enabled;
            controllers[i].enabled = false;
        }

        var settings = NavMesh.CreateSettings();
        settings.agentRadius = 0.4f;    // (0,6 m zavrelo dvere chaty - boti nemeli kudy dovnitr)
        settings.agentHeight = 1.85f;   // (zarubne a prostor pod schody)
        settings.agentClimb = 0.45f;   // prahy a vyssi schody - bot je preskoci
        settings.agentSlope = 42f;
        agentType = settings.agentTypeID;

        var go = new GameObject("BotNavigace");
        surface = go.AddComponent<NavMeshSurface>();
        surface.agentTypeID = agentType;
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.overrideVoxelSize = true;
        surface.voxelSize = 0.15f;   // jemnejsi mrizka - uzke pruchody (dvere) v siti zustanou
        float started = Time.realtimeSinceStartup;
        surface.BuildNavMesh();

        for (int i = 0; i < controllers.Length; i++)
            if (controllers[i] != null) controllers[i].enabled = states[i];

        FindIndoorSpots();
        for (int i = 1; i <= MatchManager.CapturePointCount; i++)
        {
            var marker = GameObject.Find($"CapturePoint_{i}");
            if (marker == null) continue;
            bool on = NavMesh.SamplePosition(marker.transform.position, out var hit, 2.5f, Filter);
            Debug.Log($"[Boti] Bod {MatchManager.PointNames[i - 1]}: navigacni sit {(on ? $"dosahuje ({Vector3.Distance(hit.position, marker.transform.position):0.0} m)" : "NEDOSAHUJE")}.");
        }
        Debug.Log($"[Boti] Navigacni sit postavena za {Time.realtimeSinceStartup - started:0.0} s, mist pod strechou: {indoorSpots.Count}.");
    }

    // Mista uvnitr budov (chaty, kulna...): body navigacni site, nad kterymi je do 6 m strop.
    static readonly List<Vector3> indoorSpots = new List<Vector3>();

    static void FindIndoorSpots()
    {
        indoorSpots.Clear();
        var mesh = NavMesh.CalculateTriangulation();
        if (mesh.indices == null || mesh.indices.Length < 3) return;
        var random = new System.Random(12345);
        int triangles = mesh.indices.Length / 3;
        for (int attempt = 0; attempt < 1500 && indoorSpots.Count < 80; attempt++)
        {
            int t = random.Next(triangles) * 3;
            Vector3 a = mesh.vertices[mesh.indices[t]], b = mesh.vertices[mesh.indices[t + 1]], c = mesh.vertices[mesh.indices[t + 2]];
            float u = (float)random.NextDouble(), v = (float)random.NextDouble();
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            Vector3 p = a + (b - a) * u + (c - a) * v;
            if (!Physics.Raycast(p + Vector3.up * 0.3f, Vector3.up, 6f, ~0, QueryTriggerInteraction.Ignore)) continue;
            bool near = false;
            foreach (var s in indoorSpots)
                if ((s - p).sqrMagnitude < 9f) { near = true; break; }
            if (!near) indoorSpots.Add(p);
        }
    }

    public static Vector3? RandomIndoorSpot()
    {
        if (indoorSpots.Count == 0) return null;
        return indoorSpots[Random.Range(0, indoorSpots.Count)];
    }
}
