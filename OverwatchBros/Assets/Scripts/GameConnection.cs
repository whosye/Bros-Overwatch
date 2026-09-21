using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

// Zalozeni hry (host) a pripojeni k ni (klient). Host posloucha na vsech sitovych rozhranich,
// takze nepotrebuje zadne rucni nastaveni ve scene.
public static class GameConnection
{
    public const ushort Port = 7777;

    public static string LastError { get; private set; } = "";

    public static bool Host(string gameName)
    {
        var network = NetworkManager.Singleton;
        if (network == null)
        {
            LastError = "V projektu chybí NetworkManager.";
            return false;
        }

        MatchManager.PendingGameName = gameName;

        var transport = network.GetComponent<UnityTransport>();
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
        var network = NetworkManager.Singleton;
        if (network == null)
        {
            LastError = "V projektu chybí NetworkManager.";
            return false;
        }

        var transport = network.GetComponent<UnityTransport>();
        transport.SetConnectionData(address, port);
        transport.MaxConnectAttempts = 10;

        if (!network.StartClient())
        {
            LastError = "K hostiteli se nepodařilo připojit.";
            return false;
        }

        if (LanDiscovery.Instance != null)
            LanDiscovery.Instance.StopListening();

        LastError = "";
        return true;
    }
}
