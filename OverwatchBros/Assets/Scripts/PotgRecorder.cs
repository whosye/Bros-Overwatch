using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Play of the game: kazdy hrac si u sebe prubezne nahrava poslednich par sekund sveho obrazu (pohled z prvni osoby
// i s HUD) jako zmenšené JPG snimky. Kdyz zabije, server mu rekne skore te akce a klient si odlozi 5 s zaznam
// (nejlepsi akce zapasu prepise slabsi). Na konci zapasu server vybere hrace s nejlepsi akci, ten posle sve snimky
// a vsem se prehraji jako video (PotgUI).
public class PotgRecorder : NetworkBehaviour
{
    // Rozliseni zaznamu (540p). Vetsi = ostrejsi obraz, ale vic dat k preneseni po siti.
    public const int Width = 960;
    public const int Height = 540;
    public const int Fps = 15;
    // Klip ma 12 s: s 5s uvodni kartou a sekundou dojezdu to dohromady vyjde na 18s znelku.
    public const float ClipSeconds = 12f;
    const float RingSeconds = 14.5f;
    const float AfterKillSeconds = 1.5f;   // kolik zaznamu po zabiti se jeste vezme
    const int JpgQuality = 72;

    class Frame
    {
        public float time;
        public long audio;   // pozice ve zvukovem zaznamu v okamziku snimku
        public byte[] jpg;
    }

    const int AudioChunkBytes = 30000;

    // vlastnik: prubezny zaznam a nejlepsi odlozeny klip
    readonly List<Frame> ring = new List<Frame>();
    List<byte[]> bestClip;
    byte[] bestAudio;
    int bestAudioRate;
    PotgAudioTap audioTap;
    int bestScore;
    int pendingScore;
    float pendingAt = -1f;
    RenderTexture screenTarget, smallTarget;
    float nextCapture;
    PlayerHero hero;

    // server: udalosti hrace za posledni chvili (zabiti, poskozeni, leceni, bonusy) a nejlepsi dosazene skore akce
    public const int CategoryNone = 0, CategoryMultiKill = 1, CategoryUltimate = 2, CategoryShutdown = 3,
        CategorySharpshooter = 4, CategoryLifesaver = 5, CategoryFinalBlow = 6;

    struct Event
    {
        public float time;
        public float points;
        public int category;
    }

    const float WindowSeconds = ClipSeconds - AfterKillSeconds;
    const float KillPoints = 100f;
    const float ComboPoints = 50f;        // za kazde dalsi zabiti v rade
    const float DamagePoints = 0.4f;      // za bod poskozeni
    const float HealPoints = 0.7f;        // za bod vyleceny spoluhraci
    const float SelfHealPoints = 0.2f;
    const float UltimatePoints = 50f;     // zabiti ultimatkou
    const float ShutdownPoints = 75f;     // zabiti nepritele, ktery zrovna pouzival ultimatku
    const float LongRangePoints = 30f;    // zabiti z dalky
    const float FinalBlowPoints = 50f;    // zabiti, ktere rozhodlo zapas
    const float StunPoints = 20f;
    const float LongRangeMeters = 25f;

    readonly List<float> killTimes = new List<float>();
    readonly List<Event> events = new List<Event>();
    float lastUltimateTime = -100f;
    float lastHealHighlight = -100f;
    public int ServerBestScore { get; private set; }
    public int ServerBestCategory { get; private set; }

    void Awake()
    {
        hero = GetComponent<PlayerHero>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
            StartCoroutine(CaptureLoop());
    }

    public override void OnNetworkDespawn()
    {
        Release(ref screenTarget);
        Release(ref smallTarget);
    }

    static void Release(ref RenderTexture target)
    {
        if (target == null) return;

        target.Release();
        Destroy(target);
        target = null;
    }

    // ---------------- vlastnik: nahravani ----------------

    bool ShouldRecord()
    {
        var match = MatchManager.Instance;
        return match != null && !match.IsLobby && !match.IsOver && hero != null && !hero.IsJoining
            && Screen.width > 16 && Screen.height > 16 && SystemInfo.supportsAsyncGPUReadback;
    }

    IEnumerator CaptureLoop()
    {
        var endOfFrame = new WaitForEndOfFrame();
        while (true)
        {
            yield return endOfFrame;

            if (!ShouldRecord() || Time.unscaledTime < nextCapture) continue;
            nextCapture = Time.unscaledTime + 1f / Fps;

            if (screenTarget == null || screenTarget.width != Screen.width || screenTarget.height != Screen.height)
            {
                Release(ref screenTarget);
                screenTarget = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            }

            if (smallTarget == null)
                smallTarget = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);

            ScreenCapture.CaptureScreenshotIntoRenderTexture(screenTarget);
            Graphics.Blit(screenTarget, smallTarget);

            // Zvuk hry se odposlouchava u AudioListeneru (kamera hrace).
            if (audioTap == null)
            {
                // Prednostne posluchac na kamere tohohle hrace (ve scene muze byt i jiny).
                var controller = GetComponent<FirstPersonController>();
                var listener = controller != null && controller.playerCamera != null
                    ? controller.playerCamera.GetComponent<AudioListener>()
                    : null;
                if (listener == null || !listener.isActiveAndEnabled)
                    listener = FindAnyObjectByType<AudioListener>();
                if (listener != null)
                {
                    audioTap = listener.GetComponent<PotgAudioTap>();
                    if (audioTap == null)
                        audioTap = listener.gameObject.AddComponent<PotgAudioTap>();
                }
            }

            float stamp = Time.unscaledTime;
            long audioPosition = audioTap != null ? audioTap.Position : 0;
            AsyncGPUReadback.Request(smallTarget, 0, TextureFormat.RGB24, request => OnReadback(request, stamp, audioPosition));
        }
    }

    void OnReadback(AsyncGPUReadbackRequest request, float stamp, long audioPosition)
    {
        if (request.hasError) return;

        byte[] pixels = request.GetData<byte>().ToArray();

        // Komprese bezi mimo hlavni vlakno, at nahravani nebrzdi hru.
        Task.Run(() =>
        {
            byte[] jpg = ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8_UNorm, Width, Height, 0, JpgQuality);
            lock (ring)
            {
                ring.Add(new Frame { time = stamp, audio = audioPosition, jpg = jpg });
                ring.RemoveAll(frame => frame.time < stamp - RingSeconds);
            }
        });
    }

    void Update()
    {
        if (!IsOwner || pendingAt < 0f || Time.unscaledTime < pendingAt) return;

        pendingAt = -1f;
        SaveClip(pendingScore);
        pendingScore = 0;
    }

    // Odlozi poslednich ClipSeconds zaznamu jako klip s danym skore (kdyz neni horsi nez uz odlozeny).
    void SaveClip(int score)
    {
        if (score < bestScore) return;

        List<Frame> frames;
        lock (ring)
            frames = new List<Frame>(ring);
        if (frames.Count < Fps) return;

        frames.Sort((a, b) => a.time.CompareTo(b.time));
        float from = frames[frames.Count - 1].time - ClipSeconds;

        var clip = new List<byte[]>();
        long audioFrom = -1, audioTo = 0;
        foreach (var frame in frames)
        {
            if (frame.time < from) continue;

            clip.Add(frame.jpg);
            if (audioFrom < 0) audioFrom = frame.audio;
            audioTo = frame.audio;
        }

        bestClip = clip;
        bestScore = score;

        // Zvuk ke stejnemu useku (plus delka posledniho snimku).
        bestAudio = null;
        if (audioTap != null && audioFrom >= 0)
        {
            bestAudioRate = audioTap.SampleRate;
            bestAudio = audioTap.Extract(audioFrom, audioTo + bestAudioRate / Fps);
        }
    }

    // ---------------- server: hodnoceni akci ----------------

    // Hodnoceni podobne jako v Overwatchi: pocita se, co hrac stihl behem par sekund.
    //   zabiti 100, kazde dalsi v rade +50, poskozeni 0,4 za bod, leceni spoluhracu 0,7 za bod,
    //   zabiti ultimatkou +50, zabiti nepritele pri jeho ultimatce +75, zabiti z dalky +30,
    //   rozhodujici zabiti zapasu +50, omraceni +20.

    void AddEvent(float points, int category = CategoryNone)
    {
        events.Add(new Event { time = Time.time, points = points, category = category });
    }

    // Vola Combat pri kazdem zpusobenem poskozeni.
    public void ServerAddDamage(float amount)
    {
        if (IsServer && amount > 0f)
            AddEvent(amount * DamagePoints);
    }

    // Vola lecive pole: kolik zivotu hrac vylecil (sobe / spoluhraci).
    public void ServerAddHealing(float amount, bool self)
    {
        if (!IsServer || amount <= 0f) return;

        AddEvent(amount * (self ? SelfHealPoints : HealPoints), self ? CategoryNone : CategoryLifesaver);

        // Velke leceni je akce samo o sobe, i bez zabiti.
        if (self || Time.time - lastHealHighlight < ClipSeconds) return;

        float now = Time.time;
        float healed = 0f;
        foreach (var e in events)
            if (now - e.time <= WindowSeconds && e.category == CategoryLifesaver)
                healed += e.points;

        if (healed >= 45f)
        {
            lastHealHighlight = now;
            Evaluate();
        }
    }

    public void ServerAddStun()
    {
        if (IsServer)
            AddEvent(StunPoints);
    }

    // Vola ultimatni schopnost, kdyz se pouzije (na serveru).
    public void ServerNoteUltimate()
    {
        if (IsServer)
            lastUltimateTime = Time.time;
    }

    static bool UsingUltimate(GameObject player)
    {
        if (player == null) return false;

        var leap = player.GetComponent<LeapStrikeAbility>();
        var boulder = player.GetComponent<BoulderAbility>();
        var visor = player.GetComponent<VisorAbility>();
        var storm = player.GetComponent<StormAbility>();
        return (storm != null && storm.enabled && storm.IsStormActive)
            || (leap != null && leap.enabled && leap.IsAirborne)
            || (boulder != null && boulder.enabled && boulder.IsRolling)
            || (visor != null && visor.enabled && visor.IsScanning);
    }

    // Vola MatchManager, kdyz tenhle hrac nekoho zabil.
    public void ServerOnKill(GameObject victim = null, bool finalBlow = false)
    {
        if (!IsServer) return;

        float now = Time.time;
        killTimes.Add(now);
        killTimes.RemoveAll(time => now - time > WindowSeconds);

        if (killTimes.Count > 1)
            AddEvent(ComboPoints, CategoryMultiKill);
        if (now - lastUltimateTime <= WindowSeconds || UsingUltimate(gameObject))
            AddEvent(UltimatePoints, CategoryUltimate);
        if (UsingUltimate(victim))
            AddEvent(ShutdownPoints, CategoryShutdown);
        if (victim != null && Vector3.Distance(victim.transform.position, transform.position) >= LongRangeMeters)
            AddEvent(LongRangePoints, CategorySharpshooter);
        if (finalBlow)
            AddEvent(FinalBlowPoints, CategoryFinalBlow);

        Evaluate();
    }

    // Spocita skore posledni chvile a rekne vlastnikovi, at si zaznam odlozi.
    void Evaluate()
    {
        float now = Time.time;
        events.RemoveAll(e => now - e.time > WindowSeconds);
        killTimes.RemoveAll(time => now - time > WindowSeconds);

        float total = killTimes.Count * KillPoints;
        var byCategory = new float[CategoryFinalBlow + 1];
        foreach (var e in events)
        {
            total += e.points;
            byCategory[e.category] += e.points;
        }

        // Nadpis akce: nejvyraznejsi bonus.
        int category = CategoryNone;
        float strongest = 0f;
        for (int i = 1; i < byCategory.Length; i++)
            if (byCategory[i] > strongest)
            {
                strongest = byCategory[i];
                category = i;
            }

        int score = Mathf.RoundToInt(total);
        if (score >= ServerBestScore)
        {
            ServerBestScore = score;
            ServerBestCategory = category;
        }

        SaveHighlightClientRpc(score);
    }

    [ClientRpc]
    void SaveHighlightClientRpc(int score)
    {
        if (!IsOwner) return;

        // Jeste chvili se nahrava, at je videt i dohra akce.
        if (pendingAt < 0f || score >= pendingScore)
        {
            pendingScore = score;
            pendingAt = Time.unscaledTime + AfterKillSeconds;
        }
    }

    // Novy zapas: vsechno od zacatku.
    public void ServerReset()
    {
        if (!IsServer) return;

        killTimes.Clear();
        events.Clear();
        lastUltimateTime = -100f;
        lastHealHighlight = -100f;
        ServerBestScore = 0;
        ServerBestCategory = CategoryNone;
        ResetClientRpc();
    }

    [ClientRpc]
    void ResetClientRpc()
    {
        PotgUI.Stop();
        if (!IsOwner) return;

        bestClip = null;
        bestAudio = null;
        bestScore = 0;
        pendingAt = -1f;
        pendingScore = 0;
        lock (ring)
            ring.Clear();
    }

    // ---------------- konec zapasu: prenos a prehrani klipu ----------------

    // Vola MatchManager na serveru u hrace s nejlepsi akci.
    public void ServerStartPotg()
    {
        if (IsServer)
            RequestClipClientRpc();
    }

    [ClientRpc]
    void RequestClipClientRpc()
    {
        if (!IsOwner) return;

        // Posledni zabiti mohlo ukoncit zapas driv, nez se klip stihl odlozit.
        if (pendingAt >= 0f)
        {
            pendingAt = -1f;
            SaveClip(pendingScore);
        }

        if (bestClip != null && bestClip.Count > 0)
            StartCoroutine(SendClip(new List<byte[]>(bestClip), bestAudio));
    }

    IEnumerator SendClip(List<byte[]> clip, byte[] audio)
    {
        int audioBytes = audio != null ? audio.Length : 0;
        BeginClipServerRpc(clip.Count, Fps, SystemInfo.graphicsUVStartsAtTop, audioBytes, bestAudioRate);

        // Nejdriv zvuk (je maly), pak snimky.
        for (int offset = 0; offset < audioBytes; offset += AudioChunkBytes)
        {
            var chunk = new byte[Mathf.Min(AudioChunkBytes, audioBytes - offset)];
            System.Buffer.BlockCopy(audio, offset, chunk, 0, chunk.Length);
            AudioServerRpc(offset, chunk);
            yield return null;
        }

        // Snimky se posilaji postupne (jeden za snimek hry), aby se nezahltila sit; prehravani zacne,
        // jakmile je jich dost napred, zbytek dojde behem nej.
        for (int i = 0; i < clip.Count; i++)
        {
            FrameServerRpc(i, clip[i]);
            yield return null;
        }
    }

    [ServerRpc]
    void BeginClipServerRpc(int count, int fps, bool flipped, int audioBytes, int audioRate)
    {
        var match = MatchManager.Instance;
        if (match == null || !match.IsOver || count <= 0 || count > 400) return;
        if (audioBytes < 0 || audioBytes > 2000000 || audioRate < 8000 || audioRate > 48000)
            audioBytes = 0;

        BeginClipClientRpc(count, Mathf.Clamp(fps, 5, 60), flipped, ServerBestCategory, audioBytes, audioRate);
    }

    [ClientRpc]
    void BeginClipClientRpc(int count, int fps, bool flipped, int category, int audioBytes, int audioRate)
    {
        var team = GetComponent<PlayerTeam>();
        PotgUI.Begin(hero != null ? hero.DisplayName : "?", hero != null && hero.Hero != null ? hero.Hero.heroName : "",
            team != null ? team.teamId.Value : 0, count, fps, flipped, CategoryName(category), audioBytes, audioRate);
    }

    [ServerRpc]
    void AudioServerRpc(int offset, byte[] pcm)
    {
        if (pcm == null || pcm.Length > 100000) return;
        AudioClientRpc(offset, pcm);
    }

    [ClientRpc]
    void AudioClientRpc(int offset, byte[] pcm)
    {
        PotgUI.AddAudio(offset, pcm);
    }

    static string CategoryName(int category)
    {
        switch (category)
        {
            case CategoryMultiKill: return "VÍCENÁSOBNÉ ZABITÍ";
            case CategoryUltimate: return "ULTIMÁTKA";
            case CategoryShutdown: return "ZASTAVENÁ ULTIMÁTKA";
            case CategorySharpshooter: return "ODSTŘELOVAČ";
            case CategoryLifesaver: return "ZACHRÁNCE";
            case CategoryFinalBlow: return "ROZHODUJÍCÍ ZÁSAH";
            default: return "";
        }
    }

    [ServerRpc]
    void FrameServerRpc(int index, byte[] jpg)
    {
        if (jpg == null || jpg.Length > 300000) return;
        FrameClientRpc(index, jpg);
    }

    [ClientRpc]
    void FrameClientRpc(int index, byte[] jpg)
    {
        PotgUI.AddFrame(index, jpg);
    }
}
