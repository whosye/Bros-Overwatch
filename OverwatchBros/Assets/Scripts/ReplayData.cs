using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

// Data zaznamu "play of the game" (prehravani primo ve hre, ne video): snimky stavu sveta 30x za sekundu
// (hraci, kamera a HUD hrace, ktery akci predvedl, a obecne zachycene efekty) plus udalosti (vystrely, zvuky,
// vybuchy, hlasky, animace). Zaznam se zabali do par set kB a posle vsem; kazdy si ho prehraje sam v plnem rozliseni.

// ---------------- snimek ----------------

public struct ReplayPlayerSnap
{
    public int id;            // NetworkObjectId postavy hrace (boti maji vlastni, i kdyz patri hostovi)
    public short hero;        // index v HeroRegistry
    public byte team;
    public Vector3 position;
    public float yaw;
    public byte flags;        // ReplayPlayerFlags
    public float health, maxHealth;

    public const byte Alive = 1, Rushing = 2, Blocking = 4, Boosted = 8, Hidden = 16, Revealed = 32;
}

public struct ReplaySlot
{
    public byte abilitySlot;  // 0 Q, 1 Shift, 2 blok, 3 E, 4 PTM (HeroVoice.AbilityInSlot)
    public byte key;          // 0 Q, 1 SHIFT, 2 E, 3 PTM
    public float remaining;
    public bool active;
    public float charge;
    public byte count;
    public bool fullOnly;

    public static readonly string[] Keys = { "Q", "SHIFT", "E", "PTM" };
}

public class ReplayFrame
{
    public float time;
    public ReplayPlayerSnap[] players;

    // pohled hrace, ktery akci predvedl
    public Vector3 cameraPosition;
    public Quaternion cameraRotation;
    public float fov;
    public bool thirdPerson;
    public bool ultCasting, blocking;
    public float ultWindup, charge;

    // jeho HUD
    public short hudHero = -1;
    public byte hudTeam;
    public float health, maxHealth;
    public ReplaySlot[] slots;
    public short ammo, maxAmmo;
    public bool reloading;
    public bool locked;
    public Vector3 lockPoint;

    public List<ReplayObjectState> objects = new List<ReplayObjectState>();
}

// ---------------- obecne zachycene efekty (granaty, pasti, smrst, lana...) ----------------

public class ReplayNodeTemplate
{
    public int parent = -1;
    public string mesh = "";      // nazev meshe (Cube, Sphere, ... nebo nacteny asset)
    public string material = "";
    public Color color = Color.white;
    public bool hasLine;
    public bool lineWorld = true;
    public float lineStartWidth, lineEndWidth;
    public bool hasLight;
    public byte lightType;
    public Color lightColor;
    public float lightRange;
    public string text;           // TextMeshPro (null = bez textu)
    public float textSize;
    public Color textColor;
}

public class ReplayObjectTemplate
{
    public int id;
    public ReplayNodeTemplate[] nodes;
}

public class ReplayObjectState
{
    public int id;
    public bool[] active;
    public Vector3[] position;     // koren: svet, ostatni: mistne
    public Quaternion[] rotation;
    public Vector3[] scale;
    public Vector3[][] linePoints; // jen uzly s carou
    public Color[] lineStart, lineEnd;
    public float[] lightIntensity;
}

// ---------------- udalosti ----------------

public enum ReplayEventType : byte
{
    Explosion, Sparks, Impact, Tracer, SpatialSound, GlobalSound, Sfx, ProjectileSpawn, ProjectileEnd,
    Voice, Swing, Reload, StopReload, Kick, FirstPersonKick, HudHit, HudFlash, HudSleep, HudTint, HudEnd,
    NanoAura, SleepShow, SleepHide, KillFeed
}

public class ReplayEvent
{
    public float time;
    public ReplayEventType type;
    public int player = -1;          // u udalosti na hraci
    public int id;                   // projektil, druh hlasky...
    public int a, b;                 // dalsi cela cisla (slot, pick, druh konce...)
    public Vector3 p0, p1, p2;
    public Color color;
    public float f0, f1, f2;
    public bool flag;
    public string name = "";         // zvuk / zbran
}

// ---------------- cely zaznam ----------------

public class ReplayClip
{
    public const int Version = 2;

    public int povId;
    public float duration;
    public List<ReplayFrame> frames = new List<ReplayFrame>();
    public List<ReplayEvent> events = new List<ReplayEvent>();
    public Dictionary<int, ReplayObjectTemplate> templates = new Dictionary<int, ReplayObjectTemplate>();

    // ---------------- zapis ----------------

    public byte[] Serialize()
    {
        using (var raw = new MemoryStream())
        {
            using (var gzip = new GZipStream(raw, System.IO.Compression.CompressionLevel.Optimal, true))
            using (var w = new BinaryWriter(gzip))
            {
                w.Write(Version);
                w.Write(povId);
                w.Write(duration);

                w.Write(templates.Count);
                foreach (var t in templates.Values)
                    WriteTemplate(w, t);

                w.Write(frames.Count);
                var previous = new Dictionary<int, ReplayObjectState>();
                foreach (var f in frames)
                    WriteFrame(w, f, previous);

                w.Write(events.Count);
                foreach (var e in events)
                    WriteEvent(w, e);
            }
            return raw.ToArray();
        }
    }

    public static ReplayClip Deserialize(byte[] data)
    {
        using (var raw = new MemoryStream(data))
        using (var gzip = new GZipStream(raw, CompressionMode.Decompress))
        using (var r = new BinaryReader(gzip))
        {
            if (r.ReadInt32() != Version) return null;

            var clip = new ReplayClip { povId = r.ReadInt32(), duration = r.ReadSingle() };

            int templateCount = r.ReadInt32();
            for (int i = 0; i < templateCount; i++)
            {
                var t = ReadTemplate(r);
                clip.templates[t.id] = t;
            }

            int frameCount = r.ReadInt32();
            var previous = new Dictionary<int, ReplayObjectState>();
            for (int i = 0; i < frameCount; i++)
                clip.frames.Add(ReadFrame(r, clip, previous));

            int eventCount = r.ReadInt32();
            for (int i = 0; i < eventCount; i++)
                clip.events.Add(ReadEvent(r));

            return clip;
        }
    }

    static void WriteV(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    static Vector3 ReadV(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    static void WriteQ(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
    static Quaternion ReadQ(BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    static void WriteC(BinaryWriter w, Color c) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
    static Color ReadC(BinaryReader r) => new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    static void WriteTemplate(BinaryWriter w, ReplayObjectTemplate t)
    {
        w.Write(t.id);
        w.Write(t.nodes.Length);
        foreach (var n in t.nodes)
        {
            w.Write(n.parent);
            w.Write(n.mesh ?? "");
            w.Write(n.material ?? "");
            WriteC(w, n.color);
            w.Write(n.hasLine);
            if (n.hasLine)
            {
                w.Write(n.lineWorld);
                w.Write(n.lineStartWidth);
                w.Write(n.lineEndWidth);
            }
            w.Write(n.hasLight);
            if (n.hasLight)
            {
                w.Write(n.lightType);
                WriteC(w, n.lightColor);
                w.Write(n.lightRange);
            }
            w.Write(n.text != null);
            if (n.text != null)
            {
                w.Write(n.text);
                w.Write(n.textSize);
                WriteC(w, n.textColor);
            }
        }
    }

    static ReplayObjectTemplate ReadTemplate(BinaryReader r)
    {
        var t = new ReplayObjectTemplate { id = r.ReadInt32() };
        t.nodes = new ReplayNodeTemplate[r.ReadInt32()];
        for (int i = 0; i < t.nodes.Length; i++)
        {
            var n = new ReplayNodeTemplate { parent = r.ReadInt32(), mesh = r.ReadString(), material = r.ReadString(), color = ReadC(r) };
            n.hasLine = r.ReadBoolean();
            if (n.hasLine)
            {
                n.lineWorld = r.ReadBoolean();
                n.lineStartWidth = r.ReadSingle();
                n.lineEndWidth = r.ReadSingle();
            }
            n.hasLight = r.ReadBoolean();
            if (n.hasLight)
            {
                n.lightType = r.ReadByte();
                n.lightColor = ReadC(r);
                n.lightRange = r.ReadSingle();
            }
            if (r.ReadBoolean())
            {
                n.text = r.ReadString();
                n.textSize = r.ReadSingle();
                n.textColor = ReadC(r);
            }
            t.nodes[i] = n;
        }
        return t;
    }

    static void WriteFrame(BinaryWriter w, ReplayFrame f, Dictionary<int, ReplayObjectState> previous)
    {
        w.Write(f.time);

        w.Write((byte)f.players.Length);
        foreach (var p in f.players)
        {
            w.Write(p.id);
            w.Write(p.hero);
            w.Write(p.team);
            WriteV(w, p.position);
            w.Write(p.yaw);
            w.Write(p.flags);
            w.Write(p.health);
            w.Write(p.maxHealth);
        }

        WriteV(w, f.cameraPosition);
        WriteQ(w, f.cameraRotation);
        w.Write(f.fov);
        byte viewFlags = (byte)((f.thirdPerson ? 1 : 0) | (f.ultCasting ? 2 : 0) | (f.blocking ? 4 : 0) | (f.reloading ? 8 : 0) | (f.locked ? 16 : 0));
        w.Write(viewFlags);
        w.Write(f.ultWindup);
        w.Write(f.charge);

        w.Write(f.hudHero);
        w.Write(f.hudTeam);
        w.Write(f.health);
        w.Write(f.maxHealth);
        w.Write(f.ammo);
        w.Write(f.maxAmmo);
        if (f.locked) WriteV(w, f.lockPoint);
        var slots = f.slots ?? new ReplaySlot[0];
        w.Write((byte)slots.Length);
        foreach (var s in slots)
        {
            w.Write(s.abilitySlot);
            w.Write(s.key);
            w.Write(s.remaining);
            w.Write(s.active);
            w.Write(s.charge);
            w.Write(s.count);
            w.Write(s.fullOnly);
        }

        // Efekty: delta proti predchozimu snimku stejneho objektu (staticke casti zaberou 1 bajt).
        w.Write((short)f.objects.Count);
        foreach (var o in f.objects)
        {
            w.Write(o.id);
            previous.TryGetValue(o.id, out var prev);
            for (int i = 0; i < o.position.Length; i++)
            {
                byte mask = 0;
                if (prev == null || prev.active[i] != o.active[i]) mask |= 1;
                if (prev == null || (prev.position[i] - o.position[i]).sqrMagnitude > 1e-8f) mask |= 2;
                if (prev == null || Quaternion.Dot(prev.rotation[i], o.rotation[i]) < 0.999999f) mask |= 4;
                if (prev == null || (prev.scale[i] - o.scale[i]).sqrMagnitude > 1e-8f) mask |= 8;
                if (o.linePoints[i] != null) mask |= 16;
                if (o.lightIntensity[i] >= 0f && (prev == null || Mathf.Abs(prev.lightIntensity[i] - o.lightIntensity[i]) > 0.01f)) mask |= 32;

                w.Write(mask);
                if ((mask & 1) != 0) w.Write(o.active[i]);
                if ((mask & 2) != 0) WriteV(w, o.position[i]);
                if ((mask & 4) != 0) WriteQ(w, o.rotation[i]);
                if ((mask & 8) != 0) WriteV(w, o.scale[i]);
                if ((mask & 16) != 0)
                {
                    var pts = o.linePoints[i];
                    w.Write((short)pts.Length);
                    foreach (var pt in pts) WriteV(w, pt);
                    WriteC(w, o.lineStart[i]);
                    WriteC(w, o.lineEnd[i]);
                }
                if ((mask & 32) != 0) w.Write(o.lightIntensity[i]);
            }
            previous[o.id] = o;
        }
    }

    static ReplayFrame ReadFrame(BinaryReader r, ReplayClip clip, Dictionary<int, ReplayObjectState> previous)
    {
        var f = new ReplayFrame { time = r.ReadSingle() };

        f.players = new ReplayPlayerSnap[r.ReadByte()];
        for (int i = 0; i < f.players.Length; i++)
            f.players[i] = new ReplayPlayerSnap
            {
                id = r.ReadInt32(), hero = r.ReadInt16(), team = r.ReadByte(), position = ReadV(r), yaw = r.ReadSingle(), flags = r.ReadByte(),
                health = r.ReadSingle(), maxHealth = r.ReadSingle(),
            };

        f.cameraPosition = ReadV(r);
        f.cameraRotation = ReadQ(r);
        f.fov = r.ReadSingle();
        byte viewFlags = r.ReadByte();
        f.thirdPerson = (viewFlags & 1) != 0;
        f.ultCasting = (viewFlags & 2) != 0;
        f.blocking = (viewFlags & 4) != 0;
        f.reloading = (viewFlags & 8) != 0;
        f.locked = (viewFlags & 16) != 0;
        f.ultWindup = r.ReadSingle();
        f.charge = r.ReadSingle();

        f.hudHero = r.ReadInt16();
        f.hudTeam = r.ReadByte();
        f.health = r.ReadSingle();
        f.maxHealth = r.ReadSingle();
        f.ammo = r.ReadInt16();
        f.maxAmmo = r.ReadInt16();
        if (f.locked) f.lockPoint = ReadV(r);
        f.slots = new ReplaySlot[r.ReadByte()];
        for (int i = 0; i < f.slots.Length; i++)
            f.slots[i] = new ReplaySlot
            {
                abilitySlot = r.ReadByte(), key = r.ReadByte(), remaining = r.ReadSingle(), active = r.ReadBoolean(),
                charge = r.ReadSingle(), count = r.ReadByte(), fullOnly = r.ReadBoolean(),
            };

        int objectCount = r.ReadInt16();
        for (int k = 0; k < objectCount; k++)
        {
            int id = r.ReadInt32();
            clip.templates.TryGetValue(id, out var template);
            int nodes = template != null ? template.nodes.Length : 0;
            previous.TryGetValue(id, out var prev);
            var o = NewState(id, nodes);
            for (int i = 0; i < nodes; i++)
            {
                byte mask = r.ReadByte();
                o.active[i] = (mask & 1) != 0 ? r.ReadBoolean() : prev != null && prev.active[i];
                o.position[i] = (mask & 2) != 0 ? ReadV(r) : prev != null ? prev.position[i] : Vector3.zero;
                o.rotation[i] = (mask & 4) != 0 ? ReadQ(r) : prev != null ? prev.rotation[i] : Quaternion.identity;
                o.scale[i] = (mask & 8) != 0 ? ReadV(r) : prev != null ? prev.scale[i] : Vector3.one;
                if ((mask & 16) != 0)
                {
                    var pts = new Vector3[r.ReadInt16()];
                    for (int p = 0; p < pts.Length; p++) pts[p] = ReadV(r);
                    o.linePoints[i] = pts;
                    o.lineStart[i] = ReadC(r);
                    o.lineEnd[i] = ReadC(r);
                }
                o.lightIntensity[i] = (mask & 32) != 0 ? r.ReadSingle() : prev != null ? prev.lightIntensity[i] : -1f;
            }
            previous[id] = o;
            if (template != null)
                f.objects.Add(o);
        }
        return f;
    }

    public static ReplayObjectState NewState(int id, int nodes)
    {
        return new ReplayObjectState
        {
            id = id,
            active = new bool[nodes],
            position = new Vector3[nodes],
            rotation = new Quaternion[nodes],
            scale = new Vector3[nodes],
            linePoints = new Vector3[nodes][],
            lineStart = new Color[nodes],
            lineEnd = new Color[nodes],
            lightIntensity = new float[nodes],
        };
    }

    static void WriteEvent(BinaryWriter w, ReplayEvent e)
    {
        w.Write(e.time);
        w.Write((byte)e.type);
        w.Write(e.player);
        w.Write(e.id);
        w.Write(e.a);
        w.Write(e.b);
        WriteV(w, e.p0);
        WriteV(w, e.p1);
        WriteV(w, e.p2);
        WriteC(w, e.color);
        w.Write(e.f0);
        w.Write(e.f1);
        w.Write(e.f2);
        w.Write(e.flag);
        w.Write(e.name ?? "");
    }

    static ReplayEvent ReadEvent(BinaryReader r)
    {
        return new ReplayEvent
        {
            time = r.ReadSingle(), type = (ReplayEventType)r.ReadByte(), player = r.ReadInt32(), id = r.ReadInt32(),
            a = r.ReadInt32(), b = r.ReadInt32(), p0 = ReadV(r), p1 = ReadV(r), p2 = ReadV(r), color = ReadC(r),
            f0 = r.ReadSingle(), f1 = r.ReadSingle(), f2 = r.ReadSingle(), flag = r.ReadBoolean(), name = r.ReadString(),
        };
    }
}

// Znacka na objektech, ktere zaznam nema obecne zachytavat (prehravaji se jako udalosti, nebo jsou soucasti mapy).
public class ReplayIgnore : MonoBehaviour { }
