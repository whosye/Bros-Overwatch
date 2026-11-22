using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Jednorazova oprava gauce s Maxem (MaxNaGauci na Domasove i v kopii pro Tlacenici): spusti se sama po prekladu
// (jen jednou na tomhle pocitaci), nebo rucne: BrosOverwatch > Mapa > Gauc s Maxem - opravit kolize.
//  1) smaze Mesh Collidery z drobnosti (oci, zuby, rasy... - AddCollisionTool.IsDetail),
//  2) modelum, ktere pouzivaji zbyle Mesh Collidery, zapne Read/Write (kolize pak funguji i v sestavene hre
//     a navigacni sit botu je precte presne).
// Scenu neuklada - jen ji oznaci jako zmenenou (ulozit Ctrl+S). Ctrl+Z vrati smazane kolize.
[InitializeOnLoad]
static class MaxGaucFix
{
    const string DoneKey = "BrosOverwatch.MaxGaucFix.v1";
    const string ObjectName = "MaxNaGauci";

    static MaxGaucFix()
    {
        if (EditorPrefs.GetBool(DoneKey, false)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorPrefs.GetBool(DoneKey, false)) return;
            if (Run()) EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("BrosOverwatch/Mapa/Gauc s Maxem - opravit kolize")]
    static void Menu() => Run();

    static bool Run()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return false;

        var roots = new List<GameObject>();
        foreach (var go in scene.GetRootGameObjects())
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(ObjectName)) roots.Add(t.gameObject);
        if (roots.Count == 0)
        {
            Debug.Log("[Kolize] Gauc s Maxem (MaxNaGauci) ve scene neni - nic k oprave.");
            return true;
        }

        Undo.SetCurrentGroupName("Gauc s Maxem - opravit kolize");
        int group = Undo.GetCurrentGroup();
        var removed = new List<string>();
        var paths = new HashSet<string>();
        foreach (var root in roots)
            foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true))
            {
                var mesh = collider.sharedMesh;
                var filter = collider.GetComponent<MeshFilter>();
                if (mesh == null && filter != null) mesh = filter.sharedMesh;
                if (mesh == null) continue;
                if (AddCollisionTool.IsDetail(collider.transform, mesh))
                {
                    removed.Add($"{root.name}/{collider.name}");
                    Undo.DestroyObjectImmediate(collider);
                    continue;
                }
                if (!mesh.isReadable)
                {
                    string path = AssetDatabase.GetAssetPath(mesh);
                    if (!string.IsNullOrEmpty(path)) paths.Add(path);
                }
            }
        Undo.CollapseUndoOperations(group);

        var readable = new List<string>();
        foreach (var path in paths)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer) || importer.isReadable) continue;
            importer.isReadable = true;
            importer.SaveAndReimport();
            readable.Add(path);
        }

        if (removed.Count > 0) EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Kolize] Gauc s Maxem ({roots.Count}x): odebrano kolizi z drobnosti {removed.Count}"
            + (removed.Count > 0 ? "\n  " + string.Join("\n  ", removed) : "")
            + $"\nRead/Write zapnuto u modelu: {readable.Count}" + (readable.Count > 0 ? "\n  " + string.Join("\n  ", readable) : "")
            + (removed.Count > 0 ? "\nUloz scenu (Ctrl+S)." : ""));
        return true;
    }
}
