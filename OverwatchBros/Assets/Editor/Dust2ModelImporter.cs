using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using GLTFast;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Volitelny importer jen pro Dust2.glb. Ostatni glTF soubory pouzivaji puvodni importer.
// PNG/JPEG uvnitr GLB jsou prenosovy format; do hry patri GPU-komprimovane textury.
[ScriptedImporter(3, null, overrideExts: new[] { "glb" })]
public sealed class Dust2ModelImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext context)
    {
        if (!context.assetPath.Equals(Dust2Setup.ModelPath, StringComparison.Ordinal))
            throw new InvalidOperationException("Dust II importer is only intended for Dust2.glb.");
        var gltf = new GltfImport(deferAgent: new UninterruptedDeferAgent());
        var root = new GameObject("Dust2 Export");
        try
        {
            var settings = new ImportSettings
            {
                NodeNameMethod = NameImportMethod.OriginalUnique,
                AnimationMethod = AnimationMethod.None,
                GenerateMipMaps = true,
                TexturesReadable = true
            };
            bool loaded = Run(() => gltf.Load(File.ReadAllBytes(context.assetPath),
                new Uri(Path.GetFullPath(context.assetPath)), settings));
            if (!loaded || !Run(() => gltf.InstantiateMainSceneAsync(root.transform)))
                throw new InvalidOperationException("Dust II glTF import failed.");

            var names = new HashSet<string>();
            var meshes = new Dictionary<Mesh, Mesh>();
            var materials = new Dictionary<Material, Material>();
            var textures = new Dictionary<Texture, Texture>();
            for (int i = 0; i < gltf.TextureCount; i++)
            {
                var source = gltf.GetTexture(i);
                if (source == null || textures.ContainsKey(source)) continue;
                var texture = Object.Instantiate(source);
                texture.name = source.name;
                bool opaqueColor = GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);
                if (opaqueColor)
                    foreach (var pixel in texture.GetPixels32())
                        if (pixel.a != 255) { opaqueColor = false; break; }
                EditorUtility.CompressTexture(texture, opaqueColor ? TextureFormat.DXT1 : TextureFormat.BC7,
                    TextureCompressionQuality.Best);
                texture.Apply(false, true);
                textures.Add(source, texture);
                Add("textures/" + texture.name, texture);
            }
            foreach (var source in gltf.Meshes)
            {
                if (source == null || meshes.ContainsKey(source)) continue;
                var mesh = Object.Instantiate(source);
                mesh.name = source.name;
                // glTFast otherwise expands RGBA8 vertex colors to sixteen bytes per vertex.
                // This restores the exported precision without moving a single vertex.
                PackVertexBuffer(source, mesh);
                meshes.Add(source, mesh);
                Add("meshes/" + mesh.name, mesh);
            }
            for (int i = 0; i < gltf.MaterialCount; i++)
            {
                var source = gltf.GetMaterial(i);
                if (source == null || materials.ContainsKey(source)) continue;
                var material = Object.Instantiate(source);
                material.name = source.name;
                if (source.name == "Export_black_simple")
                {
                    material.shader = Shader.Find("Universal Render Pipeline/Unlit");
                    material.shaderKeywords = Array.Empty<string>();
                    material.SetColor("_BaseColor", Color.black);
                }
                foreach (string property in material.GetTexturePropertyNames())
                {
                    var texture = material.GetTexture(property);
                    if (texture != null && textures.TryGetValue(texture, out var replacement))
                        material.SetTexture(property, replacement);
                }
                materials.Add(source, material);
                Add("materials/" + material.name, material);
            }
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) filter.sharedMesh = meshes[filter.sharedMesh];
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mapped = renderer.sharedMaterials;
                for (int i = 0; i < mapped.Length; i++)
                    mapped[i] = materials[mapped[i]];
                renderer.sharedMaterials = mapped;
            }
            Add("scenes/Dust2 Export", root);
            context.SetMainObject(root);
            Debug.Log($"[Dust II] Import: {meshes.Count} siti, {textures.Count} komprimovanych textur.");

            void Add(string name, Object obj)
            {
                string key = name;
                for (int i = 1; !names.Add(key); i++) key = name + "_" + i;
                context.AddObjectToAsset(key, obj);
            }
        }
        catch
        {
            Object.DestroyImmediate(root);
            throw;
        }
        finally
        {
            // The saved assets are independent copies. Public Dispose may safely
            // release every original texture/mesh as well as temporary native data.
            gltf.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MapVertex
    {
        public Vector3 position;
        public half4 normal;
        public half4 tangent;
        public Color32 color;
        public Vector2 uv;
    }

    static void PackVertexBuffer(Mesh source, Mesh mesh)
    {
        var positions = source.vertices;
        var normals = source.normals;
        var tangents = source.tangents;
        var colors = source.colors32;
        var uv = source.uv;
        var vertices = new NativeArray<MapVertex>(positions.Length, Allocator.Temp);
        try
        {
        for (int i = 0; i < positions.Length; i++)
        {
            var n = normals.Length > i ? normals[i] : Vector3.up;
            var t = tangents.Length > i ? tangents[i] : new Vector4(1, 0, 0, 1);
            vertices[i] = new MapVertex
            {
                position = positions[i],
                normal = new half4(new float4(n.x, n.y, n.z, 0)),
                tangent = new half4(new float4(t.x, t.y, t.z, t.w)),
                color = colors.Length > i ? colors[i] : new Color32(255, 255, 255, 255),
                uv = uv.Length > i ? uv[i] : Vector2.zero
            };
        }
        mesh.SetVertexBufferParams(positions.Length,
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
        mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length);
        mesh.indexFormat = source.indexFormat;
        mesh.subMeshCount = source.subMeshCount;
        for (int i = 0; i < source.subMeshCount; i++)
            mesh.SetIndices(source.GetIndices(i), source.GetTopology(i), i, false);
        mesh.bounds = source.bounds;
        }
        finally { vertices.Dispose(); }
    }

    // Keep glTF's Unity work on the importer thread while awaiting decode jobs.
    static T Run<T>(Func<Task<T>> operation)
    {
        var previous = SynchronizationContext.Current;
        var context = new ImportContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var task = operation();
            while (!task.IsCompleted)
            {
                if (context.queue.TryDequeue(out var work)) work.callback(work.state);
                else Thread.Sleep(1);
            }
            return task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    sealed class ImportContext : SynchronizationContext
    {
        public readonly ConcurrentQueue<(SendOrPostCallback callback, object state)> queue = new();
        public override void Post(SendOrPostCallback callback, object state) => queue.Enqueue((callback, state));
    }
}
