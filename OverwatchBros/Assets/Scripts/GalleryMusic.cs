using UnityEngine;

// Hudba galerie Grand Theft Domasov (podzemi pod tunelem): naplno v mistnosti a chodbicce k ni, v hlavni chodbe
// u vchodu slabe a tlumene (jako za zdi), jinde ani nad zemi neni slyset (pak je pozastavena a pokracuje, kde skoncila).
// S hudbou Pavoukova workshopu (TunnelWorkshop) se nepere: v galerii workshop ztichne (Inside01), v chodbe ma
// prednost workshop - cim bliz jeho dverim, tim min je slyset galerie.
// Souradnice jsou mistni vuci objektu galerie (rodic tohoto objektu = GrandTheftDomasov), ne vuci objektu Hudba.
public class GalleryMusic : MonoBehaviour
{
    public AudioSource source;
    public AudioLowPassFilter filter;
    public Bounds room = new Bounds(new Vector3(9.8f, -2.3f, 12.75f), new Vector3(10.6f, 4.0f, 11.6f));
    public Bounds passage = new Bounds(new Vector3(3.15f, -2.3f, 12.75f), new Vector3(3.3f, 4.0f, 3.1f));
    public Vector3 door = new Vector3(1.65f, -2.6f, 12.75f);
    [Tooltip("Do jake vzdalenosti od vchodu je hudba slyset v hlavni chodbe (m).")]
    public float corridorRadius = 8f;
    [Range(0f, 1f)] public float volume = 0.6f;
    [Tooltip("Nad touto vyskou (mistne) neni hudba slyset vubec - zem je v 0, dlazdice do -0,3.")]
    public float surfaceY = -0.3f;

    // 0-1: jak moc je posluchac v galerii (plynule) - TunnelWorkshop o tolik ztisi svou hudbu.
    public float Inside01 { get; private set; }

    AudioListener listener;
    TunnelWorkshop workshop;
    bool searchedWorkshop;
    float nextFind, blend, cutoff = 22000f;

    // Galerie a workshop ve stejne mape (spolecny rodic) - kazdy najde toho druheho.
    public static T FindInMap<T>(Transform from) where T : Component
    {
        Transform map = from;
        while (map.parent != null) map = map.parent;
        return map.GetComponentInChildren<T>(true);
    }

    void Update()
    {
        if (source == null) return;
        // aktivni posluchac (ostatni hraci maji AudioListener vypnuty - ten by hlasil cizi polohu)
        if ((listener == null || !listener.isActiveAndEnabled) && Time.unscaledTime >= nextFind)
        {
            nextFind = Time.unscaledTime + 0.5f;
            listener = null;
            foreach (var candidate in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (candidate.isActiveAndEnabled) { listener = candidate; break; }
        }
        Transform frame = transform.parent != null ? transform.parent : transform;

        if (!searchedWorkshop) { searchedWorkshop = true; workshop = FindInMap<TunnelWorkshop>(transform); }
        float target = 0f, wantCutoff = 22000f;
        bool inside = false;
        if (listener != null && listener.isActiveAndEnabled)
        {
            Vector3 p = frame.InverseTransformPoint(listener.transform.position);
            if (p.y < surfaceY)
            {
                if (room.Contains(p) || passage.Contains(p)) { target = 1f; inside = true; }
                else
                {
                    float d = Vector3.Distance(p, door);
                    if (d < corridorRadius)
                    {
                        target = 0.45f * (1f - d / corridorRadius);
                        if (workshop != null && workshop.isActiveAndEnabled)
                            target *= 1f - Mathf.Clamp01(workshop.GainAt(listener.transform.position));
                        wantCutoff = 1300f;
                    }
                }
            }
        }

        blend = Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime * 1.5f);
        Inside01 = Mathf.MoveTowards(Inside01, inside ? 1f : 0f, Time.unscaledDeltaTime * 1.5f);
        cutoff = Mathf.Lerp(cutoff, wantCutoff, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f));
        if (filter != null) filter.cutoffFrequency = cutoff;
        source.volume = blend * volume;

        if (blend > 0.001f) { if (!source.isPlaying) source.Play(); }
        else if (source.isPlaying) source.Pause();
    }
}
