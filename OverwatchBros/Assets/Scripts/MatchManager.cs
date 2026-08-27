using Unity.Netcode;
using UnityEngine;

public class MatchManager : NetworkBehaviour
{
    public static MatchManager Instance { get; private set; }

    public int scoreToWin = 10;

    public NetworkVariable<int> team0Score = new NetworkVariable<int>();
    public NetworkVariable<int> team1Score = new NetworkVariable<int>();

    void Awake()
    {
        Instance = this;
    }

    public void AddScore(int teamId, int amount)
    {
        if (!IsServer) return;

        if (teamId == 0)
            team0Score.Value += amount;
        else
            team1Score.Value += amount;

        CheckWinCondition();
    }

    void CheckWinCondition()
    {
        if (team0Score.Value >= scoreToWin)
            Debug.Log("Tým 0 vyhrál zápas!");
        else if (team1Score.Value >= scoreToWin)
            Debug.Log("Tým 1 vyhrál zápas!");
    }
}
