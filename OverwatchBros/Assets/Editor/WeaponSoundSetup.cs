using UnityEditor;
using UnityEngine;

// Zvuky vystrelu zbrani (Assets/Audio/Weapons, vrstvene: basovy uder + skutecna rana + prasknuti + dozvuk;
// zdroje CC0: The Free Firearm Sound Library, Kenney Sci-fi Sounds). Prirazuje jen zbranim, ktere vlastni zvuk nemaji.
public static class WeaponSoundSetup
{
    static readonly string[,] Sounds =
    {
        { "Assets/Data/Pistol_Data.asset", "gun_viktor", "" },
        { "Assets/Data/FlankerPistols_Data.asset", "gun_flanker", "" },
        { "Assets/Data/SindelPistol_Data.asset", "gun_sindel", "" },
        { "Assets/Data/SniperRifle_Data.asset", "gun_sniper_smg", "gun_sniper_scoped" },
        { "Assets/Data/AnnaRifle_Data.asset", "gun_anna", "" },
        { "Assets/Data/BardGun_Data.asset", "gun_bard", "" },
        { "Assets/Data/Rocket_Data.asset", "gun_honza", "" },
    };

    static AudioClip Clip(string name) =>
        string.IsNullOrEmpty(name) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/Weapons/{name}.wav");

    public static bool IsReady()
    {
        for (int i = 0; i < Sounds.GetLength(0); i++)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Sounds[i, 0]);
            if (weapon == null) continue;
            if (weapon.fireSound == null && Clip(Sounds[i, 1]) != null) return false;
            if (weapon.scopedFireSound == null && Clip(Sounds[i, 2]) != null) return false;
        }
        return true;
    }

    public static void Setup()
    {
        for (int i = 0; i < Sounds.GetLength(0); i++)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(Sounds[i, 0]);
            if (weapon == null) continue;

            bool changed = false;
            var fire = Clip(Sounds[i, 1]);
            if (weapon.fireSound == null && fire != null) { weapon.fireSound = fire; changed = true; }
            var scoped = Clip(Sounds[i, 2]);
            if (weapon.scopedFireSound == null && scoped != null) { weapon.scopedFireSound = scoped; changed = true; }
            if (changed) EditorUtility.SetDirty(weapon);
        }
    }
}
