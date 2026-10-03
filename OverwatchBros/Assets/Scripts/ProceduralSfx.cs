using System;
using UnityEngine;

// Placeholder zvuky generovane kodem. Az budou skutecne nahravky, staci je pripojit misto techto.
public static class ProceduralSfx
{
    const int Rate = 44100;

    static AudioClip gunshot, empty, reload, hit, dash, leapStart, explosion, death, spawn, footstep, hurt, stun, stunConfirm, captureTick, captureWon, captureUnlock, potgIntro;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        gunshot = empty = reload = hit = dash = leapStart = explosion = death = spawn = footstep = hurt = null;
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

    public static AudioClip Hurt => hurt ??= Make("hurt", 0.22f, (t, r) =>
    {
        float f = Mathf.Lerp(230f, 120f, t / 0.22f);
        return (Mathf.Sin(2f * Mathf.PI * f * t) * 0.6f + Noise(r) * 0.25f) * Mathf.Exp(-t * 14f);
    });

    // Omraceni: prasknuti a doznivajici piskani v usich (slysi vsichni kolem omraceneho).
    public static AudioClip Stun => stun ??= Make("stun", 0.9f, (t, r) =>
        Noise(r) * 0.7f * Mathf.Exp(-t * 45f)
        + (Mathf.Sin(2f * Mathf.PI * 2900f * t) * 0.30f + Mathf.Sin(2f * Mathf.PI * 3350f * t) * 0.22f) * Mathf.Exp(-t * 3.2f));

    // Potvrzeni pro toho, kdo omracil: dva stoupajici tony.
    public static AudioClip StunConfirm => stunConfirm ??= Make("stunConfirm", 0.22f, (t, r) =>
        Mathf.Sin(2f * Mathf.PI * (t < 0.09f ? 880f : 1320f) * t) * 0.55f * Mathf.Exp(-(t < 0.09f ? t : t - 0.09f) * 22f));

    // Dobyvani bodu: tikani pri zabirani, fanfara pri zabrani, znelka pri odemceni bodu.
    public static AudioClip CaptureTick => captureTick ??= Make("captureTick", 0.12f, (t, r) =>
        Mathf.Sin(2f * Mathf.PI * 740f * t) * 0.5f * Mathf.Exp(-t * 38f));

    public static AudioClip CaptureWon => captureWon ??= Make("captureWon", 1.1f, (t, r) =>
    {
        // Tri stoupajici tony a dozvuk posledniho.
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
        int index = Mathf.Min(3, (int)(t / 0.16f));
        float local = t - index * 0.16f;
        float decay = index < 3 ? Mathf.Exp(-local * 9f) : Mathf.Exp(-local * 3.5f);
        return (Mathf.Sin(2f * Mathf.PI * notes[index] * t) * 0.45f + Mathf.Sin(2f * Mathf.PI * notes[index] * 2f * t) * 0.12f) * decay;
    });

    public static AudioClip CaptureUnlock => captureUnlock ??= Make("captureUnlock", 0.4f, (t, r) =>
        Mathf.Sin(2f * Mathf.PI * (t < 0.15f ? 440f : 660f) * t) * 0.5f * Mathf.Exp(-(t < 0.15f ? t : t - 0.15f) * 12f));

    // Znelka k uvodni karte "play of the game" (vlastni, generovana): buben a stoupajici akord s dozvukem.
    public static AudioClip PotgIntro => potgIntro ??= Make("potgIntro", 2.2f, (t, r) =>
    {
        float drum = Mathf.Sin(2f * Mathf.PI * (90f - 40f * Mathf.Min(1f, t * 6f)) * t) * Mathf.Exp(-t * 7f) * 0.7f;

        float[] notes = { 293.66f, 369.99f, 440f, 587.33f };
        float chord = 0f;
        for (int i = 0; i < notes.Length; i++)
        {
            float start = 0.12f + i * 0.14f;
            if (t < start) continue;

            float local = t - start;
            float envelope = Mathf.Min(1f, local * 25f) * Mathf.Exp(-local * 1.4f);
            chord += (Mathf.Sin(2f * Mathf.PI * notes[i] * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * notes[i] * 2f * t)
                + 0.15f * Mathf.Sin(2f * Mathf.PI * notes[i] * 3f * t)) * envelope;
        }

        return Mathf.Clamp(drum + chord * 0.16f, -1f, 1f);
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
