using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Rucni vkladani nabytku z Kenney Furniture Kitu: v okne Scene se podivej na misto na podlaze (stred okna)
// a v menu BrosOverwatch > Mapa > Vlozit ... se tam model postavi (celem ke kamere, s kolizi).
// Pak ho muzes posunout a otocit sam. Jde vratit pres Ctrl+Z.
public static class FurnitureTools
{
    const string KitFolder = "Assets/Models/Kenney/FurnitureKit/";

    [MenuItem("BrosOverwatch/Mapa/Vlozit zachod (kam miri okno Scene)")]
    static void PlaceToilet()
    {
        var toilet = Place("toilet", "Zachod", 0.8f);
        if (toilet != null) toilet.AddComponent<ToiletSound>();   // zvuk pri vstupu do mistnosti
    }

    [MenuItem("BrosOverwatch/Mapa/Vlozit vanu (kam miri okno Scene)")]
    static void PlaceBathtub() => Place("bathtub", "Vana", 0.6f);

    [MenuItem("BrosOverwatch/Mapa/Vlozit umyvadlo (kam miri okno Scene)")]
    static void PlaceSink() => Place("bathroomSink", "Umyvadlo", 0.9f);

    static GameObject Place(string model, string name, float height)
    {
        var view = SceneView.lastActiveSceneView;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KitFolder + model + ".glb");
        if (view == null || prefab == null)
        {
            Debug.LogWarning($"[Nabytek] Chybi okno Scene nebo model {KitFolder}{model}.glb.");
            return null;
        }

        // misto: kam miri stred okna Scene
        var cam = view.camera.transform;
        Physics.SyncTransforms();
        if (!Physics.Raycast(cam.position, cam.forward, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore))
        {
            Debug.LogWarning("[Nabytek] Stred okna Scene nemiri na zadny povrch.");
            return null;
        }
        Vector3 foot = hit.point;
        if (hit.normal.y < 0.7f && Physics.Raycast(hit.point + hit.normal * 0.4f, Vector3.down, out var floor, 10f, ~0, QueryTriggerInteraction.Ignore))
            foot = floor.point;   // mireno na zed: postavit na podlahu pred ni

        var map = GameObject.Find("Map-Domasov");
        Transform parent = null;
        if (map != null)
        {
            parent = map.transform.Find("HlavniChata/Interier/Nabytek");
            if (parent == null)
            {
                var interior = map.transform.Find("HlavniChata/Interier");
                var group = new GameObject("Nabytek");
                Undo.RegisterCreatedObjectUndo(group, "Vlozit nabytek");
                group.transform.SetParent(interior != null ? interior : map.transform, false);
                parent = group.transform;
            }
        }

        var root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, "Vlozit nabytek");
        if (parent != null) root.transform.SetParent(parent, false);
        root.transform.position = foot;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.SetParent(root.transform, false);
        Bounds b = BoundsOf(root);
        if (b.size.y > 0.0001f) instance.transform.localScale *= height / b.size.y;
        b = BoundsOf(root);
        instance.transform.position += new Vector3(foot.x - b.center.x, foot.y - b.min.y, foot.z - b.center.z);
        b = BoundsOf(root);

        var box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(b.center);
        box.size = b.size;

        // celem ke kamere (otoceni jen kolem svisle osy)
        Vector3 toCamera = cam.position - foot;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude > 0.01f)
            root.transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"[Nabytek] {name} vlozen na {foot}. Posun/otoc ho podle potreby a uloz scenu (Ctrl+S).");
        return root;
    }

    static Bounds BoundsOf(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }
}
