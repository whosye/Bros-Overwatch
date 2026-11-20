using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// Bounded request/response discovery. Opening/refreshing is the only source of queries.
public class LanDiscovery : MonoBehaviour
{
    public const int DiscoveryPort=47777;
    public const float RefreshInterval=5f, QueryTimeout=2f;
    const string QueryMagic="BROS2?", ReplyMagic="BROS2!";
    [Serializable] public class Game
    {
        public string name,address,map,mode,phase;
        public ushort port;
        public int players;
        [NonSerialized] public float lastSeen;
    }
    public static LanDiscovery Instance { get; private set; }
    public readonly Dictionary<string,Game> Games=new Dictionary<string,Game>();
    UdpClient listener,responder;
    string advertisedName,nonce;
    ushort advertisedPort;
    Func<int> playerCount;
    float nextRefresh=float.NegativeInfinity,queryStarted=float.NegativeInfinity;
    readonly Dictionary<string,float> peers=new Dictionary<string,float>();
    readonly HashSet<string> queried = new HashSet<string>();
    int replyBudget;
    float budgetAt;
    public int RefreshCount { get; private set; }
    public string Error { get; private set; }="";
    public bool IsAdvertising=>responder!=null;
    public bool IsListening=>listener!=null;
    public bool IsQuerying=>listener!=null&&Time.unscaledTime-queryStarted<QueryTimeout;
    public float Cooldown=>Mathf.Max(0,nextRefresh-Time.unscaledTime);
    public bool HasChecked=>!float.IsNegativeInfinity(queryStarted);
    public bool WasQueried(string endpoint) => queried.Contains(endpoint);
    public bool IsCurrent(Game game)=>game!=null&&HasChecked&&game.lastSeen>=queryStarted;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { Instance=null; }
    public static LanDiscovery GetOrCreate()
    {
        if(Instance!=null)return Instance;
        var go=new GameObject("LanDiscovery"); DontDestroyOnLoad(go); Instance=go.AddComponent<LanDiscovery>(); return Instance;
    }
    public void StartListening()
    {
        if(listener!=null)return;
        try { listener=new UdpClient(new IPEndPoint(IPAddress.Any,0)); listener.EnableBroadcast=true; Error=""; }
        catch(SocketException) { Error="Hledání není dostupné. Přes uloženou IP se můžeš přesto připojit."; }
    }
    public void StopListening() { listener?.Close(); listener=null; }
    public bool Refresh(IEnumerable<ServerHistory.Entry> saved,bool includeLan=true)
    {
        if(Cooldown>0)return false;
        StartListening(); if(listener==null)return false;
        queryStarted=Time.unscaledTime; nextRefresh=queryStarted+RefreshInterval; nonce=Guid.NewGuid().ToString("N"); RefreshCount++;
        var sent=new HashSet<string>(); queried.Clear();
        foreach(var e in saved)if(e!=null&&sent.Add(e.Endpoint)) { queried.Add(e.Endpoint); SendQuery(e.address,e.port); }
        if(includeLan)SendQuery(IPAddress.Broadcast.ToString(),0);
        return true;
    }
    void SendQuery(string address,ushort port)
    {
        byte[] bytes=Encoding.UTF8.GetBytes(QueryMagic+"|"+nonce+"|"+port);
        try { listener.Send(bytes,bytes.Length,new IPEndPoint(IPAddress.Parse(address),DiscoveryPort)); }
        catch(SocketException) { Error="Některé adresy se nepodařilo ověřit. Zkontroluj síť a firewall."; }
    }
    public void StartAdvertising(string name,ushort port,Func<int> count)
    {
        StopAdvertising(); advertisedName=name; advertisedPort=port; playerCount=count;
        try { responder=new UdpClient(AddressFamily.InterNetwork); responder.Client.Bind(new IPEndPoint(IPAddress.Any,DiscoveryPort)); }
        catch(SocketException e) { Debug.LogWarning("[LanDiscovery] Stav serveru nelze nabízet: "+e.Message); StopAdvertising(); }
    }
    public void StopAdvertising() { responder?.Close(); responder=null; peers.Clear(); }
    void Update() { ReceiveQueries(); ReceiveReplies(); }
    void ReceiveQueries()
    {
        if(responder==null)return;
        if(Time.unscaledTime>=budgetAt) { budgetAt=Time.unscaledTime+1; replyBudget=32; }
        try
        {
            for(int i=0;i<64&&responder.Available>0;i++)
            {
                IPEndPoint from=null; byte[] bytes=responder.Receive(ref from);
                if(bytes.Length>128||replyBudget<=0)continue;
                string[] p=Encoding.UTF8.GetString(bytes).Split('|');
                if(p.Length!=3||p[0]!=QueryMagic||p[1].Length!=32||!ushort.TryParse(p[2],out ushort port)||(port!=0&&port!=advertisedPort))continue;
                string peer=from.ToString();
                if(peers.TryGetValue(peer,out float last)&&Time.unscaledTime-last<1)continue;
                if(peers.Count>=128)peers.Clear(); peers[peer]=Time.unscaledTime; replyBudget--;
                var m=MatchManager.Instance;
                var game=new Game { name=advertisedName,port=advertisedPort,players=playerCount?.Invoke()??1,map=m!=null?m.MapName:"",
                    mode=m!=null&&m.IsAttackMode?"Dobývání":"Team deathmatch",phase=m==null||m.IsLobby?"Lobby":m.IsOver?"Konec zápasu":"Rozehráno" };
                byte[] reply=Encoding.UTF8.GetBytes(ReplyMagic+"|"+p[1]+"|"+JsonUtility.ToJson(game));
                responder.Send(reply,reply.Length,from);
            }
        } catch(SocketException) { }
    }
    void ReceiveReplies()
    {
        if(listener==null)return;
        try
        {
            for(int i=0;i<64&&listener.Available>0;i++)
            {
                IPEndPoint from=null; byte[] bytes=listener.Receive(ref from);
                if(bytes.Length>2048||!IsQuerying||from.Port!=DiscoveryPort)continue;
                string[] p=Encoding.UTF8.GetString(bytes).Split(new[]{'|'},3);
                if(p.Length!=3||p[0]!=ReplyMagic||p[1]!=nonce)continue;
                Game game; try { game=JsonUtility.FromJson<Game>(p[2]); } catch(ArgumentException) { continue; }
                if(game==null||game.port==0||game.players<0||game.players>1000||string.IsNullOrWhiteSpace(game.name)||game.name.Length>80
                    ||(game.map?.Length??0)>50||(game.mode?.Length??0)>50||(game.phase!="Lobby"&&game.phase!="Rozehráno"&&game.phase!="Konec zápasu"))continue;
                game.address=from.Address.ToString(); game.lastSeen=Time.unscaledTime; string key=game.address+":"+game.port;
                if(Games.Count<256||Games.ContainsKey(key))Games[key]=game;
            }
        } catch(SocketException) { }
    }
    void OnDestroy() { StopAdvertising(); StopListening(); if(Instance==this)Instance=null; }
    void OnApplicationQuit() { StopAdvertising(); StopListening(); }
}
