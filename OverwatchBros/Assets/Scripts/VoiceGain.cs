using UnityEngine;

// Zesileni hlasu primo ve zvukovem vlakne (AudioSource.volume jde jen do 1). Pouziva HeroVoice pri hlasce
// k ultimatce: celkova hlasitost se ztlumi a hlas ultimatky se o tolik zesili zpet, aby vynikl nad ostatnim.
public class VoiceGain : MonoBehaviour
{
    public volatile float gain = 1f;

    void OnAudioFilterRead(float[] data, int channels)
    {
        float g = gain;
        if (g == 1f) return;
        for (int i = 0; i < data.Length; i++)
            data[i] *= g;
    }
}
