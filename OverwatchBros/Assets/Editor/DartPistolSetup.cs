using System.IO;
using UnityEditor;
using UnityEngine;

// Davidova uspavaci pistolka (Shift) ze Sketchfabu: "Dart Pistol" od GeoGianni (CC-BY, viz Models/Weapons/DartPistol/CREDITS.txt).
// GLB importuje glTFast i s materialy (jen barvy, bez textur). Prefab jde do Resources/Weapons/DartPistol - HeldWeapons
// si ho nacte misto pistolky skladane z kostek (kdyz chybi, zustane ta z kostek).
// Natoceni z tvaru (jako FuturisticRifleSetup): nejdelsi osa = hlaven (+Z), druha = vyska (+Y), rukojet vzadu visi dolu.
// Pest drzi rukojet asi tretinu vysky pod vrchem (v bode HeldWeapons.DartGrip), delka rukojet -> usti = Reach.
// Prefab se postavi SAM jednou (a znovu po zvyseni Version); pri spatnem otoceni pomuzou polozky "otocit".
[InitializeOnLoad]
public static class DartPistolSetup
{
    const string ModelPath = "Assets/Models/Weapons/DartPistol/dart_pistol.glb";
    const string PrefabPath = "Assets/Resources/Weapons/DartPistol.prefab";

    static readonly Vector3 DartGrip = new Vector3(0f, -0.06f, 0.02f);   // = HeldWeapons.DartGrip (kde ji drzi leva ruka)
    const float Reach = 0.27f;   // rukojet -> usti (i s tlumicem); cela pistolka asi 35 cm

    const int Version = 1;
    const string VersionKey = "BrosOverwatch.DartPistol.Version";
    const string FlipForwardKey = "BrosOverwatch.DartPistol.FlipForward";
    const string FlipUpKey = "BrosOverwatch.DartPistol.FlipUp";

    static DartPistolSetup()
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

    [MenuItem("BrosOverwatch/Zbrane/David - uspavaci pistolka (vytvorit)")]
    static void Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) { Debug.LogError("[Pistolka] Chybi " + ModelPath + " (glTFast ho jeste nenaimportoval?)"); return; }
        if (BuildPrefab(model) == null) return;
        EditorPrefs.SetInt(VersionKey, Version);
        Debug.Log("[Pistolka] David ma uspavaci pistolku ze Sketchfabu (" + PrefabPath + "). Kdyz je otocena spatne: BrosOverwatch/Zbrane/David - pistolka otocit ...");
    }

    [MenuItem("BrosOverwatch/Zbrane/David - pistolka otocit predkem dozadu")]
    static void FlipForward()
    {
        EditorPrefs.SetBool(FlipForwardKey, !EditorPrefs.GetBool(FlipForwardKey, false));
        Build();
    }

    [MenuItem("BrosOverwatch/Zbrane/David - pistolka otocit vzhuru nohama")]
    static void FlipUp()
    {
        EditorPrefs.SetBool(FlipUpKey, !EditorPrefs.GetBool(FlipUpKey, false));
        Build();
    }

    [MenuItem("BrosOverwatch/Zbrane/David - vratit pistolku z kostek")]
    static void Restore()
    {
        if (AssetDatabase.DeleteAsset(PrefabPath)) Debug.Log("[Pistolka] Prefab smazan - David ma zase pistolku z kostek.");
        EditorPrefs.SetInt(VersionKey, int.MaxValue);   // (at se hned zase sama nepostavi)
    }

    static GameObject BuildPrefab(GameObject model)
    {
        var root = new GameObject("DartPistol");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
            foreach (var animation in instance.GetComponentsInChildren<Animation>(true))
                Object.DestroyImmediate(animation);

            var points = FuturisticRifleSetup.Vertices(instance);
            if (points.Count < 4) { Debug.LogError("[Pistolka] Model nema citelne vrcholy."); return null; }

            var bounds = FuturisticRifleSetup.Bounds(points);
            Vector3 size = bounds.size;
            int[] order = { 0, 1, 2 };
            System.Array.Sort(order, (a, b) => size[b].CompareTo(size[a]));
            Vector3 along = Axis(order[0]), height = Axis(order[1]);

            // predek / spodek: rukojet = cast, ktera v jedne polovine delky nejvic visi dolu
            Vector3 c = bounds.center;
            float best = float.MinValue, bestL = 1f, bestH = 1f;
            foreach (float sl in new[] { 1f, -1f })
                foreach (float sh in new[] { 1f, -1f })
                {
                    float rearDown = float.MinValue, frontDown = float.MinValue;
                    foreach (var p in points)
                    {
                        float l = Vector3.Dot(p - c, along) * sl;
                        float down = -Vector3.Dot(p - c, height) * sh;
                        if (l < 0f) rearDown = Mathf.Max(rearDown, down);
                        else frontDown = Mathf.Max(frontDown, down);
                    }
                    float tail = rearDown - frontDown;
                    if (tail > best) { best = tail; bestL = sl; bestH = sh; }
                }
            if (EditorPrefs.GetBool(FlipForwardKey, false)) bestL = -bestL;
            if (EditorPrefs.GetBool(FlipUpKey, false)) bestH = -bestH;

            var rotation = Quaternion.Inverse(Quaternion.LookRotation(along * bestL, height * bestH));
            var t = instance.transform;
            t.SetPositionAndRotation(rotation * t.position, rotation * t.rotation);
            points = FuturisticRifleSetup.Vertices(instance);
            bounds = FuturisticRifleSetup.Bounds(points);
            size = bounds.size;

            // rukojet: vodorovny pas asi tretinu vysky pod vrchem; v nem nejzadnejsi cast (spoust a lucik jsou vepredu)
            float gripY = bounds.max.y - size.y * 0.35f;
            float slab = size.y * 0.04f;
            float rearZ = float.MaxValue;
            foreach (var p in points)
                if (Mathf.Abs(p.y - gripY) < slab) rearZ = Mathf.Min(rearZ, p.z);
            float gripZ = 0f;
            int count = 0;
            foreach (var p in points)
                if (Mathf.Abs(p.y - gripY) < slab && p.z < rearZ + size.z * 0.09f) { gripZ += p.z; count++; }
            gripZ = count > 0 ? gripZ / count : bounds.min.z + size.z * 0.2f;
            Vector3 grip = new Vector3(bounds.center.x, gripY, gripZ);
            float muzzleZ = bounds.max.z;

            float scale = Reach / Mathf.Max(0.001f, muzzleZ - grip.z);
            t.SetPositionAndRotation(t.position * scale, t.rotation);
            t.localScale *= scale;
            t.position += DartGrip - grip * scale;

            FuturisticRifleSetup.EnsureFolder("Assets/Resources");
            FuturisticRifleSetup.EnsureFolder("Assets/Resources/Weapons");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Pistolka] Prefab vytvoren: osa hlavne {AxisName(order[0])}{(bestL > 0 ? "+" : "-")}, nahoru {AxisName(order[1])}{(bestH > 0 ? "+" : "-")}, " +
                      $"meritko {scale:0.####}, celkova delka {size.z * scale:0.##} m.");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
    static string AxisName(int i) => i == 0 ? "X" : i == 1 ? "Y" : "Z";
}
