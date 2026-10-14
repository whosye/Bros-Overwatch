using UnityEditor;
using UnityEngine;

// Ruce v prvni osobe (FirstPersonArms) si z modelu postavy vyriznou jen paze - k tomu musi jit cist geometrii modelu.
// Tenhle skript jednou zapne "Read/Write" u modelu postav hrdinu (jen kde chybi; ostatni import se nemeni).
[InitializeOnLoad]
public static class FpArmsReadableSetup
{
    static FpArmsReadableSetup()
    {
        EditorApplication.delayCall += Run;
    }

    [MenuItem("BrosOverwatch/Ruce v 1. osobe - povolit cteni modelu postav")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        int changed = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:HeroDefinition"))
        {
            var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (hero == null || hero.characterPrefab == null) continue;

            foreach (var skinned in hero.characterPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = skinned.sharedMesh;
                if (mesh == null || mesh.isReadable) continue;
                string path = AssetDatabase.GetAssetPath(mesh);
                if (AssetImporter.GetAtPath(path) is ModelImporter importer && !importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    changed++;
                }
            }
        }
        if (changed > 0)
            Debug.Log($"[Ruce 1. osoba] Povoleno cteni u {changed} modelu postav (paze v 1. osobe se vyriznou z modelu).");
    }
}
