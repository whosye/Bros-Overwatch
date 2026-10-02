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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        Active.Clear();
    }

    public static void Spawn(int id, Vector3 position, Vector3 velocity, WeaponDefinition weapon)
    {
        if (Active.TryGetValue(id, out var old) && old != null)
            Destroy(old.gameObject);

        var go = weapon.projectilePrefab != null ? Instantiate(weapon.projectilePrefab) : CreateDefaultVisual(weapon);
        go.name = "ProjectileVisual";
        go.transform.position = position;
        go.transform.rotation = Quaternion.LookRotation(velocity);

        var visual = go.AddComponent<ProjectileVisual>();
        visual.id = id;
        visual.velocity = velocity;
        visual.gravity = weapon.projectileGravity;
        visual.bounce = weapon.Bounces ? weapon.projectileBounce : 0f;
        visual.radius = weapon.projectileRadius;
        visual.lifetime = weapon.range / Mathf.Max(0.1f, weapon.projectileSpeed) + 1f + (weapon.Bounces ? weapon.projectileFuse + 3f : 0f);
        Active[id] = visual;
    }

    public static void End(int id, Vector3 position, int kind, WeaponDefinition weapon)
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
        Fx.Paint(go, weapon.projectileColor);

        var glow = go.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = weapon.projectileColor;
        glow.range = 3f + diameter * 4f;
        glow.intensity = 2.5f;

        if (weapon.projectileTrail)
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

        return go;
    }

    void Update()
    {
        velocity.y -= gravity * Time.deltaTime;
        Vector3 step = velocity * Time.deltaTime;

        // Granat: stejny odraz od sveta jako na serveru (konec a vybuch oznami server).
        if (bounce > 0f && step.sqrMagnitude > 0f)
        {
            float distance = step.magnitude;
            var hits = Physics.SphereCastAll(transform.position, radius, step / distance, distance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (ProjectileSim.IsLiveTarget(hit.collider)) continue;

                ProjectileSim.Bounce(ref velocity, hit.distance > 0f ? hit.normal : Vector3.up, bounce);
                step = step / distance * Mathf.Max(0f, hit.distance - 0.01f);
                break;
            }
        }

        transform.position += step;

        if (velocity.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(velocity);

        lifetime -= Time.deltaTime;
        if (lifetime <= 0f)
        {
            Active.Remove(id);
            Destroy(gameObject);
        }
    }
}
