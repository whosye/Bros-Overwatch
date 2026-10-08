using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Prehravani "play of the game" primo ve hre: z ReplayClip postavi "duchy" hracu (model, zbrane, hlas), znovu
// vytvori zachycene efekty a v case spousti udalosti (vystrely, projektily, zvuky, hlasky, animace, HUD).
// Obraz kresli vlastni kamera z pohledu hrace, ktery akci predvedl - v plnem rozliseni a s jeho HUD.
// Zivi hraci jsou behem prehravani skryti (CharacterVisual / HeldWeapons se ridi ReplayPlayer.Active).
public class ReplayPlayer : MonoBehaviour
{
    static ReplayPlayer instance;

    public static bool Active => instance != null && instance.clip != null;
    // Killcam: prehrava se z pohledu vraha (clip.povId) ze zaznamu obeti - kamera z jeho polohy smerem k obeti.
    public static bool IsKillcam => Active && instance.killcamVictim >= 0;
    public static bool Finished => instance == null || instance.clip == null || instance.time >= instance.clip.duration;
    public static ReplayFrame Frame => instance != null ? instance.current : null;
    public static Camera Camera => instance != null ? instance.view : null;

    // Jmenovka nad hlavou hrace v prehravani (stejne jako v zapase).
    public struct Plate
    {
        public Vector3 head;
        public string name;
        public int team;
        public float health01;
        public bool revealed;
    }

    // Jmenovky ostatnich hracu (ne toho, z jehoz pohledu se prehrava) v aktualnim okamziku; tym = jeho tym.
    public static int PovTeam => instance != null && instance.current != null
        ? (instance.killcamVictim >= 0 ? instance.TeamOf(instance.clip.povId) : instance.current.hudTeam) : -1;

    int TeamOf(int id)
    {
        if (current != null)
            foreach (var p in current.players)
                if (p.id == id) return p.team;
        return -1;
    }

    int killcamVictim = -1;

    public static void GetPlates(List<Plate> plates)
    {
        plates.Clear();
        if (instance == null || instance.clip == null || instance.current == null) return;

        foreach (var p in instance.current.players)
        {
            if (p.id == instance.clip.povId || (p.flags & ReplayPlayerSnap.Alive) == 0) continue;
            if (!instance.ghosts.TryGetValue(p.id, out var g) || g.go == null || !g.go.activeSelf) continue;

            plates.Add(new Plate
            {
                head = g.go.transform.position + Vector3.up * 2.0f,
                name = instance.NameOf(p.id),
                team = p.team,
                health01 = p.maxHealth > 0f ? Mathf.Clamp01(p.health / p.maxHealth) : 1f,
                revealed = (p.flags & ReplayPlayerSnap.Revealed) != 0,
            });
        }
    }

    readonly Dictionary<int, string> names = new Dictionary<int, string>();

    // Jmeno hrace podle jeho id (hraci jsou na konci zapasu porad pripojeni).
    string NameOf(int id)
    {
        if (names.TryGetValue(id, out var name)) return name;

        name = $"Hráč {id}";
        foreach (var hero in FindObjectsByType<PlayerHero>())
            if (hero.IsSpawned && (int)hero.NetworkObjectId == id)
            {
                name = hero.DisplayName;
                break;
            }
        names[id] = name;
        return name;
    }

    ReplayClip clip;
    float time;
    int nextEvent;
    ReplayFrame current;

    Camera view;
    readonly List<Behaviour> disabledLive = new List<Behaviour>();

    class Ghost
    {
        public GameObject go;
        public CharacterVisual visual;
        public HeldWeapons held;
        public HeroVoice voice;
        public int hero = -1;
        public ParticleSystem rushAura;
    }

    readonly Dictionary<int, Ghost> ghosts = new Dictionary<int, Ghost>();
    readonly Dictionary<int, GameObject[]> objects = new Dictionary<int, GameObject[]>();
    readonly Dictionary<string, AudioClip> sounds = new Dictionary<string, AudioClip>();
    readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
    readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    float feetOffset = -1f;

    const int ProjectileIdOffset = 1000000;

    // killcamVictim >= 0: killcam (pohled vraha = clip.povId, kamera miri na tuto obet)
    public static void Play(ReplayClip replay, int killcamVictim = -1)
    {
        if (replay == null || replay.frames.Count < 2) return;
        if (instance == null)
            instance = new GameObject("ReplayPlayer").AddComponent<ReplayPlayer>();
        instance.Begin(replay, killcamVictim);
    }

    public static void Stop()
    {
        if (instance != null)
            instance.End();
    }

    // ---------------- zacatek a konec ----------------

    void Begin(ReplayClip replay, int killcam = -1)
    {
        MatchOverlayUI.ClearKills();
        End();
        killcamVictim = killcam;
        clip = replay;
        time = 0f;
        nextEvent = 0;
        current = clip.frames[0];

        PrepareLookups();

        // Vlastni kamera (kopie nastaveni hracovy kamery), zive kamery a posluchace vypnout.
        Camera live = null;
        foreach (var cam in FindObjectsByType<Camera>())
            if (cam.enabled && cam.gameObject.activeInHierarchy && cam.targetTexture == null)
            {
                // (kamera menu nic ze sceny nekresli - jeji nastaveni se nekopiruje)
                if (cam.GetComponent<MenuCamera>() == null && (live == null || cam == Camera.main)) live = cam;
                cam.enabled = false;
                disabledLive.Add(cam);
            }
        foreach (var listener in FindObjectsByType<AudioListener>())
            if (listener.enabled)
            {
                listener.enabled = false;
                disabledLive.Add(listener);
            }

        var camObject = new GameObject("ReplayCamera");
        camObject.tag = "MainCamera";   // efekty natacene ke kamere (Zzz, jmenovky) ji pak najdou
        view = camObject.AddComponent<Camera>();
        if (live != null)
        {
            view.clearFlags = live.clearFlags;
            view.backgroundColor = live.backgroundColor;
            view.cullingMask = live.cullingMask;
            view.nearClipPlane = live.nearClipPlane;
            view.farClipPlane = live.farClipPlane;
            var liveData = live.GetUniversalAdditionalCameraData();
            var data = view.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = liveData.renderPostProcessing;
            data.antialiasing = liveData.antialiasing;
        }
        camObject.AddComponent<AudioListener>();

        // Kde ma postava chodidla vuci stredu hrace (stejne jako u zivych hracu).
        foreach (var player in FindObjectsByType<FirstPersonController>())
        {
            var controller = player.GetComponent<CharacterController>();
            if (controller != null)
            {
                feetOffset = controller.center.y - player.standHeight * 0.5f;
                break;
            }
        }

        Apply(0f);
    }

    void End()
    {
        foreach (var g in ghosts.Values)
            if (g.go != null) Destroy(g.go);
        ghosts.Clear();

        foreach (var nodes in objects.Values)
            if (nodes.Length > 0 && nodes[0] != null) Destroy(nodes[0]);
        objects.Clear();

        if (view != null) Destroy(view.gameObject);
        view = null;

        foreach (var b in disabledLive)
            if (b != null) b.enabled = true;
        disabledLive.Clear();

        clip = null;
        current = null;
        killcamVictim = -1;
        names.Clear();
    }

    void OnDestroy()
    {
        End();
        if (instance == this) instance = null;
    }

    // ---------------- prehravani ----------------

    void Update()
    {
        if (clip == null) return;

        time += Time.unscaledDeltaTime;
        Apply(time);
    }

    void Apply(float t)
    {
        // Snimky kolem casu t (interpolace pro plynuly pohyb pri libovolnem FPS).
        var frames = clip.frames;
        int i = 0;
        while (i < frames.Count - 2 && frames[i + 1].time <= t) i++;
        var a = frames[i];
        var b = frames[Mathf.Min(i + 1, frames.Count - 1)];
        float k = b.time > a.time ? Mathf.Clamp01((t - a.time) / (b.time - a.time)) : 0f;
        current = a;

        ApplyPlayers(a, b, k);
        ApplyView(a, b, k);
        ApplyObjects(a, b, k);

        while (nextEvent < clip.events.Count && clip.events[nextEvent].time <= t)
            Fire(clip.events[nextEvent++]);
    }

    void ApplyPlayers(ReplayFrame a, ReplayFrame b, float k)
    {
        var seen = new HashSet<int>();
        foreach (var p in a.players)
        {
            seen.Add(p.id);
            var g = GetGhost(p.id, p.hero);
            if (g == null) continue;

            Vector3 position = p.position;
            float yaw = p.yaw;
            foreach (var q in b.players)
                if (q.id == p.id)
                {
                    if ((q.position - p.position).sqrMagnitude < 9f)
                    {
                        position = Vector3.Lerp(p.position, q.position, k);
                        yaw = Mathf.LerpAngle(p.yaw, q.yaw, k);
                    }
                    break;
                }

            g.go.SetActive(true);
            g.go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            bool pov = p.id == clip.povId;
            bool alive = (p.flags & ReplayPlayerSnap.Alive) != 0;
            // (killcam: vrah vzdy z prvni osoby; HUD a stav schopnosti v zaznamu patri obeti)
            bool povThird = killcamVictim < 0 && a.thirdPerson;
            g.visual.GhostDead = !alive;
            g.visual.GhostHidden = pov && !povThird;
            g.visual.GhostFrenzy = (p.flags & ReplayPlayerSnap.Rushing) != 0;
            g.visual.GhostBlocking = (p.flags & ReplayPlayerSnap.Blocking) != 0;

            g.held.GhostDead = !alive;
            g.held.GhostThirdPerson = pov && povThird;
            g.held.GhostBlocking = g.visual.GhostBlocking;
            if (pov && killcamVictim < 0)
            {
                g.held.GhostUltCasting = a.ultCasting;
                g.held.GhostUltWindup = a.ultWindup;
                g.held.GhostCharge = a.charge;
            }

            bool rushing = g.visual.GhostFrenzy && alive;
            if (rushing && g.rushAura == null)
                g.rushAura = Fx.CreateAura(g.go.transform, new Color(0.35f, 0.6f, 1f));
            if (g.rushAura != null)
            {
                if (rushing && !g.rushAura.isPlaying) g.rushAura.Play();
                if (!rushing && g.rushAura.isPlaying) g.rushAura.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        foreach (var pair in ghosts)
            if (!seen.Contains(pair.Key) && pair.Value.go != null && pair.Value.go.activeSelf)
                pair.Value.go.SetActive(false);
    }

    Ghost GetGhost(int id, int heroIndex)
    {
        var definition = HeroRegistry.Get(heroIndex);
        if (definition == null) return null;

        if (!ghosts.TryGetValue(id, out var g))
        {
            var go = new GameObject("Ghost_" + id);
            g = new Ghost { go = go };
            g.visual = go.AddComponent<CharacterVisual>();
            g.visual.IsGhost = true;
            g.held = go.AddComponent<HeldWeapons>();
            g.voice = go.AddComponent<HeroVoice>();
            ghosts[id] = g;
        }

        if (g.hero != heroIndex)
        {
            // (i zmena hrdiny behem klipu)
            g.hero = heroIndex;
            g.visual.SetFeetOffset(feetOffset);
            g.visual.SetModel(definition.characterPrefab, definition.tintCharacter ? definition.color : Color.white);
            g.held.SetGhost(definition, id == clip.povId, view != null ? view.transform : null);
        }
        return g;
    }

    void ApplyView(ReplayFrame a, ReplayFrame b, float k)
    {
        if (view == null) return;
        if (killcamVictim >= 0)
        {
            KillcamView(a, b, k);
            return;
        }
        view.transform.SetPositionAndRotation(
            (a.cameraPosition - b.cameraPosition).sqrMagnitude < 9f ? Vector3.Lerp(a.cameraPosition, b.cameraPosition, k) : a.cameraPosition,
            Quaternion.Slerp(a.cameraRotation, b.cameraRotation, k));
        view.fieldOfView = Mathf.Lerp(a.fov, b.fov, k) > 1f ? Mathf.Lerp(a.fov, b.fov, k) : 60f;
    }

    // Killcam: oci vraha (poloha a natoceni ze snimku), pohled nahoru/dolu smerem k obeti.
    const float EyeHeight = 1.62f;
    float killcamPitch;

    void KillcamView(ReplayFrame a, ReplayFrame b, float k)
    {
        if (!Snap(a, b, k, clip.povId, out Vector3 killer, out float yaw)) return;
        Vector3 eye = killer + Vector3.up * EyeHeight;
        float pitch = 0f;
        if (Snap(a, b, k, killcamVictim, out Vector3 victim, out _))
        {
            Vector3 to = victim + Vector3.up * 1.2f - eye;
            float flat = new Vector2(to.x, to.z).magnitude;
            pitch = Mathf.Clamp(-Mathf.Atan2(to.y, Mathf.Max(0.01f, flat)) * Mathf.Rad2Deg, -70f, 70f);
        }
        killcamPitch = Mathf.LerpAngle(killcamPitch, pitch, Mathf.Clamp01(Time.unscaledDeltaTime * 8f));
        view.transform.SetPositionAndRotation(eye, Quaternion.Euler(killcamPitch, yaw, 0f));
        view.fieldOfView = 75f;
    }

    static bool Snap(ReplayFrame a, ReplayFrame b, float k, int id, out Vector3 position, out float yaw)
    {
        position = Vector3.zero;
        yaw = 0f;
        bool found = false;
        foreach (var p in a.players)
            if (p.id == id) { position = p.position; yaw = p.yaw; found = true; break; }
        if (!found) return false;
        foreach (var q in b.players)
            if (q.id == id && (q.position - position).sqrMagnitude < 9f)
            {
                position = Vector3.Lerp(position, q.position, k);
                yaw = Mathf.LerpAngle(yaw, q.yaw, k);
                break;
            }
        return true;
    }

    // ---------------- zachycene efekty ----------------

    void ApplyObjects(ReplayFrame a, ReplayFrame b, float k)
    {
        var seen = new HashSet<int>();
        foreach (var state in a.objects)
        {
            if (!clip.templates.TryGetValue(state.id, out var template)) continue;
            seen.Add(state.id);

            if (!objects.TryGetValue(state.id, out var nodes))
            {
                nodes = Build(template);
                objects[state.id] = nodes;
            }

            ReplayObjectState next = null;
            foreach (var s in b.objects)
                if (s.id == state.id) { next = s; break; }

            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node == null) continue;
                if (node.activeSelf != state.active[i]) node.SetActive(state.active[i]);

                Vector3 pos = state.position[i];
                Quaternion rot = state.rotation[i];
                Vector3 scale = state.scale[i];
                if (next != null)
                {
                    pos = Vector3.Lerp(pos, next.position[i], k);
                    rot = Quaternion.Slerp(rot, next.rotation[i], k);
                    scale = Vector3.Lerp(scale, next.scale[i], k);
                }

                if (i == 0)
                {
                    node.transform.SetPositionAndRotation(pos, rot);
                    node.transform.localScale = scale;
                }
                else
                {
                    node.transform.localPosition = pos;
                    node.transform.localRotation = rot;
                    node.transform.localScale = scale;
                }

                var points = state.linePoints[i];
                if (points != null)
                {
                    var line = node.GetComponent<LineRenderer>();
                    if (line != null)
                    {
                        line.positionCount = points.Length;
                        line.SetPositions(points);
                        line.startColor = state.lineStart[i];
                        line.endColor = state.lineEnd[i];
                    }
                }

                if (state.lightIntensity[i] >= 0f)
                {
                    var light = node.GetComponent<Light>();
                    if (light != null) light.intensity = state.lightIntensity[i];
                }
            }
        }

        var gone = new List<int>();
        foreach (var pair in objects)
            if (!seen.Contains(pair.Key)) gone.Add(pair.Key);
        foreach (int id in gone)
        {
            var nodes = objects[id];
            if (nodes.Length > 0 && nodes[0] != null) Destroy(nodes[0]);
            objects.Remove(id);
        }
    }

    GameObject[] Build(ReplayObjectTemplate template)
    {
        var nodes = new GameObject[template.nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            var n = template.nodes[i];
            var go = new GameObject(i == 0 ? "ReplayObject" : "Node");
            if (n.parent >= 0 && n.parent < i && nodes[n.parent] != null)
                go.transform.SetParent(nodes[n.parent].transform, false);
            nodes[i] = go;

            if (!string.IsNullOrEmpty(n.mesh))
            {
                var mesh = FindMesh(n.mesh);
                if (mesh != null)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = FindMaterial(n.material, n.color);
                }
            }

            if (n.hasLine)
            {
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = n.lineWorld;
                line.startWidth = n.lineStartWidth;
                line.endWidth = n.lineEndWidth;
                line.sharedMaterial = FindMaterial(n.material, Color.white, Fx.ParticleMaterial);
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            if (n.hasLight)
            {
                var light = go.AddComponent<Light>();
                light.type = (LightType)n.lightType;
                light.color = n.lightColor;
                light.range = n.lightRange;
                light.shadows = LightShadows.None;
            }

            if (n.text != null)
            {
                var text = go.AddComponent<TextMeshPro>();
                text.text = n.text;
                text.fontSize = n.textSize;
                text.color = n.textColor;
                text.alignment = TextAlignmentOptions.Center;
            }
        }
        return nodes;
    }

    // ---------------- udalosti ----------------

    void Fire(ReplayEvent e)
    {
        ghosts.TryGetValue(e.player, out var g);

        switch (e.type)
        {
            case ReplayEventType.Explosion: Fx.Explosion(e.p0, e.f0); break;
            case ReplayEventType.Sparks: Fx.Sparks(e.p0, e.color); break;
            case ReplayEventType.Impact: Fx.BulletImpact(e.p0, e.color, e.f0); break;
            case ReplayEventType.Tracer: Fx.Tracer(e.p0, e.p1, e.color, e.flag); break;
            case ReplayEventType.SpatialSound: Fx.PlaySpatial(FindSound(e.name), e.p0, e.f0, e.f1, e.f2); break;
            case ReplayEventType.GlobalSound: Fx.PlayGlobal(FindSound(e.name), e.f0); break;
            case ReplayEventType.Sfx: ProceduralSfx.Play(FindSound(e.name), e.p0, e.f0); break;
            case ReplayEventType.ProjectileSpawn:
                var weapon = FindWeapon(e.name);
                if (weapon != null) ProjectileVisual.Spawn(e.id + ProjectileIdOffset, e.p0, e.p1, weapon, e.p2);
                break;
            case ReplayEventType.ProjectileEnd:
                ProjectileVisual.End(e.id + ProjectileIdOffset, e.p0, e.a, FindWeapon(e.name));
                break;
            case ReplayEventType.Voice:
                if (g != null && g.voice != null) g.voice.Play(HeroRegistry.Get(g.hero), (VoiceKind)e.id, e.a, e.b);
                break;
            case ReplayEventType.Swing: if (g != null) g.held.Swing(); break;
            case ReplayEventType.Reload: if (g != null) g.held.PlayReload(e.f0); break;
            case ReplayEventType.StopReload: if (g != null) g.held.StopReload(); break;
            case ReplayEventType.Kick: if (g != null) g.visual.PlayKick(e.f0); break;
            case ReplayEventType.FirstPersonKick: if (view != null) FirstPersonKick.Play(view); break;
            case ReplayEventType.HudHit: HudUI.NotifyHit(e.flag); break;
            case ReplayEventType.HudFlash: HudUI.NotifyFlash(e.f0); break;
            case ReplayEventType.HudSleep: HudUI.NotifySleep(e.f0); break;
            case ReplayEventType.HudTint: HudUI.NotifyTint(e.color, e.f0); break;
            case ReplayEventType.HudEnd: HudUI.EndOverlay(); break;
            case ReplayEventType.KillFeed:
                var names = (e.name ?? "").Split('\n');
                MatchOverlayUI.AddKill(names[0], e.a, names.Length > 1 ? names[1] : "", e.b, e.flag, names.Length > 2 ? names[2] : "");
                break;
            case ReplayEventType.NanoAura: if (g != null) NanoAura.Set(g.go.transform, e.flag); break;
            case ReplayEventType.SleepShow: if (g != null) SleepMarker.Show(g.go.transform, e.f0); break;
            case ReplayEventType.SleepHide: if (g != null) SleepMarker.Hide(g.go.transform); break;
        }
    }

    // ---------------- hledani assetu podle jmena ----------------

    void PrepareLookups()
    {
        // Generovane zvuky vzniknou az pri prvnim pouziti - vytvorit vsechny.
        foreach (var property in typeof(ProceduralSfx).GetProperties(BindingFlags.Public | BindingFlags.Static))
            if (property.PropertyType == typeof(AudioClip))
            {
                var clipValue = property.GetValue(null) as AudioClip;
                if (clipValue != null) sounds[clipValue.name] = clipValue;
            }

        foreach (var audio in Resources.FindObjectsOfTypeAll<AudioClip>())
            if (audio != null && !sounds.ContainsKey(audio.name))
                sounds[audio.name] = audio;
    }

    AudioClip FindSound(string name)
    {
        return !string.IsNullOrEmpty(name) && sounds.TryGetValue(name, out var clipValue) ? clipValue : null;
    }

    static WeaponDefinition FindWeapon(string name)
    {
        foreach (var hero in HeroRegistry.All)
            if (hero != null && hero.weapon != null && hero.weapon.name == name)
                return hero.weapon;
        return null;
    }

    Mesh FindMesh(string name)
    {
        if (meshes.TryGetValue(name, out var mesh)) return mesh;

        foreach (PrimitiveType type in System.Enum.GetValues(typeof(PrimitiveType)))
        {
            if (type.ToString() != name) continue;
            var temp = GameObject.CreatePrimitive(type);
            mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);
            break;
        }
        if (mesh == null)
            foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>())
                if (m.name == name) { mesh = m; break; }

        meshes[name] = mesh;
        return mesh;
    }

    static bool SameColor(Material m, Color color)
    {
        Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : Color.white;
        return Mathf.Abs(c.r - color.r) < 0.01f && Mathf.Abs(c.g - color.g) < 0.01f && Mathf.Abs(c.b - color.b) < 0.01f;
    }

    Material FindMaterial(string name, Color color, Material fallback = null)
    {
        string key = name + "|" + ColorUtility.ToHtmlStringRGBA(color);
        if (materials.TryGetValue(key, out var material)) return material;

        // Podle jmena jen kdyz sedi i barva (obarvene materialy z Fx.Paint se jmenuji stejne jako jejich zaklad).
        if (!string.IsNullOrEmpty(name) && !name.Contains("(Instance)"))
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m.name == name && SameColor(m, color)) { material = m; break; }

        if (material == null)
            material = fallback != null ? fallback : Fx.NewLit(color);

        materials[key] = material;
        return material;
    }
}
