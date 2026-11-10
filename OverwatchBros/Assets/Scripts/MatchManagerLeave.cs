using Unity.Netcode;
using UnityEngine;

// Odchod hostitele: nejdriv se odpoji vsichni ostatni sami (cisty odchod do menu), teprve pak host vypne server.
// (Kdyz server zmizel pod rukama, klientum hra spadla.) Volano z MatchUI.LeaveRoutine.
public partial class MatchManager
{
    public void ServerHostLeaving()
    {
        if (!IsServer || !IsSpawned) return;
        HostLeavingClientRpc();
    }

    [ClientRpc]
    void HostLeavingClientRpc()
    {
        if (IsServer) return;
        var ui = FindAnyObjectByType<MatchUI>();
        if (ui != null) ui.LeaveBecauseHostLeft();
    }
}
