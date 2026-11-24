using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public partial class MatchManager : NetworkBehaviour
{
    public const int PhaseLobby = 0;
    public const int PhasePlaying = 1;

    public static MatchManager Instance { get; private set; }

    // Nastavi menu pred zalozenim hry, server si to precte pri spawnu.
    public static string PendingGameName = "Hra";

    public int scoreToWin = 10;

    public NetworkVariable<int> team0Score = new NetworkVariable<int>();
    public NetworkVariable<int> team1Score = new NetworkVariable<int>();
    public NetworkVariable<int> scoreToWinSynced = new NetworkVariable<int>(10);
    public NetworkVariable<bool> matchOver = new NetworkVariable<bool>(false);
    public NetworkVariable<int> winnerTeam = new NetworkVariable<int>(-1);
    public NetworkVariable<int> phase = new NetworkVariable<int>(PhaseLobby);
    public NetworkVariable<FixedString64Bytes> gameName = new NetworkVariable<FixedString64Bytes>();

    // Testovaci cooldowny (1 s) pro vsechny hrace; prepina host v lobby.
    public NetworkVariable<bool> testCooldowns = new NetworkVariable<bool>(false);

    // Volba z hlavniho menu: se zapnutymi testovacimi cooldowny se hra rovnou zalozi.
    public static bool PendingTestCooldowns
    {
        get => PlayerPrefs.GetInt("testCooldowns", 0) == 1;
        set => PlayerPrefs.SetInt("testCooldowns", value ? 1 : 0);
    }

    public void SetTestCooldowns(bool on)
    {
        if (!IsServer) return;

        testCooldowns.Value = on;
        PendingTestCooldowns = on;
    }

    void OnTestCooldownsChanged(bool previous, bool current)
    {
        AbilityDefinition.TestCooldowns = current;
    }

    public override void OnNetworkDespawn()
    {
        chatSentAt.Clear();
        ApplyMap(MapDomasov);   // po odpojeni zpet puvodni mapa (menu, dalsi hra)
        testCooldowns.OnValueChanged -= OnTestCooldownsChanged;
        AbilityDefinition.TestCooldowns = false;
    }

    public bool IsOver => matchOver.Value;
    public bool IsLobby => phase.Value == PhaseLobby;

    void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        chatSentAt.Clear();
        testCooldowns.OnValueChanged += OnTestCooldownsChanged;
        AbilityDefinition.TestCooldowns = testCooldowns.Value;

        // Klient pripojeny do rozehraneho zapasu: hned na mapu, na ktere se hraje.
        if (!IsServer && !IsLobby)
            ApplyMap(MapIndex);

        // Klient pripojeny do rozehraneho zapasu si vyzada mista oziveni (rezim utok a obrana).
        if (!IsServer)
        {
            RequestSpawnsRpc();
            return;
        }

        testCooldowns.Value = PendingTestCooldowns;
        // MatchManager je objekt sceny - hodnoty z minule hry v nem zustanou. Ridic rolby z minula by v nove hre
        // "ridil" hrace se stejnym ID (rolba by jela a hrala pisnicka uz v lobby).
        ServerResetTractor();
        ServerResetBoiler();

        scoreToWinSynced.Value = Mathf.Max(1, scoreToWin);
        gameName.Value = new FixedString64Bytes(string.IsNullOrWhiteSpace(PendingGameName) ? "Hra" : PendingGameName);
        phase.Value = PhaseLobby;
        if (HostOptions.ApplyOnSpawn)
        {
            HostOptions.ApplyOnSpawn = false;
            int selectedMap = HostOptions.Map;
            int selectedMode = HostOptions.Mode;
            if (!MapReady(selectedMap) || !MapSupportsMode(selectedMap, selectedMode)) selectedMap = selectedMode == ModePush ? MapDomasovPush : MapDomasov;
            // (tlacenice bez postavene mapy: deathmatch na Domasove)
            if (!MapReady(selectedMap) || !MapSupportsMode(selectedMap, selectedMode)) { selectedMap = MapDomasov; selectedMode = ModeDeathmatch; }
            mapIndex.Value = selectedMap;
            gameMode.Value = selectedMode;
            scoreToWinSynced.Value = HostOptions.Kills;
            captureSeconds.Value = HostOptions.CaptureSeconds;
        }
    }

    public void SetScoreToWin(int value)
    {
        if (!IsServer) return;
        scoreToWinSynced.Value = Mathf.Clamp(value, 1, 100);
    }

    public void AddScore(int teamId, int amount)
    {
        if (!IsServer) return;
        if (matchOver.Value || IsLobby) return;

        if (teamId == 0)
            team0Score.Value = Mathf.Max(0, team0Score.Value + amount);
        else
            team1Score.Value = Mathf.Max(0, team1Score.Value + amount);

        CheckWinCondition();
    }

    // Zavola server, kdyz hrac (killer) zabil protihrace. Pripise bod tymu a spusti kill hlasku.
    public void ReportKill(GameObject killer, GameObject victim = null, string source = "")
    {
        if (!IsServer || killer == null) return;
        if (matchOver.Value || IsLobby) return;

        var hero = killer.GetComponent<PlayerHero>();
        if (hero != null)
            hero.kills.Value++;
        var killedHero = victim != null ? victim.GetComponent<PlayerHero>() : null;
        if (killedHero != null)
            killedHero.ServerKillReportedFrame = Time.frameCount;


        // Seznam zabiti u vsech hracu.
        var killerObject = killer.GetComponent<NetworkObject>();
        var victimObject = victim != null ? victim.GetComponent<NetworkObject>() : null;
        if (killerObject != null && victimObject != null)
            KillFeedClientRpc(killerObject.NetworkObjectId, victimObject.NetworkObjectId, source ?? "");

        // V utoku a obrane (a v tlacenici) se skore pocita za body / dotlacene metry, ne za zabiti.
        var team = killer.GetComponent<PlayerTeam>();
        if (team != null && IsDeathmatchMode)
            AddScore(team.teamId.Value, 1);

        // Play of the game: hodnoceni akce (az po pripsani bodu, at se vi, jestli zabiti rozhodlo zapas).
        var recorder = killer.GetComponent<PotgRecorder>();
        if (recorder != null)
            recorder.ServerOnKill(victim, matchOver.Value);

        if (hero != null)
            hero.NotifyKill();

        // Zabiti a asistence (kdo obet zranil v poslednich sekundach): zkraceni cooldownu (Pova).
        if (hero != null)
            hero.ServerTakedownRefund();
        var victimHero = victim != null ? victim.GetComponent<PlayerHero>() : null;
        if (victimHero != null)
        {
            foreach (var assister in victimHero.ServerRecentAttackers())
            {
                if (assister == killer || Combat.SameTeam(assister, victim)) continue;
                var assistHero = assister.GetComponent<PlayerHero>();
                if (assistHero != null) assistHero.ServerTakedownRefund(true);
            }
            victimHero.ServerClearAttackers();
        }
    }

    // Smrt, kterou nezpusobil hrac (auto, pad z mapy, kotel na sebe...): v kill feedu pricina misto vraha.
    public void ServerEnvironmentDeath(PlayerHero victim, string cause)
    {
        if (!IsServer || victim == null || matchOver.Value || IsLobby) return;
        EnvironmentFeedClientRpc(victim.NetworkObjectId, cause ?? "");
    }

    [ClientRpc]
    void EnvironmentFeedClientRpc(ulong victimId, string cause)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(victimId, out NetworkObject victim)) return;
        var victimHero = victim.GetComponent<PlayerHero>();
        var victimTeam = victim.GetComponent<PlayerTeam>();
        if (victimHero == null) return;
        bool local = victim.IsOwner && !BotBrain.IsBot(victim.gameObject);
        // bez vraha: jen ikona priciny a obet
        MatchOverlayUI.AddKill("", -1, victimHero.DisplayName, victimTeam != null ? victimTeam.teamId.Value : -1, local, cause);
    }

    [ClientRpc]
    void KillFeedClientRpc(ulong killerId, ulong victimId, string source)
    {
        var spawned = NetworkManager.SpawnManager.SpawnedObjects;
        spawned.TryGetValue(killerId, out NetworkObject killer);
        spawned.TryGetValue(victimId, out NetworkObject victim);
        if (victim == null) return;

        var killerHero = killer != null ? killer.GetComponent<PlayerHero>() : null;
        var victimHero = victim.GetComponent<PlayerHero>();
        if (victimHero == null) return;

        var killerTeam = killer != null ? killer.GetComponent<PlayerTeam>() : null;
        var victimTeam = victim.GetComponent<PlayerTeam>();
        bool local = (killer != null && killer.IsOwner && !BotBrain.IsBot(killer.gameObject)) || (victim.IsOwner && !BotBrain.IsBot(victim.gameObject));

        // zemrel jsem ja: killcam z pohledu vraha
        if (killer != null && victim.IsOwner && !BotBrain.IsBot(victim.gameObject))
            Killcam.OnLocalDeath(killer, victim);

        MatchOverlayUI.AddKill(
            killerHero != null ? killerHero.DisplayName : "",
            killerTeam != null ? killerTeam.teamId.Value : -1,
            victimHero.DisplayName,
            victimTeam != null ? victimTeam.teamId.Value : -1,
            local, source);
    }

    void CheckWinCondition()
    {
        int target = scoreToWinSynced.Value;

        if (team0Score.Value >= target)
            EndMatch(0);
        else if (team1Score.Value >= target)
            EndMatch(1);
    }

    // winner = -1: remiza (utok a obrana se stejnym vysledkem).
    void EndMatch(int winner)
    {
        winnerTeam.Value = winner;
        matchOver.Value = true;
        Debug.Log(winner >= 0 ? $"Tým {winner} vyhrál zápas!" : "Remíza!");

        PotgPending = true;
        StartCoroutine(PlayOfTheGame());
    }

    // Host nemuze spustit novy zapas ani se vratit do lobby, dokud se neprehraje play of the game
    // (nebo dokud neni jasne, ze zadny nebude).
    public bool PotgPending { get; private set; }

    // Chvili po konci zapasu se vsem prehraje zaznam hrace s nejlepsi akci (nejvic zabiti v kratkem case).
    System.Collections.IEnumerator PlayOfTheGame()
    {
        // (po rozhodujicim zabiti se jeste 3 s nahrava - klip konci 2,5 s po zabiti)
        yield return new WaitForSeconds(3.5f);
        if (!IsSpawned || !matchOver.Value)
        {
            PotgPending = false;
            yield break;
        }

        // Poradi hracu podle nejlepsi akce. Kdyz nejlepsi hrac odpadne (nebo zaznam neposle), vezme se dalsi v poradi.
        var ranked = new System.Collections.Generic.List<PotgRecorder>();
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var recorder = client.PlayerObject != null ? client.PlayerObject.GetComponent<PotgRecorder>() : null;
            if (recorder != null && recorder.ServerBestScore > 0)
                ranked.Add(recorder);
        }
        ranked.Sort((a, b) => b.ServerBestScore.CompareTo(a.ServerBestScore));

        bool delivered = false;
        foreach (var candidate in ranked)
        {
            if (candidate == null || !candidate.IsSpawned) continue;

            candidate.ServerStartPotg();
            while (true)
            {
                yield return null;

                // Hrac se odpojil (jeho objekt zmizel).
                if (candidate == null || !candidate.IsSpawned) break;
                if (candidate.ServerClipComplete) { delivered = true; break; }

                // Nezacal posilat do 4 s, nebo se prenos na 4 s zasekl.
                if (Time.unscaledTime - candidate.ServerLastProgress > 4f) break;
            }

            if (delivered) break;

            // Zrusit rozpracovane prehravani u vsech a zkusit dalsiho.
            CancelPotgClientRpc();
            if (!IsSpawned || !matchOver.Value) break;
        }

        if (!delivered)
        {
            PotgPending = false;
            yield break;
        }

        // Pockat, az se klip u hosta objevi (kdyby nedorazil, po chvili se ovladani uvolni) a az dohraje.
        float waited = 0f;
        while (!PotgUI.IsShowing && waited < 8f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        waited = 0f;
        while (PotgUI.IsShowing && waited < 30f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        PotgPending = false;
    }

    [ClientRpc]
    void CancelPotgClientRpc()
    {
        PotgUI.Stop();
    }

    // Host spusti zapas z lobby.
    public void StartMatch()
    {
        if (!IsServer) return;
        if (!SelectedMapSupportsMode)
        {
            Debug.LogWarning($"[Mapa] {MapName} nepodporuje zvoleny herni rezim.");
            return;
        }
        if (!SelectedMapReady)
        {
            Debug.LogWarning($"[Mapa] {MapName} jeste neni postavena - zapas nejde spustit.");
            return;
        }
        ServerApplyMap();

        phase.Value = PhasePlaying;
        ResetRound();
    }

    // Novy zapas se stejnymi tymy a hrdiny.
    public void RestartMatch()
    {
        if (!IsServer || PotgPending || !SelectedMapReady || !SelectedMapSupportsMode) return;

        phase.Value = PhasePlaying;
        ResetRound();
    }

    // Zpet do lobby (zmena tymu / hrdiny).
    public void BackToLobby()
    {
        if (!IsServer || PotgPending) return;

        phase.Value = PhaseLobby;
        ResetRound();
    }

    void ResetRound()
    {
        ServerResetCaptureTransition();
        ServerResetPickups();
        ServerResetBoiler();
        ServerResetTractor();
        ServerResetPush();
        team0Score.Value = 0;
        team1Score.Value = 0;
        winnerTeam.Value = -1;
        matchOver.Value = false;

        foreach (var client in PlayerSlots())
        {
            if (client.PlayerObject == null) continue;

            var health = client.PlayerObject.GetComponent<Health>();
            if (health != null)
                health.ResetHealth();

            var hero = client.PlayerObject.GetComponent<PlayerHero>();
            if (hero != null)
            {
                hero.ServerClearJoining();
                hero.ServerResetStats();
            }

            var recorder = client.PlayerObject.GetComponent<PotgRecorder>();
            if (recorder != null)
                recorder.ServerReset();
        }

        // Utok a obrana: body, kola a spawny podle roli; jinak se ozivuje na zakladnach ze sceny.
        if (!IsLobby && IsAttackMode)
        {
            ServerBeginAttackMatch();
        }
        else if (!IsLobby && IsPushMode)
        {
            ServerBeginPushMatch();
        }
        else
        {
            customSpawns = false;
            pointState.Value = StateLocked;
            roundPhase.Value = RoundSetup;
        }

        if (!IsLobby) ServerAssignRandomHeroes();   // (nahodni hrdinove, voli host v lobby)
        ResetPlayersClientRpc(customSpawns, customSpawn[0], customSpawn[1]);

        // Start zapasu: spawn hlasku rekne jen jeden nahodny hrac z kazdeho tymu, s odstupem (ne vsichni naraz).
        if (!IsLobby)
        {
            StopCoroutine(nameof(StartLines));
            StartCoroutine(nameof(StartLines));
        }
    }

    System.Collections.IEnumerator StartLines()
    {
        float[] delays = { 0.6f, 2.4f };
        int first = Random.Range(0, PlayerTeam.TeamCount);
        for (int i = 0; i < PlayerTeam.TeamCount; i++)
        {
            int team = (first + i) % PlayerTeam.TeamCount;
            var heroes = new System.Collections.Generic.List<PlayerHero>();
            foreach (var client in PlayerSlots())
            {
                var player = client.PlayerObject;
                var hero = player != null ? player.GetComponent<PlayerHero>() : null;
                var playerTeam = player != null ? player.GetComponent<PlayerTeam>() : null;
                if (hero != null && playerTeam != null && playerTeam.teamId.Value == team)
                    heroes.Add(hero);
            }

            yield return new WaitForSeconds(i < delays.Length ? delays[i] - (i > 0 ? delays[i - 1] : 0f) : 2f);
            if (heroes.Count > 0 && !IsLobby && !IsOver)
                heroes[Random.Range(0, heroes.Count)].Say(VoiceKind.Spawn);
        }
    }

    [ClientRpc]
    void ResetPlayersClientRpc(bool custom, Vector3 spawn0, Vector3 spawn1)
    {
        MatchOverlayUI.ClearKills();
        if (IsServer)
        {
            customSpawns = custom;
            customSpawn[0] = spawn0;
            customSpawn[1] = spawn1;
            ServerResetBots();
        }
        customSpawns = custom;
        customSpawn[0] = spawn0;
        customSpawn[1] = spawn1;

        var local = NetworkManager.Singleton.LocalClient;
        if (local == null || local.PlayerObject == null) return;

        var respawn = local.PlayerObject.GetComponent<PlayerRespawn>();
        if (respawn != null)
            respawn.ResetToSpawn();
    }
}
