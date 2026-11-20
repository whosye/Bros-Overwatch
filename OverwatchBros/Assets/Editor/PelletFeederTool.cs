using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Podavac pelet pro kotel (PelletFeeder): drevena bouda jako mala sauna - prkenne steny na kamenne podezdivce,
// sindelova sedlova strisska, dvirka, vpredu nasypka s odklopenym vikem (pelety se v ni ukazou, kdyz je plny),
// na boku motor dmychadla s vrtuli a trubka ven strechou (vzduchem tlaci pelety ke kotli). Vse pevne.
// Postavi se na podlahu tam, kam miri stred okna Scene (nebo k vybranemu objektu), celem ke kamere.
// Na mape je jen jeden; "prestavet" postavi novy vzhled na miste stavajiciho. Ctrl+Z vrati, ulozit Ctrl+S.
public static class PelletFeederTool
{
    const string RootName = "PodavacPelet";
    const string MatDir = "Assets/Materials/Feeder/";

    // rozmery boudy (lokalne, +z = celo ke hraci)
    const float W = 1.5f, D = 1.3f, H = 1.9f, BaseTop = 0.12f, Rise = 0.45f, Overhang = 0.25f;

    [MenuItem("BrosOverwatch/Mapa/Podavac pelet ke kotli (kam miri stred okna Scene)")]
    static void PlaceAtView()
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null || view.camera == null) { Debug.LogWarning("[Podavac] Otevri okno Scene a podivej se na misto, kam ma podavac prijit."); return; }
        Transform cam = view.camera.transform;
        Physics.SyncTransforms();
        if (!Physics.Raycast(cam.position, cam.forward, out var hit, 300f, ~0, QueryTriggerInteraction.Ignore))
        {
            Debug.LogWarning("[Podavac] Stred okna Scene nemiri na zadny povrch (podlahu, zed) - priblizi se k mistu a zkus znovu.");
            return;
        }
        // miri na zed / bok neceho: bouda se postavi na podlahu pred tim
        Vector3 target = hit.normal.y >= 0.7f ? hit.point : hit.point + new Vector3(hit.normal.x, 0f, hit.normal.z).normalized * 1.1f;
        Build(target, cam.position);
    }

    [MenuItem("BrosOverwatch/Mapa/Podavac pelet ke kotli (na misto vybraneho objektu)")]
    static void PlaceAtSelection()
    {
        var selected = Selection.activeTransform;
        if (selected == null) { Debug.LogWarning("[Podavac] Nejdriv vyber v Hierarchy/Scene objekt, u ktereho ma podavac stat."); return; }
        var view = SceneView.lastActiveSceneView;
        Vector3 from = view != null && view.camera != null ? view.camera.transform.position : selected.position + selected.forward * 3f;
        Build(selected.position, from);
    }

    [MenuItem("BrosOverwatch/Mapa/Podavac pelet - prestavet (novy vzhled, stejne misto)")]
    static void Rebuild()
    {
        var existing = Object.FindAnyObjectByType<PelletFeeder>(FindObjectsInactive.Include);
        var map = FindMap();
        if (existing == null || map == null) { Debug.LogWarning("[Podavac] Na mape neni zadny podavac pelet - vloz ho pres 'Podavac pelet ke kotli'."); return; }
        Vector3 foot = existing.transform.position;
        Quaternion rotation = Quaternion.Euler(0f, existing.transform.eulerAngles.y, 0f);
        Undo.DestroyObjectImmediate(existing.gameObject);
        Physics.SyncTransforms();
        try { BuildAt(map, foot, rotation); }
        catch (System.Exception e) { Debug.LogException(e); Debug.LogWarning("[Podavac] Prestavba selhala (chyba vyse) - Ctrl+Z vrati puvodni."); }
    }

    static GameObject FindMap()
    {
        foreach (var rootObject in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (rootObject.name == "Map-Domasov") return rootObject;
        return null;
    }

    // Postavi podavac na podlahu pod/u bodu 'target', celem k 'faceTowards'.
    static void Build(Vector3 target, Vector3 faceTowards)
    {
        if (Application.isPlaying) { Debug.LogWarning("[Podavac] Nejdriv vypni hru."); return; }
        var existing = Object.FindAnyObjectByType<PelletFeeder>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.LogWarning("[Podavac] Podavac pelet uz na mape je (vybral jsem ho) - presun ho W/E, nebo 'Podavac pelet - prestavet', " +
                "nebo ho smaz a vloz znovu.", existing);
            return;
        }
        var map = FindMap();
        if (map == null) { Debug.LogWarning("[Podavac] Ve scene neni Map-Domasov."); return; }

        // podlaha: prvni vodorovna plocha pod bodem
        Physics.SyncTransforms();
        Vector3? floor = null;
        var hits = Physics.RaycastAll(target + Vector3.up * 1.5f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
            if (h.normal.y >= 0.7f) { floor = h.point; break; }
        if (floor == null)
        {
            Debug.LogWarning($"[Podavac] Pod mistem {target} jsem nenasel podlahu - namir na podlahu nebo vyber objekt, ktery stoji na zemi.");
            return;
        }

        Vector3 toCamera = faceTowards - floor.Value;
        toCamera.y = 0f;
        var rotation = Quaternion.LookRotation(toCamera.sqrMagnitude > 0.01f ? toCamera.normalized : Vector3.forward, Vector3.up);
        try { BuildAt(map, floor.Value, rotation); }
        catch (System.Exception e) { Debug.LogException(e); Debug.LogWarning("[Podavac] Stavba selhala (chyba vyse) - posli mi ji."); }
    }

    static void BuildAt(GameObject map, Vector3 foot, Quaternion rotation)
    {
        Vector3 L(float x, float y, float z) => foot + rotation * new Vector3(x, y, z);
        Quaternion R(float x, float y, float z) => rotation * Quaternion.Euler(x, y, z);

        var planks = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wood/Wood_Planks.mat");
        var dark = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/House/DarkBoards.mat");
        var shingles = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wood/Wood_Shingles.mat");
        var stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Stone/Stone_Wall.mat");
        var gable = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/House/SmallCottage_Gable.mat");
        var glass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/House/Glass.mat");
        var metal = MapBuildKit.Mat(MatDir + "FeederPlech.mat", null, new Color(0.58f, 0.6f, 0.62f), Vector2.one, 0.5f);
        var motor = MapBuildKit.Mat(MatDir + "FeederTelo.mat", null, new Color(0.18f, 0.42f, 0.28f), Vector2.one, 0.45f);
        var black = MapBuildKit.Mat(MatDir + "FeederHadice.mat", null, new Color(0.12f, 0.12f, 0.13f), Vector2.one, 0.25f);
        var pellets = MapBuildKit.Mat(MatDir + "Pelety.mat", null, new Color(0.66f, 0.47f, 0.25f), Vector2.one, 0.05f);
        var warn = MapBuildKit.Mat(MatDir + "FeederVystraha.mat", null, new Color(1f, 0.78f, 0.1f), Vector2.one, 0.3f);
        var lamp = MapBuildKit.Mat(MatDir + "FeederKontrolka.mat", null, new Color(1f, 0.5f, 0.3f), Vector2.one, 0.6f,
            emission: new Color(1.4f, 0.6f, 0.3f));
        if (planks == null || dark == null || shingles == null || stone == null || gable == null || glass == null)
        {
            Debug.LogWarning("[Podavac] Chybi nektery material (Wood_Planks, DarkBoards, Wood_Shingles, Stone_Wall, SmallCottage_Gable, Glass).");
            return;
        }

        // misto by melo byt volne (jen upozorneni - bouda se postavi i tak, posun ji)
        Physics.SyncTransforms();
        foreach (var c in Physics.OverlapBox(L(0.2f, 1.3f, 0f), new Vector3(1.1f, 1.15f, 0.8f), rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            Debug.LogWarning($"[Podavac] V miste boudy neco stoji ({c.name}) - posun podavac, at nic neprekryva.", c);
            break;
        }

        var dekorace = map.transform.Find("Dekorace") ?? MapBuildKit.Group(map.transform, "Dekorace");
        var root = new GameObject(RootName).transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Podavac pelet");
        root.SetParent(dekorace, true);
        root.SetPositionAndRotation(foot, rotation);

        float eave = BaseTop + H;

        // ---- bouda: podezdivka, prkenne telo, rohove sloupky, lemy ----
        MapBuildKit.Box(root, "Podezdivka", L(0f, BaseTop * 0.5f, 0f), new Vector3(W + 0.2f, BaseTop, D + 0.2f), rotation, stone, true);
        MapBuildKit.Box(root, "Bouda", L(0f, BaseTop + H * 0.5f, 0f), new Vector3(W, H, D), rotation, planks, true);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                MapBuildKit.Box(root, "Sloupek", L(sx * W * 0.5f, BaseTop + H * 0.5f, sz * D * 0.5f), new Vector3(0.12f, H, 0.12f), rotation, dark, false, false);
        foreach (float y in new[] { BaseTop + 0.05f, eave - 0.05f })
            MapBuildKit.Box(root, "Lem", L(0f, y, 0f), new Vector3(W + 0.04f, 0.1f, D + 0.04f), rotation, dark, false);

        // ---- sedlova sindelova strecha, hreben, stity ----
        float half = D * 0.5f + Overhang;
        float slope = Mathf.Atan2(Rise + 0.05f, half) * Mathf.Rad2Deg;
        float slant = Mathf.Sqrt(half * half + (Rise + 0.05f) * (Rise + 0.05f));
        foreach (float s in new[] { -1f, 1f })
            MapBuildKit.Box(root, "Strecha", L(0f, eave + Rise * 0.5f - 0.02f, s * half * 0.5f), new Vector3(W + 0.45f, 0.07f, slant + 0.06f),
                R(s * slope, 0f, 0f), shingles, true);
        MapBuildKit.Box(root, "Hreben", L(0f, eave + Rise + 0.03f, 0f), new Vector3(W + 0.5f, 0.1f, 0.12f), rotation, dark, true);
        foreach (float s in new[] { -1f, 1f })
        {
            float z = s * (D * 0.5f + 0.005f);
            var points = new[] { L(-W * 0.5f, eave, z), L(W * 0.5f, eave, z), L(0f, eave + Rise, z) };
            var uv = new[] { new Vector2(0f, 0f), new Vector2(W, 0f), new Vector2(W * 0.5f, Rise) };
            var mesh = MapBuildKit.FlatMesh("PodavacStit", points, uv);
            var go = MapBuildKit.MeshObject(root, "Stit", mesh, Vector3.zero, Quaternion.identity, Vector3.one, gable);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        // ---- dvirka (vlevo) s okenkem ----
        float doorX = -0.38f, front = D * 0.5f;
        MapBuildKit.Box(root, "Dvirka", L(doorX, BaseTop + 0.85f, front + 0.02f), new Vector3(0.62f, 1.6f, 0.04f), rotation, dark, false, false);
        MapBuildKit.Box(root, "Okenko", L(doorX, BaseTop + 1.3f, front + 0.045f), new Vector3(0.24f, 0.32f, 0.01f), rotation, glass, false);
        MapBuildKit.Box(root, "Klika", L(doorX + 0.22f, BaseTop + 0.85f, front + 0.06f), new Vector3(0.04f, 0.14f, 0.04f), rotation, black, false);
        foreach (float sx in new[] { -1f, 1f })
            MapBuildKit.Box(root, "Zarubne", L(doorX + sx * 0.34f, BaseTop + 0.85f, front + 0.025f), new Vector3(0.06f, 1.7f, 0.05f), rotation, planks, false, false);

        // ---- nasypka vpredu (vpravo): korytko z prken, odklopene viko, pelety, cedulka, kontrolka ----
        float chuteX = 0.38f, chuteY = BaseTop + 0.85f, chuteD = 0.45f;
        Vector3 chute = L(chuteX, chuteY, front + chuteD * 0.5f);
        MapBuildKit.Box(root, "Nasypka_Dno", L(chuteX, chuteY, front + chuteD * 0.5f), new Vector3(0.6f, 0.05f, chuteD), rotation, planks, true);
        MapBuildKit.Box(root, "Nasypka_Celo", L(chuteX, chuteY + 0.16f, front + chuteD - 0.02f), new Vector3(0.64f, 0.34f, 0.04f), rotation, planks, true);
        foreach (float sx in new[] { -1f, 1f })
            MapBuildKit.Box(root, "Nasypka_Bok", L(chuteX + sx * 0.3f, chuteY + 0.16f, front + chuteD * 0.5f), new Vector3(0.04f, 0.34f, chuteD), rotation, planks, true);
        MapBuildKit.Box(root, "Nasypka_Podpera", L(chuteX, (BaseTop + chuteY) * 0.5f, front + chuteD - 0.06f), new Vector3(0.08f, chuteY - BaseTop, 0.08f), rotation, dark, true);
        var hingeRotation = R(-75f, 0f, 0f);
        Vector3 hinge = L(chuteX, chuteY + 0.33f, front + 0.03f);
        MapBuildKit.Box(root, "Nasypka_Viko", hinge + hingeRotation * new Vector3(0f, 0f, chuteD * 0.5f), new Vector3(0.64f, 0.03f, chuteD), hingeRotation, dark, true);
        var full = MapBuildKit.Box(root, "Pelety", L(chuteX, chuteY + 0.13f, front + chuteD * 0.5f), new Vector3(0.54f, 0.2f, chuteD - 0.08f), rotation, pellets, false);
        GameObjectUtility.SetStaticEditorFlags(full, 0);   // zapina se a vypina za hry
        MapBuildKit.Box(root, "Cedulka", L(chuteX, BaseTop + 1.72f, front + 0.015f), new Vector3(0.5f, 0.16f, 0.02f), rotation, warn, false);
        MapBuildKit.Box(root, "Kontrolka", L(chuteX + 0.36f, BaseTop + 1.72f, front + 0.02f), new Vector3(0.08f, 0.08f, 0.03f), rotation, lamp, false);
        var light = new GameObject("Svetlo_Kontrolka").AddComponent<Light>();
        light.transform.SetParent(root, true);
        light.transform.position = L(chuteX + 0.36f, BaseTop + 1.72f, front + 0.2f);
        light.type = LightType.Point;
        light.color = new Color(1f, 0.25f, 0.15f);
        light.range = 2.5f;
        light.intensity = 0.8f;
        light.shadows = LightShadows.None;

        // ---- motor dmychadla na pravem boku: podstavec, motor, svorkovnice, kryt a vrtule ----
        float mx = W * 0.5f + 0.3f, my = 0.66f;
        MapBuildKit.Box(root, "Motor_Podstavec", L(mx, 0.22f, 0f), new Vector3(0.44f, 0.44f, 0.56f), rotation, black, true);
        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.name = "Motor";
        body.transform.SetParent(root, true);
        body.transform.SetPositionAndRotation(L(mx, my, -0.02f), R(90f, 0f, 0f));
        body.transform.localScale = new Vector3(0.4f, 0.28f, 0.4f);
        body.GetComponent<Renderer>().sharedMaterial = motor;
        for (int i = 0; i < 4; i++)
            MapBuildKit.Box(root, "Motor_Zebro", L(mx, my + 0.2f, -0.2f + i * 0.12f), new Vector3(0.3f, 0.03f, 0.03f), rotation, motor, false);
        MapBuildKit.Box(root, "Motor_Svorkovnice", L(mx + 0.17f, my + 0.08f, -0.05f), new Vector3(0.1f, 0.14f, 0.16f), rotation, black, false);
        var cover = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cover.name = "Motor_Kryt";
        cover.transform.SetParent(root, true);
        cover.transform.SetPositionAndRotation(L(mx, my, 0.29f), R(90f, 0f, 0f));
        cover.transform.localScale = new Vector3(0.46f, 0.04f, 0.46f);
        cover.GetComponent<Renderer>().sharedMaterial = black;
        var fan = new GameObject("Vrtule").transform;
        fan.SetParent(root, true);
        fan.SetPositionAndRotation(L(mx, my, 0.34f), rotation);
        foreach (var size in new[] { new Vector3(0.38f, 0.06f, 0.01f), new Vector3(0.06f, 0.38f, 0.01f) })
        {
            var blade = MapBuildKit.Box(fan, "Lopatka", fan.position, size, fan.rotation, metal, false);
            GameObjectUtility.SetStaticEditorFlags(blade, 0);   // toci se za hry
        }
        // trubka od motoru do boudy
        MapBuildKit.Beam(root, "Trubka_Motor", L(mx, my + 0.2f, -0.2f), L(mx, 1.35f, -0.2f), 0.11f, metal, true);
        MapBuildKit.Beam(root, "Trubka_Motor", L(mx, 1.35f, -0.2f), L(W * 0.5f, 1.35f, -0.2f), 0.11f, metal, true);
        MapBuildKit.Box(root, "Koleno", L(mx, 1.35f, -0.2f), new Vector3(0.15f, 0.15f, 0.15f), rotation, metal, false);

        // ---- trubka ven strechou (ke kotli): do stropu, nebo nad strechu s kloboukem ----
        Vector3 pipeBase = L(-0.35f, eave, -0.3f);
        bool toCeiling = Physics.Raycast(pipeBase + Vector3.up * (Rise + 0.4f), Vector3.up, out var ceiling, 4f, ~0, QueryTriggerInteraction.Ignore);
        float top = toCeiling ? ceiling.point.y : eave + Rise + 1.2f;
        MapBuildKit.Beam(root, "Trubka_Ven", pipeBase, new Vector3(pipeBase.x, top, pipeBase.z), 0.16f, metal, true);
        MapBuildKit.Box(root, "Trubka_Lemovani", L(-0.35f, eave + Rise * 0.6f, -0.3f), new Vector3(0.3f, 0.06f, 0.3f), rotation, metal, false);
        if (!toCeiling)
        {
            MapBuildKit.Box(root, "Trubka_Klobouk", new Vector3(pipeBase.x, top + 0.12f, pipeBase.z), new Vector3(0.34f, 0.04f, 0.34f), rotation, metal, true);
            foreach (float sx in new[] { -1f, 1f })
                MapBuildKit.Box(root, "Trubka_Drzak", new Vector3(pipeBase.x, top + 0.06f, pipeBase.z) + rotation * new Vector3(sx * 0.1f, 0f, 0f),
                    new Vector3(0.02f, 0.12f, 0.02f), rotation, metal, false);
        }

        var usePoint = new GameObject("Nasypka").transform;
        usePoint.SetParent(root, true);
        usePoint.position = chute + Vector3.up * 0.3f;

        var feeder = root.gameObject.AddComponent<PelletFeeder>();
        feeder.usePoint = usePoint;
        feeder.fullContent = full;
        feeder.statusLight = light;
        feeder.fan = fan;
        full.SetActive(false);

        Selection.activeGameObject = root.gameObject;
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Debug.Log($"[Podavac] Podavac pelet (bouda s motorem) postaven na {foot} - posun/otoc ho podle potreby (W/E) a uloz scenu (Ctrl+S).", root);
    }
}
