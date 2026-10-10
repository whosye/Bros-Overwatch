using UnityEngine;
using Unity.Netcode;

// Kamera pro hlavni menu a lobby, kdyz ve scene zadna jina nebezi (scena sama kameru nema - ma ji az hrac).
// Bez ni Unity hlasi "No cameras rendering" a "no audio listeners". Jakmile se zapne jina kamera
// (hrac, play of the game, ukazka uvodu), tahle se sama vypne.
public class MenuCamera : MonoBehaviour
{
    static MenuCamera instance;

    Camera view;
    AudioListener listener;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("MenuCamera");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<MenuCamera>();
    }

    void Awake()
    {
        // Daleko pod mapou: posluchac zvuku v menu nema slyset nic ze sceny (napr. televizi v chate).
        transform.position = new Vector3(0f, -5000f, 0f);
        view = gameObject.AddComponent<Camera>();
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color(0.03f, 0.04f, 0.07f);
        view.cullingMask = 0;   // nic ze sceny, jen pozadi pod UI
        view.depth = -100;
        listener = gameObject.AddComponent<AudioListener>();
    }

    void LateUpdate()
    {
        // The fallback listener also serves character selection. Keep it near
        // the local player once connected, so 3D ambience is not 5 km away.
        var network = NetworkManager.Singleton;
        var player = network != null && network.IsClient && network.IsListening && network.LocalClient != null
            ? network.LocalClient.PlayerObject : null;
        if (player != null) transform.position = player.transform.position + Vector3.up * 1.6f;
        else transform.position = new Vector3(0f, -5000f, 0f);
        bool other = false;
        foreach (var cam in Camera.allCameras)
            if (cam != view && cam.enabled && cam.targetTexture == null)
            {
                other = true;
                break;
            }

        if (view.enabled == other) view.enabled = !other;
        // A rendering camera is not necessarily an audio listener (previews,
        // cinematic cameras, etc.). Keep our fallback until a real listener is active.
        bool otherListener = false;
        foreach (var candidate in FindObjectsByType<AudioListener>())
            if (candidate != listener && candidate.isActiveAndEnabled)
            {
                otherListener = true;
                break;
            }
        listener.enabled = !otherListener;
    }
}
