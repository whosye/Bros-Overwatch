using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Prubezny zaznam pro "play of the game" (jen u sebe, pro vlastni pohled): 30x za sekundu stav sveta
// (vsichni hraci, vlastni kamera a HUD, obecne zachycene efekty) a mezi tim udalosti z ReplayLog.
// Drzi se poslednich RingSeconds; PotgRecorder si z nej po akci odlozi klip.
public class ReplayCapture : MonoBehaviour
{
    public const float RingSeconds = 14.5f;
    const float FrameInterval = 1f / 30f;
    const int MaxNodes = 64;
    const int MaxObjects = 48;

    public static ReplayCapture Local { get; private set; }

    PlayerHero hero;
    FirstPersonController fpc;
    WeaponShooting shooting;
    Health health;

    readonly List<ReplayFrame> frames = new List<ReplayFrame>();
    readonly List<ReplayEvent> events = new List<ReplayEvent>();
    readonly Dictionary<int, ReplayObjectTemplate> templates = new Dictionary<int, ReplayObjectTemplate>();
    float nextFrame;

    // obecne zachycene efekty
    class Tracked
    {
        public int id;
        public Transform[] nodes;
        public LineRenderer[] lines;
        public Light[] lights;
    }

    readonly Dictionary<GameObject, Tracked> tracked = new Dictionary<GameObject, Tracked>();
    readonly HashSet<GameObject> baseline = new HashSet<GameObject>();
    readonly HashSet<GameObject> rejected = new HashSet<GameObject>();
    readonly List<GameObject> roots = new List<GameObject>();
    static int nextTemplateId = 1;
    float nextPrune;

    // hraci
    readonly List<PlayerHero> players = new List<PlayerHero>();
    float nextPlayerRefresh;
    readonly List<PlayerHero.AbilitySlot> slotBuffer = new List<PlayerHero.AbilitySlot>();

    public bool IsRecording { get; private set; }

    void Awake()
    {
        hero = GetComponent<PlayerHero>();
        fpc = GetComponent<FirstPersonController>();
        shooting = GetComponent<WeaponShooting>();
        health = GetComponent<Health>();
    }

    void OnEnable()
    {
        Local = this;
        MarkBaseline();
    }

    void OnDisable()
    {
        if (Local == this)
            Local = null;
    }

    // Co uz ve scene je (mapa, systemy), se nezaznamenava.
    void MarkBaseline()
    {
        baseline.Clear();
        SceneManager.GetActiveScene().GetRootGameObjects(roots);
        foreach (var root in roots)
            baseline.Add(root);
    }

    bool ShouldRecord()
    {
        var match = MatchManager.Instance;
        return match != null && !match.IsLobby && !match.IsOver && hero != null && hero.IsSpawned && BotBrain.IsLocalHuman(hero)
            && !hero.IsJoining && hero.Hero != null && !ReplayPlayer.Active;
    }

    public void Clear()
    {
        frames.Clear();
        events.Clear();
        templates.Clear();
        tracked.Clear();
        rejected.Clear();
    }

    public void AddEvent(ReplayEvent e)
    {
        if (!IsRecording) return;
        e.time = Time.unscaledTime;
        events.Add(e);
    }

    void LateUpdate()
    {
        IsRecording = ShouldRecord();
        if (!IsRecording || Time.unscaledTime < nextFrame) return;
        nextFrame = Time.unscaledTime + FrameInterval;

        var frame = new ReplayFrame { time = Time.unscaledTime };
        RecordPlayers(frame);
        RecordView(frame);
        RecordObjects(frame);
        frames.Add(frame);

        // Starsi nez RingSeconds pryc.
        float oldest = Time.unscaledTime - RingSeconds;
        int dropFrames = 0;
        while (dropFrames < frames.Count && frames[dropFrames].time < oldest) dropFrames++;
        if (dropFrames > 0) frames.RemoveRange(0, dropFrames);
        int dropEvents = 0;
        while (dropEvents < events.Count && events[dropEvents].time < oldest) dropEvents++;
        if (dropEvents > 0) events.RemoveRange(0, dropEvents);
    }

    // ---------------- hraci ----------------

    void RecordPlayers(ReplayFrame frame)
    {
        if (Time.unscaledTime >= nextPlayerRefresh)
        {
            nextPlayerRefresh = Time.unscaledTime + 1f;
            players.Clear();
            players.AddRange(FindObjectsByType<PlayerHero>());
        }

        var list = new List<ReplayPlayerSnap>();
        foreach (var p in players)
        {
            if (p == null || !p.IsSpawned || p.Hero == null || p.IsJoining) continue;

            var h = p.GetComponent<Health>();
            var team = p.GetComponent<PlayerTeam>();
            var rush = p.GetComponent<RushAbility>();
            var block = p.GetComponent<BlockAbility>();

            byte flags = 0;
            if (h != null && h.currentHealth.Value > 0f) flags |= ReplayPlayerSnap.Alive;
            if (rush != null && rush.enabled && rush.IsSpawned && rush.IsRushing) flags |= ReplayPlayerSnap.Rushing;
            if (block != null && block.enabled && block.IsBlocking) flags |= ReplayPlayerSnap.Blocking;
            if (p.IsBoosted) flags |= ReplayPlayerSnap.Boosted;
            if (p.revealed.Value) flags |= ReplayPlayerSnap.Revealed;

            list.Add(new ReplayPlayerSnap
            {
                id = (int)p.NetworkObjectId,
                hero = (short)p.heroId.Value,
                team = (byte)(team != null ? team.teamId.Value : 0),
                position = p.transform.position,
                yaw = p.transform.eulerAngles.y,
                flags = flags,
                health = h != null ? h.currentHealth.Value : 0f,
                maxHealth = h != null ? h.maxHealth : 100f,
            });
        }
        frame.players = list.ToArray();
    }

    // ---------------- vlastni pohled a HUD ----------------

    void RecordView(ReplayFrame frame)
    {
        var cam = fpc != null ? fpc.playerCamera : null;
        if (cam != null)
        {
            frame.cameraPosition = cam.transform.position;
            frame.cameraRotation = cam.transform.rotation;
            frame.fov = cam.fieldOfView;
        }
        frame.thirdPerson = fpc != null && fpc.ThirdPerson;
        frame.ultCasting = fpc != null && fpc.UltCasting;
        frame.ultWindup = fpc != null ? fpc.UltWindup : 0f;
        var block = GetComponent<BlockAbility>();
        frame.blocking = block != null && block.enabled && block.IsBlocking;
        frame.charge = shooting != null ? shooting.ChargeFraction : 0f;

        frame.hudHero = (short)hero.heroId.Value;
        var team = GetComponent<PlayerTeam>();
        frame.hudTeam = (byte)(team != null ? team.teamId.Value : 0);
        frame.health = health != null ? health.currentHealth.Value : 0f;
        frame.maxHealth = health != null ? health.maxHealth : 100f;
        if (shooting != null)
        {
            frame.ammo = (short)shooting.CurrentAmmo;
            frame.maxAmmo = (short)shooting.MaxAmmo;
            frame.reloading = shooting.IsReloading;
        }

        frame.locked = HudUI.LockFrame >= Time.frameCount - 1;
        frame.lockPoint = HudUI.LockPoint;

        hero.GetAbilitySlots(slotBuffer);
        var slots = new ReplaySlot[slotBuffer.Count];
        for (int i = 0; i < slotBuffer.Count; i++)
        {
            var s = slotBuffer[i];
            slots[i] = new ReplaySlot
            {
                abilitySlot = (byte)Mathf.Max(0, HeroVoice.SlotOf(hero.Hero, s.ability)),
                key = (byte)Mathf.Max(0, System.Array.IndexOf(ReplaySlot.Keys, s.key)),
                remaining = s.remaining,
                active = s.active,
                charge = s.charge,
                count = (byte)Mathf.Clamp(s.count, 0, 255),
                fullOnly = s.fullOnly,
            };
        }
        frame.slots = slots;
    }

    // ---------------- obecne efekty ----------------

    static bool IgnoredByName(string name)
    {
        return name.StartsWith("FX_") || name.StartsWith("SFX_") || name == "Tracer" || name.StartsWith("Projectile")
            || name.StartsWith("Ghost") || name.StartsWith("Replay") || name.StartsWith("SpawnZone") || name.StartsWith("CapturePoint")
            || name == "One shot audio";
    }

    bool Candidate(GameObject root)
    {
        if (baseline.Contains(root) || rejected.Contains(root)) return false;
        if (IgnoredByName(root.name) || root.GetComponent<ReplayIgnore>() != null) { rejected.Add(root); return false; }
        if (root.GetComponentInChildren<NetworkObject>(true) != null || root.GetComponentInChildren<Canvas>(true) != null
            || root.GetComponentInChildren<Camera>(true) != null) { rejected.Add(root); return false; }

        bool visible = root.GetComponentInChildren<MeshRenderer>(true) != null || root.GetComponentInChildren<LineRenderer>(true) != null
            || root.GetComponentInChildren<Light>(true) != null || root.GetComponentInChildren<TextMeshPro>(true) != null;
        return visible;   // (bez vizualu se mozna jeste naplni - zkusi se znovu)
    }

    void RecordObjects(ReplayFrame frame)
    {
        SceneManager.GetActiveScene().GetRootGameObjects(roots);

        // Zrusene objekty pryc (i ze seznamu odmitnutych, at behem dlouheho zapasu neroste).
        if (Time.unscaledTime >= nextPrune)
        {
            nextPrune = Time.unscaledTime + 5f;
            rejected.RemoveWhere(g => g == null);

            // Vzory efektu, na ktere uz zadny snimek neodkazuje (a objekt uz neexistuje), pryc.
            var used = new HashSet<int>();
            foreach (var f in frames)
                foreach (var o in f.objects) used.Add(o.id);
            foreach (var t in tracked.Values) used.Add(t.id);
            var stale = new List<int>();
            foreach (int id in templates.Keys)
                if (!used.Contains(id)) stale.Add(id);
            foreach (int id in stale) templates.Remove(id);
        }
        var dead = new List<GameObject>();
        foreach (var key in tracked.Keys)
            if (key == null) dead.Add(key);
        foreach (var key in dead) tracked.Remove(key);

        foreach (var root in roots)
        {
            if (root == null) continue;

            if (!tracked.TryGetValue(root, out var t))
            {
                if (tracked.Count >= MaxObjects || !Candidate(root)) continue;
                t = Track(root);
                if (t == null) continue;
                tracked[root] = t;
            }
            else if (root.GetComponentsInChildren<Transform>(true).Length != t.nodes.Length)
            {
                // Zmenila se stavba objektu: novy vzor.
                t = Track(root);
                if (t == null) { tracked.Remove(root); continue; }
                tracked[root] = t;
            }

            frame.objects.Add(Capture(t));
        }
    }

    Tracked Track(GameObject root)
    {
        var nodes = root.GetComponentsInChildren<Transform>(true);
        if (nodes.Length > MaxNodes)
        {
            rejected.Add(root);
            return null;
        }

        var t = new Tracked { id = nextTemplateId++, nodes = nodes, lines = new LineRenderer[nodes.Length], lights = new Light[nodes.Length] };
        var template = new ReplayObjectTemplate { id = t.id, nodes = new ReplayNodeTemplate[nodes.Length] };
        for (int i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            var n = new ReplayNodeTemplate { parent = i == 0 ? -1 : System.Array.IndexOf(nodes, node.parent) };

            var filter = node.GetComponent<MeshFilter>();
            var renderer = node.GetComponent<MeshRenderer>();
            if (filter != null && filter.sharedMesh != null && renderer != null && renderer.enabled)
            {
                n.mesh = filter.sharedMesh.name;
                var material = renderer.sharedMaterial;
                if (material != null)
                {
                    n.material = material.name;
                    n.color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
                }
            }

            var line = node.GetComponent<LineRenderer>();
            if (line != null)
            {
                t.lines[i] = line;
                n.hasLine = true;
                n.lineWorld = line.useWorldSpace;
                n.lineStartWidth = line.startWidth;
                n.lineEndWidth = line.endWidth;
                if (line.sharedMaterial != null)
                    n.material = line.sharedMaterial.name;
            }

            var light = node.GetComponent<Light>();
            if (light != null)
            {
                t.lights[i] = light;
                n.hasLight = true;
                n.lightType = (byte)light.type;
                n.lightColor = light.color;
                n.lightRange = light.range;
            }

            var text = node.GetComponent<TextMeshPro>();
            if (text != null)
            {
                n.text = text.text;
                n.textSize = text.fontSize;
                n.textColor = text.color;
            }

            template.nodes[i] = n;
        }

        templates[t.id] = template;
        return t;
    }

    ReplayObjectState Capture(Tracked t)
    {
        var state = ReplayClip.NewState(t.id, t.nodes.Length);
        for (int i = 0; i < t.nodes.Length; i++)
        {
            var node = t.nodes[i];
            if (node == null)
            {
                state.active[i] = false;
                state.rotation[i] = Quaternion.identity;
                state.scale[i] = Vector3.one;
                state.lightIntensity[i] = -1f;
                continue;
            }

            state.active[i] = node.gameObject.activeSelf;
            if (i == 0)
            {
                state.position[i] = node.position;
                state.rotation[i] = node.rotation;
                state.scale[i] = node.lossyScale;
            }
            else
            {
                state.position[i] = node.localPosition;
                state.rotation[i] = node.localRotation;
                state.scale[i] = node.localScale;
            }

            var line = t.lines[i];
            if (line != null)
            {
                var points = new Vector3[line.positionCount];
                line.GetPositions(points);
                state.linePoints[i] = points;
                state.lineStart[i] = line.startColor;
                state.lineEnd[i] = line.endColor;
            }

            state.lightIntensity[i] = t.lights[i] != null ? (t.lights[i].enabled ? t.lights[i].intensity : 0f) : -1f;
        }
        return state;
    }

    // ---------------- vyber klipu ----------------

    // Poslednich 'seconds' sekund jako samostatny klip (casy od zacatku klipu).
    public ReplayClip Extract(float seconds)
    {
        if (frames.Count < 10) return null;

        float end = frames[frames.Count - 1].time;
        float start = Mathf.Max(frames[0].time, end - seconds);

        var clip = new ReplayClip { povId = (int)hero.NetworkObjectId, duration = end - start };
        var used = new HashSet<int>();
        foreach (var f in frames)
        {
            if (f.time < start) continue;
            var copy = (ReplayFrame)f;
            clip.frames.Add(Shift(copy, start));
            foreach (var o in f.objects) used.Add(o.id);
        }
        foreach (var e in events)
        {
            if (e.time < start - 0.05f || e.time > end) continue;
            clip.events.Add(new ReplayEvent
            {
                time = e.time - start, type = e.type, player = e.player, id = e.id, a = e.a, b = e.b,
                p0 = e.p0, p1 = e.p1, p2 = e.p2, color = e.color, f0 = e.f0, f1 = e.f1, f2 = e.f2, flag = e.flag, name = e.name,
            });
        }
        foreach (int id in used)
            if (templates.TryGetValue(id, out var template))
                clip.templates[id] = template;
        return clip;
    }

    static ReplayFrame Shift(ReplayFrame f, float start)
    {
        return new ReplayFrame
        {
            time = f.time - start, players = f.players,
            cameraPosition = f.cameraPosition, cameraRotation = f.cameraRotation, fov = f.fov,
            thirdPerson = f.thirdPerson, ultCasting = f.ultCasting, blocking = f.blocking, ultWindup = f.ultWindup, charge = f.charge,
            hudHero = f.hudHero, hudTeam = f.hudTeam, health = f.health, maxHealth = f.maxHealth, slots = f.slots,
            ammo = f.ammo, maxAmmo = f.maxAmmo, reloading = f.reloading, locked = f.locked, lockPoint = f.lockPoint,
            objects = f.objects,
        };
    }
}

// Udalosti pro zaznam: volaji je efekty, zvuky, zbrane a HUD. Kdyz se zrovna nenahrava (nebo se prehrava), nic.
public static class ReplayLog
{
    public static bool Recording => muted == 0 && ReplayCapture.Local != null && ReplayCapture.Local.IsRecording;

    // Udalost, ktera pri prehrani sama vyvola dalsi (napr. konec projektilu -> vybuch), vnorene udalosti nezapisuje.
    static int muted;

    public struct MuteScope : System.IDisposable
    {
        public void Dispose() { muted--; }
    }

    public static MuteScope Mute()
    {
        muted++;
        return new MuteScope();
    }

    static void Add(ReplayEvent e)
    {
        if (Recording)
            ReplayCapture.Local.AddEvent(e);
    }

    // Hrac, ke kteremu komponenta patri (NetworkObjectId jeho postavy), nebo -1.
    public static int PlayerOf(Component c)
    {
        var hero = c != null ? c.GetComponentInParent<PlayerHero>() : null;
        return hero != null && hero.IsSpawned ? (int)hero.NetworkObjectId : -1;
    }

    public static void Explosion(Vector3 p, float radius) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.Explosion, p0 = p, f0 = radius }); }
    public static void Sparks(Vector3 p, Color c) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.Sparks, p0 = p, color = c }); }
    public static void Impact(Vector3 p, Color c, float scale) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.Impact, p0 = p, color = c, f0 = scale }); }
    public static void Tracer(Vector3 from, Vector3 to, Color c, bool strong) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.Tracer, p0 = from, p1 = to, color = c, flag = strong }); }

    public static void SpatialSound(AudioClip clip, Vector3 p, float volume, float min, float max)
    {
        if (Recording && clip != null)
            Add(new ReplayEvent { type = ReplayEventType.SpatialSound, name = clip.name, p0 = p, f0 = volume, f1 = min, f2 = max });
    }

    public static void GlobalSound(AudioClip clip, float volume)
    {
        if (Recording && clip != null)
            Add(new ReplayEvent { type = ReplayEventType.GlobalSound, name = clip.name, f0 = volume });
    }

    public static void Sfx(AudioClip clip, Vector3 p, float volume)
    {
        if (Recording && clip != null)
            Add(new ReplayEvent { type = ReplayEventType.Sfx, name = clip.name, p0 = p, f0 = volume });
    }

    public static void ProjectileSpawn(int id, Vector3 p, Vector3 v, WeaponDefinition weapon, Vector3 offset)
    {
        if (Recording && weapon != null)
            Add(new ReplayEvent { type = ReplayEventType.ProjectileSpawn, id = id, p0 = p, p1 = v, p2 = offset, name = weapon.name });
    }

    public static void ProjectileEnd(int id, Vector3 p, int kind, WeaponDefinition weapon)
    {
        if (Recording)
            Add(new ReplayEvent { type = ReplayEventType.ProjectileEnd, id = id, p0 = p, a = kind, name = weapon != null ? weapon.name : "" });
    }

    public static void Voice(Component speaker, VoiceKind kind, int slot, int pick)
    {
        if (Recording)
            Add(new ReplayEvent { type = ReplayEventType.Voice, player = PlayerOf(speaker), id = (int)kind, a = slot, b = pick });
    }

    public static void Swing(Component player) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.Swing, player = PlayerOf(player) }); }
    public static void Reload(Component player, float duration) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.Reload, player = PlayerOf(player), f0 = duration }); }
    public static void StopReload(Component player) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.StopReload, player = PlayerOf(player) }); }
    public static void Kick(Component player, float duration) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.Kick, player = PlayerOf(player), f0 = duration }); }
    public static void FirstPersonKick() { if (Recording) Add(new ReplayEvent { type = ReplayEventType.FirstPersonKick }); }

    public static void HudHit(bool kill) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.HudHit, flag = kill }); }
    public static void HudFlash(float seconds) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.HudFlash, f0 = seconds }); }
    public static void HudSleep(float seconds) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.HudSleep, f0 = seconds }); }
    public static void HudTint(Color c, float seconds) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.HudTint, color = c, f0 = seconds }); }
    public static void HudEnd() { if (Recording) Add(new ReplayEvent { type = ReplayEventType.HudEnd }); }
    public static void KillFeed(string killer, int killerTeam, string victim, int victimTeam, bool local, string icon)
    {
        if (Recording) Add(new ReplayEvent { type = ReplayEventType.KillFeed, name = killer + "\n" + victim + "\n" + icon, a = killerTeam, b = victimTeam, flag = local });
    }

    public static void NanoAura(Transform player, bool on) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.NanoAura, player = PlayerOf(player), flag = on }); }
    public static void SleepShow(Transform player, float seconds) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.SleepShow, player = PlayerOf(player), f0 = seconds }); }
    public static void SleepHide(Transform player) { if (Recording) Add(new ReplayEvent { type = ReplayEventType.SleepHide, player = PlayerOf(player) }); }
}
