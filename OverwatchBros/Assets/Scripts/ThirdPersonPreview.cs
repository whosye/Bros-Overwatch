using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// Nahled vlastniho hrdiny ze 3. osoby (ladeni zbrani a poz): F6 zapne/vypne, F7 prepina uhel kamery.
// Hra bezi dal normalne z 1. osoby - strelba, mireni (prave tlacitko), schopnosti i pohyb funguji presne jako
// bez nahledu. Jen obraz se vykresli z kamery venku, ktera se diva na postavu (kamera se posune jen na dobu
// vykresleni a hned se vrati), a vlastni postava se zbrani v rukou je videt jako ji vidi ostatni hraci.
public class ThirdPersonPreview : MonoBehaviour
{
    public static bool Active { get; private set; }

    struct View
    {
        public string name;
        public Vector3 offset;   // kamera vuci hraci (mistni osy: x vpravo, z dopredu)
        public float lookHeight; // kam se diva (vyska nad chodidly)
    }

    static readonly View[] Views =
    {
        new View { name = "zepředu vpravo", offset = new Vector3(1.3f, 1.7f, 2.6f), lookHeight = 1.3f },
        new View { name = "z boku (vpravo)", offset = new Vector3(2.8f, 1.5f, 0.3f), lookHeight = 1.3f },
        new View { name = "zepředu vlevo", offset = new Vector3(-1.3f, 1.7f, 2.6f), lookHeight = 1.3f },
        new View { name = "z boku (vlevo)", offset = new Vector3(-2.8f, 1.5f, 0.3f), lookHeight = 1.3f },
        new View { name = "zezadu přes rameno", offset = new Vector3(0.8f, 1.9f, -2.6f), lookHeight = 1.5f },
        new View { name = "detail zbraně", offset = new Vector3(0.9f, 1.5f, 1.2f), lookHeight = 1.25f },
    };

    static int view;
    static float zoom = 1f;
    const float MinZoom = 0.4f, MaxZoom = 8f;   // vzdalenost kamery: 0,4x az 8x vychozi

    Camera moved;
    Vector3 savedPosition;
    Quaternion savedRotation;
    float savedFov;
    GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Active = false;
        view = 0;
        zoom = 1f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("ThirdPersonPreview");
        DontDestroyOnLoad(go);
        go.AddComponent<ThirdPersonPreview>();
    }

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        Active = false;
    }

    static FirstPersonController LocalPlayer()
    {
        var network = NetworkManager.Singleton;
        var player = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        var fpc = player != null ? player.GetComponent<FirstPersonController>() : null;
        return fpc != null && fpc.Bot == null && fpc.playerCamera != null ? fpc : null;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.f6Key.wasPressedThisFrame)
            Active = !Active && LocalPlayer() != null;
        if (Active && keyboard.f7Key.wasPressedThisFrame)
            view = (view + 1) % Views.Length;
        if (Active && Mouse.current != null && keyboard.leftAltKey.isPressed)
        {
            // Alt + kolecko: blize / dal
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) zoom = Mathf.Clamp(zoom * (scroll > 0f ? 0.9f : 1.1f), MinZoom, MaxZoom);
        }
        // PageUp / PageDown (nebo + / - na numericke klavesnici): bliz / dal, Home: zpet na vychozi vzdalenost
        if (Active)
        {
            if (keyboard.pageDownKey.isPressed || keyboard.numpadMinusKey.isPressed) zoom = Mathf.Min(MaxZoom, zoom * (1f + 1.5f * Time.unscaledDeltaTime));
            if (keyboard.pageUpKey.isPressed || keyboard.numpadPlusKey.isPressed) zoom = Mathf.Max(MinZoom, zoom / (1f + 1.5f * Time.unscaledDeltaTime));
            if (keyboard.homeKey.wasPressedThisFrame) zoom = 1f;
        }
        if (Active && LocalPlayer() == null) Active = false;
    }

    void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        moved = null;
        if (!Active || ReplayPlayer.Active) return;
        var fpc = LocalPlayer();
        if (fpc == null || camera != fpc.playerCamera) return;

        var player = fpc.transform;
        var v = Views[view];
        // pri oddaleni i o kus vys (at je videt postava i okoli shora)
        Vector3 offset = new Vector3(v.offset.x * zoom, v.offset.y + (zoom > 1f ? (zoom - 1f) * 0.8f : 0f), v.offset.z * zoom);
        Vector3 target = player.position + Vector3.up * v.lookHeight;
        Vector3 position = player.TransformPoint(offset);

        moved = camera;
        savedPosition = camera.transform.position;
        savedRotation = camera.transform.rotation;
        savedFov = camera.fieldOfView;
        camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
        camera.fieldOfView = 50f;
    }

    void EndCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != moved || moved == null) return;
        camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
        camera.fieldOfView = savedFov;
        moved = null;
    }

    void OnGUI()
    {
        if (!Active) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            style.normal.textColor = new Color(0.4f, 1f, 0.95f);
        }
        GUI.Label(new Rect(20f, 20f, 900f, 30f),
            $"NÁHLED 3. OSOBA – {Views[view].name}   (F6 vypnout, F7 další úhel, Alt+kolečko / PageUp-PageDown vzdálenost, Home zpět)", style);
    }
}
