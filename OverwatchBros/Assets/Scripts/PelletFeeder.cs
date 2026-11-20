using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Feeder pro kotel: vzduchem tlaci pelety do kotle. Kdo nese pytel pelet (balicek), muze je u nasypky klavesou R
// nasypat; feeder je pak plny a pristi zatopeni v kotli pali silneji (MatchManager.PelletTotalDamage misto
// Boiler.TotalDamage). Stav sdili MatchManager (MatchManagerPellets.cs); tady je vzhled, vyzva a ovladani.
// Vizual stavi editorovy nastroj (BrosOverwatch/Mapa/Feeder pelet ke kotli).
public class PelletFeeder : MonoBehaviour
{
    public const float UseDistance = 2.2f;
    const float LookDistance = 12f;   // z jake dalky se pri pohledu na podavac ukaze jeho jmeno

    public Transform usePoint;       // nasypka (vzdalenost pro vyzvu se meri odsud)
    public GameObject fullContent;   // pelety v nasypce - vidi se, kdyz je feeder plny
    public Light statusLight;        // zelena = plny, cervena = prazdny
    public Transform fan;            // vrtule dmychadla (toci se, kdyz kotel topi s peletami)

    public static PelletFeeder Instance { get; private set; }
    public static bool LocalCanUse { get; private set; }
    public static string PromptText { get; private set; } = "";

    static readonly Color Full = new Color(0.25f, 1f, 0.35f), Empty = new Color(1f, 0.25f, 0.15f);
    float fanSpeed;

    public Vector3 UsePoint => usePoint != null ? usePoint.position : transform.position + Vector3.up;

    void OnEnable() => Instance = this;

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        LocalCanUse = false;
        PromptText = "";
    }

    void Update()
    {
        var match = MatchManager.Instance;
        bool live = match != null && match.IsSpawned;
        bool loaded = live && match.feederLoaded.Value;
        bool blowing = live && match.BoilerHeating && match.boilerWithPellets.Value;

        UpdateLocalPlayer(match, live, loaded, blowing);

        if (fullContent != null && fullContent.activeSelf != loaded) fullContent.SetActive(loaded);
        if (statusLight != null)
        {
            statusLight.color = loaded || blowing ? Full : Empty;
            statusLight.intensity = blowing ? 1.2f + Mathf.PingPong(Time.time * 4f, 1f) : 0.8f;
        }
        if (fan != null)
        {
            fanSpeed = Mathf.MoveTowards(fanSpeed, blowing ? 1400f : 0f, Time.deltaTime * 900f);
            if (fanSpeed > 0f) fan.Rotate(0f, 0f, fanSpeed * Time.deltaTime, Space.Self);
        }
    }

    void UpdateLocalPlayer(MatchManager match, bool live, bool loaded, bool blowing)
    {
        LocalCanUse = false;
        PromptText = "";
        if (!live || match.IsLobby || match.IsOver) return;

        var network = NetworkManager.Singleton;
        var local = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        if (local == null) return;
        var fpc = local.GetComponent<FirstPersonController>();
        if (fpc == null || fpc.IsDead || fpc.playerCamera == null) return;
        var view = fpc.playerCamera.transform;
        if (Vector3.Distance(view.position, UsePoint) > UseDistance)
        {
            // miridlem na podavaci (i z dalky): jen jmeno a stav
            if (Physics.Raycast(view.position, view.forward, out var hit, LookDistance, ~0, QueryTriggerInteraction.Ignore)
                && hit.transform.IsChildOf(transform))
                PromptText = blowing ? "Podavač pelet - tlačí pelety do kotle"
                    : loaded ? "Podavač pelet - plný" : "Podavač pelet - prázdný";
            return;
        }

        if (blowing) { PromptText = "Podavač tlačí pelety do kotle"; return; }
        if (loaded) { PromptText = "Podavač je plný - zatop v kotli"; return; }
        if (!MatchManager.LocalHasPellets) { PromptText = "Podavač pelet je prázdný - přines pytel pelet"; return; }

        LocalCanUse = true;
        PromptText = "[R] Nasypat pelety do podavače";
        if (GameSettings.CursorLocked && !fpc.InputBlocked && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            match.LoadPelletsRpc();
    }
}
