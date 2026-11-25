using System.IO;
using UnityEditor;
using UnityEngine;

// Honzova medvedi past ze Sketchfabu (viz Models/Weapons/BearTrap/CREDITS.txt), v Blenderu zmensena na 9k trojuhelniku,
// prebarvena a rozdelena na zaklad (TrapBase) a dve celisti (JawFront / JawBack, pocatek na ose pantu, zaviraji se
// otocenim kolem lokalni osy X - viz TrapAbility). GLB importuje glTFast i s materialy.
// Prefab jde do Resources/Weapons/HonzaTrap: sirka (podel hridele) 1, spodek v pocatku, stred uprostred;
// TrapAbility ho zvetsi podle polomeru pasti. Prefab se postavi SAM jednou (a znovu po zvyseni Version).
[InitializeOnLoad]
public static class TrapSetup
{
    const string ModelPath = "Assets/Models/Weapons/BearTrap/bear_trap.glb";
    const string PrefabPath = "Assets/Resources/Weapons/HonzaTrap.prefab";

    const int Version = 1;
    const string VersionKey = "BrosOverwatch.HonzaTrap.Version";

    static TrapSetup()
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

    [MenuItem("BrosOverwatch/Zbrane/Honza - medvedi past ze Sketchfabu (vytvorit)")]
    static void Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) { Debug.LogError("[Past] Chybi " + ModelPath + " (glTFast ho jeste nenaimportoval?)"); return; }

        var root = new GameObject("HonzaTrap");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
            foreach (var animation in instance.GetComponentsInChildren<Animation>(true))
                Object.DestroyImmediate(animation);

            int jaws = 0;
            foreach (var part in instance.GetComponentsInChildren<Transform>(true))
                if (part.name.StartsWith("JawFront") || part.name.StartsWith("JawBack")) jaws++;
            if (jaws < 2) Debug.LogWarning("[Past] V modelu chybi celisti JawFront / JawBack - past se nebude zavirat.");

            var points = FuturisticRifleSetup.Vertices(instance);
            if (points.Count < 4) { Debug.LogError("[Past] Model nema citelne vrcholy."); return; }
            var bounds = FuturisticRifleSetup.Bounds(points);
            float scale = 1f / Mathf.Max(0.0001f, bounds.size.x);

            var t = instance.transform;
            t.SetPositionAndRotation(t.position * scale, t.rotation);
            t.localScale *= scale;
            t.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;

            FuturisticRifleSetup.EnsureFolder("Assets/Resources");
            FuturisticRifleSetup.EnsureFolder("Assets/Resources/Weapons");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            EditorPrefs.SetInt(VersionKey, Version);
            Debug.Log($"[Past] Prefab vytvoren ({PrefabPath}): sirka 1, hloubka {bounds.size.z * scale:0.##}, vyska {bounds.size.y * scale:0.##}.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
