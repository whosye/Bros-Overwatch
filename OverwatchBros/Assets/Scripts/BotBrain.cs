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
    static void ResetStatics() => All.Clear();

    public static bool IsBot(GameObject go) => go != null && go.TryGetComponent<BotBrain>(out _);

    // Postava, kterou ovlada hrac na tomhle pocitaci (ne bot, ktery na hostu taky "patri" hostovi).
    public static bool IsLocalHuman(NetworkBehaviour behaviour) => behaviour != null && behaviour.IsOwner && !IsBot(behaviour.gameObject);

    // ---------------- obtiznost (1. etapa: jedna stredni) ----------------

    public float reactionTime = 0.6f;     // s od spatreni do prvni strely
    public float turnSpeed = 180f;         // stupnu za sekundu
    public float aimError = 3f;          // stupnu (nahodna odchylka, meni se kazdou pul sekundu)
    public float viewDistance = 55f;
    public float memorySeconds = 7f;       // jak dlouho si pamatuje, kde naposledy videl nepritele
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
        aggression = Random.Range(0.3f, 0.95f);
        reactionTime *= Random.Range(0.8f, 1.35f);
        aimError *= Random.Range(0.8f, 1.35f);
        nextHunch = Time.time + Random.Range(8f, 20f);
    }

    // ---------------- "lidske" chovani ----------------

    // 0 = opatrny (drzi pozici, strili z dalky), 1 = dravec (jde po nepritelich)
    float aggression;
    const int MaxChasers = 2;            // za jednim hracem se aktivne zenou nejvys dva boti
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

    // Kdy co zmacknout: ultimatku na nepritele v dosahu, E a PTM v boji na zamereneho nepritele, Shift pri pribliazeni.
    // (Kdyz je schopnost na cooldownu, stisk prosté nic neudela.)
    void UseAbilities(WeaponDefinition weapon, bool combat, bool chase)
    {
        if (Time.time < nextAbilityThink || hero == null || hero.Hero == null) return;
        nextAbilityThink = Time.time + Random.Range(0.35f, 0.75f);
        var def = hero.Hero;
        float hp = health != null && health.maxHealth > 0f ? health.currentHealth.Value / health.maxHealth : 1f;

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
            PickTarget();
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
        if (chase && !targetVisible && aggression < 0.5f && !melee)
            chase = false;
        chasing = chase;
        Hunch();

        if (wantsHealth)
            goal = healthPos;
        else if (chase)
            goal = targetVisible ? target.transform.position : lastSeenPos;
        else if (hasObjective)
            goal = objective;
        else if (combat)
            goal = transform.position;              // drzi pozici a strili (opatrny / uz jdou jini)
        else if (Time.time < investigateUntil)
        {
            goal = investigatePos;                  // slysel strelbu / tusi, kde nepritel je
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
        if (combat && chase && !wantsHealth && weapon != null && Time.time >= detourUntil)
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
        Vector3 lookAt = combat ? AimPoint(weapon, eye) : (moveDir.sqrMagnitude > 0.01f ? eye + moveDir * 10f : eye + transform.forward * 10f);
        TurnTowards(lookAt - eye, combat);

        // pohyb v mistnich osach tela
        Vector3 local = Quaternion.Euler(0f, -Yaw, 0f) * moveDir;
        Move = new Vector2(local.x, local.z);
        Run = !combat && moveDir.sqrMagnitude > 0.25f;

        CheckStuck(moveDir);
        Shoot(weapon, eye, combat);
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

    void TurnTowards(Vector3 direction, bool combat)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        if (combat && Time.time >= nextAimJitter)
        {
            nextAimJitter = Time.time + 0.5f;
            aimOffset = Random.insideUnitCircle * aimError;
        }
        float wantYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + (combat ? aimOffset.x : 0f);
        float flat = new Vector2(direction.x, direction.z).magnitude;
        float wantPitch = combat ? -Mathf.Atan2(direction.y, flat) * Mathf.Rad2Deg + aimOffset.y : 0f;
        Yaw = Mathf.MoveTowardsAngle(Yaw, wantYaw, turnSpeed * Time.deltaTime);
        Pitch = Mathf.Clamp(Mathf.MoveTowardsAngle(Pitch, wantPitch, turnSpeed * Time.deltaTime), -80f, 80f);
    }

    void Shoot(WeaponDefinition weapon, Vector3 eye, bool combat)
    {
        if (!combat || weapon == null || seenSince < 0f || Time.time - seenSince < reactionTime)
        {
            SetFire(false);
            return;
        }

        float distance = Vector3.Distance(eye, target.transform.position + Vector3.up * 1.25f);
        Vector3 forward = Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward;
        Vector3 toAim = AimPoint(weapon, eye) - eye;
        bool aligned = Vector3.Angle(forward, toAim) < (weapon.IsMelee ? 25f : 5f + aimError);
        bool inRange = distance <= EffectiveRange(weapon);

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

    // Bez nepritele: hraje se kolem bodu. V utoku a obrane kolem aktivniho bodu (prochazi ho a okoli),
    // v team deathmatchi strida klicova mista mapy: velka chata (A), mala chata (B), rozhledna (C).
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

        if (Time.time >= nextHotspotSwitch || hotspot == Vector3.zero)
        {
            nextHotspotSwitch = Time.time + Random.Range(18f, 30f);
            hotspot = RandomHotspot();
            nextWander = 0f;
        }
        if (Time.time >= nextWander || arrived)
        {
            nextWander = Time.time + Random.Range(5f, 9f);
            wanderGoal = AroundPoint(hotspot, 8f);
        }
        return wanderGoal;
    }

    Vector3 hotspot;
    float nextHotspotSwitch;

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

        if (corner >= path.Count)
        {
            // bez cesty (mimo sit): primo k cili
            Vector3 direct = goal - feet;
            direct.y = 0f;
            return direct.magnitude > 1f ? direct.normalized : Vector3.zero;
        }

        Vector3 next = path[corner];
        Vector3 dir = next - feet;
        // schod nebo vyvyseni kousek pred botem: vyskocit
        if (dir.y > 0.4f && new Vector2(dir.x, dir.z).magnitude < 1.6f)
            jumpRequest = true;
        dir.y = 0f;
        return dir.normalized;
    }

    void Repath(Vector3 from, Vector3 to)
    {
        path.Clear();
        corner = 0;
        if (!BotNavigation.Ready) return;
        if (!NavMesh.SamplePosition(from, out var start, 3f, BotNavigation.Filter)) return;
        if (!NavMesh.SamplePosition(to, out var end, 6f, BotNavigation.Filter)) return;
        var navPath = new NavMeshPath();
        if (!NavMesh.CalculatePath(start.position, end.position, BotNavigation.Filter, navPath)) return;
        path.AddRange(navPath.corners);
        corner = path.Count > 1 ? 1 : 0;
        // cil, kam nevede cela cesta (nedosazitelne misto): pri toulani / hledani si vybrat jiny
        if (navPath.status != NavMeshPathStatus.PathComplete)
        {
            nextWander = 0f;
            nextHotspotSwitch = 0f;
            investigateUntil = -1f;
        }
    }

    // Kdyz chce jit, ale skoro se nehne: vyskocit; opakovane - zkusit jinou cestu kousek vedle.
    void CheckStuck(Vector3 moveDir)
    {
        if (Time.time < nextStuckCheck) return;
        nextStuckCheck = Time.time + 1f;
        bool wantsMove = moveDir.sqrMagnitude > 0.25f;
        float moved = Vector3.Distance(transform.position, stuckCheckPos);
        stuckCheckPos = transform.position;
        if (!wantsMove || moved > 0.5f)
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

    Vector3 detourGoal;
    float detourUntil;
}

// Navigacni sit pro boty: postavi se za behu na hostu (z koliznich tvaru mapy), poprve pri pridani bota.
// Mapa se tak muze libovolne upravovat - sit je vzdy aktualni.
public static class BotNavigation
{
    static NavMeshSurface surface;
    public static bool Ready => surface != null && surface.navMeshData != null;

    // Vlastni typ agenta: jako postava hrace (polomer 0,45 m, vyska 2 m, schod jen 0,3 m, svah 42 st.).
    // Vychozi "Humanoid" pocita se schodem 0,75 m - sit pak vedla i pres hrany, ktere postava nevyjde.
    static int agentType = -1;
    public static NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = agentType >= 0 ? agentType : 0, areaMask = NavMesh.AllAreas };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        surface = null;
        agentType = -1;
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
        settings.agentRadius = 0.45f;
        settings.agentHeight = 2f;
        settings.agentClimb = 0.3f;
        settings.agentSlope = 42f;
        agentType = settings.agentTypeID;

        var go = new GameObject("BotNavigace");
        surface = go.AddComponent<NavMeshSurface>();
        surface.agentTypeID = agentType;
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.overrideVoxelSize = true;
        surface.voxelSize = 0.2f;
        float started = Time.realtimeSinceStartup;
        surface.BuildNavMesh();

        for (int i = 0; i < controllers.Length; i++)
            if (controllers[i] != null) controllers[i].enabled = states[i];

        Debug.Log($"[Boti] Navigacni sit postavena za {Time.realtimeSinceStartup - started:0.0} s.");
    }
}
