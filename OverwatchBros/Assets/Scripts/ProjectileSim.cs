using Unity.Netcode;
using UnityEngine;

// Serverova (autoritativni) simulace jednoho projektilu: let s gravitaci, zasah, primy damage nebo vybuch.
// Klienti nevidi tuhle tridu, jen vizual (ProjectileVisual), ktery jim posle WeaponShooting pres ClientRpc.
public class ProjectileSim : MonoBehaviour
{
    public const int KindExpired = 0;
    public const int KindHit = 1;
    public const int KindExplosion = 2;
    public const int KindSpent = 3;   // doletel tak daleko, ze by uz nedal zadne poskozeni - vybuchne ve vzduchu

    WeaponShooting shooter;
    WeaponDefinition weapon;
    int id;
    int heroId;
    Vector3 velocity;
    float traveled;
    float fuse = -1f;   // granat: bezi od prvniho odrazu
    float damage;       // poskozeni tohohle projektilu (u luku podle natazeni)

    public static void Spawn(WeaponShooting shooter, WeaponDefinition weapon, int heroId, int id, Vector3 origin, Vector3 velocity,
        float damage = -1f)
    {
        var go = new GameObject("ProjectileSim");
        go.transform.position = origin;

        var sim = go.AddComponent<ProjectileSim>();
        sim.shooter = shooter;
        sim.weapon = weapon;
        sim.heroId = heroId;
        sim.id = id;
        sim.velocity = velocity;
        sim.damage = damage >= 0f ? damage : weapon.damage;
    }

    // Poskozeni po ulete draze (pokles se vzdalenosti podle zbrane).
    float DamageNow => damage * weapon.RangeFactor(traveled);

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

        // Bezdotykovy zapalovac: tesne minuti nepritele taky odpali granat.
        if (weapon.proximityRadius > 0f && weapon.explosionRadius > 0f && NearEnemy(transform.position))
        {
            Detonate(transform.position, Vector3.up, null);
            return;
        }

        // Za dosahem poskozeni (pokles se vzdalenosti az na nulu) uz strela nic neudela: vybuchne ve vzduchu.
        if (damage > 0f && weapon.RangeFactor(traveled) <= 0.001f)
        {
            Finish(transform.position, KindSpent);
            return;
        }

        if (traveled >= weapon.range)
            Finish(transform.position, KindExpired);
    }

    // Proti hracum (a terci, balvanu, pasti) ma strela plnou velikost (projectileRadius), at se dobre trefuje.
    // Proti svetu (zdi, okna, latovani) jen tenka (WorldRadius): kdyz miris do okna, strela jim proleti.
    // Granaty se od sveta odrazi celou velikosti.
    const float WorldRadius = 0.05f;

    bool TryFindHit(Vector3 start, Vector3 direction, float distance, out RaycastHit result)
    {
        bool found = false;
        result = default;

        var hits = Physics.SphereCastAll(start, weapon.projectileRadius, direction, distance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (!weapon.Bounces && !IsLiveTarget(hit.collider)) continue;   // svet resi tenky paprsek nize
            if (!Valid(hit)) continue;
            result = hit;
            found = true;
            break;
        }

        if (!weapon.Bounces)
        {
            float thin = Mathf.Min(WorldRadius, weapon.projectileRadius);
            var world = Physics.SphereCastAll(start, thin, direction, distance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(world, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in world)
            {
                if (IsLiveTarget(hit.collider) || !Valid(hit)) continue;
                if (!found || hit.distance < result.distance)
                {
                    result = hit;
                    found = true;
                }
                break;
            }
        }

        return found;
    }

    bool Valid(RaycastHit hit)
    {
        var owner = hit.collider.GetComponentInParent<NetworkObject>();
        if (owner != null && owner == shooter.NetworkObject) return false;

        // Spoluhraci projektil propousti (lecive sipy ne - ty spoluhrace leci).
        if (owner != null && Combat.SameTeam(shooter.gameObject, owner.gameObject) && weapon.allyHeal <= 0f) return false;

        // Vlastni (a tymovy) balvan a past taky.
        var boulder = hit.collider.GetComponentInParent<BoulderHitbox>();
        if (boulder != null && boulder.IsFriendly(shooter.gameObject)) return false;
        var trap = hit.collider.GetComponentInParent<TrapHitbox>();
        if (trap != null && trap.IsFriendly(shooter.gameObject)) return false;
        return true;
    }

    public static bool IsLiveTarget(Collider collider)
    {
        return collider.GetComponentInParent<Health>() != null
            || collider.GetComponentInParent<Target>() != null
            || collider.GetComponentInParent<BoulderHitbox>() != null
            || collider.GetComponentInParent<TrapHitbox>() != null;
    }

    bool NearEnemy(Vector3 position)
    {
        foreach (var col in Physics.OverlapSphere(position, weapon.proximityRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            var health = col.GetComponentInParent<Health>();
            if (health == null || health.currentHealth.Value <= 0f || health.gameObject == shooter.gameObject) continue;
            if (Combat.SameTeam(shooter.gameObject, health.gameObject)) continue;
            return true;
        }
        return false;
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
            Combat.Explode(shooter.gameObject, center, weapon.explosionRadius, DamageNow, 0.4f, direct);
            Finish(center, KindExplosion);
            return;
        }

        if (collider == null)
        {
            Finish(point, KindExpired);
            return;
        }

        float hitDamage = DamageNow;
        var dummy = collider.GetComponentInParent<Target>();
        if (dummy != null)
            dummy.TakeDamage(hitDamage);

        var boulder = collider.GetComponentInParent<BoulderHitbox>();
        if (boulder != null)
            boulder.Damage(shooter.gameObject, hitDamage);

        var trapHit = collider.GetComponentInParent<TrapHitbox>();
        if (trapHit != null)
            trapHit.Damage(shooter.gameObject, hitDamage);

        var health = collider.GetComponentInParent<Health>();
        if (health != null && weapon.allyHeal > 0f && Combat.SameTeam(shooter.gameObject, health.gameObject))
            Combat.HealPlayer(shooter.gameObject, health, weapon.allyHeal);
        else if (health != null && hitDamage > 0f)
            Combat.DamagePlayer(shooter.gameObject, health, hitDamage);

        Finish(point, KindHit);
    }

    void Finish(Vector3 position, int kind)
    {
        shooter.NotifyProjectileEnd(id, position, kind, heroId);
        Destroy(gameObject);
    }
}
