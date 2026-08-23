using UnityEngine;

[CreateAssetMenu(fileName = "NewWeapon", menuName = "BrosOverwatch/Weapon")]
public class WeaponDefinition : ScriptableObject
{
    public string weaponName = "Pistol";
    public float damage = 10f;
    public float fireRate = 8f;
    public int maxAmmo = 12;
    public float range = 100f;
}
