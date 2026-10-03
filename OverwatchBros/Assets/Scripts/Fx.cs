using UnityEngine;

// Placeholder vizualni efekty generovane kodem (particles, svetlo, znacka dopadu).
public static class Fx
{
    static Material particleMaterial;
    static Material litBase;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        particleMaterial = null;
        litBase = null;
    }

    static Texture2D SoftDot()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float alpha = Mathf.Clamp01(1f - d);
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, alpha));
            }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    // Material pro objekty vytvarene kodem (GameObject.CreatePrimitive). Vychozi material primitiv pouziva
    // shader, ktery v URP buildu neni, takze by byly ruzove. Zaklad je v Resources (vytvari ho Editor/M7Setup).
    public static Material NewLit(Color color)
    {
        if (litBase == null)
        {
            litBase = Resources.Load<Material>("Fx/Lit");
            if (litBase == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                litBase = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            }
        }

        var material = new Material(litBase);
        material.color = color;
        return material;
    }

    public static void Paint(GameObject primitive, Color color)
    {
        primitive.GetComponent<Renderer>().sharedMaterial = NewLit(color);
    }

    public static Material ParticleMaterial
    {
        get
        {
            if (particleMaterial == null)
            {
                // Material v Resources zajisti, ze se shader dostane i do buildu (vytvari ho Editor/M7Setup).
                var loaded = Resources.Load<Material>("Fx/Particle");
                particleMaterial = loaded != null ? new Material(loaded) : new Material(Shader.Find("Sprites/Default"));

                // Mekky kulaty bod misto ctverce: plameny, jiskry i vybuchy pak vypadaji jako oblacky.
                particleMaterial.mainTexture = SoftDot();
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

    // Kratky vyprsk jisker pri primem zasahu projektilu.
    public static void Sparks(Vector3 position, Color color)
    {
        var go = new GameObject("FX_Sparks");
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, color);
        main.gravityModifier = 0.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)24) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;

        go.GetComponent<ParticleSystemRenderer>().material = ParticleMaterial;
        ps.Play();
    }

    // Maly vybuch v miste dopadu strely (hitscan) nebo uderu (melee).
    public static void BulletImpact(Vector3 position, Color tint, float scale = 1f)
    {
        var go = new GameObject("FX_BulletImpact");
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.4f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * scale, 3f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f * scale, 0.4f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.5f, 1f), Color.Lerp(new Color(1f, 0.35f, 0.05f, 1f), tint, 0.4f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)26) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f * scale;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

        go.GetComponent<ParticleSystemRenderer>().material = ParticleMaterial;
        ps.Play();

        var lightObject = new GameObject("Flash");
        lightObject.transform.SetParent(go.transform, false);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.6f, 0.2f);
        light.range = 3f * scale;
        light.intensity = 4f;
        Object.Destroy(lightObject, 0.08f);
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

    // Ohnive/modre plameny kolem tela (svetovy prostor, aby za rychle se pohybujici postavou zustavala stopa).
    public static ParticleSystem CreateAura(Transform parent, Color color)
    {
        var go = new GameObject("FX_Aura");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, -0.3f, 0f);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(color, Color.white, 0.6f), color);
        main.gravityModifier = -0.3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 140f;
        emission.rateOverDistance = 22f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.55f;
        shape.scale = new Vector3(1f, 1.6f, 1f);

        var gradientColor = ps.colorOverLifetime;
        gradientColor.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.4f), new GradientColorKey(color * 0.4f, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        gradientColor.color = new ParticleSystem.MinMaxGradient(gradient);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));

        go.GetComponent<ParticleSystemRenderer>().material = ParticleMaterial;
        return ps;
    }

    // Prostorovy zvuk vychazejici z pozice hrace: plna hlasitost poblíž (vlastnik i blizci spoluhraci),
    // pak plynuly utlum az k tichu v maxDistance. Na rozdil od AudioSource.PlayClipAtPoint (vychozi logaritmicky
    // rolloff s dosahem 500 m) jde o falloff prizpusobeny velikosti mapy.
    public static void PlaySpatial(AudioClip clip, Vector3 position, float volume, float minDistance = 4f, float maxDistance = 30f)
    {
        if (clip == null) return;

        var go = new GameObject("SFX_" + clip.name);
        go.transform.position = position;

        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.Play();

        Object.Destroy(go, clip.length + 0.1f);
    }

    // Zvuk pres celou mapu (ultimatky): bez prostoroveho utlumu, kazdy hrac ho slysi stejne hlasite.
    public static void PlayGlobal(AudioClip clip, float volume)
    {
        if (clip == null) return;

        var go = new GameObject("SFX_Global_" + clip.name);
        Object.DontDestroyOnLoad(go);

        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = 0f;
        source.Play();

        Object.Destroy(go, clip.length + 0.1f);
    }

    public static GameObject CreateMarker()
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "FX_LeapMarker";
        Object.Destroy(marker.GetComponent<Collider>());
        Paint(marker, new Color(1f, 0.25f, 0.05f));
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
