using UnityEngine;

public class Hazard : MonoBehaviour
{
    public float damagePerSecond = 30f;

    void OnTriggerStay(Collider other)
    {
        Health health = other.GetComponent<Health>();
        if (health != null)
            health.TakeDamage(damagePerSecond * Time.deltaTime);
    }
}
