using UnityEditor;
using UnityEngine;

// Zmena rozmeru kvadru postaveneho skripty (MapBuildKit.Box: sit "Box_ŠxVxH" + Box Collider) BEZ Scale:
// sit se postavi znovu v novych rozmerech (textura se neroztahne) a kolize se srovna. Ctrl+Z vrati.
// Okno: BrosOverwatch > Rozmer kvadru. Vyber kvadr v Hierarchy, zadej rozmery v metrech a Pouzit.
public class BoxResizeWindow : EditorWindow
{
    enum Anchor { Stred, Spodek, Vrch }

    Vector3 size = Vector3.one;
    Anchor anchor = Anchor.Spodek;
    GameObject shown;

    [MenuItem("BrosOverwatch/Rozmer kvadru (bez Scale)")]
    static void Open() => GetWindow<BoxResizeWindow>("Rozmer kvadru");

    void OnSelectionChange() => Repaint();

    void OnGUI()
    {
        var go = Selection.activeGameObject;
        var filter = go != null ? go.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null)
        {
            EditorGUILayout.HelpBox("Vyber v Hierarchy kvadr (objekt s Mesh Filter).", MessageType.Info);
            return;
        }
        if (go.GetComponent("ProBuilderMesh") != null)
        {
            EditorGUILayout.HelpBox("Tohle je ProBuilder objekt - rozmer zmenis v Inspectoru: Pro Builder Shape > Size (Edit Shape).", MessageType.Info);
            return;
        }
        if (go != shown)
        {
            shown = go;
            size = filter.sharedMesh.bounds.size;
        }

        EditorGUILayout.LabelField(go.name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Ted: " + Format(filter.sharedMesh.bounds.size));
        EditorGUILayout.Space();
        size = EditorGUILayout.Vector3Field("Novy rozmer (m) - X, Y (vyska), Z", size);
        anchor = (Anchor)EditorGUILayout.EnumPopup("Co zustane na miste", anchor);
        EditorGUILayout.HelpBox("Spodek: kvadr roste nahoru, spodek stoji (napr. zed na zemi). Stred: roste na vsechny strany.", MessageType.None);

        if (GUILayout.Button("Pouzit", GUILayout.Height(28)))
            Apply(go, filter);
    }

    void Apply(GameObject go, MeshFilter filter)
    {
        Vector3 newSize = new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y), Mathf.Max(0.01f, size.z));
        Bounds old = filter.sharedMesh.bounds;

        Undo.RecordObject(filter, "Rozmer kvadru");
        Undo.RecordObject(go.transform, "Rozmer kvadru");
        var mesh = MapBuildKit.BoxMesh(newSize, true);
        // stred site jako puvodne (MapBuildKit kvadry maji stred v 0); posun podle kotvy
        filter.sharedMesh = mesh;

        float dy = 0f;
        if (anchor == Anchor.Spodek) dy = (newSize.y - old.size.y) * 0.5f;
        else if (anchor == Anchor.Vrch) dy = -(newSize.y - old.size.y) * 0.5f;
        if (Mathf.Abs(dy) > 1e-5f)
            go.transform.position += go.transform.up * dy * go.transform.lossyScale.y;

        var box = go.GetComponent<BoxCollider>();
        if (box != null)
        {
            Undo.RecordObject(box, "Rozmer kvadru");
            box.center = Vector3.zero;
            box.size = newSize;
        }
        var meshCollider = go.GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            Undo.RecordObject(meshCollider, "Rozmer kvadru");
            meshCollider.sharedMesh = mesh;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log($"[Kvadr] {go.name}: {Format(old.size)} -> {Format(newSize)} (kolize srovnana). Uloz scenu (Ctrl+S).");
    }

    static string Format(Vector3 v) => $"{v.x:0.##} x {v.y:0.##} x {v.z:0.##} m";
}
