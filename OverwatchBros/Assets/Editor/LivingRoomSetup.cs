using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;

// Obyvak v prizemi hlavni chaty podle fotek: terakotova dlazba, kilim koberec, bily krb se zvysenou kachlovou
// podestou a ohnem, smetanova rohova sedacka, horcicova sametova kresla a taburety, konferencni stolek s deckou,
// televize na policce s knihami, jidelni stul s ubrusem a zidlemi, kanci kuze na zdi, stojaci lampa, obrazy, svetla.
// Rozmery mistnosti se mer z jejich zdi ve scene (HlavniChata/PRIZEMI/obyvak). Postavi se samo do
// HlavniChata/Interier/Obyvak; scenu je pak potreba ulozit. Rucne: BrosOverwatch > Mapa > Postavit obyvak znovu.
// Kolize ma nabytek (kryt pri prestrelce), drobnosti (vazy, obrazy, koberce) ne.
[InitializeOnLoad]
public static class LivingRoomSetup
{
    const string MapName = "Map-Domasov";
    const string RoomName = "Obyvak";
    const string Version = "_v7";
    const string Folder = "Assets/Materials/Interior";

    static LivingRoomSetup()
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

        var house = GameObject.Find(MapName)?.transform.Find("HlavniChata");
        if (house == null) return;
        if (house.Find(HouseSetup.InteriorName + "/" + RoomName + "/" + Version) != null) return;

        if (Build(house))
        {
            EditorSceneManager.MarkSceneDirty(house.gameObject.scene);
            Debug.Log("[Obyvak] Obyvak v hlavni chate zarizen. Uloz scenu (Ctrl+S).");
        }
        else if (retries++ < 30)
        {
            EditorApplication.delayCall += RunIfNeeded;
        }
    }

    [MenuItem("BrosOverwatch/Mapa/Postavit obyvak znovu")]
    static void RebuildMenu()
    {
        var house = GameObject.Find(MapName)?.transform.Find("HlavniChata");
        if (house != null && Build(house))
            EditorSceneManager.MarkSceneDirty(house.gameObject.scene);
    }

    // ---------------- materialy ----------------

    class Mats
    {
        public Material tiles, rug, hide, mustard, cream, wood, darkWood, plaster, black, fire, slate, white, shade, glass, lamp,
            bark, beige, painting0, painting1, dried, oldWood, oldPanel, brass, screen, photo;
        public VideoClip video;
        public Material[] books;
    }

    static Mats LoadMaterials()
    {
        string w = "Assets/Materials/Wood", h = "Assets/Materials/House", g = "Assets/Materials/Garden";
        var m = new Mats
        {
            tiles = MapBuildKit.Mat(Folder + "/Terracotta.mat", Folder + "/Terracotta.png", Color.white, Vector2.one, 0.35f),
            rug = MapBuildKit.Mat(Folder + "/Rug.mat", Folder + "/Rug.png", Color.white, Vector2.one, 0.05f),
            hide = MapBuildKit.Mat(Folder + "/BoarHide.mat", Folder + "/BoarHide.png", Color.white, Vector2.one, 0.05f, alphaClip: true, doubleSided: true),
            mustard = MapBuildKit.Mat(Folder + "/VelvetMustard.mat", Folder + "/Velvet.png", new Color(0.82f, 0.68f, 0.18f), new Vector2(2f, 2f), 0.25f),
            cream = MapBuildKit.Mat(Folder + "/VelvetCream.mat", Folder + "/Velvet.png", new Color(0.98f, 0.95f, 0.86f), new Vector2(2f, 2f), 0.15f),
            wood = MapBuildKit.Mat(Folder + "/FurnitureWood.mat", w + "/WoodBeam.png", new Color(1.25f, 0.85f, 0.65f), Vector2.one, 0.35f),
            darkWood = MapBuildKit.Mat(Folder + "/FurnitureDark.mat", w + "/WoodBeam.png", new Color(0.6f, 0.45f, 0.38f), Vector2.one, 0.35f),
            black = MapBuildKit.Mat(Folder + "/Black.mat", null, new Color(0.05f, 0.05f, 0.05f), Vector2.one, 0.3f),
            fire = MapBuildKit.Mat(Folder + "/Fire.mat", null, new Color(1f, 0.55f, 0.15f), Vector2.one, 0f, emission: new Color(4f, 1.6f, 0.25f)),
            slate = MapBuildKit.Mat(Folder + "/Slate.mat", null, new Color(0.38f, 0.43f, 0.5f), Vector2.one, 0.3f),
            white = MapBuildKit.Mat(Folder + "/Cloth.mat", null, new Color(0.95f, 0.94f, 0.9f), Vector2.one, 0.05f),
            shade = MapBuildKit.Mat(Folder + "/Wicker.mat", null, new Color(0.78f, 0.62f, 0.4f), Vector2.one, 0.1f, doubleSided: true),
            beige = MapBuildKit.Mat(Folder + "/RugBeige.mat", null, new Color(0.72f, 0.6f, 0.45f), Vector2.one, 0.05f),
            oldWood = MapBuildKit.Mat(Folder + "/OldWood.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.72f, 0.5f, 0.36f), Vector2.one, 0.3f),
            oldPanel = MapBuildKit.Mat(Folder + "/OldWoodPanel.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.95f, 0.68f, 0.48f), Vector2.one, 0.35f),
            brass = MapBuildKit.Mat(Folder + "/Brass.mat", null, new Color(0.78f, 0.62f, 0.28f), Vector2.one, 0.75f),
            dried = MapBuildKit.Mat(Folder + "/DriedFlowers.mat", null, new Color(0.8f, 0.62f, 0.2f), Vector2.one, 0.05f),
            painting0 = MapBuildKit.Mat(Folder + "/Painting0.mat", Folder + "/Painting0.png", Color.white, Vector2.one, 0.2f),
            painting1 = MapBuildKit.Mat(Folder + "/Painting1.mat", Folder + "/Painting1.png", Color.white, Vector2.one, 0.2f),
            books = new[]
            {
                MapBuildKit.Mat(Folder + "/Book_Red.mat", null, new Color(0.6f, 0.12f, 0.1f), Vector2.one, 0.2f),
                MapBuildKit.Mat(Folder + "/Book_Green.mat", null, new Color(0.15f, 0.4f, 0.2f), Vector2.one, 0.2f),
                MapBuildKit.Mat(Folder + "/Book_Blue.mat", null, new Color(0.12f, 0.2f, 0.45f), Vector2.one, 0.2f),
                MapBuildKit.Mat(Folder + "/Book_Yellow.mat", null, new Color(0.85f, 0.7f, 0.2f), Vector2.one, 0.2f),
                MapBuildKit.Mat(Folder + "/Book_White.mat", null, new Color(0.9f, 0.88f, 0.82f), Vector2.one, 0.2f),
            },
            screen = UnlitMat(Folder + "/TvScreen.mat"),
            photo = MapBuildKit.Mat(Folder + "/PhotoSofa.mat", Folder + "/PhotoSofa.png", Color.white, Vector2.one, 0.15f),
            video = AssetDatabase.LoadAssetAtPath<VideoClip>(VideoPath),
            plaster = AssetDatabase.LoadAssetAtPath<Material>(h + "/Plaster.mat"),
            glass = AssetDatabase.LoadAssetAtPath<Material>(h + "/Glass.mat"),
            lamp = AssetDatabase.LoadAssetAtPath<Material>(g + "/Lantern.mat"),
            bark = AssetDatabase.LoadAssetAtPath<Material>(g + "/Bark.mat"),
        };
        if (m.tiles == null || m.rug == null || m.hide == null || m.mustard == null || m.cream == null || m.wood == null
            || m.painting0 == null || m.photo == null || m.screen == null || m.video == null || m.plaster == null || m.glass == null || m.lamp == null || m.bark == null)
            return null;
        return m;
    }

    // Video na televizi (smycka, vcetne zvuku). Muzes ho nahradit jinym .mp4 se stejnym jmenem.
    const string VideoPath = "Assets/Video/TV_loop_video.mp4";

    // Obrazovka bez stinovani, aby video svitilo i v sere mistnosti.
    static Material UnlitMat(string path)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return null;
        material = new Material(shader);
        material.SetColor("_BaseColor", Color.white);
        MapBuildKit.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace(System.IO.Path.DirectorySeparatorChar, '/'));
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // ---------------- mistnost ----------------

    struct Room
    {
        public float x0, x1, z0, z1, ceiling;
        public float doorX0, doorX1;   // pruchod v jizni stene
    }

    static bool MeasureRoom(Transform house, out Room room)
    {
        room = new Room { x0 = float.MinValue, x1 = float.MaxValue, z0 = float.MinValue, z1 = float.MaxValue, ceiling = 3.2f };
        var obyvak = house.Find("PRIZEMI/obyvak");
        if (obyvak == null) return false;

        var south = new List<Vector2>();
        foreach (var r in obyvak.GetComponentsInChildren<MeshRenderer>(true))
        {
            Bounds b = r.bounds;
            if (b.size.z > b.size.x)
            {
                if (b.center.x < 15f) room.x0 = Mathf.Max(room.x0, b.max.x);
                else room.x1 = Mathf.Min(room.x1, b.min.x);
            }
            else
            {
                if (b.center.z < 5f)
                {
                    room.z0 = Mathf.Max(room.z0, b.max.z);
                    if (b.min.y < 1.5f && b.max.y > 1.5f) south.Add(new Vector2(b.min.x, b.max.x));
                }
                else room.z1 = Mathf.Min(room.z1, b.min.z);
            }
        }
        if (room.x1 - room.x0 < 15f || room.z1 - room.z0 < 8f || room.x1 - room.x0 > 30f) return false;

        // Strop: spodek nejnizsi stropni desky nad stredem mistnosti.
        float ceiling = float.MaxValue;
        var center = new Vector3((room.x0 + room.x1) * 0.5f, 0f, (room.z0 + room.z1) * 0.5f);
        foreach (var r in house.GetComponentsInChildren<MeshRenderer>(true))
        {
            Bounds b = r.bounds;
            if (!r.transform.parent || !r.transform.parent.name.StartsWith("strop")) continue;
            if (b.min.x <= center.x && b.max.x >= center.x && b.min.z <= center.z && b.max.z >= center.z && b.min.y > 2.5f)
                ceiling = Mathf.Min(ceiling, b.min.y);
        }
        if (ceiling < 10f) room.ceiling = ceiling;

        // Pruchod do chodby = nejvetsi mezera v jizni stene.
        south.Sort((a, b) => a.x.CompareTo(b.x));
        float cursor = room.x0, bestGap = 0f;
        room.doorX0 = room.doorX1 = -999f;
        foreach (var seg in south)
        {
            if (seg.x - cursor > bestGap)
            {
                bestGap = seg.x - cursor;
                room.doorX0 = cursor;
                room.doorX1 = seg.x;
            }
            cursor = Mathf.Max(cursor, seg.y);
        }
        if (room.x1 - cursor > bestGap)
        {
            room.doorX0 = cursor;
            room.doorX1 = room.x1;
        }
        return true;
    }

    // ---------------- stavba ----------------

    static bool Build(Transform house)
    {
        var m = LoadMaterials();
        if (m == null || !MeasureRoom(house, out var r)) return false;

        var interior = MapBuildKit.Group(house, HouseSetup.InteriorName);
        var old = interior.Find(RoomName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject(RoomName).transform;
        root.SetParent(interior, true);
        new GameObject(Version).transform.SetParent(root, false);

        // Pozice v mistnosti: u = metry od zapadni zdi, v = metry od jizni steny.
        Vector3 P(float u, float y, float v) => new Vector3(r.x0 + u, y, r.z0 + v);
        float width = r.x1 - r.x0, depth = r.z1 - r.z0;

        // --- podlaha a koberce ---
        MapBuildKit.Box(root, "Dlazba", P(width * 0.5f, 0.008f, depth * 0.5f), new Vector3(width, 0.016f, depth), Quaternion.identity, m.tiles, false, true, false);
        Rug(root, P(5.3f, 0.02f, 4.5f), 5.2f, 3.9f, m.rug);
        MapBuildKit.Box(root, "KobecJidelna", P(width - 4.9f, 0.02f, depth - 2.8f), new Vector3(3.6f, 0.01f, 4.1f), Quaternion.identity, m.beige, false, true, false);

        // --- krb na jizni stene (vedle kresel), cely otoceny do mistnosti (+z) ---
        float fu = 7.4f;
        var fireplace = new GameObject("Krb").transform;
        fireplace.SetParent(root, true);
        MapBuildKit.Box(fireplace, "Komin", P(fu, r.ceiling * 0.5f, 0.45f), new Vector3(2.2f, r.ceiling, 0.9f), m.plaster, true);
        MapBuildKit.Box(fireplace, "Podesta", P(fu, 0.15f, 1.0f), new Vector3(3.4f, 0.3f, 2.0f), m.tiles, true);
        MapBuildKit.Box(fireplace, "Hrana", P(fu, 0.15f, 2.03f), new Vector3(3.42f, 0.32f, 0.08f), m.slate, false);
        MapBuildKit.Box(fireplace, "Ohniste", P(fu, 0.78f, 0.91f), new Vector3(1.0f, 0.8f, 0.04f), m.black, false);
        MapBuildKit.Box(fireplace, "Rimsa", P(fu, 1.55f, 1.02f), new Vector3(2.5f, 0.08f, 0.3f), m.wood, false);
        MapBuildKit.Beam(fireplace, "Poleno", P(fu - 0.35f, 0.38f, 0.98f), P(fu + 0.35f, 0.38f, 1.02f), 0.12f, m.bark);
        MapBuildKit.Beam(fireplace, "Poleno", P(fu - 0.3f, 0.47f, 1.02f), P(fu + 0.28f, 0.44f, 0.98f), 0.11f, m.bark);
        var flame = MapBuildKit.MeshObject(fireplace, "Plamen", MapBuildKit.ConeMesh(), P(fu, 0.42f, 1.0f), Quaternion.identity, new Vector3(0.28f, 0.45f, 0.2f), m.fire, false);
        var fireLight = AddLight(fireplace, "SvetloOhne", P(fu, 0.8f, 1.5f), new Color(1f, 0.55f, 0.2f), 7f, 2.5f);
        var flicker = fireLight.gameObject.AddComponent<FireFlicker>();
        flicker.flame = flame.transform;
        GameObjectUtility.SetStaticEditorFlags(flame, 0);
        // Na rimse vaza se suchymi kvetinami, na podeste polena.
        Vase(fireplace, P(fu + 0.85f, 1.6f, 1.0f), m);
        for (int i = 0; i < 4; i++)
            MapBuildKit.Beam(fireplace, "Drevo", P(fu + 1.15f + (i / 2) * 0.13f - 0.06f, 0.36f + (i % 2) * 0.12f, 1.6f),
                P(fu + 1.15f + (i / 2) * 0.13f - 0.06f, 0.36f + (i % 2) * 0.12f, 1.95f), 0.11f, m.bark);

        // --- kresla, lampa a kanci kuze u jizni steny ---
        Armchair(root, P(3.0f, 0f, 0.62f), 0f, m);
        Armchair(root, P(4.25f, 0f, 0.62f), 0f, m);
        FloorLamp(root, P(5.3f, 0f, 0.45f), m);
        var hide = MapBuildKit.FlatMesh("KanciKuze", new[]
        {
            P(2.6f, 1.2f, 0.02f), P(4.6f, 1.2f, 0.02f), P(4.6f, 2.9f, 0.02f), P(2.6f, 2.9f, 0.02f),
        }, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
        MapBuildKit.MeshObject(root, "KanciKuze", hide, Vector3.zero, Quaternion.identity, Vector3.one, m.hide, false);

        // --- taburety a konferencni stolek ---
        Footstool(root, P(4.7f, 0f, 2.55f), m);
        Footstool(root, P(5.85f, 0f, 2.55f), m);
        CoffeeTable(root, P(5.3f, 0f, 4.5f), 90f, m);

        // --- sedacka u zapadni steny (pod zazdenymi okny), sedi se k vychodu ---
        Sofa(root, P(0.5f, 0f, depth * 0.476f), 5.9f, 90f, m);
        // Velka fotka v ramu na stene nad sedackou (pomer stran fotky 824 x 689).
        Photo(root, P(0.03f, 2.02f, depth * 0.476f), 90f, 1.62f, 1.62f * 689f / 824f, m.photo, m);

        // --- televize u severni steny, otocena k sedacce; z vychodu ji zakryva kratka pricka ---
        // Natocena sikmo k sedacce (ne kolmo ke zdi).
        TvStand(root, P(7.45f, 0f, depth - 1.2f), 240f, m);
        float wallFrom = depth - 2.4f;
        MapBuildKit.Box(root, "Pricka", P(8.42f, r.ceiling * 0.5f, (depth + wallFrom) * 0.5f), new Vector3(0.2f, r.ceiling, depth - wallFrom), m.plaster, true);

        // --- velka stara hneda skrin v jihovychodnim rohu (vedle pruchodu do chodby) ---
        Wardrobe(root, P(width - 1.14f, 0f, 0.38f), 0f, m);

        // --- jidelni kout ve vychodni casti ---
        var table = P(width - 4.9f, 0f, depth - 2.8f);
        // Stul i svitidlo nad nim stoji podelne se severo-jizni osou mistnosti.
        DiningTable(root, table, 90f, m);
        MapBuildKit.Box(root, "LampaNadStolem", table + Vector3.up * (r.ceiling - 0.55f), new Vector3(0.35f, 0.12f, 1.3f), m.black, false);
        MapBuildKit.Beam(root, "Zaves", table + Vector3.up * (r.ceiling - 0.5f) + Vector3.forward * 0.5f, table + Vector3.up * r.ceiling + Vector3.forward * 0.5f, 0.02f, m.black);
        MapBuildKit.Beam(root, "Zaves", table + Vector3.up * (r.ceiling - 0.5f) - Vector3.forward * 0.5f, table + Vector3.up * r.ceiling - Vector3.forward * 0.5f, 0.02f, m.black);
        MapBuildKit.Box(root, "Svitidlo", table + Vector3.up * (r.ceiling - 0.62f), new Vector3(0.3f, 0.02f, 1.2f), Quaternion.identity, m.lamp, false, true, false);
        AddLight(root, "SvetloJidelna", table + Vector3.up * (r.ceiling - 0.8f), new Color(1f, 0.85f, 0.65f), 7f, 1.6f);

        // --- obrazy, hodiny a ozdoby na zdech ---
        Picture(root, P(3.8f, 1.9f, depth - 0.03f), 180f, 0.8f, 0.55f, m.painting0, m);
        Picture(root, P(width - 2.8f, 1.85f, depth - 0.03f), 180f, 0.5f, 0.4f, m.painting1, m);
        Picture(root, P(width - 0.03f, 1.9f, depth * 0.5f), 270f, 0.9f, 0.6f, m.painting1, m);
        var clock = MapBuildKit.MeshObject(root, "Hodiny", MapBuildKit.BlobMesh(0), P(width - 3.8f, 2.35f, depth - 0.05f), Quaternion.identity, new Vector3(0.18f, 0.18f, 0.03f), m.white, false);
        MapBuildKit.MeshObject(root, "Dekorace", MapBuildKit.BlobMesh(2), P(9.6f, 1.9f, 0.05f), Quaternion.identity, new Vector3(0.38f, 0.38f, 0.05f), m.black, false);
        // Nastenna lampa nad sedackou.
        var wallLamp = P(4.6f, 2.05f, depth - 0.1f);
        MapBuildKit.Box(root, "NastennaLampa", wallLamp, new Vector3(0.22f, 0.22f, 0.12f), Quaternion.identity, m.lamp, false, true, false);
        AddLight(root, "SvetloSedacka", wallLamp + Vector3.back * 0.3f, new Color(1f, 0.78f, 0.5f), 6f, 1.4f);
        // Hlavni stropni svetlo v obyvaci casti.
        AddLight(root, "SvetloObyvak", P(5.5f, r.ceiling - 0.4f, depth * 0.5f), new Color(1f, 0.85f, 0.7f), 9f, 1.2f);
        return true;
    }

    // ---------------- nabytek ----------------

    static Transform Piece(Transform parent, string name, Vector3 position, float yaw)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, true);
        t.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        return t;
    }

    // Kvadr v lokalnich souradnicich kusu nabytku.
    static void Part(Transform piece, string name, Vector3 local, Vector3 size, Material material, float tiltX = 0f)
    {
        var rotation = piece.rotation * Quaternion.Euler(tiltX, 0f, 0f);
        MapBuildKit.Box(piece, name, piece.TransformPoint(local), size, rotation, material, false);
    }

    static void Collider(Transform piece, Vector3 center, Vector3 size)
    {
        var c = piece.gameObject.AddComponent<BoxCollider>();
        c.center = center;
        c.size = size;
    }

    // Horcicove sametove kreslo s drevenymi podrucnimi (sedi se smerem +z lokalne).
    static void Armchair(Transform parent, Vector3 position, float yaw, Mats m)
    {
        var p = Piece(parent, "Kreslo", position, yaw);
        foreach (float x in new[] { -0.4f, 0.4f })
        {
            Part(p, "Podrucka", new Vector3(x, 0.32f, 0f), new Vector3(0.09f, 0.64f, 0.82f), m.darkWood);
            Part(p, "PodruckaVrch", new Vector3(x, 0.66f, 0.02f), new Vector3(0.12f, 0.05f, 0.86f), m.darkWood);
        }
        Part(p, "Sedak", new Vector3(0f, 0.32f, 0.03f), new Vector3(0.7f, 0.2f, 0.72f), m.mustard);
        Part(p, "Spodek", new Vector3(0f, 0.15f, 0.03f), new Vector3(0.72f, 0.16f, 0.74f), m.mustard);
        Part(p, "Operadlo", new Vector3(0f, 0.68f, -0.32f), new Vector3(0.7f, 0.62f, 0.16f), m.mustard, -10f);
        Collider(p, new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.86f));
    }

    static void Footstool(Transform parent, Vector3 position, Mats m)
    {
        var p = Piece(parent, "Taburet", position, 0f);
        Part(p, "Ram", new Vector3(0f, 0.17f, 0f), new Vector3(0.66f, 0.26f, 0.56f), m.darkWood);
        Part(p, "Polstar", new Vector3(0f, 0.36f, 0f), new Vector3(0.62f, 0.14f, 0.52f), m.mustard);
        Collider(p, new Vector3(0f, 0.22f, 0f), new Vector3(0.66f, 0.44f, 0.56f));
    }

    static void CoffeeTable(Transform parent, Vector3 position, float yaw, Mats m)
    {
        var p = Piece(parent, "KonferencniStolek", position, yaw);
        Part(p, "Deska", new Vector3(0f, 0.47f, 0f), new Vector3(1.5f, 0.06f, 0.85f), m.wood);
        Part(p, "Luby", new Vector3(0f, 0.38f, 0f), new Vector3(1.4f, 0.12f, 0.75f), m.wood);
        foreach (float x in new[] { -0.66f, 0.66f })
            foreach (float z in new[] { -0.34f, 0.34f })
                Part(p, "Noha", new Vector3(x, 0.2f, z), new Vector3(0.08f, 0.4f, 0.08f), m.wood);
        Part(p, "Decka", new Vector3(0f, 0.505f, 0f), new Vector3(1.0f, 0.005f, 0.6f), m.white);
        Collider(p, new Vector3(0f, 0.25f, 0f), new Vector3(1.5f, 0.5f, 0.85f));
    }

    // Dil smetanove sedacky delky 'length' (sedi se smerem +z lokalne).
    static void Sofa(Transform parent, Vector3 position, float length, float yaw, Mats m)
    {
        var p = Piece(parent, "Sedacka", position, yaw);
        Part(p, "Spodek", new Vector3(0f, 0.2f, 0f), new Vector3(length, 0.3f, 0.95f), m.cream);
        int seats = Mathf.Max(1, Mathf.RoundToInt(length / 0.9f));
        float seatWidth = length / seats;
        for (int i = 0; i < seats; i++)
        {
            float x = -length * 0.5f + seatWidth * (i + 0.5f);
            Part(p, "Sedak", new Vector3(x, 0.42f, 0.08f), new Vector3(seatWidth - 0.03f, 0.16f, 0.78f), m.cream);
            Part(p, "Operadlo", new Vector3(x, 0.72f, -0.36f), new Vector3(seatWidth - 0.03f, 0.5f, 0.22f), m.cream, -8f);
            Part(p, "Oprerka", new Vector3(x, 1.0f, -0.42f), new Vector3(seatWidth - 0.12f, 0.12f, 0.14f), m.cream);
        }
        Collider(p, new Vector3(0f, 0.5f, 0f), new Vector3(length, 1.0f, 0.95f));
    }

    // Velka televize (85") na nizke skrince s knihami; na obrazovce bezi video ve smycce. Zvuk je prostorovy
    // a tichy - slyset je jen v obyvaku u televize.
    static void TvStand(Transform parent, Vector3 position, float yaw, Mats m)
    {
        const float standWidth = 2.1f, screenWidth = 1.95f, screenHeight = 1.12f, screenY = 1.33f;
        var p = Piece(parent, "Televize", position, yaw);
        Part(p, "Skrinka", new Vector3(0f, 0.3f, 0f), new Vector3(standWidth, 0.6f, 0.45f), m.darkWood);
        var random = new System.Random(3);
        for (int shelf = 0; shelf < 2; shelf++)
        {
            float x = -standWidth * 0.5f + 0.08f;
            while (x < standWidth * 0.5f - 0.1f)
            {
                float w = 0.04f + (float)random.NextDouble() * 0.05f;
                float h = 0.18f + (float)random.NextDouble() * 0.08f;
                Part(p, "Kniha", new Vector3(x + w * 0.5f, 0.06f + shelf * 0.28f + h * 0.5f, 0.2f), new Vector3(w - 0.005f, h, 0.2f), m.books[random.Next(m.books.Length)]);
                x += w;
            }
        }
        Part(p, "Stojan", new Vector3(0f, 0.63f, 0f), new Vector3(0.5f, 0.04f, 0.28f), m.black);
        Part(p, "Noha", new Vector3(0f, 0.72f, -0.03f), new Vector3(0.08f, 0.18f, 0.05f), m.black);
        Part(p, "Obrazovka", new Vector3(0f, screenY, -0.02f), new Vector3(screenWidth + 0.05f, screenHeight + 0.05f, 0.06f), m.black);

        // Displej: ctverec s UV 0-1 (video pres celou plochu), tesne pred cernym ramem.
        float hw = screenWidth * 0.5f - 0.02f, hh = screenHeight * 0.5f - 0.02f;
        var corners = new[]
        {
            p.TransformPoint(new Vector3(-hw, screenY - hh, 0.0125f)), p.TransformPoint(new Vector3(hw, screenY - hh, 0.0125f)),
            p.TransformPoint(new Vector3(hw, screenY + hh, 0.0125f)), p.TransformPoint(new Vector3(-hw, screenY + hh, 0.0125f)),
        };
        // (dívame se na displej zepredu, tj. proti lokalni +z: zleva doprava jde lokalni -x, proto prohozene u)
        var uv = new[] { new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        var display = MapBuildKit.MeshObject(p, "Displej", MapBuildKit.FlatMesh("TvDisplej", corners, uv), Vector3.zero, Quaternion.identity, Vector3.one, m.screen, false);
        GameObjectUtility.SetStaticEditorFlags(display, 0);

        var player = display.AddComponent<VideoPlayer>();
        player.source = VideoSource.VideoClip;
        player.clip = m.video;
        player.isLooping = true;
        player.playOnAwake = true;
        player.waitForFirstFrame = true;
        player.skipOnDrop = true;
        player.renderMode = VideoRenderMode.MaterialOverride;
        player.targetMaterialRenderer = display.GetComponent<MeshRenderer>();
        player.targetMaterialProperty = "_BaseMap";
        if (m.video.audioTrackCount > 0)
        {
            var speaker = display.AddComponent<AudioSource>();
            speaker.playOnAwake = false;
            speaker.spatialBlend = 1f;
            speaker.rolloffMode = AudioRolloffMode.Linear;
            speaker.minDistance = 2f;
            speaker.maxDistance = 12f;
            speaker.volume = 0.45f;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.controlledAudioTrackCount = 1;
            player.EnableAudioTrack(0, true);
            player.SetTargetAudioSource(0, speaker);
        }
        else
        {
            player.audioOutputMode = VideoAudioOutputMode.None;
        }

        Collider(p, new Vector3(0f, 0.3f, 0f), new Vector3(standWidth, 0.6f, 0.45f));
    }

    // Jidelni stul 2,6 x 1,15 m se sesti zidlemi (dlouha strana podel lokalni osy x).
    static void DiningTable(Transform parent, Vector3 position, float yaw, Mats m)
    {
        const float length = 2.6f, width = 1.15f;
        var p = Piece(parent, "JidelniStul", position, yaw);
        Part(p, "Deska", new Vector3(0f, 0.74f, 0f), new Vector3(length, 0.05f, width), m.darkWood);
        Part(p, "Ubrus", new Vector3(0f, 0.77f, 0f), new Vector3(length + 0.1f, 0.01f, width + 0.1f), m.white);
        Part(p, "UbrusBok", new Vector3(0f, 0.68f, (width + 0.1f) * 0.5f), new Vector3(length + 0.1f, 0.18f, 0.01f), m.white);
        Part(p, "UbrusBok", new Vector3(0f, 0.68f, -(width + 0.1f) * 0.5f), new Vector3(length + 0.1f, 0.18f, 0.01f), m.white);
        foreach (float x in new[] { -(length * 0.5f - 0.1f), length * 0.5f - 0.1f })
            foreach (float z in new[] { -(width * 0.5f - 0.08f), width * 0.5f - 0.08f })
                Part(p, "Noha", new Vector3(x, 0.36f, z), new Vector3(0.07f, 0.72f, 0.07f), m.darkWood);
        Collider(p, new Vector3(0f, 0.38f, 0f), new Vector3(length, 0.76f, width));

        foreach (float x in new[] { -0.85f, 0f, 0.85f })
        {
            Chair(parent, p.TransformPoint(new Vector3(x, 0f, -(width * 0.5f + 0.25f))), yaw, m);
            Chair(parent, p.TransformPoint(new Vector3(x, 0f, width * 0.5f + 0.25f)), yaw + 180f, m);
        }
    }

    // Velka stara skrin: soklik na nozkach, dve kridla s vystouplymi kazetami, mosazne knopky, profilovana rimsa
    // a nizky stitek nahore (sedi se/otevira se smerem +z lokalne).
    static void Wardrobe(Transform parent, Vector3 position, float yaw, Mats m)
    {
        var p = Piece(parent, "Skrin", position, yaw);
        const float w = 2.1f, d = 0.6f, front = d * 0.5f;

        foreach (float x in new[] { -w * 0.5f + 0.08f, w * 0.5f - 0.08f })
            foreach (float z in new[] { -front + 0.08f, front - 0.06f })
                Part(p, "Nozka", new Vector3(x, 0.05f, z), new Vector3(0.1f, 0.1f, 0.1f), m.oldWood);
        Part(p, "Sokl", new Vector3(0f, 0.17f, 0.01f), new Vector3(w + 0.06f, 0.14f, d + 0.06f), m.oldWood);
        Part(p, "Korpus", new Vector3(0f, 1.2f, 0f), new Vector3(w, 1.92f, d), m.oldWood);
        Part(p, "RimsaSpodni", new Vector3(0f, 2.19f, 0.02f), new Vector3(w + 0.08f, 0.06f, d + 0.08f), m.oldWood);
        Part(p, "Rimsa", new Vector3(0f, 2.26f, 0.04f), new Vector3(w + 0.18f, 0.1f, d + 0.16f), m.oldWood);
        Part(p, "Stitek", new Vector3(0f, 2.4f, front + 0.04f), new Vector3(1.1f, 0.18f, 0.05f), m.oldWood);
        Part(p, "StitekVrch", new Vector3(0f, 2.5f, front + 0.04f), new Vector3(0.6f, 0.06f, 0.06f), m.oldWood);

        // Dve kridla se dvema kazetami, mezi nimi lista, knopky u stredu.
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * w * 0.25f;
            Part(p, "Kridlo", new Vector3(x, 1.2f, front + 0.015f), new Vector3(w * 0.5f - 0.06f, 1.82f, 0.03f), m.oldWood);
            Part(p, "Kazeta", new Vector3(x, 1.62f, front + 0.04f), new Vector3(w * 0.5f - 0.3f, 0.78f, 0.025f), m.oldPanel);
            Part(p, "Kazeta", new Vector3(x, 0.72f, front + 0.04f), new Vector3(w * 0.5f - 0.3f, 0.66f, 0.025f), m.oldPanel);
            Part(p, "Knopka", new Vector3(side * 0.07f, 1.2f, front + 0.06f), new Vector3(0.04f, 0.06f, 0.04f), m.brass);
            Part(p, "Zamek", new Vector3(side * 0.07f, 1.3f, front + 0.035f), new Vector3(0.03f, 0.05f, 0.01f), m.brass);
        }
        Part(p, "Lista", new Vector3(0f, 1.2f, front + 0.035f), new Vector3(0.04f, 1.84f, 0.03f), m.oldWood);
        Collider(p, new Vector3(0f, 1.25f, 0.03f), new Vector3(w + 0.18f, 2.5f, d + 0.16f));
    }

    // Tmava drevena zidle se svetlou vyplni operadla (sedi se smerem +z lokalne).
    static void Chair(Transform parent, Vector3 position, float yaw, Mats m)
    {
        var p = Piece(parent, "Zidle", position, yaw);
        Part(p, "Sedak", new Vector3(0f, 0.46f, 0f), new Vector3(0.45f, 0.05f, 0.45f), m.darkWood);
        foreach (float x in new[] { -0.19f, 0.19f })
        {
            Part(p, "Noha", new Vector3(x, 0.23f, 0.19f), new Vector3(0.05f, 0.46f, 0.05f), m.darkWood);
            Part(p, "NohaZadni", new Vector3(x, 0.5f, -0.2f), new Vector3(0.05f, 1.0f, 0.05f), m.darkWood);
        }
        Part(p, "Operadlo", new Vector3(0f, 0.78f, -0.2f), new Vector3(0.36f, 0.4f, 0.03f), m.beige);
        Collider(p, new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.9f, 0.45f));
    }

    static void FloorLamp(Transform parent, Vector3 position, Mats m)
    {
        var p = Piece(parent, "StojaciLampa", position, 0f);
        Part(p, "Podstavec", new Vector3(0f, 0.02f, 0f), new Vector3(0.3f, 0.04f, 0.3f), m.darkWood);
        MapBuildKit.Beam(p, "Tyc", position, position + Vector3.up * 1.45f, 0.04f, m.darkWood);
        MapBuildKit.MeshObject(p, "Stinitko", MapBuildKit.ConeMesh(), position + Vector3.up * 1.3f, Quaternion.identity, new Vector3(0.42f, 0.32f, 0.42f), m.shade);
        AddLight(p, "Svetlo", position + Vector3.up * 1.25f, new Color(1f, 0.75f, 0.45f), 5f, 1.2f);
    }

    static void Vase(Transform parent, Vector3 position, Mats m)
    {
        var blob = MapBuildKit.BlobMesh(1);
        MapBuildKit.MeshObject(parent, "Vaza", blob, position + Vector3.up * 0.17f, Quaternion.identity, new Vector3(0.11f, 0.18f, 0.11f), m.white);
        var random = new System.Random(9);
        for (int i = 0; i < 7; i++)
        {
            var dir = new Vector3((float)random.NextDouble() - 0.5f, 1.4f, (float)random.NextDouble() - 0.5f).normalized;
            Vector3 top = position + Vector3.up * 0.3f + dir * (0.35f + (float)random.NextDouble() * 0.15f);
            MapBuildKit.Beam(parent, "Stonek", position + Vector3.up * 0.3f, top, 0.012f, m.bark);
            MapBuildKit.MeshObject(parent, "Kvet", blob, top, Quaternion.identity, Vector3.one * 0.05f, m.dried, false);
        }
    }

    static void Picture(Transform parent, Vector3 position, float yaw, float width, float height, Material art, Mats m)
    {
        var p = Piece(parent, "Obraz", position, yaw);
        Part(p, "Ram", new Vector3(0f, 0f, 0f), new Vector3(width + 0.08f, height + 0.08f, 0.03f), m.black);
        Part(p, "Plátno", new Vector3(0f, 0f, 0.016f), new Vector3(width, height, 0.004f), art);
    }

    // Fotka v tmavem dreveném ramu; obraz je ctverec s UV 0-1, aby fotka nebyla oriznuta ani opakovana.
    static void Photo(Transform parent, Vector3 position, float yaw, float width, float height, Material photo, Mats m)
    {
        var p = Piece(parent, "Fotka", position, yaw);
        const float frame = 0.07f;
        Part(p, "Ram", new Vector3(0f, height * 0.5f + frame * 0.5f, 0.02f), new Vector3(width + 2f * frame, frame, 0.05f), m.darkWood);
        Part(p, "Ram", new Vector3(0f, -height * 0.5f - frame * 0.5f, 0.02f), new Vector3(width + 2f * frame, frame, 0.05f), m.darkWood);
        Part(p, "Ram", new Vector3(width * 0.5f + frame * 0.5f, 0f, 0.02f), new Vector3(frame, height, 0.05f), m.darkWood);
        Part(p, "Ram", new Vector3(-width * 0.5f - frame * 0.5f, 0f, 0.02f), new Vector3(frame, height, 0.05f), m.darkWood);
        Part(p, "Podklad", new Vector3(0f, 0f, 0.005f), new Vector3(width, height, 0.01f), m.black);

        float hw = width * 0.5f, hh = height * 0.5f;
        var corners = new[]
        {
            p.TransformPoint(new Vector3(-hw, -hh, 0.012f)), p.TransformPoint(new Vector3(hw, -hh, 0.012f)),
            p.TransformPoint(new Vector3(hw, hh, 0.012f)), p.TransformPoint(new Vector3(-hw, hh, 0.012f)),
        };
        // Divame se proti lokalni +z, takze zleva doprava jde lokalni -x (u je prohozene).
        var uv = new[] { new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        MapBuildKit.MeshObject(p, "Obraz", MapBuildKit.FlatMesh("Fotka", corners, uv), Vector3.zero, Quaternion.identity, Vector3.one, photo, false);
    }

    static void Rug(Transform parent, Vector3 center, float width, float depth, Material material)
    {
        var points = new[]
        {
            center + new Vector3(-width * 0.5f, 0f, -depth * 0.5f), center + new Vector3(width * 0.5f, 0f, -depth * 0.5f),
            center + new Vector3(width * 0.5f, 0f, depth * 0.5f), center + new Vector3(-width * 0.5f, 0f, depth * 0.5f),
        };
        var uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        MapBuildKit.MeshObject(parent, "Koberec", MapBuildKit.FlatMesh("Koberec", points, uv), Vector3.zero, Quaternion.identity, Vector3.one, material, false);
    }

    static Light AddLight(Transform parent, string name, Vector3 position, Color color, float range, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = position;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        return light;
    }
}
