using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tajna odbocka v tunelu jako ponura jeskyne: steny a strop skalni (tmava hornina), podlaha hlinita, po stenach a stropu
// nepravidelne kameny (vlastni lowpoly site, kolize presne podle tvaru), par slabych mihotavych ohnu se stiny,
// zavaly pres celou chodbu (dole volno jen 1,35 m - projde se jen v drepu, Ctrl; nahore az ke stropu, preskocit
// nejdou) a hudba mista
// (Audio/Mapa/Jeskyne_OneRing.mp3, hraje jen dole v jeskyni). Opravi i zebrik pod poklopem (oblast byla moc uzka
// - hrac se k ni nedostal) a nastavi ho tak, aby chytil jen toho, kdo se k nemu diva a jde dopredu (kdo propadne
// poklopem, spadne az dolu). Jen jednou (kdyz uz "Jeskyne" existuje, kameny a materialy nemeni). Ctrl+Z vrati.
public static class CaveStyleTool
{
    const string RootPath = "Map-Domasov/Tunel/TajnaOdbocka";
    const string MaterialDir = "Assets/Materials/Jeskyne";
    const string MeshDir = "Assets/Models/Jeskyne";
    const string MusicPath = "Assets/Audio/Mapa/Jeskyne_OneRing.mp3";
    const int RockVariants = 6;

    static Transform rocksRoot;
    static Mesh[] rocks;
    static Material rockMaterial;

    [MenuItem("BrosOverwatch/Mapa/Tajna odbocka - jeskyne")]
    internal static void Run()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Jeskyne] Nejdriv vypni hru."); return; }
        var root = GameObject.Find(RootPath);
        if (root == null) { Debug.LogWarning("[Jeskyne] Nenasel jsem " + RootPath + " - nejdriv postav tajnou odbocku."); return; }

        Undo.SetCurrentGroupName("Jeskyne");
        int group = Undo.GetCurrentGroup();

        bool ladderFixed = FixLadder(root.transform);
        bool musicAdded = AddMusic(root);

        if (root.transform.Find("Jeskyne") != null)
        {
            Undo.CollapseUndoOperations(group);
            if (ladderFixed || musicAdded) EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[Jeskyne] Kameny uz jsou - nemenim je.{(ladderFixed ? " Zebrik opraven." : "")}{(musicAdded ? " Hudba pridana." : "")}", root);
            return;
        }

        var skala = EnsureMaterial("Jeskyne_Skala", new Color(0.30f, 0.26f, 0.22f), 0.12f, Color.black);
        var zem = EnsureMaterial("Jeskyne_Zem", new Color(0.27f, 0.21f, 0.15f), 0.05f, Color.black);
        var ohen = EnsureMaterial("Jeskyne_Ohen", new Color(1f, 0.55f, 0.2f), 0.3f, new Color(1f, 0.45f, 0.12f) * 2.5f);
        rockMaterial = EnsureMaterial("Jeskyne_Kamen", new Color(0.36f, 0.32f, 0.28f), 0.15f, Color.black);
        rocks = EnsureRockMeshes();

        // materialy sten / stropu / podlah (poklop zustava jako podlaha hlavni chodby)
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            string n = r.gameObject.name;
            bool part = n.StartsWith("A_") || n.StartsWith("B_") || n.StartsWith("C_") || n.StartsWith("D_") || n.StartsWith("E_")
                || n.StartsWith("F_") || n.StartsWith("Mistnost_") || n.StartsWith("Svatyne_") || n.StartsWith("Sachta_");
            if (!part) continue;
            Undo.RecordObject(r, "Jeskyne");
            r.sharedMaterial = n.Contains("Podlaha") || n.Contains("Rampa") ? zem : skala;
        }

        // svetla: slabe mihotave ohne se stiny (ponure - mezi nimi tma)
        foreach (var light in root.GetComponentsInChildren<Light>(true))
        {
            Undo.RecordObject(light, "Jeskyne");
            light.color = new Color(1f, 0.46f, 0.2f);
            light.intensity *= 0.7f;
            light.range *= 0.85f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.9f;
            if (light.GetComponent<LightFlicker>() == null)
            {
                var flicker = Undo.AddComponent<LightFlicker>(light.gameObject);
                flicker.amount = 0.4f;
            }
            foreach (var r in light.GetComponentsInChildren<MeshRenderer>(true))
            {
                Undo.RecordObject(r, "Jeskyne");
                r.sharedMaterial = ohen;
            }
        }

        var cave = new GameObject("Jeskyne");
        Undo.RegisterCreatedObjectUndo(cave, "Jeskyne");
        cave.transform.SetParent(root.transform, false);
        rocksRoot = cave.transform;

        Random.InitState(1717);
        // zavaly (podlezt v drepu): A, dole v B za rampou, C - mimo svetla a zebrik
        Collapse(root.transform, skala, true, 5.0f, 21.8f, 23.8f, -8f, -5.3f);
        Collapse(root.transform, skala, false, 38.3f, 14.0f, 16.0f, -11f, -5.3f);
        Collapse(root.transform, skala, true, 6.6f, 42.0f, 44.0f, -11f, -8f);
        bool extended = root.transform.Find("D_Podlaha") != null;   // prodlouzeni (SecretTunnelTool - prodlouzit)
        if (extended)
        {
            Collapse(root.transform, skala, true, -6.0f, 42.0f, 44.0f, -11f, -8f);
            Collapse(root.transform, skala, true, -10.5f, 60.0f, 62.0f, -12.5f, -9f);
        }

        Physics.SyncTransforms();
        int count = 0;
        // osy chodeb (podle SecretTunnelTool): A na vychod, B na sever s rampou, C na zapad, mistnost
        count += Line(root.transform, new Vector3(2.6f, 0f, 22.8f), new Vector3(15f, 0f, 22.8f), z => -8f);
        count += Line(root.transform, new Vector3(15f, 0f, 22.8f), new Vector3(15f, 0f, 43.2f), FloorB);
        count += Line(root.transform, new Vector3(15f, 0f, 43f), new Vector3(4.4f, 0f, 43f), z => -11f);
        count += Line(root.transform, new Vector3(3.6f, 0f, 43f), new Vector3(0.6f, 0f, 43f), z => -11f);
        if (extended)
        {
            count += Line(root.transform, new Vector3(-0.6f, 0f, 43f), new Vector3(-17f, 0f, 43f), z => -11f);
            count += Line(root.transform, new Vector3(-17f, 0f, 43f), new Vector3(-17f, 0f, 61.5f), FloorE);
            count += Line(root.transform, new Vector3(-17f, 0f, 61f), new Vector3(-6.4f, 0f, 61f), z => -12.5f);
            count += Line(root.transform, new Vector3(-5.6f, 0f, 61f), new Vector3(-1.4f, 0f, 61f), z => -12.5f);
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"[Jeskyne] Hotovo: {count} kamenu, {(extended ? 5 : 3)} zavalu (podlezt s Ctrl), skalni materialy, ohne.{(ladderFixed ? " Zebrik opraven." : "")}"
            + $"{(musicAdded ? " Hudba pridana." : "")} Ulozit: Ctrl+S.", cave);
    }

    static float FloorE(float z) => z < 48f ? -11f : z > 56f ? -12.5f : Mathf.Lerp(-11f, -12.5f, (z - 48f) / 8f);

    static float FloorB(float z) => z < 26f ? -8f : z > 36f ? -11f : Mathf.Lerp(-8f, -11f, (z - 26f) / 10f);

    // ---------------- zebrik ----------------

    static bool FixLadder(Transform root)
    {
        var ladder = root.GetComponentInChildren<Ladder>(true);
        if (ladder == null || ladder.grabFacing.sqrMagnitude > 0.01f) return false;
        var t = ladder.transform;
        // oblast od steny 1 m do sachty (hrac ma polomer 0,5 - k uzsi oblasti se nedostal) a vys nad podlahu chodby
        Vector3 oldPos = t.position;
        float bottom = oldPos.y - ladder.size.y * 0.5f;
        float top = -3.0f;
        Vector3 newPos = new Vector3(0.7f, (bottom + top) * 0.5f, oldPos.z);
        Undo.RecordObject(t, "Jeskyne");
        Undo.RecordObject(ladder, "Jeskyne");
        // deti (madla, pricky) zustanou na miste
        var children = new List<Transform>();
        foreach (Transform c in t) children.Add(c);
        var keep = new List<Vector3>();
        foreach (var c in children) keep.Add(c.position);
        t.position = newPos;
        for (int i = 0; i < children.Count; i++)
        {
            Undo.RecordObject(children[i], "Jeskyne");
            children[i].position = keep[i];
        }
        ladder.size = new Vector3(1.0f, top - bottom, 1.2f);
        ladder.grabFacing = Vector3.left;   // madla jsou na zapadni stene
        return true;
    }

    // ---------------- hudba ----------------

    static bool AddMusic(GameObject root)
    {
        if (root.GetComponent<ZoneMusic>() != null) return false;
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
        if (clip == null) { Debug.LogWarning("[Jeskyne] Nenasel jsem " + MusicPath + " (nenaimportovany?) - hudba nepridana."); return false; }
        var music = Undo.AddComponent<ZoneMusic>(root);
        music.clip = clip;
        return true;
    }

    // ---------------- materialy a kameny ----------------

    static Material EnsureMaterial(string name, Color color, float smoothness, Color emission)
    {
        string path = $"{MaterialDir}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder("Assets/Materials", "Jeskyne");
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader) { name = name };
        m.SetColor("_BaseColor", color);
        m.color = color;
        m.SetFloat("_Smoothness", smoothness);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // Lowpoly kameny: ikosaedr jednou zjemneny (80 trojuhelniku - jde jako konvexni kolize), vrcholy nahodne
    // posunute, ostre hrany (kazdy trojuhelnik vlastni vrcholy). Polomer ~0,5.
    static Mesh[] EnsureRockMeshes()
    {
        var meshes = new Mesh[RockVariants];
        for (int i = 0; i < RockVariants; i++)
        {
            string path = $"{MeshDir}/Kamen_{i + 1}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { meshes[i] = existing; continue; }
            if (!AssetDatabase.IsValidFolder("Assets/Models")) AssetDatabase.CreateFolder("Assets", "Models");
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Models", "Jeskyne");
            var mesh = RockMesh(500 + i * 37);
            mesh.name = $"Kamen_{i + 1}";
            AssetDatabase.CreateAsset(mesh, path);
            meshes[i] = mesh;
        }
        AssetDatabase.SaveAssets();
        return meshes;
    }

    static Mesh RockMesh(int seed)
    {
        var state = Random.state;
        Random.InitState(seed);
        float g = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var v = new List<Vector3>
        {
            new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
            new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
            new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
        };
        int[] f =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };
        // jedno zjemneni
        var mid = new Dictionary<long, int>();
        int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (mid.TryGetValue(key, out int idx)) return idx;
            v.Add((v[a] + v[b]) * 0.5f);
            mid[key] = v.Count - 1;
            return v.Count - 1;
        }
        var tris = new List<int>();
        for (int i = 0; i < f.Length; i += 3)
        {
            int a = f[i], b = f[i + 1], c = f[i + 2];
            int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        // nepravidelny tvar: kazdy vrchol jinak daleko, cely kamen trochu zplosteny
        Vector3 squash = new Vector3(Random.Range(0.85f, 1.15f), Random.Range(0.7f, 1f), Random.Range(0.85f, 1.15f));
        for (int i = 0; i < v.Count; i++)
            v[i] = Vector3.Scale(v[i].normalized * 0.5f * Random.Range(0.78f, 1.08f), squash);

        var verts = new List<Vector3>();
        var outTris = new List<int>();
        for (int i = 0; i < tris.Count; i++) { verts.Add(v[tris[i]]); outTris.Add(i); }
        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetTriangles(outTris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Random.state = state;
        return mesh;
    }

    // ---------------- rozmisteni ----------------

    // Po ose chodby: do stran (3 vysky) hledat stenu a do ni zapustit kamen (vycniva nejvys ~0,28 m),
    // nahoru hledat strop a zavesit krapnik (visi nejvys ~0,4 m).
    static int Line(Transform root, Vector3 from, Vector3 to, System.Func<float, float> floorAtZ)
    {
        int placed = 0;
        Vector3 dir = to - from; dir.y = 0f;
        float length = dir.magnitude;
        dir.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        for (float s = 0f; s <= length; s += 0.8f)
        {
            Vector3 p = from + dir * s;
            float floor = floorAtZ(p.z);
            foreach (float sign in new[] { -1f, 1f })
                foreach (float h in new[] { 0.25f, 1.1f, 2.0f })
                {
                    if (Random.value > 0.62f) continue;
                    Vector3 origin = new Vector3(p.x, floor + h, p.z) + dir * Random.Range(-0.35f, 0.35f);
                    if (!Cast(root, origin, side * sign, 4f, out var hit)) continue;
                    float scale = Random.Range(0.55f, 1.05f) * (h < 0.5f ? 0.85f : 1f);
                    Vector3 axes = new Vector3(Random.Range(0.75f, 1.3f), Random.Range(0.75f, 1.3f), Random.Range(0.75f, 1.3f)) * scale;
                    float radius = 0.5f * Mathf.Max(axes.x, Mathf.Max(axes.y, axes.z));
                    float protrude = Random.Range(0.08f, 0.28f);
                    Rock(hit.point - hit.normal * (radius - protrude), Random.rotation, axes);
                    placed++;
                }

            // krapniky ze stropu
            if (Random.value < 0.55f)
            {
                Vector3 origin = new Vector3(p.x, floor + 1.0f, p.z) + side * Random.Range(-0.6f, 0.6f);
                if (Cast(root, origin, Vector3.up, 6f, out var up) && !up.collider.name.StartsWith("Zaval"))
                {
                    float w = Random.Range(0.35f, 0.6f), len = Random.Range(0.8f, 1.4f);
                    var rotation = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 360f), Random.Range(-8f, 8f));
                    float hang = Random.Range(0.18f, 0.4f);
                    Rock(up.point + Vector3.up * (0.5f * len - hang), rotation, new Vector3(w, len, w));
                    placed++;
                }
            }
        }
        return placed;
    }

    // Zaval pres celou sirku chodby: blok od floor+1,35 do stropu, delka 1,2 m, a na jeho celech zapustene kameny
    // (jen nad volnym mistem, at nesnizuji pruchod). acrossZ = chodba vede podel X (zaval je napric v Z).
    const float CrawlClearance = 1.35f;

    static void Collapse(Transform root, Material material, bool alongX, float at, float side0, float side1, float floor, float ceiling)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(go, "Jeskyne");
        go.name = "Zaval";
        go.transform.SetParent(rocksRoot, false);
        float y0 = floor + CrawlClearance;
        float depth = 1.2f;
        go.transform.position = alongX ? new Vector3(at, (y0 + ceiling) * 0.5f, (side0 + side1) * 0.5f)
                                       : new Vector3((side0 + side1) * 0.5f, (y0 + ceiling) * 0.5f, at);
        go.transform.localScale = alongX ? new Vector3(depth, ceiling - y0, side1 - side0) : new Vector3(side1 - side0, ceiling - y0, depth);
        go.GetComponent<Renderer>().sharedMaterial = material;
        go.isStatic = true;

        // kameny na obou celech zavalu (stred aspon 0,55 m nad spodkem - kamen spodek nepresahne)
        foreach (float face in new[] { -1f, 1f })
            for (int i = 0; i < 4; i++)
            {
                float scale = Random.Range(0.5f, 0.85f);
                float y = Random.Range(y0 + 0.55f, Mathf.Min(ceiling - 0.2f, y0 + 1.6f));
                float s = Random.Range(side0 + 0.3f, side1 - 0.3f);
                float d = at + face * (depth * 0.5f + 0.12f);
                Vector3 p = alongX ? new Vector3(d, y, s) : new Vector3(s, y, d);
                Rock(p, Random.rotation, Vector3.one * scale);
            }
        // spodni hrana: par plochych kamenu prilepenych tesne pod blokem by snizily pruchod - zamerne zadne
    }

    static bool Cast(Transform root, Vector3 origin, Vector3 direction, float distance, out RaycastHit result)
    {
        result = default;
        var hits = Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        foreach (var hit in hits)
        {
            // jen steny jeskyne (ne uz polozene kameny, ne zbytek mapy)
            if (!hit.collider.transform.IsChildOf(root) || hit.collider.transform.IsChildOf(rocksRoot)) continue;
            if (hit.distance < best) { best = hit.distance; result = hit; found = true; }
        }
        return found;
    }

    static void Rock(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var go = new GameObject("Kamen");
        Undo.RegisterCreatedObjectUndo(go, "Jeskyne");
        go.transform.SetParent(rocksRoot, false);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        var mesh = rocks[Random.Range(0, rocks.Length)];
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = rockMaterial;
        var collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = true;
        go.isStatic = true;
    }
}
