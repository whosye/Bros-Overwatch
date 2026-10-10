using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Uprava vzhledu mapy Domasov (idempotentne, sama po kompilaci / otevreni sceny):
//  - Rozhledna je drevena: sloupky a zabradli z tramu, podlahy a steny z prken, strecha ze sindelu;
//    podstavec (kopec s rampami a zadni zed) je kamenny. Kulata plosina uprostred mapy (Rozhledna/Cylinder) se nemeni.
//  - Detaily rozhledny (Map-Domasov/RozhlednaDetaily): krizove vzpery, zabradli na plosinach, sedlova strecha
//    s presahem, vlajka a lucerny. Strecha ma kolize odpovidajici viditelnym plocham.
//  - Na mapu se postavi auto Toyota Yaris (Assets/Models/ToyotaYaris, CC-BY 4.0 - viz CREDITS.txt).
// Scenu je pak potreba ulozit (Ctrl+S). Rucne: BrosOverwatch > Mapa > Upravit vzhled znovu.
[InitializeOnLoad]
public static class MapDressingSetup
{
    const string MapName = "Map-Domasov";
    const string WoodFolder = "Assets/Materials/Wood";
    const string CarPath = "Assets/Models/ToyotaYaris/scene.gltf";
    const string CarName = "ToyotaYaris";

    // Auto parkuje u zapadni zdi hlavni chaty (podelne s ni), mimo spawny a body.
    static readonly Vector3 CarPosition = new Vector3(2.5f, 0f, -7f);
    const float CarYaw = 0f;
    const float CarLength = 3.8f;

    static MapDressingSetup()
    {
        EditorApplication.delayCall += RunIfNeeded;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += RunIfNeeded;
        };
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += RunIfNeeded;
    }

    static int retries;

    static void RunIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        var map = GameObject.Find(MapName);
        if (map == null) return;

        MapBuildKit.DumpLive(map.transform);
        bool changed = ApplyWood(map.transform, false);
        changed |= BuildTowerDetails(map.transform, false);
        changed |= RepairTowerRoofCollision(map.transform);
        changed |= PlaceCar(map.transform, false);

        // Textury nebo model se mozna jeste importuji: kdyz neco chybi, zkus to za chvili znovu.
        bool done = map.transform.Find("Rozhledna/" + DetailsName + "/" + DetailsVersion) != null && map.transform.Find("Props/" + CarName) != null;
        if (!done && retries++ < 30)
            EditorApplication.delayCall += RunIfNeeded;

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(map.scene);
            Debug.Log("[Mapa] Rozhledna je drevena a auto stoji u hlavni chaty. Uloz scenu (Ctrl+S).");
        }
    }

    [MenuItem("BrosOverwatch/Mapa/Upravit vzhled znovu")]
    static void RerunMenu()
    {
        var map = GameObject.Find(MapName);
        if (map == null) return;

        ApplyWood(map.transform, true);
        BuildTowerDetails(map.transform, true);
        RepairTowerRoofCollision(map.transform);
        PlaceCar(map.transform, true);
        EditorSceneManager.MarkSceneDirty(map.scene);
    }

    // ---------------- drevena rozhledna ----------------

    static bool ApplyWood(Transform map, bool force)
    {
        var tower = map.Find("Rozhledna");
        if (tower == null) return false;

        var planks = WoodMaterial("Wood_Planks", "WoodPlanks", new Color(1f, 1f, 1f));
        var beams = WoodMaterial("Wood_Beam", "WoodBeam", new Color(1f, 1f, 1f));
        var shingles = WoodMaterial("Wood_Shingles", "WoodShingles", new Color(1f, 1f, 1f));
        var stone = StoneMaterial();
        if (planks == null || beams == null || shingles == null || stone == null) return false;

        bool changed = false;
        foreach (var renderer in tower.GetComponentsInChildren<MeshRenderer>(true))
        {
            // Kulata plosina uprostred mapy patri jen organizacne pod Rozhlednu.
            if (renderer.name == "Cylinder") continue;
            if (IsDetail(renderer.transform, tower)) continue;

            Material material = Pick(renderer, planks, beams, shingles, stone);
            var materials = renderer.sharedMaterials;
            bool same = true;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] != material)
                {
                    materials[i] = material;
                    same = false;
                }

            if (same && !force) continue;
            renderer.sharedMaterials = materials;
            changed = true;
        }
        return changed;
    }

    static bool IsDetail(Transform t, Transform tower)
    {
        for (; t != null && t != tower; t = t.parent)
            if (t.name == DetailsName && t.parent == tower) return true;
        return false;
    }

    // Strecha = sindele; tenke sloupky a madla = tramy; podstavec (vse, co zacina u zeme) = kamen;
    // zbytek (plosiny, lavky, rampa na vez) = prkna.
    static Material Pick(Renderer renderer, Material planks, Material beams, Material shingles, Material stone)
    {
        if (renderer.name.StartsWith("Prism"))
            return shingles;

        Vector3 size = renderer.bounds.size;
        float thin = Mathf.Min(size.x, size.z);
        bool post = thin <= 1.05f && size.y >= 2.5f && Mathf.Max(size.x, size.z) <= 1.05f;
        bool rail = Mathf.Min(thin, size.y) <= 0.55f && Mathf.Max(size.x, size.z) >= 3f && size.y <= 0.6f && thin <= 0.55f;
        if (post || rail) return beams;
        return renderer.bounds.min.y < 3f ? stone : planks;
    }

    static Material StoneMaterial()
    {
        return MapBuildKit.Mat("Assets/Materials/Stone/Stone_Wall.mat", "Assets/Materials/Stone/StoneWall.png",
            Color.white, new Vector2(0.35f, 0.35f), 0.08f);
    }

    // ---------------- detaily rozhledny ----------------
    // Vse se odvozuje z toho, co ve scene skutecne je (sloupky, plosiny, obrubniky, strisku), a vklada se jako potomek
    // rozhledny - kdyz ji nekdo posune, detaily jdou s ni.

    const string DetailsName = "Detaily";
    const string OldDetailsName = "RozhlednaDetaily";
    const string DetailsVersion = "_v2";

    class Post
    {
        public Bounds bounds;
        public Vector2 center => new Vector2(bounds.center.x, bounds.center.z);
    }

    static bool BuildTowerDetails(Transform map, bool force)
    {
        var tower = map.Find("Rozhledna");
        if (tower == null) return false;

        var existing = tower.Find(DetailsName);
        if (existing != null && existing.Find(DetailsVersion) != null && !force) return false;

        var beams = AssetDatabase.LoadAssetAtPath<Material>(WoodFolder + "/Wood_Beam.mat");
        var planks = AssetDatabase.LoadAssetAtPath<Material>(WoodFolder + "/Wood_Planks.mat");
        var shingles = AssetDatabase.LoadAssetAtPath<Material>(WoodFolder + "/Wood_Shingles.mat");
        var lamp = MapBuildKit.Mat("Assets/Materials/Garden/Lantern.mat", null, new Color(1f, 0.85f, 0.55f), Vector2.one, 0.3f,
            emission: new Color(3f, 2.2f, 1.1f));
        var iron = MapBuildKit.Mat("Assets/Materials/Garden/Iron.mat", null, new Color(0.12f, 0.12f, 0.13f), Vector2.one, 0.45f);
        var flag = MapBuildKit.Mat("Assets/Materials/Garden/FlagCZ.mat", "Assets/Materials/Garden/FlagCZ.png", Color.white, Vector2.one, 0.05f,
            doubleSided: true);
        if (beams == null || planks == null || shingles == null || flag == null) return false;

        // Stara verze stala mimo rozhlednu (pocitana z ulozeneho souboru sceny) - pryc s ni.
        var old = map.Find(OldDetailsName);
        if (old != null)
            Object.DestroyImmediate(old.gameObject);
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        // Roztrid casti rozhledny podle tvaru (skutecne obalky ve scene).
        var posts = new List<Post>();
        var curbs = new List<Bounds>();
        var obstacles = new List<Bounds>();
        var platforms = new List<Bounds>();
        Bounds? roofBounds = null;
        var prism = tower.Find("Prism");
        foreach (var renderer in tower.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.name == "Cylinder") continue;
            Bounds b = renderer.bounds;
            Vector3 size = b.size;

            if (prism != null && renderer.transform == prism)
            {
                roofBounds = b;
                continue;
            }
            if (size.x <= 1.1f && size.z <= 1.1f && size.y >= 2.5f)
            {
                posts.Add(new Post { bounds = b });
                continue;
            }
            if (size.y <= 0.6f && Mathf.Min(size.x, size.z) <= 0.6f && Mathf.Max(size.x, size.z) >= 3f)
            {
                curbs.Add(b);
                continue;
            }
            // Ploche plosiny nevadi; vsechno ostatni (rampy, lavky) muze zakryvat stenu veze.
            if (size.y <= 0.6f && Mathf.Min(size.x, size.z) >= 3f)
                platforms.Add(b);
            else if (size.y > 0.6f)
                obstacles.Add(b);
        }
        if (posts.Count < 4) return false;

        var root = new GameObject(DetailsName).transform;
        root.SetParent(tower, true);
        new GameObject(DetailsVersion).transform.SetParent(root, false);

        var braces = MapBuildKit.Group(root, "Vzpery");
        var clusters = Cluster(posts, platforms);
        float highestTop = float.MinValue;
        Vector2 highestCenter = Vector2.zero;
        float highestFirstFloor = 0f;

        foreach (var cluster in clusters)
        {
            // Rohy veze (stredy krajnich sloupku) a polovicni sirka sloupku.
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, half = 0.5f;
            foreach (var p in cluster)
            {
                minX = Mathf.Min(minX, p.center.x);
                maxX = Mathf.Max(maxX, p.center.x);
                minZ = Mathf.Min(minZ, p.center.y);
                maxZ = Mathf.Max(maxZ, p.center.y);
                half = p.bounds.extents.x;
            }
            if (maxX - minX < 1.5f || maxZ - minZ < 1.5f) continue;
            float[] xs = { minX, maxX }, zs = { minZ, maxZ };

            // Patra = ruzne vysky sloupku.
            var levels = new List<Vector2>();
            foreach (var p in cluster)
            {
                var level = new Vector2(p.bounds.min.y, p.bounds.max.y);
                if (!levels.Exists(l => Mathf.Abs(l.x - level.x) < 0.3f))
                    levels.Add(level);
            }
            levels.Sort((a, b) => a.x.CompareTo(b.x));

            foreach (var level in levels)
            {
                float height = level.y - level.x;
                RingBeams(braces, xs, zs, half, level.y - 0.15f, beams);

                // Krize jen na vyssich patrech (nejvyssi nizke patro pod strechou nechavame volne - je tam bod).
                if (height < 3.5f) continue;
                int stacks = height > 5f ? 2 : 1;
                float y0 = level.x + 0.25f, y1 = level.y - 0.25f;
                BraceFace(braces, new Vector3(xs[0], 0f, zs[0] - half - 0.12f), new Vector3(xs[1], 0f, zs[0] - half - 0.12f), y0, y1, stacks, obstacles, beams);
                BraceFace(braces, new Vector3(xs[0], 0f, zs[1] + half + 0.12f), new Vector3(xs[1], 0f, zs[1] + half + 0.12f), y0, y1, stacks, obstacles, beams);
                BraceFace(braces, new Vector3(xs[0] - half - 0.12f, 0f, zs[0]), new Vector3(xs[0] - half - 0.12f, 0f, zs[1]), y0, y1, stacks, obstacles, beams);
                BraceFace(braces, new Vector3(xs[1] + half + 0.12f, 0f, zs[0]), new Vector3(xs[1] + half + 0.12f, 0f, zs[1]), y0, y1, stacks, obstacles, beams);
            }

            float top = levels[levels.Count - 1].y;
            if (top > highestTop)
            {
                highestTop = top;
                highestCenter = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
                highestFirstFloor = levels[0].y;
            }
        }

        // Zabradli podel nizkych obrubniku, ktere uz model ma (ostatni strany plosin zustavaji volne kvuli pristupu).
        var rails = MapBuildKit.Group(root, "Zabradli");
        var lights = MapBuildKit.Group(root, "Lucerny");
        Vector3? lowestRailEnd = null;
        foreach (var curb in curbs)
        {
            bool alongX = curb.size.x > curb.size.z;
            Vector3 from = alongX ? new Vector3(curb.min.x, curb.min.y, curb.center.z) : new Vector3(curb.center.x, curb.min.y, curb.min.z);
            Vector3 to = alongX ? new Vector3(curb.max.x, curb.min.y, curb.center.z) : new Vector3(curb.center.x, curb.min.y, curb.max.z);
            Railing(rails, from, to, beams);

            if (lowestRailEnd == null || from.y < lowestRailEnd.Value.y)
                lowestRailEnd = to;
        }

        // Sedlova strecha s presahem; RepairTowerRoofCollision vypne puvodni skrytou kolizi.
        if (roofBounds.HasValue)
        {
            var rb = roofBounds.Value;
            if (prism.GetComponent<MeshRenderer>() != null)
                prism.GetComponent<MeshRenderer>().enabled = false;

            var roof = MapBuildKit.Group(root, "Strecha");
            float eave = rb.min.y - 0.05f;
            float ridge = eave + 1.75f;
            BuildRoof(roof, new Vector3(rb.center.x, eave, rb.center.z), rb.extents.x + 0.7f, rb.size.z + 1.6f, rb.extents.z + 0.05f, ridge, planks, shingles, beams);

            Vector3 poleBase = new Vector3(rb.center.x, ridge, rb.center.z);
            Vector3 poleTop = poleBase + Vector3.up * 2.8f;
            MapBuildKit.Beam(roof, "Stozar", poleBase, poleTop, 0.08f, iron);
            var flagMesh = MapBuildKit.FlatMesh("Vlajka", new[]
            {
                poleTop + new Vector3(0.05f, -1.0f, 0f), poleTop + new Vector3(1.55f, -1.0f, 0f),
                poleTop + new Vector3(1.55f, -0.02f, 0f), poleTop + new Vector3(0.05f, -0.02f, 0f),
            }, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            var flagObject = MapBuildKit.MeshObject(roof, "Vlajka", flagMesh, Vector3.zero, Quaternion.identity, Vector3.one, flag);
            GameObjectUtility.SetStaticEditorFlags(flagObject, 0);   // vlni se, nesmi se staticky slucovat
            flagObject.AddComponent<FlagWave>();

            Lantern(lights, new Vector3(rb.center.x, eave - 0.9f, rb.center.z), ridge - 0.2f, lamp, iron);
        }

        // Lucerna pod prvni plosinou nejvyssi veze a na konci nejnizsiho zabradli.
        if (highestTop > float.MinValue)
            Lantern(lights, new Vector3(highestCenter.x, highestFirstFloor - 0.9f, highestCenter.y), highestFirstFloor, lamp, iron);
        if (lowestRailEnd.HasValue)
            Lantern(lights, lowestRailEnd.Value + Vector3.up * 1.22f, 0f, lamp, iron);

        return true;
    }

    // Jedna vez = sloupky pod stejnou plosinou (sousedni veze jsou od sebe stejne daleko jako sloupky jedne veze,
    // takze podle vzdalenosti je rozlisit nejde). Plosiny nad sebou se stejnym pudorysem patri k jedne vezi.
    static List<List<Post>> Cluster(List<Post> posts, List<Bounds> platforms)
    {
        var footprints = new List<Rect>();
        foreach (var p in platforms)
        {
            var r = Rect.MinMaxRect(p.min.x - 0.3f, p.min.z - 0.3f, p.max.x + 0.3f, p.max.z + 0.3f);
            if (!footprints.Exists(f => f.Overlaps(r) && Mathf.Abs(f.center.x - r.center.x) < 1f && Mathf.Abs(f.center.y - r.center.y) < 1f))
                footprints.Add(r);
        }

        var clusters = new List<List<Post>>();
        foreach (var footprint in footprints)
        {
            var cluster = posts.FindAll(p => footprint.Contains(p.center));
            if (cluster.Count >= 4)
                clusters.Add(cluster);
        }
        return clusters;
    }

    // Krizove vzpery na jedne stene veze, pokud stenu nezakryva rampa nebo lavka.
    static void BraceFace(Transform parent, Vector3 left, Vector3 right, float y0, float y1, int stacks, List<Bounds> obstacles, Material material)
    {
        var face = new Bounds((left + right) * 0.5f + Vector3.up * (y0 + y1) * 0.5f, Vector3.zero);
        face.Encapsulate(new Vector3(left.x, y0, left.z));
        face.Encapsulate(new Vector3(right.x, y1, right.z));
        face.Expand(new Vector3(0.3f, 0f, 0.3f));
        foreach (var o in obstacles)
            if (o.Intersects(face)) return;

        float step = (y1 - y0) / stacks;
        for (int s = 0; s < stacks; s++)
        {
            float a = y0 + s * step, b = y0 + (s + 1) * step;
            MapBuildKit.Beam(parent, "Vzpera", new Vector3(left.x, a, left.z), new Vector3(right.x, b, right.z), 0.2f, material);
            MapBuildKit.Beam(parent, "Vzpera", new Vector3(left.x, b, left.z), new Vector3(right.x, a, right.z), 0.2f, material);
        }
    }

    static void RingBeams(Transform parent, float[] xs, float[] zs, float half, float y, Material material)
    {
        float o = half + 0.12f;
        MapBuildKit.Beam(parent, "Nosnik", new Vector3(xs[0] - o, y, zs[0] - o), new Vector3(xs[1] + o, y, zs[0] - o), 0.28f, material);
        MapBuildKit.Beam(parent, "Nosnik", new Vector3(xs[0] - o, y, zs[1] + o), new Vector3(xs[1] + o, y, zs[1] + o), 0.28f, material);
        MapBuildKit.Beam(parent, "Nosnik", new Vector3(xs[0] - o, y, zs[0] - o), new Vector3(xs[0] - o, y, zs[1] + o), 0.28f, material);
        MapBuildKit.Beam(parent, "Nosnik", new Vector3(xs[1] + o, y, zs[0] - o), new Vector3(xs[1] + o, y, zs[1] + o), 0.28f, material);
    }

    // Zabradli 1 m: sloupky, horni madlo a spodni lat; jedna tenka kolize pres celou delku.
    static void Railing(Transform parent, Vector3 from, Vector3 to, Material material)
    {
        var group = new GameObject("Zabradli").transform;
        group.SetParent(parent, false);
        group.position = (from + to) * 0.5f;
        group.rotation = Quaternion.identity;

        Vector3 along = to - from;
        int posts = Mathf.Max(2, Mathf.RoundToInt(along.magnitude / 0.8f) + 1);
        for (int i = 0; i < posts; i++)
        {
            Vector3 p = from + along * (i / (float)(posts - 1));
            MapBuildKit.Beam(group, "Sloupek", p, p + Vector3.up * 1.0f, i == 0 || i == posts - 1 ? 0.14f : 0.07f, material);
        }
        MapBuildKit.Beam(group, "Madlo", from + Vector3.up * 1.02f, to + Vector3.up * 1.02f, 0.12f, material);
        MapBuildKit.Beam(group, "Lat", from + Vector3.up * 0.5f, to + Vector3.up * 0.5f, 0.07f, material);
        MapBuildKit.Beam(group, "Lat", from + Vector3.up * 0.12f, to + Vector3.up * 0.12f, 0.07f, material);

        var collider = group.gameObject.AddComponent<BoxCollider>();
        Vector3 scale = group.lossyScale;
        collider.center = new Vector3(0f, 0.55f / scale.y, 0f);
        collider.size = new Vector3(Mathf.Max(Mathf.Abs(along.x), 0.12f) / scale.x, 1.1f / scale.y, Mathf.Max(Mathf.Abs(along.z), 0.12f) / scale.z);
    }

    // Sedla strecha (hreben podel z) s presahem, stity z prken, hrebenovy tram.
    static void BuildRoof(Transform parent, Vector3 eaveCenter, float halfSpan, float lengthZ, float gableOffset, float ridgeY,
        Material planks, Material shingles, Material beams)
    {
        const float thickness = 0.14f;
        float cx = eaveCenter.x, cz = eaveCenter.z, eave = eaveCenter.y;
        float rise = ridgeY - eave;
        float slope = Mathf.Atan2(rise, halfSpan) * Mathf.Rad2Deg;
        float slant = Mathf.Sqrt(halfSpan * halfSpan + rise * rise) + 0.35f;

        for (int side = -1; side <= 1; side += 2)
        {
            var rotation = Quaternion.Euler(0f, 0f, -side * slope);
            Vector3 mid = new Vector3(cx + side * halfSpan * 0.5f, eave + rise * 0.5f, cz) + rotation * new Vector3(0f, thickness * 0.5f, 0f);
            MapBuildKit.Box(parent, side < 0 ? "Strecha_L" : "Strecha_P", mid, new Vector3(slant, thickness, lengthZ), rotation, shingles, true);
        }

        MapBuildKit.Beam(parent, "Hreben", new Vector3(cx, ridgeY + 0.05f, cz - lengthZ * 0.5f), new Vector3(cx, ridgeY + 0.05f, cz + lengthZ * 0.5f), 0.22f, beams);

        foreach (float z in new[] { cz - gableOffset, cz + gableOffset })
        {
            var points = new[]
            {
                new Vector3(cx - halfSpan + 0.15f, eave, z), new Vector3(cx + halfSpan - 0.15f, eave, z), new Vector3(cx, ridgeY - 0.1f, z),
            };
            var uv = new[] { new Vector2(0f, 0f), new Vector2(halfSpan * 2f, 0f), new Vector2(halfSpan, rise) };
            MapBuildKit.MeshObject(parent, "Stit", MapBuildKit.FlatMesh("Stit", points, uv), Vector3.zero, Quaternion.identity, Vector3.one, planks);
        }
    }

    // Upgrade existing scenes without rebuilding or moving the user's tower.
    static bool RepairTowerRoofCollision(Transform map)
    {
        var tower = map.Find("Rozhledna");
        var roof = tower != null ? tower.Find(DetailsName + "/Strecha") : null;
        if (roof == null || roof.Find("Strecha_L") == null || roof.Find("Strecha_P") == null) return false;
        bool changed = false;
        foreach (var filter in roof.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            bool box = filter.name == "Strecha_L" || filter.name == "Strecha_P" || filter.name == "Hreben";
            if (!box && filter.name != "Stit") continue;
            Collider collider = filter.GetComponent<Collider>();
            if (collider == null)
            {
                if (box)
                {
                    var added = filter.gameObject.AddComponent<BoxCollider>();
                    added.center = filter.sharedMesh.bounds.center;
                    added.size = filter.sharedMesh.bounds.size;
                    collider = added;
                }
                else
                {
                    var added = filter.gameObject.AddComponent<MeshCollider>();
                    added.sharedMesh = filter.sharedMesh; // Actual double-sided triangular gable, not its bounding box.
                    collider = added;
                }
                changed = true;
            }
            if (!collider.enabled || collider.isTrigger)
            {
                collider.enabled = true;
                collider.isTrigger = false;
                changed = true;
            }
        }
        var oldRoof = tower.Find("Prism");
        if (oldRoof != null && oldRoof.TryGetComponent<MeshRenderer>(out var renderer) && !renderer.enabled)
            foreach (var collider in oldRoof.GetComponents<Collider>())
                if (collider.enabled)
                {
                    collider.enabled = false;
                    changed = true;
                }
        return changed;
    }

    // Lucerna se svetlem; kdyz je 'hangFrom' vys nez lucerna, visi na retizku.
    static void Lantern(Transform parent, Vector3 position, float hangFrom, Material lamp, Material iron)
    {
        var group = new GameObject("Lucerna").transform;
        group.SetParent(parent, false);
        group.position = position;

        if (hangFrom > position.y + 0.3f)
            MapBuildKit.Beam(group, "Retizek", new Vector3(position.x, hangFrom, position.z), position + Vector3.up * 0.2f, 0.03f, iron);
        MapBuildKit.Box(group, "Strisky", position + Vector3.up * 0.2f, new Vector3(0.3f, 0.06f, 0.3f), iron, false);
        MapBuildKit.Box(group, "Svetlo", position, new Vector3(0.2f, 0.32f, 0.2f), Quaternion.identity, lamp, false, true, false);
        MapBuildKit.Box(group, "Spodek", position - Vector3.up * 0.18f, new Vector3(0.26f, 0.05f, 0.26f), iron, false);

        var lightObject = new GameObject("Light");
        lightObject.transform.SetParent(group, false);
        lightObject.transform.position = position - Vector3.up * 0.1f;
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.8f, 0.55f);
        light.range = 8f;
        light.intensity = 2.2f;
        light.shadows = LightShadows.None;
    }

    static Material WoodMaterial(string name, string texture, Color tint)
    {
        return MapBuildKit.Mat(WoodFolder + "/" + name + ".mat", WoodFolder + "/" + texture + ".png", tint, Vector2.one, 0.12f);
    }

    // ---------------- auto ----------------

    static bool PlaceCar(Transform map, bool force)
    {
        var props = map.Find("Props");
        var existing = props != null ? props.Find(CarName) : null;
        if (existing != null && !force) return RepairCarCollider(existing);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarPath);
        if (prefab == null) return false;   // model se jeste neimportoval, zkusi se priste

        if (props == null)
        {
            props = new GameObject("Props").transform;
            props.SetParent(map, false);
        }

        Vector3 position = CarPosition;
        float yaw = CarYaw;
        if (existing != null)
        {
            // Pri opakovani zachovej misto, kam ho nekdo presunul.
            position = existing.position;
            yaw = existing.eulerAngles.y;
            Object.DestroyImmediate(existing.gameObject);
        }

        var root = new GameObject(CarName).transform;
        root.SetParent(props, true);
        root.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        model.name = "Model";
        model.transform.SetParent(root, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        // Velikost: delka auta CarLength metru; pak postav kola na zem a vycentruj.
        Bounds bounds = LocalBounds(root, model);
        float length = Mathf.Max(bounds.size.x, bounds.size.z);
        if (length > 0.01f)
            model.transform.localScale *= CarLength / length;

        bounds = LocalBounds(root, model);
        model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

        // Delsi strana auta podel lokalni osy z (natoceni pak dava jen CarYaw).
        if (bounds.size.x > bounds.size.z)
            model.transform.RotateAround(root.position, root.up, 90f);

        bounds = LocalBounds(root, model);
        var collider = root.gameObject.AddComponent<BoxCollider>();
        collider.center = bounds.center;
        collider.size = bounds.size;

        foreach (var c in model.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(c);

        GameObjectUtility.SetStaticEditorFlags(root.gameObject, StaticEditorFlags.BatchingStatic);
        return true;
    }

    // The model can be moved, rotated or resized independently of its parent.
    // Refit existing cars too, rather than leaving collision at the original parking spot.
    static bool RepairCarCollider(Transform root)
    {
        var model = root.Find("Model");
        if (model == null) return false;
        Bounds bounds = LocalBounds(root, model.gameObject);
        if (bounds.size.sqrMagnitude < 0.0001f) return false;

        var collider = root.GetComponent<BoxCollider>();
        bool changed = collider == null;
        if (collider == null) collider = root.gameObject.AddComponent<BoxCollider>();
        changed |= !collider.enabled || collider.isTrigger
            || (collider.center - bounds.center).sqrMagnitude > 0.000001f
            || (collider.size - bounds.size).sqrMagnitude > 0.000001f;
        if (!changed) return false;
        collider.center = bounds.center;
        collider.size = bounds.size;
        collider.enabled = true;
        collider.isTrigger = false;
        return true;
    }

    // Obalka vsech rendereru modelu v souradnicich korene.
    static Bounds LocalBounds(Transform root, GameObject model)
    {
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        var bounds = new Bounds();
        bool first = true;
        foreach (var r in renderers)
        {
            var mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;

            Matrix4x4 toRoot = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
            Bounds mb = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = toRoot.MultiplyPoint3x4(corner);
                if (first)
                {
                    bounds = new Bounds(p, Vector3.zero);
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(p);
                }
            }
        }
        return bounds;
    }
}
