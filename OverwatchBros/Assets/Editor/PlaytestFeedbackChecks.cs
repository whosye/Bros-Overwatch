using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEditor.Build.Reporting;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public sealed class PlaytestFeedbackChecks
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Set(object target,string field,object value) => target.GetType().GetField(field,Fields).SetValue(target,value);
    static T Get<T>(object target,string field) => (T)target.GetType().GetField(field,Fields).GetValue(target);

    [Test]
    public void MouseDisplacementIsFrameIndependentAndFocusSafe()
    {
        foreach(int fps in new[]{30,60,144,240})
        {
            float yaw=0;
            for(int i=0;i<fps;i++) yaw+=MouseLookInput.Degrees(new Vector2(1200f/fps,0),2).x;
            Assert.That(yaw,Is.EqualTo(40).Within(.001),"Sensitivity must not depend on framerate.");
        }
        var gate=new MouseLookInput();
        Assert.IsFalse(gate.TryRead(new Vector2(4000,0),true,true,1,.016f,out _),"Discard lock warp.");
        Assert.IsTrue(gate.TryRead(new Vector2(120,0),true,true,2,.1f,out var delta));
        Assert.AreEqual(4,MouseLookInput.Degrees(delta,2).x,"A slow frame must not multiply the angle.");
        Assert.IsFalse(gate.TryRead(new Vector2(4000,0),true,false,3,.016f,out _));
        Assert.IsFalse(gate.TryRead(new Vector2(4000,0),true,true,4,.016f,out _),"Discard focus warp.");
        Assert.IsFalse(gate.TryRead(new Vector2(4000,0),true,true,5,.5f,out _),"Discard accumulated input after a long stall.");
        Assert.IsTrue(gate.TryRead(new Vector2(4000,0),true,true,6,.016f,out delta),"Preserve a genuine fast flick.");
        Assert.IsFalse(gate.TryRead(new Vector2(float.NaN,0),true,true,7,.016f,out _));
        Assert.IsFalse(gate.TryRead(Vector2.one,false,true,8,.016f,out _));
    }

    [Test]
    public void DirectionReversalBrakesWithoutReducingTopSpeed()
    {
        foreach(int fps in new[]{30,60,144,240})
        {
            var velocity=Vector3.right*6;
            velocity=PlayerMotion.Step(velocity,Vector3.left*6,6,.12f,1f/fps);
            Assert.Greater(velocity.x,0,"A/D reversal must not instantly flip full speed.");
            for(int i=0;i<fps;i++) velocity=PlayerMotion.Step(velocity,Vector3.left*6,6,.12f,1f/fps);
            Assert.That(velocity.x,Is.EqualTo(-6).Within(.001));
            for(int i=0;i<Mathf.CeilToInt(fps*.4f);i++) velocity=PlayerMotion.Step(velocity,Vector3.zero,6,.12f,1f/fps);
            Assert.That(velocity.magnitude,Is.LessThan(.001),"Ground friction must settle to a complete stop.");
        }
        Assert.That(PlayerMotion.Step(Vector3.right*10,Vector3.right*3,3,.12f,.016f).magnitude,Is.AtMost(3));
    }

    [Test]
    public void CounterStrafeHasNoFastReturnBranchOrStoredWallSpeed()
    {
        const float dt=.001f;
        float braking=PlayerMotion.Step(Vector3.left*3,Vector3.right*6,6,.12f,dt).x+3;
        float accelerating=PlayerMotion.Step(Vector3.right*3,Vector3.right*6,6,.12f,dt).x-3;
        Assert.GreaterOrEqual(braking,accelerating,"Returning toward residual velocity must not enable a faster acceleration branch.");
        foreach(int fps in new[]{30,60,144,240})
        {
            Vector3 a=Vector3.left*6,b=Vector3.right*6;
            for(int i=0;i<fps*4;i++)
            {
                float time=(float)i/fps;
                float sign=(Mathf.FloorToInt(time/.035f)%2)==0?1:-1;
                a=PlayerMotion.Step(a,Vector3.right*(sign*6),6,.12f,1f/fps);
                b=PlayerMotion.Step(b,Vector3.left*(sign*6),6,.12f,1f/fps);
                Assert.That((a+b).magnitude,Is.LessThan(.0001f),"A-D-A and D-A-D must be mirror images.");
                Assert.That(a.magnitude,Is.AtMost(6.0001f),"Rapid taps cannot build extra speed.");
            }
        }
        Assert.AreEqual(Vector3.zero,PlayerMotion.AfterCollision(Vector3.right*6,Vector3.zero,.016f));
        var resolved=PlayerMotion.AfterCollision(Vector3.right*6,new Vector3(.016f,1,0),.016f);
        Assert.That(resolved.x,Is.EqualTo(1).Within(.001));
        Assert.AreEqual(0,resolved.y,"A step up must not become horizontal momentum.");
    }

    [Test]
    public void WallCollisionDoesNotStoreSpeedForTheNextStrafe()
    {
        GameObject actor=null,wall=null,floor=null;
        try
        {
            var origin=new Vector3(20000,300,-20000);
            actor=new GameObject("MovementCollisionTest"); actor.transform.position=origin;
            var body=actor.AddComponent<CharacterController>(); body.radius=.3f; body.height=2;
            body.center=Vector3.up; body.skinWidth=.02f; body.minMoveDistance=0;
            wall=new GameObject("Wall"); wall.transform.position=origin+new Vector3(.85f,1,0);
            wall.AddComponent<BoxCollider>().size=new Vector3(.1f,4,10);
            floor=new GameObject("Ground"); floor.transform.position=origin+Vector3.down*.1f;
            floor.AddComponent<BoxCollider>().size=new Vector3(20,.2f,20);
            Physics.SyncTransforms();
            var velocity=Vector3.zero; bool hitWall=false;
            for(int i=0;i<90;i++)
            {
                velocity=PlayerMotion.Step(velocity,Vector3.right*6,6,.12f,1f/60);
                Vector3 before=actor.transform.position;
                var flags=body.Move(velocity/60+Vector3.down*.03f);
                if((flags&CollisionFlags.Sides)!=0)
                {
                    hitWall=true;
                    velocity=PlayerMotion.AfterCollision(velocity,actor.transform.position-before,1f/60);
                }
            }
            Assert.IsTrue(hitWall);
            Assert.Less(velocity.magnitude,.1f,"Blocked controller must not retain running speed.");
            float againstWall=actor.transform.position.x;
            velocity=PlayerMotion.Step(velocity,Vector3.left*6,6,.12f,1f/60);
            body.Move(velocity/60+Vector3.down*.03f);
            Assert.Less(actor.transform.position.x,againstWall,"Moving away must not first fight imaginary wall momentum.");
        }
        finally
        {
            if(actor!=null) Object.DestroyImmediate(actor);
            if(wall!=null) Object.DestroyImmediate(wall);
            if(floor!=null) Object.DestroyImmediate(floor);
            Physics.SyncTransforms();
        }
    }

    [Test]
    public void CapturePresentationHasAnOpaqueTeleportWindowAndRelativeResults()
    {
        Assert.AreEqual(0,CaptureTransitionView.Opacity(0,3.2f));
        Assert.AreEqual(1,CaptureTransitionView.Opacity(.9f,3.2f));
        Assert.AreEqual(0,CaptureTransitionView.Opacity(3.2f,3.2f));
        var state=new CapturePresentationState { kind=CapturePresentationState.Taken,team=0,point=0,started=10,duration=3.2f };
        Assert.IsTrue(state.Active(11)); Assert.IsFalse(state.Active(14));
        Assert.AreEqual("BOD A DOBYT",CaptureTransitionView.ResultTitle(state,0));
        Assert.AreEqual("BOD A ZTRACEN",CaptureTransitionView.ResultTitle(state,1));
        state.kind=CapturePresentationState.Defended;
        Assert.AreEqual("BOD A UBRÁNĚN",CaptureTransitionView.ResultTitle(state,0));
        Assert.AreEqual("BOD A NEDOBYT",CaptureTransitionView.ResultTitle(state,1));
        state.kind=CapturePresentationState.SwitchSides; state.point=-1;
        Assert.AreEqual("NOVÉ KOLO",CaptureTransitionView.ResultTitle(state,1));
    }

    public IEnumerator GameFlow()
    {
        Application.runInBackground=true;
        GameSettings.ForceCursorLocked=true;
        if(Keyboard.current==null) InputSystem.AddDevice<Keyboard>();
        if(Mouse.current==null) InputSystem.AddDevice<Mouse>();

        yield return Wait(()=>NetworkManager.Singleton!=null && NetworkManager.Singleton.GetComponent<UnityTransport>()!=null,10);
        var network=NetworkManager.Singleton;
        Debug.Log("[Feedback network] "+network.name+" ready");
        Assert.IsNotNull(network);
        var transport=network.GetComponent<UnityTransport>();
        Assert.IsNotNull(transport,"The game scene must contain its configured UnityTransport.");
        transport.SetConnectionData("127.0.0.1",17791,"127.0.0.1");
        Debug.Log("[Feedback network] transport configured");
        Assert.IsTrue(network.StartHost());
        Debug.Log("[Feedback network] host started");
        yield return Wait(()=>MatchManager.Instance!=null && MatchManager.Instance.IsSpawned && network.LocalClient?.PlayerObject!=null,30);

        var voiceObject=new GameObject("FeedbackVoiceTest");
        var voice=voiceObject.AddComponent<HeroVoice>();
        var clip=AudioClip.Create("FeedbackVoice",44100*2,1,44100,false);
        var hero=ScriptableObject.CreateInstance<HeroDefinition>();
        hero.idleLines=new[]{clip};
        var ability=ScriptableObject.CreateInstance<AbilityDefinition>();
        ability.voiceLines=new[]{clip}; hero.ability=ability;
        voice.PlayReplay(hero,VoiceKind.Idle,0,0,0,false);
        yield return null;
        var source=voiceObject.GetComponentInChildren<AudioSource>();
        Assert.IsNotNull(source); Assert.IsTrue(source.isPlaying);
        Assert.AreEqual(1,source.spatialBlend); Assert.AreEqual(1.3f,source.minDistance);
        Assert.Less(source.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(.25f),.6f);
        Assert.Less(VoiceSpatialProfile.CutoffAt(25),VoiceSpatialProfile.CutoffAt(2));
        Assert.IsTrue(source.GetComponent<AudioLowPassFilter>().enabled);
        Assert.IsTrue(source.GetComponent<AudioReverbFilter>().enabled);
        Assert.AreEqual(1,source.pitch);
        voice.PlayReplay(hero,VoiceKind.Idle,0,0,0,true);
        Assert.AreEqual(0,source.spatialBlend);
        Assert.IsFalse(source.GetComponent<AudioLowPassFilter>().enabled);
        Assert.IsFalse(source.GetComponent<AudioReverbFilter>().enabled);
        voice.PlayReplay(hero,VoiceKind.Ability,0,0,0,false);
        Assert.AreEqual(1,source.spatialBlend,"Remote ultimate should retain a direction.");
        Assert.Greater(source.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(1),.2f,"Preserve global warning.");
        for(int i=0;i<=100;i++) Assert.That(source.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(i/100f),
            Is.InRange(.299f,1.001f),"Warning rolloff must not overshoot below its audibility floor.");
        Object.Destroy(voiceObject); Object.Destroy(hero); Object.Destroy(ability); Object.Destroy(clip);

        var match=MatchManager.Instance;
        match.SetMap(MatchManager.MapSapa); match.SetGameMode(MatchManager.ModeAttack); match.SetCaptureSeconds(5);
        match.StartMatch();
        yield return null;
        var player=network.LocalClient.PlayerObject;
        var movement=player.GetComponent<FirstPersonController>();
        var health=player.GetComponent<Health>();
        player.GetComponent<PlayerTeam>().teamId.Value=0;
        player.GetComponent<PlayerHero>().ServerClearJoining();
        match.ServerAddBot(0,1);
        yield return Wait(()=>BotBrain.All.Count==1,30);
        var bot=BotBrain.All[0];
        bot.GetComponent<FirstPersonController>().ServerHold(180);
        health.ServerInvulnerable(180);
        bool client=PlaytestFeedbackCheckBatch.StartClient();
        if(client) yield return Wait(()=>File.Exists(PlaytestFeedbackCheckBatch.ClientResult+".ready"),65);
        Set(match,"lockTimer",.02f);
        yield return Wait(()=>match.roundPhase.Value==MatchManager.RoundAttack,5);
        movement.ClearForces();
        movement.OwnerTeleport(match.pointPosition.Value+Vector3.up*.06f,0);
        yield return Wait(()=>match.capturePresentation.Value.kind==CapturePresentationState.Taken,15);
        var presentation=match.capturePresentation.Value;
        Vector3 oldPosition=player.transform.position;
        float oldYaw=player.transform.eulerAngles.y;
        Assert.IsTrue(match.CaptureCombatPaused); Assert.IsTrue(movement.ShootBlocked);
        Assert.AreEqual(0,match.pointIndex.Value,"Do not teleport/change the point before the fade.");
        InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(UnityEngine.InputSystem.Key.W));
        InputSystem.QueueDeltaStateEvent(Mouse.current.delta,new Vector2(500,0));
        yield return new WaitForSeconds(.25f);
        Assert.Less(Vector3.Distance(oldPosition,player.transform.position),.05f,"Freeze movement during announcement.");
        Assert.Less(Mathf.Abs(Mathf.DeltaAngle(oldYaw,player.transform.eulerAngles.y)),.1f,"Freeze look during announcement.");
        InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState());
        yield return Wait(()=>match.pointIndex.Value==1,5);
        float age=(float)(match.CaptureClock-presentation.started);
        Assert.That(age,Is.InRange(.85f,2.65f),"Teleport inside the opaque part of the fade.");
        Assert.AreEqual(1,CaptureTransitionView.Opacity(age,presentation.duration));
        Assert.IsTrue(match.TryGetSpawn(0,out var spawn));
        Assert.Less(Vector3.Distance(player.transform.position,spawn),6);
        PlaytestFeedbackCheckBatch.CaptureUI("point-captured");
        Set(health,"invulnerableUntil",0f);
        float hp=health.currentHealth.Value;
        health.TakeDamage(20);
        Assert.AreEqual(hp,health.currentHealth.Value,"Transition must prevent projectile damage.");
        health.ServerInvulnerable(180);
        yield return Wait(()=>!match.capturePresentation.Value.Active(match.CaptureClock),6);
        if(client)
        {
            yield return Wait(()=>File.Exists(PlaytestFeedbackCheckBatch.ClientResult),15);
            Assert.That(File.ReadAllText(PlaytestFeedbackCheckBatch.ClientResult),Does.StartWith("PASS"));
        }
        Set(match,"lockTimer",.02f);
        yield return Wait(()=>match.lockSecondsLeft.Value==0,5);
        movement.ClearForces(); movement.OwnerTeleport(match.pointPosition.Value+Vector3.up*.06f,0);
        yield return Wait(()=>match.capturePresentation.Value.point==1 && match.capturePresentation.Value.Active(match.CaptureClock),15);
        Assert.AreEqual(MatchManager.RoundAttack,match.roundPhase.Value,"Final point result must precede round end.");
        yield return Wait(()=>match.roundPhase.Value==MatchManager.RoundIntermission,6);
        Assert.IsTrue(match.CaptureCombatPaused);
        Set(match,"phaseTimer",.02f);
        yield return Wait(()=>match.capturePresentation.Value.kind==CapturePresentationState.SwitchSides,5);
        Assert.AreEqual(1,match.round.Value,"Round switch waits for fade.");
        yield return Wait(()=>match.round.Value==2,5);
        Assert.AreEqual(1,match.attackTeam.Value);
        Assert.AreEqual(1,CaptureTransitionView.Opacity((float)(match.CaptureClock-match.capturePresentation.Value.started),3.2f));
        PlaytestFeedbackCheckBatch.CaptureUI("switch-sides");
        yield return Wait(()=>!match.capturePresentation.Value.Active(match.CaptureClock),6);
        Set(match,"lockTimer",.02f);
        Set(match,"timer",.02f);
        movement.ClearForces(); movement.OwnerTeleport(spawn,0);
        yield return Wait(()=>match.capturePresentation.Value.kind==CapturePresentationState.Defended,8);
        Assert.AreEqual("BOD A UBRÁNĚN",CaptureTransitionView.ResultTitle(match.capturePresentation.Value,0));
        yield return Wait(()=>match.IsOver,6);
        Assert.AreEqual(0,match.winnerTeam.Value);
        yield return Wait(()=>!match.PotgPending,20);
        yield return CheckFilm(match,player,movement);
        network.Shutdown();
        GameSettings.ForceCursorLocked=false;
        yield return null;
    }

    public IEnumerator FilmOnlyFlow()
    {
        Application.runInBackground=true; GameSettings.ForceCursorLocked=true;
        if(Keyboard.current==null) InputSystem.AddDevice<Keyboard>();
        if(Mouse.current==null) InputSystem.AddDevice<Mouse>();
        var network=NetworkManager.Singleton;
        network.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17791,"127.0.0.1");
        Assert.IsTrue(network.StartHost());
        yield return Wait(()=>MatchManager.Instance!=null && MatchManager.Instance.IsSpawned && network.LocalClient?.PlayerObject!=null,30);
        var player=network.LocalClient.PlayerObject;
        yield return CheckFilm(MatchManager.Instance,player,player.GetComponent<FirstPersonController>());
        network.Shutdown(); GameSettings.ForceCursorLocked=false;
        yield return null;
    }

    static IEnumerator CheckFilm(MatchManager match,NetworkObject player,FirstPersonController movement)
    {
        // C -> D starts the authored film immediately, with exactly one fade after it.
        match.BackToLobby(); match.SetMap(MatchManager.MapDomasov); match.SetGameMode(MatchManager.ModeAttack); match.StartMatch();
        yield return new WaitForSeconds(.5f);
        FoldingFloor filmFloor=null;
        foreach(var candidate in Object.FindObjectsByType<FoldingFloor>(FindObjectsSortMode.None))
            if(candidate.HasCutscene && candidate.unlockAfter<match.CapturePointCount) { filmFloor=candidate; break; }
        Assert.IsNotNull(filmFloor,"Expected the authored collapsing-floor film in Domasov.");
        int nextPoint=filmFloor.unlockAfter;
        typeof(MatchManager).GetMethod("ServerApplyPoint",Fields).Invoke(match,new object[]{nextPoint-1,0f});
        match.roundPhase.Value=MatchManager.RoundAttack;
        movement.ClearForces();
        yield return new WaitForSeconds(.4f);
        typeof(MatchManager).GetMethod("ServerPointCaptured",Fields).Invoke(match,null);
        Assert.AreEqual(nextPoint,match.pointIndex.Value,"Film must start immediately, without a preliminary result screen.");
        Assert.IsTrue(match.CaptureCombatPaused);
        Assert.Greater(Get<float>(match,"cutsceneTeleportAt"),Time.time);
        Assert.IsFalse(match.capturePresentation.Value.Active(match.CaptureClock),"No transfer overlay before the film.");
        yield return Wait(()=>Object.FindAnyObjectByType<FloorCutscene>()!=null,1);
        yield return Wait(()=>match.capturePresentation.Value.kind==CapturePresentationState.NextPoint,20);
        yield return new WaitForSeconds(.75f);
        Assert.AreEqual(1,CaptureTransitionView.Opacity((float)(match.CaptureClock-match.capturePresentation.Value.started),1.8f));
        Assert.IsTrue(match.TryGetSpawn(0,out var filmSpawn));
        Assert.Less(Vector3.Distance(player.transform.position,filmSpawn),6,"Teleport must be covered by the single fade after the film.");
    }

    static IEnumerator Wait(Func<bool> condition,float seconds)
    {
        float deadline=Time.realtimeSinceStartup+seconds;
        while(!condition()) { Assert.Less(Time.realtimeSinceStartup,deadline,"Timed out waiting for feedback test state."); yield return null; }
    }
}

[InitializeOnLoad]
public static class PlaytestFeedbackCheckBatch
{
    public static string Output => Path.GetFullPath("../_MIMO_GIT_K_PRESUNU/PlaytestFeedback");
    public static string ClientResult => Path.Combine(Output,"client-result.txt");
    static System.Diagnostics.Process clientProcess;
    static PlaytestFeedbackCheckBatch()
    {
        if(SessionState.GetBool("FeedbackChecks.Direct",false)) EditorApplication.update+=DirectTick;
    }
    public static void BuildClient()
    {
        BuildClientFiles();
        EditorApplication.Exit(0);
    }
    public static void BuildAndRunChecks()
    {
        BuildClientFiles();
        RunDirect();
    }
    static void BuildClientFiles()
    {
        Directory.CreateDirectory(Output);
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{"Assets/Scenes/SampleScene.unity"},
            locationPathName=Path.Combine(Output,"Client/FeedbackClient.exe"),target=BuildTarget.StandaloneWindows64,
            options=BuildOptions.Development|BuildOptions.CompressWithLz4 });
        if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Feedback client build failed.");
    }
    public static bool StartClient()
    {
        string executable=Path.Combine(Output,"Client/FeedbackClient.exe");
        if(!File.Exists(executable)) return false;
        if(File.Exists(ClientResult)) File.Delete(ClientResult);
        if(File.Exists(ClientResult+".ready")) File.Delete(ClientResult+".ready");
        var start=new System.Diagnostics.ProcessStartInfo(executable,
            "-batchmode -nographics -logFile \""+Path.Combine(Output,"client.log")+"\" --feedback-probe \""+ClientResult+"\"");
        start.CreateNoWindow=true; start.WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden;
        clientProcess=System.Diagnostics.Process.Start(start);
        return true;
    }
    public static void CaptureUI(string name)
    {
        var ui=Object.FindAnyObjectByType<MatchUI>();
        var canvas=ui.GetComponentInChildren<Canvas>();
        var go=new GameObject("FeedbackUICamera"); var camera=go.AddComponent<Camera>();
        camera.transform.position=new Vector3(0,-10000,0); camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=Color.black; camera.nearClipPlane=.1f; camera.farClipPlane=10;
        var rt=new RenderTexture(1280,720,24); camera.targetTexture=rt;
        var oldMode=canvas.renderMode; var oldCamera=canvas.worldCamera;
        var scaler=canvas.GetComponent<CanvasScaler>(); bool oldEnabled=scaler.enabled; float oldScale=canvas.scaleFactor;
        var oldActive=RenderTexture.active;
        try
        {
            scaler.enabled=false; canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera;
            canvas.planeDistance=1; canvas.scaleFactor=2f/3f;
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active=rt;
            var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG()); Object.DestroyImmediate(image);
        }
        finally
        {
            RenderTexture.active=oldActive; canvas.renderMode=oldMode; canvas.worldCamera=oldCamera;
            canvas.scaleFactor=oldScale; scaler.enabled=oldEnabled;
            camera.targetTexture=null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        }
    }
    public static void RunDirect()
    {
        if(!Application.isBatchMode) throw new InvalidOperationException("Batch session required.");
        var checks=new PlaytestFeedbackChecks();
        checks.MouseDisplacementIsFrameIndependentAndFocusSafe();
        checks.DirectionReversalBrakesWithoutReducingTopSpeed();
        checks.CapturePresentationHasAnOpaqueTeleportWindowAndRelativeResults();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool("FeedbackChecks.Direct",true);
        SessionState.SetBool("FeedbackChecks.FilmOnly",false);
        SessionState.SetBool("FeedbackChecks.BuildAfter",false);
        SessionState.SetBool("FeedbackChecks.Started",false);
        SessionState.SetBool("FeedbackChecks.Exiting",false);
        SessionState.SetFloat("FeedbackChecks.StartTime",(float)EditorApplication.timeSinceStartup);
        EditorApplication.update-=DirectTick; EditorApplication.update+=DirectTick;
        EditorApplication.isPlaying=true;
    }
    public static void RunFilmAndBuild()
    {
        RunDirect();
        SessionState.SetBool("FeedbackChecks.FilmOnly",true);
        SessionState.SetBool("FeedbackChecks.BuildAfter",true);
    }
    static void DirectTick()
    {
        if(SessionState.GetBool("FeedbackChecks.Exiting",false))
        {
            if(!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool("FeedbackChecks.Direct",false);
                EditorApplication.update-=DirectTick;
                int code=SessionState.GetInt("FeedbackChecks.ExitCode",1);
                if(code==0 && SessionState.GetBool("FeedbackChecks.BuildAfter",false))
                {
                    SessionState.SetBool("FeedbackChecks.BuildAfter",false);
                    try { Type.GetType("ScopedReleaseBuild, Assembly-CSharp-Editor").GetMethod("BuildOnly").Invoke(null,null); }
                    catch(Exception exception) { Debug.LogException(exception); code=1; }
                }
                EditorApplication.Exit(code);
            }
            return;
        }
        if(EditorApplication.timeSinceStartup-SessionState.GetFloat("FeedbackChecks.StartTime",0)>240)
        { DirectFinish("Timeout",1); return; }
        if(!EditorApplication.isPlaying || SessionState.GetBool("FeedbackChecks.Started",false)
            || NetworkManager.Singleton==null || NetworkManager.Singleton.GetComponent<UnityTransport>()==null) return;
        SessionState.SetBool("FeedbackChecks.Started",true);
        new GameObject("FeedbackCheckDriver").AddComponent<FeedbackCheckDriver>().StartChecks();
    }
    public static void DirectFinish(string message,int code)
    {
        string directory=Path.GetFullPath("../_MIMO_GIT_K_PRESUNU/PlaytestFeedback");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"direct-result.txt"),message);
        if(clientProcess!=null && !clientProcess.HasExited) clientProcess.Kill();
        clientProcess?.Dispose(); clientProcess=null;
        Debug.Log("[Feedback direct] "+message);
        GameSettings.ForceCursorLocked=false;
        SessionState.SetBool("FeedbackChecks.Exiting",true);
        SessionState.SetInt("FeedbackChecks.ExitCode",code);
        if(NetworkManager.Singleton!=null && NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
        EditorApplication.isPlaying=false;
    }
}

public sealed class FeedbackCheckDriver:MonoBehaviour
{
    bool failed;
    int runtimeErrors;
    string lastError;
    public void StartChecks()
    {
        Application.logMessageReceived+=Log;
        StartCoroutine(Execute());
    }
    void OnDestroy() => Application.logMessageReceived-=Log;
    void Log(string message,string trace,LogType type)
    {
        if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) { runtimeErrors++; lastError=message; }
    }
    IEnumerator Execute()
    {
        var checks=new PlaytestFeedbackChecks();
        bool filmOnly=SessionState.GetBool("FeedbackChecks.FilmOnly",false);
        yield return Guard(filmOnly?checks.FilmOnlyFlow():checks.GameFlow());
        if(!failed) PlaytestFeedbackCheckBatch.DirectFinish(runtimeErrors==0
            ? filmOnly?"PASS: C -> D immediate film, no preliminary overlay, one covered teleport after film; runtime errors=0"
                : "PASS: mouse, movement, voice DSP, capture fade, freeze, teleport, sides, defence and collapsing-floor film; runtime errors=0"
            : "FAIL: runtime errors="+runtimeErrors+" "+lastError,runtimeErrors==0?0:1);
    }
    IEnumerator Guard(IEnumerator iterator)
    {
        while(!failed)
        {
            object current=null;
            bool next=false;
            try { next=iterator.MoveNext(); if(next) current=iterator.Current; }
            catch(Exception exception) { failed=true; PlaytestFeedbackCheckBatch.DirectFinish(exception.ToString(),1); }
            if(failed || !next) yield break;
            if(current is IEnumerator nested) yield return Guard(nested);
            else yield return current;
        }
    }
}
