using UnityEngine;

// Plapolani ohne v krbu: svetlo jemne kolisa (Perlinuv sum), plamen se trochu hybe.
public class FireFlicker : MonoBehaviour
{
    public float baseIntensity = 2.5f;
    public float amount = 0.6f;
    public float speed = 6f;
    public Transform flame;

    Light fireLight;
    Vector3 flameScale;
    float seed;

    void Start()
    {
        fireLight = GetComponent<Light>();
        seed = Random.value * 100f;
        if (flame != null)
            flameScale = flame.localScale;
    }

    void Update()
    {
        float n = Mathf.PerlinNoise(seed, Time.time * speed);
        if (fireLight != null)
            fireLight.intensity = baseIntensity + (n - 0.5f) * 2f * amount;
        if (flame != null)
            flame.localScale = new Vector3(flameScale.x, flameScale.y * (0.8f + n * 0.4f), flameScale.z);
    }
}
