using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Recover individual decorations from the old material/cell batches without regenerating the map.
internal static class SeparateMapPlanters
{
    const string Menu = "BrosOverwatch/Mapa/Rozdelit kvetinace na jednotlive objekty";
    sealed class Source
    {
        public MeshFilter filter;
        public MeshRenderer renderer;
        public Mesh mesh;
        public Vector3[] world;
        public int[] triangles;
        public List<List<int>> islands;
    }
    sealed class Planter
    {
        public Bounds soil;
        public Source source;
        public List<int> island;
        public Transform root;
    }

    [MenuItem("BrosOverwatch/Mapa/Opravit kvetinace 21 a 22")]
    static void RepairPair()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var roots = new[] { "Kvetinac_21", "Kvetinac_22" }.Select(name =>
            Object.FindObjectsByType<MapPlanter>(FindObjectsInactive.Exclude).Single(p => p.name == name).transform).ToArray();
        var remainderMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MapVisualPolishSetup.Folder + "/JednotliveKvetinace_20261125_144454_158/Mesh_52.asset");
        var remainder = Read(Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude)
            .Single(f => ReferenceEquals(f.sharedMesh, remainderMesh) && f.GetComponent<MeshRenderer>().enabled));
        var recovered = new HashSet<int>();
        var replacements = new List<(MeshFilter filter, Mesh mesh)>();
        try
        {
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                var wood = root.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "PlanterWood");
                // These two soils were generated here; the old wooden batch had subsequently been moved.
                // Use the original frame only to recover the wall, preserving any later edits to the planter root.
                var initialLocal = new Vector3(6.1860785f, .8100008f, i == 0 ? -11.625029f : -6.4250083f);
                var initialFrame = root.parent.localToWorldMatrix * Matrix4x4.TRS(initialLocal, Quaternion.identity, Vector3.one);
                var initialInverse = initialFrame.inverse;
                var walls = remainder.islands.Where(island =>
                {
                    var center = initialInverse.MultiplyPoint3x4(BoundsOf(remainder, island).center);
                    return Mathf.Abs(center.x) < .6f && Mathf.Abs(center.z) < .05f && Mathf.Abs(center.y + .0825f) < .03f;
                }).ToArray();
                if (walls.Length != 1 || wood.sharedMesh.triangles.Length != 144)
                    throw new InvalidOperationException("Unexpected geometry in " + root.name + "; nothing changed.");
                var indices = new List<int>();
                foreach (int triangle in walls[0])
                {
                    if (!recovered.Add(triangle)) throw new InvalidOperationException("Wall assigned twice.");
                    for (int k = 0; k < 3; k++) indices.Add(remainder.triangles[triangle * 3 + k]);
                }
                if (indices.Count != 36) throw new InvalidOperationException("Incomplete recovered planter wall.");
                var wall = Subset(remainder, indices, remainder.filter.transform);
                var mesh = new Mesh { name = root.name + "_drevo_opravene" };
                mesh.CombineMeshes(new[] {
                    new CombineInstance { mesh = wood.sharedMesh, transform = root.worldToLocalMatrix * wood.transform.localToWorldMatrix },
                    new CombineInstance { mesh = wall, transform = initialInverse * remainder.filter.transform.localToWorldMatrix }
                }, true, true);
                Object.DestroyImmediate(wall);
                mesh.RecalculateBounds();
                var bounds = mesh.bounds;
                if ((bounds.size - new Vector3(.33f, .235f, 1.4f)).sqrMagnitude > .00001f || mesh.triangles.Length != 180)
                { Object.DestroyImmediate(mesh); throw new InvalidOperationException("Recovered planter dimensions do not match."); }
                // Soil is centered at zero; wood's body sits 8.25 cm below it.
                Vector3 correction = new Vector3(-bounds.center.x, -.0825f - bounds.center.y, -bounds.center.z);
                mesh.vertices = mesh.vertices.Select(v => v + correction).ToArray();
                mesh.RecalculateBounds();
                replacements.Add((wood, mesh));
            }
            var remaining = new List<int>();
            for (int t = 0; t < remainder.triangles.Length / 3; t++)
                if (!recovered.Contains(t)) for (int k = 0; k < 3; k++) remaining.Add(remainder.triangles[t * 3 + k]);
            replacements.Add((remainder.filter, Subset(remainder, remaining, remainder.filter.transform)));
            int before = roots.Sum(r => r.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "PlanterWood").sharedMesh.triangles.Length) + remainder.triangles.Length;
            int after = replacements.Sum(r => r.mesh.triangles.Length);
            if (before != after) throw new InvalidOperationException("Planter repair changed triangle count.");
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Opravit kvetinace 21 a 22");
            string folder = MapVisualPolishSetup.Folder + "/OpravaKvetinacu_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            MapBuildKit.EnsureFolder(folder);
            for (int i = 0; i < replacements.Count; i++)
            {
                var replacement = replacements[i];
                AssetDatabase.CreateAsset(replacement.mesh, folder + "/Mesh_" + i + ".asset");
                Undo.RecordObject(replacement.filter, "Opravit drevene casti");
                replacement.filter.sharedMesh = replacement.mesh;
                EditorUtility.SetDirty(replacement.filter);
            }
            Undo.CollapseUndoOperations(undo);
            AssetDatabase.SaveAssets();
            foreach (var scene in roots.Select(r => r.gameObject.scene).Distinct()) EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = roots[0].gameObject;
            Debug.Log($"[Planters] Opraveny 21 a 22: kazdy ma vsech pet drevenych dilu, drevo zarovnane s hlinou; pozice parentu zachovane. Trojuhelniky {before / 3} = {after / 3}.");
        }
        catch
        {
            foreach (var replacement in replacements)
                if (!AssetDatabase.Contains(replacement.mesh)) Object.DestroyImmediate(replacement.mesh);
            throw;
        }
    }

    [MenuItem(Menu)]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var sources = Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude)
            .Where(f => f.sharedMesh != null && f.sharedMesh.isReadable && f.sharedMesh.subMeshCount == 1
                && AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith(MapVisualPolishSetup.Folder + "/Meshes/", StringComparison.Ordinal)
                && (f.name.StartsWith("Planter") || f.name.StartsWith("Terracotta") || f.name.StartsWith("FlowerFoliage_") || f.name.StartsWith("Petals"))
                && f.GetComponent<MeshRenderer>() != null && f.GetComponent<MeshRenderer>().enabled)
            .Select(Read).ToArray();
        var planters = new List<Planter>();
        foreach (var source in sources.Where(s => s.filter.name.StartsWith("PlanterSoil_")))
            foreach (var island in source.islands)
                planters.Add(new Planter { soil = BoundsOf(source, island), source = source, island = island });
        if (planters.Count == 0) { Debug.Log("[Planters] Zadna puvodni sloucena hlina k rozdeleni."); return; }

        // Prepare the complete partition first. Every original triangle goes to exactly one output.
        var partitions = new Dictionary<Source, Dictionary<int, List<int>>>();
        foreach (var source in sources)
        {
            var groups = new Dictionary<int, List<int>>();
            foreach (var island in source.islands)
            {
                int target = -1;
                if (source.filter.name.StartsWith("PlanterSoil_"))
                    target = planters.FindIndex(p => ReferenceEquals(p.source, source) && ReferenceEquals(p.island, island));
                else
                {
                    var center = BoundsOf(source, island).center;
                    float best = float.PositiveInfinity;
                    for (int i = 0; i < planters.Count; i++)
                    {
                        var b = planters[i].soil;
                        if (center.x < b.min.x - .11f || center.x > b.max.x + .11f
                            || center.z < b.min.z - .11f || center.z > b.max.z + .11f
                            || center.y < b.min.y - .65f || center.y > b.max.y + .65f) continue;
                        float distance = (center - b.center).sqrMagnitude;
                        if (distance < best) { best = distance; target = i; }
                    }
                }
                if (!groups.TryGetValue(target, out var indices)) groups[target] = indices = new List<int>();
                foreach (int triangle in island)
                    for (int k = 0; k < 3; k++) indices.Add(source.triangles[triangle * 3 + k]);
            }
            if (groups.Values.Sum(g => g.Count) != source.triangles.Length)
                throw new InvalidOperationException("Incomplete planter partition: " + source.filter.name);
            partitions.Add(source, groups);
        }

        Undo.IncrementCurrentGroup();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Rozdelit kvetinace");
        string folder = MapVisualPolishSetup.Folder + "/JednotliveKvetinace_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        MapBuildKit.EnsureFolder(folder);
        int meshCount = 0;
        long before = sources.Sum(s => (long)s.triangles.Length / 3), after = 0;
        try
        {
            for (int i = 0; i < planters.Count; i++)
            {
                var p = planters[i];
                var go = new GameObject("Kvetinac_" + (i + 1).ToString("00"));
                Undo.RegisterCreatedObjectUndo(go, "Kvetinac");
                go.AddComponent<MapPlanter>();
                // Keep the map's hierarchy and scene; pivot is now at this planter, not the 16 m batch origin.
                go.transform.SetParent(p.source.filter.transform.parent, false);
                go.transform.position = p.soil.center;
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                p.root = go.transform;
            }
            foreach (var source in sources)
            {
                var groups = partitions[source];
                if (groups.Count == 1 && groups.ContainsKey(-1)) { after += source.triangles.Length / 3; continue; }
                foreach (var pair in groups)
                {
                    if (pair.Key < 0)
                    {
                        var remainder = Subset(source, pair.Value, source.filter.transform);
                        AssetDatabase.CreateAsset(remainder, folder + "/Mesh_" + meshCount++ + ".asset");
                        Undo.RecordObject(source.filter, "Zbytek puvodniho meshe");
                        source.filter.sharedMesh = remainder;
                    }
                    else
                    {
                        var go = new GameObject(source.filter.name.Split('_')[0]);
                        Undo.RegisterCreatedObjectUndo(go, "Cast kvetinace");
                        go.layer = source.filter.gameObject.layer;
                        go.transform.SetParent(planters[pair.Key].root, false);
                        var filter = go.AddComponent<MeshFilter>();
                        var renderer = go.AddComponent<MeshRenderer>();
                        EditorUtility.CopySerialized(source.renderer, renderer);
                        var mesh = Subset(source, pair.Value, go.transform);
                        AssetDatabase.CreateAsset(mesh, folder + "/Mesh_" + meshCount++ + ".asset");
                        filter.sharedMesh = mesh;
                    }
                    after += pair.Value.Count / 3;
                }
                if (!groups.ContainsKey(-1))
                {
                    // Keep the old objects available for Undo, but never draw their batched geometry twice.
                    Undo.RecordObject(source.renderer, "Skryt puvodni davku");
                    source.renderer.enabled = false;
                }
            }
            if (before != after) throw new InvalidOperationException("Planter triangle count changed.");
            AssetDatabase.SaveAssets();
            foreach (var scene in planters.Select(p => p.root.gameObject.scene).Distinct()) EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = planters[0].root.gameObject;
            Debug.Log($"[Planters] Rozdeleno {planters.Count} samostatnych kvetinacu. Trojuhelniky zachovany: {before} = {after}. Scena zatim neni ulozena; Ctrl+Z vrati rozdeleni.");
            Undo.CollapseUndoOperations(undo);
        }
        catch { Undo.RevertAllDownToGroup(undo); throw; }
    }

    static Source Read(MeshFilter filter)
    {
        var mesh = filter.sharedMesh;
        var s = new Source { filter = filter, renderer = filter.GetComponent<MeshRenderer>(), mesh = mesh,
            world = mesh.vertices.Select(filter.transform.TransformPoint).ToArray(), triangles = mesh.triangles };
        int[] parent = Enumerable.Range(0, s.world.Length).ToArray();
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
        var positions = new Dictionary<Vector3Int, int>();
        var vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            var key = Vector3Int.RoundToInt(vertices[i] * 100000);
            if (positions.TryGetValue(key, out int previous)) Union(i, previous); else positions.Add(key, i);
        }
        for (int t = 0; t < s.triangles.Length; t += 3)
        { Union(s.triangles[t], s.triangles[t + 1]); Union(s.triangles[t], s.triangles[t + 2]); }
        var groups = new Dictionary<int, List<int>>();
        for (int t = 0; t < s.triangles.Length; t += 3)
        {
            int key = Find(s.triangles[t]);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>();
            list.Add(t / 3);
        }
        s.islands = groups.Values.ToList(); return s;
    }

    static Bounds BoundsOf(Source source, List<int> triangles)
    {
        var bounds = new Bounds(source.world[source.triangles[triangles[0] * 3]], Vector3.zero);
        foreach (int triangle in triangles)
            for (int k = 0; k < 3; k++) bounds.Encapsulate(source.world[source.triangles[triangle * 3 + k]]);
        return bounds;
    }

    static Mesh Subset(Source source, List<int> triangles, Transform output)
    {
        var oldIndices = triangles.Distinct().ToArray();
        var lookup = new Dictionary<int, int>();
        for (int i = 0; i < oldIndices.Length; i++) lookup.Add(oldIndices[i], i);
        var mesh = new Mesh { name = source.mesh.name + "_samostatne", indexFormat = oldIndices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.vertices = oldIndices.Select(i => output.InverseTransformPoint(source.world[i])).ToArray();
        var normalTransform = output.localToWorldMatrix.transpose * source.filter.transform.worldToLocalMatrix.transpose;
        var normals = source.mesh.normals;
        if (normals.Length > 0) mesh.normals = oldIndices.Select(i => normalTransform.MultiplyVector(normals[i]).normalized).ToArray();
        for (int channel = 0; channel < 8; channel++)
        {
            var uv = new List<Vector4>(); source.mesh.GetUVs(channel, uv);
            if (uv.Count > 0) mesh.SetUVs(channel, oldIndices.Select(i => uv[i]).ToList());
        }
        var colors = source.mesh.colors;
        if (colors.Length > 0) mesh.colors = oldIndices.Select(i => colors[i]).ToArray();
        mesh.triangles = triangles.Select(i => lookup[i]).ToArray();
        if (normals.Length == 0) mesh.RecalculateNormals();
        mesh.RecalculateTangents(); mesh.RecalculateBounds();
        return mesh;
    }
}
