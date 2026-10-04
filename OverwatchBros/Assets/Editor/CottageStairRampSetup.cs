using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class CottageStairRampSetup
{
    const string RampName = "HladkaKolizeSchodu";
    const string MeshPath = "Assets/Models/Garden/Meshes/CottageStairRamp.asset";

    static CottageStairRampSetup()
    {
        EditorApplication.delayCall += Run;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += Run;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Run;
        };
    }

    [MenuItem("BrosOverwatch/Mapa/Vyhladit kolizi schodu male chaty")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var map = GameObject.Find("Map-Domasov");
        var stairs = map != null ? map.transform.Find("MensiChata/Stairs") : null;
        var filter = stairs != null ? stairs.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null) return;

        var root = stairs.Find(RampName);
        if (root != null && root.GetComponent<MeshCollider>()?.sharedMesh != null) return;
        var source = filter.sharedMesh;
        Bounds b = source.bounds;
        // Measure the real tread spacing; +Z is the ascent direction of this stair mesh.
        float tread = b.size.z;
        foreach (var vertex in source.vertices)
        {
            float distance = vertex.z - b.min.z;
            if (distance > 0.001f) tread = Mathf.Min(tread, distance);
        }
        if (tread <= 0f || tread >= b.size.z) return;
        float start = b.min.z - tread;
        float shoulder = b.max.z - tread;
        // Start one tread before the first riser; retain the flat upper landing.
        var vertices = new[]
        {
            new Vector3(b.min.x, b.min.y, start), new Vector3(b.max.x, b.min.y, start),
            new Vector3(b.min.x, b.min.y, b.max.z), new Vector3(b.max.x, b.min.y, b.max.z),
            new Vector3(b.min.x, b.max.y, b.max.z), new Vector3(b.max.x, b.max.y, b.max.z),
            new Vector3(b.min.x, b.max.y, shoulder), new Vector3(b.max.x, b.max.y, shoulder),
        };
        var triangles = new[]
        {
            0, 1, 3, 0, 3, 2, // bottom
            2, 3, 5, 2, 5, 4, // back
            6, 4, 5, 6, 5, 7, // flat landing
            0, 7, 1, 0, 6, 7, // walking slope
            0, 2, 4, 0, 4, 6, // left
            1, 7, 5, 1, 5, 3, // right
        };
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        bool create = mesh == null;
        if (create) mesh = new Mesh { name = "CottageStairRamp" };
        else mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (create)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(MeshPath));
            AssetDatabase.CreateAsset(mesh, MeshPath);
        }
        else { EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); }

        if (root == null)
        {
            root = new GameObject(RampName).transform;
            root.SetParent(stairs, false);
        }
        root.gameObject.layer = stairs.gameObject.layer;
        var ramp = root.GetComponent<MeshCollider>();
        if (ramp == null) ramp = root.gameObject.AddComponent<MeshCollider>();
        ramp.sharedMesh = mesh;
        ramp.isTrigger = false;
        foreach (var collider in stairs.GetComponents<Collider>())
        {
            ramp.sharedMaterial = collider.sharedMaterial;
            collider.enabled = false;
        }
        EditorSceneManager.MarkSceneDirty(stairs.gameObject.scene);
        Debug.Log("[Schody] Mala chata ma hladkou kolizni rampu a rovnou horni podestu. Uloz scenu (Ctrl+S).");
    }
}
