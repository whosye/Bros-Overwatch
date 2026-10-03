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
    Axe,
    Bow
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

    [Header("Natahování (luk; jen projektil)")]
    [Tooltip("Jak dlouho (s) se zbran natahuje do plne sily. 0 = strili se hned. Drzenim tlacitka se natahuje, pustenim vystreli.")]
    public float chargeTime = 0f;
    [Tooltip("Poskozeni nenatazene strely (plne natazena dava 'damage').")]
    public float minChargeDamage = 20f;
    [Tooltip("Rychlost nenatazene strely (plne natazena leti 'projectileSpeed').")]
    public float minChargeSpeed = 25f;

    [Header("Pokles poškození s dálkou (jen okamžitý zásah)")]
    [Tooltip("Do teto vzdalenosti (m) dava zbran plne poskozeni. 0 = poskozeni s dalkou neklesa.")]
    public float falloffStart = 0f;
    [Tooltip("Od teto vzdalenosti (m) dava zbran uz jen nejmensi poskozeni; mezi obema hodnotami klesa plynule.")]
    public float falloffEnd = 0f;
    [Tooltip("Nejmensi poskozeni na velkou dalku jako cast plneho (0.4 = 40 %).")]
    public float falloffMin = 0.4f;
    [Tooltip("Druhy krok: od 'Falloff End' do teto vzdalenosti (m) klesa poskozeni dal az na 'Far Falloff Min'. 0 = bez druheho kroku.")]
    public float farFalloffEnd = 0f;
    [Tooltip("Poskozeni na konci druheho kroku jako cast plneho (0.1 = 10 %).")]
    public float farFalloffMin = 0.1f;
    [Tooltip("Za touto vzdalenosti (m) zasah nedava zadne poskozeni. 0 = bez omezeni.")]
    public float maxDamageRange = 0f;

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
    public bool IsCharged => IsProjectile && chargeTime > 0f;
    // Zbran bez zasobniku (luk): nikdy se neprebiji.
    public bool HasAmmo => !IsMelee && maxAmmo > 0;

    // Poskozeni okamziteho zasahu na danou vzdalenost.
    public float DamageAt(float distance)
    {
        if (fireMode == FireMode.Hitscan && maxDamageRange > 0f && distance > maxDamageRange) return 0f;
        if (fireMode != FireMode.Hitscan || falloffEnd <= falloffStart || falloffStart <= 0f) return damage;

        // Druhy krok: za 'Falloff End' klesa dal az na 'Far Falloff Min'.
        if (farFalloffEnd > falloffEnd && distance > falloffEnd)
        {
            float far = Mathf.InverseLerp(falloffEnd, farFalloffEnd, distance);
            return damage * Mathf.Lerp(Mathf.Clamp01(falloffMin), Mathf.Clamp01(farFalloffMin), far);
        }

        float t = Mathf.InverseLerp(falloffStart, falloffEnd, distance);
        return damage * Mathf.Lerp(1f, Mathf.Clamp01(falloffMin), t);
    }
    public bool Bounces => IsProjectile && projectileBounce > 0f;
}
