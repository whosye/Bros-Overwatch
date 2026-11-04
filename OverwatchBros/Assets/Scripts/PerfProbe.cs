#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Video;

// Mereni vykonu bez cteni cisel ve hre (jen v editoru a vyvojovem buildu). Kazdou sekundu zapise do Logs/perf.log
// casy snimku, pocty vykreslovani a polohu hrace. klavesa P = test na miste: postupne na chvili vypne svetla chaty, stiny,
// televizi, stiny predmetu v chate a snizi rozliseni, zmeri kazdy krok a vse vrati zpet. Vysledek jde do stejneho logu.
public class PerfProbe : MonoBehaviour
{
    const float SettleSeconds = 1f;
    const float MeasureSeconds = 3f;

    class Step
    {
        public string name;
        public System.Action apply, revert;
    }

    class Samples
    {
        public readonly List<float> frame = new List<float>(), main = new List<float>(), render = new List<float>(), gpu = new List<float>();
        public long batches, setPass, tris, casters, skinned, gc;
        public int count;

        public void Clear()
        {
            frame.Clear(); main.Clear(); render.Clear(); gpu.Clear();
            batches = setPass = tris = casters = skinned = gc = 0;
            count = 0;
        }
    }

    StreamWriter log;
    ProfilerRecorder batchesRec, setPassRec, trisRec, castersRec, skinnedRec, gcRec;
    readonly FrameTiming[] timing = new FrameTiming[1];
    readonly Samples second = new Samples(), stepSamples = new Samples();
    float nextLine;
    bool testing;
    string status = "";
    float statusUntil;
    Bounds cottage;
    bool hasCottage;
    GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("PerfProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<PerfProbe>();
    }

    void OnEnable()
    {
#if UNITY_EDITOR
        string folder = Path.Combine(Application.dataPath, "..", "Logs");
#else
        string folder = Application.persistentDataPath;
#endif
        Directory.CreateDirectory(folder);
        log = new StreamWriter(Path.Combine(folder, "perf.log"), true, Encoding.UTF8) { AutoFlush = true };   // (pripisuje, kazde spusteni ma hlavicku)
        log.WriteLine($"# {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}  GPU: {SystemInfo.graphicsDeviceName}  CPU: {SystemInfo.processorType}  RAM: {SystemInfo.systemMemorySize} MB");
        log.WriteLine($"# okno {Screen.width}x{Screen.height}  editor: {Application.isEditor}  kvalita: {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
        log.WriteLine("# cas | misto | snimek median/nejhorsi ms | main | render | gpu | batches | setpass | tris | stiny | skin | GC kB/s");

        batchesRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        setPassRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        trisRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        castersRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
        skinnedRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Visible Skinned Meshes Count");
        gcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
    }

    void OnDisable()
    {
        batchesRec.Dispose();
        setPassRec.Dispose();
        trisRec.Dispose();
        castersRec.Dispose();
        skinnedRec.Dispose();
        gcRec.Dispose();
        log?.Dispose();
        log = null;
    }

    void Update()
    {
        Collect(second);
        if (testing) Collect(stepSamples);

        if (!testing && Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
            StartCoroutine(RunTest());

        if (Time.unscaledTime < nextLine) return;
        nextLine = Time.unscaledTime + 1f;
        if (log != null && second.count > 0)
            log.WriteLine($"{Time.unscaledTime,7:0.0} | {Place()} | {Describe(second)} | {Scene()}");
        second.Clear();
    }

    void Collect(Samples s)
    {
        s.frame.Add(Time.unscaledDeltaTime * 1000f);
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
        {
            s.main.Add((float)timing[0].cpuMainThreadFrameTime);
            s.render.Add((float)timing[0].cpuRenderThreadFrameTime);
            s.gpu.Add((float)timing[0].gpuFrameTime);
        }
        s.batches += batchesRec.LastValue;
        s.setPass += setPassRec.LastValue;
        s.tris += trisRec.LastValue;
        s.casters += castersRec.LastValue;
        s.skinned += skinnedRec.LastValue;
        s.gc += gcRec.LastValue;
        s.count++;
    }

    static float Median(List<float> values)
    {
        if (values.Count == 0) return 0f;
        values.Sort();
        return values[values.Count / 2];
    }

    static float Worst(List<float> values)
    {
        float worst = 0f;
        foreach (float v in values) worst = Mathf.Max(worst, v);
        return worst;
    }

    static string Describe(Samples s)
    {
        float worst = Worst(s.frame);
        float total = 0f;
        foreach (float v in s.frame) total += v / 1000f;
        long n = System.Math.Max(1, s.count);
        return $"{Median(s.frame),5:0.0}/{worst,6:0.0} | {Median(s.main),5:0.0} | {Median(s.render),5:0.0} | {Median(s.gpu),5:0.0}"
            + $" | {s.batches / n,5} | {s.setPass / n,4} | {s.tris / n,8} | {s.casters / n,5} | {s.skinned / n,3}"
            + $" | {s.gc / 1024f / Mathf.Max(0.01f, total),7:0}";
    }

    // Kolik veci prave existuje: hromadi se efekty a zvuky v boji? (jednou za sekundu, mereni to nezkresli)
    static string Scene()
    {
        return $"hraci {FindObjectsByType<PlayerHero>().Length} castice {FindObjectsByType<ParticleSystem>().Length}"
            + $" zvuky {FindObjectsByType<AudioSource>().Length} svetla {FindObjectsByType<Light>().Length}";
    }

    string Place()
    {
        var cam = Camera.main;
        if (cam == null) return "bez kamery";
        Vector3 p = cam.transform.position;
        if (!hasCottage) FindCottage();
        string where = hasCottage && cottage.Contains(p) ? "CHATA" : "venku";
        return $"{where} ({p.x:0},{p.y:0},{p.z:0})";
    }

    void FindCottage()
    {
        var root = GameObject.Find("HlavniChata");
        if (root == null) return;
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        cottage = renderers[0].bounds;
        foreach (var r in renderers) cottage.Encapsulate(r.bounds);
        hasCottage = true;
    }

    // ---------------- test na miste (P) ----------------

    IEnumerator RunTest()
    {
        testing = true;
        var steps = BuildSteps();
        log?.WriteLine($"# ==== TEST v {Place()} ====");
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            status = $"TEST VÝKONU {i + 1}/{steps.Count}: {step.name} - stůj a nehýbej se";
            statusUntil = float.MaxValue;
            step.apply?.Invoke();
            yield return new WaitForSecondsRealtime(SettleSeconds);
            stepSamples.Clear();
            yield return new WaitForSecondsRealtime(MeasureSeconds);
            log?.WriteLine($"TEST {step.name,-28} | {Describe(stepSamples)}");
            step.revert?.Invoke();
        }
        log?.WriteLine("# ==== KONEC TESTU ====");
        status = "Test výkonu hotový, výsledek je v Logs/perf.log";
        statusUntil = Time.unscaledTime + 5f;
        testing = false;
    }

    List<Step> BuildSteps()
    {
        var steps = new List<Step> { new Step { name = "zaklad" } };

        var root = GameObject.Find("HlavniChata");

        var cottageLights = new List<Light>();
        if (root != null)
            foreach (var light in root.GetComponentsInChildren<Light>())
                if (light.enabled) cottageLights.Add(light);
        steps.Add(new Step
        {
            name = "bez svetel chaty",
            apply = () => { foreach (var l in cottageLights) if (l != null) l.enabled = false; },
            revert = () => { foreach (var l in cottageLights) if (l != null) l.enabled = true; },
        });

        var suns = new List<Light>();
        var sunShadows = new List<LightShadows>();
        foreach (var light in FindObjectsByType<Light>())
            if (light.type == LightType.Directional && light.enabled && light.shadows != LightShadows.None)
            {
                suns.Add(light);
                sunShadows.Add(light.shadows);
            }
        steps.Add(new Step
        {
            name = "bez stinu slunce",
            apply = () => { foreach (var l in suns) if (l != null) l.shadows = LightShadows.None; },
            revert = () => { for (int i = 0; i < suns.Count; i++) if (suns[i] != null) suns[i].shadows = sunShadows[i]; },
        });

        var casters = new List<Renderer>();
        var casterModes = new List<ShadowCastingMode>();
        if (root != null)
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                if (renderer.shadowCastingMode != ShadowCastingMode.Off)
                {
                    casters.Add(renderer);
                    casterModes.Add(renderer.shadowCastingMode);
                }
        steps.Add(new Step
        {
            name = "chata bez vrhani stinu",
            apply = () => { foreach (var r in casters) if (r != null) r.shadowCastingMode = ShadowCastingMode.Off; },
            revert = () => { for (int i = 0; i < casters.Count; i++) if (casters[i] != null) casters[i].shadowCastingMode = casterModes[i]; },
        });

        var videos = new List<VideoPlayer>();
        foreach (var video in FindObjectsByType<VideoPlayer>())
            if (video.isPlaying) videos.Add(video);
        steps.Add(new Step
        {
            name = "bez videi",
            apply = () => { foreach (var v in videos) if (v != null) v.Pause(); },
            revert = () => { foreach (var v in videos) if (v != null) v.Play(); },
        });

        // Polovicni rozliseni: kdyz hodne pomuze, brzdi vykon grafiky (pixely, svetla), ne pocet objektu.
        var pipeline = UniversalRenderPipeline.asset;
        if (pipeline != null)
        {
            float scale = pipeline.renderScale;
            steps.Add(new Step
            {
                name = "polovicni rozliseni",
                apply = () => pipeline.renderScale = scale * 0.5f,
                revert = () => pipeline.renderScale = scale,
            });
        }

        steps.Add(new Step { name = "zaklad znovu" });
        return steps;
    }

    void OnGUI()
    {
        if (string.IsNullOrEmpty(status) || Time.unscaledTime > statusUntil) return;
        if (style == null)
            style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = Color.yellow } };
        GUI.Label(new Rect(20f, 20f, 1400f, 40f), status, style);
    }
}
#endif
