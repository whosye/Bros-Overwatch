using System.IO;
using UnityEditor;
using UnityEngine;

// Priradi Ayranovy nahrane zvuky (OverwatchBros/Sound/pova, mimo Assets) k jeho schopnostem a kill hlasce.
// Soubory se zkopiruji do Assets/Audio/Ayran, aby je Unity mohlo importovat jako AudioClip.
public static class AyranSoundSetup
{
    const string AssetDir = "Assets/Audio/Ayran";
    const string QFile = "pova_q_boosted_240.mp3";
    const string ShiftFile = "pova_shift_boosted_300.mp3";
    const string KillFile = "pova_kill_boosted_160.mp3";

    static string SourceDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Sound", "pova"));

    public static bool IsReady()
    {
        // Nahravky jeste nikdo nepridal - neblokuje zbytek setupu.
        if (!Directory.Exists(SourceDir)) return true;

        var leap = AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/Data/LeapStrike_Data.asset");
        var rush = AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/Data/Rush_Data.asset");
        var ayran = AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Ayran.asset");

        return leap != null && leap.sound != null
            && rush != null && rush.sound != null
            && ayran != null && HasKillClip(ayran);
    }

    static bool HasKillClip(HeroDefinition ayran)
    {
        if (ayran.killLines == null) return false;
        foreach (var clip in ayran.killLines)
            if (clip != null && clip.name.Contains("kill_boosted")) return true;
        return false;
    }

    public static void Setup()
    {
        if (!Directory.Exists(SourceDir)) return;

        EnsureFolder("Assets/Audio");
        EnsureFolder(AssetDir);

        bool copied = false;
        foreach (var file in new[] { QFile, ShiftFile, KillFile })
        {
            string src = Path.Combine(SourceDir, file);
            string dst = $"{AssetDir}/{file}";
            if (!File.Exists(src) || File.Exists(dst)) continue;

            File.Copy(src, dst);
            copied = true;
        }

        if (copied)
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var qClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AssetDir}/{QFile}");
        var shiftClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AssetDir}/{ShiftFile}");
        var killClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AssetDir}/{KillFile}");

        var leap = AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/Data/LeapStrike_Data.asset");
        if (leap != null && leap.sound == null && qClip != null)
        {
            leap.sound = qClip;
            EditorUtility.SetDirty(leap);
        }

        var rush = AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/Data/Rush_Data.asset");
        if (rush != null && rush.sound == null && shiftClip != null)
        {
            rush.sound = shiftClip;
            EditorUtility.SetDirty(rush);
        }

        var ayran = AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Ayran.asset");
        if (ayran != null && killClip != null && !HasKillClip(ayran))
        {
            var lines = ayran.killLines ?? new AudioClip[0];
            var updated = new AudioClip[lines.Length + 1];
            lines.CopyTo(updated, 0);
            updated[lines.Length] = killClip;
            ayran.killLines = updated;
            EditorUtility.SetDirty(ayran);
        }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
