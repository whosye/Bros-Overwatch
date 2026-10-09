using System;
using UnityEngine;

// Placeholder zvuky generovane kodem. Az budou skutecne nahravky, staci je pripojit misto techto.
public static class ProceduralSfx
{
    const int Rate = 44100;

    static AudioClip gunshot, empty, reload, hit, dash, leapStart, explosion, death, spawn, footstep, hurt, stun, stunConfirm, captureTick, captureWon, captureUnlock, potgIntro, ultCharge, splash, bigSplash, waterRush, jokerJingle;
    static AudioClip bowRelease, arrowFlight;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        gunshot = empty = reload = hit = dash = leapStart = explosion = death = spawn = footstep = hurt = null;
        bowRelease = arrowFlight = null;
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

    // Short string snap with a little air, rather than the long ability whoosh.
    public static AudioClip BowRelease => bowRelease ??= Make("bowRelease", 0.12f, (t, r) =>
    {
        float attack = Mathf.Clamp01(t / 0.002f);
        float tail = Mathf.Clamp01((0.12f - t) / 0.025f);
        float stringTone = Mathf.Sin(2f * Mathf.PI * (680f * t - 1300f * t * t))
            + 0.25f * Mathf.Sin(2f * Mathf.PI * 1380f * t);
        return attack * tail * (stringTone * 0.42f * Mathf.Exp(-t * 48f)
            + Noise(r) * 0.32f * Mathf.Exp(-t * 65f));
    });

    // A finite, non-looping air hiss: even a missed cleanup can never play indefinitely.
    public static AudioClip ArrowFlight => arrowFlight ??= MakeArrowFlight();

    public static AudioSource PlayArrowFlight(Transform arrow)
    {
        var source = arrow.gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.clip = ArrowFlight;
        source.loop = false;
        source.volume = 0.1f;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 0.8f;
        source.maxDistance = 12f;
        source.dopplerLevel = 0f;
        source.priority = 180;
        source.Play();
        source.SetScheduledEndTime(AudioSettings.dspTime + 10.0);
        return source;
    }

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

    // Priprava ultimatky (zastupny zvuk, dokud hrdina nema vlastni nahravku): stoupajici ton s chvenim.
    public static AudioClip UltCharge => ultCharge ??= Make("ultCharge", 1.0f, (t, r) =>
    {
        // Kmitocet stoupa ze 180 na 720 Hz (faze je integral kmitoctu).
        float phase = 2f * Mathf.PI * (180f * t + 270f * t * t);
        float tremolo = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 14f * t);
        return (Mathf.Sin(phase) * 0.5f + Noise(r) * 0.08f) * tremolo * Mathf.Min(1f, t * 8f) * Mathf.Min(1f, (1f - t) * 10f + 0.2f);
    });

    // Splouchnuti (dopad do vody z toboganu): sumivy sum s hlubokym zbuchnutim.
    public static AudioClip Splash => splash ??= MakeFiltered("splash", 0.9f, 0.3f, 4.5f, 0.7f, 70f);

    // Velky splouch (dopad do jezirka): delsi a hlubsi.
    public static AudioClip BigSplash => bigSplash ??= MakeFiltered("bigSplash", 1.8f, 0.22f, 2.4f, 0.9f, 45f);

    // Zastupna znelka buffu Respin Joker: vyherni automat - vzestupne arpeggio, cinkani minci a zaverecny akord.
    public static AudioClip JokerJingle => jokerJingle ??= MakeJingle();

    static AudioClip MakeJingle()
    {
        const float seconds = 3.6f;
        int count = Mathf.CeilToInt(seconds * Rate);
        var data = new float[count];
        var rnd = new System.Random(777);

        // tony arpeggia (C dur nahoru o dve oktavy), kazdy 0,11 s
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f, 1568f, 2093f };
        void Tone(float start, float freq, float length, float volume)
        {
            int a = (int)(start * Rate), n = (int)(length * Rate);
            for (int i = 0; i < n && a + i < count; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t * 200f) * Mathf.Exp(-t * 6f / length);
                // "osmibitovy" ton: sinus s trochou ctverce
                float s = Mathf.Sin(2f * Mathf.PI * freq * t);
                data[a + i] += (s * 0.7f + Mathf.Sign(s) * 0.3f) * env * volume;
            }
        }
        for (int r = 0; r < 2; r++)
            for (int k = 0; k < notes.Length; k++)
                Tone(r * 0.85f + k * 0.11f, notes[k], 0.16f, 0.32f);
        // cinkani minci
        for (int k = 0; k < 14; k++)
            Tone(1.7f + (float)rnd.NextDouble() * 1.1f, 2600f + (float)rnd.NextDouble() * 1600f, 0.09f, 0.18f);
        // zaverecny akord
        foreach (var f in new[] { 523.25f, 659.25f, 783.99f, 1046.5f })
            Tone(2.15f, f, 1.4f, 0.22f);

        for (int i = 0; i < count; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
        var clip = AudioClip.Create("jokerJingle", count, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Huceni vody behem jizdy (smycka): filtrovany sum s vlnenim.
    public static AudioClip WaterRush => waterRush ??= MakeRush();

    static AudioClip MakeRush()
    {
        const float seconds = 2f;
        int count = Mathf.CeilToInt(seconds * Rate);
        var data = new float[count];
        var rnd = new System.Random(1234);
        float low = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate;
            low += 0.12f * (Noise(rnd) - low);
            // vlneni s periodou delky smycky, at navaz neni slyset
            float swell = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * t * 3f / seconds);
            data[i] = Mathf.Clamp(low * 2.6f * swell, -1f, 1f);
        }
        // plynule napojeni konce na zacatek
        int fade = Rate / 20;
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            data[count - fade + i] = Mathf.Lerp(data[count - fade + i], data[i], k);
        }
        var clip = AudioClip.Create("waterRush", count, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    public static AudioClip Footstep => footstep ??= Make("footstep", 0.09f, (t, r) => Noise(r) * 0.35f * Mathf.Exp(-t * 55f));

    // Zvuk efektu v miste deje s omezenym dosahem (pres celou mapu jsou slyset jen ultimatky - Fx.PlayGlobal).
    // Drive AudioSource.PlayClipAtPoint: logaritmicky utlum s dosahem 500 m = vystrely a vybuchy slyset vsude.
    public static void Play(AudioClip clip, Vector3 position, float volume = 1f)
    {
        // Hlasite zvuky dal (vybuch, vystrel), drobne bliz.
        Play(clip, position, volume, clip == explosion ? 60f : clip == gunshot ? 45f : 30f);
    }

    public static void Play(AudioClip clip, Vector3 position, float volume, float maxDistance, bool spatial = true)
    {
        if (clip == null) return;
        ReplayLog.Sfx(clip, position, volume);

        var go = new GameObject("SFX_" + clip.name);
        go.transform.position = position;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = spatial ? 1f : 0f;
        source.panStereo = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 3f;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;
        source.Play();
        UnityEngine.Object.Destroy(go, clip.length + 0.1f);
    }

    static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

    static AudioClip MakeArrowFlight()
    {
        float fast = 0f, slow = 0f;
        return Make("arrowFlight", 10f, (t, r) =>
        {
            float noise = Noise(r);
            fast += 0.32f * (noise - fast);
            slow += 0.09f * (noise - slow);
            float envelope = Mathf.Clamp01(t / 0.04f) * Mathf.Clamp01((10f - t) / 0.08f);
            float flutter = 0.9f + 0.1f * Mathf.Sin(2f * Mathf.PI * 23f * t);
            return (fast - slow) * 1.8f * envelope * flutter;
        });
    }

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
