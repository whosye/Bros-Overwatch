using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Filmovy zaber pro vsechny hrace: kdyz visici postavicka zacne shyb (FoldingFloor), obraz se na par sekund
// prepne na kameru za postavickou - je videt shyb (fotka na zatylku) a zhrouceni podlahy. HUD se schova, nahore
// a dole cerne filmove pruhy, pak zpet do hry. Spousti si ho kazdy pocitac sam (stav podlahy se pocita
// z MatchManageru stejne vsude) - nic se neposila po siti. Hrac se behem zaberu dal muze hybat.
public class FloorCutscene : MonoBehaviour
{
    const float FadeTime = 0.35f, BarHeight = 0.12f;

    Transform figure;
    float length, start;
    Camera cam;
    Image fade, topBar, bottomBar;
    readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    Vector3 fromPosition, toPosition, lookAt;

    static FloorCutscene active;

    // Spusti zaber za postavickou na 'seconds' sekund.
    public static void Play(Transform figure, float seconds)
    {
        if (figure == null) return;
        if (active != null) Destroy(active.gameObject);
        var go = new GameObject("ZaberPodlaha");
        active = go.AddComponent<FloorCutscene>();
        active.figure = figure;
        active.length = seconds;
    }

    void Start()
    {
        start = Time.time;

        ComputeShot(figure, out fromPosition, out toPosition, out lookAt);

        cam = gameObject.AddComponent<Camera>();
        var main = Camera.main;
        if (main != null)
        {
            cam.fieldOfView = main.fieldOfView;
            cam.clearFlags = main.clearFlags;
            cam.backgroundColor = main.backgroundColor;
            cam.cullingMask = main.cullingMask;
        }
        cam.nearClipPlane = 0.05f;
        cam.depth = 100f;   // nad kamerou hrace
        Pose(0f);

        // HUD pryc (vsechny prekryvne platna), vlastni platno s pruhy a stmivanim
        foreach (var canvas in FindObjectsByType<Canvas>())
            if (canvas.isRootCanvas && canvas.enabled && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                canvas.enabled = false;
                hiddenCanvases.Add(canvas);
            }
        var canvasObject = new GameObject("ZaberPlatno");
        canvasObject.transform.SetParent(transform, false);
        var own = canvasObject.AddComponent<Canvas>();
        own.renderMode = RenderMode.ScreenSpaceOverlay;
        own.sortingOrder = 900;
        topBar = Bar(own.transform, "PruhNahore", new Vector2(0f, 1f - BarHeight), Vector2.one);
        bottomBar = Bar(own.transform, "PruhDole", Vector2.zero, new Vector2(1f, BarHeight));
        fade = Bar(own.transform, "Stmivani", Vector2.zero, Vector2.one);
    }

    // Kamera za postavickou (postavicka je celem ke zdi, fotka na zatylku), mirne z boku a zespodu; pomale priblizeni.
    public static void ComputeShot(Transform figure, out Vector3 from, out Vector3 to, out Vector3 target)
    {
        Vector3 grip = figure.position;
        Vector3 forward = figure.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        target = grip + Vector3.down * 0.75f;
        Vector3 back = -forward * 3.2f + right * 0.9f + Vector3.down * 0.9f;
        // nic mezi kamerou a postavickou (zed, sloup): kamera bliz
        float distance = back.magnitude;
        if (Physics.Raycast(target, back / distance, out var hit, distance, ~0, QueryTriggerInteraction.Ignore))
            distance = Mathf.Max(1.2f, hit.distance - 0.25f);
        from = target + back.normalized * distance;
        to = target + back.normalized * (distance * 0.82f);
    }

    public static Image Bar(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = go.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        return image;
    }

    void LateUpdate()
    {
        float t = Time.time - start;
        if (t >= length || figure == null)
        {
            Destroy(gameObject);
            return;
        }
        Pose(t / length);
        // zatmeni na zacatku a na konci
        float alpha = Mathf.Max(1f - t / FadeTime, 1f - (length - t) / FadeTime);
        fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
    }

    void Pose(float k)
    {
        float eased = k * k * (3f - 2f * k);
        transform.position = Vector3.Lerp(fromPosition, toPosition, eased);
        // pri padu podlahy kamera lehce zatrese
        float shake = HangingFigure.Duration / Mathf.Max(0.01f, length);
        float after = Mathf.Clamp01((k - shake) * length / 0.6f);
        Vector3 jitter = after > 0f && after < 1f ? Random.insideUnitSphere * 0.04f * (1f - after) : Vector3.zero;
        transform.rotation = Quaternion.LookRotation(lookAt + jitter - transform.position, Vector3.up);
    }

    void OnDestroy()
    {
        foreach (var canvas in hiddenCanvases)
            if (canvas != null) canvas.enabled = true;
        hiddenCanvases.Clear();
        if (active == this) active = null;
    }
}
