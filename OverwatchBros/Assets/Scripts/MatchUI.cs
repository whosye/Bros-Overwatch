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
    public bool ChatAvailable => !settings.IsOpen && !heroPicker.IsOpen && !heroInfo.IsOpen
        && !ReplayPlayer.Active && !PotgUI.IsShowing && !PotgIntro.Active;

    TextMeshProUGUI scoreText;
    TextMeshProUGUI infoText;
    TextMeshProUGUI abilityText;
    TextMeshProUGUI connectingText;
    GameObject connectingPanel;
    TextMeshProUGUI respawnText;
    TextMeshProUGUI endTitle;
    TextMeshProUGUI endHint;
    GameObject endPanel;
    GameObject restartButton;
    GameObject lobbyButton;
    GameObject crosshair;
    CanvasGroup crosshairFade;
    ScopeOverlay scopeOverlay;
    readonly HudUI hud = new HudUI();
    readonly MatchOverlayUI overlay = new MatchOverlayUI();
    readonly CaptureUI capture = new CaptureUI();
    readonly TopBarUI topBar = new TopBarUI();
    readonly PushUI pushBar = new PushUI();
    readonly BuffUI buffs = new BuffUI();
    TextMeshProUGUI boilerPrompt;
    // Nasazeny prsten (vidi jen nositel): zsedly "svet stinu" pres obrazovku.
    Image ringVeil;
    bool ringWornShown;
    Transform uiRoot;
    readonly HeroPickerUI heroPicker = new HeroPickerUI();
    readonly HeroInfoUI heroInfo = new HeroInfoUI();
    readonly ObjectiveHintUI objectiveHint = new ObjectiveHintUI();
    GameObject blockBar;
    Image blockFill;
    readonly SettingsMenuUI settings = new SettingsMenuUI();

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
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        canvasObject.AddComponent<GraphicRaycaster>();

        var root = canvasObject.transform;
        uiRoot = root;

        // odpocet do oziveni (po smrti)
        respawnText = UiKit.MakeText(root, "Respawn", "", 44, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f),
            new Vector2(0f, -250f), new Vector2(900f, 70f));
        respawnText.gameObject.SetActive(false);
        var center = new Vector2(0.5f, 0.5f);

        // Below the HUD and all menus: the optic never covers health, charge or hit feedback.
        var scopeObject = new GameObject("SniperScope", typeof(RectTransform), typeof(ScopeOverlay));
        scopeObject.transform.SetParent(root, false);
        scopeOverlay = scopeObject.GetComponent<ScopeOverlay>();
        UiKit.Stretch(scopeOverlay.rectTransform);
        scopeObject.SetActive(false);

        scoreText = UiKit.MakeText(root, "Score", "", 48, TextAlignmentOptions.Center, new Vector2(0.5f, 1f),
            new Vector2(0f, -20f), new Vector2(1200f, 70f));
        infoText = UiKit.MakeText(root, "Info", "", 30, TextAlignmentOptions.Left, new Vector2(0f, 0f),
            new Vector2(30f, 30f), new Vector2(800f, 50f));
        abilityText = UiKit.MakeText(root, "Ability", "", 34, TextAlignmentOptions.Center, new Vector2(0.5f, 0f),
            new Vector2(0f, 30f), new Vector2(1000f, 60f));

        overlay.Build(root);
        topBar.Build(root);
        pushBar.Build(root);
        objectiveHint.Build(root);
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
        crosshairFade = crosshair.AddComponent<CanvasGroup>();
        crosshairFade.blocksRaycasts = false;
        crosshairFade.interactable = false;

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
        heroPicker.Build(root);

        lobby.Build(root, LeaveGame);
        menu.Build(root);
        // karta hrdiny (klik na hrdinu v lobby, F2 ve hre) - nad lobby
        heroInfo.Build(root);
        settings.Build(root, () => Cursor.lockState = CursorLockMode.Locked, LeaveGame);
        TextChatUI.Build(root,this);
        menu.OnSettings = () => settings.Open(false);

        var connecting = UiKit.MakeImage(root, "ConnectingOverlay", MenuStyle.Ink, center, Vector2.zero, Vector2.zero);
        UiKit.Stretch(connecting.rectTransform);
        connecting.raycastTarget = true;
        connectingPanel = connecting.gameObject;
        MenuStyle.Background(connecting.transform);
        var tint = UiKit.MakeImage(connecting.transform, "Tint", new Color(0.02f, 0.08f, 0.04f, 0.72f), center, Vector2.zero, Vector2.zero);
        UiKit.Stretch(tint.rectTransform);
        connectingText = MenuStyle.Text(connecting.transform, "Connecting", "Připojuji se…", Vector2.zero, new Vector2(1200, 100), 56, null, TextAlignmentOptions.Center);
        MenuStyle.Button(connecting.transform, "ZRUŠIT", new Vector2(0, -120), new Vector2(340, 70), LeaveGame);
        connectingPanel.SetActive(false);

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

    // Host ukoncil hru: odejit cisty zpusobem sam (jako rucne pres Esc), ne az po pretrzeni spojeni.
    bool hostLeft;
    public void LeaveBecauseHostLeft()
    {
        hostLeft = true;
        LeaveGame();
    }

    // Zavreni okna hostem (Alt+F4...): nejdriv poslat ostatni do menu, pak teprve skoncit.
    bool quitAfterLeave, quitReady;

    void OnEnable() => Application.wantsToQuit += WantsToQuit;
    void OnDisable() => Application.wantsToQuit -= WantsToQuit;

    bool WantsToQuit()
    {
        var network = NetworkManager.Singleton;
        if (quitReady || network == null || !network.IsServer || !network.IsListening || network.ConnectedClientsIds.Count <= 1)
            return true;
        quitAfterLeave = true;
        LeaveGame();
        return false;
    }

    IEnumerator LeaveRoutine()
    {
        leaving = true;

        if (LanDiscovery.Instance != null)
            LanDiscovery.Instance.StopAdvertising();

        var network = NetworkManager.Singleton;
        if (network != null)
        {
            // Host: kdyz vypne server pod rukama ostatnim, hra jim spadne. Proto nejdriv vsem rekne, at odejdou
            // sami, a pocka, az se odpoji (nejdyl 3 s) - teprve pak server vypne.
            var match = MatchManager.Instance;
            if (network.IsServer && network.IsListening && network.ConnectedClientsIds.Count > 1 && match != null && match.IsSpawned)
            {
                match.ServerHostLeaving();
                float until = Time.unscaledTime + 3f;
                while (Time.unscaledTime < until && network.IsListening && network.ConnectedClientsIds.Count > 1)
                    yield return null;
            }

            network.Shutdown();
            while (network.ShutdownInProgress)
                yield return null;
        }

        yield return null;
        Cursor.lockState = CursorLockMode.None;
        menu.ShowStatus(hostLeft ? "Hostitel ukončil hru." : "");
        hostLeft = false;
        leaving = false;
        wasConnected = false;
        everHadPlayer = false;

        if (quitAfterLeave)
        {
            quitReady = true;
            Application.Quit();
        }
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
        UpdateRespawnCountdown(localHero, match);
        // Lobby vidi vsichni pred zapasem a taky hrac, ktery se pripojil do rozehraneho zapasu a jeste si vybira.
        bool joining = hasPlayer && !match.IsLobby && localHero.IsJoining;
        bool inLobby = hasPlayer && (match.IsLobby || joining);
        bool over = hasPlayer && !inLobby && match.IsOver;
        bool playing = hasPlayer && !inLobby;

        if (hasPlayer)
        {
            everHadPlayer = true;
            GameConnection.ConfirmJoined(match.gameName.Value.ToString());
        }

        if (wasConnected && !connected && !leaving)
            menu.ShowStatus(everHadPlayer
                ? "Spojení s hrou bylo ukončeno."
                : "Připojení se nepodařilo. Zkontroluj IP adresu a že host hru opravdu založil (firewall!).");
        if (!connected && !leaving)
            everHadPlayer = false;
        wasConnected = connected;

        menu.SetVisible(!connected && !PotgIntro.Active);   // (ukazka POTG uvodu z editoru bezi pres menu)
        menu.Tick(settings.IsOpen);

        connectingPanel.SetActive(connected && !hasPlayer);
        lobby.SetVisible(inLobby);
        lobby.Tick(localHero, joining);
        // Behem prehravani "play of the game" se koncova obrazovka neukazuje (hraje se ve scene).
        bool replay = ReplayPlayer.Active;
        endPanel.SetActive(over && !replay && !PotgIntro.Active);

        // Zmena hrdiny behem zapasu (F1).
        heroPicker.Tick(localHero, playing && !over && !PotgUI.IsShowing && !TextChatUI.BlocksGameplayInput);

        // Karta hrdiny se schopnostmi (F2; v lobby i kliknutim na hrdinu).
        heroInfo.Tick(hasPlayer && playing && !inLobby && !over && !replay && !PotgUI.IsShowing && !TextChatUI.BlocksGameplayInput,
            localHero != null ? localHero.heroId.Value : PlayerHero.PreferredHero);

        UpdateCursor(hasPlayer, inLobby, over || heroPicker.IsOpen || heroInfo.IsOpen);
        UpdateHud(match, localHero, playing, over);
        // HUD: zivy hrac, nebo pri prehravani POTG HUD hrace, ktery akci predvedl (podle zaznamu).
        // (killcam: HUD vraha v zaznamu neni - jen obraz a zamerovac)
        bool killcam = ReplayPlayer.IsKillcam;
        bool killerHealth = ReplayPlayer.KillerHealth(out int killerHero, out int killerTeam, out float killerHp, out float killerMax);
        hud.SetVisible((replay && !killcam) || (killcam && killerHealth) || (playing && !over && localHero != null && !replay));
        if (killcam) { if (killerHealth) hud.TickKillcam(killerHero, killerTeam, killerHp, killerMax); }
        else if (replay)
            hud.TickReplay(ReplayPlayer.Frame, ReplayPlayer.Camera);
        else
            hud.Tick(localHero);
        // Po konci zapasu (a po dohrani play of the game) je tabulka hracu videt porad, ne jen na Tab.
        overlay.Tick(localHero, playing && !replay && !PotgIntro.Active, match, over && !PotgUI.IsShowing && !killcam);
        topBar.Tick(localHero, playing && !over, match);
        pushBar.Tick(localHero, playing && !over, match);
        objectiveHint.Tick(localHero, match, playing && !over);
        capture.Tick(localHero, playing && !over, match);
        buffs.Tick((playing && !over && !replay) || replay);   // v POTG buffy hrace ze zaznamu, v killcamu vraha (slivovice)
        boilerPrompt.text = playing && !over && !replay ? (Tractor.PromptText != "" ? Tractor.PromptText : PelletFeeder.PromptText != "" ? PelletFeeder.PromptText : Boiler.PromptText) : "";
        UpdateRing(match, playing && !over && !replay);
        UpdateEndScreen(network, match, over);
        UpdateLegacyHud(playing);

        bool locked = GameSettings.CursorLocked;
        UpdateScope(localHero, playing && !over && locked && !replay && !PotgUI.IsShowing);
        crosshairFade.alpha = 1f - (scopeOverlay.IsVisible ? scopeOverlay.Opacity : 0f);
        crosshair.SetActive(crosshairFade.alpha > 0.005f && ((playing && !over && locked && !replay) || (replay && ReplayPlayer.Frame != null && (killcam || !ReplayPlayer.Frame.thirdPerson))));
        UpdateBlockBar(localHero, playing && !over);

        bool showSettings = playing && !over && !locked && !heroPicker.IsOpen && !heroInfo.IsOpen && !replay && !PotgUI.IsShowing && !TextChatUI.BlocksGameplayInput;
        if (showSettings && !settings.IsOpen) settings.Open(true);
        if (settings.IsOpen && ((settings.InGame && !showSettings) || (connected && !playing))) settings.Close();
        settings.Tick();
    }

    // Prsten: T nasadi / sunda (jen kdo ho nese). Nasazeny = zsedly zavoj pres obrazovku (jen pro nositele;
    // v POTG i divakum, kdyz ho mel hrac v zaznamu nasazeny).
    void UpdateRing(MatchManager match, bool playing)
    {
        if (ringVeil == null)
        {
            if (uiRoot == null) return;
            ringVeil = UiKit.MakeImage(uiRoot, "PrstenZavoj", new Color(0.42f, 0.45f, 0.5f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UiKit.Stretch(ringVeil.rectTransform);
            ringVeil.rectTransform.SetAsFirstSibling();
        }

        if (playing && match != null && GameSettings.CursorLocked && Keyboard.current != null
            && Keyboard.current.tKey.wasPressedThisFrame && MatchManager.LocalHasRing
            && (MatchManager.LocalWearsRing || MatchManager.LocalRingCooldown <= 0f))
            match.ToggleRingRpc();

        bool worn = playing && MatchManager.LocalWearsRing;
        if (worn != ringWornShown)
        {
            ringWornShown = worn;
            if (playing && MatchManager.LocalHasRing)
                CaptureUI.Announce(worn ? "Nasadil sis Prsten" : "Sundal sis Prsten", PickupSpot.KindColor(PickupKind.Ring), 2.2f);
        }
        var frame = ReplayPlayer.Active && !ReplayPlayer.IsKillcam ? ReplayPlayer.Frame : null;
        bool replayWorn = frame != null && frame.ringWorn && frame.health > 0f;
        var c = ringVeil.color;
        c.a = Mathf.MoveTowards(c.a, worn || replayWorn ? 0.32f : 0f, Time.unscaledDeltaTime * 0.8f);
        ringVeil.color = c;
        if (ringVeil.gameObject.activeSelf != c.a > 0.001f) ringVeil.gameObject.SetActive(c.a > 0.001f);
    }

    void UpdateRespawnCountdown(PlayerHero localHero, MatchManager match)
    {
        if (respawnText == null) return;
        var health = localHero != null ? localHero.GetComponent<Health>() : null;
        float left = PlayerRespawn.LocalRespawnAt - Time.time;
        bool show = health != null && health.currentHealth.Value <= 0f && left > 0f
            && match != null && !match.IsLobby && !match.IsOver && !PotgUI.IsShowing;
        if (respawnText.gameObject.activeSelf != show) respawnText.gameObject.SetActive(show);
        if (show) respawnText.text = $"OŽIVENÍ ZA <color=#F28C1A>{Mathf.CeilToInt(left)}</color> s";
    }

    void UpdateScope(PlayerHero localHero, bool allowed)
    {
        var scope = allowed && localHero != null ? localHero.GetComponent<ScopeZoom>() : null;
        var fpc = scope != null ? localHero.GetComponent<FirstPersonController>() : null;
        float blend = scope != null && scope.isActiveAndEnabled && fpc != null && !fpc.IsDead && !fpc.ThirdPerson && !ThirdPersonPreview.Active
            ? scope.ScopeBlend : 0f;
        // POTG already records the camera FOV. Killcam uses its own unzoomed camera.
        if (ReplayPlayer.Active && !ReplayPlayer.IsKillcam && ReplayPlayer.Frame != null && ReplayPlayer.Camera != null)
        {
            var frame = ReplayPlayer.Frame;
            var definition = HeroRegistry.Get(frame.hudHero);
            var weapon = definition != null ? definition.weapon : null;
            if (!frame.thirdPerson && frame.health > 0f && weapon != null && weapon.scopeFov > 0f)
                blend = ScopeZoom.BlendForFov(ReplayPlayer.Camera.fieldOfView, scope != null ? scope.BaseFov : 60f, weapon.scopeFov);
        }
        // killcam: vrah (odstrelovac) miril pres zamerovac
        if (ReplayPlayer.IsKillcam)
            blend = ReplayPlayer.KillcamScopeBlend;
        // odstrelovacka: tmavy dalekohled; puska s priblizenim (David): holograficky zamerovac
        var scopeHero = ReplayPlayer.Active && !ReplayPlayer.IsKillcam && ReplayPlayer.Frame != null ? HeroRegistry.Get(ReplayPlayer.Frame.hudHero)
            : localHero != null ? localHero.Hero : null;
        scopeOverlay.SetHolo(!ReplayPlayer.IsKillcam && scopeHero != null && scopeHero.weapon != null && !scopeHero.weapon.HasScopedShot);
        scopeOverlay.SetBlend(blend);
        scopeOverlay.gameObject.SetActive(blend > 0.005f);
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
    void LateUpdate()
    {
        // Apply after chat, gameplay and menus have chosen the cursor lock.
        // An explicit hide must never persist into an unlocked menu.
        Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
    }

    void UpdateCursor(bool hasPlayer, bool inLobby, bool over)
    {
        bool needsFree = hasPlayer && (inLobby || over || TextChatUI.IsTyping);
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

        scoreText.text = "";   // skore ukazuje horni lista (TopBarUI)

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

        if (isHost && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame && !TextChatUI.BlocksGameplayInput)
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
