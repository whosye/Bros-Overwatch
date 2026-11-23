using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Stylizovana sci-fi zbran ze Sketchfabu (Assets/Models/Weapons/StylizedSciFiGun, KB_GUN_low.fbx + PBR textury)
// jako Sindelova zbran v ruce. Model nema znacky Grip/Muzzle/Top (jako modely ve WeaponModelSetup), takze se
// natoceni odvodi z tvaru: nejdelsi osa = hlaven (+Z), druha nejdelsi = vyska (+Y); kde je predek a kde spodek,
// pozna podle rukojeti (cast, ktera vzadu nejvic visi dolu). Rukojet (vrsek, kde ji svira pest) jde do
// HitscanHand, delka rukojet -> usti = Reach. Pri spatnem otoceni pomuzou polozky "otocit".
// Puvodni model (Held_Pistol_v3) vrati polozka "vratit puvodni".
public static class SciFiGunSetup
{
    const string Dir = "Assets/Models/Weapons/StylizedSciFiGun";
    const string ModelPath = Dir + "/KB_GUN_low.fbx";
    const string BaseColorPath = Dir + "/KB_GUN_low_Default_BaseColor.png";
    const string NormalPath = Dir + "/KB_GUN_low_Default_Normal.png";
    const string MetallicPath = Dir + "/KB_GUN_low_Default_Metallic.png";
    const string RoughnessPath = Dir + "/KB_GUN_low_Default_Roughness.png";
    const string MaskPath = Dir + "/KB_GUN_low_MetallicSmoothness.png";
    const string MaterialPath = Dir + "/KB_GUN.mat";
    const string PrefabPath = "Assets/Prefab/Weapons/Held_SciFiGun.prefab";
    const string OldPrefabPath = "Assets/Prefab/Weapons/Held_Pistol_v3.prefab";
    const string WeaponPath = "Assets/Data/SindelPistol_Data.asset";

    static readonly Vector3 HitscanHand = new Vector3(0f, -0.03f, 0.05f);   // jako WeaponModelSetup
    const float Reach = 0.26f;   // rukojet -> usti (Sindelova puvodni pistole 0,2 m; tahle je mohutnejsi)

    // rucni oprava natoceni (polozky menu "otocit"), pamatuje si ji editor
    const string FlipForwardKey = "BrosOverwatch.SciFiGun.FlipForward";
    const string FlipUpKey = "BrosOverwatch.SciFiGun.FlipUp";

    [MenuItem("BrosOverwatch/Zbrane/Sindel - sci-fi zbran (vytvorit a priradit)")]
    static void Build()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        if (weapon == null) { Debug.LogError("[SciFiGun] Chybi " + WeaponPath); return; }
        if (!SetupImport()) return;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var material = BuildMaterial();
        var prefab = BuildPrefab(model, material);
        if (prefab == null) return;

        Undo.RecordObject(weapon, "Sindel sci-fi zbran");
        weapon.heldPrefab = prefab;
        weapon.heldScale = 1f;
        EditorUtility.SetDirty(weapon);
        AssetDatabase.SaveAssets();
        Debug.Log("[SciFiGun] Sindel ma sci-fi zbran (" + PrefabPath + "). Kdyz je otocena spatne: BrosOverwatch/Zbrane/Sindel - otocit ...");
    }

    [MenuItem("BrosOverwatch/Zbrane/Sindel - otocit sci-fi zbran predkem dozadu")]
    static void FlipForward()
    {
        EditorPrefs.SetBool(FlipForwardKey, !EditorPrefs.GetBool(FlipForwardKey, false));
        Build();
    }

    [MenuItem("BrosOverwatch/Zbrane/Sindel - otocit sci-fi zbran vzhuru nohama")]
    static void FlipUp()
    {
        EditorPrefs.SetBool(FlipUpKey, !EditorPrefs.GetBool(FlipUpKey, false));
        Build();
    }

    [MenuItem("BrosOverwatch/Zbrane/Sindel - vratit puvodni pistoli")]
    static void Restore()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        var old = AssetDatabase.LoadAssetAtPath<GameObject>(OldPrefabPath);
        if (weapon == null || old == null) { Debug.LogError("[SciFiGun] Chybi zbran nebo " + OldPrefabPath); return; }
        Undo.RecordObject(weapon, "Sindel puvodni pistole");
        weapon.heldPrefab = old;
        EditorUtility.SetDirty(weapon);
        AssetDatabase.SaveAssets();
        Debug.Log("[SciFiGun] Sindel ma zase puvodni pistoli.");
    }

    // ---------------- import ----------------

    static bool SetupImport()
    {
        if (!(AssetImporter.GetAtPath(ModelPath) is ModelImporter model))
        {
            Debug.LogError("[SciFiGun] Chybi " + ModelPath);
            return false;
        }
        bool dirty = false;
        if (model.materialImportMode != ModelImporterMaterialImportMode.None) { model.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
        if (!model.isReadable) { model.isReadable = true; dirty = true; }   // tvar se cte z vrcholu
        if (model.importAnimation) { model.importAnimation = false; dirty = true; }
        if (dirty) model.SaveAndReimport();

        SetTexture(BaseColorPath, TextureImporterType.Default, true);
        SetTexture(NormalPath, TextureImporterType.NormalMap, false);
        // metalika + hladkost (1 - drsnost) do jedne textury pro URP Lit (R = metalika, A = hladkost)
        if (!File.Exists(MaskPath)) BuildMask();
        SetTexture(MaskPath, TextureImporterType.Default, false);
        return true;
    }

    static void SetTexture(string path, TextureImporterType type, bool srgb)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
        if (importer.textureType == type && importer.sRGBTexture == srgb) return;
        importer.textureType = type;
        importer.sRGBTexture = srgb;
        importer.SaveAndReimport();
    }

    static void BuildMask()
    {
        var metal = LoadRaw(MetallicPath);
        var rough = LoadRaw(RoughnessPath);
        if (metal == null || rough == null) return;
        int w = metal.width, h = metal.height;
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float m = metal.GetPixel(x, y).r;
                float r = rough.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h).r;
                pixels[y * w + x] = new Color(m, m, m, 1f - r);
            }
        var mask = new Texture2D(w, h, TextureFormat.RGBA32, false);
        mask.SetPixels32(pixels);
        File.WriteAllBytes(MaskPath, mask.EncodeToPNG());
        Object.DestroyImmediate(mask);
        Object.DestroyImmediate(metal);
        Object.DestroyImmediate(rough);
        AssetDatabase.ImportAsset(MaskPath);
    }

    static Texture2D LoadRaw(string path)
    {
        if (!File.Exists(path)) return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        return texture.LoadImage(File.ReadAllBytes(path)) ? texture : null;
    }

    static Material BuildMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(BaseColorPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
        material.SetTexture("_BaseMap", baseMap);
        material.SetColor("_BaseColor", Color.white);
        if (normal != null)
        {
            material.SetTexture("_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
        }
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

    // ---------------- prefab ----------------

    static GameObject BuildPrefab(GameObject model, Material material)
    {
        if (model == null) { Debug.LogError("[SciFiGun] Model se nenacetl."); return null; }
        var root = new GameObject("Held_SciFiGun");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        try
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
            foreach (var collider in instance.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(collider);

            var points = Vertices(instance);
            if (points.Count < 4) { Debug.LogError("[SciFiGun] Model nema vrcholy (neni citelny?)."); return null; }

            // osy podle rozmeru
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) bounds.Encapsulate(p);
            Vector3 size = bounds.size;
            int[] order = { 0, 1, 2 };
            System.Array.Sort(order, (a, b) => size[b].CompareTo(size[a]));
            Vector3 along = Axis(order[0]), height = Axis(order[1]);

            // predek / spodek: rukojet = cast, ktera v jedne polovine delky nejvic visi dolu
            Vector3 c = bounds.center;
            float best = float.MinValue;
            float bestL = 1f, bestH = 1f;
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
            Vector3 forward = along * bestL, up = height * bestH;

            // otoceni do os zbrane
            var rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up));
            var t = instance.transform;
            t.SetPositionAndRotation(rotation * t.position, rotation * t.rotation);
            points = Vertices(instance);

            // rukojet: vrcholy zadni poloviny pod osou hlavne; pest svira jeji vrsek
            bounds = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) bounds.Encapsulate(p);
            c = bounds.center;
            float barrelY = c.y;
            Vector3 gripSum = Vector3.zero;
            int gripCount = 0;
            float lowest = bounds.min.y + (barrelY - bounds.min.y) * 0.5f;
            foreach (var p in points)
                if (p.z < c.z && p.y < lowest) { gripSum += p; gripCount++; }
            Vector3 gripBottom = gripCount > 0 ? gripSum / gripCount : new Vector3(c.x, bounds.min.y, c.z - bounds.extents.z * 0.5f);
            Vector3 grip = new Vector3(c.x, barrelY - (barrelY - bounds.min.y) * 0.25f, gripBottom.z);
            Vector3 muzzle = new Vector3(c.x, barrelY, bounds.max.z);

            float reach = Mathf.Max(0.001f, muzzle.z - grip.z);
            float scale = Reach / reach;
            t.SetPositionAndRotation(t.position * scale, t.rotation);
            t.localScale *= scale;
            t.position += HitscanHand - grip * scale;

            EnsureFolder("Assets/Prefab");
            EnsureFolder("Assets/Prefab/Weapons");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[SciFiGun] Prefab vytvoren: osa hlavne {AxisName(order[0])}{(bestL > 0 ? "+" : "-")}, nahoru {AxisName(order[1])}{(bestH > 0 ? "+" : "-")}, " +
                      $"delka modelu {size[order[0]]:0.###} -> meritko {scale:0.####} (rukojet->usti {Reach} m).");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // vrcholy vsech siti v prostoru korene (root je v pocatku bez otoceni)
    static List<Vector3> Vertices(GameObject instance)
    {
        var list = new List<Vector3>();
        foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = filter.sharedMesh;
            if (mesh == null) continue;
            var vertices = mesh.vertices;
            var matrix = filter.transform.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i += 1)
                list.Add(matrix.MultiplyPoint3x4(vertices[i]));
        }
        foreach (var skinned in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = skinned.sharedMesh;
            if (mesh == null) continue;
            var matrix = skinned.transform.localToWorldMatrix;
            foreach (var v in mesh.vertices) list.Add(matrix.MultiplyPoint3x4(v));
        }
        return list;
    }

    static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
    static string AxisName(int i) => i == 0 ? "X" : i == 1 ? "Y" : "Z";

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
