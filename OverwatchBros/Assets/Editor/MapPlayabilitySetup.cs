using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Hratelnost mapy Domasov podle 2D navrhu (vic jako mapy z Overwatche, mapa zustava stejna):
// kryty, obri tuje, body a spawny pro utok a obranu, balicky, voda se skluzavkou, terasa male chaty, zadni vstup...
//
// Stavi se PO CASTECH a kazda cast jen jednou (znacka v Map-Domasov/Hratelnost/_Postaveno). Co pak rucne smazes
// nebo upravis, skript uz nevraci. Znovu se postavi jen cast, ktere se zvysi verze v seznamu Features.
[InitializeOnLoad]
public static class MapPlayabilitySetup
{
    const string RootName = "Hratelnost";
    const string MarkersName = "_Postaveno";
    const string MatDir = "Assets/Materials/Hratelnost/";
    static int retries;
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static GameObject logModel;

    // Casti mapy a jejich verze. Zvyseni verze = ta cast se smaze a postavi znovu (ostatni zustanou, jak jsou).
    static readonly (string name, int version)[] Features =
    {
        ("Kryty", 1),              // drevniky, seno u tunelu, traktor, susak, kompost, sklenik
        ("Kulna", 2),              // v2: delsi stranou podel cesty, dvere k ceste
        ("BezLatovani", 2),        // latovani (stity) na rozhledne se odstrani a uz se nestavi
        ("Body", 1),               // body A, B, C
        ("Spawny", 1),             // spawny utocniku a obrancu, zakladna tymu 1 pro deathmatch
        ("ObriTuje", 1),
        ("VstupTunelu", 1),        // stromy nad severnim vstupem do tunelu
        ("Voda", 4),               // v3: plynule koryto, rychla jizda z toboganu; v4: tekouci voda (vlnky po proudu)
        ("Terasa", 1),             // blok stromu a kamenna zidka na okraji terasy male chaty
        ("Gril", 1),
        ("ZadniVstup", 1),         // zadni dvere do male chaty s rampou
        ("TravnikNadTunelem", 1),
        ("KolizeRozhledny", 1),    // kolize vzper a zabradli rozhledny
        ("Balicky", 1),
        ("OknaKulny", 1),          // okna do stavajici kulny (kulna zustava, kde ji ma uzivatel)
        ("DedovaSlivovice", 2),    // balicek nesmrtelnosti ve spizi velke chaty (v2: v rohu, neni videt z chodby)
        ("Kotel", 1),
        ("LekarnickaNaZachode", 1), // na kazdem zachodu (objekt "Zachod") lekarnicka za 50 HP
        ("BezBalicku", 1),         // balicky rozmistene po mape se odstrani (zustava dedova slivovice a Tramal)
        ("VyssiStropy", 1),        // velka chata: strop prizemi i 1. patra o 0,5 m vys (vse nad nimi se posune)
        ("OpravaSchodisteChaty", 1), // po zvyseni stropu: dolni rameno schodiste az k odpocivadlu, prujezd volny
        ("ZebrikRozhledny", 2),    // kamenna rampa na rozhlednu (sever) se vypne, misto ni zebrik; v2: soucasti rozhledny
        ("SchodisteRozhledny", 5), // druhy pristup na rozhlednu: schodiste z jihu (od bodu C) na nizsi plosinu; v2: zastresene; v3: i bocni steny; v4: hladka rampa, vyssi strecha; v5: soucasti rozhledny
              // kotel na piliny v kotelne velke chaty: packa (R) prehreje horni patra
        ("StartToboganu", 1),      // jizda z toboganu jen pro toho, kdo vyjde na plosinu (skluzavka zustava, kde je)
    };

    static MapPlayabilitySetup()
    {
        EditorApplication.delayCall += Run;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += Run;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Run;
        };
    }

    [MenuItem("BrosOverwatch/Mapa/Hratelnost - postavit chybejici casti")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var map = GameObject.Find("Map-Domasov");
        if (map == null) return;

        var root = map.transform.Find(RootName);
        if (root != null && AllBuilt(root)) return;
        if (!LoadMaterials())
        {
            if (retries++ < 30) EditorApplication.delayCall += Run;
            return;
        }

        if (root == null) root = MapBuildKit.Group(map.transform, RootName);
        MigrateOldScene(root);

        var built = new List<string>();
        foreach (var (name, version) in Features)
        {
            if (Has(root, name, version)) continue;
            Physics.SyncTransforms();
            BuildFeature(name, map.transform, root);
            Mark(root, name, version);
            built.Add(name);
        }

        EditorSceneManager.MarkSceneDirty(map.scene);
        Debug.Log($"[Hratelnost] Postaveno: {string.Join(", ", built)}. Ostatni casti zustaly beze zmeny. Uloz scenu (Ctrl+S).");
    }

    static void BuildFeature(string name, Transform map, Transform root)
    {
        var covers = MapBuildKit.Group(root, "Kryty");
        var extra = MapBuildKit.Group(root, "Doplnky");
        switch (name)
        {
            case "Kryty":
                Woodpile(covers, logModel, new Vector3(-1f, 0f, 5f), 15f);
                Woodpile(covers, logModel, new Vector3(-3f, 0f, -3f), -20f);
                HayAroundTunnel(covers);   // baliky sena kolem noveho vstupu do tunelu u paty plosiny
                Tractor(covers, new Vector3(-6f, 0f, -22f), 25f);
                ClothesLine(covers, new Vector3(12f, 0f, 18f), -35f);
                Compost(covers, new Vector3(30f, 0f, 16f), 0f);
                Greenhouse(covers, new Vector3(13f, 0f, 60f), 0f);
                break;
            case "Kulna":
                DeleteNamed(root, "Kulna");
                Shed(covers, new Vector3(-10f, 0f, 22f), 90f);   // delsi strana podel cesty, dvere k ceste (vychod)
                break;
            case "BezLatovani":
                DeleteNamed(root, "LatovaniRozhledny");
                break;
            case "Body":
                MovePoints(map);
                break;
            case "Spawny":
                AttackSpawns(map, root);
                MoveDeathmatchBase(map, root);
                break;
            case "ObriTuje":
                GiantThujas(MapBuildKit.Group(root, "ObriTuje"));
                MoreThujas(MapBuildKit.Group(root, "ObriTuje"));
                break;
            case "VstupTunelu":
                ClearTunnelEntrance(map);
                break;
            case "Voda":
                foreach (var old in new[] { "Potok", "Jezirko", "Skluzavka", "ProudVody", "ProudPotoka" })
                    DeleteNamed(extra, old);
                Stream(extra, map);
                SlideAndPond(extra, map);
                break;
            case "Terasa":
                TerraceEdge(extra);
                break;
            case "Gril":
                StoneGrill(extra);
                break;
            case "ZadniVstup":
                BackDoor(map, extra);
                break;
            case "TravnikNadTunelem":
                CutLawnOverTunnel(map, extra);
                break;
            case "KolizeRozhledny":
                TowerBeamCollision(map);
                break;
            case "Balicky":
                Pickups(MapBuildKit.Group(root, "Balicky"), root);
                break;
            case "OknaKulny":
                ShedWindows(root);
                break;
            case "OpravaSchodisteChaty":
                FixHouseStairs(map, extra);
                break;
            case "LekarnickaNaZachode":
                ToiletHealthPacks(map);
                break;
            case "BezBalicku":
                RemoveMapPickups(root);
                break;
            case "VyssiStropy":
                RaiseHouseCeilings(map);
                break;
            case "ZebrikRozhledny":
                // (drive v Hratelnost/Doplnky; ted pod rozhlednou, aby se s ni posouval)
                DeleteNamed(extra, "ZebrikRozhledny");
                if (map.Find("Rozhledna") != null) DeleteNamed(map.Find("Rozhledna"), "ZebrikRozhledny");
                TowerLadder(map, extra);
                break;
            case "SchodisteRozhledny":
                DeleteNamed(extra, "SchodisteRozhledny");
                if (map.Find("Rozhledna") != null) DeleteNamed(map.Find("Rozhledna"), "SchodisteRozhledny");
                TowerStairs(map, extra);
                break;
            case "Kotel":
                DeleteNamed(extra, "Kotel");
                BoilerRoom(extra);
                break;
            case "StartToboganu":
                SlideStart(extra);
                break;
            case "DedovaSlivovice":
            {
                // spiz vedle obyvaku (prizemi velke chaty, mezi obyvakem a kuchyni): jihovychodni roh -
                // ze dveri z chodby (zapadni stena, z -4,1 az -2,3) ho zakryva zed
                DeleteNamed(root, "Balicek_12_Invulnerable");
                var go = new GameObject("Balicek_12_Invulnerable");
                go.transform.SetParent(MapBuildKit.Group(root, "Balicky"), false);
                go.transform.position = new Vector3(26.05f, Ground(new Vector3(26.05f, 0f, -6.05f), 2.5f, root) + 0.02f, -6.05f);
                go.AddComponent<PickupSpot>().kind = PickupKind.Invulnerable;
                break;
            }
        }
    }

    // ---------------- znacky postavenych casti ----------------

    static bool AllBuilt(Transform root)
    {
        if (root.Find(MarkersName) == null) return false;
        foreach (var (name, version) in Features)
            if (!Has(root, name, version)) return false;
        return true;
    }

    static bool Has(Transform root, string name, int version)
    {
        var markers = root.Find(MarkersName);
        if (markers == null) return false;
        foreach (Transform m in markers)
        {
            int dash = m.name.LastIndexOf("_v");
            if (dash > 0 && m.name.Substring(0, dash) == name && int.TryParse(m.name.Substring(dash + 2), out int v) && v >= version)
                return true;
        }
        return false;
    }

    static void Mark(Transform root, string name, int version)
    {
        var markers = MapBuildKit.Group(root, MarkersName);
        for (int i = markers.childCount - 1; i >= 0; i--)
            if (markers.GetChild(i).name.StartsWith(name + "_v"))
                UnityEngine.Object.DestroyImmediate(markers.GetChild(i).gameObject);
        new GameObject($"{name}_v{version}").transform.SetParent(markers, false);
    }

    // Scena postavena drivejsi verzi skriptu (vse naraz, znacka "_vN"): vsechny casti uz existuji - oznacit je
    // jako postavene ve verzi 1, at se nic nestavi znovu (a nevraci se, co bylo rucne smazano).
    static void MigrateOldScene(Transform root)
    {
        if (root.Find(MarkersName) != null) return;
        bool old = false;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var child = root.GetChild(i);
            if (child.name.StartsWith("_v") && child.childCount == 0)
            {
                old = true;
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }
        if (!old) return;
        foreach (var (name, _) in Features)
            Mark(root, name, 1);
    }

    static void DeleteNamed(Transform parent, string name)
    {
        foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            if (t != null && t != parent && t.name == name)
                UnityEngine.Object.DestroyImmediate(t.gameObject);
    }

    static bool LoadMaterials()
    {
        mats.Clear();
        foreach (var (key, path) in new[]
        {
            ("boards", "Assets/Materials/House/DarkBoards.mat"), ("roof", "Assets/Materials/House/RoofTiles.mat"),
            ("iron", "Assets/Materials/Garden/Iron.mat"), ("planks", "Assets/Materials/Wood/Wood_Planks.mat"),
            ("beam", "Assets/Materials/Wood/Wood_Beam.mat"), ("barrel", "Assets/Materials/House/Barrel.mat"),
        })
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) return false;
            mats[key] = m;
        }
        mats["hay"] = MapBuildKit.Mat(MatDir + "Seno.mat", null, new Color(0.86f, 0.74f, 0.38f), Vector2.one, 0.05f);
        mats["tractor"] = MapBuildKit.Mat(MatDir + "TraktorCerveny.mat", null, new Color(0.72f, 0.12f, 0.08f), Vector2.one, 0.45f);
        mats["tyre"] = MapBuildKit.Mat(MatDir + "Pneumatika.mat", null, new Color(0.08f, 0.08f, 0.09f), Vector2.one, 0.2f);
        mats["cabin"] = MapBuildKit.Mat(MatDir + "KabinaSklo.mat", null, new Color(0.18f, 0.24f, 0.28f), Vector2.one, 0.85f);
        mats["sheet"] = MapBuildKit.Mat(MatDir + "Plachta.mat", null, new Color(0.93f, 0.92f, 0.88f), Vector2.one, 0.05f, false, true);
        mats["frosted"] = MapBuildKit.Mat(MatDir + "MatneSklo.mat", null, new Color(0.78f, 0.86f, 0.82f), Vector2.one, 0.7f);
        mats["compost"] = MapBuildKit.Mat(MatDir + "Kompost.mat", null, new Color(0.25f, 0.18f, 0.11f), Vector2.one, 0.05f);
        mats["slide"] = MapBuildKit.Mat(MatDir + "SkluzavkaZelena.mat", null, new Color(0.15f, 0.62f, 0.22f), Vector2.one, 0.6f);
        mats["pebbles"] = MapBuildKit.Mat(MatDir + "Oblazky.mat", null, new Color(0.55f, 0.53f, 0.48f), Vector2.one, 0.15f);
        var water = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/House/Water.mat");
        var stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Stone/Stone_Wall.mat");
        if (water == null || stone == null) return false;
        mats["water"] = water;
        // reka a jezirko: textura vlnek, ktera za behu ujizdi po proudu (WaterFlow)
        mats["river"] = MapBuildKit.Mat(MatDir + "RekaVoda.mat", "Assets/Materials/Hratelnost/VodaVlny.png", Color.white,
            new Vector2(0.6f, 0.25f), 0.9f);
        if (mats["river"] == null) return false;
        mats["stone"] = stone;
        foreach (var m in mats.Values) if (m == null) return false;
        logModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Kenney/NatureKit/log_stackLarge.glb");
        return logModel != null;
    }

    // ---------------- pomocne ----------------

    // Vyska povrchu pod bodem (prvni zasah shora od 'fromY'; ignoruje vlastni kryty a triggery).
    static float Ground(Vector3 p, float fromY, Transform ignore = null)
    {
        Physics.SyncTransforms();
        var hits = Physics.RaycastAll(new Vector3(p.x, fromY, p.z), Vector3.down, fromY + 20f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (ignore != null && hit.collider.transform.IsChildOf(ignore)) continue;
            return hit.point.y;
        }
        return 0f;
    }

    static Vector3 OnGround(Vector3 p, float fromY = 3f) => new Vector3(p.x, Ground(p, fromY), p.z);

    static Transform Object(Transform parent, string name, Vector3 foot, float yaw, float fromY = 3f)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation(OnGround(foot, fromY), Quaternion.Euler(0f, yaw, 0f));
        return t;
    }

    // Kvadr v mistnich souradnicich objektu (stred, velikost).
    static GameObject Local(Transform obj, string name, Vector3 center, Vector3 size, Material m, bool collider, Quaternion? rot = null)
    {
        var r = obj.rotation * (rot ?? Quaternion.identity);
        return MapBuildKit.Box(obj, name, obj.TransformPoint(center), size, r, m, collider);
    }

    static GameObject Cylinder(Transform obj, string name, Vector3 center, float radius, float length, Quaternion localRot, Material m, bool collider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(obj, false);
        go.transform.localPosition = center;
        go.transform.localRotation = localRot;
        go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    // ---------------- kryty ----------------

    // Kulna na naradi: dlouha 7 m (lokalni osa x), dvere uprostred dlouhe steny (+z), okenko v protejsi dlouhe stene,
    // sedlova strecha s hrebenem podel delky. Dovnitr se da vejit - kryt i misto na schovani.
    static void Shed(Transform parent, Vector3 foot, float yaw)
    {
        var o = Object(parent, "Kulna", foot, yaw);
        const float w = 7f, d = 5f, h = 2.6f, t = 0.15f, door = 1.5f, doorH = 2.2f;
        var wood = mats["boards"];
        // kratke steny (stity)
        foreach (float x in new[] { -w * 0.5f, w * 0.5f })
        {
            Local(o, "Stena", new Vector3(x, h * 0.5f, 0f), new Vector3(t, h, d), wood, true);
            Local(o, "Stit", new Vector3(x, h + 0.25f, 0f), new Vector3(t, 0.5f, d * 0.6f), wood, false);
        }
        // dlouha stena se dvermi uprostred (+z)
        float side = (w - door) * 0.5f;
        foreach (float sx in new[] { -1f, 1f })
            Local(o, "Stena", new Vector3(sx * (door * 0.5f + side * 0.5f), h * 0.5f, d * 0.5f), new Vector3(side, h, t), wood, true);
        Local(o, "Nadprazi", new Vector3(0f, (doorH + h) * 0.5f, d * 0.5f), new Vector3(door, h - doorH, t), wood, true);
        Local(o, "Dvere", new Vector3(door * 0.5f + 0.1f, doorH * 0.5f, d * 0.5f + 0.55f), new Vector3(door * 0.9f, doorH, 0.05f), mats["planks"], false,
            Quaternion.Euler(0f, -70f, 0f));
        // protejsi dlouha stena s okenkem (0,9 x 0,6 m ve vysce 1,3 m)
        const float win = 0.9f, winLow = 1.3f, winHigh = 1.9f;
        float part = (w - win) * 0.5f;
        foreach (float sx in new[] { -1f, 1f })
            Local(o, "Stena", new Vector3(sx * (win * 0.5f + part * 0.5f), h * 0.5f, -d * 0.5f), new Vector3(part, h, t), wood, true);
        Local(o, "Parapet", new Vector3(0f, winLow * 0.5f, -d * 0.5f), new Vector3(win, winLow, t), wood, true);
        Local(o, "Nadprazi", new Vector3(0f, (winHigh + h) * 0.5f, -d * 0.5f), new Vector3(win, h - winHigh, t), wood, true);
        // strecha s hrebenem podel delky (x)
        foreach (float sz in new[] { -1f, 1f })
            Local(o, "Strecha", new Vector3(0f, 3.05f, sz * 1.32f), new Vector3(7.5f, 0.12f, 2.95f), mats["roof"], true,
                Quaternion.Euler(sz * 22f, 0f, 0f));
        // uvnitr: ponk a sud (kryt)
        Local(o, "Ponk", new Vector3(-w * 0.5f + 1.6f, 0.45f, -d * 0.5f + 0.5f), new Vector3(2.5f, 0.9f, 0.7f), mats["planks"], true);
        Cylinder(o, "Sud", new Vector3(w * 0.5f - 0.7f, 0.5f, -d * 0.5f + 0.6f), 0.35f, 1f, Quaternion.identity, mats["barrel"], true);
    }

    // Okna do stavajici kulny (necha ji, kde je, i s rucnimi upravami): v obou stitovych stenach jedno,
    // v dlouhe stene se dvermi po jednom na kazde strane. Stena se nahradi dily kolem otvoru, kolem je ram.
    static void ShedWindows(Transform root)
    {
        foreach (var shed in root.GetComponentsInChildren<Transform>(true))
        {
            if (shed.name != "Kulna") continue;
            var walls = new List<MeshFilter>();
            foreach (Transform child in shed)
                if (child.name == "Stena" && child.GetComponent<MeshFilter>() != null)
                    walls.Add(child.GetComponent<MeshFilter>());
            foreach (var wall in walls)
            {
                if (wall.sharedMesh == null) continue;
                Vector3 local = shed.InverseTransformPoint(wall.transform.position);
                Vector3 size = wall.sharedMesh.bounds.size;
                bool gable = Mathf.Abs(local.x) > 3f && size.z > 4f;          // kratka stena (stit)
                bool doorSide = local.z > 2f && size.x > 2f && size.x < 3.5f;  // dily dlouhe steny vedle dveri
                if (gable || doorSide)
                    CutWindow(wall, 0.9f, 1.3f, 1.9f);
            }
        }
    }

    // Vyrize okno doprostred kvadru steny (sirka podel delsi vodorovne osy, vyska od 'low' do 'high' nad spodkem).
    static void CutWindow(MeshFilter wall, float width, float low, float high)
    {
        Vector3 size = wall.sharedMesh.bounds.size;
        bool alongX = size.x >= size.z;
        float length = alongX ? size.x : size.z, thick = alongX ? size.z : size.x, h = size.y;
        if (length < width + 0.3f) return;
        var t = wall.transform;
        var material = wall.GetComponent<MeshRenderer>() != null ? wall.GetComponent<MeshRenderer>().sharedMaterial : mats["boards"];
        Vector3 Axis(float a) => alongX ? new Vector3(a, 0f, 0f) : new Vector3(0f, 0f, a);
        Vector3 Size(float a, float y) => alongX ? new Vector3(a, y, thick) : new Vector3(thick, y, a);
        float bottom = -h * 0.5f, side = (length - width) * 0.5f;

        void Piece(string name, Vector3 center, Vector3 s, bool collide, Material m)
            => MapBuildKit.Box(t.parent, name, t.TransformPoint(center), s, t.rotation, m, collide);

        foreach (float dir in new[] { -1f, 1f })
            Piece("Stena", Axis(dir * (width * 0.5f + side * 0.5f)), Size(side, h), true, material);
        Piece("Parapet", new Vector3(0f, bottom + low * 0.5f, 0f), Size(width, low), true, material);
        Piece("Nadprazi", new Vector3(0f, (bottom + high + h * 0.5f) * 0.5f, 0f), Size(width, h * 0.5f - bottom - high), true, material);
        // ram okna (bez kolize)
        foreach (float dir in new[] { -1f, 1f })
            Piece("Ram", Axis(dir * (width * 0.5f + 0.04f)) + new Vector3(0f, bottom + (low + high) * 0.5f, 0f),
                Size(0.08f, high - low + 0.16f) + (alongX ? new Vector3(0f, 0f, 0.06f) : new Vector3(0.06f, 0f, 0f)), false, mats["planks"]);
        foreach (float y in new[] { low - 0.04f, high + 0.04f })
            Piece("Ram", new Vector3(0f, bottom + y, 0f), Size(width + 0.16f, 0.08f) + (alongX ? new Vector3(0f, 0f, 0.06f) : new Vector3(0.06f, 0f, 0f)),
                false, mats["planks"]);
        UnityEngine.Object.DestroyImmediate(t.gameObject);
    }

    // Hranice dreva (Kenney log_stackLarge) v rade, do vysky asi 1,4 m - kryt v drepu, da se pres ni strilet.
    static void Woodpile(Transform parent, GameObject model, Vector3 foot, float yaw)
    {
        var o = Object(parent, "Drevnik", foot, yaw);
        float x = -2.6f;
        for (int i = 0; i < 3; i++)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, o);
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            var b = Bounds(inst);
            float scale = 1.4f / Mathf.Max(0.01f, b.size.y);
            inst.transform.localScale = Vector3.one * scale;
            b = Bounds(inst);
            inst.transform.position += o.TransformPoint(new Vector3(x, 0f, 0f)) - new Vector3(b.center.x, b.min.y, b.center.z);
            x += 2.6f;
        }
        var box = o.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.7f, 0f);
        box.size = new Vector3(7.4f, 1.4f, 1.6f);
        // strisecka
        Local(o, "Striska", new Vector3(0f, 1.75f, 0f), new Vector3(7.8f, 0.08f, 1.9f), mats["planks"], false, Quaternion.Euler(8f, 0f, 0f));
        foreach (float px in new[] { -3.7f, 3.7f })
            foreach (float pz in new[] { -0.8f, 0.8f })
                Local(o, "Sloupek", new Vector3(px, 0.85f, pz), new Vector3(0.12f, 1.7f, 0.12f), mats["beam"], false);
    }

    static Bounds Bounds(GameObject root)
    {
        var rs = root.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // Baliky sena: 'count' valcu vedle sebe (podel lokalni osy x), pripadne dalsi navrchu.
    static void HayBales(Transform parent, Vector3 foot, float yaw, int count = 3, int onTop = 1)
    {
        var o = Object(parent, "BalikySena", foot, yaw);
        var lying = Quaternion.Euler(0f, 0f, 90f);
        float start = -(count - 1) * 0.85f;
        for (int i = 0; i < count; i++)
            Cylinder(o, "Balik", new Vector3(start + i * 1.7f, 0.8f, 0f), 0.8f, 1.5f, lying, mats["hay"], true);
        for (int i = 0; i < onTop && i < count - 1; i++)
            Cylinder(o, "Balik", new Vector3(start + 0.85f + i * 1.7f, 2.35f, 0f), 0.8f, 1.5f, lying, mats["hay"], true);
    }

    // Seno kolem noveho vstupu do tunelu (rampa x -9,5 az -1,5, z 35 az 37, vchazi se ze zapadu): po bocich
    // a za hlubokym koncem stohy, vchod zustava volny. Zastupuje cervene zabradli.
    static void HayAroundTunnel(Transform parent)
    {
        HayBales(parent, new Vector3(-6.5f, 0f, 38.4f), 0f, 3, 1);     // sever
        HayBales(parent, new Vector3(-3.2f, 0f, 38.6f), 0f, 1, 0);
        HayBales(parent, new Vector3(-6.5f, 0f, 33.6f), 0f, 3, 2);     // jih
        HayBales(parent, new Vector3(-0.2f, 0f, 36f), 90f, 2, 1);      // za hlubokym koncem
        HayBales(parent, new Vector3(-11.6f, 0f, 38.2f), 30f, 1, 0);   // u vchodu (volny prostor mezi nimi)
        HayBales(parent, new Vector3(-11.8f, 0f, 33.6f), -25f, 2, 0);
    }

    // Traktor s vlekem: kapota, kabina, velka zadni a mala predni kola, prkenny vlek.
    static void Tractor(Transform parent, Vector3 foot, float yaw)
    {
        var o = Object(parent, "TraktorSVlekem", foot, yaw);
        Local(o, "Kapota", new Vector3(1.3f, 1.05f, 0f), new Vector3(2.2f, 1.1f, 1.1f), mats["tractor"], true);
        Local(o, "Kabina", new Vector3(-0.3f, 1.75f, 0f), new Vector3(1.3f, 1.5f, 1.4f), mats["cabin"], true);
        Local(o, "Strecha", new Vector3(-0.3f, 2.55f, 0f), new Vector3(1.5f, 0.1f, 1.6f), mats["tractor"], false);
        var axle = Quaternion.Euler(90f, 0f, 0f);
        foreach (float side in new[] { -1f, 1f })
        {
            Cylinder(o, "ZadniKolo", new Vector3(-0.4f, 0.75f, side * 0.95f), 0.75f, 0.5f, axle, mats["tyre"], false);
            Cylinder(o, "PredniKolo", new Vector3(1.9f, 0.45f, side * 0.75f), 0.45f, 0.35f, axle, mats["tyre"], false);
        }
        Local(o, "Vlek", new Vector3(-3.4f, 1.1f, 0f), new Vector3(3.2f, 1.2f, 2f), mats["planks"], true);
        foreach (float side in new[] { -1f, 1f })
            Cylinder(o, "KoloVleku", new Vector3(-3.4f, 0.4f, side * 1.05f), 0.4f, 0.25f, axle, mats["tyre"], false);
        Local(o, "Oj", new Vector3(-1.5f, 0.55f, 0f), new Vector3(1f, 0.1f, 0.1f), mats["iron"], false);
    }

    // Susak s plachtami: blokuje vyhled, ne strely (plachty bez kolize).
    static void ClothesLine(Transform parent, Vector3 foot, float yaw)
    {
        var o = Object(parent, "SusakSPlachtami", foot, yaw);
        foreach (float x in new[] { -3f, 3f })
            Local(o, "Sloup", new Vector3(x, 1.1f, 0f), new Vector3(0.1f, 2.2f, 0.1f), mats["iron"], true);
        Local(o, "Snura", new Vector3(0f, 2.1f, 0f), new Vector3(6f, 0.02f, 0.02f), mats["iron"], false);
        for (int i = 0; i < 3; i++)
            Local(o, "Plachta", new Vector3(-1.9f + i * 1.9f, 1.35f, 0f), new Vector3(1.7f, 1.5f, 0.02f), mats["sheet"], false,
                Quaternion.Euler(0f, 0f, i == 1 ? 2f : -2f));
    }

    // Kompost z prken a dva sudy.
    static void Compost(Transform parent, Vector3 foot, float yaw)
    {
        var o = Object(parent, "KompostASudy", foot, yaw);
        Local(o, "Kompost", new Vector3(0f, 0.6f, 0f), new Vector3(2.2f, 1.2f, 1.6f), mats["planks"], true);
        Local(o, "Hlina", new Vector3(0f, 1.15f, 0f), new Vector3(2f, 0.12f, 1.4f), mats["compost"], false);
        Cylinder(o, "Sud", new Vector3(1.8f, 0.5f, 0.4f), 0.35f, 1f, Quaternion.identity, mats["barrel"], true);
        Cylinder(o, "Sud", new Vector3(1.9f, 0.5f, -0.45f), 0.35f, 1f, Quaternion.identity, mats["barrel"], true);
    }

    // Sklenik s matnym sklem: prerusi vyhled z plosiny rozhledny na sever-vychod.
    static void Greenhouse(Transform parent, Vector3 foot, float yaw)
    {
        var o = Object(parent, "Sklenik", foot, yaw);
        Local(o, "SteniJih", new Vector3(0f, 1.1f, -2f), new Vector3(8f, 2.2f, 0.04f), mats["frosted"], true);
        Local(o, "StenaSever", new Vector3(0f, 1.1f, 2f), new Vector3(8f, 2.2f, 0.04f), mats["frosted"], true);
        Local(o, "StenaZapad", new Vector3(-4f, 1.1f, 0f), new Vector3(0.04f, 2.2f, 4f), mats["frosted"], true);
        Local(o, "StenaVychod", new Vector3(4f, 1.1f, 0f), new Vector3(0.04f, 2.2f, 4f), mats["frosted"], true);
        foreach (float side in new[] { -1f, 1f })
            Local(o, "Strecha", new Vector3(0f, 2.55f, side * 1.05f), new Vector3(8.1f, 0.04f, 2.3f), mats["frosted"], true,
                Quaternion.Euler(side * 20f, 0f, 0f));
        for (int i = 0; i <= 4; i++)
        {
            float x = -4f + i * 2f;
            foreach (float z in new[] { -2f, 2f })
                Local(o, "Ram", new Vector3(x, 1.1f, z), new Vector3(0.06f, 2.2f, 0.06f), mats["iron"], false);
        }
    }

    // Body pro utok a obranu (hra si obdelnik posadi na povrch pod znackou; velikost = Scale X, Z):
    // A = obyvak velke chaty (prizemi s pruraznymi okny), B = prizemi male chaty, C = plosina pred rozhlednou.
    static void MovePoints(Transform map)
    {
        var list = new (string name, Vector3 position, Vector3 size)[]
        {
            ("CapturePoint_1", new Vector3(18f, 2f, 5.5f), new Vector3(9f, 1f, 8f)),
            ("CapturePoint_2", new Vector3(-37.3f, 4.6f, -1.8f), new Vector3(12f, 1f, 12f)),
            ("CapturePoint_3", new Vector3(-13.5f, 8f, 47f), new Vector3(14f, 1f, 12f)),
        };
        foreach (var (name, position, size) in list)
        {
            var point = map.Find(name);
            if (point == null)
            {
                point = new GameObject(name).transform;
                point.SetParent(map, false);
            }
            point.position = position;
            point.rotation = Quaternion.identity;
            point.localScale = size;
        }
    }

    // Spawny utocniku a obrancu pro body A, B, C: kazdy tym 33-44 m od bodu, z opacne strany.
    // Obranci postupne ustupuji: od velke chaty k male chate a nakonec na kopec za rozhlednu.
    static void AttackSpawns(Transform map, Transform root)
    {
        var spawns = new (string name, float x, float z, float fromY)[]
        {
            ("SpawnPoint_Utok_A", 22.6f, -29.3f, 3f),    // jih u silnice
            ("SpawnPoint_Obrana_A", -14f, 33f, 3f),      // louka severne od kulny
            ("SpawnPoint_Utok_B", 2f, -12f, 3f),         // jih louky u velke chaty
            ("SpawnPoint_Obrana_B", -36f, 42f, 3f),      // sever za tujemi u male chaty
            ("SpawnPoint_Utok_C", -32f, 10f, 6f),        // terasa male chaty
            ("SpawnPoint_Obrana_C", -12f, 85f, 3f),      // zahrada za rozhlednou
        };
        foreach (var (name, x, z, fromY) in spawns)
        {
            var marker = map.Find(name);
            if (marker == null)
            {
                marker = new GameObject(name).transform;
                marker.SetParent(map, false);
            }
            marker.position = new Vector3(x, Ground(new Vector3(x, 0f, z), fromY, root), z);
        }
    }

    // Team deathmatch: zakladna tymu 1 byla v zahrade za novou hranici mapy (sever je kratsi).
    static void MoveDeathmatchBase(Transform map, Transform root)
    {
        var marker = map.Find("SpawnPoint_Team1");
        if (marker != null)
            marker.position = new Vector3(-42f, Ground(new Vector3(-42f, 0f, 80f), 3f, root), 80f);
    }

    // Stromy a kere, ktere by staly v novem otvoru severniho vstupu do tunelu.
    static void ClearTunnelEntrance(Transform map)
    {
        var hole = new Bounds(new Vector3(-30f, 0f, 85.5f), new Vector3(10f, 40f, 5f));
        foreach (var group in new[] { "KenneyDekorace", "Zahrada" })
        {
            var g = map.Find(group);
            if (g == null) continue;
            foreach (Transform child in g.GetComponentsInChildren<Transform>(true))
            {
                if (child == g || child.parent == g) continue;
                var r = child.GetComponent<Renderer>();
                if (r != null && hole.Contains(new Vector3(r.bounds.center.x, 0f, r.bounds.center.z)))
                    child.gameObject.SetActive(false);
            }
        }
    }

    // ---------------- potok, skluzavka, terasa, gril (podle nakresu) ----------------

    // Vodni cesta: od bazenu u velke chaty korytem pres silnici (brod), dal potokem na severozapad kolem
    // sklenika az za rozhlednu do jezirka. Proud v ni hrace nese (WaterCurrent). Vychodni rameno potoka je jen ozdoba.
    // Zbytek vodni cesty za skluzavkou (zacatek se dopocita podle bazenu, viz Ride).
    static readonly Vector3[] WaterRideTail =
    {
        new Vector3(26.6f, 0f, 47.5f), new Vector3(27.4f, 0f, 51.2f), new Vector3(24.8f, 0f, 57.2f),
        new Vector3(19.1f, 0f, 66.2f), new Vector3(15.5f, 0f, 73f), new Vector3(9f, 0f, 79f), new Vector3(4f, 0f, 82f),
    };

    const float SlideTop = 2.4f, SlideRun = 4.2f, SlideGap = 2.2f;

    // Bazen ve scene (OkoliChaty/Bazen): stred a polomer podle jeho skutecnych rozmeru (i kdyz ho nekdo posunul).
    static void Pool(Transform map, out Vector3 center, out float radius)
    {
        center = new Vector3(24.67f, 0f, 31.01f);
        radius = 2.4f;
        var pool = map.Find("OkoliChaty/Bazen");
        if (pool == null) return;
        var rs = pool.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) { center = pool.position; return; }
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        center = new Vector3(b.center.x, 0f, b.center.z);
        radius = Mathf.Max(b.extents.x, b.extents.z);
    }

    // Plosina skluzavky: severne od bazenu s odstupem; skluz miri na sever, konec skluzu = zacatek vody.
    static Vector3 SlideFoot(Transform map)
    {
        Pool(map, out Vector3 c, out float r);
        return c + new Vector3(0f, 0f, r + SlideGap);
    }

    static Vector3[] Ride(Transform map)
    {
        var list = new List<Vector3> { SlideFoot(map) + new Vector3(0f, 0f, 0.7f + SlideRun) };
        list.AddRange(WaterRideTail);
        return list.ToArray();
    }

    // Koryto jako jeden plynuly pas podel hladke krivky (zadne krizici se okraje v ohybech).
    static void Stream(Transform root, Transform map)
    {
        var g = MapBuildKit.Group(root, "Potok");
        var curve = Smooth(Ride(map));
        var water = MapBuildKit.MeshObject(g, "Voda", Ribbon("Voda", curve, -0.8f, 0.8f, 0.04f), Vector3.zero, Quaternion.identity, Vector3.one, mats["river"], false);
        water.AddComponent<WaterFlow>().speed = 1.8f;
        MapBuildKit.MeshObject(g, "Breh", Ribbon("BrehL", curve, -1.25f, -0.78f, 0.06f), Vector3.zero, Quaternion.identity, Vector3.one, mats["pebbles"], false);
        MapBuildKit.MeshObject(g, "Breh", Ribbon("BrehP", curve, 0.78f, 1.25f, 0.06f), Vector3.zero, Quaternion.identity, Vector3.one, mats["pebbles"], false);
    }

    // Catmull-Rom krivka pres body trasy (body na zemi), kazdy usek rozdeleny na kratke kousky.
    static List<Vector3> Smooth(Vector3[] path)
    {
        var points = new List<Vector3>();
        foreach (var p in path) points.Add(OnGround(p));
        var result = new List<Vector3>();
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 p0 = points[Mathf.Max(0, i - 1)], p1 = points[i], p2 = points[i + 1], p3 = points[Mathf.Min(points.Count - 1, i + 2)];
            int steps = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(p1, p2) / 1.2f));
            for (int k = 0; k < steps; k++)
            {
                float t = k / (float)steps;
                result.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                    + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
            }
        }
        result.Add(points[points.Count - 1]);
        return result;
    }

    // Plochy pas podel krivky mezi bocnimi odsazenimi 'from' a 'to' (zaporne = vlevo), 'lift' nad zemi.
    // Svety souradnice (objekt stoji v pocatku). V ohybech se okraje spojuji (prumer smeru), takze se nekrizi.
    static Mesh Ribbon(string name, List<Vector3> curve, float from, float to, float lift)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        float distance = 0f;
        for (int i = 0; i < curve.Count; i++)
        {
            Vector3 back = i > 0 ? curve[i] - curve[i - 1] : curve[1] - curve[0];
            Vector3 ahead = i < curve.Count - 1 ? curve[i + 1] - curve[i] : back;
            back.y = ahead.y = 0f;
            Vector3 tangent = (back.normalized + ahead.normalized).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, tangent);
            if (i > 0) distance += Vector3.Distance(curve[i], curve[i - 1]);
            Vector3 c = curve[i] + Vector3.up * lift;
            vertices.Add(c + side * from);
            vertices.Add(c + side * to);
            uvs.Add(new Vector2(from, distance));
            uvs.Add(new Vector2(to, distance));
            if (i > 0)
            {
                int a = (i - 1) * 2;
                triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Zelena skluzavka u bazenu: z plosiny se sjede do koryta s vodou a proud hrace odnese az za rozhlednu
    // do jezirka, kde jizda konci.
    static void SlideAndPond(Transform root, Transform map)
    {
        var ride = Ride(map);
        // jezirko za rozhlednou (konec jizdy)
        Vector3 end = ride[ride.Length - 1];
        var pond = Object(root, "Jezirko", end, 0f);
        var pondWater = Cylinder(pond, "Voda", new Vector3(0f, 0.05f, 0f), 3.2f, 0.04f, Quaternion.identity, mats["river"], false);
        var pondFlow = pondWater.AddComponent<WaterFlow>();   // cil jizdy - tady je obri splouch
        pondFlow.pond = true;
        pondFlow.radius = 3.2f;
        Cylinder(pond, "Breh", new Vector3(0f, 0.03f, 0f), 3.7f, 0.06f, Quaternion.identity, mats["pebbles"], false);

        // skluzavka vedle bazenu (severne, s odstupem od okraje), skluz miri na sever do zacatku koryta
        var o = Object(root, "Skluzavka", SlideFoot(map), 0f);
        const float top = SlideTop;
        Local(o, "Plosina", new Vector3(0f, top, 0f), new Vector3(1.4f, 0.12f, 1.4f), mats["slide"], true);
        foreach (float x in new[] { -0.65f, 0.65f })
            foreach (float z in new[] { -0.65f, 0.65f })
                Local(o, "Noha", new Vector3(x, top * 0.5f, z), new Vector3(0.1f, top, 0.1f), mats["iron"], false);
        Local(o, "Zabradli", new Vector3(-0.7f, top + 0.45f, 0f), new Vector3(0.06f, 0.06f, 1.4f), mats["iron"], false);
        Local(o, "Zabradli", new Vector3(0f, top + 0.45f, -0.7f), new Vector3(1.4f, 0.06f, 0.06f), mats["iron"], false);
        // skluz
        float run = SlideRun;
        float angle = Mathf.Atan2(top - 0.25f, run) * Mathf.Rad2Deg;
        float slant = Mathf.Sqrt(run * run + (top - 0.25f) * (top - 0.25f));
        var chute = Quaternion.Euler(angle, 0f, 0f);
        Vector3 chuteCenter = new Vector3(0f, (top + 0.25f) * 0.5f, 0.7f + run * 0.5f);
        Local(o, "Skluz", chuteCenter, new Vector3(0.9f, 0.08f, slant), mats["slide"], true, chute);
        foreach (float x in new[] { -0.47f, 0.47f })
            Local(o, "Bocnice", chuteCenter + new Vector3(x, 0.12f, 0f), new Vector3(0.06f, 0.25f, slant), mats["slide"], false, chute);
        // schudky z boku (vychod; na jih je bazen), vyska stupne 0,24 m
        int steps = Mathf.CeilToInt(top / 0.24f);
        for (int i = 0; i < steps; i++)
        {
            float h = (i + 1) * top / steps;
            Local(o, "Schod", new Vector3(0.75f + (steps - 1 - i) * 0.3f, h - 0.06f, 0f), new Vector3(0.32f, 0.12f, 1f), mats["slide"], true);
        }

        // klouzani: na skluzu hrace unasi dolu
        var slideZone = new GameObject("Klouzani").transform;
        slideZone.SetParent(o, false);
        slideZone.localPosition = chuteCenter + chute * new Vector3(0f, 0.7f, 0f);
        slideZone.localRotation = chute;
        var slideCurrent = slideZone.gameObject.AddComponent<WaterCurrent>();
        slideCurrent.size = new Vector3(1f, 1.4f, slant + 0.6f);
        slideCurrent.speed = 9f;
        slideCurrent.slide = true;   // kdo sjede z toboganu, jede po vode rychleji (WaterCurrent.RideBoost)
        SlideStart(root);

        // proud po cele vodni ceste (podel stejne krivky jako koryto; posledni kus konci v jezirku)
        var flows = MapBuildKit.Group(root, "ProudVody");
        var curve = Smooth(ride);
        float total = 0f;
        for (int i = 1; i < curve.Count; i++) total += Vector3.Distance(curve[i], curve[i - 1]);
        float walked = 0f;
        for (int i = 0; i < curve.Count - 1; i++)
        {
            Vector3 a = curve[i], b = curve[i + 1];
            Vector3 along = b - a;
            along.y = 0f;
            walked += along.magnitude;
            if (walked > total - 1.5f) break;   // posledni metr a pul v jezirku uz nenese
            var zone = new GameObject("Proud").transform;
            zone.SetParent(flows, false);
            zone.position = (a + b) * 0.5f + Vector3.up * 0.6f;
            zone.rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
            var current = zone.gameObject.AddComponent<WaterCurrent>();
            current.size = new Vector3(2.2f, 1.6f, along.magnitude + 0.3f);
            current.speed = 6f;
        }
    }

    // Kotel na drevene piliny v kotelne velke chaty (prizemi, prvni mistnost vpravo od vchodu: x 10,4-13,3,
    // z -9,1 az -6,8, otevrena na zapad do chodby). Kotel u vychodni zdi, packa na jeho celni stene.
    static void BoilerRoom(Transform root)
    {
        var g = MapBuildKit.Group(root, "Kotel");
        float y = Ground(new Vector3(11.8f, 0f, -8f), 2.6f, root);

        var enamel = MapBuildKit.Mat(MatDir + "KotelSmalt.mat", null, new Color(0.22f, 0.30f, 0.26f), Vector2.one, 0.55f);
        var fire = MapBuildKit.Mat(MatDir + "KotelOhen.mat", null, new Color(1f, 0.45f, 0.1f), Vector2.one, 0.2f,
            false, false, new Color(1f, 0.42f, 0.08f) * 2.5f);
        var sack = MapBuildKit.Mat(MatDir + "PytelPilin.mat", null, new Color(0.62f, 0.52f, 0.36f), Vector2.one, 0.05f);
        var sawdust = MapBuildKit.Mat(MatDir + "Piliny.mat", null, new Color(0.85f, 0.70f, 0.45f), Vector2.one, 0.02f);

        // telo kotle a zasobnik na piliny
        MapBuildKit.Box(g, "Telo", new Vector3(12.85f, y + 0.75f, -8.3f), new Vector3(0.8f, 1.5f, 0.9f), enamel, true);
        MapBuildKit.Box(g, "Zasobnik", new Vector3(12.95f, y + 0.6f, -7.45f), new Vector3(0.6f, 1.2f, 0.6f), enamel, true);
        MapBuildKit.Box(g, "ZasobnikVika", new Vector3(12.95f, y + 1.22f, -7.45f), new Vector3(0.64f, 0.04f, 0.64f), mats["iron"], false);
        // dvirka s okenkem do ohne a horni dvirka
        MapBuildKit.Box(g, "Dvirka", new Vector3(12.435f, y + 0.55f, -8.3f), new Vector3(0.04f, 0.42f, 0.5f), mats["iron"], false);
        MapBuildKit.Box(g, "Okenko", new Vector3(12.41f, y + 0.55f, -8.3f), new Vector3(0.02f, 0.2f, 0.3f), fire, false);
        MapBuildKit.Box(g, "HorniDvirka", new Vector3(12.435f, y + 1.15f, -8.3f), new Vector3(0.04f, 0.28f, 0.5f), mats["iron"], false);
        // kourovod do zdi
        MapBuildKit.Box(g, "Kourovod", new Vector3(12.85f, y + 2.3f, -8.3f), new Vector3(0.2f, 1.6f, 0.2f), mats["iron"], false);
        MapBuildKit.Box(g, "KourovodZed", new Vector3(13.08f, y + 3.0f, -8.3f), new Vector3(0.46f, 0.2f, 0.2f), mats["iron"], false);
        // pytle s pilinami a hromadka pilin
        MapBuildKit.Box(g, "PytelPilin", new Vector3(10.9f, y + 0.25f, -8.75f), new Vector3(0.5f, 0.5f, 0.65f), Quaternion.Euler(0f, 12f, 0f), sack, true);
        MapBuildKit.Box(g, "PytelPilin", new Vector3(11.5f, y + 0.22f, -8.8f), new Vector3(0.5f, 0.44f, 0.6f), Quaternion.Euler(0f, -8f, 0f), sack, true);
        MapBuildKit.Box(g, "Piliny", new Vector3(12.2f, y + 0.03f, -7.3f), new Vector3(0.5f, 0.06f, 0.4f), sawdust, false);

        // packa: drzak na celni stene kotle, kloub se otaci (nesmi byt staticky)
        MapBuildKit.Box(g, "PackaDrzak", new Vector3(12.43f, y + 1.0f, -8.68f), new Vector3(0.04f, 0.3f, 0.12f), mats["iron"], false);
        var pivot = new GameObject("Packa").transform;
        pivot.SetParent(g, false);
        pivot.position = new Vector3(12.39f, y + 0.95f, -8.68f);
        var handle = MapBuildKit.Box(pivot, "Rukojet", pivot.position + Vector3.up * 0.2f, new Vector3(0.04f, 0.4f, 0.04f), mats["iron"], false);
        var knob = MapBuildKit.Box(pivot, "Koule", pivot.position + Vector3.up * 0.42f, new Vector3(0.09f, 0.09f, 0.09f), mats["tractor"], false);
        GameObjectUtility.SetStaticEditorFlags(handle, 0);
        GameObjectUtility.SetStaticEditorFlags(knob, 0);
        pivot.localRotation = Quaternion.Euler(0f, 0f, -30f);

        // svetlo ohne
        var fireLight = new GameObject("SvetloOhne").AddComponent<Light>();
        fireLight.transform.SetParent(g, false);
        fireLight.transform.position = new Vector3(12.2f, y + 0.6f, -8.3f);
        fireLight.type = LightType.Point;
        fireLight.color = new Color(1f, 0.5f, 0.15f);
        fireLight.range = 3.5f;
        fireLight.intensity = 0.6f;
        fireLight.shadows = LightShadows.None;

        // oranzova svetla v hornich patrech (zapnou se pri prehrati)
        var heatLights = new List<Light>();
        foreach (var p in new[] { new Vector3(12f, 5.2f, -12f), new Vector3(17f, 5.2f, -3f), new Vector3(21f, 5.2f, 6f),
                                  new Vector3(12f, 8.2f, -6f), new Vector3(21f, 8.2f, 2f) })
        {
            var l = new GameObject("SvetloHorka").AddComponent<Light>();
            l.transform.SetParent(g, false);
            l.transform.position = p;
            l.type = LightType.Point;
            l.color = new Color(1f, 0.45f, 0.15f);
            l.range = 10f;
            l.intensity = 2f;
            l.shadows = LightShadows.None;
            l.enabled = false;
            heatLights.Add(l);
        }

        var boiler = g.gameObject.AddComponent<Boiler>();
        boiler.lever = pivot;
        boiler.fireLight = fireLight;
        boiler.heatLights = heatLights.ToArray();
        // horni patra chaty (obvodove zdi x 7-27,1, z -19,3 az 11,1; prvni patro od 3 m, pod strechou do 9,3 m)
        boiler.heatMin = new Vector3(7.0f, 3.0f, -19.3f);
        boiler.heatMax = new Vector3(27.1f, 9.3f, 11.1f);
    }

    // Zona startu jizdy nad plosinou toboganu (podle toho, kde plosina ted je).
    static void SlideStart(Transform root)
    {
        var slide = root.Find("Skluzavka");
        var deck = slide != null ? slide.Find("Plosina") : null;
        if (deck == null || !deck.TryGetComponent<Renderer>(out var renderer)) return;
        DeleteNamed(slide, "StartJizdy");

        Bounds b = renderer.bounds;
        var zone = new GameObject("StartJizdy").transform;
        zone.SetParent(slide, false);
        zone.SetPositionAndRotation(new Vector3(b.center.x, b.max.y + 1f, b.center.z), Quaternion.Euler(0f, slide.eulerAngles.y, 0f));
        var start = zone.gameObject.AddComponent<WaterCurrent>();
        start.start = true;
        start.speed = 0f;
        start.size = new Vector3(b.size.x + 0.3f, 2f, b.size.z + 0.3f);
    }

    // Velke tuje podle nakresu (mimo trasu Yarisu a otvory tunelu).
    static void MoreThujas(Transform root)
    {
        var thuja = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/House/Thuja.mat");
        var cone = MapBuildKit.ConeMesh();
        if (thuja == null || cone == null) return;
        var spots = new (float x, float z, float h)[]
        {
            (31f, 38.5f, 11f), (33.5f, 40.5f, 10f), (30.5f, 42.8f, 9f),          // severovychod u silnice (vedle vodni cesty)
            (21.5f, 54f, 12f), (23f, 55.8f, 11f), (20.5f, 57f, 10f), (23.5f, 52.5f, 9f), // za sklenikem
            (-14.8f, 37.8f, 11f), (2.2f, 40f, 11f),                                // u paty plosiny rozhledny
        };
        foreach (var (x, z, h) in spots)
            Thuja(root, thuja, cone, new Vector3(x, 0f, z), h, 3f);
    }

    // Okraj terasy male chaty smerem k louce: na severu blok stromu, na jihu kamenna zidka (kryt pro obrance bodu B).
    static void TerraceEdge(Transform root)
    {
        var thuja = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/House/Thuja.mat");
        var cone = MapBuildKit.ConeMesh();
        if (thuja != null && cone != null)
        {
            var trees = MapBuildKit.Group(root, "BlokStromu");
            foreach (float z in new[] { 20.6f, 23.2f, 25.8f, 28.4f })
                Thuja(trees, thuja, cone, new Vector3(-17.3f, 0f, z), 6.5f, 8f);
        }

        var wall = MapBuildKit.Group(root, "KamennaZidka");
        float y = Ground(new Vector3(-16.7f, 0f, 2f), 8f);
        // 1,8 m - v podrepu i ve stoje se za ni da schovat, strilet jde pres ni po vyskoceni nebo z kraju
        MapBuildKit.Box(wall, "Zidka", new Vector3(-16.7f, y + 0.9f, 2.25f), new Vector3(0.6f, 1.8f, 21f), mats["stone"], true);
        MapBuildKit.Box(wall, "Krycka", new Vector3(-16.7f, y + 1.84f, 2.25f), new Vector3(0.72f, 0.08f, 21.2f), mats["stone"], false);
    }

    // Kamenny venkovni gril na terase severne od male chaty.
    static void StoneGrill(Transform root)
    {
        var o = Object(root, "KamennyGril", new Vector3(-38f, 0f, 14f), 0f, 8f);
        Local(o, "Zakladna", new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.9f, 1f), mats["stone"], true);
        Local(o, "Zadni", new Vector3(0f, 1.25f, 0.4f), new Vector3(1.6f, 0.7f, 0.2f), mats["stone"], false);
        foreach (float x in new[] { -0.7f, 0.7f })
            Local(o, "Bok", new Vector3(x, 1.1f, 0.05f), new Vector3(0.2f, 0.4f, 0.9f), mats["stone"], false);
        Local(o, "Rost", new Vector3(0f, 0.95f, 0.05f), new Vector3(1.2f, 0.03f, 0.75f), mats["iron"], false);
        Local(o, "Komin", new Vector3(0f, 1.9f, 0.4f), new Vector3(0.45f, 0.7f, 0.3f), mats["stone"], false);
        // kameny kolem (sezeni)
        for (int i = 0; i < 5; i++)
        {
            float a = Mathf.Lerp(-150f, -30f, i / 4f) * Mathf.Deg2Rad;
            Local(o, "Kamen", new Vector3(Mathf.Cos(a) * 2.2f, 0.22f, Mathf.Sin(a) * 2.2f), new Vector3(0.55f, 0.44f, 0.45f),
                mats["stone"], true, Quaternion.Euler(0f, i * 37f, 0f));
        }
    }

    // ---------------- kolize tramu rozhledny ----------------

    // Krizove vzpery, nosniky a zabradli rozhledny byly jen vzhled (strely i hraci jimi prochazeli).
    // Box Collider se sam prizpusobi kvadru tramu (trámy jsou natocene kvadry).
    static void TowerBeamCollision(Transform map)
    {
        var parts = new HashSet<string> { "Vzpera", "Nosnik", "Sloupek", "Madlo", "Lat" };
        foreach (var path in new[] { "Rozhledna/Detaily", "RozhlednaDetaily" })
        {
            var details = map.Find(path);
            if (details == null) continue;
            foreach (var filter in details.GetComponentsInChildren<MeshFilter>(true))
                if (parts.Contains(filter.name) && filter.sharedMesh != null && filter.GetComponent<Collider>() == null)
                    filter.gameObject.AddComponent<BoxCollider>();
        }
    }

    // ---------------- vyssi stropy ve velke chate ----------------

    // Vysky ve velke chate se prepocitaji po usecich: do 'Floor1' beze zmeny, mezi 'Floor1' a 'Floor2' +0,5 m,
    // nad 'Floor2' +1 m (strop prizemi lezi nad Floor1, podlaha podkrovi nad Floor2). Co lezi cele v jednom useku,
    // se jen posune; co useky protina (zdi, okenni preklady), se natahne. Schodiste se natahne rovnomerne od zeme.
    // Kromne chaty se posune i vse ostatni nad prizemim v jejim pudorysu (svetla kotle, balicky...).
    // Zmeny jdou vratit pres Ctrl+Z.
    const float CeilingRaise = 0.5f;
    const float Floor1 = 2.9f, Floor2 = 6.1f;
    const float StairTop = 3.76f;   // podlaha 1. patra, kam vede schodiste

    static float RaisedY(float y) => y <= Floor1 ? y : y <= Floor2 ? y + CeilingRaise : y + 2f * CeilingRaise;
    static float RaisedStairY(float y) => y <= 0f ? y : y * (1f + CeilingRaise / StairTop);

    static void RaiseHouseCeilings(Transform map)
    {
        var house = map.Find("HlavniChata");
        if (house == null)
        {
            Debug.LogWarning("[Hratelnost] Vyssi stropy: HlavniChata nenalezena.");
            return;
        }
        Physics.SyncTransforms();

        // pudorys chaty (se strechou)
        Bounds footprint = new Bounds();
        bool any = false;
        foreach (var r in house.GetComponentsInChildren<Renderer>())
        {
            if (!any) { footprint = r.bounds; any = true; }
            else footprint.Encapsulate(r.bounds);
        }

        // co se meni: vse v chate, mimo ni jen veci nad prizemim v jejim pudorysu
        var targets = new List<(Transform t, Bounds b, bool inHouse)>();
        foreach (var t in map.GetComponentsInChildren<Transform>(true))
        {
            if (t == map || t == house) continue;
            bool inHouse = t.IsChildOf(house);
            if (!OwnBounds(t, out Bounds b)) continue;
            if (!inHouse)
            {
                if (b.min.y < Floor1 + 0.3f) continue;
                if (b.min.x < footprint.min.x || b.max.x > footprint.max.x || b.min.z < footprint.min.z || b.max.z > footprint.max.z) continue;
            }
            else if (b.max.y <= Floor1) continue;
            targets.Add((t, b, inHouse));
        }

        // rodice pred potomky (zmena rodice pohne potomky, potomci se pak srovnaji na sve cile)
        targets.Sort((a, b) => Depth(a.t).CompareTo(Depth(b.t)));
        int moved = 0, stretched = 0;
        foreach (var (t, b, _) in targets)
        {
            Undo.RecordObject(t, "Vyssi stropy");
            bool stairs = t.name.StartsWith("Stairs");
            float newMin = stairs ? RaisedStairY(b.min.y) : RaisedY(b.min.y);
            float newMax = stairs ? RaisedStairY(b.max.y) : RaisedY(b.max.y);
            float newHeight = newMax - newMin;

            // aktualni stav (rodic uz mohl objekt posunout nebo natahnout)
            Physics.SyncTransforms();
            OwnBounds(t, out Bounds now);

            // natahnout jen rovne postavene veci (otoceni jen kolem svisle osy), ostatni jen posunout
            bool upright = Vector3.Dot(t.rotation * Vector3.up, Vector3.up) > 0.999f;
            if (upright && now.size.y > 0.01f && newHeight > 0.01f && Mathf.Abs(newHeight - now.size.y) > 0.01f)
            {
                var scale = t.localScale;
                t.localScale = new Vector3(scale.x, scale.y * newHeight / now.size.y, scale.z);
                Physics.SyncTransforms();
                OwnBounds(t, out Bounds after);
                t.position += Vector3.up * (newMin - after.min.y);
                stretched++;
            }
            else
            {
                t.position += Vector3.up * ((newMin + newMax) * 0.5f - now.center.y);
                moved++;
            }
        }

        // kotel: horka oblast sahala pod strechu - strecha je ted o metr vys
        foreach (var boiler in map.GetComponentsInChildren<Boiler>(true))
        {
            Undo.RecordObject(boiler, "Vyssi stropy");
            boiler.heatMax.y = RaisedY(boiler.heatMax.y);
            EditorUtility.SetDirty(boiler);
        }

        Debug.Log($"[Hratelnost] Vyssi stropy ve velke chate: posunuto {moved}, natazeno {stretched} objektu. " +
                  "Strop prizemi i 1. patra je o 0,5 m vys. Vratit jde pres Ctrl+Z, ulozit scenu Ctrl+S.");
    }

    // Po zvyseni stropu zustalo dolni rameno schodiste (Stairs, cele pod hranici Floor1) puvodne vysoke, odpocivadlo
    // nad nim (strop/Cube (4)) se posunulo o 0,5 m nahoru a nosna zidka pod jeho hranou (schodiste_spodek/Cube (3))
    // se natahla - schod byl moc vysoky a zidka zavrela pruchod. Rameno se natahne az pod odpocivadlo (stejny
    // odstup jako puvodne), zidka se srovna pod odpocivadlo a pres rameno vede hladka neviditelna rampa.
    static void FixHouseStairs(Transform map, Transform root)
    {
        var stairs = map.Find("HlavniChata/PRIZEMI/schodiste_spodek/Stairs");
        var landing = map.Find("HlavniChata/PRVNIPATRO/strop/Cube (4)");
        var support = map.Find("HlavniChata/PRIZEMI/schodiste_spodek/Cube (3)");
        if (stairs == null || landing == null || !stairs.TryGetComponent<Renderer>(out var stairsRenderer)
            || !landing.TryGetComponent<Renderer>(out var landingRenderer))
        {
            Debug.LogWarning("[Hratelnost] Oprava schodiste: Stairs nebo odpocivadlo (strop/Cube (4)) nenalezeno.");
            return;
        }
        Undo.RecordObject(stairs, "Oprava schodiste");
        Bounds sb = stairsRenderer.bounds, lb = landingRenderer.bounds;
        const float gapBelowLanding = 0.29f;   // puvodni odstup vrsku ramene od vrsku odpocivadla

        // rameno: spodek zustava, vrsek pod odpocivadlo
        float newTop = lb.max.y - gapBelowLanding;
        if (sb.size.y > 0.1f && newTop > sb.max.y + 0.01f)
        {
            float k = (newTop - sb.min.y) / sb.size.y;
            var scale = stairs.localScale;
            stairs.localScale = new Vector3(scale.x, scale.y * k, scale.z);
            Physics.SyncTransforms();
            stairs.position += Vector3.up * (sb.min.y - stairsRenderer.bounds.min.y);
            sb = stairsRenderer.bounds;
        }

        // zidka pod hranou odpocivadla: vrsek nejvys po spodek odpocivadla
        if (support != null && support.TryGetComponent<Renderer>(out var supportRenderer))
        {
            Bounds wb = supportRenderer.bounds;
            if (wb.max.y > lb.min.y + 0.01f && wb.size.y > 0.1f)
            {
                Undo.RecordObject(support, "Oprava schodiste");
                float k = (lb.min.y - wb.min.y) / wb.size.y;
                var scale = support.localScale;
                support.localScale = new Vector3(scale.x, scale.y * k, scale.z);
                Physics.SyncTransforms();
                support.position += Vector3.up * (wb.min.y - supportRenderer.bounds.min.y);
            }
        }

        // hladka rampa: od paty ramene (strana dal od odpocivadla) az na hranu odpocivadla
        bool alongX = sb.size.x >= sb.size.z;
        Vector3 toLanding = lb.center - sb.center;
        Vector3 low, high;
        float width;
        if (alongX)
        {
            float sign = Mathf.Sign(toLanding.x);
            low = new Vector3(sign > 0f ? sb.min.x : sb.max.x, sb.min.y + 0.15f, sb.center.z);
            high = new Vector3(sign > 0f ? lb.min.x : lb.max.x, lb.max.y, sb.center.z);
            width = sb.size.z;
        }
        else
        {
            float sign = Mathf.Sign(toLanding.z);
            low = new Vector3(sb.center.x, sb.min.y + 0.15f, sign > 0f ? sb.min.z : sb.max.z);
            high = new Vector3(sb.center.x, lb.max.y, sign > 0f ? lb.min.z : lb.max.z);
            width = sb.size.x;
        }
        Vector3 along = (high - low).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, along).normalized;
        Vector3 normal = Vector3.Cross(along, side);
        if (normal.y < 0f) normal = -normal;

        DeleteNamed(root, "RampaSchodisteChaty");
        var ramp = new GameObject("RampaSchodisteChaty");
        Undo.RegisterCreatedObjectUndo(ramp, "Oprava schodiste");
        ramp.transform.SetParent(root, false);
        ramp.transform.SetPositionAndRotation((low + high) * 0.5f - normal * 0.15f, Quaternion.LookRotation(along, normal));
        ramp.AddComponent<BoxCollider>().size = new Vector3(width, 0.3f, Vector3.Distance(low, high) + 0.2f);

        Debug.Log($"[Hratelnost] Oprava schodiste ve velke chate: rameno do {sb.max.y:0.00} m, odpocivadlo {lb.max.y:0.00} m, rampa pridana.");
    }

    // Balicky, ktere skript rozmistil po mape (lekarnicky, pivo, slivovice, stity), se smazou. Zustanou ty, ktere
    // si uzivatel vyzadal: dedova slivovice ve spizi (Invulnerable) a Tramal pod diplomem (mimo skupinu Balicky).
    static void RemoveMapPickups(Transform root)
    {
        var group = root.Find("Balicky");
        if (group == null) return;
        int removed = 0;
        foreach (var spot in group.GetComponentsInChildren<PickupSpot>(true))
        {
            if (spot.kind == PickupKind.Invulnerable || spot.kind == PickupKind.Tramal) continue;
            UnityEngine.Object.DestroyImmediate(spot.gameObject);
            removed++;
        }
        Debug.Log($"[Hratelnost] Odstraneno {removed} balicku z mapy (dedova slivovice a Tramal zustavaji).");
    }

    // Lekarnicka (50 HP) na viku kazdeho zachodu ve scene - jako podobjekt zachodu, posouva se s nim.
    static void ToiletHealthPacks(Transform map)
    {
        int added = 0;
        foreach (var toilet in map.GetComponentsInChildren<Transform>(true))
        {
            if (toilet.name != "Zachod" || toilet.Find("Balicek_Zachod") != null) continue;
            var renderers = toilet.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            var spot = new GameObject("Balicek_Zachod");
            Undo.RegisterCreatedObjectUndo(spot, "Lekarnicka na zachode");
            spot.transform.SetParent(toilet, true);
            spot.transform.position = new Vector3(b.center.x, b.max.y + 0.01f, b.center.z);
            var pickup = spot.AddComponent<PickupSpot>();
            pickup.kind = PickupKind.Health;
            pickup.healAmount = 50f;
            pickup.reachBelow = b.size.y + 0.4f;   // hrac stoji na podlaze vedle zachodu
            added++;
        }
        Debug.Log($"[Hratelnost] Lekarnicka na zachode: pridano {added}.");
    }

    static int Depth(Transform t)
    {
        int d = 0;
        for (var p = t.parent; p != null; p = p.parent) d++;
        return d;
    }

    // Hranice objektu jen z jeho vlastnich komponent (renderer, kolize); bez nich bod (svetla, balicky, znacky).
    // Prazdne skupiny (jen potomci) se vynechaji.
    static bool OwnBounds(Transform t, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        // vypnute objekty nemaji platne hranice renderu - jen bod (posunou se, nenatahnou)
        if (!t.gameObject.activeInHierarchy)
        {
            if (t.GetComponents<Component>().Length <= 1 && t.childCount > 0) return false;
            bounds = new Bounds(t.position, Vector3.zero);
            return true;
        }
        foreach (var r in t.GetComponents<Renderer>())
        {
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        foreach (var c in t.GetComponents<Collider>())
        {
            if (!c.enabled) continue;
            if (!any) { bounds = c.bounds; any = true; } else bounds.Encapsulate(c.bounds);
        }
        if (any) return true;
        if (t.GetComponents<Component>().Length > 1)
        {
            bounds = new Bounds(t.position, Vector3.zero);
            return true;
        }
        return false;
    }

    // ---------------- zebrik misto kamenne rampy ----------------

    // Kamenna rampa (Rozhledna/Cube (24)) vedla ze zeme na nizsi plosinu. Rampa se vypne (ne smaze) a na hranu
    // plosiny, kde konci, se postavi zebrik (Ladder): pomalejsi a zranitelnejsi pristup nez schodiste.
    static void TowerLadder(Transform map, Transform root)
    {
        var tower = map.Find("Rozhledna");
        var ramp = map.Find("Rozhledna/Cube (24)");
        var deck = map.Find("Rozhledna/Cube (12)");
        if (tower == null || ramp == null || deck == null || !ramp.TryGetComponent<Renderer>(out var rampRenderer)
            || !deck.TryGetComponent<Renderer>(out var deckRenderer))
        {
            Debug.LogWarning("[Hratelnost] Zebrik rozhledny: rampa Rozhledna/Cube (24) nebo plosina Cube (12) nenalezena.");
            return;
        }

        // (vypnuta rampa nema platne hranice - na chvili ji zapnout)
        ramp.gameObject.SetActive(true);
        Physics.SyncTransforms();
        Bounds rb = rampRenderer.bounds, db = deckRenderer.bounds;
        ramp.gameObject.SetActive(false);
        Physics.SyncTransforms();

        // hrana plosiny, u ktere rampa koncila (smer od stredu plosiny k rampe)
        Vector3 toRamp = rb.center - db.center;
        bool alongZ = Mathf.Abs(toRamp.z) >= Mathf.Abs(toRamp.x);
        Vector3 outward = alongZ ? new Vector3(0f, 0f, Mathf.Sign(toRamp.z)) : new Vector3(Mathf.Sign(toRamp.x), 0f, 0f);
        Vector3 edge = alongZ
            ? new Vector3(Mathf.Clamp(rb.center.x, db.min.x + 0.6f, db.max.x - 0.6f), 0f, toRamp.z > 0f ? db.max.z : db.min.z)
            : new Vector3(toRamp.x > 0f ? db.max.x : db.min.x, 0f, Mathf.Clamp(rb.center.z, db.min.z + 0.6f, db.max.z - 0.6f));
        float top = db.max.y;
        float ground = Ground(edge + outward * 0.5f, top - 0.3f, tower);
        if (top - ground < 1f) return;

        var g = MapBuildKit.Group(tower, "ZebrikRozhledny");   // pod rozhlednou - posouva se s ni
        var rotation = Quaternion.LookRotation(-outward, Vector3.up);   // zebrik celem ven, "dopredu" = k plosine
        Vector3 Local(float x, float y, float z) => edge + rotation * new Vector3(x, 0f, z) + Vector3.up * y;

        // bocnice (prectivaji metr nad plosinu - chyt pri vylezani) a pricky po 30 cm
        float railTop = top + 1f;
        foreach (float x in new[] { -0.28f, 0.28f })
            MapBuildKit.Box(g, "Bocnice", Local(x, (ground + railTop) * 0.5f, -0.1f), new Vector3(0.07f, railTop - ground, 0.07f), rotation, mats["beam"], false);
        for (float y = ground + 0.3f; y < top + 0.05f; y += 0.3f)
            MapBuildKit.Box(g, "Pricka", Local(0f, y, -0.1f), new Vector3(0.56f, 0.04f, 0.04f), rotation, mats["beam"], false);

        // neviditelna stena pod plosinou za zebrikem (aby se pod plosinu neslo vejit a lezlo se rovnou nahoru)
        float under = db.min.y;
        var blocker = new GameObject("ZaZebrikem");
        blocker.transform.SetParent(g, false);
        blocker.transform.SetPositionAndRotation(Local(0f, (ground + under) * 0.5f, 0.05f), rotation);
        blocker.AddComponent<BoxCollider>().size = new Vector3(1f, under - ground, 0.1f);

        // oblast lezeni: pred zebrikem od zeme kousek nad plosinu
        var zone = new GameObject("Lezeni");
        zone.transform.SetParent(g, false);
        zone.transform.SetPositionAndRotation(Local(0f, (ground + top + 0.6f) * 0.5f, -0.45f), rotation);
        zone.AddComponent<Ladder>().size = new Vector3(0.9f, top + 0.6f - ground, 0.9f);

        Debug.Log($"[Hratelnost] Zebrik rozhledny: kamenna rampa vypnuta, zebrik z {ground:0.0} m na {top:0.0} m.");
    }

    // ---------------- druhe schodiste na rozhlednu ----------------

    // Na rozhlednu vedla jen rampa ze severu na nizsi (zapadni) plosinu - sniper nahore se tezko dobyval.
    // Druhe schodiste vede z jizni strany (od bodu C) na tutez plosinu. Vse se meri z plosiny ve scene
    // (Rozhledna/Cube (12)), takze sedi, i kdyz se rozhledna posunula. Zabradli v miste nastupu se jen vypne.
    static void TowerStairs(Transform map, Transform root)
    {
        var deck = map.Find("Rozhledna/Cube (12)");
        if (deck == null || !deck.TryGetComponent<Renderer>(out var deckRenderer))
        {
            Debug.LogWarning("[Hratelnost] Schodiste rozhledny: plosina Rozhledna/Cube (12) nenalezena, schodiste se nepostavi.");
            return;
        }

        const float rise = 0.25f, tread = 0.32f, width = 1.4f;
        Bounds b = deckRenderer.bounds;
        float top = b.max.y;
        float x = b.center.x;
        float edge = b.min.z;   // jizni hrana plosiny

        // vyska zeme pred schodistem (bez samotne rozhledny), dvakrat - delka zavisi na vysce
        var tower = map.Find("Rozhledna");
        float ground = 0f, run = 0f;
        for (int i = 0; i < 2; i++)
        {
            ground = Ground(new Vector3(x, 0f, edge - run - 0.5f), top - 0.3f, tower);
            run = Mathf.Ceil((top - ground) / rise) * tread;
        }
        int steps = Mathf.CeilToInt((top - ground) / rise);
        if (steps < 3)
        {
            Debug.LogWarning($"[Hratelnost] Schodiste rozhledny: plosina je jen {top - ground:0.0} m nad zemi, schodiste neni potreba.");
            return;
        }

        float stepRise = (top - ground) / steps;
        float length = steps * tread;
        const float headroom = 3.1f;   // hrac je 2 m vysoky a schody stoupaji 38 st. - nizsi strecha drhne o hlavu

        // vyska zeme pod sloupy strechy a bocnimi stenami - zmerit predem (pak by paprsek narazil do strechy)
        var postFloor = new float[2, 3];
        var wallFloor = new float[2, steps];
        for (int s = 0; s < 2; s++)
        {
            float sx = x + (s == 0 ? -1f : 1f) * (width * 0.5f + 0.2f);
            for (int i = 0; i < 3; i++)
                postFloor[s, i] = Ground(new Vector3(sx, 0f, Mathf.Lerp(edge - length, edge, (i + 0.5f) / 3f)), top - 0.3f, tower);
            for (int i = 0; i < steps; i++)
                wallFloor[s, i] = Ground(new Vector3(sx, 0f, edge - (i + 0.5f) * tread), top - 0.3f, tower);
        }

        // vse (i neviditelna rampa) pod rozhlednou: kdyz se rozhledna posune, schodiste jede s ni
        var g = MapBuildKit.Group(tower, "SchodisteRozhledny");
        for (int i = 0; i < steps; i++)
        {
            // stupen i (od plosiny dolu): plny blok od zeme po svou vysku
            float h = top - i * stepRise;
            float z = edge - (i + 0.5f) * tread;
            MapBuildKit.Box(g, "Stupen", new Vector3(x, ground + (h - ground) * 0.5f, z), new Vector3(width, h - ground, tread), mats["planks"], false);
        }

        // Chodi se po hladke neviditelne rampe (o hrany jednotlivych stupnu se postava zasekavala).
        // Horni plocha rampy vede po vnitrnich rozich stupnu od zeme az na plosinu.
        {
            Vector3 low = new Vector3(x, ground, edge - steps * tread);
            Vector3 high = new Vector3(x, top, edge);
            Vector3 along = (high - low).normalized;
            Vector3 normal = Vector3.Cross(along, Vector3.right);
            if (normal.y < 0f) normal = -normal;
            var slope = new GameObject("RampaSchodu");
            slope.transform.SetParent(g, false);
            slope.transform.SetPositionAndRotation((low + high) * 0.5f - normal * 0.15f, Quaternion.LookRotation(along, normal));
            slope.AddComponent<BoxCollider>().size = new Vector3(width, 0.3f, Vector3.Distance(low, high) + 0.3f);
        }

        // (zabradli na schodisti neni - uzivatel ho odstranil, stene staci)

        // strecha nad celym schodistem (rovnobezne se stupni, 'headroom' nad nimi): obrance z rozhledny na schody
        // nevidi a nestrili. Nese ji sest sloupu vedle schodiste (od zeme).
        Vector3 roofLow = new Vector3(x, ground + stepRise + headroom, edge - length - 0.4f);
        Vector3 roofHigh = new Vector3(x, top + headroom, edge + 0.1f);
        var roof = MapBuildKit.Beam(g, "Strecha", roofLow, roofHigh, 0.12f, mats["roof"], true);   // (strecha 3,1 m nad stupni)
        roof.transform.localScale = new Vector3((width + 0.7f) / 0.12f, 1f, 1f);
        for (int s = 0; s < 2; s++)
        {
            float sx = x + (s == 0 ? -1f : 1f) * (width * 0.5f + 0.2f);
            for (int i = 0; i < 3; i++)
            {
                float t = (i + 0.5f) / 3f;
                float z = Mathf.Lerp(edge - length, edge, t);
                float floor = postFloor[s, i];
                float h = Mathf.Lerp(ground + stepRise, top, t) + headroom;
                MapBuildKit.Box(g, "SloupStrechy", new Vector3(sx, (floor + h) * 0.5f, z), new Vector3(0.14f, h - floor, 0.14f), mats["beam"], true);
            }
        }

        // bocni steny az ke strese (schodiste je kryty tunel - z boku ani shora do nej nejde strilet).
        // Po stupnich: kazdy kus steny od zeme az kousek nad strechu nad timto stupnem.
        for (int s = 0; s < 2; s++)
        {
            float sx = x + (s == 0 ? -1f : 1f) * (width * 0.5f + 0.2f);
            for (int i = 0; i < steps; i++)
            {
                float z = edge - (i + 0.5f) * tread;
                float floor = wallFloor[s, i];
                float roofAt = top - i * stepRise + headroom + 0.15f;
                MapBuildKit.Box(g, "Stena", new Vector3(sx, (floor + roofAt) * 0.5f, z), new Vector3(0.1f, roofAt - floor, tread + 0.01f), mats["boards"], true);
            }
        }

        // pruchod v zabradli plosiny nad schodistem: tenke kusy zabradli v miste nastupu se vypnou (ne smazou)
        var gap = new Bounds(new Vector3(x, top + 0.7f, edge), new Vector3(width + 0.4f, 1.2f, 0.9f));
        // (vcetne neviditelnych koliznich kusu zabradli)
        Physics.SyncTransforms();
        var blockers = new List<(GameObject go, Bounds bounds)>();
        foreach (var r in tower.GetComponentsInChildren<Renderer>())
            blockers.Add((r.gameObject, r.bounds));
        foreach (var c in tower.GetComponentsInChildren<Collider>())
            blockers.Add((c.gameObject, c.bounds));
        int hidden = 0;
        foreach (var (go, rb) in blockers)
        {
            if (go.transform == deck || !go.activeInHierarchy || go.transform.IsChildOf(g)) continue;
            if (rb.size.y > 1.4f || !rb.Intersects(gap) || rb.max.y < top + 0.1f) continue;
            go.SetActive(false);
            hidden++;
        }
        Debug.Log($"[Hratelnost] Schodiste rozhledny: {steps} stupnu z vysky {ground:0.0} m na {top:0.0} m, vypnuto {hidden} kusu zabradli v miste nastupu.");
    }

    // ---------------- travnik nad vstupy do tunelu ----------------

    // Desky travniku (bez kolize, polozene starsimi skripty) prekryvaly otvory vstupu do tunelu - vstup pak nebyl
    // videt. Desky, ktere otvor protinaji, se vypnou a nahradi kusy kolem otvoru.
    static void CutLawnOverTunnel(Transform map, Transform root)
    {
        // vstupy podle TunnelSetup (i kdyby se tunel prestavel az po tomhle skriptu) + skutecne rampy ve scene
        var holes = new List<Rect> { Rect.MinMaxRect(-9.5f, 35f, -1.5f, 37f), Rect.MinMaxRect(-34f, 84f, -26f, 87f) };
        var ramps = map.Find("Tunel/Ramps");
        if (ramps != null)
            foreach (Transform ramp in ramps)
            {
                var r = ramp.GetComponent<Renderer>();
                if (r == null) continue;
                var b = r.bounds;
                holes.Add(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z));
            }

        var g = MapBuildKit.Group(root, "TravnikVyrezy");
        foreach (var filter in map.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.name != "Travnik" || filter.sharedMesh == null || filter.transform.IsChildOf(root)) continue;
            var b = MapBuildKit.WorldBounds(filter.sharedMesh.bounds, filter.transform.localToWorldMatrix);
            var pieces = new List<Rect> { Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z) };
            bool cut = false;
            foreach (var hole in holes)
            {
                var next = new List<Rect>();
                foreach (var p in pieces)
                {
                    if (!p.Overlaps(hole)) { next.Add(p); continue; }
                    cut = true;
                    if (hole.yMin > p.yMin) next.Add(Rect.MinMaxRect(p.xMin, p.yMin, p.xMax, hole.yMin));
                    if (hole.yMax < p.yMax) next.Add(Rect.MinMaxRect(p.xMin, hole.yMax, p.xMax, p.yMax));
                    float y0 = Mathf.Max(p.yMin, hole.yMin), y1 = Mathf.Min(p.yMax, hole.yMax);
                    if (hole.xMin > p.xMin) next.Add(Rect.MinMaxRect(p.xMin, y0, hole.xMin, y1));
                    if (hole.xMax < p.xMax) next.Add(Rect.MinMaxRect(hole.xMax, y0, p.xMax, y1));
                }
                pieces = next;
            }
            if (!cut) continue;

            var material = filter.GetComponent<MeshRenderer>() != null ? filter.GetComponent<MeshRenderer>().sharedMaterial : null;
            foreach (var p in pieces)
                if (p.width > 0.02f && p.height > 0.02f)
                    MapBuildKit.Box(g, "Travnik", new Vector3(p.center.x, b.center.y, p.center.y), new Vector3(p.width, b.size.y, p.height),
                        Quaternion.identity, material, false, true, false);
            filter.gameObject.SetActive(false);
        }
    }

    // ---------------- zadni vstup do male chaty ----------------

    // Dvere v jizni zdi male chaty a rampa z louky nahoru na terasu (+3 m). Puvodni zed a obklad se vypnou
    // (ne smazou) a nahradi se dily s otvorem.
    static void BackDoor(Transform map, Transform root)
    {
        var cottage = map.Find("MensiChata");
        if (cottage == null) return;
        const float doorX = -37f, half = 0.95f, height = 2.4f;
        var g = MapBuildKit.Group(root, "ZadniVstup");

        // jizni zed prizemi: dlouhy dil, z kolem -9, od 2,9 do 6,9 m
        foreach (var filter in cottage.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.transform.IsChildOf(root)) continue;
            var b = MapBuildKit.WorldBounds(filter.sharedMesh.bounds, filter.transform.localToWorldMatrix);
            bool southWall = b.size.x > 8f && b.size.z < 1f && b.center.z < -8.4f && b.center.z > -9.8f
                && b.min.y > 2.3f && b.min.y < 3.3f && b.max.y > 6f && b.max.y < 7.3f;
            bool facade = (filter.name == "Obklad" || filter.name == "KamennySokl") && b.center.z < -9f && b.center.z > -9.8f
                && b.min.y < 5f && b.max.y > 3f;
            if (!southWall && !facade) continue;
            if (b.min.x > doorX + half || b.max.x < doorX - half) continue;

            var renderer = filter.GetComponent<MeshRenderer>();
            var material = renderer != null ? renderer.sharedMaterial : mats["boards"];
            bool collide = filter.GetComponent<Collider>() != null;
            float floor = b.min.y, top = filter.name == "KamennySokl" ? b.max.y : b.max.y;
            float doorTop = floor + height;

            // levy a pravy dil
            if (doorX - half - b.min.x > 0.05f)
                MapBuildKit.Box(g, filter.name + "_L", new Vector3((b.min.x + doorX - half) * 0.5f, b.center.y, b.center.z),
                    new Vector3(doorX - half - b.min.x, b.size.y, b.size.z), material, collide);
            if (b.max.x - (doorX + half) > 0.05f)
                MapBuildKit.Box(g, filter.name + "_P", new Vector3((b.max.x + doorX + half) * 0.5f, b.center.y, b.center.z),
                    new Vector3(b.max.x - doorX - half, b.size.y, b.size.z), material, collide);
            // nadprazi
            if (top - doorTop > 0.05f)
                MapBuildKit.Box(g, filter.name + "_Nadprazi", new Vector3(doorX, (doorTop + top) * 0.5f, b.center.z),
                    new Vector3(half * 2f, top - doorTop, b.size.z), material, collide);
            filter.gameObject.SetActive(false);
        }

        // okna a nabytek, ktere by staly ve dverich nebo v pruchodu
        var doorway = new Bounds(new Vector3(doorX, 4f, -7.8f), new Vector3(half * 2f + 0.8f, 2f, 3.4f));
        foreach (var path in new[] { "MensiChata/ExterierMaleChaty/Okna", "KenneyDekorace/MalaChataInterier" })
        {
            var group = map.Find(path);
            if (group == null) continue;
            foreach (Transform child in group)
            {
                var rs = child.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.Intersects(doorway) || (path.EndsWith("Okna") && b.center.z < -8.5f && Mathf.Abs(b.center.x - doorX) < half + 1f))
                    child.gameObject.SetActive(false);
            }
        }

        // ram dveri
        float floorY = Ground(new Vector3(doorX, 0f, -7f), 5f);
        MapBuildKit.Box(g, "Zarubne", new Vector3(doorX - half - 0.06f, floorY + height * 0.5f, -9.35f), new Vector3(0.12f, height, 0.2f), mats["boards"], false);
        MapBuildKit.Box(g, "Zarubne", new Vector3(doorX + half + 0.06f, floorY + height * 0.5f, -9.35f), new Vector3(0.12f, height, 0.2f), mats["boards"], false);
        MapBuildKit.Box(g, "Zarubne", new Vector3(doorX, floorY + height + 0.06f, -9.35f), new Vector3(half * 2f + 0.24f, 0.12f, 0.2f), mats["boards"], false);

        // rampa z louky na terasu (stoupani asi 27 stupnu)
        Vector3 upper = new Vector3(doorX, floorY, -11.1f);
        Vector3 lower = new Vector3(doorX, Ground(new Vector3(doorX, 0f, -17.2f), 3f), -17.2f);
        Vector3 along = upper - lower;
        var rot = Quaternion.LookRotation(along.normalized, Vector3.up);
        MapBuildKit.Box(g, "Rampa", (upper + lower) * 0.5f + rot * new Vector3(0f, -0.12f, 0f), new Vector3(2.2f, 0.25f, along.magnitude + 0.3f),
            rot, mats["planks"], true);
        foreach (float side in new[] { -1.15f, 1.15f })
            MapBuildKit.Beam(g, "Zabradli", lower + new Vector3(side, 1f, 0f), upper + new Vector3(side, 1f, 0f), 0.08f, mats["beam"]);
    }

    // ---------------- obri tuje ----------------

    // Clona mezi rozhlednou a obema chatami: rady 9-10m tuji s mezerami na pruchod.
    static void GiantThujas(Transform root)
    {
        var thuja = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/House/Thuja.mat");
        var cone = MapBuildKit.ConeMesh();
        if (thuja == null || cone == null) return;

        // rozhledna <-> velka chata: rada severne od chaty (mezi bazenem a kruhovou plochou)
        foreach (var x in new[] { 3f, 5.6f, 8.2f, 14.5f, 16.9f, 19.3f, 23.6f, 26f })
            Thuja(root, thuja, cone, new Vector3(x, 0f, 20.5f + Mathf.Sin(x) * 0.4f), 10f, 3f);
        // rozhledna <-> mala chata: na vyvysene terase severne od chaty, pruchod uprostred
        foreach (var x in new[] { -44f, -41.5f, -39f, -33f, -30.5f, -28f })
            Thuja(root, thuja, cone, new Vector3(x, 0f, 33f + Mathf.Sin(x) * 0.4f), 9f, 8f);
    }

    static void Thuja(Transform root, Material material, Mesh cone, Vector3 foot, float height, float fromY)
    {
        var t = Object(root, "ObriTuje", foot, (foot.x * 37f) % 360f, fromY);
        var lower = MapBuildKit.MeshObject(t, "Spodek", cone, t.position, t.rotation, new Vector3(1.5f, height * 0.65f, 1.5f), material);
        var upper = MapBuildKit.MeshObject(t, "Vrsek", cone, t.position + Vector3.up * height * 0.35f, t.rotation * Quaternion.Euler(0f, 40f, 0f),
            new Vector3(1.1f, height * 0.65f, 1.1f), material);
        // Kolize presne podle tvaru (konvexni kuzel) - drive valec, ktery nahore prekazel a dole byl uzsi.
        foreach (var part in new[] { lower, upper })
        {
            var collider = part.AddComponent<MeshCollider>();
            collider.sharedMesh = cone;
            collider.convex = true;
        }
    }

    // ---------------- balicky ----------------

    static void Pickups(Transform group, Transform root)
    {
        var list = new (PickupKind kind, float x, float z, float fromY)[]
        {
            (PickupKind.Health, 19f, 4f, 2.5f),        // obyvak velke chaty
            (PickupKind.Health, -32f, 14f, 6f),        // terasa male chaty
            (PickupKind.Health, -18f, 50f, 10f),       // plosina pod rozhlednou
            (PickupKind.Health, 3.5f, 62f, 10f),       // plosina, vychod
            (PickupKind.Health, 17f, 32.5f, 3f),       // kruhova plocha
            (PickupKind.Health, -6f, 80f, 3f),         // zahrada (sever je kratsi; vedle jezirka)
            (PickupKind.Health, -2f, -10f, 3f),        // jih louky
            (PickupKind.BigHealth, -28f, 58f, 3f),     // zapadni bocni cesta pod plosinou
            (PickupKind.Power, -12f, 67f, 10f),        // slivovice: plosina u schodu rozhledny
            (PickupKind.Speed, -20f, -28f, 3f),        // pivo: jihozapad
            (PickupKind.Shield, 31f, 26f, 3f),         // stit: vychodni bocni cesta
        };
        int n = 1;
        foreach (var (kind, x, z, fromY) in list)
        {
            var go = new GameObject($"Balicek_{n:00}_{kind}");
            go.transform.SetParent(group, false);
            go.transform.position = new Vector3(x, Ground(new Vector3(x, 0f, z), fromY, root) + 0.02f, z);
            go.AddComponent<PickupSpot>().kind = kind;
            n++;
        }
    }
}
