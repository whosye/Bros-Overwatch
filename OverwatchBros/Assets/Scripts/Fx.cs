using UnityEngine;

// Placeholder vizualni efekty generovane kodem (particles, svetlo, znacka dopadu).
public static class Fx
{
    static Material particleMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        particleMaterial = null;
    }

    static Material ParticleMaterial
    {
        get
        {
            if (particleMaterial == null)
            {
                // Material v Resources zajisti, ze se shader dostane i do buildu (vytvari ho Editor/M7Setup).
                particleMaterial = Resources.Load<Material>("Fx/Particle");
                if (particleMaterial == null)
                    particleMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            return particleMaterial;
        }
    }

    public static void Explosion(Vector3 position, float radius)
    {
        var go = new GameObject("FX_Explosion");
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.4f, radius * 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.2f, 1f), new Color(1f, 0.2f, 0f, 1f));
        main.gravityModifier = -0.15f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)220) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius * 0.2f;

        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(0.9f, 0.15f, 0f), 0.6f), new GradientColorKey(new Color(0.15f, 0.1f, 0.1f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(gradient);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.1f)));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = ParticleMaterial;

        ps.Play();

        var lightObject = new GameObject("Flash");
        lightObject.transform.SetParent(go.transform, false);
        lightObject.transform.localPosition = Vector3.up * 1.5f;
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.55f, 0.15f);
        light.range = radius * 2.5f;
        light.intensity = 12f;

        go.AddComponent<FxLifetime>().Init(light, 0.6f, 3f);
    }

    public static ParticleSystem CreateFlameTrail(Transform parent, float localY)
    {
        var go = new GameObject("FX_FlameTrail");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, localY, 0f);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.25f, 1f), new Color(1f, 0.25f, 0f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 90f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 14f;
        shape.radius = 0.35f;

        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f), new GradientColorKey(new Color(0.9f, 0.2f, 0f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(gradient);

        go.GetComponent<ParticleSystemRenderer>().material = ParticleMaterial;
        return ps;
    }

    public static GameObject CreateMarker()
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "FX_LeapMarker";
        Object.Destroy(marker.GetComponent<Collider>());
        marker.GetComponent<Renderer>().material.color = new Color(1f, 0.25f, 0.05f);
        marker.SetActive(false);
        return marker;
    }
}

public class FxLifetime : MonoBehaviour
{
    Light flash;
    float flashTime;
    float age;

    public void Init(Light light, float flashSeconds, float lifetime)
    {
        flash = light;
        flashTime = flashSeconds;
        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        age += Time.deltaTime;
        if (flash != null)
            flash.intensity = Mathf.Lerp(12f, 0f, age / flashTime);
    }
}
