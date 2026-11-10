using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Popisek pod zamerovacem, kdyz hrac miri na objekt (napr. visici postavicka = "Maskovaný bojovník").
// Objekt nepotrebuje kolizi (strely jim dal prolétaji): testuje se paprsek ze stredu obrazovky proti obalum
// jeho viditelnych dilu a jestli pred nim neni zed. Jen lokalne, nic po siti.
public class AimLabel : MonoBehaviour
{
    public string label = "Maskovaný bojovník";
    public float maxDistance = 30f;
    [Tooltip("Jen dily s temito jmeny (prazdne = vsechny viditelne dily objektu).")]
    public string[] onlyParts;

    // Popisky pridavane samy pri nacteni sceny: jmeno objektu ve scene, text, ktere dily (null = vse).
    static readonly (string objectName, string label, string[] parts)[] SceneLabels =
    {
        ("GaucSeSpacim", "Překoksovaný válečník",
            new[] { "Noha", "Ponozka", "Zadek", "Pasek", "Trup", "Ruce", "Dlan", "Hlava", "Ksiltovka", "Ksilt" }),
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        AttachSceneLabels();
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => AttachSceneLabels();

    static void AttachSceneLabels()
    {
        foreach (var t in FindObjectsByType<Transform>())
            foreach (var entry in SceneLabels)
            {
                if (t.name != entry.objectName || t.GetComponent<AimLabel>() != null) continue;
                var label = t.gameObject.AddComponent<AimLabel>();
                label.label = entry.label;
                label.onlyParts = entry.parts;
            }
    }

    static readonly List<AimLabel> all = new List<AimLabel>();
    static TextMeshProUGUI text;
    static int lastFrame = -1;

    Renderer[] parts;

    void OnEnable()
    {
        all.Add(this);
        parts = null;
    }

    void OnDisable()
    {
        all.Remove(this);
        if (all.Count == 0 && text != null) text.enabled = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        all.Clear();
        text = null;
        lastFrame = -1;
    }

    void LateUpdate()
    {
        if (lastFrame == Time.frameCount) return;   // jeden prepocet za snimek pro vsechny popisky
        lastFrame = Time.frameCount;
        string shown = Find();
        if (shown != null) EnsureText();
        if (text == null) return;
        text.text = shown ?? "";
        text.enabled = shown != null;
    }

    static string Find()
    {
        var cam = Camera.main;
        if (cam == null || ReplayPlayer.Active || PotgIntro.Active || !GameSettings.CursorLocked) return null;
        var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        AimLabel best = null;
        float bestDistance = float.MaxValue;
        foreach (var target in all)
        {
            if (target == null) continue;
            target.parts ??= target.CollectParts();
            foreach (var part in target.parts)
            {
                if (part == null || !part.enabled || !part.gameObject.activeInHierarchy) continue;
                if (part.bounds.IntersectRay(ray, out float distance) && distance < bestDistance && distance <= target.maxDistance)
                {
                    best = target;
                    bestDistance = distance;
                }
            }
        }
        if (best == null) return null;
        // zed nebo jiny objekt pred nim (vlastni hrac neprekazi - paprsek zacina uvnitr jeho kolize)
        if (Physics.Raycast(ray, out var hit, Mathf.Max(0f, bestDistance - 0.05f), ~0, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(best.transform))
            return null;
        return best.label;
    }

    Renderer[] CollectParts()
    {
        var found = GetComponentsInChildren<Renderer>(true);
        if (onlyParts == null || onlyParts.Length == 0) return found;
        return System.Array.FindAll(found, r => System.Array.IndexOf(onlyParts, r.name) >= 0);
    }

    static void EnsureText()
    {
        if (text != null) return;
        var go = new GameObject("AimLabelCanvas");
        DontDestroyOnLoad(go);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        text = UiKit.MakeText(go.transform, "Popisek", "", 30f, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -48f), new Vector2(800f, 50f), new Color(1f, 0.92f, 0.75f));
        text.fontStyle = FontStyles.Bold;
        text.outlineWidth = 0.2f;
        text.outlineColor = new Color32(0, 0, 0, 220);
        text.enabled = false;
    }
}
