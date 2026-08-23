using Unity.Netcode;

public class PlayerTeam : NetworkBehaviour
{
    public NetworkVariable<int> teamId = new NetworkVariable<int>();

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            teamId.Value = (int)(OwnerClientId % 2);
    }
}
