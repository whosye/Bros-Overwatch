using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// BrosOverwatch > Tlacenice > Prenest vybrane objekty do Tlacenice: rucne pridane veci na Domasove (napr. gauc s Maxem)
// zkopiruje do kopie mapy Map_DomasovPush - na stejne misto (+ MatchManager.DomasovPushOrigin), pod stejneho rodice
// (kdyz v kopii je), jinak primo pod Map_DomasovPush. Prefaby zustanou napojene (jako Ctrl+D), kolize se kopiruji s nimi.
// Jen jednou: kdyz uz v kopii objekt se stejnym jmenem na tom miste je, preskoci ho. Puvodni objekt se nemeni. Ctrl+Z vrati.
public static class PushCopyTool
{
    const string SourceRoot = "Map-Domasov";
    const string CopyRoot = "Map_DomasovPush";

    [MenuItem("BrosOverwatch/Tlacenice/Prenest vybrane objekty do Tlacenice")]
    static void Run()
    {
        var copyRoot = FindRoot(CopyRoot);
        if (copyRoot == null)
        {
            Debug.LogWarning("[Tlacenice] Kopie mapy Map_DomasovPush ve scene neni (krok 1).");
            return;
        }

        var previousSelection = Selection.objects;
        var sources = new List<GameObject>(Selection.gameObjects);
        var report = new List<string>();
        Undo.SetCurrentGroupName("Prenest do Tlacenice");
        int group = Undo.GetCurrentGroup();

        foreach (var source in sources)
        {
            if (source == null) continue;
            if (source.transform.IsChildOf(copyRoot.transform))
            {
                report.Add($"{source.name}: uz je v mape Tlacenice - preskoceno");
                continue;
            }
            Transform parent = TargetParent(source.transform, copyRoot.transform);
            Vector3 position = source.transform.position + MatchManager.DomasovPushOrigin;
            if (Existing(parent, source.name, position))
            {
                report.Add($"{source.name}: v Tlacenici uz je - preskoceno");
                continue;
            }

            var copy = Duplicate(source);
            Undo.SetTransformParent(copy.transform, parent, "Prenest do Tlacenice");
            copy.name = source.name;
            copy.transform.SetPositionAndRotation(position, source.transform.rotation);
            copy.SetActive(source.activeSelf);
            report.Add($"{source.name} -> {Path(parent)} na {position:0.0}");
        }

        Undo.CollapseUndoOperations(group);
        Selection.objects = previousSelection;
        EditorSceneManager.MarkSceneDirty(copyRoot.scene);
        Debug.Log($"[Tlacenice] Prenos do Tlacenice ({report.Count}):\n  " + string.Join("\n  ", report)
            + "\nZkontroluj: BrosOverwatch > Mapa > Domasov (Tlacenice) - ukazat. Pak ulozit scenu (Ctrl+S).");
    }

    [MenuItem("BrosOverwatch/Tlacenice/Prenest vybrane objekty do Tlacenice", true)]
    static bool Check() => Selection.gameObjects.Length > 0 && !EditorApplication.isPlaying;

    // Kolize pridane rucne na Domasove (u vybranych objektu a jejich podobjektu) prenese na stejne objekty v kopii:
    // protejsek = stejne jmeno na miste + DomasovPushOrigin. Kolizi stejneho typu, kterou uz protejsek ma, nepridava.
    [MenuItem("BrosOverwatch/Tlacenice/Prenest kolize vybranych objektu do Tlacenice")]
    static void CopyColliders()
    {
        var copyRoot = FindRoot(CopyRoot);
        if (copyRoot == null)
        {
            Debug.LogWarning("[Tlacenice] Kopie mapy Map_DomasovPush ve scene neni (krok 1).");
            return;
        }
        var candidates = copyRoot.GetComponentsInChildren<Transform>(true);
        var report = new List<string>();
        Undo.SetCurrentGroupName("Prenest kolize do Tlacenice");
        int group = Undo.GetCurrentGroup();

        foreach (var root in Selection.gameObjects)
        {
            if (root.transform.IsChildOf(copyRoot.transform))
            {
                report.Add($"{root.name}: je v mape Tlacenice - vyber original na Domasove");
                continue;
            }
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                var source = collider.transform;
                Transform target = Counterpart(source, candidates);
                if (target == null)
                {
                    report.Add($"{source.name}: v Tlacenici neni (prenes ho prikazem Prenest vybrane objekty do Tlacenice)");
                    continue;
                }
                if (target.GetComponent(collider.GetType()) != null)
                {
                    report.Add($"{source.name}: {collider.GetType().Name} uz ma");
                    continue;
                }
                var added = (Collider)Undo.AddComponent(target.gameObject, collider.GetType());
                EditorUtility.CopySerialized(collider, added);
                report.Add($"{source.name}: pridan {collider.GetType().Name} -> {Path(target)}");
            }
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(copyRoot.scene);
        Debug.Log($"[Tlacenice] Prenos kolizi ({report.Count}):\n  " + (report.Count == 0 ? "vybrane objekty zadnou kolizi nemaji" : string.Join("\n  ", report))
            + "\nPak ulozit scenu (Ctrl+S).");
    }

    [MenuItem("BrosOverwatch/Tlacenice/Prenest kolize vybranych objektu do Tlacenice", true)]
    static bool CopyCollidersCheck() => Selection.gameObjects.Length > 0 && !EditorApplication.isPlaying;

    static Transform Counterpart(Transform source, Transform[] candidates)
    {
        Vector3 wanted = source.position + MatchManager.DomasovPushOrigin;
        Transform best = null;
        float bestDistance = 0.25f;   // do 0,5 m
        foreach (var t in candidates)
        {
            if (t.name != source.name) continue;
            float d = (t.position - wanted).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = t; }
        }
        return best;
    }

    // Rodic v kopii: stejna cesta jako pod Map-Domasov (kdyz v kopii existuje), jinak koren kopie.
    static Transform TargetParent(Transform source, Transform copyRoot)
    {
        var names = new List<string>();
        for (var t = source.parent; t != null; t = t.parent)
        {
            if (t.parent == null)
            {
                if (t.name != SourceRoot) return copyRoot;   // mimo Domasov (napr. v koreni sceny)
                break;
            }
            names.Insert(0, t.name);
        }
        if (source.parent == null) return copyRoot;
        Transform target = copyRoot;
        foreach (var name in names)
        {
            var child = target.Find(name);
            if (child == null) return target;   // cast cesty v kopii chybi: co nejbliz
            target = child;
        }
        return target;
    }

    static bool Existing(Transform parent, string name, Vector3 position)
    {
        foreach (Transform child in parent)
            if (child.name == name && (child.position - position).sqrMagnitude < 0.25f) return true;
        return false;
    }

    // Jako Ctrl+D (prefab zustane napojeny vcetne zmen). Kdyz to nejde, obycejna kopie.
    static GameObject Duplicate(GameObject go)
    {
        Selection.objects = new Object[] { go };
        Unsupported.DuplicateGameObjectsUsingPasteboard();
        var result = Selection.activeGameObject;
        if (result != null && result != go) return result;
        var clone = Object.Instantiate(go, go.transform.parent);
        Undo.RegisterCreatedObjectUndo(clone, "Prenest do Tlacenice");
        return clone;
    }

    static string Path(Transform t)
    {
        string path = t.name;
        for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    static GameObject FindRoot(string name)
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == name) return go;
        return null;
    }
}
