using System;
using UnityEngine;

// Placeholder zvuky generovane kodem. Az budou skutecne nahravky, staci je pripojit misto techto.
public static class ProceduralSfx
{
    const int Rate = 44100;

    static AudioClip gunshot, empty, reload, hit, dash, leapStart, explosion, death, spawn, footstep;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        gunshot = empty = reload = hit = dash = leapStart = explosion = death = spawn = footstep = null;
    }

    public static AudioClip Gunshot => gunshot ??= Make("gunshot", 0.28f, (t, r) =>
        (Noise(r) * 0.9f * Mathf.Exp(-t * 38f) + Mathf.Sin(2f * Mathf.PI * 95f * t) * 0.6f * Mathf.Exp(-t * 22f)) * 0.9f);

    public static AudioClip Empty => empty ??= Make("empty", 0.05f, (t, r) => Noise(r) * 0.5f * Mathf.Exp(-t * 90f));

    public static AudioClip Reload => reload ??= Make("reload", 0.45f, (t, r) =>
    {
        float a = Noise(r) * Mathf.Exp(-(t % 0.2f) * 70f);
        return (t < 0.4f ? a : 0f) * 0.5f;
    });

    public static AudioClip Hit => hit ??= Make("hit", 0.07f, (t, r) => Mathf.Sin(2f * Mathf.PI * 1400f * t) * Mathf.Exp(-t * 55f) * 0.6f);

    public static AudioClip Dash => dash ??= Sweep("dash", 0.35f, 0.6f);

    public static AudioClip LeapStart => leapStart ??= Sweep("leap", 0.9f, 0.9f);

    public static AudioClip Explosion => explosion ??= MakeFiltered("explosion", 1.6f, 0.05f, 3.2f, 1f, 55f);

    public static AudioClip Death => death ??= Make("death", 0.7f, (t, r) =>
    {
        float f = Mathf.Lerp(320f, 70f, t / 0.7f);
        return Mathf.Sin(2f * Mathf.PI * f * t) * 0.5f * (1f - t / 0.7f);
    });

    public static AudioClip Spawn => spawn ??= Make("spawn", 0.35f, (t, r) =>
    {
        float f = t < 0.15f ? 520f : 780f;
        return Mathf.Sin(2f * Mathf.PI * f * t) * 0.35f * Mathf.Exp(-(t % 0.15f) * 12f);
    });

    public static AudioClip Footstep => footstep ??= Make("footstep", 0.09f, (t, r) => Noise(r) * 0.35f * Mathf.Exp(-t * 55f));

    public static void Play(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (clip != null)
            AudioSource.PlayClipAtPoint(clip, position, volume);
    }

    static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

    static AudioClip Make(string name, float seconds, Func<float, System.Random, float> sample)
    {
        int count = Mathf.CeilToInt(seconds * Rate);
        var data = new float[count];
        var rnd = new System.Random(name.GetHashCode());
        for (int i = 0; i < count; i++)
            data[i] = Mathf.Clamp(sample(i / (float)Rate, rnd), -1f, 1f);

        var clip = AudioClip.Create(name, count, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip Sweep(string name, float seconds, float volume)
    {
        int count = Mathf.CeilToInt(seconds * Rate);
        var data = new float[count];
        var rnd = new System.Random(name.GetHashCode());
        float low = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            float k = t / seconds;
            float env = Mathf.Sin(Mathf.PI * k);
            float coef = Mathf.Lerp(0.03f, 0.35f, k);
            low += coef * (Noise(rnd) - low);
            data[i] = Mathf.Clamp(low * 3f * env * volume, -1f, 1f);
        }

        var clip = AudioClip.Create(name, count, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip MakeFiltered(string name, float seconds, float coef, float decay, float volume, float boomHz)
    {
        int count = Mathf.CeilToInt(seconds * Rate);
        var data = new float[count];
        var rnd = new System.Random(name.GetHashCode());
        float low = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            low += coef * (Noise(rnd) - low);
            float boom = Mathf.Sin(2f * Mathf.PI * boomHz * t) * 0.6f;
            data[i] = Mathf.Clamp((low * 6f + boom) * Mathf.Exp(-t * decay) * volume, -1f, 1f);
        }

        var clip = AudioClip.Create(name, count, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
