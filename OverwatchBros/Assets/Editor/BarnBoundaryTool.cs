using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Neviditelna hranice mapy (HraniceProHrace/Hranice) nesmi prochazet stodolou: chytala by hrace i sipy.
// Nastroj najde stodoly, kterymi hranice prochazi, a hranici kolem nich obejde ZVENKU: usek stěny ve stodole
// se vyrizne a za stodolou (z vnejsi strany mapy) se postavi objizdka + spojky. Zbytek hranice zustane.
// Jen jednou (kdyz uz objizdky existuji, nic nedela); Ctrl+Z vrati.
public static class BarnBoundaryTool
{
    const string DetourName = "Hranice_Stodola";
    const float Margin = 1f;        // odstup objizdky od stodoly
    const float Thickness = 0.6f;

    [MenuItem("BrosOverwatch/Mapa/Hranici mapy obejit kolem stodol")]
    static void Run()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Hranice] Nejdriv vypni hru."); return; }

        var walls = new List<BoxCollider>();
        Transform parent = null;
        foreach (var box in Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude))
        {
            if (box.transform.parent == null || box.transform.parent.name != "HraniceProHrace") continue;
            parent = box.transform.parent;
            if (box.name.StartsWith(DetourName))
            {
                Debug.LogWarning("[Hranice] Objizdky kolem stodol uz existuji - nic nemenim.", box);
                return;
            }
            walls.Add(box);
        }
        if (walls.Count == 0) { Debug.LogWarning("[Hranice] Nenasel jsem HraniceProHrace/Hranice."); return; }

        // stred mapy (uvnitr hranic) = stred vsech hranicnich sten
        Vector3 inside = Vector3.zero;
        foreach (var w in walls) inside += w.bounds.center;
        inside /= walls.Count;

        int fixedCount = 0;
        foreach (var barn in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            if (!barn.name.StartsWith("Stodola") || barn.Find("Podlaha") == null) continue;
            var renderers = barn.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            b.Expand(new Vector3(Margin * 2f, 0f, Margin * 2f));

            foreach (var wall in walls.ToArray())
                if (Detour(wall, b, inside, parent, walls))
                {
                    fixedCount++;
                    Debug.Log($"[Hranice] {barn.name}: hranice {wall.name} ted obchazi stodolu zvenku.", barn);
                }
        }

        if (fixedCount == 0) Debug.Log("[Hranice] Zadna stodola neni na hranici mapy.");
        else EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
    }

    // Stena (osove zarovnana tenka krabice) protina obal stodoly -> vyriznout a obejit.
    static bool Detour(BoxCollider wall, Bounds barn, Vector3 inside, Transform parent, List<BoxCollider> walls)
    {
        Bounds w = wall.bounds;
        if (w.max.x < barn.min.x || w.min.x > barn.max.x || w.max.z < barn.min.z || w.min.z > barn.max.z) return false;

        bool alongX = w.size.x > w.size.z;                     // stena vede podel osy X (jinak podel Z)
        float c = alongX ? w.center.z : w.center.x;            // poloha steny napric
        float a0 = alongX ? w.min.x : w.min.z, a1 = alongX ? w.max.x : w.max.z;   // rozsah podel
        float b0 = alongX ? barn.min.x : barn.min.z, b1 = alongX ? barn.max.x : barn.max.z;
        float insideSign = Mathf.Sign((alongX ? inside.z : inside.x) - c);
        float outer = insideSign > 0f ? (alongX ? barn.min.z : barn.min.x) : (alongX ? barn.max.z : barn.max.x);
        float y = w.center.y, height = w.size.y;

        Undo.RecordObject(wall.transform, "Hranice kolem stodoly");
        Undo.RecordObject(wall, "Hranice kolem stodoly");

        // zbytky puvodni steny mimo stodolu
        bool keepLow = a0 < b0, keepHigh = a1 > b1;
        if (keepLow) SetSegment(wall, alongX, c, a0, b0, y, height);
        if (keepHigh)
        {
            if (keepLow) walls.Add(Make(parent, alongX, c, b1, a1, y, height, wall.name));
            else SetSegment(wall, alongX, c, b1, a1, y, height);
        }
        if (!keepLow && !keepHigh) Undo.DestroyObjectImmediate(wall.gameObject);

        // objizdka za stodolou pres celou jeji delku: kde stena konci uvnitr stodoly (roh mapy), potka se tam
        // s objizdkou druhe steny
        Make(parent, alongX, outer, b0, b1, y, height, DetourName);
        // spojky z puvodni linie na objizdku (jen kde stena pokracuje dal)
        if (keepLow) Make(parent, !alongX, b0, Mathf.Min(c, outer), Mathf.Max(c, outer), y, height, DetourName + "_Spojka");
        if (keepHigh) Make(parent, !alongX, b1, Mathf.Min(c, outer), Mathf.Max(c, outer), y, height, DetourName + "_Spojka");
        return true;
    }

    static void SetSegment(BoxCollider wall, bool alongX, float c, float from, float to, float y, float height)
    {
        // svet -> lokalne: stena je osove zarovnana, jen posunout stred a delku podel jeji osy
        Vector3 center = alongX ? new Vector3((from + to) * 0.5f, y, c) : new Vector3(c, y, (from + to) * 0.5f);
        Vector3 worldSize = alongX ? new Vector3(to - from, height, Thickness) : new Vector3(Thickness, height, to - from);
        wall.transform.position += center - wall.bounds.center;
        Vector3 local = wall.transform.InverseTransformVector(worldSize);
        wall.size = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
        wall.center = Vector3.zero;
    }

    static BoxCollider Make(Transform parent, bool alongX, float c, float from, float to, float y, float height, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Hranice kolem stodoly");
        go.transform.SetParent(parent, false);
        go.transform.position = alongX ? new Vector3((from + to) * 0.5f, y, c) : new Vector3(c, y, (from + to) * 0.5f);
        go.transform.rotation = Quaternion.identity;
        var box = go.AddComponent<BoxCollider>();
        Vector3 worldSize = alongX ? new Vector3(to - from + Thickness, height, Thickness) : new Vector3(Thickness, height, to - from + Thickness);
        Vector3 local = go.transform.InverseTransformVector(worldSize);
        box.size = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
        return box;
    }
}
