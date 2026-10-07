using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Dresses the existing playable cottage; original walls, stairs and collision stay in place.
[InitializeOnLoad]
public static class SmallCottageSetup
{
    const string RootName = "ExterierMaleChaty";
    const string Version = "_v2";
    static int retries;

    static SmallCottageSetup()
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
        var cottage = map != null ? map.transform.Find("MensiChata") : null;
        if (cottage == null) return;
        if (cottage.Find(RootName + "/" + Version) != null)
        {
            if (DisableOldRoofCollision(cottage))
            {
                EditorSceneManager.MarkSceneDirty(cottage.gameObject.scene);
                Debug.Log("[Mensi chata] Stara strecha uz nema kolizi (prekazela strelbe nad novou strechou). Uloz scenu (Ctrl+S).");
            }
            return;
        }
        if (Build(cottage))
        {
            EditorSceneManager.MarkSceneDirty(cottage.gameObject.scene);
            Debug.Log("[Mensi chata] Fasada, sedlova strecha, okna a vstupni veranda hotove. Uloz scenu (Ctrl+S).");
        }
        else if (retries++ < 30) EditorApplication.delayCall += Run;
    }

    // Puvodni strecha (Prism) je skryta, ale jeji kolize mela jiny tvar nez nova taskova strecha:
    // nad strechou tak zustaly neviditelne kliny, pres ktere neslo strilet. Nova strecha ma kolizi vlastni.
    static bool DisableOldRoofCollision(Transform cottage)
    {
        var newRoof = cottage.Find(RootName + "/Strecha");
        if (newRoof == null || newRoof.GetComponentInChildren<Collider>() == null) return false;

        bool changed = false;

        // Stity (trojuhelniky na koncich strechy) byly jen vzhled - drive je kryla stara strecha.
        foreach (Transform part in newRoof)
            if (part.name == "DrevenyStit" && part.GetComponent<Collider>() == null)
            {
                var filter = part.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                part.gameObject.AddComponent<MeshCollider>().sharedMesh = TwoSided(filter.sharedMesh);
                changed = true;
            }

        foreach (Transform child in cottage)
        {
            if (child.name != "Prism") continue;
            var renderer = child.GetComponent<MeshRenderer>();
            var collider = child.GetComponent<Collider>();
            if (renderer != null && !renderer.enabled && collider != null && collider.enabled)
            {
                collider.enabled = false;
                changed = true;
            }
        }
        return changed;
    }

    // Kolize z obou stran (plochy trojuhelnik by jinak zastavil jen strely z jedne strany).
    static Mesh TwoSided(Mesh source)
    {
        var triangles = source.triangles;
        var both = new int[triangles.Length * 2];
        triangles.CopyTo(both, 0);
        for (int i = 0; i < triangles.Length; i += 3)
        {
            both[triangles.Length + i] = triangles[i];
            both[triangles.Length + i + 1] = triangles[i + 2];
            both[triangles.Length + i + 2] = triangles[i + 1];
        }
        var mesh = new Mesh { name = source.name + "_Kolize", vertices = source.vertices, triangles = both };
        mesh.RecalculateBounds();
        return mesh;
    }

    [MenuItem("BrosOverwatch/Mapa/Vymodelovat mensi chatu znovu")]
    static void Rebuild()
    {
        var map = GameObject.Find("Map-Domasov");
        var cottage = map != null ? map.transform.Find("MensiChata") : null;
        if (cottage != null && Build(cottage)) EditorSceneManager.MarkSceneDirty(cottage.gameObject.scene);
    }

    static bool Build(Transform cottage)
    {
        var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/House/DarkBoards.mat");
        var roofMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/House/RoofTiles.mat");
        var stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Stone/Stone_Wall.mat");
        var glass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/House/Glass.mat");
        var iron = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Garden/Iron.mat");
        var planks = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wood/Wood_Planks.mat");
        if (wood == null || roofMat == null || stone == null || glass == null || iron == null || planks == null) return false;
        var plaster = MapBuildKit.Mat("Assets/Materials/House/SmallCottage_Plaster.mat",
            "Assets/Materials/House/Plaster.png", new Color(0.83f, 0.81f, 0.69f), new Vector2(0.5f, 0.5f));
        if (plaster == null) return false;

        var walls = new List<Bounds>();
        MeshRenderer roof = null;
        foreach (var r in cottage.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (IsDecoration(r.transform, cottage)) continue;
            if (r.name == "Prism") roof = r;
            else if (r.bounds.size.y > 3f && Mathf.Min(r.bounds.size.x, r.bounds.size.z) < 0.8f
                && r.bounds.min.y > 2f) walls.Add(r.bounds);
        }
        if (roof == null || walls.Count == 0) return false;
        Bounds rb = roof.bounds;
        // The same parent also contains retaining walls and the terrace parapet.
        // Only walls beneath the main roof define the cottage and its entrance.
        walls.RemoveAll(b => b.center.x < rb.min.x || b.center.x > rb.max.x
            || b.center.z < rb.min.z || b.center.z > rb.max.z);
        if (walls.Count == 0) return false;
        Bounds footprint = walls[0];
        foreach (var b in walls) footprint.Encapsulate(b);
        float floor = footprint.min.y, eave = rb.min.y;

        var old = cottage.Find(RootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = MapBuildKit.Group(cottage, RootName);
        // Real wall geometry is reused, so existing door and window openings remain usable.
        foreach (var r in cottage.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (IsDecoration(r.transform, cottage)) continue;
            Material material = r == roof ? roofMat : r.name.Contains("Stairs") ? planks
                : r.bounds.max.y <= floor + 0.2f ? stone
                : r.bounds.size.y < 0.8f ? planks
                : r.bounds.min.y >= floor + 4f ? wood : plaster;
            var materials = r.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            r.sharedMaterials = materials;
        }

        var facade = MapBuildKit.Group(root, "Fasada");
        foreach (var b in walls)
        {
            bool xWall = b.size.x < b.size.z;
            float edge = xWall ? (Mathf.Abs(b.center.x - footprint.min.x) < Mathf.Abs(b.center.x - footprint.max.x)
                ? footprint.min.x : footprint.max.x) : (Mathf.Abs(b.center.z - footprint.min.z) < Mathf.Abs(b.center.z - footprint.max.z)
                ? footprint.min.z : footprint.max.z);
            float wallEdge = xWall ? b.center.x : b.center.z;
            if (Mathf.Abs(edge - wallEdge) > 0.7f) continue;
            Vector3 outward = xWall ? (b.center.x < footprint.center.x ? Vector3.left : Vector3.right)
                : (b.center.z < footprint.center.z ? Vector3.back : Vector3.forward);
            Vector3 center = b.center + outward * ((xWall ? b.size.x : b.size.z) * 0.5f + 0.015f);
            Vector3 size = xWall ? new Vector3(0.025f, b.size.y, b.size.z) : new Vector3(b.size.x, b.size.y, 0.025f);
            MapBuildKit.Box(facade, "Obklad", center, size, Quaternion.identity,
                b.min.y >= floor + 4f ? wood : plaster, false, false, false);
            if (b.min.y <= floor + 0.2f)
            {
                center.y = floor + 0.28f;
                size.y = 0.55f;
                MapBuildKit.Box(facade, "KamennySokl", center + outward * 0.02f, size, stone, false);
            }
        }

        var roofRoot = MapBuildKit.Group(root, "Strecha");
        float half = rb.size.z * 0.5f + 0.3f, rise = rb.size.y;
        float slope = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
        float slant = Mathf.Sqrt(half * half + rise * rise);
        for (int side = -1; side <= 1; side += 2)
        {
            var rotation = Quaternion.Euler(side * slope, 0f, 0f);
            Vector3 center = new Vector3(rb.center.x, eave + rise * 0.5f + 0.08f, rb.center.z + side * half * 0.5f);
            MapBuildKit.Box(roofRoot, "TaskovaStrecha", center, new Vector3(rb.size.x + 0.65f, 0.16f, slant),
                rotation, roofMat, true);
            MapBuildKit.Beam(roofRoot, "Okap", new Vector3(rb.min.x - 0.3f, eave, rb.center.z + side * half),
                new Vector3(rb.max.x + 0.3f, eave, rb.center.z + side * half), 0.16f, iron);
        }
        // The old roof keeps its collision but its white mesh is replaced by the detailed roof.
        roof.enabled = false;
        var oldRoofCollider = roof.GetComponent<Collider>();
        if (oldRoofCollider != null) oldRoofCollider.enabled = false;
        foreach (float x in new[] { rb.min.x - 0.04f, rb.max.x + 0.04f })
        {
            var points = new[] { new Vector3(x, eave, rb.min.z), new Vector3(x, eave, rb.max.z),
                new Vector3(x, rb.max.y, rb.center.z) };
            var uv = new[] { new Vector2(0f, 0f), new Vector2(rb.size.z, 0f), new Vector2(rb.size.z / 2f, rise) };
            // Two-sided material lets both ends render regardless of triangle winding.
            var gableMat = MapBuildKit.Mat("Assets/Materials/House/SmallCottage_Gable.mat",
                "Assets/Materials/House/DarkBoards.png", Color.white, Vector2.one, doubleSided: true);
            MapBuildKit.MeshObject(roofRoot, "DrevenyStit", MapBuildKit.FlatMesh("MensiChataStit", points, uv),
                Vector3.zero, Quaternion.identity, Vector3.one, gableMat);
            foreach (float z in new[] { rb.min.z, rb.max.z })
                MapBuildKit.Beam(roofRoot, "Zavetri", new Vector3(x, eave, z), new Vector3(x, rb.max.y + 0.08f, rb.center.z), 0.18f, wood);
        }
        MapBuildKit.Beam(roofRoot, "Hreben", new Vector3(rb.min.x - 0.3f, rb.max.y + 0.13f, rb.center.z),
            new Vector3(rb.max.x + 0.3f, rb.max.y + 0.13f, rb.center.z), 0.22f, roofMat);
        Vector3 chimney = new Vector3(rb.center.x + rb.size.x * 0.22f, rb.max.y + 0.5f, rb.center.z);
        MapBuildKit.Box(roofRoot, "Komin", chimney, new Vector3(0.8f, 1.8f, 0.8f), stone, true);
        MapBuildKit.Box(roofRoot, "KominovaHlavice", chimney + Vector3.up * 0.95f, new Vector3(1f, 0.14f, 1f), iron, false);

        // Locate the real front door by the gap between existing front wall pieces.
        float front = footprint.max.z;
        var spans = walls.FindAll(b => Mathf.Abs(b.max.z - front) < 0.7f && b.min.y < floor + 0.2f && b.size.z < 0.8f);
        spans.Sort((a, b) => a.min.x.CompareTo(b.min.x));
        float doorX = footprint.center.x, doorWidth = 0f;
        for (int i = 1; i < spans.Count; i++)
        {
            float gap = spans[i].min.x - spans[i - 1].max.x;
            if (gap >= 1f && gap <= 4f) { doorX = (spans[i].min.x + spans[i - 1].max.x) / 2f; doorWidth = gap; break; }
        }
        if (doorWidth > 0f) Porch(root, doorX, front, floor, doorWidth, wood, roofMat);

        var windows = MapBuildKit.Group(root, "Okna");
        // Surface windows on solid wall sections; no glass or frame blocks an existing opening.
        foreach (var b in walls)
        {
            if (b.min.y > floor + 0.2f || b.size.y < 3f) continue;
            if (b.size.z < 0.8f && b.size.x > 4f)
            {
                float sign = b.center.z < footprint.center.z ? -1f : 1f;
                Window(windows, new Vector3(b.center.x, floor + 2.15f, b.center.z + sign * (b.size.z / 2f + 0.05f)),
                    false, glass, wood);
            }
            else if (b.size.x < 0.8f && b.size.z > 6f)
            {
                float sign = b.center.x < footprint.center.x ? -1f : 1f;
                foreach (float t in new[] { 0.3f, 0.7f })
                    Window(windows, new Vector3(b.center.x + sign * (b.size.x / 2f + 0.05f), floor + 2.15f,
                        Mathf.Lerp(b.min.z, b.max.z, t)), true, glass, wood);
            }
        }
        new GameObject(Version).transform.SetParent(root, false);
        DisableOldRoofCollision(cottage);
        return true;
    }

    static bool IsDecoration(Transform part, Transform cottage)
    {
        for (; part != null && part != cottage; part = part.parent)
            if (part.name == RootName) return true;
        return false;
    }

    static void Window(Transform root, Vector3 center, bool side, Material glass, Material wood)
    {
        Vector3 Size(float w, float h, float d) => side ? new Vector3(d, h, w) : new Vector3(w, h, d);
        Vector3 along = side ? Vector3.forward : Vector3.right;
        MapBuildKit.Box(root, "Sklo", center, Size(1.5f, 1.5f, 0.04f), glass, false);
        foreach (float sign in new[] { -1f, 1f })
        {
            MapBuildKit.Box(root, "Ram", center + along * sign * 0.82f, Size(0.13f, 1.76f, 0.1f), wood, false);
            MapBuildKit.Box(root, "Ram", center + Vector3.up * sign * 0.82f, Size(1.76f, 0.13f, 0.1f), wood, false);
            MapBuildKit.Box(root, "Okenice", center + along * sign * 1.13f, Size(0.45f, 1.7f, 0.07f), wood, false);
        }
        MapBuildKit.Box(root, "Pricnik", center, Size(0.08f, 1.5f, 0.08f), wood, false);
        MapBuildKit.Box(root, "Parapet", center - Vector3.up * 0.9f, Size(1.9f, 0.12f, 0.32f), wood, false);
    }

    static void Porch(Transform root, float x, float z, float floor, float width, Material wood, Material roof)
    {
        var porch = MapBuildKit.Group(root, "Veranda");
        float half = width / 2f + 0.6f, height = 4.3f;
        foreach (float sign in new[] { -1f, 1f })
        {
            MapBuildKit.Box(porch, "Zaruben", new Vector3(x + sign * (width / 2f + 0.08f), floor + 1.95f, z + 0.06f),
                new Vector3(0.14f, 3.9f, 0.16f), wood, false);
            MapBuildKit.Box(porch, "Sloupek", new Vector3(x + sign * half, floor + height / 2f, z + 3f),
                new Vector3(0.18f, height, 0.18f), wood, true);
        }
        MapBuildKit.Box(porch, "Preklad", new Vector3(x, floor + 3.95f, z + 0.06f), new Vector3(width + 0.3f, 0.18f, 0.16f), wood, false);
        MapBuildKit.Box(porch, "Striska", new Vector3(x, floor + height + 0.25f, z + 1.5f),
            new Vector3(half * 2f + 0.5f, 0.14f, 3.7f), Quaternion.Euler(9f, 0f, 0f), roof, true);
        MapBuildKit.Beam(porch, "Tram", new Vector3(x - half, floor + height, z + 3f),
            new Vector3(x + half, floor + height, z + 3f), 0.18f, wood);
    }
}
