using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Cele UI hry vytvorene kodem (nic se nemusi klikat ve scene):
// uvodni menu -> pripojovani -> lobby (tym + hrdina) -> HUD zapasu -> konec zapasu, plus nastaveni (Esc).
public class MatchUI : MonoBehaviour
{
    readonly MainMenuUI menu = new MainMenuUI();
    readonly LobbyUI lobby = new LobbyUI();

    TextMeshProUGUI scoreText;
    TextMeshProUGUI infoText;
    TextMeshProUGUI abilityText;
    TextMeshProUGUI connectingText;
    TextMeshProUGUI endTitle;
    TextMeshProUGUI endHint;
    GameObject endPanel;
    GameObject restartButton;
    GameObject lobbyButton;
    GameObject crosshair;
    GameObject settingsPanel;
    TextMeshProUGUI sensText;
    TextMeshProUGUI volumeText;

    NetworkUI legacyMenu;
    PlayerHUD legacyHud;

    bool wasConnected;
    bool everHadPlayer;
    bool leaving;
    bool wasCursorFree;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindAnyObjectByType<MatchUI>() != null) return;

        var go = new GameObject("MatchUI");
        DontDestroyOnLoad(go);
        go.AddComponent<MatchUI>();
    }

    void Awake()
    {
        PlayerHero.PreferredHero = PlayerPrefs.GetInt("preferredHero", 0);
        PlayerHero.PreferredName = PlayerPrefs.GetString("nick", "Hráč");
        GameSettings.ApplyVolume();
        Build();
    }

    void Build()
    {
        var canvasObject = new GameObject("MatchCanvas");
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        var root = canvasObject.transform;
        var center = new Vector2(0.5f, 0.5f);

        scoreText = UiKit.MakeText(root, "Score", "", 48, TextAlignmentOptions.Center, new Vector2(0.5f, 1f),
            new Vector2(0f, -20f), new Vector2(1200f, 70f));
        infoText = UiKit.MakeText(root, "Info", "", 30, TextAlignmentOptions.Left, new Vector2(0f, 0f),
            new Vector2(30f, 30f), new Vector2(800f, 50f));
        abilityText = UiKit.MakeText(root, "Ability", "", 34, TextAlignmentOptions.Center, new Vector2(0.5f, 0f),
            new Vector2(0f, 30f), new Vector2(1000f, 60f));

        crosshair = UiKit.MakeImage(root, "Crosshair", new Color(1f, 1f, 1f, 0.9f), center, Vector2.zero, new Vector2(6f, 6f)).gameObject;

        BuildEndPanel(root);
        BuildSettingsPanel(root);

        lobby.Build(root, LeaveGame);
        menu.Build(root);

        connectingText = UiKit.MakeText(root, "Connecting", "Připojuji se…", 56, TextAlignmentOptions.Center, center,
            Vector2.zero, new Vector2(1200f, 100f));
        connectingText.gameObject.SetActive(false);
    }

    void BuildEndPanel(Transform root)
    {
        var center = new Vector2(0.5f, 0.5f);

        var panel = UiKit.MakeImage(root, "EndPanel", new Color(0f, 0f, 0f, 0.72f), center, Vector2.zero, Vector2.zero);
        UiKit.Stretch(panel.rectTransform);
        panel.raycastTarget = true;
        endPanel = panel.gameObject;

        endTitle = UiKit.MakeText(endPanel.transform, "EndTitle", "", 96, TextAlignmentOptions.Center, center,
            new Vector2(0f, 110f), new Vector2(1600f, 130f));
        endHint = UiKit.MakeText(endPanel.transform, "EndHint", "", 38, TextAlignmentOptions.Center, center,
            new Vector2(0f, 10f), new Vector2(1600f, 60f), UiKit.Muted);

        restartButton = UiKit.MakeButton(endPanel.transform, "NOVÝ ZÁPAS", center, new Vector2(-230f, -110f), new Vector2(420f, 84f),
            RestartMatch, UiKit.Green, 36f).gameObject;
        lobbyButton = UiKit.MakeButton(endPanel.transform, "ZPĚT DO LOBBY", center, new Vector2(230f, -110f), new Vector2(420f, 84f),
            BackToLobby, UiKit.ButtonBase, 36f).gameObject;

        endPanel.SetActive(false);
    }

    void BuildSettingsPanel(Transform root)
    {
        var center = new Vector2(0.5f, 0.5f);

        var panel = UiKit.MakeImage(root, "SettingsPanel", new Color(0.05f, 0.07f, 0.10f, 0.92f), center, Vector2.zero, new Vector2(780f, 480f));
        panel.raycastTarget = true;
        settingsPanel = panel.gameObject;

        UiKit.MakeText(settingsPanel.transform, "Title", "NASTAVENÍ", 52, TextAlignmentOptions.Center, center,
            new Vector2(0f, 180f), new Vector2(700f, 70f), UiKit.Accent);

        sensText = UiKit.MakeText(settingsPanel.transform, "Sens", "", 36, TextAlignmentOptions.Center, center,
            new Vector2(0f, 65f), new Vector2(500f, 60f));
        volumeText = UiKit.MakeText(settingsPanel.transform, "Volume", "", 36, TextAlignmentOptions.Center, center,
            new Vector2(0f, -35f), new Vector2(500f, 60f));

        UiKit.MakeButton(settingsPanel.transform, "−", center, new Vector2(-300f, 65f), new Vector2(70f, 70f),
            () => GameSettings.Sensitivity -= 0.1f, UiKit.ButtonBase, 40f);
        UiKit.MakeButton(settingsPanel.transform, "+", center, new Vector2(300f, 65f), new Vector2(70f, 70f),
            () => GameSettings.Sensitivity += 0.1f, UiKit.ButtonBase, 40f);
        UiKit.MakeButton(settingsPanel.transform, "−", center, new Vector2(-300f, -35f), new Vector2(70f, 70f),
            () => GameSettings.Volume -= 0.1f, UiKit.ButtonBase, 40f);
        UiKit.MakeButton(settingsPanel.transform, "+", center, new Vector2(300f, -35f), new Vector2(70f, 70f),
            () => GameSettings.Volume += 0.1f, UiKit.ButtonBase, 40f);

        UiKit.MakeButton(settingsPanel.transform, "POKRAČOVAT", center, new Vector2(-190f, -150f), new Vector2(340f, 76f),
            () => Cursor.lockState = CursorLockMode.Locked, UiKit.Green, 32f);
        UiKit.MakeButton(settingsPanel.transform, "OPUSTIT HRU", center, new Vector2(190f, -150f), new Vector2(340f, 76f),
            LeaveGame, new Color(0.45f, 0.18f, 0.18f, 1f), 32f);

        settingsPanel.SetActive(false);
    }

    void RestartMatch()
    {
        if (MatchManager.Instance != null)
            MatchManager.Instance.RestartMatch();
    }

    void BackToLobby()
    {
        if (MatchManager.Instance != null)
            MatchManager.Instance.BackToLobby();
    }

    public void LeaveGame()
    {
        if (leaving) return;
        StartCoroutine(LeaveRoutine());
    }

    IEnumerator LeaveRoutine()
    {
        leaving = true;

        if (LanDiscovery.Instance != null)
            LanDiscovery.Instance.StopAdvertising();

        var network = NetworkManager.Singleton;
        if (network != null)
        {
            network.Shutdown();
            while (network.ShutdownInProgress)
                yield return null;
        }

        yield return null;
        Cursor.lockState = CursorLockMode.None;
        menu.ShowStatus("");
        leaving = false;
        wasConnected = false;
        everHadPlayer = false;
    }

    void Update()
    {
        HideLegacyUi();

        var network = NetworkManager.Singleton;
        bool connected = network != null && network.IsListening;
        var match = MatchManager.Instance;

        PlayerHero localHero = null;
        if (connected && network.LocalClient != null && network.LocalClient.PlayerObject != null)
            localHero = network.LocalClient.PlayerObject.GetComponent<PlayerHero>();

        bool hasPlayer = localHero != null && match != null && match.IsSpawned;
        bool inLobby = hasPlayer && match.IsLobby;
        bool over = hasPlayer && !inLobby && match.IsOver;
        bool playing = hasPlayer && !inLobby;

        if (hasPlayer)
            everHadPlayer = true;

        if (wasConnected && !connected && !leaving)
            menu.ShowStatus(everHadPlayer
                ? "Spojení s hrou bylo ukončeno."
                : "Připojení se nepodařilo. Zkontroluj IP adresu a že host hru opravdu založil (firewall!).");
        if (!connected && !leaving)
            everHadPlayer = false;
        wasConnected = connected;

        menu.SetVisible(!connected);
        menu.Tick();

        connectingText.gameObject.SetActive(connected && !hasPlayer);
        lobby.SetVisible(inLobby);
        lobby.Tick(localHero);
        endPanel.SetActive(over);

        UpdateCursor(hasPlayer, inLobby, over);
        UpdateHud(match, localHero, playing, over);
        UpdateEndScreen(network, match, over);
        UpdateLegacyHud(playing);

        bool locked = GameSettings.CursorLocked;
        crosshair.SetActive(playing && !over && locked);

        bool showSettings = playing && !over && !locked;
        settingsPanel.SetActive(showSettings);
        if (showSettings)
        {
            sensText.text = $"Citlivost myši: {GameSettings.Sensitivity:0.0}";
            volumeText.text = $"Hlasitost: {Mathf.RoundToInt(GameSettings.Volume * 100f)} %";
        }
    }

    // V lobby a na konci zapasu musi byt kurzor volny; pri navratu do hry se znovu zamkne.
    void UpdateCursor(bool hasPlayer, bool inLobby, bool over)
    {
        bool needsFree = hasPlayer && (inLobby || over);
        if (needsFree)
            Cursor.lockState = CursorLockMode.None;
        else if (wasCursorFree && hasPlayer)
            Cursor.lockState = CursorLockMode.Locked;

        wasCursorFree = needsFree;
    }

    void UpdateHud(MatchManager match, PlayerHero localHero, bool playing, bool over)
    {
        if (!playing || match == null)
        {
            scoreText.text = "";
            infoText.text = "";
            abilityText.text = "";
            return;
        }

        scoreText.text =
            $"<color=#{ColorUtility.ToHtmlStringRGB(UiKit.Team0)}>TÝM 0  {match.team0Score.Value}</color>" +
            $"   <size=60%>do {match.scoreToWinSynced.Value}</size>   " +
            $"<color=#{ColorUtility.ToHtmlStringRGB(UiKit.Team1)}>{match.team1Score.Value}  TÝM 1</color>";

        if (localHero.Hero != null)
        {
            var team = localHero.GetComponent<PlayerTeam>();
            infoText.text = $"{localHero.DisplayName}  |  {localHero.Hero.heroName}  |  tým {team.teamId.Value}";
            abilityText.text = over ? "" : localHero.AbilityStatus();
        }
        else
        {
            infoText.text = "";
            abilityText.text = "";
        }
    }

    void UpdateEndScreen(NetworkManager network, MatchManager match, bool over)
    {
        if (!over) return;

        int winner = match.winnerTeam.Value;
        endTitle.text = $"<color=#{ColorUtility.ToHtmlStringRGB(UiKit.TeamColor(winner))}>TÝM {winner} VYHRÁL</color>";

        bool isHost = network.IsServer;
        restartButton.SetActive(isHost);
        lobbyButton.SetActive(isHost);
        endHint.text = isHost
            ? "Stiskni R pro nový zápas, nebo se vrať do lobby změnit tým / hrdinu"
            : "Čekáme, až host rozhodne, co dál…";

        if (isHost && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            RestartMatch();
    }

    // Puvodni Host/Join tlacitka a HP/Ammo texty ve scene: menu nahrazuje nove UI, texty ukazujeme jen behem hry.
    void HideLegacyUi()
    {
        if (legacyMenu == null)
            legacyMenu = FindAnyObjectByType<NetworkUI>();
        if (legacyMenu != null && legacyMenu.menuPanel != null && legacyMenu.menuPanel.activeSelf)
            legacyMenu.menuPanel.SetActive(false);

        if (legacyHud == null)
            legacyHud = FindAnyObjectByType<PlayerHUD>(FindObjectsInactive.Include);
    }

    void UpdateLegacyHud(bool playing)
    {
        if (legacyHud == null) return;

        if (legacyHud.healthText != null && legacyHud.healthText.gameObject.activeSelf != playing)
            legacyHud.healthText.gameObject.SetActive(playing);
        if (legacyHud.ammoText != null && legacyHud.ammoText.gameObject.activeSelf != playing)
            legacyHud.ammoText.gameObject.SetActive(playing);
    }
}
