using Unity.Netcode;
using UnityEngine;

// Tajny vstup do jeskyne pod tunelem: kdo se dostane do bazenu (na hladinu - skokem nebo po zebriku), toho voda
// "vcucne" a objevi se dole v jeskyni (cakanec). Ven: po zebriku v sachte nahoru - nahore ho to vynese vedle bazenu.
// Hlida jen mistniho hrace (pohyb ridi vlastnik), boti se sem nedostanou.
public class SecretPassage : MonoBehaviour
{
    [Header("Bazen (vstup)")]
    public Vector3 poolCenter;
    public float poolRadius = 4.1f;
    public float poolTop = 2.4f;

    [Header("Prichod do jeskyne")]
    public Vector3 arrival;
    public float arrivalYaw = 90f;

    [Header("Vychod (sachta nahore, chodidla v teto oblasti)")]
    public Vector3 exitMin, exitMax;
    public Vector3 exitPoint;
    public float exitYaw;

    float readyAt;

    void Update()
    {
        if (Time.time < readyAt) return;
        var network = NetworkManager.Singleton;
        var player = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        var fpc = player != null ? player.GetComponent<FirstPersonController>() : null;
        if (fpc == null || !fpc.IsOwner || fpc.IsDead || fpc.Bot != null || ReplayPlayer.Active) return;

        Vector3 feet = fpc.transform.position;
        Vector3 fromPool = feet - poolCenter;
        fromPool.y = 0f;
        if (fromPool.magnitude < poolRadius && feet.y > poolTop - 0.8f && feet.y < poolTop + 1.6f)
        {
            ProceduralSfx.Play(ProceduralSfx.BigSplash, feet, 0.9f);
            fpc.OwnerTeleport(arrival, arrivalYaw);
            ProceduralSfx.Play(ProceduralSfx.Splash, arrival, 0.7f);
            readyAt = Time.time + 1f;
            return;
        }

        if (feet.x > exitMin.x && feet.x < exitMax.x && feet.y > exitMin.y && feet.y < exitMax.y && feet.z > exitMin.z && feet.z < exitMax.z)
        {
            fpc.OwnerTeleport(exitPoint, exitYaw);
            ProceduralSfx.Play(ProceduralSfx.Splash, exitPoint, 0.6f);
            readyAt = Time.time + 1f;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.8f);
        Gizmos.DrawWireSphere(poolCenter + Vector3.up * poolTop, poolRadius);
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.8f);
        Gizmos.DrawWireCube((exitMin + exitMax) * 0.5f, exitMax - exitMin);
        Gizmos.DrawSphere(arrival, 0.3f);
        Gizmos.DrawSphere(exitPoint, 0.3f);
    }
}
