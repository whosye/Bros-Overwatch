using UnityEngine;

[CreateAssetMenu(fileName = "NewHero", menuName = "BrosOverwatch/Hero")]
public class HeroDefinition : ScriptableObject
{
    public string heroName;
    public WeaponDefinition weapon;
    public AbilityDefinition ability;
}
