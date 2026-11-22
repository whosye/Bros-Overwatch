using System.Collections.Generic;
using UnityEngine;

// Tlacenice: jemne tyrkysove cary na zemi - dve podelne po krajich trasy Yarisu, pres trasu pricne cary
// na checkpointech obou tymu a na koncich trasy (zakladny). Dosazeny checkpoint se prebarvi barvou tymu,
// ktery k nemu dotlacil. Jen vizual (bez kolizi), stavi se za behu z PushRoute (prida ho PushRoute.OnEnable).
public class PushRouteMarks : MonoBehaviour
{
    static readonly Color Line = new Color(0.25f, 0.95f, 0.88f, 0.45f);
    static readonly Color Mark = new Color(0.25f, 0.95f, 0.88f, 0.85f);
    const float SideOffset = 1.5f;     // podelne cary od stredu trasy
    const float LineWidth = 0.07f;
    const float MarkWidth = 0.16f;
    const float MarkHalf = 2.6f;       // pricna cara: pul delky
    const float Lift = 0.05f;          // nad zemi (at neblika se zemi)
    const float Step = 0.5f;

    PushRoute route;
    Material material;
    readonly LineRenderer[] checkpointLines = new LineRenderer[8];
    readonly int[] checkpointTeam = new int[8];
    readonly int[] checkpointIndex = new int[8];
    int checkpointCount;
    float nextRefresh;
    bool built;

    void Start()
    {
        route = GetComponent<PushRoute>();
        if (route == null || route.Length < 1f) return;
        var shader = Shader.Find("Sprites/Default");
        material = shader != null ? new Material(shader) : null;

        var root = new GameObject("TrasaCary").transform;
        root.SetParent(transform, false);

        // podelne cary po krajich
        for (int side = -1; side <= 1; side += 2)
        {
            var points = new List<Vector3>();
            for (float s = 0f; s <= route.Length; s += Step)
                points.Add(Ground(route.Point(s) + route.Right(s) * (SideOffset * side)));
            points.Add(Ground(route.Point(route.Length) + route.Right(route.Length) * (SideOffset * side)));
            MakeLine(root, side < 0 ? "Levy kraj" : "Pravy kraj", points, LineWidth, Line);
        }

        // konce trasy (zakladny)
        MakeLine(root, "Konec T1", Across(0f), MarkWidth, Mark);
        MakeLine(root, "Konec T0", Across(route.Length), MarkWidth, Mark);

        // checkpointy obou tymu
        for (int team = 0; team < 2; team++)
            for (int i = 0; i < route.CheckpointCount && checkpointCount < checkpointLines.Length; i++)
            {
                float s = route.Checkpoint(team, i);
                if (s <= 0f || s >= route.Length) continue;
                checkpointLines[checkpointCount] = MakeLine(root, $"Checkpoint T{team} {i + 1}", Across(s), MarkWidth, Mark);
                checkpointTeam[checkpointCount] = team;
                checkpointIndex[checkpointCount] = i;
                checkpointCount++;
            }
        built = true;
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
    }

    void Update()
    {
        if (!built || Time.time < nextRefresh) return;
        nextRefresh = Time.time + 0.25f;
        var match = MatchManager.Instance;
        for (int k = 0; k < checkpointCount; k++)
        {
            bool reached = match != null && match.IsSpawned && match.PushCheckpointsOf(checkpointTeam[k]) > checkpointIndex[k];
            Color color = reached ? UiKit.TeamColor(checkpointTeam[k]) : Mark;
            color.a = Mark.a;
            var line = checkpointLines[k];
            if (line != null && line.startColor != color) line.startColor = line.endColor = color;
        }
    }

    List<Vector3> Across(float s)
    {
        var points = new List<Vector3>();
        Vector3 center = route.Point(s), right = route.Right(s);
        for (int i = 0; i <= 8; i++)
            points.Add(Ground(center + right * Mathf.Lerp(-MarkHalf, MarkHalf, i / 8f)));
        return points;
    }

    LineRenderer MakeLine(Transform parent, string name, List<Vector3> points, float width, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);   // cara lezi na zemi
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.TransformZ;
        line.positionCount = points.Count;
        line.SetPositions(points.ToArray());
        line.startWidth = line.endWidth = width;
        line.startColor = line.endColor = color;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        if (material != null) line.sharedMaterial = material;
        return line;
    }

    // Bod na zemi pod 'p' (ne na Yarisu, Honzovi ani hracich - ti se hybou).
    Vector3 Ground(Vector3 p)
    {
        var hits = Physics.RaycastAll(p + Vector3.up * 2.5f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 result = p;
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<PushRobot>() != null || hit.collider is CharacterController) continue;
            if (hit.distance < best)
            {
                best = hit.distance;
                result = hit.point;
            }
        }
        return result + Vector3.up * Lift;
    }
}
