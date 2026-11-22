using UnityEngine;

// Mosinuv basovy uder (prave tlacitko): viditelna tlakova vlna. Od hlavne se do kuzele (jako zasah, 40 stupnu)
// rozbehne nekolik fialovych kruhu za sebou, kolem nich se chveje vzduch (castice) a blikne svetlo.
// Vidi ji vsichni (BardAbility.WaveClientRpc).
public class BassWaveFx : MonoBehaviour
{
    const int Rings = 5;
    const int Segments = 48;
    const float Duration = 0.45f;      // jak dlouho jeden kruh leti az na konec dosahu
    const float Stagger = 0.06f;       // rozestup kruhu za sebou
    const float ConeTan = 0.62f;       // polomer kruhu / vzdalenost (o neco uzsi nez zasahovy kuzel, at vlna nepusobi prilis siroce)

    static Material ringMaterial;

    LineRenderer[] rings;
    readonly Vector3[] points = new Vector3[Segments];
    Light flash;
    Color color;
    float range, age;
    Vector3 origin, forward;

    public static void Play(Vector3 origin, Vector3 forward, float range, Color color)
    {
        if (forward.sqrMagnitude < 0.0001f) return;
        var go = new GameObject("FX_BasovaVlna");
        go.AddComponent<BassWaveFx>().Init(origin, forward.normalized, range, color);
    }

    static Material RingMaterial
    {
        get
        {
            if (ringMaterial == null)
            {
                ringMaterial = new Material(Fx.ParticleMaterial);
                ringMaterial.mainTexture = Texture2D.whiteTexture;
            }
            return ringMaterial;
        }
    }

    void Init(Vector3 from, Vector3 dir, float reach, Color tint)
    {
        origin = from;
        forward = dir;
        range = Mathf.Max(2f, reach);
        color = tint;
        transform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward));

        rings = new LineRenderer[Rings];
        for (int i = 0; i < Rings; i++)
        {
            var child = new GameObject("Kruh" + i);
            child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = Segments;
            line.material = RingMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.enabled = false;
            rings[i] = line;
        }

        flash = new GameObject("Zablesk").AddComponent<Light>();
        flash.transform.SetParent(transform, false);
        flash.transform.localPosition = Vector3.forward * 1.2f;
        flash.type = LightType.Point;
        flash.color = color;
        flash.range = range * 0.8f;
        flash.intensity = 4f;
        flash.shadows = LightShadows.None;

        Shimmer();
    }

    // Chveni vzduchu: castice vyletujici do kuzele.
    void Shimmer()
    {
        var go = new GameObject("Chveni");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(range * 1.6f, range * 2.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(color.r, color.g, color.b, 0.55f), new Color(1f, 1f, 1f, 0.35f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 30f;
        shape.radius = 0.15f;
        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.dampen = 0.25f;
        drag.limit = range * 0.8f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
        go.GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;
        ps.Play();
    }

    void Update()
    {
        age += Time.deltaTime;
        bool alive = false;
        for (int i = 0; i < rings.Length; i++)
        {
            var line = rings[i];
            float t = (age - i * Stagger) / Duration;
            if (t < 0f || t > 1f)
            {
                line.enabled = false;
                if (t < 0f) alive = true;
                continue;
            }
            alive = true;
            line.enabled = true;
            // rychly start, na konci dosahu dobiha (basa "dunivě" doznívá)
            float ease = 1f - (1f - t) * (1f - t);
            float distance = Mathf.Lerp(0.6f, range, ease);
            float radius = Mathf.Max(0.25f, distance * ConeTan);
            // (sirka cary se meritkem neskaluje - body kruhu se pocitaji primo)
            for (int s = 0; s < Segments; s++)
            {
                float a = s / (float)Segments * Mathf.PI * 2f;
                points[s] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, distance);
            }
            line.SetPositions(points);
            // prvni kruh je nejsilnejsi, dalsi slabnou
            float strength = 1f - i / (float)(Rings + 1);
            float alpha = (1f - t) * Mathf.Clamp01(t * 8f) * 0.85f * strength;
            var c = Color.Lerp(Color.white, color, 0.35f + 0.65f * t);
            c.a = alpha;
            line.startColor = line.endColor = c;
            float width = Mathf.Lerp(0.22f, 0.05f, t) * strength;
            line.startWidth = line.endWidth = width;
        }

        if (flash != null)
            flash.intensity = Mathf.Max(0f, 4f * (1f - age / 0.25f));

        if (!alive && age > Duration + Rings * Stagger + 0.5f)
            Destroy(gameObject);
    }
}
