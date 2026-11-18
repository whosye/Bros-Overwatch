using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Lekarnicky na Domasov (testeri: malo leceni - na mape byla jedina): kolem kazdeho bodu (CapturePoint_N) dve
// lekarnicky (+75 HP) na volnem miste 7-12 m od stredu (ruzne strany), mezi body jedna velka (plne zdravi).
// Misto se hleda fyzikou: pevna podlaha zhruba ve vysce bodu a volno na postavu. Jen jednou (kdyz uz skupina
// "Lekarnicky" existuje, nic nedela - rozmisteni pak muzes upravit rucne). Ctrl+Z vrati.
public static class HealthPackTool
{
    const string GroupName = "Lekarnicky";

    [MenuItem("BrosOverwatch/Mapa/Lekarnicky k bodum (Domasov)")]
    static void Run()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Lekarnicky] Nejdriv vypni hru."); return; }
        var map = GameObject.Find("Map-Domasov");
        if (map == null) { Debug.LogWarning("[Lekarnicky] Nenasel jsem Map-Domasov."); return; }
        if (map.transform.Find(GroupName) != null) { Debug.LogWarning("[Lekarnicky] Uz existuji - nic nemenim.", map); return; }

        var points = new List<Vector3>();
        for (int i = 1; i <= 4; i++)
        {
            Transform point = null;
            foreach (var t in map.GetComponentsInChildren<Transform>(true))
                if (t.name == $"CapturePoint_{i}") { point = t; break; }
            if (point == null) break;
            points.Add(Ground(point.position + Vector3.up * 0.5f, point.position.y, 40f) ?? point.position);
        }
        if (points.Count == 0) { Debug.LogWarning("[Lekarnicky] Na Domasove nejsou CapturePoint_1.."); return; }

        Undo.SetCurrentGroupName("Lekarnicky");
        int group = Undo.GetCurrentGroup();
        var root = new GameObject(GroupName);
        Undo.RegisterCreatedObjectUndo(root, "Lekarnicky");
        root.transform.SetParent(map.transform, false);
        Physics.SyncTransforms();

        int placed = 0;
        var used = new List<Vector3>();
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            // dve na ruznych stranach bodu
            int here = 0;
            for (int a = 0; a < 16 && here < 2; a++)
            {
                float angle = (a * 67.5f + i * 40f) * Mathf.Deg2Rad;
                float r = 7f + (a % 3) * 2.5f;
                Vector3 candidate = p + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;
                var spot = Ground(candidate + Vector3.up * 4f, p.y, 10f);
                if (spot == null || TooClose(spot.Value, used, 6f) || !Free(spot.Value)) continue;
                Make(root.transform, $"Balicek_Lekarnicka_{(char)('A' + i)}{here + 1}", spot.Value, PickupKind.Health);
                used.Add(spot.Value);
                here++;
                placed++;
            }
            // velka mezi timto a dalsim bodem
            if (i + 1 < points.Count)
            {
                Vector3 mid = (p + points[i + 1]) * 0.5f;
                for (int a = 0; a < 12; a++)
                {
                    Vector3 candidate = mid + new Vector3(Mathf.Cos(a * 0.9f), 0f, Mathf.Sin(a * 0.9f)) * (a * 1.2f);
                    var spot = Ground(candidate + Vector3.up * 6f, Mathf.Lerp(p.y, points[i + 1].y, 0.5f), 14f);
                    if (spot == null || TooClose(spot.Value, used, 6f) || !Free(spot.Value)) continue;
                    Make(root.transform, $"Balicek_VelkaLekarnicka_{(char)('A' + i)}{(char)('B' + i)}", spot.Value, PickupKind.BigHealth);
                    used.Add(spot.Value);
                    placed++;
                    break;
                }
            }
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(map.scene);
        Selection.activeGameObject = root;
        Debug.Log($"[Lekarnicky] Rozmisteno {placed} lekarnicek kolem {points.Count} bodu. Zkontroluj je ve scene a pripadne posun; ulozit Ctrl+S.", root);
    }

    // Pevna podlaha pod 'from' (normal nahoru) nejvys 'tolerance' m od vysky 'nearY'.
    static Vector3? Ground(Vector3 from, float nearY, float depth, float tolerance = 2.5f)
    {
        var hits = Physics.RaycastAll(from, Vector3.down, depth, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        foreach (var hit in hits)
        {
            if (hit.normal.y < 0.8f) continue;
            if (Mathf.Abs(hit.point.y - nearY) > tolerance) continue;
            return hit.point;
        }
        return null;
    }

    // Volno na postavu (aby lekarnicka nebyla ve zdi / pod stolem).
    static bool Free(Vector3 feet) =>
        !Physics.CheckCapsule(feet + Vector3.up * 0.55f, feet + Vector3.up * 1.6f, 0.45f, ~0, QueryTriggerInteraction.Ignore);

    static bool TooClose(Vector3 p, List<Vector3> used, float min)
    {
        foreach (var u in used) if (Vector3.Distance(u, p) < min) return true;
        return false;
    }

    static void Make(Transform parent, string name, Vector3 position, PickupKind kind)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Lekarnicky");
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.AddComponent<PickupSpot>().kind = kind;
    }
}
