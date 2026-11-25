using UnityEngine;

public sealed class SniperRifleVisual : MonoBehaviour
{
    [SerializeField] Transform grip, foregrip, sight;
    [SerializeField] Transform bolt, boltGrip, magazine, magazineGrip;

    public Transform Grip => grip;
    public Transform Foregrip => foregrip;
    public Transform Sight => sight;

    Vector3 boltRest, magazineRest;
    Quaternion boltRotation, magazineRotation;

    void Awake()
    {
        boltRest = bolt.localPosition;
        boltRotation = bolt.localRotation;
        magazineRest = magazine.localPosition;
        magazineRotation = magazine.localRotation;
    }

    public static float Ease(float from, float to, float t) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));

    static float Envelope(float t, float start, float inEnd, float outStart, float end) =>
        Ease(start, inEnd, t) * (1f - Ease(outStart, end, t));

    public static float BoltPose(float t) => Envelope(t, 0f, 0.21f, 0.75f, 1f);
    public static float ReloadPose(float t) => Envelope(t, 0f, 0.18f, 0.8f, 1f);

    public void SampleParts(float cycle, float reload)
    {
        float lift = Envelope(cycle, 0.16f, 0.30f, 0.73f, 0.87f);
        float travel = Envelope(cycle, 0.30f, 0.49f, 0.53f, 0.73f);
        bolt.SetLocalPositionAndRotation(boltRest + Vector3.back * (0.074f * travel),
            Quaternion.AngleAxis(53f * lift, Vector3.forward) * boltRotation);

        float removal = Envelope(reload, 0.21f, 0.43f, 0.61f, 0.79f);
        magazine.SetLocalPositionAndRotation(magazineRest + new Vector3(-0.035f, -0.18f, 0.01f) * removal,
            Quaternion.Euler(12f * removal, 0f, -13f * removal) * magazineRotation);
        bool showMagazine = reload < 0.44f || reload >= 0.61f;
        if (magazine.gameObject.activeSelf != showMagazine)
            magazine.gameObject.SetActive(showMagazine);
    }

    public Vector3 TriggerHand(float cycle) =>
        Vector3.Lerp(grip.position, boltGrip.position, Envelope(cycle, 0f, 0.16f, 0.87f, 1f));

    public Vector3 SupportHand(float reload) =>
        Vector3.Lerp(foregrip.position, magazineGrip.position, Envelope(reload, 0.07f, 0.21f, 0.79f, 0.96f));
}
