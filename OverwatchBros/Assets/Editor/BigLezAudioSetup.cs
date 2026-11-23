using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BigLezAudioSetup
{
    public const string ClipPath="Assets/Audio/Ambient/BigLez/2mangos5dollars.mp3";
    const string ModelPath="Assets/Models/BigLez/BigLez.glb";
    const string PrefabPath="Assets/Models/BigLez/BigLez.prefab";

    [MenuItem("BrosOverwatch/Mapa/Pripravit Big Lez audio")]
    public static void Setup()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Save in Edit mode first.");
        AssetDatabase.ImportAsset(ClipPath,ImportAssetOptions.ForceSynchronousImport);
        var importer=(AudioImporter)AssetImporter.GetAtPath(ClipPath);
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
