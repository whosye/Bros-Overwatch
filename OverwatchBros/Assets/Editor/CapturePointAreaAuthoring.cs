using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CapturePointAreaAuthoring
{
    public const string DesignName = "NAVRH_BODU_4_CHATA";
    public const string ReportPath = "Temp/CapturePointAreaQA.txt";

    [MenuItem("BrosOverwatch/Body/Prevest navrh bodu 4")]
    public static void ConvertPoint4()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Ukonci Play Mode pred upravou bodu.");
        var designs = Resources.FindObjectsOfTypeAll<Transform>().Where(t =>
            t.name == DesignName && t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded).ToArray();
        if (designs.Length != 1)
            throw new InvalidOperationException($"Ocekavan jeden {DesignName}, nalezeno {designs.Length}.");
        var design = designs[0];
        var map = design;
        while (map.parent != null) map = map.parent;
        var marker = map.Find("CapturePoint_4");
        if (marker == null) throw new InvalidOperationException("Na mape chybi CapturePoint_4.");
        var filters = design.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToArray();
        if (filters.Length == 0 || filters.Length > 64)
            throw new InvalidOperationException("Navrh musi obsahovat 1 az 64 bloku s meshem.");

        var frame = Matrix4x4.TRS(marker.position, Quaternion.Euler(0, marker.eulerAngles.y, 0), Vector3.one);
        var regions = new List<Rect>();
        var floorHeights = new List<float>();
        var report = new StringBuilder();
        foreach (var filter in filters)
        {
            var matrix = frame.inverse * filter.transform.localToWorldMatrix;
            foreach (var axis in new[] { Vector3.right, Vector3.forward })
            {
                var direction = matrix.MultiplyVector(axis).normalized;
                if (Mathf.Abs(direction.y) > .001f || (Mathf.Abs(direction.x) > .001f && Mathf.Abs(direction.z) > .001f))
                    throw new InvalidOperationException(filter.name + ": blok musi byt zarovnany s osami bodu (0/90 stupnu).");
            }
            var localBounds = filter.sharedMesh.bounds;
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            float bottom = float.PositiveInfinity;
            for (int i = 0; i < 8; i++)
            {
                var corner = localBounds.center + Vector3.Scale(localBounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var point = matrix.MultiplyPoint3x4(corner);
                min = Vector3.Min(min, point); max = Vector3.Max(max, point);
                bottom = Mathf.Min(bottom, filter.transform.TransformPoint(corner).y);
            }
            var region = Rect.MinMaxRect(Snap(min.x), Snap(min.z), Snap(max.x), Snap(max.z));
            if (region.width < .02f || region.height < .02f)
                throw new InvalidOperationException(filter.name + ": blok ma nulovy pudorys.");
            regions.Add(region);
            floorHeights.Add(bottom);
            report.AppendLine($"SOURCE {filter.name}: {region.ToString("F3")} bottomY={bottom:F3}");
        }
        var geometry = new CaptureAreaGeometry(regions);
        if (geometry.IsEmpty) throw new InvalidOperationException("Navrh nema zadnou plochu.");

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Bod 4 podle navrhovych bloku");
        Undo.RecordObjects(new UnityEngine.Object[] { marker, design.gameObject }, "Bod 4 podle navrhu");
        var area = marker.GetComponent<CapturePointArea>();
        if (area == null) area = Undo.AddComponent<CapturePointArea>(marker.gameObject);
        Undo.RecordObject(area, "Slozeny pudorys bodu 4");
        area.enabled = true;
        area.SetRegions(regions.ToArray());
        // Draft height was approximate. Its median bottom gives the floor level;
        // the footprint itself is independent of draft block height and collider size.
        floorHeights.Sort();
        var position = marker.position;
        position.y = Snap(floorHeights[floorHeights.Count / 2]);
        marker.position = position;
        design.gameObject.tag = "EditorOnly";
        design.gameObject.SetActive(false);
        EditorUtility.SetDirty(area);
        PrefabUtility.RecordPrefabInstancePropertyModifications(area);
        PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
        EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);
        Undo.CollapseUndoOperations(group);

        Validate(area, design, regions, report);
        File.WriteAllText(ReportPath, report.ToString());
        Selection.activeGameObject = marker.gameObject;
        SceneView.RepaintAll();
        Debug.Log($"[Bod 4] {regions.Count} casti, {geometry.area:F3} m2, {geometry.outline.Length} vnejsich hran. Kontrola: {ReportPath}");
    }

    static void Validate(CapturePointArea area, Transform design, List<Rect> source, StringBuilder report)
    {
        var geometry = area.Geometry;
        int samples = 0;
        // Compare every part of the footprint, including notches outside the union,
        // against the authored source rectangles rather than a bounding rectangle.
        var bounds = geometry.bounds;
        for (float x = bounds.xMin - .1f; x < bounds.xMax + .1f; x += .073f)
        for (float z = bounds.yMin - .1f; z < bounds.yMax + .1f; z += .079f)
        {
            var point = new Vector2(x, z);
            bool expected = source.Any(r => point.x >= r.xMin && point.x <= r.xMax && point.y >= r.yMin && point.y <= r.yMax);
            if (geometry.Contains(point) != expected)
                throw new InvalidOperationException("Pudorys se lisi od navrhu: " + point);
            samples++;
        }
        foreach (var edge in geometry.outline)
        {
            var middle = (edge.a + edge.b) * .5f;
            var direction = (edge.b - edge.a).normalized;
            var perpendicular = new Vector2(-direction.y, direction.x) * .002f;
            if (geometry.Contains(middle + perpendicular) == geometry.Contains(middle - perpendicular))
                throw new InvalidOperationException("Obrys obsahuje vnitrni nebo chybejici hranu.");
        }
        if (design.gameObject.activeSelf || !design.CompareTag("EditorOnly"))
            throw new InvalidOperationException("Navrhove bloky nesmi vstoupit do hry.");
        for (int i = 0; i < 1000; i++)
        {
            var point = geometry.Sample((i * .6180339f) % 1f, .2f, .8f);
            if (!geometry.Contains(point)) throw new InvalidOperationException("Bot muze cilit mimo bod.");
        }
        report.AppendLine($"PASS {samples} occupancy samples match all {source.Count} source boxes.");
        report.AppendLine($"PASS {geometry.outline.Length} outline segments separate inside from outside; no internal seams.");
        report.AppendLine("PASS 1000 bot destinations remain inside the composite point.");
        report.AppendLine("PASS draft is inactive and EditorOnly; no draft rendering or collisions at runtime.");
        report.AppendLine($"AREA {geometry.area:F3} m2; FLOOR {area.transform.position.y:F3}; REGIONS {source.Count}");
    }

    static float Snap(float value) => Mathf.Round(value * 1000f) / 1000f;
}

[CustomEditor(typeof(CapturePointArea))]
public sealed class CapturePointAreaInspector : Editor
{
    public override void OnInspectorGUI()
    {
        var area = (CapturePointArea)target;
        EditorGUILayout.LabelField("Slozeny dobyvaci bod", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Casti", area.RegionCount.ToString());
        EditorGUILayout.LabelField("Plocha", area.Geometry.area.ToString("F2") + " m2");
        EditorGUILayout.HelpBox("Pudorys urcuji navrhove bloky, Scale markeru ho nemeni. Pro zmenu zapni NAVRH_BODU_4_CHATA, uprav bloky a klikni na tlacitko dole. Po prevodu se navrh zase skryje.", MessageType.Info);
        if (GUILayout.Button("Obnovit podle navrhovych bloku")) CapturePointAreaAuthoring.ConvertPoint4();
    }
}
