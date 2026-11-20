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
        public string killer, victim, icon;
        public int killerTeam, victimTeam;
        public bool local, headshot;
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
    public static void AddKill(string killer, int killerTeam, string victim, int victimTeam, bool localInvolved, string icon = "")
    {
        icon = icon ?? "";
        ReplayLog.KillFeed(killer, killerTeam, victim, victimTeam, localInvolved, icon);   // (i se znackou headshotu - POTG)
        bool headshot = icon.EndsWith(Combat.HeadshotSuffix);
        if (headshot) icon = icon.Substring(0, icon.Length - Combat.HeadshotSuffix.Length);
        kills.Add(new Kill { killer = killer, killerTeam = killerTeam, victim = victim, victimTeam = victimTeam, local = localInvolved, icon = icon, headshot = headshot, time = Time.unscaledTime });
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
        public Image shield;   // overhealth (stit) - zeleny pruh navazujici na zdravi
    }

    class FeedRow
    {
        public Image background;
        public TextMeshProUGUI killer, victim;
        public RawImage icon;
        public RawImage headshot;   // znacka zasahu do hlavy (mezi zbrani a obeti)
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
            var right = new Vector2(1f, 0.5f);
            row.victim = UiKit.MakeText(row.background.transform, "Obet", "", 26, TextAlignmentOptions.Right, right, new Vector2(-12f, 0f), new Vector2(200f, 38f));
            row.victim.textWrappingMode = TextWrappingModes.NoWrap;
            row.killer = UiKit.MakeText(row.background.transform, "Vrah", "", 26, TextAlignmentOptions.Right, right, new Vector2(-260f, 0f), new Vector2(200f, 38f));
            row.killer.textWrappingMode = TextWrappingModes.NoWrap;
            var iconObject = new GameObject("Ikona", typeof(RectTransform), typeof(RawImage));
            iconObject.transform.SetParent(row.background.transform, false);
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = right;
            iconRect.sizeDelta = new Vector2(30f, 30f);
            row.icon = iconObject.GetComponent<RawImage>();
            row.icon.raycastTarget = false;
            var headObject = new GameObject("Headshot", typeof(RectTransform), typeof(RawImage));
            headObject.transform.SetParent(row.background.transform, false);
            var headRect = (RectTransform)headObject.transform;
            headRect.anchorMin = headRect.anchorMax = headRect.pivot = right;
            headRect.sizeDelta = new Vector2(28f, 28f);
            row.headshot = headObject.GetComponent<RawImage>();
            row.headshot.raycastTarget = false;
            row.headshot.texture = HeadshotIcon;
            row.headshot.gameObject.SetActive(false);
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
        // behem zaznamu (play of the game) se ukazuje kill feed ze zaznamu, barvy podle tymu hrace v zaznamu
        UpdateFeed(ReplayPlayer.Active ? ReplayPlayer.PovTeam : localTeam, (playing && !ReplayPlayer.Active) || ReplayPlayer.Active);
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
                if (player == null || player == local || !player.IsSpawned || player.Hero == null || player.IsJoining || player.IsUntargetable || player.HiddenFromLocal) continue;

                var health = player.GetComponent<Health>();
                if (health == null || health.currentHealth.Value <= 0f) continue;

                Vector3 head = player.transform.position + Vector3.up * 2.0f;
                Vector3 screen = camera.WorldToScreenPoint(head);
                if (screen.z <= 0.3f) continue;

                bool ally = TeamOf(player) == localTeam;
                float distance = screen.z;

                // Nepritel jen kdyz je opravdu videt (ne pres zed) a neni moc daleko; spoluhrac vzdy.
                // Nepritel odhaleny pruzkumnym sipem je videt vzdy.
                // Nasazeny prsten ("svet stinu"): nositel vidi nepratele do 80 m i pres zdi.
                bool ringSight = MatchManager.LocalWearsRing && distance <= 80f;
                if (!ally && !player.revealed.Value && !ringSight && (distance > 60f || !Visible(camera.transform.position, player, local))) continue;

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
                float over = Mathf.Clamp01(health.shield.Value / Mathf.Max(1f, health.maxHealth));
                plate.shield.enabled = over > 0.001f;
                plate.shield.rectTransform.anchoredPosition = new Vector2(2f + BarWidth * fraction, 0f);
                plate.shield.rectTransform.sizeDelta = new Vector2(BarWidth * over, 8f);
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
                plate.shield.enabled = false;
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
            plate.shield = UiKit.MakeImage(background.transform, "Overhealth", new Color(0.3f, 0.95f, 0.35f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(0f, 8f));
            plate.shield.enabled = false;
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

            // vrah  [ikona: cim]  obet  (bez vraha jen ikona a obet)
            row.victim.text = Colored(kill.victim, kill.victimTeam == localTeam);
            row.killer.text = string.IsNullOrEmpty(kill.killer) ? "" : Colored(kill.killer, kill.killerTeam == localTeam);
            float victimWidth = Mathf.Min(200f, row.victim.GetPreferredValues(row.victim.text).x);
            // vrah [zbran] [hlava] obet
            float x = -12f - victimWidth - 8f;
            if (row.headshot.gameObject.activeSelf != kill.headshot) row.headshot.gameObject.SetActive(kill.headshot);
            if (kill.headshot)
            {
                ((RectTransform)row.headshot.transform).anchoredPosition = new Vector2(x, 0f);
                row.headshot.color = new Color(1f, 1f, 1f, alpha);
                x -= 28f + 4f;
            }
            var iconRect = (RectTransform)row.icon.transform;
            iconRect.anchoredPosition = new Vector2(x, 0f);
            row.icon.texture = KillIcon(kill.icon);
            ((RectTransform)row.killer.transform).anchoredPosition = new Vector2(x - 30f - 8f, 0f);
            row.victim.alpha = alpha;
            row.killer.alpha = alpha;
            row.icon.color = new Color(1f, 1f, 1f, alpha);

            // Zabiti, ktera se tykaji me, jsou zvyraznena.
            var background = kill.local ? new Color(0.22f, 0.26f, 0.36f, 0.95f) : new Color(0.05f, 0.07f, 0.10f, 0.72f);
            background.a *= alpha;
            row.background.color = background;
        }
    }

    // Znacka headshotu: hlava (kruh) v zamerovaci krizi, cervenooranzova (generovana).
    static Texture2D headshotIcon;
    static Texture2D HeadshotIcon
    {
        get
        {
            if (headshotIcon != null) return headshotIcon;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            float half = (size - 1) * 0.5f;
            var color = new Color(1f, 0.38f, 0.22f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half, dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.52f) / 0.1f) * 1.5f;          // hlava
                    float cross = Mathf.Min(Mathf.Abs(dx), Mathf.Abs(dy));
                    if (r > 0.22f && r < 0.98f) a = Mathf.Max(a, Mathf.Clamp01(1f - cross / 0.07f) * 1.5f);   // kriz (stred prazdny)
                    a = Mathf.Max(a, Mathf.Clamp01(1f - r / 0.1f) * 1.5f);                        // tecka
                    pixels[y * size + x] = new Color(color.r, color.g, color.b, Mathf.Clamp01(a));
                }
            texture.SetPixels(pixels);
            texture.Apply();
            return headshotIcon = texture;
        }
    }

    // Ikona zpusobu zabiti: schopnost (jeji ikona z HUD), zbran nebo prostredi (Resources/Icons/kill_*.png).
    static readonly Dictionary<string, Texture> iconCache = new Dictionary<string, Texture>();

    static Texture KillIcon(string key)
    {
        key = key ?? "";
        if (iconCache.TryGetValue(key, out var cached)) return cached;
        Texture texture = null;
        if (key.StartsWith("ability:"))
        {
            string assetName = key.Substring(8);
            foreach (var hero in HeroRegistry.All)
                foreach (var ability in new[] { hero.ability, hero.secondaryAbility, hero.altAbility, hero.rmbAbility, hero.blockAbility })
                    if (ability != null && ability.name == assetName && ability.icon != null)
                        texture = ability.icon;
        }
        else if (key.StartsWith("weapon:") || key.StartsWith("env:"))
        {
            texture = Resources.Load<Texture2D>("Icons/kill_" + key.Substring(key.IndexOf(':') + 1));
        }
        if (texture == null)
            texture = Resources.Load<Texture2D>("Icons/kill_skull");
        iconCache[key] = texture;
        return texture;
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
        boardTitle.text = match.IsPushMode
            ? $"{gameName}  ·  tlačenice  ·  dotlačeno: tým 0 {match.pushBest0.Value:0} m, tým 1 {match.pushBest1.Value:0} m"
            : match.IsAttackMode
            ? $"{gameName}  ·  útok a obrana  ·  {match.round.Value}. kolo, útočí tým {match.attackTeam.Value}"
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
