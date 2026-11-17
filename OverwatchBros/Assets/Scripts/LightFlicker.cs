using UnityEngine;

// Mihotave svetlo (pochoden / ohen v jeskyni): intenzita a trochu i barva kolisaji podle sumu, kazde svetlo jinak.
[RequireComponent(typeof(Light))]
public class LightFlicker : MonoBehaviour
{
    [Range(0f, 1f)] public float amount = 0.3f;
    public float speed = 6f;

    Light lightSource;
    float baseIntensity, seed;
    Color baseColor;

    void Awake()
    {
        lightSource = GetComponent<Light>();
        baseIntensity = lightSource.intensity;
        baseColor = lightSource.color;
        seed = Random.value * 100f;
    }

    void Update()
    {
        float t = Time.time * speed + seed;
        float n = Mathf.PerlinNoise(t, seed) * 0.7f + Mathf.PerlinNoise(t * 2.7f, seed + 10f) * 0.3f;   // 0..1
        float k = 1f - amount + n * amount * 2f;
        lightSource.intensity = baseIntensity * k;
        lightSource.color = Color.Lerp(baseColor * 0.85f, baseColor, n);
    }
}
