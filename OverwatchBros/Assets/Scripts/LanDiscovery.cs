using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// Jednoduche hledani her v LAN: host kazdou sekundu vysila UDP broadcast, klienti poslouchaji a skladaji seznam her.
public class LanDiscovery : MonoBehaviour
{
    public const int DiscoveryPort = 47777;
    const string Magic = "BROS1";

    public class Game
    {
        public string name;
        public string address;
        public ushort port;
        public int players;
        public float lastSeen;
    }

    public static LanDiscovery Instance { get; private set; }

    public readonly Dictionary<string, Game> Games = new Dictionary<string, Game>();

    UdpClient listener;
    UdpClient sender;
    string advertisedName;
    ushort advertisedPort;
    Func<int> playerCount;
    float nextBroadcast;

    public bool IsAdvertising => sender != null;
    public bool IsListening => listener != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        Instance = null;
    }

    public static LanDiscovery GetOrCreate()
    {
        if (Instance != null) return Instance;

        var go = new GameObject("LanDiscovery");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<LanDiscovery>();
        return Instance;
    }

    public void StartListening()
    {
        if (listener != null) return;

        try
        {
            listener = new UdpClient(AddressFamily.InterNetwork);
            listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            listener.EnableBroadcast = true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LanDiscovery] Poslech se nepodařilo spustit: {e.Message}");
            StopListening();
        }
    }

    public void StopListening()
    {
        if (listener != null)
        {
            listener.Close();
            listener = null;
        }

        Games.Clear();
    }

    public void StartAdvertising(string gameName, ushort port, Func<int> currentPlayers)
    {
        StopAdvertising();

        advertisedName = (gameName ?? "Hra").Replace('|', '/');
        advertisedPort = port;
        playerCount = currentPlayers;

        try
        {
            sender = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            nextBroadcast = 0f;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LanDiscovery] Vysílání se nepodařilo spustit: {e.Message}");
            sender = null;
        }
    }

    public void StopAdvertising()
    {
        if (sender != null)
        {
            sender.Close();
            sender = null;
        }
    }

    void Update()
    {
        Receive();
        Advertise();
        Prune();
    }

    void Advertise()
    {
        if (sender == null || Time.unscaledTime < nextBroadcast) return;
        nextBroadcast = Time.unscaledTime + 1f;

        int players = playerCount != null ? playerCount() : 1;
        var bytes = Encoding.UTF8.GetBytes($"{Magic}|{advertisedName}|{advertisedPort}|{players}");

        try
        {
            sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
        }
        catch (Exception)
        {
            // Bez site se broadcast nepovede; zkusime znovu za vterinu.
        }
    }

    void Receive()
    {
        if (listener == null) return;

        try
        {
            while (listener.Available > 0)
            {
                IPEndPoint from = null;
                var data = listener.Receive(ref from);
                Parse(Encoding.UTF8.GetString(data), from);
            }
        }
        catch (Exception)
        {
            // Zavreny socket / prechodna chyba site: ignorujeme.
        }
    }

    void Parse(string text, IPEndPoint from)
    {
        var parts = text.Split('|');
        if (parts.Length < 4 || parts[0] != Magic) return;
        if (!ushort.TryParse(parts[2], out ushort port)) return;
        int.TryParse(parts[3], out int players);

        string key = $"{from.Address}:{port}";
        if (!Games.TryGetValue(key, out var game))
        {
            game = new Game();
            Games[key] = game;
        }

        game.name = parts[1];
        game.address = from.Address.ToString();
        game.port = port;
        game.players = players;
        game.lastSeen = Time.unscaledTime;
    }

    void Prune()
    {
        if (Games.Count == 0) return;

        List<string> stale = null;
        foreach (var pair in Games)
        {
            if (Time.unscaledTime - pair.Value.lastSeen > 4f)
                (stale ??= new List<string>()).Add(pair.Key);
        }

        if (stale == null) return;
        foreach (var key in stale)
            Games.Remove(key);
    }

    void OnDestroy()
    {
        StopAdvertising();
        StopListening();
    }
}
