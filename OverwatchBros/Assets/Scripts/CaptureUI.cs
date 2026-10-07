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

    TextMeshProUGUI banner;
    float bannerUntil;

    // zvuky: tikani pri zabirani, znelka pri odemceni
    float nextTick;
    int lastState = -1;
    int lastPoint = -1;

    RectTransform canvasRect;
    RectTransform marker;
    Image markerIcon;
    TextMeshProUGUI markerText;

    public static void Announce(string text, Color color, float seconds = 3.2f)
    {
        if (instance == null || instance.banner == null) return;
        instance.banner.text = text;
        instance.banner.color = color;
        instance.bannerUntil = Time.unscaledTime + seconds;
    }

    public void Build(Transform canvas)
    {
        instance = this;
        canvasRect = (RectTransform)canvas;
        var top = new Vector2(0.5f, 1f);
        var center = new Vector2(0.5f, 0.5f);

        var panelImage = UiKit.MakeImage(canvas, "AttackPanel", new Color(0f, 0f, 0f, 0f), top, new Vector2(0f, -86f), new Vector2(900f, 150f));
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

        banner = UiKit.MakeText(canvas, "AttackBanner", "", 46, TextAlignmentOptions.Center, center, new Vector2(0f, 150f), new Vector2(1400f, 60f));
        banner.gameObject.SetActive(false);

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
        if (banner.gameObject.activeSelf != bannerOn)
            banner.gameObject.SetActive(bannerOn);
        if (bannerOn)
            banner.alpha = Mathf.Clamp01((bannerUntil - Time.unscaledTime) / 0.5f);

        bool show = playing && match != null && match.IsAttackMode && !match.IsOver;
        if (panel.activeSelf != show)
            panel.SetActive(show);

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
            string taken = points switch { 1 => "bod A a ", 2 => "body A, B a ", _ => "" };
            target.text = points >= MatchManager.CapturePointCount
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
