using UnityEditor;
using UnityEngine;

// Max (inspirace: Kayn): melee zabijak s kosou. Data zbrane, schopnosti a hrdiny vytvori M7Setup (CreateMax).
// Kosa v ruce: model ze Sketchfabu (Editor/ScytheSetup, Prefab/Weapons/Held_Scythe), bez nej se stavi v kodu
// (HeldWeapons, HeldModel.Scythe). Tady se uz heldPrefab NEMAZE (drive tim zmizela kosa ze Sketchfabu).
public static class MaxSetup
{
    const string WeaponPath = "Assets/Data/Scythe_Data.asset";
    const string OldPrefabPath = "Assets/Prefab/Weapons/HeldScythe.prefab";

    public static bool IsReady()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        return weapon == null || weapon.heldModel == HeldModel.Scythe;
    }

    public static void Setup()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        if (weapon != null && weapon.heldModel != HeldModel.Scythe)
        {
            weapon.heldModel = HeldModel.Scythe;
            EditorUtility.SetDirty(weapon);
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(OldPrefabPath) != null)
            AssetDatabase.DeleteAsset(OldPrefabPath);   // starsi kosa z FBX
    }
}
