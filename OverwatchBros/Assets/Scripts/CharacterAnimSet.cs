using UnityEngine;

// Sada animaci postavy (z Universal Animation Library). Vytvari ji Editor/CharacterSetup a nacita CharacterVisual z Resources.
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

    // Pro uvodni scenky pred "play of the game".
    public AnimationClip sitIdle;
    public AnimationClip hitChest;
}
