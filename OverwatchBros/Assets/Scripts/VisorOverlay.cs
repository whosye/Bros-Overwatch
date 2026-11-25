using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Viktorova Q (takticky zamerovac): po dobu ultimatky ma hrac obraz jako pres takticke bryle - jemny oranzovy nadech,
// tmave oranzovy ram bryli se zakulacenymi rohy a sedlem pro nos, svitiva linka na vnitrni hrane, rohove znacky HUD
// a jemne radkovani. Jen mistni hrac (VisorAbility.IsActive), v POTG a killcamu hrac ze zaznamu; pod HUD; plynule nabiha a mizi.
public class VisorOverlay : MonoBehaviour
{
    static readonly Color Orange = new Color(1f, 0.55f, 0.15f);
    const int Size = 512;

    static Texture2D texture;
    RawImage image;
    float blend;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("VisorOverlay");
        DontDestroyOnLoad(go);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -5;   // pod HUD (zivoty, schopnosti zustanou citelne)
        var imageObject = new GameObject("Bryle", typeof(RectTransform));
        imageObject.transform.SetParent(go.transform, false);
        var rect = (RectTransform)imageObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var overlay = go.AddComponent<VisorOverlay>();
        overlay.image = imageObject.AddComponent<RawImage>();
        overlay.image.raycastTarget = false;
        overlay.image.enabled = false;
    }

    static bool LocalVisorActive()
    {
        var network = NetworkManager.Singleton;
        var player = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        if (ReplayPlayer.Active) return ReplayPlayer.PovVisor;   // POTG a killcam: bryle hrace ze zaznamu
        if (player == null) return false;
        var fpc = player.GetComponent<FirstPersonController>();
        if (fpc == null || fpc.IsDead || fpc.Bot != null) return false;
        var visor = player.GetComponent<VisorAbility>();
        return visor != null && visor.isActiveAndEnabled && visor.IsActive;
    }

    void Update()
    {
        bool on = LocalVisorActive();
        blend = Mathf.MoveTowards(blend, on ? 1f : 0f, Time.unscaledDeltaTime * (on ? 4f : 2.5f));
        if (blend <= 0.001f)
        {
            if (image.enabled) image.enabled = false;
            return;
        }
        if (texture == null) texture = Build();
        if (image.texture != texture) image.texture = texture;
        image.enabled = true;
        float pulse = 0.92f + 0.08f * Mathf.Sin(Time.unscaledTime * 3f);
        image.color = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, blend) * pulse);
    }

    // Zaobleny obdelnik: vzdalenost od okraje (zaporna uvnitr).
    static float RoundRect(Vector2 p, Vector2 half, float radius)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + new Vector2(radius, radius);
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }

    static Texture2D Build()
    {
        var pixels = new Color32[Size * Size];
        Color frame = new Color(0.32f, 0.13f, 0.02f);
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            Vector2 p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
            // ram bryli: zaobleny obdelnik, dole uprostred sedlo pro nos
            float d = RoundRect(p, new Vector2(0.94f, 0.88f), 0.42f);
            float nose = RoundRect(p - new Vector2(0f, -1.02f), new Vector2(0.13f, 0.2f), 0.13f);
            d = Mathf.Max(d, -nose);   // (sedlo vykrojene z otvoru = ram navic)

            // zaklad: jemny oranzovy nadech pres cely obraz
            Color c = new Color(Orange.r, Orange.g, Orange.b, 0.08f);
            // vnitrni zare k okraji
            if (d > -0.2f && d <= 0f)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.2f, 0f, d));
                c = Color.Lerp(c, new Color(Orange.r, Orange.g * 0.85f, Orange.b, 0.32f), k);
            }
            // svitiva linka na vnitrni hrane
            float edge = Mathf.Abs(d + 0.006f);
            if (edge < 0.012f)
                c = Color.Lerp(c, new Color(1f, 0.72f, 0.3f, 0.75f), 1f - edge / 0.012f);
            // ram
            if (d > 0f)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / 0.03f));
                c = Color.Lerp(c, new Color(frame.r, frame.g, frame.b, 0.88f), k);
            }

            // rohove znacky HUD (uvnitr otvoru)
            c = Bracket(c, p, new Vector2(-0.72f, 0.62f), 1f, -1f);
            c = Bracket(c, p, new Vector2(0.72f, 0.62f), -1f, -1f);
            c = Bracket(c, p, new Vector2(-0.72f, -0.55f), 1f, 1f);
            c = Bracket(c, p, new Vector2(0.72f, -0.55f), -1f, 1f);

            // jemne radkovani
            if (d < 0f && (y / 2) % 2 == 0) c.a += 0.025f;
            pixels[y * Size + x] = c;
        }
        var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "VisorOverlay", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        t.SetPixels32(pixels);
        t.Apply(false, true);
        return t;
    }

    // Roh ve tvaru L: od 'corner' smerem (sx, sy) dve kratke cary.
    static Color Bracket(Color c, Vector2 p, Vector2 corner, float sx, float sy)
    {
        const float len = 0.09f, thick = 0.007f;
        Vector2 local = new Vector2((p.x - corner.x) * sx, (p.y - corner.y) * sy);
        bool horizontal = local.x >= 0f && local.x <= len && Mathf.Abs(local.y) <= thick;
        bool vertical = local.y >= 0f && local.y <= len && Mathf.Abs(local.x) <= thick;
        return horizontal || vertical ? Color.Lerp(c, new Color(1f, 0.75f, 0.35f, 0.8f), 0.85f) : c;
    }
}
