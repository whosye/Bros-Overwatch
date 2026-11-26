using System;
using System.Collections.Generic;
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
    readonly List<RosterRow>[] rosterRows = { new List<RosterRow>(), new List<RosterRow>() };
    readonly TextMeshProUGUI[] emptyTeams = new TextMeshProUGUI[2];
    readonly Button[] teamButtons = new Button[PlayerTeam.TeamCount];
    readonly List<Button> heroButtons = new List<Button>();
    TextMeshProUGUI scoreText, scoreLabel;
    GameObject choices;
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
    readonly TextMeshProUGUI[] teamHeadings = new TextMeshProUGUI[2];
    readonly TMP_InputField[] teamNameInputs = new TMP_InputField[2];
    readonly RectTransform[] rosterContent = new RectTransform[2];
    readonly GameObject[] views = new GameObject[3];
    readonly Button[] viewTabs = new Button[3];
    readonly List<AbilityRow> abilityRows = new List<AbilityRow>();
    TextMeshProUGUI selectedName, selectedStats, roleHint, selectionHint, mapCaption;
    RawImage characterPreview;
    Image mapPreview;
    RectTransform abilityContent;
    int activeView, describedHero = -1;
    class RosterRow
    {
        public Image root;
        public TextMeshProUGUI initial, player, hero, badge;
    }
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
        var board = MenuStyle.Box(Root.transform, "LobbyBoard", new Vector2(0,10), new Vector2(1820,1000), Color.clear).transform;
        MenuStyle.Text(board, "Title", "BROS OVERWATCH", new Vector2(-690,470), new Vector2(380,60), 36).fontStyle = FontStyles.Bold | FontStyles.Italic;
        MenuStyle.Text(board,"LobbyHeading","LOBBY",new Vector2(-390,470),new Vector2(160,48),32).font=MenuStyle.DisplayFont;
        UiKit.MakeImage(board,"HeaderDivider",MenuStyle.Muted,MenuStyle.Center,new Vector2(-490,470),new Vector2(1,34));
        title = MenuStyle.Text(board, "ServerName", "", new Vector2(-65,470), new Vector2(450,38), 24, MenuStyle.Muted);
        MenuStyle.Box(board,"RoleBadge",new Vector2(570,470),new Vector2(230,44),MenuStyle.Peach);
        roleHint = MenuStyle.Text(board, "Role", "", new Vector2(570,470), new Vector2(360,44), 23, MenuStyle.Ink, TextAlignmentOptions.Center);
        roleHint.font=MenuStyle.DisplayFont;
        MenuStyle.Rule(board, new Vector2(0,425), 1760);
        for (int team=0; team<2; team++)
        {
            int t=team;
            var card = FramedPanel(board, "Team"+team, new Vector2(-665,team==0?250:-30), new Vector2(420,250)).transform;
            // nazev tymu: kliknutim prejmenovat (host kterykoli tym, hrac svuj)
            teamHeadings[team] = MenuStyle.Text(card, "TeamHeading", "TÝM "+(team+1), new Vector2(-75,92), new Vector2(200,38), 30, MenuStyle.Cream);
            teamHeadings[team].raycastTarget = true;
            var headingButton = teamHeadings[team].gameObject.AddComponent<Button>();
            headingButton.targetGraphic = teamHeadings[team];
            headingButton.onClick.AddListener(()=>OpenTeamRename(t));
            teamNameInputs[team] = MenuStyle.Input(card, "TeamNameInput", "", new Vector2(-60,92), new Vector2(240,36), MatchManager.TeamNameLimit);
            teamNameInputs[team].onEndEdit.AddListener(value=>SubmitTeamRename(t, value));
            teamNameInputs[team].gameObject.SetActive(false);
            MenuStyle.Box(card,"TeamColor",new Vector2(-190,92),new Vector2(6,25),UiKit.TeamColor(team));
            teamCounts[team] = MenuStyle.Text(card, "Count", "", new Vector2(120,92), new Vector2(130,38), 21, MenuStyle.Muted, TextAlignmentOptions.Right);
            rosterContent[team] = ScrollContent(card, "Roster", new Vector2(0,10), new Vector2(380,112));
            emptyTeams[team] = MenuStyle.Text(card,"EmptyTeam","Zatím nikdo. Přidej se!",new Vector2(0,10),new Vector2(380,36),21,MenuStyle.Muted);
            MenuStyle.Rule(card,new Vector2(0,65),380);
            teamButtons[team] = MenuStyle.Button(card, "PŘIPOJIT SE", new Vector2(0,-91), new Vector2(380,42), ()=>SelectTeam(t), false, 24);
            OutlineButton(teamButtons[team]);
        }
        var showcase = MenuStyle.Box(board, "SelectedHero", new Vector2(0,100), new Vector2(640,680), Color.clear).transform;
        var preview = new GameObject("CharacterPreview", typeof(RectTransform), typeof(RawImage));
        preview.transform.SetParent(showcase,false);
        var rect = (RectTransform)preview.transform; rect.anchorMin=rect.anchorMax=rect.pivot=MenuStyle.Center;
        rect.anchoredPosition=Vector2.zero; rect.sizeDelta=new Vector2(640,680);
        characterPreview=preview.GetComponent<RawImage>(); characterPreview.raycastTarget=false;
        var identity=MenuStyle.Box(showcase,"HeroIdentity",new Vector2(0,-385),new Vector2(430,152),new Color(.035f,.15f,.09f,.94f)).transform;
        selectedName=MenuStyle.Text(identity,"Title","",new Vector2(0,46),new Vector2(410,64),46,null,TextAlignmentOptions.Center);
        selectedStats=MenuStyle.Text(identity,"HeroStats","",new Vector2(0,4),new Vector2(410,32),22,MenuStyle.Muted,TextAlignmentOptions.Center);
        MenuStyle.Button(identity,"SCHOPNOSTI  ›",new Vector2(0,-38),new Vector2(220,34),()=>SetView(1),false,24);

        var tools=FramedPanel(board,"LobbyTools",new Vector2(665,80),new Vector2(440,650)).transform;
        var tabs=FramedPanel(tools,"Tabs",new Vector2(0,290),new Vector2(404,46)).transform;
        string[] tabNames={"ZÁPAS","SCHOPNOSTI","BOTI"};
        for(int i=0;i<3;i++)
        {
            int index=i;
            viewTabs[i]=MenuStyle.Button(tabs,tabNames[i],new Vector2(-134+i*134,0),new Vector2(132,42),()=>SetView(index),false,23);
            views[i]=MenuStyle.Box(tools,"Page"+i,new Vector2(0,-25),new Vector2(400,550),Color.clear).gameObject;
        }
        var settings=views[0].transform;
        mapButton=MenuStyle.Button(settings,"",new Vector2(0,150),new Vector2(400,220),OpenMaps,false,32);
        var crop=MenuStyle.Box(mapButton.transform,"MapCrop",Vector2.zero,new Vector2(400,220),Color.white);
        crop.gameObject.AddComponent<Mask>().showMaskGraphic=false;
        mapPreview=UiKit.MakeImage(crop.transform,"MapPreview",Color.white,MenuStyle.Center,Vector2.zero,new Vector2(400,220));
        var mapShade=MenuStyle.Box(mapButton.transform,"MapTitleShade",new Vector2(0,-77),new Vector2(400,66),new Color(.02f,.08f,.04f,.92f));
        var mapName=mapButton.GetComponentInChildren<TextMeshProUGUI>();
        mapName.alignment=TextAlignmentOptions.BottomLeft; mapName.margin=new Vector4(16,0,48,10);
        mapName.fontStyle=FontStyles.Bold|FontStyles.Italic; mapName.transform.SetAsLastSibling();
        mapCaption=MenuStyle.Text(mapButton.transform,"MapCaption","VYBRANÁ MAPA",new Vector2(-70,87),new Vector2(230,30),18,MenuStyle.Cream);
        MenuStyle.Text(mapButton.transform,"MapArrow","›",new Vector2(174,-78),new Vector2(32,40),30,MenuStyle.Cream,TextAlignmentOptions.Center).rectTransform.localRotation=Quaternion.Euler(0,0,-90);
        FieldLabel(settings,"Režim",-18);
        modeButton=ValueButton(settings,"ModeValue",-18,OpenModes);
        FieldLabel(settings,"Hrdinové",-80);
        heroModeButton=ValueButton(settings,"HeroModeValue",-80,OpenHeroModes);
        scoreLabel=FieldLabel(settings,"Zabití\nna výhru",-142);
        var scoreField=FramedPanel(settings,"ScoreField",new Vector2(72,-142),new Vector2(256,44));
        scoreText=MenuStyle.Text(scoreField.transform,"WinRule","",Vector2.zero,new Vector2(146,40),23,null,TextAlignmentOptions.Center);
        scoreMinus=MenuStyle.Button(scoreField.transform,"−",new Vector2(-104,0),new Vector2(42,40),()=>ChangeScore(-1),false,27);
        scorePlus=MenuStyle.Button(scoreField.transform,"+",new Vector2(104,0),new Vector2(42,40),()=>ChangeScore(1),false,27);
        FieldLabel(settings,"Cooldowny\n1 sekunda",-204);
        testButton=ValueButton(settings,"TestValue",-204,ToggleTestCooldowns,false);
        MenuStyle.Text(settings,"SettingsHint","Nastavení mění hostitel.",new Vector2(0,-256),new Vector2(400,32),19,MenuStyle.Muted,TextAlignmentOptions.Center);
        abilityContent=ScrollContent(views[1].transform,"Abilities",Vector2.zero,new Vector2(380,550));
        var bots=views[2].transform;
        MenuStyle.Text(bots,"BotHeading","PŘIDEJ PARŤÁKA",new Vector2(0,224),new Vector2(400,44),32).font=MenuStyle.DisplayFont;
        MenuStyle.Text(bots,"BotIntro","Nastav bota a pošli ho do týmu.",new Vector2(0,180),new Vector2(400,32),20,MenuStyle.Muted);
        FieldLabel(bots,"Hrdina",110);
        botHeroButton=ValueButton(bots,"BotHeroValue",110,()=>OpenChoices(botHeroButton,Array.ConvertAll(HeroRegistry.All,h=>h.heroName),botHero,index=>botHero=index));
        FieldLabel(bots,"Tým",48);
        botTeamButton=ValueButton(bots,"BotTeamValue",48,()=>OpenChoices(botTeamButton,new[]{MatchManager.TeamName(0),MatchManager.TeamName(1)},botTeam,index=>botTeam=index));
        FieldLabel(bots,"Obtížnost",-14);
        botDifficultyButton=ValueButton(bots,"BotDifficultyValue",-14,()=>OpenChoices(botDifficultyButton,BotBrain.DifficultyNames,BotBrain.Difficulty,index=>BotBrain.Difficulty=index));
        botAddButton=MenuStyle.Button(bots,"+ PŘIDAT BOTA",new Vector2(0,-95),new Vector2(400,46),()=>MatchManager.Instance?.ServerAddBot(botHero,botTeam),true,25);
        botRemoveButton=MenuStyle.Button(bots,"ODEBRAT BOTY",new Vector2(0,-157),new Vector2(400,42),()=>MatchManager.Instance?.ServerRemoveBots(),false,24);
        OutlineButton(botRemoveButton);
        MenuStyle.Text(bots,"BotHint","Boty spravuje pouze hostitel.",new Vector2(0,-232),new Vector2(400,32),19,MenuStyle.Muted,TextAlignmentOptions.Center);

        // A single row for the current roster; additional heroes wrap and scroll within the same strip.
        const int columns=9;
        var heroContent=ScrollContent(board,"HeroGrid",new Vector2(0,-400),new Vector2(1760,62));
        heroContent.sizeDelta=new Vector2(0,Mathf.CeilToInt(HeroRegistry.All.Length/(float)columns)*62);
        for(int i=0;i<HeroRegistry.All.Length;i++)
        {
            int hero=i;
            var button=MenuStyle.Button(heroContent,HeroRegistry.All[i].heroName,new Vector2(-780+(i%columns)*195,-30-(i/columns)*62),new Vector2(180,48),()=>SelectHero(hero),false,27);
            var r=(RectTransform)button.transform; r.anchorMin=r.anchorMax=new Vector2(.5f,1);
            OutlineButton(button);
            heroButtons.Add(button);
        }
        selectionHint=MenuStyle.Text(board,"SelectionHint","",new Vector2(0,-438),new Vector2(1100,28),19,MenuStyle.Muted,TextAlignmentOptions.Center);
        MenuStyle.Rule(board,new Vector2(0,-457),1760);
        OutlineButton(MenuStyle.Button(board,"‹   OPUSTIT LOBBY",new Vector2(-715,-496),new Vector2(330,54),()=>onLeave?.Invoke(),false,29));
        startButton=MenuStyle.Button(board,"START ZÁPASU",new Vector2(665,-496),new Vector2(420,58),StartMatch,true,34);
        enterButton=MenuStyle.Button(board,"VSTOUPIT DO HRY",new Vector2(665,-496),new Vector2(420,58),EnterMatch,true,32);
        waitText=MenuStyle.Text(board,"Wait","",new Vector2(-40,-496),new Vector2(920,46),22,MenuStyle.Muted,TextAlignmentOptions.Center);
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

    static Image FramedPanel(Transform parent,string name,Vector2 position,Vector2 size)
    {
        var border=MenuStyle.Box(parent,name,position,size,new Color(.63f,.73f,.60f,.6f));
        return MenuStyle.Box(border.transform,name+"Surface",Vector2.zero,size-new Vector2(3,3),new Color(.025f,.105f,.065f,.98f));
    }

    static void OutlineButton(Button button)
    {
        UiKit.SetButtonColor(button,MenuStyle.Ink);
        var line=button.gameObject.AddComponent<Outline>();
        line.effectColor=new Color(.73f,.83f,.68f,.7f); line.effectDistance=new Vector2(1,-1);
    }

    static TextMeshProUGUI FieldLabel(Transform parent,string label,float y)
    {
        var text=MenuStyle.Text(parent,"FieldLabel",label,new Vector2(-132,y),new Vector2(132,48),20);
        text.textWrappingMode=TextWrappingModes.Normal;
        return text;
    }

    static Button ValueButton(Transform parent,string name,float y,UnityEngine.Events.UnityAction action,bool arrow=true)
    {
        var button=MenuStyle.Button(parent,"",new Vector2(72,y),new Vector2(256,44),action,false,23);
        button.name=name; OutlineButton(button);
        var value=button.GetComponentInChildren<TextMeshProUGUI>();
        value.alignment=TextAlignmentOptions.Left; value.margin=new Vector4(12,0,arrow?36:10,0);
        if(arrow)MenuStyle.Text(button.transform,"DropdownArrow","›",new Vector2(109,0),new Vector2(30,38),26,null,TextAlignmentOptions.Center).rectTransform.localRotation=Quaternion.Euler(0,0,-90);
        return button;
    }

    void SetMapPreview(int map)
    {
        UiKit.SetButtonLabel(mapButton,MatchManager.MapLabel(map));
        mapPreview.sprite=MenuStyle.MapPreview(map);
        // Cover the rounded viewport without stretching or leaving empty side gutters.
        if(mapPreview.sprite==null)return;
        var size=mapPreview.sprite.rect.size;
        float scale=Mathf.Max(400/size.x,220/size.y);
        mapPreview.rectTransform.sizeDelta=size*scale;
    }

    static string HeroModeLabel(int mode)
        =>mode==1?"Náhodní na zápas":mode==2?"Nový po smrti":"Vlastní výběr";

    void CloseChoices()
    {
        if(choices==null)return;
        choices.SetActive(false);
        if(Application.isPlaying)UnityEngine.Object.Destroy(choices);
        else UnityEngine.Object.DestroyImmediate(choices);
        choices=null;
    }

    void OpenChoices(Button anchor,string[] labels,int selected,Action<int> onSelect,Func<int,bool> allowed=null)
    {
        CloseChoices();
        var dismiss=UiKit.MakeImage(Root.transform,"DismissChoices",Color.clear,MenuStyle.Center,Vector2.zero,Vector2.zero);
        UiKit.Stretch(dismiss.rectTransform); dismiss.raycastTarget=true;
        choices=dismiss.gameObject;
        var outside=choices.AddComponent<Button>(); outside.targetGraphic=dismiss; outside.onClick.AddListener(CloseChoices);
        var anchorRect=(RectTransform)anchor.transform;
        var position=(Vector2)Root.transform.InverseTransformPoint(anchor.transform.position);
        float width=Mathf.Max(256,anchorRect.rect.width),height=labels.Length*48+12;
        position.y-=anchorRect.rect.height/2+height/2+5;
        var rootRect=(RectTransform)Root.transform;
        position.y=Mathf.Max(rootRect.rect.yMin+height/2+20,position.y);
        var panel=FramedPanel(dismiss.transform,"Choices",position,new Vector2(width,height)).transform;
        for(int i=0;i<labels.Length;i++)
        {
            int index=i;
            var option=MenuStyle.Button(panel,labels[i],new Vector2(0,height/2-30-i*48),new Vector2(width-12,44),()=>
            { CloseChoices(); onSelect(index); nextRefresh=0; },i==selected,23);
            option.interactable=allowed==null||allowed(i);
        }
    }

    void OpenMaps()
    {
        var match=MatchManager.Instance;
        if(match==null)return;
        // (kopie Domasova pro Tlacenici v nabidce neni - v rezimu Tlacenice ji vybere polozka Domasov)
        var maps=new System.Collections.Generic.List<int>();
        for(int i=0;i<MatchManager.MapNames.Length;i++)if(MatchManager.MapListed(i))maps.Add(i);
        var labels=maps.ConvertAll(i=>MatchManager.MapNames[i]).ToArray();
        int mode=match.gameMode.Value;
        OpenChoices(mapButton,labels,maps.IndexOf(MatchManager.ListedMap(match.MapIndex)),index=>match.SetMap(MatchManager.MapFor(maps[index],match.gameMode.Value)),
            index=>MatchManager.MapPlayable(maps[index],mode));
    }

    void OpenModes()
    {
        var match=MatchManager.Instance;
        if(match==null)return;
        var modes=new[]{MatchManager.ModeDeathmatch,MatchManager.ModeAttack,MatchManager.ModePush};
        OpenChoices(modeButton,new[]{"Deathmatch","Dobývání","Tlačenice"},Array.IndexOf(modes,match.gameMode.Value),
            index=>match.SetGameMode(modes[index]),index=>modes[index]!=MatchManager.ModeAttack||match.IsPushMode||MatchManager.MapSupportsMode(match.MapIndex,MatchManager.ModeAttack));
    }

    void OpenHeroModes()
    {
        var match=MatchManager.Instance;
        if(match==null)return;
        OpenChoices(heroModeButton,new[]{HeroModeLabel(0),HeroModeLabel(1),HeroModeLabel(2)},match.heroMode.Value,index=>match.SetHeroMode(index));
    }
    void SetView(int index)
    {
        CloseChoices();
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
        if (!visible) { CloseChoices(); LobbyShowcase.Hide(); }
    }

    // joining = zapas uz bezi a tenhle hrac si po pripojeni teprve vybira tym a hrdinu.
    public void Tick(PlayerHero localHero, bool joining = false)
    {
        MainMenuUI.RefreshMusicToggle(musicButton);
        if(Root!=null&&Root.activeSelf&&Keyboard.current!=null&&!TextChatUI.BlocksGameplayInput)
        {
            if(Keyboard.current.escapeKey.wasPressedThisFrame&&choices!=null)CloseChoices();
            else if(Keyboard.current.f2Key.wasPressedThisFrame)SetView(activeView==1?0:1);
            else if(Keyboard.current.escapeKey.wasPressedThisFrame&&activeView!=0)SetView(0);
        }
        if (Root == null || !Root.activeSelf || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.15f;

        var match = MatchManager.Instance;
        var network = NetworkManager.Singleton;
        bool isHost = network != null && network.IsServer;

        title.text = joining ? "Zápas už běží · vyber tým a hrdinu" : (match!=null?match.gameName.Value.ToString():"Lobby");
        roleHint.text=joining?"PŘIPOJUJEŠ SE DO ZÁPASU":isHost?"JSI HOSTITEL":"ČEKÁME NA HOSTITELE";
        ((RectTransform)roleHint.transform.parent.Find("RoleBadge")).sizeDelta=new Vector2(Mathf.Clamp(roleHint.preferredWidth+34,230,380),44);
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
        UiKit.SetButtonLabel(modeButton,pushMode?"Tlačenice":captureMode?"Dobývání":"Deathmatch");
        modeButton.interactable = isHost && !joining;

        bool mapReady = MatchManager.MapReady(map);
        bool modeReady = MatchManager.MapSupportsMode(map, match != null ? match.gameMode.Value : MatchManager.ModeDeathmatch);
        SetMapPreview(map);
        mapCaption.text=!mapReady?"MAPA NENÍ HOTOVÁ":pushMode?$"CHECKPOINT +{MatchManager.PushCheckpointBonus:0} s":"VYBRANÁ MAPA";
        mapButton.interactable = isHost && !joining;

        bool testOn = match != null && match.testCooldowns.Value;
        UiKit.SetButtonLabel(testButton, testOn ? "ZAPNUTO  ●" : "VYPNUTO  ○");
        UiKit.SetButtonColor(testButton, testOn ? new Color(.18f,.37f,.23f) : MenuStyle.Panel);
        testButton.interactable = isHost && !joining;

        int heroMode = match != null ? match.heroMode.Value : 0;
        UiKit.SetButtonLabel(heroModeButton, HeroModeLabel(heroMode));
        UiKit.SetButtonColor(heroModeButton, heroMode != 0 ? new Color(.18f,.37f,.23f) : MenuStyle.Panel);
        heroModeButton.interactable = isHost && !joining;

        int score = match != null ? match.scoreToWinSynced.Value : 10;
        scoreLabel.text=pushMode?"Délka\nzápasu":captureMode?"Čas na bod":"Zabití\nna výhru";
        scoreText.text=pushMode?$"{MatchManager.PushMatchSeconds / 60f:0} min":captureMode?$"{match.captureSeconds.Value} s":score.ToString();
        scoreText.rectTransform.sizeDelta=new Vector2(pushMode?246:146,40);
        scoreMinus.gameObject.SetActive(isHost && !joining && !pushMode);
        scorePlus.gameObject.SetActive(isHost && !joining && !pushMode);
        startButton.gameObject.SetActive(isHost && !joining);
        startButton.interactable = mapReady && modeReady;
        UiKit.SetButtonLabel(startButton, !mapReady ? "MAPA NENÍ HOTOVÁ" : !modeReady ? "REŽIM NENÍ DOSTUPNÝ" : "START ZÁPASU");
        enterButton.gameObject.SetActive(joining);
        waitText.gameObject.SetActive(true);
        waitText.text=joining?"Až budeš připravený, vstup do hry.":isHost?"Vyberte týmy a hrdiny. Pak můžete začít. Název týmu změníš kliknutím na něj.":"Čekáme, až hostitel spustí zápas… Název svého týmu změníš kliknutím na něj.";

        bool botsVisible = isHost && !joining;
        foreach (var button in new[] { botHeroButton, botTeamButton, botAddButton, botRemoveButton, botDifficultyButton })
            button.gameObject.SetActive(botsVisible);
        if (botsVisible)
        {
            var heroes = HeroRegistry.All;
            botHero = Mathf.Clamp(botHero, 0, Mathf.Max(0, heroes.Length - 1));
            string heroName = heroes.Length > 0 ? heroes[botHero].heroName : "?";
            UiKit.SetButtonLabel(botHeroButton,heroName);
            UiKit.SetButtonLabel(botTeamButton,MatchManager.TeamName(botTeam));
            int count = BotBrain.All.Count;
            UiKit.SetButtonLabel(botRemoveButton, count > 0 ? $"ODEBRAT BOTY ({count})" : "ODEBRAT BOTY");
            botRemoveButton.interactable = count > 0;
            botAddButton.interactable = count < MatchManager.MaxBots;
            UiKit.SetButtonLabel(botDifficultyButton,BotBrain.DifficultyNames[Mathf.Clamp(BotBrain.Difficulty,0,BotBrain.DifficultyNames.Length-1)]);
        }
    }

    void RefreshTeams(PlayerHero localHero)
    {
        var players = new List<PlayerHero>(UnityEngine.Object.FindObjectsByType<PlayerHero>());
        players.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));

        var counts=new int[2];
        int localTeam = -1;
        foreach (var player in players)
        {
            if (!player.IsSpawned) continue;
            var team = player.GetComponent<PlayerTeam>();
            if (team == null) continue;
            int id = Mathf.Clamp(team.teamId.Value, 0, PlayerTeam.TeamCount - 1);
            bool isLocal = player == localHero;
            if (isLocal) localTeam = id;
            string badge=player.isBot.Value?"BOT":player.OwnerClientId==NetworkManager.ServerClientId?"HOST":"";
            SetRosterEntry(id,counts[id]++,player.DisplayName,player.Hero!=null?player.Hero.heroName:"…",badge,isLocal);
        }
        for (int i = 0; i < PlayerTeam.TeamCount; i++)
        {
            if (!teamNameInputs[i].gameObject.activeSelf) teamHeadings[i].text = MatchManager.TeamName(i);
            FinishRoster(i,counts[i]);
            teamButtons[i].interactable = localHero != null && localTeam != i;
            UiKit.SetButtonColor(teamButtons[i], localTeam == i ? new Color(.10f,.24f,.14f) : MenuStyle.Ink);
            UiKit.SetButtonLabel(teamButtons[i], localTeam == i ? "TVŮJ TÝM" : "›   PŘEJÍT DO TÝMU");
        }
    }

    void OpenTeamRename(int team)
    {
        var match=MatchManager.Instance;
        if(match==null||!MatchManager.CanRenameTeam(team))return;
        var input=teamNameInputs[team];
        teamHeadings[team].gameObject.SetActive(false);
        input.gameObject.SetActive(true);
        input.text=match.CustomTeamName(team);
        input.Select(); input.ActivateInputField();
    }

    void SubmitTeamRename(int team,string value)
    {
        teamNameInputs[team].gameObject.SetActive(false);
        teamHeadings[team].gameObject.SetActive(true);
        var match=MatchManager.Instance;
        if(match==null||!match.IsSpawned)return;
        string clean=MatchManager.CleanTeamName(value);
        if(clean!=match.CustomTeamName(team))match.RenameTeamRpc(team,new Unity.Collections.FixedString64Bytes(clean));
        teamHeadings[team].text=string.IsNullOrEmpty(clean)?"TÝM "+(team+1):clean.ToUpperInvariant();
    }

    void SetRosterEntry(int team,int index,string player,string hero,string badge,bool local)
    {
        var rows=rosterRows[team];
        while(rows.Count<=index)
        {
            var row=new RosterRow();
            row.root=MenuStyle.Box(rosterContent[team],"PlayerRow",Vector2.zero,new Vector2(380,44),MenuStyle.Field);
            var rect=row.root.rectTransform; rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);
            row.initial=MenuStyle.Text(rect,"Initial","",new Vector2(-167,0),new Vector2(28,32),22,MenuStyle.Mint,TextAlignmentOptions.Center);
            row.player=MenuStyle.Text(rect,"PlayerName","",new Vector2(-78,0),new Vector2(132,34),22);
            row.hero=MenuStyle.Text(rect,"PlayerHero","",new Vector2(58,0),new Vector2(120,32),20,MenuStyle.Muted);
            var tag=MenuStyle.Box(rect,"PlayerTag",new Vector2(152,0),new Vector2(58,26),MenuStyle.Peach);
            row.badge=MenuStyle.Text(tag.transform,"Badge","",Vector2.zero,new Vector2(58,26),18,MenuStyle.Ink,TextAlignmentOptions.Center);
            rows.Add(row);
        }
        var entry=rows[index]; entry.root.gameObject.SetActive(true);
        entry.root.rectTransform.anchoredPosition=new Vector2(0,-22-index*50);
        entry.root.color=local?new Color(.10f,.24f,.14f):MenuStyle.Field;
        entry.initial.text=string.IsNullOrEmpty(player)?"?":player.Substring(0,1).ToUpperInvariant();
        entry.player.text=SafeName(player); entry.player.fontStyle=local?FontStyles.Bold:FontStyles.Normal;
        entry.hero.text=hero; entry.badge.text=badge;
        entry.badge.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(badge));
    }

    void FinishRoster(int team,int count)
    {
        for(int i=count;i<rosterRows[team].Count;i++)rosterRows[team][i].root.gameObject.SetActive(false);
        emptyTeams[team].gameObject.SetActive(count==0);
        teamCounts[team].text=count+(count==1?" hráč":count>=2&&count<=4?" hráči":" hráčů");
        rosterContent[team].sizeDelta=new Vector2(0,Mathf.Max(112,count*50));
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
        SetRosterEntry(0,0,"miro",hero.heroName,"HOST",true);
        SetRosterEntry(0,1,"Viki","Honza","",false);
        SetRosterEntry(1,0,"David","David","",false);
        FinishRoster(0,2); FinishRoster(1,1);
        UiKit.SetButtonLabel(teamButtons[0],"TVŮJ TÝM"); UiKit.SetButtonLabel(teamButtons[1],"›   PŘEJÍT DO TÝMU");
        characterPreview.texture=LobbyShowcase.PreviewTexture; characterPreview.enabled=characterPreview.texture!=null;
        SetMapPreview(0); mapCaption.text="VYBRANÁ MAPA";
        UiKit.SetButtonLabel(modeButton,"Deathmatch");
        scoreText.text="30"; scoreLabel.text="Zabití\nna výhru";
        UiKit.SetButtonLabel(heroModeButton,HeroModeLabel(0)); UiKit.SetButtonLabel(testButton,"VYPNUTO  ○");
        UiKit.SetButtonLabel(botHeroButton,hero.heroName); UiKit.SetButtonLabel(botTeamButton,MatchManager.TeamName(1));
        UiKit.SetButtonLabel(botDifficultyButton,"Normální");
        enterButton.gameObject.SetActive(false); waitText.text="Vyberte týmy a hrdiny. Pak můžete začít.";
        SetView(page);
    }

    internal void ReviewChoicesForEditor(Action capture)
    {
        int picked=-1;
        OpenChoices(modeButton,new[]{"Deathmatch","Dobývání","Tlačenice"},0,index=>picked=index);
        Canvas.ForceUpdateCanvases();
        capture();
        var options=choices.transform.Find("Choices/ChoicesSurface").GetComponentsInChildren<Button>();
        options[2].onClick.Invoke();
        if(picked!=2||choices!=null)throw new InvalidOperationException("Lobby choice did not select and close.");
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
