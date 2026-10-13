using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Opt-in integration smoke test. Leaves the edited scene intact and returns to Edit Mode.</summary>
[InitializeOnLoad]
public static class TunnelWorkshopPlaytest
{
    const string Key = "Pavouk.WorkshopPlaytest";
    static TunnelWorkshop room;
    static AudioListener ears;
    static double started, stageStarted;
    static int stage;
    static long firstFrame;
    static Quaternion firstHead;
    static Color firstColor;
    static Renderer[] serverLeds;
    static Color[] firstLeds;
    static StringBuilder report;

    static TunnelWorkshopPlaytest()
    {
        EditorApplication.playModeStateChanged += ModeChanged;
        if (SessionState.GetBool(Key, false) && EditorApplication.isPlaying) EditorApplication.delayCall += Begin;
    }

    [MenuItem("BrosOverwatch/Easter egg/Overit prehravani v Play Mode")]
    public static void Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !UnityEngine.Object.FindAnyObjectByType<TunnelWorkshop>()) return;
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void ModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) Begin();
        if (state == PlayModeStateChange.EnteredEditMode) SessionState.EraseBool(Key);
    }

    static void Begin()
    {
        if (!EditorApplication.isPlaying || ears) return;
        room = UnityEngine.Object.FindAnyObjectByType<TunnelWorkshop>();
        if (!room) { SessionState.EraseBool(Key); EditorApplication.ExitPlaymode(); return; }
        report = new StringBuilder(); stage = 0;
        var indicators = new List<Renderer>();
        var rack = room.GetComponentInChildren<WorkshopServerActivity>();
        if (rack)
            foreach (var renderer in rack.GetComponentsInChildren<Renderer>())
                if (renderer.name == "Stav_LED") indicators.Add(renderer);
        serverLeds = indicators.ToArray();
        firstLeds = new Color[serverLeds.Length];
        started = stageStarted = EditorApplication.timeSinceStartup;
        foreach (var l in UnityEngine.Object.FindObjectsByType<AudioListener>()) l.enabled = false;
        var go = new GameObject("Workshop test listener");
        ears = go.AddComponent<AudioListener>();
        ears.transform.position = room.transform.TransformPoint(new Vector3(0, 1.66f, -0.34f));
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Check(bool ok, string description) { report.AppendLine((ok ? "PASS " : "FAIL ") + description); }
    static void Next() { stage++; stageStarted = EditorApplication.timeSinceStartup; }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || !ears || !room) { EditorApplication.update -= Tick; return; }
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - started > 45) { Check(false, "Timeout waiting for video / audio"); Finish(); return; }
            foreach (var l in UnityEngine.Object.FindObjectsByType<AudioListener>()) if (l != ears) l.enabled = false;
            if (stage == 0 && room.video.isPlaying && room.video.frame > 0 && room.music.isPlaying && now - started > 2)
            {
                Check(room.music.volume > 0.1f && !room.music.mute, "Music actually plays outside the door");
                Check(room.doorFilter.cutoffFrequency < 1500, "Door low-pass is active");
                Check(room.video.targetTexture && room.video.isPrepared, "Video decoded into the monitor texture");
                firstFrame = room.video.frame; firstHead = room.head.localRotation; firstColor = room.monitorLight.color;
                var properties = new MaterialPropertyBlock();
                for (int i = 0; i < serverLeds.Length; i++)
                {
                    serverLeds[i].GetPropertyBlock(properties);
                    firstLeds[i] = properties.GetColor("_BaseColor");
                }
                Next();
            }
            else if (stage == 1 && now - stageStarted > 2.1)
            {
                Check(room.video.frame != firstFrame, "Video frames advance");
                Check(Quaternion.Angle(firstHead, room.head.localRotation) > 0.1f, "Actor moves while media plays");
                Check(room.monitorLight.color != firstColor, "Lighting follows sampled video content");
                int changed = 0, dim = 0, bright = 0;
                var properties = new MaterialPropertyBlock();
                for (int i = 0; i < serverLeds.Length; i++)
                {
                    serverLeds[i].GetPropertyBlock(properties);
                    Color current = properties.GetColor("_BaseColor");
                    if (current != firstLeds[i]) changed++;
                    if (current.maxColorComponent < 0.5f) dim++; else bright++;
                }
                Check(serverLeds.Length == 28 && changed > 0, "Server LEDs blink during actual Play Mode (changed " + changed + "/" + serverLeds.Length + ")");
                Check(dim > 0 && bright > 0, "Server indicators have independent activity (lit " + bright + ", dim " + dim + ")");
                TunnelWorkshopVerification.Render(room, "runtime-doorway", new Vector3(0, 1.66f, -0.34f), new Vector3(0.15f, 1.46f, 2.95f), 67);
                ears.transform.position = new Vector3(ears.transform.position.x, room.surfaceY + 0.01f, ears.transform.position.z);
                Next();
            }
            else if (stage == 2 && now - stageStarted > 0.2)
            {
                Check(room.music.mute && room.music.volume == 0f, "Actual AudioSource exactly silent above ground");
                Check(!room.video.isPlaying, "Distant / above-ground video decoding pauses");
                ears.transform.position = room.transform.TransformPoint(new Vector3(0, 1.65f, 0.7f));
                Next();
            }
            else if (stage == 3 && now - stageStarted > 1.2)
            {
                Check(room.music.volume > 0f && !room.music.mute && room.video.isPlaying, "Re-entering underground resumes media");
                Check(room.doorFilter.cutoffFrequency > 7000, "Inside the room, sound opens up");
                room.video.time = Math.Max(0, room.video.length - 0.25);
                Next();
            }
            else if (stage == 4 && now - stageStarted > 2.2)
            {
                Check(room.video.isPlaying && room.video.time < 5, "Timelapse loops at the end");
                report.AppendLine("Video " + room.video.clip.width + "x" + room.video.clip.height + "; " + room.video.clip.length + " s");
                report.AppendLine("Music " + room.music.clip.length + " s; BPM " + room.beatsPerMinute);
                Finish();
            }
        }
        catch (Exception ex) { Check(false, ex.ToString()); Finish(); }
    }

    static void Finish()
    {
        EditorApplication.update -= Tick;
        Directory.CreateDirectory(TunnelWorkshopVerification.Output);
        File.WriteAllText(TunnelWorkshopVerification.Output + "/playtest.txt", report.ToString());
        Debug.Log("[Pavouk Playtest]\n" + report);
        EditorApplication.ExitPlaymode();
    }
}
