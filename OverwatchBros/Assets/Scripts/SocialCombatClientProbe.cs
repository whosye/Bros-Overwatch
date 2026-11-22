#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Dormant outside the opt-in localhost integration check.
public sealed class SocialCombatClientProbe:MonoBehaviour
{
    string directory;
    int team,stage;
    bool requested,selected;
    float deadline,settled,sentAt;
    readonly List<string> received=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args=Environment.GetCommandLineArgs(); int flag=Array.IndexOf(args,"--social-probe");
        if(flag<0 || flag+2>=args.Length) return;
        var probe=new GameObject("SocialCombatClientProbe").AddComponent<SocialCombatClientProbe>();
        DontDestroyOnLoad(probe.gameObject); probe.directory=args[flag+1]; probe.team=int.Parse(args[flag+2]);
        probe.deadline=Time.realtimeSinceStartup+100;
        TextChatUI.MessageReceived+=probe.Receive;
        Application.runInBackground=true;
    }
    void Receive(TextChatUI.Line line) => received.Add(line.text);
    void OnDestroy() => TextChatUI.MessageReceived-=Receive;
    static void Check(bool pass,string reason) { if(!pass) throw new InvalidOperationException(reason); }
    string FileName(string suffix) => Path.Combine(directory,"peer-"+team+suffix);
    void Update()
    {
        try
        {
            Check(Time.realtimeSinceStartup<deadline,"Chat peer timeout");
            var network=NetworkManager.Singleton;
            if(network==null) return;
            if(!requested)
            {
                PlayerHero.PreferredName=team==0?"Viktor QA":"Soupeř QA";
                network.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17794);
                Check(network.StartClient(),"Client did not start"); requested=true; return;
            }
            var player=network.LocalClient?.PlayerObject; var match=MatchManager.Instance;
            if(!network.IsConnectedClient || player==null || match==null || !match.IsSpawned) return;
            var playerTeam=player.GetComponent<PlayerTeam>();
            if(!selected) { playerTeam.SelectTeam(team); selected=true; settled=Time.realtimeSinceStartup+.5f; return; }
            if(playerTeam.teamId.Value!=team || Time.realtimeSinceStartup<settled) return;
            if(!File.Exists(FileName(".ready"))) File.WriteAllText(FileName(".ready"),"ready");
            if(!File.Exists(Path.Combine(directory,"send.request"))) return;
            if(stage==0) { match.SendChatRpc("peer-"+team+"-global",false); stage=1; sentAt=Time.realtimeSinceStartup; return; }
            if(stage==1 && Time.realtimeSinceStartup-sentAt>.8f)
            { match.SendChatRpc("peer-"+team+"-team",true); stage=2; sentAt=Time.realtimeSinceStartup; return; }
            if(stage!=2 || Time.realtimeSinceStartup-sentAt<2) return;
            Check(received.Contains("all-check <size=90>literal</size>"),"Missing host global message");
            Check(received.Contains("peer-0-global") && received.Contains("peer-1-global"),"Missing peer global message");
            Check(received.Contains("peer-"+team+"-team"),"Sender did not receive own team message");
            Check(!received.Contains("peer-"+(1-team)+"-team"),"Opponent team message leaked");
            Check(received.Contains("host-team-check")== (team==0),"Host team privacy failed");
            File.WriteAllText(FileName(".result"),"PASS: global delivery and team privacy; received="+string.Join("|",received));
            stage=3; enabled=false; network.Shutdown(); Application.Quit(0);
        }
        catch(Exception exception) { File.WriteAllText(FileName(".result"),"FAIL "+exception); enabled=false; Application.Quit(1); }
    }
}
#endif
