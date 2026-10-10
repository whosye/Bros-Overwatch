using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class VillageAmbientSetup
{
    public const string ClipPath = "Assets/Audio/Ambient/village_ambient_spatial.ogg";
    static int retries;
    static double diagnosticTime;

    static VillageAmbientSetup()
    {
        EditorApplication.delayCall += Setup;
        diagnosticTime = EditorApplication.timeSinceStartup + 2f;
        EditorApplication.update += LogAudioStatus;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += Setup;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Setup;
        };
    }

    static void LogAudioStatus()
    {
        if (EditorApplication.timeSinceStartup < diagnosticTime || EditorApplication.isCompiling) return;
        EditorApplication.update -= LogAudioStatus;
        var music = Object.FindAnyObjectByType<MenuMusic>();
        var playback = music != null ? music.transform.Find("MusicPlayback") : null;
        var source = playback != null ? playback.GetComponent<AudioSource>() : null;
        Debug.Log($"[AudioStatus] PlayMode={EditorApplication.isPlaying}; musicEnabled={GameSettings.MusicEnabled}; musicObject={music != null}; clip={(music != null && music.clip != null ? music.clip.name : "NONE")}; playing={(source != null && source.isPlaying)}; volume={(source != null ? source.volume : -1f)}; master={AudioListener.volume}; paused={AudioListener.pause}.");
        var ambient = Object.FindAnyObjectByType<VillageAmbient>();
        if (ambient != null)
        {
            Debug.Log($"[AmbientStatus] enabled={ambient.isActiveAndEnabled}; clip={(ambient.clip != null ? ambient.clip.name : "NONE")}; setting={GameSettings.AmbientVolume}; boost={ambient.volumeBoost}.");
            foreach (var voice in ambient.GetComponentsInChildren<AudioSource>())
                if (voice.name == "Voice0") Debug.Log($"[AmbientVoice] {voice.transform.parent.name}: playing={voice.isPlaying}; volume={voice.volume}; position={voice.transform.position}.");
        }
        foreach (var listener in Object.FindObjectsByType<AudioListener>())
            if (listener.isActiveAndEnabled) Debug.Log($"[ListenerStatus] {listener.name}: {listener.transform.position}.");
    }

    [MenuItem("BrosOverwatch/Mapa/Pripravit zvuky prostredi")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SetupMusic();
        var map = GameObject.Find("Map-Domasov");
        if (map == null) return;
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
        if (clip == null)
        {
            if (retries++ < 30) EditorApplication.delayCall += Setup;
            return;
        }
        retries = 0;
        var ambient = map.GetComponentInChildren<VillageAmbient>(true);
        bool changed = ambient == null;
        if (ambient == null)
        {
            var go = new GameObject("VillageAmbient");
            go.transform.SetParent(map.transform, false);
            ambient = go.AddComponent<VillageAmbient>();
        }
        if (ambient.clip != clip)
        {
            ambient.clip = clip;
            changed = true;
        }
        // Stream the long stereo clip rather than decompressing ten minutes into RAM.
        var importer = AssetImporter.GetAtPath(ClipPath) as AudioImporter;
        if (importer != null)
        {
            var settings = importer.defaultSampleSettings;
            if (settings.loadType != AudioClipLoadType.Streaming || importer.forceToMono)
            {
                settings.loadType = AudioClipLoadType.Streaming;
                importer.defaultSampleSettings = settings;
                importer.forceToMono = false;
                importer.SaveAndReimport();
            }
        }
        if (changed)
        {
            EditorUtility.SetDirty(ambient);
            EditorSceneManager.MarkSceneDirty(map.scene);
            Debug.Log("[Ambient] Zvuky vesnice pripraveny, hlasitost v Esc > Zvuky prostredi. Uloz scenu (Ctrl+S).");
        }
    }

    static void SetupMusic()
    {
        const string path = "Assets/Audio/Music/bros_overwatch_live_improv.ogg";
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null) return;
        var music = Object.FindAnyObjectByType<MenuMusic>();
        bool changed = music == null;
        if (music == null) music = new GameObject("MenuMusic").AddComponent<MenuMusic>();
        if (music.clip != clip)
        {
            music.clip = clip;
            changed = true;
        }
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer != null)
        {
            var settings = importer.defaultSampleSettings;
            if (settings.loadType != AudioClipLoadType.Streaming || importer.forceToMono)
            {
                settings.loadType = AudioClipLoadType.Streaming;
                importer.defaultSampleSettings = settings;
                importer.forceToMono = false;
                importer.SaveAndReimport();
            }
        }
        if (changed)
        {
            EditorUtility.SetDirty(music);
            EditorSceneManager.MarkSceneDirty(music.gameObject.scene);
            Debug.Log("[Music] Hudba v menu a lobby pripravena. Uloz scenu (Ctrl+S).");
        }
    }
}
