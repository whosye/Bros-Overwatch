using UnityEngine;

public enum AbilityKind
{
    None,
    Dash,
    LeapStrike,
    Rush,
    Mine,      // Honza: naloz (Shift hodi, prave tlacitko odpali), odhazuje
    Trap,      // Honza: past (E)
    Boulder,   // Honza: riditelny balvan (Q)
    HealField, // Viktor: lecive pole (E)
    Flash,     // Viktor: oslepujici granat (prave tlacitko)
    Visor,     // Viktor: takticky zamerovac (Q)
    Hook,      // Ayran: hak, ktery pritahne nepritele (E)
    ScoutArrow, // Mirek: pruzkumny sip - odhali nepratele pres zdi (E)
    RapidFire,  // Mirek: rychlopalba z luku (prave tlacitko)
    Storm,      // Mirek: smrst letici skrz zdi (Q)
    SleepDart,  // Anna: uspavaci sipka (Shift)
    BioticGrenade, // Anna: biotický granat - leci spoluhrace, zrani a blokuje leceni nepratel (E)
    NanoBoost,  // Anna: posileni spoluhrace (Q)
    Grapple,    // Sniper: hak, kterym se pritahne na misto (Shift)
    Kick,       // Sniper: odkopnuti na blizko (E)
    Infra,      // Sniper: infravize - odhali vsechny nepratele (Q)
    Blink,      // Flanker: kratke premisteni, 3 nabiti (Shift)
    Recall,     // Flanker: navrat v case (E)
    PulseBomb,  // Flanker: lepiva pulzni bomba (Q)
    Crossfade,  // Bard: prepnuti aury leceni / zrychleni (Shift)
    Amp,        // Bard: zesileni aury (E)
    Concert,    // Bard: koncert - docasny stit spoluhracum (Q)
    Soundwave,  // Bard: basovy uder - odhodi nepratele, mirenim pod sebe ho vystreli (prave tlacitko)
    RighteousLeap, // Sindel: vyskok se vznasenim (Shift)
    HolyGrenade,   // Sindel: svaty granat - hod obloukem, plosny vybuch (E)
    Ezekiel        // Sindel: Ezechiel 25:17 - kazani ve vzduchu a uder svetla (Q)
}

[CreateAssetMenu(fileName = "NewHero", menuName = "BrosOverwatch/Hero")]
public class HeroDefinition : ScriptableObject
{
    public string heroName;
    public Color color = Color.white;
    public float maxHealth = 100f;
    public WeaponDefinition weapon;
    public AbilityKind abilityKind = AbilityKind.Dash;
    public AbilityDefinition ability;

    [Header("Druha schopnost (Left Shift)")]
    public AbilityKind secondaryAbilityKind = AbilityKind.None;
    public AbilityDefinition secondaryAbility;

    [Header("Blok (pravé tlačítko myši, prázdné = hrdina neblokuje)")]
    public AbilityDefinition blockAbility;

    [Header("Třetí schopnost (klávesa E, např. past)")]
    public AbilityKind altAbilityKind = AbilityKind.None;
    public AbilityDefinition altAbility;

    [Header("Čtvrtá schopnost (pravé tlačítko myši, např. oslepující granát)")]
    public AbilityKind rmbAbilityKind = AbilityKind.None;
    public AbilityDefinition rmbAbility;

    [Header("Pasivní: pohyb")]
    [Tooltip("Druhy skok ve vzduchu (mezernik).")]
    public bool doubleJump = false;
    [Tooltip("Kdyz hrac ve skoku narazi na hranu, vytahne se na ni.")]
    public bool ledgeClimb = false;

    [Header("Pasivní: granáty po smrti (0 = žádné)")]
    public int deathGrenades = 0;
    public float deathGrenadeDamage = 30f;
    public float deathGrenadeRadius = 3.5f;

    [Tooltip("3D model postavy (prefab s Animatorem a Humanoid avatarem). Kdyz je prazdny, hrac je kapsle.")]
    public GameObject characterPrefab;
    [Tooltip("Zabarvit telo postavy barvou hrdiny. Vypnout u postav s vlastnimi texturami (napr. podle fotky).")]
    public bool tintCharacter = true;

    [Header("Ruce v pohledu z první osoby")]
    public Color sleeveColor = new Color(0.25f, 0.27f, 0.32f);
    public Color skinColor = new Color(0.87f, 0.67f, 0.55f);

    [Header("Hlášky (volitelné; přetáhni sem nahrávky, z více se vybírá náhodně)")]
    [Tooltip("Vyber hrdiny v lobby a oziveni.")]
    public AudioClip[] spawnLines;
    [Tooltip("Kdyz zabije protihrace.")]
    public AudioClip[] killLines;
    [Tooltip("Kdyz zemre.")]
    public AudioClip[] deathLines;
    [Tooltip("Kdyz dostane poskozeni (nejvys jednou za par sekund). Prazdne = generovany placeholder zvuk.")]
    public AudioClip[] hurtLines;
    [Tooltip("Nahodne pri chozeni (jednou za cas). Hlasky ke schopnostem jsou u kazde schopnosti zvlast (Voice Lines).")]
    public AudioClip[] idleLines;
    [Tooltip("Kdyz ho nekdo znehybni nebo zpomali (napr. slapne na past).")]
    public AudioClip[] snareLines;
}
