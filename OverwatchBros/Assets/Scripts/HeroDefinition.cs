using UnityEngine;

public enum AbilityKind
{
    None,
    Dash,
    LeapStrike
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

    [Header("Voice lines (volitelne, pretahni sem nahravky)")]
    public AudioClip[] spawnLines;
    public AudioClip[] killLines;
    public AudioClip[] deathLines;
}
