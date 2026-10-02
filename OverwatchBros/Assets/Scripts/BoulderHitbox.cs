using Unity.Netcode;
using UnityEngine;

// Znacka na Honzove balvanu: podle ni zbrane a vybuchy poznaji, ze trefily balvan, a komu patri.
public class BoulderHitbox : MonoBehaviour
{
    public BoulderAbility Owner;

    public bool IsFriendly(GameObject attacker)
    {
        return Owner == null || attacker == Owner.gameObject || Combat.SameTeam(attacker, Owner.gameObject);
    }

    // Jen server.
    public void Damage(GameObject attacker, float amount)
    {
        if (Owner != null)
            Owner.ServerDamage(attacker, amount);
    }
}
