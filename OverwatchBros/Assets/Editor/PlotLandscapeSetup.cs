using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Landscape is derived from the current ground tiles, preserving the tunnel openings.
[InitializeOnLoad]
public static class PlotLandscapeSetup
{
    const string RootName = "Pozemek";
    const string Version = "_v2";
    static int retries;

    static PlotLandscapeSetup()
    {
        EditorApplication.delayCall += Run;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += Run;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Run;
        };
    }

    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var map = GameObject.Find("Map-Domasov");
        if (map == null || map.transform.Find(RootName + "/" + Version) != null) return;
        if (Build(map.transform))
        {
            EditorSceneManager.MarkSceneDirty(map.scene);
            Debug.Log("[Pozemek] Travnik, oploceni a cesty pripraveny. Uloz scenu (Ctrl+S).");
        }
        else if (retries++ < 30) EditorApplication.delayCall += Run;
    }

    [MenuItem("BrosOverwatch/Mapa/Upravit pozemek podle reference")]
    static void Rebuild()
    {
        var map = GameObject.Find("Map-Domasov");
        if (map != null && Build(map.transform)) EditorSceneManager.MarkSceneDirty(map.scene);
    }

    static bool Build(Transform map)
    {
        var lawn = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Garden/Lawn.mat");
        var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wood/Wood_Beam.mat");
        var gravel = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Garden/Gravel.mat");
        var tiles = map.Find("Tunel/Ground_Tiles");
        if (lawn == null || wood == null || gravel == null || tiles == null) return false;
        var ground = MapBuildKit.GroundBounds(map);
        var surfaces = new List<Bounds>();
        foreach (var renderer in tiles.GetComponentsInChildren<MeshRenderer>()) surfaces.Add(renderer.bounds);
        if (surfaces.Count == 0) return false;

        var old = map.Find(RootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = MapBuildKit.Group(map, RootName);
        var grass = MapBuildKit.Group(root, "Travnik");
        // Metre-based UVs avoid stretching a single grass texture over an entire ground tile.
        foreach (var b in surfaces)
            MapBuildKit.Box(grass, "Travnik", new Vector3(b.center.x, b.max.y + 0.003f, b.center.z),
                new Vector3(b.size.x, 0.006f, b.size.z), Quaternion.identity, lawn, false, true, false);

        var fence = MapBuildKit.Group(root, "Oploceni");
        float x0 = ground.min.x + 0.3f, x1 = ground.max.x - 0.3f;
        float z0 = ground.min.z + 0.3f, z1 = ground.max.z - 0.3f;
        var spawns = new List<Vector3>();
        foreach (var t in map.GetComponentsInChildren<Transform>())
            if (t.name.StartsWith("SpawnPoint")) spawns.Add(t.position);
        Fence(fence, new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), wood, spawns, surfaces, false);
        Fence(fence, new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), wood, spawns, surfaces, false);
        Fence(fence, new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1), wood, spawns, surfaces, false);
        // A wide entrance on the road side, like the reference plot.
        Fence(fence, new Vector3(x0, 0f, z1), new Vector3(x0, 0f, z0), wood, spawns, surfaces, true);

        var paths = MapBuildKit.Group(root, "Cesty");
        var house = BuildingBounds(map.Find("HlavniChata/PRIZEMI"));
        var cottage = BuildingBounds(map.Find("MensiChata"));
        if (house.HasValue && cottage.HasValue)
        {
            var h = house.Value;
            var c = cottage.Value;
            float laneZ = Mathf.Max(h.max.z, c.max.z) + 3f;
            float roadX = x0 + 4f;
            float endX = Mathf.Min(x1 - 3f, Mathf.Max(h.max.x, c.max.x) + 3f);
            Path(paths, new Vector3(roadX, 0f, laneZ), new Vector3(endX, 0f, laneZ), 3.2f, gravel, surfaces);
            Path(paths, new Vector3(roadX, 0f, ground.min.z + 2f), new Vector3(roadX, 0f, ground.max.z - 2f),
                3.2f, gravel, surfaces);
            Path(paths, new Vector3(h.center.x, 0f, laneZ), new Vector3(h.center.x, 0f, h.max.z + 0.5f),
                2f, gravel, surfaces);
            Path(paths, new Vector3(c.center.x, 0f, laneZ), new Vector3(c.center.x, 0f, c.max.z + 0.5f),
                2f, gravel, surfaces);
            var pool = BuildingBounds(map.Find("OkoliChaty/Bazen"));
            if (pool.HasValue && pool.Value.max.z < laneZ)
                Path(paths, new Vector3(pool.Value.center.x, 0f, laneZ),
                    new Vector3(pool.Value.center.x, 0f, pool.Value.max.z + 0.7f), 1.6f, gravel, surfaces);
        }
        new GameObject(Version).transform.SetParent(root, false);
        return true;
    }

    static Bounds? BuildingBounds(Transform building)
    {
        if (building == null) return null;
        Bounds? result = null;
        foreach (var renderer in building.GetComponentsInChildren<MeshRenderer>())
        {
            if (result == null) result = renderer.bounds;
            else { var b = result.Value; b.Encapsulate(renderer.bounds); result = b; }
        }
        return result;
    }

    static void Fence(Transform root, Vector3 from, Vector3 to, Material material,
        List<Vector3> spawns, List<Bounds> surfaces, bool entrance)
    {
        if (root.parent != null && root.parent.parent != null && root.parent.parent.Find("LesniHranice") != null) return;
        int count = Mathf.CeilToInt(Vector3.Distance(from, to) / 3f);
        for (int i = 0; i < count; i++)
        {
            Vector3 a = Vector3.Lerp(from, to, (float)i / count);
            Vector3 b = Vector3.Lerp(from, to, (float)(i + 1) / count);
            Vector3 mid = (a + b) * 0.5f;
            if (entrance && Mathf.Abs(i - count / 2) <= 1) continue;
            if (spawns.Exists(p => Vector2.Distance(new Vector2(p.x, p.z), new Vector2(mid.x, mid.z)) < 3f)) continue;
            float y = SurfaceY(mid, surfaces);
            if (float.IsNaN(y)) continue;
            a.y = b.y = y;
            MapBuildKit.Box(root, "Sloupek", a + Vector3.up * 0.7f, new Vector3(0.16f, 1.4f, 0.16f), material, true);
            MapBuildKit.Box(root, "Sloupek", b + Vector3.up * 0.7f, new Vector3(0.16f, 1.4f, 0.16f), material, true);
            MapBuildKit.Beam(root, "Pricka", a + Vector3.up * 0.48f, b + Vector3.up * 0.48f, 0.12f, material, true);
            MapBuildKit.Beam(root, "Pricka", a + Vector3.up * 1.08f, b + Vector3.up * 1.08f, 0.12f, material, true);
        }
    }

    static float SurfaceY(Vector3 p, List<Bounds> surfaces)
    {
        foreach (var b in surfaces)
            if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z) return b.max.y;
        return float.NaN;
    }

    static void Path(Transform root, Vector3 from, Vector3 to, float width, Material material, List<Bounds> surfaces)
    {
        // Clip each path against the ground tiles so tunnel mouths stay open.
        bool alongX = Mathf.Abs(to.x - from.x) > Mathf.Abs(to.z - from.z);
        Rect path = alongX
            ? Rect.MinMaxRect(Mathf.Min(from.x, to.x), from.z - width / 2f, Mathf.Max(from.x, to.x), from.z + width / 2f)
            : Rect.MinMaxRect(from.x - width / 2f, Mathf.Min(from.z, to.z), from.x + width / 2f, Mathf.Max(from.z, to.z));
        foreach (var b in surfaces)
        {
            float x0 = Mathf.Max(path.xMin, b.min.x), x1 = Mathf.Min(path.xMax, b.max.x);
            float z0 = Mathf.Max(path.yMin, b.min.z), z1 = Mathf.Min(path.yMax, b.max.z);
            if (x1 <= x0 || z1 <= z0) continue;
            MapBuildKit.Box(root, "Sterk", new Vector3((x0 + x1) / 2f, b.max.y + 0.018f, (z0 + z1) / 2f),
                new Vector3(x1 - x0, 0.008f, z1 - z0), Quaternion.identity, material, false, true, false);
        }
    }
}
