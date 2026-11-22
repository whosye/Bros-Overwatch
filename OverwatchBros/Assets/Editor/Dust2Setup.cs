using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Prenos mapy: glTF obsahuje jen geometrii a standardni PBR materialy, nikoli SourceIO shadery.
public static class Dust2Setup
{
    public const string ModelPath = "Assets/Models/Dust2/Dust2.glb";
    public const string PrefabPath = "Assets/Models/Dust2/Map_Dust2.prefab";
    const string ManifestPath = "Assets/Models/Dust2/Dust2.json";
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string RootName = "Map_Dust2";

    [Serializable] public sealed class RendererInfo { public string name; public bool collidable; }
    [Serializable] public sealed class Manifest
    {
        public Vector3[] team0, team1;
        public RendererInfo[] renderers;
        public Vector3 boundsCenter, boundsSize;
        public long exportBytes;
    }

    [MenuItem("BrosOverwatch/Mapa/Dust II - importovat a overit")]
    public static void ImportAndValidate()
    {
        if (!(AssetImporter.GetAtPath(ModelPath) is Dust2ModelImporter))
            AssetDatabase.SetImporterOverride<Dust2ModelImporter>(ModelPath);
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        Require(new FileInfo(ModelPath).Length < 100_000_000, "Model mapy presahuje 100 MB.");
        Require(manifest.team0.Length > 0 && manifest.team1.Length > 0, "Chybi tymove spawny.");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Require(model != null, "glTF model se neimportoval.");
        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var previous = FindRoot();
        if (previous != null) Object.DestroyImmediate(previous);
        var root = BuildRoot(model, manifest);
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        root.name = RootName;
        root.SetActive(false);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        ValidateMap(root, manifest);
        MatchManager.ApplyMap(MatchManager.MapDomasov);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[Dust II] Import, kolize, spawny a dostupnost rezimu overeny.");
    }

    // Dalsi kontrola po zmene exportu: skutecny URP render a samostatny LZ4 balicek mapy.
    public static void FinalValidation()
    {
        ImportAndValidate();
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/Dust2"));
        MatchManager.ApplyMap(MatchManager.MapDust2);
        FindRoot().GetComponent<MapEnvironment>().Apply();
        var bounds = MatchManager.ActiveMapBounds;
        var cameraObject = new GameObject("Dust2ValidationCamera");
        var camera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<UniversalAdditionalCameraData>();
        camera.transform.position = bounds.center + new Vector3(bounds.size.x * 0.42f, 105f, -bounds.size.z * 0.46f);
        camera.transform.LookAt(bounds.center);
        camera.fieldOfView = 55;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 650;
        camera.clearFlags = CameraClearFlags.Skybox;
        var texture = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32);
        texture.Create();
        var previous = RenderTexture.active;
        try
        {
            RenderPipeline.SubmitRenderRequest(camera,
                new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(folder, "unity-overview.png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
        finally
        {
            RenderTexture.active = previous;
            texture.Release();
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(cameraObject);
            FindRoot().GetComponent<MapEnvironment>().Restore();
            MatchManager.ApplyMap(MatchManager.MapDomasov);
        }
        string bundles = Path.Combine(folder, "bundle-size-check");
        Directory.CreateDirectory(bundles);
        var result = BuildPipeline.BuildAssetBundles(bundles, new[]
        {
            new AssetBundleBuild { assetBundleName = "dust2", assetNames = new[] { PrefabPath } }
        }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
            BuildTarget.StandaloneWindows64);
        Require(result != null, "Kontrolni balicek mapy se nesestavil.");
        long bytes = new FileInfo(Path.Combine(bundles, "dust2")).Length;
        File.WriteAllText(Path.Combine(folder, "unity-distribution.json"),
            "{\"sourceModelBytes\":" + new FileInfo(ModelPath).Length + ",\"lz4BundleBytes\":" + bytes + "}");
        Require(bytes < 100_000_000, "Balicek mapy presahuje 100 MB.");
        Debug.Log($"[Dust II] LZ4 balicek mapy: {bytes} bajtu.");
    }

    static GameObject FindRoot() => SceneManager.GetActiveScene().GetRootGameObjects()
        .FirstOrDefault(go => go.name == RootName);

    static GameObject BuildRoot(GameObject model, Manifest manifest)
    {
        var root = new GameObject(RootName);
        root.transform.position = MatchManager.Dust2Origin;
        try
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = "Visuals";
            visual.transform.SetParent(root.transform, false);
            var descriptors = manifest.renderers.ToDictionary(r => r.name, r => r.collidable);
            int colliders = 0, missing = 0;
            var cooking = new Dictionary<Mesh, bool>();
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                bool solid = true, found = false;
                for (var node = filter.transform; node != null && node != visual.transform; node = node.parent)
                {
                    if (!descriptors.TryGetValue(node.name, out solid)) continue;
                    found = true;
                    break;
                }
                if (!found) missing++;
                if (solid && CanCook(filter, cooking))
                {
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    colliders++;
                }
                GameObjectUtility.SetStaticEditorFlags(filter.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }
            Require(missing == 0, $"Chybi klasifikace {missing} koliznich modelu.");
            Require(colliders > 100, "Mapa nema dostatecnou kolizni geometrii.");
            Physics.SyncTransforms();

            var data = root.AddComponent<MapSpawnPoints>();
            data.localBounds = new Bounds(manifest.boundsCenter, manifest.boundsSize + Vector3.one * 4f);
            data.team0 = BuildSpawns(root.transform, 0, manifest.team0);
            data.team1 = BuildSpawns(root.transform, 1, manifest.team1);
            var environment = root.AddComponent<MapEnvironment>();
            const string skyPath = "Assets/Models/Dust2/Dust2Sky.mat";
            environment.skybox = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (environment.skybox == null)
            {
                environment.skybox = new Material(Shader.Find("Skybox/Procedural"));
                environment.skybox.SetColor("_SkyTint", new Color(0.48f, 0.57f, 0.7f));
                environment.skybox.SetColor("_GroundColor", new Color(0.55f, 0.48f, 0.35f));
                environment.skybox.SetFloat("_Exposure", 1.0f);
                AssetDatabase.CreateAsset(environment.skybox, skyPath);
            }
            var kill = new GameObject("KillPlane");
            kill.transform.SetParent(root.transform, false);
            kill.transform.localPosition = new Vector3(0, data.localBounds.min.y - 15f, 0);
            AddBoundaries(root.transform, data.localBounds);
            Debug.Log($"[Dust II] {colliders} kolizi, spawny {data.team0.Length}/{data.team1.Length}.");
            return root;
        }
        catch
        {
            Object.DestroyImmediate(root);
            throw;
        }
    }

    static bool CanCook(MeshFilter filter, Dictionary<Mesh, bool> cache)
    {
        var scale = filter.transform.lossyScale;
        if (Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) < 0.0001f)
            return false;
        var mesh = filter.sharedMesh;
        if (cache.TryGetValue(mesh, out bool result)) return result;
        var vertices = mesh.vertices;
        var indices = mesh.triangles;
        bool hasSurface = false;
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            var a = vertices[indices[i]];
            if (Vector3.Cross(vertices[indices[i + 1]] - a, vertices[indices[i + 2]] - a).sqrMagnitude > 1e-12f)
            {
                hasSurface = true;
                break;
            }
        }
        if (!hasSurface)
        {
            Debug.LogWarning($"[Dust II] Vynechana nulova kolizni plocha: {filter.name}.");
            cache.Add(mesh, false);
            return false;
        }
        int failed = 0;
        void CookingLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error && message.Contains("[Physics.PhysX]"))
                System.Threading.Interlocked.Exchange(ref failed, 1);
        }
        Application.logMessageReceivedThreaded += CookingLog;
        try
        {
            Physics.BakeMesh(mesh.GetEntityId(), false,
                MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning
                | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase);
        }
        finally { Application.logMessageReceivedThreaded -= CookingLog; }
        result = failed == 0;
        cache.Add(mesh, result);
        if (!result) Debug.LogWarning($"[Dust II] Vynechana vadna kolizni sit: {filter.name}.");
        return result;
    }

    static Transform[] BuildSpawns(Transform root, int team, Vector3[] positions)
    {
        var group = new GameObject($"SpawnPoint_Team{team}").transform;
        group.SetParent(root, false);
        var markers = new List<Transform>();
        foreach (var local in positions)
        {
            var expected = root.TransformPoint(local);
            if (!Physics.Raycast(expected + Vector3.up * 0.25f, Vector3.down, out var ground, 5f, ~0,
                    QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException($"Dust II: spawn tymu {team} nema podlahu: {local}.");
            var point = ground.point + Vector3.up * 0.06f;
            Require(ground.normal.y > 0.65f, $"Spawn tymu {team} nelezi na schudne podlaze: {local}.");
            Require(SpawnClear(point), $"Spawn tymu {team} je ve zdi nebo pod nizkym stropem: {local}.");
            var marker = new GameObject($"Dust2_Team{team}_{markers.Count:00}").transform;
            marker.SetParent(root, false);
            marker.position = point;
            markers.Add(marker);
        }
        group.position = markers[0].position;
        foreach (var marker in markers) marker.SetParent(group, true);
        return markers.ToArray();
    }

    static bool SpawnClear(Vector3 point) => !Physics.CheckCapsule(point + Vector3.up * 0.45f,
        point + Vector3.up * 1.65f, 0.38f, ~0, QueryTriggerInteraction.Ignore);

    static void AddBoundaries(Transform root, Bounds bounds)
    {
        var parent = new GameObject("MapBoundary").transform;
        parent.SetParent(root, false);
        float bottom = bounds.min.y - 5, top = bounds.max.y + 25;
        float y = (bottom + top) * 0.5f, height = top - bottom;
        Wall("West", new Vector3(bounds.min.x - 1, y, bounds.center.z), new Vector3(2, height, bounds.size.z + 4));
        Wall("East", new Vector3(bounds.max.x + 1, y, bounds.center.z), new Vector3(2, height, bounds.size.z + 4));
        Wall("South", new Vector3(bounds.center.x, y, bounds.min.z - 1), new Vector3(bounds.size.x + 4, height, 2));
        Wall("North", new Vector3(bounds.center.x, y, bounds.max.z + 1), new Vector3(bounds.size.x + 4, height, 2));
        Wall("Ceiling", new Vector3(bounds.center.x, top + 1, bounds.center.z), new Vector3(bounds.size.x + 4, 2, bounds.size.z + 4));
        void Wall(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var collider = go.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
        }
    }

    static void ValidateMap(GameObject root, Manifest manifest)
    {
        Require(MatchManager.MapReady(MatchManager.MapDust2), "Mapa se neobjevila v registru.");
        Require(MatchManager.MapSupportsMode(MatchManager.MapDust2, MatchManager.ModeDeathmatch), "Deathmatch neni povoleny.");
        Require(!MatchManager.MapSupportsMode(MatchManager.MapDust2, MatchManager.ModeAttack), "Dust II nesmi podporovat dobyvani.");
        Require(MatchManager.NextMapForMode(MatchManager.MapHanamura, MatchManager.ModeAttack) == MatchManager.MapSapa,
            "Dobyvani nepreskocilo Dust II.");
        Require(MatchManager.NextMapForMode(MatchManager.MapHanamura, MatchManager.ModeDeathmatch) == MatchManager.MapDust2,
            "Deathmatch nenabizi Dust II.");
        foreach (int map in new[] { MatchManager.MapDomasov, MatchManager.MapHanamura })
            foreach (int mode in new[] { MatchManager.ModeDeathmatch, MatchManager.ModeAttack })
                Require(MatchManager.MapSupportsMode(map, mode), "Puvodni mapy ztratily podporu rezimu.");

        MatchManager.ApplyMap(MatchManager.MapDust2);
        Require(root.activeInHierarchy, "Mapa se nezapnula.");
        var data = root.GetComponent<MapSpawnPoints>();
        Require(data.team0.Length == manifest.team0.Length && data.team1.Length == manifest.team1.Length, "Chybi cast spawnu.");
        Require(MatchManager.ActiveMapBounds.Contains(data.team0[0].position), "Spawn neni v navigacnim rozsahu.");
        foreach (var marker in data.team0.Concat(data.team1))
        {
            Require(SpawnClear(marker.position), "Spawn je po importu prefabu zablokovany.");
            Require(Physics.Raycast(marker.position + Vector3.up * 0.1f, Vector3.down, 0.3f, ~0,
                QueryTriggerInteraction.Ignore), "Hrac by na spawnu propadl podlahou.");
        }
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            foreach (var material in renderer.sharedMaterials)
                Require(material != null && material.shader != null && material.shader.name != "Hidden/InternalErrorShader",
                    "Model ma chybejici material nebo shader.");

        BotNavigation.Invalidate();
        BotNavigation.EnsureBuilt();
        Require(BotNavigation.Ready, "Nepodarilo se sestavit navigaci botu.");
        Require(NavMesh.SamplePosition(data.team0[0].position, out var start, 3f, BotNavigation.Filter), "Spawn T neni na NavMesh.");
        Require(NavMesh.SamplePosition(data.team1[0].position, out var end, 3f, BotNavigation.Filter), "Spawn CT neni na NavMesh.");
        var path = new NavMeshPath();
        Require(NavMesh.CalculatePath(start.position, end.position, BotNavigation.Filter, path)
                && path.status == NavMeshPathStatus.PathComplete, "Boti nemaji pruchozi cestu mezi zakladnami.");
        string reportFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/Dust2"));
        Directory.CreateDirectory(reportFolder);
        File.WriteAllText(Path.Combine(reportFolder, "unity-validation.json"), JsonUtility.ToJson(new Validation
        {
            sourceBytes = new FileInfo(ModelPath).Length,
            renderers = root.GetComponentsInChildren<MeshRenderer>().Length,
            colliders = root.GetComponentsInChildren<MeshCollider>().Length,
            team0Spawns = data.team0.Length, team1Spawns = data.team1.Length,
            pathCorners = path.corners.Length, navMeshConnected = true, captureDisabled = true
        }, true));
    }

    [Serializable] sealed class Validation
    {
        public long sourceBytes;
        public int renderers, colliders, team0Spawns, team1Spawns, pathCorners;
        public bool navMeshConnected, captureDisabled;
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[Dust II] " + message);
    }

    [MenuItem("BrosOverwatch/Mapa/Dust II - ukazat")]
    static void Show()
    {
        MatchManager.ApplyMap(MatchManager.MapDust2);
        var root = FindRoot();
        if (root != null) Selection.activeGameObject = root;
    }
}
