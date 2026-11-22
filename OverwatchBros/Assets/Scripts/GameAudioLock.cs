using Unity.Netcode;
using UnityEngine;

// Zamek zvuku hry: mimo hru (v hlavnim menu - zadne pripojeni k hre) nesmi hrat zadny zvuk ze hry.
//  - ProceduralSfx a Fx nove zvuky v menu vubec nespusti (Locked),
//  - pri navratu do menu (a pak 10x za s) se zastavi vse, co jeste dohrava: jednorazove zvuky (SFX_..., hlasky,
//    pisnicka rolby...) se utnou. Smycky (ambient mapy, hudba menu) nechava - ty si ridi jejich skripty samy.
// Hudba menu (MenuMusic) je vyjimka.
public static class GameAudioLock
{
    // V menu (hra bezi, ale nejsme pripojeni / prave se odpojujeme).
    public static bool Locked
    {
        get
        {
            if (!Application.isPlaying) return false;   // (nahledy v editoru)
            var network = NetworkManager.Singleton;
            return network == null || network.ShutdownInProgress || !(network.IsClient || network.IsServer);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("GameAudioLock");
        Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideInHierarchy;
        go.AddComponent<Runner>();
    }

    // Utne vsechny zvuky hry, ktere jeste hraji.
    public static void SilenceGame()
    {
        foreach (var source in Object.FindObjectsByType<AudioSource>())
        {
            if (source == null || IsMenuAudio(source)) continue;
            if (source.gameObject.name.StartsWith("SFX_"))
            {
                source.Stop();
                Object.Destroy(source.gameObject);
            }
            else if (source.isPlaying && !source.loop)
                source.Stop();
        }
    }

    static bool IsMenuAudio(AudioSource source) =>
        source.GetComponentInParent<MenuMusic>() != null || source.GetComponentInParent<MenuMusicDsp>() != null;

    class Runner : MonoBehaviour
    {
        bool wasLocked = true;
        float nextSweep;

        void Update()
        {
            bool locked = Locked;
            if (locked && (!wasLocked || Time.unscaledTime >= nextSweep))
            {
                nextSweep = Time.unscaledTime + 0.1f;
                SilenceGame();
            }
            wasLocked = locked;
        }
    }
}
