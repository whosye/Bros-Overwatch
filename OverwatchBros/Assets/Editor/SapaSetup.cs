using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Full-detail OWM import. Original PNG dimensions and vertex precision are retained.
public static class SapaSetup
{
    public const string Folder = "Assets/Models/Sapa";
    public const string ModelPath = Folder + "/Sapa.glb";
    public const string PrefabPath = Folder + "/Map_Sapa.prefab";
    public const string ScenePath = "Assets/Scenes/SampleScene.unity";
    public static string WorkFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../_MIMO_GIT_K_PRESUNU/Sapa"));
    const string RootName = "Map_Sapa";

    [Serializable] public sealed class SourceTexture { public string name, file; public int width, height; public bool linear; }
    [Serializable] public sealed class SourceInput { public string name, texture; public float[] value; public int mode, uv; }
    [Serializable] public sealed class SourceMaterial { public string name, group; public int style; public bool transparent; public SourceInput[] inputs; }
    [Serializable] public sealed class SourceObject { public string name; public bool collidable; public string[] materials; }
    [Serializable] public sealed class SourceLight { public string name, type; public Vector3 position, direction; public float[] color; public float power, spotAngle, radius; }
    [Serializable] public sealed class Manifest
    {
        public SourceTexture[] textures;
        public SourceMaterial[] materials;
        public SourceObject[] objects;
        public SourceLight[] lights;
        public Vector3 boundsCenter, boundsSize;
        public bool geometryReduced, texturesResized;
    }
    [Serializable] public sealed class NavigationDiagnostic
    {
        public Vector3[] vertices;
        public int[] indices;
        public Vector3[] markers;
    }

    public static Manifest ReadManifest() => JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "/Sapa.json"));

    static void ImportAssets(Manifest manifest)
    {
        Require(!manifest.geometryReduced && !manifest.texturesResized, "Export zmenil kvalitu zdroje.");
        Directory.CreateDirectory(WorkFolder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var source in manifest.textures)
            {
                string path = Folder + "/" + source.file;
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                int maxSize = Mathf.Max(32, Mathf.NextPowerOfTwo(Mathf.Max(source.width, source.height)));
                if (importer.npotScale == TextureImporterNPOTScale.None
                    && importer.textureCompression == TextureImporterCompression.Uncompressed
                    && importer.maxTextureSize == maxSize && importer.sRGBTexture == !source.linear
                    && importer.mipmapEnabled) continue;
                importer.textureType = TextureImporterType.Default;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.crunchedCompression = false;
                importer.maxTextureSize = maxSize;
                importer.sRGBTexture = !source.linear;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(Folder + "/Shaders/Sapa.shader", ImportAssetOptions.ForceSynchronousImport);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/Shaders/Sapa.shader");
        Require(shader != null, "Chybi shader Sapy.");
        var errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error").ToArray();
        Require(errors.Length == 0, "Shader Sapy: " + string.Join("; ", errors.Select(m => m.message)));
        foreach (var source in manifest.textures)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + source.file);
            Require(texture != null && texture.width == source.width && texture.height == source.height,
                "Zmenene rozliseni textury " + source.name);
        }
    }

    static Dictionary<string, Material> BuildMaterials(Manifest manifest)
    {
        string directory = Folder + "/Materials";
        Directory.CreateDirectory(directory);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/Shaders/Sapa.shader");
        var result = new Dictionary<string, Material>();
        foreach (var source in manifest.materials)
        {
            string path = directory + "/" + source.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = source.name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetFloat("_Style", source.style);
            material.SetFloat("_Cutoff", 0.01f);
            material.SetFloat("_DebugSurface", 0);
            foreach (var input in source.inputs)
            {
                string key = "_OW_" + input.name;
                material.SetVector(key + "Value", new Vector4(input.value[0], input.value[1], input.value[2], input.value[3]));
                material.SetFloat(key + "Mode", input.mode);
                material.SetFloat(key + "UV", input.uv);
                material.SetTexture(key, input.mode > 0 ? AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + input.texture) : null);
            }
            material.SetInt("_SrcBlend", (int)(source.transparent ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetInt("_DstBlend", (int)(source.transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetInt("_ZWrite", source.transparent ? 0 : 1);
            material.SetInt("_Cull", (int)CullMode.Off);
            var mainTexture = source.inputs.FirstOrDefault(i => i.name == "Color" && i.mode > 0);
            material.SetTexture("_BaseMap", mainTexture != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/"+mainTexture.texture) : null);
            material.SetOverrideTag("RenderType", source.transparent ? "Transparent" : "Opaque");
            material.renderQueue = source.transparent ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            material.enableInstancing = true;
            var serialized = new SerializedObject(material);
            foreach (string field in new[]{"m_TexEnvs","m_Floats","m_Colors"})
            {
                var values = serialized.FindProperty("m_SavedProperties."+field);
                for (int i = values.arraySize - 1; i >= 0; i--)
                    if (!material.HasProperty(values.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue))
                        values.DeleteArrayElementAtIndex(i);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(material);
            result.Add(source.name, material);
        }
        AssetDatabase.SaveAssets();
        return result;
    }

    static GameObject BuildVisuals(Manifest manifest)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Require(model != null, "Model se neimportoval.");
        var materials = BuildMaterials(manifest);
        var descriptors = manifest.objects.ToDictionary(o => o.name);
        var root = new GameObject(RootName);
        root.transform.position = MatchManager.SapaOrigin;
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        // Own the renderer overrides in our generated prefab. Otherwise a fresh
        // editor may reload glTF's gray transport materials from the nested prefab.
        // Mesh references still point to the original, full-precision GLB assets.
        PrefabUtility.UnpackPrefabInstance(visual,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        visual.name = "Visuals";
        visual.transform.SetParent(root.transform, false);
        var cooked = new Dictionary<Mesh, bool>();
        int colliders = 0, rendered = 0;
        foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
        {
            SourceObject descriptor = null;
            for (var t = renderer.transform; t != null && t != visual.transform; t = t.parent)
                if (descriptors.TryGetValue(t.name, out descriptor)) break;
            Require(descriptor != null, "Neznamy model " + renderer.name);
            var mapped = renderer.sharedMaterials;
            for (int i = 0; i < mapped.Length; i++)
            {
                string name = mapped[i] != null ? mapped[i].name : "";
                if (!materials.TryGetValue(name, out var material))
                {
                    Require(descriptor.materials.Length == mapped.Length, "Nesouhlasi materialove sloty " + descriptor.name);
                    Require(materials.TryGetValue(descriptor.materials[i], out material), "Chybi material " + name);
                }
                mapped[i] = material;
            }
            renderer.sharedMaterials = mapped;
            if (mapped.Any(m => m.renderQueue >= (int)RenderQueue.Transparent)) renderer.shadowCastingMode = ShadowCastingMode.Off;
            var filter = renderer.GetComponent<MeshFilter>();
            if (descriptor.collidable && filter != null && filter.sharedMesh != null && CanCook(filter.sharedMesh, cooked))
            {
                renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                colliders++;
            }
            rendered++;
        }
        Require(rendered == manifest.objects.Length, "Chybi casti geometrie mapy.");
        root.AddComponent<MapSpawnPoints>().localBounds = new Bounds(manifest.boundsCenter, manifest.boundsSize);
        AddEnvironment(root);
        AddLights(root, manifest);
        Physics.SyncTransforms();
        Debug.Log($"[Sapa] {rendered} modelu, {colliders} kolizi; originalni rozliseni vsech {manifest.textures.Length} textur.");
        return root;
    }

    static bool CanCook(Mesh mesh, Dictionary<Mesh, bool> cache)
    {
        if (cache.TryGetValue(mesh, out bool valid)) return valid;
        var vertices = mesh.vertices;
        var indices = mesh.triangles;
        bool surface = false;
        for (int i = 0; i + 2 < indices.Length; i += 3)
            if (Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]], vertices[indices[i + 2]] - vertices[indices[i]]).sqrMagnitude > 1e-12f)
            { surface = true; break; }
        if (!surface) { cache.Add(mesh, false); return false; }
        bool failed = false;
        void Record(string message, string trace, LogType type)
        {
            if (type == LogType.Error && message.Contains("[Physics.PhysX]")) failed = true;
        }
        Application.logMessageReceivedThreaded += Record;
        try
        {
            Physics.BakeMesh(mesh.GetEntityId(), false, MeshColliderCookingOptions.CookForFasterSimulation
                | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices
                | MeshColliderCookingOptions.UseFastMidphase);
        }
        finally { Application.logMessageReceivedThreaded -= Record; }
        cache.Add(mesh, !failed);
        return !failed;
    }

    static void AddEnvironment(GameObject root)
    {
        string path = Folder + "/SapaSky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (sky == null)
        {
            sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetColor("_SkyTint", new Color(.48f, .62f, .85f));
            sky.SetColor("_GroundColor", new Color(.38f, .36f, .33f));
            sky.SetFloat("_Exposure", 1f);
            AssetDatabase.CreateAsset(sky, path);
        }
        var environment = root.AddComponent<MapEnvironment>();
        environment.skybox = sky;
        environment.ambientSky = new Color(.65f, .7f, .8f);
        environment.ambientEquator = new Color(.55f, .57f, .6f);
        environment.ambientGround = new Color(.35f, .32f, .28f);
        var sun = new GameObject("SapaSun").AddComponent<Light>();
        sun.transform.SetParent(root.transform,false);
        sun.transform.localRotation = Quaternion.Euler(50,-30,0);
        sun.type = LightType.Directional;
        sun.color = new Color(1,.97f,.94f);
        sun.intensity = .65f;
        sun.shadows = LightShadows.Soft;
        sun.enabled = false;
        environment.sun = sun;
        string reflectionPath = Folder + "/SapaReflection.asset";
        var reflection = AssetDatabase.LoadAssetAtPath<Cubemap>(reflectionPath);
        if (reflection == null)
        {
            // Neutral daylight reflection, independent of the Domasov sky probe.
            reflection = new Cubemap(16, TextureFormat.RGBAHalf, false);
            for (int face = 0; face < 6; face++)
            {
                var color = face == (int)CubemapFace.PositiveY ? new Color(.16f,.19f,.24f)
                    : face == (int)CubemapFace.NegativeY ? new Color(.06f,.05f,.04f) : new Color(.1f,.11f,.14f);
                reflection.SetPixels(Enumerable.Repeat(color,256).ToArray(), (CubemapFace)face);
            }
            reflection.Apply(false,true);
            AssetDatabase.CreateAsset(reflection,reflectionPath);
        }
        environment.customReflection = reflection;
        environment.reflectionIntensity = .15f;
    }

    static void AddLights(GameObject root, Manifest manifest)
    {
        var group = new GameObject("ImportedLights").transform;
        group.SetParent(root.transform, false);
        foreach (var source in manifest.lights)
        {
            var light = new GameObject(source.name).AddComponent<Light>();
            light.transform.SetParent(group, false);
            light.transform.localPosition = source.position;
            light.transform.rotation = Quaternion.LookRotation(source.direction);
            light.type = source.type == "SPOT" ? LightType.Spot : LightType.Point;
            light.color = new Color(source.color[0], source.color[1], source.color[2]);
            // OWM stores Blender wattage; URP's normalized scene lighting needs
            // a lower lamp gain relative to its directional light.
            light.intensity = source.power / (200f * Mathf.PI);
            light.range = Mathf.Clamp(Mathf.Sqrt(source.power / (4f * Mathf.PI)) * 2f, 4f, 25f);
            light.spotAngle = source.spotAngle;
            light.shadows = LightShadows.None;
        }
    }

    static Vector3 FromBlender(float x, float y, float z) => MatchManager.SapaOrigin + new Vector3(-x, z, -y);

    static Vector3 Ground(Vector3 guess, float searchRadius = 0f)
    {
        var candidates = new List<Vector3> { guess };
        for (float radius = 1; radius <= searchRadius; radius++)
            for (int step = 0; step < 16; step++)
            {
                float angle = step * Mathf.PI / 8;
                candidates.Add(guess + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius);
            }
        foreach (var candidate in candidates)
        {
            var hits = Physics.RaycastAll(candidate + Vector3.up * 1.2f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => Mathf.Abs(a.point.y - guess.y).CompareTo(Mathf.Abs(b.point.y - guess.y)));
            foreach (var hit in hits)
            {
                if (hit.normal.y < .7f) continue;
                var point = hit.point + Vector3.up * .06f;
                if (SpawnClear(point)) return point;
            }
        }
        throw new InvalidOperationException("[Sapa] Chybi bezpecna podlaha u " + guess);
    }

    public static bool SpawnClear(Vector3 point) => !Physics.CheckCapsule(point + Vector3.up * .45f,
        point + Vector3.up * 1.65f, .38f, ~0, QueryTriggerInteraction.Ignore);

    static Transform Marker(Transform parent, string name, Vector3 world)
    {
        var marker = new GameObject(name).transform;
        marker.SetParent(parent, false);
        marker.position = world;
        return marker;
    }

    static Transform[] TeamSpawns(Transform root, int team, Vector3 guess)
    {
        var center = Ground(guess, 5);
        var group = Marker(root, $"SpawnPoint_Team{team}", center);
        var positions = new List<Vector3> { center };
        for (int radius = 1; radius <= 4 && positions.Count < 12; radius++)
            for (int step = 0; step < 12 && positions.Count < 12; step++)
            {
                float angle = step * Mathf.PI / 6;
                try
                {
                    var p = Ground(center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius);
                    if (positions.All(other => Vector3.Distance(p, other) > .8f)) positions.Add(p);
                }
                catch (InvalidOperationException) { }
            }
        Require(positions.Count >= 6, "Spawn nema dost volnych mist.");
        return positions.Select((p, i) => Marker(group, $"Sapa_Team{team}_{i:00}", p)).ToArray();
    }

    static void Gameplay(GameObject root)
    {
        var data = root.GetComponent<MapSpawnPoints>();
        // Entry courtyard and temple garden: both are on the connected fighting area.
        data.team0 = TeamSpawns(root.transform, 0, FromBlender(24, 11, 0));
        data.team1 = TeamSpawns(root.transform, 1, FromBlender(-5, 14.5f, 1));
        var points = new[] { Ground(FromBlender(16, 27, 0), 5), Ground(FromBlender(0, 2, 1), 5) };
        var markers = new GameObject("CaptureMarkers").transform;
        markers.SetParent(root.transform, false);
        for (int i = 0; i < points.Length; i++)
        {
            var point = Marker(markers, $"CapturePoint_{i + 1}", points[i] + Vector3.up * .2f);
            point.localScale = new Vector3(i == 0 ? 10 : 12, 1, 10);
        }
        var attackA = data.team0[0].position;
        var attackB = Ground(FromBlender(20, 28, 0), 6);
        var defendA = Ground(FromBlender(-4, 22, 1), 6);
        var defendB = data.team1[0].position;
        Marker(markers, "SpawnPoint_Utok_A", attackA);
        Marker(markers, "SpawnPoint_Obrana_A", defendA);
        Marker(markers, "SpawnPoint_Utok_B", attackB);
        Marker(markers, "SpawnPoint_Obrana_B", defendB);
        Marker(root.transform, "KillPlane", MatchManager.SapaOrigin + new Vector3(0, -25, 0));
    }

    public static void ImportForLayout()
    {
        var manifest = ReadManifest();
        ImportAssets(manifest);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var old = FindRoot();
        if (old != null) Object.DestroyImmediate(old);
        var root = BuildVisuals(manifest);
        MatchManager.ApplyMap(MatchManager.MapSapa);
        var probes = new[] { (72f, -20f), (-4f, -16f), (16f, 27f), (-4f, -1f), (20f, 28f), (-4f, 22f) };
        foreach (var (x, y) in probes)
        {
            var hits = Physics.RaycastAll(FromBlender(x, y, 30), Vector3.down, 55, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a,b) => a.distance.CompareTo(b.distance));
            Debug.Log($"[Sapa layout] Blender ({x},{y}): " + string.Join("; ", hits.Where(h => h.normal.y > .6f)
                .Select(h => $"floor {h.point.y - MatchManager.SapaOrigin.y:0.00} {h.collider.name}")));
        }
        Capture(root, "layout.png", true);
        var allMaterials = root.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials).Distinct().ToArray();
        foreach (var m in allMaterials) m.SetFloat("_DebugSurface", 1);
        Capture(root, "albedo.png", true);
        foreach (var m in allMaterials) m.SetFloat("_DebugSurface", 0);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("BrosOverwatch/Mapa/Sapa - importovat a overit")]
    public static void ImportAndValidate()
    {
        var manifest = ReadManifest();
        ImportAssets(manifest);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var old = FindRoot();
        if (old != null) Object.DestroyImmediate(old);
        var root = BuildVisuals(manifest);
        Gameplay(root);
        Boundaries(root);
        MatchManager.ApplyMap(MatchManager.MapSapa);
        Capture(root, "unity-overview.png", false);
        Validate(root, manifest);
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        var dependencies=AssetDatabase.GetDependencies(PrefabPath,true);
        Require(manifest.materials.All(m => dependencies.Contains(Folder+"/Materials/"+m.name+".mat")),
            "Prefab neuchoval vsechny OWM materialy.");
        Object.DestroyImmediate(root);
        root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        root.name = RootName;
        root.SetActive(false);
        MatchManager.ApplyMap(MatchManager.MapDomasov);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[Sapa] PASS: plny model, textury, kolize, spawny a 2 body dobyvani.");
    }

    public static void ImportAndSmokeTest()
    {
        ImportAndValidate();
        SapaPlaytest.Deathmatch();
    }

    public static void ImportAndCaptureTest()
    {
        ImportAndValidate();
        SapaPlaytest.Capture();
    }

    static void Boundaries(GameObject root)
    {
        var bounds = root.GetComponent<MapSpawnPoints>().localBounds;
        var parent = new GameObject("MapBoundary").transform;
        parent.SetParent(root.transform, false);
        float bottom = -25, top = bounds.max.y + 10;
        void Wall(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var box = go.AddComponent<BoxCollider>(); box.center = center; box.size = size;
        }
        float mid = (bottom + top) / 2, height = top - bottom;
        Wall("West", new Vector3(bounds.min.x - 1, mid, bounds.center.z), new Vector3(2, height, bounds.size.z + 4));
        Wall("East", new Vector3(bounds.max.x + 1, mid, bounds.center.z), new Vector3(2, height, bounds.size.z + 4));
        Wall("South", new Vector3(bounds.center.x, mid, bounds.min.z - 1), new Vector3(bounds.size.x + 4, height, 2));
        Wall("North", new Vector3(bounds.center.x, mid, bounds.max.z + 1), new Vector3(bounds.size.x + 4, height, 2));
        Wall("Ceiling", new Vector3(bounds.center.x, top + 1, bounds.center.z), new Vector3(bounds.size.x + 4, 2, bounds.size.z + 4));
    }

    static void Validate(GameObject root, Manifest manifest)
    {
        Require(MatchManager.MapReady(MatchManager.MapSapa) && MatchManager.MapReady(MatchManager.MapHanamura), "Chybi Sapa nebo puvodni Hanamura.");
        Require(MatchManager.MapSupportsMode(MatchManager.MapSapa, MatchManager.ModeDeathmatch)
            && MatchManager.MapSupportsMode(MatchManager.MapSapa, MatchManager.ModeAttack)
            && !MatchManager.MapSupportsMode(MatchManager.MapSapa, MatchManager.ModePush), "Chybna dostupnost rezimu.");
        var data = root.GetComponent<MapSpawnPoints>();
        foreach (var marker in data.team0.Concat(data.team1))
            Require(SpawnClear(marker.position), "Spawn ve zdi: " + marker.name);
        BotNavigation.EnsureBuilt();
        Require(BotNavigation.Ready, "Navigace se nepostavila.");
        var checks = new List<(Vector3 from, Vector3 to)> { (data.team0[0].position, data.team1[0].position) };
        for (int i = 0; i < 2; i++)
        {
            string letter = MatchManager.PointNames[i];
            var point = GameObject.Find($"CapturePoint_{i+1}").transform.position;
            checks.Add((GameObject.Find("SpawnPoint_Utok_"+letter).transform.position, point));
            checks.Add((GameObject.Find("SpawnPoint_Obrana_"+letter).transform.position, point));
        }
        var tri = NavMesh.CalculateTriangulation();
        File.WriteAllText(Path.Combine(WorkFolder,"navigation.json"),JsonUtility.ToJson(new NavigationDiagnostic
        { vertices=tri.vertices, indices=tri.indices, markers=checks.SelectMany(p => new[]{p.from,p.to}).ToArray() }));
        bool connected = true;
        foreach (var (from, to) in checks)
        {
            bool start=NavMesh.SamplePosition(from, out var a, 3, BotNavigation.Filter);
            bool end=NavMesh.SamplePosition(to, out var b, 3, BotNavigation.Filter);
            var path = new NavMeshPath();
            bool complete=start && end && NavMesh.CalculatePath(a.position, b.position, BotNavigation.Filter, path)
                && path.status == NavMeshPathStatus.PathComplete;
            Debug.Log($"[Sapa route] {from} -> {to}: start={start}, cil={end}, cesta={path.status}, rohu={path.corners.Length}.");
            connected &= complete;
        }
        Require(connected,"Nektera cesta mezi spawny a body neni pruchozi.");
        File.WriteAllText(Path.Combine(WorkFolder,"unity-validation.json"),
            "{\"map\":\"Sapa\",\"originalHanamuraPreserved\":true,\"models\":"+manifest.objects.Length
            +",\"textures\":"+manifest.textures.Length+",\"sourceResolutionPreserved\":true,\"capturePoints\":2,\"navPaths\":"+checks.Count+"}");
    }

    static GameObject FindRoot() => SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(go => go.name == RootName);

    public static void Capture(GameObject root, string filename, bool top)
    {
        var environment = root.GetComponent<MapEnvironment>();
        environment.Apply();
        var bounds = root.GetComponent<MapSpawnPoints>().localBounds;
        var center = root.transform.TransformPoint(bounds.center) - Vector3.up * 15;
        var camera = new GameObject("SapaValidationCamera").AddComponent<Camera>();
        camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        camera.transform.position = center + (top ? new Vector3(0, 150, 0) : new Vector3(-105, 90, -100));
        camera.transform.LookAt(center);
        camera.fieldOfView = 55;
        camera.nearClipPlane = .3f;
        camera.farClipPlane = 2500;
        camera.clearFlags = CameraClearFlags.Skybox;
        if (top) { camera.orthographic = true; camera.orthographicSize = 95; }
        try { CaptureCamera(camera,Path.Combine(WorkFolder,filename)); }
        finally { Object.DestroyImmediate(camera.gameObject); environment.Restore(); }
    }

    public static void CaptureCamera(Camera camera, string filename, int width=1280, int height=900)
    {
        var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        texture.Create();
        var previous = RenderTexture.active;
        try
        {
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
            File.WriteAllBytes(filename, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
        finally
        {
            RenderTexture.active = previous; texture.Release(); Object.DestroyImmediate(texture);
        }
    }

    public static void DistributionValidation()
    {
        EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        MatchManager.ApplyMap(MatchManager.MapSapa);
        var root=FindRoot();
        ValidateMaterials(root);
        const string preview="Assets/Resources/UI/sapa.png";
        string renderedPreview=Path.Combine(WorkFolder,"menu-preview.png");
        Require(File.Exists(renderedPreview),"Nejprve spustte SapaPlaytest.Preview pro nahled z inicializovaneho herniho rendereru.");
        File.Copy(renderedPreview,preview,true);
        MatchManager.ApplyMap(MatchManager.MapDomasov);
        AssetDatabase.ImportAsset(preview,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(preview);
        importer.npotScale=TextureImporterNPOTScale.None;
        importer.maxTextureSize=2048;
        importer.mipmapEnabled=false;
        importer.SaveAndReimport();
        Require(MenuStyle.MapPreview(MatchManager.MapSapa)!=null,"Chybi nahled Sapy v menu.");
        string bundles=Path.Combine(WorkFolder,"bundle-check");
        Directory.CreateDirectory(bundles);
        var result=BuildPipeline.BuildAssetBundles(bundles,new[]{new AssetBundleBuild
        {assetBundleName="sapa",assetNames=new[]{PrefabPath}}},
            BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.StandaloneWindows64);
        Require(result!=null,"Balicek Sapy se nesestavil.");
        long bytes=new FileInfo(Path.Combine(bundles,"sapa")).Length;
        File.WriteAllText(Path.Combine(WorkFolder,"distribution.json"),"{\"lz4MapBundleBytes\":"+bytes
            +",\"geometryReduced\":false,\"texturesResized\":false}");
        Debug.Log($"[Sapa] Samostatny LZ4 balicek: {bytes} bajtu. Bez zmensovani zdroju.");
    }

    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[Sapa] " + message);
    }

    public static void ValidateMaterials(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            foreach (var material in renderer.sharedMaterials)
                Require(material!=null && material.shader!=null && material.shader.name=="BrosOverwatch/Sapa",
                    "Ztraceny material po nacteni prefabu: "+renderer.name);
    }
}
