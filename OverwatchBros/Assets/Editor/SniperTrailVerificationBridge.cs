using System;
using System.IO;
using UnityEditor;

[InitializeOnLoad]
public static class SniperTrailVerificationBridge
{
    static double next;
    static SniperTrailVerificationBridge() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        next = EditorApplication.timeSinceStartup + 1;
        const string path = "Temp/SniperTrailQA.request";
        if (!File.Exists(path) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string command = File.ReadAllText(path).Trim(); File.Delete(path);
        try
        {
            if (command == "test") SniperTrailVerification.Start();
            if (command == "refresh") AssetDatabase.Refresh();
            File.WriteAllText("Temp/SniperTrailQA.status", DateTime.Now + " OK " + command);
        }
        catch (Exception e) { File.WriteAllText("Temp/SniperTrailQA.status", e.ToString()); }
    }
}
