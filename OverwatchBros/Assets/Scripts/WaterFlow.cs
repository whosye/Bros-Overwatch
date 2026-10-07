using System.Collections.Generic;
using UnityEngine;

// Tekouci voda: textura vlnek ujizdi po proudu (koryto ma UV v = vzdalenost podel toku).
// U jezirka (pond) se vlnky jen pomalu prelevaji sem a tam. Jezirko je zaroven cil jizdy z toboganu
// (WaterSplashFx tam udela velky splouch).
public class WaterFlow : MonoBehaviour
{
    public float speed = 1.6f;      // m/s podel toku (koryto)
    public bool pond;               // jezirko: misto toku jen pomale vlneni
    public float radius = 3.2f;     // jezirko: polomer hladiny

    static readonly List<WaterFlow> ponds = new List<WaterFlow>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ponds.Clear();

    Material material;
    Vector2 tiling = Vector2.one;

    void OnEnable() { if (pond && !ponds.Contains(this)) ponds.Add(this); }
    void OnDisable() => ponds.Remove(this);

    void Start()
    {
        var r = GetComponent<Renderer>();
        if (r == null) return;
        material = r.material;   // vlastni kopie (posun textury jen u tohoto objektu)
        if (material.HasProperty("_BaseMap")) tiling = material.GetTextureScale("_BaseMap");
    }

    void Update()
    {
        if (material == null || !material.HasProperty("_BaseMap")) return;
        Vector2 offset = pond
            ? new Vector2(Mathf.Sin(Time.time * 0.35f) * 0.15f, Time.time * 0.03f)
            : new Vector2(0f, -Time.time * speed * tiling.y);
        material.SetTextureOffset("_BaseMap", offset);
    }

    // Je bod v nekterem jezirku (vodorovne, s rezervou)?
    public static bool InPond(Vector3 position, out Vector3 center)
    {
        foreach (var p in ponds)
        {
            if (p == null) continue;
            Vector3 c = p.transform.position;
            Vector3 d = position - c;
            d.y = 0f;
            if (d.magnitude <= p.radius + 1.5f && Mathf.Abs(position.y - c.y) < 3f)
            {
                center = c;
                return true;
            }
        }
        center = position;
        return false;
    }
}
