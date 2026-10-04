using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Male pomucky pro sestaveni UI kodem (bez nutnosti klikat ve scene).
public static class UiKit
{
    public static readonly Color Accent = new Color(0.95f, 0.55f, 0.10f, 1f);
    public static readonly Color AccentDark = new Color(0.70f, 0.38f, 0.05f, 1f);
    public static readonly Color Green = new Color(0.20f, 0.62f, 0.32f, 1f);
    public static readonly Color Panel = new Color(0.07f, 0.09f, 0.13f, 0.94f);
    public static readonly Color PanelLight = new Color(0.12f, 0.15f, 0.21f, 0.96f);
    public static readonly Color ButtonBase = new Color(0.16f, 0.20f, 0.28f, 1f);
    public static readonly Color Team0 = new Color(0.30f, 0.64f, 1f, 1f);
    public static readonly Color Team1 = new Color(1f, 0.35f, 0.30f, 1f);
    public static readonly Color Muted = new Color(0.65f, 0.70f, 0.78f, 1f);

    public static Color TeamColor(int team) => team == 0 ? Team0 : Team1;

    static RectTransform NewRect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, params System.Type[] components)
    {
        var go = new GameObject(name, components.Length == 0 ? new[] { typeof(RectTransform) } : Prepend(components));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    static System.Type[] Prepend(System.Type[] components)
    {
        var all = new System.Type[components.Length + 1];
        all[0] = typeof(RectTransform);
        components.CopyTo(all, 1);
        return all;
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public static Image MakeImage(Transform parent, string name, Color color, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rect = NewRect(parent, name, anchor, position, size, typeof(Image));
        var image = rect.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static TextMeshProUGUI MakeText(Transform parent, string name, string text, float fontSize, TextAlignmentOptions alignment,
        Vector2 anchor, Vector2 position, Vector2 size, Color? color = null)
    {
        var rect = NewRect(parent, name, anchor, position, size, typeof(TextMeshProUGUI));
        var label = rect.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = color ?? Color.white;
        label.raycastTarget = false;
        label.richText = true;
        return label;
    }

    public static Button MakeButton(Transform parent, string label, Vector2 anchor, Vector2 position, Vector2 size,
        UnityAction onClick, Color? color = null, float fontSize = 30f)
    {
        var rect = NewRect(parent, label + "Button", anchor, position, size, typeof(Image), typeof(Button));
        var image = rect.GetComponent<Image>();
        image.color = color ?? ButtonBase;

        var button = rect.GetComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.pressedColor = new Color(0.62f, 0.62f, 0.62f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        button.colors = colors;
        if (onClick != null)
            button.onClick.AddListener(onClick);

        var text = MakeText(rect, "Label", label, fontSize, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), Vector2.zero, size);
        Stretch(text.rectTransform);
        return button;
    }

    public static Slider MakeSlider(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size,
        float min, float max, float initial, UnityAction<float> onChanged, bool wholeNumbers = false)
    {
        var rect = NewRect(parent, name, anchor, position, size, typeof(Image), typeof(Slider));
        // Transparent hit area makes the whole row clickable, not just the thin track.
        rect.GetComponent<Image>().color = Color.clear;
        var center = new Vector2(0.5f, 0.5f);
        MakeImage(rect, "Track", ButtonBase, center, Vector2.zero, new Vector2(size.x, 10f));

        var fillArea = NewRect(rect, "FillArea", center, Vector2.zero, new Vector2(size.x - 28f, 10f));
        var fill = MakeImage(fillArea, "Fill", Accent, center, Vector2.zero, Vector2.zero);
        Stretch(fill.rectTransform);
        var handleArea = NewRect(rect, "HandleArea", center, Vector2.zero, new Vector2(size.x - 28f, size.y));
        var handle = MakeImage(handleArea, "Handle", Color.white, center, Vector2.zero, new Vector2(28f, 34f));
        handle.raycastTarget = true;

        var slider = rect.GetComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = wholeNumbers;
        slider.SetValueWithoutNotify(initial);
        if (onChanged != null) slider.onValueChanged.AddListener(onChanged);
        return slider;
    }

    public static void SetButtonLabel(Button button, string label)
    {
        var text = button.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null)
            text.text = label;
    }

    public static void SetButtonColor(Button button, Color color)
    {
        var image = button.targetGraphic as Image;
        if (image != null)
            image.color = color;
    }

    public static TMP_InputField MakeInput(Transform parent, string placeholder, Vector2 anchor, Vector2 position, Vector2 size,
        string initial = "", float fontSize = 30f)
    {
        var rect = NewRect(parent, "Input", anchor, position, size, typeof(Image), typeof(TMP_InputField));
        var background = rect.GetComponent<Image>();
        background.color = new Color(0.16f, 0.20f, 0.29f, 1f);

        var outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = Accent;
        outline.effectDistance = new Vector2(2f, -2f);

        var area = NewRect(rect, "Text Area", new Vector2(0.5f, 0.5f), Vector2.zero, size, typeof(RectMask2D));
        Stretch(area);
        area.offsetMin = new Vector2(14f, 6f);
        area.offsetMax = new Vector2(-14f, -6f);

        var placeholderText = MakeText(area, "Placeholder", placeholder, fontSize, TextAlignmentOptions.Left,
            new Vector2(0.5f, 0.5f), Vector2.zero, size, new Color(1f, 1f, 1f, 0.35f));
        placeholderText.fontStyle = FontStyles.Italic;
        Stretch(placeholderText.rectTransform);

        var text = MakeText(area, "Text", "", fontSize, TextAlignmentOptions.Left, new Vector2(0.5f, 0.5f), Vector2.zero, size);
        Stretch(text.rectTransform);

        var input = rect.GetComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = placeholderText;
        input.pointSize = fontSize;
        input.text = initial;
        return input;
    }
}
