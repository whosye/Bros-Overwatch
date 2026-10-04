using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Hlavni chata podle fotek: bila omitka s kamennym soklem, tmave svisle prkenne obklady (horni patro a stity),
// tmava taskova strecha s vikyrem, stresnimi okny, komíny a okapy, tmave ramy oken, okenice a dvoukridle dvere.
// Okoli: travnik, kamenna dlazba u vchodu a podel boku, vysoky zivy plot, tuje u zdi, smrky, hole brizy,
// nadzemni bazen, sud na destovku a satelit.
// Vse se odvozuje ze skutecne podoby chaty v otevrene scene (okna a dvere = mezery ve zdech), postavi se samo
// (idempotentne) a scenu je pak potreba ulozit. Rucne: BrosOverwatch > Mapa > Upravit hlavni chatu znovu.
// Kolize: komíny, zivy plot, kmeny stromu, bazen a sud. Ozdoby na fasade a rostliny kolize nemaji.
[InitializeOnLoad]
public static class HouseSetup
{
    const string MapName = "Map-Domasov";
    const string HouseName = "HlavniChata";
    const string ExteriorName = "Exterier";
    const string SurroundingsName = "OkoliChaty";
    const string Version = "_v3";
    const string Folder = "Assets/Materials/House";

    static HouseSetup()
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
        var house = map != null ? map.transform.Find(HouseName) : null;
        if (house == null) return;

        bool built = house.Find(ExteriorName + "/" + Version) != null && map.transform.Find(SurroundingsName + "/" + Version) != null;
        if (built && !NeedsMaterials(house))
        {
            if (RepairDormerCollision(house))
                EditorSceneManager.MarkSceneDirty(map.scene);
            return;
        }

        if (Build(map.transform, house, false))
        {
            EditorSceneManager.MarkSceneDirty(map.scene);
            Debug.Log("[Chata] Hlavni chata a jeji okoli upraveny podle fotek. Uloz scenu (Ctrl+S).");
        }
        else if (retries++ < 30)
        {
            EditorApplication.delayCall += RunIfNeeded;   // textury se jeste importuji
        }
    }

    [MenuItem("BrosOverwatch/Mapa/Upravit hlavni chatu znovu")]
    static void RebuildMenu()
    {
        var map = GameObject.Find(MapName);
        var house = map != null ? map.transform.Find(HouseName) : null;
        if (house == null) return;

        if (Build(map.transform, house, true))
            EditorSceneManager.MarkSceneDirty(map.scene);
    }

    // ---------------- materialy ----------------

    class Mats
    {
        public Material plaster, boards, roof, planks, stone, iron, glass, chimney, leaves, thuja, conifer, bark, birch, lawn,
            poolWall, water, barrel, dish, lamp;
    }

    static Mats LoadMaterials()
    {
        string g = "Assets/Materials/Garden";
        var m = new Mats
        {
            plaster = MapBuildKit.Mat(Folder + "/Plaster.mat", Folder + "/Plaster.png", Color.white, new Vector2(0.5f, 0.5f), 0.05f),
            boards = MapBuildKit.Mat(Folder + "/DarkBoards.mat", Folder + "/DarkBoards.png", Color.white, new Vector2(1f, 1f), 0.08f),
            roof = MapBuildKit.Mat(Folder + "/RoofTiles.mat", Folder + "/RoofTiles.png", Color.white, new Vector2(1f, 1f), 0.25f),
            chimney = MapBuildKit.Mat(Folder + "/Chimney.mat", Folder + "/Plaster.png", new Color(0.86f, 0.84f, 0.8f), new Vector2(0.5f, 0.5f), 0.05f),
            glass = MapBuildKit.Mat(Folder + "/Glass.mat", null, new Color(0.12f, 0.16f, 0.2f), Vector2.one, 0.92f),
            poolWall = MapBuildKit.Mat(Folder + "/PoolWall.mat", null, new Color(0.2f, 0.45f, 0.8f), Vector2.one, 0.35f),
            water = MapBuildKit.Mat(Folder + "/Water.mat", null, new Color(0.3f, 0.65f, 0.85f), Vector2.one, 0.95f),
            barrel = MapBuildKit.Mat(Folder + "/Barrel.mat", null, new Color(0.15f, 0.35f, 0.7f), Vector2.one, 0.3f),
            dish = MapBuildKit.Mat(Folder + "/Dish.mat", null, new Color(0.85f, 0.85f, 0.85f), Vector2.one, 0.4f),
            birch = MapBuildKit.Mat(Folder + "/Birch.mat", null, new Color(0.62f, 0.6f, 0.57f), Vector2.one, 0.1f),
            thuja = MapBuildKit.Mat(Folder + "/Thuja.mat", g + "/Leaves.png", new Color(0.55f, 0.72f, 0.45f), new Vector2(1.2f, 1.2f), 0.05f),
            planks = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wood/Wood_Planks.mat"),
            stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Stone/Stone_Wall.mat"),
            iron = AssetDatabase.LoadAssetAtPath<Material>(g + "/Iron.mat"),
            leaves = AssetDatabase.LoadAssetAtPath<Material>(g + "/Hedge.mat"),
            conifer = AssetDatabase.LoadAssetAtPath<Material>(g + "/Conifer.mat"),
            bark = AssetDatabase.LoadAssetAtPath<Material>(g + "/Bark.mat"),
            lawn = AssetDatabase.LoadAssetAtPath<Material>(g + "/Lawn.mat"),
            lamp = AssetDatabase.LoadAssetAtPath<Material>(g + "/Lantern.mat"),
        };

        if (m.plaster == null || m.boards == null || m.roof == null || m.chimney == null || m.thuja == null || m.planks == null
            || m.stone == null || m.iron == null || m.leaves == null || m.conifer == null || m.bark == null || m.lawn == null || m.lamp == null)
            return null;
        return m;
    }

    static bool NeedsMaterials(Transform house)
    {
        var plaster = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Plaster.mat");
        if (plaster == null) return true;
        var any = house.Find("PRIZEMI")?.GetComponentInChildren<MeshRenderer>();
        return any != null && any.sharedMaterial != plaster && !IsDecor(any.transform, house);
    }

    // Omitka na zdech, tmave obklady v hornim patre, tasky na strese, prkna na podlahach a schodech.
    static void ApplyMaterials(Transform house, Mats m)
    {
        foreach (var renderer in house.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (IsDecor(renderer.transform, house)) continue;

            Material material;
            bool upper = IsUnder(renderer.transform, house, "DRUHEPATRO");
            if (upper && renderer.name == "Prism") material = m.roof;
            else if (upper) material = m.boards;
            else if (HasAncestorNamed(renderer.transform, house, "strop") || renderer.name.Contains("Stairs")) material = m.planks;
            else material = m.plaster;

            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                materials[i] = material;
            renderer.sharedMaterials = materials;
        }
    }

    // Nase ozdoby (fasada, interier) - neprebarvuji se a nepocitaji se jako zdi.
    public const string InteriorName = "Interier";
    static bool IsDecor(Transform t, Transform house) => IsUnder(t, house, ExteriorName) || IsUnder(t, house, InteriorName);

    static bool IsUnder(Transform t, Transform root, string childName)
    {
        for (; t != null && t != root; t = t.parent)
            if (t.parent == root && t.name == childName) return true;
        return false;
    }

    static bool HasAncestorNamed(Transform t, Transform root, string prefix)
    {
        for (; t != null && t != root; t = t.parent)
            if (t.name.StartsWith(prefix)) return true;
        return false;
    }

    // ---------------- mereni chaty ----------------

    class Shape
    {
        public Rect footprint;          // venkovni obrys prizemi (xz)
        public float eaveY, ridgeY, ridgeZ;
        public Rect roof;               // pudorys strechy (xz), hreben podel x
        public List<Bounds> walls = new List<Bounds>();
        public List<Opening> openings = new List<Opening>();
    }

    class Opening
    {
        public Vector3 center;          // stred otvoru na venkovni lici zdi
        public Vector3 outward;         // jednotkovy smer ven
        public Vector3 along;           // jednotkovy smer podel zdi
        public float width, bottom, top;
        public int floor;
        public bool IsDoor => bottom < 0.3f;
    }

    static Shape Measure(Transform house)
    {
        var shape = new Shape();
        bool first = true;
        Bounds ground = new Bounds();
        foreach (var r in house.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (IsDecor(r.transform, house)) continue;
            Bounds b = r.bounds;

            if (IsUnder(r.transform, house, "DRUHEPATRO") && r.name == "Prism")
            {
                shape.eaveY = b.min.y;
                shape.ridgeY = b.max.y;
                shape.ridgeZ = b.center.z;
                shape.roof = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
                continue;
            }
            if (HasAncestorNamed(r.transform, house, "strop") || r.name.Contains("Stairs")) continue;

            shape.walls.Add(b);
            if (IsUnder(r.transform, house, "PRIZEMI"))
            {
                if (first) { ground = b; first = false; }
                else ground.Encapsulate(b);
            }
        }
        if (first || shape.ridgeY <= 0f) return null;

        shape.footprint = Rect.MinMaxRect(ground.min.x, ground.min.z, ground.max.x, ground.max.z);
        FindOpenings(shape);
        return shape;
    }

    // Okna a dvere = mezery v obvodovych zdech prizemi a prvniho patra.
    static void FindOpenings(Shape s)
    {
        var f = s.footprint;
        var sides = new[]
        {
            (axisX: true, coord: f.xMin + 0.25f, from: f.yMin, to: f.yMax, outward: Vector3.left),
            (axisX: true, coord: f.xMax - 0.25f, from: f.yMin, to: f.yMax, outward: Vector3.right),
            (axisX: false, coord: f.yMin + 0.25f, from: f.xMin, to: f.xMax, outward: Vector3.back),
            (axisX: false, coord: f.yMax - 0.25f, from: f.xMin, to: f.xMax, outward: Vector3.forward),
        };
        var floors = new[] { (probe: 1.9f, low: 0f, high: 3.3f), (probe: 4.9f, low: 3.2f, high: 6.5f) };

        foreach (var side in sides)
            for (int fl = 0; fl < floors.Length; fl++)
            {
                var floor = floors[fl];
                // (souradnice muzou byt zaporne, proto zvlastni priznak misto "start = -1")
                bool inGap = false;
                float start = 0f;
                for (float t = side.from + 0.3f; t <= side.to - 0.3f + 0.001f; t += 0.05f)
                {
                    bool covered = Covered(s, side.axisX, side.coord, t, floor.probe);
                    if (!covered && !inGap)
                    {
                        inGap = true;
                        start = t;
                    }
                    if ((covered || t + 0.05f > side.to - 0.3f) && inGap)
                    {
                        float end = t;
                        float width = end - start;
                        if (width >= 0.6f && width <= 3.2f)
                        {
                            float mid = (start + end) * 0.5f;
                            float bottom = floor.probe, top = floor.probe;
                            while (bottom > floor.low && !Covered(s, side.axisX, side.coord, mid, bottom - 0.05f)) bottom -= 0.05f;
                            while (top < floor.high && !Covered(s, side.axisX, side.coord, mid, top + 0.05f)) top += 0.05f;

                            float face = OuterFace(s, side.axisX, side.coord, mid, width, floor.low, floor.high, side.outward);
                            Vector3 along = side.axisX ? Vector3.forward : Vector3.right;
                            Vector3 center = side.axisX ? new Vector3(face, 0f, mid) : new Vector3(mid, 0f, face);
                            s.openings.Add(new Opening
                            {
                                center = center, outward = side.outward, along = along, width = width,
                                bottom = Mathf.Max(bottom, floor.low), top = top, floor = fl,
                            });
                        }
                        inGap = false;
                    }
                }
            }
    }

    static bool Covered(Shape s, bool axisX, float coord, float t, float y)
    {
        Vector3 p = axisX ? new Vector3(coord, y, t) : new Vector3(t, y, coord);
        foreach (var b in s.walls)
        {
            if (axisX ? (p.x < b.min.x - 0.1f || p.x > b.max.x + 0.1f) : (p.z < b.min.z - 0.1f || p.z > b.max.z + 0.1f)) continue;
            if (p.y < b.min.y || p.y > b.max.y) continue;
            if (axisX ? (p.z >= b.min.z && p.z <= b.max.z) : (p.x >= b.min.x && p.x <= b.max.x)) return true;
        }
        return false;
    }

    // Venkovni lic zdi u otvoru (nejvzdalenejsi plocha zdi ve smeru ven).
    static float OuterFace(Shape s, bool axisX, float coord, float mid, float width, float low, float high, Vector3 outward)
    {
        float sign = axisX ? outward.x : outward.z;
        float best = coord;
        foreach (var b in s.walls)
        {
            if (b.max.y < low + 0.2f || b.min.y > high) continue;
            float lo = axisX ? b.min.z : b.min.x, hi = axisX ? b.max.z : b.max.x;
            if (hi < mid - width * 0.5f - 0.8f || lo > mid + width * 0.5f + 0.8f) continue;
            float near = axisX ? (sign < 0 ? b.min.x : b.max.x) : (sign < 0 ? b.min.z : b.max.z);
            float other = axisX ? (sign < 0 ? b.max.x : b.min.x) : (sign < 0 ? b.max.z : b.min.z);
            if (Mathf.Abs(other - coord) > 1.2f && Mathf.Abs(near - coord) > 1.2f) continue;
            if (sign < 0 ? near < best : near > best) best = near;
        }
        return best;
    }

    // ---------------- stavba ----------------

    static bool Build(Transform map, Transform house, bool force)
    {
        var m = LoadMaterials();
        if (m == null) return false;

        var shape = Measure(house);
        if (shape == null) return false;

        // Okna obyvaku na zapadni stene jsou zazdena (sedacka pod nimi) - vypln patri ke stavbe, takze se pak
        // chova jako zed (omitka, kolize, zadne okenice).
        if (BrickUpLivingRoomWindows(house, shape, m))
            shape = Measure(house);
        ApplyMaterials(house, m);

        var exterior = house.Find(ExteriorName);
        if (force || exterior == null || exterior.Find(Version) == null)
        {
            if (exterior != null) Object.DestroyImmediate(exterior.gameObject);
            exterior = new GameObject(ExteriorName).transform;
            exterior.SetParent(house, true);
            new GameObject(Version).transform.SetParent(exterior, false);
            BuildExterior(exterior, shape, m);
        }

        var surroundings = map.Find(SurroundingsName);
        if (force || surroundings == null || surroundings.Find(Version) == null)
        {
            if (surroundings != null) Object.DestroyImmediate(surroundings.gameObject);
            surroundings = new GameObject(SurroundingsName).transform;
            surroundings.SetParent(map, true);
            new GameObject(Version).transform.SetParent(surroundings, false);
            BuildSurroundings(map, surroundings, shape, m);
        }
        RepairDormerCollision(house);
        return true;
    }

    // Upgrade existing decoration without rebuilding the house or its surroundings.
    static bool RepairDormerCollision(Transform house)
    {
        var exterior = house.Find(ExteriorName);
        if (exterior == null) return false;
        bool changed = false;
        foreach (var meshFilter in exterior.GetComponentsInChildren<MeshFilter>(true))
        {
            var part = meshFilter.transform;
            if (part.parent == null || part.parent.name != "Vikyr" || meshFilter.sharedMesh == null) continue;
            if (part.name != "Telo" && part.name != "Striska" && part.name != "Stit") continue;

            Collider collider = part.GetComponent<Collider>();
            if (collider == null)
            {
                if (part.name == "Stit")
                    part.gameObject.AddComponent<MeshCollider>().sharedMesh = meshFilter.sharedMesh;
                else
                {
                    var box = part.gameObject.AddComponent<BoxCollider>();
                    box.center = meshFilter.sharedMesh.bounds.center;
                    box.size = meshFilter.sharedMesh.bounds.size;
                }
                collider = part.GetComponent<Collider>();
                changed = true;
            }
            changed |= !collider.enabled || collider.isTrigger;
            collider.enabled = true;
            collider.isTrigger = false;
        }
        return changed;
    }

    static void BuildExterior(Transform root, Shape s, Mats m)
    {
        var f = s.footprint;

        // --- kamenny sokl kolem prizemi (krome dveri) ---
        var plinth = MapBuildKit.Group(root, "Sokl");
        const float plinthHeight = 0.55f, plinthOut = 0.07f;
        void PlinthRun(Vector3 a, Vector3 b, Vector3 outward)
        {
            Vector3 along = b - a;
            if (along.magnitude < 0.2f) return;
            Vector3 c = (a + b) * 0.5f + outward * (plinthOut * 0.5f) + Vector3.up * plinthHeight * 0.5f;
            Vector3 size = outward.x != 0f ? new Vector3(plinthOut + 0.02f, plinthHeight, along.magnitude + 0.14f)
                                            : new Vector3(along.magnitude + 0.14f, plinthHeight, plinthOut + 0.02f);
            MapBuildKit.Box(plinth, "Sokl", c, size, Quaternion.identity, m.stone, false, true, false);
        }
        PlinthSide(f.xMin, true, f.yMin, f.yMax, Vector3.left, s, PlinthRun);
        PlinthSide(f.xMax, true, f.yMin, f.yMax, Vector3.right, s, PlinthRun);
        PlinthSide(f.yMin, false, f.xMin, f.xMax, Vector3.back, s, PlinthRun);
        PlinthSide(f.yMax, false, f.xMin, f.xMax, Vector3.forward, s, PlinthRun);

        // --- tmava rimsa mezi bilou omitkou a dreveným patrem ---
        var trim = MapBuildKit.Group(root, "Rimsa");
        float trimY = 6.5f;
        MapBuildKit.Box(trim, "Rimsa", new Vector3(f.center.x, trimY, f.yMin - 0.08f), new Vector3(f.width + 0.5f, 0.28f, 0.2f), m.boards, false);
        MapBuildKit.Box(trim, "Rimsa", new Vector3(f.center.x, trimY, f.yMax + 0.08f), new Vector3(f.width + 0.5f, 0.28f, 0.2f), m.boards, false);
        MapBuildKit.Box(trim, "Rimsa", new Vector3(f.xMin - 0.08f, trimY, f.center.y), new Vector3(0.2f, 0.28f, f.height + 0.5f), m.boards, false);
        MapBuildKit.Box(trim, "Rimsa", new Vector3(f.xMax + 0.08f, trimY, f.center.y), new Vector3(0.2f, 0.28f, f.height + 0.5f), m.boards, false);

        // --- okna a dvere ---
        var windows = MapBuildKit.Group(root, "Okna");
        foreach (var o in s.openings)
        {
            if (o.IsDoor) Door(windows, o, m);
            else Window(windows, o, m);
        }

        // --- stity z tmavych prken, cela strechy, okapy ---
        var roof = MapBuildKit.Group(root, "Strecha");
        foreach (float x in new[] { s.roof.xMin - 0.03f, s.roof.xMax + 0.03f })
        {
            var points = new[]
            {
                new Vector3(x, s.eaveY + 0.02f, s.roof.yMin + 0.05f), new Vector3(x, s.eaveY + 0.02f, s.roof.yMax - 0.05f),
                new Vector3(x, s.ridgeY - 0.05f, s.ridgeZ),
            };
            var uv = new[] { new Vector2(s.roof.yMin, s.eaveY), new Vector2(s.roof.yMax, s.eaveY), new Vector2(s.ridgeZ, s.ridgeY) };
            MapBuildKit.MeshObject(roof, "Stit", MapBuildKit.FlatMesh("Stit", points, uv), Vector3.zero, Quaternion.identity, Vector3.one, m.boards);

            // Celni prkna podel hran strechy.
            MapBuildKit.Beam(roof, "CelniPrkno", new Vector3(x, s.eaveY, s.roof.yMin), new Vector3(x, s.ridgeY + 0.05f, s.ridgeZ), 0.22f, m.boards);
            MapBuildKit.Beam(roof, "CelniPrkno", new Vector3(x, s.eaveY, s.roof.yMax), new Vector3(x, s.ridgeY + 0.05f, s.ridgeZ), 0.22f, m.boards);
        }
        foreach (float z in new[] { s.roof.yMin - 0.08f, s.roof.yMax + 0.08f })
            MapBuildKit.Beam(roof, "Okap", new Vector3(s.roof.xMin, s.eaveY - 0.05f, z), new Vector3(s.roof.xMax, s.eaveY - 0.05f, z), 0.14f, m.iron);
        MapBuildKit.Beam(roof, "Hreben", new Vector3(s.roof.xMin, s.ridgeY + 0.04f, s.ridgeZ), new Vector3(s.roof.xMax, s.ridgeY + 0.04f, s.ridgeZ), 0.2f, m.roof);

        // --- komíny na hrebeni ---
        foreach (float t in new[] { 0.32f, 0.68f })
        {
            float x = Mathf.Lerp(s.roof.xMin, s.roof.xMax, t);
            Vector3 baseCenter = new Vector3(x, s.ridgeY - 0.2f, s.ridgeZ + (t < 0.5f ? 0.9f : -0.9f));
            MapBuildKit.Box(roof, "Komin", baseCenter, new Vector3(0.95f, 2.6f, 0.95f), m.chimney, true);
            MapBuildKit.Box(roof, "KominStriska", baseCenter + Vector3.up * 1.35f, new Vector3(1.15f, 0.1f, 1.15f), m.chimney, false);
            MapBuildKit.Box(roof, "KominHlava", baseCenter + Vector3.up * 1.6f, new Vector3(0.45f, 0.4f, 0.45f), m.iron, false);
        }

        // --- vikyr a stresni okna na severni (+z) strane ---
        BuildDormer(roof, s, m);
        float slope = (s.ridgeY - s.eaveY) / Mathf.Max(0.1f, s.roof.yMax - s.ridgeZ);
        float angle = Mathf.Atan(slope) * Mathf.Rad2Deg;
        foreach (float t in new[] { 0.15f, 0.85f })
        {
            float x = Mathf.Lerp(s.roof.xMin, s.roof.xMax, t);
            float z = s.ridgeZ + (s.roof.yMax - s.ridgeZ) * 0.45f;
            float y = s.ridgeY - slope * (z - s.ridgeZ);
            var rotation = Quaternion.Euler(angle, 0f, 0f);
            MapBuildKit.Box(roof, "StresniOkno", new Vector3(x, y, z) + rotation * new Vector3(0f, 0.04f, 0f), new Vector3(1.0f, 0.08f, 1.3f), rotation, m.iron, false);
            MapBuildKit.Box(roof, "Sklo", new Vector3(x, y, z) + rotation * new Vector3(0f, 0.085f, 0f), new Vector3(0.84f, 0.02f, 1.14f), rotation, m.glass, false);
        }

        // --- satelit na zapadnim stitu ---
        var blob = MapBuildKit.BlobMesh(0);
        Vector3 dishPos = new Vector3(f.xMin - 0.55f, 5.3f, f.yMin + 3.2f);
        MapBuildKit.MeshObject(root, "Satelit", blob, dishPos, Quaternion.Euler(0f, -60f, 0f), new Vector3(0.12f, 0.42f, 0.42f), m.dish);
        MapBuildKit.Beam(root, "SatelitDrzak", new Vector3(f.xMin, 5.1f, f.yMin + 3.2f), dishPos - Vector3.up * 0.15f, 0.05f, m.iron);
    }

    static bool BrickUpLivingRoomWindows(Transform house, Shape s, Mats m)
    {
        var obyvak = house.Find("PRIZEMI/obyvak");
        if (obyvak == null) return false;

        // Obyvak lezi severne od pricky u vchodu: mezi jeji severni stenou a severni zdi chaty.
        float roomMinZ = float.MinValue;
        foreach (var r in obyvak.GetComponentsInChildren<MeshRenderer>(true))
        {
            Bounds b = r.bounds;
            if (b.size.x > b.size.z && b.center.z < s.footprint.center.y + 6f && b.size.x > 3f)
                roomMinZ = Mathf.Max(roomMinZ, b.max.z);
        }

        bool added = false;
        foreach (var o in s.openings)
        {
            if (o.IsDoor || o.floor != 0 || o.outward != Vector3.left) continue;
            if (o.center.z < roomMinZ || o.center.z > s.footprint.yMax) continue;

            Vector3 c = new Vector3(o.center.x + 0.27f, (o.bottom + o.top) * 0.5f, o.center.z);
            MapBuildKit.Box(obyvak, "ZazdeneOkno", c, new Vector3(0.56f, o.top - o.bottom + 0.02f, o.width + 0.02f), m.plaster, true);
            added = true;
        }
        return added;
    }

    // Sokl po jedne strane; vynecha dvere.
    static void PlinthSide(float coord, bool axisX, float from, float to, Vector3 outward, Shape s, System.Action<Vector3, Vector3, Vector3> run)
    {
        var doors = s.openings.FindAll(o => o.IsDoor && o.outward == outward);
        doors.Sort((a, b) => (axisX ? a.center.z : a.center.x).CompareTo(axisX ? b.center.z : b.center.x));
        float t = from;
        foreach (var d in doors)
        {
            float c = axisX ? d.center.z : d.center.x;
            Segment(coord, axisX, t, c - d.width * 0.5f, outward, run);
            t = c + d.width * 0.5f;
        }
        Segment(coord, axisX, t, to, outward, run);
    }

    static void Segment(float coord, bool axisX, float a, float b, Vector3 outward, System.Action<Vector3, Vector3, Vector3> run)
    {
        if (b - a < 0.2f) return;
        run(axisX ? new Vector3(coord, 0f, a) : new Vector3(a, 0f, coord), axisX ? new Vector3(coord, 0f, b) : new Vector3(b, 0f, coord), outward);
    }

    static void Window(Transform parent, Opening o, Mats m)
    {
        var group = new GameObject("Okno").transform;
        group.SetParent(parent, true);
        float h = o.top - o.bottom;
        Vector3 c = o.center + Vector3.up * (o.bottom + h * 0.5f) + o.outward * 0.03f;

        Frame(group, c, o, h, m.boards);

        // Parapet.
        MapBuildKit.Box(group, "Parapet", OrientBox(o.center + Vector3.up * (o.bottom - 0.04f) + o.outward * 0.1f), Size(o, o.width + 0.25f, 0.07f, 0.25f), m.chimney, false);

        // Okenice (jen v prizemi, jako na fotkach): obe kridla otevrena ke zdi.
        if (o.floor == 0)
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 p = c + o.along * side * (o.width * 0.5f + 0.1f + o.width * 0.25f) + o.outward * 0.02f;
                MapBuildKit.Box(group, "Okenice", p, Size(o, o.width * 0.5f, h + 0.05f, 0.05f), m.boards, false);
            }
    }

    static void Door(Transform parent, Opening o, Mats m)
    {
        var group = new GameObject("Dvere").transform;
        group.SetParent(parent, true);
        float h = o.top - o.bottom;
        Vector3 c = o.center + Vector3.up * (o.bottom + h * 0.5f) + o.outward * 0.03f;

        Frame(group, c, o, h, m.boards, false);

        // Dve kridla otevrena ven az ke zdi (pruchod zustava volny).
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 p = o.center + Vector3.up * (o.bottom + (h - 0.05f) * 0.5f) + o.along * side * (o.width * 0.5f + 0.12f + o.width * 0.25f) + o.outward * 0.05f;
            MapBuildKit.Box(group, "Kridlo", p, Size(o, o.width * 0.5f, h - 0.05f, 0.06f), m.boards, false);
        }

        // Lampa nad dvermi.
        Vector3 lampPos = o.center + Vector3.up * (o.top + 0.45f) + o.outward * 0.18f;
        MapBuildKit.Box(group, "Lampa", lampPos, new Vector3(0.18f, 0.26f, 0.18f), Quaternion.identity, m.lamp, false, true, false);
        MapBuildKit.Box(group, "LampaStriska", lampPos + Vector3.up * 0.17f, new Vector3(0.26f, 0.05f, 0.26f), m.iron, false);
        var lightObject = new GameObject("Light");
        lightObject.transform.SetParent(group, true);
        lightObject.transform.position = lampPos + o.outward * 0.2f;
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.8f, 0.55f);
        light.range = 7f;
        light.intensity = 1.6f;
        light.shadows = LightShadows.None;
    }

    // Tmavy ram kolem otvoru (u dveri bez spodni hrany).
    static void Frame(Transform parent, Vector3 c, Opening o, float h, Material material, bool bottom = true)
    {
        const float t = 0.11f, d = 0.12f;
        MapBuildKit.Box(parent, "Ram", c + Vector3.up * (h * 0.5f + t * 0.5f), Size(o, o.width + 2f * t, t, d), material, false);
        if (bottom)
            MapBuildKit.Box(parent, "Ram", c - Vector3.up * (h * 0.5f + t * 0.5f), Size(o, o.width + 2f * t, t, d), material, false);
        MapBuildKit.Box(parent, "Ram", c + o.along * (o.width * 0.5f + t * 0.5f), Size(o, t, h, d), material, false);
        MapBuildKit.Box(parent, "Ram", c - o.along * (o.width * 0.5f + t * 0.5f), Size(o, t, h, d), material, false);
    }

    // Rozmery kvadru v osach sveta pro dany otvor (sirka podel zdi, hloubka ven).
    static Vector3 Size(Opening o, float width, float height, float depth)
    {
        return o.along == Vector3.forward ? new Vector3(depth, height, width) : new Vector3(width, height, depth);
    }

    static Vector3 OrientBox(Vector3 p) => p;

    // Vikyr uprostred severni strany strechy: tmava prkenna stena se dvema okny a vlastni sedlovou striskou.
    static void BuildDormer(Transform parent, Shape s, Mats m)
    {
        float span = s.roof.yMax - s.ridgeZ;
        float k = (s.ridgeY - s.eaveY) / Mathf.Max(0.1f, span);
        float zFront = s.ridgeZ + span * 0.62f;
        float ySlope = s.ridgeY - k * (zFront - s.ridgeZ);
        float top = Mathf.Min(ySlope + 2.2f, s.ridgeY - 0.35f);
        float zBack = s.ridgeZ + (s.ridgeY - top) / Mathf.Max(0.01f, k);
        float cx = s.roof.center.x, half = 2.5f;

        var group = new GameObject("Vikyr").transform;
        group.SetParent(parent, true);

        float bottom = ySlope - 1.2f;
        MapBuildKit.Box(group, "Telo", new Vector3(cx, (bottom + top) * 0.5f, (zBack + zFront) * 0.5f),
            new Vector3(half * 2f, top - bottom, zFront - zBack), m.boards, true);

        // Okna ve celni stene.
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 w = new Vector3(cx + side * 0.9f, ySlope + 1.05f, zFront + 0.03f);
            MapBuildKit.Box(group, "Sklo", w, new Vector3(0.75f, 1.0f, 0.04f), m.glass, false);
            MapBuildKit.Box(group, "Ram", w + Vector3.up * 0.55f, new Vector3(0.95f, 0.1f, 0.08f), m.boards, false);
            MapBuildKit.Box(group, "Ram", w - Vector3.up * 0.55f, new Vector3(0.95f, 0.1f, 0.08f), m.boards, false);
        }

        // Sedla striska s hrebenem podel z, prekryva celo.
        float rise = 1.3f, roofHalf = half + 0.35f;
        float slant = Mathf.Sqrt(roofHalf * roofHalf + rise * rise) + 0.1f;
        float a = Mathf.Atan2(rise, roofHalf) * Mathf.Rad2Deg;
        float length = (zFront + 0.45f) - (zBack - 0.3f);
        float zMid = ((zFront + 0.45f) + (zBack - 0.3f)) * 0.5f;
        for (int side = -1; side <= 1; side += 2)
        {
            var rotation = Quaternion.Euler(0f, 0f, -side * a);
            Vector3 mid = new Vector3(cx + side * roofHalf * 0.5f, top + rise * 0.5f, zMid) + rotation * new Vector3(0f, 0.06f, 0f);
            MapBuildKit.Box(group, "Striska", mid, new Vector3(slant, 0.12f, length), rotation, m.roof, true);
        }
        var gable = new[] { new Vector3(cx - half, top, zFront + 0.01f), new Vector3(cx + half, top, zFront + 0.01f), new Vector3(cx, top + rise - 0.05f, zFront + 0.01f) };
        var uv = new[] { new Vector2(-half, 0f), new Vector2(half, 0f), new Vector2(0f, rise) };
        var front = MapBuildKit.MeshObject(group, "Stit", MapBuildKit.FlatMesh("VikyrStit", gable, uv), Vector3.zero, Quaternion.identity, Vector3.one, m.boards);
        front.AddComponent<MeshCollider>().sharedMesh = front.GetComponent<MeshFilter>().sharedMesh;
    }

    // ---------------- okoli ----------------

    static void BuildSurroundings(Transform map, Transform root, Shape s, Mats m)
    {
        var f = s.footprint;
        Bounds ground = MapBuildKit.GroundBounds(map);
        Rect plot = Rect.MinMaxRect(Mathf.Max(f.xMin - 8f, ground.min.x + 0.3f), Mathf.Max(f.yMin - 7.5f, ground.min.z + 0.3f),
            Mathf.Min(f.xMax + 11f, ground.max.x - 0.3f), Mathf.Min(f.yMax + 8f, ground.max.z - 0.3f));

        var spawns = new List<Vector2>();
        foreach (var t in map.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("SpawnPoint") && t.position.y < 1f)
                spawns.Add(new Vector2(t.position.x, t.position.z));
        bool NearSpawn(Vector2 p, float r) => spawns.Exists(sp => Vector2.Distance(sp, p) < r);

        var car = map.Find("Props/ToyotaYaris");
        Rect carArea = car != null ? new Rect(car.position.x - 1.6f, car.position.z - 2.6f, 3.2f, 5.2f) : new Rect(-999f, -999f, 0f, 0f);

        // --- travnik kolem chaty (ne pod ni) ---
        var lawn = MapBuildKit.Group(root, "Travnik");
        var house = Rect.MinMaxRect(f.xMin - 0.05f, f.yMin - 0.05f, f.xMax + 0.05f, f.yMax + 0.05f);
        var lawnRects = new[]
        {
            Rect.MinMaxRect(plot.xMin, plot.yMin, plot.xMax, house.yMin),
            Rect.MinMaxRect(plot.xMin, house.yMax, plot.xMax, plot.yMax),
            Rect.MinMaxRect(plot.xMin, house.yMin, house.xMin, house.yMax),
            Rect.MinMaxRect(house.xMax, house.yMin, plot.xMax, house.yMax),
        };
        foreach (var r in lawnRects)
            if (r.width > 0.1f && r.height > 0.1f)
                MapBuildKit.Box(lawn, "Travnik", new Vector3(r.center.x, 0.012f, r.center.y), new Vector3(r.width, 0.02f, r.height),
                    Quaternion.identity, m.lawn, false, true, false);

        // --- kamenna dlazba pred vchodem a podel vychodni zdi ---
        var paving = MapBuildKit.Group(root, "Dlazba");
        var avoid = new List<Rect> { carArea };
        foreach (var door in s.openings.FindAll(o => o.IsDoor))
        {
            Vector3 c = door.center + door.outward * 1.9f;
            Vector3 size = Size(door, door.width + 3.5f, 0.04f, 3.6f);
            MapBuildKit.Box(paving, "Dlazba", new Vector3(c.x, 0.03f, c.z), size, Quaternion.identity, m.stone, false, true, false);
            avoid.Add(new Rect(c.x - size.x * 0.5f, c.z - size.z * 0.5f, size.x, size.z));
        }
        {
            float x = f.xMax + 1.2f;
            var r = Rect.MinMaxRect(f.xMax + 0.05f, Mathf.Lerp(f.yMin, f.yMax, 0.2f), f.xMax + 2.3f, Mathf.Lerp(f.yMin, f.yMax, 0.8f));
            MapBuildKit.Box(paving, "Dlazba", new Vector3(r.center.x, 0.03f, r.center.y), new Vector3(r.width, 0.04f, r.height), Quaternion.identity, m.stone, false, true, false);
            avoid.Add(r);
        }
        bool Blocked(Vector2 p, float r)
        {
            if (house.Contains(p)) return true;
            foreach (var a in avoid)
                if (Rect.MinMaxRect(a.xMin - r, a.yMin - r, a.xMax + r, a.yMax + r).Contains(p)) return true;
            return false;
        }

        // --- vysoky zivy plot v jihozapadnim rohu pozemku (bloky po 2 m, mimo spawny) ---
        var hedge = MapBuildKit.Group(root, "ZivyPlot");
        void HedgeRun(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            int blocks = Mathf.Max(1, Mathf.RoundToInt(d.magnitude / 2f));
            for (int i = 0; i < blocks; i++)
            {
                Vector2 p0 = a + d * (i / (float)blocks), p1 = a + d * ((i + 1) / (float)blocks);
                Vector2 c = (p0 + p1) * 0.5f;
                if (NearSpawn(c, 9f) || Blocked(c, 0.8f)) continue;
                Vector3 size = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector3((p1 - p0).magnitude + 0.02f, 2.4f, 1.1f) : new Vector3(1.1f, 2.4f, (p1 - p0).magnitude + 0.02f);
                MapBuildKit.Box(hedge, "Plot", new Vector3(c.x, 1.2f, c.y), size, m.leaves, true);
            }
        }
        HedgeRun(new Vector2(plot.xMin + 0.6f, plot.yMin + 0.6f), new Vector2(plot.xMin + 17f, plot.yMin + 0.6f));
        HedgeRun(new Vector2(plot.xMin + 0.6f, plot.yMin + 0.6f), new Vector2(plot.xMin + 0.6f, plot.yMin + 13f));

        // --- tuje a kere podel zdi (ne pred okny a dvermi) ---
        var plants = MapBuildKit.Group(root, "Tuje");
        var cone = MapBuildKit.ConeMesh();
        var blobs = new[] { MapBuildKit.BlobMesh(1), MapBuildKit.BlobMesh(2), MapBuildKit.BlobMesh(3) };
        var random = new System.Random(77);
        void AlongWall(Vector3 from, Vector3 to, Vector3 outward)
        {
            Vector3 along = to - from;
            int count = Mathf.Max(1, Mathf.RoundToInt(along.magnitude / 3.2f));
            for (int i = 0; i < count; i++)
            {
                Vector3 p = from + along * ((i + 0.5f) / count) + outward * 0.9f;
                var p2 = new Vector2(p.x, p.z);
                bool atOpening = s.openings.Exists(o => o.outward == outward && o.floor == 0
                    && Vector3.Distance(Vector3.ProjectOnPlane(o.center - p, Vector3.up), Vector3.zero) < o.width * 0.5f + 1.3f);
                if (atOpening || Blocked(p2, 0.6f)) continue;

                if (i % 2 == 0)
                {
                    // Tuje: dva kuzely nad sebou.
                    float h = 1.4f + (float)random.NextDouble() * 0.9f;
                    MapBuildKit.MeshObject(plants, "Tuje", cone, p, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), new Vector3(0.55f, h * 0.65f, 0.55f), m.thuja);
                    MapBuildKit.MeshObject(plants, "Tuje", cone, p + Vector3.up * h * 0.35f, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), new Vector3(0.4f, h * 0.65f, 0.4f), m.thuja);
                }
                else
                {
                    float sc = 0.35f + (float)random.NextDouble() * 0.2f;
                    MapBuildKit.MeshObject(plants, "Ker", blobs[random.Next(blobs.Length)], p + Vector3.up * sc * 0.5f, Quaternion.identity, Vector3.one * sc, m.thuja);
                }
            }
        }
        AlongWall(new Vector3(f.xMin, 0f, f.yMax), new Vector3(f.xMax, 0f, f.yMax), Vector3.forward);
        AlongWall(new Vector3(f.xMin, 0f, f.yMin), new Vector3(f.xMin, 0f, f.yMax), Vector3.left);
        AlongWall(new Vector3(f.xMax, 0f, f.yMin), new Vector3(f.xMax, 0f, f.yMax), Vector3.right);
        AlongWall(new Vector3(f.xMin, 0f, f.yMin), new Vector3(f.xMax, 0f, f.yMin), Vector3.back);

        // --- velke smrky v rozich pozemku ---
        var trees = MapBuildKit.Group(root, "Stromy");
        foreach (var p in new[] { new Vector2(plot.xMax - 2.5f, plot.yMax - 2.5f), new Vector2(plot.xMax - 3f, plot.yMin + 3.5f) })
        {
            if (NearSpawn(p, 4f) || Blocked(p, 1.5f)) continue;
            Spruce(trees, new Vector3(p.x, 0f, p.y), 1.5f, cone, m, random);
        }

        // --- hole brizy na zapadni strane ---
        foreach (var p in new[] { new Vector2(plot.xMin + 1.8f, f.yMax - 3f), new Vector2(plot.xMin + 2.6f, f.yMax - 8.5f), new Vector2(plot.xMin + 1.5f, plot.yMax - 1.8f) })
        {
            if (NearSpawn(p, 4f) || Blocked(p, 1f)) continue;
            BareBirch(trees, new Vector3(p.x, 0f, p.y), random, m);
        }

        // --- nadzemni bazen na severozapade ---
        var pool = new Vector2(plot.xMin + 6.5f, plot.yMax - 4.2f);
        if (!NearSpawn(pool, 6f) && !Blocked(pool, 2.4f))
        {
            var group = new GameObject("Bazen").transform;
            group.SetParent(root, true);
            group.position = new Vector3(pool.x, 0f, pool.y);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wall.name = "Stena";
            wall.transform.SetParent(group, false);
            wall.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            wall.transform.localScale = new Vector3(4.4f, 0.6f, 4.4f);
            wall.GetComponent<Renderer>().sharedMaterial = m.poolWall;
            Object.DestroyImmediate(wall.GetComponent<Collider>());
            var meshCollider = wall.AddComponent<MeshCollider>();
            meshCollider.convex = true;

            var water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            water.name = "Voda";
            water.transform.SetParent(group, false);
            water.transform.localPosition = new Vector3(0f, 1.12f, 0f);
            water.transform.localScale = new Vector3(4.2f, 0.02f, 4.2f);
            water.GetComponent<Renderer>().sharedMaterial = m.water;
            Object.DestroyImmediate(water.GetComponent<Collider>());

            // Kovove oblouky zastreseni (jako na fotce).
            for (int i = 0; i < 3; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 prev = group.position + dir * 2.2f + Vector3.up * 1.2f;
                for (int sgm = 1; sgm <= 8; sgm++)
                {
                    float u = sgm / 8f;
                    Vector3 next = group.position + dir * Mathf.Lerp(2.2f, -2.2f, u) + Vector3.up * (1.2f + Mathf.Sin(u * Mathf.PI) * 0.9f);
                    MapBuildKit.Beam(group, "Oblouk", prev, next, 0.05f, m.iron);
                    prev = next;
                }
            }
        }

        // --- sud na destovku u jihozapadniho rohu chaty ---
        var barrelPos = new Vector2(f.xMin - 0.7f, f.yMin + 0.9f);
        if (!Blocked(barrelPos, 0.2f))
        {
            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "Sud";
            barrel.transform.SetParent(root, true);
            barrel.transform.position = new Vector3(barrelPos.x, 0.45f, barrelPos.y);
            barrel.transform.localScale = new Vector3(0.65f, 0.45f, 0.65f);
            barrel.GetComponent<Renderer>().sharedMaterial = m.barrel;
        }
    }

    static void Spruce(Transform parent, Vector3 position, float size, Mesh cone, Mats m, System.Random random)
    {
        var tree = new GameObject("Smrk").transform;
        tree.SetParent(parent, true);
        tree.position = position;

        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Kmen";
        trunk.transform.SetParent(tree, false);
        trunk.transform.localPosition = new Vector3(0f, 1.5f * size, 0f);
        trunk.transform.localScale = new Vector3(0.45f * size, 1.5f * size, 0.45f * size);
        trunk.GetComponent<Renderer>().sharedMaterial = m.bark;

        const int layers = 6;
        for (int i = 0; i < layers; i++)
        {
            float t = i / (float)(layers - 1);
            float radius = Mathf.Lerp(2.4f, 0.6f, t) * size;
            float height = Mathf.Lerp(2.4f, 1.6f, t) * size;
            MapBuildKit.MeshObject(tree, "Jehlici", cone, position + Vector3.up * (1.0f + i * 1.25f) * size,
                Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), new Vector3(radius, height, radius), m.conifer);
        }
    }

    // Hola briza (zima na fotkach): svetly kmen a rozvetvene vetve bez listi.
    static void BareBirch(Transform parent, Vector3 position, System.Random random, Mats m)
    {
        var tree = new GameObject("Briza").transform;
        tree.SetParent(parent, true);
        tree.position = position;

        float height = 7f + (float)random.NextDouble() * 2.5f;
        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Kmen";
        trunk.transform.SetParent(tree, false);
        trunk.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        trunk.transform.localScale = new Vector3(0.24f, height * 0.5f, 0.24f);
        trunk.GetComponent<Renderer>().sharedMaterial = m.birch;

        void Branch(Vector3 from, Vector3 dir, float length, float thickness, int depth)
        {
            Vector3 to = from + dir * length;
            MapBuildKit.Beam(tree, "Vetev", from, to, thickness, m.birch);
            if (depth <= 0) return;
            for (int i = 0; i < 2; i++)
            {
                var turn = Quaternion.Euler(((float)random.NextDouble() - 0.5f) * 50f, ((float)random.NextDouble() - 0.5f) * 120f, ((float)random.NextDouble() - 0.5f) * 50f);
                Vector3 next = (turn * dir + Vector3.up * 0.4f).normalized;
                Branch(to, next, length * 0.68f, thickness * 0.65f, depth - 1);
            }
        }

        int branches = 5;
        for (int i = 0; i < branches; i++)
        {
            float a = (i / (float)branches + (float)random.NextDouble() * 0.1f) * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a) * 0.55f, 1f, Mathf.Sin(a) * 0.55f).normalized;
            Vector3 start = position + Vector3.up * height * (0.45f + i * 0.09f);
            Branch(start, dir, 1.8f, 0.09f, 2);
        }
    }
}
