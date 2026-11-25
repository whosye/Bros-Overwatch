using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class CapturePointAreaChecks
{
    [MenuItem("BrosOverwatch/Body/Overit vykreslovani bodu 4")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Kontrolu spust mimo Play Mode.");
        var area = Resources.FindObjectsOfTypeAll<CapturePointArea>().Single(a =>
            a.gameObject.scene.IsValid() && a.gameObject.activeInHierarchy && a.name == "CapturePoint_4");
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var type = typeof(CapturePointView);
        var rootField = type.GetField("root", flags);
        if ((GameObject)rootField.GetValue(null) != null)
            throw new InvalidOperationException("Existujici herni nahled nebude menen.");
        var resize = type.GetMethod("Resize", flags);
        var report = new StringBuilder();
        try
        {
            type.GetMethod("Build", flags).Invoke(null, null);
            var root = (GameObject)rootField.GetValue(null);
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.SetPositionAndRotation(area.transform.position,
                Quaternion.Euler(0, area.transform.eulerAngles.y, 0));
            resize.Invoke(null, new object[] { new Vector2(19, 10.5f), area });
            Verify(root, area.Geometry.area, area.Geometry.outline.Length, report, "composite point");
            resize.Invoke(null, new object[] { new Vector2(10, 7), null });
            Verify(root, 70, 4, report, "legacy rectangle after composite");
            resize.Invoke(null, new object[] { new Vector2(19, 10.5f), area });
            Verify(root, area.Geometry.area, area.Geometry.outline.Length, report, "composite after legacy rectangle");
            report.AppendLine("PASS runtime rendering uses collision-free geometry and upward-facing non-overlapping fill triangles.");
            File.WriteAllText("Temp/CapturePointViewQA.txt", report.ToString());
            Debug.Log("[Bod 4 QA] " + report);
        }
        finally
        {
            var root = (GameObject)rootField.GetValue(null);
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            foreach (string field in new[] { "fillMesh", "borderMaterial", "fillMaterial", "beaconMaterial" })
            {
                var value = (UnityEngine.Object)type.GetField(field, flags).GetValue(null);
                if (value != null) UnityEngine.Object.DestroyImmediate(value);
            }
            rootField.SetValue(null, null);
        }
    }

    static void Verify(GameObject root, float expectedArea, int expectedEdges, StringBuilder report, string label)
    {
        var surface = root.transform.Find("Surface");
        var fill = surface.Find("Fill").GetComponent<MeshFilter>().sharedMesh;
        var vertices = fill.vertices;
        var triangles = fill.triangles;
        float area = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var cross = Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]], vertices[triangles[i + 2]] - vertices[triangles[i]]);
            if (cross.y <= 0) throw new InvalidOperationException("Fill faces downwards.");
            area += cross.magnitude * .5f;
        }
        if (Mathf.Abs(area - expectedArea) > .002f || surface.childCount != expectedEdges + 1
            || root.GetComponentsInChildren<Collider>(true).Length != 0)
            throw new InvalidOperationException("Runtime footprint/outline mismatch: " + label);
        report.AppendLine($"PASS {label}: fill {area:F3} m2, {surface.childCount - 1} border segments, no colliders.");
    }
}
