using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

// Rebuilds the held prop from the cleaned OBJ while retaining both original UV/material groups.
public static class HolyRevolverSetup
{
    const string Dir = "Assets/Models/Weapons/HolyRevolver";
    const string PrefabPath = "Assets/Prefab/Weapons/Held_HolyRevolver_v3.prefab";
    const string WeaponPath = "Assets/Data/SindelPistol_Data.asset";

    [MenuItem("BrosOverwatch/Zbrane/Sindel - opravit materialy")]
    public static void RepairMaterials()
    {
        Material("Material.001");
        Material("Material.002");
        AssetDatabase.SaveAssets();
    }

    [MenuItem("BrosOverwatch/Zbrane/Sindel - Holy Revolver")]
    public static void Build()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Dir + "/HolyRevolver.asset");
        var cylinderMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Dir + "/HolyRevolverCylinder.asset");
        var center = new Vector3(0f, -.028f, .134f);
        // OBJ is a local regeneration intermediate. The two ready-to-use meshes ship in Git.
        if (File.Exists(Dir + "/HolyRevolver.obj"))
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new[] { new List<int>(), new List<int>(), new List<int>() };
            int part = 0;
            foreach (var line in File.ReadLines(Dir + "/HolyRevolver.obj"))
            {
                var p = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                if (p[0] == "v") positions.Add(Vector(p));
                else if (p[0] == "vn") normals.Add(Vector(p));
                else if (p[0] == "vt") uv.Add(new Vector2(Number(p[1]), Number(p[2])));
                else if (p[0] == "usemtl") part = p[1] == "Part0" ? 0 : p[1] == "Cylinder" ? 2 : 1;
                else if (p[0] == "f")
                    for (int i = 1; i < p.Length; i++) triangles[part].Add(int.Parse(p[i].Split('/')[0]) - 1);
            }
            if (positions.Count == 0 || normals.Count != positions.Count || uv.Count != positions.Count)
                throw new InvalidDataException("Holy Revolver has missing geometry or UVs.");
            if (mesh == null)
            {
                mesh = new Mesh { name = "Holy Revolver" };
                AssetDatabase.CreateAsset(mesh, Dir + "/HolyRevolver.asset");
            }
            mesh.Clear();
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 2;
            for (int i = 0; i < 2; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            if (cylinderMesh == null)
            {
                cylinderMesh = new Mesh { name = "Holy Revolver Cylinder" };
                AssetDatabase.CreateAsset(cylinderMesh, Dir + "/HolyRevolverCylinder.asset");
            }
            cylinderMesh.Clear();
            var cylinderVertices = new List<Vector3>();
            var cylinderNormals = new List<Vector3>();
            var cylinderUV = new List<Vector2>();
            var cylinderIndices = new List<int>();
            var remap = new Dictionary<int, int>();
            foreach (int index in triangles[2])
            {
                if (!remap.TryGetValue(index, out int mapped))
                {
                    mapped = cylinderVertices.Count;
                    remap.Add(index, mapped);
                    cylinderVertices.Add(positions[index] - center);
                    cylinderNormals.Add(normals[index]);
                    cylinderUV.Add(uv[index]);
                }
                cylinderIndices.Add(mapped);
            }
            cylinderMesh.SetVertices(cylinderVertices);
            cylinderMesh.SetNormals(cylinderNormals);
            cylinderMesh.SetUVs(0, cylinderUV);
            cylinderMesh.SetTriangles(cylinderIndices, 0);
            cylinderMesh.RecalculateTangents();
            cylinderMesh.RecalculateBounds();
            EditorUtility.SetDirty(cylinderMesh);
        }
        else if (mesh == null || cylinderMesh == null)
            throw new FileNotFoundException("Missing revolver meshes. Regenerate the local OBJ with tools/models/prepare_holy_revolver.py.");

        var materials = new[] { Material("Material.001"), Material("Material.002") };
        var root = new GameObject("Held_HolyRevolver_v3");
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterials = materials;
            var presentation = root.AddComponent<RevolverVisual>();
            var cylinder = new GameObject("Cylinder", typeof(MeshFilter), typeof(MeshRenderer));
            cylinder.transform.SetParent(root.transform, false);
            cylinder.transform.localPosition = center;
            cylinder.GetComponent<MeshFilter>().sharedMesh = cylinderMesh;
            cylinder.GetComponent<MeshRenderer>().sharedMaterial = materials[0];
            presentation.cylinder = cylinder.transform;
            presentation.cylinderCenter = center;
            presentation.speedLoader = Cartridges(root.transform, "SpeedLoader", true);
            presentation.spentCases = Cartridges(root.transform, "SpentCases", false);
            presentation.supportPoint = new GameObject("ReloadSupport").transform;
            presentation.supportPoint.SetParent(root.transform, false);
            presentation.SampleReload(1f);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
            if (weapon == null || prefab == null) throw new InvalidOperationException("Missing Sindel weapon or prefab.");
            weapon.heldPrefab = prefab;
            weapon.heldScale = 1f;
            weapon.revolverReload = true;
            EditorUtility.SetDirty(weapon);
            AssetDatabase.SaveAssets();
            Debug.Log($"[HolyRevolver] Assigned to Sindel: {mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles, size {mesh.bounds.size}.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static float Number(string value) => float.Parse(value, CultureInfo.InvariantCulture);
    static Transform Cartridges(Transform parent, string name, bool loader)
    {
        string path = Dir + "/ReloadBrass.mat";
        var brass = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (brass == null)
        {
            brass = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            brass.color = new Color(.65f, .43f, .14f);
            brass.SetFloat("_Metallic", .8f);
            brass.SetFloat("_Smoothness", .55f);
            AssetDatabase.CreateAsset(brass, path);
        }
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        for (int i = 0; i < (loader ? 7 : 6); i++)
        {
            var cartridge = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.DestroyImmediate(cartridge.GetComponent<Collider>());
            cartridge.name = i == 6 ? "LoaderHandle" : "Cartridge";
            cartridge.transform.SetParent(group, false);
            float angle = i * Mathf.PI / 3f;
            cartridge.transform.localPosition = i == 6 ? Vector3.back * .018f : new Vector3(Mathf.Sin(angle) * .012f, Mathf.Cos(angle) * .012f, 0f);
            cartridge.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cartridge.transform.localScale = i == 6 ? new Vector3(.029f, .006f, .029f) : new Vector3(.007f, .011f, .007f);
            cartridge.GetComponent<Renderer>().sharedMaterial = brass;
        }
        group.gameObject.SetActive(false);
        return group;
    }
    static Vector3 Vector(string[] p) => new Vector3(Number(p[1]), Number(p[2]), Number(p[3]));

    static Texture2D Texture(string name, bool linear = false, bool normal = false)
    {
        string path = Dir + "/" + name + ".png";
        if (!File.Exists(path)) path = Dir + "/" + name + ".jpg";
        if (!File.Exists(path)) return null;
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType != type || importer.sRGBTexture == linear || importer.alphaIsTransparency)
        {
            importer.textureType = type;
            importer.sRGBTexture = !linear;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Material(string prefix)
    {
        string path = Dir + "/" + prefix + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        // Material.002 has no albedo in the supplied export: retain its metal/normal/AO maps
        // with a neutral steel base rather than applying the unrelated .001 UV atlas.
        var albedo = Texture(prefix + "_albedo");
        material.SetTexture("_BaseMap", albedo);
        if(material.HasProperty("_MainTex")) material.SetTexture("_MainTex",albedo);
        material.SetColor("_BaseColor", albedo != null ? Color.white : new Color(.68f, .7f, .72f));
        material.SetTexture("_BumpMap", Texture(prefix + "_normal", true, true));
        material.EnableKeyword("_NORMALMAP");
        string maskPath = Dir + "/" + prefix + "_MetallicSmoothness.png";
        // Packed R/A masks are committed; high-resolution source maps stay in the local archive.
        if (File.Exists(Dir + "/" + prefix + "_metallic.png") && File.Exists(Dir + "/" + prefix + "_roughness.png"))
        {
            var metal = Raw(prefix + "_metallic");
            var rough = Raw(prefix + "_roughness");
            if (metal.width != rough.width || metal.height != rough.height)
                throw new InvalidDataException("Metallic and roughness dimensions differ.");
            var pixels = metal.GetPixels32();
            var roughPixels = rough.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(pixels[i].r, 0, 0, (byte)(255 - roughPixels[i].r));
            var mask = new Texture2D(metal.width, metal.height, TextureFormat.RGBA32, false, true);
            mask.SetPixels32(pixels);
            mask.Apply();
            File.WriteAllBytes(maskPath, mask.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(mask);
            UnityEngine.Object.DestroyImmediate(metal);
            UnityEngine.Object.DestroyImmediate(rough);
            AssetDatabase.ImportAsset(maskPath);
        }
        material.SetTexture("_MetallicGlossMap", Texture(prefix + "_MetallicSmoothness", true));
        material.SetFloat("_Smoothness", 1f);
        material.EnableKeyword("_METALLICSPECGLOSSMAP");
        // Keep the emissive Lit variant in player builds. A keyword enabled only on
        // runtime material instances is otherwise stripped while the base material is black.
        // URP validates keywords again on import. Keep a visually negligible
        // authored emission so the charged-shot variant survives player stripping.
        // HeldWeapons sets the instance to black while the shot is not charged.
        material.SetColor("_EmissionColor",new Color(.0001f,.0001f,.0001f,1f));
        material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
        material.EnableKeyword("_EMISSION");
        var ao = Texture(prefix + "_AO", true);
        if (ao != null) material.SetTexture("_OcclusionMap", ao);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Texture2D Raw(string name)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!texture.LoadImage(File.ReadAllBytes(Dir + "/" + name + ".png")))
            throw new InvalidDataException("Cannot load " + name);
        return texture;
    }
}
