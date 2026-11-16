using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Kolize stromum: vybranym objektum (i celym skupinam, napr. "Stromy" - projdou se vsechny deti) se ke kazde
// viditelne siti prida Mesh Collider presne podle tvaru (kmen i koruna) - strely a projektily uz stromem neproleti.
// Co uz kolizi ma, zustane jak je. Ctrl+Z vrati.
public static class TreeColliderTool
{
    [MenuItem("BrosOverwatch/Kolize/Pridat kolizi vybranym stromum")]
    static void AddToSelection()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Stromy] Nejdriv vypni hru (zmeny ve hre se neulozi)."); return; }
        var selected = Selection.gameObjects;
        if (selected.Length == 0) { Debug.LogWarning("[Stromy] Nejdriv v Hierarchy vyber stromy (nebo skupinu stromu)."); return; }

        int added = 0, skipped = 0;
        Undo.SetCurrentGroupName("Kolize stromum");
        foreach (var root in selected)
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled) continue;
                if (filter.GetComponent<Collider>() != null) { skipped++; continue; }
                var collider = Undo.AddComponent<MeshCollider>(filter.gameObject);
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;   // presny tvar (pod korunou jde projit, strela mezi vetvemi proleti jen kde je mezera)
                added++;
            }

        if (added > 0)
            foreach (var root in selected) EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"[Stromy] Pridana kolize: {added} siti" + (skipped > 0 ? $", {skipped} uz kolizi melo (beze zmeny)." : "."));
    }

    [MenuItem("BrosOverwatch/Kolize/Pridat kolizi vybranym stromum", true)]
    static bool CanAdd() => Selection.gameObjects.Length > 0;
}
