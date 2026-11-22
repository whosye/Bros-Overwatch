using System.Collections.Generic;
using System.Text;
using Unity.Netcode;

public partial class MatchManager
{
    public const int ChatCharacterLimit=200;
    readonly Dictionary<ulong,double> chatSentAt=new Dictionary<ulong,double>();
    public static string CleanChatText(string text)
    {
        if(string.IsNullOrWhiteSpace(text)) return "";
        var result=new StringBuilder();
        foreach(char c in text)
        {
            if(result.Length>=ChatCharacterLimit) break;
            if(c=='\r' || c=='\n' || c=='\t') { result.Append(' '); continue; }
            if(!char.IsControl(c)) result.Append(c);
        }
        // Never send half a UTF-16 surrogate pair at the length boundary.
        if(result.Length>0 && char.IsHighSurrogate(result[result.Length-1])) result.Length--;
        return result.ToString().Trim();
    }

    [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
    public void SendChatRpc(string text,bool teamOnly,RpcParams rpcParams=default)
    {
        ulong sender=rpcParams.Receive.SenderClientId;
        if(!NetworkManager.ConnectedClients.TryGetValue(sender,out var client) || client.PlayerObject==null) return;
        if(text==null || text.Length>1024) return;
        text=CleanChatText(text);
        if(text.Length==0) return;
        double now=NetworkManager.ServerTime.Time;
        if(chatSentAt.TryGetValue(sender,out double previous) && now-previous<.6) return;
        var hero=client.PlayerObject.GetComponent<PlayerHero>();
        var team=client.PlayerObject.GetComponent<PlayerTeam>();
        if(hero==null || team==null) return;
        chatSentAt[sender]=now;
        var recipients=new List<ulong>();
        foreach(var peer in NetworkManager.ConnectedClientsList)
        {
            var peerTeam=peer.PlayerObject!=null?peer.PlayerObject.GetComponent<PlayerTeam>():null;
            if(!teamOnly || (peerTeam!=null && peerTeam.teamId.Value==team.teamId.Value)) recipients.Add(peer.ClientId);
        }
        if(recipients.Count==0) return;
        ReceiveChatClientRpc(hero.playerName.Value.ToString(),text,team.teamId.Value,teamOnly,
            new ClientRpcParams { Send=new ClientRpcSendParams { TargetClientIds=recipients } });
    }

    [ClientRpc]
    void ReceiveChatClientRpc(string author,string text,int team,bool teamOnly,ClientRpcParams targets=default) =>
        TextChatUI.Receive(author,text,team,teamOnly);
}
