using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class TunnelWorkshopVerification
{
    public const string Output = "Temp/WorkshopQA";

    [MenuItem("BrosOverwatch/Easter egg/Vytvorit nahledy a overit")]
    public static void Preview()
    {
        var room = Object.FindAnyObjectByType<TunnelWorkshop>();
        if (!room) return;
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        void Check(bool success, string message) { report.AppendLine((success ? "PASS " : "FAIL ") + message); }
        Check(room.music && room.music.clip && room.video && room.video.clip, "Both media assets referenced");
        Check(room.monitor && room.monitorLight && room.doorFilter && room.head && room.mouseHand && room.keyboardHand, "All runtime references assigned");
        if (!room.monitor || !room.music || !room.video || !room.doorFilter || !room.head || !room.mouseHand)
        {
            File.WriteAllText(Output + "/verification.txt", report.ToString());
            Debug.LogError("[Pavouk QA] Incomplete workshop. Rebuild before testing.");
            return;
        }
        Vector3 doorway = room.transform.TransformPoint(new Vector3(0, 1.65f, -0.5f));
        Check(room.GainAt(doorway) > 0.95f, "Music audible immediately outside the door");
        Check(room.GainAt(new Vector3(doorway.x, room.surfaceY, doorway.z)) == 0, "Exactly silent on the ground plane");
        Check(room.GainAt(new Vector3(doorway.x, room.surfaceY + 2, doorway.z)) == 0, "Exactly silent above the ground plane");
        Check(room.GainAt(doorway + Vector3.forward * 50) == 0, "Exactly silent beyond the radius");
        Check(room.GainAt(new Vector3(doorway.x, room.transform.position.y - 2, doorway.z)) == 0, "Exactly silent below the room");
        Check(!room.doorFilter.gameObject.GetComponent<AudioReverbFilter>(), "No reverb can leak beyond audio gate");
        Check(room.video.audioOutputMode == UnityEngine.Video.VideoAudioOutputMode.None, "Video cannot bypass the music gate with its own audio");
        Check(!room.video.GetComponent<Renderer>() && !room.video.targetMaterialRenderer, "Timelapse excluded from Joker TV gameplay buff");
        foreach (var label in room.GetComponentsInChildren<TMP_Text>())
        {
            label.ForceMeshUpdate();
            Bounds ink = label.textBounds;
            Rect area = label.rectTransform.rect;
            bool fits = ink.min.x >= area.xMin - 0.001f && ink.max.x <= area.xMax + 0.001f
                && ink.min.y >= area.yMin - 0.001f && ink.max.y <= area.yMax + 0.001f;
            Check(fits && !label.isTextTruncated, "Complete label fits its backing: " + label.name);
            report.AppendLine("  " + label.text.Replace('\n', ' ') + "; font=" + label.font.name + "; size=" + label.fontSize
                + "; ink=" + ink.size.ToString("F3") + "; rect=" + area.size.ToString("F3"));
        }
        Check(room.GetComponentInChildren<WorkshopServerActivity>(), "Server activity component installed");
        Physics.SyncTransforms();
        var rackCollider = room.transform.Find("Detaily/Rack/Skrin").GetComponent<BoxCollider>();
        bool overlaps = false;
        foreach (var collider in room.GetComponentsInChildren<Collider>())
        {
            if (collider == rackCollider || !collider.enabled) continue;
            if (Physics.ComputePenetration(rackCollider, rackCollider.transform.position, rackCollider.transform.rotation,
                collider, collider.transform.position, collider.transform.rotation, out _, out float distance) && distance > 0.001f)
            {
                overlaps = true;
                report.AppendLine("OVERLAP rack / " + collider.name + ": " + distance);
            }
        }
        Check(!overlaps, "Relocated rack clears room walls and other furniture");
        Vector3 approachLow = room.transform.TransformPoint(new Vector3(0, 0.4f, -4.8f));
        Vector3 approachHigh = room.transform.TransformPoint(new Vector3(0, 1.4f, -4.8f));
        bool approachBlocked = Physics.CapsuleCast(approachLow, approachHigh, 0.32f, room.transform.forward,
            out var approachHit, 4.2f, ~0, QueryTriggerInteraction.Ignore);
        Check(!approachBlocked, "Player-sized capsule can approach the door from the tunnel" + (approachBlocked ? ": blocked by " + approachHit.collider.name : ""));
        var target = room.monitor.bounds.center;
        var sight = Physics.RaycastAll(doorway, (target - doorway).normalized, Vector3.Distance(doorway, target), ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in sight)
            report.AppendLine("SIGHT collider: " + hit.collider.name + " at " + hit.point.ToString("F3") + " visible=" + (hit.collider.GetComponent<Renderer>() != null));
        // Snapshot actor poses, animate once, then restore the authored pose exactly.
        var transforms = room.GetComponentsInChildren<Transform>();
        var positions = new Vector3[transforms.Length];
        var rotations = new Quaternion[transforms.Length];
        var scales = new Vector3[transforms.Length];
        for (int i = 0; i < transforms.Length; i++) { positions[i] = transforms[i].localPosition; rotations[i] = transforms[i].localRotation; scales[i] = transforms[i].localScale; }
        var headBefore = room.head.localRotation;
        var handBefore = room.mouseHand.localPosition;
        room.Animate(0.25);
        Check(Quaternion.Angle(headBefore, room.head.localRotation) > 1f && Vector3.Distance(handBefore, room.mouseHand.localPosition) > 0.001f, "Head and mouse hand animate");
        for (int i = 0; i < transforms.Length; i++) { transforms[i].localPosition = positions[i]; transforms[i].localRotation = rotations[i]; transforms[i].localScale = scales[i]; }
        report.AppendLine("Objects: " + transforms.Length);
        report.AppendLine("Origin: " + room.transform.position + "; room: " + room.roomSize + "; surface Y: " + room.surfaceY);
        Render(room, "doorway", new Vector3(0, 1.66f, -0.34f), new Vector3(0.15f, 1.46f, 2.95f), 67);
        Render(room, "entrance", new Vector3(0, 1.65f, -1.1f), new Vector3(0, 1.35f, 0), 67);
        Render(room, "interior", new Vector3(-1.65f, 1.85f, 0.65f), new Vector3(0.3f, 1.4f, 3.2f), 68);
        Render(room, "character", new Vector3(1.05f, 1.85f, 1.4f), new Vector3(-0.34f, 1.24f, 2.68f), 58);
        Render(room, "door-label", new Vector3(0, 2.17f, -0.68f), new Vector3(0, 2.17f, 0), 65);
        Render(room, "plan-label", new Vector3(1.8f, 1.7f, room.roomSize.z - 2.1f), new Vector3(room.roomSize.x / 2, 1.7f, room.roomSize.z - 1.5f), 65);
        File.WriteAllText(Output + "/verification.txt", report.ToString());
        Debug.Log("[Pavouk QA]\n" + report);
    }

    public static void Render(TunnelWorkshop room, string name, Vector3 position, Vector3 lookAt, float fov)
    {
        var go = new GameObject("Workshop verification camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.transform.position = room.transform.TransformPoint(position);
        camera.transform.LookAt(room.transform.TransformPoint(lookAt));
        camera.fieldOfView = fov; camera.nearClipPlane = 0.025f; camera.farClipPlane = 35;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.025f, 0.03f, 0.035f);
        camera.allowHDR = true; camera.enabled = false;
        var additional = camera.GetUniversalAdditionalCameraData();
        additional.renderPostProcessing = true; additional.renderShadows = true;
        var texture = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.DestroyImmediate(image); texture.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(go);
        }
    }

    [MenuItem("BrosOverwatch/Easter egg/Ulozit workshop a scenu")]
    public static void Save()
    {
        var room = Object.FindAnyObjectByType<TunnelWorkshop>();
        if (!room || EditorApplication.isPlayingOrWillChangePlaymode) return;
        MapBuildKit.EnsureFolder("Assets/Prefab/EasterEggs");
        PrefabUtility.SaveAsPrefabAsset(room.gameObject, "Assets/Prefab/EasterEggs/PavoukuvWorkshop.prefab");
        EditorSceneManager.SaveScene(room.gameObject.scene);
        AssetDatabase.SaveAssets();
    }
}
