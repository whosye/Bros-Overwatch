using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Kvalita grafiky pro slabsi pocitace (Nastaveni > Grafika): Vysoka / Stredni / Nizka / Brambora.
// Meni se kopie URP assetu (puvodni asset v projektu zustava netknuty), trava a stromy terenu, post-processing
// a uroven detailu. Ulozeno v PlayerPrefs (kazdy pocitac zvlast), plati hned i po nacteni mapy.
public static class GraphicsQuality
{
    public static readonly string[] Names = { "Vysoká", "Střední", "Nízká", "Brambora" };
    const string Key = "settings.graphics";

    //                                       Vysoka  Stredni  Nizka  Brambora
    static readonly float[] RenderScale =   { 1f,     0.9f,    0.75f, 0.6f };
    static readonly float[] ShadowDist =    { 90f,    50f,     25f,   0f };
    static readonly int[] Cascades =        { 4,      2,       1,     1 };
    static readonly int[] ExtraLights =     { 4,      4,       2,     0 };
    static readonly bool[] Hdr =            { true,   true,    false, false };
    static readonly bool[] PostFx =         { true,   true,    false, false };
    static readonly float[] GrassDensity =  { 1f,     0.6f,    0.3f,  0f };   // nasobek puvodni hustoty
    static readonly float[] GrassDistance = { 1f,     0.65f,   0.4f,  0f };   // nasobek puvodni vzdalenosti
    static readonly float[] LodBias =       { 2f,     1.5f,    1f,    0.6f };

    static UniversalRenderPipelineAsset original, runtime;

    public static int Level
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(Key, 0), 0, Names.Length - 1);
        set
        {
            PlayerPrefs.SetInt(Key, Mathf.Clamp(value, 0, Names.Length - 1));
            PlayerPrefs.Save();
            Apply();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        var runner = new GameObject("GraphicsQuality");
        runner.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(runner);
        runner.AddComponent<Runner>();
        Apply();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

    public static void Apply()
    {
        int q = Level;

        // vlastni kopie URP assetu (zmeny za behu se nesmi propsat do assetu v projektu)
        if (runtime == null)
        {
            original = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (original != null)
            {
                runtime = Object.Instantiate(original);
                runtime.name = original.name + " (za behu)";
                QualitySettings.renderPipeline = runtime;
            }
        }
        if (runtime != null)
        {
            runtime.renderScale = RenderScale[q];
            runtime.shadowDistance = ShadowDist[q];
            runtime.shadowCascadeCount = Cascades[q];
            runtime.maxAdditionalLightsCount = ExtraLights[q];
            runtime.supportsHDR = Hdr[q];
            runtime.upscalingFilter = q >= 2 ? UpscalingFilterSelection.Linear : UpscalingFilterSelection.Auto;
        }
        QualitySettings.lodBias = LodBias[q];

        // hlavni (slunecni) svetlo bez stinu na "Brambore" (puvodni nastaveni si pamatuje)
        foreach (var light in Object.FindObjectsByType<Light>())
        {
            if (light.type != LightType.Directional) continue;
            if (!sunShadows.ContainsKey(light)) sunShadows[light] = light.shadows;
            light.shadows = ShadowDist[q] > 0f ? sunShadows[light] : LightShadows.None;
        }

        // trava a detaily terenu
        foreach (var terrain in Terrain.activeTerrains)
        {
            if (terrain == null) continue;
            if (!terrainGrass.ContainsKey(terrain)) terrainGrass[terrain] = (terrain.detailObjectDensity, terrain.detailObjectDistance);
            var (density, distance) = terrainGrass[terrain];
            terrain.detailObjectDensity = density * GrassDensity[q];
            terrain.detailObjectDistance = distance * GrassDistance[q];
        }

        ApplyCameras();
    }

    // Post-processing kamer (vola se i kdyz vznikne nova kamera - hrac, zaznam, uvod POTG).
    public static void ApplyCameras()
    {
        bool post = PostFx[Level];
        foreach (var camera in Camera.allCameras)
        {
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) continue;
            if (!post && data.renderPostProcessing) { data.renderPostProcessing = false; postOff.Add(data); }
            else if (post && postOff.Remove(data)) data.renderPostProcessing = true;
        }
    }

    static readonly System.Collections.Generic.Dictionary<Terrain, (float, float)> terrainGrass = new System.Collections.Generic.Dictionary<Terrain, (float, float)>();
    static readonly System.Collections.Generic.Dictionary<Light, LightShadows> sunShadows = new System.Collections.Generic.Dictionary<Light, LightShadows>();
    static readonly System.Collections.Generic.HashSet<UniversalAdditionalCameraData> postOff = new System.Collections.Generic.HashSet<UniversalAdditionalCameraData>();

    // Nove kamery (hrac, zaznam, uvod POTG) dostanou nastaveni do vteriny.
    class Runner : MonoBehaviour
    {
        float next;
        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;
            ApplyCameras();
        }
    }
}
