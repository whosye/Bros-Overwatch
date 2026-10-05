using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Play of the game: kazdy hrac si u sebe prubezne nahrava poslednich par sekund hry jako ZAZNAM (ReplayCapture:
// polohy hracu, vlastni kamera a HUD, efekty, vystrely, zvuky - ne video). Kdyz zabije, server mu rekne skore te
// akce a klient si odlozi 12 s zaznamu (nejlepsi akce zapasu prepise slabsi). Na konci zapasu server vybere hrace
// s nejlepsi akci, ten posle sva data (par set kB) a vsem se akce prehraje primo ve hre v plnem rozliseni (PotgUI + ReplayPlayer).
public class PotgRecorder : NetworkBehaviour
{
    // Klip ma 12 s: s 5s uvodni kartou a sekundou dojezdu to dohromady vyjde na 18s znelku.
    public const float ClipSeconds = 12f;
    const float AfterKillSeconds = 1.5f;   // kolik zaznamu po zabiti se jeste vezme
    const int ChunkBytes = 30000;
    const int MaxClipBytes = 16 * 1024 * 1024;

    // vlastnik: prubezny zaznam a nejlepsi odlozeny klip
    ReplayCapture capture;
    ReplayClip bestClip;
    int bestScore;
    int pendingScore;
    float pendingAt = -1f;
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
        {
            capture = GetComponent<ReplayCapture>();
            if (capture == null)
                capture = gameObject.AddComponent<ReplayCapture>();
        }
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
        if (score < bestScore || capture == null) return;

        var clip = capture.Extract(ClipSeconds);
        if (clip == null) return;

        bestClip = clip;
        bestScore = score;
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
        bestScore = 0;
        pendingAt = -1f;
        pendingScore = 0;
        if (capture != null)
            capture.Clear();
    }

    // ---------------- konec zapasu: prenos a prehrani klipu ----------------

    // Server: jak daleko je prenos zaznamu (MatchManager podle toho pozna, ze hrac odpadl, a vezme dalsiho v poradi).
    public bool ServerClipStarted { get; private set; }
    public bool ServerClipComplete => ServerClipStarted && serverReceived >= serverExpected;
    public float ServerLastProgress { get; private set; }
    int serverExpected, serverReceived;

    // Vola MatchManager na serveru u hrace s nejlepsi akci.
    public void ServerStartPotg()
    {
        if (!IsServer) return;

        ServerClipStarted = false;
        serverExpected = serverReceived = 0;
        ServerLastProgress = Time.unscaledTime;
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

        if (bestClip != null && bestClip.frames.Count > 1)
            StartCoroutine(SendClip(bestClip.Serialize()));
    }

    // Zaznam se posila po kouscich (jeden za snimek hry), at se nezahlti sit.
    IEnumerator SendClip(byte[] data)
    {
        BeginClipServerRpc(data.Length);
        for (int offset = 0; offset < data.Length; offset += ChunkBytes)
        {
            var chunk = new byte[Mathf.Min(ChunkBytes, data.Length - offset)];
            System.Buffer.BlockCopy(data, offset, chunk, 0, chunk.Length);
            ChunkServerRpc(offset, chunk);
            yield return null;
        }
    }

    [ServerRpc]
    void BeginClipServerRpc(int totalBytes)
    {
        var match = MatchManager.Instance;
        if (match == null || !match.IsOver || totalBytes <= 0 || totalBytes > MaxClipBytes) return;

        ServerClipStarted = true;
        serverExpected = totalBytes;
        serverReceived = 0;
        ServerLastProgress = Time.unscaledTime;
        BeginClipClientRpc(totalBytes, ServerBestCategory);
    }

    [ClientRpc]
    void BeginClipClientRpc(int totalBytes, int category)
    {
        var team = GetComponent<PlayerTeam>();
        PotgUI.Begin(hero != null ? hero.DisplayName : "?", hero != null && hero.Hero != null ? hero.Hero.heroName : "",
            team != null ? team.teamId.Value : 0, totalBytes, CategoryName(category), hero != null ? hero.heroId.Value : -1);
    }

    [ServerRpc]
    void ChunkServerRpc(int offset, byte[] data)
    {
        if (data == null || data.Length > ChunkBytes || offset < 0 || offset > MaxClipBytes) return;

        serverReceived += data.Length;
        ServerLastProgress = Time.unscaledTime;
        ChunkClientRpc(offset, data);
    }

    [ClientRpc]
    void ChunkClientRpc(int offset, byte[] data)
    {
        PotgUI.AddChunk(offset, data);
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

}
