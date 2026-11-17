using UnityEngine;

// Znacka na Glumovi (RingWorldView mu da kolizi): podle ni zbrane, projektily a vybuchy poznaji, ze trefily Gluma.
// Poskozeni ho nezabije - kdyz ho dostane dost (podle toho, jak je nastvany), na chvili uteče (MatchManagerRing).
public class GollumHitbox : MonoBehaviour
{
    // Jen server.
    public static void Damage(GameObject attacker, float amount)
    {
        var match = MatchManager.Instance;
        if (match != null && match.IsServer) match.ServerDamageGollum(attacker, amount);
    }
}
