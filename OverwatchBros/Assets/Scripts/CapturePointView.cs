using UnityEngine;

// Vzhled dobyvaneho bodu ve svete (u kazdeho hrace zvlast, ridi se stavem v MatchManageru):
// barevne ohraniceny obdelnik na zemi, slabe vybarvena plocha a vysoky svetelny sloup, aby byl bod videt z dalky.
// Sedy = zamceny, bily = volny, barva tymu = zabira ho ten tym, zluta = sporny.
public static class CapturePointView
{
    static GameObject root;
    static Renderer[] border;
    static Renderer fill, beacon;
    static Material borderMaterial, fillMaterial, beaconMaterial;
    static Vector2 builtSize;
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

        if (builtSize != match.pointSize.Value)
            Resize(match.pointSize.Value);

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
        Object.DontDestroyOnLoad(root);

        borderMaterial = Fx.NewLit(Free);
        border = new[] { Bar(Vector3.zero, Vector3.one), Bar(Vector3.zero, Vector3.one), Bar(Vector3.zero, Vector3.one), Bar(Vector3.zero, Vector3.one) };

        fillMaterial = new Material(Fx.ParticleMaterial) { mainTexture = null };
        fill = Shape(PrimitiveType.Cube, new Vector3(0f, 0.01f, 0f), Vector3.one, fillMaterial);
        Resize(MatchManager.CaptureSize);

        beaconMaterial = new Material(Fx.ParticleMaterial) { mainTexture = null };
        beacon = Shape(PrimitiveType.Cylinder, new Vector3(0f, 20f, 0f), new Vector3(0.35f, 20f, 0.35f), beaconMaterial);
    }

    // Kazdy bod muze mit jinou velikost obdelniku.
    static void Resize(Vector2 size)
    {
        builtSize = size;
        float width = size.x, length = size.y;

        Place(border[0], new Vector3(0f, 0.06f, length * 0.5f), new Vector3(width + Thickness, 0.12f, Thickness));
        Place(border[1], new Vector3(0f, 0.06f, -length * 0.5f), new Vector3(width + Thickness, 0.12f, Thickness));
        Place(border[2], new Vector3(width * 0.5f, 0.06f, 0f), new Vector3(Thickness, 0.12f, length + Thickness));
        Place(border[3], new Vector3(-width * 0.5f, 0.06f, 0f), new Vector3(Thickness, 0.12f, length + Thickness));
        Place(fill, new Vector3(0f, 0.01f, 0f), new Vector3(width, 0.01f, length));
    }

    static void Place(Renderer renderer, Vector3 position, Vector3 scale)
    {
        renderer.transform.localPosition = position;
        renderer.transform.localScale = scale;
    }

    static Renderer Bar(Vector3 position, Vector3 scale)
    {
        return Shape(PrimitiveType.Cube, position, scale, borderMaterial);
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
