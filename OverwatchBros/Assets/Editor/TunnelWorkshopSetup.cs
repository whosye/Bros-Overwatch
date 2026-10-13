using System;
using System.IO;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

/// <summary>Builds only our new objects; the hand-edited tunnel and floor are never rebuilt.</summary>
[InitializeOnLoad]
public static class TunnelWorkshopSetup
{
    public const string RootName = "PavoukuvWorkshop";
    const string Folder = "Assets/Materials/TunnelWorkshop";
    const string FloorPath = "Tunel/Corridors/Floor_1 (2)";
    static Material wall, black, metal, cream, wood, rubber, green, amber, violet, paper, screen, red;
    static TMP_FontAsset font;
    static Mesh bevelMesh;

    static TunnelWorkshopSetup() { EditorApplication.delayCall += AutoBuild; }
    static void AutoBuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var map = GameObject.Find("Map-Domasov");
        if (map && map.transform.Find(FloorPath) && !map.transform.Find(RootName)) Build();
    }

    [MenuItem("BrosOverwatch/Easter egg/Postavit Pavoukuv workshop")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var map = GameObject.Find("Map-Domasov");
        var floor = map ? map.transform.Find(FloorPath) : null;
        if (!floor || !floor.TryGetComponent<Renderer>(out var floorRenderer))
        {
            Debug.LogWarning("[Pavouk] Chybi pripravena podlaha " + FloorPath + ". Tunel nebyl zmenen.");
            return;
        }
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Music/tunnel_vision.mp3");
        var movie = AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Video/chata_timelapse.mp4");
        if (!clip || !movie) { Debug.LogError("[Pavouk] Chybi tunnel_vision.mp3 nebo chata_timelapse.mp4."); return; }
        Directory.CreateDirectory("Temp/WorkshopQA");
        if (!File.Exists("Temp/WorkshopQA/BeforeWorkshop.unity"))
            EditorSceneManager.SaveScene(map.scene, "Temp/WorkshopQA/BeforeWorkshop.unity", true);
        LoadMaterials();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Postavit Pavoukuv workshop");
        var old = map.transform.Find(RootName);
        try
        {
        Bounds b = floorRenderer.bounds;
        // East side of the hand-made floor meets the user's existing branch / door frame.
        var root = new GameObject(RootName + " (building)");
        Undo.RegisterCreatedObjectUndo(root, "Postavit Pavoukuv workshop");
        root.transform.SetParent(map.transform, true);
        root.transform.SetPositionAndRotation(new Vector3(b.max.x, b.max.y, b.center.z), Quaternion.Euler(0, -90, 0));
        var workshop = root.AddComponent<TunnelWorkshop>();
        workshop.roomSize = new Vector3(b.size.z, 3.25f, b.size.x);
        workshop.surfaceY = MapBuildKit.GroundBounds(map.transform).max.y;
        workshop.beatsPerMinute = 175f;
        workshop.beatOffsetSeconds = 0f;
        Shell(root.transform, b.size.z, b.size.x);
        Door(root.transform);
        Workstation(root.transform, workshop, b.size.x);
        Dress(root.transform, b.size.z, b.size.x);

        var audio = Group(root.transform, "Zvuk_za_dvermi", new Vector3(0, 1.35f, 0.36f));
        workshop.music = audio.gameObject.AddComponent<AudioSource>();
        workshop.music.clip = clip;
        workshop.music.playOnAwake = false;
        workshop.music.loop = true;
        workshop.music.spatialBlend = 1f;
        workshop.music.volume = 0f;
        workshop.music.mute = true;
        workshop.music.reverbZoneMix = 0f;
        workshop.doorFilter = audio.gameObject.AddComponent<AudioLowPassFilter>();
        workshop.doorFilter.cutoffFrequency = workshop.throughDoorCutoff;
        workshop.doorFilter.lowpassResonanceQ = 1f;
        workshop.video.clip = movie;

        var audioImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
        if (audioImporter)
        {
            var settings = audioImporter.defaultSampleSettings;
            if (settings.loadType != AudioClipLoadType.Streaming)
            {
                settings.loadType = AudioClipLoadType.Streaming;
                audioImporter.defaultSampleSettings = settings;
                audioImporter.SaveAndReimport();
            }
        }
        workshop.Animate(0);
        if (old) Undo.DestroyObjectImmediate(old.gameObject);
        root.name = RootName;
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(map.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Pavouk] Workshop postaven na pripravene podlaze. Nahledy: BrosOverwatch > Easter egg > Vytvorit nahledy a overit.");
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    static Material Mat(string name, Color color, float smooth = 0.25f, Color? emission = null, float metallic = 0f)
    {
        string path = Folder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            m.shader = Shader.Find("Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", emission.Value);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static void LoadMaterials()
    {
        MapBuildKit.EnsureFolder(Folder);
        wall = Mat("Mineral_painted_wall", new Color(0.19f, 0.23f, 0.23f), 0.13f);
        black = Mat("Graphite_powdercoat", new Color(0.027f, 0.037f, 0.045f), 0.28f, metallic: 0.25f);
        metal = Mat("Brushed_aluminium", new Color(0.32f, 0.39f, 0.4f), 0.55f, metallic: 0.7f);
        cream = Mat("CRT_ivory", new Color(0.66f, 0.69f, 0.59f), 0.3f);
        wood = Mat("Dark_worktop", new Color(0.20f, 0.105f, 0.055f), 0.3f);
        rubber = Mat("Acoustic_foam", new Color(0.038f, 0.045f, 0.047f), 0f);
        green = Mat("Phosphor_green", new Color(0.15f, 0.55f, 0.22f), 0.25f, new Color(0.4f, 2.5f, 0.7f));
        amber = Mat("Warm_amber", new Color(0.9f, 0.35f, 0.05f), 0.3f, new Color(2.7f, 0.8f, 0.1f));
        violet = Mat("Indigo_LED", new Color(0.2f, 0.2f, 0.6f), 0.25f, new Color(0.4f, 0.3f, 2f));
        paper = Mat("Warm_paper", new Color(0.74f, 0.77f, 0.66f), 0f);
        red = Mat("Warning_paint", new Color(0.68f, 0.12f, 0.07f), 0.2f);
        screen = Mat("Timelapse_screen", Color.white, 0f, Color.white * 1.1f);
        screen.shader = Shader.Find("Universal Render Pipeline/Unlit");
        screen.SetColor("_BaseColor", Color.white);
        var poster = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/TimelapsePoster.jpg");
        if (poster) { screen.SetTexture("_BaseMap", poster); screen.SetTexture("_EmissionMap", poster); }
        font = TunnelWorkshopPolish.LoadFont();
        bevelMesh = BevelMesh();
    }

    static Transform Group(Transform parent, string name, Vector3 position = default)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false); t.localPosition = position; return t;
    }
    static Transform Shape(Transform parent, string name, PrimitiveType type, Vector3 p, Vector3 size, Material m, bool collision = false)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name;
        go.transform.SetParent(parent, false); go.transform.localPosition = p; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        if (!collision) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        return go.transform;
    }
    static Transform Box(Transform p, string n, Vector3 v, Vector3 s, Material m, bool c = false) => Shape(p, n, PrimitiveType.Cube, v, s, m, c);
    static Transform SoftBox(Transform p, string n, Vector3 v, Vector3 s, Material m, bool c = false)
    {
        var t = Box(p, n, v, s, m, c);
        t.GetComponent<MeshFilter>().sharedMesh = bevelMesh;
        return t;
    }
    static Mesh BevelMesh()
    {
        const string path = "Assets/Models/TunnelWorkshop/BeveledBox.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing) return existing;
        MapBuildKit.EnsureFolder("Assets/Models/TunnelWorkshop");
        var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
        const float outer = 0.5f, inner = 0.435f;
        void Face(params Vector3[] points)
        {
            Vector3 center = Vector3.zero;
            foreach (var point in points) center += point;
            if (Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), center) < 0) Array.Reverse(points);
            int first = vertices.Count;
            for (int i = 0; i < points.Length; i++) { vertices.Add(points[i]); uv.Add(new Vector2((i == 1 || i == 2) ? 1 : 0, i > 1 ? 1 : 0)); }
            for (int i = 1; i < points.Length - 1; i++) { triangles.Add(first); triangles.Add(first + i); triangles.Add(first + i + 1); }
        }
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        for (int axis = 0; axis < 3; axis++)
            foreach (float sign in new[] { -1f, 1f })
            {
                Vector3 n = axes[axis] * sign * outer, a = axes[(axis + 1) % 3] * inner, b = axes[(axis + 2) % 3] * inner;
                Face(n - a - b, n + a - b, n + a + b, n - a + b);
            }
        for (int axis = 0; axis < 3; axis++)
            foreach (float a in new[] { -1f, 1f }) foreach (float b in new[] { -1f, 1f })
            {
                Vector3 u = axes[(axis + 1) % 3] * a, v = axes[(axis + 2) % 3] * b, along = axes[axis] * inner;
                Face(u * outer + v * inner - along, u * inner + v * outer - along, u * inner + v * outer + along, u * outer + v * inner + along);
            }
        foreach (float x in new[] { -1f, 1f }) foreach (float y in new[] { -1f, 1f }) foreach (float z in new[] { -1f, 1f })
            Face(new Vector3(x * outer, y * inner, z * inner), new Vector3(x * inner, y * outer, z * inner), new Vector3(x * inner, y * inner, z * outer));
        var mesh = new Mesh { name = "Beveled equipment housing" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path); return mesh;
    }
    static Transform Ball(Transform p, string n, Vector3 v, Vector3 s, Material m) => Shape(p, n, PrimitiveType.Sphere, v, s, m);
    static Transform Rod(Transform p, string n, Vector3 a, Vector3 b, float radius, Material m)
    {
        var t = Shape(p, n, PrimitiveType.Cylinder, (a + b) * 0.5f, new Vector3(radius * 2, (b - a).magnitude * 0.5f, radius * 2), m);
        t.localRotation = Quaternion.FromToRotation(Vector3.up, b - a); return t;
    }
    static void Cable(Transform p, string name, Material m, float r, params Vector3[] points)
    {
        for (int i = 1; i < points.Length; i++) Rod(p, name + "_" + i, points[i - 1], points[i], r, m);
    }
    static void Ring(Transform p, string name, Vector3 center, float radius, float tube, Material m, int pieces = 20)
    {
        for (int i = 0; i < pieces; i++)
        {
            float a = i * Mathf.PI * 2 / pieces, b = (i + 1) * Mathf.PI * 2 / pieces;
            Rod(p, name + i, center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius,
                center + new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0) * radius, tube, m);
        }
    }
    static TMP_Text Label(Transform p, string name, string text, Vector3 at, float size, Color color, bool faceBack = true)
    {
        var t = Group(p, name, at);
        // TextMesh fronts face local -Z, like Unity's Quad primitive.
        if (!faceBack) t.localRotation = Quaternion.Euler(0, 180, 0);
        var label = t.gameObject.AddComponent<TextMeshPro>();
        label.text = text; label.font = font; label.fontSize = size * 20f;
        label.alignment = TextAlignmentOptions.Center; label.color = color;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        TunnelWorkshopPolish.StyleLabel(label, font);
        var renderer = label.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return label;
    }
    static Light Lamp(Transform p, string name, Vector3 at, Color color, float intensity, float range, bool shadows = false)
    {
        var l = Group(p, name, at).gameObject.AddComponent<Light>();
        l.type = LightType.Point; l.color = color; l.intensity = intensity; l.range = range;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        if (shadows)
        {
            // One shadow map per spot, instead of six faces per point light.
            l.type = LightType.Spot; l.spotAngle = 115f; l.innerSpotAngle = 70f;
            l.transform.LookAt(p.TransformPoint(new Vector3(0, 0.9f, 2.5f)));
        }
        l.shadowStrength = 0.9f; l.shadowBias = 0.025f; l.shadowNormalBias = 0.08f;
        l.lightmapBakeType = LightmapBakeType.Realtime;
        return l;
    }

    static void Shell(Transform p, float width, float depth)
    {
        var shell = Group(p, "Mistnost");
        const float h = 3.25f, t = 0.16f;
        Box(shell, "Leva_zed", new Vector3(-width / 2 + t / 2, h / 2, depth / 2), new Vector3(t, h, depth), wall, true);
        Box(shell, "Prava_zed", new Vector3(width / 2 - t / 2, h / 2, depth / 2), new Vector3(t, h, depth), wall, true);
        Box(shell, "Zadni_zed", new Vector3(0, h / 2, depth - t / 2), new Vector3(width, h, t), wall, true);
        Box(shell, "Strop", new Vector3(0, h + t / 2, depth / 2), new Vector3(width, t, depth), black, true);
        // Front wall surrounds the preserved branch opening.
        float side = (width - 1.24f) / 2;
        for (int sign = -1; sign <= 1; sign += 2)
            Box(shell, "Predni_zed_" + sign, new Vector3(sign * (0.62f + side / 2), h / 2, 0.08f), new Vector3(side, h, t), wall, true);
        Box(shell, "Nad_dvermi", new Vector3(0, (h + 2.42f) / 2, 0.08f), new Vector3(1.24f, h - 2.42f, t), wall, true);
        Box(shell, "Gumova_podlaha", new Vector3(0, 0.018f, depth / 2), new Vector3(width - 0.32f, 0.036f, depth - 0.32f), rubber);
        for (int i = 1; i < 7; i++)
            Box(shell, "Spary_podlahy_" + i, new Vector3(0, 0.038f, depth * i / 7), new Vector3(width - 0.35f, 0.003f, 0.009f), metal);
        for (int sideIndex = -1; sideIndex <= 1; sideIndex += 2)
        {
            Box(shell, "Sokl_" + sideIndex, new Vector3(sideIndex * (width / 2 - 0.10f), 0.13f, depth / 2), new Vector3(0.09f, 0.22f, depth - 0.2f), black);
            Box(shell, "Zelena_linka_" + sideIndex, new Vector3(sideIndex * (width / 2 - 0.15f), 0.23f, depth / 2), new Vector3(0.015f, 0.012f, depth - 0.4f), green);
            Rod(shell, "Stropni_trubka_" + sideIndex, new Vector3(sideIndex * 1.9f, 3.02f, 0.2f), new Vector3(sideIndex * 1.9f, 3.02f, depth - 0.2f), 0.045f, metal);
        }
        Box(shell, "Zadni_svetelna_lista", new Vector3(0, 2.95f, depth - 0.18f), new Vector3(width - 0.6f, 0.035f, 0.03f), amber);
        Lamp(shell, "Jantarova_zed", new Vector3(1.5f, 2.1f, depth - 0.7f), new Color(1f, 0.38f, 0.1f), 3.8f, 3.5f, true);
        Lamp(shell, "Jemna_vypln", new Vector3(-1.3f, 2.25f, 1.8f), new Color(0.17f, 0.55f, 0.4f), 2.1f, 3.2f, true);
    }

    static void Door(Transform p)
    {
        var door = Group(p, "Dvere_s_pruhledem", new Vector3(0, 0, 0.07f));
        const float width = 1.16f, height = 2.38f, windowWidth = 0.76f, bottom = 1.38f, top = 1.94f;
        Box(door, "Spodni_panel", new Vector3(0, bottom / 2, 0), new Vector3(width, bottom, 0.09f), black, true);
        Box(door, "Horni_panel", new Vector3(0, (height + top) / 2, 0), new Vector3(width, height - top, 0.09f), black, true);
        for (int side = -1; side <= 1; side += 2)
        {
            Box(door, "Bok_okna_" + side, new Vector3(side * (windowWidth + width) / 4, (bottom + top) / 2, 0), new Vector3((width - windowWidth) / 2, top - bottom, 0.09f), black, true);
            Box(door, "Ocelova_zaruben_" + side, new Vector3(side * 0.61f, 1.22f, 0), new Vector3(0.07f, 2.44f, 0.2f), metal, true);
            Box(door, "Okraj_okna_" + side, new Vector3(side * (windowWidth / 2 + 0.016f), (bottom + top) / 2, -0.057f), new Vector3(0.026f, top - bottom + 0.05f, 0.024f), metal);
        }
        Box(door, "Zaruben_nahore", new Vector3(0, 2.44f, 0), new Vector3(1.29f, 0.08f, 0.2f), metal, true);
        Box(door, "Pruchod_prah", new Vector3(0, 0.035f, 0), new Vector3(1.2f, 0.07f, 0.22f), metal, true);
        foreach (float y in new[] { bottom - 0.016f, top + 0.016f })
            Box(door, "Vodorovny_okraj_okna", new Vector3(0, y, -0.057f), new Vector3(windowWidth + 0.06f, 0.026f, 0.024f), metal);
        // A transparent physical pane stops bullets without closing the view.
        var pane = Group(door, "Prusvitne_okno_kolize", new Vector3(0, (bottom + top) / 2, 0));
        pane.gameObject.AddComponent<BoxCollider>().size = new Vector3(windowWidth, top - bottom, 0.02f);
        Rod(door, "Klika", new Vector3(0.39f, 1.05f, -0.12f), new Vector3(0.52f, 1.05f, -0.12f), 0.022f, metal);
        Box(door, "Stitek", new Vector3(0, 2.17f, -0.052f), new Vector3(0.82f, 0.18f, 0.014f), cream);
        Label(door, "PAVOUK", "PAVOUK / WORKSHOP", new Vector3(0, 2.17f, -0.063f), 0.042f, new Color(0.05f, 0.07f, 0.07f));
        Label(door, "Nerusit", "NEKLEPAT. RENDERUJU.", new Vector3(0, 1.22f, -0.057f), 0.037f, new Color(0.7f, 0.75f, 0.67f));
        for (int i = 0; i < 6; i++) Box(door, "Ventilace_" + i, new Vector3(0, 0.26f + i * 0.055f, -0.055f), new Vector3(0.52f, 0.02f, 0.018f), metal);
        Ball(door, "Kontrolka", new Vector3(0.48f, 2.18f, -0.058f), Vector3.one * 0.032f, green);
    }

    static void Workstation(Transform p, TunnelWorkshop w, float depth)
    {
        var desk = Group(p, "Pracovni_stanice", new Vector3(0.12f, 0, Mathf.Min(3.05f, depth - 1.8f)));
        desk.localRotation = Quaternion.Euler(0, -12, 0);
        Box(desk, "Masivni_stul", new Vector3(0, 0.82f, 0.12f), new Vector3(2.85f, 0.095f, 1.05f), wood, true);
        Box(desk, "Kovova_hrana", new Vector3(0, 0.8f, -0.405f), new Vector3(2.86f, 0.08f, 0.027f), metal);
        foreach (float x in new[] { -1.25f, 1.25f })
        {
            Box(desk, "Noha_stolu", new Vector3(x, 0.4f, 0.1f), new Vector3(0.065f, 0.8f, 0.7f), black, true);
            Box(desk, "Patka", new Vector3(x, 0.065f, 0.1f), new Vector3(0.18f, 0.1f, 0.8f), black);
        }
        Box(desk, "Podlozka", new Vector3(-0.08f, 0.874f, -0.12f), new Vector3(1.7f, 0.012f, 0.45f), rubber);
        Box(desk, "RGB_pod_stolem", new Vector3(0, 0.759f, -0.32f), new Vector3(2.5f, 0.018f, 0.018f), green);

        var mon = Group(desk, "Monitor_timelapse", new Vector3(0.26f, 1.40f, 0.38f));
        Box(mon, "Stojan_podstava", new Vector3(0, -0.485f, 0.035f), new Vector3(0.48f, 0.025f, 0.3f), black);
        Box(mon, "Stojan", new Vector3(0, -0.27f, 0.07f), new Vector3(0.075f, 0.41f, 0.06f), metal);
        SoftBox(mon, "Ramecek", Vector3.zero, new Vector3(1.43f, 0.84f, 0.08f), black);
        var display = Shape(mon, "Video_displej", PrimitiveType.Quad, new Vector3(0, 0.01f, -0.044f), new Vector3(1.34f, 0.75375f, 1), screen);
        w.monitor = display.GetComponent<Renderer>();
        w.monitor.shadowCastingMode = ShadowCastingMode.Off;
        // Keep the VideoPlayer off a Renderer: the existing Joker TV buff discovers Renderer-backed players.
        w.video = Group(mon, "Timelapse_player").gameObject.AddComponent<VideoPlayer>();
        w.video.playOnAwake = false; w.video.audioOutputMode = VideoAudioOutputMode.None;
        w.video.isLooping = true; w.video.renderMode = VideoRenderMode.RenderTexture;
        Ball(mon, "Power_LED", new Vector3(0.66f, -0.39f, -0.047f), Vector3.one * 0.012f, green);
        Label(mon, "Modeluji", "msi", new Vector3(0, -0.40f, -0.049f), 0.022f, Color.gray);
        w.monitorLight = Lamp(mon, "Svetlo_z_aktualniho_videa", new Vector3(0, 0, -0.16f), new Color(0.4f, 0.75f, 0.65f), 1.5f, 2.4f, true);
        w.monitorLight.transform.localRotation = Quaternion.Euler(8, 180, 0);

        var keyboard = Group(desk, "Klavesnice", new Vector3(-0.39f, 0.905f, -0.15f));
        Box(keyboard, "Sasi", Vector3.zero, new Vector3(0.62f, 0.042f, 0.23f), black);
        for (int row = 0; row < 4; row++)
            for (int key = 0; key < 13; key++)
                Box(keyboard, "Klavesa_" + row + "_" + key, new Vector3(-0.277f + key * 0.044f, 0.03f, -0.077f + row * 0.049f), new Vector3(0.036f, 0.021f, 0.037f), (key + row) % 11 == 0 ? green : cream);
        Box(keyboard, "Mezernik", new Vector3(0, 0.03f, -0.1f), new Vector3(0.24f, 0.02f, 0.026f), metal);
        w.mouse = Ball(desk, "Mys", new Vector3(0.17f, 0.925f, -0.19f), new Vector3(0.075f, 0.052f, 0.13f), black);
        Box(w.mouse, "LED", new Vector3(0, 0.37f, -0.1f), new Vector3(0.09f, 0.12f, 0.48f), green);
        Cable(desk, "Kabel_mysi", black, 0.006f, new Vector3(0.17f, 0.89f, -0.12f), new Vector3(0.21f, 0.89f, 0.08f), new Vector3(0.62f, 0.89f, 0.21f), new Vector3(0.74f, 0.88f, 0.54f));

        foreach (float x in new[] { -1.12f, 1.14f }) Speaker(desk, x);
        Computer(desk);
        Android(desk, w);
        w.accentLight = Lamp(desk, "Zelena_zare_stanice", new Vector3(-0.95f, 1.65f, 0.2f), new Color(0.3f, 1, 0.48f), 0.85f, 2.3f, true);
        w.accentLight.transform.localRotation = Quaternion.Euler(35, 145, 0);
        Label(desk, "Tempo", "175 BPM  /  FLOW STATE", new Vector3(0.25f, 0.78f, -0.425f), 0.032f, new Color(0.5f, 1f, 0.6f));
        // Coffee, loose notes, interface and a little desk lamp.
        var cup = Shape(desk, "Hrnek", PrimitiveType.Cylinder, new Vector3(-0.96f, 0.968f, -0.18f), new Vector3(0.12f, 0.087f, 0.12f), cream);
        Shape(desk, "Kava", PrimitiveType.Cylinder, new Vector3(-0.96f, 1.057f, -0.18f), new Vector3(0.103f, 0.003f, 0.103f), wood);
        Ring(desk, "Ucho_hrnku", new Vector3(-1.036f, 0.98f, -0.18f), 0.045f, 0.012f, cream, 12);
        var note = Box(desk, "Poznamky", new Vector3(0.78f, 0.876f, -0.22f), new Vector3(0.22f, 0.003f, 0.3f), paper);
        note.localRotation = Quaternion.Euler(0, 18, 0);
        Rod(desk, "Propiska", new Vector3(0.64f, 0.885f, -0.32f), new Vector3(0.72f, 0.885f, -0.13f), 0.007f, amber);
        Cable(desk, "Kabely_pod_stolem", black, 0.015f, new Vector3(0.7f, 0.82f, 0.53f), new Vector3(0.72f, 0.3f, 0.54f), new Vector3(1.3f, 0.07f, 0.75f), new Vector3(1.7f, 0.07f, 0.5f));
    }

    static void Speaker(Transform desk, float x)
    {
        var p = Group(desk, x < 0 ? "Reproduktor_L" : "Reproduktor_R", new Vector3(x, 1.125f, 0.34f));
        SoftBox(p, "Ozvucnice", Vector3.zero, new Vector3(0.31f, 0.5f, 0.3f), black);
        foreach (float y in new[] { -0.085f, 0.145f })
        {
            float radius = y < 0 ? 0.105f : 0.04f;
            var cone = Shape(p, "Membrana", PrimitiveType.Cylinder, new Vector3(0, y, -0.155f), new Vector3(radius * 2, 0.009f, radius * 2), rubber);
            cone.localRotation = Quaternion.Euler(90, 0, 0);
            Ring(p, "Lem", new Vector3(0, y, -0.169f), radius, 0.007f, metal, 16);
            Ball(p, "Stred", new Vector3(0, y, -0.173f), new Vector3(radius * 0.7f, radius * 0.7f, 0.028f), black);
        }
        Ball(p, "LED", new Vector3(0.11f, -0.21f, -0.16f), Vector3.one * 0.013f, green);
    }

    static void Computer(Transform desk)
    {
        var pc = Group(desk, "Pocitac", new Vector3(1.83f, 0, 0.23f));
        SoftBox(pc, "Skrin", new Vector3(0, 0.38f, 0), new Vector3(0.43f, 0.74f, 0.66f), black, true);
        Box(pc, "Bocni_panel", new Vector3(-0.218f, 0.4f, 0), new Vector3(0.018f, 0.62f, 0.57f), metal);
        for (int i = 0; i < 3; i++)
        {
            Ring(pc, "Ventilator_" + i, new Vector3(0, 0.15f + i * 0.22f, -0.342f), 0.085f, 0.009f, i == 1 ? amber : green);
            Ball(pc, "Osa_" + i, new Vector3(0, 0.15f + i * 0.22f, -0.345f), Vector3.one * 0.038f, metal);
            for (int j = 0; j < 4; j++)
            {
                var blade = Box(pc, "Lopatka", new Vector3(0, 0.15f + i * 0.22f, -0.339f), new Vector3(0.115f, 0.016f, 0.009f), metal);
                blade.localRotation = Quaternion.Euler(0, 0, j * 45);
            }
        }
        Label(pc, "PC_nalepka", "PVK // 01", new Vector3(0, 0.71f, -0.34f), 0.034f, Color.white);
    }

    static void Android(Transform desk, TunnelWorkshop w)
    {
        var actor = Group(desk, "Pavouk_CRT_android", new Vector3(-0.66f, 0, -0.64f));
        actor.localRotation = Quaternion.Euler(0, 27, 0);
        var chair = Group(actor, "Pracovni_zidle");
        Shape(chair, "Pist", PrimitiveType.Cylinder, new Vector3(0, 0.29f, 0), new Vector3(0.09f, 0.24f, 0.09f), metal);
        SoftBox(chair, "Sedak", new Vector3(0, 0.58f, 0), new Vector3(0.54f, 0.11f, 0.5f), rubber);
        SoftBox(chair, "Operadlo", new Vector3(0, 0.95f, -0.24f), new Vector3(0.48f, 0.62f, 0.09f), black);
        Box(chair, "Zadni_pas", new Vector3(0, 0.98f, -0.29f), new Vector3(0.06f, 0.51f, 0.035f), cream);
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.PI * 2 / 5;
            Vector3 end = new Vector3(Mathf.Cos(a) * 0.36f, 0.08f, Mathf.Sin(a) * 0.36f);
            Rod(chair, "Rameno_podnoze", new Vector3(0, 0.13f, 0), end, 0.023f, metal);
            Ball(chair, "Kolecko", end, new Vector3(0.09f, 0.09f, 0.065f), black);
        }
        w.body = Group(actor, "Telo_animace", new Vector3(0, 0.69f, 0.02f));
        SoftBox(w.body, "Bunda", new Vector3(0, 0.24f, 0), new Vector3(0.44f, 0.5f, 0.26f), cream);
        Box(w.body, "Zadovy_panel", new Vector3(0, 0.26f, -0.143f), new Vector3(0.3f, 0.33f, 0.025f), black);
        Label(w.body, "PVK_na_zadech", "PVK\n01", new Vector3(0, 0.29f, -0.161f), 0.065f, new Color(0.4f, 1, 0.6f));
        Rod(w.body, "Krk", new Vector3(0, 0.48f, 0), new Vector3(0, 0.61f, 0), 0.058f, metal);
        w.head = Group(w.body, "Hlava_CRT_animace", new Vector3(0, 0.67f, 0.015f));
        SoftBox(w.head, "CRT_kryt", new Vector3(0, 0.02f, -0.035f), new Vector3(0.49f, 0.37f, 0.4f), cream);
        SoftBox(w.head, "CRT_predni_ramecek", new Vector3(0, 0.02f, 0.177f), new Vector3(0.5f, 0.37f, 0.065f), black);
        Box(w.head, "CRT_sklo", new Vector3(0, 0.04f, 0.214f), new Vector3(0.409f, 0.267f, 0.018f), black);
        foreach (float x in new[] { -0.10f, 0.10f }) Box(w.head, "Pixel_oko", new Vector3(x, 0.065f, 0.227f), new Vector3(0.061f, 0.047f, 0.012f), green);
        Box(w.head, "Pixel_usmev", new Vector3(0, -0.04f, 0.227f), new Vector3(0.12f, 0.013f, 0.012f), green);
        for (int i = 0; i < 7; i++) Box(w.head, "CRT_vetrani", new Vector3(-0.18f + i * 0.06f, 0.035f, -0.24f), new Vector3(0.026f, 0.17f, 0.015f), black);
        foreach (float side in new[] { -1f, 1f })
        {
            SoftBox(w.head, "Sluchatko", new Vector3(side * 0.287f, 0.02f, 0.01f), new Vector3(0.10f, 0.24f, 0.2f), black);
            Box(w.head, "Sluchatko_krouzek", new Vector3(side * 0.34f, 0.02f, 0.01f), new Vector3(0.008f, 0.14f, 0.115f), green);
            Rod(w.head, "Oblouk_bok", new Vector3(side * 0.28f, 0.1f, 0), new Vector3(side * 0.23f, 0.27f, 0), 0.025f, black);
        }
        Rod(w.head, "Oblouk_vrsek", new Vector3(-0.23f, 0.27f, 0), new Vector3(0.23f, 0.27f, 0), 0.028f, black);
        Cable(w.body, "Kabel_sluchatek", black, 0.01f, new Vector3(-0.29f, 0.6f, 0), new Vector3(-0.34f, 0.38f, 0), new Vector3(-0.30f, 0.13f, 0.13f));
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 hip = new Vector3(side * 0.145f, 0.65f, 0.05f), knee = new Vector3(side * 0.18f, 0.46f, 0.39f), foot = new Vector3(side * 0.2f, 0.13f, 0.48f);
            Rod(actor, "Stehno", hip, knee, 0.08f, black); Ball(actor, "Koleno", knee, Vector3.one * 0.15f, metal);
            Rod(actor, "Holen", knee, foot, 0.066f, cream);
            Box(actor, "Bota", foot + new Vector3(0, -0.05f, 0.08f), new Vector3(0.17f, 0.12f, 0.29f), black);
        }
        w.leftShoulder = Group(w.body, "Leve_rameno", new Vector3(-0.25f, 0.4f, 0));
        w.rightShoulder = Group(w.body, "Prave_rameno", new Vector3(0.25f, 0.4f, 0));
        w.keyboardHand = Box(desk, "Leva_ruka_klavesnice", new Vector3(-0.43f, 0.956f, -0.19f), new Vector3(0.09f, 0.045f, 0.14f), cream);
        w.mouseHand = Box(desk, "Prava_ruka_mys", new Vector3(0.17f, 0.955f, -0.19f), new Vector3(0.08f, 0.035f, 0.12f), cream);
        w.leftUpperArm = Shape(actor, "Leva_paze", PrimitiveType.Cylinder, Vector3.zero, Vector3.one, cream);
        w.leftForearm = Shape(actor, "Leve_predlokti", PrimitiveType.Cylinder, Vector3.zero, Vector3.one, metal);
        w.rightUpperArm = Shape(actor, "Prava_paze", PrimitiveType.Cylinder, Vector3.zero, Vector3.one, cream);
        w.rightForearm = Shape(actor, "Prave_predlokti", PrimitiveType.Cylinder, Vector3.zero, Vector3.one, metal);
    }

    static void Dress(Transform p, float width, float depth)
    {
        var dressing = Group(p, "Detaily");
        // Ribbed acoustic panels, with a wall-sized spider signature behind the workstation.
        foreach (float x in new[] { -1.75f, 1.75f })
        {
            Box(dressing, "Akusticky_panel", new Vector3(x, 1.85f, depth - 0.20f), new Vector3(0.9f, 1.28f, 0.085f), black);
            for (int i = 0; i < 9; i++) Box(dressing, "Pena_zebro", new Vector3(x - 0.38f + i * 0.095f, 1.85f, depth - 0.255f), new Vector3(0.043f, 1.17f, 0.09f), rubber);
        }
        var sign = Group(dressing, "Pavouci_podpis", new Vector3(0, 2.68f, depth - 0.20f));
        sign.localScale = Vector3.one * 0.70f;
        Ball(sign, "Telo", Vector3.zero, new Vector3(0.20f, 0.27f, 0.025f), green);
        Ball(sign, "Hlavicka", new Vector3(0, 0.18f, 0), new Vector3(0.12f, 0.12f, 0.026f), green);
        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 4; i++)
                Cable(sign, "Noha_" + side + "_" + i, green, 0.012f, new Vector3(side * 0.08f, 0.08f - i * 0.055f, 0), new Vector3(side * (0.20f + i * 0.025f), 0.24f - i * 0.14f, 0), new Vector3(side * (0.42f - i * 0.02f), 0.30f - i * 0.19f, 0));
        Label(dressing, "Nazev_na_zdi", "P A V O U K", new Vector3(0, 2.28f, depth - 0.22f), 0.12f, new Color(0.43f, 0.8f, 0.6f));
        Label(dressing, "Flow_state", "UNDERGROUND / OVERCLOCKED", new Vector3(0, 2.06f, depth - 0.22f), 0.034f, new Color(0.57f, 0.63f, 0.58f));

        var shelf = Group(dressing, "Police", new Vector3(-width / 2 + 0.48f, 0, depth - 0.7f));
        for (int level = 0; level < 3; level++)
        {
            float y = 0.55f + level * 0.55f;
            Box(shelf, "Police_" + level, new Vector3(0, y, 0), new Vector3(0.56f, 0.05f, 0.9f), wood);
            for (int i = 0; i < 5; i++)
                Box(shelf, "Knihy_" + level + "_" + i, new Vector3(-0.05f, y + 0.18f, -0.30f + i * 0.12f), new Vector3(0.28f, 0.28f + (i % 3) * 0.04f, 0.08f), i % 2 == 0 ? cream : black);
        }
        foreach (float z in new[] { -0.42f, 0.42f }) Box(shelf, "Konstrukce", new Vector3(0.18f, 1.02f, z), new Vector3(0.04f, 2, 0.04f), metal);

        // Framed cottage drawing and sticky notes on the opposite wall.
        var board = Group(dressing, "Plan_chatky", new Vector3(width / 2 - 0.18f, 1.7f, depth - 1.5f));
        board.localRotation = Quaternion.Euler(0, 90, 0);
        Box(board, "Ram", Vector3.zero, new Vector3(1.32f, 0.94f, 0.045f), wood);
        Box(board, "Papir", new Vector3(0, 0, -0.027f), new Vector3(1.22f, 0.84f, 0.008f), paper);
        Cable(board, "Chata_obrys", black, 0.008f, new Vector3(-0.45f, -0.20f, -0.035f), new Vector3(-0.45f, 0.10f, -0.035f), new Vector3(0, 0.32f, -0.035f), new Vector3(0.45f, 0.10f, -0.035f), new Vector3(0.45f, -0.20f, -0.035f), new Vector3(-0.45f, -0.20f, -0.035f));
        for (int i = 0; i < 3; i++) Box(board, "Okno_planu", new Vector3(-0.30f + i * 0.30f, -0.035f, -0.037f), new Vector3(0.10f, 0.12f, 0.005f), black);
        Label(board, "Plan_titulek", "CHATA / TOPOLOGY FIX / v137", new Vector3(0, -0.33f, -0.04f), 0.047f, Color.black);

        // Face the rack toward the window, with independent network/disk indicators.
        var rack = Group(dressing, "Rack", TunnelWorkshopPolish.RackPosition(width, depth));
        rack.localRotation = Quaternion.Euler(0, 22, 0);
        Box(rack, "Skrin", Vector3.zero, new Vector3(0.65f, 1.6f, 0.65f), black, true);
        for (int i = 0; i < 7; i++)
        {
            Box(rack, "Jednotka_" + i, new Vector3(0, -0.60f + i * 0.19f, -0.34f), new Vector3(0.57f, 0.14f, 0.025f), metal);
            for (int j = 0; j < 4; j++) Ball(rack, "Stav_LED", new Vector3(-0.20f + j * 0.045f, -0.60f + i * 0.19f, -0.36f), Vector3.one * 0.018f, j == 3 ? amber : green);
            Box(rack, "Mrizka", new Vector3(0.13f, -0.60f + i * 0.19f, -0.36f), new Vector3(0.21f, 0.06f, 0.008f), black);
        }
        Label(rack, "Rack_popis", "BUILD / BAKE / REPEAT", new Vector3(0, 0.74f, -0.36f), 0.037f, Color.white);
        rack.gameObject.AddComponent<WorkshopServerActivity>();
        Cable(dressing, "Napajeni", black, 0.017f, rack.localPosition + new Vector3(0.1f, -0.76f, 0.18f), new Vector3(width / 2 - 0.5f, 0.09f, 3.0f), new Vector3(1, 0.06f, 3.3f));
    }
}
