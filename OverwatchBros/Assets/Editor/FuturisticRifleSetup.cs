using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Davidova (Anna) puska ze Sketchfabu: "Futuristic Rifle" od Andre Lages (CC-BY, viz Models/Weapons/FuturisticRifle/CREDITS.txt).
// GLB importuje glTFast i s materialy (pouziji se, jak jsou). Model ma navic cernou obrysovou slupku "rifleToon"
// (toon obrys - v Unity by zakryla celou pusku), ta se z prefabu vyhodi.
// Natoceni se odvodi z tvaru (jako SciFiGunSetup): nejdelsi osa = hlaven (+Z), druha = vyska (+Y); predek/spodek
// podle pistolove rukojeti (vzadu nejvic visi dolu). Pest drzi vrsek rukojeti, delka rukojet -> usti = Reach.
// Prefab se postavi a priradi SAM jednou (kdyz jeste neexistuje); pri spatnem otoceni pomuzou polozky "otocit",
// puvodni pusku vrati "vratit puvodni pusku".
[InitializeOnLoad]
public static class FuturisticRifleSetup
{
    const string ModelPath = "Assets/Models/Weapons/FuturisticRifle/futuristic_rifle.glb";
    const string PrefabPath = "Assets/Prefab/Weapons/Held_FuturisticRifle.prefab";
    const string OldPrefabPath = "Assets/Prefab/Weapons/Held_LongPistol_v3.prefab";
    const string WeaponPath = "Assets/Data/AnnaRifle_Data.asset";

    static readonly Vector3 ProjectileHand = new Vector3(0f, -0.06f, 0.12f);   // jako WeaponModelSetup (projektilove zbrane)
    const float Reach = 0.42f;   // rukojet -> usti (puvodni Davidova puska 0,5 m, Viktorova 0,62 m; 0,6 byla moc dlouha)

    // zvysit pri zmene stavby - prefab se pak jednou postavi znovu
    const int Version = 3;   // 3: znacka Foregrip (leva ruka v prvni osobe)
    const string VersionKey = "BrosOverwatch.FuturisticRifle.Version";

    const string FlipForwardKey = "BrosOverwatch.FuturisticRifle.FlipForward";
    const string FlipUpKey = "BrosOverwatch.FuturisticRifle.FlipUp";

    static FuturisticRifleSetup()
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

    // jednou: prefab jeste neni a model uz je naimportovany
    static void AutoBuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(PrefabPath) && EditorPrefs.GetInt(VersionKey, 1) >= Version) return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null) return;
        Build();
    }

    [MenuItem("BrosOverwatch/Zbrane/David - futuristicka puska (vytvorit a priradit)")]
    static void Build()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        if (weapon == null) { Debug.LogError("[Puska] Chybi " + WeaponPath); return; }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) { Debug.LogError("[Puska] Chybi " + ModelPath + " (glTFast ho jeste nenaimportoval?)"); return; }

        var prefab = BuildPrefab(model);
        if (prefab == null) return;
        EditorPrefs.SetInt(VersionKey, Version);

        Undo.RecordObject(weapon, "David futuristicka puska");
        weapon.heldPrefab = prefab;
        weapon.heldScale = 1f;
        EditorUtility.SetDirty(weapon);
        AssetDatabase.SaveAssets();
        Debug.Log("[Puska] David ma futuristickou pusku (" + PrefabPath + "). Kdyz je otocena spatne: BrosOverwatch/Zbrane/David - otocit ...");
    }

    [MenuItem("BrosOverwatch/Zbrane/David - otocit pusku predkem dozadu")]
    static void FlipForward()
    {
        EditorPrefs.SetBool(FlipForwardKey, !EditorPrefs.GetBool(FlipForwardKey, false));
        Build();
    }

    [MenuItem("BrosOverwatch/Zbrane/David - otocit pusku vzhuru nohama")]
    static void FlipUp()
    {
        EditorPrefs.SetBool(FlipUpKey, !EditorPrefs.GetBool(FlipUpKey, false));
        Build();
    }

    [MenuItem("BrosOverwatch/Zbrane/David - vratit puvodni pusku")]
    static void Restore()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        var old = AssetDatabase.LoadAssetAtPath<GameObject>(OldPrefabPath);
        if (weapon == null || old == null) { Debug.LogError("[Puska] Chybi zbran nebo " + OldPrefabPath); return; }
        Undo.RecordObject(weapon, "David puvodni puska");
        weapon.heldPrefab = old;
        EditorUtility.SetDirty(weapon);
        AssetDatabase.SaveAssets();
        Debug.Log("[Puska] David ma zase puvodni pusku.");
    }

    static GameObject BuildPrefab(GameObject model)
    {
        var root = new GameObject("Held_FuturisticRifle");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // obrysova slupka pryc (cerna kopie modelu o kousek vetsi)
            var outlines = new List<GameObject>();
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                if (IsOutline(renderer)) outlines.Add(renderer.gameObject);
            foreach (var go in outlines) Object.DestroyImmediate(go);

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
            foreach (var animation in instance.GetComponentsInChildren<Animation>(true))
                Object.DestroyImmediate(animation);

            var points = Vertices(instance);
            if (points.Count < 4) { Debug.LogError("[Puska] Model nema citelne vrcholy."); return null; }

            var bounds = Bounds(points);
            Vector3 size = bounds.size;
            int[] order = { 0, 1, 2 };
            System.Array.Sort(order, (a, b) => size[b].CompareTo(size[a]));
            Vector3 along = Axis(order[0]), height = Axis(order[1]);

            // predek / spodek: pistolova rukojet = cast, ktera v jedne polovine delky nejvic visi dolu
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
            points = Vertices(instance);
            bounds = Bounds(points);
            c = bounds.center;

            // rukojet: nejnizsi bod (spodek pistolove rukojeti); jeji poloha po delce = prumer vrcholu kolem nej
            // (jen spodni pas vysky a blizko po delce - pazba, ktera taky trochu visi, se nepocita)
            Vector3 lowest = points[0];
            foreach (var p in points) if (p.y < lowest.y) lowest = p;
            float band = size[order[1]] * 0.12f, near = size[order[0]] * 0.1f;
            float gripZ = 0f;
            int gripCount = 0;
            foreach (var p in points)
                if (p.y < lowest.y + band && Mathf.Abs(p.z - lowest.z) < near) { gripZ += p.z; gripCount++; }
            gripZ = gripCount > 0 ? gripZ / gripCount : lowest.z;
            if (gripZ > c.z) Debug.LogWarning("[Puska] Rukojet vysla v predni polovine - zkontroluj natoceni (polozky 'otocit').");

            float barrelY = c.y;
            Vector3 grip = new Vector3(c.x, barrelY - (barrelY - bounds.min.y) * 0.25f, gripZ);
            float muzzleZ = bounds.max.z;

            float reach = Mathf.Max(0.001f, muzzleZ - grip.z);
            float scale = Reach / reach;
            t.SetPositionAndRotation(t.position * scale, t.rotation);
            t.localScale *= scale;
            t.position += ProjectileHand - grip * scale;

            // Znacka "Foregrip": spodek predpazbi (stred zespodu) tam, kde ji v prvni osobe podpira leva dlan
            // (HeldWeapons ji priklada presne sem). Misto po delce jako HeldWeapons.ForegripInWeapon.
            float foreZ = ProjectileHand.z + Mathf.Clamp(Reach * 0.45f, 0.15f, 0.32f);
            points = Vertices(instance);
            float bottom = float.MaxValue;
            foreach (var p in points)
                if (Mathf.Abs(p.z - foreZ) < 0.015f && p.y < bottom) bottom = p.y;
            if (bottom < float.MaxValue)
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                foreach (var p in points)
                    if (Mathf.Abs(p.z - foreZ) < 0.015f && p.y < bottom + 0.02f) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); }
                var marker = new GameObject("Foregrip").transform;
                marker.SetParent(root.transform, false);
                marker.localPosition = new Vector3((minX + maxX) * 0.5f, bottom, foreZ);
                Debug.Log($"[Puska] Predpazbi: spodek {bottom:0.###} m, sirka {maxX - minX:0.###} m (z {foreZ:0.###}).");
            }

            EnsureFolder("Assets/Prefab");
            EnsureFolder("Assets/Prefab/Weapons");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Puska] Prefab vytvoren: vynechano obrysu {outlines.Count}, osa hlavne {AxisName(order[0])}{(bestL > 0 ? "+" : "-")}, " +
                      $"nahoru {AxisName(order[1])}{(bestH > 0 ? "+" : "-")}, meritko {scale:0.####}, celkova delka {size[order[0]] * scale:0.##} m.");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static bool IsOutline(Renderer renderer)
    {
        if (renderer.name.IndexOf("Toon", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        foreach (var m in renderer.sharedMaterials)
            if (m != null && m.name.IndexOf("Toon", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // vrcholy vsech siti v prostoru korene (root je v pocatku bez otoceni); cte i site bez Read/Write (v editoru)
    internal static List<Vector3> Vertices(GameObject instance)
    {
        var list = new List<Vector3>();
        foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            Add(list, filter.sharedMesh, filter.transform.localToWorldMatrix);
        foreach (var skinned in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            Add(list, skinned.sharedMesh, skinned.transform.localToWorldMatrix);
        return list;
    }

    static void Add(List<Vector3> list, Mesh mesh, Matrix4x4 matrix)
    {
        if (mesh == null) return;
        using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
        {
            var vertices = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp);
            data[0].GetVertices(vertices);
            foreach (var v in vertices) list.Add(matrix.MultiplyPoint3x4(v));
            vertices.Dispose();
        }
    }

    internal static Bounds Bounds(List<Vector3> points)
    {
        var b = new Bounds(points[0], Vector3.zero);
        foreach (var p in points) b.Encapsulate(p);
        return b;
    }

    static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
    static string AxisName(int i) => i == 0 ? "X" : i == 1 ? "Y" : "Z";

    internal static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
