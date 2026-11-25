using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Viktorova puska ze Sketchfabu: "M4A1 With Grenade Launcher" od ARIA (CC-BY-NC, viz Models/Weapons/M4A1/CREDITS.txt).
// GLB importuje glTFast i s materialy. Model je uz natoceny jako zbran (hlaven +Z, nahoru +Y), dily ma zvlast
// (zasobnik M4A1_Magazine001, hlaven granatometu M203_Barrel...).
// Prefab: pest drzi pistolovou rukojet (stred vrcholu v jejim miste, podil rozmeru modelu) v bode HitscanHand,
// delka rukojet -> usti = Reach. Znacky: "Muzzle" (usti pusky, zablesk pri vystrelu), "GrenadeMuzzle" (usti M203,
// odtud leti granat), "Foregrip" (spodek hlavne M203, kde ji podpira leva ruka - je pod M203_Barrel, takze pri
// prebiti granatometu jede ruka s hlavni). Prefab se postavi a priradi SAM jednou (a znovu po zvyseni Version).
[InitializeOnLoad]
public static class M4Setup
{
    const string ModelPath = "Assets/Models/Weapons/M4A1/m4a1.glb";
    const string PrefabPath = "Assets/Prefab/Weapons/Held_M4A1.prefab";
    const string OldPrefabPath = "Assets/Prefab/Weapons/Held_Rifle_v3.prefab";
    const string WeaponPath = "Assets/Data/Pistol_Data.asset";

    // stred pistolove rukojeti jde presne do bodu dlane (HeldWeapons.GripInWeapon pro hitscan zbrane) - pest ji pak
    // obemyka (u modelu z WeaponModelSetup je znacka Grip na vrsku rukojeti o 4 cm vys)
    static readonly Vector3 HitscanHand = new Vector3(0f, -0.07f, 0.05f);
    const float Reach = 0.62f;   // rukojet -> usti (jako puvodni Viktorova puska)
    const float ForegripAhead = 0.36f;   // leva ruka pod hlavni M203 (m pred rukojeti)

    // pistolova rukojet (podil rozmeru modelu: 0 = pazba / spodek, 1 = usti / vrch)
    static readonly Vector2 GripAlong = new Vector2(0.27f, 0.34f);
    static readonly Vector2 GripHeight = new Vector2(0.22f, 0.40f);

    const int Version = 2;   // 2: stred rukojeti v dlani (predtim o 4 cm vys - ruka visela pod rukojeti)
    const string VersionKey = "BrosOverwatch.M4A1.Version";

    static M4Setup()
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

    [MenuItem("BrosOverwatch/Zbrane/Viktor - M4A1 s granatometem (vytvorit a priradit)")]
    static void Build()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        if (weapon == null) { Debug.LogError("[M4A1] Chybi " + WeaponPath); return; }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) { Debug.LogError("[M4A1] Chybi " + ModelPath + " (glTFast ho jeste nenaimportoval?)"); return; }

        var prefab = BuildPrefab(model);
        if (prefab == null) return;
        EditorPrefs.SetInt(VersionKey, Version);

        Undo.RecordObject(weapon, "Viktor M4A1");
        weapon.heldPrefab = prefab;
        weapon.heldScale = 1f;
        EditorUtility.SetDirty(weapon);
        AssetDatabase.SaveAssets();
        Debug.Log("[M4A1] Viktor ma M4A1 s granatometem (" + PrefabPath + ").");
    }

    [MenuItem("BrosOverwatch/Zbrane/Viktor - vratit puvodni pusku")]
    static void Restore()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        var old = AssetDatabase.LoadAssetAtPath<GameObject>(OldPrefabPath);
        if (weapon == null || old == null) { Debug.LogError("[M4A1] Chybi zbran nebo " + OldPrefabPath); return; }
        Undo.RecordObject(weapon, "Viktor puvodni puska");
        weapon.heldPrefab = old;
        EditorUtility.SetDirty(weapon);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetInt(VersionKey, int.MaxValue);   // (at se hned zase sama neprepne)
        Debug.Log("[M4A1] Viktor ma zase puvodni pusku.");
    }

    static GameObject BuildPrefab(GameObject model)
    {
        var root = new GameObject("Held_M4A1");
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
            if (points.Count < 4) { Debug.LogError("[M4A1] Model nema citelne vrcholy."); return null; }
            var bounds = FuturisticRifleSetup.Bounds(points);
            Vector3 min = bounds.min, size = bounds.size;

            // pistolova rukojet: stred vrcholu v jejim miste
            Vector3 grip = Vector3.zero;
            int count = 0;
            foreach (var p in points)
            {
                float along = (p.z - min.z) / size.z, height = (p.y - min.y) / size.y;
                if (along > GripAlong.x && along < GripAlong.y && height > GripHeight.x && height < GripHeight.y) { grip += p; count++; }
            }
            if (count == 0) { Debug.LogError("[M4A1] Rukojet nenalezena."); return null; }
            grip /= count;
            grip.x = bounds.center.x;

            var body = Find(instance.transform, "M44A1_Body001");
            var launcher = Find(instance.transform, "M203_Barrel");
            Vector3 muzzle = Front(body != null ? body : instance.transform, out float muzzleZ);

            float scale = Reach / Mathf.Max(0.001f, muzzleZ - grip.z);
            var t = instance.transform;
            t.SetPositionAndRotation(t.position * scale, t.rotation);
            t.localScale *= scale;
            t.position += HitscanHand - grip * scale;

            // znacky (po zmenseni a posunu)
            muzzle = Front(body != null ? body : instance.transform, out _);
            AddMarker(root.transform, "Muzzle", muzzle).gameObject.AddComponent<MuzzleFlash>();
            if (launcher != null)
            {
                Vector3 launcherMuzzle = Front(launcher, out _);
                var flash = AddMarker(root.transform, "GrenadeMuzzle", launcherMuzzle).gameObject.AddComponent<MuzzleFlash>();
                flash.grenade = true;
                flash.size = 0.24f;

                // leva ruka: spodek hlavne M203 kousek pred rukojeti
                float foreZ = HitscanHand.z + ForegripAhead;
                var barrel = FuturisticRifleSetup.Vertices(launcher.gameObject);
                float bottom = float.MaxValue, minX = float.MaxValue, maxX = float.MinValue;
                foreach (var p in barrel)
                    if (Mathf.Abs(p.z - foreZ) < 0.02f && p.y < bottom) bottom = p.y;
                foreach (var p in barrel)
                    if (Mathf.Abs(p.z - foreZ) < 0.02f && p.y < bottom + 0.02f) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); }
                if (bottom < float.MaxValue)
                {
                    var foregrip = AddMarker(root.transform, "Foregrip", new Vector3((minX + maxX) * 0.5f, bottom, foreZ));
                    foregrip.SetParent(launcher, true);   // jede s hlavni granatometu
                }
                else Debug.LogWarning("[M4A1] Spodek hlavne M203 nenalezen - leva ruka bude podle odhadu.");
            }
            else Debug.LogWarning("[M4A1] V modelu chybi M203_Barrel.");

            FuturisticRifleSetup.EnsureFolder("Assets/Prefab");
            FuturisticRifleSetup.EnsureFolder("Assets/Prefab/Weapons");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[M4A1] Prefab vytvoren: meritko {scale:0.#####}, delka {size.z * scale:0.##} m, usti {muzzle}.");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // Predni konec dilu (stred vrcholu v poslednich 4 mm po delce) v prostoru korene.
    static Vector3 Front(Transform part, out float frontZ)
    {
        var points = FuturisticRifleSetup.Vertices(part.gameObject);
        frontZ = float.MinValue;
        foreach (var p in points) frontZ = Mathf.Max(frontZ, p.z);
        float band = Mathf.Max(0.004f, (frontZ - Mathf.Min(frontZ, MinZ(points))) * 0.004f);
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var p in points)
            if (p.z > frontZ - band) { sum += p; count++; }
        return count > 0 ? new Vector3(sum.x / count, sum.y / count, frontZ) : new Vector3(0f, 0f, frontZ);
    }

    static float MinZ(List<Vector3> points)
    {
        float m = float.MaxValue;
        foreach (var p in points) m = Mathf.Min(m, p.z);
        return m;
    }

    static Transform AddMarker(Transform root, string name, Vector3 position)
    {
        var marker = new GameObject(name).transform;
        marker.SetParent(root, false);
        marker.position = position;
        marker.rotation = Quaternion.identity;
        return marker;
    }
}
