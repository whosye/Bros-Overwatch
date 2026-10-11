using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Small details share meshes by material and 16 m cell, so they can still be culled locally.
internal sealed class MapFinishGeometry
{
    internal sealed class Part
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();

        public void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            int n = vertices.Count;
            foreach (var p in new[] { a, b, c }) { vertices.Add(p); uv.Add(new Vector2(p.x, p.z)); colors.Add(Color.white); }
            triangles.AddRange(new[] { n, n + 1, n + 2 });
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float alphaA = 1, float alphaB = 1, float alphaC = 1, float alphaD = 1)
        {
            int n = vertices.Count;
            foreach (var p in new[] { a, b, c, d }) { vertices.Add(p); uv.Add(new Vector2(p.x, p.z)); }
            foreach (float alpha in new[] { alphaA, alphaB, alphaC, alphaD }) colors.Add(new Color(1, 1, 1, alpha));
            triangles.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
        }

        public void Box(Vector3 center, Vector3 size, Quaternion rotation)
        {
            Vector3 h = size * .5f;
            Vector3 P(float x, float y, float z) => center + rotation * Vector3.Scale(new Vector3(x, y, z), h);
            Quad(P(-1,-1,-1), P(-1,1,-1), P(1,1,-1), P(1,-1,-1));
            Quad(P(1,-1,1), P(1,1,1), P(-1,1,1), P(-1,-1,1));
            Quad(P(-1,-1,1), P(-1,1,1), P(-1,1,-1), P(-1,-1,-1));
            Quad(P(1,-1,-1), P(1,1,-1), P(1,1,1), P(1,-1,1));
            Quad(P(-1,1,-1), P(-1,1,1), P(1,1,1), P(1,1,-1));
            Quad(P(-1,-1,1), P(-1,-1,-1), P(1,-1,-1), P(1,-1,1));
        }

        public Mesh Mesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }
    }

    readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();
    readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    public int Triangles { get; private set; }

    public Part For(Material material, Vector3 p)
    {
        string key = material.name + "_" + Mathf.FloorToInt(p.x / 16) + "_" + Mathf.FloorToInt(p.z / 16);
        if (!parts.TryGetValue(key, out var part))
        {
            part = new Part(); parts.Add(key, part); materials.Add(key, material);
        }
        return part;
    }

    public void Bake(Transform parent)
    {
        MapBuildKit.EnsureFolder(MapVisualPolishSetup.Folder + "/Meshes");
        foreach (var item in parts)
        {
            var mesh = Save(item.Value.Mesh(item.Key), item.Key);
            Triangles += (int)mesh.GetIndexCount(0) / 3;
            var go = MapBuildKit.MeshObject(parent, item.Key, mesh, Vector3.zero, Quaternion.identity, Vector3.one, materials[item.Key], false);
            go.GetComponent<Renderer>().receiveShadows = true;
        }
    }

    public static Mesh Save(Mesh mesh, string name)
    {
        string path = MapVisualPolishSetup.Folder + "/Meshes/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }
}
