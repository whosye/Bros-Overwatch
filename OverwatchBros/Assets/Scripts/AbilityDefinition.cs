using UnityEngine;

[CreateAssetMenu(fileName = "NewAbility", menuName = "BrosOverwatch/Ability")]
public class AbilityDefinition : ScriptableObject
{
    public string abilityName;
    [Tooltip("Ikona schopnosti v HUD (bily piktogram na pruhlednem pozadi).")]
    public Texture2D icon;
    public float cooldown;
    [Tooltip("Ultimatni schopnost (Q): kolik bodu nabiti stoji. Nabiji se zpusobenym poskozenim (1 bod za bod poskozeni), "
        + "lecenim spoluhracu a pomalu sama casem. 0 = schopnost se ridi jen cooldownem.")]
    public float ultCost = 0f;
    public float power;

    public float duration = 0.2f;

    public float radius = 6f;

    // Nejvetsi vodorovna vzdalenost od mista vzletu, kam jde dopadnout (Ohnivy dopad).
    public float range = 25f;

    [Header("Výbušniny (nálož, balvan)")]
    [Tooltip("Sila odhozeni v m/s (naloz odhodi nepratele i vlastniho hrace).")]
    public float knockback = 0f;
    [Tooltip("Pocet naboju schopnosti (kazdy se dobiji 'cooldown' sekund).")]
    public int charges = 1;

    [Header("Zvuk")]
    [Tooltip("Vlastní nahrávka pro tuto schopnost (Ohnivý dopad: při dopadu; Modrý plamen: při aktivaci). Prázdné = jen generovaný placeholder zvuk.")]
    public AudioClip sound;

    [Tooltip("Hlasky hrdiny pri pouziti teto schopnosti (z vice se vybira nahodne). Prazdne = bez hlasky.")]
    public AudioClip[] voiceLines;

    [Header("Blok (pravé tlačítko myši)")]
    [Tooltip("Kolik prichoziho poskozeni se zablokuje (0.8 = 80 %). Zablokovana cast se odecita z baru; bar = plne zdravi hrdiny.")]
    public float blockAbsorb = 0.8f;
    [Tooltip("Nasobek rychlosti pohybu behem bloku (0.5 = pulka).")]
    public float blockSpeed = 0.5f;
    [Tooltip("Za jak dlouho (s) bez poskozeni se bar zacne regenerovat.")]
    public float regenDelay = 5f;
    [Tooltip("Za jak dlouho (s) se prazdny bar cely obnovi.")]
    public float regenTime = 6f;

    [Header("Modrý plamen (Rush)")]
    [Tooltip("Rychlost pohybu v m/s, kdyz se schopnost drzi.")]
    public float speed = 14f;
    [Tooltip("Jak casto (s) se pri dotyku uděluje poškození. 'power' = damage jednoho tiku, 'duration' = nejdelsi doba drzeni, 'radius' = dosah dotyku.")]
    public float tickInterval = 0.25f;
    [Tooltip("Kolik zpusobeneho poskozeni se vrati jako zivoty (1 = 100 %).")]
    public float healRatio = 1f;
}
