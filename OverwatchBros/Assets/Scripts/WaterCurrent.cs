using System.Collections.Generic;
using UnityEngine;

// Proud / klouzani: oblast (kvadr podle transformu a 'size'), ve ktere je hrac unasen smerem 'forward' objektu
// rychlosti 'speed' (m/s). Pouziva skluzavka (rozjede dolu do potoka) a potok (odnese do jezirka).
// Pohyb ridi vlastnik hrace (FirstPersonController), proto se oblasti jen registruji a hraci se ptaji.
public class WaterCurrent : MonoBehaviour
{
    public Vector3 size = new Vector3(2f, 1.5f, 8f);
    public float speed = 5f;
    // Skluz toboganu: kdo po nem sjede do vody, jede po vode rychleji (RideBoost), dokud z ni nevystoupi.
    public bool slide;
    // Start jizdy: plosina toboganu (nic nenese). Jizda z toboganu (rychla voda, splouchani) se zapne jen tomu,
    // kdo na skluz vjel z teto plosiny - kdo do skluzu nebo vody vleze jinudy, jede normalne.
    public bool start;
    const float ArmedSeconds = 1.5f;   // jak dlouho po opusteni plosiny jeste plati, ze z ni hrac jede

    public const float RideBoost = 2.5f;

    static readonly List<WaterCurrent> all = new List<WaterCurrent>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() => all.Remove(this);

    // Rychlost unaseni v miste chodidel (nulova mimo vsechny oblasti). 'riding' = hrac prijel z toboganu:
    // zapne se na skluzu, plati po vode a vypne se, jakmile hrac ze vsech oblasti vystoupi.
    // 'surfing' = jede z toboganu po vode (ne na skluzu) - podle toho se kresli splouchani.
    // 'armedUntil' = do kdy hrac smi odstartovat jizdu (byl na plosine).
    public static Vector3 PushAt(Vector3 feet, ref bool riding, ref float armedUntil, out bool surfing)
    {
        surfing = false;
        Vector3 direction = Vector3.zero;
        float speed = 0f;
        bool onSlide = false;
        foreach (var zone in all)
        {
            if (zone == null || !zone.Contains(feet)) continue;
            if (zone.start)
            {
                armedUntil = Time.time + ArmedSeconds;
                continue;
            }
            onSlide |= zone.slide;
            direction += zone.transform.forward * zone.speed;
            speed = Mathf.Max(speed, zone.speed);
        }

        if (onSlide)
        {
            if (Time.time < armedUntil) riding = true;
            if (riding) armedUntil = Time.time + ArmedSeconds;   // po celem skluzu
        }
        else if (speed <= 0f) riding = false;
        if (speed <= 0f || direction.sqrMagnitude < 0.0001f) return Vector3.zero;
        surfing = riding && !onSlide;

        // V ohybech se oblasti prekryvaji: smer jejich souctu, rychlost jedne (ne dvojnasobna).
        return direction.normalized * speed * (riding && !onSlide ? RideBoost : 1f);
    }

    bool Contains(Vector3 feet)
    {
        Vector3 local = transform.InverseTransformPoint(feet + Vector3.up * 0.3f);
        return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = start ? new Color(0.3f, 1f, 0.4f, 0.8f) : new Color(0.3f, 0.7f, 1f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * size.z * 0.5f);
    }
}
