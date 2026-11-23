using UnityEngine;

// A sampled presentation, driven by HeldWeapons' existing reload clock on owners, peers and replays.
public sealed class RevolverVisual : MonoBehaviour
{
    public Transform cylinder;
    public Transform speedLoader;
    public Transform spentCases;
    public Transform supportPoint;
    public Vector3 cylinderCenter = new Vector3(0f, -.028f, .134f);
    public Transform SupportPoint => supportPoint != null ? supportPoint : cylinder;

    static float Ramp(float t, float from, float to) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));
    public static float ReloadBlend(float t) => Ramp(t, 0f, .16f) * (1f - Ramp(t, .86f, 1f));

    public static void ReloadPose(float t, out Vector3 offset, out Vector3 euler)
    {
        float blend = ReloadBlend(t);
        float eject = Ramp(t, .25f, .32f) * (1f - Ramp(t, .38f, .46f));
        offset = new Vector3(-.17f, .07f, -.08f) * blend;
        // Roll the open cylinder into view, tip up to eject, then lower for the speedloader.
        euler = new Vector3(-16f - 40f * eject, -18f, 32f) * blend;
    }

    public void SampleReload(float t)
    {
        t = Mathf.Clamp01(t);
        float open = Ramp(t, .12f, .25f) * (1f - Ramp(t, .80f, .90f));
        Vector3 openCenter = cylinderCenter + new Vector3(-.055f * open, -.012f * open, 0f);
        if (cylinder != null)
        {
            cylinder.localPosition = openCenter;
            cylinder.localRotation = Quaternion.Euler(0f, 0f, 65f * open);
        }
        bool loading = t > .44f && t < .79f;
        if (speedLoader != null)
        {
            speedLoader.gameObject.SetActive(loading);
            float insert = Ramp(t, .44f, .64f);
            float withdraw = Ramp(t, .70f, .79f);
            speedLoader.localPosition = openCenter + new Vector3(-.07f * (1f - insert + withdraw), 0f, -.065f * (1f - insert + withdraw) - .025f);
            speedLoader.localRotation = Quaternion.identity;
        }
        if (spentCases != null)
        {
            bool ejecting = t > .29f && t < .44f;
            spentCases.gameObject.SetActive(ejecting);
            float eject = Mathf.InverseLerp(.29f, .44f, t);
            spentCases.localPosition = openCenter + new Vector3(-.02f * eject, -.035f * eject * eject, -.09f * eject);
            spentCases.localRotation = Quaternion.Euler(25f * eject, 0f, 50f * eject);
        }
        if (supportPoint != null)
        {
            supportPoint.localPosition = loading && speedLoader != null ? speedLoader.localPosition : openCenter + Vector3.left * .018f;
            supportPoint.localRotation = Quaternion.identity;
        }
    }

    void OnDisable() => SampleReload(1f);
}
