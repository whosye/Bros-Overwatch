using UnityEngine;

// Sada animaci postavy (z Universal Animation Library). Vytvari ji Editor/CharacterSetup a nacita CharacterVisual z Resources.
// Hrdina muze mit i vlastni sadu (HeroDefinition.animations): jeji vyplnena policka prepisi spolecnou sadu, prazdna se vezmou ze spolecne.
[CreateAssetMenu(menuName = "Overwatch Bros/Character Anim Set", fileName = "HeroAnimations")]
public class CharacterAnimSet : ScriptableObject
{
    public AnimationClip idle;
    public AnimationClip walk;
    public AnimationClip jog;
    public AnimationClip sprint;
    public AnimationClip jump;
    public AnimationClip death;
    public AnimationClip meleeAttack;
    public AnimationClip shoot;
    public AnimationClip reload;
    public AnimationClip blockPose;
    public AnimationClip dashPose;
    // Ultimatka pres cele telo (Mosino: tanec Flair), prehrava se ve smycce po dobu ultimatky.
    public AnimationClip ultimate;
    // Animace schopnosti na Shift a PTM pres cele telo (Max: skok s uderem kosou, otocka kosou), prehrava se jen jejich usek.
    public AnimationClip shiftAbility;
    public AnimationClip rmbAbility;
    // Schopnost na E pres cele telo (Kondulic: kop), prehrava se cela.
    public AnimationClip eAbility;

    // Pro uvodni scenky pred "play of the game".
    public AnimationClip sitIdle;
    public AnimationClip hitChest;
}
