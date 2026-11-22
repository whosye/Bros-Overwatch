using UnityEngine;
using UnityEngine.Rendering;

// Prostredi mapy se zapina spolu s jejim korenem; po odchodu vrati puvodni nastaveni sceny.
public sealed class MapEnvironment : MonoBehaviour
{
    public Material skybox;
    public Color ambientSky = new Color(0.7f, 0.78f, 0.9f);
    public Color ambientEquator = new Color(0.65f, 0.62f, 0.55f);
    public Color ambientGround = new Color(0.42f, 0.36f, 0.27f);
    public Texture customReflection;
    public float reflectionIntensity = 0.25f;
    public Light sun;
    bool applied;
    Material previousSky;
    AmbientMode previousMode;
    Color previousSkyColor, previousEquator, previousGround;
    float previousReflection;
    DefaultReflectionMode previousReflectionMode;
    Texture previousReflectionTexture;
    bool previousSunEnabled;
    Light previousSun;
    readonly System.Collections.Generic.List<Light> previousSuns = new();

    void OnEnable() { if (Application.isPlaying) Apply(); }
    void OnDisable() => Restore();

    public void Apply()
    {
        if (applied || skybox == null) return;
        previousSky = RenderSettings.skybox;
        previousMode = RenderSettings.ambientMode;
        previousSkyColor = RenderSettings.ambientSkyColor;
        previousEquator = RenderSettings.ambientEquatorColor;
        previousGround = RenderSettings.ambientGroundColor;
        previousReflection = RenderSettings.reflectionIntensity;
        previousReflectionMode = RenderSettings.defaultReflectionMode;
        previousReflectionTexture = RenderSettings.customReflectionTexture;
        previousSun = RenderSettings.sun;
        applied = true;
        if (sun != null)
        {
            previousSunEnabled = sun.enabled;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light != sun && light.type == LightType.Directional && light.enabled)
                { previousSuns.Add(light); light.enabled = false; }
            sun.enabled = true;
            RenderSettings.sun = sun;
        }
        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
        if (customReflection != null)
        {
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = customReflection;
        }
        RenderSettings.reflectionIntensity = reflectionIntensity;
        if (Application.isPlaying) DynamicGI.UpdateEnvironment();
    }

    public void Restore()
    {
        if (!applied) return;
        if (sun != null) sun.enabled = previousSunEnabled;
        if (sun != null) RenderSettings.sun = previousSun;
        foreach (var light in previousSuns) if (light != null) light.enabled = true;
        previousSuns.Clear();
        RenderSettings.skybox = previousSky;
        RenderSettings.ambientMode = previousMode;
        RenderSettings.ambientSkyColor = previousSkyColor;
        RenderSettings.ambientEquatorColor = previousEquator;
        RenderSettings.ambientGroundColor = previousGround;
        RenderSettings.reflectionIntensity = previousReflection;
        if (customReflection != null)
        {
            RenderSettings.defaultReflectionMode = previousReflectionMode;
            RenderSettings.customReflectionTexture = previousReflectionTexture;
        }
        applied = false;
        if (Application.isPlaying) DynamicGI.UpdateEnvironment();
    }
}
