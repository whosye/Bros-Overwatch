using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using HandleUtility = UnityEditor.HandleUtility;

// Scene tool: first click samples a face, subsequent clicks paint other objects.
// UVs are transferred in world units, so object size/scale cannot stretch the pattern.
[EditorTool("Texture Eyedropper")]
public sealed class TextureEyedropperTool : EditorTool
{
    Stamp source;
    string status = "Klikni na vzorovou plochu.";
    readonly HashSet<ProBuilderMesh> touched = new HashSet<ProBuilderMesh>();

    public override GUIContent toolbarIcon => new GUIContent("UV", "Texture kapatko");

    [MenuItem("Tools/Texture kapatko")]
    static void Activate() => ToolManager.SetActiveTool<TextureEyedropperTool>();

    public override void OnActivated()
    {
        source = null;
        status = "Klikni na vzorovou plochu.";
        Undo.undoRedoPerformed += RebuildAfterUndo;
    }

    public override void OnWillBeDeactivated()
    {
        Undo.undoRedoPerformed -= RebuildAfterUndo;
        // Mesh data is included in Undo, so it remains undoable after leaving the tool.
        touched.Clear();
    }

    void RebuildAfterUndo()
    {
        foreach (var mesh in touched)
        {
            if (!mesh) continue;
            mesh.ToMesh();
            mesh.Refresh();
        }
        SceneView.RepaintAll();
    }

    public override void OnToolGUI(EditorWindow window)
    {
        if (!(window is SceneView view)) return;
        var e = Event.current;
        var panel = new Rect(12, 12, Mathf.Min(570, view.position.width - 24), 78);
        Handles.BeginGUI();
        GUILayout.BeginArea(panel, GUI.skin.box);
        GUILayout.Label("Texture kapatko", EditorStyles.boldLabel);
        GUILayout.Label(status, EditorStyles.wordWrappedLabel);
        GUILayout.Label("Leve: nanest | Prave: nabrat vzor | Esc: konec", EditorStyles.miniLabel);
        GUILayout.EndArea();
        Handles.EndGUI();

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            e.Use();
            ToolManager.RestorePreviousTool();
            return;
        }
        if (panel.Contains(e.mousePosition)) return;
        // Keep ordinary object/face selection and ProBuilder shortcuts out of this tool.
        if (e.type == EventType.Layout)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        if (e.type == EventType.ContextClick)
        {
            e.Use();
            return;
        }
        if (e.type != EventType.MouseDown || e.alt || (e.button != 0 && e.button != 1)) return;

        bool sample = source == null || e.button == 1;
        e.Use();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            status = "Pouzij kapatko mimo Play Mode.";
        }
        else if (!TryPick(e.mousePosition, out var mesh, out var face))
        {
            status = "Klikni na plochu ProBuilder objektu.";
        }
        else
        {
            try
            {
                if (sample)
                {
                    source = Stamp.Capture(mesh, face);
                    status = "Vzor: " + mesh.name + ". Klikni na cilovou plochu.";
                }
                else
                {
                    source.Apply(mesh, face);
                    touched.Add(mesh);
                    status = "Nanesen vzor na " + mesh.name + ". Dalsi plocha nebo Ctrl+Z.";
                }
            }
            catch (InvalidOperationException ex)
            {
                status = ex.Message;
            }
        }
        view.Repaint();
    }

    static bool TryPick(Vector2 mouse, out ProBuilderMesh mesh, out Face face)
    {
        mesh = null;
        face = null;
        // Unity's picker respects visibility and occlusion; no Physics collider required.
        var go = HandleUtility.PickGameObject(mouse, false);
        if (!go || !go.TryGetComponent(out mesh)) return false;
        var ray = HandleUtility.GUIPointToWorldRay(mouse);
        float nearest = float.PositiveInfinity;
        var positions = mesh.positions;
        foreach (var candidate in mesh.faces)
        {
            var indices = candidate.indexes;
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                var a = mesh.transform.TransformPoint(positions[indices[i]]);
                var b = mesh.transform.TransformPoint(positions[indices[i + 1]]);
                var c = mesh.transform.TransformPoint(positions[indices[i + 2]]);
                if (RayTriangle(ray, a, b, c, out float distance) && distance < nearest)
                {
                    nearest = distance;
                    face = candidate;
                }
            }
        }
        return face != null;
    }

    static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
    {
        distance = 0;
        var ab = b - a;
        var ac = c - a;
        var p = Vector3.Cross(ray.direction, ac);
        float det = Vector3.Dot(ab, p);
        if (Mathf.Abs(det) < 1e-8f) return false;
        var t = ray.origin - a;
        float u = Vector3.Dot(t, p) / det;
        if (u < 0 || u > 1) return false;
        var q = Vector3.Cross(t, ab);
        float v = Vector3.Dot(ray.direction, q) / det;
        if (v < 0 || u + v > 1) return false;
        distance = Vector3.Dot(ac, q) / det;
        return distance >= 0;
    }

    internal sealed class Stamp
    {
        internal Material material;
        Vector3 origin, normal, dualU, dualV;
        Vector2 uvOrigin, uvU, uvV;

        static Vector3[] Triangle(ProBuilderMesh mesh, Face face)
        {
            var indices = face.indexes;
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                var a = mesh.transform.TransformPoint(mesh.positions[indices[i]]);
                var b = mesh.transform.TransformPoint(mesh.positions[indices[i + 1]]);
                var c = mesh.transform.TransformPoint(mesh.positions[indices[i + 2]]);
                if (Vector3.Cross(b - a, c - a).sqrMagnitude > 1e-12f)
                    return new[] { a, b, c, new Vector3(indices[i], indices[i + 1], indices[i + 2]) };
            }
            throw new InvalidOperationException("Plocha nema platny trojuhelnik.");
        }

        internal static Stamp Capture(ProBuilderMesh mesh, Face face)
        {
            var t = Triangle(mesh, face);
            var ab = t[1] - t[0];
            var ac = t[2] - t[0];
            var n = Vector3.Cross(ab, ac).normalized;
            var uv = mesh.textures;
            var materials = mesh.GetComponent<MeshRenderer>().sharedMaterials;
            if (uv == null || uv.Count != mesh.vertexCount || face.submeshIndex >= materials.Length
                || face.submeshIndex < 0 || !materials[face.submeshIndex])
                throw new InvalidOperationException("Vzorova plocha nema material nebo UV.");
            float aa = Vector3.Dot(ab, ab), bb = Vector3.Dot(ac, ac), abac = Vector3.Dot(ab, ac);
            float determinant = aa * bb - abac * abac;
            if (determinant <= 1e-12f)
                throw new InvalidOperationException("Vzorova plocha je prilis mala nebo uzka.");
            int i0 = (int)t[3].x, i1 = (int)t[3].y, i2 = (int)t[3].z;
            var stamp = new Stamp
            {
                origin = t[0], normal = n,
                dualU = (bb * ab - abac * ac) / determinant,
                dualV = (aa * ac - abac * ab) / determinant,
                uvOrigin = uv[i0], uvU = uv[i1] - uv[i0], uvV = uv[i2] - uv[i0],
                material = materials[face.submeshIndex]
            };
            foreach (int index in face.distinctIndexes)
            {
                var p = mesh.transform.TransformPoint(mesh.positions[index]);
                if (Mathf.Abs(Vector3.Dot(p - stamp.origin, n)) > .001f
                    || (stamp.Evaluate(p) - uv[index]).sqrMagnitude > 1e-6f)
                    throw new InvalidOperationException("Vzor musi byt rovna plocha s pravidelnym UV mapovanim.");
            }
            return stamp;
        }

        internal Vector2 Evaluate(Vector3 point)
        {
            var delta = point - origin;
            return uvOrigin + uvU * Vector3.Dot(delta, dualU) + uvV * Vector3.Dot(delta, dualV);
        }

        internal void Apply(ProBuilderMesh mesh, Face face)
        {
            if (!material) throw new InvalidOperationException("Material vzoru uz neexistuje. Nabrat novy vzor.");
            var triangle = Triangle(mesh, face);
            var targetNormal = Vector3.Cross(triangle[1] - triangle[0], triangle[2] - triangle[0]).normalized;
            var uv = mesh.textures?.ToArray();
            if (uv == null || uv.Length != mesh.vertexCount)
                throw new InvalidOperationException("Cilovy objekt nema platne UV souradnice.");
            var indices = new HashSet<int>(face.distinctIndexes);
            if (mesh.faces.Any(other => other != face && other.distinctIndexes.Any(indices.Contains)))
                throw new InvalidOperationException("Plocha sdili UV vrcholy s jinou plochou. Nejprve je rozdel.");

            // Fold the target plane into the sample plane around their intersection.
            // Coplanar floors keep the exact same world mapping; walls continue at the seam.
            var projectedNormal = targetNormal - normal * Vector3.Dot(targetNormal, normal);
            var pivot = origin;
            if (projectedNormal.sqrMagnitude > 1e-8f)
                pivot += projectedNormal * (Vector3.Dot(targetNormal, triangle[0] - origin) / projectedNormal.sqrMagnitude);
            var rotation = Quaternion.FromToRotation(targetNormal, normal);
            foreach (int index in indices)
            {
                var point = mesh.transform.TransformPoint(mesh.positions[index]);
                if (Mathf.Abs(Vector3.Dot(point - triangle[0], targetNormal)) > .001f)
                    throw new InvalidOperationException("Cil musi byt rovna plocha.");
                uv[index] = Evaluate(pivot + rotation * (point - pivot));
            }

            var renderer = mesh.GetComponent<MeshRenderer>();
            var meshFilter = mesh.GetComponent<MeshFilter>();
            var undo = new List<UnityEngine.Object> { mesh, renderer };
            if (meshFilter && meshFilter.sharedMesh) undo.Add(meshFilter.sharedMesh);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Texture kapatko");
            Undo.RegisterCompleteObjectUndo(undo.ToArray(), "Texture kapatko");
            face.manualUV = true;
            face.textureGroup = -1;
            mesh.textures = uv;
            mesh.SetMaterial(new[] { face }, material);
            mesh.ToMesh();
            mesh.Refresh();
            EditorUtility.SetDirty(mesh);
            EditorUtility.SetDirty(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(mesh);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorSceneManager.MarkSceneDirty(mesh.gameObject.scene);
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
        }
    }
}
