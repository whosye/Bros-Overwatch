using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Spolecna serverova bojova logika (zasahy, vybuchy, kill kredit) pro projektily i schopnosti.
public static class Combat
{
    public static bool SameTeam(GameObject a, GameObject b)
    {
        var teamA = a != null ? a.GetComponent<PlayerTeam>() : null;
        var teamB = b != null ? b.GetComponent<PlayerTeam>() : null;
        return teamA != null && teamB != null && teamA.teamId.Value == teamB.teamId.Value;
    }

    // Vybuch nejde skrz zdi: musi byt primy vyhled z mista vybuchu na cil.
    public static bool HasLineOfSight(Vector3 from, Collider target)
    {
        Vector3 to = target.bounds.center;
        if (Vector3.Distance(from, to) < 1.5f) return true;
        if (!Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)) return true;

        if (hit.collider == target) return true;

        var targetOwner = target.GetComponentInParent<NetworkObject>();
        return targetOwner != null && hit.collider.GetComponentInParent<NetworkObject>() == targetOwner;
    }

    // Prime poskozeni hrace (jen server). Vlastni tym se neraní. Kdyz zasah zabije, pripise se kill utocnikovi.
    public static void DamagePlayer(GameObject attacker, Health target, float amount)
    {
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsServer || target == null) return;

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;
        if (SameTeam(attacker, target.gameObject)) return;

        float before = target.currentHealth.Value;
        bool wasAlive = before > 0f;

        var targetHero = target.GetComponent<PlayerHero>();
        if (targetHero != null)
            targetHero.ServerNoteAttacker(attacker);
        target.TakeDamage(amount);

        bool killed = wasAlive && target.currentHealth.Value <= 0f;

        // Utocnik dostane potvrzeni zasahu (krizek u zamerovace, pri zabiti lebka).
        if (attacker != null && attacker != target.gameObject && target.currentHealth.Value < before)
        {
            var attackerHero = attacker.GetComponent<PlayerHero>();
            if (attackerHero != null)
            {
                attackerHero.ServerNotifyHit(killed);
                attackerHero.ServerAddUltCharge(before - target.currentHealth.Value);
            }

            var recorder = attacker.GetComponent<PotgRecorder>();
            if (recorder != null)
                recorder.ServerAddDamage(before - target.currentHealth.Value);
        }

        if (killed && match != null)
            match.ReportKill(attacker, target.gameObject);
    }

    // Plosny vybuch (jen server): damage klesa od stredu (100 %) po okraj (edgeFactor), neprochazi zdmi, nevraci vlastni tym.
    // 'direct' = hrac, ktereho projektil trefil primo: dostane plne poskozeni bez ohledu na vzdalenost od stredu.
    public static void Explode(GameObject attacker, Vector3 position, float radius, float damage, float edgeFactor, Health direct = null)
    {
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsServer) return;

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;

        var damaged = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(position, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var dummy = col.GetComponentInParent<Target>();
            if (dummy != null)
                dummy.TakeDamage(damage);

            var boulder = col.GetComponentInParent<BoulderHitbox>();
            if (boulder != null)
                boulder.Damage(attacker, damage);

            var health = col.GetComponentInParent<Health>();
            if (health == null || !damaged.Add(health)) continue;
            if (SameTeam(attacker, health.gameObject)) continue;
            if (!HasLineOfSight(position + Vector3.up * 0.6f, col)) continue;

            float distance = Vector3.Distance(position, health.transform.position);
            float scaled = health == direct ? damage : damage * Mathf.Lerp(1f, edgeFactor, Mathf.Clamp01(distance / radius));
            DamagePlayer(attacker, health, scaled);
        }
    }

    // Odhozeni hracu vybuchem (jen server): neprochazi zdmi, spoluhrace neodhazuje, utocnika sameho ano (selfScale).
    public static void Knockback(GameObject attacker, Vector3 position, float radius, float force, float selfScale)
    {
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsServer || force <= 0f) return;

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;

        var pushed = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(position, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var health = col.GetComponentInParent<Health>();
            if (health == null || !pushed.Add(health) || health.currentHealth.Value <= 0f) continue;

            bool self = health.gameObject == attacker;
            if (!self && SameTeam(attacker, health.gameObject)) continue;
            if (self && selfScale <= 0f) continue;
            if (!HasLineOfSight(position + Vector3.up * 0.3f, col)) continue;

            var controller = health.GetComponent<FirstPersonController>();
            if (controller == null) continue;

            Vector3 direction = col.bounds.center - position;
            float distance = direction.magnitude;
            direction = distance > 0.05f ? direction / distance : Vector3.up;
            direction = (direction + Vector3.up * 0.7f).normalized;

            float scale = Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(distance / radius)) * (self ? selfScale : 1f);
            controller.ServerKnockback(direction * force * scale);

            // Kdyz odhozeny spadne z mapy, zabiti patri tomu, kdo ho odhodil.
            var pushedHero = health.GetComponent<PlayerHero>();
            if (pushedHero != null && !self)
                pushedHero.ServerNoteAttacker(attacker);
        }
    }
}
