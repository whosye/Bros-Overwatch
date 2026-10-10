using UnityEngine;
using UnityEngine.SceneManagement;

// Persistent asset references make audio startup independent of editor scene repairs.
public class GameAudioLibrary : ScriptableObject
{
    public AudioClip menuMusic;
    public AudioClip villageAmbient;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureAudio();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureAudio();

    static void EnsureAudio()
    {
        var library = Resources.Load<GameAudioLibrary>("GameAudioLibrary");
        if (library == null)
        {
            Debug.LogError("[Audio] Missing Resources/GameAudioLibrary asset.");
            return;
        }
        var music = Object.FindAnyObjectByType<MenuMusic>();
        if (music == null) music = new GameObject("MenuMusic").AddComponent<MenuMusic>();
        if (music.clip == null) music.clip = library.menuMusic;

        var map = GameObject.Find("Map-Domasov");
        if (map == null) return;
        var ambient = map.GetComponentInChildren<VillageAmbient>(true);
        if (ambient == null)
        {
            var go = new GameObject("VillageAmbient");
            go.transform.SetParent(map.transform, false);
            ambient = go.AddComponent<VillageAmbient>();
        }
        if (ambient.clip == null || ambient.clip.name == "village_ambient_10min" || ambient.clip.name == "village_ambient_10min_boosted")
            ambient.clip = library.villageAmbient;
    }
}
