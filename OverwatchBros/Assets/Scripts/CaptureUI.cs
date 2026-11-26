using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD rezimu utok a obrana: nahore kolo, kdo utoci, bod, cas (priprava / utok / prodlouzeni / vymena stran),
// postup zabirani s ryskami po tretinach a ve druhem kole cil, ktery je potreba prekonat.
// Ve svete znacka bodu se vzdalenosti. Velka oznameni pres stred obrazovky (bod zabran, utok zacal...).
public class CaptureUI
{
    const float BarWidth = 420f;

    static CaptureUI instance;

    GameObject panel;
    TextMeshProUGUI header, timeText, status, target;
    Image barFill;

    // 2. kolo: cil na pruhu (souperuv postup) a "zavod" bodu soupeř vs. utocnici
    static readonly Color GoalColor = new Color(1f, 0.82f, 0.15f);
    const float PipWidth = 54f, PipGap = 6f, LabelWidth = 70f, RaceWidth = 420f;
    TextMeshProUGUI raceTitle;
    Image ghostFill, goalLine;
    TextMeshProUGUI goalText;
    GameObject raceGroup;
    readonly TextMeshProUGUI[] raceLabel = new TextMeshProUGUI[2];
    readonly Image[][] pips = new Image[2][];
    readonly Image[][] pipFills = new Image[2][];

    // Velke oznameni pres stred (ve stylu OW): tmavy pas, barevne linky se roztahnou ze stredu, titulek "vyskoci".
    GameObject bannerGroup;
    Image bannerBand, lineTop, lineBottom;
    TextMeshProUGUI banner, bannerSub;
    float bannerUntil, bannerStart, bannerLength;
    int lastPhase = -1;

    // zvuky: tikani pri zabirani, znelka pri odemceni
    float nextTick;
    int lastState = -1;
    int lastPoint = -1;

    // Cekani na odemceni bodu (priprava, mezi body): vyrazny panel s odpoctem a pruhem, at hrac vi, proc stoji.
    GameObject waitGroup;
    TextMeshProUGUI waitTitle, waitCount, waitSub;
    Image waitFill;
    int waitTotal = 1, waitPoint = -1, waitPhase = -1;
    const float WaitBarWidth = 600f;

    RectTransform canvasRect;
    RectTransform marker;
    Image markerIcon;
    TextMeshProUGUI markerText;

    public static void Announce(string text, Color color, float seconds = 3.2f) => Show(text, "", color, seconds, false);

    // Velke oznameni zapasu (bod zabran, utok, prodlouzeni, konec kola) s podtitulkem.
    public static void Headline(string title, string subtitle, Color color, float seconds = 3.5f) => Show(title, subtitle, color, seconds, true);

    static void Show(string title, string subtitle, Color color, float seconds, bool big)
    {
        if (instance == null || instance.banner == null) return;
        instance.banner.text = title;
        instance.banner.color = big ? Color.white : color;
        instance.banner.fontSize = big ? 62f : 40f;
        instance.bannerSub.text = subtitle;
        instance.bannerSub.color = color;
        instance.lineTop.color = color;
        instance.lineBottom.color = color;
        instance.bannerBand.rectTransform.sizeDelta = new Vector2(2600f, big ? 120f : 78f);
        instance.bannerStart = Time.unscaledTime;
        instance.bannerLength = seconds;
        instance.bannerUntil = Time.unscaledTime + seconds;
    }

    public void Build(Transform canvas)
    {
        instance = this;
        CaptureTransitionView.Build(canvas);
        canvasRect = (RectTransform)canvas;
        var top = new Vector2(0.5f, 1f);
        var center = new Vector2(0.5f, 0.5f);

        // (cas a kolo ukazuje horni lista TopBarUI; tady jen pruh zabirani, stav a cil druheho kola - pod listou)
        var panelImage = UiKit.MakeImage(canvas, "AttackPanel", new Color(0f, 0f, 0f, 0f), top, new Vector2(0f, -160f), new Vector2(900f, 150f));
        panel = panelImage.gameObject;

        header = UiKit.MakeText(panel.transform, "Header", "", 22, TextAlignmentOptions.Center, top, new Vector2(0f, 0f), new Vector2(900f, 28f));
        timeText = UiKit.MakeText(panel.transform, "Time", "", 40, TextAlignmentOptions.Center, top, new Vector2(0f, -28f), new Vector2(900f, 46f));

        var background = UiKit.MakeImage(panel.transform, "Bar", new Color(0.05f, 0.07f, 0.10f, 0.75f), top,
            new Vector2(0f, -82f), new Vector2(BarWidth + 6f, 20f));
        // 2. kolo: "duch" souperova vysledku na rozhodujicim bode (pod skutecnou vyplni) + zlata ryska cile
        ghostFill = UiKit.MakeImage(background.transform, "SouperFill", Color.white, new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(0f, 14f));
        ghostFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        barFill = UiKit.MakeImage(background.transform, "Fill", Color.white, new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(0f, 14f));
        barFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        goalLine = UiKit.MakeImage(background.transform, "Cil", GoalColor, new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(4f, 30f));
        goalLine.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        goalText = UiKit.MakeText(goalLine.transform, "CilText", "", 17, TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0f, 12f), new Vector2(160f, 22f), GoalColor);
        goalText.fontStyle = FontStyles.Bold;
        ghostFill.gameObject.SetActive(false);
        goalLine.gameObject.SetActive(false);
        foreach (float t in new[] { 1f / 3f, 2f / 3f })
        {
            var tick = UiKit.MakeImage(background.transform, "Tick", new Color(1f, 1f, 1f, 0.75f), new Vector2(0f, 0.5f),
                new Vector2(3f + BarWidth * t, 0f), new Vector2(3f, 20f));
            tick.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        }

        status = UiKit.MakeText(panel.transform, "Status", "", 22, TextAlignmentOptions.Center, top, new Vector2(0f, -100f), new Vector2(900f, 28f));
        target = UiKit.MakeText(panel.transform, "Target", "", 19, TextAlignmentOptions.Center, top, new Vector2(0f, -126f), new Vector2(900f, 24f),
            new Color(0.85f, 0.87f, 0.92f));

        // 2. kolo: karticka pod stavem - nahore cil, pod nim dva radky bodu (soupeř v 1. kole / utocnici ted)
        // (levy horni roh obrazovky - pod pruhem zabirani prekazela)
        var card = UiKit.MakeImage(canvas, "Zavod", new Color(0.03f, 0.05f, 0.09f, 0.6f), new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(RaceWidth, 80f));
        raceGroup = card.gameObject;
        raceTitle = UiKit.MakeText(card.transform, "Cil", "", 17, TextAlignmentOptions.Center, top, new Vector2(0f, -5f), new Vector2(RaceWidth - 16f, 22f), GoalColor);
        raceTitle.fontStyle = FontStyles.Bold;
        for (int row = 0; row < 2; row++)
        {
            float y = row == 0 ? -42f : -64f;
            raceLabel[row] = UiKit.MakeText(card.transform, "Popis", "", 15, TextAlignmentOptions.Right, top,
                new Vector2(0f, y + 10f), new Vector2(LabelWidth, 20f));
            raceLabel[row].fontStyle = FontStyles.Bold;
            int count = MatchManager.PointNames.Length;
            pips[row] = new Image[count];
            pipFills[row] = new Image[count];
            for (int i = 0; i < count; i++)
            {
                var back = UiKit.MakeImage(card.transform, "Bod", new Color(1f, 1f, 1f, 0.08f), top, new Vector2(0f, y + 8f), new Vector2(PipWidth, 16f));
                var fill = UiKit.MakeImage(back.transform, "Fill", Color.white, new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(0f, 12f));
                fill.rectTransform.pivot = new Vector2(0f, 0.5f);
                UiKit.MakeText(back.transform, "Pismeno", MatchManager.PointNames[i], 13, TextAlignmentOptions.Center,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PipWidth, 16f)).fontStyle = FontStyles.Bold;
                pips[row][i] = back;
                pipFills[row][i] = fill;
            }
        }
        raceGroup.SetActive(false);

        header.gameObject.SetActive(false);
        timeText.gameObject.SetActive(false);

        bannerBand = UiKit.MakeImage(canvas, "AttackBanner", new Color(0f, 0f, 0f, 0.5f), center, new Vector2(0f, 170f), new Vector2(2600f, 120f));
        bannerGroup = bannerBand.gameObject;
        lineTop = UiKit.MakeImage(bannerBand.transform, "LinkaNahore", Color.white, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 4f));
        lineBottom = UiKit.MakeImage(bannerBand.transform, "LinkaDole", Color.white, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 4f));
        banner = UiKit.MakeText(bannerBand.transform, "Titulek", "", 62, TextAlignmentOptions.Center, center, new Vector2(0f, 10f), new Vector2(1600f, 76f));
        banner.fontStyle = FontStyles.Bold | FontStyles.Italic;
        banner.outlineWidth = 0.15f;
        banner.outlineColor = new Color32(0, 0, 0, 200);
        bannerSub = UiKit.MakeText(bannerBand.transform, "Podtitulek", "", 26, TextAlignmentOptions.Center, center, new Vector2(0f, -36f), new Vector2(1600f, 32f));
        bannerSub.fontStyle = FontStyles.Bold;
        bannerGroup.SetActive(false);

        // hned pod horni listou (body A-D konci ~110 px od hornho okraje); behem cekani se pruh zabirani schova
        var waitBand = UiKit.MakeImage(canvas, "CekaniNaBod", new Color(0.03f, 0.05f, 0.09f, 0.72f), top, new Vector2(0f, -140f), new Vector2(760f, 168f));
        waitGroup = waitBand.gameObject;
        waitTitle = UiKit.MakeText(waitBand.transform, "Titulek", "", 28, TextAlignmentOptions.Center, center, new Vector2(0f, 58f), new Vector2(740f, 36f));
        waitTitle.fontStyle = FontStyles.Bold | FontStyles.Italic;
        waitCount = UiKit.MakeText(waitBand.transform, "Odpocet", "", 50, TextAlignmentOptions.Center, center, new Vector2(0f, 16f), new Vector2(740f, 56f));
        waitCount.fontStyle = FontStyles.Bold;
        var waitBar = UiKit.MakeImage(waitBand.transform, "Pruh", new Color(1f, 1f, 1f, 0.15f), center, new Vector2(0f, -20f), new Vector2(WaitBarWidth, 8f));
        waitFill = UiKit.MakeImage(waitBar.transform, "Fill", Color.white, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(WaitBarWidth, 8f));
        waitFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        waitSub = UiKit.MakeText(waitBand.transform, "Podtitulek", "", 20, TextAlignmentOptions.Top, center, new Vector2(0f, -54f), new Vector2(740f, 52f),
            new Color(0.85f, 0.87f, 0.92f));
        waitGroup.SetActive(false);

        // Znacka bodu ve svete.
        var markerObject = new GameObject("CaptureMarker", typeof(RectTransform));
        markerObject.transform.SetParent(canvas, false);
        marker = (RectTransform)markerObject.transform;
        marker.anchorMin = marker.anchorMax = center;
        marker.pivot = new Vector2(0.5f, 0f);
        markerIcon = UiKit.MakeImage(marker, "Icon", Color.white, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(22f, 22f));
        markerIcon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        markerText = UiKit.MakeText(marker, "Text", "", 22, TextAlignmentOptions.Center, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(240f, 26f));

        panel.SetActive(false);
        markerObject.SetActive(false);
    }

    static string Clock(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    public void Tick(PlayerHero local, bool playing, MatchManager match)
    {
        if (panel == null) return;

        bool bannerOn = Time.unscaledTime < bannerUntil;
        if (bannerGroup.activeSelf != bannerOn)
            bannerGroup.SetActive(bannerOn);
        if (bannerOn)
        {
            float age = Time.unscaledTime - bannerStart;
            float fade = Mathf.Clamp01((bannerUntil - Time.unscaledTime) / 0.45f);
            float grow = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / 0.3f), 3f);
            float pop = Mathf.Lerp(1.3f, 1f, Mathf.Clamp01(age / 0.18f));
            banner.rectTransform.localScale = new Vector3(pop, pop, 1f);
            banner.alpha = fade * Mathf.Clamp01(age / 0.1f);
            bannerSub.alpha = fade * Mathf.Clamp01((age - 0.15f) / 0.2f);
            float lineWidth = 1150f * grow;
            lineTop.rectTransform.sizeDelta = new Vector2(lineWidth, 4f);
            lineBottom.rectTransform.sizeDelta = new Vector2(lineWidth, 4f);
            var c = lineTop.color; c.a = fade; lineTop.color = c; lineBottom.color = c;
            bannerBand.color = new Color(0f, 0f, 0f, 0.5f * fade * grow);
        }

        bool show = playing && match != null && match.IsAttackMode && !match.IsOver;
        UpdateWait(local, match, show);
        // behem cekani na odemceni je vsechno v panelu cekani (pruh by se s nim prekryval)
        bool showPanel = show && !waitGroup.activeSelf;
        if (panel.activeSelf != showPanel)
            panel.SetActive(showPanel);

        if (!show)
        {
            if (marker.gameObject.activeSelf)
                marker.gameObject.SetActive(false);
            if (raceGroup.activeSelf)
                raceGroup.SetActive(false);
            return;
        }

        int attackers = match.attackTeam.Value;
        int localTeam = TeamOf(local);
        string role = localTeam < 0 ? "" : localTeam == attackers ? "  ·  <b>ÚTOČÍŠ</b>" : "  ·  <b>BRÁNÍŠ</b>";
        string pointName = MatchManager.PointNames[Mathf.Clamp(match.pointIndex.Value, 0, MatchManager.PointNames.Length - 1)];
        header.text = $"{match.round.Value}. KOLO  ·  ÚTOČÍ {MatchManager.TeamName(attackers)}  ·  BOD {pointName}{role}";
        header.color = UiKit.TeamColor(attackers);

        int phase = match.roundPhase.Value;
        int state = match.pointState.Value;
        // prodlouzeni prave zacalo
        if (lastPhase >= 0 && phase == MatchManager.RoundOvertime && lastPhase != MatchManager.RoundOvertime)
        {
            Headline("PRODLOUŽENÍ", localTeam == attackers ? "Udržte se na bodě!" : localTeam >= 0 ? "Vyžeňte je z bodu!" : "", new Color(1f, 0.62f, 0.12f), 2.8f);
            ProceduralSfx.Play(ProceduralSfx.CaptureUnlock, match.pointPosition.Value, 0.6f);
        }
        lastPhase = phase;
        int seconds = match.secondsLeft.Value;
        switch (phase)
        {
            case MatchManager.RoundSetup:
                timeText.text = $"PŘÍPRAVA  {Clock(seconds)}";
                timeText.color = new Color(0.85f, 0.87f, 0.92f);
                break;
            case MatchManager.RoundOvertime:
                timeText.text = "PRODLOUŽENÍ";
                timeText.color = Color.Lerp(new Color(1f, 0.82f, 0.15f), Color.white, Mathf.PingPong(Time.unscaledTime * 3f, 1f));
                break;
            case MatchManager.RoundIntermission:
                timeText.text = $"VÝMĚNA STRAN  {Clock(seconds)}";
                timeText.color = Color.white;
                break;
            default:
                timeText.text = Clock(seconds);
                timeText.color = seconds <= 30 ? new Color(1f, 0.45f, 0.35f) : Color.white;
                break;
        }

        float progress = match.progress.Value;
        barFill.rectTransform.sizeDelta = new Vector2(BarWidth * Mathf.Clamp01(progress), 14f);
        barFill.color = UiKit.TeamColor(attackers);

        if (phase == MatchManager.RoundIntermission)
            status.text = "";
        else if (state == MatchManager.StateLocked)
            status.text = match.lockSecondsLeft.Value > 0 ? $"bod {pointName} se odemkne za {match.lockSecondsLeft.Value} s" : $"bod {pointName} zamčený";
        else if (state == MatchManager.StateContested)
            status.text = $"SPORNÝ  ·  {Mathf.FloorToInt(progress * 100f)} %";
        else if (state == MatchManager.StateFree)
            status.text = $"{Mathf.FloorToInt(progress * 100f)} %  ·  bod je volný";
        else
            status.text = $"ZABÍRÁ SE  ·  {Mathf.FloorToInt(progress * 100f)} %";
        status.color = state == MatchManager.StateLocked ? new Color(0.85f, 0.87f, 0.92f) : CapturePointView.StateColor(state);

        // Druhe kolo: co je potreba prekonat.
        bool secondRound = match.ChasingRound && match.firstPoints.Value >= 0 && phase != MatchManager.RoundIntermission;
        bool solo = match.roundKind.Value == MatchManager.KindSolo;
        bool goalOnBar = false;
        if (secondRound)
        {
            int points = match.firstPoints.Value;
            int percent = Mathf.FloorToInt(match.firstProgress.Value * 100f);
            bool defending = localTeam >= 0 && localTeam != attackers;
            string goalPoint = MatchManager.PointNames[Mathf.Clamp(points, 0, MatchManager.PointNames.Length - 1)];
            string taken = points switch { 1 => "bod A a ", 2 => "body A, B a ", 3 => "body A, B, C a ", _ => "" };
            if (solo)
                target.text = defending
                    ? $"ROZSTŘEL  ·  Ubraňte: soupeř nesmí dobýt {percent} % bodu A, jinak prohráváte"
                    : $"ROZSTŘEL  ·  Cíl: dobýt aspoň {percent} % bodu A = výhra";
            else if (points >= match.CapturePointCount)
                target.text = defending
                    ? $"Ubraňte: soupeř potřebuje všechny body (vám zbylo v bance {Clock(Mathf.CeilToInt(match.firstTime.Value))})"
                    : $"Cíl: všechny body  ·  soupeři zbylo v bance {Clock(Mathf.CeilToInt(match.firstTime.Value))} - šetřete čas na kola navíc";
            else
                target.text = defending
                    ? $"Ubraňte: soupeř potřebuje {taken}na bodě {goalPoint} víc než {percent} %"
                    : $"Cíl: {taken}na bodě {goalPoint} víc než {percent} %";
            target.color = GoalColor;

            // rozhodujici bod: na pruhu je videt, kam az se soupeř dostal
            goalOnBar = points < match.CapturePointCount && match.pointIndex.Value == points;
            if (goalOnBar)
            {
                float goal = Mathf.Clamp01(match.firstProgress.Value);
                ghostFill.rectTransform.sizeDelta = new Vector2(BarWidth * goal, 14f);
                var ghost = UiKit.TeamColor(1 - attackers); ghost.a = 0.4f;
                ghostFill.color = ghost;
                goalLine.rectTransform.anchoredPosition = new Vector2(3f + BarWidth * goal, 0f);
                bool beaten = progress > goal + 0.01f;
                goalText.text = beaten ? "PŘEKONÁNO" : $"CÍL {percent} %";
                if (solo) ghostFill.color = new Color(GoalColor.r, GoalColor.g, GoalColor.b, 0.3f);
                float pulse = Mathf.PingPong(Time.unscaledTime * 2f, 1f);
                goalLine.color = Color.Lerp(GoalColor, Color.white, pulse * 0.5f);
            }

            // zavod: soupeř (1. kolo) nahore, utocnici ted dole
            int count = Mathf.Min(match.CapturePointCount, MatchManager.PointNames.Length);
            int opponent = 1 - attackers;
            raceLabel[0].text = defending ? "VY" : "SOUPEŘ";
            raceLabel[0].color = UiKit.TeamColor(opponent);
            raceLabel[1].text = defending ? "SOUPEŘ" : "VY";
            raceLabel[1].color = UiKit.TeamColor(attackers);
            raceTitle.text = points >= match.CapturePointCount
                ? (defending ? $"Soupeř potřebuje všechny body  ·  vaše banka {Clock(Mathf.CeilToInt(match.firstTime.Value))}"
                             : $"Cíl: všechny body  ·  soupeřova banka {Clock(Mathf.CeilToInt(match.firstTime.Value))}")
                : (defending ? $"Nepusťte je přes {percent} % na bodě {goalPoint}" : $"Cíl: přes {percent} % na bodě {goalPoint}");
            // radky vystredit: popis + body
            float block = LabelWidth + 8f + count * PipWidth + (count - 1) * PipGap;
            float left = -block * 0.5f;
            for (int row = 0; row < 2; row++)
            {
                var labelRect = raceLabel[row].rectTransform;
                labelRect.anchoredPosition = new Vector2(left + LabelWidth * 0.5f, labelRect.anchoredPosition.y);
                for (int i = 0; i < count; i++)
                {
                    var r = pips[row][i].rectTransform;
                    r.anchoredPosition = new Vector2(left + LabelWidth + 8f + PipWidth * 0.5f + i * (PipWidth + PipGap), r.anchoredPosition.y);
                }
            }
            for (int i = 0; i < pips[0].Length; i++)
            {
                bool used = i < count;
                for (int row = 0; row < 2; row++)
                    if (pips[row][i].gameObject.activeSelf != used) pips[row][i].gameObject.SetActive(used);
                if (!used) continue;
                float first = i < points ? 1f : i == points ? match.firstProgress.Value : 0f;
                float now = i < match.pointIndex.Value ? 1f : i == match.pointIndex.Value ? progress : 0f;
                SetPip(pipFills[0][i], first, UiKit.TeamColor(opponent));
                SetPip(pipFills[1][i], now, UiKit.TeamColor(attackers));
            }
        }
        else
        {
            target.text = "";
        }
        bool race = secondRound && !solo;
        if (raceGroup.activeSelf != race) raceGroup.SetActive(race);
        // cil je v karticce (s body); samostatny radek jen pri rozstrelu
        if (target.gameObject.activeSelf != !race) target.gameObject.SetActive(!race);
        if (ghostFill.gameObject.activeSelf != goalOnBar) ghostFill.gameObject.SetActive(goalOnBar);
        if (goalLine.gameObject.activeSelf != goalOnBar) goalLine.gameObject.SetActive(goalOnBar);

        // cil 2. kola je behem cekani videt v panelu cekani
        if (waitGroup.activeSelf && target.text.Length > 0 && !raceGroup.activeSelf)
            waitSub.text += $"\n<color=#{ColorUtility.ToHtmlStringRGB(GoalColor)}>{target.text}</color>";

        UpdateMarker(local, match, state);
        UpdateSounds(local, match, state);
    }

    void UpdateWait(PlayerHero local, MatchManager match, bool show)
    {
        int phase = match != null ? match.roundPhase.Value : -1;
        int left = match != null ? match.lockSecondsLeft.Value : 0;
        bool on = show && !ReplayPlayer.Active && phase != MatchManager.RoundIntermission
            && match.pointState.Value == MatchManager.StateLocked && left > 0;
        if (waitGroup.activeSelf != on) waitGroup.SetActive(on);
        if (!on) return;

        // celkova delka cekani (pro pruh): nejvyssi hodnota od zacatku tohoto cekani
        int point = match.pointIndex.Value;
        if (point != waitPoint || phase != waitPhase || left > waitTotal)
        {
            waitPoint = point;
            waitPhase = phase;
            waitTotal = Mathf.Max(1, left);
        }

        int attackers = match.attackTeam.Value;
        int localTeam = TeamOf(local);
        bool attacking = localTeam == attackers;
        string pointName = MatchManager.PointNames[Mathf.Clamp(point, 0, MatchManager.PointNames.Length - 1)];
        Color teamColor = UiKit.TeamColor(attackers);

        bool setup = phase == MatchManager.RoundSetup;
        bool defending = localTeam >= 0 && !attacking;
        if (defending) teamColor = UiKit.TeamColor(localTeam);   // obrance: barva vlastniho tymu
        // V priprave ukazuje odpocet horni lista (PRIPRAVA 0:06) - tady jen co se deje, bez dalsiho odpoctu.
        // Mezi body ma horni lista cas utoku, takze odpocet odemceni je jen tady.
        waitTitle.text = setup
            ? (defending ? $"PŘÍPRAVA  ·  OBRAŇTE BOD {pointName}" : $"PŘÍPRAVA  ·  ÚTOK NA BOD {pointName}")
            : (defending ? $"BRAŇTE BOD {pointName}  ·  ODEMČE SE ZA" : $"ČEKÁ SE NA ODEMČENÍ BODU {pointName}");
        waitTitle.color = teamColor;
        bool count = !setup;
        if (waitCount.gameObject.activeSelf != count)
        {
            waitCount.gameObject.SetActive(count);
            // bez odpoctu nizsi panel
            float h = count ? 168f : 116f;
            waitGroup.GetComponent<RectTransform>().sizeDelta = new Vector2(760f, h);
            waitTitle.rectTransform.anchoredPosition = new Vector2(0f, count ? 58f : 32f);
            waitFill.transform.parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, count ? -20f : 2f);
            waitSub.rectTransform.anchoredPosition = new Vector2(0f, count ? -54f : -30f);
        }
        waitCount.text = Clock(left);
        waitCount.color = left <= 3 ? Color.Lerp(Color.white, new Color(1f, 0.82f, 0.15f), Mathf.PingPong(Time.unscaledTime * 4f, 1f)) : Color.white;
        waitSub.text = localTeam < 0 ? ""
            : attacking ? (setup ? "Vyber hrdinu a připrav se - dveře spawnu se otevřou se začátkem útoku"
                                 : $"Čekejte ve spawnu - po odemčení vyrazte na bod {pointName}")
            : $"Obsaďte bod {pointName} a připravte obranu";
        float k = Mathf.Clamp01((float)left / waitTotal);
        waitFill.rectTransform.sizeDelta = new Vector2(WaitBarWidth * k, 8f);
        waitFill.color = teamColor;
    }

    static void SetPip(Image fill, float amount, Color color)
    {
        fill.rectTransform.sizeDelta = new Vector2((PipWidth - 4f) * Mathf.Clamp01(amount), 12f);
        color.a = amount >= 0.999f ? 1f : 0.8f;
        fill.color = color;
    }

    static int TeamOf(PlayerHero local)
    {
        var team = local != null ? local.GetComponent<PlayerTeam>() : null;
        return team != null ? team.teamId.Value : -1;
    }

    void UpdateSounds(PlayerHero local, MatchManager match, int state)
    {
        var fpc = local != null ? local.GetComponent<FirstPersonController>() : null;
        Vector3 ear = fpc != null && fpc.playerCamera != null ? fpc.playerCamera.transform.position : match.pointPosition.Value;

        // Bod se prave odemkl.
        bool samePoint = lastPoint == match.pointIndex.Value;
        if (samePoint && lastState == MatchManager.StateLocked && state != MatchManager.StateLocked)
            ProceduralSfx.Play(ProceduralSfx.CaptureUnlock, ear, 0.8f);

        lastState = state;
        lastPoint = match.pointIndex.Value;

        // Utocnici bod zabiraji: tikani, ktere se s postupem zrychluje.
        if (state != MatchManager.StateTeam0 && state != MatchManager.StateTeam1) return;
        if (Time.unscaledTime < nextTick) return;

        nextTick = Time.unscaledTime + Mathf.Lerp(0.6f, 0.22f, match.progress.Value);
        ProceduralSfx.Play(CollectedAudioBank.Pick("ui_capture_tick", ProceduralSfx.CaptureTick), ear, 0.55f);
    }

    void UpdateMarker(PlayerHero local, MatchManager match, int state)
    {
        bool visible = false;
        var fpc = local != null ? local.GetComponent<FirstPersonController>() : null;
        var camera = fpc != null ? fpc.playerCamera : null;

        if (camera != null && match.roundPhase.Value != MatchManager.RoundIntermission)
        {
            Vector3 world = match.pointPosition.Value + Vector3.up * 2.6f;
            Vector3 screen = camera.WorldToScreenPoint(world);
            float distance = Vector3.Distance(local.transform.position, match.pointPosition.Value);

            // Kdyz hrac v bodu stoji, znacka nad nim neprekazi.
            if (screen.z > 0.5f && distance > 6f
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 point))
            {
                visible = true;
                marker.anchoredPosition = point;
                var color = CapturePointView.StateColor(state);
                markerIcon.color = color;
                markerText.color = color;
                string name = MatchManager.PointNames[Mathf.Clamp(match.pointIndex.Value, 0, MatchManager.PointNames.Length - 1)];
                markerText.text = $"{name}  {Mathf.RoundToInt(distance)} m";
            }
        }

        if (marker.gameObject.activeSelf != visible)
            marker.gameObject.SetActive(visible);
    }
}
