using UnityEngine;
using UnityEngine.UI;

// A standard RawImage, like the other fullscreen HUD effects. The shared sight texture
// is generated once; clamped black borders fill any screen aspect ratio without stretching the circle.
[RequireComponent(typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage))]
public class ScopeOverlay : MonoBehaviour
{
    const int TextureSize = 1024;
    static Texture2D sightTexture;
    RawImage image;
    float blend;

    public float Blend => blend;
    public float Opacity => Mathf.SmoothStep(0f, 1f, blend);
    public RectTransform rectTransform => (RectTransform)transform;
    public RawImage Graphic => image;
    // Only replace the normal crosshair when the actual UI graphic is ready to draw.
    public bool IsVisible => blend > 0.005f && image != null && image.isActiveAndEnabled
        && image.texture != null && image.canvas != null && image.depth >= 0 && !image.canvasRenderer.cull
        && rectTransform.rect.width > 0f && rectTransform.rect.height > 0f;

    void Awake() => EnsureGraphic();
    void OnEnable() { EnsureGraphic(); Apply(); }
    void OnRectTransformDimensionsChange() => Apply();

    void EnsureGraphic()
    {
        if (image == null)
        {
            image = GetComponent<RawImage>();
            // Existing runtime UI may survive an editor script reload with the old component layout.
            if (image == null) image = gameObject.AddComponent<RawImage>();
        }
        if (sightTexture == null) sightTexture = CreateSightTexture();
        image.texture = sightTexture;
        image.raycastTarget = false;
        image.maskable = false;
    }

    public void SetBlend(float value)
    {
        if (image == null) EnsureGraphic();
        blend = Mathf.Clamp01(value);
        Apply();
    }

    void Apply()
    {
        if (image == null) return;
        image.color = new Color(1f, 1f, 1f, Opacity);
        Rect rect = rectTransform.rect;
        if (rect.width <= 0f || rect.height <= 0f) return;
        float aspect = rect.width / rect.height;
        float settle = Mathf.Lerp(0.48f / 0.43f, 1f, Opacity);
        Vector2 extent = aspect >= 1f ? new Vector2(aspect, 1f) : new Vector2(1f, 1f / aspect);
        extent /= settle;
        image.uvRect = new Rect((1f - extent.x) * 0.5f, (1f - extent.y) * 0.5f, extent.x, extent.y);
    }

    static Texture2D CreateSightTexture()
    {
        var pixels = new Color32[TextureSize * TextureSize];
        float radius = TextureSize * 0.43f;
        Color frame = new Color(0.008f, 0.011f, 0.014f, 1f);
        for (int y = 0; y < TextureSize; y++)
        for (int x = 0; x < TextureSize; x++)
        {
            float dx = x + 0.5f - TextureSize * 0.5f;
            float dy = y + 0.5f - TextureSize * 0.5f;
            float distance = Mathf.Sqrt(dx * dx + dy * dy) / radius;
            float alpha = distance < 0.975f
                ? Mathf.SmoothStep(0f, 0.6f, Mathf.InverseLerp(0.88f, 0.975f, distance))
                : Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(0.975f, 1f, distance));
            Color pixel = new Color(frame.r, frame.g, frame.b, alpha);
            if (distance >= 0.989f && distance <= 0.994f)
                pixel = Color.Lerp(pixel, new Color(0.20f, 0.23f, 0.25f, alpha), 0.4f);
            pixels[y * TextureSize + x] = pixel;
        }

        Color ink = new Color(0.01f, 0.012f, 0.014f, 0.85f);
        float gap = radius * 0.025f;
        float reach = radius * 0.965f;
        const float halfLine = 1.25f;
        Rectangle(pixels, -reach, -halfLine, -gap, halfLine, ink);
        Rectangle(pixels, gap, -halfLine, reach, halfLine, ink);
        Rectangle(pixels, -halfLine, -reach, halfLine, -gap, ink);
        Rectangle(pixels, -halfLine, gap, halfLine, reach, ink);
        for (int i = 1; i <= 5; i++)
        {
            float offset = radius * 0.12f * i;
            float halfTick = radius * (i % 2 == 0 ? 0.018f : 0.011f);
            Rectangle(pixels, -halfTick, -offset - halfLine, halfTick, -offset + halfLine, ink);
            for (int side = -1; side <= 1; side += 2)
                Rectangle(pixels, side * offset - halfLine, -halfTick, side * offset + halfLine, halfTick, ink);
        }

        // A small bright aiming point with a restrained glow, on a transparent lens interior.
        float dot = Mathf.Max(1.5f, radius * 0.004f);
        int reachPixels = Mathf.CeilToInt(dot * 2.1f + 1f);
        for (int y = TextureSize / 2 - reachPixels; y < TextureSize / 2 + reachPixels; y++)
        for (int x = TextureSize / 2 - reachPixels; x < TextureSize / 2 + reachPixels; x++)
        {
            float dx = x + 0.5f - TextureSize * 0.5f;
            float dy = y + 0.5f - TextureSize * 0.5f;
            float distance = Mathf.Sqrt(dx * dx + dy * dy);
            Paint(pixels, x, y, new Color(0.8f, 0.08f, 0.055f, 0.4f * (1f - Mathf.SmoothStep(0f, 1f, distance / (dot * 2.1f)))));
            Paint(pixels, x, y, new Color(1f, 0.22f, 0.12f, Mathf.Clamp01(dot + 0.5f - distance)));
        }

        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        texture.name = "SniperScopeSight";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    static void Rectangle(Color32[] pixels, float minX, float minY, float maxX, float maxY, Color color)
    {
        int startX = Mathf.Max(0, Mathf.FloorToInt(minX + TextureSize * 0.5f - 1f));
        int endX = Mathf.Min(TextureSize, Mathf.CeilToInt(maxX + TextureSize * 0.5f + 1f));
        int startY = Mathf.Max(0, Mathf.FloorToInt(minY + TextureSize * 0.5f - 1f));
        int endY = Mathf.Min(TextureSize, Mathf.CeilToInt(maxY + TextureSize * 0.5f + 1f));
        for (int y = startY; y < endY; y++)
        for (int x = startX; x < endX; x++)
        {
            float dx = x + 0.5f - TextureSize * 0.5f;
            float dy = y + 0.5f - TextureSize * 0.5f;
            float coverage = Mathf.Clamp01(Mathf.Min(Mathf.Min(dx - minX, maxX - dx), Mathf.Min(dy - minY, maxY - dy)) + 0.5f);
            Paint(pixels, x, y, new Color(color.r, color.g, color.b, color.a * coverage));
        }
    }

    static void Paint(Color32[] pixels, int x, int y, Color source)
    {
        if (source.a <= 0f) return;
        int index = y * TextureSize + x;
        Color destination = pixels[index];
        float retained = destination.a * (1f - source.a);
        float alpha = source.a + retained;
        pixels[index] = new Color((source.r * source.a + destination.r * retained) / alpha,
            (source.g * source.a + destination.g * retained) / alpha,
            (source.b * source.a + destination.b * retained) / alpha, alpha);
    }
}
