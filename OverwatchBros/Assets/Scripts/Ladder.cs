using System.Collections.Generic;
using UnityEngine;

// Zebrik: oblast (kvadr podle transformu a 'size'), ve ktere hrac misto padani leze - W nahoru, S dolu, bez
// klaves se drzi na miste, mezernikem seskoci. Pohyb ridi vlastnik hrace (FirstPersonController), proto se
// oblasti jen registruji a hraci se ptaji (jako WaterCurrent).
public class Ladder : MonoBehaviour
{
    public Vector3 size = new Vector3(0.9f, 8f, 0.9f);

    public const float ClimbSpeed = 4f;
    public const float SideSpeedScale = 0.35f;   // na zebriku se do stran a dopredu pohybuje pomaleji

    static readonly List<Ladder> all = new List<Ladder>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() => all.Remove(this);

    // Je hrac (chodidla) na nekterem zebriku?
    public static bool At(Vector3 feet)
    {
        foreach (var ladder in all)
            if (ladder != null && ladder.Contains(feet))
                return true;
        return false;
    }

    bool Contains(Vector3 feet)
    {
        Vector3 local = transform.InverseTransformPoint(feet + Vector3.up * 0.3f);
        return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, size);
    }
}
