using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class YarisTrafficSetup
{
    const string RootName = "ProvozYaris";
    const float Cell = 1.5f;
    static int resourceRetries;
    static YarisTrafficSetup()
    {
        EditorApplication.delayCall += Run;
        EditorApplication.delayCall += WriteCollisionReport;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += Run;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.EnteredPlayMode)
                EditorApplication.delayCall += Run;
        };
    }

    static void WriteCollisionReport()
    {
        var map = GameObject.Find("Map-Domasov");
        if (map == null) return;
        var report = new System.Text.StringBuilder("name\tminX\tminY\tminZ\tmaxX\tmaxY\tmaxZ\tremovable\n");
        foreach (var c in map.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger) continue;
            Bounds b = c.bounds;
            string path = c.name;
            for (var p = c.transform.parent; p != null && p != map.transform; p = p.parent) path = p.name + "/" + path;
            report.AppendLine(FormattableString.Invariant($"{path}\t{b.min.x}\t{b.min.y}\t{b.min.z}\t{b.max.x}\t{b.max.y}\t{b.max.z}\t{IsRemovable(c.transform, map.transform) || IsTraffic(c.transform, map.transform)}"));
        }
        System.IO.File.WriteAllText("Temp/YarisCollisionAudit.tsv", report.ToString());
    }

    static void Run()
    {
        var map = GameObject.Find("Map-Domasov");
        if (map == null) return;
        var car = map.transform.Find("Props/ToyotaYaris");
        var traffic = car != null ? car.GetComponent<YarisTraffic>() : null;
        // Existing routes used to skip Configure, leaving new audio fields empty in an open scene.
        // Bind missing clips independently of rebuilding the route, including after a Play-mode reload.
        if (traffic != null) BindMissingAudio(traffic);
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (map.transform.Find(RootName + "/_v4") != null && traffic != null)
        {
            if (map.transform.Find("LesniHranice/" + ForestBoundarySetup.Marker) == null) Configure(map.transform, traffic);
        }
        else Build(map.transform);
    }

    static void BindMissingAudio(YarisTraffic traffic)
    {
        bool changed = traffic.UpgradeRadioRange();
        if (traffic.radioMusic == null)
        {
            traffic.radioMusic = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Traffic/why_is_this_dealer.mp3");
            changed |= traffic.radioMusic != null;
        }
        if (traffic.impactSound == null)
        {
            traffic.impactSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Misc/bohuno_cut.mp3");
            changed |= traffic.impactSound != null;
        }
        if (changed && !EditorApplication.isPlaying)
        {
            EditorUtility.SetDirty(traffic);
            EditorSceneManager.MarkSceneDirty(traffic.gameObject.scene);
        }
        if ((traffic.radioMusic == null || traffic.impactSound == null) && resourceRetries++ < 30)
            EditorApplication.delayCall += Run;
    }

    [MenuItem("BrosOverwatch/Mapa/Pripravit okruh Yarisu")]
    static void Rebuild()
    {
        var map = GameObject.Find("Map-Domasov");
        if (map != null && !EditorApplication.isPlayingOrWillChangePlaymode) Build(map.transform);
    }

    static void Build(Transform map)
    {
        var car = map.Find("Props/ToyotaYaris");
        var model = car != null ? car.Find("Model") : null;
        var gravel = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Garden/Gravel.mat");
        if (model == null || gravel == null) return;
        // The component is also the persistent marker: rebuilding never halves the car again.
        bool first = car.GetComponent<YarisTraffic>() == null && car.Find("_TrafficHalfScale") == null && map.Find(RootName) == null;
        Vector3 savedScale = model.localScale;
        if (first) model.localScale *= 0.5f;
        Bounds size = ModelBounds(car, model);
        float radius = new Vector2(size.extents.x, size.extents.z).magnitude + 0.7f;
        // Plan by road width; a circumscribed circle falsely closes straight corridors.
        float laneClearance = Mathf.Min(size.extents.x, size.extents.z) + 0.9f;
        Physics.SyncTransforms();
        var ground = MapBuildKit.GroundBounds(map);
        float y = ground.max.y + 0.055f;
        var blockers = new List<Rect>();
        foreach (var collider in map.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger || collider.transform.IsChildOf(car)) continue;
            if (IsRemovable(collider.transform, map) || IsTraffic(collider.transform, map)) continue;
            Bounds b = collider.bounds;
            if (b.max.y < y + 0.15f || b.min.y > y + size.size.y + 0.2f) continue;
            blockers.Add(Rect.MinMaxRect(b.min.x - laneClearance, b.min.z - laneClearance, b.max.x + laneClearance, b.max.z + laneClearance));
        }
        // Keep the lane away from respawn locations.
        foreach (Transform t in map.GetComponentsInChildren<Transform>())
            if (t.name.StartsWith("SpawnPoint")) blockers.Add(new Rect(t.position.x - radius - 2f, t.position.z - radius - 2f, (radius + 2f) * 2, (radius + 2f) * 2));
        Vector2 center = new Vector2(ground.center.x, ground.center.z);
        // One through-pass with two gates; the return loop is deliberately outside.
        // Enter below the cottage and its respawn areas, then turn into the central yard.
        float entryZ = ground.min.z + 2.5f;
        float exitZ = ground.center.z + 8f;
        var goals = new[]
        {
            new Vector2(ground.min.x - 18f, entryZ),
            new Vector2(ground.min.x + 7f, entryZ),
            new Vector2(center.x - 2f, ground.min.z + 55f),
            new Vector2(center.x + 2f, ground.center.z - 14f),
            new Vector2(ground.max.x - 7f, exitZ),
            new Vector2(ground.max.x + 18f, exitZ),
            new Vector2(ground.max.x + 18f, ground.min.z - 20f),
            new Vector2(ground.min.x - 18f, ground.min.z - 20f)
        };
        var grid = new Grid(ground, blockers, laneClearance, map);
        var raw = new List<Vector2>();
        for (int i = 0; i < goals.Length; i++)
        {
            bool insideOnly = i >= 1 && i <= 3;
            var leg = grid.Find(goals[i], goals[(i + 1) % goals.Length], insideOnly);
            if (leg == null)
            {
                model.localScale = savedScale;
                Debug.LogError($"[Yaris] Pro vnitrni prujezd {i}: {goals[i]} -> {goals[(i + 1) % goals.Length]} neni volna trasa. Scena nebyla prestavena.");
                return;
            }
            if (raw.Count > 0) leg.RemoveAt(0);
            raw.AddRange(leg);
        }
        raw.RemoveAt(raw.Count - 1); // closed path stores the first node only once
        RemoveDuplicates(raw);
        // Preserve the required interior waypoints; only merge straight segments.
        for (int i = raw.Count - 1; i >= 0 && raw.Count > 3; i--)
        {
            Vector2 p = raw[(i + raw.Count - 1) % raw.Count], c = raw[i], n = raw[(i + 1) % raw.Count];
            if (Vector2.Dot((c - p).normalized, (n - c).normalized) > 0.999f && grid.Clear(p, n)) raw.RemoveAt(i);
        }
        var path = Smooth(raw, grid);
        var central = goals[3];
        if (!path.Exists(p => Vector2.Distance(p, central) < 8f))
        { model.localScale = savedScale; Debug.LogError("[Yaris] Trasa neprochazi povinnym stredem mapy."); return; }
        RemoveDuplicates(path);
        if (path.Count < 3) { model.localScale = savedScale; return; }
        var playable = Rect.MinMaxRect(ground.min.x, ground.min.z, ground.max.x, ground.max.z);
        int entries = 0, exits = 0;
        bool inside = playable.Contains(path[0]);
        float insideDistance = 0f;
        for (int i = 0; i < path.Count; i++)
        {
            Vector2 a = path[i], b = path[(i + 1) % path.Count];
            float distance = Vector2.Distance(a, b);
            int samples = Mathf.Max(1, Mathf.CeilToInt(distance / 0.25f));
            for (int j = 1; j <= samples; j++)
            {
                bool next = playable.Contains(Vector2.Lerp(a, b, j / (float)samples));
                if (next && !inside) entries++;
                if (!next && inside) exits++;
                if (next) insideDistance += distance / samples;
                inside = next;
            }
        }
        if (entries != 1 || exits != 1 || insideDistance < ground.size.x)
        {
            model.localScale = savedScale;
            Debug.LogError($"[Yaris] Kontrola prujezdu selhala: vjezdy {entries}, vyjezdy {exits}, uvnitr {insideDistance:0.0} m.");
            return;
        }
        var old = map.Find(RootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = MapBuildKit.Group(map, RootName);
        var route = MapBuildKit.Group(root, "Trasa");
        var road = MapBuildKit.Group(root, "SterkovaCesta");
        root.gameObject.AddComponent<YarisRoutePreview>().route = route;
        // Recenter a previously offset imported model so steering and collision use the car's center.
        model.localPosition -= new Vector3(size.center.x, size.min.y, size.center.z);
        size = ModelBounds(car, model);
        if (size.size.x > size.size.z)
        {
            model.RotateAround(car.position, car.up, 90f);
            size = ModelBounds(car, model);
        }
        var box = car.GetComponent<BoxCollider>();
        if (box == null) box = car.gameObject.AddComponent<BoxCollider>();
        box.center = size.center;
        box.size = size.size;
        box.isTrigger = false;
        box.enabled = true;
        foreach (Transform t in car.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        float width = size.size.x + 1.8f;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 a = new Vector3(path[i].x, y, path[i].y);
            Vector2 next = path[(i + 1) % path.Count];
            Vector3 b = new Vector3(next.x, y, next.y);
            var node = new GameObject($"Bod_{i:000}").transform;
            node.SetParent(route, true);
            node.position = a;
            Vector3 along = b - a;
            MapBuildKit.Box(road, $"Cesta_{i:000}", (a + b) * 0.5f - Vector3.up * 0.04f,
                new Vector3(width, 0.08f, along.magnitude + 0.15f), Quaternion.LookRotation(along), gravel, true, false, false);
            // Overlapping tiles fill the corners and support the off-map part of the route.
            MapBuildKit.Box(road, $"Spoj_{i:000}", a - Vector3.up * 0.04f,
                new Vector3(width, 0.08f, width), gravel, true, false);
        }
        // Only generated fence sections and Kenney decorations are cleared; buildings remain intact.
        ClearLane(map, path, radius);
        var rb = car.GetComponent<Rigidbody>();
        if (rb == null) rb = car.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        var traffic = car.GetComponent<YarisTraffic>();
        if (traffic == null) traffic = car.gameObject.AddComponent<YarisTraffic>();
        traffic.route = route;
        traffic.speed = 18f;
        car.SetPositionAndRotation(route.GetChild(0).position, Quaternion.LookRotation(route.GetChild(1).position - route.GetChild(0).position));
        if (car.Find("_TrafficHalfScale") == null) new GameObject("_TrafficHalfScale").transform.SetParent(car, false);
        new GameObject("_v4").transform.SetParent(root, false);
        var report = new System.Text.StringBuilder();
        report.AppendLine($"Validated through-pass: entries={entries}, exits={exits}, interior distance={insideDistance:0.0} m");
        report.AppendLine($"Required central pass: {central}; closest point: {path.Find(p => Vector2.Distance(p, central) < 8f)}");
        report.AppendLine($"Car local size: {size.size}; road width: {width}; clearance: {radius}");
        for (int i = 0; i < path.Count; i++) report.AppendLine($"{i}: {path[i]}");
        System.IO.File.WriteAllText("Temp/YarisTrafficAudit.txt", report.ToString());
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        Configure(map, traffic);
        Debug.Log($"[Yaris] Okruh s {path.Count} body pripraven. Rychlost 18 m/s, smrtici naraz. Uloz scenu (Ctrl+S).");
    }

    static void Configure(Transform map, YarisTraffic traffic)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Traffic/YarisHorn.wav");
        traffic.impactSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Misc/bohuno_cut.mp3");
        traffic.impactVolume = 0.7f;
        traffic.radioMusic = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Traffic/why_is_this_dealer.mp3");
        if (clip == null)
        {
            if (resourceRetries++ < 30) EditorApplication.delayCall += Run;
            return;
        }
        Bounds ground = MapBuildKit.GroundBounds(map);
        traffic.speed = 18f;
        traffic.hornLeadSeconds = 2f;
        traffic.boundaryFrame = map;
        Vector3 min = map.InverseTransformPoint(ground.min), max = map.InverseTransformPoint(ground.max);
        traffic.boundaryMin = new Vector2(min.x, min.z);
        traffic.boundaryMax = new Vector2(max.x, max.z);
        var source = traffic.GetComponent<AudioSource>();
        if (source == null) source = traffic.gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.volume = 0.9f;
        source.minDistance = 12f;
        source.maxDistance = 110f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0f;
        traffic.horn = source;
        // Start and wait behind the trees, well outside the playable rectangle.
        var nodes = new List<Transform>();
        int first = 0; float farthest = 0f;
        for (int i = 0; i < traffic.route.childCount; i++)
        {
            var node = traffic.route.GetChild(i); nodes.Add(node);
            Vector3 p = node.position;
            float outside = Mathf.Max(ground.min.x - p.x, p.x - ground.max.x, ground.min.z - p.z, p.z - ground.max.z);
            if (outside > farthest) { farthest = outside; first = i; }
        }
        for (int i = 0; i < nodes.Count; i++) nodes[(i + first) % nodes.Count].SetSiblingIndex(i);
        traffic.transform.SetPositionAndRotation(traffic.route.GetChild(0).position,
            Quaternion.LookRotation(traffic.route.GetChild(1).position - traffic.route.GetChild(0).position));
        ForestBoundarySetup.Build(map, traffic);
        EditorUtility.SetDirty(traffic);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
    }

    static void RemoveDuplicates(List<Vector2> points)
    {
        for (int i = points.Count - 1; i >= 0 && points.Count > 1; i--)
            if ((points[i] - points[(i + 1) % points.Count]).sqrMagnitude < 0.0001f) points.RemoveAt(i);
    }

    static bool IsTraffic(Transform t, Transform map)
    {
        for (; t != null && t != map; t = t.parent) if (t.name == RootName) return true;
        return false;
    }

    static bool IsRemovable(Transform t, Transform map)
    {
        for (; t != null && t != map; t = t.parent)
        {
            if (t.name == "KenneyDekorace" || t.name == "LesniHranice" || (t.name == "Oploceni" && t.parent != null && t.parent.name == "Pozemek")) return true;
            if (t.parent != null && (t.parent.name == "OkoliChaty" || t.parent.name == "Zahrada")
                && (t.name == "Stromy" || t.name == "Kere" || t.name == "Tuje" || t.name == "ZivyPlot" || t.name == "Oploceni")) return true;
        }
        return false;
    }

    static void ClearLane(Transform map, List<Vector2> path, float radius)
    {
        var remove = new HashSet<GameObject>();
        foreach (var r in map.GetComponentsInChildren<Renderer>())
        {
            if (!IsRemovable(r.transform, map)) continue;
            Bounds b = r.bounds;
            bool intersects = false;
            for (int i = 0; i < path.Count && !intersects; i++)
            {
                Vector2 p = path[i], q = path[(i + 1) % path.Count];
                int steps = Mathf.CeilToInt(Vector2.Distance(p, q) / 0.75f);
                for (int j = 0; j <= steps; j++)
                {
                    Vector2 s = Vector2.Lerp(p, q, j / (float)Mathf.Max(1, steps));
                    if (s.x >= b.min.x - radius && s.x <= b.max.x + radius && s.y >= b.min.z - radius && s.y <= b.max.z + radius) { intersects = true; break; }
                }
            }
            if (!intersects) continue;
            Transform top = r.transform;
            while (top.parent != null && top.parent != map && top.parent.name != "Oploceni"
                && top.parent.parent?.name != "KenneyDekorace"
                && !((top.parent.parent?.name == "OkoliChaty" || top.parent.parent?.name == "Zahrada")
                    && (top.parent.name == "Stromy" || top.parent.name == "Kere" || top.parent.name == "Tuje"
                        || top.parent.name == "ZivyPlot" || top.parent.name == "Oploceni"))) top = top.parent;
            remove.Add(top.gameObject);
        }
        foreach (var go in remove) if (go != null) Object.DestroyImmediate(go);
    }

    static Bounds ModelBounds(Transform car, Transform model)
    {
        var bounds = new Bounds();
        bool first = true;
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            var mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            Matrix4x4 matrix = car.worldToLocalMatrix * r.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = matrix.MultiplyPoint3x4(mesh.bounds.center + Vector3.Scale(mesh.bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
            }
        }
        return bounds;
    }

    static List<Vector2> Smooth(List<Vector2> input, Grid grid)
    {
        // Cut corners only where the complete clearance envelope remains free.
        for (int pass = 0; pass < 3; pass++)
        {
            var output = new List<Vector2>();
            for (int i = 0; i < input.Count; i++)
            {
                Vector2 p = input[(i + input.Count - 1) % input.Count], c = input[i], n = input[(i + 1) % input.Count];
                Vector2 a = Vector2.Lerp(c, p, 0.22f), b = Vector2.Lerp(c, n, 0.22f);
                if (grid.Clear(p, a) && grid.Clear(a, b) && grid.Clear(b, n)) { output.Add(a); output.Add(b); }
                else output.Add(c);
            }
            input = output;
        }
        return input;
    }

    sealed class Grid
    {
        readonly Vector2 origin;
        readonly int nx, nz;
        readonly bool[] blocked;
        readonly List<Rect> obstacles;
        readonly Bounds ground;
        readonly float clearance;
        readonly List<Rect> surfaces = new List<Rect>();
        public Grid(Bounds ground, List<Rect> obstacles, float clearance, Transform map)
        {
            this.ground = ground; this.obstacles = obstacles; this.clearance = clearance;
            var tiles = map.Find("Tunel/Ground_Tiles");
            if (tiles != null) foreach (var renderer in tiles.GetComponentsInChildren<Renderer>())
            {
                Bounds b = renderer.bounds;
                surfaces.Add(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z));
            }
            else surfaces.Add(Rect.MinMaxRect(ground.min.x, ground.min.z, ground.max.x, ground.max.z));
            origin = new Vector2(ground.min.x - 28f, ground.min.z - 28f);
            nx = Mathf.CeilToInt((ground.size.x + 56f) / Cell) + 1;
            nz = Mathf.CeilToInt((ground.size.z + 56f) / Cell) + 1;
            blocked = new bool[nx * nz];
            for (int i = 0; i < blocked.Length; i++) blocked[i] = !Free(Position(i));
        }
        Vector2 Position(int i) => origin + new Vector2(i % nx, i / nx) * Cell;
        bool Free(Vector2 p)
        {
            foreach (var r in obstacles) if (r.Contains(p)) return false;
            // Ground cut-outs (tunnel entrances) are obstacles as well. Test the entire car footprint.
            if (p.x > ground.min.x && p.x < ground.max.x && p.y > ground.min.z && p.y < ground.max.z)
                for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                {
                    Vector2 probe = p + new Vector2(x * clearance, z * clearance);
                    bool supported = surfaces.Exists(r => r.Contains(probe));
                    if (!supported && probe.x > ground.min.x && probe.x < ground.max.x && probe.y > ground.min.z && probe.y < ground.max.z) return false;
                }
            return true;
        }
        public bool Clear(Vector2 a, Vector2 b)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 0.5f));
            for (int i = 0; i <= steps; i++) if (!Free(Vector2.Lerp(a, b, i / (float)steps))) return false;
            return true;
        }
        bool Interior(Vector2 p) => p.x >= ground.min.x + 1.5f && p.x <= ground.max.x - 1.5f
            && p.y >= ground.min.z + 1.5f && p.y <= ground.max.z - 1.5f;
        int Nearest(Vector2 p, bool insideOnly)
        {
            int best = -1; float distance = float.MaxValue;
            for (int i = 0; i < blocked.Length; i++) if (!blocked[i] && (!insideOnly || Interior(Position(i))))
            {
                float d = (Position(i) - p).sqrMagnitude;
                if (d < distance) { best = i; distance = d; }
            }
            return distance <= 36f ? best : -1;
        }
        public List<Vector2> Find(Vector2 from, Vector2 to, bool insideOnly)
        {
            int start = Nearest(from, insideOnly), end = Nearest(to, insideOnly);
            if (start < 0 || end < 0) return null;
            int count = blocked.Length;
            var costs = new float[count]; var parents = new int[count]; var closed = new bool[count];
            for (int i = 0; i < count; i++) { costs[i] = float.MaxValue; parents[i] = -1; }
            var open = new SortedSet<(float score, int id)>();
            costs[start] = 0; open.Add((Vector2.Distance(Position(start), Position(end)), start));
            while (open.Count > 0)
            {
                var entry = open.Min; open.Remove(entry); int current = entry.id;
                if (closed[current]) continue;
                if (current == end)
                {
                    var path = new List<Vector2>();
                    for (int i = end; i != -1; i = parents[i]) path.Add(Position(i));
                    path.Reverse();
                    var simplified = new List<Vector2> { path[0] };
                    for (int i = 0; i < path.Count - 1;)
                    {
                        int next = i + 1;
                        while (next + 1 < path.Count && Clear(path[i], path[next + 1])) next++;
                        simplified.Add(path[next]); i = next;
                    }
                    return simplified;
                }
                closed[current] = true;
                int cx = current % nx, cz = current / nx;
                for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= nx || z >= nz) continue;
                    int n = z * nx + x;
                    if (blocked[n] || closed[n] || (insideOnly && !Interior(Position(n)))) continue;
                    if (dx != 0 && dz != 0 && (blocked[cz * nx + x] || blocked[z * nx + cx])) continue;
                    float cost = costs[current] + (dx != 0 && dz != 0 ? Cell * 1.414214f : Cell);
                    if (cost >= costs[n]) continue;
                    costs[n] = cost; parents[n] = current;
                    open.Add((cost + Vector2.Distance(Position(n), Position(end)), n));
                }
            }
            return null;
        }
    }
}
