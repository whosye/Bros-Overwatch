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
        barFill = UiKit.MakeImage(background.transform, "Fill", Color.white, new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(0f, 14f));
        barFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        foreach (float t in new[] { 1f / 3f, 2f / 3f })
        {
            var tick = UiKit.MakeImage(background.transform, "Tick", new Color(1f, 1f, 1f, 0.75f), new Vector2(0f, 0.5f),
                new Vector2(3f + BarWidth * t, 0f), new Vector2(3f, 20f));
            tick.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        }

        status = UiKit.MakeText(panel.transform, "Status", "", 22, TextAlignmentOptions.Center, top, new Vector2(0f, -100f), new Vector2(900f, 28f));
        target = UiKit.MakeText(panel.transform, "Target", "", 19, TextAlignmentOptions.Center, top, new Vector2(0f, -126f), new Vector2(900f, 24f),
            new Color(0.85f, 0.87f, 0.92f));

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

        var waitBand = UiKit.MakeImage(canvas, "CekaniNaBod", new Color(0.03f, 0.05f, 0.09f, 0.72f), center, new Vector2(0f, 300f), new Vector2(760f, 140f));
        waitGroup = waitBand.gameObject;
        waitTitle = UiKit.MakeText(waitBand.transform, "Titulek", "", 28, TextAlignmentOptions.Center, center, new Vector2(0f, 44f), new Vector2(740f, 36f));
        waitTitle.fontStyle = FontStyles.Bold | FontStyles.Italic;
        waitCount = UiKit.MakeText(waitBand.transform, "Odpocet", "", 50, TextAlignmentOptions.Center, center, new Vector2(0f, 2f), new Vector2(740f, 56f));
        waitCount.fontStyle = FontStyles.Bold;
        var waitBar = UiKit.MakeImage(waitBand.transform, "Pruh", new Color(1f, 1f, 1f, 0.15f), center, new Vector2(0f, -34f), new Vector2(WaitBarWidth, 8f));
        waitFill = UiKit.MakeImage(waitBar.transform, "Fill", Color.white, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(WaitBarWidth, 8f));
        waitFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        waitSub = UiKit.MakeText(waitBand.transform, "Podtitulek", "", 20, TextAlignmentOptions.Center, center, new Vector2(0f, -56f), new Vector2(740f, 26f),
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
        if (panel.activeSelf != show)
            panel.SetActive(show);
        UpdateWait(local, match, show);

        if (!show)
        {
            if (marker.gameObject.activeSelf)
                marker.gameObject.SetActive(false);
            return;
        }

        int attackers = match.attackTeam.Value;
        int localTeam = TeamOf(local);
        string role = localTeam < 0 ? "" : localTeam == attackers ? "  ·  <b>ÚTOČÍŠ</b>" : "  ·  <b>BRÁNÍŠ</b>";
        string pointName = MatchManager.PointNames[Mathf.Clamp(match.pointIndex.Value, 0, MatchManager.PointNames.Length - 1)];
        header.text = $"{match.round.Value}. KOLO  ·  ÚTOČÍ TÝM {attackers}  ·  BOD {pointName}{role}";
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
        if (match.round.Value == 2 && match.firstPoints.Value >= 0)
        {
            int points = match.firstPoints.Value;
            string taken = points switch { 1 => "bod A a ", 2 => "body A, B a ", 3 => "body A, B, C a ", _ => "" };
            target.text = points >= match.CapturePointCount
                ? $"Cíl: všechny body rychleji než za {Clock(Mathf.CeilToInt(match.firstTime.Value))}"
                : $"Cíl: {taken}na bodě {MatchManager.PointNames[points]} víc než {Mathf.FloorToInt(match.firstProgress.Value * 100f)} %";
        }
        else
        {
            target.text = "";
        }

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
        waitTitle.text = setup ? $"PŘÍPRAVA  ·  ÚTOK NA BOD {pointName} ZAČNE ZA" : $"ČEKÁ SE NA ODEMČENÍ BODU {pointName}";
        waitTitle.color = teamColor;
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
        ProceduralSfx.Play(ProceduralSfx.CaptureTick, ear, 0.55f);
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
