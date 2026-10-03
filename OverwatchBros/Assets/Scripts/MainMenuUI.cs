using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Uvodni menu: zalozit hru, nebo se pripojit (seznam her nalezenych v LAN + rucni IP).
public class MainMenuUI
{
    const int MaxRows = 4;

    public GameObject Root { get; private set; }

    TMP_InputField nickInput;
    TMP_InputField gameNameInput;
    TMP_InputField ipInput;
    TextMeshProUGUI statusText;
    TextMeshProUGUI noGamesText;
    readonly List<Button> gameRows = new List<Button>();
    readonly List<LanDiscovery.Game> shownGames = new List<LanDiscovery.Game>();
    float nextRefresh;

    public void Build(Transform canvas)
    {
        var center = new Vector2(0.5f, 0.5f);

        var root = UiKit.MakeImage(canvas, "MainMenu", new Color(0.03f, 0.04f, 0.07f, 0.97f), center, Vector2.zero, Vector2.zero);
        UiKit.Stretch(root.rectTransform);
        root.raycastTarget = true;
        Root = root.gameObject;

        UiKit.MakeText(Root.transform, "Title", "BROS OVERWATCH", 100, TextAlignmentOptions.Center, center,
            new Vector2(0f, 440f), new Vector2(1400f, 130f), UiKit.Accent);
        UiKit.MakeText(Root.transform, "Subtitle", "LAN HERO SHOOTER  ·  TEAM DEATHMATCH  ·  DOBÝVÁNÍ BODŮ", 34, TextAlignmentOptions.Center, center,
            new Vector2(0f, 362f), new Vector2(1400f, 46f), UiKit.Muted);

        // Prezdivka je nad kartami (karty zacinaji az pod ni), aby se nic neprekryvalo.
        UiKit.MakeText(Root.transform, "NickLabel", "PŘEZDÍVKA", 34, TextAlignmentOptions.Right, center,
            new Vector2(-330f, 290f), new Vector2(260f, 60f), Color.white);
        string nick = PlayerPrefs.GetString("nick", "Hráč" + Random.Range(10, 99));
        nickInput = UiKit.MakeInput(Root.transform, "Tvoje jméno", center, new Vector2(80f, 290f), new Vector2(520f, 66f), nick, 36f);
        nickInput.characterLimit = 20;

        BuildHostCard(center, nick);
        BuildJoinCard(center);

        UiKit.MakeButton(Root.transform, "UKONČIT HRU", new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(300f, 70f),
            QuitGame, new Color(0.45f, 0.18f, 0.18f, 1f), 30f);

        statusText = UiKit.MakeText(Root.transform, "Status", "", 34, TextAlignmentOptions.Center, center,
            new Vector2(0f, -385f), new Vector2(1600f, 50f), new Color(1f, 0.45f, 0.4f));

        UiKit.MakeText(Root.transform, "Controls",
            "WASD pohyb  ·  Shift běh / schopnost  ·  Mezerník skok  ·  Ctrl dřep  ·  LMB útok  ·  PTM blok / odpal  ·  E schopnost  ·  R přebití  ·  Tab tabulka  ·  F1 změna hrdiny  ·  Q schopnost  ·  Esc nastavení",
            24, TextAlignmentOptions.Center, center, new Vector2(0f, -470f), new Vector2(1800f, 40f), UiKit.Muted);
    }

    void BuildHostCard(Vector2 center, string nick)
    {
        var card = UiKit.MakeImage(Root.transform, "HostCard", UiKit.PanelLight, center, new Vector2(-470f, -70f), new Vector2(820f, 560f));

        UiKit.MakeText(card.transform, "Heading", "ZALOŽIT HRU", 52, TextAlignmentOptions.Center, center,
            new Vector2(0f, 225f), new Vector2(760f, 70f), UiKit.Accent);
        UiKit.MakeText(card.transform, "Sub", "Ostatní uvidí tvoji hru v seznamu a připojí se.", 28, TextAlignmentOptions.Center, center,
            new Vector2(0f, 160f), new Vector2(760f, 40f), UiKit.Muted);

        UiKit.MakeText(card.transform, "NameLabel", "NÁZEV HRY", 26, TextAlignmentOptions.Left, center,
            new Vector2(0f, 95f), new Vector2(700f, 36f), UiKit.Muted);
        gameNameInput = UiKit.MakeInput(card.transform, "Název hry", center, new Vector2(0f, 40f), new Vector2(700f, 64f),
            PlayerPrefs.GetString("gameName", $"Hra hráče {nick}"));
        gameNameInput.characterLimit = 30;

        UiKit.MakeButton(card.transform, "ZALOŽIT HRU", center, new Vector2(0f, -100f), new Vector2(700f, 100f), OnHostClicked, UiKit.Green, 44f);

        UiKit.MakeText(card.transform, "Hint", "Budeš hostitel: v lobby vyberete týmy a hrdiny a ty spustíš zápas.", 26,
            TextAlignmentOptions.Center, center, new Vector2(0f, -205f), new Vector2(740f, 70f), UiKit.Muted);
    }

    void BuildJoinCard(Vector2 center)
    {
        var card = UiKit.MakeImage(Root.transform, "JoinCard", UiKit.PanelLight, center, new Vector2(470f, -70f), new Vector2(820f, 560f));

        UiKit.MakeText(card.transform, "Heading", "PŘIPOJIT SE", 52, TextAlignmentOptions.Center, center,
            new Vector2(0f, 225f), new Vector2(760f, 70f), UiKit.Accent);
        UiKit.MakeText(card.transform, "Sub", "Hry nalezené v místní síti:", 28, TextAlignmentOptions.Center, center,
            new Vector2(0f, 165f), new Vector2(760f, 40f), UiKit.Muted);

        noGamesText = UiKit.MakeText(card.transform, "NoGames", "Hledám hry v síti…", 30, TextAlignmentOptions.Center, center,
            new Vector2(0f, 70f), new Vector2(740f, 50f), UiKit.Muted);

        for (int i = 0; i < MaxRows; i++)
        {
            int index = i;
            var row = UiKit.MakeButton(card.transform, "-", center, new Vector2(0f, 105f - i * 70f), new Vector2(740f, 60f),
                () => OnGameRowClicked(index), UiKit.ButtonBase, 28f);
            row.gameObject.SetActive(false);
            gameRows.Add(row);
        }

        UiKit.MakeText(card.transform, "ManualLabel", "nebo zadej IP hostitele:", 26, TextAlignmentOptions.Left, center,
            new Vector2(0f, -172f), new Vector2(740f, 36f), UiKit.Muted);
        ipInput = UiKit.MakeInput(card.transform, "např. 192.168.0.12", center, new Vector2(-135f, -232f), new Vector2(470f, 60f),
            PlayerPrefs.GetString("lastIp", ""));
        UiKit.MakeButton(card.transform, "PŘIPOJIT", center, new Vector2(260f, -232f), new Vector2(220f, 60f), OnManualJoinClicked, UiKit.Accent, 30f);
    }

    public void SetVisible(bool visible)
    {
        if (Root == null || Root.activeSelf == visible) return;

        Root.SetActive(visible);

        var discovery = LanDiscovery.GetOrCreate();
        if (visible)
        {
            discovery.StartListening();
            nextRefresh = 0f;
        }
        else
        {
            discovery.StopListening();
        }
    }

    public void ShowStatus(string message)
    {
        if (statusText != null)
            statusText.text = message ?? "";
    }

    public void Tick()
    {
        if (Root == null || !Root.activeSelf || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;

        var discovery = LanDiscovery.Instance;
        shownGames.Clear();
        if (discovery != null)
        {
            shownGames.AddRange(discovery.Games.Values);
            shownGames.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        noGamesText.gameObject.SetActive(shownGames.Count == 0);
        noGamesText.text = discovery != null && discovery.IsListening
            ? "Hledám hry v síti…"
            : "Hledání v síti není dostupné — zadej IP ručně.";

        for (int i = 0; i < gameRows.Count; i++)
        {
            bool has = i < shownGames.Count;
            gameRows[i].gameObject.SetActive(has);
            if (!has) continue;

            var game = shownGames[i];
            UiKit.SetButtonLabel(gameRows[i], $"{game.name}   <size=70%><color=#A6B3C7>{game.players} hráč(ů)  ·  {game.address}</color></size>");
        }
    }

    void SaveInputs()
    {
        string nick = nickInput.text.Trim();
        if (string.IsNullOrEmpty(nick))
            nick = "Hráč";

        PlayerHero.PreferredName = nick;
        PlayerPrefs.SetString("nick", nick);
        PlayerPrefs.SetString("gameName", gameNameInput.text.Trim());
    }

    // Ukonci hru (v editoru zastavi Play mod).
    void QuitGame()
    {
        SaveInputs();
        PlayerPrefs.Save();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnHostClicked()
    {
        SaveInputs();

        string gameName = gameNameInput.text.Trim();
        if (string.IsNullOrEmpty(gameName))
            gameName = $"Hra hráče {PlayerHero.PreferredName}";

        ShowStatus(GameConnection.Host(gameName) ? "" : GameConnection.LastError);
    }

    void OnGameRowClicked(int index)
    {
        if (index >= shownGames.Count) return;

        SaveInputs();
        var game = shownGames[index];
        ShowStatus(GameConnection.Join(game.address, game.port) ? $"Připojuji se k „{game.name}“…" : GameConnection.LastError);
    }

    void OnManualJoinClicked()
    {
        SaveInputs();

        string address = ipInput.text.Trim();
        if (string.IsNullOrEmpty(address))
        {
            ShowStatus("Zadej IP adresu hostitele.");
            return;
        }

        PlayerPrefs.SetString("lastIp", address);
        ShowStatus(GameConnection.Join(address, GameConnection.Port) ? $"Připojuji se k {address}…" : GameConnection.LastError);
    }
}
