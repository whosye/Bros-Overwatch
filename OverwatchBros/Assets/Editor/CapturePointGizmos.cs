using UnityEditor;
using UnityEngine;

// Pomucka pro stavbu mapy: v okne Scene kresli obrys obdelniku u objektu CapturePoint_1..3
// (velikost podle Scale X/Z, nezmeneny Scale = vychozi velikost) a znacky mist oziveni SpawnPoint_N_TeamT.
[InitializeOnLoad]
public static class CapturePointGizmos
{
    static CapturePointGizmos()
    {
        SceneView.duringSceneGui += Draw;
    }

    static void Draw(SceneView view)
    {
        for (int i = 1; i <= MatchManager.CapturePointCount; i++)
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
                Handles.DrawWireCube(new Vector3(0f, 1f, 0f), new Vector3(size.x, 2f, size.y));
                Handles.matrix = Matrix4x4.identity;
                Handles.Label(point.transform.position + Vector3.up * 2.4f, $"Bod {i}  ({size.x:0.#} × {size.y:0.#} m)");
            }

            for (int team = 0; team < 2; team++)
            {
                var spawn = GameObject.Find($"SpawnPoint_{i}_Team{team}");
                if (spawn == null) continue;

                Handles.color = team == 0 ? new Color(0.3f, 0.64f, 1f) : new Color(1f, 0.35f, 0.3f);
                Handles.DrawWireDisc(spawn.transform.position, Vector3.up, 1.5f);
                Handles.Label(spawn.transform.position + Vector3.up * 2f, $"Oživení bodu {i}, tým {team}");
            }
        }
    }
}
