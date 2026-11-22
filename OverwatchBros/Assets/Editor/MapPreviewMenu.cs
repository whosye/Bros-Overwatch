using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Prepinac nahledu map v editoru: BrosOverwatch > Nahled mapy > (mapa). Zapne vybranou mapu, ostatni vypne
// a namiri na ni okno Scene. Zaskrtnuta je mapa, ktera je prave videt.
// Scena se vzdy uklada (a Play se spousti) s vychozim stavem - zapnuty Domasov, ostatni mapy vypnute: na to
// spolehaji hra (MatchManager, traktor, spawn stodoly) i nastroje Domasova. Po ulozeni / po Play se nahled vrati.
// Ve hre si mapy prepina MatchManager.ApplyMap sam - tenhle prepinac je jen pro praci v editoru.
[InitializeOnLoad]
public static class MapPreviewMenu
{
    // poradi jako MatchManager.MapNames (indexy map)
    static readonly string[] Roots = { "Map-Domasov", "Map_Hanamura", "Map_Dust2", "Map_DomasovPush", "Map_Sapa" };
    const string MenuPath = "BrosOverwatch/Nahled mapy/";
    const string PreviewKey = "BrosOverwatch.MapPreview";

    static MapPreviewMenu()
    {
        EditorSceneManager.sceneSaving += (scene, path) => Defaults();
        EditorSceneManager.sceneSaved += scene => EditorApplication.delayCall += Reapply;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Defaults();
            else if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Reapply;
        };
    }

    [MenuItem(MenuPath + "Domasov", false, 1)] static void Domasov() => Show(MatchManager.MapDomasov);
    [MenuItem(MenuPath + "Hanamura", false, 2)] static void Hanamura() => Show(MatchManager.MapHanamura);
    [MenuItem(MenuPath + "Dust II", false, 3)] static void Dust2() => Show(MatchManager.MapDust2);
    [MenuItem(MenuPath + "Domasov (Tlacenice)", false, 4)] static void Push() => Show(MatchManager.MapDomasovPush);
    [MenuItem(MenuPath + "Sapa", false, 5)] static void Sapa() => Show(MatchManager.MapSapa);

    [MenuItem(MenuPath + "Domasov", true)] static bool DomasovCheck() => Check(MatchManager.MapDomasov);
    [MenuItem(MenuPath + "Hanamura", true)] static bool HanamuraCheck() => Check(MatchManager.MapHanamura);
    [MenuItem(MenuPath + "Dust II", true)] static bool Dust2Check() => Check(MatchManager.MapDust2);
    [MenuItem(MenuPath + "Domasov (Tlacenice)", true)] static bool PushCheck() => Check(MatchManager.MapDomasovPush);
    [MenuItem(MenuPath + "Sapa", true)] static bool SapaCheck() => Check(MatchManager.MapSapa);

    // Zaskrtnuti u mapy, ktera je videt; mapa, ktera ve scene neni (jeste nepostavena), je sediva.
    static bool Check(int map)
    {
        Menu.SetChecked(MenuPath + MenuName(map), Current() == map);
        return !EditorApplication.isPlayingOrWillChangePlaymode && FindRoot(Roots[map]) != null;
    }

    static string MenuName(int map) => map == MatchManager.MapSapa ? "Sapa" : map == MatchManager.MapDomasovPush ? "Domasov (Tlacenice)" : map == MatchManager.MapDust2 ? "Dust II" : map == MatchManager.MapHanamura ? "Hanamura" : "Domasov";

    static void Show(int map)
    {
        var root = FindRoot(Roots[map]);
        if (root == null)
        {
            Debug.LogWarning($"[Nahled mapy] {MatchManager.MapNames[map]} ve scene neni (jeste nepostavena).");
            return;
        }
        Activate(map);
        SessionState.SetInt(PreviewKey, map);

        // okno Scene na mapu (shora sikmo, cela mapa)
        var view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            Bounds bounds = MapBounds(root);
            view.LookAt(bounds.center, Quaternion.Euler(55f, 0f, 0f), Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z) * 0.6f, 40f, 260f));
        }
        Debug.Log($"[Nahled mapy] Zobrazena mapa {MatchManager.MapNames[map]}. Scena se ulozi s vychozim stavem (Domasov), nahled zustane.");
    }

    // Zapnout jen mapu 'map' (ostatni koreny map vypnout).
    static void Activate(int map)
    {
        for (int i = 0; i < Roots.Length; i++)
        {
            if (i == map) continue;
            var root = FindRoot(Roots[i]);
            if (root != null && root.activeSelf) root.SetActive(false);
        }
        var selected = FindRoot(Roots[map]);
        if (selected != null && !selected.activeSelf) selected.SetActive(true);
        if (map == MatchManager.MapSapa)
            FindRoot(Roots[map])?.GetComponent<MapEnvironment>()?.Apply();
    }

    static int Current()
    {
        for (int i = 0; i < Roots.Length; i++)
        {
            var root = FindRoot(Roots[i]);
            if (root != null && root.activeSelf && i != MatchManager.MapDomasov) return i;
        }
        return MatchManager.MapDomasov;
    }

    // Vychozi stav sceny (ukladani, Play): Domasov zapnuty, ostatni mapy vypnute.
    static void Defaults()
    {
        if (Current() == MatchManager.MapDomasov && FindRoot(Roots[0]) != null && FindRoot(Roots[0]).activeSelf) return;
        Activate(MatchManager.MapDomasov);
    }

    // Po ulozeni / po Play zase ukazat mapu, ktera byla v nahledu.
    static void Reapply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        int map = SessionState.GetInt(PreviewKey, MatchManager.MapDomasov);
        if (map <= MatchManager.MapDomasov || map >= Roots.Length || FindRoot(Roots[map]) == null) return;
        Activate(map);
    }

    static Bounds MapBounds(GameObject root)
    {
        bool any = false;
        var bounds = new Bounds(root.transform.position, Vector3.one * 50f);
        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (renderer.bounds.size.sqrMagnitude > 1e6f) continue;   // obri pozadi / nebe
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    static GameObject FindRoot(string name)
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == name) return go;
        return null;
    }
}
