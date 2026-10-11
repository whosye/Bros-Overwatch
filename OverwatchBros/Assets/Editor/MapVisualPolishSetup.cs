using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class MapVisualPolishSetup
{
    public const string Folder = "Assets/Materials/MapFinish";
    public const string SettingsPath = "Assets/Settings/MapFinishSettings.asset";
    const string RootName = "FinalniVzhled";
    const string Version = "_v4";
    static bool building;
    static double retryAfter;
    static int retries;

    static MapVisualPolishSetup()
    {
        EditorApplication.delayCall += AutoApply;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += AutoApply;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += AutoApply;
        };
    }

    static MapFinishSettings Settings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<MapFinishSettings>(SettingsPath);
        if (settings != null) return settings;
        MapBuildKit.EnsureFolder("Assets/Settings");
        settings = ScriptableObject.CreateInstance<MapFinishSettings>();
        AssetDatabase.CreateAsset(settings, SettingsPath);
        return settings;
    }

    static void AutoApply()
    {
        if (building || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var map = GameObject.Find("Map-Domasov");
        if (map == null || map.transform.Find(RootName + "/" + Version) != null) return;
        // Existing map generators must finish first; do not build over a half-imported scene.
        if (map.transform.Find("Pozemek/Cesty") == null || map.transform.Find("OkoliChaty") == null ||
            AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/leafy_grass_Diffuse.jpg") == null ||
            AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/stone_wall_AO.jpg") == null)
        {
            if (retries++ < 30)
            {
                retryAfter = EditorApplication.timeSinceStartup + 1;
                EditorApplication.update -= Retry;
                EditorApplication.update += Retry;
            }
            return;
        }
        Apply();
    }

    static void Retry()
    {
        if (EditorApplication.timeSinceStartup < retryAfter) return;
        EditorApplication.update -= Retry;
        AutoApply();
    }

    [MenuItem("BrosOverwatch/Mapa/Finalni vzhled/Nastaveni")]
    static void SelectSettings() { Selection.activeObject = Settings(); EditorGUIUtility.PingObject(Selection.activeObject); }

    [MenuItem("BrosOverwatch/Mapa/Finalni vzhled/Pouzit nastaveni a obnovit dekorace")]
    public static void Apply()
    {
        if (building || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var mapObject = GameObject.Find("Map-Domasov");
        if (mapObject == null) return;
        building = true;
        try
        {
            var map = mapObject.transform;
            var settings = Settings();
            MapBuildKit.EnsureFolder(Folder + "/Meshes");
            MapBuildKit.EnsureFolder(Folder + "/Originals");
            if (!File.Exists("Temp/MapFinish_Before.png")) Capture("Before");
            int originalColliders = map.GetComponentsInChildren<Collider>(true).Length;
            ConfigureMaterials(settings);
            var old = map.Find(RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = MapBuildKit.Group(map, RootName);
            ConfigureDaylight(root, settings);
            UnifyGroundUVs(map);
            var dressing = new Dressing(map, root, settings);
            dressing.Build();
            new GameObject(Version).transform.SetParent(root, false);
            int newColliders = map.GetComponentsInChildren<Collider>(true).Length;
            if (originalColliders != newColliders) throw new InvalidOperationException("Map finish changed collision count.");
            EditorSceneManager.MarkSceneDirty(mapObject.scene);
            AssetDatabase.SaveAssets();
            SceneView.RepaintAll();
            string report = $"Grass clumps: {dressing.grassCount}\nWildflower clumps: {dressing.flowerCount}\nWindow boxes: {dressing.boxCount}\nGarden pots: {dressing.potCount}\nDecoration triangles: {dressing.geometry.Triangles}\nColliders before/after: {originalColliders}/{newColliders}\nSettings: {SettingsPath}\n";
            File.WriteAllText("Temp/MapFinishReport.txt", report);
            Debug.Log("[MapFinish] Denni svetlo, materialy a dekorace pripraveny. Uloz scenu Ctrl+S.\n" + report);
            EditorApplication.delayCall += CapturePreviews;
        }
        catch (Exception e) { Debug.LogException(e); }
        finally { building = false; }
    }

    static void ConfigureMaterials(MapFinishSettings s)
    {
        foreach (string path in Directory.GetFiles(Folder + "/Textures", "*.jpg"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
            if (importer == null) throw new InvalidOperationException("Texture not imported: " + path);
            bool normal = path.Contains("_nor_gl");
            bool srgb = path.Contains("_Diffuse");
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool changed = importer.textureType != type || importer.sRGBTexture != srgb || importer.anisoLevel != 8 ||
                           importer.maxTextureSize != 2048 || !importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Repeat;
            if (!changed) continue;
            importer.textureType = type; importer.sRGBTexture = srgb;
            importer.anisoLevel = 8; importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        Surface("Assets/Materials/Garden/Lawn.mat", "leafy_grass", s.lawnTint, 1f / s.grassTextureMetres, .45f, .06f);
        Surface("Assets/Materials/Garden/Gravel.mat", "gravelly_sand", new Color(.9f,.91f,.89f), .4f, .7f, .07f);
        Surface("Assets/Materials/House/Plaster.mat", "painted_plaster_wall", new Color(.96f,.94f,.89f), .5f, .35f, .09f);
        Surface("Assets/Materials/House/SmallCottage_Plaster.mat", "painted_plaster_wall", new Color(.92f,.88f,.77f), .5f, .35f, .09f);
        Surface("Assets/Materials/House/Chimney.mat", "painted_plaster_wall", new Color(.75f,.75f,.72f), .5f, .4f, .07f);
        Surface("Assets/Materials/House/RoofTiles.mat", "grey_roof_01", new Color(.88f,.89f,.90f), .125f, .7f, .18f);
        Surface("Assets/Materials/Stone/Stone_Wall.mat", "stone_wall", new Color(.87f,.88f,.87f), .5f, .65f, .08f);

        // Mutate the existing materials: the older setup scripts recognize them by asset identity.
        for (int i = 0; i < 3; i++)
        {
            Tint($"Assets/Materials/Forest/Forest_Broadleaf_{i}.mat", Color.Lerp(new Color(.66f,.77f,.61f), new Color(.85f,.87f,.72f), i / 2f));
            Tint($"Assets/Materials/Forest/Forest_Conifer_{i}.mat", Color.Lerp(new Color(.50f,.66f,.59f), new Color(.68f,.76f,.64f), i / 2f));
            Tint($"Assets/Materials/Forest/Forest_Shrub_{i}.mat", Color.Lerp(new Color(.69f,.78f,.58f), new Color(.83f,.86f,.69f), i / 2f));
        }
        Tint("Assets/Materials/Garden/Hedge.mat", new Color(.70f,.79f,.61f));
    }

    static void Backup(Material material)
    {
        string path = Folder + "/Originals/" + material.name + ".mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) == null)
            AssetDatabase.CreateAsset(new Material(material), path);
    }

    static void Tint(string path, Color tint)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) return;
        Backup(material);
        material.SetColor("_BaseColor", tint); material.color = tint;
        EditorUtility.SetDirty(material);
    }

    static Material Surface(string path, string texture, Color tint, float tiling, float normal, float smoothness)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        else Backup(material);
        Texture2D Load(string suffix) => AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/" + texture + suffix + ".jpg");
        material.SetTexture("_BaseMap", Load("_Diffuse"));
        material.mainTexture = Load("_Diffuse");
        material.SetTextureScale("_BaseMap", Vector2.one * tiling);
        material.mainTextureScale = Vector2.one * tiling;
        material.SetTexture("_BumpMap", Load("_nor_gl")); material.SetFloat("_BumpScale", normal);
        material.SetTexture("_OcclusionMap", Load("_AO")); material.SetFloat("_OcclusionStrength", .65f);
        material.SetColor("_BaseColor", tint); material.color = tint;
        material.SetFloat("_Smoothness", smoothness); material.SetFloat("_Metallic", 0);
        material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_OCCLUSIONMAP");
        EditorUtility.SetDirty(material);
        return material;
    }

    static void ConfigureDaylight(Transform root, MapFinishSettings s)
    {
        var scene = root.gameObject.scene;
        var sun = Object.FindObjectsByType<Light>()
            .FirstOrDefault(l => l.gameObject.scene == scene && l.type == LightType.Directional && l.isActiveAndEnabled);
        if (sun == null)
        {
            var go = new GameObject("Slunce"); go.transform.SetParent(root, false);
            sun = go.AddComponent<Light>(); sun.type = LightType.Directional;
        }
        Undo.RecordObjects(new Object[] { sun, sun.transform }, "Denni svetlo mapy");
        sun.transform.rotation = Quaternion.Euler(s.sunElevation, s.sunAzimuth, 0);
        sun.color = Color.white; sun.useColorTemperature = true; sun.colorTemperature = s.sunTemperature;
        sun.intensity = s.sunIntensity; sun.shadows = LightShadows.Soft;
        sun.shadowStrength = .92f; sun.shadowBias = .035f; sun.shadowNormalBias = .25f;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.70f,.78f,.88f) * s.ambientIntensity;
        RenderSettings.ambientEquatorColor = new Color(.55f,.60f,.56f) * s.ambientIntensity;
        RenderSettings.ambientGroundColor = new Color(.32f,.33f,.25f) * s.ambientIntensity;
        RenderSettings.reflectionIntensity = .8f;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(.67f,.76f,.82f);
        RenderSettings.fogStartDistance = 180; RenderSettings.fogEndDistance = 650;
        DynamicGI.UpdateEnvironment();

        string path = Folder + "/DaylightVolume.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
        T Effect<T>() where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing)) return existing;
            var component = profile.Add<T>(false); AssetDatabase.AddObjectToAsset(component, profile); return component;
        }
        var tone = Effect<Tonemapping>(); tone.mode.Override(TonemappingMode.Neutral);
        var color = Effect<ColorAdjustments>();
        color.postExposure.Override(s.exposure); color.contrast.Override(3); color.saturation.Override(s.saturation);
        var white = Effect<WhiteBalance>(); white.temperature.Override(1); white.tint.Override(0);
        var bloom = Effect<Bloom>(); bloom.intensity.Override(.08f); bloom.threshold.Override(1.25f);
        foreach (var component in profile.components) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(profile);
        var volume = root.gameObject.AddComponent<Volume>();
        volume.isGlobal = true; volume.priority = 5; volume.sharedProfile = profile;
        // Extend distant shadows without increasing shadow-map size or adding real-time lights.
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        if (pipeline != null) { pipeline.shadowDistance = 90; EditorUtility.SetDirty(pipeline); }
    }

    static void UnifyGroundUVs(Transform map)
    {
        var lawn = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Garden/Lawn.mat");
        var gravel = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Garden/Gravel.mat");
        int index = 0;
        foreach (var renderer in map.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.sharedMaterial != lawn && renderer.sharedMaterial != gravel) continue;
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            // Only horizontal ground surfaces; leave building walls and mesh colliders untouched.
            if (renderer.bounds.size.y > .1f) continue;
            var mesh = Object.Instantiate(filter.sharedMesh);
            var uv = mesh.vertices.Select(v => renderer.transform.TransformPoint(v)).Select(p => new Vector2(p.x, p.z)).ToArray();
            mesh.uv = uv; mesh.RecalculateTangents();
            filter.sharedMesh = MapFinishGeometry.Save(mesh, "GroundUV_" + index++);
        }
    }

    sealed class Dressing
    {
        readonly Transform map, root;
        readonly MapFinishSettings settings;
        readonly System.Random random;
        readonly List<PathSurface> paths = new List<PathSurface>();
        readonly List<Vector3> reserved = new List<Vector3>();
        readonly Bounds ground;
        readonly Material green, dry, stem, soil, terracotta, rim, timber, edge, wear;
        readonly Material[] petals;
        public readonly MapFinishGeometry geometry = new MapFinishGeometry();
        public int grassCount, flowerCount, boxCount, potCount;

        sealed class PathSurface
        {
            public MeshRenderer renderer;
            public Bounds local;
            public bool gravel;
            public Matrix4x4 toLocal, toWorld;
        }

        public Dressing(Transform map, Transform root, MapFinishSettings settings)
        {
            this.map = map; this.root = root; this.settings = settings;
            random = new System.Random(settings.decorationSeed); ground = MapBuildKit.GroundBounds(map);
            var gravel = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Garden/Gravel.mat");
            foreach (var r in map.GetComponentsInChildren<MeshRenderer>())
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || r.bounds.size.y > .15f) continue;
                if (r.sharedMaterial == gravel || r.name == "Dlazba")
                    paths.Add(new PathSurface { renderer=r, local=filter.sharedMesh.bounds, gravel=r.sharedMaterial==gravel,
                        toLocal=r.transform.worldToLocalMatrix, toWorld=r.transform.localToWorldMatrix });
            }
            foreach (var t in map.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("SpawnPoint") || t.name.StartsWith("CapturePoint")) reserved.Add(t.position);
            green = Solid("MeadowGreen", new Color(.25f,.36f,.12f), true);
            dry = Solid("MeadowDry", new Color(.46f,.43f,.24f), true);
            stem = Solid("FlowerFoliage", new Color(.17f,.30f,.10f), true);
            soil = Solid("PlanterSoil", new Color(.14f,.105f,.065f));
            terracotta = Solid("Terracotta", new Color(.49f,.235f,.13f));
            rim = Solid("TerracottaRim", new Color(.59f,.31f,.19f));
            timber = Solid("PlanterWood", new Color(.22f,.14f,.08f));
            petals = new[] { Solid("PetalsCream",new Color(.93f,.86f,.63f),true),
                Solid("PetalsLavender",new Color(.46f,.31f,.57f),true), Solid("PetalsGeranium",new Color(.66f,.11f,.16f),true) };
            edge = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/PathEdge.mat");
            if (edge == null) { edge = new Material(Shader.Find("BrosOverwatch/Ground Edge")); AssetDatabase.CreateAsset(edge, Folder + "/PathEdge.mat"); }
            edge.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/gravelly_sand_Diffuse.jpg"));
            edge.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/gravelly_sand_nor_gl.jpg"));
            edge.SetTextureScale("_BaseMap", Vector2.one * .4f); edge.SetColor("_BaseColor", new Color(.9f,.91f,.89f));
            EditorUtility.SetDirty(edge);
            wear = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/WornGrassEdge.mat");
            if(wear==null) { wear = new Material(edge); AssetDatabase.CreateAsset(wear,Folder+"/WornGrassEdge.mat"); }
            wear.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/grass_ground_Diffuse.jpg"));
            wear.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/grass_ground_nor_gl.jpg"));
            wear.SetTextureScale("_BaseMap",Vector2.one*.5f); wear.SetColor("_BaseColor",new Color(.8f,.85f,.67f));
            EditorUtility.SetDirty(wear);
        }

        Material Solid(string name, Color color, bool doubleSided = false)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + name + ".mat");
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, Folder + "/" + name + ".mat"); }
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .1f);
            material.SetFloat("_Cull", doubleSided ? 0 : 2);
            EditorUtility.SetDirty(material); return material;
        }

        float Range(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
        Vector3 Offset(float r) => new Vector3(Range(-r,r),0,Range(-r,r));
        static bool In(Bounds b, Vector3 p, float r) => p.x >= b.min.x-r && p.x <= b.max.x+r && p.z >= b.min.z-r && p.z <= b.max.z+r;
        static bool In(PathSurface path, Vector3 p, float radius)
        {
            Vector3 local = path.toLocal.MultiplyPoint3x4(p);
            Vector3 scale = path.renderer.transform.lossyScale;
            return local.x >= path.local.min.x-radius/Mathf.Abs(scale.x) && local.x <= path.local.max.x+radius/Mathf.Abs(scale.x)
                && local.z >= path.local.min.z-radius/Mathf.Abs(scale.z) && local.z <= path.local.max.z+radius/Mathf.Abs(scale.z);
        }

        bool GroundPoint(ref Vector3 p)
        {
            if (!In(ground, p, -.5f)) return false;
            if (!Physics.Raycast(new Vector3(p.x, 40, p.z), Vector3.down, out var hit, 42, ~0, QueryTriggerInteraction.Ignore)) return false;
            string n = hit.collider.name;
            if (hit.point.y < -.2f || hit.point.y > .3f || !(n.StartsWith("Tile_") || n == "Ground" || n == "Travnik")) return false;
            p.y = hit.point.y + .018f; return true;
        }

        bool Free(ref Vector3 p, float radius)
        {
            Vector3 position = p;
            if (reserved.Any(q => new Vector2(q.x-position.x,q.z-position.z).sqrMagnitude < (radius+3.8f)*(radius+3.8f))) return false;
            if (paths.Any(b => In(b, position, radius + .12f)) || !GroundPoint(ref p)) return false;
            position = p;
            return !Physics.OverlapBox(p+Vector3.up*.6f,new Vector3(radius,.48f,radius),Quaternion.identity,~0,QueryTriggerInteraction.Ignore)
                .Any(c => c.bounds.max.y > position.y+.15f);
        }

        public void Build()
        {
            Physics.SyncTransforms();
            SoftenPaths();
            // Prefer verges and the woodland fringe, rather than evenly sprinkling the playing field.
            for (int i = 0; i < settings.grassClumps*18 && grassCount < settings.grassClumps; i++)
            {
                Vector3 p = new Vector3(Range(ground.min.x+1,ground.max.x-1),0,Range(ground.min.z+1,ground.max.z-1));
                float boundary = Mathf.Min(p.x-ground.min.x,ground.max.x-p.x,p.z-ground.min.z,ground.max.z-p.z);
                bool verge = paths.Any(b => In(b,p,2.1f));
                float noise = Mathf.PerlinNoise(p.x*.17f+41,p.z*.17f+12);
                if ((!verge && boundary > 12) || noise < .37f || !Free(ref p,.18f)) continue;
                Grass(p, verge ? Range(.12f,.26f) : Range(.20f,.40f)); grassCount++;
                if(grassCount%8==0) WornPatch(p);
                if (flowerCount < settings.wildflowerClumps && noise > .56f && random.Next(5)==0)
                {
                    for (int j=0;j<4;j++) Flower(p+Offset(.24f),Range(.20f,.38f),random.Next(2));
                    flowerCount++;
                }
            }
            if (settings.windowBoxes) WindowBoxes();
            if (settings.gardenPots) GardenPots();
            geometry.Bake(root);
        }

        void SoftenPaths()
        {
            foreach (var path in paths)
            {
                if (!path.gravel) continue;
                // Include the rotated Yaris road pieces as well as the old axis-aligned footpaths.
                Bounds b = path.local;
                Vector3[] corners = { new Vector3(b.min.x,b.max.y,b.min.z),new Vector3(b.max.x,b.max.y,b.min.z),new Vector3(b.max.x,b.max.y,b.max.z),new Vector3(b.min.x,b.max.y,b.max.z) };
                for(int corner=0;corner<4;corner++)corners[corner]=path.toWorld.MultiplyPoint3x4(corners[corner]);
                for (int side=0;side<4;side++)
                {
                    Vector3 a = corners[side], z = corners[(side+1)%4], direction=(z-a).normalized;
                    Vector3 outward = Vector3.Cross(Vector3.up,direction);
                    int count = Mathf.CeilToInt(Vector3.Distance(a,z)/.7f);
                    for (int j=0;j<count;j++)
                    {
                        Vector3 p=Vector3.Lerp(a,z,(float)j/count), q=Vector3.Lerp(a,z,(float)(j+1)/count);
                        float Width(Vector3 v) => settings.pathEdgeWidth*(.60f+.65f*Mathf.PerlinNoise(v.x*1.6f+73,v.z*1.6f+16));
                        Vector3 o=p+outward*Width(p), t=q+outward*Width(q), mid=(o+t)*.5f;
                        if (paths.Any(other => In(other,mid,.01f)) || !GroundPoint(ref mid) || !GroundPoint(ref o) || !GroundPoint(ref t)) continue;
                        p -= outward*.08f; q -= outward*.08f;
                        p.y+=.006f; q.y+=.006f; o.y+=.006f; t.y+=.006f;
                        geometry.For(edge,mid).Quad(p,q,t,o,1,1,0,0);
                    }
                }
            }
        }

        void Grass(Vector3 p, float height)
        {
            var part=geometry.For(random.Next(6)==0?dry:green,p);
            for(int i=0;i<12;i++)
            {
                float angle=Range(0,Mathf.PI*2), h=height*Range(.55f,1.2f);
                Vector3 lateral=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Vector3 at=p+Offset(.17f), bend=Vector3.Cross(lateral,Vector3.up)*Range(.035f,.12f), width=lateral*Range(.009f,.018f);
                Vector3 middle=at+Vector3.up*h*.55f+bend*.25f;
                part.Quad(at-width,at+width,middle+width*.55f,middle-width*.55f);
                part.Triangle(middle-width*.55f,middle+width*.55f,at+Vector3.up*h+bend);
            }
        }

        void WornPatch(Vector3 p)
        {
            const float radius=.7f;
            if(!Free(ref p,radius))return;
            var part=geometry.For(wear,p);
            p.y+=.009f;
            for(int i=0;i<12;i++)
            {
                float a=i*Mathf.PI/6,b=(i+1)*Mathf.PI/6;
                Vector3 q=p+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius;
                Vector3 r=p+new Vector3(Mathf.Cos(b),0,Mathf.Sin(b))*radius;
                Vector3 checkQ=q,checkR=r;
                if(!GroundPoint(ref checkQ)||!GroundPoint(ref checkR))continue;
                part.Quad(p,p,q,r,.40f,.40f,0,0);
            }
        }

        void Flower(Vector3 p, float height, int color)
        {
            var foliage=geometry.For(stem,p);
            Vector3 head=p+Vector3.up*height+Offset(.04f), width=Vector3.right*.006f;
            foliage.Quad(p-width,p+width,head+width,head-width);
            for(int j=0;j<3;j++)
            {
                float a=Range(0,6.28f); Vector3 at=Vector3.Lerp(p,head,.24f+j*.19f);
                Vector3 along=new Vector3(Mathf.Cos(a),.5f,Mathf.Sin(a))*.10f;
                Vector3 across=Vector3.Cross(along,Vector3.up).normalized*.022f;
                foliage.Quad(at,at+along*.55f+across,at+along,at+along*.55f-across);
            }
            var flower=geometry.For(petals[color],p);
            float radius=color==1?.030f:.045f;
            for(int j=0;j<6;j++)
            {
                float a=j*Mathf.PI/3;
                Vector3 along=new Vector3(Mathf.Cos(a),.16f,Mathf.Sin(a))*radius;
                Vector3 across=Vector3.Cross(along,Vector3.up).normalized*radius*.48f;
                flower.Quad(head,head+along*.8f+across,head+along*1.4f,head+along*.8f-across);
            }
        }

        void WindowBoxes()
        {
            foreach (string path in new[] { "HlavniChata/Exterier", "MensiChata" })
            {
                var house=map.Find(path); if(house==null) continue;
                var building=map.Find(path.StartsWith("Hlavni")?"HlavniChata/PRIZEMI":"MensiChata");
                var renderers=building.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled).ToArray();
                if(renderers.Length==0) continue;
                Bounds footprint=renderers[0].bounds; foreach(var r in renderers)footprint.Encapsulate(r.bounds);
                foreach(var sill in house.GetComponentsInChildren<MeshRenderer>())
                {
                    if(sill.name!="Parapet" || sill.bounds.size.y>.25f)continue;
                    Bounds b=sill.bounds;
                    // High windows remain unobstructed; boxes sit entirely below the existing sill.
                    if(b.center.y<.65f || b.center.y>8f)continue;
                    bool alongX=b.size.x>b.size.z;
                    Vector3 outward=alongX?(b.center.z<footprint.center.z?Vector3.back:Vector3.forward):(b.center.x<footprint.center.x?Vector3.left:Vector3.right);
                    float length=Mathf.Clamp((alongX?b.size.x:b.size.z)*.72f,.65f,1.4f);
                    Vector3 p=b.center+outward*.22f-Vector3.up*.23f;
                    Quaternion rotation=Quaternion.LookRotation(outward);
                    Vector3 Local(float x,float y,float z)=>p+rotation*new Vector3(x,y,z);
                    var wood=geometry.For(timber,p);
                    wood.Box(Local(0,-.10f,0),new Vector3(length,.04f,.28f),rotation);
                    wood.Box(Local(0,0,.145f),new Vector3(length,.23f,.04f),rotation);
                    wood.Box(Local(0,0,-.145f),new Vector3(length,.23f,.04f),rotation);
                    foreach(float side in new[]{-1f,1f})wood.Box(Local(side*(length*.5f-.02f),0,0),new Vector3(.04f,.23f,.28f),rotation);
                    geometry.For(soil,p).Box(Local(0,.08f,0),new Vector3(length-.06f,.025f,.24f),rotation);
                    for(int k=0;k<12;k++)Flower(Local(Range(-length*.42f,length*.42f),.09f,Range(-.09f,.09f)),Range(.12f,.20f),boxCount%3==0?1:2);
                    boxCount++;
                }
            }
        }

        void GardenPots()
        {
            // Place in clear lawns beside existing paths, rather than guessing building or door positions.
            for(int i=0;i<1200 && potCount<12;i++)
            {
                Vector3 p=new Vector3(Range(ground.min.x+7,ground.max.x-7),0,Range(ground.min.z+4,45));
                if(!paths.Any(b=>In(b,p,2.0f)) || !Free(ref p,.55f))continue;
                if(pots.Any(q=>(q-p).sqrMagnitude<12*12))continue;
                pots.Add(p); Pot(p,Range(.40f,.53f)); potCount++;
            }
        }
        readonly List<Vector3> pots=new List<Vector3>();

        void Pot(Vector3 p,float h)
        {
            // Hollow, tapered terracotta body with a proper lip and visible soil, no collider.
            var clay=geometry.For(terracotta,p); var lip=geometry.For(rim,p);
            const int sides=24;
            for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
                Vector3 P(float angle,float radius,float y)=>p+new Vector3(Mathf.Cos(angle)*radius,y,Mathf.Sin(angle)*radius);
                clay.Quad(P(b,h*.34f,0),P(a,h*.34f,0),P(a,h*.49f,h),P(b,h*.49f,h));
                lip.Quad(P(b,h*.54f,h*.87f),P(a,h*.54f,h*.87f),P(a,h*.54f,h*1.02f),P(b,h*.54f,h*1.02f));
                lip.Quad(P(a,h*.54f,h*1.02f),P(a,h*.43f,h*1.02f),P(b,h*.43f,h*1.02f),P(b,h*.54f,h*1.02f));
                clay.Quad(P(a,h*.43f,h*1.02f),P(a,h*.40f,h*.85f),P(b,h*.40f,h*.85f),P(b,h*.43f,h*1.02f));
                geometry.For(soil,p).Triangle(p+Vector3.up*h*.86f,P(b,h*.40f,h*.86f),P(a,h*.40f,h*.86f));
            }
            for(int j=0;j<24;j++)Flower(p+Offset(h*.22f)+Vector3.up*h*.87f,Range(.18f,.38f),potCount%3);
        }
    }

    [MenuItem("BrosOverwatch/Mapa/Finalni vzhled/Ulozit kontrolni nahledy")]
    static void CapturePreviews() { Capture("After"); Capture("Walk"); Capture("Detail"); }

    static void Capture(string name)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var go = new GameObject("MapFinishPreview") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>(); camera.enabled = false;
        var data = go.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = true;
        camera.fieldOfView = 62; camera.nearClipPlane = .15f; camera.farClipPlane = 400;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.useOcclusionCulling = false;
        bool walk = name == "Walk";
        bool detail = name == "Detail";
        go.transform.position = walk ? new Vector3(-5,1.8f,-17) : detail ? new Vector3(5,2.2f,-21) : new Vector3(58,52,-67);
        go.transform.LookAt(walk ? new Vector3(0,2,19) : detail ? new Vector3(14,2,-14) : new Vector3(-7,1,28));
        var rt = new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            rt.Create(); camera.aspect = 1600f/900; camera.targetTexture = rt;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
            RenderTexture.active = rt;
            image = new Texture2D(1600,900,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1600,900),0,0); image.Apply();
            File.WriteAllBytes("Temp/MapFinish_"+name+".png",image.EncodeToPNG());
        }
        catch(Exception e) { Debug.LogWarning("[MapFinish] Preview: "+e.Message); }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            if(image!=null)Object.DestroyImmediate(image);
            rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        }
    }
}

[CustomEditor(typeof(MapFinishSettings))]
public sealed class MapFinishSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Uprav hodnoty, klikni na Pouzit a potom uloz scenu Ctrl+S. Dekorace se sestavuji pouze v editoru.",MessageType.Info);
        DrawDefaultInspector();
        using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if(GUILayout.Button("Pouzit vzhled a obnovit dekorace"))MapVisualPolishSetup.Apply();
    }
}
