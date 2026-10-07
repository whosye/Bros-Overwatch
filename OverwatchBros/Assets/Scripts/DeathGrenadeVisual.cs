using UnityEngine;

// Vizual granatu, ktery vypadne z Honzy po smrti: obloukem doleti na misto, blika a po 'delay' vybuchne.
// Poskozeni pocita server (PlayerHero.DeathGrenades), tohle je jen efekt.
public class DeathGrenadeVisual : MonoBehaviour
{
    const float FlightTime = 0.45f;

    Vector3 from, to;
    float age, delay, radius;
    Light blinker;

    public static void Spawn(Vector3 from, Vector3 to, float delay, float radius)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(go.GetComponent<Collider>());
        go.name = "DeathGrenade";
        go.transform.position = from;
        go.transform.localScale = Vector3.one * 0.22f;
        Fx.Paint(go, new Color(0.18f, 0.20f, 0.16f));

        var visual = go.AddComponent<DeathGrenadeVisual>();
        visual.from = from;
        visual.to = to + Vector3.up * 0.11f;
        visual.delay = delay;
        visual.radius = radius;

        visual.blinker = go.AddComponent<Light>();
        visual.blinker.type = LightType.Point;
        visual.blinker.color = new Color(1f, 0.35f, 0.15f);
        visual.blinker.range = 2.5f;
    }

    void Update()
    {
        age += Time.deltaTime;

        float t = Mathf.Clamp01(age / FlightTime);
        Vector3 position = Vector3.Lerp(from, to, t);
        position.y += Mathf.Sin(t * Mathf.PI) * 0.8f;
        transform.position = position;

        // Blika cim dal rychleji.
        float rate = Mathf.Lerp(3f, 14f, age / delay);
        blinker.intensity = Mathf.Repeat(age * rate, 1f) < 0.5f ? 2.5f : 0.2f;

        if (age < delay) return;

        Fx.Explosion(to, radius);
        ProceduralSfx.Play(ProceduralSfx.Explosion, to, 0.7f);
        Destroy(gameObject);
    }
}
