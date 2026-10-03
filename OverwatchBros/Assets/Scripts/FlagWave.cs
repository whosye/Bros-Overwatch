using UnityEngine;

// Vlajka na rozhledne: jemne vlneni latky (posouva vrcholy kolmo k vlajce, cim dal od zerdi, tim vic).
public class FlagWave : MonoBehaviour
{
    public float amplitude = 0.14f;
    public float speed = 4f;
    public float waveLength = 1.1f;

    Mesh mesh;
    Vector3[] baseVertices;
    Vector3[] vertices;
    float poleX;
    float length;

    void Start()
    {
        var filter = GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;

        mesh = Instantiate(filter.sharedMesh);
        filter.sharedMesh = mesh;
        mesh.MarkDynamic();
        baseVertices = mesh.vertices;
        vertices = new Vector3[baseVertices.Length];

        poleX = float.MaxValue;
        float maxX = float.MinValue;
        foreach (var v in baseVertices)
        {
            poleX = Mathf.Min(poleX, v.x);
            maxX = Mathf.Max(maxX, v.x);
        }
        length = Mathf.Max(0.01f, maxX - poleX);
    }

    void Update()
    {
        if (mesh == null) return;

        float time = Time.time * speed;
        for (int i = 0; i < baseVertices.Length; i++)
        {
            Vector3 v = baseVertices[i];
            float d = (v.x - poleX) / length;
            v.z += Mathf.Sin(time - d * length / waveLength * Mathf.PI * 2f + v.y * 0.6f) * amplitude * d;
            vertices[i] = v;
        }
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
    }
}
