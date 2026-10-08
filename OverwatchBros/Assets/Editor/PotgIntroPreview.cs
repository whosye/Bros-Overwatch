using UnityEditor;
using UnityEngine;

// Ukazka uvodni scenky pred "play of the game" bez zakladani hry: BrosOverwatch > Ukazka POTG uvodu > hrdina.
// Kdyz neni zapnuty Play mod, zapne se sam a scenka se pusti hned po startu (v hlavnim menu).
[InitializeOnLoad]
public static class PotgIntroPreview
{
    const string PendingKey = "BrosOverwatch.PotgIntroPreview";

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
                if (Application.isPlaying)
                    PotgIntro.Preview(hero);
            };
            EditorApplication.update += wait;
        };
    }

    static void Run(string heroAsset)
    {
        if (Application.isPlaying)
        {
            PotgIntro.Preview(heroAsset);
            return;
        }

        SessionState.SetString(PendingKey, heroAsset);
        EditorApplication.isPlaying = true;
    }

    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Pova")] static void Pova() => Run("Ayran");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Honza (Tomášek)")] static void Honza() => Run("Honza");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Viktor")] static void Viktor() => Run("Viktor");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Mirek")] static void Mirek() => Run("Mirek");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Anna")] static void Anna() => Run("Anna");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Sniper")] static void Sniper() => Run("Sniper");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Max")] static void Max() => Run("Max");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Bard")] static void Bard() => Run("Bard");
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Šindel")] static void Sindel() => Run("Sindel");
}
