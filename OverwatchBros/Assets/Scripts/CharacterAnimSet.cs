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
    [Tooltip("Utok zblizka pres cele telo, kdyz postava stoji (nohy udelaji krok / napraZeni z animace). Pri chuzi "
        + "a behu zustanou nohy z chuze (jinak by postava klouzala).")]
    public bool meleeFullBody;
    public AnimationClip shoot;
    [Tooltip("Animace strelby mirici do strany (postava v klipu stoji bokem, napr. Mixamo Gunplay): o kolik stupnu se pri strelbe "
        + "dotoci horni polovina tela kolem svisle osy (+ = doprava), aby zbran mirila dopredu. Zmeri a nastavi "
        + "BrosOverwatch > Animace > Oriznout strelbu na jeden vystrel.")]
    public float shootYaw;
    [Tooltip("Pri strelbe s dotocenim (Shoot Yaw) se hlava srovna, aby se postava divala tam, kam miri. Tady jde pridat "
        + "rucni korekce natoceni hlavy ve stupnich (+ = doprava), kdyby se divala trochu vedle.")]
    public float shootHeadYaw;
    public AnimationClip reload;
    public AnimationClip blockPose;
    public AnimationClip dashPose;
    // Ultimatka pres cele telo (Mosino: tanec Flair), prehrava se ve smycce po dobu ultimatky.
    public AnimationClip ultimate;
    // Animace schopnosti na Shift a PTM pres cele telo (Max: skok s uderem kosou, otocka kosou), prehrava se jen jejich usek.
    public AnimationClip shiftAbility;
    [Tooltip("Skok s uderem na Shiftu (Max): odraz, dopad (uder do zeme) a konec useku klipu v sekundach. 0 = vychozi casy "
        + "puvodniho klipu. Zmeri BrosOverwatch > Animace > Zmerit skok s uderem (Shift).")]
    public float shiftFrom, shiftImpact, shiftTo;
    public AnimationClip rmbAbility;
    // Schopnost na E pres cele telo (Kondulic: kop), prehrava se cela.
    public AnimationClip eAbility;

    // Pro uvodni scenky pred "play of the game".
    public AnimationClip sitIdle;
    public AnimationClip hitChest;
}
