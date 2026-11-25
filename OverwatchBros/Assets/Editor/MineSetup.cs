using System.IO;
using UnityEditor;
using UnityEngine;

// Honzova mina ze Sketchfabu: "Mine" od Vikking (CC-BY, viz Models/Weapons/Mine/CREDITS.txt). GLB importuje glTFast
// i s materialem (svitici prstenec). Prefab jde do Resources/Weapons/HonzaMine: prumer jako mina ve hre
// (MineAbility, 36 cm), spodek v pocatku a kopule nahoru (+Y) - prilepena mina pak sedi na povrchu.
// Pouziva ho MineAbility (hozena / prilepena mina) a HeldWeapons (v leve ruce pri hodu); kdyz chybi, zustane mina z kostek.
// Prefab se postavi SAM jednou (a znovu po zvyseni Version).
[InitializeOnLoad]
public static class MineSetup
{
    const string ModelPath = "Assets/Models/Weapons/Mine/mine.glb";
    const string PrefabPath = "Assets/Resources/Weapons/HonzaMine.prefab";
    const float Diameter = 0.36f;   // = MineAbility.Size

    const int Version = 1;
    const string VersionKey = "BrosOverwatch.HonzaMine.Version";

    static MineSetup()
    {
        EditorApplication.delayCall += AutoBuild;
        // skript se casto nacte az pri vstupu do Play (tam se nestavi) - postavit hned po navratu do editoru
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += AutoBuild;
        };
    }

    // (skript se muze nacist driv, nez glTFast naimportuje model - pak az po jeho importu)
    class ImportWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
                if (path == ModelPath) { EditorApplication.delayCall += AutoBuild; return; }
        }
    }

    static void AutoBuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(PrefabPath) && EditorPrefs.GetInt(VersionKey, 0) >= Version) return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null) return;
        Build();
    }

    [MenuItem("BrosOverwatch/Zbrane/Honza - mina ze Sketchfabu (vytvorit)")]
    static void Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) { Debug.LogError("[Mina] Chybi " + ModelPath + " (glTFast ho jeste nenaimportoval?)"); return; }

        var root = new GameObject("HonzaMine");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
            foreach (var animation in instance.GetComponentsInChildren<Animation>(true))
                Object.DestroyImmediate(animation);

            var points = FuturisticRifleSetup.Vertices(instance);
            if (points.Count < 4) { Debug.LogError("[Mina] Model nema citelne vrcholy."); return; }
            var bounds = FuturisticRifleSetup.Bounds(points);
            float width = Mathf.Max(bounds.size.x, bounds.size.z);
            float scale = Diameter / Mathf.Max(0.0001f, width);

            var t = instance.transform;
            t.SetPositionAndRotation(t.position * scale, t.rotation);
            t.localScale *= scale;
            t.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;

            FuturisticRifleSetup.EnsureFolder("Assets/Resources");
            FuturisticRifleSetup.EnsureFolder("Assets/Resources/Weapons");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            EditorPrefs.SetInt(VersionKey, Version);
            Debug.Log($"[Mina] Prefab vytvoren ({PrefabPath}): prumer {Diameter} m, vyska {bounds.size.y * scale:0.###} m.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
