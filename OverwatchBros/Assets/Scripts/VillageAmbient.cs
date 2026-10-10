using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class VillageAmbient : MonoBehaviour
{
    public AudioClip clip;
    [Range(0f, 4f)] public float volumeBoost = 4f;
    [Min(0.1f)] public float crossfadeSeconds = 3f;
    [Min(1f)] public float minDistance = 18f;
    [Min(20f)] public float maxDistance = 140f;
    readonly AudioSource[] sources = new AudioSource[8];
    readonly double[] starts = new double[8];
    readonly double[] ends = new double[8];
    readonly double[] nextStarts = new double[4];
    readonly int[] nextVoices = new int[4];
    readonly float[] distanceGains = new float[4];
    AudioListener activeListener;
    bool running;
    float matchFade;

    void OnEnable()
    {
        foreach (var old in GetComponentsInChildren<AudioSource>()) old.Stop();
        var ground = transform.parent != null ? transform.parent.Find("Ground") : null;
        var mesh = ground != null ? ground.GetComponent<MeshFilter>() : null;
        Bounds bounds = new Bounds(transform.position, new Vector3(100f, 0f, 100f));
        if (mesh != null && mesh.sharedMesh != null)
        {
            var local = mesh.sharedMesh.bounds;
            bounds = new Bounds(ground.TransformPoint(local.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                bounds.Encapsulate(ground.TransformPoint(local.center + Vector3.Scale(local.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f))));
        }
        for (int emitter = 0; emitter < 4; emitter++)
        {
            var node = transform.Find("ForestAmbient" + emitter);
            if (node == null) { node = new GameObject("ForestAmbient" + emitter).transform; node.SetParent(transform, false); }
            node.position = bounds.center + new Vector3(bounds.extents.x * (emitter % 2 == 0 ? -0.65f : 0.65f),
                bounds.extents.y + 6f, bounds.extents.z * (emitter < 2 ? -0.65f : 0.65f));
            for (int voice = 0; voice < 2; voice++)
            {
                int i = emitter * 2 + voice;
                var child = node.Find("Voice" + voice);
                if (child == null) { child = new GameObject("Voice" + voice).transform; child.SetParent(node, false); }
                var source = child.GetComponent<AudioSource>();
                if (source == null) source = child.gameObject.AddComponent<AudioSource>();
                sources[i] = source;
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.spread = 100f;
                source.dopplerLevel = 0f;
                source.priority = 220;
                source.rolloffMode = AudioRolloffMode.Custom;
                // Distance weights are applied below and normalized across the
                // four voices. Unity still supplies directional 3D panning.
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0f, 1f, 1f, 1f));
                source.volume = 0f;
            }
        }
        StopPlayback();
    }

    float Crossfade => Mathf.Clamp(crossfadeSeconds, 0.1f, clip.length * 0.1f);
    void Update()
    {
        var network = NetworkManager.Singleton;
        var match = MatchManager.Instance;
        bool playing = clip != null && clip.length > 1f && network != null && network.IsClient &&
            network.IsListening && match != null && match.IsSpawned && !match.IsOver;
        matchFade = Mathf.MoveTowards(matchFade, playing ? 1f : 0f, Time.unscaledDeltaTime);
        if (!playing && matchFade <= 0f) { if (running) StopPlayback(); return; }
        if (clip == null) return;
        double now = AudioSettings.dspTime;
        if (!running && playing)
        {
            running = true;
            for (int emitter = 0; emitter < 4; emitter++)
            {
                nextVoices[emitter] = 0;
                nextStarts[emitter] = now + 0.2;
                ScheduleNext(emitter, clip.length * emitter * 0.21f);
            }
        }
        for (int emitter = 0; emitter < 4; emitter++)
            if (running && now + 1.0 >= nextStarts[emitter]) ScheduleNext(emitter, 0f);
        if (activeListener == null || !activeListener.isActiveAndEnabled)
            foreach (var candidate in FindObjectsByType<AudioListener>())
                if (candidate.isActiveAndEnabled) { activeListener = candidate; break; }
        float energy = 0f;
        for (int emitter = 0; emitter < 4; emitter++)
        {
            float distance = activeListener != null && activeListener.isActiveAndEnabled
                ? Vector3.Distance(activeListener.transform.position, sources[emitter * 2].transform.position) : float.PositiveInfinity;
            float weight = Mathf.Clamp01(1f - (distance - minDistance) / Mathf.Max(1f, maxDistance - minDistance));
            distanceGains[emitter] = weight;
            energy += weight * weight;
        }
        float normalization = energy > 0.000001f ? 1f / Mathf.Sqrt(energy) : 0f;
        for (int i = 0; i < sources.Length; i++)
        {
            sources[i].minDistance = minDistance;
            sources[i].maxDistance = Mathf.Max(minDistance + 1f, maxDistance);
            float envelope = Mathf.Min(Mathf.Clamp01((float)(now - starts[i]) / Crossfade),
                Mathf.Clamp01((float)(ends[i] - now) / Crossfade));
            sources[i].volume = Mathf.Clamp01(GameSettings.AmbientVolume * volumeBoost * distanceGains[i / 2] * normalization) * matchFade * envelope;
        }
    }

    void ScheduleNext(int emitter, float offset)
    {
        int i = emitter * 2 + nextVoices[emitter];
        sources[i].clip = clip;
        sources[i].time = offset;
        starts[i] = nextStarts[emitter];
        ends[i] = starts[i] + clip.length - offset;
        sources[i].PlayScheduled(starts[i]);
        nextStarts[emitter] = ends[i] - Crossfade;
        nextVoices[emitter] = 1 - nextVoices[emitter];
    }

    void StopPlayback()
    {
        foreach (var source in sources) if (source != null) source.Stop();
        running = false;
        matchFade = 0f;
        System.Array.Clear(starts, 0, starts.Length);
        System.Array.Clear(ends, 0, ends.Length);
    }
    void OnDisable() => StopPlayback();
}
