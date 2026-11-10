using UnityEditor;
using UnityEngine;

// Nahled Mirkovy ultimatky (smrst) v okne Scene bez spousteni hry: postavi se pred kameru okna Scene, bokem
// (leti zleva doprava), plna velikost, prstence se toci. Da se obchazet a zoomovat jako jiny objekt.
// Do sceny se neuklada. Stejny prikaz nahled zase schova (zmizi i pri spusteni hry).
public static class StormPreview
{
    static StormVisual preview;

    [MenuItem("BrosOverwatch/Nahled - Mirkova ultimatka (smrst) v okne Scene")]
    static void Toggle()
    {
        if (preview != null) { Remove(); return; }
        var view = SceneView.lastActiveSceneView;
        if (view == null) { Debug.LogWarning("[Nahled] Otevri okno Scene."); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[Nahled] Jen mimo hru."); return; }

        var ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/Data/Storm_Data.asset");
        float radius = ability != null ? ability.radius : 6f;
        Transform cam = view.camera.transform;
        Vector3 side = cam.right;
        side.y = 0f;
        side = side.sqrMagnitude > 0.01f ? side.normalized : Vector3.right;
        float length = radius * StormAbility.LengthFactor;
        Vector3 start = view.pivot - side * length * 0.5f;   // stred tela v miste, kam se okno diva

        var go = new GameObject("NahledSmrsti") { hideFlags = HideFlags.DontSave };
        go.transform.position = start;
        preview = go.AddComponent<StormVisual>();
        var mirek = Resources.Load<HeroDefinition>("Heroes/Mirek");
        preview.Init(side, 0f, 9999f, radius, UiKit.TeamColor(0), mirek != null ? mirek.characterPrefab : null);
        preview.PreviewTick(0f);
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;

        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayMode;
        Selection.activeGameObject = go;
        view.Frame(new Bounds(view.pivot, Vector3.one * length * 1.3f), false);
        Debug.Log("[Nahled] Smrst v okne Scene (modra = tym 0). Prohlizej ji mysi; zpet stejnym prikazem.");
    }

    static void Tick()
    {
        if (preview == null) { Remove(); return; }
        preview.PreviewTick(0.016f);
        SceneView.RepaintAll();
    }

    static void OnPlayMode(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode) Remove();
    }

    static void Remove()
    {
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        if (preview != null) Object.DestroyImmediate(preview.gameObject);
        preview = null;
    }
}
