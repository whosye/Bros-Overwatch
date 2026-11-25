using UnityEngine;

// Vzhled dobyvaneho bodu ve svete (u kazdeho hrace zvlast, ridi se stavem v MatchManageru):
// barevne ohraniceny pudorys na zemi, slabe vybarvena plocha a vysoky svetelny sloup.
// Sedy = zamceny, bily = volny, barva tymu = zabira ho ten tym, zluta = sporny.
public static class CapturePointView
{
    static GameObject root;
    static GameObject surface;
    static Renderer beacon;
    static Mesh fillMesh;
    static Material borderMaterial, fillMaterial, beaconMaterial;
    static Vector2 builtSize;
    static CapturePointArea builtArea;
    static int builtRevision;
    const float Thickness = 0.16f;

    public static readonly Color Locked = new Color(0.55f, 0.57f, 0.62f);
    public static readonly Color Free = new Color(0.95f, 0.96f, 1f);
    public static readonly Color Contested = new Color(1f, 0.82f, 0.15f);

    public static Color StateColor(int state)
    {
        switch (state)
        {
            case MatchManager.StateFree: return Free;
            case MatchManager.StateTeam0: return UiKit.Team0;
            case MatchManager.StateTeam1: return UiKit.Team1;
            case MatchManager.StateContested: return Contested;
            default: return Locked;
        }
    }

    public static void Sync(MatchManager match)
    {
        bool show = match != null && match.IsSpawned && match.IsAttackMode && !match.IsLobby && !match.IsOver
            && match.roundPhase.Value != MatchManager.RoundIntermission;
        if (!show)
        {
            if (root != null && root.activeSelf)
                root.SetActive(false);
            return;
        }

        if (root == null)
            Build();
        if (!root.activeSelf)
            root.SetActive(true);

        var area = match.CurrentCaptureArea;
        int revision = area != null ? area.Revision : 0;
        if (builtSize != match.pointSize.Value || !ReferenceEquals(builtArea, area) || builtRevision != revision)
            Resize(match.pointSize.Value, area);

        root.transform.SetPositionAndRotation(match.pointPosition.Value + Vector3.up * 0.03f, Quaternion.Euler(0f, match.pointYaw.Value, 0f));

        int state = match.pointState.Value;
        Color color = StateColor(state);

        // Sporny bod blika.
        if (state == MatchManager.StateContested)
            color = Color.Lerp(color, Color.white, Mathf.PingPong(Time.time * 3f, 0.5f));

        borderMaterial.color = color;
        fillMaterial.color = new Color(color.r, color.g, color.b, state == MatchManager.StateLocked ? 0.06f : 0.14f);
        beaconMaterial.color = new Color(color.r, color.g, color.b, 0.22f);
    }

    static void Build()
    {
        root = new GameObject("CapturePoint");
        if (Application.isPlaying) Object.DontDestroyOnLoad(root);

        borderMaterial = Fx.NewLit(Free);

        fillMaterial = new Material(Fx.ParticleMaterial) { mainTexture = null };
        Resize(MatchManager.CaptureSize, null);

        beaconMaterial = new Material(Fx.ParticleMaterial) { mainTexture = null };
        beacon = Shape(PrimitiveType.Cylinder, new Vector3(0f, 20f, 0f), new Vector3(0.35f, 20f, 0.35f), beaconMaterial);
    }

    // Composite and legacy points use one union geometry for fill and border.
    static void Resize(Vector2 size, CapturePointArea area)
    {
        builtSize = size;
        builtArea = area;
        builtRevision = area != null ? area.Revision : 0;
        if (surface != null)
        {
            surface.SetActive(false);
            Release(surface);
        }
        if (fillMesh != null) Release(fillMesh);
        surface = new GameObject("Surface");
        surface.transform.SetParent(root.transform, false);
        var geometry = area != null ? area.Geometry : new CaptureAreaGeometry(new[] {
            new Rect(-size.x * .5f, -size.y * .5f, size.x, size.y)
        });
        foreach (var edge in geometry.outline)
        {
            Vector2 mid = (edge.a + edge.b) * .5f;
            bool alongX = Mathf.Abs(edge.a.y - edge.b.y) < .0001f;
            float length = Vector2.Distance(edge.a, edge.b);
            var bar = Shape(PrimitiveType.Cube, new Vector3(mid.x, .06f, mid.y),
                alongX ? new Vector3(length + Thickness, .12f, Thickness) : new Vector3(Thickness, .12f, length + Thickness), borderMaterial);
            bar.transform.SetParent(surface.transform, false);
        }

        // Non-overlapping cells prevent darker overlapping transparent patches.
        var vertices = new Vector3[geometry.cells.Length * 4];
        var triangles = new int[geometry.cells.Length * 6];
        for (int i = 0; i < geometry.cells.Length; i++)
        {
            var cell = geometry.cells[i]; int v = i * 4, t = i * 6;
            vertices[v] = new Vector3(cell.xMin, .01f, cell.yMin);
            vertices[v + 1] = new Vector3(cell.xMax, .01f, cell.yMin);
            vertices[v + 2] = new Vector3(cell.xMax, .01f, cell.yMax);
            vertices[v + 3] = new Vector3(cell.xMin, .01f, cell.yMax);
            triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
            triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
        }
        fillMesh = new Mesh { name = "CapturePointFootprint" };
        if (vertices.Length > 65535) fillMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        fillMesh.vertices = vertices;
        fillMesh.triangles = triangles;
        fillMesh.RecalculateNormals();
        fillMesh.RecalculateBounds();
        var fillObject = new GameObject("Fill", typeof(MeshFilter), typeof(MeshRenderer));
        fillObject.transform.SetParent(surface.transform, false);
        fillObject.GetComponent<MeshFilter>().sharedMesh = fillMesh;
        var renderer = fillObject.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = fillMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void Release(Object value)
    {
        if (Application.isPlaying) Object.Destroy(value);
        else Object.DestroyImmediate(value);
    }

    static Renderer Shape(PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;

        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return renderer;
    }
}
