using UnityEngine;

// First pass without authored room zones: distance, tone and a very quiet short tail.
// Does not change pitch or infer walls/rooms. Ultimate warnings remain audible map-wide.
[RequireComponent(typeof(AudioSource))]
public sealed class VoiceSpatialProfile : MonoBehaviour
{
    AudioSource source;
    AudioLowPassFilter lowPass;
    AudioReverbFilter reverb;
    static AudioListener listener;
    bool distantWarning;
    bool spatialSpeech;

    public static float CutoffAt(float metres) => Mathf.Lerp(18000f,7500f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(2,30,metres)));

    void Awake()
    {
        source = GetComponent<AudioSource>();
        lowPass = gameObject.AddComponent<AudioLowPassFilter>();
        lowPass.lowpassResonanceQ = 1f;
        lowPass.cutoffFrequency = 18000f;
        lowPass.enabled = false;
        reverb = gameObject.AddComponent<AudioReverbFilter>();
        reverb.reverbPreset = AudioReverbPreset.User;
        reverb.dryLevel = 0f;
        reverb.room = -2600f;
        reverb.roomHF = -1800f;
        reverb.decayTime = .24f;
        reverb.decayHFRatio = .65f;
        reverb.reflectionsLevel = -3000f;
        reverb.reflectionsDelay = .012f;
        reverb.reverbLevel = -3400f;
        reverb.reverbDelay = .022f;
        reverb.enabled = false;
    }

    public void Configure(bool own, bool ultimate, bool music = false)
    {
        distantWarning = ultimate;
        spatialSpeech = !own && !music;
        source.spatialBlend = own ? 0f : 1f;
        source.spread = 0f;
        source.dopplerLevel = 0f;
        source.minDistance = 1.3f;
        source.maxDistance = ultimate ? 1500f : 32f;
        source.rolloffMode = AudioRolloffMode.Custom;
        // A lower but nonzero ultimate floor keeps a directional gameplay warning across the map.
        var curve = ultimate
            ? new AnimationCurve(new Keyframe(0,1),new Keyframe(.004f,.7f),new Keyframe(.02f,.4f),new Keyframe(1,.3f))
            : new AnimationCurve(new Keyframe(0,1),new Keyframe(.06f,.82f),new Keyframe(.18f,.55f),
                new Keyframe(.4f,.28f),new Keyframe(.7f,.10f),new Keyframe(1,0));
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff,curve);
        lowPass.enabled = spatialSpeech;
        reverb.enabled = spatialSpeech;
        RefreshTone();
    }

    void Update()
    {
        if (spatialSpeech && source.isPlaying) RefreshTone();
    }

    void RefreshTone()
    {
        if (!spatialSpeech) return;
        if (listener == null || !listener.isActiveAndEnabled)
        {
            listener = null;
            foreach (var candidate in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (candidate.isActiveAndEnabled) { listener = candidate; break; }
        }
        float distance = listener != null ? Vector3.Distance(transform.position,listener.transform.position) : 0f;
        float cutoff = CutoffAt(distance);
        lowPass.cutoffFrequency = distantWarning ? Mathf.Max(11000f,cutoff) : cutoff;
    }
}
