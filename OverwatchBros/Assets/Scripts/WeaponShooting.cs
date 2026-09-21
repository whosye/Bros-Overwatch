using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class WeaponShooting : NetworkBehaviour
{
    public WeaponDefinition weapon;
    public Camera playerCamera;

    int currentAmmo;
    float nextFireTime;
    bool ammoInitialized;
    FirstPersonController fpc;

    public int CurrentAmmo => currentAmmo;

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
    }

    void Start()
    {
        if (!ammoInitialized && weapon != null)
        {
            currentAmmo = weapon.maxAmmo;
            ammoInitialized = true;
        }
    }

    public void SetWeapon(WeaponDefinition newWeapon)
    {
        if (newWeapon == null) return;

        weapon = newWeapon;
        currentAmmo = newWeapon.maxAmmo;
        ammoInitialized = true;
    }

    void Update()
    {
        if (!IsOwner) return;
        if (weapon == null) return;
        if (fpc != null && fpc.InputBlocked) return;
        if (!GameSettings.CursorLocked) return;

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
            nextFireTime = Time.time + 0.35f;
            ProceduralSfx.Play(ProceduralSfx.Empty, transform.position, 0.6f);
            return;
        }

        nextFireTime = Time.time + 1f / weapon.fireRate;
        currentAmmo--;

        ProceduralSfx.Play(ProceduralSfx.Gunshot, playerCamera.transform.position, 0.7f);
        ShotFxServerRpc();

        if (Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward, out RaycastHit hit, weapon.range, ~0, QueryTriggerInteraction.Ignore))
        {
            Target target = hit.collider.GetComponent<Target>();
            if (target != null)
                target.TakeDamage(weapon.damage);

            NetworkObject hitNetworkObject = hit.collider.GetComponentInParent<NetworkObject>();
            if (hitNetworkObject != null && hitNetworkObject != NetworkObject)
            {
                RequestDamageServerRpc(hitNetworkObject, weapon.damage);
                ProceduralSfx.Play(ProceduralSfx.Hit, transform.position, 0.8f);
            }
        }
    }

    [ServerRpc]
    void ShotFxServerRpc()
    {
        ShotFxClientRpc();
    }

    [ClientRpc]
    void ShotFxClientRpc()
    {
        if (IsOwner) return;
        ProceduralSfx.Play(ProceduralSfx.Gunshot, transform.position, 0.8f);
    }

    [ServerRpc]
    void RequestDamageServerRpc(NetworkObjectReference targetRef, float amount)
    {
        if (MatchManager.Instance != null && MatchManager.Instance.IsOver) return;
        if (!targetRef.TryGet(out NetworkObject targetObject)) return;

        Health targetHealth = targetObject.GetComponent<Health>();
        if (targetHealth == null) return;

        var shooterTeam = GetComponent<PlayerTeam>();
        var targetTeam = targetObject.GetComponent<PlayerTeam>();
        if (shooterTeam != null && targetTeam != null && shooterTeam.teamId.Value == targetTeam.teamId.Value)
            return;

        bool wasAlive = targetHealth.currentHealth.Value > 0f;
        targetHealth.TakeDamage(amount);

        if (wasAlive && targetHealth.currentHealth.Value <= 0f && MatchManager.Instance != null)
            MatchManager.Instance.ReportKill(gameObject);
    }

    void Reload()
    {
        if (currentAmmo == weapon.maxAmmo) return;

        currentAmmo = weapon.maxAmmo;
        ProceduralSfx.Play(ProceduralSfx.Reload, transform.position, 0.6f);
    }
}
