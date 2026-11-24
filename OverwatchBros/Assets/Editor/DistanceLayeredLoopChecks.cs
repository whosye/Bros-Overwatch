using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class DistanceLayeredLoopChecks
{
    const string Key="DistanceLoopCheck.";
    public static string Output=>Path.GetFullPath("../_MIMO_GIT_K_PRESUNU/BigLezAudio");
    static DistanceLayeredLoopChecks() { if(SessionState.GetBool(Key+"active",false)) EditorApplication.update+=Tick; }
    public static void Run()
    {
        if(!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch editor.");
        SessionState.SetFloat(Key+"lodBias",QualitySettings.lodBias);
        Directory.CreateDirectory(Output);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        BigLezAudioSetup.PrepareClip();
        var savedLoop=Object.FindAnyObjectByType<DistanceLayeredLoop>();
        if(savedLoop==null || savedLoop.clip==null)
            throw new InvalidOperationException("Saved BigLez scene has a missing audio reference; the playback test must not silently repair it.");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/BigLez/BigLez.prefab");
        var prefabLoop=prefab!=null?prefab.GetComponent<DistanceLayeredLoop>():null;
        if(prefabLoop==null || prefabLoop.clip!=savedLoop.clip)
            throw new InvalidOperationException("Saved BigLez prefab must reference the same existing clip as the placed model.");
        Debug.Log("[BigLezAudio] Saved scene and prefab references verified before playback; no scene repair performed.");
        SessionState.SetBool(Key+"active",true);SessionState.SetBool(Key+"started",false);SessionState.SetBool(Key+"exiting",false);
        SessionState.SetFloat(Key+"time",(float)EditorApplication.timeSinceStartup);
        EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        EditorApplication.isPlaying=true;
    }
    static void Tick()
    {
        if(SessionState.GetBool(Key+"exiting",false))
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(Key+"active",false);EditorApplication.update-=Tick;
            QualitySettings.lodBias=SessionState.GetFloat(Key+"lodBias",QualitySettings.lodBias);
            int code=SessionState.GetInt(Key+"exit",1);
            if(code==0)
            {
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes=new[]{"Assets/Scenes/SampleScene.unity"},target=BuildTarget.StandaloneWindows64,
                    locationPathName=Path.GetFullPath("../../compiled game/OverwatchBros.exe"),options=BuildOptions.CompressWithLz4 });
                File.WriteAllText(Path.Combine(Output,"release.txt"),report.summary.result+" errors="+report.summary.totalErrors+" bytes="+report.summary.totalSize);
                if(report.summary.result!=BuildResult.Succeeded) code=1;
            }
            EditorApplication.Exit(code);return;
        }
        if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+"time",0)>180) { Finish("FAIL: timeout",1);return; }
        if(!EditorApplication.isPlaying || SessionState.GetBool(Key+"started",false) || NetworkManager.Singleton==null) return;
        SessionState.SetBool(Key+"started",true);
        new GameObject("DistanceLoopDriver").AddComponent<DistanceLoopDriver>().Run();
    }
    public static void Finish(string result,int code)
    {
        File.WriteAllText(Path.Combine(Output,"result.txt"),result);Debug.Log("[BigLezAudio] "+result);
        if(NetworkManager.Singleton!=null && NetworkManager.Singleton.IsListening) NetworkManager.Singleton.Shutdown();
        SessionState.SetBool(Key+"exiting",true);SessionState.SetInt(Key+"exit",code);EditorApplication.isPlaying=false;
    }
}

public sealed class DistanceLoopDriver:MonoBehaviour
{
    float oldAmbient,oldMaster;
    int errors;
    bool failed;
    public void Run()
    {
        oldAmbient=GameSettings.AmbientVolume;oldMaster=AudioListener.volume;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        if(Keyboard.current==null) InputSystem.AddDevice<Keyboard>("Loop QA keyboard").MakeCurrent();
        if(Mouse.current==null) InputSystem.AddDevice<Mouse>("Loop QA mouse").MakeCurrent();
        Application.logMessageReceived+=Log;
        StartCoroutine(Execute());
    }
    void Log(string message,string trace,LogType type) { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) errors++; }
    void OnDestroy() { Application.logMessageReceived-=Log;GameSettings.AmbientVolume=oldAmbient;AudioListener.volume=oldMaster; }
    IEnumerator Execute()
    {
        yield return Guard(Checks());
        if(!failed) Finish(errors==0?"PASS: actual placed BigLez, mono shared clip, synchronized playback, clean/blended/wet/distant gains, ambient mute, disable/restart, map switch and disconnect; runtime errors=0":"FAIL: runtime errors="+errors,errors==0?0:1);
    }
    void Finish(string result,int code)
    {
        GameSettings.AmbientVolume=oldAmbient;AudioListener.volume=oldMaster;
        DistanceLayeredLoopChecks.Finish(result,code);
    }
    IEnumerator Guard(IEnumerator iterator)
    {
        while(!failed)
        {
            object current=null;bool next=false;
            try { next=iterator.MoveNext();if(next) current=iterator.Current; }
            catch(Exception exception) { failed=true;Finish(exception.ToString(),1); }
            if(failed || !next) yield break;
            if(current is IEnumerator nested) yield return Guard(nested);else yield return current;
        }
    }
    static IEnumerator Wait(Func<bool> condition,float seconds=12)
    {
        float until=Time.realtimeSinceStartup+seconds;
        while(!condition()) { Assert.Less(Time.realtimeSinceStartup,until,"Timed out waiting for loop playback.");yield return null; }
    }
    static void Synchronized(DistanceLayeredLoop loop)
    {
        int difference=Math.Abs(loop.CleanSource.timeSamples-loop.DistantSource.timeSamples);
        difference=Math.Min(difference,loop.clip.samples-difference);
        AudioSettings.GetDSPBufferSize(out int buffer,out _);
        Assert.LessOrEqual(difference,buffer*2,"The two layers drifted apart.");
    }
    IEnumerator Checks()
    {
        var loop=Object.FindAnyObjectByType<DistanceLayeredLoop>();Assert.IsNotNull(loop);
        Assert.AreEqual("BigLez",loop.name);
        Assert.AreEqual(1,Object.FindObjectsByType<DistanceLayeredLoop>(FindObjectsSortMode.None).Length);
        Assert.AreEqual(1,loop.clip.channels);Assert.Greater(loop.clip.length,90);
        Assert.AreEqual(4,loop.clearDistance);Assert.AreEqual(18,loop.distantDistance);Assert.AreEqual(55,loop.audibleDistance);
        Assert.AreEqual(new Vector2(1,0),loop.MixAt(0));Assert.AreEqual(Vector2.zero,loop.MixAt(55));
        float previousPower=1,previousRatio=0;
        for(float d=0;d<55;d+=.1f)
        {
            Vector2 mix=loop.MixAt(d);float power=mix.magnitude;
            Assert.LessOrEqual(power,previousPower+.0001f);Assert.GreaterOrEqual(mix.x,0);Assert.GreaterOrEqual(mix.y,0);
            float total=mix.x+mix.y;
            // At the audible cutoff both gains tend to zero. A denominator floor would
            // invent a falling wet ratio even though the actual crossfade remains monotonic.
            float ratio=total>0f?mix.y/total:previousRatio;
            Assert.GreaterOrEqual(ratio,previousRatio-.0001f);previousPower=power;previousRatio=ratio;
        }
        yield return null;Assert.IsFalse(loop.Running,"Main menu must be silent.");
        var network=NetworkManager.Singleton;
        network.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17796,"127.0.0.1");
        Assert.IsTrue(network.StartHost());
        yield return Wait(()=>MatchManager.Instance!=null && MatchManager.Instance.IsSpawned && network.LocalClient?.PlayerObject!=null);
        var match=MatchManager.Instance;match.SetMap(MatchManager.MapDomasov);match.SetGameMode(MatchManager.ModeDeathmatch);
        yield return null;Assert.IsFalse(loop.Running,"Lobby must be silent.");
        match.StartMatch();
        yield return new WaitForSecondsRealtime(.5f);
        network.LocalClient.PlayerObject.GetComponent<FirstPersonController>().enabled=false;
        foreach(var listener in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) listener.enabled=false;
        var ear=new GameObject("Loop QA listener").AddComponent<AudioListener>();
        Vector3 center=loop.transform.TransformPoint(loop.emitterOffset);
        ear.transform.position=center+Vector3.right*2;
        GameSettings.AmbientVolume=1;AudioListener.volume=0; // No test audio on the user's speakers.
        yield return Wait(()=>loop.Running && loop.CleanSource.isPlaying && loop.DistantSource.isPlaying);
        yield return new WaitForSecondsRealtime(1);
        Assert.AreSame(loop.CleanSource.clip,loop.DistantSource.clip);
        Assert.AreEqual(loop.transform,loop.CleanSource.transform.parent);
        Assert.AreEqual(loop.CleanSource.transform.position,loop.DistantSource.transform.position);
        Assert.Greater(loop.CleanSource.volume,.95f);Assert.Less(loop.DistantSource.volume,.001f);
        Assert.IsNull(loop.CleanSource.GetComponent<AudioLowPassFilter>());
        Assert.IsNull(loop.CleanSource.GetComponent<AudioReverbFilter>());
        Assert.AreEqual(1f,loop.CleanSource.spatialBlend);Assert.AreEqual(1f,loop.DistantSource.spatialBlend);
        Assert.AreEqual(0,loop.CleanSource.spread);Assert.AreEqual(0,loop.DistantSource.spread);
        Assert.AreEqual(0,loop.DistantSource.dopplerLevel);Assert.AreEqual(1,loop.DistantSource.pitch);
        Assert.AreEqual(16000,loop.DistantSource.GetComponent<AudioLowPassFilter>().cutoffFrequency);
        Assert.AreEqual(-300,loop.DistantSource.GetComponent<AudioReverbFilter>().roomHF);
        Assert.AreEqual(1.8f,loop.DistantSource.GetComponent<AudioReverbFilter>().decayTime);
        Assert.Less(loop.DistantSource.GetComponent<AudioReverbFilter>().dryLevel,-9000);
        foreach(float distance in new[]{10f,22f,60f,2f})
        {
            ear.transform.position=center+Vector3.right*distance;
            yield return new WaitForSecondsRealtime(1);
            Vector2 expected=loop.MixAt(distance);
            Assert.That(loop.CleanSource.volume,Is.EqualTo(expected.x).Within(.015f));
            Assert.That(loop.DistantSource.volume,Is.EqualTo(expected.y).Within(.015f));
            Synchronized(loop);
            Debug.Log($"[BigLezAudio] distance={distance} dry={loop.CleanSource.volume:0.000} wet={loop.DistantSource.volume:0.000}");
        }
        GameSettings.AmbientVolume=0;yield return null;yield return null;
        Assert.AreEqual(0,loop.CleanSource.volume);Assert.AreEqual(0,loop.DistantSource.volume);
        GameSettings.AmbientVolume=1;
        loop.enabled=false;Assert.IsFalse(loop.Running);Assert.IsFalse(loop.DistantSource.gameObject.activeSelf);
        loop.enabled=true;yield return Wait(()=>loop.Running && loop.CleanSource.isPlaying);
        yield return new WaitForSecondsRealtime(.5f);Synchronized(loop);
        match.mapIndex.Value=MatchManager.MapSapa;yield return null;yield return null;
        Assert.IsFalse(loop.Running,"No audio on other maps.");
        match.mapIndex.Value=MatchManager.MapDomasov;
        yield return Wait(()=>loop.Running);
        network.Shutdown();yield return Wait(()=>!network.IsListening && !network.ShutdownInProgress);
        yield return null;Assert.IsFalse(loop.Running,"Disconnect must stop the loop and reverb tail.");
        Assert.IsFalse(loop.CleanSource.gameObject.activeSelf);Assert.IsFalse(loop.DistantSource.gameObject.activeSelf);
    }
}
