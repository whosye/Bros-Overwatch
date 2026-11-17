using UnityEngine;

// Hudba mista (tajna jeskyne pod tunelem): kdyz je kamera mistniho hrace uvnitr oblasti (obal vsech kolizi pod timto
// objektem, a jen pod 'maxY' - ne v chodbe nad ni), plynule se rozehraje smyckou; po odchodu se ztlumi a pozastavi
// (pri navratu pokracuje, kde prestala). Hraje jen u toho hrace, ktery tam je (2D zvuk, nesitovy).
// Volitelne 'focus' (prsten): kdo k nemu dojde tak blizko, ze ho primo vidi (a prsten lezi na miste), tomu se hudba
// prolne na 'focusClip'; kdyz odejde / ztrati ho z oci, prolne se zpatky.
public class ZoneMusic : MonoBehaviour
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 0.6f;
    public float fadeSeconds = 2f;
    [Tooltip("Hraje jen pod touto vyskou (svet) - at ji neslysi hraci v chodbe nad jeskyni.")]
    public float maxY = -4.6f;

    [Header("Hudba u predmetu (prsten)")]
    public Transform focus;
    public AudioClip focusClip;
    [Range(0f, 1f)] public float focusVolume = 0.75f;
    public float focusDistance = 12f;

    AudioSource source, focusSource;
    PickupSpot focusSpot;
    Bounds bounds;
    bool hasBounds;
    float level, focusLevel;
    float nextSight;
    bool seesFocus;

    void Start()
    {
        foreach (var c in GetComponentsInChildren<Collider>())
        {
            if (!c.enabled) continue;
            if (!hasBounds) { bounds = c.bounds; hasBounds = true; }
            else bounds.Encapsulate(c.bounds);
        }
        source = MakeSource(clip);
        if (focusClip != null) focusSource = MakeSource(focusClip);
        if (focus != null) focusSpot = focus.GetComponent<PickupSpot>();
    }

    AudioSource MakeSource(AudioClip c)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = c;
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.volume = 0f;
        return s;
    }

    void Update()
    {
        if (source == null || clip == null) return;
        var cam = Camera.main;
        Vector3 p = cam != null ? cam.transform.position : Vector3.zero;
        bool inside = cam != null && hasBounds && bounds.Contains(p) && p.y < maxY;

        if (Time.time >= nextSight)
        {
            nextSight = Time.time + 0.2f;
            seesFocus = inside && focusSource != null && SeesFocus(p);
        }

        float step = Time.unscaledDeltaTime / Mathf.Max(0.05f, fadeSeconds);
        level = Mathf.MoveTowards(level, inside && !seesFocus ? 1f : 0f, step);
        focusLevel = Mathf.MoveTowards(focusLevel, seesFocus ? 1f : 0f, step);
        Drive(source, volume * level, level);
        if (focusSource != null) Drive(focusSource, focusVolume * focusLevel, focusLevel);
    }

    static void Drive(AudioSource s, float vol, float lvl)
    {
        s.volume = vol;
        if (lvl > 0f && !s.isPlaying)
        {
            if (s.time > 0f) s.UnPause();
            else s.Play();
        }
        else if (lvl <= 0f && s.isPlaying)
            s.Pause();
    }

    // Prsten lezi na miste, je blizko a nic mezi nim a kamerou neni.
    bool SeesFocus(Vector3 eye)
    {
        if (focus == null) return false;
        if (focusSpot != null)
        {
            var match = MatchManager.Instance;
            bool present = ReplayPlayer.Active ? ReplayPlayer.PickupAvailable(focusSpot.Index) : match == null || match.PickupAvailable(focusSpot.Index);
            if (!present) return false;
        }
        Vector3 target = focus.position + Vector3.up * 0.75f;
        float distance = Vector3.Distance(eye, target);
        if (distance > focusDistance) return false;
        if (!Physics.Linecast(eye, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)) return true;
        return hit.distance > distance - 0.8f || hit.collider.transform.IsChildOf(focus);
    }
}
