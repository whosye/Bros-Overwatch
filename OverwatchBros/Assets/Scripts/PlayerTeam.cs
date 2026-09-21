using Unity.Netcode;
using UnityEngine;

public class PlayerTeam : NetworkBehaviour
{
    public const int TeamCount = 2;

    public NetworkVariable<int> teamId = new NetworkVariable<int>();

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            teamId.Value = LeastPopulatedTeam();
    }

    // Novy hrac se automaticky zaradi do mensiho tymu; v lobby si to muze zmenit.
    int LeastPopulatedTeam()
    {
        var counts = new int[TeamCount];
        foreach (var other in FindObjectsByType<PlayerTeam>())
        {
            if (other == this || !other.IsSpawned) continue;

            int id = other.teamId.Value;
            if (id >= 0 && id < TeamCount)
                counts[id]++;
        }

        int best = 0;
        for (int i = 1; i < TeamCount; i++)
            if (counts[i] < counts[best])
                best = i;

        return best;
    }

    public void SelectTeam(int team)
    {
        if (!IsOwner) return;
        RequestTeamServerRpc(team);
    }

    [ServerRpc]
    void RequestTeamServerRpc(int team)
    {
        if (MatchManager.Instance != null && !MatchManager.Instance.IsLobby) return;

        teamId.Value = Mathf.Clamp(team, 0, TeamCount - 1);
    }
}
