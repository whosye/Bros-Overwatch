using System.IO;
using UnityEditor;
using UnityEngine;

// Testovaci nastroj: vypise rendery/materialy avatara a ulozi pro kazdy mesh mapu "UV texel -> pozice + normala".
public static class AvatarDump
{
    const int Size = 2048;

    public static void Setup()
    {
        AyranAvatarSetup.Setup();
        AssetDatabase.SaveAssets();
        Debug.Log("[AvatarDump] setup ready=" + AyranAvatarSetup.IsReady());
    }

    public static void Run()
    {
        AyranAvatarSetup.Setup();
        AssetDatabase.SaveAssets();

        string outDir = "C:/Users/budha/ows_dump";
        Directory.CreateDirectory(outDir);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Honza/HonzaAvatar.fbx");
        var go = Object.Instantiate(model);
        var log = new System.Text.StringBuilder();

        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var l2w = smr.transform.localToWorldMatrix;
            var verts = baked.vertices;
            var normals = baked.normals;
            var uvs = smr.sharedMesh.uv;
            var bounds = new Bounds(l2w.MultiplyPoint3x4(verts[0]), Vector3.zero);
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = l2w.MultiplyPoint3x4(verts[i]);
                normals[i] = l2w.MultiplyVector(normals[i]).normalized;
                bounds.Encapsulate(verts[i]);
            }

            log.AppendLine($"MESH {smr.name} verts={verts.Length} submeshes={smr.sharedMesh.subMeshCount} boundsMin={bounds.min:F3} boundsMax={bounds.max:F3}");
            foreach (var mat in smr.sharedMaterials)
            {
                var tex = mat != null ? mat.mainTexture : null;
                log.AppendLine($"   MAT {(mat != null ? mat.name : "null")} shader={(mat != null ? mat.shader.name : "")} tex={(tex != null ? AssetDatabase.GetAssetPath(tex) : "none")} color={(mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor").ToString() : "")}");
            }

            var data = new float[Size * Size * 6];
            for (int i = 0; i < data.Length; i++) data[i] = float.NaN;

            var tris = smr.sharedMesh.triangles;
            for (int t = 0; t < tris.Length; t += 3)
                Raster(data, uvs[tris[t]], uvs[tris[t + 1]], uvs[tris[t + 2]],
                    verts[tris[t]], verts[tris[t + 1]], verts[tris[t + 2]],
                    normals[tris[t]], normals[tris[t + 1]], normals[tris[t + 2]]);

            var bytes = new byte[data.Length * 4];
            System.Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes($"{outDir}/{smr.name}.bin", bytes);
        }

        File.WriteAllText(outDir + "/info.txt", log.ToString());
        Object.DestroyImmediate(go);
        Debug.Log("[AvatarDump] done\n" + log);
    }

    static void Raster(float[] data, Vector2 a, Vector2 b, Vector2 c, Vector3 pa, Vector3 pb, Vector3 pc, Vector3 na, Vector3 nb, Vector3 nc)
    {
        a *= Size; b *= Size; c *= Size;
        float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        if (Mathf.Abs(area) < 1e-9f) return;

        int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))) - 2, 0, Size - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))) + 2, 0, Size - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))) - 2, 0, Size - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))) + 2, 0, Size - 1);

        // Tolerance ~1.5 texelu za hranou trojuhelniku, aby nevznikaly svy.
        float edge = Mathf.Max((a - b).magnitude, Mathf.Max((b - c).magnitude, (c - a).magnitude));
        float eps = 1.5f * edge / Mathf.Abs(area);

        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            float px = x + 0.5f, py = y + 0.5f;
            float w0 = ((b.x - px) * (c.y - py) - (b.y - py) * (c.x - px)) / area;
            float w1 = ((c.x - px) * (a.y - py) - (c.y - py) * (a.x - px)) / area;
            float w2 = 1f - w0 - w1;
            if (w0 < -eps || w1 < -eps || w2 < -eps) continue;

            bool inside = w0 >= 0f && w1 >= 0f && w2 >= 0f;
            int i = (y * Size + x) * 6;
            if (!inside && !float.IsNaN(data[i])) continue;

            Vector3 p = pa * w0 + pb * w1 + pc * w2;
            Vector3 n = (na * w0 + nb * w1 + nc * w2).normalized;
            data[i] = p.x; data[i + 1] = p.y; data[i + 2] = p.z;
            data[i + 3] = n.x; data[i + 4] = n.y; data[i + 5] = n.z;
        }
    }
}
