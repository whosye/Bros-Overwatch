using UnityEngine;

// Zablesk u usti zbrane: nekolik kratkych svitivych obracku, cervenozlute svetlo na 0,05 s; u granatometu navic
// obracek dymu. Komponentu prida setup zbrane na znacku "Muzzle" (pri kazdem vystrelu, HeldWeapons.AnimateShotParts)
// nebo "GrenadeMuzzle" (grenade = true, jen pri vystrelu granatu, HeldWeapons.PlayGrenadeLaunch).
// Velikosti jsou ve svetovych metrech (nezavisle na zmenseni modelu zbrane).
public class MuzzleFlash : MonoBehaviour
{
    [Tooltip("Zablesk granatometu (jen pri vystrelu granatu, s dymem); jinak pri kazdem vystrelu zbrane.")]
    public bool grenade;
    [Tooltip("Velikost zablesku (m).")]
    public float size = 0.16f;
    public Color color = new Color(1f, 0.78f, 0.38f);

    ParticleSystem flash, smoke;
    Light glow;
    float lightUntil;

    void Build()
    {
        flash = NewSystem("Flash", ParticleSystemSimulationSpace.Local);
        var main = flash.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.035f, 0.06f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
        var shape = flash.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 8f;
        shape.radius = 0.005f;
        var velocity = flash.velocityOverLifetime;   // (obracky kousek pred ustim)
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        // (vsechny osy ve stejnem rezimu - jinak Unity hlasi chybu pri Emit)
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.z = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);

        if (grenade)
        {
            smoke = NewSystem("Smoke", ParticleSystemSimulationSpace.World);
            var sm = smoke.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.75f, 0.75f, 0.35f), new Color(0.55f, 0.55f, 0.58f, 0.25f));
            sm.gravityModifier = -0.05f;
            var shape2 = smoke.shape;
            shape2.enabled = true;
            shape2.shapeType = ParticleSystemShapeType.Cone;
            shape2.angle = 25f;
            shape2.radius = 0.02f;
            var grow = smoke.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 2.4f)));
            var fade = smoke.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        var lightObject = new GameObject("FlashLight");
        lightObject.transform.SetParent(transform, false);
        glow = lightObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = color;
        glow.range = grenade ? 4f : 3f;
        glow.intensity = grenade ? 5f : 3.5f;
        glow.shadows = LightShadows.None;
        glow.enabled = false;
    }

    ParticleSystem NewSystem(string name, ParticleSystemSimulationSpace space)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = space;
        main.scalingMode = ParticleSystemScalingMode.Local;   // velikosti v metrech i pod zmensenym modelem
        main.maxParticles = 64;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = Fx.ParticleMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    public void Flash()
    {
        if (flash == null) Build();
        // (zmensene rodicovske meritko neplati: velikost v metrech)
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(1f / Mathf.Max(0.0001f, parentScale.x), 1f / Mathf.Max(0.0001f, parentScale.y), 1f / Mathf.Max(0.0001f, parentScale.z));
        flash.Emit(grenade ? 6 : 3);
        if (smoke != null) smoke.Emit(14);
        glow.enabled = true;
        lightUntil = Time.time + (grenade ? 0.08f : 0.045f);
    }

    void Update()
    {
        if (glow != null && glow.enabled && Time.time > lightUntil) glow.enabled = false;
    }
}
