using Unity.Netcode;
using UnityEngine;

// Herni rezim "Tlacenice" (jako Push v Overwatchi 2). Hraje se jen na mape Domasov (Tlacenice) - MapDomasovPush.
//
//  - Trasa (PushRoute): s = 0 u zakladny tymu 0 (modri), s = Length u zakladny tymu 1 (cerveni).
//  - Kazdy tym ma svuj Yaris. Na zacatku stoji oba uprostred trasy a mezi nimi obri Honza (robot, PushRobot).
//    Yaris tymu 0 je na strane tymu 1 a tym 0 ho tlaci k zakladne tymu 1 (s roste); Yaris tymu 1 opacne.
//  - Honza tlaci Yaris tymu, ktery je u nej sam (PushRadius). Oba tymy = sporny, stoji. Nikdo = stoji (Yarisy necouvaji).
//  - Kdyz Honzu prevezme druhy tym, Honza dobehne po trase k jeho Yarisu (PushWalkSpeed) a teprve pak tlaci.
//    Yarisy zustavaji, kde je kdo nechal - postup tymu = jak daleko je jeho Yaris od startu.
//  - Checkpointy (PushRoute.checkpoints, metry od startu Yarisu): poprve dotlaceno = bonusovy cas a spawn dopredu.
//  - Yaris dotlaceny az na konec trasy = okamzita vyhra. Jinak po case vyhrava, kdo dotlacil svuj Yaris dal.
//    Prodlouzeni: cas vyprsel a prohravajici tym stoji u Honzy - hraje se dal, dokud neodejde.
// Server je autorita: poloha obou Yarisu a Honzy na trase (s), komu Honza patri, stav, postup a checkpointy jsou
// NetworkVariable. Polohu Honzy posila server 5x za s i s rychlosti - klienti mezi tim dopocitavaji (PushRobot).
public partial class MatchManager
{
    public const int ModePush = 2;
    public bool IsPushMode => gameMode.Value == ModePush;
    public bool IsDeathmatchMode => gameMode.Value == ModeDeathmatch;

    // stav Honzy
    public const int PushLocked = 0, PushIdle = 1, PushPushing = 2, PushContested = 3, PushWalking = 4;

    public const float PushSetupSeconds = 15f;
    public const float PushMatchSeconds = 480f;
    public const float PushCheckpointBonus = 60f;
    public const float PushRadius = 8f;            // kdo je bliz k Honzovi (nebo k Yarisu, ktery prave tlaci), je "u nej"
    const float PushHeight = 3f;
    public const float PushWalkSpeed = 3f;         // Honza bezi k Yarisu druheho tymu
    public const float PushHealPerSecond = 10f;    // tym, ktery ma Honzu sam pro sebe, se u nej leci (jako u nakladu v OW)
    const float PushHealInterval = 0.5f;
    float nextPushHeal;
    const float PushOvertimeGrace = 1.5f;
    static readonly float[] PushSpeeds = { 0f, 0.9f, 1.05f, 1.2f };   // podle poctu tlacicich (strop 3+)
    // (rozestup Honzy od stredu Yarisu: PushRoute.robotOffset - podle velikosti auta, u vsech stejne ze sceny)

    public NetworkVariable<float> pushCar0 = new NetworkVariable<float>(-1f);   // s Yarisu tymu 0 (-1 = start)
    public NetworkVariable<float> pushCar1 = new NetworkVariable<float>(-1f);   // s Yarisu tymu 1
    public NetworkVariable<float> pushRobot = new NetworkVariable<float>(-1f);  // s Honzy
    public NetworkVariable<float> pushRobotRate = new NetworkVariable<float>(0f); // jak rychle se Honza prave hybe (+ k zakladne tymu 1)
    public NetworkVariable<int> pushOwner = new NetworkVariable<int>(-1);       // komu Honza patri (-1 = nikomu, start)
    public NetworkVariable<int> pushState = new NetworkVariable<int>(PushLocked);
    public NetworkVariable<int> pushers = new NetworkVariable<int>(0);          // u Honzy: tym 0 + 16 * tym 1
    public NetworkVariable<float> pushBest0 = new NetworkVariable<float>(0f);   // jak daleko od startu je Yaris tymu (m)
    public NetworkVariable<float> pushBest1 = new NetworkVariable<float>(0f);
    public NetworkVariable<int> pushCheckpoints0 = new NetworkVariable<int>(0);
    public NetworkVariable<int> pushCheckpoints1 = new NetworkVariable<int>(0);

    // Kde Honza prave stoji (server - boti).
    public static Vector3 PushRobotPosition { get; private set; }

    // Cil botu: v tlacenici Honza, jinak bod.
    public Vector3 ObjectivePosition => IsPushMode ? PushRobotPosition : pointPosition.Value;

    public int PushersOf(int team) => team == 0 ? pushers.Value & 15 : (pushers.Value >> 4) & 15;
    public float PushBest(int team) => team == 0 ? pushBest0.Value : pushBest1.Value;
    public int PushCheckpointsOf(int team) => team == 0 ? pushCheckpoints0.Value : pushCheckpoints1.Value;
    public int PushOwner => pushOwner.Value;

    // Kam tym tlaci svuj Yaris (+1 k zakladne tymu 1, -1 k zakladne tymu 0).
    public static int PushDirection(int team) => team == 0 ? 1 : -1;

    // Start Yarisu tymu a kde stoji Honza, kdyz ho tlaci.
    public static float PushCarStart(PushRoute route, int team) => route.Center + PushDirection(team) * route.robotOffset;
    public static float PushBehind(PushRoute route, int team, float car) => car - PushDirection(team) * route.robotOffset;

    // Polohy ze sitovych promennych (-1 = start).
    public float PushCarOf(PushRoute route, int team)
    {
        float s = team == 0 ? pushCar0.Value : pushCar1.Value;
        return s < 0f ? PushCarStart(route, team) : s;
    }

    public float PushRobotOf(PushRoute route) => pushRobot.Value < 0f ? route.Center : pushRobot.Value;

    // server
    readonly float[] serverCar = { -1f, -1f };
    float serverRobot = -1f;
    float pushTimer, pushLock, pushOvertimeEmpty, nextRobotSync;

    // ---------------- prubeh (server) ----------------

    // Vola ResetRound (vzdy): Honza a Yarisy zpet na start, vse vynulovat.
    void ServerResetPush()
    {
        if (!IsServer) return;
        var route = PushRoute.Active;
        serverCar[0] = route != null ? PushCarStart(route, 0) : -1f;
        serverCar[1] = route != null ? PushCarStart(route, 1) : -1f;
        serverRobot = route != null ? route.Center : -1f;
        pushCar0.Value = serverCar[0];
        pushCar1.Value = serverCar[1];
        pushRobot.Value = serverRobot;
        pushRobotRate.Value = 0f;
        pushOwner.Value = -1;
        pushState.Value = PushLocked;
        pushers.Value = 0;
        pushBest0.Value = 0f;
        pushBest1.Value = 0f;
        pushCheckpoints0.Value = 0;
        pushCheckpoints1.Value = 0;
        pushOvertimeEmpty = 0f;
        if (route != null) PushRobotPosition = route.Point(route.Center);
    }

    // Vola ResetRound pri startu zapasu v tlacenici.
    void ServerBeginPushMatch()
    {
        ServerResetPush();
        var route = PushRoute.Active;
        pushTimer = PushMatchSeconds;
        pushLock = PushSetupSeconds;
        round.Value = 1;
        roundKind.Value = KindFirst;
        roundPhase.Value = RoundSetup;
        secondsLeft.Value = Mathf.CeilToInt(pushLock);
        team0Score.Value = 0;
        team1Score.Value = 0;

        customSpawns = route != null;
        for (int team = 0; team < 2; team++)
            customSpawn[team] = PushSpawn(route, team, 0);

        // Oba tymy chvili stoji ve spawnu (Honza se probouzi) - po presunu na spawn, proto s malym zpozdenim.
        StopCoroutine(nameof(HoldPushTeamsSoon));
        StartCoroutine(nameof(HoldPushTeamsSoon));
    }

    System.Collections.IEnumerator HoldPushTeamsSoon()
    {
        yield return new WaitForSeconds(0.3f);
        if (!IsPushMode || roundPhase.Value != RoundSetup) yield break;
        foreach (var client in PlayerSlots())
        {
            var movement = client.PlayerObject != null ? client.PlayerObject.GetComponent<FirstPersonController>() : null;
            if (movement != null) movement.ServerHold(Mathf.Max(0f, pushLock));
        }
    }

    Vector3 PushSpawn(PushRoute route, int team, int level)
    {
        if (route != null && route.TryGetSpawn(team, level, out var position)) return position;
        var marker = GameObject.Find($"SpawnPoint_Team{team}");
        return marker != null ? marker.transform.position : Vector3.zero;
    }

    void ServerPushTick()
    {
        if (!IsPushMode || IsLobby || IsOver) return;
        var route = PushRoute.Active;
        if (route == null) return;
        float length = route.Length;
        if (serverRobot < 0f) serverRobot = route.Center;
        for (int t = 0; t < 2; t++) if (serverCar[t] < 0f) serverCar[t] = PushCarStart(route, t);
        float dt = Time.deltaTime;

        Vector3 robot = route.Point(serverRobot);
        PushRobotPosition = robot;

        // priprava: Honza spi
        if (pushLock > 0f)
        {
            pushLock -= dt;
            SetSeconds(secondsLeft, Mathf.Max(0f, pushLock));
            if (pushLock > 0f) return;
            roundPhase.Value = RoundAttack;
            pushState.Value = PushIdle;
            secondsLeft.Value = Mathf.CeilToInt(pushTimer);
            PushStartedClientRpc();
        }
        if (roundPhase.Value == RoundDone) return;

        // kdo je u Honzy (a u Yarisu, ktery prave tlaci)
        int owner = pushOwner.Value;
        bool attached = owner >= 0 && Mathf.Abs(serverRobot - PushBehind(route, owner, serverCar[owner])) < 0.05f;
        Vector3 car = attached ? route.Point(serverCar[owner]) : robot;
        CountAtRobot(robot, car, out int n0, out int n1);
        int packed = Mathf.Min(n0, 15) + Mathf.Min(n1, 15) * 16;
        if (pushers.Value != packed) pushers.Value = packed;

        int state;
        float rate = 0f;
        if (n0 > 0 && n1 > 0) state = PushContested;
        else if (n0 > 0 || n1 > 0)
        {
            int team = n0 > 0 ? 0 : 1;
            if (owner != team)
            {
                owner = team;
                pushOwner.Value = team;
            }
            int dir = PushDirection(team);
            float behind = PushBehind(route, team, serverCar[team]);
            if (Mathf.Abs(serverRobot - behind) > 0.05f)
            {
                // Honza bezi po trase k Yarisu tymu, ktery ho prevzal
                state = PushWalking;
                float step = PushWalkSpeed * dt;
                rate = Mathf.Sign(behind - serverRobot) * PushWalkSpeed;
                serverRobot = Mathf.Abs(behind - serverRobot) <= step ? behind : serverRobot + Mathf.Sign(behind - serverRobot) * step;
            }
            else
            {
                state = PushPushing;
                float speed = PushSpeeds[Mathf.Min(team == 0 ? n0 : n1, PushSpeeds.Length - 1)];
                rate = dir * speed;
                serverCar[team] = Mathf.Clamp(serverCar[team] + rate * dt, 0f, length);
                serverRobot = PushBehind(route, team, serverCar[team]);
                SyncCar(team);
            }
        }
        else state = PushIdle;

        if (pushState.Value != state) pushState.Value = state;

        // leceni u Honzy: jen tym, ktery u nej je sam (ne pri sporu)
        if ((state == PushPushing || state == PushWalking) && Time.time >= nextPushHeal)
        {
            nextPushHeal = Time.time + PushHealInterval;
            ServerPushHeal(robot, car, owner, PushHealPerSecond * PushHealInterval);
        }
        if (!Mathf.Approximately(pushRobotRate.Value, rate))
        {
            pushRobotRate.Value = rate;
            pushRobot.Value = serverRobot;
            for (int t = 0; t < 2; t++) ForceSyncCar(t);
            nextRobotSync = Time.time + 0.2f;
        }
        // poloha Honzy 5x za sekundu (klienti mezi tim dopocitavaji podle pushRobotRate)
        if (Time.time >= nextRobotSync && !Mathf.Approximately(pushRobot.Value, serverRobot))
        {
            nextRobotSync = Time.time + 0.2f;
            pushRobot.Value = serverRobot;
        }

        // postup (jak daleko od startu je Yaris tymu) a skore = dotlacene metry
        float reach0 = serverCar[0] - PushCarStart(route, 0), reach1 = PushCarStart(route, 1) - serverCar[1];
        if (reach0 > pushBest0.Value + 0.05f)
        {
            pushBest0.Value = reach0;
            team0Score.Value = Mathf.FloorToInt(reach0);
        }
        if (reach1 > pushBest1.Value + 0.05f)
        {
            pushBest1.Value = reach1;
            team1Score.Value = Mathf.FloorToInt(reach1);
        }
        ServerPushCheckpoints(route, 0, reach0);
        ServerPushCheckpoints(route, 1, reach1);

        // Yaris dotlaceny az na konec = vyhra
        if (serverCar[0] >= length - 0.01f || serverCar[1] <= 0.01f)
        {
            int winner = serverCar[0] >= length - 0.01f ? 0 : 1;
            ForceSyncCar(winner);
            pushRobot.Value = serverRobot;
            ServerEndPush(winner, "Yaris dotlačen až do základny", $"{TeamName(0)} {PushBest(0):0} m : {TeamName(1)} {PushBest(1):0} m");
            return;
        }

        // cas a prodlouzeni
        pushTimer -= dt;
        SetSeconds(secondsLeft, Mathf.Max(0f, pushTimer));
        if (pushTimer > 0f) return;

        int leader = PushLeader();
        int loser = leader < 0 ? -1 : 1 - leader;
        bool losingAtRobot = loser < 0 ? n0 + n1 > 0 : (loser == 0 ? n0 : n1) > 0;
        if (losingAtRobot)
        {
            pushOvertimeEmpty = 0f;
            if (roundPhase.Value != RoundOvertime) roundPhase.Value = RoundOvertime;
            return;
        }
        pushOvertimeEmpty += dt;
        if (roundPhase.Value == RoundOvertime && pushOvertimeEmpty < PushOvertimeGrace) return;

        if (leader < 0)
            ServerEndPush(-1, "oba týmy dotlačily stejně daleko", $"{TeamName(0)} {PushBest(0):0} m : {TeamName(1)} {PushBest(1):0} m");
        else
            ServerEndPush(leader, "dotlačil Yaris dál", $"{TeamName(0)} {PushBest(0):0} m : {TeamName(1)} {PushBest(1):0} m");
    }

    // Poloha tlaceneho Yarisu do site 5x za s (klienti ho mezi tim dopocitavaji z polohy Honzy).
    void SyncCar(int team)
    {
        if (Time.time >= nextRobotSync) ForceSyncCar(team);
    }

    void ForceSyncCar(int team)
    {
        var variable = team == 0 ? pushCar0 : pushCar1;
        if (!Mathf.Approximately(variable.Value, serverCar[team])) variable.Value = serverCar[team];
    }

    // Kdo dotlacil dal (-1 = stejne).
    int PushLeader()
    {
        float diff = pushBest0.Value - pushBest1.Value;
        if (Mathf.Abs(diff) < 0.1f) return -1;
        return diff > 0f ? 0 : 1;
    }

    void ServerPushCheckpoints(PushRoute route, int team, float reach)
    {
        var reached = team == 0 ? pushCheckpoints0 : pushCheckpoints1;
        while (reached.Value < route.CheckpointCount && reach >= route.checkpoints[reached.Value])
        {
            reached.Value++;
            pushTimer += PushCheckpointBonus;
            customSpawns = true;
            customSpawn[team] = PushSpawn(route, team, reached.Value);
            SpawnsClientRpc(customSpawns, customSpawn[0], customSpawn[1]);
            PushCheckpointClientRpc(team, reached.Value, route.Point(serverCar[team]));
        }
    }

    void ServerEndPush(int winner, string title, string subtitle)
    {
        roundPhase.Value = RoundDone;
        pushRobotRate.Value = 0f;
        pushState.Value = PushIdle;
        PushOverClientRpc(winner, title, subtitle);
        EndMatch(winner);
    }

    // Kdo stoji u Honzy: do PushRadius od usecky Honza - tlaceny Yaris (vodorovne), vyskove do PushHeight.
    void CountAtRobot(Vector3 robot, Vector3 car, out int team0, out int team1)
    {
        team0 = 0;
        team1 = 0;
        foreach (var client in PlayerSlots())
        {
            var player = client.PlayerObject;
            if (player == null) continue;
            var team = player.GetComponent<PlayerTeam>();
            if (!AtRobot(player, robot, car)) continue;

            if (team.teamId.Value == 0) team0++;
            else team1++;
        }
    }

    // Leceni hracu tymu 'team', kteri jsou u Honzy (server).
    void ServerPushHeal(Vector3 robot, Vector3 car, int team, float amount)
    {
        if (team < 0) return;
        foreach (var client in PlayerSlots())
        {
            var player = client.PlayerObject;
            if (player == null) continue;
            var playerTeam = player.GetComponent<PlayerTeam>();
            if (playerTeam == null || playerTeam.teamId.Value != team || !AtRobot(player, robot, car)) continue;
            var health = player.GetComponent<Health>();
            if (health != null && health.currentHealth.Value < health.maxHealth) health.Heal(amount);
        }
    }

    // Je hrac (zivy, ve hre) u Honzy - do PushRadius od usecky Honza - tlaceny Yaris, vyskove do PushHeight (nebo na strese)?
    static bool AtRobot(NetworkObject player, Vector3 robot, Vector3 car)
    {
        {
            var health = player.GetComponent<Health>();
            var hero = player.GetComponent<PlayerHero>();
            var team = player.GetComponent<PlayerTeam>();
            if (health == null || team == null || health.currentHealth.Value <= 0f || (hero != null && hero.IsJoining)) return false;

            Vector3 feet = player.transform.position;
            Vector3 a = new Vector3(robot.x, 0f, robot.z), b = new Vector3(car.x, 0f, car.z), p = new Vector3(feet.x, 0f, feet.z);
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            if (Vector3.Distance(p, a + ab * t) > PushRadius) return false;
            float groundY = Mathf.Lerp(robot.y, car.y, t);
            // kdo stoji na strese Yarisu (nebo na Honzovi), je u nej taky - i kdyz je vys nez PushHeight
            bool onTop = feet.y > groundY + PushHeight && feet.y < groundY + 8f
                && PushRobot.Instance != null && PushRobot.Instance.Occupies(feet, 0.3f);
            if (feet.y < groundY - 1.5f || (feet.y > groundY + PushHeight && !onTop)) return false;
            return true;
        }
    }

    // ---------------- hlaseni (klienti) ----------------

    // Konec Tlacenice: kazdemu velke VITEZSTVI / PORAZKA (podle jeho tymu), kdo vyhral a proc.
    [ClientRpc]
    void PushOverClientRpc(int winner, string title, string subtitle)
    {
        int local = LocalTeam();
        CaptureUI.Headline(Verdict(winner, local), $"{WinnerLine(winner, local)}  ·  {title}  ·  {subtitle}", VerdictColor(winner, local), 8f);
    }

    [ClientRpc]
    void PushStartedClientRpc()
    {
        var listener = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        ProceduralSfx.Play(ProceduralSfx.CaptureUnlock, listener, 1f);
        CaptureUI.Headline("HONZA SE PROBUDIL", "stůj u obřího Honzy - tlačí Yaris tvého týmu k základně soupeře", UiKit.Accent);
        if (PushRobot.Instance != null) PushRobot.Instance.Say();
    }

    [ClientRpc]
    void PushCheckpointClientRpc(int team, int index, Vector3 position)
    {
        Fx.Sparks(position + Vector3.up, UiKit.TeamColor(team));
        Fx.Sparks(position + Vector3.up * 2f, UiKit.TeamColor(team));
        if (PushRobot.Instance != null)
        {
            PushRobot.Instance.Horn(team);
            PushRobot.Instance.Say();
        }
        CaptureUI.Headline($"CHECKPOINT {index}  ·  {TeamName(team)}",
            $"+{PushCheckpointBonus:0} s  ·  {TeamName(team)} se oživuje blíž", UiKit.TeamColor(team));
    }

    // ---------------- texty pro HUD ----------------

    public static string PushObjectiveText(MatchManager match, int myTeam)
    {
        if (myTeam < 0) return "";
        int state = match.pushState.Value;
        if (state == PushLocked || match.roundPhase.Value == RoundOvertime) return "";
        int owner = match.PushOwner;
        if (state == PushContested)
            return "ÚKOL: vyžeň soupeře od Honzy  ·  dokud jsou u něj oba týmy, stojí";
        if (owner == myTeam)
            return state == PushWalking
                ? "ÚKOL: Honza běží k vašemu Yarisu  ·  doprovoď ho a kryj"
                : $"ÚKOL: tlač svůj Yaris k základně týmu {1 - myTeam}  ·  stůj u Honzy, víc vás = rychleji";
        if (owner < 0)
            return "ÚKOL: dojdi k obřímu Honzovi uprostřed - bude tlačit Yaris vašeho týmu";
        return "ÚKOL: převezmi Honzu  ·  stoupni si k němu - až tam soupeř nebude, poběží k vašemu Yarisu";
    }
}
