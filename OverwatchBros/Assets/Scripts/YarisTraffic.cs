using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Every peer evaluates the same closed route using the shared server clock.
// No dynamically spawned network prefab or per-frame RPC is needed.
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public sealed class YarisTraffic : MonoBehaviour
{
    public Transform route;
    [Min(0.1f)] public float speed = 18f;
    [Min(0f)] public float pauseSeconds = 25f;
    [Min(0f)] public float hornLeadSeconds = 2f;
    public AudioSource horn;
    public Transform boundaryFrame;
    public Vector2 boundaryMin, boundaryMax;
    public float modelYawOffset;

    Rigidbody body;
    BoxCollider carCollider;
    Vector3[] points;
    float[] distances;
    float length;
    bool positioned;
    bool wasNetworked;
    readonly List<float> entries = new List<float>();
    double previousTime;
    readonly HashSet<Health> hitThisStep = new HashSet<Health>();

    void Awake()
    {
        // Klakson je slyset jen v okoli (pres celou mapu jsou slyset jen ultimatky).
        if (horn != null)
        {
            horn.rolloffMode = AudioRolloffMode.Linear;
            horn.minDistance = Mathf.Min(horn.minDistance, 8f);
            horn.maxDistance = Mathf.Min(horn.maxDistance, 45f);
        }
        body = GetComponent<Rigidbody>();
        carCollider = GetComponent<BoxCollider>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        RebuildRoute();
    }

    public void RebuildRoute()
    {
        length = 0f;
        entries.Clear();
        positioned = false;
        if (route == null || route.childCount < 3) { points = null; return; }
        points = new Vector3[route.childCount];
        distances = new float[points.Length + 1];
        for (int i = 0; i < points.Length; i++) points[i] = route.GetChild(i).localPosition;
        for (int i = 0; i < points.Length; i++)
        {
            length += Vector3.Distance(route.TransformPoint(points[i]), route.TransformPoint(points[(i + 1) % points.Length]));
            distances[i + 1] = length;
        }
        // Sample the actual route, including smoothed corners, to locate each entry.
        bool inside = Inside(route.TransformPoint(points[0]));
        for (float d = 0.25f; d <= length; d += 0.25f)
        {
            Evaluate(d, out Vector3 p, out _);
            bool next = Inside(p);
            if (!inside && next) entries.Add(d);
            inside = next;
        }
    }

    bool Inside(Vector3 world)
    {
        if (boundaryFrame == null) return false;
        Vector3 p = boundaryFrame.InverseTransformPoint(world);
        return p.x >= boundaryMin.x && p.x <= boundaryMax.x && p.z >= boundaryMin.y && p.z <= boundaryMax.y;
    }

    void Evaluate(float distance, out Vector3 position, out Vector3 forward)
    {
        distance = Mathf.Repeat(distance, length);
        int lo = 0, hi = points.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (distances[mid + 1] <= distance) lo = mid + 1; else hi = mid;
        }
        Vector3 a = route.TransformPoint(points[lo]);
        Vector3 b = route.TransformPoint(points[(lo + 1) % points.Length]);
        position = Vector3.Lerp(a, b, (distance - distances[lo]) / Mathf.Max(0.001f, distances[lo + 1] - distances[lo]));
        forward = (b - a).normalized;
    }

    void FixedUpdate()
    {
        if (points == null || length < 0.1f) return;
        var network = NetworkManager.Singleton;
        bool networked = network != null && network.IsListening;
        double time = networked ? network.ServerTime.Time : Time.timeAsDouble;
        float driveDuration = length / speed;
        double cycle = driveDuration + pauseSeconds;
        double phase = time % cycle;
        float distance = phase < pauseSeconds ? 0f : (float)((phase - pauseSeconds) * speed);
        Evaluate(distance, out Vector3 position, out Vector3 forward);
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, modelYawOffset, 0f);
        if (!positioned || wasNetworked != networked)
        {
            body.position = position;
            body.rotation = rotation;
            positioned = true;
            wasNetworked = networked;
            previousTime = time;
            return; // Joining a session must not sweep an impact across the map.
        }
        if (horn != null && time >= previousTime && time - previousTime < 1f)
            foreach (float entry in entries)
            {
                double cue = pauseSeconds + entry / speed - hornLeadSeconds;
                if (System.Math.Floor((time - cue) / cycle) > System.Math.Floor((previousTime - cue) / cycle))
                { horn.Play(); break; }
            }
        previousTime = time;
        if (networked && network.IsServer) HitPlayers(position, rotation);
        body.MovePosition(position);
        body.MoveRotation(rotation);
    }

    void HitPlayers(Vector3 position, Quaternion rotation)
    {
        var match = MatchManager.Instance;
        if (match != null && (match.IsLobby || match.IsOver)) return;
        Vector3 scale = transform.lossyScale;
        Vector3 center = position + rotation * Vector3.Scale(carCollider.center, scale);
        Vector3 previousCenter = body.position + body.rotation * Vector3.Scale(carCollider.center, scale);
        float travel = Vector3.Distance(center, previousCenter);
        if (travel > speed * Time.fixedDeltaTime * 4f + 1f) return;
        Vector3 half = Vector3.Scale(carCollider.size * 0.5f, scale);
        half += Vector3.one * 0.12f;
        half.z += travel * 0.5f;
        hitThisStep.Clear();
        foreach (var hit in Physics.OverlapBox((center + previousCenter) * 0.5f, half, rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            var health = hit.GetComponentInParent<Health>();
            if (health == null || !health.IsSpawned || health.currentHealth.Value <= 0f || !hitThisStep.Add(health)) continue;
            var hero = health.GetComponent<PlayerHero>();
            if (hero != null && hero.IsJoining) continue;
            if (hero != null) hero.ServerDeathCause = "env:car";
            health.Kill();
        }
    }

    void OnDrawGizmosSelected()
    {
        if (route == null || route.childCount < 2) return;
        Gizmos.color = new Color(1f, 0.7f, 0.1f);
        for (int i = 0; i < route.childCount; i++)
            Gizmos.DrawLine(route.GetChild(i).position + Vector3.up * 0.2f,
                route.GetChild((i + 1) % route.childCount).position + Vector3.up * 0.2f);
    }
}
