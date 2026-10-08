using UnityEditor;
using UnityEngine;

// Max (inspirace: Kayn): melee zabijak s kosou. Data zbrane, schopnosti a hrdiny vytvori M7Setup (CreateMax).
// Kosa v ruce se stavi primo v kodu (HeldWeapons, HeldModel.Scythe) - presne rozmery a natoceni, bez importu
// z FBX (Unity pri importu modelu z Blenderu otaci osy a materialy vychazeji pruhledne).
public static class MaxSetup
{
    const string WeaponPath = "Assets/Data/Scythe_Data.asset";
    const string OldPrefabPath = "Assets/Prefab/Weapons/HeldScythe.prefab";

    public static bool IsReady()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        return weapon == null || (weapon.heldModel == HeldModel.Scythe && weapon.heldPrefab == null);
    }

    public static void Setup()
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        if (weapon != null && (weapon.heldModel != HeldModel.Scythe || weapon.heldPrefab != null))
        {
            weapon.heldModel = HeldModel.Scythe;
            weapon.heldPrefab = null;
            EditorUtility.SetDirty(weapon);
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(OldPrefabPath) != null)
            AssetDatabase.DeleteAsset(OldPrefabPath);   // starsi kosa z FBX
    }
}
