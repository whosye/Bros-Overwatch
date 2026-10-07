using Unity.Netcode;
using UnityEngine;

// Kotel v hlavni chate (Boiler): kdo zatahne za packu, prehreje na chvili horni patra - vsem, kdo tam jsou
// (i jemu a jeho tymu), ubyvaji zivoty; nepratelum se to pripisuje jemu jako poskozeni a zabiti.
// Casy hlida server (Time.time) a klientum posila jen zbyvajici sekundy.
public partial class MatchManager
{
    const float BoilerTickInterval = 0.5f;

    // zbyvajici sekundy prehrati / do dalsiho pouziti (server zapisuje, klienti ctou)
    public NetworkVariable<float> boilerHeatLeft = new NetworkVariable<float>(0f);
    public NetworkVariable<float> boilerCooldownLeft = new NetworkVariable<float>(0f);

    GameObject boilerStoker;
    float boilerHeatEnd = -1000f, boilerReadyAt;
    float nextBoilerTick, nextBoilerSync;

    public bool BoilerHeating => boilerHeatLeft.Value > 0f;
    public float BoilerCooldownLeft => boilerCooldownLeft.Value;

    // Na zacatku kola je kotel studeny a hned pripraveny.
    void ServerResetBoiler()
    {
        if (!IsServer) return;
        boilerHeatEnd = -1000f;
        boilerReadyAt = 0f;
        boilerStoker = null;
        boilerHeatLeft.Value = 0f;
        boilerCooldownLeft.Value = 0f;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UseBoilerRpc(RpcParams rpcParams = default)
    {
        var boiler = Boiler.Instance;
        if (boiler == null || IsLobby || IsOver || Time.time < boilerReadyAt) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out var client)) return;

        var player = client.PlayerObject;
        if (player == null) return;
        var health = player.GetComponent<Health>();
        if (health == null || health.currentHealth.Value <= 0f) return;
        // rezerva na zpozdeni pohybu po siti
        if (Vector3.Distance(player.transform.position + Vector3.up * 1.6f, boiler.LeverPoint) > Boiler.UseDistance + 1.5f) return;

        boilerStoker = player.gameObject;
        boilerHeatEnd = Time.time + Boiler.HeatSeconds;
        boilerReadyAt = Time.time + Boiler.Cooldown;
        nextBoilerTick = Time.time + BoilerTickInterval;
        nextBoilerSync = 0f;
        Debug.Log($"[Kotel] Zatopeno ({player.name}) - horni patra se prehrivaji {Boiler.HeatSeconds} s.");
    }

    void ServerBoilerTick()
    {
        // zbyvajici casy pro klienty (vzhled, vyzva u packy)
        if (Time.time >= nextBoilerSync)
        {
            nextBoilerSync = Time.time + 0.1f;
            float heat = Mathf.Max(0f, boilerHeatEnd - Time.time);
            float wait = Mathf.Max(0f, boilerReadyAt - Time.time);
            if (boilerHeatLeft.Value != heat) boilerHeatLeft.Value = heat;
            if (boilerCooldownLeft.Value != wait) boilerCooldownLeft.Value = wait;
        }

        if (Time.time >= boilerHeatEnd || Time.time < nextBoilerTick) return;
        nextBoilerTick = Time.time + BoilerTickInterval;

        var boiler = Boiler.Instance;
        if (boiler == null || IsLobby || IsOver) return;

        float damage = Boiler.TotalDamage / Boiler.HeatSeconds * BoilerTickInterval;
        int burned = 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var player = client.PlayerObject;
            if (player == null) continue;
            var health = player.GetComponent<Health>();
            if (health == null || health.currentHealth.Value <= 0f) continue;
            if (!boiler.InHeatZone(player.transform.position)) continue;
            burned++;

            // Pali vsechny nahore. Nepratelum se poskozeni i zabiti pripise tomu, kdo zatopil;
            // jemu samotnemu, jeho tymu (a kdyz uz neni ve hre) bez pripsani (jako pad z vysky).
            if (boilerStoker == null || Combat.SameTeam(boilerStoker, player.gameObject))
                health.TakeDamage(damage);
            else
                Combat.DamagePlayer(boilerStoker, health, damage);
        }
        if (burned == 0 && Boiler.LogMisses)
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
                if (client.PlayerObject != null)
                    Debug.Log($"[Kotel] {client.PlayerObject.name} je na {client.PlayerObject.transform.position} - mimo horni patra " +
                              $"({boiler.heatMin} az {boiler.heatMax}).");
    }
}
