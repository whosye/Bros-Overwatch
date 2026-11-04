using Unity.Netcode;
using UnityEngine;

// Limit snimku: bez nej hra zene grafickou kartu naplno (menu i 300 FPS), zbytecne topi a na noteboocich
// se muze spoustet omezeni vykonu. V menu a lobby 60 FPS, ve hre 120 FPS.
public class FrameRateLimit : MonoBehaviour
{
    const int MenuFps = 60;
    const int GameFps = 120;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("FrameRateLimit");
        DontDestroyOnLoad(go);
        go.AddComponent<FrameRateLimit>();
    }

    void Update()
    {
        var network = NetworkManager.Singleton;
        var match = MatchManager.Instance;
        bool playing = network != null && network.IsListening && match != null && match.IsSpawned && !match.IsLobby;

        int wanted = playing ? GameFps : MenuFps;
        if (Application.targetFrameRate == wanted && QualitySettings.vSyncCount == 0) return;

        QualitySettings.vSyncCount = 0;   // (se zapnutou vertikalni synchronizaci se targetFrameRate ignoruje)
        Application.targetFrameRate = wanted;
    }
}
