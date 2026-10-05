using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Unity.Netcode;

// Prekryvne UI behem zapasu (stavi se kodem, viz MatchUI):
//  - jmenovky se zivoty nad hlavami hracu: spoluhraci modre (videt i pres zed), nepratele cervene (jen kdyz jsou videt),
//  - seznam zabiti v pravem hornim rohu,
//  - tabulka hracu pri drzeni Tabulatoru.
public class MatchOverlayUI
{
    public static readonly Color Ally = new Color(0.36f, 0.72f, 1f, 1f);
    public static readonly Color Enemy = new Color(1f, 0.33f, 0.28f, 1f);

    // ---------------- seznam zabiti (plni MatchManager.KillFeedClientRpc) ----------------

    class Kill
    {
        public string killer, victim;
        public int killerTeam, victimTeam;
        public bool local;
        public float time;
    }

    const int MaxKills = 5;
    const float KillLifetime = 6f;
    static readonly List<Kill> kills = new List<Kill>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        kills.Clear();
    }

    // killer muze byt prazdny (smrt bez utocnika).
    public static void AddKill(string killer, int killerTeam, string victim, int victimTeam, bool localInvolved)
    {
        kills.Add(new Kill { killer = killer, killerTeam = killerTeam, victim = victim, victimTeam = victimTeam, local = localInvolved, time = Time.unscaledTime });
        while (kills.Count > MaxKills)
            kills.RemoveAt(0);
    }

    public static void ClearKills()
    {
        kills.Clear();
    }

    // ---------------- prvky ----------------

    class Plate
    {
        public RectTransform root;
        public TextMeshProUGUI name;
        public Image fill;
    }

    class FeedRow
    {
        public Image background;
        public TextMeshProUGUI text;
    }

    const float BarWidth = 110f;

    RectTransform canvasRect;
    RectTransform plateRoot;
    readonly List<Plate> plates = new List<Plate>();
    readonly FeedRow[] feedRows = new FeedRow[MaxKills];
    GameObject feedRoot;

    GameObject board;
    TextMeshProUGUI boardTitle;
    readonly TextMeshProUGUI[] boardLists = new TextMeshProUGUI[PlayerTeam.TeamCount];
    readonly TextMeshProUGUI[] boardHeads = new TextMeshProUGUI[PlayerTeam.TeamCount];
    float nextBoardRefresh;

    readonly List<PlayerHero> players = new List<PlayerHero>();
    float nextPlayerRefresh;

    public void Build(Transform canvas)
    {
        canvasRect = (RectTransform)canvas;
        var center = new Vector2(0.5f, 0.5f);

        var plateObject = new GameObject("Nameplates", typeof(RectTransform));
        plateObject.transform.SetParent(canvas, false);
        plateRoot = (RectTransform)plateObject.transform;
        UiKit.Stretch(plateRoot);

        // Seznam zabiti vpravo nahore.
        var feedImage = UiKit.MakeImage(canvas, "KillFeed", new Color(0f, 0f, 0f, 0f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(560f, 240f));
        feedRoot = feedImage.gameObject;
        for (int i = 0; i < MaxKills; i++)
        {
            var row = new FeedRow();
            row.background = UiKit.MakeImage(feedRoot.transform, "Row", new Color(0.05f, 0.07f, 0.10f, 0.72f), new Vector2(1f, 1f),
                new Vector2(0f, -i * 44f), new Vector2(460f, 38f));
            row.text = UiKit.MakeText(row.background.transform, "Text", "", 26, TextAlignmentOptions.Right, center, new Vector2(-12f, 0f), new Vector2(440f, 38f));
            row.text.textWrappingMode = TextWrappingModes.NoWrap;
            row.background.gameObject.SetActive(false);
            feedRows[i] = row;
        }

        // Tabulka hracu (Tab).
        var boardImage = UiKit.MakeImage(canvas, "Scoreboard", new Color(0.03f, 0.04f, 0.07f, 0.90f), center, Vector2.zero, new Vector2(1500f, 640f));
        board = boardImage.gameObject;
        boardTitle = UiKit.MakeText(board.transform, "Title", "", 40, TextAlignmentOptions.Center, center, new Vector2(0f, 270f), new Vector2(1400f, 56f), UiKit.Accent);
        for (int team = 0; team < PlayerTeam.TeamCount; team++)
        {
            float x = team == 0 ? -370f : 370f;
            boardHeads[team] = UiKit.MakeText(board.transform, "Head", "", 34, TextAlignmentOptions.Left, center, new Vector2(x, 195f), new Vector2(700f, 50f), UiKit.TeamColor(team));
            var columns = UiKit.MakeText(board.transform, "Columns", "<pos=0%>HRÁČ<pos=40%>HRDINA<pos=63%>ZABITÍ<pos=76%>SMRTI<pos=88%>LÉČENÍ", 20, TextAlignmentOptions.Left, center,
                new Vector2(x, 150f), new Vector2(700f, 30f), UiKit.Muted);
            columns.textWrappingMode = TextWrappingModes.NoWrap;
            boardLists[team] = UiKit.MakeText(board.transform, "List", "", 28, TextAlignmentOptions.TopLeft, center, new Vector2(x, -60f), new Vector2(700f, 380f));
            boardLists[team].textWrappingMode = TextWrappingModes.NoWrap;
        }
        board.SetActive(false);
    }

    // Tabulka ma byt videt i nad panelem konce zapasu.
    public void PlaceBoardAbove(Transform panel)
    {
        if (board != null && panel != null)
            board.transform.SetSiblingIndex(panel.GetSiblingIndex() + 1);
    }

    public void Tick(PlayerHero local, bool playing, MatchManager match, bool alwaysShowBoard = false)
    {
        if (canvasRect == null) return;

        if (Time.unscaledTime >= nextPlayerRefresh)
        {
            nextPlayerRefresh = Time.unscaledTime + 0.5f;
            players.Clear();
            players.AddRange(Object.FindObjectsByType<PlayerHero>());
        }

        int localTeam = TeamOf(local);
        if (ReplayPlayer.Active)
            UpdateReplayPlates();
        else
            UpdatePlates(local, localTeam, playing);
        UpdateFeed(localTeam, playing && !ReplayPlayer.Active);
        UpdateBoard(local, playing, match, alwaysShowBoard);
    }

    static int TeamOf(PlayerHero hero)
    {
        var team = hero != null ? hero.GetComponent<PlayerTeam>() : null;
        return team != null ? team.teamId.Value : -1;
    }

    // ---------------- jmenovky ----------------

    void UpdatePlates(PlayerHero local, int localTeam, bool playing)
    {
        int used = 0;
        var fpc = local != null ? local.GetComponent<FirstPersonController>() : null;
        var camera = fpc != null ? fpc.playerCamera : null;

        if (playing && camera != null)
        {
            foreach (var player in players)
            {
                if (player == null || player == local || !player.IsSpawned || player.Hero == null || player.IsJoining) continue;

                var health = player.GetComponent<Health>();
                if (health == null || health.currentHealth.Value <= 0f) continue;

                Vector3 head = player.transform.position + Vector3.up * 2.0f;
                Vector3 screen = camera.WorldToScreenPoint(head);
                if (screen.z <= 0.3f) continue;

                bool ally = TeamOf(player) == localTeam;
                float distance = screen.z;

                // Nepritel jen kdyz je opravdu videt (ne pres zed) a neni moc daleko; spoluhrac vzdy.
                // Nepritel odhaleny pruzkumnym sipem je videt vzdy.
                if (!ally && !player.revealed.Value && (distance > 60f || !Visible(camera.transform.position, player, local))) continue;

                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(plateRoot, screen, null, out Vector2 point)) continue;

                var plate = GetPlate(used++);
                plate.root.anchoredPosition = point;
                plate.root.localScale = Vector3.one * Mathf.Clamp(14f / Mathf.Max(1f, distance), 0.55f, 1f);

                var color = ally ? Ally : Enemy;
                plate.name.text = player.DisplayName;
                plate.name.color = color;
                plate.fill.color = color;
                float fraction = Mathf.Clamp01(health.currentHealth.Value / Mathf.Max(1f, health.maxHealth));
                plate.fill.rectTransform.sizeDelta = new Vector2(BarWidth * fraction, 8f);
            }
        }

        for (int i = 0; i < plates.Count; i++)
        {
            bool active = i < used;
            if (plates[i].root.gameObject.activeSelf != active)
                plates[i].root.gameObject.SetActive(active);
        }
    }

    // Prehravani "play of the game": jmenovky nad duchy z pohledu hrace, ktery akci predvedl (stejna pravidla jako v zapase).
    readonly List<ReplayPlayer.Plate> replayPlates = new List<ReplayPlayer.Plate>();

    void UpdateReplayPlates()
    {
        int used = 0;
        var camera = ReplayPlayer.Camera;
        int povTeam = ReplayPlayer.PovTeam;
        ReplayPlayer.GetPlates(replayPlates);

        if (camera != null)
            foreach (var p in replayPlates)
            {
                Vector3 screen = camera.WorldToScreenPoint(p.head);
                if (screen.z <= 0.3f) continue;

                bool ally = p.team == povTeam;
                float distance = screen.z;
                if (!ally && !p.revealed && (distance > 60f || !ReplayVisible(camera.transform.position, p.head - Vector3.up * 0.8f))) continue;

                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(plateRoot, screen, null, out Vector2 point)) continue;

                var plate = GetPlate(used++);
                plate.root.anchoredPosition = point;
                plate.root.localScale = Vector3.one * Mathf.Clamp(14f / Mathf.Max(1f, distance), 0.55f, 1f);

                var color = ally ? Ally : Enemy;
                plate.name.text = p.name;
                plate.name.color = color;
                plate.fill.color = color;
                plate.fill.rectTransform.sizeDelta = new Vector2(BarWidth * p.health01, 8f);
            }

        for (int i = 0; i < plates.Count; i++)
        {
            bool active = i < used;
            if (plates[i].root.gameObject.activeSelf != active)
                plates[i].root.gameObject.SetActive(active);
        }
    }

    // Prima viditelnost v prehravani: duchove nemaji kolize, zivi (skryti) hraci se nepocitaji.
    static bool ReplayVisible(Vector3 eye, Vector3 chest)
    {
        Vector3 direction = chest - eye;
        float distance = direction.magnitude;
        if (distance < 0.5f) return true;

        foreach (var hit in Physics.RaycastAll(eye, direction / distance, distance, ~0, QueryTriggerInteraction.Ignore))
            if (hit.collider.GetComponentInParent<NetworkObject>() == null)
                return false;
        return true;
    }

    static bool Visible(Vector3 eye, PlayerHero target, PlayerHero local)
    {
        Vector3 chest = target.transform.position + Vector3.up * 1.2f;
        Vector3 direction = chest - eye;
        float distance = direction.magnitude;
        if (distance < 0.5f) return true;

        var hits = Physics.RaycastAll(eye, direction / distance, distance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && local != null && owner == local.NetworkObject) continue;

            return owner != null && owner == target.NetworkObject;
        }

        return true;
    }

    Plate GetPlate(int index)
    {
        while (plates.Count <= index)
        {
            var plate = new Plate();
            var go = new GameObject("Plate", typeof(RectTransform));
            go.transform.SetParent(plateRoot, false);
            plate.root = (RectTransform)go.transform;
            plate.root.anchorMin = plate.root.anchorMax = new Vector2(0.5f, 0.5f);
            plate.root.pivot = new Vector2(0.5f, 0f);
            plate.root.sizeDelta = new Vector2(240f, 44f);

            var bottom = new Vector2(0.5f, 0f);
            plate.name = UiKit.MakeText(plate.root, "Name", "", 22, TextAlignmentOptions.Bottom, bottom, new Vector2(0f, 16f), new Vector2(300f, 30f));
            plate.name.textWrappingMode = TextWrappingModes.NoWrap;
            plate.name.fontStyle = FontStyles.Bold;

            var background = UiKit.MakeImage(plate.root, "Bar", new Color(0f, 0f, 0f, 0.65f), bottom, new Vector2(0f, 0f), new Vector2(BarWidth + 4f, 12f));
            plate.fill = UiKit.MakeImage(background.transform, "Fill", Color.white, new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(BarWidth, 8f));
            plates.Add(plate);
        }

        return plates[index];
    }

    // ---------------- seznam zabiti ----------------

    void UpdateFeed(int localTeam, bool playing)
    {
        if (feedRoot.activeSelf != playing)
            feedRoot.SetActive(playing);
        if (!playing) return;

        kills.RemoveAll(k => Time.unscaledTime - k.time > KillLifetime);

        for (int i = 0; i < MaxKills; i++)
        {
            var row = feedRows[i];
            // Nejnovejsi nahore.
            int index = kills.Count - 1 - i;
            bool used = index >= 0;
            if (row.background.gameObject.activeSelf != used)
                row.background.gameObject.SetActive(used);
            if (!used) continue;

            var kill = kills[index];
            float alpha = Mathf.Clamp01(KillLifetime - (Time.unscaledTime - kill.time));

            string victim = Colored(kill.victim, kill.victimTeam == localTeam);
            row.text.text = string.IsNullOrEmpty(kill.killer)
                ? $"{victim}  zemřel"
                : $"{Colored(kill.killer, kill.killerTeam == localTeam)}  <color=#FFFFFF>»</color>  {victim}";
            row.text.alpha = alpha;

            // Zabiti, ktera se tykaji me, jsou zvyraznena.
            var background = kill.local ? new Color(0.22f, 0.26f, 0.36f, 0.95f) : new Color(0.05f, 0.07f, 0.10f, 0.72f);
            background.a *= alpha;
            row.background.color = background;
        }
    }

    static string Colored(string name, bool ally)
    {
        return $"<color=#{ColorUtility.ToHtmlStringRGB(ally ? Ally : Enemy)}><b>{name}</b></color>";
    }

    // ---------------- tabulka hracu ----------------

    void UpdateBoard(PlayerHero local, bool playing, MatchManager match, bool always)
    {
        bool show = playing && match != null && (always || (Keyboard.current != null && Keyboard.current.tabKey.isPressed));
        if (board.activeSelf != show)
        {
            board.SetActive(show);
            nextBoardRefresh = 0f;
        }

        if (!show || Time.unscaledTime < nextBoardRefresh) return;
        nextBoardRefresh = Time.unscaledTime + 0.2f;

        string gameName = match.gameName.Value.Length > 0 ? match.gameName.Value.ToString() : "Zápas";
        boardTitle.text = match.IsCapture
            ? $"{gameName}  ·  dobývání bodů ({MatchManager.CapturePointsToWin} ze {MatchManager.CapturePointCount})"
            : $"{gameName}  ·  do {match.scoreToWinSynced.Value} zabití";

        var sorted = new List<PlayerHero>();
        foreach (var player in players)
            if (player != null && player.IsSpawned)
                sorted.Add(player);
        sorted.Sort((a, b) => b.kills.Value != a.kills.Value ? b.kills.Value.CompareTo(a.kills.Value) : string.CompareOrdinal(a.DisplayName, b.DisplayName));

        for (int team = 0; team < PlayerTeam.TeamCount; team++)
        {
            int score = team == 0 ? match.team0Score.Value : match.team1Score.Value;
            boardHeads[team].text = $"TÝM {team}   <color=#FFFFFF>{score}</color>";

            var text = new StringBuilder();
            foreach (var player in sorted)
            {
                if (Mathf.Clamp(TeamOf(player), 0, PlayerTeam.TeamCount - 1) != team) continue;

                string name = player == local ? $"<b><color=#F28C1A>{player.DisplayName}</color></b>" : player.DisplayName;
                string hero = player.Hero != null ? player.Hero.heroName : "…";
                if (player.IsJoining) hero = "vybírá…";
                text.AppendLine($"<pos=0%>{name}<pos=40%><color=#A6B3C7>{hero}</color><pos=66%>{player.kills.Value}<pos=79%>{player.deaths.Value}<pos=89%><color=#7CE08A>{Mathf.RoundToInt(player.healing.Value)}</color>");
            }

            boardLists[team].text = text.ToString();
        }
    }
}
