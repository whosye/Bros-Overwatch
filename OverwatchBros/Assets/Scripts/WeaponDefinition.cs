using UnityEngine;

public enum FireMode
{
    Hitscan,
    Projectile,
    Melee
}

public enum HeldModel
{
    Auto,
    None,
    Gun,
    Axe
}

[CreateAssetMenu(fileName = "NewWeapon", menuName = "BrosOverwatch/Weapon")]
public class WeaponDefinition : ScriptableObject
{
    public string weaponName = "Pistol";
    public float damage = 10f;
    public float fireRate = 8f;
    public int maxAmmo = 12;

    [Header("Přebíjení (R, nebo samo při prázdném zásobníku)")]
    [Tooltip("Jak dlouho trva prebiti (s). Behem prebijeni nejde strilet.")]
    public float reloadTime = 1.5f;
    [Tooltip("Vlastni animace prebijeni zbrane v ruce (granatomet). Vypnuto = zbran se jen skloni a vrati.")]
    public bool reloadAnimation = false;

    // Hitscan: dosah okamziteho paprsku. Projectile: nejvetsi vzdalenost, kterou projektil preleti.
    public float range = 100f;

    [Header("Projektil (jen kdyz Fire Mode = Projectile)")]
    public FireMode fireMode = FireMode.Hitscan;
    public float projectileSpeed = 40f;
    [Tooltip("Zrychleni smerem dolu v m/s^2 (0 = leti primo, ~9.8 = balisticky oblouk).")]
    public float projectileGravity = 0f;
    [Tooltip("Polomer projektilu (kolize i velikost vizualu).")]
    public float projectileRadius = 0.15f;
    [Tooltip("Vetsi nez 0 = vybuch s timto polomerem (damage klesa ke kraji), 0 = jen primy zasah.")]
    public float explosionRadius = 0f;
    [Tooltip("Granat: od zdi a zeme se odrazi (0 = neodrazi se a vybuchne hned; 0.45 = odskoci s 45 % rychlosti). Zasah hrace vybuchne vzdy.")]
    public float projectileBounce = 0f;
    [Tooltip("Granat: za jak dlouho po prvnim odrazu vybuchne (s).")]
    public float projectileFuse = 1f;
    public Color projectileColor = new Color(1f, 0.8f, 0.2f, 1f);
    public bool projectileTrail = true;
    [Tooltip("Volitelny vlastni model projektilu. Kdyz je prazdny, pouzije se svitici koule v barve projektilu.")]
    public GameObject projectilePrefab;

    [Header("Blizky souboj (jen kdyz Fire Mode = Melee)")]
    [Tooltip("Sirka uderu (polomer sweepu pred hracem). Dosah uderu je 'range', ammo se u melee nepouziva.")]
    public float meleeRadius = 0.6f;

    [Header("Zbran v ruce")]
    [Tooltip("Auto = sekyrka pro melee, jinak pistole (u projektilu raketomet).")]
    public HeldModel heldModel = HeldModel.Auto;
    [Tooltip("Hrdina drzi zbran v obou rukou (napr. dve sekyrky), utoky se strida.")]
    public bool dualWield = false;
    [Tooltip("Volitelny vlastni model zbrane. Kdyz je prazdny, pouzije se model z kostek.")]
    public GameObject heldPrefab;

    public bool IsProjectile => fireMode == FireMode.Projectile;
    public bool IsMelee => fireMode == FireMode.Melee;
    public bool Bounces => IsProjectile && projectileBounce > 0f;
}
