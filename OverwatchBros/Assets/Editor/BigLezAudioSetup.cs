using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BigLezAudioSetup
{
    public const string ClipPath="Assets/Audio/Ambient/BigLez/2mangos5dollars.mp3";
    const string ModelPath="Assets/Models/BigLez/BigLez.glb";
    const string PrefabPath="Assets/Models/BigLez/BigLez.prefab";

    [MenuItem("BrosOverwatch/Mapa/Pripravit Big Lez audio")]
    public static void Setup()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Save in Edit mode first.");
        var clip=PrepareClip();
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var targets=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .Where(t=>t.gameObject.scene==scene && t.name=="BigLez"
                && AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject))==ModelPath).ToArray();
        if(targets.Length!=1) throw new Exception("Expected exactly one placed BigLez model; found "+targets.Length);
        foreach(var target in targets)
        {
            var beforePosition=target.position; var beforeRotation=target.rotation; var beforeScale=target.localScale;
            Configure(target.gameObject,clip);
            if(target.position!=beforePosition || target.rotation!=beforeRotation || target.localScale!=beforeScale)
                throw new Exception("Audio setup moved the placed model.");
            Debug.Log("[BigLezAudio] Attached to placed BigLez at "+target.position);
        }
        // New prefab instances get the same emitter; the existing imported-model instance above is kept intact.
        var prefab=PrefabUtility.LoadPrefabContents(PrefabPath);
        try { Configure(prefab,clip); PrefabUtility.SaveAsPrefabAsset(prefab,PrefabPath); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    // Imports audio only. Checks must validate saved references before repairing any scene/prefab.
    public static AudioClip PrepareClip()
    {
        AssetDatabase.ImportAsset(ClipPath,ImportAssetOptions.ForceSynchronousImport);
        var importer=AssetImporter.GetAtPath(ClipPath) as AudioImporter;
        if(importer==null) throw new Exception("Big Lez audio file is missing or has no AudioImporter: "+ClipPath);
        var settings=importer.defaultSampleSettings;
        settings.loadType=AudioClipLoadType.CompressedInMemory;
        settings.compressionFormat=AudioCompressionFormat.Vorbis;
        settings.quality=.7f;
        settings.sampleRateSetting=AudioSampleRateSetting.OverrideSampleRate;
        settings.sampleRateOverride=44100;
        settings.preloadAudioData=true;
        importer.forceToMono=true;
        importer.loadInBackground=true;
        importer.defaultSampleSettings=settings;
        importer.SaveAndReimport();
        var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
        if(clip==null) throw new Exception("Big Lez audio import failed.");
        return clip;
    }

    static void Configure(GameObject target,AudioClip clip)
    {
        var loop=target.GetComponent<DistanceLayeredLoop>();
        bool added=loop==null;
        if(added) loop=target.AddComponent<DistanceLayeredLoop>();
        loop.clip=clip;
        if(added)
        {
            var renderers=target.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length==0) throw new Exception("BigLez has no renderer.");
            Bounds bounds=renderers[0].bounds;
            foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            loop.emitterOffset=target.transform.InverseTransformPoint(bounds.center+Vector3.up*.12f);
        }
        EditorUtility.SetDirty(loop);
        if(PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(loop);
    }
}

// Reject the dangling clip reference that previously produced a successful but silent build.
public sealed class BigLezAudioBuildCheck : IProcessSceneWithReport
{
    public int callbackOrder => 0;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        foreach(var root in scene.GetRootGameObjects())
            foreach(var loop in root.GetComponentsInChildren<DistanceLayeredLoop>(true))
            {
                if(loop.name!="BigLez" || loop.clip!=null) continue;
                // Unity vola kontrolu i pri vstupu do Play modu (report == null) - tam hru nezastavovat:
                // zvuk se doplni ze souboru (jen v kopii sceny pro hrani, nic se neuklada) a vypise se varovani.
                var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(BigLezAudioSetup.ClipPath);
                if(clip!=null)
                {
                    loop.clip=clip;
                    Debug.LogWarning("[BigLezAudio] BigLez ve scene nemel ulozeny zvuk - doplnen z "+BigLezAudioSetup.ClipPath
                        +". Trvale opravit: BrosOverwatch/Mapa/Pripravit Big Lez audio (v Edit modu) a ulozit scenu.");
                    continue;
                }
                if(report!=null)
                    throw new BuildFailedException("BigLez has no valid audio clip in "+scene.path+". Repair its saved audio reference before building.");
                Debug.LogWarning("[BigLezAudio] BigLez nema zvuk a soubor "+BigLezAudioSetup.ClipPath+" chybi - hra pobezi bez nej.");
            }
    }
}
