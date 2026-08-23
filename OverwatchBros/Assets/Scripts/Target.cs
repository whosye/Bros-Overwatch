using UnityEngine;

public class Target : MonoBehaviour
{
    public void TakeDamage(float amount)
    {
        Debug.Log($"{gameObject.name} zasažen za {amount} damage.");
    }
}
