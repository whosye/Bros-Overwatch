using UnityEditor;
using UnityEngine;

public static class YarisRoutePreviewEditor
{
    [MenuItem("BrosOverwatch/Mapa/Zobrazit trasu Yarisu")]
    static void ShowRoute()
    {
        var map = GameObject.Find("Map-Domasov");
        var root = map != null ? map.transform.Find("ProvozYaris") : null;
        if (root == null) return;
        var car = map.transform.Find("Props/ToyotaYaris");
        Selection.activeGameObject = root.GetComponent<YarisRoutePreview>() != null || car == null ? root.gameObject : car.gameObject;
        var view = SceneView.lastActiveSceneView;
        if (view != null) { view.drawGizmos = true; view.FrameSelected(); view.Repaint(); }
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.InSelectionHierarchy)]
    static void DrawRoute(YarisRoutePreview preview, GizmoType type)
    {
        Draw(preview.route);
    }

    [DrawGizmo(GizmoType.Selected)]
    static void DrawCarRoute(YarisTraffic car, GizmoType type) => Draw(car.route);

    static void Draw(Transform route)
    {
        if (route == null || route.childCount < 2) return;
        Color previous = Handles.color;
        Handles.color = new Color(1f, 0.65f, 0.05f);
        var points = new Vector3[route.childCount + 1];
        for (int i = 0; i < route.childCount; i++) points[i] = route.GetChild(i).position + Vector3.up * 0.35f;
        points[route.childCount] = points[0];
        Handles.DrawAAPolyLine(4f, points);
        for (int i = 0; i < route.childCount; i++)
        {
            if (i % 6 != 0) continue;
            Vector3 direction = points[i + 1] - points[i];
            if (direction.sqrMagnitude < 0.01f) continue;
            Handles.ArrowHandleCap(0, points[i], Quaternion.LookRotation(direction), 2f, EventType.Repaint);
        }
        Handles.Label(points[0] + Vector3.up, "Yaris: zacatek / cekani mimo mapu");
        Handles.color = previous;
    }
}
