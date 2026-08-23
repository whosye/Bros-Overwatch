using UnityEngine;
using TMPro;
using Unity.Netcode;

public class PlayerHUD : MonoBehaviour
{
    public Health health;
    public WeaponShooting weapon;
    public TMP_Text healthText;
    public TMP_Text ammoText;

    void Update()
    {
        if (health == null || weapon == null)
        {
            FindLocalPlayer();
            return;
        }

        healthText.text = $"HP: {health.currentHealth.Value:0} / {health.maxHealth:0}";
        ammoText.text = $"Ammo: {weapon.CurrentAmmo} / {weapon.weapon.maxAmmo}";
    }

    void FindLocalPlayer()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            return;

        NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localPlayerObject == null)
            return;

        health = localPlayerObject.GetComponent<Health>();
        weapon = localPlayerObject.GetComponent<WeaponShooting>();
    }
}
