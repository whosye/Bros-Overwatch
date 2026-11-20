using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

// Lobby pred zapasem: hraci vidi jeden druheho, kazdy si vybere tym a hrdinu, host nastavi skore a spusti zapas.
public class LobbyUI
{
    public GameObject Root { get; private set; }

    TextMeshProUGUI title;
    readonly TextMeshProUGUI[] teamLists = new TextMeshProUGUI[PlayerTeam.TeamCount];
    readonly Button[] teamButtons = new Button[PlayerTeam.TeamCount];
    readonly List<Button> heroButtons = new List<Button>();
    TextMeshProUGUI scoreText;
    Button scoreMinus;
    Button scorePlus;
    Button startButton;
    Button enterButton;
    Button modeButton;
    Button mapButton;
    Button testButton;
    Button heroModeButton;
    Button musicButton;
    TextMeshProUGUI waitText;

    // Boti (jen host): hrdina a tym pristiho bota, pridat / odebrat vsechny.
    Button botHeroButton, botTeamButton, botAddButton, botRemoveButton, botDifficultyButton;
    int botHero;
    int botTeam = 1;

    float nextRefresh;

    readonly TextMeshProUGUI[] teamCounts = new TextMeshProUGUI[2];
    readonly RectTransform[] rosterContent = new RectTransform[2];
    readonly GameObject[] views = new GameObject[3];
    readonly Button[] viewTabs = new Button[3];
    readonly List<AbilityRow> abilityRows = new List<AbilityRow>();
    TextMeshProUGUI selectedName, selectedStats, roleHint, selectionHint, mapCaption;
    RawImage characterPreview;
    Image mapPreview;
    RectTransform abilityContent;
    int activeView, describedHero = -1;
    class AbilityRow
    {
        public Image root;
        public TextMeshProUGUI key, name, stats, description;
    }

    public void Build(Transform canvas, Action onLeave)
    {
        var root = UiKit.MakeImage(canvas, "Lobby", Color.clear, MenuStyle.Center, Vector2.zero, Vector2.zero);
        UiKit.Stretch(root.rectTransform); root.raycastTarget = true; Root = root.gameObject;
        MenuStyle.Background(Root.transform);
        var veil = UiKit.MakeImage(Root.transform, "LobbyShade", new Color(.015f,.07f,.035f,.40f), MenuStyle.Center, Vector2.zero, Vector2.zero);
        UiKit.Stretch(veil.rectTransform);
        musicButton = MainMenuUI.BuildMusicToggle(Root.transform);
        var board = MenuStyle.Box(Root.transform, "LobbyBoard", new Vector2(0,-10), new Vector2(1420,880)).transform;
        MenuStyle.Compact(board, .88f);
        MenuStyle.Text(board, "Title", "LOBBY", new Vector2(-430,377), new Vector2(480,60), 46).fontStyle = FontStyles.Bold | FontStyles.Italic;
        title = MenuStyle.Text(board, "ServerName", "", new Vector2(-260,331), new Vector2(820,38), 24, MenuStyle.Muted);
        roleHint = MenuStyle.Text(board, "Role", "", new Vector2(470,367), new Vector2(420,48), 24, MenuStyle.Peach, TextAlignmentOptions.Right);
        MenuStyle.Rule(board, new Vector2(0,295), 1330);
        for (int team=0; team<2; team++)
        {
            int t=team;
            var card = MenuStyle.Box(board, "Team"+team, new Vector2(-470,team==0?175:-35), new Vector2(420,190), MenuStyle.Field).transform;
            MenuStyle.Text(card, "TeamHeading", "TÝM "+(team+1), new Vector2(-90,65), new Vector2(200,38), 30, UiKit.TeamColor(team));
            teamCounts[team] = MenuStyle.Text(card, "Count", "", new Vector2(120,65), new Vector2(130,38), 21, MenuStyle.Muted, TextAlignmentOptions.Right);
            rosterContent[team] = ScrollContent(card, "Roster", new Vector2(0,5), new Vector2(380,76));
            teamLists[team] = MenuStyle.Text(rosterContent[team], "Players", "", Vector2.zero, new Vector2(380,76), 22, MenuStyle.Cream, TextAlignmentOptions.TopLeft);
            UiKit.Stretch(teamLists[team].rectTransform); teamLists[team].richText = true;
            teamLists[team].overflowMode = TextOverflowModes.Overflow;
            teamButtons[team] = MenuStyle.Button(card, "PŘIPOJIT SE", new Vector2(0,-66), new Vector2(380,36), ()=>SelectTeam(t), false, 24);
        }
        var showcase = MenuStyle.Box(board, "SelectedHero", new Vector2(0,70), new Vector2(420,420), MenuStyle.Field).transform;
        var preview = new GameObject("CharacterPreview", typeof(RectTransform), typeof(RawImage));
        preview.transform.SetParent(showcase,false);
        var rect = (RectTransform)preview.transform; rect.anchorMin=rect.anchorMax=rect.pivot=MenuStyle.Center;
        rect.anchoredPosition=new Vector2(0,-25); rect.sizeDelta=new Vector2(260,260);
        characterPreview=preview.GetComponent<RawImage>(); characterPreview.raycastTarget=false;
        selectedName=MenuStyle.Text(showcase,"Title","",new Vector2(0,169),new Vector2(380,50),40,null,TextAlignmentOptions.Center);
        selectedStats=MenuStyle.Text(showcase,"HeroStats","",new Vector2(0,127),new Vector2(380,36),22,MenuStyle.Muted,TextAlignmentOptions.Center);
        MenuStyle.Button(showcase,"SCHOPNOSTI  ›",new Vector2(0,-177),new Vector2(220,34),()=>SetView(1),false,24);

        var tools=MenuStyle.Box(board,"LobbyTools",new Vector2(470,70),new Vector2(420,420),MenuStyle.Field).transform;
        string[] tabNames={"ZÁPAS","SCHOPNOSTI","BOTI"};
        for(int i=0;i<3;i++)
        {
            int index=i;
            viewTabs[i]=MenuStyle.Button(tools,tabNames[i],new Vector2(-130+i*130,174),new Vector2(120,34),()=>SetView(index),false,23);
            views[i]=MenuStyle.Box(tools,"Page"+i,new Vector2(0,-25),new Vector2(400,330),Color.clear).gameObject;
        }
        var settings=views[0].transform;
        mapButton=MenuStyle.Button(settings,"",new Vector2(0,136),new Vector2(380,44),NextMap,false,25);
        modeButton=MenuStyle.Button(settings,"",new Vector2(0,80),new Vector2(380,44),ToggleMode,false,24);
        scoreText=MenuStyle.Text(settings,"WinRule","",new Vector2(0,24),new Vector2(270,44),23,null,TextAlignmentOptions.Center);
        scoreMinus=MenuStyle.Button(settings,"−",new Vector2(-165,24),new Vector2(48,36),()=>ChangeScore(-1),false,28);
        scorePlus=MenuStyle.Button(settings,"+",new Vector2(165,24),new Vector2(48,36),()=>ChangeScore(1),false,28);
        heroModeButton=MenuStyle.Button(settings,"",new Vector2(0,-34),new Vector2(380,44),()=>
        { var m=MatchManager.Instance; if(m!=null)m.SetHeroMode((m.heroMode.Value+1)%MatchManager.HeroModeNames.Length); },false,23);
        testButton=MenuStyle.Button(settings,"",new Vector2(0,-90),new Vector2(380,38),ToggleTestCooldowns,false,22);
        MenuStyle.Text(settings,"SettingsHint","Nastavení mění hostitel.",new Vector2(0,-141),new Vector2(380,32),20,MenuStyle.Muted,TextAlignmentOptions.Center);
        abilityContent=ScrollContent(views[1].transform,"Abilities",Vector2.zero,new Vector2(380,330));
        var bots=views[2].transform;
        botHeroButton=MenuStyle.Button(bots,"",new Vector2(0,133),new Vector2(380,44),()=>botHero=(botHero+1)%Mathf.Max(1,HeroRegistry.All.Length),false,25);
        botTeamButton=MenuStyle.Button(bots,"",new Vector2(0,79),new Vector2(380,42),()=>botTeam=(botTeam+1)%2,false,24);
        botDifficultyButton=MenuStyle.Button(bots,"",new Vector2(0,25),new Vector2(380,42),()=>BotBrain.Difficulty=(BotBrain.Difficulty+1)%BotBrain.DifficultyNames.Length,false,24);
        botAddButton=MenuStyle.Button(bots,"+ PŘIDAT BOTA",new Vector2(0,-35),new Vector2(380,44),()=>MatchManager.Instance?.ServerAddBot(botHero,botTeam),true,25);
        botRemoveButton=MenuStyle.Button(bots,"ODEBRAT BOTY",new Vector2(0,-92),new Vector2(380,42),()=>MatchManager.Instance?.ServerRemoveBots(),false,24);
        MenuStyle.Text(bots,"BotHint","Boty může spravovat pouze hostitel.",new Vector2(0,-145),new Vector2(380,30),19,MenuStyle.Muted,TextAlignmentOptions.Center);

        MenuStyle.Text(board,"HeroHeading","VYBER HRDINU",new Vector2(-240,-172),new Vector2(880,32),25,MenuStyle.Muted);
        var heroContent=ScrollContent(board,"HeroGrid",new Vector2(-240,-250),new Vector2(880,110));
        heroContent.sizeDelta=new Vector2(0,Mathf.CeilToInt(HeroRegistry.All.Length/5f)*58-10);
        for(int i=0;i<HeroRegistry.All.Length;i++)
        {
            int hero=i;
            var button=MenuStyle.Button(heroContent,HeroRegistry.All[i].heroName,new Vector2(-355+(i%5)*173,-25-(i/5)*58),new Vector2(162,46),()=>SelectHero(hero),false,29);
            var r=(RectTransform)button.transform; r.anchorMin=r.anchorMax=new Vector2(.5f,1);
            heroButtons.Add(button);
        }
        selectionHint=MenuStyle.Text(board,"SelectionHint","",new Vector2(-240,-325),new Vector2(880,30),20,MenuStyle.Muted);
        var mapCard=MenuStyle.Box(board,"MapCard",new Vector2(470,-250),new Vector2(420,180),MenuStyle.Field).transform;
        mapPreview=MenuStyle.Box(mapCard,"MapPreview",new Vector2(0,9),new Vector2(390,130),Color.white);
        mapPreview.type=Image.Type.Simple; mapPreview.preserveAspect=true;
        mapCaption=MenuStyle.Text(mapCard,"MapCaption","",new Vector2(0,-70),new Vector2(390,32),23,MenuStyle.Muted,TextAlignmentOptions.Center);
        MenuStyle.Rule(board,new Vector2(0,-353),1330);
        MenuStyle.Button(board,"OPUSTIT LOBBY",new Vector2(-530,-397),new Vector2(300,54),()=>onLeave?.Invoke(),false,29);
        startButton=MenuStyle.Button(board,"START ZÁPASU",new Vector2(470,-397),new Vector2(420,58),StartMatch,true,34);
        enterButton=MenuStyle.Button(board,"VSTOUPIT DO HRY",new Vector2(470,-397),new Vector2(420,58),EnterMatch,true,32);
        waitText=MenuStyle.Text(board,"Wait","",new Vector2(-60,-397),new Vector2(600,46),22,MenuStyle.Muted,TextAlignmentOptions.Center);
        SetView(0); Root.SetActive(false);
    }

    static RectTransform ScrollContent(Transform parent,string name,Vector2 position,Vector2 size)
    {
        var viewport=MenuStyle.Box(parent,name,position,size,Color.clear);
        viewport.raycastTarget=true; viewport.gameObject.AddComponent<RectMask2D>();
        var scroll=viewport.gameObject.AddComponent<ScrollRect>();
        var track=UiKit.MakeImage(viewport.transform,"ScrollTrack",MenuStyle.Panel,MenuStyle.Center,new Vector2(size.x/2-4,0),new Vector2(4,size.y));
        var bar=track.gameObject.AddComponent<Scrollbar>();
        var handle=UiKit.MakeImage(track.transform,"ScrollHandle",MenuStyle.Mint,MenuStyle.Center,Vector2.zero,Vector2.zero);
        UiKit.Stretch(handle.rectTransform); handle.raycastTarget=true;
        bar.handleRect=handle.rectTransform; bar.targetGraphic=handle; bar.direction=Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar=bar; scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
        var go=new GameObject("Content",typeof(RectTransform)); go.transform.SetParent(viewport.transform,false);
        var content=(RectTransform)go.transform; content.anchorMin=new Vector2(0,1); content.anchorMax=Vector2.one; content.pivot=new Vector2(.5f,1);
        content.sizeDelta=Vector2.zero;
        scroll.viewport=viewport.rectTransform; scroll.content=content; scroll.horizontal=false; scroll.vertical=true;
        scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=32;
        track.transform.SetAsLastSibling();
        return content;
    }
    void SetView(int index)
    {
        activeView=index;
        for(int i=0;i<views.Length;i++)
        {
            views[i].SetActive(i==index); UiKit.SetButtonColor(viewTabs[i],i==index?MenuStyle.Mint:MenuStyle.Panel);
            viewTabs[i].GetComponentInChildren<TextMeshProUGUI>().color=i==index?MenuStyle.Ink:MenuStyle.Cream;
        }
    }

    public void SetVisible(bool visible)
    {
        if (Root != null && Root.activeSelf != visible)
        {
            Root.SetActive(visible);
            nextRefresh = 0f;
        }
        if (!visible) LobbyShowcase.Hide();
    }

    // joining = zapas uz bezi a tenhle hrac si po pripojeni teprve vybira tym a hrdinu.
    public void Tick(PlayerHero localHero, bool joining = false)
    {
        MainMenuUI.RefreshMusicToggle(musicButton);
        if(Root!=null&&Root.activeSelf&&Keyboard.current!=null)
        {
            if(Keyboard.current.f2Key.wasPressedThisFrame)SetView(activeView==1?0:1);
            else if(Keyboard.current.escapeKey.wasPressedThisFrame&&activeView!=0)SetView(0);
        }
        if (Root == null || !Root.activeSelf || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.15f;

        var match = MatchManager.Instance;
        var network = NetworkManager.Singleton;
        bool isHost = network != null && network.IsServer;

        title.text = joining ? "Zápas už běží · vyber tým a hrdinu" : (match!=null?match.gameName.Value.ToString():"Lobby");
        roleHint.text=joining?"PŘIPOJUJEŠ SE DO ZÁPASU":isHost?"JSI HOSTITEL":"ČEKÁME NA HOSTITELE";
        viewTabs[2].interactable=isHost&&!joining;

        RefreshTeams(localHero);
        RefreshHeroes(localHero);
        var localTeamComponent = localHero != null ? localHero.GetComponent<PlayerTeam>() : null;
        LobbyShowcase.Show(localHero != null ? localHero.heroId.Value : PlayerHero.PreferredHero,
            localTeamComponent != null ? localTeamComponent.teamId.Value : -1);
        characterPreview.texture=LobbyShowcase.PreviewTexture;
        characterPreview.enabled=characterPreview.texture!=null;

        // Rezim hry voli host (ostatni ho jen vidi): team deathmatch na pocet zabiti, utok a obrana, nebo tlacenice
        // (ta ma vlastni mapu - pri prepnuti na ni se mapa vybere sama).
        bool captureMode = match != null && match.IsAttackMode;
        bool pushMode = match != null && match.IsPushMode;
        int map = match != null ? match.MapIndex : MatchManager.MapDomasov;
        bool attackAvailable = MatchManager.MapSupportsMode(map, MatchManager.ModeAttack);
        UiKit.SetButtonLabel(modeButton,"REŽIM: "+(pushMode?"TLAČENICE":captureMode?"DOBÝVÁNÍ":"DEATHMATCH")+"  ›");
        modeButton.interactable = isHost && !joining;

        bool mapReady = MatchManager.MapReady(map);
        bool modeReady = MatchManager.MapSupportsMode(map, match != null ? match.gameMode.Value : MatchManager.ModeDeathmatch);
        UiKit.SetButtonLabel(mapButton,"MAPA: "+MatchManager.MapNames[map]+(mapReady?"  ›":" · nedostupná"));
        mapPreview.sprite=MenuStyle.MapPreview(map);
        mapCaption.text=MatchManager.MapNames[map]+" · "+(pushMode?"Tlačenice":captureMode?"Dobývání":"Team deathmatch");
        mapButton.interactable = isHost && !joining;

        bool testOn = match != null && match.testCooldowns.Value;
        UiKit.SetButtonLabel(testButton, testOn ? "Testovací cooldowny 1 s: ZAP" : "Testovací cooldowny 1 s: VYP");
        UiKit.SetButtonColor(testButton, testOn ? new Color(.18f,.37f,.23f) : MenuStyle.Panel);
        testButton.interactable = isHost && !joining;

        int heroMode = match != null ? match.heroMode.Value : 0;
        UiKit.SetButtonLabel(heroModeButton, $"Hrdinové: {MatchManager.HeroModeNames[Mathf.Clamp(heroMode, 0, MatchManager.HeroModeNames.Length - 1)]}  ›");
        UiKit.SetButtonColor(heroModeButton, heroMode != 0 ? new Color(.18f,.37f,.23f) : MenuStyle.Panel);
        heroModeButton.interactable = isHost && !joining;

        int score = match != null ? match.scoreToWinSynced.Value : 10;
        scoreText.text = pushMode
            ? $"Zápas: {MatchManager.PushMatchSeconds / 60f:0} min · checkpoint +{MatchManager.PushCheckpointBonus:0} s"
            : captureMode
            ? $"Zabrání bodu: {match.captureSeconds.Value} s"
            : $"Zabití na výhru: {score}";
        scoreMinus.gameObject.SetActive(isHost && !joining && !pushMode);
        scorePlus.gameObject.SetActive(isHost && !joining && !pushMode);
        startButton.gameObject.SetActive(isHost && !joining);
        startButton.interactable = mapReady && modeReady;
        UiKit.SetButtonLabel(startButton, !mapReady ? "MAPA NENÍ HOTOVÁ" : !modeReady ? "REŽIM NENÍ DOSTUPNÝ" : "START ZÁPASU");
        enterButton.gameObject.SetActive(joining);
        waitText.gameObject.SetActive(true);
        waitText.text=joining?"Až budeš připravený, vstup do hry.":isHost?"Vyberte týmy a hrdiny. Pak můžete začít.":"Čekáme, až hostitel spustí zápas…";

        bool botsVisible = isHost && !joining;
        foreach (var button in new[] { botHeroButton, botTeamButton, botAddButton, botRemoveButton, botDifficultyButton })
            button.gameObject.SetActive(botsVisible);
        if (botsVisible)
        {
            var heroes = HeroRegistry.All;
            botHero = Mathf.Clamp(botHero, 0, Mathf.Max(0, heroes.Length - 1));
            string heroName = heroes.Length > 0 ? heroes[botHero].heroName : "?";
            UiKit.SetButtonLabel(botHeroButton, $"Hrdina bota: {heroName}  ›");
            UiKit.SetButtonLabel(botTeamButton, $"Přidat do týmu {botTeam+1}  ›");
            int count = BotBrain.All.Count;
            UiKit.SetButtonLabel(botRemoveButton, count > 0 ? $"ODEBRAT BOTY ({count})" : "ODEBRAT BOTY");
            botRemoveButton.interactable = count > 0;
            botAddButton.interactable = count < MatchManager.MaxBots;
            UiKit.SetButtonLabel(botDifficultyButton, $"Obtížnost: {BotBrain.DifficultyNames[Mathf.Clamp(BotBrain.Difficulty, 0, 2)]}  ›");
        }
    }

    void RefreshTeams(PlayerHero localHero)
    {
        var players = new List<PlayerHero>(UnityEngine.Object.FindObjectsByType<PlayerHero>());
        players.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));

        var builders = new StringBuilder[PlayerTeam.TeamCount];
        for (int i = 0; i < builders.Length; i++)
            builders[i] = new StringBuilder();

        var counts=new int[2];
        int localTeam = -1;
        foreach (var player in players)
        {
            if (!player.IsSpawned) continue;

            var team = player.GetComponent<PlayerTeam>();
            if (team == null) continue;

            int id = Mathf.Clamp(team.teamId.Value, 0, PlayerTeam.TeamCount - 1);
            counts[id]++;
            bool isLocal = player == localHero;
            if (isLocal) localTeam = id;

            string host = player.isBot.Value ? "<color=#AEC4AD>[BOT]</color> "
                : player.OwnerClientId == NetworkManager.ServerClientId ? "<color=#FFC08A>[HOST]</color> " : "";
            string name = isLocal ? $"<b>{SafeName(player.DisplayName)}</b>" : SafeName(player.DisplayName);
            string hero = player.Hero != null ? player.Hero.heroName : "…";
            builders[id].AppendLine($"{host}{name}   <size=75%><color=#AEC4AD>{hero}</color></size>");
        }

        for (int i = 0; i < PlayerTeam.TeamCount; i++)
        {
            teamLists[i].text = counts[i]==0?"<color=#AEC4AD>Zatím nikdo. Přidej se!</color>":builders[i].ToString();
            teamCounts[i].text=counts[i]+" hráčů";
            rosterContent[i].sizeDelta=new Vector2(0,Mathf.Max(76,counts[i]*29));
            teamButtons[i].interactable = localHero != null && localTeam != i;
            UiKit.SetButtonColor(teamButtons[i], localTeam == i ? new Color(.18f,.37f,.23f) : MenuStyle.Panel);
            UiKit.SetButtonLabel(teamButtons[i], localTeam == i ? $"TVŮJ TÝM" : $"PŘIPOJIT SE DO TÝMU {i+1}");
        }
    }

    static string SafeName(string value)=>(value??"").Replace("<","‹").Replace(">","›");
    void RefreshHeroes(PlayerHero localHero)
    {
        int selected=localHero!=null?localHero.heroId.Value:PlayerHero.PreferredHero;
        var team=localHero!=null?localHero.GetComponent<PlayerTeam>():null;
        bool manual=MatchManager.Instance==null||MatchManager.Instance.heroMode.Value==0;
        for(int i=0;i<heroButtons.Count;i++)
        {
            bool taken=team!=null&&i!=selected&&PlayerHero.TakenInTeam(i,team.teamId.Value,localHero);
            heroButtons[i].interactable=manual&&!taken;
            UiKit.SetButtonColor(heroButtons[i],i==selected?MenuStyle.Mint:MenuStyle.Field);
            heroButtons[i].GetComponentInChildren<TextMeshProUGUI>().color=i==selected?MenuStyle.Ink:MenuStyle.Cream;
        }
        selectionHint.text=manual?"Kliknutí vybere hrdinu. Schopnosti najdeš v pravém panelu (F2).":"Hrdinu přiděluje zvolený náhodný režim.";
        var hero=HeroRegistry.Get(selected);
        if(hero==null)return;
        selectedName.text=hero.heroName;
        selectedStats.text=hero.maxHealth.ToString("0")+" HP · "+(hero.weapon!=null?hero.weapon.weaponName:"");
        if(describedHero==selected)return;
        describedHero=selected;
        var entries=HeroInfoUI.Entries(hero);
        entries.Insert(0,new HeroInfoUI.Entry { key="INFO",name=hero.heroName,stats=HeroInfoUI.Passives(hero),description=hero.description });
        while(abilityRows.Count<entries.Count)
        {
            var row=new AbilityRow();
            row.root=MenuStyle.Box(abilityContent,"AbilityRow",Vector2.zero,new Vector2(380,120),MenuStyle.Panel);
            var r=row.root.rectTransform; r.anchorMin=r.anchorMax=new Vector2(.5f,1); r.pivot=new Vector2(.5f,1);
            row.key=MenuStyle.Text(r,"Key","",Vector2.zero,new Vector2(70,44),19,MenuStyle.Mint,TextAlignmentOptions.Center);
            row.key.richText=true; row.key.textWrappingMode=TextWrappingModes.Normal;
            row.name=MenuStyle.Text(r,"AbilityName","",Vector2.zero,new Vector2(280,34),25);
            row.stats=MenuStyle.Text(r,"Stats","",Vector2.zero,new Vector2(350,30),18,MenuStyle.Muted); row.stats.richText=true; row.stats.textWrappingMode=TextWrappingModes.Normal; row.stats.overflowMode=TextOverflowModes.Overflow;
            row.description=MenuStyle.Text(r,"Description","",Vector2.zero,new Vector2(350,50),21);
            row.description.textWrappingMode=TextWrappingModes.Normal; row.description.overflowMode=TextOverflowModes.Overflow;
            abilityRows.Add(row);
        }
        float top=0;
        for(int i=0;i<abilityRows.Count;i++)
        {
            var row=abilityRows[i]; row.root.gameObject.SetActive(i<entries.Count); if(i>=entries.Count)continue;
            var e=entries[i]; string description=string.IsNullOrWhiteSpace(e.description)?"":e.description;
            float descriptionHeight=Mathf.Max(26,row.description.GetPreferredValues(description,350,0).y);
            float statsHeight=Mathf.Max(24,row.stats.GetPreferredValues(e.stats,350,0).y);
            float height=58+statsHeight+descriptionHeight;
            row.root.rectTransform.sizeDelta=new Vector2(380,height); row.root.rectTransform.anchoredPosition=new Vector2(0,-top);
            row.key.text=e.key.Replace("LEVÉ TL.","LEVÉ").Replace("PRAVÉ TL.","PRAVÉ");
            row.key.fontSize=e.key.Contains("\n")?16:19; row.key.rectTransform.anchoredPosition=new Vector2(-148,height/2-22);
            row.name.text=e.name; row.name.rectTransform.anchoredPosition=new Vector2(38,height/2-22);
            row.stats.text=e.stats; row.stats.rectTransform.sizeDelta=new Vector2(350,statsHeight+2); row.stats.rectTransform.anchoredPosition=new Vector2(0,height/2-44-statsHeight/2);
            row.description.text=description; row.description.rectTransform.sizeDelta=new Vector2(350,descriptionHeight+2);
            row.description.rectTransform.anchoredPosition=new Vector2(0,height/2-52-statsHeight-descriptionHeight/2);
            top+=height+8;
        }
        abilityContent.sizeDelta=new Vector2(0,top); abilityContent.anchoredPosition=Vector2.zero;
    }

#if UNITY_EDITOR
    // Read-only layout fixture: no host, sockets, scene changes or player preference writes.
    internal void ReviewForEditor(int heroIndex,int page)
    {
        Root.SetActive(true);
        int old=PlayerHero.PreferredHero; PlayerHero.PreferredHero=heroIndex;
        RefreshHeroes(null); PlayerHero.PreferredHero=old;
        var hero=HeroRegistry.Get(heroIndex);
        title.text="Víkend na chatě"; roleHint.text="JSI HOSTITEL";
        teamLists[0].text="<b>Ty</b> · "+hero.heroName; teamLists[1].text="Kamarád · Viktor";
        teamCounts[0].text="1 hráč"; teamCounts[1].text="1 hráč";
        UiKit.SetButtonLabel(teamButtons[0],"TVŮJ TÝM"); UiKit.SetButtonLabel(teamButtons[1],"PŘIPOJIT SE DO TÝMU 2");
        characterPreview.texture=Resources.Load<Texture2D>("Portraits/"+hero.name); characterPreview.enabled=characterPreview.texture!=null;
        mapPreview.sprite=MenuStyle.MapPreview(0); mapCaption.text="Chaty · Team deathmatch";
        UiKit.SetButtonLabel(mapButton,"MAPA: Chaty  ›"); UiKit.SetButtonLabel(modeButton,"REŽIM: DEATHMATCH  ›");
        scoreText.text="Zabití na výhru: 30";
        UiKit.SetButtonLabel(heroModeButton,"Hrdinové: vlastní výběr  ›"); UiKit.SetButtonLabel(testButton,"Testovací cooldowny 1 s: VYP");
        UiKit.SetButtonLabel(botHeroButton,"Hrdina bota: "+hero.heroName+"  ›"); UiKit.SetButtonLabel(botTeamButton,"Přidat do týmu 2  ›");
        UiKit.SetButtonLabel(botDifficultyButton,"Obtížnost: normální  ›");
        enterButton.gameObject.SetActive(false); waitText.text="Vyberte týmy a hrdiny. Pak můžete začít.";
        SetView(page);
    }
#endif

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
        nextRefresh=0;
    }

    void ChangeScore(int delta)
    {
        var match = MatchManager.Instance;
        if (match == null || match.IsPushMode) return;

        if (match.IsAttackMode)
            match.SetCaptureSeconds(match.captureSeconds.Value + delta * 5);
        else
            match.SetScoreToWin(match.scoreToWinSynced.Value + delta);
    }

    void ToggleTestCooldowns()
    {
        var match = MatchManager.Instance;
        if (match != null)
            match.SetTestCooldowns(!match.testCooldowns.Value);
    }

    void NextMap()
    {
        var match = MatchManager.Instance;
        if (match != null)
            match.SetMap(MatchManager.NextMapForMode(match.MapIndex, match.gameMode.Value));
    }

    void ToggleMode()
    {
        var match = MatchManager.Instance;
        if (match == null) return;
        // poradi: deathmatch -> utok a obrana (kde jde) -> tlacenice -> deathmatch
        int mode = match.gameMode.Value;
        int next = mode == MatchManager.ModeDeathmatch && MatchManager.MapSupportsMode(match.MapIndex, MatchManager.ModeAttack) ? MatchManager.ModeAttack
            : mode == MatchManager.ModePush ? MatchManager.ModeDeathmatch : MatchManager.ModePush;
        match.SetGameMode(next);
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
