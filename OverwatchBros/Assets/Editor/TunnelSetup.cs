using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Podzemni tunel na mape Domasov: vstup za rozhlednou (vpravo), vychod v leve prizemni mistnosti hlavni chaty.
// Postavi se samo (idempotentne) v otevrene scene s mapou; puvodni Ground se vypne a nahradi dlazdicemi s otvory
// pro oba vstupy. Scenu je pak potreba ulozit (Ctrl+S).
// Rucne: BrosOverwatch > Tunel > Postavit znovu / Odstranit.
[InitializeOnLoad]
public static class TunnelSetup
{
    const string MapName = "Map-Domasov";
    const string RootName = "Tunel";
    const string Version = "_v6";   // v4: treti vstup; v5: severni vstup bliz; v6: drevene zabradli, u tretiho vstupu zadne (seno)
    const string DisabledPref = "BrosOverwatch.TunnelDisabled";
    const string MaterialFolder = "Assets/Materials/Tunnel";

    const float Depth = 4f;           // podlaha tunelu je 4 m pod zemi
    const float Cell = 0.25f;         // rastr pro vypocet sten
    const float Wall = 0.3f;          // tloustka sten, podlahy a dlazdic
    const float CeilingBottom = -0.5f;
    const float RailHeight = 1f;
    const float LightSpacing = 12f;

    // Ramp: vodorovny obdelnik (xz), melky konec na vysce zeme, hluboky konec na podlaze tunelu.
    struct Ramp
    {
        public Rect area;
        public Vector2 down;          // smer, kterym rampa klesa (jednotkovy, podel x nebo z)
    }

    // Vstup hned za zadni stenou rozhledny (v puvodni scene konci na z = 93): rampa klesa smerem +x.
    // Melky konec je mimo okruh spawnu tymu 1 u bodu 3 (8 m kolem SpawnPoint_3_Team1).
    // Kdyz je rozhledna v otevrene scene posunuta, vstup se posune s ni (viz MapBuildKit.TowerShift).
    // (v5: posunuto z z 94-97 na 84-87 - sever mapy konci na z 92, viz ForestBoundarySetup.NorthLimit)
    static readonly Rect BaseEntrance = Rect.MinMaxRect(-34f, 84f, -26f, 87f);
    static Ramp EntranceRamp = new Ramp { area = BaseEntrance, down = Vector2.right };
    // Vychod v leve prizemni mistnosti hlavni chaty: rampa u venkovni zdi, klesa smerem -x; vedle ni zustava ulicka ke dverim.
    static readonly Ramp HouseRamp = new Ramp { area = Rect.MinMaxRect(17f, -18.75f, 25f, -16.75f), down = Vector2.left };
    // Treti vstup v pulce tunelu u paty plosiny rozhledny (bocni cesta k bodu 2): rampa zapadne od hlavni chodby,
    // klesa smerem +x a ustí primo do ni. Vychodne od chodby vede trasa Yarisu, proto na zapadni strane.
    static readonly Ramp MidRamp = new Ramp { area = Rect.MinMaxRect(-9.5f, 35f, -1.5f, 37f), down = Vector2.right };

    // Chodby (xz): od vstupu k ose x = 0, podel ni k chate a pak pod chatu k rampe. Pocitaji se v Layout() podle vstupu.
    static Rect[] Corridors;

    // Osy chodeb pro svetla (od - do).
    static Vector2[][] LightPaths;

    // Rozlozeni tunelu podle polohy rozhledny v otevrene scene.
    static void Layout(Transform map)
    {
        Vector3 shift = Shift(map);
        Rect e = new Rect(BaseEntrance.position + new Vector2(shift.x, shift.z), BaseEntrance.size);
        EntranceRamp = new Ramp { area = e, down = Vector2.right };

        Corridors = new[]
        {
            Rect.MinMaxRect(e.xMax, e.yMin, 1.5f, e.yMax),
            Rect.MinMaxRect(-1.5f, -19.25f, 1.5f, e.yMax),
            Rect.MinMaxRect(-1.5f, -19.25f, 17f, -16.25f),
        };
        LightPaths = new[]
        {
            new[] { new Vector2(e.xMax + 2f, e.center.y), new Vector2(0f, e.center.y) },
            new[] { new Vector2(0f, e.center.y - 11.5f), new Vector2(0f, -17.75f) },
            new[] { new Vector2(7f, -17.75f), new Vector2(15f, -17.75f) },
        };
    }

    // Posun rozhledny; kdyz by tunel vysel mimo mapu nebo pres osu chodby, zustane puvodni rozlozeni.
    static Vector3 Shift(Transform map)
    {
        Vector3 shift = MapBuildKit.TowerShift(map);
        Bounds ground = MapBuildKit.GroundBounds(map);
        Rect e = new Rect(BaseEntrance.position + new Vector2(shift.x, shift.z), BaseEntrance.size);
        bool ok = e.xMax < -4f && e.yMin > 5f && e.xMin > ground.min.x + 1f && e.yMax < ground.max.z - 1f;
        return ok ? shift : Vector3.zero;
    }

    static string Marker(Transform map)
    {
        Vector3 shift = Shift(map);
        return $"{Version}_{shift.x:0.0}_{shift.z:0.0}";
    }

    static TunnelSetup()
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
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorPrefs.GetBool(DisabledPref, false)) return;

        var map = GameObject.Find(MapName);
        if (map == null) return;

        var root = map.transform.Find(RootName);
        if (root != null && root.Find(Marker(map.transform)) != null) return;

        Build(map.transform);
    }

    [MenuItem("BrosOverwatch/Tunel/Postavit znovu")]
    static void RebuildMenu()
    {
        EditorPrefs.SetBool(DisabledPref, false);
        var map = GameObject.Find(MapName);
        if (map == null)
        {
            Debug.LogWarning("[Tunel] Ve scene neni mapa " + MapName + ".");
            return;
        }
        Build(map.transform);
    }

    [MenuItem("BrosOverwatch/Tunel/Odstranit")]
    static void RemoveMenu()
    {
        EditorPrefs.SetBool(DisabledPref, true);
        var map = GameObject.Find(MapName);
        if (map == null) return;

        var root = map.transform.Find(RootName);
        if (root != null)
            Object.DestroyImmediate(root.gameObject);

        var ground = map.transform.Find("Ground");
        if (ground != null)
            ground.gameObject.SetActive(true);

        EditorSceneManager.MarkSceneDirty(map.scene);
        Debug.Log("[Tunel] Tunel odstranen, puvodni Ground je zase zapnuty. Uloz scenu.");
    }

    static void Build(Transform map)
    {
        var ground = map.Find("Ground");
        if (ground == null)
        {
            Debug.LogWarning("[Tunel] Na mape chybi objekt Ground.");
            return;
        }

        var old = map.Find(RootName);
        if (old != null)
            Object.DestroyImmediate(old.gameObject);

        // Rozmer puvodni podlahy zjistime pred vypnutim.
        ground.gameObject.SetActive(true);
        var groundRenderer = ground.GetComponent<Renderer>();
        Bounds groundBounds = groundRenderer.bounds;
        Material groundMaterial = groundRenderer.sharedMaterial;
        float groundY = ground.position.y;
        ground.gameObject.SetActive(false);

        var root = new GameObject(RootName).transform;
        root.SetParent(map, true);
        new GameObject(Marker(map)).transform.SetParent(root, false);
        Layout(map);

        var concrete = GetMaterial("Tunnel_Concrete", new Color(0.42f, 0.41f, 0.39f), Color.black);
        var darkConcrete = GetMaterial("Tunnel_ConcreteDark", new Color(0.30f, 0.29f, 0.28f), Color.black);
        var lamp = GetMaterial("Tunnel_Lamp", new Color(1f, 0.9f, 0.7f), new Color(2.4f, 1.9f, 1.2f));
        // (drive cervene zabradli - ted drevene jako ostatni ploty na mape)
        var rail = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wood/Wood_Beam.mat")
            ?? GetMaterial("Tunnel_Rail", new Color(0.55f, 0.18f, 0.12f), Color.black);

        float floorY = groundY - Depth;

        BuildGround(root, groundBounds, groundY, groundMaterial);
        BuildFloorAndCeiling(root, floorY, groundY, concrete, darkConcrete);
        BuildRamp(root, EntranceRamp, groundY, floorY, darkConcrete);
        BuildRamp(root, HouseRamp, groundY, floorY, darkConcrete);
        BuildRamp(root, MidRamp, groundY, floorY, darkConcrete);
        BuildWalls(root, floorY, groundY, concrete);
        BuildRails(root, EntranceRamp, groundY, rail, true);
        BuildRails(root, HouseRamp, groundY, rail, false);
        // treti vstup: bez zabradli, kolem nej jsou baliky sena (MapPlayabilitySetup)
        BuildLights(root, groundY, lamp);

        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        Debug.Log("[Tunel] Tunel postaven (vstup za rozhlednou -> leva prizemni mistnost hlavni chaty). Uloz scenu (Ctrl+S).");
    }

    // ---------------- podlaha mapy s otvory ----------------

    static void BuildGround(Transform root, Bounds bounds, float groundY, Material material)
    {
        var parent = Group(root, "Ground_Tiles");
        var holes = new[] { EntranceRamp.area, HouseRamp.area, MidRamp.area };

        // Rozrez podlahy na svisle pasy podle hran otvoru; v kazdem pasu vynech otvory, ktere ho cele protinaji.
        var xs = new List<float> { bounds.min.x, bounds.max.x };
        foreach (var h in holes)
        {
            xs.Add(h.xMin);
            xs.Add(h.xMax);
        }
        xs.Sort();

        int index = 0;
        for (int i = 0; i < xs.Count - 1; i++)
        {
            float x0 = xs[i], x1 = xs[i + 1];
            if (x1 - x0 < 0.001f || x0 < bounds.min.x - 0.001f || x1 > bounds.max.x + 0.001f) continue;

            var cuts = new List<Vector2>();
            foreach (var h in holes)
                if (h.xMin <= x0 + 0.001f && h.xMax >= x1 - 0.001f)
                    cuts.Add(new Vector2(h.yMin, h.yMax));
            cuts.Sort((a, b) => a.x.CompareTo(b.x));

            float z = bounds.min.z;
            foreach (var c in cuts)
            {
                if (c.x > z + 0.001f)
                    Box(parent, "Tile_" + index++, new Vector3(x0, groundY - Wall, z), new Vector3(x1, groundY, c.x), material);
                z = Mathf.Max(z, c.y);
            }
            if (bounds.max.z > z + 0.001f)
                Box(parent, "Tile_" + index++, new Vector3(x0, groundY - Wall, z), new Vector3(x1, groundY, bounds.max.z), material);
        }
    }

    // ---------------- chodby ----------------

    static void BuildFloorAndCeiling(Transform root, float floorY, float groundY, Material floor, Material ceiling)
    {
        var parent = Group(root, "Corridors");
        for (int i = 0; i < Corridors.Length; i++)
        {
            var r = Corridors[i];
            Box(parent, "Floor_" + i, new Vector3(r.xMin, floorY - Wall, r.yMin), new Vector3(r.xMax, floorY, r.yMax), floor);
            Box(parent, "Ceiling_" + i, new Vector3(r.xMin, groundY + CeilingBottom, r.yMin),
                new Vector3(r.xMax, groundY - Wall, r.yMax), ceiling);
        }
    }

    static void BuildRamp(Transform root, Ramp ramp, float groundY, float floorY, Material material)
    {
        var a = ramp.area;
        Vector2 center = a.center;
        float length = ramp.down.x != 0f ? a.width : a.height;
        float width = ramp.down.x != 0f ? a.height : a.width;

        // Melky konec (na zemi) a hluboky konec (na podlaze tunelu) uprostred sirky rampy.
        Vector2 top2 = center - ramp.down * (length * 0.5f);
        Vector2 bottom2 = center + ramp.down * (length * 0.5f);
        Vector3 top = new Vector3(top2.x, groundY, top2.y);
        Vector3 bottom = new Vector3(bottom2.x, floorY, bottom2.y);

        Vector3 along = bottom - top;
        var rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
        float slab = Wall;

        // Hluboky konec se protahne pod podlahu, aby na prechodu nebyla mezera.
        float extra = 0.6f;
        Vector3 mid = (top + bottom) * 0.5f + along.normalized * (extra * 0.5f) + rotation * new Vector3(0f, -slab * 0.5f, 0f);

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Ramp";
        go.transform.SetParent(Group(root, "Ramps"), true);
        go.transform.SetPositionAndRotation(mid, rotation);
        go.transform.localScale = new Vector3(width, slab, along.magnitude + extra);
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    // Steny po obvodu sjednoceni chodeb a ramp (spocitane na rastru, takze rohy i napojeni sedi samy).
    // Konec steny se protahne jen na vnejsim rohu (jinak by ve vnitrnim rohu zasahoval do chodby).
    static void BuildWalls(Transform root, float floorY, float groundY, Material material)
    {
        var parent = Group(root, "Walls");
        var areas = new List<Rect>(Corridors) { EntranceRamp.area, HouseRamp.area, MidRamp.area };

        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var r in areas)
        {
            minX = Mathf.Min(minX, r.xMin);
            minZ = Mathf.Min(minZ, r.yMin);
            maxX = Mathf.Max(maxX, r.xMax);
            maxZ = Mathf.Max(maxZ, r.yMax);
        }

        int nx = Mathf.CeilToInt((maxX - minX) / Cell);
        int nz = Mathf.CeilToInt((maxZ - minZ) / Cell);
        var inside = new bool[nx, nz];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                var p = new Vector2(minX + (i + 0.5f) * Cell, minZ + (j + 0.5f) * Cell);
                foreach (var r in areas)
                    if (r.Contains(p))
                    {
                        inside[i, j] = true;
                        break;
                    }
            }

        bool In(int i, int j) => i >= 0 && j >= 0 && i < nx && j < nz && inside[i, j];

        float bottom = floorY - Wall;
        float top = groundY - Wall * 0.5f;
        int count = 0;

        // Steny kolme na x (hranice mezi sloupci i-1 a i).
        for (int i = 0; i <= nx; i++)
        {
            int runStart = -1, runSide = 0;
            for (int j = 0; j <= nz; j++)
            {
                int side = 0;
                if (j < nz)
                {
                    bool a = In(i - 1, j), b = In(i, j);
                    if (a && !b) side = 1;
                    else if (!a && b) side = -1;
                }

                if (side != runSide)
                {
                    if (runSide != 0)
                    {
                        float x = minX + i * Cell;
                        float x0 = runSide > 0 ? x : x - Wall, x1 = runSide > 0 ? x + Wall : x;
                        int column = runSide > 0 ? i - 1 : i;
                        float z0 = minZ + runStart * Cell - (In(column, runStart - 1) ? 0f : Wall);
                        float z1 = minZ + j * Cell + (In(column, j) ? 0f : Wall);
                        Box(parent, "Wall_" + count++, new Vector3(x0, bottom, z0), new Vector3(x1, top, z1), material);
                    }
                    runStart = j;
                    runSide = side;
                }
            }
        }

        // Steny kolme na z (hranice mezi radky j-1 a j).
        for (int j = 0; j <= nz; j++)
        {
            int runStart = -1, runSide = 0;
            for (int i = 0; i <= nx; i++)
            {
                int side = 0;
                if (i < nx)
                {
                    bool a = In(i, j - 1), b = In(i, j);
                    if (a && !b) side = 1;
                    else if (!a && b) side = -1;
                }

                if (side != runSide)
                {
                    if (runSide != 0)
                    {
                        float z = minZ + j * Cell;
                        float z0 = runSide > 0 ? z : z - Wall, z1 = runSide > 0 ? z + Wall : z;
                        int row = runSide > 0 ? j - 1 : j;
                        float x0 = minX + runStart * Cell - (In(runStart - 1, row) ? 0f : Wall);
                        float x1 = minX + i * Cell + (In(i, row) ? 0f : Wall);
                        Box(parent, "Wall_" + count++, new Vector3(x0, bottom, z0), new Vector3(x1, top, z1), material);
                    }
                    runStart = i;
                    runSide = side;
                }
            }
        }
    }

    // Zabradli kolem otvoru (melky konec zustava volny, tudy se do tunelu vchazi).
    static void BuildRails(Transform root, Ramp ramp, float groundY, Material material, bool bothSides)
    {
        var parent = Group(root, "Rails");
        var a = ramp.area;
        const float t = 0.12f;
        float y0 = groundY, y1 = groundY + RailHeight;

        if (ramp.down.x != 0f)
        {
            // Rampa klesa podel x: boky jsou na z = yMin / yMax, hluboky konec na x podle smeru.
            Box(parent, "Rail_Side", new Vector3(a.xMin, y0, a.yMax), new Vector3(a.xMax, y1, a.yMax + t), material);
            if (bothSides)
                Box(parent, "Rail_Side", new Vector3(a.xMin, y0, a.yMin - t), new Vector3(a.xMax, y1, a.yMin), material);

            float x = ramp.down.x > 0f ? a.xMax : a.xMin - t;
            Box(parent, "Rail_End", new Vector3(x, y0, a.yMin - t), new Vector3(x + t, y1, a.yMax + t), material);
        }
        else
        {
            Box(parent, "Rail_Side", new Vector3(a.xMax, y0, a.yMin), new Vector3(a.xMax + t, y1, a.yMax), material);
            if (bothSides)
                Box(parent, "Rail_Side", new Vector3(a.xMin - t, y0, a.yMin), new Vector3(a.xMin, y1, a.yMax), material);

            float z = ramp.down.y > 0f ? a.yMax : a.yMin - t;
            Box(parent, "Rail_End", new Vector3(a.xMin - t, y0, z), new Vector3(a.xMax + t, y1, z + t), material);
        }
    }

    static void BuildLights(Transform root, float groundY, Material lampMaterial)
    {
        var parent = Group(root, "Lights");
        int count = 0;
        foreach (var path in LightPaths)
        {
            float length = Vector2.Distance(path[0], path[1]);
            int steps = Mathf.Max(1, Mathf.RoundToInt(length / LightSpacing));
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = Vector2.Lerp(path[0], path[1], s / (float)steps);

                var lampBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lampBox.name = "Lamp_" + count;
                lampBox.transform.SetParent(parent, true);
                lampBox.transform.position = new Vector3(p.x, groundY + CeilingBottom - 0.06f, p.y);
                lampBox.transform.localScale = new Vector3(0.5f, 0.12f, 0.5f);
                lampBox.GetComponent<Renderer>().sharedMaterial = lampMaterial;
                lampBox.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Object.DestroyImmediate(lampBox.GetComponent<Collider>());

                var lightObject = new GameObject("Light_" + count++);
                lightObject.transform.SetParent(parent, true);
                lightObject.transform.position = new Vector3(p.x, groundY + CeilingBottom - 0.4f, p.y);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.82f, 0.6f);
                light.range = 11f;
                light.intensity = 3f;
                light.shadows = LightShadows.None;
            }
        }
    }

    // ---------------- pomocne ----------------

    static Transform Group(Transform root, string name)
    {
        var existing = root.Find(name);
        if (existing != null) return existing;

        var group = new GameObject(name).transform;
        group.SetParent(root, false);
        return group;
    }

    // Kvadr mezi dvema rohy (svetove souradnice), s BoxColliderem.
    static void Box(Transform parent, string name, Vector3 min, Vector3 max, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.position = (min + max) * 0.5f;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = new Vector3(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y), Mathf.Abs(max.z - min.z));
        if (material != null)
            go.GetComponent<Renderer>().sharedMaterial = material;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
    }

    static Material GetMaterial(string name, Color color, Color emission)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/Materials", "Tunnel");

        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        material = new Material(shader);
        material.SetColor("_BaseColor", color);
        material.color = color;
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.15f);
        if (emission.maxColorComponent > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
