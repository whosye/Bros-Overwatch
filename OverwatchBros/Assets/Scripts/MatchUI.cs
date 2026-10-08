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
    readonly HudUI hud = new HudUI();
    readonly MatchOverlayUI overlay = new MatchOverlayUI();
    readonly CaptureUI capture = new CaptureUI();
    readonly BuffUI buffs = new BuffUI();
    TextMeshProUGUI boilerPrompt;
    readonly HeroPickerUI heroPicker = new HeroPickerUI();
    GameObject blockBar;
    Image blockFill;
    GameObject settingsPanel;
    TextMeshProUGUI sensText;
    TextMeshProUGUI volumeText;
    Slider sensitivitySlider;
    Slider volumeSlider;
    Button headBobButton;
    Slider headBobSlider;
    TextMeshProUGUI headBobText;
    bool settingsWereVisible;

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

        overlay.Build(root);
        capture.Build(root);
        buffs.Build(root);
        boilerPrompt = UiKit.MakeText(root, "KotelVyzva", "", 30, TextAlignmentOptions.Center, center,
            new Vector2(0f, -90f), new Vector2(900f, 44f), new Color(1f, 0.72f, 0.35f));
        hud.Build(root);

        // Zamerovac: maly bily krizek s jemnym tmavym obrysem (at je videt i na svetlem pozadi).
        var cross = UiKit.MakeImage(root, "Crosshair", new Color(0f, 0f, 0f, 0f), center, Vector2.zero, new Vector2(20f, 20f));
        var outline = new Color(0f, 0f, 0f, 0.45f);
        UiKit.MakeImage(cross.transform, "OutlineH", outline, center, Vector2.zero, new Vector2(18f, 4f));
        UiKit.MakeImage(cross.transform, "OutlineV", outline, center, Vector2.zero, new Vector2(4f, 18f));
        UiKit.MakeImage(cross.transform, "H", new Color(1f, 1f, 1f, 0.95f), center, Vector2.zero, new Vector2(16f, 2f));
        UiKit.MakeImage(cross.transform, "V", new Color(1f, 1f, 1f, 0.95f), center, Vector2.zero, new Vector2(2f, 16f));
        crosshair = cross.gameObject;

        // Bar bloku pod zaměřovačem (velikost = zbývající kapacita bloku).
        blockBar = UiKit.MakeImage(root, "BlockBar", new Color(0f, 0f, 0f, 0.55f), center, new Vector2(0f, -70f), new Vector2(240f, 14f)).gameObject;
        blockFill = UiKit.MakeImage(blockBar.transform, "Fill", new Color(0.55f, 0.75f, 1f, 0.95f), center, Vector2.zero, Vector2.zero);
        var fillRect = blockFill.rectTransform;
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);
        blockBar.SetActive(false);

        BuildEndPanel(root);
        BuildSettingsPanel(root);
        heroPicker.Build(root);

        lobby.Build(root, LeaveGame);
        menu.Build(root);

        connectingText = UiKit.MakeText(root, "Connecting", "Připojuji se…", 56, TextAlignmentOptions.Center, center,
            Vector2.zero, new Vector2(1200f, 100f));
        connectingText.gameObject.SetActive(false);

        // Prehravac "play of the game" (vlastni platno nad vsim ostatnim).
        gameObject.AddComponent<PotgUI>();
    }

    void BuildEndPanel(Transform root)
    {
        var center = new Vector2(0.5f, 0.5f);

        var panel = UiKit.MakeImage(root, "EndPanel", new Color(0f, 0f, 0f, 0.72f), center, Vector2.zero, Vector2.zero);
        UiKit.Stretch(panel.rectTransform);
        panel.raycastTarget = true;
        endPanel = panel.gameObject;

        // Nahore vitez, uprostred tabulka hracu (kresli MatchOverlayUI), dole ovladani hosta.
        endTitle = UiKit.MakeText(endPanel.transform, "EndTitle", "", 84, TextAlignmentOptions.Center, center,
            new Vector2(0f, 400f), new Vector2(1600f, 110f));
        endHint = UiKit.MakeText(endPanel.transform, "EndHint", "", 32, TextAlignmentOptions.Center, center,
            new Vector2(0f, -365f), new Vector2(1600f, 50f), UiKit.Muted);

        restartButton = UiKit.MakeButton(endPanel.transform, "NOVÝ ZÁPAS", center, new Vector2(-230f, -450f), new Vector2(420f, 84f),
            RestartMatch, UiKit.Green, 36f).gameObject;
        lobbyButton = UiKit.MakeButton(endPanel.transform, "ZPĚT DO LOBBY", center, new Vector2(230f, -450f), new Vector2(420f, 84f),
            BackToLobby, UiKit.ButtonBase, 36f).gameObject;

        endPanel.SetActive(false);

        // Tabulka hracu se na konci zapasu ukazuje nad timhle panelem.
        overlay.PlaceBoardAbove(endPanel.transform);
    }

    void BuildSettingsPanel(Transform root)
    {
        var center = new Vector2(0.5f, 0.5f);

        var panel = UiKit.MakeImage(root, "SettingsPanel", new Color(0.05f, 0.07f, 0.10f, 0.92f), center, Vector2.zero, new Vector2(780f, 700f));
        panel.raycastTarget = true;
        settingsPanel = panel.gameObject;

        UiKit.MakeText(settingsPanel.transform, "Title", "NASTAVENÍ", 52, TextAlignmentOptions.Center, center,
            new Vector2(0f, 290f), new Vector2(700f, 70f), UiKit.Accent);

        sensText = UiKit.MakeText(settingsPanel.transform, "Sens", "", 36, TextAlignmentOptions.Center, center,
            new Vector2(0f, 210f), new Vector2(700f, 50f));
        volumeText = UiKit.MakeText(settingsPanel.transform, "Volume", "", 36, TextAlignmentOptions.Center, center,
            new Vector2(0f, 100f), new Vector2(700f, 50f));

        // Logarithmic sensitivity keeps low values easy to adjust across the full range.
        sensitivitySlider = UiKit.MakeSlider(settingsPanel.transform, "SensitivitySlider", center,
            new Vector2(0f, 165f), new Vector2(620f, 44f), 0f, 1f, SensitivityToSlider(),
            value => GameSettings.Sensitivity = Mathf.Round(GameSettings.MinSensitivity
                * Mathf.Pow(GameSettings.MaxSensitivity / GameSettings.MinSensitivity, value) * 100f) / 100f);
        volumeSlider = UiKit.MakeSlider(settingsPanel.transform, "VolumeSlider", center,
            new Vector2(0f, 55f), new Vector2(620f, 44f), 0f, 100f, GameSettings.Volume * 100f,
            value => GameSettings.Volume = value / 100f, wholeNumbers: true);

        headBobButton = UiKit.MakeButton(settingsPanel.transform, "Pohupování kamery: ZAPNUTO", center,
            new Vector2(0f, -30f), new Vector2(620f, 60f),
            () => GameSettings.HeadBobEnabled = !GameSettings.HeadBobEnabled, fontSize: 30f);
        headBobText = UiKit.MakeText(settingsPanel.transform, "HeadBobIntensity", "", 32, TextAlignmentOptions.Center, center,
            new Vector2(0f, -100f), new Vector2(700f, 50f));
        headBobSlider = UiKit.MakeSlider(settingsPanel.transform, "HeadBobSlider", center,
            new Vector2(0f, -145f), new Vector2(620f, 44f), 0f, 200f, GameSettings.HeadBobIntensity * 100f,
            value => GameSettings.HeadBobIntensity = value / 100f, wholeNumbers: true);

        UiKit.MakeButton(settingsPanel.transform, "POKRAČOVAT", center, new Vector2(-190f, -260f), new Vector2(340f, 76f),
            () => Cursor.lockState = CursorLockMode.Locked, UiKit.Green, 32f);
        UiKit.MakeButton(settingsPanel.transform, "OPUSTIT HRU", center, new Vector2(190f, -260f), new Vector2(340f, 76f),
            LeaveGame, new Color(0.45f, 0.18f, 0.18f, 1f), 32f);

        settingsPanel.SetActive(false);
    }

    static float SensitivityToSlider()
    {
        float sensitivity = Mathf.Clamp(GameSettings.Sensitivity, GameSettings.MinSensitivity, GameSettings.MaxSensitivity);
        return Mathf.Log(sensitivity / GameSettings.MinSensitivity)
            / Mathf.Log(GameSettings.MaxSensitivity / GameSettings.MinSensitivity);
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
        // Lobby vidi vsichni pred zapasem a taky hrac, ktery se pripojil do rozehraneho zapasu a jeste si vybira.
        bool joining = hasPlayer && !match.IsLobby && localHero.IsJoining;
        bool inLobby = hasPlayer && (match.IsLobby || joining);
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

        menu.SetVisible(!connected && !PotgIntro.Active);   // (ukazka POTG uvodu z editoru bezi pres menu)
        menu.Tick();

        connectingText.gameObject.SetActive(connected && !hasPlayer);
        lobby.SetVisible(inLobby);
        lobby.Tick(localHero, joining);
        // Behem prehravani "play of the game" se koncova obrazovka neukazuje (hraje se ve scene).
        bool replay = ReplayPlayer.Active;
        endPanel.SetActive(over && !replay && !PotgIntro.Active);

        // Zmena hrdiny behem zapasu (F1).
        heroPicker.Tick(localHero, playing && !over && !PotgUI.IsShowing);

        UpdateCursor(hasPlayer, inLobby, over || heroPicker.IsOpen);
        UpdateHud(match, localHero, playing, over);
        // HUD: zivy hrac, nebo pri prehravani POTG HUD hrace, ktery akci predvedl (podle zaznamu).
        // (killcam: HUD vraha v zaznamu neni - jen obraz a zamerovac)
        bool killcam = ReplayPlayer.IsKillcam;
        hud.SetVisible((replay && !killcam) || (playing && !over && localHero != null && !replay));
        if (killcam) { }
        else if (replay)
            hud.TickReplay(ReplayPlayer.Frame, ReplayPlayer.Camera);
        else
            hud.Tick(localHero);
        // Po konci zapasu (a po dohrani play of the game) je tabulka hracu videt porad, ne jen na Tab.
        overlay.Tick(localHero, playing && !replay && !PotgIntro.Active, match, over && !PotgUI.IsShowing && !killcam);
        capture.Tick(localHero, playing && !over, match);
        buffs.Tick(playing && !over && !replay);
        boilerPrompt.text = playing && !over && !replay ? Boiler.PromptText : "";
        UpdateEndScreen(network, match, over);
        UpdateLegacyHud(playing);

        bool locked = GameSettings.CursorLocked;
        crosshair.SetActive((playing && !over && locked && !replay) || (replay && ReplayPlayer.Frame != null && (killcam || !ReplayPlayer.Frame.thirdPerson)));
        UpdateBlockBar(localHero, playing && !over);

        bool showSettings = playing && !over && !locked && !heroPicker.IsOpen;
        if (settingsWereVisible && !showSettings) PlayerPrefs.Save();
        settingsWereVisible = showSettings;
        settingsPanel.SetActive(showSettings);
        if (showSettings)
        {
            sensitivitySlider.SetValueWithoutNotify(SensitivityToSlider());
            volumeSlider.SetValueWithoutNotify(GameSettings.Volume * 100f);
            sensText.text = $"Citlivost myši: {GameSettings.Sensitivity:0.0#}  <size=65%>(max {GameSettings.MaxSensitivity:0})</size>";
            volumeText.text = $"Hlasitost: {Mathf.RoundToInt(GameSettings.Volume * 100f)} %";
            UiKit.SetButtonLabel(headBobButton, GameSettings.HeadBobEnabled ? "Pohupování kamery: ZAPNUTO" : "Pohupování kamery: VYPNUTO");
            headBobSlider.SetValueWithoutNotify(GameSettings.HeadBobIntensity * 100f);
            headBobSlider.interactable = GameSettings.HeadBobEnabled;
            headBobText.text = $"Intenzita pohupování: {Mathf.RoundToInt(GameSettings.HeadBobIntensity * 100f)} %";
        }
    }

    // Bar bloku se ukazuje, kdyz hrdina blokuje, nebo kdyz bar neni plny (ubyva pri zásazích, po 5 s bez poškození se obnovuje).
    void UpdateBlockBar(PlayerHero localHero, bool playing)
    {
        var block = playing && localHero != null ? localHero.GetComponent<BlockAbility>() : null;
        bool show = block != null && block.enabled && block.ability != null && (block.IsBlocking || block.Fraction < 0.999f);

        if (blockBar.activeSelf != show)
            blockBar.SetActive(show);

        if (!show) return;

        float fraction = block.Fraction;
        blockFill.rectTransform.anchorMax = new Vector2(Mathf.Max(0.001f, fraction), 1f);
        blockFill.color = fraction > 0.3f ? new Color(0.55f, 0.75f, 1f, 0.95f) : new Color(1f, 0.45f, 0.30f, 0.95f);
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

        // Na konci zapasu je skore v tabulce pod jmenem viteze.
        if (over)
        {
            scoreText.text = "";
            infoText.text = "";
            abilityText.text = "";
            return;
        }

        scoreText.text =
            $"<color=#{ColorUtility.ToHtmlStringRGB(UiKit.Team0)}>TÝM 0  {match.team0Score.Value}</color>" +
            $"   <size=60%>{(match.IsAttackMode ? "zabrané body" : $"do {match.scoreToWinSynced.Value}")}</size>   " +
            $"<color=#{ColorUtility.ToHtmlStringRGB(UiKit.Team1)}>{match.team1Score.Value}  TÝM 1</color>";

        // Zivoty, schopnosti a munici kresli HudUI.
        infoText.text = "";
        abilityText.text = "";
    }

    void UpdateEndScreen(NetworkManager network, MatchManager match, bool over)
    {
        if (!over) return;

        int winner = match.winnerTeam.Value;
        endTitle.text = winner < 0
            ? "REMÍZA"
            : $"<color=#{ColorUtility.ToHtmlStringRGB(UiKit.TeamColor(winner))}>TÝM {winner} VYHRÁL</color>";

        bool isHost = network.IsServer;
        // Dokud nedobehne play of the game, host nemuze zapas ukoncit.
        bool waitingForPotg = isHost && match.PotgPending;
        restartButton.SetActive(isHost && !waitingForPotg);
        lobbyButton.SetActive(isHost && !waitingForPotg);
        endHint.text = waitingForPotg
            ? "Za chvíli se přehraje play of the game…"
            : isHost
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

        // Puvodni texty HP/Ammo ze sceny nahradil HudUI.
        if (legacyHud.healthText != null && legacyHud.healthText.gameObject.activeSelf)
            legacyHud.healthText.gameObject.SetActive(false);
        if (legacyHud.ammoText != null && legacyHud.ammoText.gameObject.activeSelf)
            legacyHud.ammoText.gameObject.SetActive(false);
    }
}
