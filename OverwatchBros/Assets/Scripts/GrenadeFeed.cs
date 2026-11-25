using UnityEngine;

// Granaty v bedne zasobniku (Honzuv granatomet): pri kazdem vystrelu (HeldWeapons.Swing) prvni granat u hlavne zmizi
// (byl vystrelen), ostatni se posunou o jedno misto blize k hlavni a na misto druheho tak "prijede" novy.
// Po posunu se vse nenapadne vrati na puvodni mista (granaty jsou stejne - sdileji jednu sit), takze se to opakuje
// donekonecna. Komponentu prida Editor/GrenadeLauncherSetup na dil "Magazine" (granaty jsou jeho deti).
public class GrenadeFeed : MonoBehaviour
{
    [Tooltip("Granaty po rade: 1. u hlavne (pri vystrelu zmizi), dalsi postupne hloubeji v bedne.")]
    public Transform[] grenades;
    [Tooltip("Za jak dlouho prvni granat zmizi (s).")]
    public float hideSeconds = 0.06f;
    [Tooltip("Jak dlouho se ostatni posouvaji o misto (s).")]
    public float moveSeconds = 0.22f;
    [Tooltip("Zpozdeni posunu po vystrelu (s).")]
    public float moveDelay = 0.04f;

    Vector3[] positions, scales;
    Quaternion[] rotations;
    bool ready;
    float t = 999f;

    void Awake()
    {
        Init();
        enabled = false;   // bezi jen behem posunu
    }

    void Init()
    {
        if (ready || grenades == null) return;
        ready = true;
        int n = grenades.Length;
        positions = new Vector3[n];
        rotations = new Quaternion[n];
        scales = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            if (grenades[i] == null) continue;
            positions[i] = grenades[i].localPosition;
            rotations[i] = grenades[i].localRotation;
            scales[i] = grenades[i].localScale;
        }
    }

    public void Feed()
    {
        Init();
        if (!ready || grenades.Length == 0) return;
        if (t < moveDelay + moveSeconds) ResetGrenades();   // (dalsi vystrel driv, nez posun dobehl)
        t = 0f;
        enabled = true;
    }

    void Update()
    {
        t += Time.deltaTime;
        float done = moveDelay + moveSeconds;
        if (t >= done)
        {
            ResetGrenades();
            enabled = false;
            return;
        }

        // prvni zmizi (rychle se zmensi)
        if (grenades[0] != null)
            grenades[0].localScale = scales[0] * (1f - Mathf.Clamp01(t / Mathf.Max(0.01f, hideSeconds)));

        // ostatni o misto blize k hlavni
        float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - moveDelay) / Mathf.Max(0.01f, moveSeconds)));
        for (int i = 1; i < grenades.Length; i++)
        {
            if (grenades[i] == null) continue;
            grenades[i].localPosition = Vector3.Lerp(positions[i], positions[i - 1], u);
            grenades[i].localRotation = Quaternion.Slerp(rotations[i], rotations[i - 1], u);
        }
    }

    void ResetGrenades()
    {
        if (!ready) return;
        for (int i = 0; i < grenades.Length; i++)
        {
            if (grenades[i] == null) continue;
            grenades[i].localPosition = positions[i];
            grenades[i].localRotation = rotations[i];
            grenades[i].localScale = scales[i];
        }
    }

    void OnDisable()
    {
        // (zbran schovana uprostred posunu: priste vse na svem miste)
        if (ready && !gameObject.activeInHierarchy)
        {
            ResetGrenades();
            t = 999f;
        }
    }
}
