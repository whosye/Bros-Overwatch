using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// BrosOverwatch > Mapa > Kolize na vybrany objekt: vsem modelum ve vybranych objektech (i v podobjektech) prida
// kolizi podle tvaru - Mesh Collider (presny tvar, ne konvexni), postave se skinned meshem Box Collider podle jejiho
// obrysu. Kde uz kolize je, nic nepridava. Prazdne Mesh Collidery (bez meshe) na vybranych objektech smaze.
// Jde vratit pres Ctrl+Z. Po pridani: zkontrolovat zeleny obrys v okne Scene (kolize = vizual).
public static class AddCollisionTool
{
    [MenuItem("BrosOverwatch/Mapa/Kolize na vybrany objekt")]
    static void Run()
    {
        var added = new List<string>();
        var skipped = new List<string>();
        int removed = 0;
        Undo.SetCurrentGroupName("Kolize na vybrany objekt");
        int group = Undo.GetCurrentGroup();

        foreach (var root in Selection.gameObjects)
        {
            // prazdne Mesh Collidery (pridane rucne na objekt bez modelu)
            foreach (var empty in root.GetComponentsInChildren<MeshCollider>(true))
                if (empty.sharedMesh == null && empty.GetComponent<MeshFilter>() == null)
                {
                    Undo.DestroyObjectImmediate(empty);
                    removed++;
                }

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                if (Detail(filter.transform, filter.sharedMesh))
                {
                    skipped.Add(filter.name);
                    continue;
                }
                var collider = Undo.AddComponent<MeshCollider>(filter.gameObject);
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
                added.Add($"{Path(filter.transform, root.transform)} (Mesh Collider)");
            }

            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh == null || skinned.GetComponent<Collider>() != null) continue;
                if (Detail(skinned.transform, skinned.sharedMesh))
                {
                    skipped.Add(skinned.name);
                    continue;
                }
                var box = Undo.AddComponent<BoxCollider>(skinned.gameObject);
                Bounds local = skinned.localBounds;
                box.center = local.center;
                box.size = local.size;
                added.Add($"{Path(skinned.transform, root.transform)} (Box Collider - postava)");
            }
        }
        Undo.CollapseUndoOperations(group);

        if (Selection.gameObjects.Length == 0)
            Debug.LogWarning("[Kolize] Nic neni vybrane - vyber objekt v Hierarchii.");
        else
            Debug.Log($"[Kolize] Pridano {added.Count}{(removed > 0 ? $", smazano prazdnych Mesh Collideru {removed}" : "")}:"
                + (added.Count == 0 ? " nic (modely uz kolizi maji, nebo vybrany objekt zadny model nema)" : "\n  " + string.Join("\n  ", added))
                + (skipped.Count > 0 ? $"\nVynechane drobnosti (oci, zuby, rasy, casti mensi nez {MinSize} m): {string.Join(", ", skipped)}" : ""));
    }

    // Uz pridane kolize z drobnosti (oci, zuby, rasy...) ve vybranych objektech smaze - i v kopii mapy pro Tlacenici.
    [MenuItem("BrosOverwatch/Mapa/Kolize - odebrat z drobnosti (oci, zuby...)")]
    static void RemoveDetails()
    {
        var removed = new List<string>();
        Undo.SetCurrentGroupName("Odebrat kolize z drobnosti");
        int group = Undo.GetCurrentGroup();
        foreach (var root in Selection.gameObjects)
            foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true))
            {
                var mesh = collider.sharedMesh;
                var filter = collider.GetComponent<MeshFilter>();
                if (mesh == null && filter != null) mesh = filter.sharedMesh;
                if (mesh == null || !Detail(collider.transform, mesh)) continue;
                removed.Add(Path(collider.transform, root.transform));
                Undo.DestroyObjectImmediate(collider);
            }
        Undo.CollapseUndoOperations(group);
        Debug.Log($"[Kolize] Odebrano z drobnosti: {removed.Count}" + (removed.Count > 0 ? "\n  " + string.Join("\n  ", removed) : ""));
    }

    [MenuItem("BrosOverwatch/Mapa/Kolize - odebrat z drobnosti (oci, zuby...)", true)]
    static bool RemoveDetailsCheck() => Selection.gameObjects.Length > 0 && !EditorApplication.isPlaying;

    // Drobne casti modelu (oci, zuby, rasy, jazyk, oboci... a cokoli mensiho nez MinSize) kolizi nepotrebuji -
    // jsou uvnitr vetsich casti, jen by zbytecne zatezovaly fyziku a navigacni sit botu.
    const float MinSize = 0.25f;
    static readonly string[] DetailNames = { "eye", "teeth", "tooth", "lash", "tongue", "brow", "pupil", "iris" };

    public static bool IsDetail(Transform t, Mesh mesh) => Detail(t, mesh);

    static bool Detail(Transform t, Mesh mesh)
    {
        string lower = t.name.ToLowerInvariant() + " " + mesh.name.ToLowerInvariant();
        foreach (var word in DetailNames)
            if (lower.Contains(word)) return true;
        Vector3 size = Vector3.Scale(mesh.bounds.size, t.lossyScale);
        return Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) < MinSize;
    }

    [MenuItem("BrosOverwatch/Mapa/Kolize na vybrany objekt", true)]
    static bool Check() => Selection.gameObjects.Length > 0 && !EditorApplication.isPlaying;

    static string Path(Transform t, Transform root)
    {
        string path = t.name;
        while (t != root && t.parent != null) { t = t.parent; path = t.name + "/" + path; if (t == root) break; }
        return path;
    }
}
