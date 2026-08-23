using UnityEngine;
using UnityEngine.InputSystem;

public class WeaponShooting : MonoBehaviour
{
    public WeaponDefinition weapon;
    public Camera playerCamera;

    int currentAmmo;
    float nextFireTime;

    void Start()
    {
        currentAmmo = weapon.maxAmmo;
    }

    void Update()
    {
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
        }

        Debug.Log($"Vystřeleno. Zbývá náboju: {currentAmmo}/{weapon.maxAmmo}");
    }

    void Reload()
    {
        currentAmmo = weapon.maxAmmo;
        Debug.Log("Přebito.");
    }
}
