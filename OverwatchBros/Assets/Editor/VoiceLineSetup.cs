using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Hlasky hrdinu ze slozek: nahravky (mp3 / wav / ogg) staci nakopirovat do Assets/Audio/<Hrdina>/<druh>/
// a samy se priradi do prislusne schranky hrdiny nebo jeho schopnosti. Slozky se vytvori samy.
//   spawn, kill, death, hurt, idle, snare          -> HeroDefinition
//   ability_Q, ability_Shift, ability_E, ability_Block -> AbilityDefinition.voiceLines dane schopnosti
// Nahravky prirazene rucne v Inspectoru (odjinud nez z techto slozek) zustavaji.
public class VoiceLineSetup : AssetPostprocessor
{
    const string AudioRoot = "Assets/Audio";
    const string HeroDir = "Assets/Resources/Heroes";

    static readonly string[] AbilityFolders = { "ability_Q", "ability_Shift", "ability_Block", "ability_E", "ability_RMB" };

    [InitializeOnLoadMethod]
    static void OnLoad()
    {
        EditorApplication.delayCall += Apply;
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var list in new[] { imported, deleted, moved, movedFrom })
            foreach (var path in list)
                if (path.StartsWith(AudioRoot + "/"))
                {
                    EditorApplication.delayCall -= Apply;
                    EditorApplication.delayCall += Apply;
                    return;
                }
    }

    [MenuItem("BrosOverwatch/Přiřadit hlášky ze složek")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!AssetDatabase.IsValidFolder(HeroDir)) return;

        bool changed = false;
        foreach (var guid in AssetDatabase.FindAssets("t:HeroDefinition", new[] { HeroDir }))
        {
            var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (hero == null) continue;

            string root = $"{AudioRoot}/{hero.name}";
            EnsureFolder(AudioRoot);
            EnsureFolder(root);

            changed |= Assign(ref hero.spawnLines, $"{root}/spawn", hero);
            changed |= Assign(ref hero.killLines, $"{root}/kill", hero);
            changed |= Assign(ref hero.deathLines, $"{root}/death", hero);
            changed |= Assign(ref hero.hurtLines, $"{root}/hurt", hero);
            changed |= Assign(ref hero.idleLines, $"{root}/idle", hero);
            changed |= Assign(ref hero.snareLines, $"{root}/snare", hero);

            var abilities = new[] { hero.ability, hero.secondaryAbility, hero.blockAbility, hero.altAbility, hero.rmbAbility };
            for (int i = 0; i < abilities.Length; i++)
            {
                // Slozka jen pro schopnosti, ktere hrdina opravdu ma.
                if (abilities[i] == null) continue;
                changed |= Assign(ref abilities[i].voiceLines, $"{root}/{AbilityFolders[i]}", abilities[i]);
            }
        }

        if (changed)
            AssetDatabase.SaveAssets();
    }

    // Schranka = rucne prirazene nahravky (mimo slozku) + vsechny nahravky ze slozky.
    static bool Assign(ref AudioClip[] lines, string folder, Object owner)
    {
        EnsureFolder(folder);

        var result = new List<AudioClip>();
        if (lines != null)
            foreach (var clip in lines)
                if (clip != null && !AssetDatabase.GetAssetPath(clip).StartsWith(folder + "/"))
                    result.Add(clip);

        var paths = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        paths.Sort(string.CompareOrdinal);

        foreach (var path in paths)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null && !result.Contains(clip))
                result.Add(clip);
        }

        if (Same(lines, result)) return false;

        lines = result.ToArray();
        EditorUtility.SetDirty(owner);
        Debug.Log($"[VoiceLineSetup] {owner.name}: {folder} -> {result.Count} nahrávek");
        return true;
    }

    static bool Same(AudioClip[] current, List<AudioClip> wanted)
    {
        int count = current != null ? current.Length : 0;
        if (count != wanted.Count) return false;

        for (int i = 0; i < count; i++)
            if (current[i] != wanted[i]) return false;

        return true;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
