using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

public enum SurfaceMarkKind { Bullet, Slash, Arrow }

// Surface marks and embedded arrows share a fixed pool per scene.
// Tune these defaults here; the manager is created automatically on the first wall hit.
public sealed class BulletHoleDecals : MonoBehaviour
{
    public const int MaxDecals = 128;
    public const float Lifetime = 20f;
    public const float Diameter = 0.28f;
    public const float ArrowHoleDiameter = 0.16f;
    public const float SlashLength = 0.9f;
    const float FadeSeconds = 2f;
    static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    static readonly int StyleId = Shader.PropertyToID("_Style");
    static BulletHoleDecals instance;
    readonly List<Mark> marks = new List<Mark>();
    Mesh mesh;
    Material material;
    MaterialPropertyBlock properties;

    sealed class Mark
    {
        public GameObject gameObject;
        public MeshRenderer renderer;
        public float expires;
        public Collider surface;
        public SurfaceMarkKind kind;
        public GameObject arrow;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    public static bool CanMark(Collider surface) => surface != null && !surface.isTrigger
        && surface.attachedRigidbody == null
        && surface.GetComponentInParent<Health>() == null
        && surface.GetComponentInParent<NetworkObject>() == null
        && surface.GetComponentInParent<Target>() == null;

    public static bool TryResolveSurface(Vector3 point, Vector3 normal, out RaycastHit hit)
    {
        hit = default;
        return normal.sqrMagnitude > 0.5f
            && Physics.Raycast(point + normal.normalized * 0.05f, -normal.normalized, out hit, 0.1f, ~0, QueryTriggerInteraction.Ignore)
            && Vector3.Distance(hit.point, point) < 0.025f && CanMark(hit.collider);
    }

    public static void Place(Collider surface, Vector3 point, Vector3 normal,
        SurfaceMarkKind kind = SurfaceMarkKind.Bullet, float angle = 0f)
    {
        if (!CanMark(surface) || normal.sqrMagnitude < 0.5f) return;
        if (instance == null)
            instance = new GameObject("BulletHoleDecals").AddComponent<BulletHoleDecals>();
        instance.Add(surface, point, normal.normalized, kind, angle);
        ReplayLog.SurfaceMark(point, normal, kind, angle);
    }

    // Takes ownership of the stopped projectile visual; it no longer participates in flight simulation.
    public static bool EmbedArrow(Collider surface, Vector3 point, Vector3 normal, Vector3 direction, GameObject arrow)
    {
        if (arrow == null || !CanMark(surface) || normal.sqrMagnitude < 0.5f) return false;
        if (instance == null)
            instance = new GameObject("BulletHoleDecals").AddComponent<BulletHoleDecals>();
        var mark = instance.Add(surface, point, normal.normalized, SurfaceMarkKind.Arrow, 0f);
        if (mark == null) return false;
        direction = direction.sqrMagnitude > 0.001f ? direction.normalized : -normal.normalized;
        arrow.transform.SetParent(instance.transform, true);
        arrow.transform.rotation = Quaternion.LookRotation(direction);
        // Imported arrow prefabs have a Muzzle marker at the tip, not at the root.
        Vector3 tipOffset = direction * 0.425f;
        foreach (var child in arrow.GetComponentsInChildren<Transform>(true))
            if (child.name == "Muzzle") { tipOffset = child.position - arrow.transform.position; break; }
        arrow.transform.position = point - tipOffset + direction * 0.025f;
        foreach (var trail in arrow.GetComponentsInChildren<TrailRenderer>()) { trail.Clear(); trail.enabled = false; }
        foreach (var light in arrow.GetComponentsInChildren<Light>()) light.enabled = false;
        foreach (var collider in arrow.GetComponentsInChildren<Collider>()) collider.enabled = false;
        mark.arrow = arrow;
        return true;
    }

    void Awake()
    {
        var shader = Resources.Load<Shader>("Fx/BulletHole");
        if (shader == null) return;
        material = new Material(shader);
        properties = new MaterialPropertyBlock();
        mesh = new Mesh { name = "BulletHoleQuad" };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
    }

    Mark Add(Collider surface, Vector3 point, Vector3 normal, SurfaceMarkKind kind, float angle)
    {
        if (material == null) return null;
        Mark mark = null;
        foreach (var candidate in marks)
            if (!candidate.gameObject.activeSelf) { mark = candidate; break; }
        if (mark == null && marks.Count < MaxDecals)
        {
            var go = new GameObject("BulletHole", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            mark = new Mark { gameObject = go, renderer = renderer };
            marks.Add(mark);
        }
        if (mark == null)
        {
            // Replace the oldest mark when the pool is full.
            mark = marks[0];
            foreach (var candidate in marks)
                if (candidate.expires < mark.expires) mark = candidate;
        }
        ClearArrow(mark);
        mark.surface = surface;
        mark.kind = kind;
        mark.expires = Time.time + Lifetime;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
        if (up.sqrMagnitude < 0.001f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
        float rotation = kind == SurfaceMarkKind.Slash ? angle : Random.Range(0f, 360f);
        mark.gameObject.transform.SetPositionAndRotation(point + normal * 0.003f,
            Quaternion.LookRotation(normal, up.normalized) * Quaternion.Euler(0f, 0f, rotation));
        float diameter = kind == SurfaceMarkKind.Arrow ? ArrowHoleDiameter : Diameter;
        mark.gameObject.transform.localScale = kind == SurfaceMarkKind.Slash
            ? new Vector3(SlashLength, 0.18f, 1f) : Vector3.one * diameter;
        properties.SetFloat(OpacityId, 1f);
        properties.SetFloat(StyleId, kind == SurfaceMarkKind.Slash ? 1f : 0f);
        mark.renderer.SetPropertyBlock(properties);
        mark.gameObject.SetActive(true);
        return mark;
    }

    void Update()
    {
        foreach (var mark in marks)
        {
            if (!mark.gameObject.activeSelf) continue;
            float left = mark.expires - Time.time;
            if (left <= 0f || mark.surface == null || !mark.surface.enabled || !mark.surface.gameObject.activeInHierarchy)
            {
                mark.gameObject.SetActive(false);
                ClearArrow(mark);
                continue;
            }
            properties.SetFloat(OpacityId, Mathf.Clamp01(left / FadeSeconds));
            properties.SetFloat(StyleId, mark.kind == SurfaceMarkKind.Slash ? 1f : 0f);
            mark.renderer.SetPropertyBlock(properties);
        }
    }

    static void ClearArrow(Mark mark)
    {
        if (mark.arrow != null)
        {
            mark.arrow.SetActive(false);
            Destroy(mark.arrow);
            mark.arrow = null;
        }
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (material != null) Destroy(material);
        if (mesh != null) Destroy(mesh);
    }
}
