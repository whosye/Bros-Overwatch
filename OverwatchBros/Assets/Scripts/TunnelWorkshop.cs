using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

/// <summary>Local, cosmetic easter egg. No network state or gameplay effects.</summary>
[DisallowMultipleComponent]
public sealed class TunnelWorkshop : MonoBehaviour
{
    [Header("Media")]
    public AudioSource music;
    public AudioLowPassFilter doorFilter;
    public VideoPlayer video;
    public Renderer monitor;
    public Light monitorLight;
    [Range(0f, 1f)] public float musicVolume = 0.62f;
    [Min(1f)] public float audibleRadius = 15f;
    [Tooltip("World height of the main map floor. The sound is exactly zero at and above this plane.")]
    public float surfaceY;
    public Vector3 roomSize = new Vector3(5.75f, 3.25f, 5.25f);
    [Range(200f, 5000f)] public float throughDoorCutoff = 1250f;

    [Header("Performance / rhythm")]
    [Range(60f, 220f)] public float beatsPerMinute = 175f;
    public float beatOffsetSeconds;
    public Transform body, head, mouseHand, keyboardHand, mouse;
    public Transform leftShoulder, rightShoulder, leftUpperArm, leftForearm, rightUpperArm, rightForearm;
    public Light accentLight;

    AudioListener listener;
    RenderTexture screenTexture, sampleTexture;
    Texture2D cpuSample;
    MaterialPropertyBlock screenProperties;
    Vector3 bodyRest, headRest, mouseHandRest, keyboardHandRest, mouseRest;
    Quaternion bodyRotation, headRotation;
    float listenerSearchTime, nextSampleTime, currentGain;
    Color screenColor = new Color(0.35f, 0.65f, 0.58f);
    bool readbackPending, disposed, preparedRequested, capturedPose;
    AsyncGPUReadbackRequest pendingReadback;
    double musicStart;
    int generation;

    void OnEnable()
    {
        disposed = false;
        generation++;
        CapturePose();
        if (video && monitor)
        {
            screenTexture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32) { name = "Pavouk live monitor" };
            screenTexture.Create();
            sampleTexture = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32) { name = "Pavouk light sample" };
            sampleTexture.Create();
            video.playOnAwake = false;
            video.isLooping = true;
            video.audioOutputMode = VideoAudioOutputMode.None;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.targetTexture = screenTexture;
            video.prepareCompleted += VideoPrepared;
            video.errorReceived += VideoError;
            screenProperties = new MaterialPropertyBlock();
            monitor.GetPropertyBlock(screenProperties);
            screenProperties.SetTexture("_BaseMap", screenTexture);
            screenProperties.SetTexture("_EmissionMap", screenTexture);
            monitor.SetPropertyBlock(screenProperties);
        }
        if (music && music.clip)
        {
            music.playOnAwake = false;
            music.loop = true;
            music.spatialBlend = 1f;
            music.dopplerLevel = 0f;
            music.spread = 35f;
            music.reverbZoneMix = 0f; // No reverberation tail may leak above ground.
            music.rolloffMode = AudioRolloffMode.Custom;
            music.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0f, 1f, 1f, 1f));
            music.minDistance = 1f;
            music.maxDistance = audibleRadius;
            music.volume = 0f;
            music.mute = true;
            musicStart = AudioSettings.dspTime + 0.12;
            music.PlayScheduled(musicStart);
        }
    }

    void CapturePose()
    {
        if (capturedPose) return;
        if (body) { bodyRest = body.localPosition; bodyRotation = body.localRotation; }
        if (head) { headRest = head.localPosition; headRotation = head.localRotation; }
        if (mouseHand) mouseHandRest = mouseHand.localPosition;
        if (keyboardHand) keyboardHandRest = keyboardHand.localPosition;
        if (mouse) mouseRest = mouse.localPosition;
        capturedPose = true;
    }

    /// <summary>Explicitly bounded attenuation, independently testable without an AudioListener.</summary>
    public float GainAt(Vector3 ear)
    {
        if (ear.y >= surfaceY || ear.y < transform.position.y - 0.5f) return 0f;
        Vector3 doorway = transform.TransformPoint(new Vector3(0f, 1.55f, 0f));
        float distance = Vector3.Distance(ear, doorway);
        float distanceGain = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.2f, audibleRadius, distance));
        float depthGain = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((surfaceY - ear.y) / 0.8f));
        return distanceGain * depthGain;
    }

    bool Inside(Vector3 world)
    {
        Vector3 p = transform.InverseTransformPoint(world);
        return Mathf.Abs(p.x) < roomSize.x * 0.5f && p.z > 0.12f && p.z < roomSize.z && p.y > 0f && p.y < roomSize.y;
    }

    void LateUpdate()
    {
        if ((!listener || !listener.isActiveAndEnabled) && Time.unscaledTime >= listenerSearchTime)
        {
            listenerSearchTime = Time.unscaledTime + 0.5f;
            listener = null;
            foreach (var candidate in FindObjectsByType<AudioListener>())
                if (candidate.isActiveAndEnabled) { listener = candidate; break; }
        }
        bool valid = listener && listener.isActiveAndEnabled;
        Vector3 ear = valid ? listener.transform.position : new Vector3(0f, float.PositiveInfinity, 0f);
        float gain = valid ? GainAt(ear) : 0f;
        // Hard gate precedes smoothing, including teleport / respawn above ground.
        currentGain = gain <= 0f ? 0f : Mathf.MoveTowards(currentGain, gain, Time.unscaledDeltaTime * 2.5f);
        if (music)
        {
            music.mute = gain <= 0f;
            music.volume = currentGain * musicVolume;
        }
        if (doorFilter)
            doorFilter.cutoffFrequency = Mathf.Lerp(doorFilter.cutoffFrequency,
                valid && Inside(ear) ? 9000f : throughDoorCutoff, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 5f));

        bool nearby = valid && ear.y < surfaceY && Vector3.SqrMagnitude(ear - transform.position) < 28f * 28f;
        if (video && video.clip)
        {
            if (nearby && !preparedRequested && !video.isPrepared) { preparedRequested = true; video.Prepare(); }
            if (video.isPrepared)
            {
                if (nearby && !video.isPlaying) video.Play();
                else if (!nearby && video.isPlaying) video.Pause();
            }
        }
        if (nearby)
        {
            double seconds = music && music.isPlaying ? (double)music.timeSamples / music.clip.frequency : Math.Max(0.0, AudioSettings.dspTime - musicStart);
            Animate((seconds - beatOffsetSeconds) * beatsPerMinute / 60.0);
            if (video && video.isPlaying && Time.unscaledTime >= nextSampleTime && !readbackPending) SampleScreen();
        }
        if (monitorLight)
        {
            monitorLight.enabled = nearby;
            monitorLight.color = Color.Lerp(monitorLight.color, screenColor, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 6f));
            float brightness = Mathf.Clamp(screenColor.grayscale, 0.05f, 1f);
            monitorLight.intensity = Mathf.Lerp(monitorLight.intensity, 0.45f + brightness * 2f, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 5f));
        }
    }

    public void Animate(double beat)
    {
        CapturePose();
        float phase = (float)(beat % 64.0) * Mathf.PI * 2f;
        float nod = Mathf.Sin(phase), sway = Mathf.Sin(phase * 0.25f);
        if (body)
        {
            body.localPosition = bodyRest + new Vector3(sway * 0.018f, nod * 0.009f, 0f);
            body.localRotation = bodyRotation * Quaternion.Euler(nod * 1.4f, sway * 1.5f, sway * 2.1f);
        }
        if (head)
        {
            head.localPosition = headRest + Vector3.up * (nod * 0.018f);
            head.localRotation = headRotation * Quaternion.Euler(4f + nod * 7f, sway * 3.5f, Mathf.Sin(phase * 0.5f) * 3f);
        }
        Vector3 drag = new Vector3(Mathf.Sin(phase * 0.47f) * 0.046f, 0f, Mathf.Sin(phase * 0.73f) * 0.031f);
        if (mouseHand) mouseHand.localPosition = mouseHandRest + drag;
        if (mouse) mouse.localPosition = mouseRest + drag;
        if (keyboardHand) keyboardHand.localPosition = keyboardHandRest + new Vector3(Mathf.Sin(phase * 0.37f) * 0.025f, Mathf.Max(0, Mathf.Sin(phase * 2f)) * 0.013f, 0f);
        Arm(leftShoulder, keyboardHand, leftUpperArm, leftForearm, -1f);
        Arm(rightShoulder, mouseHand, rightUpperArm, rightForearm, 1f);
        if (accentLight) accentLight.intensity = 0.85f + 0.07f * Mathf.Sin(phase * 0.25f);
    }

    static void Arm(Transform shoulder, Transform hand, Transform upper, Transform lower, float side)
    {
        if (!shoulder || !hand || !upper || !lower) return;
        Vector3 elbow = Vector3.Lerp(shoulder.position, hand.position, 0.53f) + shoulder.right * (side * 0.11f) + Vector3.down * 0.13f;
        Segment(upper, shoulder.position, elbow, 0.055f);
        Segment(lower, elbow, hand.position, 0.047f);
    }

    static void Segment(Transform part, Vector3 from, Vector3 to, float radius)
    {
        part.position = (from + to) * 0.5f;
        part.rotation = Quaternion.FromToRotation(Vector3.up, to - from);
        part.localScale = new Vector3(radius * 2f, Vector3.Distance(from, to) * 0.5f, radius * 2f);
    }

    void VideoPrepared(VideoPlayer source) { if (isActiveAndEnabled) source.Play(); }
    void VideoError(VideoPlayer source, string error)
    {
        Debug.LogWarning("[Pavouk workshop] Video could not play: " + error, this);
        preparedRequested = true; // Avoid retrying a broken clip every frame.
    }

    void SampleScreen()
    {
        nextSampleTime = Time.unscaledTime + 0.15f;
        if (!screenTexture || !sampleTexture) return;
        Graphics.Blit(screenTexture, sampleTexture);
        if (SystemInfo.supportsAsyncGPUReadback)
        {
            readbackPending = true;
            int requestedGeneration = generation;
            pendingReadback = AsyncGPUReadback.Request(sampleTexture, 0, TextureFormat.RGBA32, result =>
            {
                if (disposed || requestedGeneration != generation) return;
                readbackPending = false;
                if (result.hasError) return;
                var pixels = result.GetData<Color32>();
                Color sum = Color.black;
                for (int i = 0; i < pixels.Length; i++) sum += (Color)pixels[i];
                if (pixels.Length > 0) screenColor = (sum / pixels.Length).linear;
            });
        }
        else
        {
            // Tiny readback only, throttled to 3 Hz on hardware without async support.
            nextSampleTime = Time.unscaledTime + 0.33f;
            if (!cpuSample) cpuSample = new Texture2D(8, 8, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = sampleTexture;
                cpuSample.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                cpuSample.Apply(false);
                Color sum = Color.black;
                foreach (var pixel in cpuSample.GetPixels()) sum += pixel;
                screenColor = (sum / 64f).linear;
            }
            finally { RenderTexture.active = previous; }
        }
    }

    void OnDisable()
    {
        disposed = true;
        generation++;
        if (readbackPending) pendingReadback.WaitForCompletion();
        readbackPending = false;
        preparedRequested = false;
        currentGain = 0f;
        if (music) { music.Stop(); music.volume = 0f; music.mute = true; }
        if (video)
        {
            video.prepareCompleted -= VideoPrepared;
            video.errorReceived -= VideoError;
            video.Stop(); video.targetTexture = null;
        }
        if (monitor) monitor.SetPropertyBlock(null);
        if (screenTexture) { screenTexture.Release(); Destroy(screenTexture); }
        if (sampleTexture) { sampleTexture.Release(); Destroy(sampleTexture); }
        if (cpuSample) Destroy(cpuSample);
        if (body) { body.localPosition = bodyRest; body.localRotation = bodyRotation; }
        if (head) { head.localPosition = headRest; head.localRotation = headRotation; }
        if (mouseHand) mouseHand.localPosition = mouseHandRest;
        if (keyboardHand) keyboardHand.localPosition = keyboardHandRest;
        if (mouse) mouse.localPosition = mouseRest;
    }
}
