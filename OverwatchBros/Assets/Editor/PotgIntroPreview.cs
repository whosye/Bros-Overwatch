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
    const string QueueKey = "BrosOverwatch.PotgIntroPreview.Queue";
    static readonly string[] AllHeroes = { "Ayran", "Honza", "Viktor", "Mirek", "Anna", "Sniper", "Max", "Bard", "Sindel" };

    static PotgIntroPreview()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;

            string hero = SessionState.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(hero))
            {
                // vsichni postupne
                if (!string.IsNullOrEmpty(SessionState.GetString(QueueKey, ""))) NextInQueue(0.5);
                return;
            }

            SessionState.EraseString(PendingKey);
            // Chvili pockat, az se nacte menu a hrdinove.
            double at = EditorApplication.timeSinceStartup + 0.5;
            EditorApplication.CallbackFunction wait = null;
            wait = () =>
            {
                if (EditorApplication.timeSinceStartup < at) return;
                EditorApplication.update -= wait;
                if (Application.isPlaying && StartPreview(hero))
                    WatchEnd();
                else if (SessionState.GetBool(AutoExitKey, false))
                    StopPlay();
            };
            EditorApplication.update += wait;
        };
    }

    // Vsechny scenky za sebou (jeden Play mod): dalsi az po dohrani predchozi, nakonec vypis do konzole.
    [MenuItem("BrosOverwatch/Ukázka POTG úvodu/Všichni postupně")]
    static void RunAll()
    {
        SessionState.SetString(QueueKey, string.Join(",", AllHeroes));
        SessionState.SetString(QueueKey + ".Report", "");
        if (Application.isPlaying) { NextInQueue(0.1); return; }
        SessionState.SetBool(AutoExitKey, true);
        EditorApplication.isPlaying = true;
    }

    static void NextInQueue(double delay)
    {
        double at = EditorApplication.timeSinceStartup + delay;
        EditorApplication.CallbackFunction step = null;
        step = () =>
        {
            if (!Application.isPlaying) { EditorApplication.update -= step; return; }
            if (EditorApplication.timeSinceStartup < at || PotgIntro.Active) return;
            EditorApplication.update -= step;

            var queue = new System.Collections.Generic.List<string>(SessionState.GetString(QueueKey, "").Split(','));
            queue.RemoveAll(string.IsNullOrEmpty);
            if (queue.Count == 0)
            {
                Debug.Log("[POTG] Vsechny ukazky prehrany:\n" + SessionState.GetString(QueueKey + ".Report", ""));
                SessionState.EraseString(QueueKey);
                if (SessionState.GetBool(AutoExitKey, false)) StopPlay();
                return;
            }
            string hero = queue[0];
            queue.RemoveAt(0);
            SessionState.SetString(QueueKey, string.Join(",", queue));
            bool started = false;
            try { started = StartPreview(hero); }
            catch (System.Exception e) { Debug.LogException(e); }
            string line = started ? $"  {hero}: prehrano" : $"  {hero}: NESPUSTILO SE (chybi scenka / hrdina?)";
            SessionState.SetString(QueueKey + ".Report", SessionState.GetString(QueueKey + ".Report", "") + line + "\n");
            Debug.Log($"[POTG] Ukazka {hero}" + (started ? "" : " - NESPUSTILA SE"));
            NextInQueue(started ? 0.8 : 0.2);
        };
        EditorApplication.update += step;
    }

    static bool StartPreview(string hero) => PotgIntro.Preview(hero);

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
            StartPreview(heroAsset);
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
