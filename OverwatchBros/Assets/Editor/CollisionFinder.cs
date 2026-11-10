using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// Hledani neviditelnych kolizi (strely se zastavi ve vzduchu, hrac narazi do niceho):
// 1) "Co je pod zamerovacem": ve hre paprsek ze stredu obrazovky (zamerovac), mimo hru ze stredu okna Scene.
//    Trefeny objekt se vybere v Hierarchy (i po vypnuti hry) a vypise se, cim to je.
// 2) "Srovnat kolizi vybraneho objektu s jeho vzhledem": Box/Mesh Collider podle viditelne site
//    (objekt bez viditelne site - kolize se vypne). Ctrl+Z vrati.
// 3) "Najit neviditelne kolize kolem okna Scene": vypise (a vybere) kolize, ktere presahuji vzhled nebo nemaji zadny.
public static class CollisionFinder
{
    const string PendingKey = "BrosOverwatch.CollisionFinder.Pending";
    const float Tolerance = 0.25f;

    [InitializeOnLoadMethod]
    static void Init()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            string path = SessionState.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(path)) return;
            SessionState.EraseString(PendingKey);
            var go = FindByPath(path);
            if (go == null) return;
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            Debug.Log($"[Kolize] Vybrano: {path}. Oprava: BrosOverwatch > Kolize > Srovnat kolizi vybraneho objektu s jeho vzhledem.", go);
        };
    }

    [MenuItem("BrosOverwatch/Kolize/Co je pod zamerovacem (ve hre) nebo uprostred okna Scene")]
    static void WhatIsHere()
    {
        Ray ray;
        if (Application.isPlaying)
        {
            var cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[Kolize] Ve hre neni hlavni kamera."); return; }
            ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        }
        else
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) { Debug.LogWarning("[Kolize] Otevri okno Scene."); return; }
            ray = new Ray(view.camera.transform.position, view.camera.transform.forward);
        }

        Physics.SyncTransforms();
        var hits = Physics.RaycastAll(ray, 300f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            // vlastni hrac (ve hre) preskocit
            if (Application.isPlaying && hit.collider.GetComponentInParent<FirstPersonController>() != null) continue;
            var go = hit.collider.gameObject;
            string path = PathOf(go.transform);
            Debug.Log($"[Kolize] Trefeno ve vzdalenosti {hit.distance:0.0} m: {path}\n{Describe(hit.collider)}", go);
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            if (Application.isPlaying) SessionState.SetString(PendingKey, path);   // po vypnuti hry ho vybrat znovu
            return;
        }
        Debug.Log("[Kolize] Paprsek nic netrefil.");
    }

    [MenuItem("BrosOverwatch/Kolize/Srovnat kolizi vybraneho objektu s jeho vzhledem")]
    static void FitSelected()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Kolize] Nejdriv vypni hru (zmeny ve hre se neulozi)."); return; }
        var go = Selection.activeGameObject;
        if (go == null) { Debug.LogWarning("[Kolize] Vyber objekt v Hierarchy."); return; }
        int fixedCount = 0;
        foreach (var collider in go.GetComponents<Collider>())
        {
            if (collider.isTrigger) continue;
            if (!LocalVisualBounds(collider.transform, out var local))
            {
                Undo.RecordObject(collider, "Srovnat kolizi");
                collider.enabled = false;
                Debug.Log($"[Kolize] {go.name}: nema zadnou viditelnou sit - kolize vypnuta.", go);
                fixedCount++;
                continue;
            }
            if (collider is BoxCollider box)
            {
                Undo.RecordObject(box, "Srovnat kolizi");
                Debug.Log($"[Kolize] {go.name}: Box Collider {box.center} / {box.size} -> {local.center} / {local.size}", go);
                box.center = local.center;
                box.size = local.size;
                fixedCount++;
            }
            else if (collider is MeshCollider meshCollider)
            {
                var filter = go.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && meshCollider.sharedMesh != filter.sharedMesh)
                {
                    Undo.RecordObject(meshCollider, "Srovnat kolizi");
                    meshCollider.sharedMesh = filter.sharedMesh;
                    Debug.Log($"[Kolize] {go.name}: Mesh Collider ted pouziva viditelnou sit.", go);
                    fixedCount++;
                }
            }
            else
                Debug.Log($"[Kolize] {go.name}: {collider.GetType().Name} neumim srovnat automaticky - uprav v Inspectoru.", go);
        }
        if (fixedCount > 0) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        else Debug.Log($"[Kolize] {go.name}: nic ke srovnani (zadna Box/Mesh kolize mimo vzhled).", go);
    }

    [MenuItem("BrosOverwatch/Kolize/Najit neviditelne kolize kolem okna Scene (40 m)")]
    static void FindAround()
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null) { Debug.LogWarning("[Kolize] Otevri okno Scene."); return; }
        Vector3 center = view.pivot;
        var found = new List<GameObject>();
        var report = new StringBuilder();
        foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
        {
            if (!collider.enabled || collider.isTrigger || collider is TerrainCollider || collider is CharacterController) continue;
            if (collider.GetComponentInParent<Rigidbody>() is Rigidbody rb && !rb.isKinematic) continue;
            Bounds b = collider.bounds;
            if (b.SqrDistance(center) > 40f * 40f) continue;
            string problem = Problem(collider);
            if (problem == null) continue;
            found.Add(collider.gameObject);
            report.AppendLine($"{PathOf(collider.transform)}: {problem}");
            Debug.Log($"[Kolize] {PathOf(collider.transform)}: {problem}", collider.gameObject);
        }
        Selection.objects = found.ToArray();
        Debug.Log(found.Count == 0
            ? "[Kolize] V okoli zadna podezrela kolize."
            : $"[Kolize] Podezrelych kolizi: {found.Count} (vybrane v Hierarchy, kliknuti na radek v Console ukaze objekt).\n{report}");
    }

    // Kolize presahuje vzhled o vic nez Tolerance, nebo vzhled nema.
    static string Problem(Collider collider)
    {
        if (!WorldVisualBounds(collider.transform, out var visual))
            return "kolize bez viditelne site";
        Bounds c = collider.bounds;
        float over = Mathf.Max(
            Mathf.Max(visual.min.x - c.min.x, c.max.x - visual.max.x),
            Mathf.Max(Mathf.Max(visual.min.y - c.min.y, c.max.y - visual.max.y), Mathf.Max(visual.min.z - c.min.z, c.max.z - visual.max.z)));
        return over > Tolerance ? $"kolize presahuje vzhled o {over:0.00} m" : null;
    }

    static string Describe(Collider collider)
    {
        string problem = Problem(collider);
        return problem == null ? "Kolize odpovida vzhledu (problem bude jinde - mozna v tom, co je tesne za tim)." : "Podezrele: " + problem;
    }

    static IEnumerable<(MeshFilter filter, Renderer renderer)> Visuals(Transform t)
    {
        foreach (var filter in t.GetComponentsInChildren<MeshFilter>())
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || filter.sharedMesh == null) continue;
            yield return (filter, renderer);
        }
    }

    static bool WorldVisualBounds(Transform t, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var (_, renderer) in Visuals(t))
        {
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return any;
    }

    // Obal viditelnych siti v lokalnich souradnicich objektu s kolizi.
    static bool LocalVisualBounds(Transform t, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var (filter, _) in Visuals(t))
        {
            Bounds mb = filter.sharedMesh.bounds;
            var toLocal = t.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = toLocal.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        return any;
    }

    static string PathOf(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    static GameObject FindByPath(string path)
    {
        var parts = path.Split('/');
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            var t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
            if (t != null) return t.gameObject;
        }
        return null;
    }
}
