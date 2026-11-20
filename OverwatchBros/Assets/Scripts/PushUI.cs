using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Horni lista rezimu Tlacenice (misto TopBarUI): cas zapasu, pruh postupu Yarisu a stav robota.
// Pruh: vlevo zakladna tymu 0, vpravo zakladna tymu 1, uprostred start. Tym 0 tlaci doprava, tym 1 doleva.
// Barevne vyplneni = nejdal dotlaceno (postup tymu), bily jezdec = kde Yaris prave je, kosoctverce = checkpointy
// (plne = dosazene). Pod pruhem: kdo tlaci / sporny / robot prechazi, a metry obou tymu.
public class PushUI
{
    const float BarWidth = 640f, BarHeight = 14f, CenterWidth = 180f;
    static readonly Color Dark = new Color(0.05f, 0.07f, 0.1f, 0.88f);
    static readonly Color Overtime = new Color(1f, 0.62f, 0.12f);
    static readonly Color Contested = new Color(1f, 0.62f, 0.12f);

    GameObject root;
    Image centerEdge, fill0, fill1, car;
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

        // pruh postupu
        var bar = UiKit.MakeImage(root.transform, "Pruh", Dark, mid, new Vector2(0f, -56f), new Vector2(BarWidth + 8f, BarHeight + 8f));
        var back = UiKit.MakeImage(bar.transform, "Podklad", new Color(1f, 1f, 1f, 0.08f), mid, Vector2.zero, new Vector2(BarWidth, BarHeight));
        fill0 = UiKit.MakeImage(back.transform, "Postup0", UiKit.Team0, mid, Vector2.zero, new Vector2(0f, BarHeight));
        fill0.rectTransform.pivot = new Vector2(0f, 0.5f);   // od stredu doprava
        fill1 = UiKit.MakeImage(back.transform, "Postup1", UiKit.Team1, mid, Vector2.zero, new Vector2(0f, BarHeight));
        fill1.rectTransform.pivot = new Vector2(1f, 0.5f);   // od stredu doleva
        UiKit.MakeImage(back.transform, "Start", new Color(1f, 1f, 1f, 0.7f), mid, Vector2.zero, new Vector2(2f, BarHeight + 8f));

        for (int i = 0; i < marks0.Length; i++)
        {
            MakeMark(back.transform, out marks0[i], out markFill0[i]);
            MakeMark(back.transform, out marks1[i], out markFill1[i]);
        }
        car = UiKit.MakeImage(back.transform, "Yaris", Color.white, mid, Vector2.zero, new Vector2(10f, BarHeight + 12f));

        base0 = UiKit.MakeText(bar.transform, "Zakladna0", "", 15, TextAlignmentOptions.Right, mid, new Vector2(-BarWidth * 0.5f - 76f, 0f), new Vector2(140f, 22f), UiKit.Team0);
        base0.fontStyle = FontStyles.Bold;
        base1 = UiKit.MakeText(bar.transform, "Zakladna1", "", 15, TextAlignmentOptions.Left, mid, new Vector2(BarWidth * 0.5f + 76f, 0f), new Vector2(140f, 22f), UiKit.Team1);
        base1.fontStyle = FontStyles.Bold;

        status = UiKit.MakeText(root.transform, "Stav", "", 20, TextAlignmentOptions.Center, mid, new Vector2(0f, -84f), new Vector2(700f, 26f));
        status.fontStyle = FontStyles.Bold | FontStyles.Italic;
        distances = UiKit.MakeText(root.transform, "Metry", "", 15, TextAlignmentOptions.Center, mid, new Vector2(0f, -106f), new Vector2(700f, 20f),
            new Color(0.82f, 0.85f, 0.92f));

        overtimePlate = UiKit.MakeImage(root.transform, "Prodlouzeni", Overtime, mid, new Vector2(0f, -134f), new Vector2(260f, 30f));
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

        // pruh
        float half = Mathf.Max(1f, route.Center);
        float best0 = match.pushBest0.Value, best1 = match.pushBest1.Value;
        fill0.rectTransform.sizeDelta = new Vector2(Mathf.Clamp01(best0 / half) * BarWidth * 0.5f, BarHeight);
        fill1.rectTransform.sizeDelta = new Vector2(Mathf.Clamp01(best1 / half) * BarWidth * 0.5f, BarHeight);
        float carS = PushRobot.DisplayCar >= 0f ? PushRobot.DisplayCar : match.pushCar.Value < 0f ? route.Center : match.pushCar.Value;
        car.rectTransform.anchoredPosition = new Vector2(BarX(route, carS), 0f);
        int state = match.pushState.Value;
        car.color = state == MatchManager.PushContested ? Contested : owner >= 0 && state == MatchManager.PushPushing ? Color.Lerp(Color.white, UiKit.TeamColor(owner), 0.35f) : Color.white;

        for (int i = 0; i < marks0.Length; i++)
        {
            bool on = i < route.CheckpointCount;
            SetMark(marks0[i], markFill0[i], on, BarX(route, route.Checkpoint(0, i)), i < match.pushCheckpoints0.Value, UiKit.Team0);
            SetMark(marks1[i], markFill1[i], on, BarX(route, route.Checkpoint(1, i)), i < match.pushCheckpoints1.Value, UiKit.Team1);
        }

        base0.text = mine == 0 ? "VY  «" : "TÝM 0  «";
        base1.text = mine == 1 ? "»  VY" : "»  TÝM 1";

        // stav robota
        int pushers0 = match.PushersOf(0), pushers1 = match.PushersOf(1);
        switch (state)
        {
            case MatchManager.PushLocked:
                status.text = "Robot se probouzí…";
                status.color = UiKit.Accent;
                break;
            case MatchManager.PushContested:
                status.text = $"SPORNÝ  ·  u robota jsou oba týmy ({pushers0} : {pushers1})";
                status.color = Contested;
                break;
            case MatchManager.PushCrossing:
                status.text = owner >= 0 ? $"ROBOT PŘECHÁZÍ K TÝMU {owner}{(owner == mine ? " (VÁM)" : "")}" : "ROBOT PŘECHÁZÍ";
                status.color = owner >= 0 ? UiKit.TeamColor(owner) : Color.white;
                break;
            case MatchManager.PushPushing:
                int count = owner == 0 ? pushers0 : pushers1;
                status.text = $"TLAČÍ TÝM {owner}{(owner == mine ? " (VY)" : "")}  ·  {new string('»', Mathf.Clamp(count, 1, 3))}";
                status.color = UiKit.TeamColor(owner);
                break;
            default:
                status.text = owner < 0 ? "ROBOT ČEKÁ UPROSTŘED  ·  dojdi k němu" : "ROBOT STOJÍ  ·  nikdo u něj není";
                status.color = new Color(0.85f, 0.88f, 0.95f);
                break;
        }
        distances.text = $"<color=#{ColorUtility.ToHtmlStringRGB(UiKit.Team0)}>tým 0: {best0:0} m</color>      "
            + $"<color=#{ColorUtility.ToHtmlStringRGB(UiKit.Team1)}>tým 1: {best1:0} m</color>";
    }

    // s na trase -> x na pruhu (s = 0 vlevo).
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
