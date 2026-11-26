using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Prehravani "play of the game" na konci zapasu: uvodni karta se jmenem hrace a pak jeho akce prehrana primo ve hre
// (data posila PotgRecorder, prehrava ReplayPlayer - v plnem rozliseni, s jeho HUD a zvuky hry).
// Uvod = scenka hrdiny (PotgIntro) a karta jako v Overwatchi (PotgCard). Casy uvodu jdou podle hodin scenky, takze
// zpomaleni ve vrcholu scenky uvod o tu chvili prodlouzi (nic se neutne). Ukazka z editoru (PotgIntro.Preview)
// ukaze kartu a pusti znelku i bez zapasu.
public class PotgUI : MonoBehaviour
{
    enum Phase { Hidden, Intro, Playing, Outro }

    // Uvod trva 5 s (uvod znelky; se zpomalenim o chvili dele), pak 12 s zaznamu a sekunda dojezdu.
    const float IntroSeconds = 5f;
    const float OutroSeconds = 1f;
    const float MaxWaitSeconds = 10f;
    const float CardInAt = 3.0f;          // scenka: karta vjede v detailu obliceje
    const float BlackCardInAt = 0.3f;     // cerna karta (hrdina bez scenky)
    const float CardAwaySeconds = 0.25f;  // odlet karty pred strihem na zaznam

    static PotgUI instance;

    GameObject overlay;
    Image background;
    SkewBar bottomShade;
    PotgCard card;
    TextMeshProUGUI cornerLabel;

    bool cinematic;   // uvodni scenka hrdiny (jinak cerna karta)
    bool preview;     // ukazka z editoru: bez zapasu a zaznamu, konci se scenkou
    bool cardSwoosh, cardHit;

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

        // Tmavy stin vlevo dole pod kartou (doprava mizi - vpravo v zaberu zustane videt, co se deje za hrdinou).
        bottomShade = SkewBar.Make(overlay.transform, "BottomShade", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, new Vector2(1500f, 360f), 0f);
        bottomShade.gradient = true;
        bottomShade.rightColor = new Color(0f, 0f, 0f, 0f);
        bottomShade.gameObject.SetActive(false);

        card = new PotgCard(overlay.transform);
        cornerLabel = UiKit.MakeText(overlay.transform, "Corner", "", 34, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f),
            new Vector2(40f, -30f), new Vector2(1200f, 100f));

        var music = new GameObject("PotgZnelka");
        music.transform.SetParent(transform, false);
        introSource = music.AddComponent<AudioSource>();
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
        // vse, co jeste dohrava ze hry (hlasky, ultimatky), hned utichne
        HeroVoice.SilenceAll();
        Fx.StopGlobalSounds();
        instance.preview = false;
        instance.BeginInternal(playerName, heroName, team, totalBytes, category);

        // Scenka hrdiny; kdyz nejde (hrdina bez modelu), zustane cerna karta.
        instance.cinematic = PotgIntro.Play(heroIndex);
        instance.LayoutIntro();
    }

    // Ukazka z editoru (scenka uz bezi): karta s prezdivkou z menu a znelka, bez zapasu a zaznamu.
    public static void BeginPreview(string heroName)
    {
        if (instance == null) return;
        instance.preview = true;
        instance.BeginInternal(MainMenuUI.PlayerName, heroName, 0, 0, "Ukázka z editoru");
        instance.cinematic = true;
        instance.LayoutIntro();
    }

    // Karta: pri scence vlevo dole, na cerne karte uprostred.
    void LayoutIntro()
    {
        card.Place(cinematic);
        background.color = cinematic ? new Color(0f, 0f, 0f, 0f) : Color.black;
        bottomShade.gameObject.SetActive(false);
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
        data = new byte[totalBytes];
        received = 0;
        cardSwoosh = cardHit = false;

        card.Set(playerName, heroName, category, UiKit.TeamColor(team));
        card.Tick(-1f, 0f);

        string color = ColorUtility.ToHtmlStringRGB(UiKit.TeamColor(team));
        string who = string.IsNullOrEmpty(heroName) ? playerName : $"{playerName}  ·  {heroName}";
        cornerLabel.text = $"<color=#F28C1A>PLAY OF THE GAME</color>\n<size=80%><color=#{color}>{who}</color></size>"
            + (string.IsNullOrEmpty(category) ? "" : $"\n<size=65%><color=#A6B3C7>{category}</color></size>");

        SetPhase(Phase.Intro);
        overlay.SetActive(true);

        introSource.clip = IntroClip();
        introSource.volume = 0.9f;
        introSource.pitch = 1f;
        introSource.Play();
    }

    void SetPhase(Phase next)
    {
        phase = next;
        phaseStart = Time.unscaledTime;

        bool intro = next == Phase.Intro;
        card.SetVisible(intro);
        cornerLabel.gameObject.SetActive(!intro);
        if (!intro) bottomShade.gameObject.SetActive(false);

        // Uvodni karta na cernem pozadi (pri scence pruhledna); pri prehravani je pozadi pruhledne (hraje se ve scene).
        background.color = intro && !cinematic ? Color.black : new Color(0f, 0f, 0f, 0f);
    }

    void Hide()
    {
        phase = Phase.Hidden;
        data = null;
        received = 0;
        cinematic = false;
        preview = false;
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

        // Hrac opustil hru nebo zacal novy zapas (ukazka z editoru zapas nema).
        var network = NetworkManager.Singleton;
        var match = MatchManager.Instance;
        if (!preview && (network == null || !network.IsListening || match == null || !match.IsOver))
        {
            Hide();
            return;
        }

        float elapsed = Time.unscaledTime - phaseStart;

        // Znelka: pri uvodni karte naplno, pod akci tise (at je slyset zvuk hry), na konci do ztracena.
        // (zpomaleni scenky ji nemeni - nahravka hraje porad stejne)
        if (introSource != null && introSource.isPlaying)
        {
            float wanted = phase == Phase.Intro ? 0.9f : phase == Phase.Playing ? 0.6f : 0.6f * Mathf.Clamp01(1f - elapsed / OutroSeconds);
            introSource.volume = Mathf.MoveTowards(introSource.volume, wanted, Time.unscaledDeltaTime * 1.5f);
        }

        if (phase == Phase.Intro)
        {
            // hodiny uvodu: cas scenky (zpomaleni ho protahne), bez scenky realny cas
            float clock = cinematic && PotgIntro.Active ? PotgIntro.Clock : elapsed;
            float k = clock - (cinematic ? CardInAt : BlackCardInAt);
            float away = Mathf.Clamp01((clock - (IntroSeconds - CardAwaySeconds)) / CardAwaySeconds);
            card.Tick(k, away);
            if (k >= 0f && !cardSwoosh)
            {
                cardSwoosh = true;
                Fx.PlayGlobal(ProceduralSfx.Dash, 0.3f);
            }
            if (k >= 0.12f && !cardHit)
            {
                cardHit = true;
                Fx.PlayGlobal(ProceduralSfx.PotgStamp, 0.5f);
            }
            if (cinematic)
            {
                float shade = Mathf.Clamp01(k / 0.35f) * (1f - away);
                bottomShade.gameObject.SetActive(shade > 0f);
                bottomShade.color = new Color(0f, 0f, 0f, 0.72f * shade);
            }

            // ukazka: konci se scenkou (zaznam neni)
            if (preview)
            {
                if (!PotgIntro.Active) Hide();
                return;
            }

            bool complete = data != null && received >= data.Length;
            if (clock >= IntroSeconds && (complete || elapsed > MaxWaitSeconds))
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
