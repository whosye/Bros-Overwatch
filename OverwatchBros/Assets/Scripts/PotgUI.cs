using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Prehravani "play of the game" na konci zapasu: uvodni karta se jmenem hrace a pak 5 s zaznamu jeho obrazovky
// (snimky posila PotgRecorder). Kresli se pres vsechno ostatni UI.
public class PotgUI : MonoBehaviour
{
    enum Phase { Hidden, Intro, Playing, Outro }

    const float IntroSeconds = 2f;
    const float OutroSeconds = 0.8f;
    const float MaxWaitSeconds = 8f;

    static PotgUI instance;

    GameObject overlay;
    RawImage video;
    TextMeshProUGUI introTitle, introName, introCategory, cornerLabel;
    Texture2D texture;

    // Zvuk klipu: 16bit mono PCM, sklada se z kousku.
    AudioSource audioSource;
    byte[] audioData;
    int audioReceived;
    int audioRate;
    AudioClip audioClip;

    public static bool AudioPlaying => instance != null && instance.audioSource != null && instance.audioSource.isPlaying;
    public static float AudioSeconds => instance != null && instance.audioClip != null ? instance.audioClip.length : 0f;
    public static float AudioPeak { get; private set; }

    Phase phase = Phase.Hidden;
    byte[][] frames;
    int received;
    int fps = 15;
    float phaseStart;
    int shown = -1;

    public static bool IsShowing => instance != null && instance.phase != Phase.Hidden;
    public static bool IsPlaying => instance != null && instance.phase == Phase.Playing;
    public static int ReceivedFrames => instance != null ? instance.received : 0;

    void Awake()
    {
        instance = this;

        var canvasObject = new GameObject("PotgCanvas");
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var center = new Vector2(0.5f, 0.5f);
        var background = UiKit.MakeImage(canvasObject.transform, "PotgOverlay", Color.black, center, Vector2.zero, Vector2.zero);
        UiKit.Stretch(background.rectTransform);
        overlay = background.gameObject;

        var videoObject = new GameObject("Video", typeof(RectTransform), typeof(RawImage));
        videoObject.transform.SetParent(overlay.transform, false);
        video = videoObject.GetComponent<RawImage>();
        video.raycastTarget = false;
        UiKit.Stretch(video.rectTransform);

        introTitle = UiKit.MakeText(overlay.transform, "Title", "PLAY OF THE GAME", 96, TextAlignmentOptions.Center, center,
            new Vector2(0f, 70f), new Vector2(1700f, 130f), UiKit.Accent);
        introName = UiKit.MakeText(overlay.transform, "Name", "", 60, TextAlignmentOptions.Center, center,
            new Vector2(0f, -50f), new Vector2(1700f, 90f));
        introCategory = UiKit.MakeText(overlay.transform, "Category", "", 40, TextAlignmentOptions.Center, center,
            new Vector2(0f, -130f), new Vector2(1700f, 60f), UiKit.Muted);
        cornerLabel = UiKit.MakeText(overlay.transform, "Corner", "", 34, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
            new Vector2(40f, -30f), new Vector2(1200f, 100f));

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        overlay.SetActive(false);
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public static void Begin(string playerName, string heroName, int team, int count, int framesPerSecond, bool flipped, string category = "",
        int audioBytes = 0, int audioSampleRate = 0)
    {
        if (instance == null) return;

        instance.BeginInternal(playerName, heroName, team, count, framesPerSecond, flipped, category);
        instance.audioData = audioBytes > 0 ? new byte[audioBytes] : null;
        instance.audioReceived = 0;
        instance.audioRate = audioSampleRate;
    }

    public static void AddAudio(int offset, byte[] pcm)
    {
        if (instance == null || instance.audioData == null || pcm == null) return;
        if (offset < 0 || offset + pcm.Length > instance.audioData.Length) return;

        System.Buffer.BlockCopy(pcm, 0, instance.audioData, offset, pcm.Length);
        instance.audioReceived += pcm.Length;
    }

    // Slozi zvuk klipu a pusti ho spolu s videem.
    void StartAudio()
    {
        AudioPeak = 0f;
        if (audioData == null || audioReceived < audioData.Length || audioRate <= 0) return;

        int count = audioData.Length / 2;
        if (count < audioRate / 10) return;

        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            short value = (short)(audioData[i * 2] | (audioData[i * 2 + 1] << 8));
            samples[i] = value / 32768f;
            AudioPeak = Mathf.Max(AudioPeak, Mathf.Abs(samples[i]));
        }

        if (audioClip != null)
            Destroy(audioClip);
        audioClip = AudioClip.Create("potg", count, 1, audioRate, false);
        audioClip.SetData(samples, 0);

        audioSource.clip = audioClip;
        audioSource.volume = 1f;
        audioSource.Play();
    }

    public static void AddFrame(int index, byte[] jpg)
    {
        if (instance == null || instance.frames == null || index < 0 || index >= instance.frames.Length) return;

        if (instance.frames[index] == null)
            instance.received++;
        instance.frames[index] = jpg;
    }

    public static void Stop()
    {
        if (instance != null)
            instance.Hide();
    }

    void BeginInternal(string playerName, string heroName, int team, int count, int framesPerSecond, bool flipped, string category)
    {
        introCategory.text = category;
        frames = new byte[count][];
        received = 0;
        fps = Mathf.Max(1, framesPerSecond);
        shown = -1;

        string color = ColorUtility.ToHtmlStringRGB(UiKit.TeamColor(team));
        string who = string.IsNullOrEmpty(heroName) ? playerName : $"{playerName}  ·  {heroName}";
        introName.text = $"<color=#{color}>{who}</color>";
        cornerLabel.text = $"<color=#F28C1A>PLAY OF THE GAME</color>\n<size=80%><color=#{color}>{who}</color></size>"
            + (string.IsNullOrEmpty(category) ? "" : $"\n<size=65%><color=#A6B3C7>{category}</color></size>");

        // Zaznam obrazovky je na nekterych grafickych API vzhuru nohama.
        video.uvRect = flipped ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);

        SetPhase(Phase.Intro);
        overlay.SetActive(true);
    }

    void SetPhase(Phase next)
    {
        phase = next;
        phaseStart = Time.unscaledTime;

        bool intro = next == Phase.Intro;
        introTitle.gameObject.SetActive(intro);
        introName.gameObject.SetActive(intro);
        introCategory.gameObject.SetActive(intro);
        cornerLabel.gameObject.SetActive(!intro);
        video.enabled = !intro;
    }

    void Hide()
    {
        phase = Phase.Hidden;
        frames = null;
        received = 0;
        audioData = null;
        if (audioSource != null)
            audioSource.Stop();
        if (overlay != null)
            overlay.SetActive(false);
    }

    void Update()
    {
        if (phase == Phase.Hidden) return;

        // Hrac opustil hru nebo zacal novy zapas.
        var network = NetworkManager.Singleton;
        var match = MatchManager.Instance;
        if (network == null || !network.IsListening || match == null || !match.IsOver)
        {
            Hide();
            return;
        }

        float elapsed = Time.unscaledTime - phaseStart;

        if (phase == Phase.Intro)
        {
            // Ceka se na vsechny snimky (nejdyl par sekund, pak se prehraje, co dorazilo).
            bool audioReady = audioData == null || audioReceived >= audioData.Length;
            bool ready = (received >= frames.Length && audioReady) || elapsed > MaxWaitSeconds;
            if (elapsed >= IntroSeconds && ready)
            {
                if (received == 0)
                {
                    Hide();
                }
                else
                {
                    SetPhase(Phase.Playing);
                    StartAudio();
                }
            }
            return;
        }

        if (phase == Phase.Playing)
        {
            int index = Mathf.FloorToInt(elapsed * fps);
            if (index >= frames.Length)
            {
                SetPhase(Phase.Outro);
                return;
            }

            if (index != shown && frames[index] != null)
            {
                shown = index;
                texture.LoadImage(frames[index]);
                video.texture = texture;
            }
            return;
        }

        if (elapsed >= OutroSeconds)
            Hide();
    }
}
