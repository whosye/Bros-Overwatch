using UnityEngine;

// Zakladna tymu: kruh kolem mista oziveni. Jen v nem jde behem zapasu menit hrdinu (F1).
// Na zemi ji u kazdeho hrace oznacuje kruh v barve tymu. V rezimu dobyvani bodu se zakladny stehuji k aktualnimu bodu.
public static class SpawnZone
{
    public const float Radius = 8f;
    const int Segments = 64;

    static readonly GameObject[] rings = new GameObject[PlayerTeam.TeamCount];
    static readonly Transform[] sceneSpawns = new Transform[PlayerTeam.TeamCount];

    // Stred zakladny tymu (misto oziveni).
    public static bool TryGetCenter(int team, out Vector3 center)
    {
        center = Vector3.zero;
        if (team < 0 || team >= PlayerTeam.TeamCount) return false;

        var match = MatchManager.Instance;
        if (match != null && match.TryGetSpawn(team, out center)) return true;

        if (sceneSpawns[team] == null)
        {
            var marker = GameObject.Find($"SpawnPoint_Team{team}");
            if (marker != null)
                sceneSpawns[team] = marker.transform;
        }

        if (sceneSpawns[team] == null) return false;

        center = sceneSpawns[team].position;
        return true;
    }

    public static bool Contains(int team, Vector3 position)
    {
        if (!TryGetCenter(team, out Vector3 center)) return false;

        Vector3 offset = position - center;
        float height = offset.y;
        offset.y = 0f;
        return offset.magnitude <= Radius && height > -3f && height < 6f;
    }

    // Vola MatchManager kazdy snimek: kruhy zakladen na zemi.
    public static void Sync(MatchManager match)
    {
        bool show = match != null && match.IsSpawned;
        for (int team = 0; team < PlayerTeam.TeamCount; team++)
        {
            bool visible = show && TryGetCenter(team, out Vector3 center);
            if (!visible)
            {
                if (rings[team] != null && rings[team].activeSelf)
                    rings[team].SetActive(false);
                continue;
            }

            if (rings[team] == null)
                rings[team] = BuildRing(team);
            if (!rings[team].activeSelf)
                rings[team].SetActive(true);

            TryGetCenter(team, out center);
            rings[team].transform.position = center + Vector3.up * 0.06f;
        }
    }

    static GameObject BuildRing(int team)
    {
        var go = new GameObject($"SpawnZone_Team{team}");
        Object.DontDestroyOnLoad(go);

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = Segments;
        line.startWidth = line.endWidth = 0.18f;
        line.alignment = LineAlignment.TransformZ;
        line.material = Fx.NewLit(UiKit.TeamColor(team));
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Kruh lezi naplocho na zemi.
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        for (int i = 0; i < Segments; i++)
        {
            float angle = i * Mathf.PI * 2f / Segments;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * Radius, Mathf.Sin(angle) * Radius, 0f));
        }

        return go;
    }
}
