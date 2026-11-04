using UnityEngine;

// Ukazatel omraceni nad hlavou (vsichni ho vidi, jako "Zzz" u uspani): tri zlute zarici hvezdicky krouzi kolem hlavy.
// Zmizi po case nebo pri smrti. Spousti ho FirstPersonController.StunClientRpc (a zaznam pro POTG).
public class StunMarker : MonoBehaviour
{
    const float Height = 2.05f;
    const float Radius = 0.32f;
    const int Stars = 3;
    static readonly Color StarColor = new Color(1f, 0.85f, 0.2f);
    static Material starMaterial;

    float until;
    Health health;
    readonly Transform[] stars = new Transform[Stars];

    public static void Show(Transform player, float seconds)
    {
        ReplayLog.StunShow(player, seconds);
        var marker = player.GetComponentInChildren<StunMarker>(true);
        if (marker == null)
        {
            var go = new GameObject("StunMarker");
            go.transform.SetParent(player, false);
            go.transform.localPosition = new Vector3(0f, Height, 0f);
            marker = go.AddComponent<StunMarker>();
            marker.health = player.GetComponent<Health>();
            marker.Build();
        }
        marker.until = Mathf.Max(marker.until, Time.time + seconds);
        marker.gameObject.SetActive(true);
    }

    void Build()
    {
        if (starMaterial == null)
        {
            starMaterial = Fx.NewLit(StarColor);
            starMaterial.EnableKeyword("_EMISSION");
            starMaterial.SetColor("_EmissionColor", StarColor * 2.5f);
        }
        for (int i = 0; i < Stars; i++)
        {
            var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            star.name = "Star";
            DestroyImmediate(star.GetComponent<Collider>());
            star.transform.SetParent(transform, false);
            star.transform.localScale = Vector3.one * 0.09f;
            var renderer = star.GetComponent<Renderer>();
            renderer.sharedMaterial = starMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stars[i] = star.transform;
        }
    }

    void LateUpdate()
    {
        if (Time.time >= until || (health != null && health.currentHealth.Value <= 0f))
        {
            until = 0f;
            gameObject.SetActive(false);
            return;
        }

        // vlastni hlava v 1. osobe: hvezdicky by poletovaly pred kamerou
        var cam = Camera.main;
        bool own = cam != null && (cam.transform.position - transform.position).sqrMagnitude < 0.8f * 0.8f;
        foreach (var star in stars)
            if (star.gameObject.activeSelf == own) star.gameObject.SetActive(!own);

        // krouzi kolem hlavy, kazda hvezdicka trochu poskakuje a pulzuje
        float spin = Time.time * 6f;
        for (int i = 0; i < Stars; i++)
        {
            float a = spin + i * Mathf.PI * 2f / Stars;
            stars[i].localPosition = new Vector3(Mathf.Cos(a) * Radius, Mathf.Sin(a * 2f) * 0.05f, Mathf.Sin(a) * Radius);
            stars[i].localScale = Vector3.one * (0.08f + 0.025f * Mathf.Sin(Time.time * 14f + i));
        }
        transform.rotation = Quaternion.identity;
    }
}
