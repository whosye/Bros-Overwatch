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
    [Tooltip("Pri behu (klus a sprint) narovnat trup: o kolik stupnu se zakloni proti predklonu z animace (0 = jak je v animaci). "
        + "Lze ladit za behu hry (F6).")]
    [Range(-20f, 45f)] public float runUpright;
    public AnimationClip jump;
    public AnimationClip death;
    public AnimationClip meleeAttack;
    [Tooltip("Utok zblizka pres cele telo, kdyz postava stoji (nohy udelaji krok / napraZeni z animace). Pri chuzi "
        + "a behu zustanou nohy z chuze (jinak by postava klouzala).")]
    public bool meleeFullBody;
    public AnimationClip shoot;
    [Tooltip("Animace strelby mirici do strany (postava v klipu stoji bokem, napr. Mixamo Gunplay): o kolik stupnu se pri strelbe "
        + "dotoci horni polovina tela kolem svisle osy (+ = doprava), aby zbran mirila dopredu. Nastavi se samo pri nove animaci "
        + "strelby. U luku se trup srovnava sam podle ramen a tohle cislo se k tomu pricte (rucni doladeni).")]
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
    // Trojsek (Pova, Shift): tri udery komba pres cele telo (combo2 = zrcadlovy combo1 - leva ruka).
    public AnimationClip combo1, combo2, combo3;

    // Pohyb navic (jen postava - 3. osoba a ostatni hraci); prazdne = jako dosud (chuze/beh).
    [Header("Pohyb navic (volitelne)")]
    public AnimationClip strafeLeft;    // ukrok doleva (chuze bokem)
    public AnimationClip strafeRight;   // ukrok doprava (z FBX ukroku doleva zrcadlove - MixamoAnimationSetup)
    public AnimationClip crouchWalk;    // chuze v drepu; ve stoji v drepu zamrzly snimek
    [Tooltip("V drepu se hlava srovna, aby se postava divala rovne pred sebe. Tady jde bradu jeste zvednout (+) nebo sklopit (-), ve stupnich.")]
    [Range(-40f, 60f)] public float crouchHeadPitch;
    public AnimationClip jumpStart;     // odraz pri skoku z behu (zacatek klipu)
    public AnimationClip landIdle;      // dopad ze stoje / pomale chuze
    public AnimationClip landRun;       // dopad v behu
    public AnimationClip climb;         // vytazeni na hranu (Mirek: Climbing Up Wall)
    [Tooltip("Luk: drzeni plne natazeneho luku (smycka). Natahovani prehrava klip Shoot podle sily natazeni.")]
    public AnimationClip bowHold;
    [Tooltip("Luk: naklon luku do strany pri natahovani (kolem osy sipu), ve stupnich: + = horni rameno luku doprava, "
        + "- = doleva, 0 = svisle. Ruce se k luku dotahnou samy. Lze ladit za behu hry (F6).")]
    [Range(-70f, 70f)] public float bowTilt;

    // Pro uvodni scenky pred "play of the game".
    public AnimationClip sitIdle;
    public AnimationClip hitChest;
}
