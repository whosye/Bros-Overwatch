using UnityEngine;

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
        bool other = false;
        foreach (var cam in Camera.allCameras)
            if (cam != view && cam.enabled && cam.targetTexture == null)
            {
                other = true;
                break;
            }

        if (view.enabled == other) view.enabled = !other;
        if (listener.enabled == other) listener.enabled = !other;
    }
}
