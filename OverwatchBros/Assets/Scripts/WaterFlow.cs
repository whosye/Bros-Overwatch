using System.Collections.Generic;
using UnityEngine;

// Tekouci voda: textura vlnek ujizdi po proudu (koryto ma UV v = vzdalenost podel toku).
// U jezirka (pond) se vlnky jen pomalu prelevaji sem a tam. Jezirko je zaroven cil jizdy z toboganu
// (WaterSplashFx tam udela velky splouch).
public class WaterFlow : MonoBehaviour
{
    public float speed = 1.6f;      // m/s podel toku (koryto)
    public bool pond;               // jezirko: misto toku jen pomale vlneni
    public float radius = 3.2f;     // jezirko: polomer hladiny

    static readonly List<WaterFlow> ponds = new List<WaterFlow>();
    static readonly List<WaterFlow> surfaces = new List<WaterFlow>();
    Vector3[] surfaceVertices;
    int[] surfaceTriangles;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { ponds.Clear(); surfaces.Clear(); }

    Material material;
    Vector2 tiling = Vector2.one;

    void OnEnable()
    {
        if (pond && !ponds.Contains(this)) ponds.Add(this);
        if (!surfaces.Contains(this)) surfaces.Add(this);
    }
    void OnDisable() { ponds.Remove(this); surfaces.Remove(this); }

    // Audio-only intersection. Water remains non-solid and does not change player/projectile physics.
    public static bool RaycastSurface(Vector3 origin, Vector3 direction, float distance, out Vector3 point)
    {
        bool found = false;
        point = default;
        if (Mathf.Abs(direction.y) < 0.0001f) return false;
        foreach (var surface in surfaces)
        {
            if (surface == null || !surface.isActiveAndEnabled) continue;
            var renderer = surface.GetComponent<Renderer>();
            if (renderer == null || !renderer.enabled) continue;
            var bounds = renderer.bounds;
            float t = (bounds.max.y - origin.y) / direction.y;
            if (t < 0f || t > distance) continue;
            Vector3 candidate = origin + direction * t;
            if (candidate.x < bounds.min.x || candidate.x > bounds.max.x || candidate.z < bounds.min.z || candidate.z > bounds.max.z) continue;
            if (surface.pond)
            {
                Vector3 delta = candidate - bounds.center;
                if (delta.x * delta.x / (bounds.extents.x * bounds.extents.x)
                    + delta.z * delta.z / (bounds.extents.z * bounds.extents.z) > 1f) continue;
            }
            else
            {
                if (surface.surfaceVertices == null)
                {
                    var filter = surface.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                    surface.surfaceVertices = filter.sharedMesh.vertices;
                    surface.surfaceTriangles = filter.sharedMesh.triangles;
                }
                Vector3 local = surface.transform.InverseTransformPoint(candidate);
                bool inside = false;
                for (int i = 0; i + 2 < surface.surfaceTriangles.Length; i += 3)
                {
                    Vector3 a = surface.surfaceVertices[surface.surfaceTriangles[i]], b = surface.surfaceVertices[surface.surfaceTriangles[i + 1]], c = surface.surfaceVertices[surface.surfaceTriangles[i + 2]];
                    float Cross(Vector3 u, Vector3 v) => u.x * v.z - u.z * v.x;
                    if (Mathf.Abs(Cross(b - a, c - a)) < 0.00001f) continue;
                    float x = Cross(b - a, local - a), y = Cross(c - b, local - b), z = Cross(a - c, local - c);
                    if ((x >= -0.001f && y >= -0.001f && z >= -0.001f) || (x <= 0.001f && y <= 0.001f && z <= 0.001f)) { inside = true; break; }
                }
                if (!inside) continue;
            }
            point = candidate;
            distance = t;
            found = true;
        }
        return found;
    }

    void Start()
    {
        var r = GetComponent<Renderer>();
        if (r == null) return;
        material = r.material;   // vlastni kopie (posun textury jen u tohoto objektu)
        if (material.HasProperty("_BaseMap")) tiling = material.GetTextureScale("_BaseMap");
    }

    void Update()
    {
        if (material == null || !material.HasProperty("_BaseMap")) return;
        Vector2 offset = pond
            ? new Vector2(Mathf.Sin(Time.time * 0.35f) * 0.15f, Time.time * 0.03f)
            : new Vector2(0f, -Time.time * speed * tiling.y);
        material.SetTextureOffset("_BaseMap", offset);
    }

    // Je bod v nekterem jezirku (vodorovne, s rezervou)?
    public static bool InPond(Vector3 position, out Vector3 center)
    {
        foreach (var p in ponds)
        {
            if (p == null) continue;
            Vector3 c = p.transform.position;
            Vector3 d = position - c;
            d.y = 0f;
            if (d.magnitude <= p.radius + 1.5f && Mathf.Abs(position.y - c.y) < 3f)
            {
                center = c;
                return true;
            }
        }
        center = position;
        return false;
    }
}
