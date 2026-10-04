using System.Text;
using UnityEditor;
using UnityEngine;

// Read-only measurements in world units, including unsaved changes in the open scene.
[InitializeOnLoad]
public static class MapScaleAudit
{
    static MapScaleAudit() { EditorApplication.delayCall += WriteReport; }

    [MenuItem("BrosOverwatch/Mapa/Zmerit meritko mapy")]
    public static void WriteReport()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var map = GameObject.Find("Map-Domasov");
        if (map == null) return;
        var report = new StringBuilder();
        report.AppendLine("WORLD UNITS (metres with the project's normal physics convention)");
        report.AppendLine("Gravity: " + Physics.gravity.ToString("F3"));
        report.AppendLine("Ground: " + MapBuildKit.GroundBounds(map.transform).ToString("F3"));
        foreach (string path in new[] { "", "HlavniChata/PRIZEMI", "HlavniChata/PRVNIPATRO",
            "HlavniChata/DRUHEPATRO", "MensiChata", "Rozhledna", "Props/ToyotaYaris", "OkoliChaty/Bazen" })
        {
            var t = path == "" ? map.transform : map.transform.Find(path);
            if (t == null) continue;
            report.AppendLine("OBJECT " + (path == "" ? map.name : path)
                + " local scale=" + t.localScale.ToString("F3") + " world scale=" + t.lossyScale.ToString("F3"));
            Bounds? bounds = null;
            foreach (var renderer in t.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (bounds == null) bounds = renderer.bounds;
                else { var b = bounds.Value; b.Encapsulate(renderer.bounds); bounds = b; }
            }
            if (bounds.HasValue) report.AppendLine("  bounds=" + bounds.Value.ToString("F3")
                + " size=" + bounds.Value.size.ToString("F3"));
        }
        foreach (var t in map.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "Prism" && !(t.parent != null && t.parent.name == "MensiChata")
                && !(t.parent != null && t.parent.name == "ToyotaYaris")) continue;
            var renderer = t.GetComponent<Renderer>();
            report.AppendLine("PART " + t.name + " parent=" + t.parent.name
                + " scale=" + t.localScale.ToString("F3") + " worldScale=" + t.lossyScale.ToString("F3")
                + (renderer != null ? " size=" + renderer.bounds.size.ToString("F3") : ""));
        }
        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Player.prefab");
        if (player != null)
        {
            var cc = player.GetComponent<CharacterController>();
            report.AppendLine("PLAYER scale=" + player.transform.localScale.ToString("F3")
                + " capsule height=" + cc.height + " radius=" + cc.radius + " step=" + cc.stepOffset);
            var movement = player.GetComponent<FirstPersonController>();
            report.AppendLine("MOVEMENT walk=" + movement.walkSpeed + " run=" + movement.runSpeed
                + " eyes=" + movement.eyeHeight + " jump=" + movement.jumpHeight + " gravity=" + movement.gravity);
            foreach (var camera in player.GetComponentsInChildren<Camera>(true))
                report.AppendLine("PLAYER CAMERA " + camera.name + " near=" + camera.nearClipPlane
                    + " far=" + camera.farClipPlane + " FOV=" + camera.fieldOfView);
        }
        foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            report.AppendLine("SCENE CAMERA " + camera.name + " near=" + camera.nearClipPlane
                + " far=" + camera.farClipPlane + " FOV=" + camera.fieldOfView);
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            report.AppendLine("LIGHT " + light.name + " type=" + light.type + " range=" + light.range
                + " intensity=" + light.intensity);
        System.IO.Directory.CreateDirectory("Temp");
        System.IO.File.WriteAllText("Temp/MapScaleAudit.txt", report.ToString());
        Debug.Log("[Meritko] Mereni ulozeno do Temp/MapScaleAudit.txt (scena se nemeni).");
    }
}
