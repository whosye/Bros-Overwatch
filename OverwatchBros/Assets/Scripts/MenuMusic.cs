using Unity.Netcode;
using UnityEngine;

// One persistent local music stream; lobby changes its filter, not its playback position.
[DisallowMultipleComponent]
public class MenuMusic : MonoBehaviour
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 0.22f;
    static MenuMusic instance;
    AudioSource source;
    AudioLowPassFilter lowpass;
    float gain;
    float silentTime;
    bool paused;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        if (instance == null) instance = this;
        if (instance != this) return;
        // Keep the primary stream off the object carrying the old audio callback.
        var legacy = GetComponent<AudioSource>();
        if (legacy != null) legacy.Stop();
        var scriptedFilter = GetComponent<MenuMusicDsp>();
        if (scriptedFilter != null) scriptedFilter.enabled = false;
        var playback = transform.Find("MusicPlayback");
        if (playback == null)
        {
            playback = new GameObject("MusicPlayback").transform;
            playback.SetParent(transform, false);
        }
        source = playback.GetComponent<AudioSource>();
        if (source == null) source = playback.gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.priority = 32;
        source.volume = volume;
        lowpass = playback.GetComponent<AudioLowPassFilter>();
        if (lowpass == null) lowpass = playback.gameObject.AddComponent<AudioLowPassFilter>();
        lowpass.cutoffFrequency = 850f;
        var echo = playback.GetComponent<AudioEchoFilter>();
        if (echo == null) echo = playback.gameObject.AddComponent<AudioEchoFilter>();
        echo.delay = 240f;
        echo.decayRatio = 0.28f;
        echo.wetMix = 0.12f;
        echo.dryMix = 1f;
    }

    void Update()
    {
        if (source == null || clip == null) return;
        var network = NetworkManager.Singleton;
        var match = MatchManager.Instance;
        bool connected = network != null && network.IsClient && network.IsListening && match != null && match.IsSpawned;
        var player = connected && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        var hero = player != null ? player.GetComponent<PlayerHero>() : null;
        bool lobby = connected && (match.IsLobby || (hero != null && hero.IsJoining));
        bool wanted = GameSettings.MusicEnabled && (!connected || lobby) && !PotgIntro.Active;
        if (network != null && network.IsListening && !network.IsClient) wanted = false;

        gain = Mathf.MoveTowards(gain, wanted ? 1f : 0f, Time.unscaledDeltaTime / 0.8f);
        lowpass.cutoffFrequency = Mathf.Lerp(lowpass.cutoffFrequency, lobby ? 2300f : 850f, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.5f));
        source.volume = Mathf.Max(0.0001f, Mathf.Clamp01(volume) * gain);
        if (wanted)
        {
            silentTime = 0f;
            if (!source.isPlaying)
            {
                if (paused) source.UnPause();
                else
                {
                    source.clip = clip;
                    source.Play();
                }
                paused = false;
                Debug.Log($"[Music] Playing {clip.name}, volume={volume:0.00}, master={AudioListener.volume:0.00}, enabled={GameSettings.MusicEnabled}.");
            }
        }
        else if (gain <= 0f)
        {
            // Keep the audio graph alive while its delay drains, then release the stream.
            silentTime += Time.unscaledDeltaTime;
            if (silentTime >= 3f && source.isPlaying)
            {
                source.Pause();
                paused = true;
            }
        }
    }

    void OnDisable()
    {
        if (source != null) source.Stop();
        gain = 0f;
        paused = false;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}

