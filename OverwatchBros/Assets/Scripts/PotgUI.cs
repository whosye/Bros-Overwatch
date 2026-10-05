using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Prehravani "play of the game" na konci zapasu: uvodni karta se jmenem hrace a pak jeho akce prehrana primo ve hre
// (data posila PotgRecorder, prehrava ReplayPlayer - v plnem rozliseni, s jeho HUD a zvuky hry).
public class PotgUI : MonoBehaviour
{
    enum Phase { Hidden, Intro, Playing, Outro }

    // Karta se jmenem hrace trva 5 s (uvod znelky), pak 12 s zaznamu a sekunda dojezdu = 18 s jako znelka.
    const float IntroSeconds = 5f;
    const float OutroSeconds = 1f;
    const float MaxWaitSeconds = 10f;

    static PotgUI instance;

    GameObject overlay;
    Image background;
    Image bottomShade;

    // Uvodni scenka hrdiny (PotgIntro) misto cerne karty: texty pak vjedou zleva dole v detailu obliceje.
    bool cinematic;
    const float TextInAt = 3.0f;
    TextMeshProUGUI introTitle, introName, introCategory, cornerLabel;

    AudioSource introSource;

    // Znelka uvodni karty: vlastni nahravka z Assets/Resources/Audio/potg_intro (mp3 / wav / ogg), jinak generovana.
    static AudioClip IntroClip()
    {
        var custom = Resources.Load<AudioClip>("Audio/potg_intro");
        return custom != null ? custom : ProceduralSfx.PotgIntro;
    }

    // Data zaznamu (prichazeji po kouscich), pak se prehraji ve scene (ReplayPlayer).
    byte[] data;
    int received;

    Phase phase = Phase.Hidden;
    float phaseStart;

    public static bool IsShowing => instance != null && instance.phase != Phase.Hidden;
    public static bool IsPlaying => instance != null && instance.phase == Phase.Playing;

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
        background = UiKit.MakeImage(canvasObject.transform, "PotgOverlay", Color.black, center, Vector2.zero, Vector2.zero);
        UiKit.Stretch(background.rectTransform);
        overlay = background.gameObject;

        introTitle = UiKit.MakeText(overlay.transform, "Title", "PLAY OF THE GAME", 96, TextAlignmentOptions.Center, center,
            new Vector2(0f, 70f), new Vector2(1700f, 130f), UiKit.Accent);
        introName = UiKit.MakeText(overlay.transform, "Name", "", 60, TextAlignmentOptions.Center, center,
            new Vector2(0f, -50f), new Vector2(1700f, 90f));
        introCategory = UiKit.MakeText(overlay.transform, "Category", "", 40, TextAlignmentOptions.Center, center,
            new Vector2(0f, -130f), new Vector2(1700f, 60f), UiKit.Muted);
        cornerLabel = UiKit.MakeText(overlay.transform, "Corner", "", 34, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
            new Vector2(40f, -30f), new Vector2(1200f, 100f));

        // Tmavy pruh dole pod texty scenky (at jsou citelne).
        bottomShade = UiKit.MakeImage(overlay.transform, "BottomShade", new Color(0f, 0f, 0f, 0.6f), new Vector2(0.5f, 0f),
            new Vector2(0f, 150f), new Vector2(4000f, 300f));
        bottomShade.transform.SetSiblingIndex(0);
        bottomShade.gameObject.SetActive(false);

        introSource = gameObject.AddComponent<AudioSource>();
        introSource.playOnAwake = false;
        introSource.spatialBlend = 0f;

        overlay.SetActive(false);
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    public static void Begin(string playerName, string heroName, int team, int totalBytes, string category = "", int heroIndex = -1)
    {
        if (instance == null) return;
        instance.BeginInternal(playerName, heroName, team, totalBytes, category);

        // Scenka hrdiny; kdyz nejde (hrdina bez modelu), zustane cerna karta.
        instance.cinematic = PotgIntro.Play(heroIndex);
        instance.LayoutIntro();
    }

    // Rozlozeni textu uvodu: na cerne karte uprostred, pri scence vlevo dole.
    void LayoutIntro()
    {
        var center = new Vector2(0.5f, 0.5f);
        var left = new Vector2(0f, 0f);
        Place(introTitle, cinematic ? left : center, cinematic ? new Vector2(110f, 230f) : new Vector2(0f, 70f), cinematic ? 84 : 96);
        Place(introName, cinematic ? left : center, cinematic ? new Vector2(110f, 140f) : new Vector2(0f, -50f), cinematic ? 58 : 60);
        Place(introCategory, cinematic ? left : center, cinematic ? new Vector2(110f, 78f) : new Vector2(0f, -130f), 40);

        var alignment = cinematic ? TMPro.TextAlignmentOptions.Left : TMPro.TextAlignmentOptions.Center;
        introTitle.alignment = introName.alignment = introCategory.alignment = alignment;
        background.color = cinematic ? new Color(0f, 0f, 0f, 0f) : Color.black;
        bottomShade.gameObject.SetActive(false);
    }

    static void Place(TMPro.TextMeshProUGUI text, Vector2 anchor, Vector2 position, float size)
    {
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(anchor.x, 0.5f);
        rect.anchoredPosition = position;
        text.fontSize = size;
    }

    public static void AddChunk(int offset, byte[] chunk)
    {
        if (instance == null || instance.data == null || chunk == null) return;
        if (offset < 0 || offset + chunk.Length > instance.data.Length) return;

        System.Buffer.BlockCopy(chunk, 0, instance.data, offset, chunk.Length);
        instance.received += chunk.Length;
    }

    public static void Stop()
    {
        if (instance != null)
            instance.Hide();
    }

    void BeginInternal(string playerName, string heroName, int team, int totalBytes, string category)
    {
        introCategory.text = category;
        data = new byte[totalBytes];
        received = 0;

        string color = ColorUtility.ToHtmlStringRGB(UiKit.TeamColor(team));
        string who = string.IsNullOrEmpty(heroName) ? playerName : $"{playerName}  ·  {heroName}";
        introName.text = $"<color=#{color}>{who}</color>";
        cornerLabel.text = $"<color=#F28C1A>PLAY OF THE GAME</color>\n<size=80%><color=#{color}>{who}</color></size>"
            + (string.IsNullOrEmpty(category) ? "" : $"\n<size=65%><color=#A6B3C7>{category}</color></size>");

        SetPhase(Phase.Intro);
        overlay.SetActive(true);

        introSource.clip = IntroClip();
        introSource.volume = 0.9f;
        introSource.Play();
    }

    static void SlideIn(TMPro.TextMeshProUGUI text, float y, float k, float unused)
    {
        float ease = 1f - Mathf.Pow(1f - Mathf.Clamp01(k), 3f);
        text.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-500f, 110f, ease), y);
        text.alpha = ease;
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

        // Uvodni karta na cernem pozadi (pri scence pruhledna); pri prehravani je pozadi pruhledne (hraje se ve scene).
        background.color = intro && !cinematic ? Color.black : new Color(0f, 0f, 0f, 0f);
        if (!intro)
        {
            introTitle.alpha = introName.alpha = introCategory.alpha = 1f;
        }
    }

    void Hide()
    {
        phase = Phase.Hidden;
        data = null;
        received = 0;
        cinematic = false;
        PotgIntro.Stop();
        ReplayPlayer.Stop();
        if (introSource != null)
            introSource.Stop();
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

        // Znelka: pri uvodni karte naplno, pod akci tise (at je slyset zvuk hry), na konci do ztracena.
        if (introSource != null && introSource.isPlaying)
        {
            float wanted = phase == Phase.Intro ? 0.9f : phase == Phase.Playing ? 0.6f : 0.6f * Mathf.Clamp01(1f - elapsed / OutroSeconds);
            introSource.volume = Mathf.MoveTowards(introSource.volume, wanted, Time.unscaledDeltaTime * 1.5f);
        }

        if (phase == Phase.Intro)
        {
            // Scenka: texty vjedou zleva v detailu obliceje.
            if (cinematic)
            {
                float k = Mathf.Clamp01((elapsed - TextInAt) / 0.35f);
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                bottomShade.gameObject.SetActive(k > 0f);
                bottomShade.color = new Color(0f, 0f, 0f, 0.6f * ease);
                SlideIn(introTitle, 230f, ease, 0f);
                SlideIn(introName, 140f, Mathf.Clamp01((elapsed - TextInAt - 0.12f) / 0.35f), 0f);
                SlideIn(introCategory, 78f, Mathf.Clamp01((elapsed - TextInAt - 0.24f) / 0.35f), 0f);
            }

            bool complete = data != null && received >= data.Length;
            if (elapsed >= IntroSeconds && (complete || elapsed > MaxWaitSeconds))
            {
                ReplayClip clip = null;
                if (complete)
                {
                    try { clip = ReplayClip.Deserialize(data); }
                    catch (System.Exception e) { Debug.LogWarning("[POTG] Zaznam nejde precist: " + e.Message); }
                }

                if (clip == null)
                {
                    Hide();
                    return;
                }

                PotgIntro.Stop();
                bottomShade.gameObject.SetActive(false);
                SetPhase(Phase.Playing);
                ReplayPlayer.Play(clip);
            }
            return;
        }

        if (phase == Phase.Playing)
        {
            if (ReplayPlayer.Finished)
                SetPhase(Phase.Outro);
            return;
        }

        // Dojezd: obraz plynule zcerna a konec.
        background.color = new Color(0f, 0f, 0f, Mathf.Clamp01(elapsed / OutroSeconds));
        if (elapsed >= OutroSeconds)
            Hide();
    }
}
