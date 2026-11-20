using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Herni rezim "Utok a obrana" (druha cast MatchManageru), jako Assault v Overwatchi, s vymenou stran.
//
// Dve kola. V kazdem jeden tym utoci, druhy brani; v druhem kole se strany prohodi.
//  - Priprava: utocnici stoji ve spawnu, obranci se rozestavuji (body jsou zamcene).
//  - Body A (velka chata), B (mala chata), C (plosina pred rozhlednou) jdou po sobe.
//  - Utocnici bod zabiraji (jen oni), obrance na bode = sporny bod. Postup se uklada po tretinach;
//    kdyz na bode nikdo z utocniku neni, pomalu klesa k posledni tretine. Vic utocniku zabira rychleji.
//  - Po zabrani bodu: utocnici dostanou cas navic, oba tymy se presunou k dalsimu bodu, ten se chvili odemyka.
//  - Dojde-li cas a utocnici stoji na bode, je prodlouzeni - hraje se dal, dokud z bodu neodejdou.
// Vyhodnoceni jako v kompetitivnim Assaultu Overwatche (casova banka):
//  - skore = vsechny dobyte body (scitaji se pres vsechna kola); vic bodu vyhrava
//  - kdo dobude vsechny body, zbyly cas utoku si uklada do CASOVE BANKY (jinak banka 0)
//  - rovnost a oba tymy maji v bance aspon MinBankSeconds: dalsi dvojice kol, kazdy utoci jen s casem z banky
//    (zase od bodu A, za dobyti bodu se cas pricita jako obvykle)
//  - rovnost a banku ma jen jeden tym: ten utoci sam; kdyz s casem z banky dobude aspon tretinu bodu A, vyhrava, jinak remiza
//  - rovnost a nikdo nema banku: vetsi postup na bodu, kde oba skoncili; stejny postup = remiza
// Druhe kolo dvojice skonci hned, jakmile je o vitezi rozhodnuto.
public partial class MatchManager
{
    public const int ModeDeathmatch = 0;
    public const int ModeAttack = 1;

    public const int StateLocked = 0, StateFree = 1, StateTeam0 = 2, StateTeam1 = 3, StateContested = 4;

    // faze kola
    public const int RoundSetup = 0, RoundAttack = 1, RoundOvertime = 2, RoundIntermission = 3, RoundDone = 4;

    // Pocet bodu podle mapy (CapturePoint_1..N ve scene, nejvic MaxCapturePoints): chaty 4, Hanamura 3.
    public const int MaxCapturePoints = 4;
    public static readonly string[] PointNames = { "A", "B", "C", "D" };
    public NetworkVariable<int> pointCount = new NetworkVariable<int>(3);
    public int CapturePointCount => Mathf.Clamp(pointCount.Value, 1, MaxCapturePoints);
    // Kdy se pustilo auto (po dobyti bodu A) - u bodu A stoji zaparkovane (YarisTraffic).
    public NetworkVariable<double> trafficReleaseTime = new NetworkVariable<double>(0);
    public static readonly Vector2 CaptureSize = new Vector2(10f, 7f);

    const float SetupSeconds = 20f;
    const float UnlockNextSeconds = 10f;
    const float AttackSeconds = 180f;
    const float BonusSeconds = 120f;
    const float LateBonusSeconds = 90f;
    const float IntermissionSeconds = 8f;
    const float DecayDelay = 2f;
    const float OvertimeGrace = 1.2f;
    const float MinBankSeconds = 60f;        // od kolika sekund v bance se hraje kolo navic
    public const float SoloGoal = 1f / 3f;   // samostatne kolo navic: tolik bodu A staci k vyhre

    // druh kola: prvni / druhe ve dvojici (druhy tym prekonava vysledek prvniho), samostatne kolo navic
    public const int KindFirst = 0, KindSecond = 1, KindSolo = 2;
    public NetworkVariable<int> roundKind = new NetworkVariable<int>(KindFirst);
    public bool ChasingRound => roundKind.Value != KindFirst;

    public NetworkVariable<int> gameMode = new NetworkVariable<int>(ModeDeathmatch);
    public NetworkVariable<int> captureSeconds = new NetworkVariable<int>(20);
    public NetworkVariable<int> pointIndex = new NetworkVariable<int>(0);
    public NetworkVariable<int> pointState = new NetworkVariable<int>(StateLocked);
    public NetworkVariable<Vector3> pointPosition = new NetworkVariable<Vector3>();
    public NetworkVariable<float> pointYaw = new NetworkVariable<float>();
    public NetworkVariable<Vector2> pointSize = new NetworkVariable<Vector2>(new Vector2(10f, 7f));
    public NetworkVariable<float> progress = new NetworkVariable<float>();

    public NetworkVariable<int> round = new NetworkVariable<int>(1);
    public NetworkVariable<int> roundPhase = new NetworkVariable<int>(RoundSetup);
    public NetworkVariable<int> attackTeam = new NetworkVariable<int>(0);
    public NetworkVariable<int> secondsLeft = new NetworkVariable<int>();      // cas utoku (nebo pripravy / odemykani)
    public NetworkVariable<int> lockSecondsLeft = new NetworkVariable<int>();  // do odemceni bodu (0 = odemceny)

    // Vysledek prvniho kola dvojice (cil pro druhe): kolik bodu, postup na dalsim bodu a kolik mu zbylo v bance.
    // V samostatnem kole navic: cil = SoloGoal bodu A.
    public NetworkVariable<int> firstPoints = new NetworkVariable<int>(-1);
    public NetworkVariable<float> firstProgress = new NetworkVariable<float>();
    public NetworkVariable<float> firstTime = new NetworkVariable<float>();   // banka souperova kola (s)

    public bool IsAttackMode => gameMode.Value == ModeAttack;
    public int DefendTeam => 1 - attackTeam.Value;

    // Mista oziveni (plati na vsech klientech; posila se i v RPC, aby dorazila vcas).
    bool customSpawns;
    readonly Vector3[] customSpawn = new Vector3[2];

    // server
    readonly Vector3[] points = new Vector3[MaxCapturePoints];
    readonly float[] pointYaws = new float[MaxCapturePoints];
    readonly Vector2[] pointSizes = new Vector2[MaxCapturePoints];
    readonly Vector3[] attackSpawns = new Vector3[MaxCapturePoints];
    readonly Vector3[] defendSpawns = new Vector3[MaxCapturePoints];
    float timer;          // cas utoku
    float lockTimer;      // priprava / odemykani bodu
    float phaseTimer;     // mezihra
    float timeUsed;       // jak dlouho utocnici utoci (bez pripravy)
    float lastAttackersOnPoint;
    float overtimeEmpty;
    readonly float[] timeBank = new float[2];   // casova banka tymu (s)
    int nextAttacker, nextKind;                 // co se hraje po mezihre
    float nextTime;

    // ---------------- nastaveni v lobby ----------------

    public void SetGameMode(int mode)
    {
        if (IsServer && IsLobby)
            gameMode.Value = mode == ModeAttack ? ModeAttack : ModeDeathmatch;
    }

    public void SetCaptureSeconds(int seconds)
    {
        if (IsServer)
            captureSeconds.Value = Mathf.Clamp(seconds, 5, 120);
    }

    // Kam se ma hrac daneho tymu ozivit. False = pouzij puvodni spawn ze sceny.
    public bool TryGetSpawn(int team, out Vector3 position)
    {
        position = customSpawn[Mathf.Clamp(team, 0, 1)];
        return customSpawns;
    }

    // ---------------- prubeh ----------------

    void Update()
    {
        if (IsSpawned && IsServer)
        {
            ServerAttackTick();
            ServerPickupTick();
            ServerBoilerTick();
            ServerTractorTick();
            ServerGollumUpdate();
            ServerBanterTick();
        }

        CapturePointView.Sync(this);
        RingCooldownTick();
        RingWorldView.Sync(this);
        SpawnZone.Sync(this);
    }

    // Vyska, pod kterou je hrac "mimo mapu" a zemre: 20 m pod nizsi ze zakladen
    // (nebo vyska objektu KillPlane, kdyz ve scene je).
    static float killHeight = float.NaN;

    public static float KillHeight
    {
        get
        {
            if (float.IsNaN(killHeight))
            {
                var plane = GameObject.Find("KillPlane");
                if (plane != null)
                {
                    killHeight = plane.transform.position.y;
                }
                else
                {
                    var base0 = GameObject.Find("SpawnPoint_Team0");
                    var base1 = GameObject.Find("SpawnPoint_Team1");
                    float lowest = Mathf.Min(base0 != null ? base0.transform.position.y : 0f, base1 != null ? base1.transform.position.y : 0f);
                    killHeight = lowest - 20f;
                }
            }

            return killHeight;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetKillHeight()
    {
        killHeight = float.NaN;
    }

    // Vola ResetRound pri startu zapasu.
    void ServerBeginAttackMatch()
    {
        ChoosePoints();
        firstPoints.Value = -1;
        firstProgress.Value = 0f;
        firstTime.Value = 0f;
        round.Value = 1;
        roundKind.Value = KindFirst;
        timeBank[0] = timeBank[1] = 0f;
        attackTeam.Value = 0;
        ServerStartRound(AttackSeconds);
    }

    void ServerStartRound(float attackSeconds)
    {
        timer = attackSeconds;
        timeUsed = 0f;
        overtimeEmpty = 0f;
        lastAttackersOnPoint = -100f;
        ServerApplyPoint(0, SetupSeconds);
        roundPhase.Value = RoundSetup;
        secondsLeft.Value = Mathf.CeilToInt(timer);
        if (round.Value == 1)
        {
            team0Score.Value = 0;
            team1Score.Value = 0;
        }

        // Utocnici cekaji ve spawnu, nez obranci rozestavi (pasti, pole, pozice). Az po presunu na spawn
        // (presun podrzeni rusi), proto s malym zpozdenim.
        StopCoroutine(nameof(HoldAttackersSoon));
        StartCoroutine(nameof(HoldAttackersSoon));
    }

    System.Collections.IEnumerator HoldAttackersSoon()
    {
        yield return new WaitForSeconds(0.3f);
        if (roundPhase.Value == RoundSetup)
            HoldAttackers(Mathf.Max(0f, lockTimer));
    }

    void HoldAttackers(float seconds)
    {
        foreach (var client in PlayerSlots())
        {
            var player = client.PlayerObject;
            var team = player != null ? player.GetComponent<PlayerTeam>() : null;
            var movement = player != null ? player.GetComponent<FirstPersonController>() : null;
            // spawn ve stodole: drzi je zavrene dvere (uvnitr se muzou hybat); bez stodoly stoji na miste
            if (team != null && movement != null && team.teamId.Value == attackTeam.Value
                && SpawnBarn.AtSpawn(customSpawn[Mathf.Clamp(team.teamId.Value, 0, 1)]) == null)
                movement.ServerHold(seconds);
        }
    }

    void ServerApplyPoint(int index, float lockSeconds)
    {
        pointIndex.Value = index;
        // auto se pusti po dobyti prvniho bodu (u bodu A stoji)
        if (index == 1 && NetworkManager != null)
            trafficReleaseTime.Value = NetworkManager.ServerTime.Time;
        pointPosition.Value = points[index];
        pointYaw.Value = pointYaws[index];
        pointSize.Value = pointSizes[index];
        progress.Value = 0f;
        lockTimer = lockSeconds;
        lockSecondsLeft.Value = Mathf.CeilToInt(lockSeconds);
        pointState.Value = StateLocked;

        customSpawns = true;
        customSpawn[attackTeam.Value] = attackSpawns[index];
        customSpawn[DefendTeam] = defendSpawns[index];
        SpawnsClientRpc(customSpawns, customSpawn[0], customSpawn[1]);
    }

    void ServerAttackTick()
    {
        if (!IsAttackMode || IsLobby || IsOver) return;
        ServerCutsceneTick();

        if (roundPhase.Value == RoundIntermission)
        {
            phaseTimer -= Time.deltaTime;
            SetSeconds(secondsLeft, phaseTimer);
            if (phaseTimer <= 0f) ServerBeginNextRound();
            return;
        }
        if (roundPhase.Value == RoundDone) return;

        // Priprava / odemykani bodu.
        if (lockTimer > 0f)
        {
            lockTimer -= Time.deltaTime;
            SetSeconds(lockSecondsLeft, lockTimer);
            if (roundPhase.Value == RoundSetup)
                SetSeconds(secondsLeft, lockTimer);
            if (lockTimer > 0f) return;

            lockSecondsLeft.Value = 0;
            pointState.Value = StateFree;
            if (roundPhase.Value == RoundSetup)
            {
                roundPhase.Value = RoundAttack;
                RoundStartedClientRpc(attackTeam.Value);
            }
        }

        // Kdo stoji v obdelniku.
        CountOnPoint(out int attackers, out int defenders);

        int state = StateFree;
        if (attackers > 0 && defenders > 0) state = StateContested;
        else if (attackers > 0) state = attackTeam.Value == 0 ? StateTeam0 : StateTeam1;
        if (pointState.Value != state)
            pointState.Value = state;
        if (attackers > 0)
            lastAttackersOnPoint = Time.time;

        // Zabirani: jen utocnici, rychleji ve vic lidech; bez utocniku pomalu zpet k posledni tretine.
        float capture = Mathf.Max(1f, captureSeconds.Value);
        if (attackers > 0 && defenders == 0)
        {
            float speed = attackers >= 3 ? 1.5f : attackers == 2 ? 1.25f : 1f;
            progress.Value = Mathf.Min(1f, progress.Value + Time.deltaTime * speed / capture);
            if (progress.Value >= 1f)
            {
                ServerPointCaptured();
                return;
            }
        }
        else if (attackers == 0 && Time.time - lastAttackersOnPoint > DecayDelay)
        {
            float floor = Mathf.Floor(progress.Value * 3f + 0.0001f) / 3f;
            if (progress.Value > floor)
                progress.Value = Mathf.Max(floor, progress.Value - Time.deltaTime / (capture * 2f));
        }

        // Cas utoku.
        timer -= Time.deltaTime;
        timeUsed += Time.deltaTime;
        SetSeconds(secondsLeft, Mathf.Max(0f, timer));

        if (timer <= 0f)
        {
            // Prodlouzeni: hraje se dal, dokud jsou utocnici na bode.
            if (attackers > 0)
            {
                overtimeEmpty = 0f;
                if (roundPhase.Value != RoundOvertime) roundPhase.Value = RoundOvertime;
            }
            else
            {
                overtimeEmpty += Time.deltaTime;
                if (roundPhase.Value != RoundOvertime || overtimeEmpty >= OvertimeGrace)
                    ServerEndRound(pointIndex.Value, progress.Value);
            }
        }

        // Druhe kolo dvojice: konec, jakmile utocnici prekonali postup souperu na stejnem bode (pri stejnem skore).
        if (roundKind.Value == KindSecond && roundPhase.Value != RoundDone)
        {
            if (firstPoints.Value < CapturePointCount && pointIndex.Value == firstPoints.Value
                && Score(attackTeam.Value) == Score(DefendTeam) && progress.Value > firstProgress.Value + 0.01f)
                ServerEndRound(pointIndex.Value, progress.Value);
        }
        // Samostatne kolo navic: staci tretina bodu A.
        else if (roundKind.Value == KindSolo && roundPhase.Value != RoundDone && progress.Value >= SoloGoal - 0.001f)
            ServerEndRound(pointIndex.Value, progress.Value);
    }

    void CountOnPoint(out int attackers, out int defenders)
    {
        attackers = 0;
        defenders = 0;
        foreach (var client in PlayerSlots())
        {
            var player = client.PlayerObject;
            if (player == null) continue;

            var health = player.GetComponent<Health>();
            var hero = player.GetComponent<PlayerHero>();
            var team = player.GetComponent<PlayerTeam>();
            if (health == null || team == null || health.currentHealth.Value <= 0f || (hero != null && hero.IsJoining)) continue;
            // kdo sedi ve snezne rolbe, bod nezabira ani neblokuje
            if (IsDriving(player.NetworkObjectId) || IsRiding(player.NetworkObjectId)) continue;

            if (!InCaptureArea(player.transform.position)) continue;

            if (team.teamId.Value == attackTeam.Value) attackers++;
            else defenders++;
        }
    }

    // Stoji 'feet' na bodu? Obdelnik bodu (natoceni), vyska -1,5..4 m - ale ne v jinem patre: bod v chate (C v obyvaku)
    // nesmi zabirat ani blokovat nikdo z patra nad nim. Kdo je vys nez 1,2 m nad bodem, musi pod sebou mit volno
    // (zadnou podlahu / strop) skoro az k urovni bodu - stul, schod nebo bedna v bode jsou nizko, patro ne.
    public bool InCaptureArea(Vector3 feet)
    {
        Vector3 local = Quaternion.Euler(0f, -pointYaw.Value, 0f) * (feet - pointPosition.Value);
        if (Mathf.Abs(local.x) > pointSize.Value.x * 0.5f || Mathf.Abs(local.z) > pointSize.Value.y * 0.5f) return false;
        if (local.y < -1.5f || local.y > 4f) return false;
        if (local.y <= 1.2f) return true;

        float baseY = pointPosition.Value.y;
        var hits = Physics.RaycastAll(feet + Vector3.up * 0.1f, Vector3.down, local.y - 0.4f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;   // hraci, vozidla
            if (hit.point.y > baseY + 1.2f) return false;   // stoji na podlaze vyssiho patra
        }
        return true;
    }

    static void SetSeconds(NetworkVariable<int> variable, float seconds)
    {
        int shown = Mathf.Max(0, Mathf.CeilToInt(seconds));
        if (variable.Value != shown) variable.Value = shown;
    }

    // Filmova scena po dobyti bodu (sklopna podlaha s visici postavickou, FoldingFloor s unlockAfter = dalsi bod):
    // vsichni zmrazeni a nesmrtelni, dokud bezi, bod se odemyka o jeji delku dele; po ni vsichni zpet na spawny.
    float cutsceneTeleportAt = -1f;

    float ServerStartCutscene(int nextPoint)
    {
        float film = 0f;
        foreach (var floor in FindObjectsByType<FoldingFloor>())
            if (floor.unlockAfter == nextPoint && floor.HasCutscene)
                film = Mathf.Max(film, floor.CutsceneSeconds);
        if (film <= 0f) return 0f;
        float hold = film + 0.4f;
        foreach (var client in PlayerSlots())
        {
            var player = client.PlayerObject;
            if (player == null) continue;
            var movement = player.GetComponent<FirstPersonController>();
            var health = player.GetComponent<Health>();
            if (movement != null) movement.ServerHold(hold);
            if (health != null) health.ServerInvulnerable(hold);
        }
        cutsceneTeleportAt = Time.time + hold;
        return hold;
    }

    void ServerCutsceneTick()
    {
        if (cutsceneTeleportAt < 0f || Time.time < cutsceneTeleportAt) return;
        cutsceneTeleportAt = -1f;
        ResetPlayersClientRpc(customSpawns, customSpawn[0], customSpawn[1]);   // vsichni na spawny dalsiho bodu
        ServerHoldUntilUnlock();
    }

    void ServerPointCaptured()
    {
        int team = attackTeam.Value;
        PointWonClientRpc(team, pointPosition.Value, pointIndex.Value);
        if (team == 0) team0Score.Value++;
        else team1Score.Value++;

        int next = pointIndex.Value + 1;
        if (next >= CapturePointCount)
        {
            ServerEndRound(CapturePointCount, 0f);
            return;
        }

        // Druhe kolo dvojice: kdyz uz utocnici maji vic bodu nez souper, je rozhodnuto. Samostatne kolo: bod A = vyhra.
        if ((roundKind.Value == KindSecond && Score(team) > Score(1 - team)) || roundKind.Value == KindSolo)
        {
            ServerEndRound(next, 0f);
            return;
        }

        // Dalsi bod: cas navic (za A a B plny, za dalsi mensi), oba tymy se presunou, bod se chvili odemyka.
        timer += pointIndex.Value < 2 ? BonusSeconds : LateBonusSeconds;
        if (roundPhase.Value == RoundOvertime) roundPhase.Value = RoundAttack;
        overtimeEmpty = 0f;
        float film = ServerStartCutscene(next);
        ServerApplyPoint(next, UnlockNextSeconds + film);
        // vsichni hned na spawny dalsiho bodu a cekaji tam, nez se bod odemkne (s filmem az po nem - ServerCutsceneTick)
        if (film <= 0f)
        {
            ResetPlayersClientRpc(customSpawns, customSpawn[0], customSpawn[1]);
            ServerHoldUntilUnlock();
        }
    }

    // Do odemceni bodu: hraci se spawnem ve stodole cekaji za zavrenymi dvermi (SpawnBarn), ostatni stoji na spawnu.
    void ServerHoldUntilUnlock()
    {
        float seconds = Mathf.Max(0f, lockTimer);
        if (seconds <= 0f) return;
        foreach (var client in PlayerSlots())
        {
            var player = client.PlayerObject;
            if (player == null) continue;
            var team = player.GetComponent<PlayerTeam>();
            var movement = player.GetComponent<FirstPersonController>();
            if (team == null || movement == null) continue;
            Vector3 spawn = customSpawn[Mathf.Clamp(team.teamId.Value, 0, 1)];
            if (SpawnBarn.AtSpawn(spawn) == null) movement.ServerHold(seconds);
        }
    }

    void ServerEndRound(int pointsTaken, float progressOnNext)
    {
        roundPhase.Value = RoundDone;
        pointState.Value = StateLocked;

        int team = attackTeam.Value, other = 1 - team;
        bool all = pointsTaken >= CapturePointCount;
        // casova banka: zbyly cas utoku, jen kdyz tym dobyl vsechno
        timeBank[team] = all ? Mathf.Max(0f, timer) : 0f;
        string bankText = all ? $"  ·  v bance {Clock(timeBank[team])}" : "";

        if (roundKind.Value == KindFirst)
        {
            firstPoints.Value = pointsTaken;
            firstProgress.Value = all ? 0f : progressOnNext;
            firstTime.Value = timeBank[team];
            RoundOverClientRpc($"KONEC {round.Value}. KOLA",
                $"tým {team}: {PointsWord(pointsTaken)}{bankText}  ·  výměna stran", team);
            ServerQueueRound(other, KindSecond, round.Value == 1 ? AttackSeconds : timeBank[other]);
            return;
        }

        if (roundKind.Value == KindSolo)
        {
            bool won = pointsTaken > 0 || progressOnNext >= SoloGoal - 0.001f;
            EndMatch(won ? team : -1);
            return;
        }

        // Vyhodnoceni dvojice kol.
        int scoreAttack = Score(team), scoreOther = Score(other);
        if (scoreAttack != scoreOther) { EndMatch(scoreAttack > scoreOther ? team : other); return; }

        if (!all)
        {
            // oba skoncili na stejnem bode: rozhoduje postup na nem
            if (Mathf.Abs(progressOnNext - firstProgress.Value) > 0.01f) EndMatch(progressOnNext > firstProgress.Value ? team : other);
            else EndMatch(-1);
            return;
        }

        // oba dobyli vsechno: kola navic s casovou bankou
        bool bankOther = timeBank[other] >= MinBankSeconds, bankAttack = timeBank[team] >= MinBankSeconds;
        if (bankOther && bankAttack)
        {
            RoundOverClientRpc("NEROZHODNĚ - KOLA NAVÍC",
                $"oba týmy dobyly vše  ·  útočí se jen s časem z banky ({Clock(timeBank[0])} : {Clock(timeBank[1])})", -1);
            ServerQueueRound(other, KindFirst, timeBank[other]);
        }
        else if (bankOther || bankAttack)
        {
            int solo = bankOther ? other : team;
            RoundOverClientRpc("NEROZHODNĚ - ROZSTŘEL",
                $"jen tým {solo} má v bance aspoň {Mathf.RoundToInt(MinBankSeconds)} s  ·  za {Clock(timeBank[solo])} musí dobýt třetinu bodu A", solo);
            ServerQueueRound(solo, KindSolo, timeBank[solo]);
        }
        else
            EndMatch(-1);
    }

    int Score(int team) => team == 0 ? team0Score.Value : team1Score.Value;

    static string Clock(float seconds)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    static string PointsWord(int points) =>
        $"{points} {(points == 1 ? "bod" : points >= 2 && points <= 4 ? "body" : "bodů")}";

    // Po mezihre se hraje dalsi kolo (vymena stran / kolo navic).
    void ServerQueueRound(int attacker, int kind, float attackSeconds)
    {
        nextAttacker = attacker;
        nextKind = kind;
        nextTime = attackSeconds;
        roundPhase.Value = RoundIntermission;
        phaseTimer = IntermissionSeconds;
        SetSeconds(secondsLeft, phaseTimer);
    }

    void ServerBeginNextRound()
    {
        round.Value++;
        roundKind.Value = nextKind;
        if (nextKind == KindSolo)
        {
            firstPoints.Value = 0;            // cil: tretina bodu A
            firstProgress.Value = SoloGoal;
            firstTime.Value = 0f;
        }
        cutsceneTeleportAt = -1f;
        attackTeam.Value = nextAttacker;
        ServerResetPickups();
        ServerResetBoiler();
        ServerResetTractor();

        // Vsichni zpet na spawny (nove role), zivoty dopoli.
        foreach (var client in PlayerSlots())
        {
            var health = client.PlayerObject != null ? client.PlayerObject.GetComponent<Health>() : null;
            if (health != null) health.ResetHealth();
        }

        ServerStartRound(nextTime);
        ResetPlayersClientRpc(customSpawns, customSpawn[0], customSpawn[1]);
    }

    // ---------------- klienti ----------------

    // Zabrani bodu slysi vsichni stejne hlasite, at jsou kdekoliv.
    [ClientRpc]
    void PointWonClientRpc(int team, Vector3 position, int index)
    {
        var listener = Camera.main != null ? Camera.main.transform.position : position;
        ProceduralSfx.Play(ProceduralSfx.CaptureWon, listener, 1f);
        Fx.Sparks(position + Vector3.up, UiKit.TeamColor(team));
        Fx.Sparks(position + Vector3.up * 2f, UiKit.TeamColor(team));
        CaptureUI.Headline($"BOD {PointNames[Mathf.Clamp(index, 0, PointNames.Length - 1)]} DOBYT",
            index + 1 < CapturePointCount ? $"další cíl: bod {PointNames[index + 1]}" : "všechny body dobyty", UiKit.TeamColor(team));
    }

    [ClientRpc]
    void RoundStartedClientRpc(int attackers)
    {
        var listener = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        ProceduralSfx.Play(ProceduralSfx.CaptureUnlock, listener, 1f);
        var localPlayer = NetworkManager != null && NetworkManager.LocalClient != null ? NetworkManager.LocalClient.PlayerObject : null;
        var localTeam = localPlayer != null ? localPlayer.GetComponent<PlayerTeam>() : null;
        if (localTeam != null && localTeam.teamId.Value != attackers)
            CaptureUI.Headline("BRAŇTE BOD", $"útočí tým {attackers}  ·  vy bráníte", UiKit.TeamColor(localTeam.teamId.Value));
        else
            CaptureUI.Headline("ÚTOK ZAČAL", $"útočí tým {attackers}", UiKit.TeamColor(attackers));
    }

    [ClientRpc]
    void RoundOverClientRpc(string title, string subtitle, int team)
    {
        CaptureUI.Headline(title, subtitle, team >= 0 ? UiKit.TeamColor(team) : new Color(1f, 0.82f, 0.15f), 5f);
    }

    // Pozde pripojeny hrac si mista oziveni vyzada.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSpawnsRpc()
    {
        SpawnsClientRpc(customSpawns, customSpawn[0], customSpawn[1]);
    }

    [ClientRpc]
    void SpawnsClientRpc(bool custom, Vector3 spawn0, Vector3 spawn1)
    {
        customSpawns = custom;
        customSpawn[0] = spawn0;
        customSpawn[1] = spawn1;
    }

    // ---------------- mista na mape ----------------

    // Bod A = CapturePoint_1 (velka chata), B = CapturePoint_2 (mala chata), C = CapturePoint_3 (plosina pred rozhlednou).
    // Velikost podle Scale (X, Z). Spawny: SpawnPoint_Utok_A/B/C (utocnici) a SpawnPoint_Obrana_A/B/C (obranci);
    // kdyz chybi, zakladny tymu.
    void ChoosePoints()
    {
        var base0 = GameObject.Find("SpawnPoint_Team0");
        var base1 = GameObject.Find("SpawnPoint_Team1");
        Vector3 a = base0 != null ? base0.transform.position : Vector3.zero;
        Vector3 b = base1 != null ? base1.transform.position : new Vector3(40f, 0f, 0f);

        // kolik bodu mapa ma (CapturePoint_1, _2... po sobe); zadny = 3 body mezi zakladnami
        int count = 0;
        while (count < MaxCapturePoints && GameObject.Find($"CapturePoint_{count + 1}") != null) count++;
        pointCount.Value = count > 0 ? count : 3;

        for (int i = 0; i < CapturePointCount; i++)
        {
            pointYaws[i] = 0f;
            pointSizes[i] = CaptureSize;
            points[i] = Vector3.Lerp(a, b, (i + 1f) / (CapturePointCount + 1f));

            var manual = GameObject.Find($"CapturePoint_{i + 1}");
            if (manual != null)
            {
                // Objekt muze viset nad zemi: obdelnik si sedne na prvni povrch pod nim.
                Vector3 placed = manual.transform.position;
                foreach (var hit in SortedHits(placed + Vector3.up * 0.5f, 40f))
                {
                    if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
                    placed = hit.point;
                    break;
                }
                points[i] = placed;
                pointYaws[i] = manual.transform.eulerAngles.y;
                Vector3 scale = manual.transform.lossyScale;
                if (scale.x > 1.01f || scale.z > 1.01f)
                    pointSizes[i] = new Vector2(Mathf.Clamp(scale.x, 2f, 60f), Mathf.Clamp(scale.z, 2f, 60f));
            }

            string name = PointNames[i];
            attackSpawns[i] = Marker($"SpawnPoint_Utok_{name}", a);
            defendSpawns[i] = Marker($"SpawnPoint_Obrana_{name}", b);
            Debug.Log($"[Utok] bod {name}: {points[i]}  utocnici: {attackSpawns[i]}  obranci: {defendSpawns[i]}");
        }
    }

    static Vector3 Marker(string name, Vector3 fallback)
    {
        var marker = GameObject.Find(name);
        return marker != null ? marker.transform.position : fallback;
    }

    static RaycastHit[] SortedHits(Vector3 from, float distance)
    {
        var hits = Physics.RaycastAll(from, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        return hits;
    }
}
