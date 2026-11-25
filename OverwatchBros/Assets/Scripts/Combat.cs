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
    public static bool HasLineOfSight(Vector3 from, Collider target, Transform ignoredOccluder = null)
    {
        Vector3 to = target.bounds.center;
        if (Vector3.Distance(from, to) < 1.5f) return true;

        // Kryje jen zed nebo predmet - jiny hrac v ceste neni stit (vybuch i odhozeni dostane i ten za nim).
        Vector3 delta = to - from;
        var hits = Physics.RaycastAll(from, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        var targetOwner = target.GetComponentInParent<NetworkObject>();
        foreach (var hit in hits)
        {
            if (ignoredOccluder != null && hit.collider.transform.IsChildOf(ignoredOccluder)) continue;
            if (hit.collider == target) return true;
            if (targetOwner != null && hit.collider.GetComponentInParent<NetworkObject>() == targetOwner) return true;
            if (hit.collider.GetComponentInParent<PlayerHero>() != null) continue;
            return false;
        }
        return true;
    }

    // Prime poskozeni hrace (jen server). Vlastni tym se neraní. Kdyz zasah zabije, pripise se kill utocnikovi.
    // Zdroj poskozeni pro kill feed (ikona mezi jmeny): "ability:<asset>", "weapon:gun|bow|explosion|melee",
    // "env:car|fall|boiler". Prazdny = nezname.
    public static string AbilitySource(AbilityDefinition ability) => ability != null ? "ability:" + ability.name : "";

    // Nastavi zbran tesne pred DamagePlayer, kdyz zasah byl do hlavy (s bonusem): pri zabiti znacka v seznamu zabiti.
    public static bool HeadshotHit;
    public const string HeadshotSuffix = "|hs";

    public static void DamagePlayer(GameObject attacker, Health target, float amount, string source = "")
    {
        bool headshot = HeadshotHit;
        HeadshotHit = false;
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsServer || target == null) return;

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;
        if (SameTeam(attacker, target.gameObject)) return;

        // Na vlastnim spawnu (kruh u mista oziveni / spawn stodola) je hrac vuci nepratelum (i Glumovi) nezranitelny.
        if (attacker != target.gameObject && SpawnProtected(target.gameObject))
            return;

        // Posileni (Annina ultimatka): posileny utoci silneji a dostava mensi poskozeni.
        var boostedAttacker = attacker != null ? attacker.GetComponent<PlayerHero>() : null;
        if (boostedAttacker != null && boostedAttacker.IsBoosted)
            amount *= NanoBoostAbility.DamageMultiplier;
        if (boostedAttacker != null)
            amount *= boostedAttacker.ServerDamageScale;   // pivo z vycepu
        // Mosinuv tanec (ultimatka): spoluhraci v jeho okruhu davaji dvojnasobne poskozeni
        amount *= BardAbility.DamageScaleFor(attacker);
        // Max: odolnost pri vypadu (mene poskozeni)
        if (attacker != target.gameObject)
            amount *= MaxAbility.IncomingScale(target.gameObject);
        var boostedTarget = target.GetComponent<PlayerHero>();
        if (boostedTarget != null && boostedTarget.IsBoosted)
            amount *= NanoBoostAbility.DamageTakenMultiplier;

        // posadka jedouci rolby: poskozeni bere rolba (2000 HP; pri zniceni posadka umre) - MatchManagerTractor
        if (match != null && attacker != target.gameObject && match.ServerTractorAbsorbs(attacker, target, amount))
            return;


        float before = target.Effective;   // vcetne stitu (zasah do stitu se taky pocita)
        bool wasAlive = target.currentHealth.Value > 0f;

        var targetHero = target.GetComponent<PlayerHero>();
        if (targetHero != null)
            targetHero.ServerNoteAttacker(attacker);
        target.TakeDamage(amount);
        // nasazeny prsten sklouzne, kdyz nositel nekoho zrani
        if (boostedAttacker != null && match != null) match.ServerRingSlips(boostedAttacker);

        bool killed = wasAlive && target.currentHealth.Value <= 0f;

        // Utocnik dostane potvrzeni zasahu (krizek u zamerovace, pri zabiti lebka).
        if (attacker != null && attacker != target.gameObject && target.Effective < before)
        {
            var attackerHero = attacker.GetComponent<PlayerHero>();
            if (attackerHero != null)
            {
                attackerHero.ServerNotifyHit(killed);
                attackerHero.ServerAddUltCharge(before - target.Effective);
                // zasah zbrani: obcas hlaska k utoku (pri zabiti mluvi hlaska k zabiti)
                if (!killed && source != null && source.StartsWith("weapon:"))
                    attackerHero.ServerAttackLine(target.GetComponent<PlayerHero>());
            }

            var recorder = attacker.GetComponent<PotgRecorder>();
            if (recorder != null)
                recorder.ServerAddDamage(before - target.Effective);
        }

        // Lifesteal (Pova): utocnik si vyleci cast skutecne zpusobeneho poskozeni.
        float dealt = before - target.Effective;
        var stealer = attacker != null ? attacker.GetComponent<PlayerHero>() : null;
        if (dealt > 0f && stealer != null && stealer.Hero != null && stealer.Hero.lifesteal > 0f && attacker != target.gameObject)
        {
            var stealerHealth = attacker.GetComponent<Health>();
            if (stealerHealth != null && stealerHealth.currentHealth.Value > 0f)
                stealerHealth.Heal(dealt * stealer.Hero.lifesteal);
        }

        if (killed && match != null)
            match.ReportKill(attacker, target.gameObject, headshot ? (source ?? "") + HeadshotSuffix : source);
    }

    // Leceni spoluhrace (jen server): lecitel dostane potvrzeni zasahu a nabiti ultimatky za vylecene HP.
    // Nasobek leceni od hrdiny lecitele (HeroDefinition.healingScale; David a Mosino 0,75).
    public static float HealingScale(GameObject healer)
    {
        var hero = healer != null ? healer.GetComponent<PlayerHero>() : null;
        return hero != null && hero.Hero != null ? hero.Hero.healingScale : 1f;
    }

    public static float HealPlayer(GameObject healer, Health target, float amount)
    {
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsServer || target == null) return 0f;

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return 0f;

        amount *= HealingScale(healer);
        float healed = target.Heal(amount);
        if (healer != null && healed > 0f && healer != target.gameObject)
        {
            var healerHero = healer.GetComponent<PlayerHero>();
            if (healerHero != null)
            {
                healerHero.ServerNotifyHit(false);
                healerHero.ServerAddUltCharge(healed);
                healerHero.ServerAddHealingStat(healed);
            }
            HealedLine(target, healed);
        }
        return healed;
    }

    // Vyleceny obcas podekuje / okomentuje to (hlaska "healed") - leceni od spoluhrace i z balicku (lekarnicka, Tramal...).
    public static void HealedLine(Health target, float healed, float minAmount = 5f)
    {
        if (target == null || healed < minAmount || healed <= 0f) return;
        var targetHero = target.GetComponent<PlayerHero>();
        if (targetHero != null)
            targetHero.SayOccasionally(VoiceKind.Healed, 0.5f, 12f, 25f);
    }

    // Plosny vybuch (jen server): damage klesa od stredu (100 %) po okraj (edgeFactor), neprochazi zdmi, nevraci vlastni tym.
    // 'direct' = hrac, ktereho projektil trefil primo: dostane plne poskozeni bez ohledu na vzdalenost od stredu.
    // Collider.ClosestPoint umi jen box / koule / kapsle / konvexni mesh; u ostatnich (nekonvexni mesh, teren)
    // by jen varoval a vratil nesmysl - pak nejblizsi bod obalu.
    public static Vector3 ClosestPoint(Collider col, Vector3 point)
    {
        bool supported = col is BoxCollider || col is SphereCollider || col is CapsuleCollider || (col is MeshCollider mesh && mesh.convex);
        return supported ? col.ClosestPoint(point) : col.bounds.ClosestPoint(point);
    }

    // Vraci, kolik nepratel (a cvicnych tercu) vybuch zasahl.
    public static int Explode(GameObject attacker, Vector3 position, float radius, float damage, float edgeFactor, Health direct = null, string source = "", Transform ignoredOccluder = null)
    {
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsServer) return 0;

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return 0;

        int hits = 0;
        var damaged = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(position, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var dummy = col.GetComponentInParent<Target>();
            if (dummy != null)
            {
                dummy.TakeDamage(damage);
                hits++;
            }

            var boulder = col.GetComponentInParent<BoulderHitbox>();
            if (boulder != null)
                boulder.Damage(attacker, damage);

            var trap = col.GetComponentInParent<TrapHitbox>();
            if (trap != null)
                trap.Damage(attacker, damage);

            if (col.GetComponentInParent<GollumHitbox>() != null)
                GollumHitbox.Damage(attacker, damage);

            var health = col.GetComponentInParent<Health>();
            if (health == null || damaged.Contains(health)) continue;
            if (SameTeam(attacker, health.gameObject)) continue;
            if (!HasLineOfSight(position + Vector3.up * 0.6f, col, ignoredOccluder)) continue;
            damaged.Add(health);

            float distance = Vector3.Distance(position, health.transform.position);
            float scaled = health == direct ? damage : damage * Mathf.Lerp(1f, edgeFactor, Mathf.Clamp01(distance / radius));
            DamagePlayer(attacker, health, scaled, source);
            hits++;
        }
        return hits;
    }

    // Stoji hrac na spawnu sveho tymu? Tam na nej nepratele nemuzou - poskozeni ani omezeni (omraceni, uspani,
    // past, zpomaleni, umlceni, odhozeni, pritazeni, blokace leceni).
    public static bool SpawnProtected(GameObject player)
    {
        var team = player != null ? player.GetComponent<PlayerTeam>() : null;
        var match = MatchManager.Instance;
        return team != null && match != null && !match.IsLobby && SpawnZone.Contains(team.teamId.Value, player.transform.position);
    }

    // Odhozeni hracu vybuchem (jen server): neprochazi zdmi, spoluhrace neodhazuje, utocnika sameho ano (selfScale).
    public static void Knockback(GameObject attacker, Vector3 position, float radius, float force, float selfScale, Transform ignoredOccluder = null)
    {
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsServer || force <= 0f) return;

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;

        var pushed = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(position, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var health = col.GetComponentInParent<Health>();
            if (health == null || pushed.Contains(health) || health.currentHealth.Value <= 0f) continue;

            bool self = health.gameObject == attacker;
            if (!self && SameTeam(attacker, health.gameObject)) continue;
            if (self && selfScale <= 0f) continue;
            if (!HasLineOfSight(position + Vector3.up * 0.3f, col, ignoredOccluder)) continue;
            pushed.Add(health);

            var controller = health.GetComponent<FirstPersonController>();
            if (controller == null) continue;

            Vector3 direction = col.bounds.center - position;
            float distance = direction.magnitude;
            direction = distance > 0.05f ? direction / distance : Vector3.up;
            direction = (direction + Vector3.up * 0.7f).normalized;

            float scale = Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(distance / radius)) * (self ? selfScale : 1f);
            controller.ServerKnockback(direction * force * scale, !self);

            // Kdyz odhozeny spadne z mapy, zabiti patri tomu, kdo ho odhodil.
            var pushedHero = health.GetComponent<PlayerHero>();
            if (pushedHero != null && !self)
                pushedHero.ServerNoteAttacker(attacker);
        }
    }
}
