using System;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Actual localhost host tests; scene and user input devices are left unchanged.
[InitializeOnLoad]
public static class SapaPlaytest
{
    const string Key = "SapaPlaytest.";
    static SapaPlaytest()
    {
        if (SessionState.GetBool(Key+"running",false) || SessionState.GetBool(Key+"exiting",false))
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += Error;
        }
    }
    public static void Deathmatch() => Run(false);
    public static void Capture() => Run(true);
    public static void Preview() => Run(false,true);
    public static void Lighting()
    {
        Run(false);
        SessionState.SetBool(Key+"lighting",true);
    }
    static void Run(bool capture,bool preview=false)
    {
        EditorSceneManager.OpenScene(SapaSetup.ScenePath);
        SessionState.SetBool(Key+"capture",capture);
        SessionState.SetBool(Key+"preview",preview);
        SessionState.SetBool(Key+"lighting",false);
        SessionState.SetBool(Key+"running",true);
        SessionState.SetBool(Key+"exiting",false);
        SessionState.SetInt(Key+"stage",0);
        SessionState.SetInt(Key+"errors",0);
        SessionState.SetFloat(Key+"started",(float)EditorApplication.timeSinceStartup);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= Error;
        Application.logMessageReceived += Error;
        EditorApplication.isPaused=false;
        EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Input()
    {
        if (!SessionState.GetBool(Key+"running",false)) return;
        if (Keyboard.current==null) InputSystem.AddDevice<Keyboard>();
        if (Mouse.current==null) InputSystem.AddDevice<Mouse>();
        Application.runInBackground=true;
        GameSettings.ForceCursorLocked=true;
    }
    static void Error(string message,string trace,LogType type)
    {
        if (SessionState.GetBool(Key+"running",false) && SessionState.GetInt(Key+"stage",0)>1
            && (type==LogType.Error || type==LogType.Exception || type==LogType.Assert))
        {
            SessionState.SetInt(Key+"errors",SessionState.GetInt(Key+"errors",0)+1);
            SessionState.SetString(Key+"lastError",message);
        }
    }
    static void Require(bool condition,string message) => SapaSetup.Require(condition,message);
    static void Tick()
    {
        if (SessionState.GetBool(Key+"exiting",false))
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool(Key+"exiting",false);
                EditorApplication.Exit(SessionState.GetInt(Key+"exit",1));
            }
            return;
        }
        if (!SessionState.GetBool(Key+"running",false)) return;
        try
        {
            Require(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+"started",0)<200,"Playtest prekrocil casovy limit.");
            if (!EditorApplication.isPlaying || Time.realtimeSinceStartup<2) return;
            Input();
            var network=NetworkManager.Singleton;
            if (network==null) return;
            int stage=SessionState.GetInt(Key+"stage",0);
            bool capture=SessionState.GetBool(Key+"capture",false);
            if (stage==0)
            {
                Require(!network.IsListening,"Uz bezi sitova relace.");
                network.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17789,"127.0.0.1");
                Require(network.StartHost(),"Host se nespustil.");
                Stage(1); return;
            }
            var match=MatchManager.Instance;
            if (match==null || !match.IsSpawned || network.LocalClient?.PlayerObject==null) return;
            if (stage==1)
            {
                match.SetMap(MatchManager.MapSapa);
                match.SetGameMode(capture?MatchManager.ModeAttack:MatchManager.ModeDeathmatch);
                match.SetCaptureSeconds(5);
                Require(match.MapIndex==MatchManager.MapSapa && match.IsAttackMode==capture,"Sapa neni dostupna pro rezim.");
                match.StartMatch();
                Require(!match.IsLobby,"Zapas se nespustil.");
                network.LocalClient.PlayerObject.GetComponent<Health>().ServerInvulnerable(120);
                match.ServerAddBot(0,capture?match.DefendTeam:1);
                Require(BotBrain.All.Count==1,"Bot se nepridal.");
                Stage(2); return;
            }
            var player=network.LocalClient.PlayerObject.transform;
            var bot=BotBrain.All[0].transform;
            if (stage==2)
            {
                if (Elapsed<4) return;
                Require(MatchManager.ActiveMapBounds.Contains(player.position) && MatchManager.ActiveMapBounds.Contains(bot.position),"Hrac nebo bot neni na Sape.");
                Require(Physics.Raycast(player.position+Vector3.up*.1f,Vector3.down,.7f,~0,QueryTriggerInteraction.Ignore),"Hrac propadl mapou.");
                Require(!player.GetComponent<FirstPersonController>().InTunnel,"Na Sape se spustil tunelovy sprint.");
                Require(BotNavigation.Ready,"Bot nema navigaci.");
                var mapRoot=GameObject.Find("Map_Sapa");
                Require(mapRoot!=null,"Chybi koren Sapy.");
                SapaSetup.ValidateMaterials(mapRoot);
                if (SessionState.GetBool(Key+"lighting",false))
                {
                    SapaSetup.CaptureCamera(player.GetComponent<FirstPersonController>().playerCamera,
                        Path.Combine(SapaSetup.WorkFolder,"lighting-map.png"));
                    SapaLightingValidation.Check(mapRoot.GetComponent<MapEnvironment>().sun);
                    Finish(0); return;
                }
                if (SessionState.GetBool(Key+"preview",false))
                {
                    var renderers=player.GetComponentsInChildren<Renderer>(true);
                    var enabled=new bool[renderers.Length];
                    for(int i=0;i<renderers.Length;i++) { enabled[i]=renderers[i].enabled; renderers[i].enabled=false; }
                    try { SapaSetup.CaptureCamera(player.GetComponent<FirstPersonController>().playerCamera,
                        Path.Combine(SapaSetup.WorkFolder,"menu-preview.png"),1280,720); }
                    finally { for(int i=0;i<renderers.Length;i++) renderers[i].enabled=enabled[i]; }
                    Finish(0); return;
                }
                if (capture)
                {
                    Require(match.CapturePointCount==2,"Sapa nema presne 2 body.");
                    SessionState.SetInt(Key+"firstAttacker",match.attackTeam.Value);
                    bot.GetComponent<FirstPersonController>().ServerHold(150);
                    Require(match.TryGetSpawn(match.DefendTeam,out var defender),"Chybi spawn obrancu.");
                    bot.GetComponent<FirstPersonController>().OwnerTeleport(defender,0);
                    Stage(4); return;
                }
                SessionState.SetFloat(Key+"walkX",player.position.x);
                SessionState.SetFloat(Key+"walkZ",player.position.z);
                var direction=bot.position-player.position;
                var path=new NavMeshPath();
                if (NavMesh.SamplePosition(player.position,out var a,3,BotNavigation.Filter)
                    && NavMesh.SamplePosition(bot.position,out var b,3,BotNavigation.Filter)
                    && NavMesh.CalculatePath(a.position,b.position,BotNavigation.Filter,path) && path.corners.Length>1)
                    direction=path.corners[1]-player.position;
                player.GetComponent<FirstPersonController>().OwnerTeleport(player.position,Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg);
                Stage(3); return;
            }
            if (stage==3)
            {
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(UnityEngine.InputSystem.Key.W));
                InputSystem.Update();
                if (Elapsed<.9) return;
                InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState()); InputSystem.Update();
                float walked=Vector2.Distance(new Vector2(player.position.x,player.position.z),
                    new Vector2(SessionState.GetFloat(Key+"walkX",0),SessionState.GetFloat(Key+"walkZ",0)));
                Debug.Log($"[Sapa test] Hrac usel {walked:0.000} m.");
                Require(walked>.2f,"Hrac se nemuze pohnout.");
                SapaSetup.CaptureCamera(player.GetComponent<FirstPersonController>().playerCamera,
                    Path.Combine(SapaSetup.WorkFolder,"player-view.png"));
                var sapaEnvironment=GameObject.Find("Map_Sapa").GetComponent<MapEnvironment>();
                MatchManager.ApplyMap(MatchManager.MapDust2);
                var dustEnvironment=GameObject.Find("Map_Dust2").GetComponent<MapEnvironment>();
                Require(RenderSettings.skybox==dustEnvironment.skybox,"Sapa prepsala prostredi Dust II pri prepnuti.");
                MatchManager.ApplyMap(MatchManager.MapSapa);
                Require(RenderSettings.skybox==sapaEnvironment.skybox && sapaEnvironment.sun.enabled
                    && RenderSettings.sun==sapaEnvironment.sun,
                    "Po navratu se neobnovilo prostredi Sapy.");
                Require(SessionState.GetInt(Key+"errors",0)==0,"Runtime chyba: "+SessionState.GetString(Key+"lastError",""));
                File.WriteAllText(Path.Combine(SapaSetup.WorkFolder,"playtest-deathmatch.json"),
                    "{\"host\":true,\"playerAndBot\":true,\"grounded\":true,\"playerWalked\":true,\"tunnelSprint\":false,\"environmentSwitchRestored\":true,\"runtimeErrors\":0}");
                Finish(0); return;
            }
            if (stage==4)
            {
                if (match.roundPhase.Value!=MatchManager.RoundAttack || match.pointIndex.Value!=0) return;
                SapaSetup.CaptureCamera(player.GetComponent<FirstPersonController>().playerCamera,
                    Path.Combine(SapaSetup.WorkFolder,"capture-player-view.png"));
                player.GetComponent<FirstPersonController>().OwnerTeleport(match.pointPosition.Value+Vector3.up*.06f,0);
                Stage(5); return;
            }
            if (stage==5)
            {
                if (match.pointIndex.Value!=1)
                {
                    Require(Elapsed<12,"Bod A se nedobyva; progress="+match.progress.Value);
                    return;
                }
                Require(match.pointPosition.Value!=GameObject.Find("CapturePoint_1").transform.position,"Bod se nepresunul.");
                Require(match.TryGetSpawn(match.attackTeam.Value,out var spawn) && Vector3.Distance(player.position,spawn)<6,"Po dobyti A se hrac nepresunul na novy spawn.");
                bot.GetComponent<FirstPersonController>().ServerHold(150);
                Stage(6); return;
            }
            if (stage==6)
            {
                if (match.roundPhase.Value!=MatchManager.RoundAttack || match.lockSecondsLeft.Value>0) return;
                player.GetComponent<FirstPersonController>().OwnerTeleport(match.pointPosition.Value+Vector3.up*.06f,0);
                Stage(7); return;
            }
            if (stage==7)
            {
                if (match.round.Value<2)
                {
                    Require(Elapsed<25,"Bod B nebo vymena stran se nedokoncila; progress="+match.progress.Value);
                    return;
                }
                Require(match.attackTeam.Value!=SessionState.GetInt(Key+"firstAttacker",-1),"Po kole se neprohodily strany.");
                Require(match.pointIndex.Value==0 && match.CapturePointCount==2,"Druhe kolo nezacalo u A.");
                Require(SessionState.GetInt(Key+"errors",0)==0,"Runtime chyba: "+SessionState.GetString(Key+"lastError",""));
                File.WriteAllText(Path.Combine(SapaSetup.WorkFolder,"playtest-capture.json"),
                    "{\"host\":true,\"captureStarted\":true,\"points\":2,\"pointACaptured\":true,\"pointBCaptured\":true,\"spawnsMoved\":true,\"sidesSwapped\":true,\"runtimeErrors\":0}");
                Finish(0);
            }
        }
        catch(Exception exception) { Debug.LogException(exception); Finish(1); }
    }
    static double Elapsed => EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+"changed",0);
    static void Stage(int stage)
    {
        SessionState.SetInt(Key+"stage",stage);
        SessionState.SetFloat(Key+"changed",(float)EditorApplication.timeSinceStartup);
        Debug.Log("[Sapa test] Stage "+stage);
    }
    static void Finish(int code)
    {
        Debug.Log(code==0?"[Sapa test] PASS":"[Sapa test] FAIL");
        SessionState.SetBool(Key+"running",false);
        SessionState.SetBool(Key+"exiting",true);
        SessionState.SetInt(Key+"exit",code);
        if (NetworkManager.Singleton!=null && NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
        GameSettings.ForceCursorLocked=false;
        EditorApplication.isPlaying=false;
    }
}
