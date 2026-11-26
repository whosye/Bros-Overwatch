using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Domasov (puvodni mapa Map-Domasov): dira ve strese chaty se zebrikem - zezdola se jde vylezt, ze strechy se ale
// dovnitr nedalo (dira 1,5 x 1,5 m po svahu je na sikme strese vodorovne uzsi a zebrik pod ni zabira cast otvoru,
// hrac ma prumer 1 m). Diry se zvetsi (RoofHoleTool.EnlargeHoles - jen kolik dovoli okraj strechy) a zkontroluje
// se pruchod. Jen jednou (a znovu po zvyseni Version); kopie pro Tlacenici (Map_DomasovPush) se nemeni - prevezme
// to az pri "Tlacenice > Postavit znovu od nuly".
[InitializeOnLoad]
public static class DomasovRoofHoleFix
{
    const string MapRoot = "Map-Domasov";
    const float TargetSize = 2.3f;
    const int Version = 1;
    const string VersionKey = "BrosOverwatch.DomasovRoofHole.Version";

    static DomasovRoofHoleFix()
    {
        EditorApplication.delayCall += AutoRun;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += AutoRun;
        };
    }

    static void AutoRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorPrefs.GetInt(VersionKey, 0) >= Version) return;
        if (Apply()) EditorPrefs.SetInt(VersionKey, Version);
    }

    [MenuItem("BrosOverwatch/Dekorace/Domasov - zvetsit diru ve strese (prolezt zpet dovnitr)")]
    static void Menu() => Apply();

    // false = mapa neni nactena (zkusi se priste)
    static bool Apply()
    {
        GameObject map = null;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == MapRoot) { map = root; break; }
        if (map == null) return false;

        var roofs = map.GetComponentsInChildren<RoofHoles>(true);
        if (roofs.Length == 0) { Debug.Log("[Strecha] V Domasove neni strecha s dirou."); return true; }
        foreach (var roof in roofs)
        {
            string path = roof.name;
            for (var t = roof.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            Debug.Log($"[Strecha] {path}: {RoofHoleTool.EnlargeHoles(roof, TargetSize)}");
        }
        EditorSceneManager.MarkSceneDirty(map.scene);
        Debug.Log("[Strecha] Diry ve strese Domasova upraveny - uloz scenu (Ctrl+S). Tlacenice je prevezme po 'Postavit znovu od nuly'.");
        return true;
    }
}
