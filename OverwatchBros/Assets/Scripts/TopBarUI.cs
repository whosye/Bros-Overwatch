using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Horni lista skore ve stylu Overwatche: vlevo vzdy vlastni tym (s "VY"), vpravo nepritel. Barevne desky se skore,
// uprostred cas / faze (priprava, prodlouzeni...). Team deathmatch: pod deskami postup k cili ("do 10").
// Utok a obrana: pod stredem body A-D (dobyte v barve utocniku, aktualni se plni podle zabirani, zamcene sede)
// a pri prodlouzeni oranzova blikajici cedule.
public class TopBarUI
{
    const float PlateWidth = 300f, PlateHeight = 54f, CenterWidth = 180f;
    static readonly Color Dark = new Color(0.05f, 0.07f, 0.1f, 0.88f);
    static readonly Color Overtime = new Color(1f, 0.62f, 0.12f);

    GameObject root;
    Plate left, right;
    Image centerBox, centerEdge;
    TextMeshProUGUI clock, phase;
    readonly Image[] pips = new Image[MatchManager.MaxCapturePoints];
    readonly Image[] pipFills = new Image[MatchManager.MaxCapturePoints];
    readonly TextMeshProUGUI[] pipLabels = new TextMeshProUGUI[MatchManager.MaxCapturePoints];
    Image overtimePlate;
    TextMeshProUGUI overtimeText;

    class Plate
    {
        public Image back, edge, progressBack, progressFill;
        public TextMeshProUGUI name, you, score;
    }

    public void Build(Transform canvas)
    {
        var top = new Vector2(0.5f, 1f);
        var rootImage = UiKit.MakeImage(canvas, "HorniLista", Color.clear, top, new Vector2(0f, -40f), new Vector2(900f, 80f));
        root = rootImage.gameObject;

        left = MakePlate(-1f);
        right = MakePlate(1f);

        centerBox = UiKit.MakeImage(root.transform, "Stred", Dark, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CenterWidth, 70f));
        centerEdge = UiKit.MakeImage(centerBox.transform, "Hrana", Color.white, new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(CenterWidth, 4f));
        clock = UiKit.MakeText(centerBox.transform, "Cas", "", 38, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(CenterWidth, 44f));
        clock.fontStyle = FontStyles.Bold | FontStyles.Italic;
        phase = UiKit.MakeText(centerBox.transform, "Faze", "", 16, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0f, -22f), new Vector2(CenterWidth, 20f),
            new Color(0.8f, 0.83f, 0.9f));
        phase.fontStyle = FontStyles.Bold;

        // body A-D pod stredem
        for (int i = 0; i < pips.Length; i++)
        {
            float x = (i - (pips.Length - 1) * 0.5f) * 38f;
            var pip = UiKit.MakeImage(root.transform, "Bod", new Color(1f, 1f, 1f, 0.25f), new Vector2(0.5f, 0.5f), new Vector2(x, -56f), new Vector2(24f, 24f));
            pip.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var inner = UiKit.MakeImage(pip.transform, "Vnitrek", Dark, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(19f, 19f));
            var fill = UiKit.MakeImage(inner.transform, "Plneni", Color.white, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(19f, 0f));
            fill.rectTransform.pivot = new Vector2(0.5f, 0f);
            var label = UiKit.MakeText(root.transform, "Pismeno", MatchManager.PointNames[i], 15, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(x, -56f), new Vector2(24f, 22f));
            label.fontStyle = FontStyles.Bold;
            pips[i] = pip;
            pipFills[i] = fill;
            pipLabels[i] = label;
        }

        overtimePlate = UiKit.MakeImage(root.transform, "Prodlouzeni", Overtime, new Vector2(0.5f, 0.5f), new Vector2(0f, -92f), new Vector2(260f, 30f));
        overtimeText = UiKit.MakeText(overtimePlate.transform, "Text", "»  PRODLOUŽENÍ  «", 22, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260f, 30f),
            new Color(0.08f, 0.05f, 0.02f));
        overtimeText.fontStyle = FontStyles.Bold | FontStyles.Italic;

        root.SetActive(false);
    }

    Plate MakePlate(float side)
    {
        var p = new Plate();
        Vector2 at = new Vector2(side * (CenterWidth * 0.5f + 4f + PlateWidth * 0.5f), 4f);
        p.back = UiKit.MakeImage(root.transform, side < 0 ? "Vlevo" : "Vpravo", Dark, new Vector2(0.5f, 0.5f), at, new Vector2(PlateWidth, PlateHeight));
        // barevny pruh u stredu (jako v OW) a barevny podklad
        p.edge = UiKit.MakeImage(p.back.transform, "Pruh", Color.white, new Vector2(side < 0 ? 1f : 0f, 0.5f), new Vector2(-side * 4f, 0f), new Vector2(8f, PlateHeight));
        float scoreX = -side * (PlateWidth * 0.5f - 52f);
        p.score = UiKit.MakeText(p.back.transform, "Skore", "0", 44, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(scoreX, 2f), new Vector2(90f, 54f));
        p.score.fontStyle = FontStyles.Bold | FontStyles.Italic;
        var align = side < 0 ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
        float nameX = side * 30f;
        p.name = UiKit.MakeText(p.back.transform, "Jmeno", "", 24, align, new Vector2(0.5f, 0.5f), new Vector2(nameX, 8f), new Vector2(PlateWidth - 140f, 30f));
        p.name.fontStyle = FontStyles.Bold | FontStyles.Italic;
        p.you = UiKit.MakeText(p.back.transform, "Vy", "", 15, align, new Vector2(0.5f, 0.5f), new Vector2(nameX, -15f), new Vector2(PlateWidth - 140f, 20f),
            new Color(0.85f, 0.88f, 0.95f));
        p.progressBack = UiKit.MakeImage(p.back.transform, "Postup", new Color(0f, 0f, 0f, 0.6f), new Vector2(0.5f, 0f), new Vector2(0f, -6f), new Vector2(PlateWidth, 6f));
        p.progressFill = UiKit.MakeImage(p.progressBack.transform, "Plneni", Color.white, new Vector2(side < 0 ? 1f : 0f, 0.5f), Vector2.zero, new Vector2(0f, 6f));
        p.progressFill.rectTransform.pivot = new Vector2(side < 0 ? 1f : 0f, 0.5f);   // plni se od stredu ven
        return p;
    }

    static string Clock(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    public void Tick(PlayerHero local, bool playing, MatchManager match)
    {
        if (root == null) return;
        bool show = playing && match != null && !match.IsOver;
        if (root.activeSelf != show) root.SetActive(show);
        if (!show) return;

        var localTeam = local != null ? local.GetComponent<PlayerTeam>() : null;
        int mine = localTeam != null ? localTeam.teamId.Value : 0;
        int enemy = 1 - mine;
        bool attack = match.IsAttackMode;
        int target = Mathf.Max(1, match.scoreToWinSynced.Value);

        FillPlate(left, mine, match, attack, target, localTeam != null);
        FillPlate(right, enemy, match, attack, target, false);

        // stred: cas a faze
        int seconds = attack ? match.secondsLeft.Value : -1;
        int roundPhase = match.roundPhase.Value;
        bool overtime = attack && roundPhase == MatchManager.RoundOvertime;
        if (!attack)
        {
            clock.text = $"{target}";
            phase.text = "PRVNÍ DO";
            clock.color = Color.white;
            centerEdge.color = UiKit.Accent;
        }
        else
        {
            switch (roundPhase)
            {
                case MatchManager.RoundSetup: phase.text = "PŘÍPRAVA"; break;
                case MatchManager.RoundOvertime: phase.text = "PRODLOUŽENÍ"; break;
                case MatchManager.RoundIntermission: phase.text = "VÝMĚNA STRAN"; break;
                default: phase.text = $"{match.round.Value}. KOLO"; break;
            }
            clock.text = overtime ? "0:00" : Clock(Mathf.Max(0, seconds));
            bool hurry = roundPhase == MatchManager.RoundAttack && seconds <= 30;
            clock.color = overtime ? Overtime : hurry ? Color.Lerp(new Color(1f, 0.35f, 0.3f), Color.white, Mathf.PingPong(Time.unscaledTime * 2f, 1f) * (seconds <= 10 ? 1f : 0.3f)) : Color.white;
            centerEdge.color = overtime ? Overtime : UiKit.TeamColor(match.attackTeam.Value);
        }

        // body A-D
        int count = attack ? match.CapturePointCount : 0;
        int current = match.pointIndex.Value;
        Color attackColor = UiKit.TeamColor(match.attackTeam.Value);
        for (int i = 0; i < pips.Length; i++)
        {
            bool on = i < count;
            if (pips[i].gameObject.activeSelf != on)
            {
                pips[i].gameObject.SetActive(on);
                pipLabels[i].gameObject.SetActive(on);
            }
            if (!on) continue;
            float x = (i - (count - 1) * 0.5f) * 38f;
            pips[i].rectTransform.anchoredPosition = new Vector2(x, -56f);
            pipLabels[i].rectTransform.anchoredPosition = new Vector2(x, -56f);
            float fill;
            if (i < current) { fill = 1f; pips[i].color = attackColor; }
            else if (i == current)
            {
                fill = match.pointState.Value == MatchManager.StateLocked ? 0f : Mathf.Clamp01(match.progress.Value);
                float pulse = 0.65f + 0.35f * Mathf.PingPong(Time.unscaledTime * 2f, 1f);
                pips[i].color = match.pointState.Value == MatchManager.StateLocked ? new Color(1f, 1f, 1f, 0.45f) : new Color(1f, 1f, 1f, pulse);
            }
            else { fill = 0f; pips[i].color = new Color(1f, 1f, 1f, 0.2f); }
            pipFills[i].color = attackColor;
            pipFills[i].rectTransform.sizeDelta = new Vector2(19f, 19f * fill);
            pipLabels[i].color = i < current ? Color.white : i == current ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        }

        // prodlouzeni: blikajici oranzova cedule
        if (overtimePlate.gameObject.activeSelf != overtime) overtimePlate.gameObject.SetActive(overtime);
        if (overtime)
        {
            float blink = 0.55f + 0.45f * Mathf.PingPong(Time.unscaledTime * 3f, 1f);
            overtimePlate.color = new Color(Overtime.r, Overtime.g, Overtime.b, blink);
            float s = 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 9f);
            overtimePlate.rectTransform.localScale = new Vector3(s, s, 1f);
        }
    }

    void FillPlate(Plate p, int team, MatchManager match, bool attack, int target, bool isMine)
    {
        Color color = UiKit.TeamColor(team);
        p.back.color = new Color(color.r * 0.32f, color.g * 0.32f, color.b * 0.36f, 0.9f);
        p.edge.color = color;
        int score = team == 0 ? match.team0Score.Value : match.team1Score.Value;
        p.score.text = score.ToString();
        if (attack)
        {
            bool attacking = match.attackTeam.Value == team;
            p.name.text = attacking ? "ÚTOK" : "OBRANA";
        }
        else
            p.name.text = $"TÝM {team}";
        p.name.color = color;
        p.you.text = isMine ? "VY" : "NEPŘÁTELÉ";
        // postup k cili (team deathmatch); v utoku a obrane bez pruhu
        p.progressBack.gameObject.SetActive(!attack);
        if (!attack)
        {
            p.progressFill.color = color;
            p.progressFill.rectTransform.sizeDelta = new Vector2(PlateWidth * Mathf.Clamp01(score / (float)target), 6f);
        }
    }
}
