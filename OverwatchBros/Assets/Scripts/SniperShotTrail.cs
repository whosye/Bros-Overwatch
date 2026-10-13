using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

// Cosmetic world-space afterimage. Its endpoints stay at the shot, never follow the shooter.
public sealed class SniperShotTrail : MonoBehaviour
{
    public const float Lifetime = 2.1f;
    public static readonly Color FriendlyColor = new Color(0.84f, 0.87f, 0.9f);
    public static readonly Color EnemyColor = new Color(1f, 0.12f, 0.09f);

    static Material ribbonMaterial;
    static Texture2D ribbonTexture;
    LineRenderer core, smoke;
    Color tint;
    float age, smokeWidth;
    bool replay;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetResources()
    {
        if (ribbonMaterial) Destroy(ribbonMaterial);
        if (ribbonTexture) Destroy(ribbonTexture);
        ribbonMaterial = null;
        ribbonTexture = null;
    }

    public static Color ColorForTeams(int shooterTeam, int viewerTeam)
    {
        return shooterTeam >= 0 && viewerTeam >= 0 && shooterTeam != viewerTeam ? EnemyColor : FriendlyColor;
    }

    public static int ViewerTeam
    {
        get
        {
            if (ReplayPlayer.Active) return ReplayPlayer.PovTeam;
            var network = NetworkManager.Singleton;
            var player = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
            var team = player != null ? player.GetComponent<PlayerTeam>() : null;
            return team != null ? team.teamId.Value : -1;
        }
    }

    // Uses the already bundled particle shader, including its normal depth test against walls.
    // A ribbon keeps alpha at the muzzle; stretching a round particle across the whole shot hides both ends.
    static Material RibbonMaterial
    {
        get
        {
            if (ribbonMaterial) return ribbonMaterial;
            ribbonMaterial = new Material(Fx.ParticleMaterial) { name = "Sniper smoke ribbon" };
            const int width = 64, height = 32;
            ribbonTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Sniper smoke profile",
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float across = (y / (height - 1f) - 0.5f) * 2f;
                    float along = x / (width - 1f) * Mathf.PI * 2f;
                    float wisps = 0.82f + 0.12f * Mathf.Sin(along) + 0.06f * Mathf.Cos(along * 3f + across * 2f);
                    float edge = Mathf.Pow(Mathf.Clamp01(1f - across * across), 2f);
                    pixels[y * width + x] = new Color(1, 1, 1, edge * wisps);
                }
            ribbonTexture.SetPixels(pixels);
            ribbonTexture.Apply(false, true);
            ribbonMaterial.mainTexture = ribbonTexture;
            return ribbonMaterial;
        }
    }

    public static SniperShotTrail Spawn(Vector3 from, Vector3 to, Color color, bool full, float elapsed = 0f)
    {
        if (elapsed >= Lifetime || (to - from).sqrMagnitude < 0.0001f) return null;
        var trail = new GameObject("FX_SniperShotTrail").AddComponent<SniperShotTrail>();
        trail.replay = ReplayPlayer.Active;
        trail.tint = color;
        trail.age = Mathf.Max(0, elapsed);
        trail.smokeWidth = full ? 0.16f : 0.13f;
        trail.smoke = trail.MakeLine("Smoke", from, to, trail.smokeWidth);
        trail.core = trail.MakeLine("Core", from, to, full ? 0.055f : 0.04f);
        trail.ApplyFade();
        return trail;
    }

    LineRenderer MakeLine(string name, Vector3 from, Vector3 to, float width)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        var line = child.AddComponent<LineRenderer>();
        line.sharedMaterial = RibbonMaterial;
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.widthCurve = AnimationCurve.Linear(0, 1, 1, 1);
        line.widthMultiplier = width;
        line.textureMode = LineTextureMode.Tile;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 3;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return line;
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= Lifetime || replay != ReplayPlayer.Active)
        {
            Destroy(gameObject);
            return;
        }
        ApplyFade();
    }

    void ApplyFade()
    {
        float t = Mathf.Clamp01(age / Lifetime);
        SetAlpha(core, 0.95f * (1f - Mathf.Clamp01(age / 0.3f)));
        SetAlpha(smoke, 0.8f * (1f - Mathf.SmoothStep(0, 1, t)));
        smoke.widthMultiplier = Mathf.Lerp(smokeWidth, smokeWidth * 1.65f, t);
    }

    void SetAlpha(LineRenderer line, float alpha)
    {
        var color = tint;
        color.a = alpha;
        line.startColor = color;
        color.a *= 0.85f;
        line.endColor = color;
    }
}
