using UnityEngine;

// The editor bakes these values into ordinary scene objects/materials. No runtime generator.
[CreateAssetMenu(menuName = "BrosOverwatch/Vzhled mapy")]
public sealed class MapFinishSettings : ScriptableObject
{
    [Header("Slunecny den kolem poledne")]
    [Range(35f, 75f), Tooltip("Vyska slunce nad horizontem ve stupnich.")]
    public float sunElevation = 56f;
    [Range(0f, 360f)] public float sunAzimuth = 145f;
    [Range(0.5f, 2f)] public float sunIntensity = 1.35f;
    [Range(4500f, 7500f)] public float sunTemperature = 5900f;
    [Range(0.5f, 1.5f)] public float ambientIntensity = 1f;
    [Range(-0.5f, 0.5f)] public float exposure = 0.2f;
    [Range(-20f, 10f)] public float saturation = -2f;

    [Header("Povrchy")]
    public Color lawnTint = new Color(0.72f, 0.88f, 0.60f);
    [Range(1f, 5f), Tooltip("Velikost jednoho opakovani textury v metrech.")]
    public float grassTextureMetres = 2f;
    [Range(0.2f, 1.2f)] public float pathEdgeWidth = 0.65f;

    [Header("Drobne dekorace bez kolizi")]
    [Range(0, 1600)] public int grassClumps = 1000;
    [Range(0, 160)] public int wildflowerClumps = 100;
    public bool windowBoxes = true;
    public bool gardenPots = true;
    public int decorationSeed = 13102026;
}
