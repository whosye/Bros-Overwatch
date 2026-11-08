using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Druha verze rozvrzeni chat (navrh s novou zapadni casti):
//   - sever mapy za sikmou carou se orizne (objekty se vypnou, podel cary novy zivy plot a neviditelna stena);
//   - na zapad pribude kopcovita louka obehnana zivym plotem: start utocniku (SZ roh) a rozhledna s bodem A;
//   - do mapy se z ni vchazi tremi dirami v zapadni rade stromu (dve na severu, jedna u male chaty) - do dobyti A
//     je v nich modra stena, kterou projdou jen obranci (spawn obrancu pro A je v male chate);
//   - tunel: nova vetev od rozhledny (rampa) pod zapadni hranici k hlavni chodbe pod senem (otevre se po A),
//     sever puvodniho tunelu se zazdi; karburator lezi v nove vetvi;
//   - potok (reka z toboganu) zmizi, zelena skluzavka se otoci do bazenu;
//   - snezna rolba stoji v kulne u male chaty.
// Spousti se z ChataKonceptSetup (po prvni verzi); kazda cast jednou (znacka _Prvek_V2_*), rucni upravy zustanou.
public static class ChataNovaCast
{
    const string MatDir = "Assets/Materials/Koncept/";
    const string MeshPath = "Assets/Models/Koncept/NovaCastTeren.asset";

    // nova cast: obrys (x, z) - SZ roh, napojeni na zapadni hranici mapy, JZ...
    static readonly Vector2[] Area =
    {
        new Vector2(-140f, 100f), new Vector2(-51.2f, 70.9f), new Vector2(-51.2f, -21.5f), new Vector2(-57f, -21.5f), new Vector2(-140f, 21f),
    };
    // orez severu: cara z SZ rohu nove casti az k mistu nad silnici, pak rovne na vychod
    static readonly Vector2 CutWest = new Vector2(-140f, 100f), CutBend = new Vector2(8f, 51.5f);
    const float CutEastX = 45f;

    static readonly Vector3 TowerTarget = new Vector3(-82f, 0f, 38f);     // stred rozhledny (bod A)
    static readonly Vector3 AttackStart = new Vector3(-128f, 0f, 86f);
    static readonly Vector3 AttackAfterA = new Vector3(-75f, 0f, 50f);
    const float WestWallX = -51.2f;
    static readonly (float a, float b)[] Holes = { (46f, 53f), (35.5f, 38.5f), (-2f, 2f) };   // diry v zapadni hranici (z)

    // tunel: nova chodba z 30..33 od x -72 k hlavni chodbe (x -1.5); rampa od povrchu x -72..-68, z 18..30
    const float TunZ0 = 30f, TunZ1 = 33f, TunX0 = -72f, TunFloor = -4f, TunCeil = -0.5f;
    static readonly Rect RampRect = Rect.MinMaxRect(-72f, 18f, -68f, 30f);

    static Material hedge, lawn, rock, bark, plank, post, hay, logs, roof, shed, barrier, concrete, concreteDark, lamp, metal;
    static Material[] leaves;

    // kopce (x, z, polomer, vyska) a rovna mista (x, z, polomer)
    static readonly Vector4[] Hills =
    {
        new Vector4(-118f, 66f, 14f, 4.2f), new Vector4(-104f, 86f, 9f, 2.4f), new Vector4(-128f, 44f, 10f, 3.0f),
        new Vector4(-110f, 14f, 13f, 3.6f), new Vector4(-86f, 6f, 9f, 2.2f), new Vector4(-96f, 41f, 4.5f, 1.4f),
        new Vector4(-69f, 40f, 4f, 1.3f), new Vector4(-88f, 72f, 8f, 2.0f), new Vector4(-60f, 20f, 6f, 1.6f),
    };
    static readonly Vector3[] Flats =
    {
        new Vector3(-128f, 86f, 9f), new Vector3(-82f, 38f, 11f), new Vector3(-75f, 50f, 6f),
    };

    public static bool Apply(Transform map, Transform root, System.Func<Transform, string, System.Action, bool> feature)
    {
        if (!LoadMaterials()) return false;
        bool changed = false;
        changed |= feature(root, "V2_Uklid", () => CleanupV1(map, root));
        changed |= feature(root, "V2_Rozhledna", () => MoveTower(map));
        changed |= feature(root, "V2_Orez", () => CutNorth(map, root));
        changed |= feature(root, "V2_NovaCast", () => NewArea(root));
        changed |= feature(root, "V2_Diry", () => HedgeHoles(map, root));
        changed |= feature(root, "V2_Tunel", () => TunnelBranch(map, root));
        changed |= feature(root, "V2_Tobogan", () => Slide(map));
        changed |= feature(root, "V2_BodySpawny", () => PointsAndSpawns(map));
        changed |= feature(root, "V2_RolbaKulna", () => GroomerShed(map, root));
        return changed;
    }

    // ---------------- materialy ----------------

    static Material Load(string path) => AssetDatabase.LoadAssetAtPath<Material>(path);

    static bool LoadMaterials()
    {
        hedge = Load("Assets/Materials/Garden/Hedge.mat")
            ?? MapBuildKit.Mat(MatDir + "ZivyPlot.mat", "Assets/Materials/Garden/Leaves.png", new Color(0.78f, 0.9f, 0.72f), new Vector2(0.9f, 0.9f), 0.05f);
        lawn = Load("Assets/Materials/Garden/Lawn.mat")
            ?? MapBuildKit.Mat(MatDir + "Louka.mat", "Assets/Materials/Garden/Lawn.png", Color.white, new Vector2(0.5f, 0.5f), 0.05f);
        rock = MapBuildKit.Mat(MatDir + "Kamen.mat", null, new Color(0.52f, 0.51f, 0.5f), Vector2.one, 0.15f);
        bark = Load("Assets/Materials/Forest/Forest_Bark.mat") ?? Load("Assets/Materials/Garden/Bark.mat");
        leaves = new[]
        {
            Load("Assets/Materials/Forest/Forest_Broadleaf_0.mat"), Load("Assets/Materials/Forest/Forest_Broadleaf_1.mat"),
            Load("Assets/Materials/Forest/Forest_Broadleaf_2.mat"),
        };
        plank = Load(MatDir + "Plot.mat");
        post = Load(MatDir + "Sloupek.mat");
        hay = Load(MatDir + "Seno.mat");
        logs = Load(MatDir + "Polena.mat");
        roof = Load(MatDir + "StrechaKulny.mat");
        shed = Load(MatDir + "Kulna.mat");
        metal = Load(MatDir + "Mriz.mat");
        concrete = Load("Assets/Materials/Tunnel/Tunnel_Concrete.mat");
        concreteDark = Load("Assets/Materials/Tunnel/Tunnel_ConcreteDark.mat");
        lamp = Load("Assets/Materials/Tunnel/Tunnel_Lamp.mat");

        barrier = Load(MatDir + "ModraStena.mat");
        if (barrier == null)
        {
            MapBuildKit.EnsureFolder(MatDir.TrimEnd('/'));
            barrier = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.3f, 0.62f, 1f, 0.32f) };
            AssetDatabase.CreateAsset(barrier, MatDir + "ModraStena.mat");
        }
        bool ok = hedge != null && lawn != null && rock != null && bark != null && plank != null && post != null && hay != null
            && logs != null && roof != null && shed != null && metal != null && concrete != null && concreteDark != null && lamp != null;
        foreach (var l in leaves) ok &= l != null;
        if (!ok) Debug.LogWarning("[Koncept] Nektere materialy chybi (Garden / Forest / Tunnel) - nova cast se postavi pozdeji.");
        return ok;
    }

    // ---------------- geometrie ----------------

    static float CutZ(float x) => x >= CutBend.x ? CutBend.y : Mathf.Lerp(CutWest.y, CutBend.y, (x - CutWest.x) / (CutBend.x - CutWest.x));

    static bool Inside(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        return inside;
    }

    static float EdgeDistance(Vector2 p, Vector2[] poly)
    {
        float best = float.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 a = poly[j], b = poly[i], ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }

    // Vyska nove casti v (x, z): kopce, u okraju, startu, rozhledny a rampy tunelu rovina.
    public static float H(float x, float z)
    {
        var p = new Vector2(x, z);
        float h = 0f;
        foreach (var hill in Hills)
        {
            float d2 = (x - hill.x) * (x - hill.x) + (z - hill.y) * (z - hill.y);
            h += hill.w * Mathf.Exp(-d2 / (hill.z * hill.z));
        }
        float fade = Inside(p, Area) ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 12f, EdgeDistance(p, Area))) : 0f;
        foreach (var f in Flats)
            fade *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(f.z, f.z + 6f, Vector2.Distance(p, new Vector2(f.x, f.y))));
        float rampDist = Mathf.Max(Mathf.Max(RampRect.xMin - x, x - RampRect.xMax), Mathf.Max(RampRect.yMin - z, z - RampRect.yMax));
        fade *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 7f, rampDist));
        return 0.06f + h * fade;
    }

    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material, bool collider = true, float yaw = 0f)
    {
        var go = MapBuildKit.Box(parent, name, center, size, Quaternion.Euler(0f, yaw, 0f), material, collider);
        GameObjectUtility.SetStaticEditorFlags(go, 0);
        return go;
    }

    static void Invisible(Transform parent, string name, Vector3 center, Vector3 size, float yaw = 0f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, yaw, 0f));
        go.AddComponent<BoxCollider>().size = size;
    }

    // Zivy plot podel useku a-b (vyska 3,2 m) a neviditelna stena nad nim.
    static void HedgeLine(Transform parent, Vector2 a, Vector2 b)
    {
        Vector2 d = b - a;
        float length = d.magnitude;
        float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        int pieces = Mathf.Max(1, Mathf.CeilToInt(length / 6f));
        for (int i = 0; i < pieces; i++)
        {
            Vector2 m = Vector2.Lerp(a, b, (i + 0.5f) / pieces);
            float baseY = Mathf.Min(0f, H(m.x, m.y) - 0.1f);
            Box(parent, "ZivyPlot", new Vector3(m.x, baseY + 1.45f, m.y), new Vector3(1.4f, 3.5f, length / pieces + 0.6f), hedge, true, yaw);
        }
        Vector2 c = (a + b) * 0.5f;
        Invisible(parent, "Stena", new Vector3(c.x, 14f, c.y), new Vector3(0.6f, 28f, length), yaw);
    }

    // ---------------- 1. uklid prvni verze (sever) ----------------

    static void CleanupV1(Transform map, Transform root)
    {
        foreach (var path in new[]
        {
            "Hranice/Plot_Sever", "Hranice/Plot_Sever_Stena", "Hranice/Plot_Sever_StenaTobogan", "Hranice/Branka_Silnice",
            "Hranice/Branka_Zapad", "Hranice/Tobogan_Pruchod", "Kryty", "TunelMrize/Mriz_Rozhledna",
        })
        {
            var t = root.Find(path);
            if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
        }

        // posun rozhledny nesmi prestavet tunel a zahradu (pocitaji se od ni): zapamatovat soucasny posun
        if (map.Find("_TowerShiftPin") == null)
        {
            Vector3 shift = MapBuildKit.TowerShift(map);
            var pin = new GameObject("_TowerShiftPin").transform;
            pin.SetParent(map, false);
            pin.localPosition = shift;
            Undo.RegisterCreatedObjectUndo(pin.gameObject, "Koncept");
        }
    }

    // ---------------- 2. rozhledna ----------------

    static void MoveTower(Transform map)
    {
        var tower = map.Find("Rozhledna");
        if (tower == null) { Debug.LogWarning("[Koncept] Rozhledna ve scene neni."); return; }
        var renderers = tower.GetComponentsInChildren<Renderer>();
        bool any = false;
        Bounds b = default;
        foreach (var r in renderers)
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return;
        Vector3 shift = new Vector3(TowerTarget.x - b.center.x, H(TowerTarget.x, TowerTarget.z) - 0.06f, TowerTarget.z - b.center.z);
        Undo.RecordObject(tower, "Koncept");
        tower.position += shift;
        Debug.Log($"[Koncept] Rozhledna presunuta o {shift} (novy stred {TowerTarget}).");
    }

    // ---------------- 3. orez severu ----------------

    static readonly string[] CutSkip = { "KonceptUtoku", "Rozhledna", "Tunel", "PavoukuvWorkshop", "HraniceProHrace", "ProvozYaris", "Props", "_TowerShiftPin" };

    static void CutNorth(Transform map, Transform root)
    {
        int disabled = 0;
        foreach (var t in map.GetComponentsInChildren<Transform>(true))
        {
            if (t == map || !t.gameObject.activeSelf) continue;
            if (t.GetComponent<Renderer>() == null && t.GetComponent<Collider>() == null) continue;
            string path = Path(map, t);
            bool skip = false;
            foreach (var s in CutSkip) if (path.Contains(s)) { skip = true; break; }
            if (skip) continue;

            Bounds b;
            var r = t.GetComponent<Renderer>();
            var c = t.GetComponent<Collider>();
            if (r != null) b = r.bounds; else b = c.bounds;
            if (b.size.x > 40f || b.size.z > 40f || b.max.y < -0.6f) continue;
            bool north = true;
            foreach (float x in new[] { b.min.x, b.max.x })
                if (b.min.z < CutZ(x) + 0.3f) north = false;
            if (!north) continue;
            Undo.RecordObject(t.gameObject, "Koncept");
            t.gameObject.SetActive(false);
            disabled++;
        }

        var g = MapBuildKit.Group(root, "Orez");
        HedgeLine(g, CutWest, CutBend);
        HedgeLine(g, CutBend, new Vector2(CutEastX, CutBend.y));
        Debug.Log($"[Koncept] Sever orezan: vypnuto {disabled} objektu za sikmou carou, podel ni zivy plot.");
    }

    static string Path(Transform map, Transform t)
    {
        var parts = new List<string>();
        while (t != null && t != map) { parts.Add(t.name); t = t.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    // ---------------- 4. nova cast ----------------

    static void NewArea(Transform root)
    {
        var g = MapBuildKit.Group(root, "NovaCast");
        BuildTerrain(g);

        // obvodovy zivy plot (vychodni strana = puvodni rada stromu se zdi)
        var fence = MapBuildKit.Group(g, "Ohraniceni");
        HedgeLine(fence, Area[4], Area[0]);                       // zapad
        HedgeLine(fence, Area[4], Area[3]);                       // jih (sikmo)
        HedgeLine(fence, Area[3], Area[2]);                       // napojeni na hranici mapy
        // (sever = orezova cara, postavena v CutNorth)

        Covers(MapBuildKit.Group(g, "Kryty"));
        Trees(MapBuildKit.Group(g, "Stromy"));
    }

    static void BuildTerrain(Transform parent)
    {
        const float step = 2f, x0 = -142f, x1 = -49f, z0 = -24f, z1 = 102f;
        int nx = Mathf.RoundToInt((x1 - x0) / step), nz = Mathf.RoundToInt((z1 - z0) / step);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float x = x0 + i * step, z = z0 + j * step;
                vertices.Add(new Vector3(x, H(x, z), z));
                uvs.Add(new Vector2(x, z));
            }
        var triangles = new List<int>();
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                float cx = x0 + (i + 0.5f) * step, cz = z0 + (j + 0.5f) * step;
                var c = new Vector2(cx, cz);
                bool keep = Inside(c, Area) || (EdgeDistance(c, Area) < 2.5f && cx > -142f);
                if (!keep) continue;
                if (cx > RampRect.xMin && cx < RampRect.xMax && cz > RampRect.yMin && cz < RampRect.yMax) continue;   // otvor rampy tunelu
                int v = j * (nx + 1) + i;
                triangles.AddRange(new[] { v, v + nx + 1, v + 1, v + 1, v + nx + 1, v + nx + 2 });
            }
        var mesh = new Mesh { name = "NovaCastTeren", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        MapBuildKit.EnsureFolder("Assets/Models/Koncept");
        if (AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath) != null) AssetDatabase.DeleteAsset(MeshPath);
        AssetDatabase.CreateAsset(mesh, MeshPath);

        var go = new GameObject("Teren");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = lawn;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    static Vector3 On(float x, float z, float lift = 0f) => new Vector3(x, H(x, z) + lift, z);

    static void Rock(Transform parent, float x, float z, Vector3 size, int seed)
    {
        var go = MapBuildKit.MeshObject(parent, "Kamen", MapBuildKit.BlobMesh(60 + seed % 6), On(x, z, size.y * 0.3f),
            Quaternion.Euler(0f, seed * 53f, 0f), size * 0.5f, rock);
        GameObjectUtility.SetStaticEditorFlags(go, 0);
        go.AddComponent<BoxCollider>().size = new Vector3(1.6f, 1.5f, 1.6f);
    }

    static void Covers(Transform g)
    {
        // kulna se strechou (vyvysene misto utocniku - nahoru pres bednu)
        Vector3 k = On(-102f, 52f);
        Box(g, "Kulna", k + Vector3.up * 1.15f, new Vector3(5f, 2.3f, 4f), shed, true, 15f);
        Box(g, "Kulna_Strecha", k + Vector3.up * 2.4f, new Vector3(5.8f, 0.2f, 4.8f), roof, true, 15f);
        Box(g, "Kulna_Bedna", On(-99.3f, 54.2f, 0.55f), new Vector3(1.1f, 1.1f, 1.1f), shed, true, 15f);
        // senik (dve patra baliku) a hromada dreva
        Vector3 s = On(-112f, 30f);
        Box(g, "Senik_1", s + Vector3.up * 0.75f, new Vector3(3.4f, 1.5f, 1.6f), hay, true, -20f);
        Box(g, "Senik_2", s + new Vector3(0.4f, 2.0f, 0f), new Vector3(1.7f, 1.0f, 1.6f), hay, true, -20f);
        Vector3 p = On(-92f, 64f);
        for (int i = 0; i < 3; i++)
            Box(g, "Poleno", p + new Vector3(0f, 0.3f + i * 0.5f, i == 1 ? 0.2f : 0f), new Vector3(4.2f - i * 0.6f, 0.5f, 1.6f - i * 0.3f), logs, true, 30f);
        // kamenna zidka pred rozhlednou (kryt utocniku pri utoku na bod A) s mezerou uprostred
        foreach (var (x0, x1) in new[] { (-93f, -86f), (-82f, -75f) })
            for (float x = x0; x < x1 - 0.1f; x += 1.75f)
                Box(g, "Zidka", On(x + 0.875f, 51f, 0.45f), new Vector3(1.75f, 0.9f, 0.7f), rock);
        // baliky sena u der do mapy (kryt pri prechodu)
        Box(g, "Seno_Dira_S", On(-62f, 44f, 0.6f), new Vector3(1.6f, 1.2f, 3.2f), hay);
        Box(g, "Seno_Dira_J", On(-62f, 5f, 0.6f), new Vector3(1.6f, 1.2f, 2.6f), hay);
        // balvany
        Rock(g, -108f, 60f, new Vector3(3.2f, 2.2f, 2.8f), 1);
        Rock(g, -96f, 26f, new Vector3(2.6f, 1.6f, 2.2f), 2);
        Rock(g, -122f, 50f, new Vector3(2.4f, 1.8f, 2.6f), 3);
        Rock(g, -70f, 62f, new Vector3(2.8f, 1.7f, 2.4f), 4);
        Rock(g, -86f, 12f, new Vector3(2.2f, 1.5f, 2.6f), 5);
        Rock(g, -118f, 12f, new Vector3(3f, 2f, 2.6f), 6);
        Rock(g, -132f, 72f, new Vector3(2.4f, 1.6f, 2.2f), 7);
    }

    static void Tree(Transform g, float x, float z, float scale, int seed)
    {
        var random = new System.Random(seed);
        Vector3 foot = On(x, z, -0.1f);
        float height = 4.2f * scale;
        var trunk = MapBuildKit.Beam(g, "Kmen", foot, foot + Vector3.up * height, 0.45f * scale, bark, true);
        GameObjectUtility.SetStaticEditorFlags(trunk, 0);
        for (int i = 0; i < 3; i++)
        {
            Vector3 offset = new Vector3(((float)random.NextDouble() - 0.5f) * 2.4f, height + (float)random.NextDouble() * 1.4f,
                ((float)random.NextDouble() - 0.5f) * 2.4f) * 1f;
            float size = (2.4f + (float)random.NextDouble()) * scale;
            var crown = MapBuildKit.MeshObject(g, "Koruna", MapBuildKit.BlobMesh(40 + (seed + i) % 6), foot + offset * scale,
                Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), new Vector3(size, size * 0.8f, size), leaves[(seed + i) % leaves.Length]);
            GameObjectUtility.SetStaticEditorFlags(crown, 0);
        }
    }

    static void Trees(Transform g)
    {
        var spots = new (float x, float z, float s)[]
        {
            (-132f, 62f, 1.1f), (-128f, 30f, 1.0f), (-110f, 82f, 1.2f), (-66f, 12f, 1.0f), (-75f, -6f, 1.1f), (-100f, 6f, 1.0f),
            (-60f, 64f, 0.9f), (-122f, 90f, 0.9f), (-134f, 28f, 1.0f), (-90f, -4f, 0.9f), (-70f, 74f, 1.0f), (-106f, 34f, 0.8f),
        };
        int seed = 11;
        foreach (var (x, z, s) in spots)
            if (Inside(new Vector2(x, z), Area) && EdgeDistance(new Vector2(x, z), Area) > 3f)
                Tree(g, x, z, s, seed++);
    }

    // ---------------- 5. diry v zapadni hranici ----------------

    static void HedgeHoles(Transform map, Transform root)
    {
        var g = MapBuildKit.Group(root, "Diry");

        // stromy a kere v dirach pryc
        var forest = map.Find("LesniHranice");
        int cleared = 0;
        if (forest != null)
            foreach (var t in forest.GetComponentsInChildren<Transform>(true))
            {
                if (t.parent == null || t.parent.parent != forest) continue;   // jednotlive stromy (LesniHranice/Skupina/Strom)
                if (t.parent.name == "HraniceProHrace" || t.parent.name == "PrujezdyProAuto") continue;
                Vector3 p = t.position;
                if (p.x < -61f || p.x > -46f) continue;
                foreach (var (a, b) in Holes)
                    if (p.z > a - 2f && p.z < b + 2f)
                    {
                        Undo.RecordObject(t.gameObject, "Koncept");
                        t.gameObject.SetActive(false);
                        cleared++;
                        break;
                    }
            }

        // neviditelna zed na zapadni hranici: rozdelit kolem der
        var walls = forest != null ? forest.Find("HraniceProHrace") : null;
        if (walls != null)
            foreach (var wall in walls.GetComponentsInChildren<BoxCollider>(true))
            {
                Bounds wb = wall.bounds;
                if (Mathf.Abs(wb.center.x - WestWallX) > 3f || wb.size.z < 20f) continue;
                Undo.RecordObject(wall, "Koncept");
                wall.enabled = false;
                var cuts = new List<(float a, float b)>(Holes);
                cuts.Sort((x, y) => x.a.CompareTo(y.a));
                float cursor = wb.min.z;
                var parts = MapBuildKit.Group(g, "HraniceRozdelena");
                foreach (var (a, b) in cuts)
                {
                    if (a > cursor) Invisible(parts, "Hranice", new Vector3(wb.center.x, wb.center.y, (cursor + a) * 0.5f), new Vector3(wb.size.x, wb.size.y, a - cursor));
                    cursor = Mathf.Max(cursor, b);
                }
                if (cursor < wb.max.z) Invisible(parts, "Hranice", new Vector3(wb.center.x, wb.center.y, (cursor + wb.max.z) * 0.5f), new Vector3(wb.size.x, wb.size.y, wb.max.z - cursor));
            }

        // v kazde dire: modra stena (do dobyti A projdou jen obranci), drevene sloupky po stranach
        int n = 0;
        foreach (var (a, b) in Holes)
        {
            var gate = new GameObject("Dira_" + (++n)).transform;
            gate.SetParent(g, false);
            var zg = gate.gameObject.AddComponent<ZoneGate>();
            zg.unlockAfter = 1;
            zg.defendersPass = true;
            var closed = new GameObject("Zavreno").transform;
            closed.SetParent(gate, false);
            float mid = (a + b) * 0.5f, width = b - a;
            Box(closed, "ModraStena", new Vector3(-53.5f, 1.6f, mid), new Vector3(0.12f, 3.2f, width), barrier, false);
            Invisible(closed, "Stena", new Vector3(-53.5f, 12f, mid), new Vector3(0.6f, 24f, width));
            var posts = MapBuildKit.Group(g, "Sloupky_" + n);
            Box(posts, "Sloupek", new Vector3(-53.5f, 1.4f, a - 0.15f), new Vector3(0.3f, 2.8f, 0.3f), post);
            Box(posts, "Sloupek", new Vector3(-53.5f, 1.4f, b + 0.15f), new Vector3(0.3f, 2.8f, 0.3f), post);
        }
        Debug.Log($"[Koncept] Diry v zapadni hranici: {Holes.Length} (vypnuto {cleared} stromu / keru).");
    }

    // ---------------- 6. tunel ----------------

    static void TunnelBranch(Transform map, Transform root)
    {
        var g = MapBuildKit.Group(root, "TunelVetev");

        // sever puvodniho tunelu (od stare rozhledny) zazdit
        Box(g, "Zazdeni", new Vector3(0f, (TunFloor + TunCeil) * 0.5f, 47f), new Vector3(3.6f, TunCeil - TunFloor + 0.4f, 0.4f), concrete);

        // otvor v zapadni stene hlavni chodby (x -1.6) v miste napojeni
        var tunnel = map.Find("Tunel/Walls");
        if (tunnel != null)
            foreach (Transform w in tunnel)
            {
                var r = w.GetComponent<Renderer>();
                if (r == null || !w.gameObject.activeSelf) continue;
                Bounds wb = r.bounds;
                if (Mathf.Abs(wb.center.x + 1.6f) > 0.4f || wb.min.z > TunZ0 || wb.max.z < TunZ1) continue;
                Undo.RecordObject(w.gameObject, "Koncept");
                w.gameObject.SetActive(false);
                foreach (var (a, b) in new[] { (wb.min.z, TunZ0), (TunZ1, wb.max.z) })
                {
                    if (b - a < 0.05f) continue;
                    var part = Object.Instantiate(w.gameObject, w.parent);
                    part.name = w.name + "_cast";
                    part.SetActive(true);
                    part.transform.position = new Vector3(w.position.x, w.position.y, (a + b) * 0.5f);
                    Vector3 s = part.transform.localScale;
                    float worldPerLocal = wb.size.z / Mathf.Max(0.001f, s.z);
                    part.transform.localScale = new Vector3(s.x, s.y, (b - a) / worldPerLocal);
                    Undo.RegisterCreatedObjectUndo(part, "Koncept");
                }
                break;
            }

        // nova chodba z 30..33 od x -72 k hlavni chodbe
        float x1 = -1.75f, len = x1 - TunX0, cx = (TunX0 + x1) * 0.5f, cz = (TunZ0 + TunZ1) * 0.5f;
        float height = TunCeil - TunFloor;
        Box(g, "Podlaha", new Vector3(cx, TunFloor - 0.15f, cz), new Vector3(len, 0.3f, TunZ1 - TunZ0 + 0.6f), concreteDark);
        Box(g, "Strop", new Vector3(cx, TunCeil + 0.15f, cz), new Vector3(len, 0.3f, TunZ1 - TunZ0 + 0.6f), concreteDark);
        Box(g, "Stena_S", new Vector3(cx, TunFloor + height * 0.5f, TunZ1 + 0.15f), new Vector3(len, height, 0.3f), concrete);
        float southStart = RampRect.xMax;   // jizni stena az za rampou (rampa vusti do chodby z jihu)
        Box(g, "Stena_J", new Vector3((southStart + x1) * 0.5f, TunFloor + height * 0.5f, TunZ0 - 0.15f), new Vector3(x1 - southStart, height, 0.3f), concrete);
        Box(g, "Stena_Z", new Vector3(TunX0 - 0.15f, TunFloor + height * 0.5f, cz), new Vector3(0.3f, height, TunZ1 - TunZ0 + 0.6f), concrete);

        // rampa z povrchu (x -72..-68, z 18 -> 30)
        float top = H(-70f, 18f);
        Vector3 low = new Vector3(-70f, TunFloor, RampRect.yMax), high = new Vector3(-70f, top, RampRect.yMin);
        Vector3 along = low - high;
        var rot = Quaternion.LookRotation(along.normalized, Vector3.up);
        var ramp = MapBuildKit.Box(g, "Rampa", (low + high) * 0.5f - rot * Vector3.up * 0.15f, new Vector3(RampRect.width, 0.3f, along.magnitude + 0.3f), rot, concreteDark, true);
        GameObjectUtility.SetStaticEditorFlags(ramp, 0);
        foreach (float x in new[] { RampRect.xMin - 0.15f, RampRect.xMax + 0.15f })
            Box(g, "Rampa_Stena", new Vector3(x, (TunFloor + top + 0.4f) * 0.5f, (RampRect.yMin + RampRect.yMax) * 0.5f),
                new Vector3(0.3f, top + 0.4f - TunFloor, RampRect.height), concrete);
        Box(g, "Rampa_Celo", new Vector3(-70f, (top + 0.4f + TunCeil) * 0.5f, RampRect.yMax + 0.15f), new Vector3(RampRect.width + 0.6f, top + 0.4f - TunCeil, 0.3f), concrete);

        // svetla
        for (float x = TunX0 + 4f; x < x1; x += 12f)
        {
            Box(g, "Lampa", new Vector3(x, TunCeil - 0.08f, cz), new Vector3(0.6f, 0.1f, 0.3f), lamp, false);
            var light = new GameObject("Svetlo").AddComponent<Light>();
            light.transform.SetParent(g, false);
            light.transform.position = new Vector3(x, TunCeil - 0.6f, cz);
            light.type = LightType.Point;
            light.range = 9f;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.86f, 0.62f);
            light.shadows = LightShadows.None;
        }

        // mriz za rampou: otevre se po bodu A
        var gate = new GameObject("Mriz_Rozhledna_Nova").transform;
        gate.SetParent(g, false);
        gate.gameObject.AddComponent<ZoneGate>().unlockAfter = 1;
        var closed = new GameObject("Zavreno").transform;
        closed.SetParent(gate, false);
        float gx = RampRect.xMax + 2f;
        for (float z = TunZ0 + 0.2f; z < TunZ1; z += 0.3f)
            Box(closed, "Tyc", new Vector3(gx, (TunFloor + TunCeil) * 0.5f, z), new Vector3(0.06f, height, 0.06f), metal, false);
        Invisible(closed, "Kolize", new Vector3(gx, (TunFloor + TunCeil) * 0.5f, cz), new Vector3(0.3f, height + 0.4f, TunZ1 - TunZ0 + 0.4f));

        // karburator do nove vetve
        var carburetor = FindDeep(map, "Balicek_Karburator");
        if (carburetor != null)
        {
            Undo.RecordObject(carburetor, "Koncept");
            float lift = Mathf.Clamp(carburetor.position.y - (carburetor.position.y > -2f ? 0f : TunFloor), 0f, 1.2f);
            carburetor.position = new Vector3(-35f, TunFloor + lift, cz);
        }
    }

    static Transform FindDeep(Transform parent, string name)
    {
        foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // ---------------- 7. tobogan ----------------

    static void Slide(Transform map)
    {
        foreach (var path in new[] { "Hratelnost/Doplnky/Potok", "Hratelnost/Doplnky/ProudVody", "Hratelnost/Doplnky/Jezirko", "Hratelnost/Doplnky/ProudPotoka" })
        {
            var t = map.Find(path);
            if (t == null) continue;
            Undo.RecordObject(t.gameObject, "Koncept");
            t.gameObject.SetActive(false);
        }
        // zelena skluzavka: otocit, at skluz miri do bazenu (na jih)
        var slide = map.Find("Hratelnost/Doplnky/Skluzavka");
        if (slide != null)
        {
            Undo.RecordObject(slide, "Koncept");
            slide.rotation = Quaternion.Euler(0f, slide.eulerAngles.y + 180f, 0f);
        }
    }

    // ---------------- 8. body a spawny ----------------

    static void SetMarker(Transform map, string name, Vector3 position, Vector3? scale = null)
    {
        var t = FindDeep(map, name);
        if (t == null)
        {
            t = new GameObject(name).transform;
            t.SetParent(map, true);
            Undo.RegisterCreatedObjectUndo(t.gameObject, "Koncept");
        }
        else Undo.RecordObject(t, "Koncept");
        t.position = position;
        if (scale.HasValue)
        {
            Vector3 ps = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(scale.Value.x / ps.x, scale.Value.y / ps.y, scale.Value.z / ps.z);
        }
    }

    static void PointsAndSpawns(Transform map)
    {
        SetMarker(map, "CapturePoint_1", On(TowerTarget.x, TowerTarget.z, 0.5f), new Vector3(14f, 1f, 14f));
        SetMarker(map, "SpawnPoint_Utok_A", On(AttackStart.x, AttackStart.z, 0.1f));
        SetMarker(map, "SpawnPoint_Utok_B", On(AttackAfterA.x, AttackAfterA.z, 0.1f));
        // obranci pro A se oziveji v male chate (prizemi) a do nove casti projdou dirami
        SetMarker(map, "SpawnPoint_Obrana_A", new Vector3(-37.3f, 2.6f, -1.8f));
        // team deathmatch: zakladna severniho tymu byla za orezem - na sever male chaty
        SetMarker(map, "SpawnPoint_Team1", new Vector3(-20f, 0.1f, 45f));
    }

    // ---------------- 9. kulna pro rolbu ----------------

    static void GroomerShed(Transform map, Transform root)
    {
        var g = MapBuildKit.Group(root, "KulnaRolby");
        const float x0 = -23f, x1 = -13f, z0 = 22f, z1 = 28f, h = 3.2f;
        Box(g, "Stena_S", new Vector3((x0 + x1) * 0.5f, h * 0.5f, z1 - 0.1f), new Vector3(x1 - x0, h, 0.2f), shed);
        Box(g, "Stena_J", new Vector3((x0 + x1) * 0.5f, h * 0.5f, z0 + 0.1f), new Vector3(x1 - x0, h, 0.2f), shed);
        Box(g, "Stena_Z", new Vector3(x0 + 0.1f, h * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.2f, h, z1 - z0), shed);
        Box(g, "Strecha", new Vector3((x0 + x1) * 0.5f + 0.3f, h + 0.12f, (z0 + z1) * 0.5f), new Vector3(x1 - x0 + 1.2f, 0.24f, z1 - z0 + 1f), roof);
        Box(g, "Tram", new Vector3(x1 - 0.1f, h - 0.25f, (z0 + z1) * 0.5f), new Vector3(0.25f, 0.5f, z1 - z0), post);

        var groomer = FindDeep(map, "SneznaRolba");
        if (groomer != null)
        {
            Undo.RecordObject(groomer, "Koncept");
            groomer.position = new Vector3(-17.5f, groomer.position.y, 25f);
            groomer.rotation = Quaternion.Euler(0f, 90f, 0f);
        }
    }

    // ---------------- nahledy ----------------

    public static void Snapshots(Transform map)
    {
        Shot("Temp/Koncept_Shora.png", new Vector3(-45f, 160f, 30f), Quaternion.Euler(90f, 0f, 0f), true, 92f, 1700, 1500);
        Vector3 chata = new Vector3(-36f, 2f, 4f);
        Shot("Temp/Koncept_Chata_1.png", chata + new Vector3(26f, 16f, 26f), Look(chata + new Vector3(26f, 16f, 26f), chata), false, 0f, 1280, 720);
        Shot("Temp/Koncept_Chata_2.png", chata + new Vector3(-24f, 14f, -26f), Look(chata + new Vector3(-24f, 14f, -26f), chata), false, 0f, 1280, 720);
        Shot("Temp/Koncept_Chata_3.png", chata + new Vector3(30f, 10f, -18f), Look(chata + new Vector3(30f, 10f, -18f), chata), false, 0f, 1280, 720);
        Vector3 west = new Vector3(-95f, 0f, 45f);
        Shot("Temp/Koncept_Zapad_1.png", new Vector3(-48f, 26f, 40f), Look(new Vector3(-48f, 26f, 40f), west), false, 0f, 1280, 720);
        Shot("Temp/Koncept_Zapad_2.png", new Vector3(-100f, 34f, -18f), Look(new Vector3(-100f, 34f, -18f), west + new Vector3(0f, 0f, 15f)), false, 0f, 1280, 720);
        Shot("Temp/Koncept_Zapad_3.png", new Vector3(-128f, 4f, 86f), Look(new Vector3(-128f, 4f, 86f), new Vector3(-82f, 4f, 38f)), false, 0f, 1280, 720);
    }

    static Quaternion Look(Vector3 from, Vector3 to) => Quaternion.LookRotation(to - from, Vector3.up);

    static void Shot(string file, Vector3 position, Quaternion rotation, bool ortho, float size, int w, int h)
    {
        var camObject = new GameObject("KonceptKamera");
        var cam = camObject.AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(position, rotation);
        cam.orthographic = ortho;
        if (ortho) cam.orthographicSize = size;
        else cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 600f;
        cam.clearFlags = CameraClearFlags.Skybox;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        cam.aspect = w / (float)h;
        try
        {
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        catch (System.Exception e) { Debug.LogWarning($"[Koncept] Nahled {file} se nepodaril: {e.Message}"); }
        finally
        {
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camObject);
        }
    }
}
