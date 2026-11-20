#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Opt-in real-process join test. Dormant unless launched with --menu-join-probe.
public class MenuJoinProbe : MonoBehaviour
{
    string output;
    bool requested,confirmed;
    float started,confirmedAt;
    int mode;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var args=Environment.GetCommandLineArgs(); int flag=Array.IndexOf(args,"--menu-join-probe");
        if(flag<0||flag+2>=args.Length)return;
        var probe=new GameObject("MenuJoinProbe").AddComponent<MenuJoinProbe>();
        probe.output=args[flag+1]; probe.mode=int.Parse(args[flag+2]); probe.started=Time.realtimeSinceStartup;
        Application.runInBackground=true;
        if(Keyboard.current==null)InputSystem.AddDevice<Keyboard>();
        if(Mouse.current==null)InputSystem.AddDevice<Mouse>();
    }
    void Update()
    {
        try
        {
            if(Time.realtimeSinceStartup-started>65)throw new Exception("Join timeout");
            var network=NetworkManager.Singleton;
            if(network==null)return;
            if(!requested)
            {
                PlayerHero.PreferredName="Menu test client";
                if(!GameConnection.Join("127.0.0.1",GameConnection.Port))throw new Exception(GameConnection.LastError);
                requested=true; return;
            }
            var match=MatchManager.Instance;
            if(!network.IsConnectedClient||network.LocalClient?.PlayerObject==null||match==null||!match.IsSpawned)return;
            var hero=network.LocalClient.PlayerObject.GetComponent<PlayerHero>();
            if(!confirmed)
            {
                if(!hero.IsJoining)throw new Exception("Late arrival skipped team selection");
                if(match.gameMode.Value!=mode)throw new Exception("Wrong replicated game mode");
                hero.GetComponent<PlayerTeam>().SelectTeam(1);
                hero.SelectHero(0);
                hero.ConfirmJoin(); confirmed=true; confirmedAt=Time.realtimeSinceStartup; return;
            }
            if(Time.realtimeSinceStartup-confirmedAt<4)return;
            if(hero.IsJoining)throw new Exception("ConfirmJoin did not reach server");
            if(hero.GetComponent<PlayerTeam>().teamId.Value!=1)throw new Exception("Chosen team did not replicate");
            if(!MatchManager.ActiveMapBounds.Contains(hero.transform.position)||hero.transform.position.y>200)throw new Exception("Player did not enter selected map");
            File.WriteAllText(output,"PASS mode="+mode+" team=1 position="+hero.transform.position);
            enabled=false; network.Shutdown(); Application.Quit(0);
        }
        catch(Exception e)
        {
            File.WriteAllText(output,"FAIL "+e); enabled=false; Application.Quit(1);
        }
    }
}
#endif
