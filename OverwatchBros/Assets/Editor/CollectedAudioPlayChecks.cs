using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class CollectedAudioPlayChecks
{
    [UnityTest]
    public IEnumerator SelectedAudioPlaybackAndReloadCancellation()
    {
        yield return new EnterPlayMode();
        var actor = new GameObject("AudioPlaybackTest");
        actor.transform.position = new Vector3(10000f, 1.1f, 10000f);
        var movement = actor.AddComponent<FirstPersonController>();
        movement.enabled = false;
        var shooting = actor.AddComponent<WeaponShooting>();
        shooting.enabled = false;
        actor.GetComponent<HeldWeapons>().enabled = false;
        shooting.weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Data/Pistol_Data.asset");

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Wood_Planks";
        floor.transform.position = new Vector3(10000f, -0.1f, 10000f);
        floor.transform.localScale = new Vector3(10f, 0.2f, 10f);
        floor.GetComponent<Renderer>().enabled = false;
        Physics.SyncTransforms();
        Assert.AreEqual(AudioSurfaceKind.Wood, AudioSurface.AtFeet(movement));
        var overrideSurface = floor.AddComponent<AudioSurface>();
        overrideSurface.kind = AudioSurfaceKind.Metal;
        Assert.AreEqual(AudioSurfaceKind.Metal, AudioSurface.AtFeet(movement));

        var carpet = GameObject.CreatePrimitive(PrimitiveType.Plane);
        carpet.name = "Koberec";
        carpet.transform.position = new Vector3(10000f, 0.02f, 10000f);
        Object.Destroy(carpet.GetComponent<Collider>());
        yield return null;
        AudioSurface.RefreshOverlays();
        Assert.AreEqual(AudioSurfaceKind.Carpet, AudioSurface.AtFeet(movement), "A non-colliding decorative rug must override the hard floor below it.");
        carpet.SetActive(false);
        Assert.AreEqual(AudioSurfaceKind.Metal, AudioSurface.AtFeet(movement), "An inactive rug/map must not override the active floor.");

        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(FirstPersonController).GetMethod("EmitMovementAudio", flags).Invoke(movement,
            new object[] { "steps_wood.step", 0.3f, null });
        AudioSource stepSource = null;
        foreach (var source in Object.FindObjectsByType<AudioSource>())
            if (source.clip != null && source.clip.name.StartsWith("wood_")) stepSource = source;
        Assert.IsNotNull(stepSource, "Walking must play a selected wood step.");
        Assert.AreEqual(1f, stepSource.spatialBlend, "An unowned/remote actor must be positional.");
        Assert.AreEqual(CollectedAudioBank.Find(stepSource.clip.name), stepSource.clip, "Replay variant lookup.");

        var playReload = typeof(WeaponShooting).GetMethod("PlayReloadAudio", flags);
        playReload.Invoke(shooting, new object[] { 1.5f });
        var reloadField = typeof(WeaponShooting).GetField("reloadAudio", flags);
        var reload = (AudioSource)reloadField.GetValue(shooting);
        Assert.IsNotNull(reload);
        Assert.AreEqual("viktor_reload", reload.clip.name);
        Assert.That(reload.clip.length / reload.pitch, Is.EqualTo(1.5f).Within(0.03f));
        Assert.IsFalse(reload.loop);
        Assert.AreEqual(actor.transform, reload.transform.parent, "Reload follows moving actor.");
        yield return null;
        Assert.AreEqual(AudioDataLoadState.Loaded, reload.clip.loadState);
        Assert.IsTrue(reload.isPlaying, "Assembled reload should play in the audio engine.");

        typeof(WeaponShooting).GetMethod("StopReloadAudio", flags).Invoke(shooting, null);
        Assert.IsFalse(reload.isPlaying, "Interrupting a reload must silence it immediately.");
        yield return null;
        Assert.IsTrue(reload == null, "Interrupted source is cleaned up.");

        var loop = CollectedWorldAudio.AddLoop(actor.transform, "world_boiler_fire", 0.1f, 9f);
        Assert.IsNotNull(loop);
        Assert.IsTrue(loop.loop);
        Assert.AreEqual(1f, loop.spatialBlend);
        Assert.AreEqual(9f, loop.maxDistance);
        yield return null;
        Assert.IsTrue(loop.isPlaying, "Environment loop should play.");

        var custom = AudioClip.Create("PreservedCustomSound", 4410, 1, 44100, false);
        var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
        weapon.name = "Scythe_Data";
        weapon.fireSound = custom;
        shooting.weapon = weapon;
        typeof(WeaponShooting).GetMethod("PlayMeleeSwing", flags).Invoke(shooting, new object[] { actor.transform.position });
        bool customPlayed = false;
        foreach (var source in Object.FindObjectsByType<AudioSource>())
            if (source.clip == custom) { customPlayed = true; Object.Destroy(source.gameObject); }
        Assert.IsTrue(customPlayed, "Hand-authored weapon sound must take priority.");

        Object.Destroy(actor);
        Object.Destroy(floor);
        Object.Destroy(carpet);
        Object.Destroy(weapon);
        Object.Destroy(custom);
        if (stepSource != null) Object.Destroy(stepSource.gameObject);
        yield return new ExitPlayMode();
    }
}

// Run after the project's automatic scene repair callbacks have finished. Their
// initial untitled music scene would otherwise trigger a save dialog in batchmode.
[InitializeOnLoad]
public static class CollectedAudioPlayCheckBatch
{
    const string Active = "CollectedAudio.PlayCheck.Active";
    static double prepareAt;

    static CollectedAudioPlayCheckBatch()
    {
        if (SessionState.GetBool(Active, false)) TestRunnerApi.RegisterTestCallback(new Results());
    }

    public static void Run()
    {
        if (!Application.isBatchMode)
            throw new System.InvalidOperationException("Audio batch checks must run in a separate batch editor, never in an interactive session.");
        SessionState.SetBool(Active, true);
        TestRunnerApi.RegisterTestCallback(new Results());
        prepareAt = EditorApplication.timeSinceStartup + 5;
        EditorApplication.update += Prepare;
    }

    static void Prepare()
    {
        if (EditorApplication.timeSinceStartup < prepareAt) return;
        EditorApplication.update -= Prepare;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        // Save only the isolated batch session's scratch scene, never SampleScene.
        if (!string.IsNullOrEmpty(scene.path))
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Editor/CollectedAudioSmoke.unity");
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.Execute(new ExecutionSettings(new Filter { testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
            testNames = new[] { "CollectedAudioPlayChecks.SelectedAudioPlaybackAndReloadCancellation" } }));
    }

    sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            TestRunnerApi.SaveResultToFile(result, System.IO.Path.GetFullPath("../tools/sound-collector/research/UNITY_AUDIO_PLAY_TESTS.xml"));
            SessionState.SetBool(Active, false);
            // Leave the real game scene selected for the next editor launch.
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            Debug.Log("[CollectedAudio] Play check: " + result.ResultState + "; assertions=" + result.AssertCount);
            EditorApplication.Exit(result.FailCount == 0 && result.PassCount > 0 ? 0 : 1);
        }
    }
}
