using UnityEngine;

// Odposlech vysledneho zvuku hry pro "play of the game": komponenta sedi u AudioListeneru (tam Unity pousti
// do OnAudioFilterRead cely smichany zvuk) a drzi si poslednich par sekund jako mono se snizenou vzorkovaci frekvenci.
public class PotgAudioTap : MonoBehaviour
{
    const float RingSeconds = 16f;

    short[] ring;
    long written;          // celkovy pocet zapsanych (zredenych) vzorku
    int decimation = 2;
    readonly object gate = new object();

    public int SampleRate { get; private set; }

    // Pozice na zvukove ose: podle ni se zvuk paruje se snimky videa.
    public long Position
    {
        get { lock (gate) return written; }
    }

    void Awake()
    {
        int output = AudioSettings.outputSampleRate;
        decimation = output >= 40000 ? 2 : 1;
        SampleRate = output / decimation;
        ring = new short[Mathf.CeilToInt(SampleRate * RingSeconds)];
    }

    // Bezi ve zvukovem vlakne.
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (ring == null || channels <= 0) return;

        lock (gate)
        {
            int frames = data.Length / channels;
            for (int i = 0; i + decimation <= frames; i += decimation)
            {
                float sum = 0f;
                for (int k = 0; k < decimation; k++)
                    for (int c = 0; c < channels; c++)
                        sum += data[(i + k) * channels + c];

                float sample = Mathf.Clamp(sum / (decimation * channels), -1f, 1f);
                ring[(int)(written % ring.Length)] = (short)(sample * 32767f);
                written++;
            }
        }
    }

    // Vzorky z useku [from, to) jako 16bit PCM (little endian). Co uz v zasobniku neni, se vynecha.
    public byte[] Extract(long from, long to)
    {
        lock (gate)
        {
            to = System.Math.Min(to, written);
            from = System.Math.Max(from, written - ring.Length);
            if (to <= from) return new byte[0];

            var bytes = new byte[(to - from) * 2];
            for (long i = from; i < to; i++)
            {
                short sample = ring[(int)(i % ring.Length)];
                long at = (i - from) * 2;
                bytes[at] = (byte)(sample & 0xFF);
                bytes[at + 1] = (byte)((sample >> 8) & 0xFF);
            }

            return bytes;
        }
    }
}
