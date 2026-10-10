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
    [Tooltip("Seconds spent waiting at the start of the route before each lap.")]
    [Min(0f)] public float pauseSeconds = 25f;
    [Min(0f)] public float hornLeadSeconds = 2f;
    public AudioSource horn;
    public AudioClip impactSound;
    [Range(0f, 1f)] public float impactVolume = 0.7f;
    public Transform boundaryFrame;
    public Vector2 boundaryMin, boundaryMax;
    public float modelYawOffset;

    [Header("Bouncy Yaris")]
    public bool bounceEnabled = true;
    [Min(1f)] public float bounceBpm = 128f;
    [Tooltip("How much of the car's height is compressed on each beat.")]
    [Range(0f, 0.6f)] public float squashAmount = 0.3f;
    [Tooltip("Small upward stretch between compressions.")]
    [Range(0f, 0.3f)] public float stretchAmount = 0.08f;

    [Header("Car radio")]
    public AudioClip radioMusic;
    [Range(0f, 1f)] public float radioVolume = 0.7f;
    [Min(1f)] public float radioRange = 28f;
    [Min(0f)] public float radioFullVolumeDistance = 0.5f;
    [Tooltip("X: distance / Radio Range (0 to 1). Y: multiplier of Radio Volume (0 to 1). Keep the last point at (1, 0) for silence beyond the range.")]
    public AnimationCurve radioRolloff = CreateRadioRolloff();
    [SerializeField, HideInInspector] int radioRangeVersion;
    [Range(200f, 22000f)] public float radioCutoff = 1100f;
    AudioSource radioSource;
    AudioLowPassFilter radioFilter;

    Transform bouncePivot;

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
        EnsureBouncePivot();
        EnsureRadio();
        RebuildRoute();
    }

    void EnsureRadio()
    {
        if (radioSource != null && radioFilter != null) return;
        // Reuse the child after a script reload during Play mode; Awake need not run again.
        var radio = transform.Find("CarRadio");
        if (radio == null)
        {
            radio = new GameObject("CarRadio").transform;
            radio.SetParent(transform, false);
            radio.localPosition = GetComponent<BoxCollider>().center;
        }
        // A separate source keeps the muffling off the horn and impact sound.
        radioSource = radio.GetComponent<AudioSource>();
        if (radioSource == null) radioSource = radio.gameObject.AddComponent<AudioSource>();
        radioSource.playOnAwake = false;
        radioSource.loop = true;
        radioSource.spatialBlend = 1f;
        radioSource.rolloffMode = AudioRolloffMode.Custom;
        radioSource.minDistance = 1f;
        radioSource.dopplerLevel = 0f;
        radioSource.priority = 180;
        radioFilter = radio.GetComponent<AudioLowPassFilter>();
        if (radioFilter == null) radioFilter = radio.gameObject.AddComponent<AudioLowPassFilter>();
        radioFilter.lowpassResonanceQ = 1f;
    }

    void LateUpdate()
    {
        UpdateRadio();
        EnsureBouncePivot();
        if (bouncePivot == null) return;
        if (!bounceEnabled)
        {
            ResetBounce();
            return;
        }

        // Shared time keeps the dance in phase on host and clients, even for late joiners.
        var network = NetworkManager.Singleton;
        double time = network != null && network.IsListening ? network.ServerTime.Time : Time.timeAsDouble;
        double beats = time * Mathf.Max(1f, bounceBpm) / 60.0;
        float beat = (float)(beats % 1.0);
        float wave = Mathf.Cos(beat * Mathf.PI * 2f);
        float compression = Mathf.Max(0f, wave);
        float rebound = Mathf.Max(0f, -wave);
        float height = 1f - Mathf.Clamp(squashAmount, 0f, 0.6f) * compression * compression
            + Mathf.Clamp(stretchAmount, 0f, 0.3f) * rebound * rebound;
        // Wider when compressed, narrower on rebound, preserving the model's approximate volume.
        float width = 1f / Mathf.Sqrt(height);
        bouncePivot.localScale = new Vector3(width, height, width);
    }

    void EnsureBouncePivot()
    {
        if (bouncePivot != null) return;
        bouncePivot = transform.Find("_YarisSquish");
        if (bouncePivot != null) return;
        var model = transform.Find("Model");
        if (model == null) return;

        // The imported model is rotated 270 degrees around X. Scale an upright parent instead,
        // so vertical compression is always in the car's Y axis, with its bottom anchored.
        var collider = GetComponent<BoxCollider>();
        bouncePivot = new GameObject("_YarisSquish").transform;
        bouncePivot.SetParent(transform, false);
        bouncePivot.localPosition = collider.center - Vector3.up * collider.size.y * 0.5f;
        model.SetParent(bouncePivot, true);
    }

    void ResetBounce()
    {
        if (bouncePivot != null) bouncePivot.localScale = Vector3.one;
    }

    void UpdateRadio()
    {
        UpgradeRadioRange();
        EnsureRadio();
        radioSource.volume = Mathf.Clamp01(radioVolume);
        radioSource.maxDistance = Mathf.Max(1.01f, radioRange);
        radioSource.minDistance = Mathf.Clamp(radioFullVolumeDistance, 0f, radioSource.maxDistance - 0.01f);
        if (radioRolloff == null || radioRolloff.length < 2) radioRolloff = CreateRadioRolloff();
        radioSource.rolloffMode = AudioRolloffMode.Custom;
        radioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, radioRolloff);
        radioFilter.cutoffFrequency = Mathf.Clamp(radioCutoff, 200f, 22000f);
        if (radioSource.clip != radioMusic)
        {
            radioSource.Stop();
            radioSource.clip = radioMusic;
        }
        if (radioMusic != null && !radioSource.isPlaying)
            radioSource.Play();
    }

    // Migrate values already serialized in an open scene; changing a C# default alone cannot do that.
    public bool UpgradeRadioRange()
    {
        if (radioRangeVersion >= 3) return false;
        if (radioRangeVersion < 2)
        {
            radioVolume = 0.7f;
            radioRange = 28f;
            radioFullVolumeDistance = 0.5f;
        }
        radioRolloff = CreateRadioRolloff();
        radioRangeVersion = 3;
        return true;
    }

    static AnimationCurve CreateRadioRolloff()
    {
        // Gentler nearby decay, retaining a quiet tail and a smooth, silent endpoint.
        var keys = new Keyframe[9];
        for (int i = 0; i < keys.Length; i++)
        {
            float x = i / 8f;
            float tail = 1f - x;
            float decay = Mathf.Exp(-x);
            float value = decay * tail * tail;
            float slope = -decay * (tail * tail + 2f * tail);
            keys[i] = new Keyframe(x, value, slope, slope);
        }
        return new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
    }

    void OnDisable()
    {
        ResetBounce();
        if (radioSource != null) radioSource.Stop();
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
            if (health.currentHealth.Value <= 0f)
                health.ServerPlayCarImpact();
        }
    }

    public void PlayImpact(Vector3 position, bool localVictim)
    {
        ProceduralSfx.Play(impactSound, position, impactVolume, 30f, spatial: !localVictim);
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
