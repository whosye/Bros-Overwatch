using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

// Zalozeni hry (host) a pripojeni k ni (klient). Host posloucha na vsech sitovych rozhranich,
// takze nepotrebuje zadne rucni nastaveni ve scene.
public static class GameConnection
{
    public const ushort Port = 7777;

    static string pendingAddress;
    static ushort pendingPort;

    public static string LastError { get; private set; } = "";

    public static bool Host(string gameName)
    {
        var network = NetworkManager.Singleton;
        if (network == null)
        {
            LastError = "V projektu chybí NetworkManager.";
            return false;
        }

        pendingAddress = null;
        MatchManager.PendingGameName = gameName;

        var transport = network.GetComponent<UnityTransport>();
        // Vetsi fronta paketu: s 10 hraci (pohyb, strely, schopnosti, prenos POTG) by vychozich 128 nestacilo.
        transport.MaxPacketQueueSize = 1024;
        transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");

        if (!network.StartHost())
        {
            LastError = "Hru se nepodařilo založit (je port 7777 už obsazený?).";
            return false;
        }

        var discovery = LanDiscovery.GetOrCreate();
        discovery.StopListening();
        discovery.StartAdvertising(gameName, Port, () => network.ConnectedClientsIds.Count);
        LastError = "";
        return true;
    }

    public static bool Join(string address, ushort port)
    {
        pendingAddress = null;
        if (!ServerHistory.TryParse(address + ":" + port, out address, out port, out string error))
        { LastError = error; return false; }
        var network = NetworkManager.Singleton;
        if (network == null)
        {
            LastError = "V projektu chybí NetworkManager.";
            return false;
        }

        var transport = network.GetComponent<UnityTransport>();
        // Vetsi fronta paketu: s 10 hraci (pohyb, strely, schopnosti, prenos POTG) by vychozich 128 nestacilo.
        transport.MaxPacketQueueSize = 1024;
        transport.SetConnectionData(address, port);
        transport.MaxConnectAttempts = 10;

        pendingAddress = address;
        pendingPort = port;
        if (!network.StartClient())
        {
            pendingAddress = null;
            LastError = "K hostiteli se nepodařilo připojit.";
            return false;
        }

        if (LanDiscovery.Instance != null)
            LanDiscovery.Instance.StopListening();

        LastError = "";
        return true;
    }
    // Called only after both the local player and replicated match state exist.
    public static void ConfirmJoined(string serverName)
    {
        if (pendingAddress == null) return;
        ServerHistory.Joined(pendingAddress, pendingPort, serverName);
        pendingAddress = null;
    }

}
