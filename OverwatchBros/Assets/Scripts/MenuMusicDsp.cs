using UnityEngine;

// Gain precedes the effects so muting the input preserves the audible echo tail.
// Runs on Unity's audio thread: no Unity object access or per-buffer allocation.
public class MenuMusicDsp : MonoBehaviour
{
    public volatile float inputGain;
    public volatile float cutoffHz = 650f;
    float[] delay;
    readonly float[] lowpass = new float[8];
    int cursor;
    int sampleRate;
    float smoothedGain;

    public void Configure(int rate)
    {
        sampleRate = Mathf.Max(8000, rate);
        delay = new float[Mathf.CeilToInt(sampleRate * 0.24f) * 8];
        cursor = 0;
        smoothedGain = 0f;
        System.Array.Clear(lowpass, 0, lowpass.Length);
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        var buffer = delay;
        if (buffer == null || channels < 1 || channels > 8)
        {
            System.Array.Clear(data, 0, data.Length);
            return;
        }
        float coefficient = (float)(1.0 - System.Math.Exp(-2.0 * System.Math.PI * cutoffHz / sampleRate));
        float gainTarget = inputGain;
        float smoothing = (float)(1.0 - System.Math.Exp(-1.0 / (sampleRate * 0.01)));
        for (int frame = 0; frame < data.Length; frame += channels)
        {
            smoothedGain += (gainTarget - smoothedGain) * smoothing;
            for (int channel = 0; channel < channels; channel++)
            {
                lowpass[channel] += coefficient * (data[frame + channel] * smoothedGain - lowpass[channel]);
                float echo = buffer[cursor + channel];
                buffer[cursor + channel] = lowpass[channel] + echo * 0.28f;
                data[frame + channel] = lowpass[channel] + echo * 0.18f;
            }
            cursor += 8;
            if (cursor >= buffer.Length) cursor = 0;
        }
    }
}
