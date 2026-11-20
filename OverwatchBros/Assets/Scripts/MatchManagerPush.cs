using Unity.Netcode;
using UnityEngine;

// Herni rezim "Tlacenice" (jako Push v Overwatchi). Hraje se jen na mape Domasov (Tlacenice) - MapDomasovPush.
//
//  - Uprostred trasy (PushRoute) stoji obri Honza (robot, PushRobot) a pred sebou ma Toyotu Yaris.
//  - Robot tlaci Yaris pro tym, ktery je u nej (PushRadius) sam. Oba tymy = sporny, stoji. Nikdo = stoji (necouva).
//  - Kdyz Yaris prevezme druhy tym, robot ho nejdriv obejde na druhou stranu (PushCrossing) a az pak tlaci opacne.
//  - Tym 0 tlaci k s = Length (zakladna tymu 1), tym 1 k s = 0. Checkpointy na strane soupere: poprve dotlaceno
//    = bonusovy cas a spawn tymu se posune dopredu.
//  - Dotlaceno na konec trasy = okamzita vyhra. Jinak po case vyhrava, kdo dotlacil nejdal od stredu.
//    Prodlouzeni: cas vyprsel a prohravajici tym stoji u robota - hraje se dal, dokud neodejde.
// Server je autorita: poloha Yarisu na trase (s), strana robota, prechazeni, postup a checkpointy jsou NetworkVariable.
// Klienti si polohu mezi aktualizacemi (5x za s) dopocitavaji podle rychlosti (PushRobot).
public partial class MatchManager
{
    public const int ModePush = 2;
    public bool IsPushMode => gameMode.Value == ModePush;
    public bool IsDeathmatchMode => gameMode.Value == ModeDeathmatch;

    // stav robota
    public const int PushLocked = 0, PushIdle = 1, PushPushing = 2, PushContested = 3, PushCrossing = 4;

    public const float PushSetupSeconds = 15f;
    public const float PushMatchSeconds = 480f;
    public const float PushCheckpointBonus = 60f;
    public const float PushRadius = 5f;            // kdo je bliz (k obrovi nebo Yarisu), je "u robota"
    const float PushHeight = 3f;
    // (rozestup obra od Yarisu: PushRoute.robotOffset / sideOffset - podle velikosti auta, u vsech stejne ze sceny)
    public const float PushWalkSpeed = 2.5f;       // obr bez tlaceni (obchazeni Yarisu)
    const float PushOvertimeGrace = 1.5f;
    static readonly float[] PushSpeeds = { 0f, 0.9f, 1.05f, 1.2f };   // podle poctu tlacicich (strop 3+)

    public NetworkVariable<float> pushCar = new NetworkVariable<float>(-1f);   // s Yarisu (-1 = stred trasy)
    public NetworkVariable<float> pushRate = new NetworkVariable<float>(0f);   // rychlost tlaceni (+ k zakladne tymu 1)
    public NetworkVariable<int> pushSide = new NetworkVariable<int>(0);        // +1 tlaci tym 0, -1 tlaci tym 1, 0 stoji vedle (start)
    public NetworkVariable<int> pushCrossFrom = new NetworkVariable<int>(0);
    public NetworkVariable<double> pushCrossStart = new NetworkVariable<double>(-1000.0);
    public NetworkVariable<int> pushState = new NetworkVariable<int>(PushLocked);
    public NetworkVariable<int> pushers = new NetworkVariable<int>(0);         // u robota: tym 0 + 16 * tym 1
    public NetworkVariable<float> pushBest0 = new NetworkVariable<float>(0f);  // nejdal za stred (m)
    public NetworkVariable<float> pushBest1 = new NetworkVariable<float>(0f);
    public NetworkVariable<int> pushCheckpoints0 = new NetworkVariable<int>(0);
    public NetworkVariable<int> pushCheckpoints1 = new NetworkVariable<int>(0);

    // Kde obr prave stoji (server - boti).
    public static Vector3 PushRobotPosition { get; private set; }

    // Cil botu: v tlacenici robot, jinak bod.
    public Vector3 ObjectivePosition => IsPushMode ? PushRobotPosition : pointPosition.Value;

    public int PushersOf(int team) => team == 0 ? pushers.Value & 15 : (pushers.Value >> 4) & 15;
    public float PushBest(int team) => team == 0 ? pushBest0.Value : pushBest1.Value;
    public int PushCheckpointsOf(int team) => team == 0 ? pushCheckpoints0.Value : pushCheckpoints1.Value;
    public static int PushSideOf(int team) => team == 0 ? 1 : -1;

    // Tym, ktery robota vlastni (tlaci nebo k nemu prave prechazi). -1 = nikdo (start).
    public int PushOwner => pushSide.Value > 0 ? 0 : pushSide.Value < 0 ? 1 : -1;

    // server
    float serverCar = -1f;
    float pushTimer, pushLock, pushOvertimeEmpty, nextCarSync;

    // ---------------- geometrie robota (server i klienti stejne) ----------------

    static float CrossLength(PushRoute route, int from, int to) =>
        (from != 0 ? route.sideOffset : 0f) + Mathf.Abs(to - from) * route.robotOffset + route.sideOffset;

    public static float CrossDuration(PushRoute route, int from, int to) => CrossLength(route, from, to) / PushWalkSpeed;

    // Jak daleko je obchazeni (0..1; >= 1 = neobchazi).
    public float PushCrossT(PushRoute route, double serverTime)
    {
        if (pushCrossStart.Value < 0.0 || route == null) return 1f;
        float duration = CrossDuration(route, pushCrossFrom.Value, pushSide.Value);
        return duration <= 0f ? 1f : (float)((serverTime - pushCrossStart.Value) / duration);
    }

    // Misto obra vuci Yarisu: podel trasy (along) a do strany (lat).
    static Vector2 RobotLocal(PushRoute route, int side) =>
        side == 0 ? new Vector2(0f, route.sideOffset) : new Vector2(-side * route.robotOffset, 0f);

    // Poloha a smer obra. moving = jde (obchazi Yaris); tlaceni urcuje pushRate.
    public static void PushRobotPose(PushRoute route, float car, int side, int crossFrom, float crossT,
        out Vector3 position, out Vector3 facing, out bool crossing)
    {
        crossing = crossT < 1f && crossFrom != side;
        Vector2 local;
        Vector2 dir;
        if (!crossing)
        {
            local = RobotLocal(route, side);
            dir = side == 0 ? new Vector2(0f, -1f) : new Vector2(side, 0f);
        }
        else
        {
            // A (start) -> B (vedle Yarisu) -> C (vedle Yarisu na druhe strane) -> D (za Yarisem)
            Vector2 a = RobotLocal(route, crossFrom), d = RobotLocal(route, side);
            Vector2 b = new Vector2(a.x, route.sideOffset), c = new Vector2(d.x, route.sideOffset);
            float ab = Vector2.Distance(a, b), bc = Vector2.Distance(b, c), cd = Vector2.Distance(c, d);
            float walked = Mathf.Clamp01(crossT) * (ab + bc + cd);
            if (walked < ab && ab > 0f) { local = Vector2.Lerp(a, b, walked / ab); dir = b - a; }
            else if (walked < ab + bc && bc > 0f) { local = Vector2.Lerp(b, c, (walked - ab) / bc); dir = c - b; }
            else { local = cd > 0f ? Vector2.Lerp(c, d, Mathf.Clamp01((walked - ab - bc) / cd)) : d; dir = cd > 0f ? d - c : c - b; }
        }

        float s = car + local.x;
        Vector3 forward = route.Forward(s), right = route.Right(s);
        position = route.Point(s) + right * local.y;
        Vector3 world = forward * dir.x + right * dir.y;
        facing = world.sqrMagnitude > 1e-4f ? world.normalized : forward;
    }

    // ---------------- prubeh (server) ----------------

    // Vola ResetRound (vzdy): robot zpet na stred, vse vynulovat.
    void ServerResetPush()
    {
        if (!IsServer) return;
        var route = PushRoute.Active;
        serverCar = route != null ? route.Center : -1f;
        pushCar.Value = serverCar;
        pushRate.Value = 0f;
        pushSide.Value = 0;
        pushCrossFrom.Value = 0;
        pushCrossStart.Value = -1000.0;
        pushState.Value = PushLocked;
        pushers.Value = 0;
        pushBest0.Value = 0f;
        pushBest1.Value = 0f;
        pushCheckpoints0.Value = 0;
        pushCheckpoints1.Value = 0;
        pushOvertimeEmpty = 0f;
        if (route != null) PushRobotPosition = route.Point(route.Center) + route.Right(route.Center) * route.sideOffset;
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

        // Oba tymy chvili stoji ve spawnu (robot se probouzi) - po presunu na spawn, proto s malym zpozdenim.
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
        float length = route.Length, center = length * 0.5f;
        if (serverCar < 0f) serverCar = center;
        float dt = Time.deltaTime;
        double now = NetworkManager.ServerTime.Time;

        PushRobotPose(route, serverCar, pushSide.Value, pushCrossFrom.Value, PushCrossT(route, now), out var robot, out _, out bool crossing);
        PushRobotPosition = robot;

        // priprava: robot spi
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

        CountAtRobot(robot, route.Point(serverCar), out int n0, out int n1);
        int packed = Mathf.Min(n0, 15) + Mathf.Min(n1, 15) * 16;
        if (pushers.Value != packed) pushers.Value = packed;

        int state;
        float rate = 0f;
        if (crossing) state = PushCrossing;
        else if (n0 > 0 && n1 > 0) state = PushContested;
        else if (n0 > 0 || n1 > 0)
        {
            int team = n0 > 0 ? 0 : 1;
            int want = PushSideOf(team);
            if (pushSide.Value != want)
            {
                // prevzeti: robot nejdriv obejde Yaris na druhou stranu
                pushCrossFrom.Value = pushSide.Value;
                pushSide.Value = want;
                pushCrossStart.Value = now;
                state = PushCrossing;
            }
            else
            {
                state = PushPushing;
                rate = want * PushSpeeds[Mathf.Min(Mathf.Max(n0, n1), PushSpeeds.Length - 1)];
                serverCar = Mathf.Clamp(serverCar + rate * dt, 0f, length);
            }
        }
        else state = PushIdle;

        if (pushState.Value != state) pushState.Value = state;
        if (!Mathf.Approximately(pushRate.Value, rate)) pushRate.Value = rate;
        // poloha Yarisu 5x za sekundu (klienti mezi tim dopocitavaji podle pushRate); po zastaveni hned
        if ((Time.time >= nextCarSync || rate == 0f) && !Mathf.Approximately(pushCar.Value, serverCar))
        {
            nextCarSync = Time.time + 0.2f;
            pushCar.Value = serverCar;
        }

        // postup (nejdal za stred) a skore = dotlacene metry
        float reach0 = serverCar - center, reach1 = center - serverCar;
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

        // dotlaceno az na konec = vyhra
        if (serverCar >= length - 0.01f || serverCar <= 0.01f)
        {
            int winner = serverCar >= length - 0.01f ? 0 : 1;
            pushCar.Value = serverCar;
            ServerEndPush(winner, $"TÝM {winner} DOTLAČIL YARIS", $"až do základny týmu {1 - winner}");
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
            ServerEndPush(-1, "REMÍZA", "oba týmy dotlačily stejně daleko");
        else
            ServerEndPush(leader, $"TÝM {leader} VYHRÁL", $"dotlačil dál: {PushBest(leader):0} m proti {PushBest(1 - leader):0} m");
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
            PushCheckpointClientRpc(team, reached.Value, route.Point(serverCar));
        }
    }

    void ServerEndPush(int winner, string title, string subtitle)
    {
        roundPhase.Value = RoundDone;
        pushRate.Value = 0f;
        pushState.Value = PushIdle;
        RoundOverClientRpc(title, subtitle, winner);
        EndMatch(winner);
    }

    // Kdo stoji u robota: do PushRadius od usecky obr - Yaris (vodorovne), vyskove do PushHeight.
    void CountAtRobot(Vector3 robot, Vector3 car, out int team0, out int team1)
    {
        team0 = 0;
        team1 = 0;
        foreach (var client in PlayerSlots())
        {
            var player = client.PlayerObject;
            if (player == null) continue;

            var health = player.GetComponent<Health>();
            var hero = player.GetComponent<PlayerHero>();
            var team = player.GetComponent<PlayerTeam>();
            if (health == null || team == null || health.currentHealth.Value <= 0f || (hero != null && hero.IsJoining)) continue;

            Vector3 feet = player.transform.position;
            Vector3 a = new Vector3(robot.x, 0f, robot.z), b = new Vector3(car.x, 0f, car.z), p = new Vector3(feet.x, 0f, feet.z);
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            if (Vector3.Distance(p, a + ab * t) > PushRadius) continue;
            float groundY = Mathf.Lerp(robot.y, car.y, t);
            if (feet.y < groundY - 1.5f || feet.y > groundY + PushHeight) continue;

            if (team.teamId.Value == 0) team0++;
            else team1++;
        }
    }

    // ---------------- hlaseni (klienti) ----------------

    [ClientRpc]
    void PushStartedClientRpc()
    {
        var listener = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        ProceduralSfx.Play(ProceduralSfx.CaptureUnlock, listener, 1f);
        CaptureUI.Headline("ROBOT SE PROBUDIL", "stůj u obřího Honzy - tlačí Yaris k základně soupeře", UiKit.Accent);
        if (PushRobot.Instance != null) PushRobot.Instance.Say();
    }

    [ClientRpc]
    void PushCheckpointClientRpc(int team, int index, Vector3 position)
    {
        Fx.Sparks(position + Vector3.up, UiKit.TeamColor(team));
        Fx.Sparks(position + Vector3.up * 2f, UiKit.TeamColor(team));
        if (PushRobot.Instance != null)
        {
            PushRobot.Instance.Horn();
            PushRobot.Instance.Say();
        }
        CaptureUI.Headline($"CHECKPOINT {index}  ·  TÝM {team}",
            $"+{PushCheckpointBonus:0} s  ·  spawn týmu {team} se posunul dopředu", UiKit.TeamColor(team));
    }

    // ---------------- texty pro HUD ----------------

    public static string PushObjectiveText(MatchManager match, int myTeam)
    {
        if (myTeam < 0) return "";
        int state = match.pushState.Value;
        if (state == PushLocked || match.roundPhase.Value == RoundOvertime) return "";
        int owner = match.PushOwner;
        if (state == PushContested)
            return "ÚKOL: vyžeň soupeře od robota  ·  dokud jsou u něj oba týmy, stojí";
        if (owner == myTeam)
            return $"ÚKOL: tlač Yaris k základně týmu {1 - myTeam}  ·  stůj u obřího Honzy, víc vás = rychleji";
        if (owner < 0)
            return "ÚKOL: dojdi k obřímu Honzovi uprostřed a tlač Yaris k základně soupeře";
        return "ÚKOL: zastav robota  ·  stoupni si k němu - až tam soupeř nebude, převezmeš ho";
    }
}
