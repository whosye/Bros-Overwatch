using UnityEngine;

// Presna mista oziveni dovezena s mapou. Zachova chodby pruchozi i pri hromadnem respawnu.
public sealed class MapSpawnPoints : MonoBehaviour
{
    public Transform[] team0 = new Transform[0];
    public Transform[] team1 = new Transform[0];
    public Bounds localBounds;
    static MapSpawnPoints active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetCache() => active = null;

    static MapSpawnPoints Active
    {
        get
        {
            if (active == null || !active.gameObject.activeInHierarchy)
                active = FindAnyObjectByType<MapSpawnPoints>();
            return active;
        }
    }

    public static bool TryGetBounds(out Bounds bounds)
    {
        var map = Active;
        bounds = map != null
            ? new Bounds(map.transform.TransformPoint(map.localBounds.center), map.localBounds.size)
            : default;
        return map != null;
    }

    public static bool TryGetSpawn(int team, ulong slot, out Vector3 position)
    {
        position = default;
        var map = Active;
        if (map == null || team < 0 || team > 1) return false;
        var markers = team == 0 ? map.team0 : map.team1;
        if (markers == null || markers.Length == 0) return false;
        int start = (int)(slot % (ulong)markers.Length);
        for (int offset = 0; offset < markers.Length; offset++)
        {
            var marker = markers[(start + offset) % markers.Length];
            if (marker == null) continue;
            var point = marker.position;
            if (!Physics.CheckCapsule(point + Vector3.up * 0.45f, point + Vector3.up * 1.65f,
                    0.38f, ~0, QueryTriggerInteraction.Ignore))
            {
                position = point;
                return true;
            }
        }
        // Pri docasne obsazenych spawnech se hrac alespon vrati do sve zakladny.
        var fallback = markers[start];
        if (fallback == null) return false;
        position = fallback.position;
        return true;
    }
}
