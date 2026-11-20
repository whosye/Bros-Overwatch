using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Pelety do kotle: kdo sebere pytel pelet (balicek), nese ho, dokud zije (jeden pytel na hrace). U feederu
// (PelletFeeder - vzduchem tlaci pelety do kotle) ho klavesou R nasype - feeder je pak plny a pristi zatopeni
// v kotli pali misto Boiler.TotalDamage za HeatSeconds celkem PelletTotalDamage. Zatopenim se feeder vyprazdni.
// Kdo pelety nese, vi server a ten hrac (ClientRpc); plny feeder a topeni s peletami vidi vsichni.
public partial class MatchManager
{
    public const float PelletTotalDamage = 180f;   // za Boiler.HeatSeconds (bez pelet Boiler.TotalDamage)

    public NetworkVariable<bool> feederLoaded = new NetworkVariable<bool>(false);
    public NetworkVariable<bool> boilerWithPellets = new NetworkVariable<bool>(false);   // prave se topi s peletami

    readonly HashSet<ulong> pelletCarriers = new HashSet<ulong>();
    float nextPelletCheck;

    // Mistni hrac nese pytel pelet (HUD, vyzva u feederu).
    public static bool LocalHasPellets { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPelletStatics() => LocalHasPellets = false;

    // Balicek: pytel dostane hrac, ktery na nej vstoupil (kdo uz jeden nese, druhy nesebere).
    bool ServerGivePellets(PlayerHero hero) => pelletCarriers.Add(hero.NetworkObjectId);

    void ServerResetPellets()
    {
        if (!IsServer) return;
        pelletCarriers.Clear();
        feederLoaded.Value = false;
        boilerWithPellets.Value = false;
        PelletsGoneClientRpc(Nobody);
    }

    // Kdo zemrel nebo odesel, o pelety prisel.
    void ServerPelletTick()
    {
        if (pelletCarriers.Count == 0 || Time.time < nextPelletCheck) return;
        nextPelletCheck = Time.time + 0.25f;
        List<ulong> lost = null;
        foreach (ulong id in pelletCarriers)
        {
            var player = SpawnedPlayer(id);
            var health = player != null ? player.GetComponent<Health>() : null;
            if (health != null && health.currentHealth.Value > 0f) continue;
            (lost ??= new List<ulong>()).Add(id);
        }
        if (lost == null) return;
        foreach (ulong id in lost)
        {
            pelletCarriers.Remove(id);
            PelletsGoneClientRpc(id);
        }
    }

    // R u feederu: nasypat nesene pelety.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void LoadPelletsRpc(RpcParams rpcParams = default)
    {
        var feeder = PelletFeeder.Instance;
        if (feeder == null || IsLobby || IsOver || feederLoaded.Value) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out var client)) return;
        var player = client.PlayerObject;
        if (player == null || !pelletCarriers.Contains(player.NetworkObjectId)) return;
        var health = player.GetComponent<Health>();
        if (health == null || health.currentHealth.Value <= 0f) return;
        // rezerva na zpozdeni pohybu po siti
        if (Vector3.Distance(player.transform.position + Vector3.up * 1.6f, feeder.UsePoint) > PelletFeeder.UseDistance + 1.5f) return;

        pelletCarriers.Remove(player.NetworkObjectId);
        feederLoaded.Value = true;
        PelletsLoadedClientRpc(player.NetworkObjectId);
        Debug.Log($"[Kotel] {player.name} nasypal pelety do feederu - pristi zatopeni pali {PelletTotalDamage} HP.");
    }

    [ClientRpc]
    void PelletsLoadedClientRpc(ulong playerId)
    {
        if (IsLocalPlayer(playerId)) LocalHasPellets = false;
        var feeder = PelletFeeder.Instance;
        if (feeder != null) ProceduralSfx.Play(ProceduralSfx.Reload, feeder.UsePoint, 0.9f);
        CaptureUI.Announce($"{NameOfPlayer(playerId)} nasypal pelety do podavače - kotel bude pálit {PelletTotalDamage:0} HP!",
            PickupSpot.KindColor(PickupKind.Pellets), 3f);
    }

    // Pelety pryc (smrt / novy kolo); Nobody = u vsech.
    [ClientRpc]
    void PelletsGoneClientRpc(ulong playerId)
    {
        if (playerId == Nobody || IsLocalPlayer(playerId)) LocalHasPellets = false;
    }

    void LocalPelletsTaken(ulong playerId)
    {
        if (!IsLocalPlayer(playerId)) return;
        LocalHasPellets = true;
        CaptureUI.Announce("Sebral jsi pelety do kotle - nasyp je do podavače [R]", PickupSpot.KindColor(PickupKind.Pellets), 4f);
    }

    static bool IsLocalPlayer(ulong playerId)
    {
        var network = NetworkManager.Singleton;
        var local = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        return local != null && local.NetworkObjectId == playerId;
    }
}
