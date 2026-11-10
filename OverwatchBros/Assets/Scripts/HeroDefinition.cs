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
    Ezekiel,       // Sindel: Ezechiel 25:17 - kazani ve vzduchu a uder svetla (Q)
    MaxReap,       // Max: Rozpolceni - vypad a otocka kosou (Shift)
    ShadowStep,    // Max: Stinovy krok - pruchod zdi (E)
    MaxSlash,      // Max: Otocka kosou - 360 st. kolem sebe (prave tlacitko)
    Trespass,      // Max: Vpad do stinu - zmizi v nepriteli a vyrazi ven (Q)
    SayWhat        // Sindel: "Say what again?!" - vykrik do kuzele, nepratele zpomali a umlci (prave tlacitko)
}

[CreateAssetMenu(fileName = "NewHero", menuName = "BrosOverwatch/Hero")]
public class HeroDefinition : ScriptableObject
{
    public string heroName;
    public Color color = Color.white;
    public float maxHealth = 100f;
    [Tooltip("Nasobek veskereho leceni, ktere hrdina dava (1 = plne, 0.75 = o 25 % mene).")]
    public float healingScale = 1f;
    [Tooltip("Pri mireni dalekohledem (prave tlacitko) postava drzi zbran namirenou obema rukama v poze z animace strelby (jako Viktor). Potrebuje animaci Shoot v sade animaci.")]
    public bool scopeAimPose;
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

    [Header("Pasivni bonusy")]
    [Tooltip("Nasobek rychlosti chuze a behu (1 = jako ostatni).")]
    public float moveSpeed = 1f;
    [Tooltip("Podil zpusobeneho poskozeni (zbran i schopnosti), ktery si hrdina vyleci.")]
    [Range(0f, 1f)] public float lifesteal = 0f;
    [Tooltip("Zabiti nebo asistence zkrati zbyvajici cooldowny schopnosti o tento podil.")]
    [Range(0f, 1f)] public float takedownCooldownCut = 0f;

    [Header("Pasivní: granáty po smrti (0 = žádné)")]
    public int deathGrenades = 0;
    public float deathGrenadeDamage = 30f;
    public float deathGrenadeRadius = 3.5f;

    [Tooltip("3D model postavy (prefab s Animatorem a Humanoid avatarem). Kdyz je prazdny, hrac je kapsle.")]
    public GameObject characterPrefab;
    [Tooltip("Zabarvit telo postavy barvou hrdiny. Vypnout u postav s vlastnimi texturami (napr. podle fotky).")]
    public bool tintCharacter = true;
    [Tooltip("Vlastni animace hrdiny (napr. strelba z luku). Prazdna policka = spolecna sada Resources/Characters/CharacterAnimations.")]
    public CharacterAnimSet animations;

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
    [Tooltip("Kdyz strili po nepriteli a trefuje ho (obcas, nejvys jednou za nekolik sekund).")]
    public AudioClip[] attackLines;
    [Tooltip("Kdyz zbrani vyleci spoluhrace (obcas, nejvys jednou za nekolik sekund).")]
    public AudioClip[] healLines;
    [Tooltip("Kdyz zbrani trefi nepritele do hlavy (obcas).")]
    public AudioClip[] headshotLines;
    [Tooltip("Kdyz schopnost mine (David: uspavaci sipka nikoho netrefi).")]
    public AudioClip[] missLines;
    [Tooltip("Kdyz priblizi zamerovacem - prave tlacitko (obcas).")]
    public AudioClip[] scopeLines;
    [Tooltip("Hlaska k ultimatce (Q) dostane ozvenu. Vypnout, kdyz ji uz ma nahravka sama (Mirek, Sindel).")]
    public bool ultVoiceEcho = true;
    [Tooltip("Hlaska k ultimatce se prehraje jen poslednich N sekund (0 = cela). U Sindela urcuje i delku ultimatky - uder prijde na konci hlasky.")]
    public float ultVoiceMaxSeconds = 0f;

    [Tooltip("Rychlost hlasky k ultimatce (1 = normalne, 1.5 = o polovinu rychleji a vys). Konec (Ult Voice Normal Tail) hraje normalne.")]
    public float ultVoiceSpeed = 1f;
    [Tooltip("Kolik poslednich sekund hlasky k ultimatce hraje normalni rychlosti (zaverecne \"buuh\").")]
    public float ultVoiceNormalTail = 0f;

    // Od kolikate sekundy hrat nahravku ultimatky (zbytek = poslednich ultVoiceMaxSeconds).
    public float UltVoiceSkip(AudioClip clip) =>
        clip != null && ultVoiceMaxSeconds > 0.5f && clip.length > ultVoiceMaxSeconds ? clip.length - ultVoiceMaxSeconds : 0f;

    float Speed => Mathf.Clamp(ultVoiceSpeed, 0.5f, 3f);

    // Kde v nahravce se prepina ze zrychlene na normalni rychlost.
    public float UltVoiceNormalFrom(AudioClip clip) =>
        clip == null ? 0f : Mathf.Max(UltVoiceSkip(clip), clip.length - Mathf.Max(0f, ultVoiceNormalTail));

    // Jak dlouho hlaska k ultimatce skutecne hraje (se zkracenim a zrychlenim).
    public float UltVoiceDuration(AudioClip clip)
    {
        if (clip == null) return 0f;
        float start = UltVoiceSkip(clip), normalFrom = UltVoiceNormalFrom(clip);
        return (normalFrom - start) / Speed + (clip.length - normalFrom);
    }

    // 'seconds' od zacatku hlasky -> misto v nahravce a rychlost prehravani.
    public void UltVoiceAt(AudioClip clip, float seconds, out float clipTime, out float pitch)
    {
        float start = UltVoiceSkip(clip), normalFrom = UltVoiceNormalFrom(clip);
        float fastPart = (normalFrom - start) / Speed;
        if (seconds < fastPart) { clipTime = start + seconds * Speed; pitch = Speed; }
        else { clipTime = normalFrom + (seconds - fastPart); pitch = 1f; }
    }
}
