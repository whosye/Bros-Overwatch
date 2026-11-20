using UnityEngine;
using UnityEngine.Rendering;

// Prostredi mapy se zapina spolu s jejim korenem; po odchodu vrati puvodni nastaveni sceny.
public sealed class MapEnvironment : MonoBehaviour
{
    public Material skybox;
    bool applied;
    Material previousSky;
    AmbientMode previousMode;
    Color previousSkyColor, previousEquator, previousGround;
    float previousReflection;

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
        applied = true;
        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.7f, 0.78f, 0.9f);
        RenderSettings.ambientEquatorColor = new Color(0.65f, 0.62f, 0.55f);
        RenderSettings.ambientGroundColor = new Color(0.42f, 0.36f, 0.27f);
        RenderSettings.reflectionIntensity = 0.25f;
        if (Application.isPlaying) DynamicGI.UpdateEnvironment();
    }

    public void Restore()
    {
        if (!applied) return;
        RenderSettings.skybox = previousSky;
        RenderSettings.ambientMode = previousMode;
        RenderSettings.ambientSkyColor = previousSkyColor;
        RenderSettings.ambientEquatorColor = previousEquator;
        RenderSettings.ambientGroundColor = previousGround;
        RenderSettings.reflectionIntensity = previousReflection;
        applied = false;
        if (Application.isPlaying) DynamicGI.UpdateEnvironment();
    }
}
