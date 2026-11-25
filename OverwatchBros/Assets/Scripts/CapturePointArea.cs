using System.Collections.Generic;
using UnityEngine;

// Optional composite footprint. Coordinates are meters relative to the marker's
// position and yaw, independent of its old rectangle Scale. Scene data is shared
// by host and clients; the usual network point position/yaw moves the whole area.
[DisallowMultipleComponent]
public sealed class CapturePointArea : MonoBehaviour
{
    [SerializeField, HideInInspector] Rect[] regions = new Rect[0];
    CaptureAreaGeometry geometry;
    public int Revision { get; private set; }
    public int RegionCount => regions != null ? regions.Length : 0;
    public CaptureAreaGeometry Geometry => geometry ?? (geometry = new CaptureAreaGeometry(regions));
    public bool HasArea => enabled && !Geometry.IsEmpty;

    static readonly List<CapturePointArea> active = new List<CapturePointArea>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => active.Clear();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void RegisterLoadedAreas()
    {
        foreach (var area in FindObjectsByType<CapturePointArea>(FindObjectsInactive.Exclude))
            if (!active.Contains(area)) active.Add(area);
    }

    void OnEnable() { if (!active.Contains(this)) active.Add(this); }
    void OnDisable() => active.Remove(this);
    void OnValidate() { geometry = null; Revision++; }

    public void SetRegions(Rect[] value)
    {
        regions = value != null ? (Rect[])value.Clone() : new Rect[0];
        geometry = null;
        Revision++;
    }

    public static CapturePointArea FindActive(int number)
    {
        string markerName = "CapturePoint_" + number;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var candidate = active[i];
            if (candidate && candidate.isActiveAndEnabled && candidate.gameObject.activeInHierarchy
                && candidate.name == markerName && candidate.HasArea)
                return candidate;
        }
        return null;
    }
}
