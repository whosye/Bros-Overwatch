using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Zahrada za rozhlednou (mezi zadni zdi rozhledny a okrajem mapy): travnik, stromy (listnate i jehlicnany),
// kere, zivy plot po okraji mapy, drevene oploceni s brankou, sterkova cesticka, vyvysene zahony s kvetinami a lavicka.
// Vynechava okruh spawnu tymu 1 u bodu 3 a vstup do tunelu. Postavi se samo (idempotentne) do Map-Domasov/Zahrada;
// scenu je pak potreba ulozit (Ctrl+S). Rucne: BrosOverwatch > Mapa > Postavit zahradu znovu.
// Kolize maji kmeny stromu, zivy plot, plot, zahony a lavicka; trava, kvetiny, kere a koruny ne.
[InitializeOnLoad]
public static class GardenSetup
{
    const string MapName = "Map-Domasov";
    const string RootName = "Zahrada";
    const string Version = "_v2";
    const string MaterialFolder = "Assets/Materials/Garden";

    // Rozmery jsou navrzene k puvodni poloze zadni zdi rozhledny (konci na z = 93); kdyz je rozhledna v otevrene scene
    // posunuta, zahrada se posune s ni (MapBuildKit.TowerShift). Zadni okraj (zivy plot) je vzdy na okraji mapy.
    static readonly Rect BaseArea = Rect.MinMaxRect(-50.9f, 93.3f, -8f, 114.6f);
    static readonly Rect BaseTunnelHole = Rect.MinMaxRect(-34.4f, 93.6f, -25.6f, 97.5f);
    static readonly Rect BaseTunnelApproach = Rect.MinMaxRect(-38f, 93.3f, -34f, 97.8f);
    static readonly Rect BasePath = Rect.MinMaxRect(-38f, 99.9f, -8f, 101.5f);
    static readonly Rect[] BaseBeds =
    {
        Rect.MinMaxRect(-30f, 103.9f, -26f, 105.3f),
        Rect.MinMaxRect(-37.2f, 106.2f, -35.8f, 109.8f),
    };
    static readonly Vector3 BaseBench = new Vector3(-16f, 0f, 102.4f);

    // Skutecne rozlozeni (po posunu), pocita Layout().
    static Rect Area, TunnelHole, TunnelApproach, Path;
    static Rect[] Beds;
    static Vector3 BenchPosition;
    static Vector2 Offset;
    static float BackZ;          // zadni okraj (zivy plot)

    // Misto, kde nesmi nic pevneho stat: spawn tymu 1 u bodu 3 (bere se ze sceny).
    static Vector2 Spawn = new Vector2(-43.53f, 97.34f);
    const float SpawnClear = 9f;

    static void Layout(Transform map)
    {
        Vector3 shift = MapBuildKit.TowerShift(map);
        Offset = new Vector2(shift.x, shift.z);
        Bounds ground = MapBuildKit.GroundBounds(map);
        BackZ = ground.max.z - 0.35f;

        Rect Move(Rect r) => new Rect(r.position + Offset, r.size);
        Area = Move(BaseArea);
        Area = Rect.MinMaxRect(Mathf.Max(Area.xMin, ground.min.x + 0.3f), Area.yMin, Mathf.Min(Area.xMax, ground.max.x - 0.3f), BackZ);
        TunnelHole = Move(BaseTunnelHole);
        TunnelApproach = Move(BaseTunnelApproach);
        Path = Move(BasePath);
        Beds = System.Array.ConvertAll(BaseBeds, Move);
        BenchPosition = BaseBench + new Vector3(Offset.x, 0f, Offset.y);

        var spawn = GameObject.Find("SpawnPoint_3_Team1");
        Spawn = spawn != null ? new Vector2(spawn.transform.position.x, spawn.transform.position.z) : Spawn + Offset;
    }

    static string Marker(Transform map)
    {
        Vector3 shift = MapBuildKit.TowerShift(map);
        return $"{Version}_{shift.x:0.0}_{shift.z:0.0}";
    }

    static GardenSetup()
    {
        EditorApplication.delayCall += RunIfNeeded;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += RunIfNeeded;
        };
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += RunIfNeeded;
    }

    static void RunIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        var map = GameObject.Find(MapName);
        if (map == null) return;

        var root = map.transform.Find(RootName);
        if (root != null && root.Find(Marker(map.transform)) != null) return;

        if (Build(map.transform))
        {
            EditorSceneManager.MarkSceneDirty(map.scene);
            Debug.Log("[Zahrada] Zahrada za rozhlednou postavena. Uloz scenu (Ctrl+S).");
        }
        else if (retries++ < 30)
        {
            // Materialy dreva (MapDressingSetup) nebo textury jeste nejsou pripravene - zkus to za chvili znovu.
            EditorApplication.delayCall += RunIfNeeded;
        }
    }

    static int retries;

    [MenuItem("BrosOverwatch/Mapa/Postavit zahradu znovu")]
    static void RebuildMenu()
    {
        var map = GameObject.Find(MapName);
        if (map == null) return;

        if (Build(map.transform))
            EditorSceneManager.MarkSceneDirty(map.scene);
    }

    // ---------------- stavba ----------------

    class Mats
    {
        public Material lawn, gravel, hedge, bark, soil, grass, stem, conifer;
        public Material[] leaves, flowers;
        public Material planks, beams, apple;
    }

    static bool Build(Transform map)
    {
        var m = LoadMaterials();
        if (m == null) return false;

        var old = map.Find(RootName);
        if (old != null)
            Object.DestroyImmediate(old.gameObject);

        var root = new GameObject(RootName).transform;
        root.SetParent(map, true);
        new GameObject(Marker(map)).transform.SetParent(root, false);
        Layout(map);

        var random = new System.Random(1234);

        BuildLawn(root, m);
        BuildPath(root, m);
        BuildHedge(root, m);
        BuildFence(root, m);
        foreach (var bed in Beds)
            BuildBed(root, bed, m);
        BuildBench(root, m);
        BuildTrees(root, m, random);
        BuildBushes(root, m, random);
        BuildGrassAndFlowers(root, m, random);
        return true;
    }

    static Mats LoadMaterials()
    {
        string f = MaterialFolder;
        var m = new Mats
        {
            lawn = MapBuildKit.Mat(f + "/Lawn.mat", f + "/Lawn.png", Color.white, new Vector2(0.5f, 0.5f), 0.05f),
            gravel = MapBuildKit.Mat(f + "/Gravel.mat", f + "/Gravel.png", Color.white, new Vector2(0.6f, 0.6f), 0.05f),
            hedge = MapBuildKit.Mat(f + "/Hedge.mat", f + "/Leaves.png", new Color(0.78f, 0.9f, 0.72f), new Vector2(0.9f, 0.9f), 0.05f),
            bark = MapBuildKit.Mat(f + "/Bark.mat", f + "/Bark.png", Color.white, new Vector2(1f, 1f), 0.05f),
            soil = MapBuildKit.Mat(f + "/Soil.mat", null, new Color(0.24f, 0.16f, 0.1f), Vector2.one, 0.02f),
            grass = MapBuildKit.Mat(f + "/GrassBlades.mat", f + "/GrassBlades.png", Color.white, Vector2.one, 0.05f, alphaClip: true, doubleSided: true),
            stem = MapBuildKit.Mat(f + "/Stem.mat", null, new Color(0.2f, 0.45f, 0.15f), Vector2.one, 0.05f, doubleSided: true),
            conifer = MapBuildKit.Mat(f + "/Conifer.mat", f + "/Leaves.png", new Color(0.5f, 0.7f, 0.58f), new Vector2(0.8f, 0.8f), 0.05f),
            leaves = new[]
            {
                MapBuildKit.Mat(f + "/Leaves_A.mat", f + "/Leaves.png", Color.white, new Vector2(0.8f, 0.8f), 0.05f),
                MapBuildKit.Mat(f + "/Leaves_B.mat", f + "/Leaves.png", new Color(0.85f, 1f, 0.72f), new Vector2(0.8f, 0.8f), 0.05f),
                MapBuildKit.Mat(f + "/Leaves_C.mat", f + "/Leaves.png", new Color(1.1f, 1.05f, 0.66f), new Vector2(0.8f, 0.8f), 0.05f),
            },
            flowers = new[]
            {
                MapBuildKit.Mat(f + "/Flower_Red.mat", null, new Color(0.85f, 0.12f, 0.15f), Vector2.one, 0.2f),
                MapBuildKit.Mat(f + "/Flower_Yellow.mat", null, new Color(0.98f, 0.82f, 0.15f), Vector2.one, 0.2f),
                MapBuildKit.Mat(f + "/Flower_White.mat", null, new Color(0.95f, 0.95f, 0.92f), Vector2.one, 0.2f),
                MapBuildKit.Mat(f + "/Flower_Purple.mat", null, new Color(0.55f, 0.25f, 0.8f), Vector2.one, 0.2f),
            },
            apple = MapBuildKit.Mat(f + "/Apple.mat", null, new Color(0.8f, 0.1f, 0.08f), Vector2.one, 0.5f),
            planks = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wood/Wood_Planks.mat"),
            beams = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wood/Wood_Beam.mat"),
        };

        if (m.lawn == null || m.gravel == null || m.hedge == null || m.bark == null || m.grass == null || m.conifer == null
            || m.planks == null || m.beams == null || System.Array.IndexOf(m.leaves, null) >= 0)
            return null;
        return m;
    }

    // Travnik: tenke desky bez kolize nad zemi (zem pod nimi ma kolizi), kolem otvoru tunelu vynechane.
    static void BuildLawn(Transform root, Mats m)
    {
        var group = MapBuildKit.Group(root, "Travnik");
        var rects = new[]
        {
            Rect.MinMaxRect(Area.xMin, 97.6f, Area.xMax, Area.yMax),
            Rect.MinMaxRect(Area.xMin, Area.yMin, TunnelHole.xMin, 97.6f),
            Rect.MinMaxRect(TunnelHole.xMax, Area.yMin, Area.xMax, 97.6f),
        };
        foreach (var r in rects)
            MapBuildKit.Box(group, "Travnik", new Vector3(r.center.x, 0.01f, r.center.y), new Vector3(r.width, 0.02f, r.height),
                Quaternion.identity, m.lawn, false, true, false);
    }

    static void BuildPath(Transform root, Mats m)
    {
        var group = MapBuildKit.Group(root, "Cesta");
        MapBuildKit.Box(group, "Sterk", new Vector3(Path.center.x, 0.02f, Path.center.y), new Vector3(Path.width, 0.03f, Path.height),
            Quaternion.identity, m.gravel, false, true, false);

        // Obrubniky z tramku podel cesty.
        MapBuildKit.Beam(group, "Obrubnik", new Vector3(Path.xMin, 0.05f, Path.yMin), new Vector3(Path.xMax, 0.05f, Path.yMin), 0.1f, m.beams);
        MapBuildKit.Beam(group, "Obrubnik", new Vector3(Path.xMin, 0.05f, Path.yMax), new Vector3(Path.xMax, 0.05f, Path.yMax), 0.1f, m.beams);
    }

    // Zivy plot po okraji mapy (zaroven zabrani padu z mapy).
    static void BuildHedge(Transform root, Mats m)
    {
        var group = MapBuildKit.Group(root, "ZivyPlot");
        MapBuildKit.Box(group, "Plot_Zadni", new Vector3((Area.xMin - 0.3f + Area.xMax) * 0.5f, 0.85f, BackZ - 0.2f),
            new Vector3(Area.xMax - Area.xMin + 0.3f, 1.7f, 1.1f), m.hedge, true);

        // Bocni plot po okraji mapy za spawnem (spawn nechava volny).
        float z0 = Mathf.Max(Area.yMin, Spawn.y + SpawnClear + 0.3f), z1 = BackZ - 0.75f;
        if (z1 - z0 > 1f)
            MapBuildKit.Box(group, "Plot_Bocni", new Vector3(Area.xMin + 0.2f, 0.85f, (z0 + z1) * 0.5f), new Vector3(1.0f, 1.7f, z1 - z0), m.hedge, true);
    }

    // Drevene oploceni na vychodni strane zahrady s brankou na cestu.
    static void BuildFence(Transform root, Mats m)
    {
        var group = MapBuildKit.Group(root, "Oploceni");
        float x = Area.xMax;
        FenceRun(group, new Vector3(x, 0f, Area.yMin + 0.2f), new Vector3(x, 0f, Path.yMin - 0.2f), m);
        FenceRun(group, new Vector3(x, 0f, Path.yMax + 0.2f), new Vector3(x, 0f, BackZ - 0.8f), m);
    }

    static void FenceRun(Transform parent, Vector3 from, Vector3 to, Mats m)
    {
        var run = new GameObject("Plot").transform;
        run.SetParent(parent, true);

        Vector3 along = to - from;
        int posts = Mathf.Max(2, Mathf.RoundToInt(along.magnitude / 2f) + 1);
        for (int i = 0; i < posts; i++)
        {
            Vector3 p = from + along * (i / (float)(posts - 1));
            MapBuildKit.Beam(run, "Sloupek", p, p + Vector3.up * 1.05f, 0.12f, m.beams);
        }
        foreach (float y in new[] { 0.35f, 0.8f })
            MapBuildKit.Box(run, "Lat", (from + to) * 0.5f + Vector3.up * y, new Vector3(0.05f, 0.14f, along.magnitude), m.planks, false);

        var collider = run.gameObject.AddComponent<BoxCollider>();
        collider.center = run.InverseTransformPoint((from + to) * 0.5f + Vector3.up * 0.55f);
        collider.size = new Vector3(0.15f, 1.1f, along.magnitude);
    }

    static void BuildBed(Transform root, Rect bed, Mats m)
    {
        var group = new GameObject("Zahon").transform;
        group.SetParent(MapBuildKit.Group(root, "Zahony"), true);

        const float h = 0.4f, t = 0.12f;
        Vector3 c = new Vector3(bed.center.x, h * 0.5f, bed.center.y);
        MapBuildKit.Box(group, "Prkno", c + new Vector3(0f, 0f, -bed.height * 0.5f + t * 0.5f), new Vector3(bed.width, h, t), m.planks, false);
        MapBuildKit.Box(group, "Prkno", c + new Vector3(0f, 0f, bed.height * 0.5f - t * 0.5f), new Vector3(bed.width, h, t), m.planks, false);
        MapBuildKit.Box(group, "Prkno", c + new Vector3(-bed.width * 0.5f + t * 0.5f, 0f, 0f), new Vector3(t, h, bed.height - 2f * t), m.planks, false);
        MapBuildKit.Box(group, "Prkno", c + new Vector3(bed.width * 0.5f - t * 0.5f, 0f, 0f), new Vector3(t, h, bed.height - 2f * t), m.planks, false);
        MapBuildKit.Box(group, "Hlina", new Vector3(bed.center.x, h - 0.06f, bed.center.y), new Vector3(bed.width - 2f * t, 0.04f, bed.height - 2f * t),
            Quaternion.identity, m.soil, false, true, false);

        var collider = group.gameObject.AddComponent<BoxCollider>();
        collider.center = group.InverseTransformPoint(c);
        collider.size = new Vector3(bed.width, h, bed.height);
    }

    static void BuildBench(Transform root, Mats m)
    {
        var group = new GameObject("Lavicka").transform;
        group.SetParent(root, true);
        group.position = BenchPosition;

        Vector3 p = BenchPosition;
        // Lavicka stoji podel cesty, sedi se smerem k ceste (-z), operadlo je na +z.
        for (int i = 0; i < 3; i++)
            MapBuildKit.Box(group, "Sedak", p + new Vector3(0f, 0.45f, -0.15f + i * 0.15f), new Vector3(1.8f, 0.05f, 0.13f), m.planks, false);
        for (int i = 0; i < 2; i++)
            MapBuildKit.Box(group, "Operadlo", p + new Vector3(0f, 0.65f + i * 0.2f, 0.24f), new Vector3(1.8f, 0.13f, 0.05f), m.planks, false);
        foreach (float x in new[] { -0.75f, 0.75f })
        {
            MapBuildKit.Beam(group, "Noha", p + new Vector3(x, 0f, -0.15f), p + new Vector3(x, 0.43f, -0.15f), 0.08f, m.beams);
            MapBuildKit.Beam(group, "Noha", p + new Vector3(x, 0f, 0.24f), p + new Vector3(x, 0.95f, 0.24f), 0.08f, m.beams);
        }

        var collider = group.gameObject.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 0.48f, 0.05f);
        collider.size = new Vector3(1.85f, 0.96f, 0.5f);
    }

    // ---------------- stromy a kere ----------------

    struct TreeSpot
    {
        public Vector2 position;
        public bool conifer;
        public float size;
        public bool apples;
    }

    static readonly TreeSpot[] Trees =
    {
        new TreeSpot { position = new Vector2(-48.2f, 111.2f), conifer = true, size = 1.1f },
        new TreeSpot { position = new Vector2(-41f, 110.6f), size = 1.05f },
        new TreeSpot { position = new Vector2(-33.8f, 112f), conifer = true, size = 0.95f },
        new TreeSpot { position = new Vector2(-26.5f, 110.8f), size = 1.15f, apples = true },
        new TreeSpot { position = new Vector2(-19.5f, 112f), conifer = true, size = 1.2f },
        new TreeSpot { position = new Vector2(-12.5f, 110.5f), size = 1f },
        new TreeSpot { position = new Vector2(-31.5f, 107.3f), size = 0.75f, apples = true },
        new TreeSpot { position = new Vector2(-21f, 106f), size = 0.8f, apples = true },
        new TreeSpot { position = new Vector2(-20.8f, 95.6f), size = 0.85f },
        new TreeSpot { position = new Vector2(-11.5f, 96f), conifer = true, size = 0.8f },
    };

    static void BuildTrees(Transform root, Mats m, System.Random random)
    {
        var group = MapBuildKit.Group(root, "Stromy");
        var blobs = new[] { MapBuildKit.BlobMesh(0), MapBuildKit.BlobMesh(1), MapBuildKit.BlobMesh(2), MapBuildKit.BlobMesh(3) };
        var cone = MapBuildKit.ConeMesh();

        int index = 0;
        foreach (var spot in Trees)
        {
            Vector2 at = spot.position + Offset;
            if (!SolidAllowed(at, 1.5f)) continue;

            var tree = new GameObject((spot.conifer ? "Jehlican_" : "Strom_") + index++).transform;
            tree.SetParent(group, true);
            tree.position = new Vector3(at.x, 0f, at.y);

            if (spot.conifer)
                Conifer(tree, spot.size, cone, m, random);
            else
                Deciduous(tree, spot, blobs, m, random);
        }
    }

    static GameObject Trunk(Transform tree, float height, float radius, Material bark)
    {
        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Kmen";
        trunk.transform.SetParent(tree, false);
        trunk.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        trunk.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        trunk.GetComponent<Renderer>().sharedMaterial = bark;
        GameObjectUtility.SetStaticEditorFlags(trunk, StaticEditorFlags.BatchingStatic);
        return trunk;
    }

    static void Deciduous(Transform tree, TreeSpot spot, Mesh[] blobs, Mats m, System.Random random)
    {
        float s = spot.size;
        float trunkHeight = (2.6f + (float)random.NextDouble() * 0.6f) * s;
        Trunk(tree, trunkHeight + 0.6f * s, 0.22f * s, m.bark);

        var leaves = m.leaves[random.Next(m.leaves.Length)];
        Vector3 top = tree.position + Vector3.up * (trunkHeight + 1.3f * s);

        // Hlavni koruna + nekolik mensich kolem; vetve k nim z vrcholu kmene.
        var crown = new List<Vector3> { top };
        MapBuildKit.MeshObject(tree, "Koruna", blobs[random.Next(blobs.Length)], top, RandomYaw(random), Vector3.one * 2.3f * s, leaves);
        int count = 4 + random.Next(3);
        for (int i = 0; i < count; i++)
        {
            float a = (i + (float)random.NextDouble() * 0.5f) / count * Mathf.PI * 2f;
            float r = (1.3f + (float)random.NextDouble() * 0.5f) * s;
            Vector3 p = top + new Vector3(Mathf.Cos(a) * r, ((float)random.NextDouble() - 0.4f) * 1.1f * s, Mathf.Sin(a) * r);
            float scale = (1.2f + (float)random.NextDouble() * 0.6f) * s;
            MapBuildKit.MeshObject(tree, "Koruna", blobs[random.Next(blobs.Length)], p, RandomYaw(random), Vector3.one * scale, leaves);
            crown.Add(p);

            if (i % 2 == 0)
                MapBuildKit.Beam(tree, "Vetev", tree.position + Vector3.up * trunkHeight, Vector3.Lerp(tree.position + Vector3.up * trunkHeight, p, 0.75f),
                    0.12f * s, m.bark);
        }

        if (!spot.apples) return;

        // Jablka na povrchu koruny.
        for (int i = 0; i < 14; i++)
        {
            Vector3 c = crown[random.Next(crown.Count)];
            Vector3 dir = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 1.2f - 0.7f, (float)random.NextDouble() * 2f - 1f).normalized;
            MapBuildKit.MeshObject(tree, "Jablko", blobs[0], c + dir * 1.25f * s, RandomYaw(random), Vector3.one * 0.09f, m.apple, false);
        }
    }

    static void Conifer(Transform tree, float s, Mesh cone, Mats m, System.Random random)
    {
        Trunk(tree, 2.2f * s, 0.2f * s, m.bark);

        const int layers = 4;
        for (int i = 0; i < layers; i++)
        {
            float t = i / (float)(layers - 1);
            float radius = Mathf.Lerp(2.1f, 0.9f, t) * s;
            float height = Mathf.Lerp(2.4f, 1.8f, t) * s;
            float y = (1.1f + i * 1.35f) * s;
            MapBuildKit.MeshObject(tree, "Jehlici", cone, tree.position + Vector3.up * y, RandomYaw(random),
                new Vector3(radius, height, radius), m.conifer);
        }
    }

    static readonly Vector2[] BushSpots =
    {
        new Vector2(-23.5f, 94.4f), new Vector2(-17.5f, 94.5f), new Vector2(-14f, 94.3f), new Vector2(-9.4f, 98.2f),
        new Vector2(-9.3f, 104f), new Vector2(-9.5f, 107.5f), new Vector2(-15.5f, 113f), new Vector2(-23f, 113.1f),
        new Vector2(-30f, 113.2f), new Vector2(-37.5f, 113f), new Vector2(-44.5f, 113.1f), new Vector2(-49.6f, 107.5f),
        new Vector2(-24.5f, 102.8f), new Vector2(-33f, 102.6f), new Vector2(-12.8f, 103f),
    };

    static void BuildBushes(Transform root, Mats m, System.Random random)
    {
        var group = MapBuildKit.Group(root, "Kere");
        var blobs = new[] { MapBuildKit.BlobMesh(1), MapBuildKit.BlobMesh(2), MapBuildKit.BlobMesh(3) };

        foreach (var baseSpot in BushSpots)
        {
            Vector2 spot = baseSpot + Offset;
            if (!SolidAllowed(spot, 0.8f)) continue;

            var leaves = m.leaves[random.Next(m.leaves.Length)];
            int parts = 2 + random.Next(2);
            for (int i = 0; i < parts; i++)
            {
                float scale = 0.55f + (float)random.NextDouble() * 0.4f;
                Vector3 p = new Vector3(spot.x + ((float)random.NextDouble() - 0.5f) * 1.1f, scale * 0.55f, spot.y + ((float)random.NextDouble() - 0.5f) * 1.1f);
                MapBuildKit.MeshObject(group, "Ker", blobs[random.Next(blobs.Length)], p, RandomYaw(random), Vector3.one * scale, leaves);
            }
        }
    }

    // ---------------- trava a kvetiny (spojene do par meshu) ----------------

    static void BuildGrassAndFlowers(Transform root, Mats m, System.Random random)
    {
        var group = MapBuildKit.Group(root, "Trava");

        // Trsy travy: dva zkrizene ctverce s alfa texturou.
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        int placed = 0;
        for (int attempt = 0; attempt < 4000 && placed < 900; attempt++)
        {
            var p = new Vector2(Mathf.Lerp(Area.xMin + 0.5f, Area.xMax - 0.5f, (float)random.NextDouble()),
                Mathf.Lerp(Area.yMin + 0.3f, Area.yMax - 1.2f, (float)random.NextDouble()));
            if (!SoftAllowed(p)) continue;

            float w = 0.5f + (float)random.NextDouble() * 0.4f;
            float h = 0.3f + (float)random.NextDouble() * 0.3f;
            float yaw = (float)random.NextDouble() * Mathf.PI;
            for (int q = 0; q < 2; q++)
            {
                float a = yaw + q * Mathf.PI * 0.5f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (w * 0.5f);
                Vector3 c = new Vector3(p.x, 0.02f, p.y);
                int start = vertices.Count;
                vertices.AddRange(new[] { c - d, c + d, c + d + Vector3.up * h, c - d + Vector3.up * h });
                normals.AddRange(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
                uvs.AddRange(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
            placed++;
        }

        var grassMesh = new Mesh { name = "GardenGrass" };
        grassMesh.SetVertices(vertices);
        grassMesh.SetNormals(normals);
        grassMesh.SetUVs(0, uvs);
        grassMesh.SetTriangles(triangles, 0);
        grassMesh.RecalculateBounds();
        MapBuildKit.SaveMesh(grassMesh, "GardenGrass");
        MapBuildKit.MeshObject(group, "Trsy", grassMesh, Vector3.zero, Quaternion.identity, Vector3.one, m.grass, false);

        // Kvetiny: stonek (tenky ctverec) a kvet (osmisten); barvy jako submeshe.
        var fv = new List<Vector3>();
        var fuv = new List<Vector2>();
        var perColor = new List<int>[m.flowers.Length + 1];
        for (int i = 0; i < perColor.Length; i++)
            perColor[i] = new List<int>();

        void Flower(Vector2 p, float ground, int color)
        {
            float h = 0.25f + (float)random.NextDouble() * 0.25f;
            float r = 0.06f + (float)random.NextDouble() * 0.05f;
            Vector3 b = new Vector3(p.x, ground, p.y);
            Vector3 top = b + Vector3.up * h;
            float a = (float)random.NextDouble() * Mathf.PI;
            Vector3 side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.012f;

            int s = fv.Count;
            fv.AddRange(new[] { b - side, b + side, top + side, top - side });
            fuv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            perColor[m.flowers.Length].AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });

            // Osmisten: 6 vrcholu, 8 sten (sdilene vrcholy staci, je maly).
            s = fv.Count;
            fv.AddRange(new[]
            {
                top + Vector3.up * r * 0.7f, top - Vector3.up * r * 0.5f,
                top + new Vector3(r, 0f, 0f), top + new Vector3(0f, 0f, r), top + new Vector3(-r, 0f, 0f), top + new Vector3(0f, 0f, -r),
            });
            for (int i = 0; i < 6; i++)
                fuv.Add(Vector2.zero);
            for (int i = 0; i < 4; i++)
            {
                int e0 = s + 2 + i, e1 = s + 2 + (i + 1) % 4;
                perColor[color].AddRange(new[] { s, e1, e0 });
                perColor[color].AddRange(new[] { s + 1, e0, e1 });
            }
        }

        // Plne zahony.
        foreach (var bed in Beds)
            for (int i = 0; i < 70; i++)
            {
                var p = new Vector2(Mathf.Lerp(bed.xMin + 0.2f, bed.xMax - 0.2f, (float)random.NextDouble()),
                    Mathf.Lerp(bed.yMin + 0.2f, bed.yMax - 0.2f, (float)random.NextDouble()));
                Flower(p, 0.36f, random.Next(m.flowers.Length));
            }

        // Ridke shluky v trave.
        int clusters = 0;
        for (int attempt = 0; attempt < 600 && clusters < 45; attempt++)
        {
            var c = new Vector2(Mathf.Lerp(Area.xMin + 1f, Area.xMax - 1f, (float)random.NextDouble()),
                Mathf.Lerp(Area.yMin + 1f, Area.yMax - 1.5f, (float)random.NextDouble()));
            if (!SoftAllowed(c)) continue;

            int color = random.Next(m.flowers.Length);
            int n = 3 + random.Next(5);
            for (int i = 0; i < n; i++)
            {
                var p = c + new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 0.9f;
                if (SoftAllowed(p))
                    Flower(p, 0.02f, color);
            }
            clusters++;
        }

        var flowerMesh = new Mesh { name = "GardenFlowers" };
        flowerMesh.SetVertices(fv);
        flowerMesh.SetUVs(0, fuv);
        flowerMesh.subMeshCount = perColor.Length;
        for (int i = 0; i < perColor.Length; i++)
            flowerMesh.SetTriangles(perColor[i], i);
        flowerMesh.RecalculateNormals();
        flowerMesh.RecalculateBounds();
        MapBuildKit.SaveMesh(flowerMesh, "GardenFlowers");

        var flowers = MapBuildKit.MeshObject(group, "Kvetiny", flowerMesh, Vector3.zero, Quaternion.identity, Vector3.one, m.flowers[0], false);
        var materials = new Material[perColor.Length];
        for (int i = 0; i < m.flowers.Length; i++)
            materials[i] = m.flowers[i];
        materials[m.flowers.Length] = m.stem;
        flowers.GetComponent<MeshRenderer>().sharedMaterials = materials;
    }

    // ---------------- pravidla rozmisteni ----------------

    // Pevne veci (stromy, kere): ne na spawnu, u tunelu, na ceste a v zahonech.
    static bool SolidAllowed(Vector2 p, float radius)
    {
        if (Vector2.Distance(p, Spawn) < SpawnClear + radius) return false;
        if (Grow(TunnelHole, radius).Contains(p) || Grow(TunnelApproach, radius).Contains(p)) return false;
        if (Grow(Path, radius).Contains(p)) return false;
        foreach (var bed in Beds)
            if (Grow(bed, radius).Contains(p)) return false;
        return Area.Contains(p);
    }

    // Trava a kvetiny: jen mimo cestu, zahony a tunel (na spawnu muzou byt).
    static bool SoftAllowed(Vector2 p)
    {
        if (Grow(TunnelHole, 0.3f).Contains(p) || Grow(Path, 0.25f).Contains(p)) return false;
        foreach (var bed in Beds)
            if (Grow(bed, 0.15f).Contains(p)) return false;
        if (p.y > BackZ - 1f) return false;   // pod zivym plotem
        if (p.x < Area.xMin + 0.8f && p.y > Spawn.y + SpawnClear) return false;
        if (Mathf.Abs(p.x - BenchPosition.x) < 1.1f && Mathf.Abs(p.y - BenchPosition.z) < 0.5f) return false;
        return Area.Contains(p);
    }

    static Rect Grow(Rect r, float by)
    {
        return Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);
    }

    static Quaternion RandomYaw(System.Random random)
    {
        return Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
    }
}
