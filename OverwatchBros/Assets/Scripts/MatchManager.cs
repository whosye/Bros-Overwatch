using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class MatchManager : NetworkBehaviour
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

    public bool IsOver => matchOver.Value;
    public bool IsLobby => phase.Value == PhaseLobby;

    void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        scoreToWinSynced.Value = Mathf.Max(1, scoreToWin);
        gameName.Value = new FixedString64Bytes(string.IsNullOrWhiteSpace(PendingGameName) ? "Hra" : PendingGameName);
        phase.Value = PhaseLobby;
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
            team0Score.Value += amount;
        else
            team1Score.Value += amount;

        CheckWinCondition();
    }

    // Zavola server, kdyz hrac (killer) zabil protihrace. Pripise bod tymu a spusti kill hlasku.
    public void ReportKill(GameObject killer)
    {
        if (!IsServer || killer == null) return;
        if (matchOver.Value || IsLobby) return;

        var team = killer.GetComponent<PlayerTeam>();
        if (team != null)
            AddScore(team.teamId.Value, 1);

        var hero = killer.GetComponent<PlayerHero>();
        if (hero != null)
            hero.NotifyKill();
    }

    void CheckWinCondition()
    {
        int target = scoreToWinSynced.Value;

        if (team0Score.Value >= target)
            EndMatch(0);
        else if (team1Score.Value >= target)
            EndMatch(1);
    }

    void EndMatch(int winner)
    {
        winnerTeam.Value = winner;
        matchOver.Value = true;
        Debug.Log($"Tým {winner} vyhrál zápas!");
    }

    // Host spusti zapas z lobby.
    public void StartMatch()
    {
        if (!IsServer) return;

        phase.Value = PhasePlaying;
        ResetRound();
    }

    // Novy zapas se stejnymi tymy a hrdiny.
    public void RestartMatch()
    {
        if (!IsServer) return;

        phase.Value = PhasePlaying;
        ResetRound();
    }

    // Zpet do lobby (zmena tymu / hrdiny).
    public void BackToLobby()
    {
        if (!IsServer) return;

        phase.Value = PhaseLobby;
        ResetRound();
    }

    void ResetRound()
    {
        team0Score.Value = 0;
        team1Score.Value = 0;
        winnerTeam.Value = -1;
        matchOver.Value = false;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            var health = client.PlayerObject.GetComponent<Health>();
            if (health != null)
                health.ResetHealth();
        }

        ResetPlayersClientRpc();
    }

    [ClientRpc]
    void ResetPlayersClientRpc()
    {
        var local = NetworkManager.Singleton.LocalClient;
        if (local == null || local.PlayerObject == null) return;

        var respawn = local.PlayerObject.GetComponent<PlayerRespawn>();
        if (respawn != null)
            respawn.ResetToSpawn();
    }
}
