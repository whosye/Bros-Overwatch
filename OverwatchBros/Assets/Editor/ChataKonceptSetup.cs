using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Nove rozvrzeni chat pro rezim utok a obrana (navrh "Chata: ctyri body a tunel"):
//   A = pata rozhledny, B = mala chata, C = prizemi velke chaty, D = 1. patro velke chaty.
//   Utocnici zacinaji na severu za rozhlednou a postupuji na jih; obranci od C brani z jihu za velkou chatou.
//   Hranice zon (ZoneGate): sever | stred na z = 40 (branka na silnici, kudy jezdi auto, a branka u zapadniho okraje),
//   stred | velka chata na x = -5 a z = 20. Branky se otevrou po bodu A, cele hranice zmizi po bodu B.
//   Mrize v tunelu: pod rozhlednou (otevre se po A) a za vylezem u sena (po B).
//   Kryty v zahrade u rozhledny (kulna se strechou, senik, hromada dreva) a val u hranice stredu.
//   Snezna rolba na louku k male chate, karburator do tunelu.
// Kazda cast se postavi jen jednou (znacka v Map-Domasov/KonceptUtoku) - rucni upravy zustanou.
// Po stavbe se mapa vyfoti shora do Temp/ChataKoncept.png (kontrola rozvrzeni). Scenu je pak potreba ulozit (Ctrl+S).
[InitializeOnLoad]
public static class ChataKonceptSetup
{
    const string MapName = "Map-Domasov";
    const string RootName = "KonceptUtoku";
    const string MatDir = "Assets/Materials/Koncept/";
    const float FenceHeight = 1.7f;
    const float WallTop = 25f;          // neviditelna stena nad plotem (proti skokum, schopnostem, toboganu)

    // hranice hratelne plochy (les) - plot vede az do nich
    const float WestX = -56f, EastX = 48f, SouthZ = -42f;

    static Material plank, post, metal, hay, logs, roof, shed;

    static ChataKonceptSetup()
    {
        EditorApplication.delayCall += RunIfNeeded;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += RunIfNeeded;
    }

    static GameObject FindMap()
    {
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == MapName) return go;
        return null;
    }

    static void RunIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var map = FindMap();
        if (map == null) return;
        if (Apply(map.transform))
        {
            EditorSceneManager.MarkSceneDirty(map.scene);
            Snapshot(map.transform);
            ChataNovaCast.Snapshots(map.transform);
            if (ChataNovaCast.AuditPending) ChataNovaCast.Audit();
            Debug.Log("[Koncept] Mapa chat upravena podle navrhu (body, spawny, hranice zon, tunel, kryty). Uloz scenu (Ctrl+S). Nahledy: Temp/Koncept_*.png");
        }
    }

    [MenuItem("BrosOverwatch/Mapa/Koncept utoku - nahled shora")]
    static void SnapshotMenu()
    {
        var map = FindMap();
        if (map == null) return;
        Snapshot(map.transform);
        ChataNovaCast.Snapshots(map.transform);
    }

    static bool Apply(Transform map)
    {
        if (!LoadMaterials()) return false;
        var root = MapBuildKit.Group(map, RootName);
        bool changed = false;
        changed |= Feature(root, "Body", () => Points(map));
        changed |= Feature(root, "Spawny", () => Spawns(map));
        changed |= Feature(root, "Hranice", () => Borders(root));
        changed |= Feature(root, "TunelMrize", () => TunnelGrates(root));
        changed |= Feature(root, "Kryty", () => Covers(root));
        changed |= Feature(root, "RolbaKarburator", () => GroomerAndCarburetor(map));
        // spawn utoku pro A byl za radou stromu na severnim okraji - pred ni
        changed |= Feature(root, "SpawnUtokA_v2", () => Marker(map, "SpawnPoint_Utok_A", new Vector3(-2f, 0f, 88f)));
        // druha verze: nova zapadni cast, orez severu, diry, tunel, skluzavka, kulna pro rolbu
        changed |= ChataNovaCast.Apply(map, root, Feature);
        return changed;
    }

    static bool Feature(Transform root, string name, System.Action build)
    {
        string marker = "_Prvek_" + name;
        if (root.Find(marker) != null) return false;
        build();
        new GameObject(marker).transform.SetParent(root, false);
        return true;
    }

    static bool LoadMaterials()
    {
        plank = MapBuildKit.Mat(MatDir + "Plot.mat", "Assets/Materials/Wood/WoodPlanks.png", new Color(0.78f, 0.62f, 0.45f), new Vector2(0.5f, 0.5f), 0.15f);
        post = MapBuildKit.Mat(MatDir + "Sloupek.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.45f, 0.33f, 0.24f), new Vector2(0.5f, 0.5f), 0.15f);
        metal = MapBuildKit.Mat(MatDir + "Mriz.mat", null, new Color(0.28f, 0.29f, 0.31f), Vector2.one, 0.6f);
        hay = MapBuildKit.Mat(MatDir + "Seno.mat", null, new Color(0.86f, 0.74f, 0.38f), Vector2.one, 0.05f);
        logs = MapBuildKit.Mat(MatDir + "Polena.mat", "Assets/Materials/Garden/Bark.png", new Color(0.8f, 0.68f, 0.58f), Vector2.one, 0.05f);
        roof = MapBuildKit.Mat(MatDir + "StrechaKulny.mat", "Assets/Materials/Wood/WoodShingles.png", new Color(0.55f, 0.5f, 0.48f), new Vector2(0.6f, 0.6f), 0.2f);
        shed = MapBuildKit.Mat(MatDir + "Kulna.mat", "Assets/Materials/Wood/WoodPlanks.png", new Color(0.62f, 0.48f, 0.36f), new Vector2(0.5f, 0.5f), 0.15f);
        return plank != null && post != null && metal != null && hay != null && logs != null && roof != null && shed != null;
    }

    static Transform FindDeep(Transform parent, string name)
    {
        foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static float GroundY(Vector3 at, float fallback = 0f)
    {
        foreach (var hit in Physics.RaycastAll(at + Vector3.up * 30f, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.normal.y > 0.5f && hit.point.y < at.y + 25f) return hit.point.y;
        return fallback;
    }

    // ---------------- body a spawny ----------------

    static void Marker(Transform map, string name, Vector3 position, Vector3? scale = null)
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
        t.rotation = Quaternion.identity;
        if (scale.HasValue)
        {
            Vector3 parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(scale.Value.x / parentScale.x, scale.Value.y / parentScale.y, scale.Value.z / parentScale.z);
        }
    }

    static void Points(Transform map)
    {
        // A: pata rozhledny (plosina nahore se do bodu nepocita - bod meri jen 4 m nad zemi)
        Marker(map, "CapturePoint_1", new Vector3(8.5f, 0.5f, 58.6f), new Vector3(14f, 1f, 14f));
        // B: mala chata (prizemi)
        Marker(map, "CapturePoint_2", new Vector3(-37.3f, 4.6f, -1.8f), new Vector3(12f, 1f, 12f));
        // C: velka chata, obyvak v prizemi
        Marker(map, "CapturePoint_3", new Vector3(18f, 2f, 5.5f), new Vector3(9f, 1f, 8f));
        // D: velka chata, 1. patro (znacka nad podlahou patra - bod si sedne na prvni podlahu pod ni)
        Marker(map, "CapturePoint_4", new Vector3(17f, 5.2f, -4f), new Vector3(10f, 1f, 10f));
    }

    static void Spawns(Transform map)
    {
        // utocnici: sever za rozhlednou -> u rozhledny -> za malou chatou (C i D)
        Marker(map, "SpawnPoint_Utok_A", new Vector3(-2f, 0f, 88f));
        Marker(map, "SpawnPoint_Utok_B", new Vector3(8f, 0f, 76f));
        Marker(map, "SpawnPoint_Utok_C", new Vector3(-40f, 0f, 26f));
        Marker(map, "SpawnPoint_Utok_D", new Vector3(-40f, 0f, 26f));
        // obranci: u hranice severni zony -> jizne od male chaty -> jizne za velkou chatou (C i D)
        Marker(map, "SpawnPoint_Obrana_A", new Vector3(-22f, 0f, 45f));
        Marker(map, "SpawnPoint_Obrana_B", new Vector3(-40f, 0f, -20f));
        Marker(map, "SpawnPoint_Obrana_C", new Vector3(18f, 0f, -30f));
        Marker(map, "SpawnPoint_Obrana_D", new Vector3(18f, 0f, -30f));
    }

    // ---------------- hranice zon ----------------

    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material, bool collider = true, Quaternion? rotation = null)
    {
        var go = MapBuildKit.Box(parent, name, center, size, rotation ?? Quaternion.identity, material, collider);
        GameObjectUtility.SetStaticEditorFlags(go, 0);   // vypina se za behu
        return go;
    }

    static void Invisible(Transform parent, string name, Vector3 min, Vector3 max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = (min + max) * 0.5f;
        go.AddComponent<BoxCollider>().size = max - min;
    }

    static Transform GateGroup(Transform parent, string name, int unlockAfter, int removeAfter = 0)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var gate = go.AddComponent<ZoneGate>();
        gate.unlockAfter = unlockAfter;
        gate.removeAfter = removeAfter;
        return go.transform;
    }

    // Plot podel osy x (z = konst.) nebo z (x = konst.) od 'from' do 'to', s mezerami (branami) 'gaps' (od, do).
    static void Fence(Transform parent, string name, bool alongX, float fixedCoord, float from, float to, (float a, float b)[] gaps,
        (float a, float b)[] slideWindows = null)
    {
        var fence = GateGroup(parent, name, 2);   // plot zmizi po bodu B
        var wall = GateGroup(parent, name + "_Stena", 2);
        var window = slideWindows != null ? GateGroup(parent, name + "_StenaTobogan", 1) : null;

        var cuts = new List<(float a, float b)>(gaps);
        cuts.Sort((x, y) => x.a.CompareTo(y.a));
        float cursor = from;
        var spans = new List<(float a, float b)>();
        foreach (var g in cuts)
        {
            if (g.a > cursor) spans.Add((cursor, g.a));
            cursor = Mathf.Max(cursor, g.b);
        }
        if (cursor < to) spans.Add((cursor, to));

        Vector3 At(float along, float y) => alongX ? new Vector3(along, y, fixedCoord) : new Vector3(fixedCoord, y, along);

        foreach (var span in spans)
        {
            for (float s = span.a; s < span.b - 0.01f; s += 5f)
            {
                float e = Mathf.Min(span.b, s + 5f);
                float mid = (s + e) * 0.5f, len = e - s;
                Vector3 size = alongX ? new Vector3(len, FenceHeight - 0.2f, 0.08f) : new Vector3(0.08f, FenceHeight - 0.2f, len);
                Box(fence, "Prkna", At(mid, 0.1f + (FenceHeight - 0.2f) * 0.5f), size, plank);
                Box(fence, "Sloupek", At(s, FenceHeight * 0.5f), new Vector3(0.16f, FenceHeight, 0.16f), post, false);
            }
            Box(fence, "Sloupek", At(span.b, FenceHeight * 0.5f), new Vector3(0.16f, FenceHeight, 0.16f), post, false);

            // neviditelna stena nad plotem (az do WallTop), okno pro tobogan zvlast (otevre se uz po A)
            var pieces = new List<(float a, float b, bool slide)> { (span.a, span.b, false) };
            if (slideWindows != null)
                foreach (var w in slideWindows)
                {
                    var next = new List<(float a, float b, bool slide)>();
                    foreach (var p in pieces)
                    {
                        if (p.slide || w.b <= p.a || w.a >= p.b) { next.Add(p); continue; }
                        if (w.a > p.a) next.Add((p.a, w.a, false));
                        next.Add((Mathf.Max(p.a, w.a), Mathf.Min(p.b, w.b), true));
                        if (w.b < p.b) next.Add((w.b, p.b, false));
                    }
                    pieces = next;
                }
            foreach (var p in pieces)
            {
                Vector3 min = alongX ? new Vector3(p.a, FenceHeight, fixedCoord - 0.1f) : new Vector3(fixedCoord - 0.1f, FenceHeight, p.a);
                Vector3 max = alongX ? new Vector3(p.b, WallTop, fixedCoord + 0.1f) : new Vector3(fixedCoord + 0.1f, WallTop, p.b);
                Invisible(p.slide ? window : wall, "Stena", min, max);
            }
        }
    }

    // Branka: zavrena kridla + neviditelna stena nad ni; po otevreni kridla otocena (bez kolize), po bodu B zmizi.
    static void Gate(Transform parent, string name, bool alongX, float fixedCoord, float a, float b, int unlockAfter)
    {
        var gate = GateGroup(parent, name, unlockAfter, 2);
        var closed = new GameObject("Zavreno").transform;
        closed.SetParent(gate, false);
        var open = new GameObject("Otevreno").transform;
        open.SetParent(gate, false);

        float width = b - a, half = width * 0.5f;
        Vector3 At(float along, float y) => alongX ? new Vector3(along, y, fixedCoord) : new Vector3(fixedCoord, y, along);
        Vector3 leaf = alongX ? new Vector3(half - 0.05f, FenceHeight - 0.25f, 0.1f) : new Vector3(0.1f, FenceHeight - 0.25f, half - 0.05f);
        Box(closed, "Kridlo_L", At(a + half * 0.5f, 0.15f + leaf.y * 0.5f), leaf, plank);
        Box(closed, "Kridlo_P", At(b - half * 0.5f, 0.15f + leaf.y * 0.5f), leaf, plank);
        Vector3 min = alongX ? new Vector3(a, FenceHeight, fixedCoord - 0.1f) : new Vector3(fixedCoord - 0.1f, FenceHeight, a);
        Vector3 max = alongX ? new Vector3(b, WallTop, fixedCoord + 0.1f) : new Vector3(fixedCoord + 0.1f, WallTop, b);
        Invisible(closed, "Stena", min, max);

        // otevrena kridla: otocena o 90 st. ke strane (na stranu utocniku - na sever / na zapad)
        Vector3 side = alongX ? Vector3.forward : Vector3.left;
        Vector3 hingeA = At(a, 0f), hingeB = At(b, 0f);
        Vector3 openLeaf = alongX ? new Vector3(0.1f, leaf.y, half - 0.05f) : new Vector3(half - 0.05f, leaf.y, 0.1f);
        Box(open, "Kridlo_L", hingeA + side * (half * 0.5f) + Vector3.up * (0.15f + leaf.y * 0.5f), openLeaf, plank, false);
        Box(open, "Kridlo_P", hingeB + side * (half * 0.5f) + Vector3.up * (0.15f + leaf.y * 0.5f), openLeaf, plank, false);

        Box(gate, "Sloup_L", At(a, (FenceHeight + 0.4f) * 0.5f), new Vector3(0.22f, FenceHeight + 0.4f, 0.22f), post, false);
        Box(gate, "Sloup_P", At(b, (FenceHeight + 0.4f) * 0.5f), new Vector3(0.22f, FenceHeight + 0.4f, 0.22f), post, false);
    }

    static void Borders(Transform root)
    {
        var g = MapBuildKit.Group(root, "Hranice");
        // sever | stred (z = 40): branka na silnici (x 5..14, tudy jede auto) a branka u zapadniho okraje (x -47..-43);
        // okno pro tobogan z rozhledny k bazenu (x 14..34) - neviditelna stena se tam otevre uz po bodu A
        Fence(g, "Plot_Sever", true, 40f, WestX, EastX, new[] { (5f, 14f), (-47f, -43f), (18f, 31f) }, new[] { (14f, 34f) });
        // pruchod pro tobogan / skluzavku u bazenu (x 18..31): bez plotu, jen neviditelna stena az k zemi - otevre se po A
        Invisible(GateGroup(g, "Tobogan_Pruchod", 1), "Stena", new Vector3(18f, 0f, 39.9f), new Vector3(31f, FenceHeight, 40.1f));
        Gate(g, "Branka_Silnice", true, 40f, 5f, 14f, 1);
        Gate(g, "Branka_Zapad", true, 40f, -47f, -43f, 1);
        // stred | velka chata: x = -5 (od jizniho okraje k z = 20) a z = 20 (od x = -5 k vychodnimu okraji)
        Fence(g, "Plot_Chata_Zapad", false, -5f, SouthZ, 20f, new (float, float)[0]);
        Fence(g, "Plot_Chata_Sever", true, 20f, -5f, EastX, new (float, float)[0]);
    }

    // ---------------- tunel ----------------

    static void Grate(Transform parent, string name, float z, int unlockAfter)
    {
        var gate = GateGroup(parent, name, unlockAfter, 0);
        var closed = new GameObject("Zavreno").transform;
        closed.SetParent(gate, false);
        const float floor = -4f, top = -0.4f;
        for (float x = -1.8f; x <= 1.81f; x += 0.3f)
            Box(closed, "Tyc", new Vector3(x, (floor + top) * 0.5f, z), new Vector3(0.06f, top - floor, 0.06f), metal, false);
        Box(closed, "Prickla", new Vector3(0f, -1.2f, z), new Vector3(3.8f, 0.08f, 0.08f), metal, false);
        Box(closed, "Prickla", new Vector3(0f, -3.2f, z), new Vector3(3.8f, 0.08f, 0.08f), metal, false);
        Invisible(closed, "Kolize", new Vector3(-2.2f, floor - 0.2f, z - 0.1f), new Vector3(2.2f, top + 0.3f, z + 0.1f));
    }

    static void TunnelGrates(Transform root)
    {
        var g = MapBuildKit.Group(root, "TunelMrize");
        // hlavni chodba tunelu vede po ose x = 0: mriz mezi rozhlednou a vylezem u sena, druha mezi senem a chatou
        Grate(g, "Mriz_Rozhledna", 66f, 1);
        Grate(g, "Mriz_Chata", 26f, 2);
    }

    // ---------------- kryty ----------------

    static void Covers(Transform root)
    {
        var g = MapBuildKit.Group(root, "Kryty");
        // zahrada u rozhledny: kulna (strecha = vyvysene misto utocniku, vylezt pres bednu), senik, hromada dreva
        float y = GroundY(new Vector3(-6f, 0f, 80f));
        Box(g, "Kulna", new Vector3(-6f, y + 1.1f, 80f), new Vector3(4.5f, 2.2f, 3.5f), shed);
        Box(g, "Kulna_Strecha", new Vector3(-6f, y + 2.3f, 80f), new Vector3(5.2f, 0.2f, 4.2f), roof);
        Box(g, "Kulna_Bedna", new Vector3(-3.1f, y + 0.55f, 81.3f), new Vector3(1.1f, 1.1f, 1.1f), shed);
        y = GroundY(new Vector3(19f, 0f, 77f));
        Box(g, "Senik_1", new Vector3(19f, y + 0.75f, 77f), new Vector3(3.2f, 1.5f, 1.6f), hay);
        Box(g, "Senik_2", new Vector3(19.8f, y + 2.0f, 77f), new Vector3(1.6f, 1.0f, 1.6f), hay);
        y = GroundY(new Vector3(-18f, 0f, 63f));
        for (int i = 0; i < 3; i++)
        {
            var log = Box(g, "Poleno", new Vector3(-18f, y + 0.35f + i * 0.55f, 63f + (i == 1 ? 0.2f : 0f)),
                new Vector3(4.2f - i * 0.6f, 0.55f, 1.6f - i * 0.3f), logs);
        }
        // (stred: jako kryt utocniku slouzi baliky sena u vylezu z tunelu, ktere uz na mape jsou)
    }

    // ---------------- rolba a karburator ----------------

    static void GroomerAndCarburetor(Transform map)
    {
        var groomer = FindDeep(map, "SneznaRolba");
        if (groomer != null)
        {
            Undo.RecordObject(groomer, "Koncept");
            Vector3 p = new Vector3(-18f, 0f, 25f);
            groomer.position = new Vector3(p.x, GroundY(p), p.z);
            groomer.rotation = Quaternion.Euler(0f, 90f, 0f);
        }
        else Debug.LogWarning("[Koncept] Snezna rolba (SneznaRolba) ve scene neni - nepresunuta.");

        var carburetor = FindDeep(map, "Balicek_Karburator");
        if (carburetor != null)
        {
            Undo.RecordObject(carburetor, "Koncept");
            float lift = carburetor.position.y - GroundY(carburetor.position, carburetor.position.y);
            carburetor.position = new Vector3(0f, -4f + Mathf.Clamp(lift, 0f, 1.5f), 50f);
        }
        else Debug.LogWarning("[Koncept] Karburator (Balicek_Karburator) ve scene neni - nepresunut.");
    }

    // ---------------- nahled shora ----------------

    static void Snapshot(Transform map)
    {
        var markers = new List<GameObject>();
        void Dot(Vector3 at, Color color, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = at + Vector3.up * 30f;
            go.transform.localScale = new Vector3(size, 0.1f, size);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            markers.Add(go);
        }
        for (int i = 1; i <= 4; i++)
        {
            var p = FindDeep(map, $"CapturePoint_{i}");
            if (p != null) Dot(p.position, new Color(1f, 0.55f, 0.1f), 3.5f + i * 0.4f);
        }
        foreach (var n in new[] { "A", "B", "C", "D" })
        {
            var a = FindDeep(map, "SpawnPoint_Utok_" + n);
            var d = FindDeep(map, "SpawnPoint_Obrana_" + n);
            if (a != null) Dot(a.position, new Color(0.9f, 0.15f, 0.1f), 2.5f);
            if (d != null) Dot(d.position, new Color(0.15f, 0.35f, 0.95f), 2.5f);
        }

        var camObject = new GameObject("KonceptKamera");
        var cam = camObject.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 92f;
        cam.transform.position = new Vector3(-4f, 120f, 36f);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.nearClipPlane = 1f;
        cam.farClipPlane = 300f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.2f, 0.22f, 0.2f);
        var rt = new RenderTexture(1400, 1600, 24);
        cam.targetTexture = rt;
        cam.aspect = 1400f / 1600f;
        try
        {
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes("Temp/ChataKoncept.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Koncept] Nahled shora se nepodaril: " + e.Message);
        }
        finally
        {
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camObject);
            foreach (var m in markers) Object.DestroyImmediate(m);
        }
    }
}
