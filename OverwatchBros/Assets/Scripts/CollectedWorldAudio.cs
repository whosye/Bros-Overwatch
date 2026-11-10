using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Scene-owned ambience: no changes to existing music/voice AudioSources or scene files.
public sealed class CollectedWorldAudio : MonoBehaviour
{
    AudioSource stream;
    WaterCurrent[] water;
    AudioListener listener;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= Loaded;
        SceneManager.sceneLoaded += Loaded;
        Loaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void Loaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var root in scene.GetRootGameObjects())
            if (root.GetComponent<CollectedWorldAudio>() != null) return;
        var go = new GameObject("CollectedWorldAudio");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<CollectedWorldAudio>();
    }

    IEnumerator Start()
    {
        // Other map bootstrap components may attach their water/fire objects in Start.
        yield return null;
        AudioSurface.RefreshOverlays();
        water = FindObjectsByType<WaterCurrent>();
        if (water.Length > 0) stream = AddLoop(transform, "world_stream", 0.18f, 18f);
        foreach (var boiler in FindObjectsByType<Boiler>())
            AddLoop(boiler.transform, "world_boiler_fire", 0.1f, 9f);
        foreach (var t in FindObjectsByType<Transform>())
            if (t.name == "Krb" || t.name == "KamennyGril")
                AddLoop(t, "world_fireplace", 0.12f, 10f, "world_boiler_fire");
    }

    public static AudioSource AddLoop(Transform parent, string cue, float volume, float range, string fallbackCue = null)
    {
        var clip = CollectedAudioBank.Pick(cue) ?? CollectedAudioBank.Pick(fallbackCue);
        if (clip == null) return null;
        var go = new GameObject("CollectedLoop_" + cue);
        go.transform.SetParent(parent, false);
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.volume = volume;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1.5f;
        source.maxDistance = range;
        source.dopplerLevel = 0f;
        source.time = Random.Range(0f, Mathf.Max(0.01f, clip.length - 0.1f));
        source.Play();
        return source;
    }

    void LateUpdate()
    {
        if (stream == null || water == null) return;
        if (listener == null || !listener.isActiveAndEnabled)
        {
            listener = null;
            foreach (var candidate in FindObjectsByType<AudioListener>())
                if (candidate.isActiveAndEnabled) { listener = candidate; break; }
        }
        if (listener == null) return;
        float nearest = float.PositiveInfinity;
        Vector3 point = transform.position;
        foreach (var zone in water)
        {
            if (zone == null || !zone.isActiveAndEnabled || zone.slide || zone.start) continue;
            Vector3 local = zone.transform.InverseTransformPoint(listener.transform.position);
            local = new Vector3(Mathf.Clamp(local.x, -zone.size.x * 0.5f, zone.size.x * 0.5f), 0f,
                Mathf.Clamp(local.z, -zone.size.z * 0.5f, zone.size.z * 0.5f));
            Vector3 candidate = zone.transform.TransformPoint(local);
            float distance = (candidate - listener.transform.position).sqrMagnitude;
            if (distance < nearest) { nearest = distance; point = candidate; }
        }
        stream.transform.position = point;
        stream.mute = float.IsPositiveInfinity(nearest);
    }
}
