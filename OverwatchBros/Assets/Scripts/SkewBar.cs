using UnityEngine;
using UnityEngine.UI;

// Sikmy pruh do UI (rovnobeznik jako v Overwatchi): horni hrana posunuta o 'skew' px doprava, 'fill' ho vysune zleva
// (0 = nic, 1 = cely) a 'rightColor' dela vodorovny prechod (napr. stin, ktery doprava mizi). Karta POTG (PotgCard).
public class SkewBar : MaskableGraphic
{
    public float skew = 24f;
    [Range(0f, 1f)] public float fill = 1f;
    public bool gradient;
    public Color rightColor = Color.clear;

    public void SetFill(float value)
    {
        value = Mathf.Clamp01(value);
        if (Mathf.Abs(value - fill) < 1e-4f) return;
        fill = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        float width = r.width * Mathf.Clamp01(fill);
        if (width <= 0.01f) return;

        float x0 = r.xMin, x1 = r.xMin + width, y0 = r.yMin, y1 = r.yMax;
        Color32 left = color;
        Color32 right = gradient ? (Color32)(rightColor * new Color(1f, 1f, 1f, color.a)) : left;
        if (gradient && fill < 1f) right = Color32.Lerp(left, right, fill);
        vh.AddVert(new Vector3(x0, y0), left, new Vector2(0f, 0f));
        vh.AddVert(new Vector3(x0 + skew, y1), left, new Vector2(0f, 1f));
        vh.AddVert(new Vector3(x1 + skew, y1), right, new Vector2(1f, 1f));
        vh.AddVert(new Vector3(x1, y0), right, new Vector2(1f, 0f));
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }

    // Novy pruh v rodici: kotva i pivot vlevo dole, pozice a velikost v px.
    public static SkewBar Make(Transform parent, string name, Color color, Vector2 position, Vector2 size, float skew)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(SkewBar));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var bar = go.GetComponent<SkewBar>();
        bar.color = color;
        bar.skew = skew;
        bar.raycastTarget = false;
        return bar;
    }
}
