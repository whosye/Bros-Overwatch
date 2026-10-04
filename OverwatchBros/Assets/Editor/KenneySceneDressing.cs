using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Adds a separate, editable dressing layer; does not regenerate the user's landscape.
[InitializeOnLoad]
public static class KenneySceneDressing
{
    const string RootName = "KenneyDekorace";
    const string Marker = "_v3";
    const string Nature = "Assets/Models/Kenney/NatureKit/";
    const string Furniture = "Assets/Models/Kenney/FurnitureKit/";
    static readonly Dictionary<string, GameObject> Models = new Dictionary<string, GameObject>();
    static readonly List<Vector3> Reserved = new List<Vector3>();
    static System.Random random;
    static int retries;
    static int placed;

    static KenneySceneDressing()
    {
        EditorApplication.delayCall += Run;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += Run;
    }

    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var map = GameObject.Find("Map-Domasov");
        if (map == null || map.transform.Find(RootName + "/" + Marker) != null) return;
        if (!Build(map.transform) && retries++ < 60) EditorApplication.delayCall += Run;
    }

    [MenuItem("BrosOverwatch/Mapa/Zkraslit scenu Kenney assety")]
    static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var map = GameObject.Find("Map-Domasov");
        if (map != null) Build(map.transform);
    }

    static bool Build(Transform map)
    {
        string[] trees = { "tree_oak", "tree_default", "tree_pineTallA", "tree_pineRoundA", "tree_pineDefaultB" };
        string[] shrubs = { "plant_bush", "plant_bushDetailed", "plant_bushSmall", "rock_smallA", "rock_smallC" };
        string[] flowers = { "flower_redA", "flower_yellowB", "flower_purpleA", "grass", "grass_large" };
        string[] props = { "table", "chair", "bench", "bedSingle", "bookcaseOpen", "books", "radio", "rugRectangle",
            "lampRoundFloor", "lampRoundTable", "kitchenCabinet", "kitchenSink", "kitchenStove", "kitchenFridgeSmall",
            "pottedPlant", "sideTable", "coatRackStanding", "cardboardBoxClosed", "televisionVintage", "cabinetTelevision" };
        Models.Clear();
        foreach (string n in trees) if (!Load(Nature, n)) return false;
        foreach (string n in shrubs) if (!Load(Nature, n)) return false;
        foreach (string n in flowers) if (!Load(Nature, n)) return false;
        foreach (string n in new[] { "campfire_stones", "log_stack", "pot_small" }) if (!Load(Nature, n)) return false;
        foreach (string n in props) if (!Load(Furniture, n)) return false;

        var old = map.Find(RootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = MapBuildKit.Group(map, RootName);
        random = new System.Random(2604);
        placed = 0;
        Reserved.Clear();
        foreach (var t in map.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("SpawnPoint") || t.name.StartsWith("CapturePoint")) Reserved.Add(t.position);
        Physics.SyncTransforms();
        Bounds plot = MapBuildKit.GroundBounds(map);
        var woodland = MapBuildKit.Group(root, "StromyPoOkraji");
        int treeCount = 0;
        for (int i = 0; i < 240 && treeCount < 34; i++)
        {
            Vector3 p = RandomPoint(plot, 3f);
            float edge = Mathf.Min(p.x - plot.min.x, plot.max.x - p.x, p.z - plot.min.z, plot.max.z - p.z);
            if (edge > 12f || !Outdoor(ref p, 2f)) continue;
            var tree = Place(woodland, trees[random.Next(trees.Length)], p, Range(6.5f, 10.5f), Range(0f, 360f));
            Bounds b = BoundsOf(tree);
            var trunk = tree.AddComponent<CapsuleCollider>();
            trunk.center = new Vector3(0f, b.size.y * 0.3f, 0f);
            trunk.height = b.size.y * 0.6f;
            trunk.radius = 0.24f;
            Physics.SyncTransforms();
            treeCount++;
        }
        var undergrowth = MapBuildKit.Group(root, "KereKamenyKvetiny");
        for (int i = 0; i < 170; i++)
        {
            Vector3 p = RandomPoint(plot, 2f);
            float edge = Mathf.Min(p.x - plot.min.x, plot.max.x - p.x, p.z - plot.min.z, plot.max.z - p.z);
            if (edge > 17f || !Outdoor(ref p, 0.65f)) continue;
            Place(undergrowth, shrubs[random.Next(shrubs.Length)], p, Range(0.35f, 1.1f), Range(0f, 360f));
            for (int j = 0; j < 3; j++)
            {
                Vector3 q = p + new Vector3(Range(-1.5f, 1.5f), 0f, Range(-1.5f, 1.5f));
                if (Outdoor(ref q, 0.2f)) Place(undergrowth, flowers[random.Next(flowers.Length)], q, Range(0.15f, 0.45f), Range(0f, 360f));
            }
        }

        // Social spaces in clear lawns, with no new obstacles across circulation routes.
        var garden = MapBuildKit.Group(root, "ZahradniPosezeni");
        for (int i = 0, sites = 0; i < 100 && sites < 3; i++)
        {
            Vector3 p = RandomPoint(plot, 12f);
            if (!Outdoor(ref p, 4f)) continue;
            var table = Place(garden, "table", p, 0.8f, 0f, true);
            Bounds tableBounds = BoundsOf(table);
            Place(garden, "pot_small", new Vector3(p.x, tableBounds.max.y, p.z), 0.2f, 0f);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 q = p + new Vector3(side * 1.8f, 0f, 0f);
                if (Outdoor(ref q, 0.6f)) Place(garden, "chair", q, 0.95f, side < 0f ? 90f : -90f, true);
            }
            sites++;
        }
        var cottage = map.Find("MensiChata");
        if (cottage != null) FurnishCottage(root, cottage);
        var kitchen = map.Find("HlavniChata/PRIZEMI/kuchyn");
        if (kitchen != null) FurnishKitchen(root, kitchen);
        new GameObject(Marker).transform.SetParent(root, false);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        Debug.Log($"[Kenney] Scena doplnena: {placed} modelu, z toho {treeCount} stromu. Uloz scenu (Ctrl+S).");
        return true;
    }

    static bool Load(string folder, string name)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(folder + name + ".glb");
        if (model == null) return false;
        Models[name] = model;
        return true;
    }

    static float Range(float a, float b) => a + (b - a) * (float)random.NextDouble();
    static Vector3 RandomPoint(Bounds b, float margin) => new Vector3(Range(b.min.x + margin, b.max.x - margin),
        0f, Range(b.min.z + margin, b.max.z - margin));

    static bool Outdoor(ref Vector3 p, float radius)
    {
        foreach (var spawn in Reserved)
            if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(spawn.x, spawn.z)) < radius + 4f) return false;
        if (!Physics.Raycast(p + Vector3.up * 50f, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (hit.point.y > 0.3f || hit.point.y < -0.2f) return false;
        // Only the real ground, never roofs, platforms or tunnel ramps.
        string name = hit.collider.name;
        if (!name.StartsWith("Tile_") && name != "Ground" && name != "Travnik") return false;
        p.y = hit.point.y + 0.01f;
        foreach (var c in Physics.OverlapBox(p + Vector3.up, new Vector3(radius, 0.9f, radius),
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            if (c.bounds.max.y > p.y + 0.2f) return false;
        // Keep existing gravel and paved circulation routes clear.
        var map = GameObject.Find("Map-Domasov").transform;
        foreach (var r in map.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.name != "Sterk" && r.name != "Dlazba") continue;
            Bounds b = r.bounds;
            if (p.x >= b.min.x - radius && p.x <= b.max.x + radius && p.z >= b.min.z - radius && p.z <= b.max.z + radius) return false;
        }
        return true;
    }

    static GameObject Place(Transform parent, string name, Vector3 foot, float height, float yaw, bool collision = false)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = foot;
        var model = (GameObject)PrefabUtility.InstantiatePrefab(Models[name]);
        model.transform.SetParent(root.transform, false);
        Bounds b = BoundsOf(root);
        if (b.size.y > 0.0001f) model.transform.localScale *= height / b.size.y;
        b = BoundsOf(root);
        model.transform.position += new Vector3(foot.x - b.center.x, foot.y - b.min.y, foot.z - b.center.z);
        b = BoundsOf(root);
        if (collision)
        {
            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(b.center);
            box.size = b.size;
        }
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (collision) Physics.SyncTransforms();
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
        placed++;
        return root;
    }

    static Bounds BoundsOf(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    static void FurnishCottage(Transform root, Transform cottage)
    {
        var stairs = cottage.Find("Stairs");
        var roof = cottage.Find("Prism");
        if (stairs == null || roof == null) return;
        var rb = roof.GetComponent<Renderer>().bounds;
        var floor = cottage.Find("Cube")?.GetComponent<Renderer>();
        if (floor == null) return;
        float y = floor.bounds.max.y + 0.015f;
        var room = MapBuildKit.Group(root, "MalaChataInterier");
        // Furniture hugs the right/back walls, leaving the stair and door corridor open.
        float x = rb.max.x - 3.2f, z = rb.min.z + 2.7f;
        var bed = Place(room, "bedSingle", new Vector3(x, y, z), 0.8f, 0f, true);
        Place(room, "sideTable", new Vector3(x - 2f, y, z), 0.65f, 0f, true);
        Place(room, "lampRoundTable", new Vector3(x - 2f, y + 0.65f, z), 0.4f, 0f);
        Place(room, "bookcaseOpen", new Vector3(rb.center.x, y, rb.min.z + 1.5f), 2f, 0f, true);
        Place(room, "rugRectangle", new Vector3(rb.center.x + 2f, y + 0.01f, rb.center.z), 0.025f, 0f);
        var cabinet = Place(room, "cabinetTelevision", new Vector3(x, y, rb.center.z + 2f), 0.7f, -90f, true);
        Place(room, "televisionVintage", new Vector3(x, BoundsOf(cabinet).max.y, rb.center.z + 2f), 0.55f, -90f);
        var sideTable = Place(room, "sideTable", new Vector3(x - 2f, y, rb.center.z + 2f), 0.65f, 0f, true);
        Place(room, "books", new Vector3(x - 2f, BoundsOf(sideTable).max.y, rb.center.z + 2f), 0.2f, 0f);
        Place(room, "coatRackStanding", new Vector3(rb.max.x - 2f, y, rb.max.z - 3.5f), 1.6f, 0f);
        Place(room, "pottedPlant", new Vector3(rb.max.x - 2f, y, rb.max.z - 2f), 1f, 0f);
        var lamp = Place(room, "lampRoundFloor", new Vector3(rb.center.x + 1f, y, rb.min.z + 2f), 1.65f, 0f);
        WarmLight(lamp.transform, 1.4f, 5f);
        var outside = MapBuildKit.Group(root, "TerasaMaleChaty");
        Vector3 bench = new Vector3(rb.center.x + 3f, y, rb.max.z + 4f);
        Place(outside, "bench", bench, 1f, 180f, true);
        Place(outside, "log_stack", new Vector3(rb.min.x + 2f, y, rb.max.z + 5f), 0.8f, 90f, true);
        Place(outside, "pottedPlant", bench + Vector3.right * 2.5f, 0.8f, 0f);
        Place(outside, "cardboardBoxClosed", new Vector3(rb.min.x + 3.5f, y, rb.max.z + 5f), 0.55f, 20f, true);
    }

    static void FurnishKitchen(Transform root, Transform kitchen)
    {
        var rs = kitchen.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        if (b.size.x < 4f || b.size.z < 4f) return;
        var room = MapBuildKit.Group(root, "KuchynHlavniChaty");
        float y = Mathf.Max(0.03f, b.min.y), z = b.min.z + 1.3f;
        string[] units = { "kitchenFridgeSmall", "kitchenCabinet", "kitchenSink", "kitchenStove" };
        for (int i = 0; i < units.Length; i++)
        {
            float x = b.min.x + 1.4f + i * 1.5f;
            if (x > b.max.x - 1.3f) break;
            var unit = Place(room, units[i], new Vector3(x, y, z), i == 0 ? 1.4f : 0.9f, 0f, true);
            if (i == 1) Place(room, "radio", new Vector3(x, BoundsOf(unit).max.y, z), 0.25f, 0f);
        }
        Place(room, "pottedPlant", new Vector3(b.max.x - 1.5f, y, b.max.z - 1.5f), 0.9f, 0f);
    }

    static void WarmLight(Transform parent, float y, float range)
    {
        var light = new GameObject("TepleSvetlo").AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.transform.localPosition = new Vector3(0f, y, 0f);
        light.type = LightType.Point;
        light.color = new Color(1f, 0.78f, 0.48f);
        light.intensity = 1.2f;
        light.range = range;
        light.shadows = LightShadows.None;
    }
}
