using System.Collections.Generic;
using UnityEngine;

// Trasa robota v rezimu Tlacenice (MatchManagerPush). Lomena cara z bodu Bod_0..Bod_N (deti tohoto objektu),
// rohy jsou zaoblene (polomer 'cornerRadius'), aby se Yaris v zatacce plynule natacel.
//  s = vzdalenost podel trasy: s = 0 je konec u zakladny tymu 0, s = Length konec u zakladny tymu 1.
//  Stred (Length / 2) = start Honzy, Yarisy obou tymu stoji 'robotOffset' pred nim a za nim.
//  Tym 0 tlaci svuj Yaris k s = Length, tym 1 svuj k s = 0.
//  Checkpointy: 'checkpoints' metru od startu Yarisu tymu (na strane soupere).
//  Spawny: deti skupiny 'spawns' se jmeny Spawn_T{tym}_{uroven} (0 = zakladna, 1.. = po checkpointu).
// V editoru je trasa videt (Gizmos): cara, koridor, checkpointy, spawny.
public class PushRoute : MonoBehaviour
{
    [Tooltip("Polomer zaobleni rohu trasy (m).")]
    public float cornerRadius = 7f;
    [Tooltip("Checkpointy - kolik metru od startu Yarisu (pro oba tymy stejne).")]
    public float[] checkpoints = { 35f, 65f };
    [Tooltip("Skupina se spawny Spawn_T0_0, Spawn_T0_1, ... Spawn_T1_2.")]
    public Transform spawns;
    [Tooltip("Pulka sirky koridoru, ktery musi byt volny (jen pro nahled a audit).")]
    public float halfWidth = 3.0f;
    [Tooltip("Honza stoji tolik metru za stredem Yarisu (podel trasy). Nastavi PushSetup podle velikosti auta.")]
    public float robotOffset = 5.1f;

    public static PushRoute Active { get; private set; }

    const float Step = 0.25f;
    readonly List<Vector3> samples = new List<Vector3>();
    float length;
    bool built;

    void OnEnable()
    {
        Active = this;
        built = false;
    }

    void OnDisable()
    {
        if (Active == this) Active = null;
    }

    public float Length { get { Build(); return length; } }
    public float Center => Length * 0.5f;

    // Bod na trase. Za konci pokracuje rovne ve smeru konce (obr stoji za Yarisem i na konci trasy).
    public Vector3 Point(float s)
    {
        Build();
        if (samples.Count < 2) return transform.position;
        if (s < 0f) return samples[0] - EndDirection(false) * -s;
        if (s > length) return samples[samples.Count - 1] + EndDirection(true) * (s - length);
        float f = s / Step;
        int i = Mathf.Min((int)f, samples.Count - 2);
        return Vector3.Lerp(samples[i], samples[i + 1], f - i);
    }

    // Smer trasy (vodorovne, k zakladne tymu 1).
    public Vector3 Forward(float s)
    {
        Vector3 d = Point(s + 0.75f) - Point(s - 0.75f);
        d.y = 0f;
        return d.sqrMagnitude > 1e-6f ? d.normalized : transform.forward;
    }

    public Vector3 Right(float s) => Vector3.Cross(Vector3.up, Forward(s));

    // Nejblizsi misto na trase k bodu (vodorovne) - vraci s.
    public float Closest(Vector3 position)
    {
        Build();
        float best = float.MaxValue, bestS = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 d = samples[i] - position;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq < best) { best = sq; bestS = i * Step; }
        }
        return Mathf.Min(bestS, length);
    }

    // Kde je checkpoint 'index' tymu 'team' (s) - metry od startu jeho Yarisu.
    public float Checkpoint(int team, int index)
    {
        float offset = robotOffset + (checkpoints != null && index >= 0 && index < checkpoints.Length ? checkpoints[index] : 0f);
        return team == 0 ? Center + offset : Center - offset;
    }

    public int CheckpointCount => checkpoints != null ? checkpoints.Length : 0;

    // Spawn tymu po 'level' dosazenych checkpointech (kdyz takovy neni, nejblizsi nizsi).
    public bool TryGetSpawn(int team, int level, out Vector3 position)
    {
        position = Vector3.zero;
        if (spawns == null) return false;
        for (int l = level; l >= 0; l--)
        {
            var marker = spawns.Find($"Spawn_T{team}_{l}");
            if (marker == null) continue;
            position = marker.position;
            return true;
        }
        return false;
    }

    // ---------------- stavba ----------------

    Vector3 signature;
    int signatureCount = -1;
    float signatureRadius;

    void Build(bool force = false)
    {
        if (built && !force && Application.isPlaying) return;

        var points = new List<Vector3>();
        var nodes = new List<Transform>();
        foreach (Transform child in transform)
            if (child.name.StartsWith("Bod_")) nodes.Add(child);
        nodes.Sort((a, b) => NodeIndex(a).CompareTo(NodeIndex(b)));
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < nodes.Count; i++)
        {
            points.Add(nodes[i].position);
            sum += nodes[i].position * (i + 1);
        }
        // v editoru se prepocita, jen kdyz se nektery bod posunul
        if (built && sum == signature && points.Count == signatureCount && cornerRadius == signatureRadius) return;
        built = true;
        signature = sum;
        signatureCount = points.Count;
        signatureRadius = cornerRadius;
        samples.Clear();
        length = 0f;
        if (points.Count < 2) return;

        // lomena cara se zaoblenymi rohy (kvadraticka krivka s ridicim bodem v rohu)
        var dense = new List<Vector3> { points[0] };
        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector3 a = points[i] - points[i - 1], b = points[i + 1] - points[i];
            float lenA = a.magnitude, lenB = b.magnitude;
            if (lenA < 0.01f || lenB < 0.01f) continue;
            a /= lenA;
            b /= lenB;
            float angle = Vector3.Angle(a, b) * Mathf.Deg2Rad;
            float t = Mathf.Min(cornerRadius * Mathf.Tan(angle * 0.5f), lenA * 0.45f, lenB * 0.45f);
            Vector3 t1 = points[i] - a * t, t2 = points[i] + b * t;
            dense.Add(t1);
            const int Segments = 16;
            for (int k = 1; k < Segments; k++)
            {
                float u = k / (float)Segments;
                dense.Add((1 - u) * (1 - u) * t1 + 2 * (1 - u) * u * points[i] + u * u * t2);
            }
            dense.Add(t2);
        }
        dense.Add(points[points.Count - 1]);

        // rovnomerne vzorky po 'Step' metrech
        samples.Add(dense[0]);
        float carried = 0f;
        for (int i = 1; i < dense.Count; i++)
        {
            Vector3 from = dense[i - 1], to = dense[i];
            float segment = Vector3.Distance(from, to);
            if (segment < 1e-5f) continue;
            float pos = Step - carried;
            while (pos <= segment)
            {
                samples.Add(Vector3.Lerp(from, to, pos / segment));
                pos += Step;
            }
            carried = segment - (pos - Step);
            length += segment;
        }
        if (Vector3.Distance(samples[samples.Count - 1], dense[dense.Count - 1]) > 0.01f)
            samples.Add(dense[dense.Count - 1]);
        length = (samples.Count - 1) * Step;
    }

    Vector3 EndDirection(bool end)
    {
        int n = samples.Count;
        Vector3 d = end ? samples[n - 1] - samples[Mathf.Max(0, n - 4)] : samples[Mathf.Min(n - 1, 3)] - samples[0];
        d.y = 0f;
        return d.sqrMagnitude > 1e-6f ? d.normalized : transform.forward;
    }

    static int NodeIndex(Transform node) => int.TryParse(node.name.Substring(4), out int index) ? index : 0;

    // ---------------- nahled v editoru ----------------

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Build(true);
        if (samples.Count < 2) return;
        Gizmos.color = new Color(0.1f, 0.85f, 0.45f, 0.9f);
        for (int i = 1; i < samples.Count; i++)
            Gizmos.DrawLine(samples[i - 1] + Vector3.up * 0.15f, samples[i] + Vector3.up * 0.15f);

        // stred, checkpointy, konce
        DrawMarker(Center, new Color(1f, 0.85f, 0.1f), 1.2f, "START Honzy");
        DrawMarker(Center + robotOffset, new Color(0.3f, 0.64f, 1f), 0.7f, "Yaris tymu 0");
        DrawMarker(Center - robotOffset, new Color(1f, 0.35f, 0.3f), 0.7f, "Yaris tymu 1");
        for (int team = 0; team < 2; team++)
        {
            Color c = team == 0 ? new Color(0.3f, 0.64f, 1f) : new Color(1f, 0.35f, 0.3f);
            for (int i = 0; i < CheckpointCount; i++)
                DrawMarker(Checkpoint(team, i), c, 0.9f, $"CP tym {team} #{i + 1}");
        }
        DrawMarker(0f, new Color(0.3f, 0.64f, 1f), 1.5f, "KONEC u zakladny tymu 0");
        DrawMarker(length, new Color(1f, 0.35f, 0.3f), 1.5f, "KONEC u zakladny tymu 1");

        if (spawns != null)
            foreach (Transform spawn in spawns)
            {
                bool team0 = spawn.name.StartsWith("Spawn_T0");
                Gizmos.color = team0 ? new Color(0.3f, 0.64f, 1f) : new Color(1f, 0.35f, 0.3f);
                Gizmos.DrawWireCube(spawn.position + Vector3.up * 1f, new Vector3(1f, 2f, 1f));
                UnityEditor.Handles.Label(spawn.position + Vector3.up * 2.4f, spawn.name);
            }
    }

    void OnDrawGizmosSelected()
    {
        Build(true);
        Gizmos.color = new Color(0.1f, 0.85f, 0.45f, 0.35f);
        for (float s = 0f; s <= length; s += 1f)
        {
            Vector3 p = Point(s), r = Right(s) * halfWidth;
            Gizmos.DrawLine(p - r + Vector3.up * 0.1f, p + r + Vector3.up * 0.1f);
        }
    }

    void DrawMarker(float s, Color color, float size, string label)
    {
        Vector3 p = Point(s);
        Gizmos.color = color;
        Gizmos.DrawWireSphere(p + Vector3.up * size, size);
        Vector3 r = Right(s) * (halfWidth + 0.5f);
        Gizmos.DrawLine(p - r + Vector3.up * 0.2f, p + r + Vector3.up * 0.2f);
        UnityEditor.Handles.Label(p + Vector3.up * (size * 2f + 0.5f), label);
    }
#endif
}
