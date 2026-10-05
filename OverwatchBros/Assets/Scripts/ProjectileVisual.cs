using System.Collections.Generic;
using UnityEngine;

// Vizual projektilu na klientovi: leti stejnou balistikou jako serverova simulace a zmizi, az server oznami konec.
// Vzhled (model / barva / velikost / stopa) se bere ze zbrane (WeaponDefinition).
public class ProjectileVisual : MonoBehaviour
{
    static readonly Dictionary<int, ProjectileVisual> Active = new Dictionary<int, ProjectileVisual>();

    Vector3 velocity;
    float gravity;
    float lifetime;
    int id;
    float bounce;
    float radius;

    // Skutecna draha (stejna jako na serveru) a docasny posun vykresleni: strelci sip vyleti od luku
    // a behem chvilky se srovna na skutecnou drahu ve stredu obrazovky, takze je videt i jeho stopa.
    Vector3 position;
    Vector3 visualOffset;
    float age;
    const float OffsetSeconds = 0.22f;

    static Material trailMaterial;

    // Hladka stopa bez textury (material castic ma kulatou tecku, ktera by stopu skoro zneviditelnila).
    static Material TrailMaterial
    {
        get
        {
            if (trailMaterial == null)
                trailMaterial = new Material(Fx.ParticleMaterial) { mainTexture = null };
            return trailMaterial;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        Active.Clear();
    }

    public static void Spawn(int id, Vector3 position, Vector3 velocity, WeaponDefinition weapon, Vector3 visualOffset = default)
    {
        ReplayLog.ProjectileSpawn(id, position, velocity, weapon, visualOffset);
        if (Active.TryGetValue(id, out var old) && old != null)
            Destroy(old.gameObject);

        GameObject go;
        if (weapon.projectilePrefab != null)
        {
            // Vlastni model (napr. sip): stejna stopa jako u vychoziho vzhledu.
            go = Instantiate(weapon.projectilePrefab);
            AddTrail(go, weapon, Mathf.Max(0.1f, weapon.projectileRadius * 2f));
        }
        else
        {
            go = CreateDefaultVisual(weapon);
        }
        go.name = "ProjectileVisual";
        go.transform.position = position + visualOffset;
        go.transform.rotation = Quaternion.LookRotation(velocity);

        var visual = go.AddComponent<ProjectileVisual>();
        visual.position = position;
        visual.visualOffset = visualOffset;
        visual.id = id;
        visual.velocity = velocity;
        visual.gravity = weapon.projectileGravity;
        visual.bounce = weapon.Bounces ? weapon.projectileBounce : 0f;
        visual.radius = weapon.projectileRadius;
        visual.spins = go.transform.Find("Band") != null;
        visual.lifetime = weapon.range / Mathf.Max(0.1f, weapon.projectileSpeed) + 1f + (weapon.Bounces ? weapon.projectileFuse + 3f : 0f);
        Active[id] = visual;
    }

    public static void End(int id, Vector3 position, int kind, WeaponDefinition weapon)
    {
        // (vybuch a jiskry, ktere konec sam vyvola, se zvlast nezapisuji)
        ReplayLog.ProjectileEnd(id, position, kind, weapon);
        using (ReplayLog.Mute())
            EndInternal(id, position, kind, weapon);
    }

    static void EndInternal(int id, Vector3 position, int kind, WeaponDefinition weapon)
    {
        if (Active.TryGetValue(id, out var visual) && visual != null)
            Destroy(visual.gameObject);
        Active.Remove(id);

        if (weapon == null) return;

        if (kind == ProjectileSim.KindExplosion)
        {
            Fx.Explosion(position, weapon.explosionRadius);
            ProceduralSfx.Play(ProceduralSfx.Explosion, position, 0.9f);
        }
        else if (kind == ProjectileSim.KindHit)
        {
            Fx.Sparks(position, weapon.projectileColor);
            ProceduralSfx.Play(ProceduralSfx.Hit, position, 0.6f);
        }
    }

    static GameObject CreateDefaultVisual(WeaponDefinition weapon)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());

        float diameter = Mathf.Max(0.1f, weapon.projectileRadius * 2f);
        go.transform.localScale = Vector3.one * diameter;

        // Sip: uzky a dlouhy ve smeru letu.
        if (weapon.IsCharged)
        {
            diameter = 0.07f;
            go.transform.localScale = new Vector3(0.07f, 0.07f, 0.85f);
        }
        Fx.Paint(go, weapon.projectileColor);

        // Granat: cerny pruh kolem stredu (koule se v letu otaci, aby pruh bylo videt).
        if (weapon.Bounces)
        {
            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(band.GetComponent<Collider>());
            band.name = "Band";
            band.transform.SetParent(go.transform, false);
            band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            band.transform.localScale = new Vector3(1.04f, 0.13f, 1.04f);
            Fx.Paint(band, new Color(0.05f, 0.05f, 0.06f));
        }

        var glow = go.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = weapon.projectileColor;
        glow.range = 3f + diameter * 4f;
        glow.intensity = 2.5f;

        AddTrail(go, weapon, diameter);
        return go;
    }

    static void AddTrail(GameObject go, WeaponDefinition weapon, float diameter)
    {
        if (weapon.projectileTrail && weapon.IsCharged)
        {
            // Sip: kratka pruhledna stopa s lehkym modrym nadechem, ktera se k chvostu zuzuje a mizi.
            // Je videt, kudy sip proletel, ale nezakryva vyhled.
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.16f;
            trail.minVertexDistance = 0.05f;
            trail.widthMultiplier = 0.2f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f));
            trail.material = TrailMaterial;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var tint = new Color(0.62f, 0.84f, 1f);
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.Lerp(tint, Color.white, 0.35f), 0f), new GradientColorKey(tint, 1f) },
                new[] { new GradientAlphaKey(0.65f, 0f), new GradientAlphaKey(0.3f, 0.5f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
        }
        else if (weapon.projectileTrail)
        {
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.35f;
            trail.widthMultiplier = diameter * 0.9f;
            trail.material = Fx.ParticleMaterial;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var gradient = new Gradient();
            var tail = weapon.projectileColor;
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.Lerp(tail, Color.white, 0.5f), 0f), new GradientColorKey(tail, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
        }
    }

    // Otaceni granatu v letu (at je videt cerny pruh).
    bool spins;
    float spin;

    void Update()
    {
        velocity.y -= gravity * Time.deltaTime;
        Vector3 step = velocity * Time.deltaTime;

        // Granat: stejny odraz od sveta jako na serveru (konec a vybuch oznami server).
        if (bounce > 0f && step.sqrMagnitude > 0f)
        {
            float distance = step.magnitude;
            var hits = Physics.SphereCastAll(position, radius, step / distance, distance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (ProjectileSim.IsLiveTarget(hit.collider)) continue;

                ProjectileSim.Bounce(ref velocity, hit.distance > 0f ? hit.normal : Vector3.up, bounce);
                step = step / distance * Mathf.Max(0f, hit.distance - 0.01f);
                break;
            }
        }

        position += step;
        age += Time.deltaTime;

        // Posun vykresleni plynule mizi.
        float offsetLeft = 1f - Mathf.SmoothStep(0f, 1f, age / OffsetSeconds);
        transform.position = position + visualOffset * offsetLeft;

        if (velocity.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(velocity);
            if (spins)
            {
                spin += Time.deltaTime * 720f;
                transform.rotation *= Quaternion.Euler(spin, 0f, 0f);
            }
        }

        lifetime -= Time.deltaTime;
        if (lifetime <= 0f)
        {
            Active.Remove(id);
            Destroy(gameObject);
        }
    }
}
