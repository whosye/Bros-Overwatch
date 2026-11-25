using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Honzuv detonator ze Sketchfabu: "First high poly" od iskandar1ch (CC-BY, viz Models/Weapons/Detonator/CREDITS.txt),
// v Blenderu zmenseny na 6k trojuhelniku a prebarveny. GLB importuje glTFast i s materialy.
// Prefab jde do Resources/Weapons/HonzaDetonator: vysoky 20 cm, nahoru +Y, predek (spoust, antena) +Z,
// pocatek ve stredu tela v miste, kde ho drzi pest (HeldWeapons.PlayDetonate). Prefab se postavi SAM jednou.
[InitializeOnLoad]
public static class DetonatorSetup
{
    const string ModelPath = "Assets/Models/Weapons/Detonator/detonator.glb";
    const string PrefabPath = "Assets/Resources/Weapons/HonzaDetonator.prefab";
    const float Height = 0.2f;
    // uchop: pas tela v teto vysce (podil vysky modelu)
    static readonly Vector2 GripHeight = new Vector2(0.3f, 0.55f);

    const int Version = 1;
    const string VersionKey = "BrosOverwatch.HonzaDetonator.Version";

    static DetonatorSetup()
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

    [MenuItem("BrosOverwatch/Zbrane/Honza - detonator ze Sketchfabu (vytvorit)")]
    static void Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) { Debug.LogError("[Detonator] Chybi " + ModelPath + " (glTFast ho jeste nenaimportoval?)"); return; }

        var root = new GameObject("HonzaDetonator");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
            foreach (var animation in instance.GetComponentsInChildren<Animation>(true))
                Object.DestroyImmediate(animation);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var points = FuturisticRifleSetup.Vertices(instance);
            if (points.Count < 4) { Debug.LogError("[Detonator] Model nema citelne vrcholy."); return; }
            var bounds = FuturisticRifleSetup.Bounds(points);
            float scale = Height / Mathf.Max(0.0001f, bounds.size.y);

            // uchop: median vrcholu v pasu tela (spoust vepredu a dratky ho tak nevychyli)
            var xs = new List<float>();
            var zs = new List<float>();
            foreach (var p in points)
            {
                float h = (p.y - bounds.min.y) / bounds.size.y;
                if (h > GripHeight.x && h < GripHeight.y) { xs.Add(p.x); zs.Add(p.z); }
            }
            xs.Sort(); zs.Sort();
            Vector3 grip = new Vector3(xs.Count > 0 ? xs[xs.Count / 2] : bounds.center.x,
                bounds.min.y + bounds.size.y * (GripHeight.x + GripHeight.y) * 0.5f,
                zs.Count > 0 ? zs[zs.Count / 2] : bounds.center.z);

            var t = instance.transform;
            t.SetPositionAndRotation(t.position * scale, t.rotation);
            t.localScale *= scale;
            t.position -= grip * scale;

            FuturisticRifleSetup.EnsureFolder("Assets/Resources");
            FuturisticRifleSetup.EnsureFolder("Assets/Resources/Weapons");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            EditorPrefs.SetInt(VersionKey, Version);
            Debug.Log($"[Detonator] Prefab vytvoren ({PrefabPath}): vyska {Height} m, hloubka {bounds.size.z * scale:0.###} m.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
