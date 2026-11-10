using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Nahled filmu "shyb a zhrouceni stropu" BEZ spousteni hry: v okne Game se prehraje stejny zaber jako ve hre
// (kamera za postavickou, cerne pruhy, shyb s fotkou na zatylku, pad podlahy). Po skonceni se vse vrati presne
// tak, jak bylo - scena se nemeni. Behem nahledu (asi 5 s) ve scene nic neupravuj.
public static class FilmPreview
{
    class Saved { public Transform t; public Vector3 p, s; public Quaternion r; }

    static readonly List<Saved> saved = new List<Saved>();
    static readonly List<(GameObject go, bool active)> savedActive = new List<(GameObject, bool)>();
    static readonly List<(Renderer r, bool enabled)> savedRenderers = new List<(Renderer, bool)>();
    static FoldingFloor floor;
    static HangingFigure figure;
    static bool addedFigure;
    static GameObject rig;
    static Image fade;
    static double start;
    static float length;
    static Vector3 from, to, target;
    static bool soundPlayed;

    // Zvuk v editoru (mimo hru): nahled klipu jako v okne Project.
    static void PlayClip(AudioClip clip)
    {
        if (clip == null) return;
        var util = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        var method = util?.GetMethod("PlayPreviewClip", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
            null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        method?.Invoke(null, new object[] { clip, 0, false });
    }

    [MenuItem("BrosOverwatch/Nahled filmu - shyb a zhrouceni stropu (bez hry, okno Game)")]
    static void Play()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Film] Nahled je jen mimo hru. Ve hre pouzij BrosOverwatch > Test - shyb postavicky a zhrouceni stropu.");
            return;
        }
        if (rig != null) Stop();
        floor = Object.FindAnyObjectByType<FoldingFloor>();
        GameObject figureObject = null;
        if (floor != null)
            foreach (var go in floor.hideWhenOpen)
                if (go != null && go.transform.Find("PravaDlan") != null) figureObject = go;
        if (floor == null || figureObject == null)
        {
            Debug.LogWarning("[Film] Ve scene chybi sklopna podlaha nebo visici postavicka (v jejim Hide When Open).");
            return;
        }

        // ulozit vse, co nahled pohne
        saved.Clear(); savedActive.Clear(); savedRenderers.Clear();
        foreach (var t in figureObject.GetComponentsInChildren<Transform>(true)) Save(t);
        Save(floor.transform);
        foreach (var go in floor.hideWhenOpen) if (go != null) savedActive.Add((go, go.activeSelf));
        foreach (var r in figureObject.GetComponentsInChildren<Renderer>(true)) savedRenderers.Add((r, r.enabled));

        figure = figureObject.GetComponent<HangingFigure>();
        addedFigure = figure == null;
        if (addedFigure)
        {
            figure = figureObject.AddComponent<HangingFigure>();
            figure.hideFlags = HideFlags.DontSave;
        }
        figure.Setup(false);

        // kamera a pruhy (jen docasne, neulozi se)
        Physics.SyncTransforms();
        FloorCutscene.ComputeShot(figureObject.transform, out from, out to, out target);
        rig = new GameObject("NahledFilmu") { hideFlags = HideFlags.DontSave };
        var cam = rig.AddComponent<Camera>();
        var main = Camera.main;
        if (main != null) { cam.fieldOfView = main.fieldOfView; cam.clearFlags = main.clearFlags; cam.cullingMask = main.cullingMask; }
        cam.nearClipPlane = 0.05f;
        cam.depth = 100f;
        var canvasObject = new GameObject("Pruhy") { hideFlags = HideFlags.DontSave };
        canvasObject.transform.SetParent(rig.transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        FloorCutscene.Bar(canvas.transform, "PruhNahore", new Vector2(0f, 0.88f), Vector2.one);
        FloorCutscene.Bar(canvas.transform, "PruhDole", Vector2.zero, new Vector2(1f, 0.12f));
        fade = FloorCutscene.Bar(canvas.transform, "Stmivani", Vector2.zero, Vector2.one);

        length = HangingFigure.Duration + floor.duration + 1.3f;
        start = EditorApplication.timeSinceStartup;
        soundPlayed = false;
        EditorApplication.update += Tick;
        FocusGameView();
        Debug.Log($"[Film] Nahled ({length:0.0} s) v okne Game. Pak se scena vrati, jak byla.");
    }

    static void Save(Transform t) => saved.Add(new Saved { t = t, p = t.localPosition, r = t.localRotation, s = t.localScale });

    static void Tick()
    {
        if (rig == null || floor == null || figure == null) { Stop(); return; }
        float t = (float)(EditorApplication.timeSinceStartup - start);
        if (t >= length) { Stop(); return; }

        // postavicka (shyb), pak podlaha (pad se zrychlenim), co ma zmizet, zmizi
        figure.Evaluate(t, t);
        float fall = Mathf.Clamp01((t - HangingFigure.Duration) / Mathf.Max(0.05f, floor.duration));
        if (!soundPlayed && t >= HangingFigure.Duration)
        {
            soundPlayed = true;
            PlayClip(floor.collapseSound != null ? floor.collapseSound : AssetDatabase.LoadAssetAtPath<AudioClip>(DecorTools.CollapseSoundPath));
        }
        floor.transform.localRotation = floor.closedLocalRotation * Quaternion.AngleAxis(floor.openAngle * fall * fall, floor.axis);
        bool show = fall <= 0.001f;
        foreach (var go in floor.hideWhenOpen) if (go != null && go.activeSelf != show) go.SetActive(show);

        // kamera jako ve hre
        float k = t / length, eased = k * k * (3f - 2f * k);
        rig.transform.position = Vector3.Lerp(from, to, eased);
        float after = Mathf.Clamp01((t - HangingFigure.Duration) / 0.6f);
        Vector3 jitter = after > 0f && after < 1f ? Random.insideUnitSphere * 0.04f * (1f - after) : Vector3.zero;
        rig.transform.rotation = Quaternion.LookRotation(target + jitter - rig.transform.position, Vector3.up);
        float alpha = Mathf.Max(1f - t / 0.35f, 1f - (length - t) / 0.35f);
        fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));

        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    static void Stop()
    {
        EditorApplication.update -= Tick;
        foreach (var s in saved)
            if (s.t != null) { s.t.localPosition = s.p; s.t.localRotation = s.r; s.t.localScale = s.s; }
        foreach (var (go, active) in savedActive) if (go != null) go.SetActive(active);
        foreach (var (r, enabled) in savedRenderers) if (r != null) r.enabled = enabled;
        if (addedFigure && figure != null) Object.DestroyImmediate(figure);
        if (rig != null) Object.DestroyImmediate(rig);
        saved.Clear(); savedActive.Clear(); savedRenderers.Clear();
        rig = null; figure = null; floor = null;
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    static void FocusGameView()
    {
        var type = System.Type.GetType("UnityEditor.GameView,UnityEditor");
        if (type != null) EditorWindow.GetWindow(type, false, null, true);
    }
}
