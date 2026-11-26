using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Horni lista rezimu Tlacenice (misto TopBarUI): cas zapasu, pruh trasy s obema Yarisy a stav Honzy.
// Pruh: vlevo zakladna tymu 0, vpravo zakladna tymu 1, uprostred start. Yaris tymu 0 (modry) jede doprava,
// Yaris tymu 1 (cerveny) doleva - barevne vyplneni = jak daleko ho tym dotlacil. Oranzova znacka = Honza.
// Kosoctverce = checkpointy (plne = dosazene). Pod pruhem: kdo tlaci / sporny / Honza bezi k Yarisu, metry obou tymu.
public class PushUI
{
    const float BarWidth = 640f, BarHeight = 14f, CenterWidth = 180f;
    static readonly Color Dark = new Color(0.05f, 0.07f, 0.1f, 0.88f);
    static readonly Color Overtime = new Color(1f, 0.62f, 0.12f);
    static readonly Color Contested = new Color(1f, 0.62f, 0.12f);

    GameObject root;
    Image centerEdge, fill0, fill1, car0, car1, robot;
    TextMeshProUGUI clock, phase, status, distances, base0, base1;
    Image overtimePlate;
    readonly Image[] marks0 = new Image[4], marks1 = new Image[4];
    readonly Image[] markFill0 = new Image[4], markFill1 = new Image[4];

    public void Build(Transform canvas)
    {
        var top = new Vector2(0.5f, 1f);
        root = UiKit.MakeImage(canvas, "TlaceniceLista", Color.clear, top, new Vector2(0f, -40f), new Vector2(900f, 80f)).gameObject;
        var mid = new Vector2(0.5f, 0.5f);

        var centerBox = UiKit.MakeImage(root.transform, "Stred", Dark, mid, Vector2.zero, new Vector2(CenterWidth, 70f));
        centerEdge = UiKit.MakeImage(centerBox.transform, "Hrana", UiKit.Accent, new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(CenterWidth, 4f));
        clock = UiKit.MakeText(centerBox.transform, "Cas", "", 38, TextAlignmentOptions.Center, mid, new Vector2(0f, 8f), new Vector2(CenterWidth, 44f));
        clock.fontStyle = FontStyles.Bold | FontStyles.Italic;
        phase = UiKit.MakeText(centerBox.transform, "Faze", "", 16, TextAlignmentOptions.Center, mid, new Vector2(0f, -22f), new Vector2(CenterWidth, 20f),
            new Color(0.8f, 0.83f, 0.9f));
        phase.fontStyle = FontStyles.Bold;

        // pruh trasy
        var bar = UiKit.MakeImage(root.transform, "Pruh", Dark, mid, new Vector2(0f, -56f), new Vector2(BarWidth + 8f, BarHeight + 8f));
        var back = UiKit.MakeImage(bar.transform, "Podklad", new Color(1f, 1f, 1f, 0.08f), mid, Vector2.zero, new Vector2(BarWidth, BarHeight));
        fill0 = UiKit.MakeImage(back.transform, "Postup0", UiKit.Team0, mid, Vector2.zero, new Vector2(0f, BarHeight));
        fill0.rectTransform.pivot = new Vector2(0f, 0.5f);   // od startu doprava
        fill1 = UiKit.MakeImage(back.transform, "Postup1", UiKit.Team1, mid, Vector2.zero, new Vector2(0f, BarHeight));
        fill1.rectTransform.pivot = new Vector2(1f, 0.5f);   // od startu doleva
        UiKit.MakeImage(back.transform, "Start", new Color(1f, 1f, 1f, 0.7f), mid, Vector2.zero, new Vector2(2f, BarHeight + 8f));

        for (int i = 0; i < marks0.Length; i++)
        {
            MakeMark(back.transform, out marks0[i], out markFill0[i]);
            MakeMark(back.transform, out marks1[i], out markFill1[i]);
        }
        car0 = MakeCar(back.transform, UiKit.Team0);
        car1 = MakeCar(back.transform, UiKit.Team1);
        robot = UiKit.MakeImage(back.transform, "Honza", UiKit.Accent, mid, new Vector2(0f, BarHeight * 0.5f + 7f), new Vector2(12f, 12f));
        robot.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        base0 = UiKit.MakeText(bar.transform, "Zakladna0", "", 15, TextAlignmentOptions.Right, mid, new Vector2(-BarWidth * 0.5f - 76f, 0f), new Vector2(140f, 22f), UiKit.Team0);
        base0.fontStyle = FontStyles.Bold;
        base1 = UiKit.MakeText(bar.transform, "Zakladna1", "", 15, TextAlignmentOptions.Left, mid, new Vector2(BarWidth * 0.5f + 76f, 0f), new Vector2(140f, 22f), UiKit.Team1);
        base1.fontStyle = FontStyles.Bold;

        status = UiKit.MakeText(root.transform, "Stav", "", 20, TextAlignmentOptions.Center, mid, new Vector2(0f, -82f), new Vector2(700f, 26f));
        status.fontStyle = FontStyles.Bold | FontStyles.Italic;
        distances = UiKit.MakeText(root.transform, "Metry", "", 15, TextAlignmentOptions.Center, mid, new Vector2(0f, -102f), new Vector2(700f, 20f),
            new Color(0.82f, 0.85f, 0.92f));

        overtimePlate = UiKit.MakeImage(root.transform, "Prodlouzeni", Overtime, mid, new Vector2(0f, -164f), new Vector2(260f, 30f));
        var overtimeText = UiKit.MakeText(overtimePlate.transform, "Text", "»  PRODLOUŽENÍ  «", 22, TextAlignmentOptions.Center, mid, Vector2.zero, new Vector2(260f, 30f),
            new Color(0.08f, 0.05f, 0.02f));
        overtimeText.fontStyle = FontStyles.Bold | FontStyles.Italic;

        root.SetActive(false);
    }

    static void MakeMark(Transform parent, out Image mark, out Image fill)
    {
        mark = UiKit.MakeImage(parent, "Checkpoint", new Color(1f, 1f, 1f, 0.6f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 14f));
        mark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        fill = UiKit.MakeImage(mark.transform, "Plne", Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(9f, 9f));
    }

    // Yaris na pruhu: bily ramecek s vnitrkem v barve tymu.
    static Image MakeCar(Transform parent, Color color)
    {
        var frame = UiKit.MakeImage(parent, "Yaris", Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, BarHeight + 12f));
        UiKit.MakeImage(frame.transform, "Barva", color, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, BarHeight + 8f));
        return frame;
    }

    public void Tick(PlayerHero local, bool playing, MatchManager match)
    {
        if (root == null) return;
        var route = PushRoute.Active;
        bool show = playing && match != null && match.IsPushMode && !match.IsOver && route != null;
        if (root.activeSelf != show) root.SetActive(show);
        if (!show) return;

        var localTeam = local != null ? local.GetComponent<PlayerTeam>() : null;
        int mine = localTeam != null ? localTeam.teamId.Value : -1;

        // cas a faze
        int seconds = Mathf.Max(0, match.secondsLeft.Value);
        int roundPhase = match.roundPhase.Value;
        bool overtime = roundPhase == MatchManager.RoundOvertime;
        phase.text = roundPhase == MatchManager.RoundSetup ? "PŘÍPRAVA" : overtime ? "PRODLOUŽENÍ" : "TLAČENICE";
        clock.text = overtime ? "0:00" : $"{seconds / 60}:{seconds % 60:00}";
        bool hurry = roundPhase == MatchManager.RoundAttack && seconds <= 30;
        clock.color = overtime ? Overtime : hurry
            ? Color.Lerp(new Color(1f, 0.35f, 0.3f), Color.white, Mathf.PingPong(Time.unscaledTime * 2f, 1f) * (seconds <= 10 ? 1f : 0.3f))
            : Color.white;
        int owner = match.PushOwner;
        centerEdge.color = overtime ? Overtime : owner >= 0 ? UiKit.TeamColor(owner) : UiKit.Accent;
        if (overtimePlate.gameObject.activeSelf != overtime) overtimePlate.gameObject.SetActive(overtime);
        if (overtime) overtimePlate.color = Color.Lerp(Overtime, Color.white, Mathf.PingPong(Time.unscaledTime * 2.5f, 1f) * 0.35f);

        // pruh: Yarisy, postup, Honza
        float center = route.Center;
        float s0 = PushRobot.DisplayCars[0] >= 0f ? PushRobot.DisplayCars[0] : match.PushCarOf(route, 0);
        float s1 = PushRobot.DisplayCars[1] >= 0f ? PushRobot.DisplayCars[1] : match.PushCarOf(route, 1);
        float sRobot = PushRobot.DisplayRobot >= 0f ? PushRobot.DisplayRobot : match.PushRobotOf(route);
        fill0.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, BarX(route, s0)), BarHeight);
        fill1.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, -BarX(route, s1)), BarHeight);
        car0.rectTransform.anchoredPosition = new Vector2(BarX(route, s0), 0f);
        car1.rectTransform.anchoredPosition = new Vector2(BarX(route, s1), 0f);
        robot.rectTransform.anchoredPosition = new Vector2(BarX(route, sRobot), BarHeight * 0.5f + 7f);
        int state = match.pushState.Value;
        robot.color = state == MatchManager.PushContested ? Contested : owner >= 0 ? Color.Lerp(UiKit.Accent, UiKit.TeamColor(owner), 0.6f) : UiKit.Accent;

        for (int i = 0; i < marks0.Length; i++)
        {
            bool on = i < route.CheckpointCount;
            SetMark(marks0[i], markFill0[i], on, BarX(route, route.Checkpoint(0, i)), i < match.pushCheckpoints0.Value, UiKit.Team0);
            SetMark(marks1[i], markFill1[i], on, BarX(route, route.Checkpoint(1, i)), i < match.pushCheckpoints1.Value, UiKit.Team1);
        }

        base0.text = mine == 0 ? "VY  «" : $"{MatchManager.TeamName(0)}  «";
        base1.text = mine == 1 ? "»  VY" : $"»  {MatchManager.TeamName(1)}";

        // stav Honzy
        int pushers0 = match.PushersOf(0), pushers1 = match.PushersOf(1);
        switch (state)
        {
            case MatchManager.PushLocked:
                status.text = "Honza se probouzí…";
                status.color = UiKit.Accent;
                break;
            case MatchManager.PushContested:
                status.text = $"SPORNÝ  ·  u Honzy jsou oba týmy ({pushers0} : {pushers1})";
                status.color = Contested;
                break;
            case MatchManager.PushWalking:
                status.text = $"HONZA BĚŽÍ K YARISU: {MatchManager.TeamName(owner)}{(owner == mine ? " (VÁŠ)" : "")}";
                status.color = UiKit.TeamColor(owner);
                break;
            case MatchManager.PushPushing:
                int count = owner == 0 ? pushers0 : pushers1;
                status.text = $"TLAČÍ {MatchManager.TeamName(owner)}{(owner == mine ? " (VY)" : "")}  ·  {new string('»', Mathf.Clamp(count, 1, 3))}";
                status.color = UiKit.TeamColor(owner);
                break;
            default:
                status.text = owner < 0 ? "HONZA ČEKÁ UPROSTŘED  ·  dojdi k němu" : "HONZA STOJÍ  ·  nikdo u něj není";
                status.color = new Color(0.85f, 0.88f, 0.95f);
                break;
        }
        distances.text = $"<color=#{ColorUtility.ToHtmlStringRGB(UiKit.Team0)}>{MatchManager.TeamName(0)}: {match.pushBest0.Value:0} m</color>      "
            + $"<color=#{ColorUtility.ToHtmlStringRGB(UiKit.Team1)}>{MatchManager.TeamName(1)}: {match.pushBest1.Value:0} m</color>";
    }

    // s na trase -> x na pruhu (s = 0 vlevo, stred trasy uprostred).
    static float BarX(PushRoute route, float s) => (Mathf.Clamp01(s / Mathf.Max(1f, route.Length)) - 0.5f) * BarWidth;

    static void SetMark(Image mark, Image fill, bool on, float x, bool reached, Color color)
    {
        if (mark.gameObject.activeSelf != on) mark.gameObject.SetActive(on);
        if (!on) return;
        mark.rectTransform.anchoredPosition = new Vector2(x, 0f);
        mark.color = reached ? color : new Color(color.r, color.g, color.b, 0.75f);
        fill.color = reached ? color : new Color(0.05f, 0.07f, 0.1f, 1f);
    }
}
