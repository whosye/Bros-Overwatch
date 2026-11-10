using System.Collections.Generic;
using UnityEngine;

// Ohen v grilu na drevo: plameny (kuzele) se kazdy zvlast vlni, natahuji a kyvou (Perlinuv sum), zhave uhliky
// pulzuji, svetlo blika a za hry z ohne leti jiskry a stoupa kour. Plameny jsou deti objektu "Plameny",
// uhliky deti "Uhliky" (staveno v DecorTools > Gril na drevo).
public class GrillFire : MonoBehaviour
{
    public Transform flames;
    public Transform embers;
    public Light fireLight;
    public float baseIntensity = 2.2f;
    public float size = 1f;   // sirka ohniste v metrech - podle ni jiskry a kour

    readonly List<(Transform t, Vector3 pos, Vector3 scale, Quaternion rot, float seed)> flameParts = new();
    readonly List<(Renderer r, float seed)> emberParts = new();
    MaterialPropertyBlock block;
    Color emberEmission;

    void Start()
    {
        if (flames != null)
            foreach (Transform child in flames)
                flameParts.Add((child, child.localPosition, child.localScale, child.localRotation, Random.value * 100f));
        if (embers != null)
        {
            foreach (var r in embers.GetComponentsInChildren<Renderer>())
                emberParts.Add((r, Random.value * 100f));
            if (emberParts.Count > 0 && emberParts[0].r.sharedMaterial != null && emberParts[0].r.sharedMaterial.HasProperty("_EmissionColor"))
                emberEmission = emberParts[0].r.sharedMaterial.GetColor("_EmissionColor");
        }
        block = new MaterialPropertyBlock();
        Sparks();
        Smoke();
    }

    void Update()
    {
        float time = Time.time;
        foreach (var f in flameParts)
        {
            float n = Mathf.PerlinNoise(f.seed, time * 3.2f);
            float m = Mathf.PerlinNoise(f.seed + 7.3f, time * 4.1f);
            float h = 0.65f + n * 0.7f;
            float w = 1.08f - n * 0.25f;
            f.t.localScale = new Vector3(f.scale.x * w, f.scale.y * h, f.scale.z * w);
            f.t.localRotation = f.rot * Quaternion.Euler((m - 0.5f) * 22f, 0f, (Mathf.PerlinNoise(f.seed + 3.1f, time * 3.7f) - 0.5f) * 22f);
            f.t.localPosition = f.pos + new Vector3((m - 0.5f) * 0.02f, 0f, (n - 0.5f) * 0.02f);
        }

        if (emberEmission.maxColorComponent > 0f)
            foreach (var e in emberParts)
            {
                float glow = 0.55f + Mathf.PerlinNoise(e.seed, time * 1.6f) * 0.9f;
                e.r.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", emberEmission * glow);
                e.r.SetPropertyBlock(block);
            }

        if (fireLight != null)
            fireLight.intensity = baseIntensity * (0.75f + Mathf.PerlinNoise(0.37f, time * 7f) * 0.5f);
    }

    // Jiskry: male zlutooranzove tecky vyletuji nahoru, kmitaji do stran a hasnou.
    void Sparks()
    {
        var ps = NewSystem("Jiskry");
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.35f), new Color(1f, 0.4f, 0.08f));
        main.gravityModifier = -0.15f;
        main.maxParticles = 60;
        var emission = ps.emission;
        emission.rateOverTime = 9f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.3f * size;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 1.5f;
        Fade(ps, new Color(1f, 0.9f, 0.5f), new Color(1f, 0.25f, 0f), 1f);
        ps.Play();
    }

    // Kour: sede mekke obláčky pomalu stoupaji, rostou a mizi.
    void Smoke()
    {
        var ps = NewSystem("Kour");
        ps.transform.localPosition += Vector3.up * 0.35f;
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f * size, 0.55f * size);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.45f, 0.45f, 0.45f, 0.35f);
        main.maxParticles = 40;
        var emission = ps.emission;
        emission.rateOverTime = 4f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.2f * size;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.15f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
        Fade(ps, new Color(0.5f, 0.5f, 0.5f), new Color(0.75f, 0.75f, 0.75f), 0.35f);
        ps.Play();
    }

    ParticleSystem NewSystem(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(flames != null ? flames : transform, false);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // emise nahoru
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.prewarm = true;
        main.loop = true;
        main.duration = 5f;
        go.GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;
        return go.GetComponent<ParticleSystem>();
    }

    static void Fade(ParticleSystem ps, Color start, Color end, float alpha)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, 0.15f), new GradientAlphaKey(alpha * 0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(gradient);
    }
}
