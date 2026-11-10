using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Animace z Mixama pro jednotlive hrdiny: FBX staci ulozit do Assets/Models/Animations/Mixamo/<Hrdina>/<Hrdina>_<Policko>.fbx
// (<Hrdina> = nazev assetu v Resources/Heroes, napr. Viktor, Anna pro Davida; <Policko> = policko sady animaci:
// Idle, Walk, Jog, Sprint, Jump, Death, MeleeAttack, Shoot, Reload, BlockPose, DashPose, Ultimate, ShiftAbility, RmbAbility, EAbility).
// Import se nastavi sam (Humanoid, bez materialu, smycky u chuze/behu/klidu) a klip se priradi do sady
// <Hrdina>_Animations.asset ve stejne slozce; sada se priradi hrdinovi (HeroDefinition.animations), kdyz zadnou nema.
public class MixamoAnimationSetup : AssetPostprocessor
{
    const string Root = "Assets/Models/Animations/Mixamo";
    const string HeroDir = "Assets/Resources/Heroes";

    static readonly string[] Looping = { "idle", "walk", "jog", "sprint", "jump" };
    // Animace se skokem dopredu (skok s uderem): posun dopredu i vyska skoku se z pozy vyjmou (chodidla zustanou
    // u zeme, skok i pohyb ridi hra), drep a skrceni nohou ve vzduchu zustanou.
    static readonly string[] InPlace = { "shiftability" };
    // Chuze a beh: posun dopredu se do pozy NEzapeka (jinak klip bez "In Place" postavu po krocich posouva
    // dopredu a na konci smycky ji cukne zpet - sekany pohyb). Posun jde do root motion, ktery hra zahazuje.
    static readonly string[] Locomotion = { "walk", "jog", "sprint" };

    static bool InRoot(string path) => path.StartsWith(Root + "/") && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

    static string Slot(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int underscore = name.LastIndexOf('_');
        return underscore >= 0 ? name.Substring(underscore + 1) : name;
    }

    void OnPreprocessModel()
    {
        if (!InRoot(assetPath)) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;   // (Mixamo "With Skin": model se nepouzije, jen kostra)
        importer.importCameras = false;
        importer.importLights = false;
    }

    void OnPreprocessAnimation()
    {
        if (!InRoot(assetPath)) return;
        var importer = (ModelImporter)assetImporter;
        bool loop = System.Array.IndexOf(Looping, Slot(assetPath).ToLowerInvariant()) >= 0;
        bool inPlace = System.Array.IndexOf(InPlace, Slot(assetPath).ToLowerInvariant()) >= 0;
        bool locomotion = System.Array.IndexOf(Locomotion, Slot(assetPath).ToLowerInvariant()) >= 0;
        string clipName = Path.GetFileNameWithoutExtension(assetPath);

        var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            clip.name = clipName;   // misto obecneho "mixamo.com"
            clip.loopTime = loop;
            // postava stoji na miste a nenataci se (pohyb ridi hra)
            clip.lockRootRotation = true;
            clip.lockRootHeightY = !inPlace;
            clip.lockRootPositionXZ = !inPlace && !locomotion;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = !inPlace;
            clip.heightFromFeet = inPlace;
            clip.keepOriginalPositionXZ = true;
        }
        importer.clipAnimations = clips;
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var list in new[] { imported, moved })
            foreach (var path in list)
                if (InRoot(path))
                {
                    EditorApplication.delayCall -= Apply;
                    EditorApplication.delayCall += Apply;
                    return;
                }
    }

    // Chuze/beh importovane driv (se zapecenym posunem): jednou znovu naimportovat s novym nastavenim.
    [UnityEditor.Callbacks.DidReloadScripts]
    static void ReimportLocomotion()
    {
        if (!AssetDatabase.IsValidFolder(Root)) return;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!InRoot(path) || System.Array.IndexOf(Locomotion, Slot(path).ToLowerInvariant()) < 0) continue;
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;
            bool baked = false;
            foreach (var clip in importer.clipAnimations) baked |= clip.lockRootPositionXZ;
            if (!baked) continue;
            Debug.Log($"[Mixamo] {path}: chuze/beh bez zapeceneho posunu (plynuly pohyb) - znovu import.");
            importer.SaveAndReimport();
        }
    }

    // Po prekompilovani znovu (nove policko sady se jinak nevyplni, kdyz se FBX naimportovalo driv nez skripty).
    [UnityEditor.Callbacks.DidReloadScripts]
    static void AfterScriptsReload()
    {
        EditorApplication.delayCall -= Apply;
        EditorApplication.delayCall += Apply;
    }

    [MenuItem("BrosOverwatch/Přiřadit animace z Mixama")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !AssetDatabase.IsValidFolder(Root)) return;

        foreach (string folder in AssetDatabase.GetSubFolders(Root))
        {
            string heroName = Path.GetFileName(folder);
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!InRoot(path)) continue;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    {
                        clips[Slot(path).ToLowerInvariant()] = clip;
                        break;
                    }
            }
            if (clips.Count == 0) continue;

            string setPath = $"{folder}/{heroName}_Animations.asset";
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimSet>(setPath);
            bool created = set == null;
            if (created) set = ScriptableObject.CreateInstance<CharacterAnimSet>();

            bool changed = false;
            foreach (var field in typeof(CharacterAnimSet).GetFields())
            {
                if (field.FieldType != typeof(AnimationClip)) continue;
                if (!clips.TryGetValue(field.Name.ToLowerInvariant(), out var clip)) continue;
                if ((AnimationClip)field.GetValue(set) == clip) continue;
                field.SetValue(set, clip);
                changed = true;
            }
            foreach (var slot in clips.Keys)
                if (typeof(CharacterAnimSet).GetField(slot, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.IgnoreCase) == null)
                    Debug.LogWarning($"[MixamoAnimationSetup] {folder}: neznámé políčko '{slot}' (název souboru <Hrdina>_<Políčko>.fbx).");

            if (created) AssetDatabase.CreateAsset(set, setPath);
            else if (changed) EditorUtility.SetDirty(set);

            var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>($"{HeroDir}/{heroName}.asset");
            if (hero == null)
                Debug.LogWarning($"[MixamoAnimationSetup] Hrdina {HeroDir}/{heroName}.asset neexistuje (složka se musí jmenovat jako asset hrdiny).");
            else if (hero.animations == null)
            {
                hero.animations = set;
                EditorUtility.SetDirty(hero);
            }

            if (created || changed)
                Debug.Log($"[MixamoAnimationSetup] {heroName}: {string.Join(", ", clips.Keys)} -> {setPath}");
        }
        AssetDatabase.SaveAssets();
    }
}
