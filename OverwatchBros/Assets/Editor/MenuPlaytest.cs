using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Run with -batchmode -executeMethod MenuPlaytest.Run (graphics enabled for screenshots).
[InitializeOnLoad]
public static class MenuPlaytest
{
    const string Key="MenuPlaytest.";
    static readonly string Output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../artifacts/Menu"));
    static readonly string[] StringKeys={"servers.history.v1","nick","gameName"};
    static readonly string[] IntKeys={"host.map","host.mode","host.kills","host.capture","testCooldowns","settings.musicEnabled"};
    static readonly string[] FloatKeys={"settings.volume","settings.voiceVolume","settings.ambientVolume","settings.musicVolume","settings.sensitivity"};
    static MenuPlaytest()
    {
        if(SessionState.GetBool(Key+"running",false)||SessionState.GetBool(Key+"exiting",false))EditorApplication.update+=Tick;
    }
    public static void BuildClient()
    {
        MenuTypography.EnsureFonts();
        Directory.CreateDirectory(Output);
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{"Assets/Scenes/SampleScene.unity"},
            locationPathName=Path.Combine(Output,"Client/MenuClient.exe"),target=BuildTarget.StandaloneWindows64,
            options=BuildOptions.Development|BuildOptions.CompressWithLz4 });
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Probe build failed: "+report.summary.result);
        Debug.Log("[Menu test] Client build PASS");
    }
    [MenuItem("BrosOverwatch/Menu/Build Windows Game")]
    public static void BuildGame()
    {
        MenuTypography.EnsureFonts();
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes=new[]{"Assets/Scenes/SampleScene.unity"},
            locationPathName=Path.Combine(Output,"Build/BrosOverwatch.exe"),
            target=BuildTarget.StandaloneWindows64, options=BuildOptions.CompressWithLz4
        });
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Game build failed: "+report.summary.result);
        Debug.Log("[Menu test] Release build PASS");
    }
    public static void RunVisual()
    {
        Run(); SessionState.SetBool(Key+"visualOnly",true);
    }
    public static void Run()
    {
        SessionState.SetBool(Key+"visualOnly",false);
        MenuTypography.EnsureFonts();
        Directory.CreateDirectory(Output); Backup();
        PlayerPrefs.DeleteKey("servers.history.v1"); ServerHistory.Reload();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(Key+"running",true); SessionState.SetInt(Key+"stage",0);
        SessionState.SetFloat(Key+"started",(float)EditorApplication.timeSinceStartup);
        EditorApplication.update-=Tick; EditorApplication.update+=Tick;
        EditorApplication.isPlaying=true;
    }
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target);
    static void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
    static void Stage(int value) { SessionState.SetInt(Key+"stage",value); SessionState.SetFloat(Key+"at",(float)EditorApplication.timeSinceStartup); }
    static bool Wait(float seconds)=>EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+"at",0)<seconds;
    static void Tick()
    {
        if(SessionState.GetBool(Key+"exiting",false))
        {
            if(!EditorApplication.isPlayingOrWillChangePlaymode) { Restore(); SessionState.SetBool(Key+"exiting",false); EditorApplication.Exit(SessionState.GetInt(Key+"exit",1)); }
            return;
        }
        if(!SessionState.GetBool(Key+"running",false))return;
        try
        {
            Require(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+"started",0)<220,"Test timeout");
            if(!EditorApplication.isPlaying||Time.realtimeSinceStartup<3)return;
            if(Keyboard.current==null)InputSystem.AddDevice<Keyboard>();
            if(Mouse.current==null)InputSystem.AddDevice<Mouse>();
            var ui=UnityEngine.Object.FindAnyObjectByType<MatchUI>(); if(ui==null)return;
            var menu=Field<MainMenuUI>(ui,"menu"); var settings=Field<SettingsMenuUI>(ui,"settings");
            var network=NetworkManager.Singleton; int stage=SessionState.GetInt(Key+"stage",0);
            if(stage==0)
            {
                Require(ServerHistory.TryParse(" localhost:7777 ",out var host,out var port,out _)&&host=="127.0.0.1"&&port==7777,"Endpoint normalization");
                foreach(string bad in new[]{"","0.0.0.0","255.255.255.255","224.1.1.1","127.0.0.1:0","127.0.0.1:65536","bad address"})Require(!ServerHistory.TryParse(bad,out _,out _,out _),"Accepted bad endpoint "+bad);
                MainMenuUI.SaveName("Viktor"); menu.Navigate("Home"); Stage(1); return;
            }
            if(stage==1)
            {
                if(Wait(.5f))return; Capture(ui,"01-main-menu",1920,1080);
                Capture(ui,"09-main-720p",1280,720); Capture(ui,"10-main-4x3",1024,768);
                var musicButton=Field<Button>(menu,"musicButton");
                Require(musicButton.GetComponentInChildren<MenuMusicIcon>().canvasRenderer!=null,"Music icon has no renderer");
                bool oldMusic=GameSettings.MusicEnabled;
                GameSettings.MusicEnabled=false; menu.Tick(); Capture(ui,"14-music-muted",1920,1080);
                GameSettings.MusicEnabled=oldMusic; menu.Tick();
                menu.Navigate("Host"); Stage(2); return;
            }
            if(stage==2)
            {
                if(Wait(.5f))return; Capture(ui,"02-host",1920,1080);
                HostOptions.Map=1;
                typeof(MainMenuUI).GetMethod("RefreshHost",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(menu,null);
                Require(Field<Image>(menu,"mapPreview").sprite!=null,"Hanamura thumbnail missing");
                Capture(ui,"11-host-hanamura",1920,1080);
                HostOptions.Map=2; HostOptions.Mode=0;
                typeof(MainMenuUI).GetMethod("RefreshHost",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(menu,null);
                Require(Field<Image>(menu,"mapPreview").sprite!=null,"Dust thumbnail missing");
                Capture(ui,"12-host-prdust",1920,1080);
                var e=ServerHistory.Add("127.0.0.1:7777","U Honzy",out _);
                Require(e!=null&&ServerHistory.LastJoined==null,"Adding server must not count as a visit");
                ServerHistory.Add("localhost:7777","",out _); Require(ServerHistory.All.Count==1,"Duplicate server");
                ServerHistory.Reload(); Require(ServerHistory.All.Count==1,"History did not persist");
                menu.Navigate("Servers"); var d=LanDiscovery.Instance;
                int calls=d.RefreshCount; for(int i=0;i<50;i++)d.Refresh(ServerHistory.All);
                Require(d.RefreshCount==calls,"Refresh spam bypassed cooldown");
                menu.Navigate("Home"); menu.Navigate("Servers"); Require(d.RefreshCount==calls,"Reopening bypassed cooldown");
                Stage(3); return;
            }
            if(stage==3)
            {
                if(Wait(2.3f))return; Capture(ui,"03-servers-offline",1920,1080);
                menu.Navigate("Home"); settings.Open(false);
                Capture(ui,"04-settings",1920,1080);
                settings.Select(0); Capture(ui,"05-settings-game",1920,1080);
                settings.Select(2); Capture(ui,"06-settings-graphics",1920,1080);
                settings.Select(3); Capture(ui,"07-settings-controls",1920,1080);
                Capture(ui,"13-controls-720p",1280,720);
                settings.Close(); menu.Navigate("Servers");
                if(SessionState.GetBool(Key+"visualOnly",false))
                {
                    MenuPreview.ValidateLabels(settings.Root);
                    File.WriteAllText(Path.Combine(Output,"visual-result.txt"),"PASS: compact UI, fonts, map previews, mute icon, controls at 1080p and 720p.");
                    Finish(0); return;
                }
                var browser=Field<ServerBrowserUI>(menu,"servers");
                Field<Button>(browser,"join").onClick.Invoke();
                Stage(4); return;
            }
            if(stage==4)
            {
                if(network.IsListening)network.Shutdown();
                if(network.ShutdownInProgress)return;
                Require(ServerHistory.LastJoined==null,"Failed join became last visited");
                menu.Navigate("Host"); HostOptions.Map=0; HostOptions.Mode=0; HostOptions.Kills=37; HostOptions.CaptureSeconds=25;
                HostOptions.ApplyOnSpawn=true; Require(GameConnection.Host("Menu smoke host"),GameConnection.LastError);
                Stage(5); return;
            }
            if(stage==12)
            {
                if(network.ShutdownInProgress||Wait(.5f))return;
                menu.SetVisible(true); menu.Navigate("Home");
                Capture(ui,"09-main-720p",1280,720); Capture(ui,"10-main-4x3",1024,768);
                File.WriteAllText(Path.Combine(Output,"result.txt"),"PASS: UI, persistence, validation, cooldown, UDP status, host options, late joins in both modes.");
                Finish(0);
            }
            var match=MatchManager.Instance;
            if(match==null||!match.IsSpawned||network.LocalClient?.PlayerObject==null)return;
            if(stage==5)
            {
                Require(match.scoreToWinSynced.Value==37&&match.captureSeconds.Value==25&&match.MapIndex==0,"Host options not applied");
                LanDiscovery.GetOrCreate().Refresh(ServerHistory.All,false); Stage(6); return;
            }
            if(stage==6)
            {
                if(Wait(.5f))return;
                var d=LanDiscovery.Instance;
                if(!d.Games.TryGetValue("127.0.0.1:7777",out var game)||!d.IsCurrent(game)) { if(d.Cooldown<=0)d.Refresh(ServerHistory.All,false); if(Wait(9))return; throw new Exception("Live UDP query failed"); }
                Require(game.phase=="Lobby"&&game.players>=1&&game.map=="Chaty","Wrong live server metadata");
                Stage(7); return;
            }
            if(stage==7)
            {
                if(Wait(.5f))return;
                match.StartMatch(); Require(!match.IsLobby,"Deathmatch did not start");
                GameSettings.ForceCursorLocked=true; StartClient(0); Stage(8); return;
            }
            if(stage==8||stage==10)
            {
                int mode=stage==8?0:1; string path=Path.Combine(Output,"join-"+mode+".txt");
                if(!File.Exists(path))return;
                string result;
                try { result=File.ReadAllText(path); } catch(IOException) { return; }
                if(string.IsNullOrEmpty(result))return;
                Require(result.StartsWith("PASS"),result);
                if(network.ConnectedClientsIds.Count>1)return;
                if(stage==8)
                {
                    match.BackToLobby(); match.SetGameMode(MatchManager.ModeAttack); match.StartMatch();
                    Require(match.IsAttackMode&&!match.IsLobby,"Capture match did not start"); StartClient(1); Stage(10); return;
                }
                GameSettings.ForceCursorLocked=false; Cursor.lockState=CursorLockMode.None; settings.Open(true); Stage(11); return;
            }
            if(stage==11)
            {
                if(Wait(.5f))return; Capture(ui,"08-escape",1920,1080); settings.Close();
                network.Shutdown(); Stage(12); return;
            }

        }
        catch(Exception e) { File.WriteAllText(Path.Combine(Output,"result.txt"),"FAIL "+e); Debug.LogException(e); Finish(1); }
    }
    static void StartClient(int mode)
    {
        string result=Path.Combine(Output,"join-"+mode+".txt"); if(File.Exists(result))File.Delete(result);
        var start=new System.Diagnostics.ProcessStartInfo(Path.Combine(Output,"Client/MenuClient.exe"),
            "-batchmode -nographics -logFile \""+Path.Combine(Output,"client-"+mode+".log")+"\" --menu-join-probe \""+result+"\" "+mode);
        start.CreateNoWindow=true; start.WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden;
        System.Diagnostics.Process.Start(start);
    }
    static void Capture(MatchUI ui,string name,int width,int height)
    {
        Field<SettingsMenuUI>(ui,"settings").Tick();
        var canvas=ui.GetComponentInChildren<Canvas>();
        var go=new GameObject("Menu capture camera"); var camera=go.AddComponent<Camera>();
        camera.transform.position=new Vector3(0,-10000,0); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
        camera.nearClipPlane=.1f; camera.farClipPlane=10;
        var rt=new RenderTexture(width,height,24); camera.targetTexture=rt;
        var oldMode=canvas.renderMode; var oldCamera=canvas.worldCamera;
        var scaler=canvas.GetComponent<CanvasScaler>(); bool oldEnabled=scaler.enabled; float oldScale=canvas.scaleFactor;
        scaler.enabled=false;
        canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
        canvas.scaleFactor=Mathf.Min(width/1920f,height/1080f);
        Canvas.ForceUpdateCanvases(); camera.Render();
        var oldActive=RenderTexture.active; RenderTexture.active=rt;
        var texture=new Texture2D(width,height,TextureFormat.RGB24,false); texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply();
        File.WriteAllBytes(Path.Combine(Output,name+".png"),texture.EncodeToPNG());
        RenderTexture.active=oldActive; canvas.renderMode=oldMode; canvas.worldCamera=oldCamera; canvas.scaleFactor=oldScale; scaler.enabled=oldEnabled;
        UnityEngine.Object.DestroyImmediate(texture); camera.targetTexture=null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go);
    }
    static void Finish(int code)
    {
        GameSettings.ForceCursorLocked=false; SessionState.SetBool(Key+"running",false); SessionState.SetBool(Key+"exiting",true); SessionState.SetInt(Key+"exit",code);
        EditorApplication.isPlaying=false;
    }
    static void Backup()
    {
        foreach(string k in StringKeys) { SessionState.SetBool(Key+"had"+k,PlayerPrefs.HasKey(k)); SessionState.SetString(Key+"old"+k,PlayerPrefs.GetString(k)); }
        foreach(string k in IntKeys) { SessionState.SetBool(Key+"had"+k,PlayerPrefs.HasKey(k)); SessionState.SetInt(Key+"old"+k,PlayerPrefs.GetInt(k)); }
        foreach(string k in FloatKeys) { SessionState.SetBool(Key+"had"+k,PlayerPrefs.HasKey(k)); SessionState.SetFloat(Key+"old"+k,PlayerPrefs.GetFloat(k)); }
    }
    static void Restore()
    {
        foreach(string k in StringKeys) { if(SessionState.GetBool(Key+"had"+k,false))PlayerPrefs.SetString(k,SessionState.GetString(Key+"old"+k,"")); else PlayerPrefs.DeleteKey(k); }
        foreach(string k in IntKeys) { if(SessionState.GetBool(Key+"had"+k,false))PlayerPrefs.SetInt(k,SessionState.GetInt(Key+"old"+k,0)); else PlayerPrefs.DeleteKey(k); }
        foreach(string k in FloatKeys) { if(SessionState.GetBool(Key+"had"+k,false))PlayerPrefs.SetFloat(k,SessionState.GetFloat(Key+"old"+k,0)); else PlayerPrefs.DeleteKey(k); }
        PlayerPrefs.Save(); ServerHistory.Reload();
    }
}
