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
// Vyhodnoceni po obou kolech: vic zabranych bodu; pri rovnosti rychlejsi cas (oba body) nebo vetsi postup na dalsim bodu.
// Druhe kolo skonci hned, jakmile je o vitezi rozhodnuto.
public partial class MatchManager
{
    public const int ModeDeathmatch = 0;
    public const int ModeAttack = 1;

    public const int StateLocked = 0, StateFree = 1, StateTeam0 = 2, StateTeam1 = 3, StateContested = 4;

    // faze kola
    public const int RoundSetup = 0, RoundAttack = 1, RoundOvertime = 2, RoundIntermission = 3, RoundDone = 4;

    public const int CapturePointCount = 3;
    public static readonly string[] PointNames = { "A", "B", "C" };
    public static readonly Vector2 CaptureSize = new Vector2(10f, 7f);

    const float SetupSeconds = 20f;
    const float UnlockNextSeconds = 10f;
    const float AttackSeconds = 180f;
    const float BonusSeconds = 120f;
    const float IntermissionSeconds = 8f;
    const float DecayDelay = 2f;
    const float OvertimeGrace = 1.2f;

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

    // Vysledek prvniho kola (cil pro druhe): kolik bodu, postup na dalsim bodu a za jak dlouho.
    public NetworkVariable<int> firstPoints = new NetworkVariable<int>(-1);
    public NetworkVariable<float> firstProgress = new NetworkVariable<float>();
    public NetworkVariable<float> firstTime = new NetworkVariable<float>();

    public bool IsAttackMode => gameMode.Value == ModeAttack;
    public int DefendTeam => 1 - attackTeam.Value;

    // Mista oziveni (plati na vsech klientech; posila se i v RPC, aby dorazila vcas).
    bool customSpawns;
    readonly Vector3[] customSpawn = new Vector3[2];

    // server
    readonly Vector3[] points = new Vector3[CapturePointCount];
    readonly float[] pointYaws = new float[CapturePointCount];
    readonly Vector2[] pointSizes = new Vector2[CapturePointCount];
    readonly Vector3[] attackSpawns = new Vector3[CapturePointCount];
    readonly Vector3[] defendSpawns = new Vector3[CapturePointCount];
    float timer;          // cas utoku
    float lockTimer;      // priprava / odemykani bodu
    float phaseTimer;     // mezihra
    float timeUsed;       // jak dlouho utocnici utoci (bez pripravy)
    float lastAttackersOnPoint;
    float overtimeEmpty;

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
        }

        CapturePointView.Sync(this);
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
    static void ResetKillHeight()
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
        attackTeam.Value = 0;
        ServerStartRound();
    }

    void ServerStartRound()
    {
        timer = AttackSeconds;
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
            if (team != null && movement != null && team.teamId.Value == attackTeam.Value)
                movement.ServerHold(seconds);
        }
    }

    void ServerApplyPoint(int index, float lockSeconds)
    {
        pointIndex.Value = index;
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

        if (roundPhase.Value == RoundIntermission)
        {
            phaseTimer -= Time.deltaTime;
            SetSeconds(secondsLeft, phaseTimer);
            if (phaseTimer <= 0f) ServerBeginSecondRound();
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

        // Druhe kolo: konec, jakmile je rozhodnuto.
        if (round.Value == 2 && roundPhase.Value != RoundDone)
        {
            // Utocnici prekonali postup souperu na stejnem bode.
            if (firstPoints.Value < CapturePointCount && pointIndex.Value == firstPoints.Value
                && progress.Value > firstProgress.Value + 0.01f)
                ServerEndRound(pointIndex.Value, progress.Value);
            // Souper vzal oba body a utocnicim uz dosel jeho cas - nemuzou byt rychlejsi.
            else if (firstPoints.Value >= CapturePointCount && timeUsed > firstTime.Value + 0.5f)
                ServerEndRound(pointIndex.Value, progress.Value);
        }
    }

    void CountOnPoint(out int attackers, out int defenders)
    {
        attackers = 0;
        defenders = 0;
        Quaternion toLocal = Quaternion.Euler(0f, -pointYaw.Value, 0f);
        foreach (var client in PlayerSlots())
        {
            var player = client.PlayerObject;
            if (player == null) continue;

            var health = player.GetComponent<Health>();
            var hero = player.GetComponent<PlayerHero>();
            var team = player.GetComponent<PlayerTeam>();
            if (health == null || team == null || health.currentHealth.Value <= 0f || (hero != null && hero.IsJoining)) continue;

            Vector3 local = toLocal * (player.transform.position - pointPosition.Value);
            if (Mathf.Abs(local.x) > pointSize.Value.x * 0.5f || Mathf.Abs(local.z) > pointSize.Value.y * 0.5f) continue;
            if (local.y < -1.5f || local.y > 4f) continue;

            if (team.teamId.Value == attackTeam.Value) attackers++;
            else defenders++;
        }
    }

    static void SetSeconds(NetworkVariable<int> variable, float seconds)
    {
        int shown = Mathf.Max(0, Mathf.CeilToInt(seconds));
        if (variable.Value != shown) variable.Value = shown;
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

        // Druhe kolo: kdyz uz utocnici maji vic bodu nez souper v prvnim kole, je rozhodnuto.
        if (round.Value == 2 && next > firstPoints.Value)
        {
            ServerEndRound(next, 0f);
            return;
        }

        // Dalsi bod: cas navic, oba tymy se presunou, bod se chvili odemyka.
        timer += BonusSeconds;
        if (roundPhase.Value == RoundOvertime) roundPhase.Value = RoundAttack;
        overtimeEmpty = 0f;
        ServerApplyPoint(next, UnlockNextSeconds);
    }

    void ServerEndRound(int pointsTaken, float progressOnNext)
    {
        roundPhase.Value = RoundDone;
        pointState.Value = StateLocked;

        if (round.Value == 1)
        {
            firstPoints.Value = pointsTaken;
            firstProgress.Value = pointsTaken >= CapturePointCount ? 0f : progressOnNext;
            firstTime.Value = timeUsed;
            RoundOverClientRpc(attackTeam.Value, pointsTaken);

            roundPhase.Value = RoundIntermission;
            phaseTimer = IntermissionSeconds;
            SetSeconds(secondsLeft, phaseTimer);
            return;
        }

        // Vyhodnoceni.
        int first = 1 - attackTeam.Value, second = attackTeam.Value;
        int winner;
        if (pointsTaken != firstPoints.Value)
            winner = pointsTaken > firstPoints.Value ? second : first;
        else if (pointsTaken >= CapturePointCount)
            winner = Mathf.Abs(timeUsed - firstTime.Value) < 0.5f ? -1 : timeUsed < firstTime.Value ? second : first;
        else if (Mathf.Abs(progressOnNext - firstProgress.Value) > 0.01f)
            winner = progressOnNext > firstProgress.Value ? second : first;
        else
            winner = -1;

        EndMatch(winner);
    }

    void ServerBeginSecondRound()
    {
        round.Value = 2;
        attackTeam.Value = 1 - attackTeam.Value;
        ServerResetPickups();
        ServerResetBoiler();

        // Vsichni zpet na spawny (nove role), zivoty dopoli.
        foreach (var client in PlayerSlots())
        {
            var health = client.PlayerObject != null ? client.PlayerObject.GetComponent<Health>() : null;
            if (health != null) health.ResetHealth();
        }

        ServerStartRound();
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
        CaptureUI.Announce($"BOD {PointNames[Mathf.Clamp(index, 0, PointNames.Length - 1)]} ZABRÁN", UiKit.TeamColor(team));
    }

    [ClientRpc]
    void RoundStartedClientRpc(int attackers)
    {
        var listener = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        ProceduralSfx.Play(ProceduralSfx.CaptureUnlock, listener, 1f);
        CaptureUI.Announce($"ÚTOK! · TÝM {attackers} ÚTOČÍ", UiKit.TeamColor(attackers));
    }

    [ClientRpc]
    void RoundOverClientRpc(int attackers, int pointsTaken)
    {
        CaptureUI.Announce($"KONEC 1. KOLA · TÝM {attackers}: {pointsTaken} {(pointsTaken == 1 ? "BOD" : "BODY")} · VÝMĚNA STRAN", UiKit.TeamColor(attackers));
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
