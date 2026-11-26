using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Galerie "Grand Theft Domasov": odbocka z hlavni chodby tunelu (vychodni zed, jizne od workshopu - mezi nim a odbockou k rampe)
// do podzemni mistnosti s deseti plakaty (Assets/Materials/GrandTheftDomasov/gtd_01-10.png), neony a lavickou.
// Stavi se samo (po nacteni skriptu a po skonceni hry) v obou mapach s tunelem (Map-Domasov, Map_DomasovPush),
// a to jen jednou: znacka "_GTDGalerie_v1" v Tunelu. Kdyz galerii smazes, znovu se nepostavi (smaz i znacku).
// Pruchod: puvodni Wall_4 se vypne (zustava ve scene) a nahradi ji tri kusy s otvorem 2,5 x 2,7 m.
// Souradnice = mistni souradnice galerie = Tunel posunuty o Shift po z (vchod do galerie z = 12,75 + Shift = -9). Podlaha chodby -4,0, strop -0,5, zem nahore 0 (dlazdice do -0,3).
// Scenu je potreba ulozit (Ctrl+S).
[InitializeOnLoad]
public static class GrandTheftDomasovGallery
{
    const string RootName = "GrandTheftDomasov";
    const string Marker = "_GTDGalerie_v2";        // v2: galerie posunuta o 21,75 m na jih (dal od hudby workshopu)
    const string OldMarker = "_GTDGalerie_v1";     // puvodni poloha naproti workshopu - pri prvnim nacteni se odstrani
    const float Shift = -21.75f;                    // posun cele galerie po ose z (vchod z = -9, workshop ~18 m daleko)
    const string MusicMarker = "_GTDGalerie_hudba";   // hudba galerie (Assets/Audio/Music/grand_theft_domasov.wav)
    const string MusicPath = "Assets/Audio/Music/grand_theft_domasov.wav";
    const string ExtraMarker = "_GTDGalerie_plakaty_9_10";   // dalsi dva plakaty (pridane do uz postavene galerie)
    const string RampageMarker = "_GTDGalerie_rampage";     // lebka RAMPAGE (balicek) mezi lavickou a vychodni zdi
    const string Folder = "Assets/Materials/GrandTheftDomasov";
    static readonly string[] Maps = { "Map-Domasov", "Map_DomasovPush" };

    const float Floor = -4.0f, Ceil = -0.5f, SlabBottom = -4.3f, RoofTop = -0.3f;
    const float DoorZ0 = 11.5f, DoorZ1 = 14.0f, DoorTop = -1.3f;
    const float WallX = 1.8f;                               // vnejsi lic vychodni zdi chodby (Wall_4: x 1,5-1,8)
    const float X0 = 4.8f, X1 = 14.8f, Z0 = 7.25f, Z1 = 18.25f;   // vnitrek galerie
    const float T = 0.3f;                                   // tloustka zdi
    const float PosterY = -2.3f, PosterMaxH = 2.2f;

    static Material wallMat, floorMat, ceilMat, concrete, frameMat, pink, cyan, lampMat, benchMat;
    static bool warned;

    static GrandTheftDomasovGallery()
    {
        EditorApplication.delayCall += Auto;
        EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Auto; };
    }

    static void Auto()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(Maps, root.name) < 0) continue;
                var tunel = root.transform.Find("Tunel");
                if (tunel == null) continue;
                if (tunel.Find(OldMarker) != null) RemoveOld(root.transform, tunel);
                if (tunel.Find(Marker) == null) Build(root.transform, tunel);
                if (tunel.Find(Marker) != null && tunel.Find(ExtraMarker) == null) AddExtraPosters(root.transform, tunel);
                if (tunel.Find(Marker) != null && tunel.Find(MusicMarker) == null) AddMusic(root.transform, tunel);
                if (tunel.Find(Marker) != null && tunel.Find(RampageMarker) == null) AddRampage(root.transform, tunel);
            }
        }
    }

    static void Build(Transform map, Transform tunel)
    {
        var walls = tunel.Find("Walls");
        var w4 = walls != null ? walls.Find("Wall_4") : null;
        if (w4 == null || !w4.gameObject.activeSelf
            || (w4.localPosition - new Vector3(1.65f, -2.225f, 15.475f)).sqrMagnitude > 0.01f
            || (w4.localScale - new Vector3(0.3f, 4.15f, 63.45f)).sqrMagnitude > 0.01f
            || Quaternion.Angle(w4.localRotation, Quaternion.identity) > 0.5f)
        {
            if (!warned) Debug.LogWarning("[GTD galerie] " + map.name + ": Tunel/Walls/Wall_4 neni na puvodnim miste (rucne zmenena?) - galerie se nepostavila.");
            warned = true;
            return;
        }

        LoadMaterials();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Galerie Grand Theft Domasov");

        // ---- pruchod ve vychodni zdi chodby ----
        float wallBottom = -4.3f, wallTop = -0.15f;
        WallPiece(w4, walls, "Wall_4_Jih", -16.25f, DoorZ0 + Shift, wallBottom, wallTop);
        WallPiece(w4, walls, "Wall_4_Sever", DoorZ1 + Shift, 47.2f, wallBottom, wallTop);
        WallPiece(w4, walls, "Wall_4_Preklad", DoorZ0 + Shift, DoorZ1 + Shift, DoorTop, wallTop);
        Undo.RecordObject(w4.gameObject, "Galerie");
        w4.gameObject.SetActive(false);

        var marker = new GameObject(Marker);
        Undo.RegisterCreatedObjectUndo(marker, "Galerie");
        marker.transform.SetParent(tunel, false);

        // ---- koren galerie (soused Tunelu, stejna poloha) ----
        var old = map.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Galerie");
        root.transform.SetParent(map, false);
        root.transform.localPosition = tunel.localPosition + tunel.localRotation * new Vector3(0f, 0f, Shift);
        root.transform.localRotation = tunel.localRotation;
        root.transform.localScale = tunel.localScale;
        root.AddComponent<BotNoNav>();   // easter egg jen pro hrace

        var shell = Group(root.transform, "Stavba");
        // chodbicka do galerie
        Slab(shell, "Pruchod_Podlaha", 1.5f, X0, SlabBottom, Floor, DoorZ0 - T, DoorZ1 + T, concrete, true);
        Slab(shell, "Pruchod_Strop", 1.5f, X0, Ceil, RoofTop, DoorZ0 - T, DoorZ1 + T, ceilMat, true);
        Slab(shell, "Pruchod_Zed_J", WallX, X0 - T, SlabBottom, RoofTop, DoorZ0 - T, DoorZ0, concrete, true);
        Slab(shell, "Pruchod_Zed_S", WallX, X0 - T, SlabBottom, RoofTop, DoorZ1, DoorZ1 + T, concrete, true);
        // mistnost
        Slab(shell, "Podlaha", X0 - T, X1 + T, SlabBottom, Floor, Z0 - T, Z1 + T, floorMat, true);
        Slab(shell, "Strop", X0 - T, X1 + T, Ceil, RoofTop, Z0 - T, Z1 + T, ceilMat, true);
        Slab(shell, "Zed_V", X1, X1 + T, SlabBottom, RoofTop, Z0 - T, Z1 + T, wallMat, true);
        Slab(shell, "Zed_S", X0 - T, X1 + T, SlabBottom, RoofTop, Z1, Z1 + T, wallMat, true);
        Slab(shell, "Zed_J", X0 - T, X1 + T, SlabBottom, RoofTop, Z0 - T, Z0, wallMat, true);
        Slab(shell, "Zed_Z_Jih", X0 - T, X0, SlabBottom, RoofTop, Z0 - T, DoorZ0, wallMat, true);
        Slab(shell, "Zed_Z_Sever", X0 - T, X0, SlabBottom, RoofTop, DoorZ1, Z1 + T, wallMat, true);
        Slab(shell, "Zed_Z_Preklad", X0 - T, X0, DoorTop, RoofTop, DoorZ0, DoorZ1, wallMat, true);
        // lavicka uprostred
        Slab(shell, "Lavicka", 9.5f, 10.1f, Floor, Floor + 0.45f, 11.55f, 13.95f, benchMat, true);

        // ---- plakaty ----
        float zc = (Z0 + Z1) * 0.5f;   // 12,75 = osa pruchodu
        var posters = Group(root.transform, "Plakaty");
        Poster(posters, 1, new Vector3(X1, PosterY, zc), Vector3.left, 3.0f);
        Poster(posters, 2, new Vector3(X1, PosterY, zc - 3.75f), Vector3.left, 2.0f);
        Poster(posters, 3, new Vector3(X1, PosterY, zc + 3.75f), Vector3.left, 2.0f);
        Poster(posters, 4, new Vector3(7.35f, PosterY, Z1), Vector3.back, 3.4f);
        Poster(posters, 5, new Vector3(12.25f, PosterY, Z1), Vector3.back, 3.4f);
        Poster(posters, 6, new Vector3(7.35f, PosterY, Z0), Vector3.forward, 2.2f);
        Poster(posters, 7, new Vector3(12.25f, PosterY, Z0), Vector3.forward, 2.2f);
        Poster(posters, 8, new Vector3(X0, PosterY, 16.2f), Vector3.right, 2.0f);

        // ---- napis nad hlavnim plakatem ----
        var sign = new GameObject("Napis", typeof(RectTransform));
        sign.transform.SetParent(root.transform, false);
        sign.transform.localPosition = new Vector3(X1 - 0.04f, -0.92f, zc);
        sign.transform.localRotation = Quaternion.LookRotation(Vector3.right);
        var text = sign.AddComponent<TextMeshPro>();
        text.text = "GRAND THEFT DOMAŠOV";
        text.fontStyle = FontStyles.Bold | FontStyles.Italic;
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(6.5f, 0.5f);
        text.enableAutoSizing = true;
        text.fontSizeMin = 0.5f;
        text.fontSizeMax = 12f;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.color = new Color(1f, 0.55f, 0.85f);

        // ---- neony: ruzovy pruh pod stropem, tyrkysovy u podlahy ----
        var neon = Group(root.transform, "Neony");
        const float topY = -0.62f, lowY = Floor + 0.06f, d = 0.03f, k = 0.05f;
        Strip(neon, "Ruzovy_V", new Vector3(X1 - d, topY, zc), new Vector3(k, k, Z1 - Z0), pink);
        Strip(neon, "Ruzovy_S", new Vector3((X0 + X1) / 2, topY, Z1 - d), new Vector3(X1 - X0, k, k), pink);
        Strip(neon, "Ruzovy_J", new Vector3((X0 + X1) / 2, topY, Z0 + d), new Vector3(X1 - X0, k, k), pink);
        Strip(neon, "Ruzovy_Z", new Vector3(X0 + d, topY, zc), new Vector3(k, k, Z1 - Z0), pink);
        Strip(neon, "Tyrkys_V", new Vector3(X1 - d, lowY, zc), new Vector3(k, k, Z1 - Z0), cyan);
        Strip(neon, "Tyrkys_S", new Vector3((X0 + X1) / 2, lowY, Z1 - d), new Vector3(X1 - X0, k, k), cyan);
        Strip(neon, "Tyrkys_J", new Vector3((X0 + X1) / 2, lowY, Z0 + d), new Vector3(X1 - X0, k, k), cyan);
        Strip(neon, "Tyrkys_Z_Jih", new Vector3(X0 + d, lowY, (Z0 + DoorZ0) / 2), new Vector3(k, k, DoorZ0 - Z0), cyan);
        Strip(neon, "Tyrkys_Z_Sever", new Vector3(X0 + d, lowY, (DoorZ1 + Z1) / 2), new Vector3(k, k, Z1 - DoorZ1), cyan);

        // ---- svetla ----
        var lights = Group(root.transform, "Svetla");
        Lamp(lights, "Pruchod", new Vector3(3.2f, Ceil, zc), new Color(1f, 0.82f, 0.6f), 2.2f, 6f);
        Lamp(lights, "Galerie_1", new Vector3(7.9f, Ceil, zc), new Color(1f, 0.88f, 0.92f), 2.6f, 9f);
        Lamp(lights, "Galerie_2", new Vector3(11.7f, Ceil, zc), new Color(1f, 0.88f, 0.92f), 2.6f, 9f);
        PointLight(lights, "Neon_Ruzova", new Vector3(9.8f, -1.2f, zc), new Color(1f, 0.3f, 0.75f), 1.4f, 8f);

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[GTD galerie] " + map.name + ": galerie postavena (odbocka z tunelu na vychod, z = " + zc + "). Uloz scenu (Ctrl+S).");
    }

    // Plakaty 9 a 10: do hotove galerie jen pridat (zbytek galerie se nemeni). 9 = zapadni zed jizne od vchodu,
    // 10 = jizni zed mezi plakaty 6 a 7. Kdyz galerie (nebo jeji skupina Plakaty) chybi, nic nedela.
    static void AddExtraPosters(Transform map, Transform tunel)
    {
        var root = map.Find(RootName);
        var posters = root != null ? root.Find("Plakaty") : null;
        if (posters == null) return;
        LoadMaterials();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Galerie: plakaty 9 a 10");
        if (posters.Find("Plakat_9") == null) Poster(posters, 9, new Vector3(X0, PosterY, (Z0 + DoorZ0) / 2), Vector3.right, 2.6f);
        if (posters.Find("Plakat_10") == null) Poster(posters, 10, new Vector3((X0 + X1) / 2, PosterY, Z0), Vector3.forward, 2.0f);
        var marker = new GameObject(ExtraMarker);
        marker.transform.SetParent(tunel, false);
        foreach (Transform t in posters)
            if (t.name == "Plakat_9" || t.name == "Plakat_10") Undo.RegisterCreatedObjectUndo(t.gameObject, "Galerie");
        Undo.RegisterCreatedObjectUndo(marker, "Galerie");
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[GTD galerie] " + map.name + ": pridany plakaty 9 a 10. Uloz scenu (Ctrl+S).");
    }

    // Lebka RAMPAGE (PickupSpot) na podlaze mezi lavickou a vychodni zdi. Jednou - kdyz ji presunes nebo smazes, zustane.
    static void AddRampage(Transform map, Transform tunel)
    {
        var root = map.Find(RootName);
        if (root == null) return;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Galerie: rampage");
        if (root.Find("Balicek_Rampage") == null)
        {
            var go = new GameObject("Balicek_Rampage");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(12.4f, Floor, 12.75f);
            go.AddComponent<PickupSpot>().kind = PickupKind.Rampage;
            Undo.RegisterCreatedObjectUndo(go, "Galerie");
        }
        var marker = new GameObject(RampageMarker);
        marker.transform.SetParent(tunel, false);
        Undo.RegisterCreatedObjectUndo(marker, "Galerie");
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        Debug.Log("[GTD galerie] " + map.name + ": pridana lebka RAMPAGE. Uloz scenu (Ctrl+S).");
    }

    // Hudba: zdroj uprostred galerie (2D, hlasitost a tlumeni ridi GalleryMusic podle polohy posluchace). Jednou.
    static void AddMusic(Transform map, Transform tunel)
    {
        var root = map.Find(RootName);
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
        if (root == null || clip == null) return;
        if (AssetImporter.GetAtPath(MusicPath) is AudioImporter importer)
        {
            var settings = importer.defaultSampleSettings;
            if (settings.loadType != AudioClipLoadType.Streaming || settings.compressionFormat != AudioCompressionFormat.Vorbis)
            {
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.6f;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            }
        }
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Galerie: hudba");
        if (root.Find("Hudba") == null)
        {
            var go = new GameObject("Hudba");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(9.8f, -2.3f, 12.75f);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.reverbZoneMix = 0f;
            var music = go.AddComponent<GalleryMusic>();
            music.source = source;
            music.filter = go.AddComponent<AudioLowPassFilter>();
            music.filter.cutoffFrequency = 22000f;
            Undo.RegisterCreatedObjectUndo(go, "Galerie");
        }
        var marker = new GameObject(MusicMarker);
        marker.transform.SetParent(tunel, false);
        Undo.RegisterCreatedObjectUndo(marker, "Galerie");
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        Debug.Log("[GTD galerie] " + map.name + ": pridana hudba. Uloz scenu (Ctrl+S).");
    }

    // Odstrani galerii z puvodni polohy (v1): koren, kusy zdi s otvorem a znacky; puvodni Wall_4 znovu zapne.
    static void RemoveOld(Transform map, Transform tunel)
    {
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Galerie: presun");
        var walls = tunel.Find("Walls");
        if (walls != null)
        {
            foreach (var n in new[] { "Wall_4_Jih", "Wall_4_Sever", "Wall_4_Preklad" })
            {
                var piece = walls.Find(n);
                if (piece != null) Undo.DestroyObjectImmediate(piece.gameObject);
            }
            var w4 = walls.Find("Wall_4");
            if (w4 != null) { Undo.RecordObject(w4.gameObject, "Galerie"); w4.gameObject.SetActive(true); }
        }
        var old = map.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        foreach (var n in new[] { OldMarker, ExtraMarker, MusicMarker })
        {
            var m = tunel.Find(n);
            if (m != null) Undo.DestroyObjectImmediate(m.gameObject);
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        Debug.Log("[GTD galerie] " + map.name + ": galerie odstranena z puvodni polohy, stavi se o kus jizneji.");
    }

    // ---------------- stavebni dily ----------------

    static Transform Group(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static void WallPiece(Transform source, Transform parent, string name, float z0, float z1, float y0, float y1)
    {
        var go = Object.Instantiate(source.gameObject, parent);
        Undo.RegisterCreatedObjectUndo(go, "Galerie");
        go.name = name;
        go.transform.localPosition = new Vector3(source.localPosition.x, (y0 + y1) / 2, (z0 + z1) / 2);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = new Vector3(source.localScale.x, y1 - y0, z1 - z0);
    }

    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material m, bool collider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static void Slab(Transform parent, string name, float x0, float x1, float y0, float y1, float z0, float z1, Material m, bool collider)
    {
        Box(parent, name, new Vector3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3(x1 - x0, y1 - y0, z1 - z0), m, collider);
    }

    static void Strip(Transform parent, string name, Vector3 center, Vector3 size, Material m)
    {
        var go = Box(parent, name, center, size, m, false);
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // Plakat na zdi: ram (pevny, s kolizi) a obraz; 'normal' miri od zdi do mistnosti.
    static void Poster(Transform parent, int index, Vector3 wallPoint, Vector3 normal, float maxWidth)
    {
        string path = Folder + "/gtd_" + index.ToString("00") + ".png";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null) { Debug.LogWarning("[GTD galerie] Chybi " + path); return; }
        float aspect = (float)tex.width / tex.height;
        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.GetSourceTextureWidthAndHeight(out int w, out int h);
            if (h > 0) aspect = (float)w / h;
        }
        float height = PosterMaxH, width = height * aspect;
        if (width > maxWidth) { width = maxWidth; height = width / aspect; }

        var rot = Quaternion.LookRotation(-normal);
        var holder = Group(parent, "Plakat_" + index);
        holder.localPosition = wallPoint;
        holder.localRotation = rot;

        var frame = Box(holder, "Ram", new Vector3(0, 0, -0.03f), new Vector3(width + 0.16f, height + 0.16f, 0.06f), frameMat, true);
        frame.transform.localRotation = Quaternion.identity;

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Obraz";
        quad.transform.SetParent(holder, false);
        quad.transform.localPosition = new Vector3(0, 0, -0.065f);
        quad.transform.localScale = new Vector3(width, height, 1f);
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        var r = quad.GetComponent<Renderer>();
        r.sharedMaterial = PosterMaterial(index, tex);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void Lamp(Transform parent, string name, Vector3 ceilingPoint, Color color, float intensity, float range)
    {
        Box(parent, "Lampa_" + name, ceilingPoint + new Vector3(0, -0.06f, 0), new Vector3(0.5f, 0.08f, 0.25f), lampMat, false);
        PointLight(parent, "Svetlo_" + name, ceilingPoint + new Vector3(0, -0.4f, 0), color, intensity, range);
    }

    static void PointLight(Transform parent, string name, Vector3 p, Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = p;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
        light.lightmapBakeType = LightmapBakeType.Realtime;
    }

    // ---------------- materialy ----------------

    static void LoadMaterials()
    {
        MapBuildKit.EnsureFolder(Folder);
        concrete = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Tunnel/Tunnel_Concrete.mat");
        ceilMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Tunnel/Tunnel_ConcreteDark.mat");
        lampMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Tunnel/Tunnel_Lamp.mat");
        wallMat = Lit("GTD_Zed", new Color(0.13f, 0.09f, 0.19f), 0.2f);
        floorMat = Lit("GTD_Podlaha", new Color(0.05f, 0.045f, 0.065f), 0.7f);
        frameMat = Lit("GTD_Ram", new Color(0.03f, 0.03f, 0.035f), 0.6f);
        benchMat = Lit("GTD_Lavicka", new Color(0.08f, 0.32f, 0.36f), 0.45f);
        pink = Unlit("GTD_Neon_Ruzovy", new Color(3.2f, 0.5f, 1.9f));
        cyan = Unlit("GTD_Neon_Tyrkys", new Color(0.3f, 2.3f, 3.0f));
        if (concrete == null) concrete = wallMat;
        if (ceilMat == null) ceilMat = wallMat;
        if (lampMat == null) lampMat = pink;
    }

    static Material Lit(string name, Color color, float smooth)
    {
        string path = Folder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smooth);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material Unlit(string name, Color color)
    {
        string path = Folder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // Plakat: Lit (odlesky, stiny lamp) + jemne vlastni svitivost, aby byl citelny i v sere chodbe.
    static Material PosterMaterial(int index, Texture2D tex)
    {
        string path = Folder + "/GTD_Plakat_" + index.ToString("00") + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.35f);
        m.SetTexture("_EmissionMap", tex);
        m.SetColor("_EmissionColor", new Color(0.45f, 0.45f, 0.45f));
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
