using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Netcode;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.SceneManagement;

// Logovani vykonu (Nastaveni > HRA > Logovani vykonu). Do souboru <persistentDataPath>/PerfLogs/perf_<datum>.txt pise:
//  - hlavicku: pocitac, grafika, rozliseni, kvalita, vsync...
//  - kazdou sekundu souhrn (radek "SEC"): FPS, prumerny / nejhorsi / 99% snimek, alokace a GC, pamet, vykreslovani,
//    zvuky, sit (RTT), stav zapasu, hrdina a jeho poloha,
//  - kazdy zasek (radek "HITCH"): snimek vyrazne delsi nez obvykle + co se v nem delo (GC, fyzika, alokace...),
//  - udalosti (radek "EVT"): zmena stavu zapasu, POTG / killcam, nacteni sceny, ztrata fokusu okna,
//  - chyby a varovani z konzole (radek "LOG"), kazda zprava (bez cisel) nejvys 5x,
//  - u zaseku radek "TOP": ktere casti hry (skripty Update(), fyzika, vykreslovani, nacitani, GC...) v tom snimku
//    zabraly nejvic casu - podle profilovacich znacek Unity (v editoru a vyvojovem buildu; jinak radek chybi).
public class PerfLogger : MonoBehaviour
{
    static PerfLogger instance;

    public static string Folder => Path.Combine(Application.persistentDataPath, "PerfLogs");
    public static string CurrentFile { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; CurrentFile = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => Apply();

    // Zapne / vypne podle nastaveni (vola i GameSettings pri zmene).
    public static void Apply()
    {
        bool on = GameSettings.PerfLogEnabled;
        if (on && instance == null)
        {
            var go = new GameObject("PerfLogger");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<PerfLogger>();
        }
        else if (!on && instance != null)
            Destroy(instance.gameObject);
    }

    // Vlastni udalost odjinud (volitelne).
    public static void Event(string text)
    {
        if (instance != null) instance.Line("EVT", text);
    }

    StreamWriter writer;
    readonly StringBuilder sb = new StringBuilder(512);
    float startTime;

    ProfilerRecorder gcAlloc, gcReserved, gcUsed, sysMem, drawCalls, batches, setPass, triangles, mainThread, renderThread;

    // snimky
    float avgMs = 16f;
    int lastGc0, lastGc1, lastGc2;
    int fixedSteps, fixedStepsThisFrame;

    // sekunda
    readonly List<float> secFrames = new List<float>(512);
    float secStart, secWorst;
    long secAlloc;
    int secGc0, secGc2, secHitches, secMaxFixed;

    // stav pro udalosti
    string lastState = "";
    bool lastFocus = true;

    // profilovaci znacky (cas v ms za snimek) - skripty a vybrane casti enginu
    const int MaxMarkers = 1500;
    readonly List<ProfilerRecorder> markers = new List<ProfilerRecorder>();
    readonly List<string> markerNames = new List<string>();
    readonly HashSet<string> knownMarkers = new HashSet<string>();
    readonly List<ProfilerRecorderHandle> handles = new List<ProfilerRecorderHandle>();
    float nextMarkerScan;
    static readonly string[] EngineKeys =
    {
        "Physics", "GC.", "Shader", "Camera.Render", "RenderPipeline", "Render", "Loading", "Resources", "Animator",
        "Animation", "Director", "NavMesh", "Audio", "Canvas", "Layout", "Gfx.", "WaitFor", "Particle", "Light",
        "Culling", "Instantiate", "Destroy", "TextMeshPro", "TMP", "Netcode", "Network", "EditorLoop", "Inspector",
        "Console", "Update.", "PreLateUpdate", "PostLateUpdate", "FixedUpdate", "Skinned", "Mesh", "Texture",
    };
    static readonly HashSet<string> Umbrellas = new HashSet<string> { "PlayerLoop", "Main Thread" };

    void ScanMarkers()
    {
        nextMarkerScan = Time.realtimeSinceStartup + 15f;
        if (markers.Count >= MaxMarkers) return;
        handles.Clear();
        try { ProfilerRecorderHandle.GetAvailable(handles); } catch { return; }
        foreach (var handle in handles)
        {
            if (markers.Count >= MaxMarkers) break;
            var description = ProfilerRecorderHandle.GetDescription(handle);
            if (description.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
            string name = description.Name;
            if (string.IsNullOrEmpty(name) || knownMarkers.Contains(name)) continue;
            bool wanted = description.Category.Name == "Scripts";
            if (!wanted)
                foreach (var key in EngineKeys)
                    if (name.Contains(key)) { wanted = true; break; }
            if (!wanted) continue;
            knownMarkers.Add(name);
            ProfilerRecorder recorder;
            try { recorder = ProfilerRecorder.StartNew(description.Category, name, 1); }
            catch { continue; }
            if (!recorder.Valid) continue;
            markers.Add(recorder);
            markerNames.Add(name);
        }
    }

    readonly List<KeyValuePair<double, string>> top = new List<KeyValuePair<double, string>>();

    string TopMarkers()
    {
        top.Clear();
        for (int i = 0; i < markers.Count; i++)
        {
            var r = markers[i];
            if (!r.Valid || Umbrellas.Contains(markerNames[i])) continue;
            double ms = r.LastValue / 1e6;
            if (ms >= 1.0) top.Add(new KeyValuePair<double, string>(ms, markerNames[i]));
        }
        if (top.Count == 0) return null;
        top.Sort((a, b) => b.Key.CompareTo(a.Key));
        var text = new StringBuilder();
        for (int i = 0; i < top.Count && i < 16; i++)
            text.Append(i == 0 ? "" : " | ").Append(top[i].Value).Append(' ').Append(top[i].Key.ToString("0.0"));
        return text.ToString();
    }

    // zpravy z konzole (prichazi i z jinych vlaken)
    readonly Queue<string> pendingLogs = new Queue<string>();
    readonly Dictionary<string, int> logCounts = new Dictionary<string, int>();

    void OnEnable()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            CurrentFile = Path.Combine(Folder, "perf_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            writer = new StreamWriter(CurrentFile, false, new UTF8Encoding(false)) { AutoFlush = false };
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PerfLogger] nejde vytvorit soubor: " + e.Message);
            enabled = false;
            return;
        }

        startTime = Time.realtimeSinceStartup;
        secStart = startTime;
        lastGc0 = GC.CollectionCount(0);
        lastGc1 = GC.CollectionCount(1);
        lastGc2 = GC.CollectionCount(2);

        gcAlloc = Start(ProfilerCategory.Memory, "GC Allocated In Frame");
        gcReserved = Start(ProfilerCategory.Memory, "GC Reserved Memory");
        gcUsed = Start(ProfilerCategory.Memory, "GC Used Memory");
        sysMem = Start(ProfilerCategory.Memory, "System Used Memory");
        drawCalls = Start(ProfilerCategory.Render, "Draw Calls Count");
        batches = Start(ProfilerCategory.Render, "Batches Count");
        setPass = Start(ProfilerCategory.Render, "SetPass Calls Count");
        triangles = Start(ProfilerCategory.Render, "Triangles Count");
        mainThread = Start(ProfilerCategory.Internal, "Main Thread");
        renderThread = Start(ProfilerCategory.Internal, "Render Thread");

        WriteHeader();
        ScanMarkers();
        Application.logMessageReceivedThreaded += OnLog;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        Application.logMessageReceivedThreaded -= OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        foreach (var r in new[] { gcAlloc, gcReserved, gcUsed, sysMem, drawCalls, batches, setPass, triangles, mainThread, renderThread })
            if (r.Valid) r.Dispose();
        foreach (var r in markers)
            if (r.Valid) r.Dispose();
        markers.Clear();
        markerNames.Clear();
        knownMarkers.Clear();
        if (writer != null)
        {
            Line("EVT", "logovani vypnuto");
            writer.Flush();
            writer.Dispose();
            writer = null;
        }
    }

    static ProfilerRecorder Start(ProfilerCategory category, string name)
    {
        try { return ProfilerRecorder.StartNew(category, name, 1); }
        catch { return default; }
    }

    static long Val(ProfilerRecorder r) => r.Valid ? r.LastValue : -1;

    void WriteHeader()
    {
        var res = Screen.currentResolution;
        Line("HDR", $"OverwatchBros {Application.version}, Unity {Application.unityVersion}, {(Application.isEditor ? "EDITOR" : "BUILD")}{(Debug.isDebugBuild ? " debug" : "")}");
        Line("HDR", $"OS: {SystemInfo.operatingSystem}");
        Line("HDR", $"CPU: {SystemInfo.processorType}, {SystemInfo.processorCount} vlaken, {SystemInfo.processorFrequency} MHz; RAM {SystemInfo.systemMemorySize} MB");
        Line("HDR", $"GPU: {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsMemorySize} MB, {SystemInfo.graphicsDeviceType}, driver {SystemInfo.graphicsDeviceVersion}");
        Line("HDR", $"Okno: {Screen.width}x{Screen.height} ({Screen.fullScreenMode}), monitor {res.width}x{res.height} @ {res.refreshRateRatio.value:0.##} Hz");
        Line("HDR", $"Kvalita: {GraphicsQuality.Names[GraphicsQuality.Level]} (Unity level {QualitySettings.GetQualityLevel()} {QualitySettings.names[QualitySettings.GetQualityLevel()]}), vSync {QualitySettings.vSyncCount}, targetFrameRate {Application.targetFrameRate}, fixedDelta {Time.fixedDeltaTime * 1000f:0.#} ms, maxDelta {Time.maximumDeltaTime * 1000f:0} ms");
        Line("HDR", "SEC = souhrn za sekundu: fps | ms prumer / 99% / nejhorsi | hitch = zaseky | fix = max kroku fyziky ve snimku | alloc = GC alokace za s | gc0/gc2 = sber smeti | mono = pouzita managed pamet | sys = pamet procesu | main/render = cas vlaken (ms) | dc/batch/setpass/tris | zvuky (hraje/celkem) | net | stav | hrdina @ poloha");
        Line("HDR", "HITCH = zasek: ms snimku (prumer predtim) | gc = probehl sber smeti (generace) | fix = kroku fyziky | alloc v snimku | main/render ms | stav");
        Line("HDR", "TOP = v zaseku nejdrazsi casti (ms, vnorene znacky se prekryvaji - rodic obsahuje deti)");
        writer.Flush();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Line("EVT", $"nactena scena {scene.name} ({mode})");

    void OnApplicationFocus(bool focus)
    {
        if (focus == lastFocus) return;
        lastFocus = focus;
        Line("EVT", focus ? "okno ziskalo fokus" : "okno ztratilo fokus");
    }

    void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log) return;
        // stejna zprava s jinymi cisly (souradnice, casy) = stejna zprava
        string key = type + ":" + System.Text.RegularExpressions.Regex.Replace(condition.Length > 200 ? condition.Substring(0, 200) : condition, @"[-\d.,]+", "#");
        lock (pendingLogs)
        {
            logCounts.TryGetValue(key, out int count);
            logCounts[key] = ++count;
            if (count > 5) return;
            string text = $"{type}: {condition}";
            if ((type == LogType.Exception || type == LogType.Error) && !string.IsNullOrEmpty(stackTrace))
            {
                string[] lines = stackTrace.Split('\n');
                text += " | " + string.Join(" < ", lines, 0, Mathf.Min(4, lines.Length)).Replace("\r", "");
            }
            if (count == 5) text += "  (dalsi stejne uz se nezapisuji)";
            pendingLogs.Enqueue(text);
        }
    }

    void FixedUpdate()
    {
        fixedSteps++;
    }

    void Update()
    {
        if (writer == null) return;

        float ms = Time.unscaledDeltaTime * 1000f;
        fixedStepsThisFrame = fixedSteps;
        fixedSteps = 0;

        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        bool gcHappened = gc0 != lastGc0;
        string gcText = gc2 != lastGc2 ? "gen2" : gc1 != lastGc1 ? "gen1" : gcHappened ? "gen0" : "-";
        secGc0 += gc0 - lastGc0;
        secGc2 += gc2 - lastGc2;
        lastGc0 = gc0; lastGc1 = gc1; lastGc2 = gc2;

        long alloc = Val(gcAlloc);
        if (alloc > 0) secAlloc += alloc;

        // zasek: vyrazne delsi nez klouzavy prumer (a aspon 20 ms)
        bool hitch = Time.frameCount > 30 && ms > Mathf.Max(avgMs * 2.2f, avgMs + 10f, 20f);
        if (hitch)
        {
            secHitches++;
            Line("HITCH", $"{ms:0.0} ms (prumer {avgMs:0.0}) | gc {gcText} | fix {fixedStepsThisFrame} | alloc {Kb(alloc)} | main {Ms(mainThread)} render {Ms(renderThread)} | {State()}");
            string topText = TopMarkers();
            if (topText != null) Line("TOP", topText);
        }
        // prumer se zasekem nekazi
        avgMs = Mathf.Lerp(avgMs, hitch ? Mathf.Min(ms, avgMs * 1.5f) : ms, 0.05f);

        secFrames.Add(ms);
        if (ms > secWorst) secWorst = ms;
        if (fixedStepsThisFrame > secMaxFixed) secMaxFixed = fixedStepsThisFrame;

        lock (pendingLogs)
            while (pendingLogs.Count > 0)
                Line("LOG", pendingLogs.Dequeue());

        float now = Time.realtimeSinceStartup;
        if (now >= nextMarkerScan) ScanMarkers();   // znacky skriptu vznikaji az pri prvnim volani
        if (now - secStart >= 1f)
        {
            WriteSecond(now - secStart);
            secStart = now;
        }
    }

    void WriteSecond(float span)
    {
        int frames = secFrames.Count;
        float sum = 0f;
        foreach (float f in secFrames) sum += f;
        secFrames.Sort();
        float p99 = frames > 0 ? secFrames[Mathf.Clamp(Mathf.CeilToInt(frames * 0.99f) - 1, 0, frames - 1)] : 0f;

        int playing = 0, total = 0;
        foreach (var source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
        {
            total++;
            if (source.isPlaying) playing++;
        }

        string state = State();
        if (state != lastState)
        {
            Line("EVT", "stav: " + state);
            lastState = state;
        }

        Line("SEC", $"{frames / Mathf.Max(0.01f, span):0} fps | ms {(frames > 0 ? sum / frames : 0f):0.0} / {p99:0.0} / {secWorst:0.0} | hitch {secHitches} | fix {secMaxFixed} | alloc {Kb(secAlloc)} | gc0 {secGc0} gc2 {secGc2} | mono {Mb(Val(gcUsed))}/{Mb(Val(gcReserved))} sys {Mb(Val(sysMem))} | main {Ms(mainThread)} render {Ms(renderThread)} | dc {Val(drawCalls)} batch {Val(batches)} setpass {Val(setPass)} tris {Val(triangles) / 1000}k | zvuky {playing}/{total} | {Net()} | {state} | {LocalHero()}");

        secFrames.Clear();
        secWorst = 0f;
        secAlloc = 0;
        secGc0 = secGc2 = secHitches = secMaxFixed = 0;
        writer.Flush();
    }

    static string State()
    {
        var match = MatchManager.Instance;
        string s = match == null || !match.IsSpawned ? "menu" : match.IsLobby ? "lobby" : match.IsOver ? "konec" : "zapas";
        if (ReplayPlayer.Active) s += ReplayPlayer.IsKillcam ? "+killcam" : "+replay";
        if (PotgUI.IsShowing) s += "+potg";
        return s;
    }

    static string Net()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return "offline";
        string role = nm.IsHost ? "host" : nm.IsServer ? "server" : "klient";
        if (nm.IsServer) return $"{role} {nm.ConnectedClientsIds.Count} klientu";
        ulong rtt = 0;
        try { rtt = nm.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId); } catch { }
        return $"{role} rtt {rtt} ms";
    }

    static string LocalHero()
    {
        int players = 0, bots = 0;
        PlayerHero local = null;
        foreach (var hero in FindObjectsByType<PlayerHero>(FindObjectsSortMode.None))
        {
            if (hero.isBot.Value) bots++; else players++;
            if (hero.IsOwner && !hero.isBot.Value) local = hero;
        }
        string who = local != null && local.Hero != null ? $"{local.Hero.heroName} @ {local.transform.position.x:0},{local.transform.position.y:0},{local.transform.position.z:0}" : "-";
        return $"{players} hracu {bots} botu | {who}";
    }

    static string Kb(long bytes) => bytes < 0 ? "?" : bytes < 1024 ? bytes + " B" : (bytes / 1024) + " kB";
    static string Mb(long bytes) => bytes < 0 ? "?" : (bytes / (1024 * 1024)) + " MB";
    static string Ms(ProfilerRecorder r) => r.Valid && r.LastValue > 0 ? (r.LastValue / 1e6).ToString("0.0") : "?";

    void Line(string tag, string text)
    {
        if (writer == null) return;
        sb.Clear();
        sb.Append((Time.realtimeSinceStartup - startTime).ToString("0000.000")).Append(' ')
          .Append('#').Append(Time.frameCount).Append(' ')
          .Append(tag).Append(' ').Append(text);
        writer.WriteLine(sb.ToString());
    }
}
