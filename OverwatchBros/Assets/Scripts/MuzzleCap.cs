using UnityEngine;

// Vicko na usti zbrane (Honzuv granatomet): pri kazdem vystrelu (HeldWeapons.Swing) se na pantu odklopi
// dopredu nahoru, chvili drzi a zaklapne se (s malym odskokem). Pocatek objektu = pant, otaci se kolem lokalni osy X.
// Komponentu prida Editor/GrenadeLauncherSetup na dil "MuzzleCap" v prefabu zbrane.
public class MuzzleCap : MonoBehaviour
{
    [Tooltip("Odklopeni kolem pantu (stupne kolem lokalni osy X; zaporne = dopredu nahoru).")]
    public float openAngle = -120f;
    public float openSeconds = 0.06f;
    public float holdSeconds = 0.12f;
    public float closeSeconds = 0.25f;

    Quaternion closed;
    bool ready;
    float angle, from, t = 999f;

    void Awake()
    {
        if (ready) return;
        ready = true;
        closed = transform.localRotation;
        enabled = false;   // bezi jen behem odklopeni
    }

    public void Flap()
    {
        Awake();
        from = angle;   // (rychla strelba: z prave otevrene polohy, bez skoku)
        t = 0f;
        enabled = true;
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t < openSeconds)
            angle = Mathf.Lerp(from, openAngle, Mathf.SmoothStep(0f, 1f, t / openSeconds));
        else if (t < openSeconds + holdSeconds)
            angle = openAngle;
        else
        {
            float u = (t - openSeconds - holdSeconds) / Mathf.Max(0.01f, closeSeconds);
            if (u >= 1f)
            {
                angle = 0f;
                enabled = false;
            }
            else angle = openAngle * (1f - BounceOut(u));
        }
        transform.localRotation = closed * Quaternion.Euler(angle, 0f, 0f);
    }

    void OnDisable()
    {
        // (zbran schovana uprostred odklopeni: priste zavrene)
        if (ready && angle != 0f && !gameObject.activeInHierarchy)
        {
            angle = 0f;
            t = 999f;
            transform.localRotation = closed;
        }
    }

    // zaklapnuti s odskokem (vicko o hlaven cvakne a jeste dvakrat lehce poskoci)
    static float BounceOut(float x)
    {
        const float n = 7.5625f, d = 2.75f;
        if (x < 1f / d) return n * x * x;
        if (x < 2f / d) { x -= 1.5f / d; return n * x * x + 0.75f; }
        if (x < 2.5f / d) { x -= 2.25f / d; return n * x * x + 0.9375f; }
        x -= 2.625f / d;
        return n * x * x + 0.984375f;
    }
}
