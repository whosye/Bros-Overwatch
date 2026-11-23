using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Maxova kosa ze Sketchfabu (Assets/Models/Weapons/Scythe, CC-BY Ovesis - viz CREDITS.txt) misto kosy skladane v kodu.
// Prefab se srovna do stejnych os jako kosa z kodu (HeldWeapons.BuildScythe): nasada svisle (+Y) od -0,55 do 0,95 m,
// ruka drzi bod (0; 0,12; 0), cepel nahore vybiha dopredu (+Z). Osy se odvodi z tvaru: nejdelsi osa = nasada,
// konec, kde model nejvic vycniva do strany = cepel (nahoru), smer, kam tam vycniva = dopredu.
// Spatne otoceni opravi polozky "otocit"; puvodni kosu z kodu vrati "vratit kosu z kodu".
public static class ScytheSetup
{
    const string Dir = "Assets/Models/Weapons/Scythe";
    const string ModelPath = Dir + "/Scythe.fbx";
    const string BaseColorPath = Dir + "/Scythe_BaseColor.jpg";
    const string NormalPath = Dir + "/Scythe_Normal.jpg";
    const string MaskPath = Dir + "/Scythe_MetallicSmoothness.png";
    const string MaterialPath = Dir + "/Scythe.mat";
    const string PrefabPath = "Assets/Prefab/Weapons/Held_Scythe.prefab";
    const string WeaponPath = "Assets/Data/Scythe_Data.asset";

    const float Bottom = -0.55f, Top = 0.95f;   // jako HeldWeapons.BuildScythe

    const string FlipBladeKey = "BrosOverwatch.Scythe.FlipBlade";     // cepel dozadu
    const string FlipEndKey = "BrosOverwatch.Scythe.FlipEnd";         // cepel dole
    const string SpinKey = "BrosOverwatch.Scythe.Spin";               // otocit kolem nasady o 90 st.

    [MenuItem("BrosOverwatch/Zbrane/Max - kosa ze Sketchfabu (vytvorit a priradit)")]
    static void Build()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        if (weapon == null) { Debug.LogError("[Kosa] Chybi " + WeaponPath); return; }
        if (!(AssetImporter.GetAtPath(ModelPath) is ModelImporter importer)) { Debug.LogError("[Kosa] Chybi " + ModelPath); return; }
        bool dirty = false;
        if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
        if (!importer.isReadable) { importer.isReadable = true; dirty = true; }
        if (importer.importAnimation) { importer.importAnimation = false; dirty = true; }
        if (dirty) importer.SaveAndReimport();
        SetTexture(BaseColorPath, TextureImporterType.Default, true);
        SetTexture(NormalPath, TextureImporterType.NormalMap, false);
        SetTexture(MaskPath, TextureImporterType.Default, false);

        var prefab = BuildPrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), BuildMaterial());
        if (prefab == null) return;
        Undo.RecordObject(weapon, "Max kosa");
        weapon.heldPrefab = prefab;
        weapon.heldScale = 1f;
        EditorUtility.SetDirty(weapon);
        AssetDatabase.SaveAssets();
        Debug.Log("[Kosa] Max ma kosu ze Sketchfabu (" + PrefabPath + ").");
    }

    [MenuItem("BrosOverwatch/Zbrane/Max - kosa: cepel na druhou stranu")]
    static void FlipBlade() { Toggle(FlipBladeKey); Build(); }

    [MenuItem("BrosOverwatch/Zbrane/Max - kosa: vzhuru nohama (cepel dole)")]
    static void FlipEnd() { Toggle(FlipEndKey); Build(); }

    [MenuItem("BrosOverwatch/Zbrane/Max - kosa: otocit kolem nasady o 90 st")]
    static void Spin() { EditorPrefs.SetInt(SpinKey, (EditorPrefs.GetInt(SpinKey, 0) + 1) % 4); Build(); }

    [MenuItem("BrosOverwatch/Zbrane/Max - vratit kosu z kodu")]
    static void Restore()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        if (weapon == null) return;
        Undo.RecordObject(weapon, "Max kosa z kodu");
        weapon.heldPrefab = null;
        EditorUtility.SetDirty(weapon);
        AssetDatabase.SaveAssets();
        Debug.Log("[Kosa] Max ma zase kosu skladanou v kodu.");
    }

    static void Toggle(string key) => EditorPrefs.SetBool(key, !EditorPrefs.GetBool(key, false));

    static void SetTexture(string path, TextureImporterType type, bool srgb)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter t)) return;
        if (t.textureType == type && t.sRGBTexture == srgb) return;
        t.textureType = type;
        t.sRGBTexture = srgb;
        t.SaveAndReimport();
    }

    static Material BuildMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BaseColorPath));
        material.SetColor("_BaseColor", Color.white);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        if (normal != null) { material.SetTexture("_BumpMap", normal); material.EnableKeyword("_NORMALMAP"); }
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
        if (mask != null)
        {
            material.SetTexture("_MetallicGlossMap", mask);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static GameObject BuildPrefab(GameObject model, Material material)
    {
        if (model == null) { Debug.LogError("[Kosa] Model se nenacetl."); return null; }
        var root = new GameObject("Held_Scythe");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        try
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                renderer.sharedMaterials = mats;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (var collider in instance.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(collider);

            var points = Vertices(instance);
            if (points.Count < 4) { Debug.LogError("[Kosa] Model nema citelne vrcholy."); return null; }
            var bounds = Bounds(points);

            // nasada = nejdelsi osa
            Vector3 size = bounds.size;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            Vector3 shaft = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

            // osa nasady (stred z dolni poloviny, kde je jen nasada) a konec s cepeli (vic vycniva do strany)
            float mid = Vector3.Dot(bounds.center, shaft);
            Vector3 center = Vector3.zero;
            int count = 0;
            float spreadMin = 0f, spreadMax = 0f;
            foreach (var p in points)
            {
                float along = Vector3.Dot(p, shaft);
                if (Mathf.Abs(along - mid) < size[axis] * 0.3f) { center += p - shaft * along; count++; }
            }
            center = count > 0 ? center / count : bounds.center - shaft * mid;
            foreach (var p in points)
            {
                float along = Vector3.Dot(p, shaft);
                float side = (p - shaft * along - center).magnitude;
                if (along > mid) spreadMax = Mathf.Max(spreadMax, side);
                else spreadMin = Mathf.Max(spreadMin, side);
            }
            Vector3 up = spreadMax >= spreadMin ? shaft : -shaft;
            if (EditorPrefs.GetBool(FlipEndKey, false)) up = -up;

            // smer cepele: kam nejvic vycniva v horni ctvrtine
            float upTop = float.MinValue, upBottom = float.MaxValue;
            foreach (var p in points) { float a = Vector3.Dot(p, up); upTop = Mathf.Max(upTop, a); upBottom = Mathf.Min(upBottom, a); }
            Vector3 blade = Vector3.zero;
            float bladeReach = 0f;
            foreach (var p in points)
            {
                if (Vector3.Dot(p, up) < upTop - (upTop - upBottom) * 0.25f) continue;
                Vector3 off = Vector3.ProjectOnPlane(p - center, up);
                if (off.magnitude > bladeReach) { bladeReach = off.magnitude; blade = off; }
            }
            if (blade.sqrMagnitude < 1e-6f) blade = Vector3.Cross(up, Vector3.right).sqrMagnitude > 1e-4f ? Vector3.Cross(up, Vector3.right) : Vector3.forward;
            // na hlavni osu (stabilnejsi), otoceni / prevraceni podle voleb
            blade = Snap(blade.normalized, up);
            blade = Quaternion.AngleAxis(90f * EditorPrefs.GetInt(SpinKey, 0), up) * blade;
            if (EditorPrefs.GetBool(FlipBladeKey, false)) blade = -blade;

            var rotation = Quaternion.Inverse(Quaternion.LookRotation(blade, up));
            var t = instance.transform;
            t.SetPositionAndRotation(rotation * t.position, rotation * t.rotation);

            // velikost: cela vyska Bottom..Top, osa nasady v x = z = 0
            points = Vertices(instance);
            bounds = Bounds(points);
            float scale = (Top - Bottom) / Mathf.Max(0.001f, bounds.size.y);
            t.SetPositionAndRotation(t.position * scale, t.rotation);
            t.localScale *= scale;
            points = Vertices(instance);
            bounds = Bounds(points);
            Vector3 axisCenter = Vector3.zero;
            count = 0;
            foreach (var p in points)
                if (p.y < bounds.center.y) { axisCenter += new Vector3(p.x, 0f, p.z); count++; }
            axisCenter = count > 0 ? axisCenter / count : new Vector3(bounds.center.x, 0f, bounds.center.z);
            t.position += new Vector3(-axisCenter.x, Bottom - bounds.min.y, -axisCenter.z);

            EnsureFolder("Assets/Prefab");
            EnsureFolder("Assets/Prefab/Weapons");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[Kosa] Prefab vytvoren: nasada podel {"XYZ"[axis]}, delka modelu {size[axis]:0.###} -> meritko {scale:0.####}. " +
                      "Kdyz je cepel spatne: BrosOverwatch/Zbrane/Max - kosa: ...");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static Vector3 Snap(Vector3 v, Vector3 up)
    {
        Vector3 best = v;
        float bestDot = -1f;
        foreach (var a in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
        {
            if (Mathf.Abs(Vector3.Dot(a, up)) > 0.5f) continue;
            float d = Vector3.Dot(a, v);
            if (d > bestDot) { bestDot = d; best = a; }
        }
        return bestDot > 0.7f ? best : Vector3.ProjectOnPlane(v, up).normalized;
    }

    static Bounds Bounds(List<Vector3> points)
    {
        var b = new Bounds(points[0], Vector3.zero);
        foreach (var p in points) b.Encapsulate(p);
        return b;
    }

    static List<Vector3> Vertices(GameObject instance)
    {
        var list = new List<Vector3>();
        foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            var m = filter.transform.localToWorldMatrix;
            foreach (var v in filter.sharedMesh.vertices) list.Add(m.MultiplyPoint3x4(v));
        }
        foreach (var skinned in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (skinned.sharedMesh == null) continue;
            var m = skinned.transform.localToWorldMatrix;
            foreach (var v in skinned.sharedMesh.vertices) list.Add(m.MultiplyPoint3x4(v));
        }
        return list;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
