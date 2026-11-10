using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Pohled dovnitr budov v okne Scene: "rez" skryje vse, co je cele nad danou vyskou (strop, patro, strecha, koruny
// stromu...) v okoli mista, kam se okno Scene diva. Jen viditelnost v editoru (jako oko v Hierarchy) - na hru ani
// na scenu to nema vliv, nic se neuklada. Zpet: Pohled > Ukazat vse.
public static class ScenePeek
{
    const float Radius = 45f;   // jak daleko od stredu pohledu se skryva

    [MenuItem("BrosOverwatch/Pohled/Rez - prizemi (skryt vse nad 3,2 m)")]
    static void GroundFloor() => Cut(3.2f);

    [MenuItem("BrosOverwatch/Pohled/Rez - 1. patro (skryt vse nad 7 m)")]
    static void FirstFloor() => Cut(7.0f);

    [MenuItem("BrosOverwatch/Pohled/Rez - nad vybranym objektem (skryt vse nad jeho vrchem)")]
    static void AboveSelection()
    {
        var go = Selection.activeGameObject;
        var renderer = go != null ? go.GetComponentInChildren<Renderer>() : null;
        if (renderer == null)
        {
            Debug.LogWarning("[Pohled] Vyber objekt (napr. podlahu patra) - skryje se vse nad jeho vrchem.");
            return;
        }
        Cut(renderer.bounds.max.y + 0.05f);
    }

    // Okno Scene do normalniho perspektivniho pohledu (misto "Iso") s blizkym orezem - pri priblizeni se nic
    // nerozmaze pres pul obrazovky. Kamera se zameri na vybrany objekt (kdyz nejaky je).
    [MenuItem("BrosOverwatch/Pohled/Normalni kamera (perspektiva, opravi rozmazane priblizeni)")]
    static void PerspectiveCamera()
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null) { Debug.LogWarning("[Pohled] Otevri okno Scene."); return; }
        view.orthographic = false;
        view.in2DMode = false;
        var settings = view.cameraSettings;
        settings.dynamicClip = true;
        settings.nearClip = 0.01f;
        view.cameraSettings = settings;
        if (Selection.activeGameObject != null) view.FrameSelected();
        view.Repaint();
        Debug.Log("[Pohled] Okno Scene v perspektive. Priblizovani: kolecko mysi; letani: drz prave tlacitko + W A S D (Q/E dolu/nahoru); F = najet na vybrany objekt.");
    }

    [MenuItem("BrosOverwatch/Pohled/Ukazat vse")]
    static void ShowAll()
    {
        SceneVisibilityManager.instance.ShowAll();
        Debug.Log("[Pohled] Vse zase viditelne.");
    }

    static void Cut(float height)
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null)
        {
            Debug.LogWarning("[Pohled] Otevri okno Scene.");
            return;
        }
        SceneVisibilityManager.instance.ShowAll();
        Vector3 center = view.pivot;
        var hide = new List<GameObject>();
        foreach (var renderer in Object.FindObjectsByType<Renderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            Bounds b = renderer.bounds;
            if (b.min.y < height) continue;
            Vector2 flat = new Vector2(b.center.x - center.x, b.center.z - center.z);
            if (flat.magnitude - Mathf.Max(b.extents.x, b.extents.z) > Radius) continue;
            hide.Add(renderer.gameObject);
        }
        SceneVisibilityManager.instance.Hide(hide.ToArray(), false);
        Debug.Log($"[Pohled] Rez ve vysce {height:0.0} m: skryto {hide.Count} objektu v okoli {Radius:0} m. Zpet: BrosOverwatch > Pohled > Ukazat vse.");
    }
}
