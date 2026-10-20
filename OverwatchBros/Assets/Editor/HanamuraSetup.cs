using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Mapa Hanamura (inspirace: Overwatch, vlastni stavba z kvadru): japonske mestecko, hradni nadvori, zahrada se sakurami a dojo.
// Stoji stranou od chat (MatchManager.HanamuraOrigin) pod korenem Map_Hanamura, ktery je ve scene vypnuty - zapne ho
// MatchManager.ApplyMap, kdyz host v lobby vybere Hanamuru. Postavi se samo jednou (kdyz koren chybi); scenu je pak potreba
// ulozit (Ctrl+S). Znovu (smaze rucni upravy): BrosOverwatch > Mapa > Hanamura - postavit znovu.
// Prohlizeni v editoru: BrosOverwatch > Mapa > Hanamura - ukazat / skryt (prepne viditelnost obou map).
//
// Rozlozeni (lokalne, +z smerem k obrancum; podlaha mesta y = 0, hradu y = 2,5):
//   spawn utocniku (z ~ 0) -> mesto se tremi cestami: ulicka vlevo, hlavni ulice, ulice vpravo; lavky a most v patre
//   -> brana s ochozem (hrdlo, z = 50) a bocni dvirka vlevo -> bod A: nadvori se zahradou kamenu, balkon vpravo
//   -> hradni brana (z = 84) s rampou, bocni dvere vlevo a prechod z balkonu na terasu -> bod B: zahrada se sakurami,
//   terasa vpravo -> dojo (z 118-150): bod C v sale, podium, galerie, vyhled vlevo nahore, spawn obrancu za podiem.
[InitializeOnLoad]
public static class HanamuraSetup
{
    const string RootName = "Map_Hanamura";
    const string MaterialFolder = "Assets/Materials/Hanamura";
    const float CastleY = 2.5f;      // podlaha hradu (zahrada, dojo)
    const float PlateauTop = 2.4f;   // vrch kamenneho podstavce (podlahy na nem jsou 0,1 m silne)
    const float BalconyY = 4.3f;     // ochoz nad branou, balkon nadvori, terasa zahrady

    static Vector3 O;                // pocatek mapy ve svete
    static Transform root;
    static Mats m;
    static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

    class Mats
    {
        public Material paving, plaster, wood, darkWood, planks, roof, red, stone, lawn, gravel, bark, sakura, sakura2,
            paper, lantern, gold, water, rock, black, blue, banner;
    }

    static HanamuraSetup()
    {
        EditorApplication.delayCall += RunIfNeeded;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += RunIfNeeded;
    }

    static int retries;

    static void RunIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (FindDomasov() == null) return;   // jina scena
        var existing = FindRoot();
        if (existing != null)
        {
            if (ApplyFeatures(existing.transform))
            {
                EditorSceneManager.MarkSceneDirty(existing.scene);
                Debug.Log("[Hanamura] Mapa doplnena. Uloz scenu (Ctrl+S).");
            }
            return;
        }

        if (Build())
        {
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log("[Hanamura] Mapa Hanamura postavena (Map_Hanamura, vypnuta - zapne se ve hre). Uloz scenu (Ctrl+S).");
        }
        else if (retries++ < 30)
        {
            EditorApplication.delayCall += RunIfNeeded;   // textury se jeste importuji
        }
    }

    static GameObject FindDomasov()
    {
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == "Map-Domasov") return go;
        return null;
    }

    static GameObject FindRoot()
    {
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == RootName) return go;
        return null;
    }

    [MenuItem("BrosOverwatch/Mapa/Hanamura - postavit znovu")]
    static void RebuildMenu()
    {
        if (!EditorUtility.DisplayDialog("Hanamura", "Smazat Map_Hanamura (i rucni upravy) a postavit znovu?", "Postavit znovu", "Zrusit"))
            return;
        if (Build())
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
    }

    [MenuItem("BrosOverwatch/Mapa/Hanamura - ukazat ⁄ skryt")]
    static void ToggleMenu()
    {
        var hanamura = FindRoot();
        var domasov = GameObject.Find("Map-Domasov");
        if (hanamura == null) return;
        bool show = !hanamura.activeSelf;
        hanamura.SetActive(show);
        if (domasov != null) domasov.SetActive(!show);
        else
            foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == "Map-Domasov") go.SetActive(!show);
        if (show)
        {
            Selection.activeGameObject = hanamura;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
        Debug.Log(show
            ? "[Hanamura] Zobrazena Hanamura (chaty skryte). Pred ulozenim klidne nech - ve hre se mapy prepnou samy."
            : "[Hanamura] Zobrazeny chaty.");
    }

    // ---------------- materialy ----------------

    static Mats LoadMaterials()
    {
        string f = MaterialFolder;
        var mats = new Mats
        {
            paving = MapBuildKit.Mat(f + "/Paving.mat", "Assets/Materials/Stone/StoneWall.png", new Color(0.86f, 0.84f, 0.8f), new Vector2(0.3f, 0.3f), 0.15f),
            stone = MapBuildKit.Mat(f + "/CastleStone.mat", "Assets/Materials/Stone/StoneWall.png", new Color(0.72f, 0.7f, 0.68f), new Vector2(0.4f, 0.4f), 0.1f),
            plaster = MapBuildKit.Mat(f + "/Plaster.mat", null, new Color(0.93f, 0.9f, 0.84f), Vector2.one, 0.1f),
            wood = MapBuildKit.Mat(f + "/Wood.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.62f, 0.44f, 0.32f), new Vector2(0.5f, 0.5f), 0.2f),
            darkWood = MapBuildKit.Mat(f + "/DarkWood.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.32f, 0.22f, 0.17f), new Vector2(0.5f, 0.5f), 0.2f),
            planks = MapBuildKit.Mat(f + "/Planks.mat", "Assets/Materials/Wood/WoodPlanks.png", new Color(0.92f, 0.76f, 0.58f), new Vector2(0.5f, 0.5f), 0.3f),
            roof = MapBuildKit.Mat(f + "/RoofTiles.mat", "Assets/Materials/Wood/WoodShingles.png", new Color(0.4f, 0.44f, 0.52f), new Vector2(0.7f, 0.7f), 0.25f),
            red = MapBuildKit.Mat(f + "/RedLacquer.mat", null, new Color(0.72f, 0.13f, 0.1f), Vector2.one, 0.45f),
            lawn = MapBuildKit.Mat(f + "/Lawn.mat", "Assets/Materials/Garden/Lawn.png", new Color(0.95f, 1f, 0.9f), new Vector2(0.5f, 0.5f), 0.05f),
            gravel = MapBuildKit.Mat(f + "/Sand.mat", "Assets/Materials/Garden/Gravel.png", new Color(1f, 0.95f, 0.84f), new Vector2(0.6f, 0.6f), 0.05f),
            bark = MapBuildKit.Mat(f + "/Bark.mat", "Assets/Materials/Garden/Bark.png", new Color(0.55f, 0.45f, 0.45f), Vector2.one, 0.05f),
            sakura = MapBuildKit.Mat(f + "/Sakura.mat", null, new Color(1f, 0.74f, 0.84f), Vector2.one, 0.1f),
            sakura2 = MapBuildKit.Mat(f + "/Sakura2.mat", null, new Color(0.97f, 0.6f, 0.75f), Vector2.one, 0.1f),
            paper = MapBuildKit.Mat(f + "/Shoji.mat", null, new Color(0.97f, 0.94f, 0.84f), Vector2.one, 0.1f, emission: new Color(0.25f, 0.22f, 0.15f)),
            lantern = MapBuildKit.Mat(f + "/Lantern.mat", null, new Color(1f, 0.55f, 0.25f), Vector2.one, 0.3f, emission: new Color(2.2f, 1.1f, 0.4f)),
            gold = MapBuildKit.Mat(f + "/Gold.mat", null, new Color(0.88f, 0.68f, 0.28f), Vector2.one, 0.6f),
            water = MapBuildKit.Mat(f + "/Water.mat", null, new Color(0.22f, 0.42f, 0.55f), Vector2.one, 0.9f),
            rock = MapBuildKit.Mat(f + "/Rock.mat", null, new Color(0.5f, 0.5f, 0.52f), Vector2.one, 0.1f),
            black = MapBuildKit.Mat(f + "/Black.mat", null, new Color(0.1f, 0.1f, 0.12f), Vector2.one, 0.3f),
            blue = MapBuildKit.Mat(f + "/Indigo.mat", null, new Color(0.15f, 0.25f, 0.5f), Vector2.one, 0.2f),
            banner = MapBuildKit.Mat(f + "/Banner.mat", null, new Color(0.95f, 0.85f, 0.3f), Vector2.one, 0.2f),
        };
        foreach (var field in typeof(Mats).GetFields())
            if (field.GetValue(mats) == null) return null;   // textura se jeste neimportovala
        return mats;
    }

    // ---------------- stavebni prvky (souradnice lokalne vuci pocatku mapy) ----------------

    static Mesh BoxMeshShared(Vector3 size, bool grain)
    {
        string key = $"{size.x:0.###}_{size.y:0.###}_{size.z:0.###}_{grain}";
        if (!meshes.TryGetValue(key, out var mesh) || mesh == null)
            meshes[key] = mesh = MapBuildKit.BoxMesh(size, grain);
        return mesh;
    }

    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material,
        bool collider = true, float yaw = 0f, Quaternion? rotation = null, bool shadows = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(O + center, rotation ?? Quaternion.Euler(0f, yaw, 0f));
        go.AddComponent<MeshFilter>().sharedMesh = BoxMeshShared(size, true);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        if (!shadows) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (collider) go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    // Kvadr zadany rohy (min, max) - pohodlnejsi pro steny a podlahy.
    static GameObject Span(Transform parent, string name, Vector3 min, Vector3 max, Material material, bool collider = true)
    {
        return Box(parent, name, (min + max) * 0.5f, max - min, material, collider);
    }

    // Neviditelna zed (jen kolize).
    static void Invisible(Transform parent, string name, Vector3 min, Vector3 max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = O + (min + max) * 0.5f;
        go.AddComponent<BoxCollider>().size = max - min;
    }

    // Rampa z bodu 'low' na 'high' (stred spodni a horni hrany pochozi plochy), sirka 'width'.
    static void Ramp(Transform parent, string name, Vector3 low, Vector3 high, float width, Material material)
    {
        Vector3 along = high - low;
        var rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
        const float thickness = 0.4f;
        Vector3 center = (low + high) * 0.5f - rotation * Vector3.up * (thickness * 0.5f);
        Box(parent, name, center, new Vector3(width, thickness, along.magnitude + 0.2f), material, true, rotation: rotation);
    }

    // Sedlova strecha: hrebenem podel x (alongX) nebo z. 'eaveY' = vyska okapu, 'angle' sklon.
    static void Roof(Transform parent, string name, Vector3 center, float width, float depth, float eaveY, bool alongX,
        float angle = 27f, float overhang = 0.8f, Material material = null)
    {
        material = material ?? m.roof;
        float half = (alongX ? depth : width) * 0.5f + overhang;
        float rise = half * Mathf.Tan(angle * Mathf.Deg2Rad);
        float length = half / Mathf.Cos(angle * Mathf.Deg2Rad) + 0.15f;
        float span = (alongX ? width : depth) + overhang * 2f;
        const float t = 0.22f;
        float ridgeY = eaveY + rise;
        var group = MapBuildKit.Group(parent, name);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 c;
            Quaternion r;
            Vector3 size;
            if (alongX)
            {
                c = new Vector3(center.x, ridgeY - rise * 0.5f, center.z + side * half * 0.5f);
                r = Quaternion.Euler(side * angle, 0f, 0f);
                size = new Vector3(span, t, length);
            }
            else
            {
                c = new Vector3(center.x + side * half * 0.5f, ridgeY - rise * 0.5f, center.z);
                r = Quaternion.Euler(0f, 0f, -side * angle);
                size = new Vector3(length, t, span);
            }
            Box(group, side < 0 ? "Slope_A" : "Slope_B", c, size, material, true, rotation: r);
        }
        // hreben a stity (trojuhelniky pod strechou)
        Vector3 ridgeSize = alongX ? new Vector3(span + 0.2f, 0.35f, 0.4f) : new Vector3(0.4f, 0.35f, span + 0.2f);
        Box(group, "Ridge", new Vector3(center.x, ridgeY + 0.08f, center.z), ridgeSize, m.black, false);
        float wallHalf = (alongX ? depth : width) * 0.5f;
        float gableRise = wallHalf * Mathf.Tan(angle * Mathf.Deg2Rad);
        for (int end = -1; end <= 1; end += 2)
        {
            Vector3[] points;
            if (alongX)
            {
                float x = center.x + end * width * 0.5f;
                points = new[]
                {
                    O + new Vector3(x, eaveY, center.z - wallHalf), O + new Vector3(x, eaveY + gableRise, center.z),
                    O + new Vector3(x, eaveY, center.z + wallHalf),
                };
            }
            else
            {
                float z = center.z + end * depth * 0.5f;
                points = new[]
                {
                    O + new Vector3(center.x - wallHalf, eaveY, z), O + new Vector3(center.x, eaveY + gableRise, z),
                    O + new Vector3(center.x + wallHalf, eaveY, z),
                };
            }
            var uv = new[] { new Vector2(0f, 0f), new Vector2(wallHalf, gableRise), new Vector2(wallHalf * 2f, 0f) };
            var mesh = MapBuildKit.FlatMesh($"Gable_{name}_{end}", points, uv);
            var gable = MapBuildKit.MeshObject(group, "Gable", mesh, Vector3.zero, Quaternion.identity, Vector3.one, m.plaster);
            GameObjectUtility.SetStaticEditorFlags(gable, 0);
        }
    }

    // Plny dum (nejde dovnitr): omitkove telo, tmave tramy, okna, strecha. 'floorY' = vyska zakladu.
    static void House(Transform parent, string name, float minX, float maxX, float minZ, float maxZ, float height,
        float floorY = 0f, bool ridgeAlongX = true, Material wall = null, Material roofMaterial = null, int windowSide = 0)
    {
        var group = MapBuildKit.Group(parent, name);
        wall = wall ?? m.plaster;
        Span(group, "Body", new Vector3(minX, floorY, minZ), new Vector3(maxX, floorY + height, maxZ), wall);
        // tmavy sokl a tram pod strechou
        Span(group, "Plinth", new Vector3(minX - 0.05f, floorY, minZ - 0.05f), new Vector3(maxX + 0.05f, floorY + 0.6f, maxZ + 0.05f), m.darkWood, false);
        Span(group, "Beam", new Vector3(minX - 0.08f, floorY + height - 0.35f, minZ - 0.08f), new Vector3(maxX + 0.08f, floorY + height, maxZ + 0.08f), m.darkWood, false);
        if (height > 5f)
            Span(group, "FloorBeam", new Vector3(minX - 0.06f, floorY + 3.2f, minZ - 0.06f), new Vector3(maxX + 0.06f, floorY + 3.45f, maxZ + 0.06f), m.darkWood, false);

        // okna (shoji) na delsich stenach, kde jsou videt (windowSide: -1 = jen -x/-z strana, 1 = jen +x/+z, 0 = obe)
        float w = maxX - minX, d = maxZ - minZ;
        bool alongX = w >= d;
        int count = Mathf.Max(1, Mathf.FloorToInt((alongX ? w : d) / 3.2f));
        for (int floor = 0; floor < (height > 5f ? 2 : 1); floor++)
        {
            float y = floorY + 1.7f + floor * 3.2f;
            if (y + 0.8f > floorY + height - 0.4f) break;
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                for (int side = -1; side <= 1; side += 2)
                {
                    if (windowSide != 0 && side != windowSide) continue;
                    Vector3 c = alongX
                        ? new Vector3(Mathf.Lerp(minX, maxX, t), y, side < 0 ? minZ - 0.04f : maxZ + 0.04f)
                        : new Vector3(side < 0 ? minX - 0.04f : maxX + 0.04f, y, Mathf.Lerp(minZ, maxZ, t));
                    Vector3 size = alongX ? new Vector3(1.4f, 1.1f, 0.1f) : new Vector3(0.1f, 1.1f, 1.4f);
                    Box(group, "Window", c, size, m.paper, false, shadows: false);
                    Vector3 frame = alongX ? new Vector3(1.6f, 0.12f, 0.14f) : new Vector3(0.14f, 0.12f, 1.6f);
                    Box(group, "Sill", c + Vector3.down * 0.6f, frame, m.darkWood, false, shadows: false);
                }
            }
        }
        Roof(group, "Roof", new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f), w, d, floorY + height, ridgeAlongX,
            material: roofMaterial);
    }

    static void Railing(Transform parent, string name, Vector3 from, Vector3 to, float baseY)
    {
        var group = MapBuildKit.Group(parent, name);
        Vector3 a = new Vector3(from.x, baseY, from.z), b = new Vector3(to.x, baseY, to.z);
        Vector3 along = b - a;
        float length = along.magnitude;
        var rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
        Box(group, "Rail", (a + b) * 0.5f + Vector3.up * 1.0f, new Vector3(0.12f, 0.12f, length), m.darkWood, false, rotation: rotation);
        Box(group, "Mid", (a + b) * 0.5f + Vector3.up * 0.5f, new Vector3(0.08f, 0.08f, length), m.darkWood, false, rotation: rotation);
        int posts = Mathf.Max(2, Mathf.CeilToInt(length / 2f) + 1);
        for (int i = 0; i < posts; i++)
            Box(group, "Post", Vector3.Lerp(a, b, i / (posts - 1f)) + Vector3.up * 0.55f, new Vector3(0.14f, 1.1f, 0.14f), m.darkWood, false);
        // kolize: jedna tenka stena po vysku zabradli
        var wallObject = new GameObject("Collider");
        wallObject.transform.SetParent(group, false);
        wallObject.transform.SetPositionAndRotation(O + (a + b) * 0.5f + Vector3.up * 0.55f, rotation);
        wallObject.AddComponent<BoxCollider>().size = new Vector3(0.15f, 1.1f, length);
    }

    // Plosina na sloupech (balkon, lavka, most): deska nahore v 'top', sloupy po okrajich.
    static void Deck(Transform parent, string name, float minX, float maxX, float minZ, float maxZ, float top, float footY,
        bool posts = true)
    {
        var group = MapBuildKit.Group(parent, name);
        Span(group, "Deck", new Vector3(minX, top - 0.3f, minZ), new Vector3(maxX, top, maxZ), m.planks);
        Span(group, "Fascia", new Vector3(minX - 0.05f, top - 0.55f, minZ - 0.05f), new Vector3(maxX + 0.05f, top - 0.3f, maxZ + 0.05f), m.darkWood, false);
        if (!posts) return;
        float w = maxX - minX, d = maxZ - minZ;
        int nx = Mathf.Max(2, Mathf.CeilToInt(w / 4.5f) + 1), nz = Mathf.Max(2, Mathf.CeilToInt(d / 4.5f) + 1);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                if (i != 0 && i != nx - 1 && j != 0 && j != nz - 1) continue;
                float x = Mathf.Lerp(minX + 0.25f, maxX - 0.25f, i / (nx - 1f));
                float z = Mathf.Lerp(minZ + 0.25f, maxZ - 0.25f, j / (nz - 1f));
                Span(group, "Post", new Vector3(x - 0.18f, footY, z - 0.18f), new Vector3(x + 0.18f, top - 0.3f, z + 0.18f), m.darkWood);
            }
    }

    static void Torii(Transform parent, string name, Vector3 center, float width, float height, float yaw = 0f)
    {
        var group = MapBuildKit.Group(parent, name);
        var rotation = Quaternion.Euler(0f, yaw, 0f);
        for (int side = -1; side <= 1; side += 2)
            Box(group, "Pillar", center + rotation * new Vector3(side * width * 0.5f, height * 0.5f, 0f), new Vector3(0.55f, height, 0.55f), m.red, true, yaw);
        Box(group, "Kasagi", center + rotation * new Vector3(0f, height + 0.25f, 0f), new Vector3(width + 2.4f, 0.5f, 0.8f), m.black, false, yaw);
        Box(group, "Shimaki", center + rotation * new Vector3(0f, height - 0.1f, 0f), new Vector3(width + 1.6f, 0.35f, 0.6f), m.red, false, yaw);
        Box(group, "Nuki", center + rotation * new Vector3(0f, height - 1.3f, 0f), new Vector3(width + 0.8f, 0.35f, 0.4f), m.red, false, yaw);
        Box(group, "Gakuzuka", center + rotation * new Vector3(0f, height - 0.7f, 0f), new Vector3(0.3f, 0.9f, 0.35f), m.black, false, yaw);
    }

    static void StoneLantern(Transform parent, Vector3 foot)
    {
        var group = MapBuildKit.Group(parent, "StoneLantern");
        Box(group, "Base", foot + Vector3.up * 0.2f, new Vector3(0.9f, 0.4f, 0.9f), m.rock);
        Box(group, "Post", foot + Vector3.up * 0.85f, new Vector3(0.35f, 0.9f, 0.35f), m.rock);
        Box(group, "Light", foot + Vector3.up * 1.55f, new Vector3(0.6f, 0.5f, 0.6f), m.lantern, false);
        Box(group, "Cap", foot + Vector3.up * 1.95f, new Vector3(1.0f, 0.3f, 1.0f), m.rock, false);
        Box(group, "Top", foot + Vector3.up * 2.2f, new Vector3(0.3f, 0.25f, 0.3f), m.rock, false);
    }

    static void PaperLantern(Transform parent, Vector3 position)
    {
        var group = MapBuildKit.Group(parent, "PaperLantern");
        Box(group, "Body", position, new Vector3(0.45f, 0.65f, 0.45f), m.lantern, false, shadows: false);
        Box(group, "CapTop", position + Vector3.up * 0.36f, new Vector3(0.3f, 0.08f, 0.3f), m.black, false, shadows: false);
        Box(group, "CapBottom", position + Vector3.down * 0.36f, new Vector3(0.3f, 0.08f, 0.3f), m.black, false, shadows: false);
    }

    static void Sakura(Transform parent, Vector3 foot, float scale, int seed)
    {
        var group = MapBuildKit.Group(parent, "Sakura");
        var random = new System.Random(seed);
        float height = 3.2f * scale;
        Vector3 top = foot + new Vector3(((float)random.NextDouble() - 0.5f) * 0.8f, height, ((float)random.NextDouble() - 0.5f) * 0.8f);
        var trunk = MapBuildKit.Beam(group, "Trunk", O + foot, O + top, 0.35f * scale, m.bark, true);
        GameObjectUtility.SetStaticEditorFlags(trunk, 0);
        for (int i = 0; i < 2; i++)
        {
            Vector3 branch = top + new Vector3(((float)random.NextDouble() - 0.5f) * 3f, 0.6f, ((float)random.NextDouble() - 0.5f) * 3f) * scale;
            var limb = MapBuildKit.Beam(group, "Branch", O + top - Vector3.up * 0.6f * scale, O + branch, 0.2f * scale, m.bark, false);
            GameObjectUtility.SetStaticEditorFlags(limb, 0);
        }
        for (int i = 0; i < 4; i++)
        {
            Vector3 offset = new Vector3(((float)random.NextDouble() - 0.5f) * 3.2f, 0.8f + (float)random.NextDouble() * 1.2f,
                ((float)random.NextDouble() - 0.5f) * 3.2f) * scale;
            float size = (1.6f + (float)random.NextDouble() * 0.8f) * scale;
            var blob = MapBuildKit.MeshObject(group, "Blossom", MapBuildKit.BlobMesh(40 + (seed + i) % 6), O + top + offset,
                Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), new Vector3(size, size * 0.75f, size), i % 2 == 0 ? m.sakura : m.sakura2);
            GameObjectUtility.SetStaticEditorFlags(blob, 0);
        }
    }

    static void Rock(Transform parent, Vector3 foot, Vector3 size, int seed)
    {
        var rock = MapBuildKit.MeshObject(parent, "Rock", MapBuildKit.BlobMesh(60 + seed % 6), O + foot + Vector3.up * size.y * 0.35f,
            Quaternion.Euler(0f, seed * 47f, 0f), size * 0.5f, m.rock);
        GameObjectUtility.SetStaticEditorFlags(rock, 0);
        rock.AddComponent<BoxCollider>().size = new Vector3(1.5f, 1.4f, 1.5f);
    }

    static void Marker(Transform parent, string name, Vector3 position, Vector3 scale = default)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = O + position;
        if (scale != default) go.transform.localScale = scale;
    }

    static void Lamp(Transform parent, Vector3 position, float range, float intensity)
    {
        var go = new GameObject("Lamp");
        go.transform.SetParent(parent, false);
        go.transform.position = O + position;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.78f, 0.55f);
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
    }

    // ---------------- stavba ----------------

    static bool Build()
    {
        m = LoadMaterials();
        if (m == null) return false;
        meshes.Clear();

        var old = FindRoot();
        if (old != null) Object.DestroyImmediate(old);

        O = MatchManager.HanamuraOrigin;
        root = new GameObject(RootName).transform;
        root.position = O;

        BuildGround();
        BuildTown();
        BuildGate();
        BuildCourtyard();
        BuildCastleWall();
        BuildGarden();
        BuildDojo();
        BuildBounds();
        BuildMarkers();
        ApplyFeatures(root, alreadyLoaded: true);

        // nic staticke (mapa se zapina az za behu) - kolize a vzhled funguji i tak
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

        root.gameObject.SetActive(false);
        return true;
    }

    // ---------------- doplnky hotove mapy (kazdy jednou - rucni upravy zustanou) ----------------

    static bool ApplyFeatures(Transform map, bool alreadyLoaded = false)
    {
        bool changed = false;
        changed |= Feature(map, "SpawnKryt", alreadyLoaded, SpawnCover);
        return changed;
    }

    static bool Feature(Transform map, string name, bool alreadyLoaded, System.Action<Transform> apply)
    {
        string marker = "_Prvek_" + name;
        if (map.Find(marker) != null) return false;
        if (!alreadyLoaded)
        {
            m = LoadMaterials();
            if (m == null) return false;
            O = map.position;
            root = map;
        }
        apply(map);
        var tag = new GameObject(marker);
        tag.transform.SetParent(map, false);
        foreach (var t in map.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        return true;
    }

    // Spawn utocniku nesmi byt videt z hlavni ulice: dum uprostred namesti spawnu, utocnici vybihaji kolem nej
    // po stranach; spawn za nim, torii na zacatek hlavni ulice.
    static void SpawnCover(Transform map)
    {
        var town = MapBuildKit.Group(map, "Mesto");
        House(town, "Spawn_Predni", -6f, 6f, 3f, 7.5f, 6f, windowSide: 1);
        var torii = town.Find("ToriiSpawn");
        if (torii != null)
            foreach (Transform part in torii)
                part.position += new Vector3(0f, 0f, 2.5f);
        var markers = map.Find("Body");
        var spawn = markers != null ? markers.Find("SpawnPoint_Utok_A") : null;
        if (spawn != null) spawn.position = O + new Vector3(0f, 0.1f, -2.5f);
        Debug.Log("[Hanamura] Pred spawn utocniku pridan dum (neni na nej videt z hlavni ulice).");
    }

    static void BuildGround()
    {
        var g = MapBuildKit.Group(root, "Zem");
        // mesto a nadvori (y = 0)
        Span(g, "Mesto", new Vector3(-26f, -0.5f, -10f), new Vector3(26f, 0f, 54f), m.paving);
        Span(g, "Nadvori", new Vector3(-26f, -0.5f, 54f), new Vector3(26f, 0f, 87f), m.gravel);
        // hradni podstavec (y = 2,4) s vyrezy pro rampy u brany
        Span(g, "Hrad", new Vector3(-26f, -0.5f, 87f), new Vector3(26f, PlateauTop, 154f), m.stone);
        Span(g, "HradPredek_L", new Vector3(-26f, -0.5f, 85.5f), new Vector3(-23f, PlateauTop, 87f), m.stone);
        Span(g, "HradPredek_M", new Vector3(-20f, -0.5f, 85.5f), new Vector3(-4f, PlateauTop, 87f), m.stone);
        Span(g, "HradPredek_P", new Vector3(4f, -0.5f, 85.5f), new Vector3(26f, PlateauTop, 87f), m.stone);
        // travnik zahrady a okoli dojo, sterkove cesty
        Span(g, "Travnik", new Vector3(-26f, PlateauTop, 87f), new Vector3(26f, CastleY, 118f), m.lawn);
        Span(g, "Travnik_Z", new Vector3(-26f, PlateauTop, 118f), new Vector3(-15f, CastleY, 154f), m.lawn);
        Span(g, "Travnik_V", new Vector3(15f, PlateauTop, 118f), new Vector3(26f, CastleY, 154f), m.lawn);
        Span(g, "Travnik_S", new Vector3(-15f, PlateauTop, 150f), new Vector3(15f, CastleY, 154f), m.lawn);
        Span(g, "Cesta", new Vector3(-2.5f, CastleY, 87f), new Vector3(2.5f, CastleY + 0.02f, 118f), m.gravel);
        Span(g, "CestaDojo", new Vector3(-15f, CastleY, 114f), new Vector3(15f, CastleY + 0.02f, 118f), m.gravel);
    }

    static void BuildTown()
    {
        var g = MapBuildKit.Group(root, "Mesto");

        // zadni rada za spawnem utocniku a domy po okrajich (fasady)
        House(g, "Spawn_Zadni", -20f, 20f, -10f, -6f, 8f, windowSide: 1);
        float[] rows = { -10f, 6f, 18f, 32f, 50f };
        float[] heights = { 9f, 7f, 10f, 8f };
        for (int i = 0; i < 4; i++)
        {
            House(g, $"Levy_{i}", -26f, -20f, rows[i], rows[i + 1], heights[i], ridgeAlongX: false, windowSide: 1);
            House(g, $"Pravy_{i}", 20f, 26f, rows[i], rows[i + 1], heights[(i + 2) % 4], ridgeAlongX: false, windowSide: -1);
        }

        // spawn: torii brana, lucerny
        Torii(g, "ToriiSpawn", new Vector3(0f, 0f, 8f), 7f, 5.5f);
        StoneLantern(g, new Vector3(-6.5f, 0f, 8f));
        StoneLantern(g, new Vector3(6.5f, 0f, 8f));

        // levy blok: dum (z 12-26) a nizka budova s terasou v patre (z 30-44) - "lavka" nad ulickou
        House(g, "Levy_Blok", -14f, -6f, 12f, 26f, 7f, ridgeAlongX: false);
        Span(g, "LevaTerasa_Telo", new Vector3(-14f, 0f, 30f), new Vector3(-6f, 3.2f, 44f), m.plaster);
        Span(g, "LevaTerasa_Sokl", new Vector3(-14.05f, 0f, 29.95f), new Vector3(-5.95f, 0.6f, 44.05f), m.darkWood, false);
        Deck(g, "LevaTerasa", -14f, -6f, 30f, 44f, 3.5f, 3.2f, posts: false);
        Railing(g, "LevaTerasa_Zabradli_S", new Vector3(-14f, 0f, 44f), new Vector3(-6f, 0f, 44f), 3.5f);
        Railing(g, "LevaTerasa_Zabradli_J", new Vector3(-14f, 0f, 30f), new Vector3(-6f, 0f, 30f), 3.5f);
        Ramp(g, "LevaTerasa_Rampa", new Vector3(-15.6f, 0f, 32f), new Vector3(-15.6f, 3.5f, 40f), 3f, m.planks);
        Span(g, "LevaTerasa_Napojeni", new Vector3(-17.1f, 3.2f, 40f), new Vector3(-14f, 3.5f, 42f), m.planks);
        // markyza nad terasou (stin, kryt shora)
        Roof(g, "LevaTerasa_Strisska", new Vector3(-10f, 0f, 37f), 8f, 14f, 6.6f, false, 20f, 0.4f);
        for (int i = 0; i < 4; i++)
        {
            float z = Mathf.Lerp(30.3f, 43.7f, i / 3f);
            Span(g, "Strisska_Sloup", new Vector3(-6.4f, 3.5f, z - 0.15f), new Vector3(-6.1f, 6.6f, z + 0.15f), m.darkWood);
        }

        // pravy blok: dum (z 12-24) s plochou strechou (lavka), kavarna s podkrovim (z 28-44)
        Span(g, "Pravy_Blok", new Vector3(6f, 0f, 12f), new Vector3(14f, 3.2f, 24f), m.plaster);
        Span(g, "Pravy_Blok_Sokl", new Vector3(5.95f, 0f, 11.95f), new Vector3(14.05f, 0.6f, 24.05f), m.darkWood, false);
        Deck(g, "PravaLavka", 6f, 14f, 12f, 28f, 3.5f, 0f);
        Railing(g, "PravaLavka_Zabradli", new Vector3(6f, 0f, 12f), new Vector3(6f, 0f, 24f), 3.5f);
        Ramp(g, "PravaLavka_Rampa", new Vector3(15.6f, 0f, 22f), new Vector3(15.6f, 3.5f, 14f), 3f, m.planks);
        Span(g, "PravaLavka_Napojeni", new Vector3(14f, 3.2f, 12f), new Vector3(17.1f, 3.5f, 14f), m.planks);
        // markyza (zluta strecha v OW) nad pravou ulici
        Roof(g, "PravaLavka_Strisska", new Vector3(10f, 0f, 18f), 8f, 12f, 6.6f, false, 18f, 0.4f, m.banner);
        for (int i = 0; i < 3; i++)
        {
            float z = Mathf.Lerp(12.3f, 23.7f, i / 2f);
            Span(g, "Strisska_Sloup", new Vector3(13.6f, 3.5f, z - 0.15f), new Vector3(13.9f, 6.6f, z + 0.15f), m.darkWood);
        }

        // kavarna: prizemi otevrene do hlavni ulice, podkrovi (y 3,5) otevrene na most
        var cafe = MapBuildKit.Group(g, "Kavarna");
        Span(cafe, "ZadniStena", new Vector3(13.5f, 0f, 28f), new Vector3(14f, 6.8f, 44f), m.plaster);
        Span(cafe, "JizniStena", new Vector3(6f, 0f, 28f), new Vector3(14f, 3.2f, 28.5f), m.plaster);
        Span(cafe, "JizniStena_Patro", new Vector3(10.5f, 3.5f, 28f), new Vector3(14f, 6.8f, 28.5f), m.plaster);
        Span(cafe, "SeverniStena", new Vector3(6f, 0f, 43.5f), new Vector3(14f, 6.8f, 44f), m.plaster);
        Span(cafe, "Dvere_Ulice", new Vector3(13.5f, 0f, 35f), new Vector3(14.05f, 2.4f, 37f), m.darkWood, false);
        Deck(cafe, "Podkrovi", 6f, 10.7f, 28f, 43.5f, 3.5f, 0f);
        Deck(cafe, "Podkrovi_Podesta", 10.7f, 13.5f, 28f, 35.5f, 3.5f, 0f, posts: false);
        Railing(cafe, "Podkrovi_Zabradli_A", new Vector3(6f, 0f, 28.5f), new Vector3(6f, 0f, 33.8f), 3.5f);
        Railing(cafe, "Podkrovi_Zabradli_B", new Vector3(6f, 0f, 37.2f), new Vector3(6f, 0f, 43.5f), 3.5f);
        Ramp(cafe, "Schody", new Vector3(12f, 0f, 43f), new Vector3(12f, 3.5f, 35.5f), 2.6f, m.planks);
        Roof(cafe, "Strecha", new Vector3(10f, 0f, 36f), 8f, 16f, 6.8f, false);
        Box(cafe, "Pult", new Vector3(9.5f, 0.55f, 31f), new Vector3(3f, 1.1f, 0.8f), m.wood);
        Box(cafe, "Noren", new Vector3(6.05f, 2.7f, 36f), new Vector3(0.05f, 0.9f, 3f), m.blue, false, shadows: false);
        PaperLantern(cafe, new Vector3(6.2f, 2.9f, 29.5f));
        PaperLantern(cafe, new Vector3(6.2f, 2.9f, 42.5f));

        // most nad hlavni ulici: leva terasa <-> podkrovi kavarny
        Deck(g, "Most", -6f, 6f, 34f, 37f, 3.5f, 0f, posts: false);
        Railing(g, "Most_Zabradli_J", new Vector3(-6f, 0f, 34f), new Vector3(6f, 0f, 34f), 3.5f);
        Railing(g, "Most_Zabradli_S", new Vector3(-6f, 0f, 37f), new Vector3(6f, 0f, 37f), 3.5f);

        // obchudky po stranach hlavni ulice v prizemi domu - poutace, lucerny, krabice (kryti)
        Box(g, "Poutac_A", new Vector3(-5.9f, 4.5f, 18f), new Vector3(0.2f, 3f, 1f), m.red, false);
        Box(g, "Poutac_B", new Vector3(-5.9f, 4.5f, 22f), new Vector3(0.2f, 2.4f, 0.9f), m.blue, false);
        Box(g, "Poutac_C", new Vector3(19.9f, 5f, 34f), new Vector3(0.2f, 3.4f, 1f), m.banner, false);
        Box(g, "Poutac_D", new Vector3(-19.9f, 4.6f, 24f), new Vector3(0.2f, 3f, 1f), m.red, false);
        for (int i = 0; i < 6; i++)
            PaperLantern(g, new Vector3(i % 2 == 0 ? -5.6f : 5.6f, 3f, 14f + i * 5f));
        Box(g, "Bedny_A", new Vector3(-3.8f, 0.6f, 24f), new Vector3(1.2f, 1.2f, 1.2f), m.wood);
        Box(g, "Bedny_B", new Vector3(4f, 0.5f, 40f), new Vector3(1.6f, 1f, 1.1f), m.wood);
        Box(g, "Dodavka", new Vector3(3.4f, 1.1f, 46f), new Vector3(2.2f, 2.2f, 4.5f), m.plaster);
        Box(g, "Dodavka_Okna", new Vector3(3.4f, 1.6f, 47.6f), new Vector3(2.25f, 0.7f, 1.2f), m.black, false);
        Box(g, "Automat", new Vector3(-5.4f, 1f, 42f), new Vector3(0.9f, 2f, 0.8f), m.red);
        Sakura(g, new Vector3(-17f, 0f, 47f), 0.9f, 3);
    }

    static void BuildGate()
    {
        var g = MapBuildKit.Group(root, "Brana");
        // hradba mezi mestem a nadvorim (z 50-52), hlavni brana x -3..3 (vyska 3,8), bocni dvirka x -12..-10
        Span(g, "Zed_L", new Vector3(-26f, 0f, 50f), new Vector3(-12f, 5.4f, 52f), m.plaster);
        Span(g, "Zed_LM", new Vector3(-10f, 0f, 50f), new Vector3(-3f, 5.4f, 52f), m.plaster);
        Span(g, "Zed_P", new Vector3(3f, 0f, 50f), new Vector3(26f, 5.4f, 52f), m.plaster);
        Span(g, "Preklad_Brana", new Vector3(-3f, 3.8f, 50f), new Vector3(3f, 5.4f, 52f), m.plaster);
        Span(g, "Preklad_Dvirka", new Vector3(-12f, 2.6f, 50f), new Vector3(-10f, 5.4f, 52f), m.plaster);
        Span(g, "Sokl", new Vector3(-26.05f, 0f, 49.95f), new Vector3(26.05f, 0.7f, 50.4f), m.stone, false);
        Span(g, "Strisska_Zdi", new Vector3(-26.2f, 5.4f, 49.6f), new Vector3(26.2f, 5.65f, 52.2f), m.roof, false);
        // velka drevena vrata (otevrena) a ramovani brany
        Span(g, "Vrata_L", new Vector3(-3.6f, 0f, 52f), new Vector3(-3.2f, 3.7f, 55f), m.darkWood);
        Span(g, "Vrata_P", new Vector3(3.2f, 0f, 52f), new Vector3(3.6f, 3.7f, 55f), m.darkWood);
        Span(g, "Ram_L", new Vector3(-3.6f, 0f, 49.6f), new Vector3(-3f, 5.6f, 50.2f), m.red, false);
        Span(g, "Ram_P", new Vector3(3f, 0f, 49.6f), new Vector3(3.6f, 5.6f, 50.2f), m.red, false);
        Roof(g, "Strecha_Brany", new Vector3(0f, 0f, 51f), 9f, 3.2f, 5.6f, true, 25f, 0.7f);
        Span(g, "Dvirka", new Vector3(-12.2f, 0f, 52f), new Vector3(-12f, 2.5f, 53.9f), m.wood);
        PaperLantern(g, new Vector3(-4.3f, 3.2f, 49.4f));
        PaperLantern(g, new Vector3(4.3f, 3.2f, 49.4f));

        // ochoz za zdi (y 4,3) - obranci strileji pres zed do mesta; rampa dolu vlevo, vpravo navazuje balkon
        Deck(g, "Ochoz", -16f, 19f, 52f, 55f, BalconyY, 0f, posts: false);
        foreach (float x in new[] { -15.7f, -7f, 6f, 12.5f })
            Span(g, "Ochoz_Sloup", new Vector3(x - 0.18f, 0f, 54.5f), new Vector3(x + 0.18f, BalconyY - 0.3f, 54.86f), m.darkWood);
        Ramp(g, "Ochoz_Rampa", new Vector3(-14.5f, 0f, 63.5f), new Vector3(-14.5f, BalconyY, 55f), 3f, m.planks);
        Railing(g, "Ochoz_Zabradli", new Vector3(-13f, 0f, 55f), new Vector3(-4f, 0f, 55f), BalconyY);
        Railing(g, "Ochoz_Zabradli_B", new Vector3(4f, 0f, 55f), new Vector3(13f, 0f, 55f), BalconyY);
    }

    static void BuildCourtyard()
    {
        var g = MapBuildKit.Group(root, "Nadvori");

        // bod A: zahrada kamenu uprostred (kameny = kryti), sakura, lucerny
        Rock(g, new Vector3(-5f, 0f, 63f), new Vector3(2.6f, 1.6f, 2.2f), 1);
        Rock(g, new Vector3(6f, 0f, 72f), new Vector3(2.2f, 1.8f, 2.6f), 2);
        Rock(g, new Vector3(-3f, 0f, 76.5f), new Vector3(3f, 1.4f, 2f), 3);
        Rock(g, new Vector3(3f, 0f, 62f), new Vector3(1.6f, 1.1f, 1.6f), 4);
        Sakura(g, new Vector3(-9f, 0f, 59f), 1.1f, 7);
        Sakura(g, new Vector3(9.5f, 0f, 80f), 1f, 8);
        StoneLantern(g, new Vector3(-7f, 0f, 70f));
        StoneLantern(g, new Vector3(7.5f, 0f, 64f));
        Box(g, "Jezirko", new Vector3(8f, 0.01f, 58.5f), new Vector3(4f, 0.04f, 3f), m.water, false, shadows: false);
        Span(g, "Jezirko_Lem", new Vector3(5.8f, 0f, 56.8f), new Vector3(10.2f, 0.3f, 57.1f), m.rock);

        // vlevo: chyse (x -24..-17), terasa (y 1,5) a rampa k bocnim dverim hradu (x -23..-20)
        House(g, "Chyse", -24f, -17f, 60f, 70f, 3.4f, ridgeAlongX: false, windowSide: 1);
        Span(g, "Terasa", new Vector3(-16f, 0f, 72f), new Vector3(-10f, 1.5f, 80f), m.stone);
        Span(g, "Terasa_Podlaha", new Vector3(-16f, 1.5f, 72f), new Vector3(-10f, 1.6f, 80f), m.planks);
        Ramp(g, "Terasa_Rampa", new Vector3(-5f, 0f, 76f), new Vector3(-10f, 1.6f, 76f), 3f, m.planks);
        Railing(g, "Terasa_Zabradli", new Vector3(-16f, 0f, 72f), new Vector3(-10f, 0f, 72f), 1.6f);
        Ramp(g, "Rampa_BocniDvere", new Vector3(-21.5f, 0f, 77f), new Vector3(-21.5f, CastleY, 87f), 3f, m.stone);
        Span(g, "Rampa_Zed", new Vector3(-20f, 0f, 77f), new Vector3(-19.6f, 1.2f, 85.5f), m.stone);

        // vpravo: balkon (y 4,3) od ochozu nad branou az k hradni zdi, rampa z nadvori; za nim budova (pagoda)
        Deck(g, "Balkon", 13f, 19f, 55f, 84f, BalconyY, 0f);
        Ramp(g, "Balkon_Rampa", new Vector3(11.5f, 0f, 66f), new Vector3(11.5f, BalconyY, 58f), 3f, m.planks);
        Span(g, "Balkon_Napojeni", new Vector3(10f, BalconyY - 0.3f, 55f), new Vector3(13f, BalconyY, 58f), m.planks);
        Railing(g, "Balkon_Zabradli", new Vector3(13f, 0f, 58f), new Vector3(13f, 0f, 84f), BalconyY);
        House(g, "Pagoda_Budova", 19f, 26f, 55f, 84f, 7.5f, ridgeAlongX: false, wall: m.red, windowSide: -1);
        Roof(g, "Pagoda_Stupen", new Vector3(22.5f, 0f, 70f), 5f, 8f, 10.5f, false, 30f, 0.6f);
        Span(g, "Pagoda_Vez", new Vector3(20.5f, 7.5f, 66f), new Vector3(24.5f, 10.5f, 74f), m.red);

        // vzadu vlevo: dum (apartma)
        House(g, "Apartma", -10f, -5f, 80.5f, 84f, 6f, windowSide: -1);
        PaperLantern(g, new Vector3(-7.5f, 3f, 80.1f));
    }

    static void BuildCastleWall()
    {
        var g = MapBuildKit.Group(root, "HradniZed");
        const float z0 = 84f, z1 = 85.5f, top = 7.5f;
        // otvory: hlavni brana x -4..4 (do 6,5), bocni dvere x -23..-20 (do 4,5), prechod z balkonu x 13..19 (y 4,3..7)
        Span(g, "Zed_1", new Vector3(-26f, 0f, z0), new Vector3(-23f, top, z1), m.stone);
        Span(g, "Zed_2", new Vector3(-20f, 0f, z0), new Vector3(-4f, top, z1), m.stone);
        Span(g, "Zed_3", new Vector3(4f, 0f, z0), new Vector3(13f, top, z1), m.stone);
        Span(g, "Zed_4", new Vector3(19f, 0f, z0), new Vector3(26f, top, z1), m.stone);
        Span(g, "Preklad_Brana", new Vector3(-4f, 6.5f, z0), new Vector3(4f, top, z1), m.stone);
        Span(g, "Preklad_Dvere", new Vector3(-23f, 4.5f, z0), new Vector3(-20f, top, z1), m.stone);
        Span(g, "Pod_Prechodem", new Vector3(13f, 0f, z0), new Vector3(19f, BalconyY - 0.3f, z1), m.stone);
        Span(g, "Nad_Prechodem", new Vector3(13f, 7f, z0), new Vector3(19f, top, z1), m.stone);
        Span(g, "Strisska", new Vector3(-26.2f, top, z0 - 0.4f), new Vector3(26.2f, top + 0.25f, z1 + 0.4f), m.roof, false);
        // hlavni brana: rampa, cervene sloupy, strecha
        Ramp(g, "Rampa_Brana", new Vector3(0f, 0f, 79f), new Vector3(0f, CastleY, 87f), 8f, m.stone);
        Span(g, "Sloup_L", new Vector3(-4.6f, 0f, z0 - 0.6f), new Vector3(-4f, 7.2f, z0), m.red, false);
        Span(g, "Sloup_P", new Vector3(4f, 0f, z0 - 0.6f), new Vector3(4.6f, 7.2f, z0), m.red, false);
        Roof(g, "Strecha_Brany", new Vector3(0f, 0f, 84.75f), 11f, 3f, 7.6f, true, 26f, 0.9f);
        // prechod z balkonu na terasu zahrady (y 4,3)
        Span(g, "Prechod", new Vector3(13f, BalconyY - 0.3f, z0), new Vector3(19f, BalconyY, z1), m.planks);
    }

    static void BuildGarden()
    {
        var g = MapBuildKit.Group(root, "Zahrada");
        float y = CastleY;

        // bod B: travnik se sakurami, kamennymi luceriami, svatynka za bodem
        Sakura(g, new Vector3(-7f, y, 95f), 1.2f, 11);
        Sakura(g, new Vector3(7f, y, 99f), 1.1f, 12);
        Sakura(g, new Vector3(-6f, y, 110f), 1.3f, 13);
        Sakura(g, new Vector3(-12f, y, 90f), 1f, 14);
        Sakura(g, new Vector3(9f, y, 113f), 1f, 15);
        StoneLantern(g, new Vector3(-3.5f, y, 99f));
        StoneLantern(g, new Vector3(3.5f, y, 108f));
        Rock(g, new Vector3(4f, y, 101f), new Vector3(2f, 1.5f, 2.4f), 5);
        Rock(g, new Vector3(-4.5f, y, 106f), new Vector3(2.4f, 1.3f, 1.8f), 6);
        House(g, "Svatynka", -3f, 3f, 110.5f, 114f, 3f, y, wall: m.red);
        Torii(g, "Torii_Zahrada", new Vector3(0f, y, 92f), 5f, 4.5f);

        // vlevo: obchod s krytou verandou
        House(g, "Obchod", -24f, -15f, 92f, 108f, 5f, y, ridgeAlongX: false, windowSide: 1);
        Deck(g, "Obchod_Veranda", -15f, -12.5f, 92f, 108f, y + 0.4f, y, posts: false);
        Ramp(g, "Obchod_Schod", new Vector3(-11.6f, y, 100f), new Vector3(-12.5f, y + 0.4f, 100f), 3f, m.planks);
        Roof(g, "Obchod_Pristresek", new Vector3(-13.8f, 0f, 100f), 2.6f, 16f, y + 3.2f, false, 15f, 0.3f);
        for (int i = 0; i < 4; i++)
            Span(g, "Veranda_Sloup", new Vector3(-12.8f, y + 0.4f, Mathf.Lerp(92.2f, 107.5f, i / 3f)), new Vector3(-12.5f, y + 3.2f, Mathf.Lerp(92.5f, 107.8f, i / 3f)), m.darkWood);
        Box(g, "Obchod_Noren", new Vector3(-14.95f, y + 2.4f, 100f), new Vector3(0.05f, 1f, 3f), m.blue, false, shadows: false);

        // vpravo: terasa (y 4,3) navazujici na balkon nadvori - vyhled na bod B, rampa z travniku
        Span(g, "Terasa_Telo", new Vector3(12f, PlateauTop, 85.5f), new Vector3(25.4f, BalconyY - 0.1f, 114f), m.stone);
        Span(g, "Terasa_Podlaha", new Vector3(12f, BalconyY - 0.1f, 85.5f), new Vector3(25.4f, BalconyY, 114f), m.planks);
        Ramp(g, "Terasa_Rampa", new Vector3(10.5f, y, 88f), new Vector3(10.5f, BalconyY, 96f), 3f, m.planks);
        Railing(g, "Terasa_Zabradli", new Vector3(12f, 0f, 96f), new Vector3(12f, 0f, 114f), BalconyY);
        Railing(g, "Terasa_Zabradli_S", new Vector3(12f, 0f, 114f), new Vector3(15f, 0f, 114f), BalconyY);
        House(g, "Sin", 19f, 24f, 96f, 112f, 3.6f, BalconyY, ridgeAlongX: false, windowSide: -1);
        PaperLantern(g, new Vector3(18.6f, BalconyY + 2.6f, 98f));
        PaperLantern(g, new Vector3(18.6f, BalconyY + 2.6f, 110f));
    }

    static void BuildDojo()
    {
        var g = MapBuildKit.Group(root, "Dojo");
        float y = CastleY;
        const float minX = -15f, maxX = 15f, minZ = 118f, maxZ = 150f, wallTop = 10f, t = 0.6f;

        // podlaha (prkna), podium, spawn obrancu za nim (y 3,5)
        Span(g, "Podlaha", new Vector3(minX, PlateauTop, minZ), new Vector3(maxX, y, maxZ), m.planks);
        Span(g, "Podium", new Vector3(-8f, y, 134f), new Vector3(8f, 3.5f, 140f), m.planks);
        Ramp(g, "Podium_Rampa", new Vector3(0f, y, 132f), new Vector3(0f, 3.5f, 134f), 16f, m.planks);
        Span(g, "Zazemi", new Vector3(minX, y, 140f), new Vector3(maxX, 3.5f, maxZ), m.planks);
        Span(g, "Podium_Bok_L", new Vector3(minX, y, 134f), new Vector3(-8f, 3.5f, 140f), m.planks);
        Span(g, "Podium_Bok_P", new Vector3(8f, y, 134f), new Vector3(maxX, 3.5f, 140f), m.planks);
        // pricka mezi podiem a spawnem, dva pruchody x -10..-7 a 7..10
        Span(g, "Pricka_L", new Vector3(minX, 3.5f, 140f), new Vector3(-10f, wallTop, 140.5f), m.wood);
        Span(g, "Pricka_M", new Vector3(-7f, 3.5f, 140f), new Vector3(7f, wallTop, 140.5f), m.wood);
        Span(g, "Pricka_P", new Vector3(10f, 3.5f, 140f), new Vector3(maxX, wallTop, 140.5f), m.wood);
        Span(g, "Pricka_Preklad_L", new Vector3(-10f, 6.5f, 140f), new Vector3(-7f, wallTop, 140.5f), m.wood);
        Span(g, "Pricka_Preklad_P", new Vector3(7f, 6.5f, 140f), new Vector3(10f, wallTop, 140.5f), m.wood);
        // obraz draka (zlaty) na pricce nad podiem
        Box(g, "Obraz", new Vector3(0f, 6.6f, 139.95f), new Vector3(9f, 4.4f, 0.1f), m.gold, false, shadows: false);
        Box(g, "Obraz_Ram", new Vector3(0f, 6.6f, 140f), new Vector3(9.6f, 5f, 0.08f), m.black, false, shadows: false);
        Box(g, "Drak_1", new Vector3(-2f, 7.4f, 139.88f), new Vector3(4.5f, 0.6f, 0.05f), m.blue, false, yaw: 0f, rotation: Quaternion.Euler(0f, 0f, 18f), shadows: false);
        Box(g, "Drak_2", new Vector3(2f, 5.8f, 139.88f), new Vector3(4.5f, 0.6f, 0.05f), m.blue, false, rotation: Quaternion.Euler(0f, 0f, -14f), shadows: false);

        // obvodove zdi s otvory: predni vrata x -3..3 (do 6,5), boky - vychod: dvere z 122-125 (dole) a 130-133 (galerie y 6),
        // zapad: dvere z 120-123 (dole)
        Span(g, "Predni_L", new Vector3(minX, y, minZ), new Vector3(-3f, wallTop, minZ + t), m.plaster);
        Span(g, "Predni_P", new Vector3(3f, y, minZ), new Vector3(maxX, wallTop, minZ + t), m.plaster);
        Span(g, "Predni_Preklad", new Vector3(-3f, 6.5f, minZ), new Vector3(3f, wallTop, minZ + t), m.plaster);
        Span(g, "Zadni", new Vector3(minX, y, maxZ - t), new Vector3(maxX, wallTop, maxZ), m.plaster);
        Span(g, "Zapad_1", new Vector3(minX, y, minZ), new Vector3(minX + t, wallTop, 120f), m.plaster);
        Span(g, "Zapad_Preklad", new Vector3(minX, 5.5f, 120f), new Vector3(minX + t, wallTop, 123f), m.plaster);
        Span(g, "Zapad_2", new Vector3(minX, y, 123f), new Vector3(minX + t, wallTop, maxZ), m.plaster);
        Span(g, "Vychod_1", new Vector3(maxX - t, y, minZ), new Vector3(maxX, wallTop, 122f), m.plaster);
        Span(g, "Vychod_Preklad", new Vector3(maxX - t, 5.5f, 122f), new Vector3(maxX, 6f, 125f), m.plaster);
        Span(g, "Vychod_2", new Vector3(maxX - t, y, 125f), new Vector3(maxX, wallTop, 130f), m.plaster);
        Span(g, "Vychod_Pod_Galerii", new Vector3(maxX - t, y, 130f), new Vector3(maxX, 6f, 133f), m.plaster);
        Span(g, "Vychod_Nad_Galerii", new Vector3(maxX - t, 9f, 130f), new Vector3(maxX, wallTop, 133f), m.plaster);
        Span(g, "Vychod_3", new Vector3(maxX - t, y, 133f), new Vector3(maxX, wallTop, maxZ), m.plaster);
        Span(g, "Vychod_Nad_Dvermi", new Vector3(maxX - t, 6f, 122f), new Vector3(maxX, wallTop, 125f), m.plaster);
        // tmave tramy a sokl zvenku
        Span(g, "Sokl", new Vector3(minX - 0.05f, y, minZ - 0.05f), new Vector3(maxX + 0.05f, y + 0.7f, maxZ + 0.05f), m.darkWood, false);
        Span(g, "Tram", new Vector3(minX - 0.08f, wallTop - 0.4f, minZ - 0.08f), new Vector3(maxX + 0.08f, wallTop, maxZ + 0.08f), m.darkWood, false);
        Roof(g, "Strecha", new Vector3(0f, 0f, 134f), 30f, 32f, wallTop, false, 30f, 1.5f);
        // predni prostresek nad vstupem, cervene sloupy
        Roof(g, "Vstup_Strecha", new Vector3(0f, 0f, 116f), 10f, 4f, 7f, true, 24f, 0.6f);
        Span(g, "Vstup_Sloup_L", new Vector3(-4.5f, y, 114.2f), new Vector3(-4f, 7f, 114.7f), m.red);
        Span(g, "Vstup_Sloup_P", new Vector3(4f, y, 114.2f), new Vector3(4.5f, 7f, 114.7f), m.red);

        // bod C: sloupy (kryti) a lucerny
        foreach (var p in new[] { new Vector2(-6f, 123f), new Vector2(6f, 123f), new Vector2(-6f, 131f), new Vector2(6f, 131f) })
            Span(g, "Sloup", new Vector3(p.x - 0.35f, y, p.y - 0.35f), new Vector3(p.x + 0.35f, wallTop, p.y + 0.35f), m.red);

        // vyhled vlevo nahore (y 5,5) s rampou podel zapadni zdi
        Deck(g, "Vyhled", minX + t, -10f, 120f, 130f, 5.5f, y, posts: false);
        foreach (float z in new[] { 120.3f, 125f, 129.7f })
            Span(g, "Vyhled_Sloup", new Vector3(-10.4f, y, z - 0.18f), new Vector3(-10.04f, 5.2f, z + 0.18f), m.darkWood);
        Ramp(g, "Vyhled_Rampa", new Vector3(-13f, 3.5f, 136f), new Vector3(-13f, 5.5f, 130f), 3.2f, m.planks);
        Railing(g, "Vyhled_Zabradli", new Vector3(-10f, 0f, 120f), new Vector3(-10f, 0f, 130f), 5.5f);
        // galerie vpravo nahore (y 6) s rampou a dvermi ven na lavku
        Deck(g, "Galerie", 10f, maxX - t, 128f, 138f, 6f, y);
        Ramp(g, "Galerie_Rampa", new Vector3(11.7f, y, 120.5f), new Vector3(11.7f, 6f, 128f), 2.8f, m.planks);
        Railing(g, "Galerie_Zabradli", new Vector3(10f, 0f, 128.3f), new Vector3(10f, 0f, 138f), 6f);
        Railing(g, "Galerie_Zabradli_S", new Vector3(10f, 0f, 138f), new Vector3(14.4f, 0f, 138f), 6f);

        // venkovni lavka: z terasy zahrady (y 4,3) nahoru k hornim dverim galerie (y 6)
        Ramp(g, "Lavka_Rampa", new Vector3(17f, BalconyY, 114f), new Vector3(17f, 6f, 122f), 4f, m.planks);
        Deck(g, "Lavka", 15f, 19f, 122f, 134f, 6f, y);
        Railing(g, "Lavka_Zabradli", new Vector3(19f, 0f, 122f), new Vector3(19f, 0f, 134f), 6f);

        // svetla v sale (strecha ho zastini)
        Lamp(g, new Vector3(0f, 8f, 124f), 18f, 2.2f);
        Lamp(g, new Vector3(0f, 8f, 134f), 18f, 2.2f);
        Lamp(g, new Vector3(-11f, 8f, 125f), 12f, 1.4f);
        Lamp(g, new Vector3(11f, 8.5f, 134f), 12f, 1.4f);
        Lamp(g, new Vector3(0f, 6f, 145f), 14f, 1.5f);
        foreach (var p in new[] { new Vector3(-6f, 5.5f, 123.6f), new Vector3(6f, 5.5f, 123.6f), new Vector3(-6f, 5.5f, 131.6f), new Vector3(6f, 5.5f, 131.6f) })
            PaperLantern(g, p);
    }

    static void BuildBounds()
    {
        var g = MapBuildKit.Group(root, "Hranice");
        // vysoke neviditelne steny po obvodu (nad domy) a strop
        Invisible(g, "Zapad", new Vector3(-27f, -5f, -12f), new Vector3(-26f, 40f, 156f));
        Invisible(g, "Vychod", new Vector3(26f, -5f, -12f), new Vector3(27f, 40f, 156f));
        Invisible(g, "Jih", new Vector3(-27f, -5f, -12f), new Vector3(27f, 40f, -10f));
        Invisible(g, "Sever", new Vector3(-27f, -5f, 154f), new Vector3(27f, 40f, 156f));
        // kamenne zdi hradu po stranach a vzadu (viditelne)
        Span(g, "HradZed_Z", new Vector3(-26f, PlateauTop, 85.5f), new Vector3(-25.4f, 6.5f, 154f), m.stone);
        Span(g, "HradZed_V", new Vector3(25.4f, PlateauTop, 85.5f), new Vector3(26f, 6.5f, 154f), m.stone);
        Span(g, "HradZed_S", new Vector3(-26f, PlateauTop, 153.4f), new Vector3(26f, 6.5f, 154f), m.stone);
        Span(g, "NadvoriZed_Z", new Vector3(-26f, 0f, 52f), new Vector3(-25.4f, 5.4f, 84f), m.plaster);
        // pad z mapy
        Marker(g, "KillPlane", new Vector3(0f, -15f, 70f));
    }

    static void BuildMarkers()
    {
        var g = MapBuildKit.Group(root, "Body");
        float y = CastleY;
        // body A, B, C (velikost = meritko X, Z)
        Marker(g, "CapturePoint_1", new Vector3(0f, 0.5f, 68f), new Vector3(10f, 1f, 10f));
        Marker(g, "CapturePoint_2", new Vector3(0f, y + 0.5f, 103f), new Vector3(9f, 1f, 9f));
        Marker(g, "CapturePoint_3", new Vector3(0f, y + 0.5f, 127f), new Vector3(10f, 1f, 8f));
        // zakladny (team deathmatch): mesto a zahrada
        Marker(g, "SpawnPoint_Team0", new Vector3(0f, 0.1f, 22f));
        Marker(g, "SpawnPoint_Team1", new Vector3(0f, y + 0.1f, 97f));
        // utok a obrana
        Marker(g, "SpawnPoint_Utok_A", new Vector3(0f, 0.1f, 1f));
        Marker(g, "SpawnPoint_Obrana_A", new Vector3(0f, y + 0.1f, 96f));
        Marker(g, "SpawnPoint_Utok_B", new Vector3(-4f, 0.1f, 58f));
        Marker(g, "SpawnPoint_Obrana_B", new Vector3(0f, 3.6f, 145f));
        Marker(g, "SpawnPoint_Utok_C", new Vector3(-8f, y + 0.1f, 90f));
        Marker(g, "SpawnPoint_Obrana_C", new Vector3(0f, 3.6f, 145f));
    }
}
