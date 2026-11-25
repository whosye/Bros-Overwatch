using UnityEditor;
using UnityEngine;

// Pomucka pro stavbu mapy: v okne Scene kresli obrys obdelniku u objektu CapturePoint_1..4
// (velikost podle Scale X/Z, nezmeneny Scale = vychozi velikost) a znacky mist oziveni SpawnPoint_Utok_X / SpawnPoint_Obrana_X.
[InitializeOnLoad]
public static class CapturePointGizmos
{
    static CapturePointGizmos()
    {
        SceneView.duringSceneGui += Draw;
    }

    static void Draw(SceneView view)
    {
        for (int i = 1; i <= MatchManager.MaxCapturePoints; i++)
        {
            var point = GameObject.Find($"CapturePoint_{i}");
            if (point != null)
            {
                Vector3 scale = point.transform.lossyScale;
                Vector2 size = scale.x > 1.01f || scale.z > 1.01f
                    ? new Vector2(Mathf.Clamp(scale.x, 2f, 60f), Mathf.Clamp(scale.z, 2f, 60f))
                    : MatchManager.CaptureSize;

                Handles.color = new Color(1f, 0.85f, 0.15f);
                Handles.matrix = Matrix4x4.TRS(point.transform.position, Quaternion.Euler(0f, point.transform.eulerAngles.y, 0f), Vector3.one);
                var area = point.GetComponent<CapturePointArea>();
                if (area != null && area.HasArea)
                {
                    foreach (var edge in area.Geometry.outline)
                    {
                        var a = new Vector3(edge.a.x, .05f, edge.a.y);
                        var b = new Vector3(edge.b.x, .05f, edge.b.y);
                        Handles.DrawLine(a, b);
                        Handles.DrawLine(a + Vector3.up * 2f, b + Vector3.up * 2f);
                        Handles.DrawLine(a, a + Vector3.up * 2f);
                        Handles.DrawLine(b, b + Vector3.up * 2f);
                    }
                }
                else Handles.DrawWireCube(new Vector3(0f, 1f, 0f), new Vector3(size.x, 2f, size.y));
                Handles.matrix = Matrix4x4.identity;
                Handles.Label(point.transform.position + Vector3.up * 2.4f, area != null && area.HasArea
                    ? $"Bod {i}  ({area.RegionCount} casti, {area.Geometry.area:0.#} m²)"
                    : $"Bod {i}  ({size.x:0.#} × {size.y:0.#} m)");
            }

            // mista oziveni v rezimu utok a obrana (SpawnPoint_Utok_X / SpawnPoint_Obrana_X), ktere hra opravdu pouziva
            string letter = MatchManager.PointNames[i - 1];
            foreach (var (prefix, label, color) in new[]
            {
                ("SpawnPoint_Utok_", "Spawn útoku", new Color(1f, 0.35f, 0.3f)),
                ("SpawnPoint_Obrana_", "Spawn obrany", new Color(0.3f, 0.64f, 1f)),
            })
            {
                var spawn = GameObject.Find(prefix + letter);
                if (spawn == null) continue;
                Handles.color = color;
                Handles.DrawWireDisc(spawn.transform.position, Vector3.up, 1.5f);
                Handles.Label(spawn.transform.position + Vector3.up * 2f, $"{label} (bod {letter})");
            }
        }
    }
}
