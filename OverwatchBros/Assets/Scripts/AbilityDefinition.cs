using UnityEngine;

[CreateAssetMenu(fileName = "NewAbility", menuName = "BrosOverwatch/Ability")]
public class AbilityDefinition : ScriptableObject
{
    public string abilityName;
    public float cooldown;
    public float power;

    public float duration = 0.2f;

}
