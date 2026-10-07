using UnityEngine;

// Splouchani vody u hrace, ktery jede z toboganu po vode (FirstPersonController.waterSurfing). Kresli kazdy klient:
//  - dopad do vody: splouch se zvukem,
//  - jizda: sprska dozadu, vlny do stran (jako za lodi), penova stopa na hladine a huceni vody,
//  - konec jizdy v jezirku: obri splouch (sloup vody, trist, rozbihajici se kruhy vln, hlasite zbluňknuti).
public class WaterSplashFx : MonoBehaviour
{
    static readonly Color Spray = new Color(0.82f, 0.92f, 1f, 0.9f);
    static readonly Color Mist = new Color(0.9f, 0.96f, 1f, 0.35f);

    FirstPersonController fpc;
    HeroVoice voice;
    bool wasRiding;
    static AudioClip rideMusic;
    Transform rig;
    ParticleSystem spray, wakeLeft, wakeRight;
    TrailRenderer foam;
    AudioSource rush;
    bool wasSurfing;
    Vector3 lastPosition, lastMotion = Vector3.forward;

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        voice = GetComponent<HeroVoice>();
        if (rideMusic == null) rideMusic = Resources.Load<AudioClip>("Audio/tobogan");
        lastPosition = transform.position;
    }

    void LateUpdate()
    {
        bool surfing = fpc != null && fpc.IsSpawned && fpc.waterSurfing.Value && !fpc.IsDead;
        Vector3 feet = transform.position + Vector3.up * 0.12f;

        Vector3 motion = transform.position - lastPosition;
        motion.y = 0f;
        if (motion.sqrMagnitude > 0.0001f) lastMotion = motion.normalized;
        lastPosition = transform.position;

        if (surfing && !wasSurfing)
            Splash(feet);
        if (!surfing && wasSurfing && WaterFlow.InPond(transform.position, out Vector3 pond))
            GiantSplash(new Vector3(transform.position.x, pond.y, transform.position.z));
        wasSurfing = surfing;

        // Hudba jizdy (Resources/Audio/tobogan): zacne se skluzem; kdo vystoupi driv nez v jezirku, tomu skonci,
        // kdo dojede do jezirka, tomu dohraje.
        bool riding = fpc != null && fpc.IsSpawned && fpc.waterRiding.Value && !fpc.IsDead;
        if (voice == null) voice = GetComponent<HeroVoice>();
        if (voice != null)
        {
            if (riding && !wasRiding)
                voice.PlayRide(rideMusic);
            else if (!riding && wasRiding && (fpc == null || fpc.IsDead || !WaterFlow.InPond(transform.position, out _)))
                voice.StopRide();
        }
        wasRiding = riding;

        if (surfing && rig == null) BuildRig();
        if (rig == null) return;

        rig.SetPositionAndRotation(feet, Quaternion.LookRotation(lastMotion, Vector3.up));
        SetRate(spray, surfing ? 160f : 0f);
        SetRate(wakeLeft, surfing ? 70f : 0f);
        SetRate(wakeRight, surfing ? 70f : 0f);
        foam.emitting = surfing;
        if (surfing && !rush.isPlaying) rush.Play();
        rush.volume = Mathf.MoveTowards(rush.volume, surfing ? 0.7f : 0f, Time.deltaTime * 2f);
        if (!surfing && rush.volume <= 0.001f && rush.isPlaying) rush.Stop();
    }

    static void SetRate(ParticleSystem ps, float rate)
    {
        var emission = ps.emission;
        emission.rateOverTime = rate;
    }

    // ---------------- jizda ----------------

    void BuildRig()
    {
        rig = new GameObject("FX_JizdaPoVode").transform;
        rig.SetParent(transform, false);

        // sprska dozadu a nahoru
        spray = Emitter(rig, "Sprska", Quaternion.Euler(-35f, 180f, 0f), 30f, 0.3f, 0.35f, 0.75f, 2f, 5f, 0.05f, 0.18f, 1.3f, Spray);
        // vlny do stran (rozevrene dozadu)
        wakeLeft = Emitter(rig, "VlnaL", Quaternion.Euler(-25f, -120f, 0f), 15f, 0.2f, 0.3f, 0.6f, 2f, 4f, 0.06f, 0.2f, 1.5f, Spray);
        wakeRight = Emitter(rig, "VlnaP", Quaternion.Euler(-25f, 120f, 0f), 15f, 0.2f, 0.3f, 0.6f, 2f, 4f, 0.06f, 0.2f, 1.5f, Spray);

        // penova stopa na hladine
        var trailObject = new GameObject("PenovaStopa");
        trailObject.transform.SetParent(rig, false);
        trailObject.transform.localPosition = new Vector3(0f, -0.05f, -0.3f);
        foam = trailObject.AddComponent<TrailRenderer>();
        foam.time = 0.9f;
        foam.minVertexDistance = 0.15f;
        foam.widthCurve = new AnimationCurve(new Keyframe(0f, 1.3f), new Keyframe(1f, 0.2f));
        foam.alignment = LineAlignment.TransformZ;
        foam.material = new Material(Fx.ParticleMaterial) { mainTexture = null };
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.75f, 0.9f, 1f), 1f) },
            new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
        foam.colorGradient = gradient;
        foam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        foam.emitting = false;
        // stopa lezi naplocho na hladine
        trailObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // huceni vody
        rush = rig.gameObject.AddComponent<AudioSource>();
        rush.clip = ProceduralSfx.WaterRush;
        rush.loop = true;
        rush.volume = 0f;
        rush.spatialBlend = 1f;
        rush.rolloffMode = AudioRolloffMode.Linear;
        rush.minDistance = 3f;
        rush.maxDistance = 25f;
        rush.dopplerLevel = 0f;
    }

    static ParticleSystem Emitter(Transform parent, string name, Quaternion rotation, float angle, float radius,
        float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = rotation;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = color;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 600;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = radius;
        go.GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;
        ps.Play();
        return ps;
    }

    // ---------------- dopady ----------------

    void Splash(Vector3 position)
    {
        ProceduralSfx.Play(ProceduralSfx.Splash, position, 0.9f);
        Burst(position, 90, 35f, 0.5f, 2.5f, 6f, 0.08f, 0.24f, 0.5f, 1f, 1.4f, Spray);
        Ring(position, 2.5f, 0.6f, 0f);
    }

    // Obri splouch v jezirku: sloup vody, siroka trist, mlha a tri kruhy vln.
    void GiantSplash(Vector3 position)
    {
        ProceduralSfx.Play(ProceduralSfx.BigSplash, position, 1f);
        ProceduralSfx.Play(ProceduralSfx.Splash, position, 1f);
        Burst(position, 260, 14f, 0.4f, 8f, 15f, 0.12f, 0.35f, 1.2f, 2.2f, 1.6f, Spray);   // sloup
        Burst(position, 220, 60f, 1f, 3f, 8f, 0.1f, 0.3f, 0.8f, 1.6f, 1.4f, Spray);        // trist do stran
        Burst(position + Vector3.up * 1.5f, 70, 80f, 1.5f, 1f, 3f, 0.6f, 1.4f, 1.5f, 2.5f, -0.05f, Mist);   // mlha
        for (int i = 0; i < 3; i++)
            Ring(position, 6f + i * 2.5f, 1.3f + i * 0.2f, i * 0.18f);
    }

    static void Burst(Vector3 position, int count, float angle, float radius, float speedMin, float speedMax,
        float sizeMin, float sizeMax, float lifeMin, float lifeMax, float gravity, Color color)
    {
        var go = new GameObject("FX_Splouchnuti");
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(-90f, 0f, 0f));   // nahoru
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = color;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count + 10;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = radius;
        go.GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;
        ps.Play();
        Destroy(go, lifeMax + 0.5f);
    }

    // Kruh vlny na hladine: rozbehne se do 'radius' a vybledne.
    static void Ring(Vector3 position, float radius, float seconds, float delay)
    {
        var go = new GameObject("FX_KruhVlny");
        go.transform.position = position + Vector3.up * 0.08f;
        go.AddComponent<WaterRing>().Init(radius, seconds, delay);
    }
}

// Rozbihajici se kruh vlny (LineRenderer na hladine).
public class WaterRing : MonoBehaviour
{
    const int Segments = 48;
    LineRenderer line;
    float radius, seconds, delay, age;

    public void Init(float maxRadius, float duration, float startDelay)
    {
        radius = maxRadius;
        seconds = duration;
        delay = startDelay;
        line = gameObject.AddComponent<LineRenderer>();
        line.loop = true;
        line.useWorldSpace = false;
        line.positionCount = Segments;
        line.alignment = LineAlignment.TransformZ;
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        line.material = new Material(Fx.ParticleMaterial) { mainTexture = null };
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.enabled = false;
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = (age - delay) / Mathf.Max(0.05f, seconds);
        if (t < 0f) return;
        if (t >= 1f) { Destroy(gameObject); return; }

        line.enabled = true;
        float r = Mathf.Lerp(0.4f, radius, 1f - (1f - t) * (1f - t));
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            line.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
        }
        line.widthMultiplier = Mathf.Lerp(0.5f, 0.12f, t);
        var c = new Color(0.9f, 0.97f, 1f, (1f - t) * 0.85f);
        line.startColor = line.endColor = c;
    }
}
