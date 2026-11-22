#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Opt-in separate-process check. Never starts in a normal game or release build.
public sealed class FeedbackClientProbe:MonoBehaviour
{
    string output;
    float deadline;
    bool requested,confirmed,observed,teleported;
    Vector3 before;
    float yaw;
    float selectionStarted=-1;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args=Environment.GetCommandLineArgs(); int flag=Array.IndexOf(args,"--feedback-probe");
        if(flag<0 || flag+1>=args.Length) return;
        var probe=new GameObject("FeedbackClientProbe").AddComponent<FeedbackClientProbe>();
        DontDestroyOnLoad(probe.gameObject); probe.output=args[flag+1]; probe.deadline=Time.realtimeSinceStartup+100;
        Application.runInBackground=true; GameSettings.ForceCursorLocked=true;
        if(Keyboard.current==null) InputSystem.AddDevice<Keyboard>();
        if(Mouse.current==null) InputSystem.AddDevice<Mouse>();
    }
    static void Check(bool pass,string reason) { if(!pass) throw new InvalidOperationException(reason); }
    void Update()
    {
        try
        {
            Check(Time.realtimeSinceStartup<deadline,"Client feedback timeout");
            var network=NetworkManager.Singleton;
            if(network==null) return;
            if(!requested)
            {
                network.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17791);
                Check(network.StartClient(),"Client start failed"); requested=true; return;
            }
            var match=MatchManager.Instance; var player=network.LocalClient?.PlayerObject;
            if(!network.IsConnectedClient || player==null || match==null || !match.IsSpawned) return;
            var hero=player.GetComponent<PlayerHero>();
            if(!confirmed)
            {
                // Simulate the real picker: wait for the joining state and parking transform to replicate.
                if(!hero.IsJoining || player.transform.position.y<350) return;
                if(selectionStarted<0) selectionStarted=Time.realtimeSinceStartup;
                if(Time.realtimeSinceStartup-selectionStarted<1) return;
                hero.GetComponent<PlayerTeam>().SelectTeam(1); hero.SelectHero(0); hero.ConfirmJoin();
                confirmed=true; return;
            }
            if(hero.IsJoining || hero.GetComponent<PlayerTeam>().teamId.Value!=1) return;
            if(player.GetComponent<Health>().currentHealth.Value<=0 || !MatchManager.ActiveMapBounds.Contains(player.transform.position)) return;
            if(!File.Exists(output+".ready")) File.WriteAllText(output+".ready","ready");
            var state=match.capturePresentation.Value;
            if(state.kind!=CapturePresentationState.Taken || state.point!=0) return;
            var movement=player.GetComponent<FirstPersonController>();
            float age=(float)(match.CaptureClock-state.started);
            if(!observed && state.Active(match.CaptureClock))
            {
                Check(match.CaptureCombatPaused && movement.ShootBlocked,"Remote combat not frozen");
                Check(CaptureTransitionView.ResultTitle(state,1)=="BOD A ZTRACEN","Wrong defender result");
                before=player.transform.position; yaw=player.transform.eulerAngles.y; observed=true;
            }
            if(!observed) return;
            InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(UnityEngine.InputSystem.Key.W));
            InputSystem.QueueDeltaStateEvent(Mouse.current.delta,new Vector2(100,0));
            if(age<.7f)
            {
                Check(Vector3.Distance(before,player.transform.position)<.1f,"Remote movement not frozen");
                Check(Mathf.Abs(Mathf.DeltaAngle(yaw,player.transform.eulerAngles.y))<.1f,"Remote look not frozen");
            }
            if(match.pointIndex.Value==1 && !teleported)
            {
                // Point state and respawn RPC are separate network messages; allow both to arrive.
                bool atSpawn=match.TryGetSpawn(1,out var spawn) && Vector3.Distance(spawn,player.transform.position)<6
                    && Vector3.Distance(before,player.transform.position)>2;
                if(atSpawn)
                {
                    Check(CaptureTransitionView.Opacity(age,state.duration)>.99f,"Remote teleport outside opaque fade");
                    teleported=true;
                }
                else Check(age<2.5f,$"Remote spawn did not move: age={age} position={player.transform.position} expected={spawn}");
            }
            if(teleported && !state.Active(match.CaptureClock))
            {
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
                File.WriteAllText(output,"PASS: separate client replicated result, defender text, freeze and covered teleport");
                enabled=false; network.Shutdown(); Application.Quit(0);
            }
        }
        catch(Exception exception) { File.WriteAllText(output,"FAIL "+exception); enabled=false; Application.Quit(1); }
    }
}
#endif
