using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class SocialCombatChecks
{
    const string Key="SocialCombatChecks.";
    public static string Output=>Path.GetFullPath("../_MIMO_GIT_K_PRESUNU/SocialCombat");
    static readonly List<System.Diagnostics.Process> peers=new List<System.Diagnostics.Process>();
    static SocialCombatChecks() { if(SessionState.GetBool(Key+"active",false)) EditorApplication.update+=Tick; }
    public static void BuildAndRun()
    {
        if(!Application.isBatchMode) throw new InvalidOperationException("Separate batch editor required.");
        Directory.CreateDirectory(Output);
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{"Assets/Scenes/SampleScene.unity"},
            locationPathName=Path.Combine(Output,"Client/SocialClient.exe"),target=BuildTarget.StandaloneWindows64,
            options=BuildOptions.Development|BuildOptions.CompressWithLz4 });
        if(report.summary.result!=BuildResult.Succeeded) throw new Exception("QA client build failed.");
        Run();
    }
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        foreach(string name in new[]{"peer-0.ready","peer-1.ready","peer-0.result","peer-1.result","send.request"})
            if(File.Exists(Path.Combine(Output,name))) File.Delete(Path.Combine(Output,name));
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(Key+"active",true); SessionState.SetBool(Key+"started",false); SessionState.SetBool(Key+"exiting",false);
        SessionState.SetFloat(Key+"start",(float)EditorApplication.timeSinceStartup);
        EditorApplication.update-=Tick; EditorApplication.update+=Tick; EditorApplication.isPlaying=true;
    }
    static void Tick()
    {
        if(SessionState.GetBool(Key+"exiting",false))
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(Key+"active",false); EditorApplication.update-=Tick;
            int code=SessionState.GetInt(Key+"exit",1);
            if(code==0)
            {
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{"Assets/Scenes/SampleScene.unity"},
                    locationPathName=Path.GetFullPath("../../compiled game/OverwatchBros.exe"),target=BuildTarget.StandaloneWindows64,
                    options=BuildOptions.CompressWithLz4 });
                File.WriteAllText(Path.Combine(Output,"release.txt"),report.summary.result+" errors="+report.summary.totalErrors+" bytes="+report.summary.totalSize);
                if(report.summary.result!=BuildResult.Succeeded) code=1;
            }
            EditorApplication.Exit(code); return;
        }
        if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+"start",0)>240) { Finish("FAIL: timeout",1); return; }
        if(!EditorApplication.isPlaying || SessionState.GetBool(Key+"started",false) || NetworkManager.Singleton==null) return;
        SessionState.SetBool(Key+"started",true);
        new GameObject("SocialCombatDriver").AddComponent<SocialCombatDriver>().Run();
    }
    public static void StartPeers()
    {
        for(int team=0;team<2;team++)
        {
            var start=new System.Diagnostics.ProcessStartInfo(Path.Combine(Output,"Client/SocialClient.exe"),
                "-batchmode -nographics -logFile \""+Path.Combine(Output,"peer-"+team+".log")+"\" --social-probe \""+Output+"\" "+team);
            start.CreateNoWindow=true; start.WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden;
            peers.Add(System.Diagnostics.Process.Start(start));
        }
    }
    public static void Finish(string message,int code)
    {
        File.WriteAllText(Path.Combine(Output,"result.txt"),message); Debug.Log("[SocialCombat] "+message);
        foreach(var peer in peers) { if(!peer.HasExited) peer.Kill(); peer.Dispose(); } peers.Clear();
        if(NetworkManager.Singleton!=null && NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
        GameSettings.ForceCursorLocked=false;
        SessionState.SetBool(Key+"exiting",true); SessionState.SetInt(Key+"exit",code); EditorApplication.isPlaying=false;
    }
}

public sealed class SocialCombatDriver:MonoBehaviour
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    int errors;
    bool failed;
    string lastError;
    public void Run() { Application.logMessageReceived+=Log; StartCoroutine(Execute()); }
    void OnDestroy()=>Application.logMessageReceived-=Log;
    void Log(string message,string trace,LogType type) { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) { errors++; lastError=message; } }
    IEnumerator Execute()
    {
        yield return Guard(Checks());
        if(!failed) SocialCombatChecks.Finish(errors==0?"PASS: chat (3 processes, global/team privacy, input isolation, dismissal), tire group damage, walls and friendly blockers; runtime errors=0":"FAIL: "+lastError,errors==0?0:1);
    }
    IEnumerator Guard(IEnumerator iterator)
    {
        while(!failed)
        {
            object current=null; bool next=false;
            try { next=iterator.MoveNext(); if(next) current=iterator.Current; }
            catch(Exception exception) { failed=true; SocialCombatChecks.Finish(exception.ToString(),1); }
            if(failed || !next) yield break;
            if(current is IEnumerator nested) yield return Guard(nested); else yield return current;
        }
    }
    static IEnumerator Wait(Func<bool> condition,float seconds)
    {
        float until=Time.realtimeSinceStartup+seconds;
        while(!condition()) { Assert.Less(Time.realtimeSinceStartup,until,"Timed out waiting for social/combat check."); yield return null; }
    }
    static IEnumerator Press(UnityEngine.InputSystem.Key key)
    {
        InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(key)); yield return null;
        InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState()); yield return null;
    }
    static void Screenshot(string name)
    {
        PlaytestFeedbackCheckBatch.CaptureUI(name);
        File.Copy(Path.Combine(PlaytestFeedbackCheckBatch.Output,name+".png"),Path.Combine(SocialCombatChecks.Output,name+".png"),true);
    }
    IEnumerator Checks()
    {
        Application.runInBackground=true; GameSettings.ForceCursorLocked=true;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var qaKeyboard=InputSystem.AddDevice<Keyboard>("SocialQAKeyboard");
        qaKeyboard.MakeCurrent();
        if(Mouse.current==null) InputSystem.AddDevice<Mouse>();
        Assert.AreEqual("a b c",MatchManager.CleanChatText(" a\nb\tc\0 "));
        Assert.LessOrEqual(MatchManager.CleanChatText(new string('a',500)).Length,200);
        var network=NetworkManager.Singleton; PlayerHero.PreferredName="Lenit QA";
        network.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17794,"127.0.0.1");
        Assert.IsTrue(network.StartHost());
        yield return Wait(()=>MatchManager.Instance!=null && MatchManager.Instance.IsSpawned && network.LocalClient?.PlayerObject!=null,30);
        var match=MatchManager.Instance; var player=network.LocalClient.PlayerObject;
        player.GetComponent<PlayerTeam>().teamId.Value=0;
        match.SetMap(MatchManager.MapDomasov); match.SetGameMode(MatchManager.ModeDeathmatch); match.SetScoreToWin(100);
        SocialCombatChecks.StartPeers();
        yield return Wait(()=>File.Exists(Path.Combine(SocialCombatChecks.Output,"peer-0.ready")) && File.Exists(Path.Combine(SocialCombatChecks.Output,"peer-1.ready")),65);
        match.SendChatRpc("all-check <size=90>literal</size>",false);
        yield return new WaitForSecondsRealtime(.8f);
        match.SendChatRpc("host-team-check",true);
        File.WriteAllText(Path.Combine(SocialCombatChecks.Output,"send.request"),"go");
        yield return Wait(()=>File.Exists(Path.Combine(SocialCombatChecks.Output,"peer-0.result")) && File.Exists(Path.Combine(SocialCombatChecks.Output,"peer-1.result")),20);
        foreach(int team in new[]{0,1}) Assert.That(File.ReadAllText(Path.Combine(SocialCombatChecks.Output,"peer-"+team+".result")),Does.StartWith("PASS"));
        var chat=TextChatUI.Instance; Assert.IsNotNull(chat);
        var messages=new List<string>(); foreach(var line in chat.Messages) messages.Add(line.text);
        Assert.Contains("peer-0-team",messages); Assert.IsFalse(messages.Contains("peer-1-team"),"Opponent team channel reached host.");
        yield return Wait(()=>network.ConnectedClientsIds.Count==1,10);
        int honza=Array.FindIndex(HeroRegistry.All,h=>h.name=="Honza");
        player.GetComponent<PlayerHero>().SelectHero(honza);
        yield return null;
        match.StartMatch();
        var movement=player.GetComponent<FirstPersonController>();
        movement.ClearForces();
        yield return new WaitForSeconds(.5f);
        Debug.Log($"[SocialCombat] before Enter: available={Object.FindAnyObjectByType<MatchUI>().ChatAvailable} chatActive={chat.isActiveAndEnabled} keyboard={Keyboard.current.enabled} focused={chat.Draft.isFocused}");
        yield return Press(UnityEngine.InputSystem.Key.Enter);
        Assert.IsTrue(TextChatUI.IsTyping,"Enter must open chat.");
        Assert.IsFalse(GameSettings.CursorLocked); Assert.IsTrue(movement.ShootBlocked);
        Vector3 before=player.transform.position; float yaw=player.transform.eulerAngles.y;
        InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(UnityEngine.InputSystem.Key.W,UnityEngine.InputSystem.Key.Q,UnityEngine.InputSystem.Key.R,UnityEngine.InputSystem.Key.Space));
        InputSystem.QueueDeltaStateEvent(Mouse.current.delta,new Vector2(100,0));
        Assert.IsTrue(chat.Draft.isFocused,"Chat must focus the text editor.");
        // TMP consumes native GUI character events; QueueTextEvent alone does not
        // generate that OS event queue in a hidden batch editor.
        foreach(char c in "wasd ahoj") chat.Draft.ProcessEvent(new Event { type=EventType.KeyDown,character=c,keyCode=KeyCode.None });
        yield return null; yield return null;
        Assert.Less(new Vector2(player.transform.position.x-before.x,player.transform.position.z-before.z).magnitude,.02f,"Typing moved the player.");
        Assert.Less(Mathf.Abs(Mathf.DeltaAngle(yaw,player.transform.eulerAngles.y)),.1f,"Typing moved the camera.");
        Assert.IsFalse(HeroInput.Pressed(movement,HeroInput.Key.Q));
        InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
        yield return null;
        Assert.That(chat.Draft.text,Does.Contain("wasd ahoj"),"Input field did not receive typed characters.");
        yield return Press(UnityEngine.InputSystem.Key.Tab);
        Assert.IsTrue(chat.TeamOnly,"Tab must select team channel.");
        Assert.IsTrue(TextChatUI.IsTyping);
        Assert.IsFalse(chat.Draft.textComponent.richText);
        chat.Draft.text="Kryju vám záda, jdeme na bod!";
        TextChatUI.Receive("Viktor","Pneumatika připravená. Jdeme spolu?",0,true);
        yield return null; Screenshot("chat-team");
        yield return Press(UnityEngine.InputSystem.Key.Enter);
        Assert.IsFalse(TextChatUI.IsTyping); Assert.IsTrue(chat.Visible);
        yield return Press(UnityEngine.InputSystem.Key.Enter);
        Assert.IsTrue(TextChatUI.IsTyping);
        yield return Press(UnityEngine.InputSystem.Key.Tab);
        Assert.IsFalse(chat.TeamOnly);
        chat.Draft.text="Čau všichni, dáme další kolo?";
        yield return null; Screenshot("chat-global");
        yield return Press(UnityEngine.InputSystem.Key.Escape);
        Assert.IsFalse(TextChatUI.IsTyping);
        Assert.IsFalse(Object.FindAnyObjectByType<MatchUI>().GetType().GetField("settings",Private).GetValue(Object.FindAnyObjectByType<MatchUI>()) is SettingsMenuUI settings && settings.IsOpen,"Escape from chat opened settings.");
        float hiddenAt=Time.unscaledTime+TextChatUI.DisplaySeconds+.1f;
        BlastChecks(match,player);
        yield return Wait(()=>Time.unscaledTime>hiddenAt,12);
        Assert.IsFalse(chat.Visible,"Chat must disappear after its display interval.");
        yield return CursorMenuChecks(chat);
        match.BackToLobby();
        Cursor.visible=false;
        yield return null; yield return null;
        Assert.IsTrue(Cursor.visible,"Lobby must restore the cursor after chat.");
        network.Shutdown();
        yield return Wait(()=>!network.IsListening && !network.ShutdownInProgress,10);
        Cursor.visible=false;
        yield return null; yield return null;
        Assert.IsTrue(Cursor.visible,"Main menu must restore the cursor after leaving the server.");
        Debug.Log("[SocialCombat] Cursor PASS: chat send/cancel, Escape settings, resume, lobby and main menu.");
    }

    static IEnumerator CursorMenuChecks(TextChatUI chat)
    {
        var ui=Object.FindAnyObjectByType<MatchUI>();
        var settings=(SettingsMenuUI)typeof(MatchUI).GetField("settings",Private).GetValue(ui);
        var crosshair=(GameObject)typeof(MatchUI).GetField("crosshair",Private).GetValue(ui);
        foreach(bool send in new[]{true,false})
        {
            GameSettings.ForceCursorLocked=true;
            yield return Press(UnityEngine.InputSystem.Key.Enter);
            Assert.IsTrue(TextChatUI.IsTyping);
            Assert.IsTrue(Cursor.visible,"Chat must show the cursor.");
            chat.Draft.text=send?"cursor regression":"cancel me";
            yield return Press(send?UnityEngine.InputSystem.Key.Enter:UnityEngine.InputSystem.Key.Escape);
            Assert.IsFalse(TextChatUI.IsTyping);
            yield return null;
            Assert.IsTrue(crosshair.activeSelf,"Closing chat must restore the aiming crosshair.");
            // Batch mode cannot lock the OS cursor: use its real unlocked state
            // for settings, including the stale visibility left by the old chat.
            GameSettings.ForceCursorLocked=false;
            Cursor.visible=false;
            yield return Press(UnityEngine.InputSystem.Key.Escape);
            yield return Wait(()=>settings.IsOpen,3);
            yield return null;
            Assert.IsTrue(settings.Root.activeInHierarchy);
            Assert.IsTrue(Cursor.visible,"Settings cursor remained hidden after chat.");
            Assert.AreEqual(CursorLockMode.None,Cursor.lockState);
            // Exercise the same callback as the menu's Continue button.
            GameSettings.ForceCursorLocked=true;
            typeof(SettingsMenuUI).GetMethod("CloseAndResume",Private).Invoke(settings,null);
            yield return null; yield return null;
            Assert.IsFalse(settings.IsOpen);
            Assert.IsTrue(crosshair.activeSelf);
            yield return new WaitForSecondsRealtime(.7f);
        }
    }

    static void BlastChecks(MatchManager match,NetworkObject player)
    {
        var ability=player.GetComponent<BoulderAbility>(); Assert.IsNotNull(ability);
        var definition=AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/Data/Boulder_Data.asset");
        Assert.AreEqual(350,definition.power); Assert.AreEqual(8,definition.radius);
        ability.Configure(definition);
        var origin=new Vector3(20000,500,20000);
        var targets=new List<Health>();
        for(int i=0;i<5;i++)
        {
            match.ServerAddBot(i,i==4?0:1);
            var bot=BotBrain.All[BotBrain.All.Count-1]; bot.enabled=false;
            bot.GetComponent<FirstPersonController>().enabled=false;
            bot.GetComponent<PlayerHero>().enabled=false;
            var h=bot.GetComponent<Health>(); h.enabled=false; h.maxHealth=1000; h.ResetHealth();
            bot.transform.position=origin+new Vector3(0,0,i==4?1:2+i*1.5f);
            targets.Add(h);
        }
        // An additional hit collider must not cause damage twice.
        var extra=new GameObject("ExtraHitCollider"); extra.transform.SetParent(targets[2].transform,false);
        extra.transform.localPosition=Vector3.up*1.2f; extra.AddComponent<SphereCollider>().radius=.2f;
        var casing=new GameObject("TireCasingQA"); casing.transform.position=origin+new Vector3(0,1,.55f);
        casing.AddComponent<BoxCollider>().size=new Vector3(1,2,.3f);
        casing.AddComponent<BoulderHitbox>().Owner=ability;
        typeof(BoulderAbility).GetField("boulder",Private).SetValue(ability,casing);
        Physics.SyncTransforms();
        var front=targets[0].GetComponent<Collider>();
        Assert.IsFalse(Combat.HasLineOfSight(origin+Vector3.up*1.2f,front),"QA casing must actually obstruct the ray.");
        Assert.IsTrue(Combat.HasLineOfSight(origin+Vector3.up*1.2f,front,casing.transform));
        typeof(BoulderAbility).GetMethod("ExplodeServerRpc",Private).Invoke(ability,new object[]{origin});
        for(int i=0;i<4;i++)
        {
            float distance=Vector3.Distance(origin+Vector3.up*.6f,targets[i].transform.position);
            float expected=350*Mathf.Lerp(1,.5f,distance/8);
            Assert.That(1000-targets[i].currentHealth.Value,Is.EqualTo(expected).Within(.1),"Every lined-up enemy must take one full falloff-scaled hit.");
        }
        Assert.AreEqual(1000,targets[4].currentHealth.Value,"Friendly player must not be damaged or shield the enemies behind them.");
        var wall=new GameObject("BlastWallQA"); wall.transform.position=origin+new Vector3(0,1,1.5f);
        wall.AddComponent<BoxCollider>().size=new Vector3(4,4,.2f);
        foreach(var h in targets) h.ResetHealth();
        Physics.SyncTransforms();
        typeof(BoulderAbility).GetMethod("ExplodeServerRpc",Private).Invoke(ability,new object[]{origin});
        for(int i=0;i<4;i++) Assert.AreEqual(1000,targets[i].currentHealth.Value,"A real wall must still block the blast.");
        Object.DestroyImmediate(wall);
        foreach(var h in targets) { h.maxHealth=180; h.ResetHealth(); }
        Physics.SyncTransforms();
        typeof(BoulderAbility).GetMethod("ExplodeServerRpc",Private).Invoke(ability,new object[]{origin});
        for(int i=0;i<4;i++) Assert.AreEqual(0,targets[i].currentHealth.Value,"A close group must all take lethal damage.");
        typeof(BoulderAbility).GetField("boulder",Private).SetValue(ability,null);
        Object.DestroyImmediate(casing);
        match.ServerRemoveBots();
        Debug.Log("[SocialCombat] Tire PASS: 4 aligned enemies, friendly blocker, source casing, duplicate collider, solid wall and group kill.");
    }
}
