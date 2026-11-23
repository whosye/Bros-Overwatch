using UnityEngine;

// Visual only: keeps the original emitter's 22 cm body diameter / 28 cm height.
public static class CaroHealCan
{
    static Mesh bodyMesh;
    static Material labelMaterial;
    static Material lidMaterial;
    static Material metalMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        bodyMesh = null;
        labelMaterial = null;
        lidMaterial = null;
        metalMaterial = null;
    }

    public static GameObject Create(Transform parent)
    {
        EnsureAssets();
        var can = new GameObject("CaroHealCan");
        can.transform.SetParent(parent, false);

        // Separate side mesh: the label wraps once, upright, without covering either cap.
        var body = new GameObject("CaroLabel", typeof(MeshFilter), typeof(MeshRenderer));
        body.transform.SetParent(can.transform, false);
        body.GetComponent<MeshFilter>().sharedMesh = bodyMesh;
        body.GetComponent<MeshRenderer>().sharedMaterial = labelMaterial;

        AddCap(can.transform, "MetalBase", 0.008f, 0.224f, 0.016f, metalMaterial);
        AddCap(can.transform, "RedPlasticLid", 0.264f, 0.23f, 0.028f, lidMaterial);
        AddCap(can.transform, "LidInset", 0.2785f, 0.216f, 0.003f, lidMaterial);
        return can;
    }

    static void EnsureAssets()
    {
        if (bodyMesh == null)
            bodyMesh = BuildBody();
        if (labelMaterial == null)
        {
            labelMaterial = Fx.NewLit(Color.white);
            labelMaterial.name = "Caro printed paper label";
            labelMaterial.mainTexture = Resources.Load<Texture2D>("Props/CaroLabel");
            labelMaterial.SetFloat("_Metallic", 0f);
            labelMaterial.SetFloat("_Smoothness", 0.22f);
        }
        if (lidMaterial == null)
        {
            lidMaterial = Fx.NewLit(new Color(0.72f, 0.025f, 0.045f));
            lidMaterial.name = "Caro red plastic";
            lidMaterial.SetFloat("_Metallic", 0f);
            lidMaterial.SetFloat("_Smoothness", 0.38f);
        }
        if (metalMaterial == null)
        {
            metalMaterial = Fx.NewLit(new Color(0.68f, 0.7f, 0.72f));
            metalMaterial.name = "Caro tin base";
            metalMaterial.SetFloat("_Metallic", 0.85f);
            metalMaterial.SetFloat("_Smoothness", 0.55f);
        }
    }

    static void AddCap(Transform parent, string name, float y, float diameter, float height, Material material)
    {
        var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cap.name = name;
        Object.DestroyImmediate(cap.GetComponent<Collider>());
        cap.transform.SetParent(parent, false);
        cap.transform.localPosition = Vector3.up * y;
        cap.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
        cap.GetComponent<Renderer>().sharedMaterial = material;
    }

    static Mesh BuildBody()
    {
        const int segments = 64;
        var vertices = new Vector3[(segments + 1) * 2];
        var normals = new Vector3[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float u = (float)i / segments;
            float angle = u * Mathf.PI * 2f;
            var normal = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            int v = i * 2;
            vertices[v] = normal * 0.11f + Vector3.up * 0.012f;
            vertices[v + 1] = normal * 0.11f + Vector3.up * 0.255f;
            normals[v] = normals[v + 1] = normal;
            uv[v] = new Vector2(1f - u, 0f);
            uv[v + 1] = new Vector2(1f - u, 1f);
            if (i == segments) continue;
            int t = i * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 2;
            triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;
        }
        var mesh = new Mesh { name = "Caro cylindrical label", vertices = vertices, normals = normals, uv = uv, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }
}
