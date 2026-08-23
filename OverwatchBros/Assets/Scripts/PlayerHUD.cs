using UnityEngine;
using TMPro;

public class PlayerHUD : MonoBehaviour
{
    public Health health;
    public WeaponShooting weapon;
    public TMP_Text healthText;
    public TMP_Text ammoText;

    void Update()
    {
        healthText.text = $"HP: {health.currentHealth:0} / {health.maxHealth:0}";
        ammoText.text = $"Ammo: {weapon.CurrentAmmo} / {weapon.weapon.maxAmmo}";
    }
}
