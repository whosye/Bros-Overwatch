using System.IO;
using UnityEditor;
using UnityEngine;

// Priradi Ayranovu nahranou kill hlasku (OverwatchBros/Sound/pova, mimo Assets).
// Soubory se zkopiruji do Assets/Audio/Ayran, aby je Unity mohlo importovat jako AudioClip.
// Shift (pova_shift) se tu uz neresi: je to hlaska ve slozce Audio/Ayran/ability_Shift a strida se s druhou hlaskou
// (schopnost Shift proto nema vlastni zvuk - jinak by hraly oba naraz). Q (pova_q) take ne: u ultimatky hraji
// jen hlasky step_1 (vzlet) a step_2 (dopad) ze slozky Audio/Ayran/ability_Q.
public static class AyranSoundSetup
{
    const string AssetDir = "Assets/Audio/Ayran";
    const string KillFile = "pova_kill_boosted_160.mp3";

    static string SourceDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Sound", "pova"));

    public static bool IsReady()
    {
        // Nahravky jeste nikdo nepridal - neblokuje zbytek setupu.
        if (!Directory.Exists(SourceDir)) return true;

        var ayran = AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Ayran.asset");
        return ayran != null && HasKillClip(ayran);
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
        foreach (var file in new[] { KillFile })
        {
            string src = Path.Combine(SourceDir, file);
            string dst = $"{AssetDir}/{file}";
            if (!File.Exists(src) || File.Exists(dst)) continue;

            File.Copy(src, dst);
            copied = true;
        }

        if (copied)
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var killClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AssetDir}/{KillFile}");

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
