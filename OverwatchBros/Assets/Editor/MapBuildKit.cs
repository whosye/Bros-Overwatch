using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Pomocne funkce pro stavbu dekoraci mapy z editorovych skriptu (rozhledna, zahrada).
// Kvadry maji UV v metrech, takze textury maji vsude stejne meritko (1 opakovani = 1 m * tiling materialu).
public static class MapBuildKit
{
    const string MeshFolder = "Assets/Models/Garden/Meshes";

    // ---------------- materialy ----------------

    public static Material Mat(string path, string texturePath, Color tint, Vector2 tiling, float smoothness = 0.1f,
        bool alphaClip = false, bool doubleSided = false, Color? emission = null)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        Texture2D albedo = null, normal = null;
        if (!string.IsNullOrEmpty(texturePath))
        {
            if (alphaClip && AssetImporter.GetAtPath(texturePath) is TextureImporter alphaImporter && !alphaImporter.alphaIsTransparency)
            {
                alphaImporter.alphaIsTransparency = true;
                alphaImporter.SaveAndReimport();
            }

            albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (albedo == null) return null;   // textura se jeste neimportovala, zkusi se priste

            string normalPath = texturePath.Replace(".png", "_Normal.png");
            if (AssetImporter.GetAtPath(normalPath) is TextureImporter importer)
            {
                if (importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.SaveAndReimport();
                }
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            }
        }

        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        material = new Material(shader);
        material.SetColor("_BaseColor", tint);
        material.color = tint;
        if (albedo != null)
        {
            material.SetTexture("_BaseMap", albedo);
            material.mainTexture = albedo;
            material.SetTextureScale("_BaseMap", tiling);
            material.mainTextureScale = tiling;
        }
        if (normal != null)
        {
            material.SetTexture("_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
        }
        material.SetFloat("_Smoothness", smoothness);

        if (alphaClip)
        {
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        if (doubleSided)
        {
            material.SetFloat("_Cull", 0f);
            material.doubleSidedGI = true;
        }
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    public static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }

    // ---------------- poloha mapy ve scene ----------------

    // Tunel a zahrada jsou navrzene k zadni zdi rozhledny (Rozhledna/Cube (5)), ktera v puvodni scene konci na x = -24.61, z = 93.
    // Vraci, o kolik je zed v otevrene scene posunuta (kdyz rozhlednu nekdo presune, tunel a zahrada se posunou s ni).
    public static Vector3 TowerShift(Transform map)
    {
        var wall = map.Find("Rozhledna/Cube (5)");
        var renderer = wall != null ? wall.GetComponent<Renderer>() : null;
        if (renderer == null) return Vector3.zero;

        Bounds b = renderer.bounds;
        return new Vector3(b.max.x + 24.61f, 0f, b.max.z - 93f);
    }

    // Obalka puvodni podlahy mapy (funguje i kdyz je Ground vypnuty - pocita se z meshe a transformu).
    public static Bounds GroundBounds(Transform map)
    {
        var ground = map.Find("Ground");
        var filter = ground != null ? ground.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null) return new Bounds(Vector3.zero, Vector3.one * 100f);

        return WorldBounds(filter.sharedMesh.bounds, ground.localToWorldMatrix);
    }

    public static Bounds WorldBounds(Bounds local, Matrix4x4 toWorld)
    {
        var result = new Bounds(toWorld.MultiplyPoint3x4(local.min), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            result.Encapsulate(toWorld.MultiplyPoint3x4(corner));
        }
        return result;
    }

    // Zapise skutecne polohy dulezitych objektu mapy do Temp/BrosMapDump.txt (pro ladeni rozmisteni).
    public static void DumpLive(Transform map)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Map " + map.position + " shift " + TowerShift(map) + " ground " + GroundBounds(map));
        foreach (var t in map.GetComponentsInChildren<Transform>(true))
        {
            if (t.parent != map && t.parent != null && t.parent.parent != map) continue;
            var r = t.GetComponent<Renderer>();
            sb.AppendLine($"{t.name} | parent {t.parent?.name} | pos {t.position} | {(r != null ? "bounds " + r.bounds.min + " - " + r.bounds.max : "")}");
        }
        System.IO.File.WriteAllText("Temp/BrosMapDump.txt", sb.ToString());
    }

    // ---------------- objekty ----------------

    public static Transform Group(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) return existing;

        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        return group;
    }

    // Kvadr se stredem 'center' (svet), rozmery 'size' (lokalne), natocenim 'rotation'.
    // grainAlongU: delsi strana kazde steny jde podel u (prkna, sindele); jinak podel v (tramy - leta podel delky).
    public static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation,
        Material material, bool collider, bool grainAlongU = true, bool castShadows = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(center, rotation);
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size, grainAlongU);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        if (!castShadows)
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (collider)
            go.AddComponent<BoxCollider>().size = size;

        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    public static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material, bool collider, bool grainAlongU = true)
    {
        return Box(parent, name, center, size, Quaternion.identity, material, collider, grainAlongU);
    }

    // Tram mezi dvema body (svet), ctvercovy prurez 'thickness'.
    public static GameObject Beam(Transform parent, string name, Vector3 from, Vector3 to, float thickness, Material material, bool collider = false)
    {
        Vector3 along = to - from;
        Vector3 up = Mathf.Abs(Vector3.Dot(along.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
        var rotation = Quaternion.LookRotation(along.normalized, up);
        return Box(parent, name, (from + to) * 0.5f, new Vector3(thickness, thickness, along.magnitude), rotation, material, collider, false);
    }

    public static GameObject MeshObject(Transform parent, string name, Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale,
        Material material, bool castShadows = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        if (!castShadows)
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    // ---------------- meshe ----------------

    public static Mesh BoxMesh(Vector3 size, bool grainAlongU)
    {
        var mesh = new Mesh { name = $"Box_{size.x:0.##}x{size.y:0.##}x{size.z:0.##}" };
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        Vector3 h = size * 0.5f;

        void Face(Vector3 normal, Vector3 a, Vector3 b, float la, float lb)
        {
            // a, b = osy steny (jednotkove), la, lb = jejich delky.
            bool swap = grainAlongU ? lb > la : la > lb;
            int start = vertices.Count;
            Vector3 c = Vector3.Scale(normal, h);
            for (int i = 0; i < 4; i++)
            {
                float sa = (i == 1 || i == 2) ? 1f : -1f;
                float sb = (i >= 2) ? 1f : -1f;
                vertices.Add(c + a * (sa * la * 0.5f) + b * (sb * lb * 0.5f));
                normals.Add(normal);
                float u = (sa * 0.5f + 0.5f) * la, v = (sb * 0.5f + 0.5f) * lb;
                uvs.Add(swap ? new Vector2(v, u) : new Vector2(u, v));
            }
            // Smer vinuti podle normaly.
            // (Unity: predni strana je ta, kam miri Cross(v1 - v0, v2 - v0).)
            if (Vector3.Dot(Vector3.Cross(a, b), normal) > 0f)
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            else
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }

        Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
        Face(Vector3.left, Vector3.forward, Vector3.up, size.z, size.y);
        Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
        Face(Vector3.down, Vector3.right, Vector3.forward, size.x, size.z);
        Face(Vector3.forward, Vector3.right, Vector3.up, size.x, size.y);
        Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    // Oboustranny trojuhelnik / ctyruhelnik (stit, vlajka) - body ve svete, UV v metrech (nebo zadane).
    public static Mesh FlatMesh(string name, Vector3[] points, Vector2[] uv)
    {
        var mesh = new Mesh { name = name };
        int n = points.Length;
        var vertices = new Vector3[n * 2];
        var uvs = new Vector2[n * 2];
        for (int i = 0; i < n; i++)
        {
            vertices[i] = vertices[i + n] = points[i];
            uvs[i] = uvs[i + n] = uv[i];
        }
        var triangles = new List<int>();
        for (int i = 1; i < n - 1; i++)
        {
            triangles.AddRange(new[] { 0, i, i + 1 });
            triangles.AddRange(new[] { n, n + i + 1, n + i });
        }
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    // Nizkopolygonova "koule" s nahodne zvlnenym povrchem (koruny stromu, kere, kvety). Ulozi se jako asset.
    public static Mesh BlobMesh(int seed)
    {
        string path = $"{MeshFolder}/Blob{seed}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        var random = new System.Random(seed * 7919 + 13);
        Ico(2, out var points, out var faces);

        // Zvlneni: nekolik nahodnych "boulí".
        var bumps = new Vector3[6];
        for (int i = 0; i < bumps.Length; i++)
            bumps[i] = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f).normalized;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            float r = 1f;
            foreach (var b in bumps)
                r += Mathf.Max(0f, Vector3.Dot(p, b) - 0.5f) * 0.35f;
            r += ((float)random.NextDouble() - 0.5f) * 0.12f;
            p *= r;
            p.y *= p.y < 0f ? 0.7f : 1f;   // spodek koruny plossi
            points[i] = p;
        }

        var mesh = FlatShaded("Blob" + seed, points, faces);
        EnsureFolder(MeshFolder);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // Kuzel (jehlicnany): polomer 1, vyska 1, pivot uprostred podstavy.
    public static Mesh ConeMesh()
    {
        string path = $"{MeshFolder}/Cone.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        const int segments = 9;
        var random = new System.Random(5);
        var points = new List<Vector3> { Vector3.up };
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float r = 1f + ((float)random.NextDouble() - 0.5f) * 0.2f;
            points.Add(new Vector3(Mathf.Cos(a) * r, ((float)random.NextDouble() - 0.5f) * 0.1f, Mathf.Sin(a) * r));
        }
        points.Add(new Vector3(0f, 0.12f, 0f));
        var faces = new List<int>();
        for (int i = 0; i < segments; i++)
        {
            int a = 1 + i, b = 1 + (i + 1) % segments;
            faces.AddRange(new[] { 0, b, a });
            faces.AddRange(new[] { segments + 1, a, b });
        }

        var mesh = FlatShaded("Cone", points, faces);
        EnsureFolder(MeshFolder);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    public static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = $"{MeshFolder}/{name}.asset";
        EnsureFolder(MeshFolder);
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static Mesh FlatShaded(string name, List<Vector3> points, List<int> faces)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int i = 0; i < faces.Count; i += 3)
            for (int k = 0; k < 3; k++)
            {
                Vector3 p = points[faces[i + k]];
                triangles.Add(vertices.Count);
                vertices.Add(p);
                uvs.Add(new Vector2(p.x + p.z, p.y));
            }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    // Ikosfera (jednotkova), pocet deleni 'subdivisions'.
    static void Ico(int subdivisions, out List<Vector3> points, out List<int> faces)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        points = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        for (int i = 0; i < points.Count; i++)
            points[i] = points[i].normalized;

        faces = new List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };

        for (int s = 0; s < subdivisions; s++)
        {
            var cache = new Dictionary<long, int>();
            var next = new List<int>();
            var pts = points;
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int index)) return index;
                pts.Add(((pts[a] + pts[b]) * 0.5f).normalized);
                cache[key] = pts.Count - 1;
                return pts.Count - 1;
            }
            for (int i = 0; i < faces.Count; i += 3)
            {
                int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            faces = next;
        }
    }
}
