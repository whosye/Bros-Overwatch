using UnityEditor;
using UnityEngine;

// Ukazka uvodni scenky pred "play of the game" bez zakladani hry: BrosOverwatch > Ukazka POTG uvodu > hrdina.
// Postavy ve scenkach hybe herni animace, takze se na chvili sam zapne Play mod: scenka se pusti hned po startu
// (v hlavnim menu, nic se nezaklada) a po dohrani se Play mod sam zase vypne - jako prehrani videa.
[InitializeOnLoad]
public static class PotgIntroPreview
{
    const string PendingKey = "BrosOverwatch.PotgIntroPreview";
    const string AutoExitKey = "BrosOverwatch.PotgIntroPreview.AutoExit";

    static PotgIntroPreview()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;

            string hero = SessionState.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(hero)) return;

            SessionState.EraseString(PendingKey);
            // Chvili pockat, az se nacte menu a hrdinove.
            double at = EditorApplication.timeSinceStartup + 0.5;
            EditorApplication.CallbackFunction wait = null;
            wait = () =>
            {
                if (EditorApplication.timeSinceStartup < at) return;
                EditorApplication.update -= wait;
                if (Application.isPlaying && PotgIntro.Preview(hero))
                    WatchEnd();
                else if (SessionState.GetBool(AutoExitKey, false))
                    StopPlay();
            };
            EditorApplication.update += wait;
        };
    }

    // Po dohrani scenky zpet z Play modu (jen kdyz ho zapnul nahled).
    static void WatchEnd()
    {
        if (!SessionState.GetBool(AutoExitKey, false)) return;
        EditorApplication.CallbackFunction check = null;
        check = () =>
        {
            if (!Application.isPlaying) { EditorApplication.update -= check; return; }
            if (PotgIntro.Active) return;
            EditorApplication.update -= check;
            StopPlay();
        };
        EditorApplication.update += check;
    }

    static void StopPlay()
    {
        SessionState.EraseBool(AutoExitKey);
        EditorApplication.isPlaying = false;
    }

    static void Run(string heroAsset)
    {
        if (Application.isPlaying)
        {
            PotgIntro.Preview(heroAsset);
            return;
        }

        SessionState.SetString(PendingKey, heroAsset);
        SessionState.SetBool(AutoExitKey, true);
        EditorApplication.isPlaying = true;
    }

    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Pova")] static void Pova() => Run("Ayran");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Honza")] static void Honza() => Run("Honza");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Viktor")] static void Viktor() => Run("Viktor");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Mirek")] static void Mirek() => Run("Mirek");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/David")] static void David() => Run("Anna");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Kondulič")] static void Kondulic() => Run("Sniper");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Max")] static void Max() => Run("Max");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Mošino")] static void Mosino() => Run("Bard");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Šindel")] static void Sindel() => Run("Sindel");
}
