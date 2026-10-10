using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ForestBoundarySetup
{
    const string ForestMaterials = "Assets/Materials/Forest/";
    static int paletteRetries;

    static ForestBoundarySetup()
    {
        EditorApplication.delayCall += ApplyForestPalette;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += ApplyForestPalette;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += ApplyForestPalette;
        };
    }

    [MenuItem("BrosOverwatch/Mapa/Prirozene barvy lesni vegetace")]
    public static void ApplyForestPalette()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var map = GameObject.Find("Map-Domasov");
        if (map == null) return;
        var palettes = new Dictionary<string, Material[]>();
        foreach (string kind in new[] { "Conifer", "Broadleaf", "Shrub" })
        {
            var variants = new Material[3];
            for (int i = 0; i < variants.Length; i++)
            {
                variants[i] = AssetDatabase.LoadAssetAtPath<Material>($"{ForestMaterials}Forest_{kind}_{i}.mat");
                if (variants[i] == null)
                {
                    if (paletteRetries++ < 60) EditorApplication.delayCall += ApplyForestPalette;
                    return;
                }
            }
            palettes.Add(kind, variants);
        }
        var bark = AssetDatabase.LoadAssetAtPath<Material>(ForestMaterials + "Forest_Bark.mat");
        if (bark == null) return;
        int changed = 0;
        foreach (string path in new[] { "LesniHranice/Stromy", "LesniHranice/HustyPodrost",
            "KenneyDekorace/StromyPoOkraji", "KenneyDekorace/KereKamenyKvetiny" })
        {
            var group = map.transform.Find(path);
            if (group == null) continue;
            foreach (Transform plant in group)
            {
                string name = plant.name.ToLowerInvariant();
                if (!name.StartsWith("tree_") && !name.StartsWith("plant_bush")) continue;
                string kind = name.Contains("pine") ? "Conifer" : name.StartsWith("tree_") ? "Broadleaf" : "Shrub";
                Vector3 p = plant.localPosition;
                // Stable variation per plant: reloading or rebuilding does not shuffle its shade.
                int seed = unchecked(Mathf.RoundToInt(p.x * 100f) * 73856093 ^ Mathf.RoundToInt(p.z * 100f) * 19349663);
                var foliage = palettes[kind][(int)((uint)seed % 3u)];
                foreach (var renderer in plant.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    bool dirty = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null) continue;
                        string slot = materials[i].name.ToLowerInvariant();
                        Material replacement = null;
                        if (slot.Contains("leaf") || slot == "grass" || (slot.StartsWith("forest_") && slot != "forest_bark"))
                            replacement = foliage;
                        else if (slot.Contains("woodbark") || slot == "forest_bark") replacement = bark;
                        if (replacement == null || replacement == materials[i]) continue;
                        materials[i] = replacement;
                        dirty = true;
                    }
                    if (!dirty) continue;
                    renderer.sharedMaterials = materials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    EditorUtility.SetDirty(renderer);
                    changed++;
                }
            }
        }
        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(map.scene);
            Debug.Log($"[Les] Prirozene materialy aplikovany na {changed} rendereru. Uloz scenu (Ctrl+S).");
        }
    }
    const string RootName = "LesniHranice";
    // Sever mapy je kratsi (zahrada za rozhlednou), at je bojiste kompaktnejsi; puvodne konec podlahy (~115 m).
    public const float NorthLimit = 92f;
    public const string Marker = "_v3";
    const string Nature = "Assets/Models/Kenney/NatureKit/";
    static readonly string[] TreeNames = { "tree_pineDefaultA", "tree_pineDefaultB", "tree_pineRoundA", "tree_pineRoundB", "tree_default_dark", "tree_oak_dark" };

    [MenuItem("BrosOverwatch/Mapa/Obnovit lesni hranici")]
    static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var map = GameObject.Find("Map-Domasov");
        var car = map != null ? map.transform.Find("Props/ToyotaYaris") : null;
        var traffic = car != null ? car.GetComponent<YarisTraffic>() : null;
        if (traffic != null) Build(map.transform, traffic);
    }

    public static bool Build(Transform map, YarisTraffic traffic)
    {
        var trees = new List<GameObject>();
        foreach (string name in TreeNames)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Nature + name + ".glb");
            if (prefab == null) return false;
            trees.Add(prefab);
        }
        var shrub = AssetDatabase.LoadAssetAtPath<GameObject>(Nature + "plant_bushDetailed.glb");
        var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wood/Wood_Beam.mat");
        if (shrub == null || wood == null || traffic.route == null) return false;
        var old = map.Find(RootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var fence = map.Find("Pozemek/Oploceni");
        if (fence != null) Object.DestroyImmediate(fence.gameObject);
        var root = MapBuildKit.Group(map, RootName);
        var forest = MapBuildKit.Group(root, "Stromy");
        var hedge = MapBuildKit.Group(root, "HustyPodrost");
        var walls = MapBuildKit.Group(root, "HraniceProHrace");
        var gates = MapBuildKit.Group(root, "PrujezdyProAuto");
        Bounds ground = MapBuildKit.GroundBounds(map);
        float y = ground.max.y;
        float north = Mathf.Min(ground.max.z, NorthLimit);
        Vector3[] corners =
        {
            new Vector3(ground.min.x, y, ground.min.z), new Vector3(ground.max.x, y, ground.min.z),
            new Vector3(ground.max.x, y, north), new Vector3(ground.min.x, y, north)
        };
        var road = new List<Vector2>();
        foreach (Transform node in traffic.route) road.Add(new Vector2(node.position.x, node.position.z));
        var carBox = traffic.GetComponent<BoxCollider>();
        Vector3 carHalf = Vector3.Scale(carBox.size * 0.5f, traffic.transform.lossyScale);
        float clearance = new Vector2(carHalf.x, carHalf.z).magnitude + 1.2f;
        var random = new System.Random(43187);
        int count = 0, gateCount = 0;
        var crossingPoints = new List<Vector3>();
        for (int edge = 0; edge < 4; edge++)
        {
            Vector3 a = corners[edge], b = corners[(edge + 1) % 4];
            Vector3 tangent = (b - a).normalized;
            Vector3 outward = Vector3.Cross(Vector3.up, tangent);
            int steps = Mathf.CeilToInt(Vector3.Distance(a, b) / 3.1f);
            // A continuous tall collider keeps jumps and traffic gates inside the game area.
            var wall = new GameObject("Hranice");
            wall.transform.SetParent(walls, true);
            wall.transform.SetPositionAndRotation((a + b) * 0.5f + Vector3.up * 10f, Quaternion.LookRotation(tangent));
            wall.AddComponent<BoxCollider>().size = new Vector3(0.6f, 24f, Vector3.Distance(a, b) + 0.6f);
            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = Vector3.Lerp(a, b, i / (float)steps);
                for (int row = 0; row < 2; row++)
                {
                    Vector3 foot = point + outward * (2.4f + row * 3.6f) + tangent * Range(random, -0.55f, 0.55f);
                    float height = Range(random, 7.5f, 11.5f);
                    var tree = Place(forest, trees[random.Next(trees.Count)], foot, height, Range(random, 0f, 360f));
                    if (NearRoad(tree, road, clearance)) Object.DestroyImmediate(tree); else count++;
                }
                var bush = Place(hedge, shrub, point + outward * 1.5f, Range(random, 3.5f, 4.8f), Range(random, 0f, 360f));
                if (NearRoad(bush, road, clearance)) Object.DestroyImmediate(bush);
            }
            // Mark every actual route crossing, so entries match the driver's horn cues.
            for (int i = 0; i < road.Count; i++)
            {
                if (!Intersection(road[i], road[(i + 1) % road.Count], new Vector2(a.x, a.z), new Vector2(b.x, b.z), out Vector2 p)) continue;
                Vector3 center = new Vector3(p.x, y, p.y);
                if (crossingPoints.Exists(c => Vector3.Distance(c, center) < 4f)) continue;
                crossingPoints.Add(center);
                var gate = MapBuildKit.Group(gates, $"Brana_{++gateCount}");
                float halfWidth = clearance + 1.2f;
                foreach (float side in new[] { -1f, 1f })
                    MapBuildKit.Box(gate, "Sloup", center + tangent * halfWidth * side + Vector3.up * 3.1f,
                        new Vector3(0.45f, 6.2f, 0.45f), wood, false);
                MapBuildKit.Beam(gate, "Preklad", center - tangent * halfWidth + Vector3.up * 6.2f,
                    center + tangent * halfWidth + Vector3.up * 6.2f, 0.35f, wood);
            }
        }
        var boundary = root.gameObject.AddComponent<TrafficBoundary>();
        boundary.car = traffic;
        Physics.SyncTransforms();
        boundary.ApplyCollisionExceptions();
        new GameObject(Marker).transform.SetParent(root, false);
        ApplyForestPalette();
        var report = new System.Text.StringBuilder();
        report.AppendLine($"Trees: {count}; gates: {gateCount}; clearance: {clearance}; bounds: {ground}");
        report.AppendLine("Boundary: 4 solid walls, 24 metres high, ignored only by the Yaris colliders.");
        int verified = 0;
        foreach (var wall in walls.GetComponentsInChildren<Collider>())
        {
            if (wall.enabled && !wall.isTrigger && Physics.GetIgnoreCollision(wall, carBox)) verified++;
        }
        report.AppendLine($"Verified enabled solid boundary colliders with Yaris exception: {verified}/4");
        if (verified != 4) Debug.LogError("[Lesni hranice] Kolizni vyjimka auta neni spravne nastavena.");
        foreach (var point in crossingPoints) report.AppendLine($"Gate: {point}");
        System.IO.File.WriteAllText("Temp/ForestBoundaryAudit.txt", report.ToString());
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        Debug.Log($"[Lesni hranice] {count} stromu, {gateCount} prujezdu. Hranice blokuje hrace, auto projede. Uloz scenu (Ctrl+S).");
        return true;
    }

    static float Range(System.Random r, float min, float max) => Mathf.Lerp(min, max, (float)r.NextDouble());

    static GameObject Place(Transform parent, GameObject prefab, Vector3 foot, float height, float yaw)
    {
        var root = new GameObject(prefab.name);
        root.transform.SetParent(parent, true);
        root.transform.position = foot;
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        model.transform.SetParent(root.transform, false);
        Bounds bounds = BoundsOf(root);
        if (bounds.size.y > 0.001f) model.transform.localScale *= height / bounds.size.y;
        bounds = BoundsOf(root);
        model.transform.position += new Vector3(foot.x - bounds.center.x, foot.y - bounds.min.y, foot.z - bounds.center.z);
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        foreach (var c in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        foreach (Transform t in root.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        return root;
    }

    static Bounds BoundsOf(GameObject root)
    {
        var bounds = new Bounds(); bool first = true;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
        return bounds;
    }

    static bool NearRoad(GameObject tree, List<Vector2> road, float clearance)
    {
        Bounds bounds = BoundsOf(tree);
        Vector2 center = new Vector2(bounds.center.x, bounds.center.z);
        float radius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude + clearance;
        for (int i = 0; i < road.Count; i++)
        {
            Vector2 a = road[i], delta = road[(i + 1) % road.Count] - a;
            float t = Mathf.Clamp01(Vector2.Dot(center - a, delta) / Mathf.Max(0.001f, delta.sqrMagnitude));
            if ((center - (a + delta * t)).sqrMagnitude < radius * radius) return true;
        }
        return false;
    }

    static bool Intersection(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 point)
    {
        Vector2 r = b - a, s = d - c;
        float cross = r.x * s.y - r.y * s.x;
        point = default;
        if (Mathf.Abs(cross) < 0.0001f) return false;
        Vector2 delta = c - a;
        float t = (delta.x * s.y - delta.y * s.x) / cross;
        float u = (delta.x * r.y - delta.y * r.x) / cross;
        if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
        point = a + r * t;
        return true;
    }
}
