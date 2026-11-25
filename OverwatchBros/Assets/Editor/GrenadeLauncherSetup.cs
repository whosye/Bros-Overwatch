using System.IO;
using UnityEditor;
using UnityEngine;

// Honzuv granatomet ze Sketchfabu ("Junkrat's Grenade Launcher", viz Models/Weapons/GrenadeLauncher/CREDITS.txt),
// 13,7k trojuhelniku, textury 1k. GLB importuje glTFast i s materialem. Zlute vicko usti je v Blenderu vyriznute
// jako samostatny dil "MuzzleCap" s pocatkem v pantu (pri vystrelu se odklopi, viz MuzzleCap.cs). Bedna zasobniku
// je dil "Magazine" a granaty v ni "Grenade_1".."Grenade_4" (1 = u hlavne; pri vystrelu se posunou, viz GrenadeFeed.cs).
// Natoceni modelu je dane (hlaven -Z, nahoru +Y) - tvar se tu nehada: krabicovy zasobnik visi skoro jako
// rukojet, takze by se predek s zadkem mohl splest. Pest drzi pistolovou rukojet (stred vrcholu v jejim miste,
// podil rozmeru modelu), delka rukojet -> usti = Reach.
// Prefab se postavi a priradi SAM jednou (a znovu po zvyseni Version); pri spatnem otoceni pomuzou polozky "otocit",
// puvodni zbran vrati "vratit puvodni granatomet".
[InitializeOnLoad]
public static class GrenadeLauncherSetup
{
    const string ModelPath = "Assets/Models/Weapons/GrenadeLauncher/grenade_launcher.glb";
    const string PrefabPath = "Assets/Prefab/Weapons/Held_GrenadeLauncher.prefab";
    const string OldPrefabPath = "Assets/Prefab/Weapons/Held_LightningGun_v3.prefab";
    const string WeaponPath = "Assets/Data/Rocket_Data.asset";

    static readonly Vector3 ProjectileHand = new Vector3(0f, -0.06f, 0.12f);   // jako WeaponModelSetup (projektilove zbrane)
    const float Reach = 0.45f;   // rukojet -> usti (puvodni Honzova zbran 0,4 m); cely granatomet asi 0,7 m

    // hlaven modelu (glTF): -Z; po natoceni +Z
    const bool ModelForwardIsMinusZ = true;
    // rukojet v modelu (podil rozmeru po natoceni: 0 = zadek / spodek, 1 = usti / vrch)
    static readonly Vector2 GripAlong = new Vector2(0.25f, 0.42f);
    static readonly Vector2 GripHeight = new Vector2(0.15f, 0.30f);
    // odklopeni vicka (stupne kolem jeho lokalni osy X; kladne = dopredu nahoru u tohoto modelu)
    const float CapOpenAngle = 120f;

    const int Version = 5;   // 2: odklapeci vicko usti; 3: novy model (Junkrat's Grenade Launcher); 4: bedna a granaty zvlast;
                             // 5: radici packa "CockingLever" zvlast (pocatek dole u tycky) - natahuje se pri prebiti
    const string VersionKey = "BrosOverwatch.GrenadeLauncher.Version";
    const string FlipForwardKey = "BrosOverwatch.GrenadeLauncher.FlipForward";
    const string FlipUpKey = "BrosOverwatch.GrenadeLauncher.FlipUp";

    static GrenadeLauncherSetup()
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

    [MenuItem("BrosOverwatch/Zbrane/Honza - granatomet ze Sketchfabu (vytvorit a priradit)")]
    static void Build()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        if (weapon == null) { Debug.LogError("[Granatomet] Chybi " + WeaponPath); return; }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) { Debug.LogError("[Granatomet] Chybi " + ModelPath + " (glTFast ho jeste nenaimportoval?)"); return; }

        var prefab = BuildPrefab(model);
        if (prefab == null) return;
        EditorPrefs.SetInt(VersionKey, Version);

        Undo.RecordObject(weapon, "Honza granatomet");
        weapon.heldPrefab = prefab;
        weapon.heldScale = 1f;
        EditorUtility.SetDirty(weapon);
        AssetDatabase.SaveAssets();
        Debug.Log("[Granatomet] Honza ma granatomet ze Sketchfabu (" + PrefabPath + "). Kdyz je otoceny spatne: BrosOverwatch/Zbrane/Honza - granatomet otocit ...");
    }

    [MenuItem("BrosOverwatch/Zbrane/Honza - granatomet otocit predkem dozadu")]
    static void FlipForward()
    {
        EditorPrefs.SetBool(FlipForwardKey, !EditorPrefs.GetBool(FlipForwardKey, false));
        Build();
    }

    [MenuItem("BrosOverwatch/Zbrane/Honza - granatomet otocit vzhuru nohama")]
    static void FlipUp()
    {
        EditorPrefs.SetBool(FlipUpKey, !EditorPrefs.GetBool(FlipUpKey, false));
        Build();
    }

    [MenuItem("BrosOverwatch/Zbrane/Honza - vratit puvodni granatomet")]
    static void Restore()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        var old = AssetDatabase.LoadAssetAtPath<GameObject>(OldPrefabPath);
        if (weapon == null || old == null) { Debug.LogError("[Granatomet] Chybi zbran nebo " + OldPrefabPath); return; }
        Undo.RecordObject(weapon, "Honza puvodni granatomet");
        weapon.heldPrefab = old;
        EditorUtility.SetDirty(weapon);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetInt(VersionKey, int.MaxValue);   // (at se hned zase sam neprepne)
        Debug.Log("[Granatomet] Honza ma zase puvodni granatomet.");
    }

    static GameObject BuildPrefab(GameObject model)
    {
        var root = new GameObject("Held_GrenadeLauncher");
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
            // zlute vicko na usti (v Blenderu vyriznute jako samostatny dil, pocatek v pantu) - pri vystrelu se odklopi
            bool capFound = false;
            foreach (var part in instance.GetComponentsInChildren<Transform>(true))
                if (part.name.StartsWith("MuzzleCap") && part.GetComponent<MuzzleCap>() == null)
                {
                    part.gameObject.AddComponent<MuzzleCap>().openAngle = CapOpenAngle;
                    capFound = true;
                    break;
                }
            if (!capFound) Debug.LogWarning("[Granatomet] V modelu chybi dil MuzzleCap - vicko se nebude odklapet.");

            // bedna zasobniku s granaty: granaty jako jeji deti (pozdeji se s ni muzou hybat), posun pri vystrelu
            Transform magazine = null;
            var grenades = new System.Collections.Generic.List<Transform>();
            foreach (var part in instance.GetComponentsInChildren<Transform>(true))
            {
                if (part.name.StartsWith("Magazine") && magazine == null) magazine = part;
                else if (part.name.StartsWith("Grenade_") && !(part.parent != null && part.parent.name.StartsWith("Grenade_")))
                    grenades.Add(part);   // (uzel granatu; pripadne dite se siti ne)
            }
            if (magazine != null && grenades.Count > 0)
            {
                grenades.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                foreach (var grenade in grenades) grenade.SetParent(magazine, true);
                var feed = magazine.gameObject.AddComponent<GrenadeFeed>();
                feed.grenades = grenades.ToArray();
            }
            else Debug.LogWarning("[Granatomet] V modelu chybi dil Magazine nebo Grenade_* - granaty se nebudou posouvat.");
            bool lever = false;
            foreach (var part in instance.GetComponentsInChildren<Transform>(true))
                if (part.name.StartsWith("CockingLever")) { lever = true; break; }
            if (!lever) Debug.LogWarning("[Granatomet] V modelu chybi dil CockingLever - pri prebiti se nebude natahovat.");

            // hlaven modelu dopredu (+Z); polozky "otocit" to jeste prevrati
            var t = instance.transform;
            bool minusZ = ModelForwardIsMinusZ != EditorPrefs.GetBool(FlipForwardKey, false);
            Vector3 forward = minusZ ? Vector3.back : Vector3.forward;
            Vector3 up = EditorPrefs.GetBool(FlipUpKey, false) ? Vector3.down : Vector3.up;
            var rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up));
            t.SetPositionAndRotation(rotation * t.position, rotation * t.rotation);

            var points = FuturisticRifleSetup.Vertices(instance);
            if (points.Count < 4) { Debug.LogError("[Granatomet] Model nema citelne vrcholy."); return null; }
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
            if (count == 0) { Debug.LogError("[Granatomet] Rukojet nenalezena - zkontroluj natoceni."); return null; }
            grip /= count;
            grip.x = bounds.center.x;
            float muzzleZ = bounds.max.z;

            float scale = Reach / Mathf.Max(0.001f, muzzleZ - grip.z);
            t.SetPositionAndRotation(t.position * scale, t.rotation);
            t.localScale *= scale;
            t.position += ProjectileHand - grip * scale;

            FuturisticRifleSetup.EnsureFolder("Assets/Prefab");
            FuturisticRifleSetup.EnsureFolder("Assets/Prefab/Weapons");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Granatomet] Prefab vytvoren: meritko {scale:0.#####}, delka {size.z * scale:0.##} m, vyska {size.y * scale:0.##} m, " +
                      $"rukojet z {count} vrcholu.");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
