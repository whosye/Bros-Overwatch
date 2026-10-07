using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Kotel na drevene piliny v kotelne hlavni chaty (prizemi, prvni mistnost vpravo od vchodu). Kdo stoji u packy,
// muze ji na vyzvani klavesou R zatahnout (jednou za 'Cooldown' s pro vsechny): kotel na 'HeatSeconds' s
// prehreje horni patra chaty a vsem, kdo tam jsou (i tomu, kdo zatopil), ubere celkem 'TotalDamage' zivotu.
// Stav sdili MatchManager (MatchManagerBoiler.cs); tady je vzhled, vyzva a ovladani pro mistniho hrace.
public class Boiler : MonoBehaviour
{
    public const float Cooldown = 90f;
    public const float HeatSeconds = 10f;
    public const float TotalDamage = 60f;
    public const float UseDistance = 2.2f;
    // Ladeni: pri prehrati vypsat do konzole, kde hraci jsou, kdyz nikdo neni v horni oblasti.
    public const bool LogMisses = true;

    public Transform lever;          // kloub packy (otaci se kolem mistni osy z)
    public Light fireLight;          // plamen za dvirky
    public Light[] heatLights;       // oranzova svetla v hornich patrech (sviti jen pri prehrati)
    public Vector3 heatMin, heatMax; // oblast hornich pater (svet) - kde prehrati boli

    const float LeverIdle = -30f, LeverPulled = 45f;
    static readonly Color HeatColor = new Color(1f, 0.55f, 0.2f);

    public static Boiler Instance { get; private set; }

    // Mistni hrac stoji u packy a kotel je pripraven (R pak nenabiji zbran).
    public static bool LocalCanUse { get; private set; }
    // Text vyzvy pod zamerovacem (prazdny = nic).
    public static string PromptText { get; private set; } = "";

    ParticleSystem heatHaze;
    AudioSource roar;
    bool wasHeating;

    void OnEnable() => Instance = this;

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        LocalCanUse = false;
        PromptText = "";
    }

    public Vector3 LeverPoint => lever != null ? lever.position : transform.position;

    public bool InHeatZone(Vector3 feet) =>
        feet.x >= heatMin.x && feet.x <= heatMax.x && feet.y >= heatMin.y && feet.y <= heatMax.y && feet.z >= heatMin.z && feet.z <= heatMax.z;

    void Update()
    {
        var match = MatchManager.Instance;
        bool live = match != null && match.IsSpawned;
        bool heating = live && match.BoilerHeating;

        UpdateLocalPlayer(match, live, heating);
        UpdateLook(heating);

        if (heating && !wasHeating) OnStarted();
        wasHeating = heating;
    }

    void UpdateLocalPlayer(MatchManager match, bool live, bool heating)
    {
        LocalCanUse = false;
        PromptText = "";
        if (!live || match.IsLobby || match.IsOver) return;

        var network = NetworkManager.Singleton;
        var local = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        if (local == null) return;
        var fpc = local.GetComponent<FirstPersonController>();
        if (fpc == null || fpc.IsDead || fpc.playerCamera == null) return;
        if (Vector3.Distance(fpc.playerCamera.transform.position, LeverPoint) > UseDistance) return;

        if (heating)
        {
            PromptText = "Kotel topí naplno";
            return;
        }
        float wait = match.BoilerCooldownLeft;
        if (wait > 0f)
        {
            PromptText = $"Kotel: znovu za {Mathf.CeilToInt(wait)} s";
            return;
        }

        LocalCanUse = true;
        PromptText = "[R] Zatopit v kotli";
        if (GameSettings.CursorLocked && !fpc.InputBlocked && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            match.UseBoilerRpc();
    }

    // ---------------- vzhled ----------------

    void UpdateLook(bool heating)
    {
        if (lever != null)
        {
            float z = lever.localEulerAngles.z;
            if (z > 180f) z -= 360f;
            z = Mathf.MoveTowards(z, heating ? LeverPulled : LeverIdle, Time.deltaTime * (heating ? 400f : 60f));
            lever.localRotation = Quaternion.Euler(0f, 0f, z);
        }

        float flicker = Mathf.PerlinNoise(Time.time * 7f, 0.3f);
        if (fireLight != null)
            fireLight.intensity = heating ? 3f + flicker * 2.5f : 0.5f + flicker * 0.3f;

        if (heatLights != null)
            foreach (var l in heatLights)
            {
                if (l == null) continue;
                if (l.enabled != heating) l.enabled = heating;
                if (heating) l.intensity = 1.6f + Mathf.PerlinNoise(Time.time * 2f, l.transform.position.x) * 1.4f;
            }

        if (heatHaze != null)
        {
            var emission = heatHaze.emission;
            emission.rateOverTime = heating ? 90f : 0f;
        }
        if (roar != null)
        {
            roar.volume = Mathf.MoveTowards(roar.volume, heating ? 0.9f : 0f, Time.deltaTime * 0.8f);
            if (!heating && roar.volume <= 0.001f && roar.isPlaying) roar.Stop();
        }
    }

    void OnStarted()
    {
        ProceduralSfx.Play(ProceduralSfx.Explosion, LeverPoint, 0.45f);
        ProceduralSfx.Play(ProceduralSfx.Reload, LeverPoint, 0.9f);
        CaptureUI.Announce("Někdo zatopil v kotli - horní patro chaty se přehřívá!", HeatColor, 3f);

        if (heatHaze == null) BuildHaze();
        if (roar == null)
        {
            // huceni ohne: zpomalene huceni vody
            roar = gameObject.AddComponent<AudioSource>();
            roar.clip = ProceduralSfx.WaterRush;
            roar.loop = true;
            roar.pitch = 0.35f;
            roar.spatialBlend = 1f;
            roar.rolloffMode = AudioRolloffMode.Linear;
            roar.minDistance = 4f;
            roar.maxDistance = 22f;
            roar.dopplerLevel = 0f;
            roar.volume = 0f;
        }
        roar.transform.position = LeverPoint;
        if (!roar.isPlaying) roar.Play();
    }

    // Tetelici se horko nad podlahou hornich pater.
    void BuildHaze()
    {
        var go = new GameObject("FX_Horko");
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(new Vector3((heatMin.x + heatMax.x) * 0.5f, heatMin.y + 0.6f, (heatMin.z + heatMax.z) * 0.5f),
            Quaternion.Euler(-90f, 0f, 0f));
        heatHaze = go.AddComponent<ParticleSystem>();
        heatHaze.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = heatHaze.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.25f, 0.18f), new Color(1f, 0.35f, 0.1f, 0.28f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;
        var emission = heatHaze.emission;
        emission.rateOverTime = 0f;
        var shape = heatHaze.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(heatMax.x - heatMin.x - 1f, heatMax.z - heatMin.z - 1f, 0.1f);
        var fade = heatHaze.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        go.GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;
        heatHaze.Play();
    }
}
