using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Lobby pred zapasem: hraci vidi jeden druheho, kazdy si vybere tym a hrdinu, host nastavi skore a spusti zapas.
public class LobbyUI
{
    public GameObject Root { get; private set; }

    TextMeshProUGUI title;
    readonly TextMeshProUGUI[] teamLists = new TextMeshProUGUI[PlayerTeam.TeamCount];
    readonly Button[] teamButtons = new Button[PlayerTeam.TeamCount];
    readonly List<Button> heroButtons = new List<Button>();
    TextMeshProUGUI heroInfo;
    TextMeshProUGUI scoreText;
    Button scoreMinus;
    Button scorePlus;
    Button startButton;
    Button enterButton;
    Button modeButton;
    TextMeshProUGUI waitText;

    float nextRefresh;

    public void Build(Transform canvas, Action onLeave)
    {
        var center = new Vector2(0.5f, 0.5f);

        var root = UiKit.MakeImage(canvas, "Lobby", new Color(0.03f, 0.04f, 0.07f, 0.80f), center, Vector2.zero, Vector2.zero);
        UiKit.Stretch(root.rectTransform);
        root.raycastTarget = true;
        Root = root.gameObject;

        title = UiKit.MakeText(Root.transform, "Title", "LOBBY", 70, TextAlignmentOptions.Center, center,
            new Vector2(0f, 455f), new Vector2(1600f, 90f), UiKit.Accent);

        for (int team = 0; team < PlayerTeam.TeamCount; team++)
        {
            int t = team;
            float x = team == 0 ? -450f : 450f;

            var card = UiKit.MakeImage(Root.transform, $"Team{team}Card", UiKit.PanelLight, center, new Vector2(x, 145f), new Vector2(800f, 440f));
            UiKit.MakeText(card.transform, "Heading", $"TÝM {team}", 48, TextAlignmentOptions.Center, center,
                new Vector2(0f, 185f), new Vector2(760f, 60f), UiKit.TeamColor(team));

            teamLists[team] = UiKit.MakeText(card.transform, "List", "", 34, TextAlignmentOptions.TopLeft, center,
                new Vector2(0f, 10f), new Vector2(720f, 270f));

            teamButtons[team] = UiKit.MakeButton(card.transform, $"PŘEJÍT DO TÝMU {team}", center, new Vector2(0f, -175f),
                new Vector2(520f, 66f), () => SelectTeam(t), UiKit.ButtonBase, 30f);
        }

        UiKit.MakeText(Root.transform, "HeroHeading", "HRDINA", 40, TextAlignmentOptions.Center, center,
            new Vector2(0f, -110f), new Vector2(600f, 50f), UiKit.Accent);

        var heroes = HeroRegistry.All;
        float spacing = 340f;
        float startX = -(heroes.Length - 1) * spacing / 2f;
        for (int i = 0; i < heroes.Length; i++)
        {
            int index = i;
            string label = heroes[i].heroName;
            var abilityNames = new List<string>();
            if (heroes[i].ability != null) abilityNames.Add(heroes[i].ability.abilityName);
            if (heroes[i].secondaryAbility != null) abilityNames.Add(heroes[i].secondaryAbility.abilityName);
            if (heroes[i].altAbility != null) abilityNames.Add(heroes[i].altAbility.abilityName);
            if (heroes[i].rmbAbility != null) abilityNames.Add(heroes[i].rmbAbility.abilityName);
            if (abilityNames.Count > 0)
                label += $"\n<size=55%>{string.Join(" · ", abilityNames)}</size>";

            var button = UiKit.MakeButton(Root.transform, label, center, new Vector2(startX + i * spacing, -195f),
                new Vector2(320f, 94f), () => SelectHero(index), UiKit.ButtonBase, 34f);
            heroButtons.Add(button);
        }

        heroInfo = UiKit.MakeText(Root.transform, "HeroInfo", "", 28, TextAlignmentOptions.Center, center,
            new Vector2(0f, -285f), new Vector2(1500f, 50f), UiKit.Muted);

        modeButton = UiKit.MakeButton(Root.transform, "REŽIM", center, new Vector2(-40f, -352f), new Vector2(760f, 56f),
            ToggleMode, UiKit.ButtonBase, 28f);

        UiKit.MakeButton(Root.transform, "OPUSTIT HRU", center, new Vector2(-640f, -430f), new Vector2(380f, 80f),
            () => onLeave?.Invoke(), new Color(0.45f, 0.18f, 0.18f, 1f), 32f);

        scoreText = UiKit.MakeText(Root.transform, "Score", "", 34, TextAlignmentOptions.Center, center,
            new Vector2(-40f, -430f), new Vector2(420f, 60f));
        scoreMinus = UiKit.MakeButton(Root.transform, "−", center, new Vector2(-300f, -430f), new Vector2(70f, 70f),
            () => ChangeScore(-1), UiKit.ButtonBase, 40f);
        scorePlus = UiKit.MakeButton(Root.transform, "+", center, new Vector2(220f, -430f), new Vector2(70f, 70f),
            () => ChangeScore(1), UiKit.ButtonBase, 40f);

        startButton = UiKit.MakeButton(Root.transform, "START ZÁPASU", center, new Vector2(600f, -430f), new Vector2(460f, 90f),
            StartMatch, UiKit.Green, 42f);
        enterButton = UiKit.MakeButton(Root.transform, "VSTOUPIT DO HRY", center, new Vector2(600f, -430f), new Vector2(460f, 90f),
            EnterMatch, UiKit.Green, 42f);
        waitText = UiKit.MakeText(Root.transform, "Wait", "Čekáme, až host spustí zápas…", 34, TextAlignmentOptions.Center, center,
            new Vector2(560f, -430f), new Vector2(700f, 60f), UiKit.Muted);

        Root.SetActive(false);
    }

    public void SetVisible(bool visible)
    {
        if (Root != null && Root.activeSelf != visible)
        {
            Root.SetActive(visible);
            nextRefresh = 0f;
        }
    }

    // joining = zapas uz bezi a tenhle hrac si po pripojeni teprve vybira tym a hrdinu.
    public void Tick(PlayerHero localHero, bool joining = false)
    {
        if (Root == null || !Root.activeSelf || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.15f;

        var match = MatchManager.Instance;
        var network = NetworkManager.Singleton;
        bool isHost = network != null && network.IsServer;

        string gameTitle = match != null && match.gameName.Value.Length > 0 ? $"  ·  {match.gameName.Value}" : "";
        title.text = joining ? "ZÁPAS UŽ BĚŽÍ  ·  vyber si tým a hrdinu" : "LOBBY" + gameTitle;

        RefreshTeams(localHero);
        RefreshHeroes(localHero);

        // Rezim hry voli host (ostatni ho jen vidi): team deathmatch na pocet zabiti, nebo dobyvani bodu.
        bool captureMode = match != null && match.IsCapture;
        UiKit.SetButtonLabel(modeButton, captureMode
            ? $"REŽIM:  <color=#F28C1A>DOBÝVÁNÍ BODŮ</color>  <size=75%>({MatchManager.CapturePointsToWin} body ze {MatchManager.CapturePointCount})</size>"
            : "REŽIM:  <color=#F28C1A>TEAM DEATHMATCH</color>");
        modeButton.interactable = isHost && !joining;

        int score = match != null ? match.scoreToWinSynced.Value : 10;
        scoreText.text = captureMode
            ? $"Zabrání bodu: <color=#F28C1A>{match.captureSeconds.Value} s</color>"
            : $"Zabití na výhru: <color=#F28C1A>{score}</color>";
        scoreMinus.gameObject.SetActive(isHost && !joining);
        scorePlus.gameObject.SetActive(isHost && !joining);
        startButton.gameObject.SetActive(isHost && !joining);
        enterButton.gameObject.SetActive(joining);
        waitText.gameObject.SetActive(!isHost && !joining);
    }

    void RefreshTeams(PlayerHero localHero)
    {
        var players = new List<PlayerHero>(UnityEngine.Object.FindObjectsByType<PlayerHero>());
        players.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));

        var builders = new StringBuilder[PlayerTeam.TeamCount];
        for (int i = 0; i < builders.Length; i++)
            builders[i] = new StringBuilder();

        int localTeam = -1;
        foreach (var player in players)
        {
            if (!player.IsSpawned) continue;

            var team = player.GetComponent<PlayerTeam>();
            if (team == null) continue;

            int id = Mathf.Clamp(team.teamId.Value, 0, PlayerTeam.TeamCount - 1);
            bool isLocal = player == localHero;
            if (isLocal) localTeam = id;

            string host = player.OwnerClientId == NetworkManager.ServerClientId ? "<color=#F28C1A>[HOST]</color> " : "";
            string name = isLocal ? $"<b>{player.DisplayName}</b>" : player.DisplayName;
            string hero = player.Hero != null ? player.Hero.heroName : "…";
            builders[id].AppendLine($"{host}{name}   <size=75%><color=#A6B3C7>{hero}</color></size>");
        }

        for (int i = 0; i < PlayerTeam.TeamCount; i++)
        {
            teamLists[i].text = builders[i].ToString();
            teamButtons[i].interactable = localHero != null && localTeam != i;
            UiKit.SetButtonColor(teamButtons[i], localTeam == i ? UiKit.TeamColor(i) : UiKit.ButtonBase);
            UiKit.SetButtonLabel(teamButtons[i], localTeam == i ? $"JSI V TÝMU {i}" : $"PŘEJÍT DO TÝMU {i}");
        }
    }

    void RefreshHeroes(PlayerHero localHero)
    {
        int selected = localHero != null ? localHero.heroId.Value : PlayerHero.PreferredHero;
        var heroes = HeroRegistry.All;

        for (int i = 0; i < heroButtons.Count; i++)
            UiKit.SetButtonColor(heroButtons[i], i == selected ? UiKit.Accent : UiKit.ButtonBase);

        var hero = HeroRegistry.Get(selected);
        if (hero == null || hero.weapon == null)
        {
            heroInfo.text = "";
            return;
        }

        string ability = AbilityLabel(hero);
        string mode = hero.weapon.IsMelee ? "blízký souboj" : hero.weapon.IsProjectile ? "projektil" : "okamžitý zásah";
        string reach = hero.weapon.IsMelee ? "dosah" : "dostřel";
        heroInfo.text = $"{hero.heroName}:  {hero.maxHealth:0} HP  ·  {hero.weapon.weaponName} ({hero.weapon.damage:0} dmg, {mode}, {reach} {hero.weapon.range:0} m)  ·  {ability}";
    }

    static string AbilityLabel(HeroDefinition hero)
    {
        var parts = new List<string>();
        if (hero.ability != null)
            parts.Add($"[Q] {hero.ability.abilityName}");
        if (hero.secondaryAbility != null)
            parts.Add(hero.secondaryAbilityKind == AbilityKind.Mine
                ? $"[SHIFT hodit, PRAVÉ TL. odpálit] {hero.secondaryAbility.abilityName}"
                : $"[SHIFT] {hero.secondaryAbility.abilityName}");

        if (hero.blockAbility != null)
            parts.Add($"[PRAVÉ TL.] {hero.blockAbility.abilityName}");
        if (hero.altAbility != null)
            parts.Add($"[E] {hero.altAbility.abilityName}");
        if (hero.rmbAbility != null)
            parts.Add($"[PRAVÉ TL.] {hero.rmbAbility.abilityName}");

        return parts.Count > 0 ? string.Join("  ·  ", parts) : "bez schopnosti";
    }

    void SelectTeam(int team)
    {
        var local = LocalPlayer();
        if (local == null) return;

        var teamComponent = local.GetComponent<PlayerTeam>();
        if (teamComponent != null)
            teamComponent.SelectTeam(team);
    }

    void SelectHero(int index)
    {
        PlayerHero.PreferredHero = index;
        PlayerPrefs.SetInt("preferredHero", index);

        var local = LocalPlayer();
        if (local != null)
            local.SelectHero(index);
    }

    void ChangeScore(int delta)
    {
        var match = MatchManager.Instance;
        if (match == null) return;

        if (match.IsCapture)
            match.SetCaptureSeconds(match.captureSeconds.Value + delta * 5);
        else
            match.SetScoreToWin(match.scoreToWinSynced.Value + delta);
    }

    void ToggleMode()
    {
        var match = MatchManager.Instance;
        if (match != null)
            match.SetGameMode(match.IsCapture ? MatchManager.ModeDeathmatch : MatchManager.ModeCapture);
    }

    void EnterMatch()
    {
        var local = LocalPlayer();
        if (local != null)
            local.ConfirmJoin();
    }

    void StartMatch()
    {
        var match = MatchManager.Instance;
        if (match != null)
            match.StartMatch();
    }

    static PlayerHero LocalPlayer()
    {
        var network = NetworkManager.Singleton;
        if (network == null || network.LocalClient == null || network.LocalClient.PlayerObject == null) return null;
        return network.LocalClient.PlayerObject.GetComponent<PlayerHero>();
    }
}
