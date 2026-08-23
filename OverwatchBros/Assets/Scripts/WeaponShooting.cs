using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class WeaponShooting : NetworkBehaviour
{
    public WeaponDefinition weapon;
    public Camera playerCamera;

    int currentAmmo;
    float nextFireTime;
    public int CurrentAmmo => currentAmmo;
    void Start()
    {
        currentAmmo = weapon.maxAmmo;
    }

    void Update()
    {
        if (!IsOwner) return;

        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            Reload();
            return;
        }

        if (Mouse.current.leftButton.isPressed && Time.time >= nextFireTime)
            Fire();
    }

    void Fire()
    {
        if (currentAmmo <= 0)
        {
            Debug.Log("Cvak — prázdný zásobník, zmáčkni R.");
            return;
        }

        nextFireTime = Time.time + 1f / weapon.fireRate;
        currentAmmo--;

        if (Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, weapon.range))
        {
            Target target = hit.collider.GetComponent<Target>();
            if (target != null)
                target.TakeDamage(weapon.damage);

            NetworkObject hitNetworkObject = hit.collider.GetComponent<NetworkObject>();
            if (hitNetworkObject != null)
                RequestDamageServerRpc(hitNetworkObject, weapon.damage);
        }

        Debug.Log($"Vystřeleno. Zbývá náboju: {currentAmmo}/{weapon.maxAmmo}");
    }

    [ServerRpc]
    void RequestDamageServerRpc(NetworkObjectReference targetRef, float amount)
    {
        if (targetRef.TryGet(out NetworkObject targetObject))
        {
            Health targetHealth = targetObject.GetComponent<Health>();
            if (targetHealth != null)
                targetHealth.TakeDamage(amount);
        }
    }

    void Reload()
    {
        currentAmmo = weapon.maxAmmo;
        Debug.Log("Přebito.");
    }
}
