using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// Blikajici plochy (z-fighting): dva kvadry maji stenu presne ve stejne rovine (napr. vrsek zdi z prizemi
// zarovnany s podlahou patra) - grafika nevi, ktera je vpredu, a plocha blika.
// "Najit": projde kvadry ve scene (sit s 24/48 vrcholy, otocene jen kolem svisle osy), najde dvojice s plochou
// ve stejne rovine (do 3 mm) natocenou stejnym smerem a prekryvajici se; vybere ten mensi z kazde dvojice.
// "Opravit": vybrane posune o 1 cm dovnitr (pod/za vetsi plochu), takze uz se nekryji. Ctrl+Z vrati.
public static class ZFightFixer
{
    const float Same = 0.003f;
    const float MinOverlap = 0.0025f;   // 5 x 5 cm
    const float Shift = 0.01f;

    class Box
    {
        public Transform t;
        public Bounds b;        // v souradnicich skupiny (otoceni o yaw mod 90)
        public int group;
    }

    static readonly Dictionary<Transform, Vector3> planned = new Dictionary<Transform, Vector3>();

    [MenuItem("BrosOverwatch/Opravy/Blikajici plochy - 1. najit (vybere je)")]
    static void Find()
    {
        planned.Clear();
        var boxes = new List<Box>();
        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || (mesh.vertexCount != 24 && mesh.vertexCount != 48)) continue;
            var t = renderer.transform;
            if (Vector3.Dot(t.up, Vector3.up) < 0.9999f) continue;   // jen otocene kolem svisle osy
            float yaw = t.eulerAngles.y;
            float groupYaw = Mathf.Repeat(yaw, 90f);
            int group = Mathf.RoundToInt(groupYaw * 10f) % 900;
            var frame = Quaternion.Inverse(Quaternion.Euler(0f, group / 10f, 0f));
            Bounds mb = mesh.bounds;
            Bounds b = default;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = frame * t.TransformPoint(corner);
                if (i == 0) b = new Bounds(p, Vector3.zero); else b.Encapsulate(p);
            }
            boxes.Add(new Box { t = t, b = b, group = group });
        }

        // trideni podle skupiny a x, at se neporovnava kazdy s kazdym
        boxes.Sort((a, c) => a.group != c.group ? a.group.CompareTo(c.group) : a.b.min.x.CompareTo(c.b.min.x));
        var report = new StringBuilder();
        int pairs = 0;
        for (int i = 0; i < boxes.Count; i++)
        {
            var a = boxes[i];
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var c = boxes[j];
                if (c.group != a.group || c.b.min.x > a.b.max.x + Same) break;
                for (int axis = 0; axis < 3; axis++)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float fa = side > 0 ? a.b.max[axis] : a.b.min[axis];
                        float fc = side > 0 ? c.b.max[axis] : c.b.min[axis];
                        if (Mathf.Abs(fa - fc) > Same) continue;
                        int u = (axis + 1) % 3, v = (axis + 2) % 3;
                        float ou = Mathf.Min(a.b.max[u], c.b.max[u]) - Mathf.Max(a.b.min[u], c.b.min[u]);
                        float ov = Mathf.Min(a.b.max[v], c.b.max[v]) - Mathf.Max(a.b.min[v], c.b.min[v]);
                        if (ou <= 0f || ov <= 0f || ou * ov < MinOverlap) continue;
                        // posune se ten s mensi plochou na teto strane
                        float areaA = a.b.size[u] * a.b.size[v], areaC = c.b.size[u] * c.b.size[v];
                        var moved = areaA < areaC ? a : c;
                        var other = moved == a ? c : a;
                        Vector3 dir = Vector3.zero;
                        dir[axis] = -side;
                        Vector3 world = Quaternion.Euler(0f, moved.group / 10f, 0f) * dir * Shift;
                        planned.TryGetValue(moved.t, out var sum);
                        if (Vector3.Dot(sum, world) > 0f) continue;   // stejnym smerem uz je naplanovany
                        planned[moved.t] = sum + world;
                        pairs++;
                        string face = axis == 1 ? (side > 0 ? "vrsek" : "spodek") : "bok";
                        report.AppendLine($"{PathOf(moved.t)}  ({face} v rovine s {PathOf(other.t)})");
                    }
            }
        }

        var selection = new List<Object>();
        foreach (var t in planned.Keys) selection.Add(t.gameObject);
        Selection.objects = selection.ToArray();
        Debug.Log(pairs == 0
            ? "[Blikani] Zadne kvadry s plochou ve stejne rovine."
            : $"[Blikani] Nalezeno {pairs} prekryvu, k posunu {planned.Count} objektu (vybrane v Hierarchy):\n{report}\n" +
              "Oprava: BrosOverwatch > Opravy > Blikajici plochy - 2. opravit vybrane.");
    }

    [MenuItem("BrosOverwatch/Opravy/Blikajici plochy - 2. opravit vybrane (posun o 1 cm)")]
    static void Fix()
    {
        if (planned.Count == 0) { Debug.LogWarning("[Blikani] Nejdriv spust 1. najit."); return; }
        var selected = new HashSet<GameObject>(Selection.gameObjects);
        int count = 0;
        foreach (var pair in planned)
        {
            if (pair.Key == null || !selected.Contains(pair.Key.gameObject)) continue;   // co uzivatel odznacil, zustane
            Undo.RecordObject(pair.Key, "Oprava blikani");
            pair.Key.position += pair.Value;
            count++;
        }
        planned.Clear();
        if (count > 0) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[Blikani] Posunuto {count} objektu o 1 cm. Zkontroluj a uloz scenu (Ctrl+S); Ctrl+Z vrati.");
    }

    static string PathOf(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}
