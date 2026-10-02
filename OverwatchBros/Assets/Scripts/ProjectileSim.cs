using Unity.Netcode;
using UnityEngine;

// Serverova (autoritativni) simulace jednoho projektilu: let s gravitaci, zasah, primy damage nebo vybuch.
// Klienti nevidi tuhle tridu, jen vizual (ProjectileVisual), ktery jim posle WeaponShooting pres ClientRpc.
public class ProjectileSim : MonoBehaviour
{
    public const int KindExpired = 0;
    public const int KindHit = 1;
    public const int KindExplosion = 2;

    WeaponShooting shooter;
    WeaponDefinition weapon;
    int id;
    int heroId;
    Vector3 velocity;
    float traveled;
    float fuse = -1f;   // granat: bezi od prvniho odrazu

    public static void Spawn(WeaponShooting shooter, WeaponDefinition weapon, int heroId, int id, Vector3 origin, Vector3 velocity)
    {
        var go = new GameObject("ProjectileSim");
        go.transform.position = origin;

        var sim = go.AddComponent<ProjectileSim>();
        sim.shooter = shooter;
        sim.weapon = weapon;
        sim.heroId = heroId;
        sim.id = id;
        sim.velocity = velocity;
    }

    void FixedUpdate()
    {
        if (shooter == null || !shooter.IsSpawned)
        {
            Destroy(gameObject);
            return;
        }

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby))
        {
            Finish(transform.position, KindExpired);
            return;
        }

        float dt = Time.fixedDeltaTime;

        if (fuse >= 0f)
        {
            fuse -= dt;
            if (fuse <= 0f)
            {
                Detonate(transform.position, Vector3.up, null);
                return;
            }
        }

        velocity.y -= weapon.projectileGravity * dt;

        Vector3 start = transform.position;
        Vector3 step = velocity * dt;
        float distance = step.magnitude;
        if (distance <= 0f) return;

        if (TryFindHit(start, step / distance, distance, out RaycastHit hit))
        {
            Vector3 point = hit.distance > 0f ? hit.point : start;

            // Granat se od sveta odrazi, o hrace (terc, balvan) vybuchne.
            if (weapon.Bounces && !IsLiveTarget(hit.collider))
            {
                Bounce(ref velocity, hit.distance > 0f ? hit.normal : Vector3.up, weapon.projectileBounce);
                transform.position = start + step / distance * Mathf.Max(0f, hit.distance - 0.01f);
                if (fuse < 0f)
                    fuse = weapon.projectileFuse;
                return;
            }

            Detonate(point, hit.normal, hit.collider);
            return;
        }

        transform.position = start + step;
        traveled += distance;

        if (traveled >= weapon.range)
            Finish(transform.position, KindExpired);
    }

    bool TryFindHit(Vector3 start, Vector3 direction, float distance, out RaycastHit result)
    {
        var hits = Physics.SphereCastAll(start, weapon.projectileRadius, direction, distance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && owner == shooter.NetworkObject) continue;

            // Spoluhraci projektil propousti.
            if (owner != null && Combat.SameTeam(shooter.gameObject, owner.gameObject)) continue;

            // Vlastni (a tymovy) balvan taky.
            var boulder = hit.collider.GetComponentInParent<BoulderHitbox>();
            if (boulder != null && boulder.IsFriendly(shooter.gameObject)) continue;

            result = hit;
            return true;
        }

        result = default;
        return false;
    }

    public static bool IsLiveTarget(Collider collider)
    {
        return collider.GetComponentInParent<Health>() != null
            || collider.GetComponentInParent<Target>() != null
            || collider.GetComponentInParent<BoulderHitbox>() != null;
    }

    // Odraz granatu: kolma slozka rychlosti se otoci a ztlumi, tecna se pribrzdi (spolecne pro server i vizual).
    public static void Bounce(ref Vector3 velocity, Vector3 normal, float bounce)
    {
        Vector3 along = Vector3.Project(velocity, normal);
        Vector3 tangent = velocity - along;
        velocity = tangent * 0.7f - along * bounce;

        // Skoro stoji: uz neposkakuje.
        if (Mathf.Abs(Vector3.Dot(velocity, normal)) < 0.8f)
            velocity -= Vector3.Project(velocity, normal);
    }

    void Detonate(Vector3 point, Vector3 normal, Collider collider)
    {
        if (weapon.explosionRadius > 0f)
        {
            Vector3 center = point + normal * 0.1f;
            var direct = collider != null ? collider.GetComponentInParent<Health>() : null;
            Combat.Explode(shooter.gameObject, center, weapon.explosionRadius, weapon.damage, 0.4f, direct);
            Finish(center, KindExplosion);
            return;
        }

        if (collider == null)
        {
            Finish(point, KindExpired);
            return;
        }

        var dummy = collider.GetComponentInParent<Target>();
        if (dummy != null)
            dummy.TakeDamage(weapon.damage);

        var boulder = collider.GetComponentInParent<BoulderHitbox>();
        if (boulder != null)
            boulder.Damage(shooter.gameObject, weapon.damage);

        var health = collider.GetComponentInParent<Health>();
        if (health != null)
            Combat.DamagePlayer(shooter.gameObject, health, weapon.damage);

        Finish(point, KindHit);
    }

    void Finish(Vector3 position, int kind)
    {
        shooter.NotifyProjectileEnd(id, position, kind, heroId);
        Destroy(gameObject);
    }
}
