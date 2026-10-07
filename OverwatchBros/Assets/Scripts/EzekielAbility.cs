using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Sindelova ultimatka (Q): "Ezechiel 25:17". Vznese se vysoko do vzduchu, pres celou mapu hraje hlaska a na zemi, kam miri,
// sviti kruh - vidi ho jen Sindel a jeho tym (nepratele slysi hlasku a vidi zariciho Sindela na nebi, ale nevi, kam miri).
// Ve vzduchu se da pomalu pohybovat.
// Na konci hlasky ("...when I lay my vengeance upon thee!") do kruhu uderi sloup svetla: 'power' ve stredu, k okraji
// mene ('radius'). Doba = delka nahravky ultimatky (Audio/Sindel/ability_Q), bez nahravky 'duration'.
public class EzekielAbility : NetworkBehaviour
{
    enum Phase { Idle, Rising, Preaching, Descending }

    public AbilityDefinition ability;

    public float hoverHeight = 25f;
    public float riseTime = 1.4f;
    public float hoverSpeed = 3.5f;
    public float cameraDistance = 6.5f;
    public float cameraLift = 1.2f;
    const float StrikeEdge = 0.35f;
    public static readonly Color HolyColor = new Color(1f, 0.85f, 0.45f, 1f);

    CharacterController controller;
    FirstPersonController fpc;
    PlayerHero hero;
    Camera playerCamera;

    Phase phase = Phase.Idle;
    float phaseTimer;
    float castEnd;
    float targetY;
    Vector3 savedCameraLocalPosition;
    float nextUseTime;

    // Sdileny stav pro vsechny (kruh na zemi, aura).
    readonly NetworkVariable<bool> casting = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<Vector3> aim = new NetworkVariable<Vector3>(Vector3.zero,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<bool> aimValid = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<float> castSeconds = new NetworkVariable<float>(6f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool IsCasting => casting.Value;
    bool UsesCharge => hero != null && hero.UsesUltCharge;
    bool CanUse => UsesCharge ? hero.UltReady : Time.time >= nextUseTime;
    public float CooldownRemaining => UsesCharge ? 0f : Mathf.Max(0f, nextUseTime - Time.time);

    // vizual (vsichni)
    GameObject circle, core;
    Material circleMaterial;
    Light circleLight;
    ParticleSystem halo;
    Light haloLight;
    float castStartLocal;

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
    }

    public override void OnNetworkSpawn()
    {
        playerCamera = fpc.playerCamera;
        casting.OnValueChanged += OnCastingChanged;
    }

    public override void OnNetworkDespawn()
    {
        casting.OnValueChanged -= OnCastingChanged;
        HideVisual();
    }

    void OnDisable()
    {
        Cancel();
        HideVisual();
    }

    void OnCastingChanged(bool previous, bool current)
    {
        if (current) castStartLocal = Time.time;
        else HideVisual();
    }

    // Doba kazani = nejdelsi nahravka ultimatky (bez fazi step_N).
    float CastDuration()
    {
        float longest = 0f;
        if (ability != null && ability.voiceLines != null)
            foreach (var clip in ability.voiceLines)
                if (clip != null && !clip.name.ToLowerInvariant().StartsWith("step_"))
                    longest = Mathf.Max(longest, clip.length);
        float seconds = longest > 0.5f ? longest : (ability != null ? ability.duration : 6f);
        return Mathf.Clamp(seconds, 2.5f, 25f);
    }

    void Update()
    {
        UpdateVisual();

        if (!IsOwner || ability == null) return;

        if (phase == Phase.Idle)
        {
            if (Keyboard.current.qKey.wasPressedThisFrame && CanUse && GameSettings.CursorLocked
                && !fpc.InputBlocked && !fpc.Rooted)
                Begin();
            return;
        }

        if (fpc.CannotAct)
        {
            Cancel();
            return;
        }

        switch (phase)
        {
            case Phase.Rising: TickRising(); break;
            case Phase.Preaching: TickPreaching(); break;
            case Phase.Descending: TickDescending(); break;
        }

        if (phase != Phase.Idle)
            ThirdPersonCamera.PlaceOrbit(transform, NetworkObject, playerCamera, savedCameraLocalPosition, cameraDistance, cameraLift);
    }

    void Begin()
    {
        nextUseTime = Time.time + ability.Cooldown;
        hero.SpendUlt();
        fpc.AbilityActive = true;
        fpc.ResetVertical();
        savedCameraLocalPosition = playerCamera.transform.localPosition;

        float seconds = CastDuration();
        castEnd = Time.time + seconds;
        targetY = transform.position.y + hoverHeight;
        phase = Phase.Rising;

        castSeconds.Value = seconds;
        aimValid.Value = false;
        casting.Value = true;
        hero.SayAbility(ability);
        BeginServerRpc(seconds);
    }

    void TickRising()
    {
        float step = hoverHeight / Mathf.Max(0.05f, riseTime) * Time.deltaTime;
        float newY = Mathf.MoveTowards(transform.position.y, targetY, step);
        var flags = controller.Move(Vector3.up * (newY - transform.position.y));
        UpdateAim();

        if ((flags & CollisionFlags.Above) != 0 || Mathf.Abs(transform.position.y - targetY) < 0.1f)
            phase = Phase.Preaching;
        if (Time.time >= castEnd)
            Strike();
    }

    void TickPreaching()
    {
        // Pomaly let ve vysce (WASD podle smeru kamery).
        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed) input.y -= 1f;
        if (Keyboard.current.dKey.isPressed) input.x += 1f;
        if (Keyboard.current.aKey.isPressed) input.x -= 1f;

        Vector3 forward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 move = (forward * input.y + right * input.x);
        if (move.sqrMagnitude > 1f) move.Normalize();
        // drobne vlneni nahoru a dolu
        float bob = Mathf.Sin(Time.time * 2.2f) * 0.25f;
        controller.Move(move * hoverSpeed * Time.deltaTime + Vector3.up * (targetY + bob - transform.position.y) * Mathf.Min(1f, Time.deltaTime * 3f));

        UpdateAim();
        if (Time.time >= castEnd)
            Strike();
    }

    void UpdateAim()
    {
        // Z kamery (krizek uprostred obrazovky ukazuje, kam kruh dopadne).
        Vector3 origin = playerCamera.transform.position;
        Vector3 direction = playerCamera.transform.forward;
        var hits = Physics.RaycastAll(origin, direction, ability.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && owner == NetworkObject) continue;
            if (hit.collider.GetComponentInParent<PlayerHero>() != null)
            {
                // Miri na hrace: kruh pod nim.
                if (Physics.Raycast(hit.point, Vector3.down, out var below, 30f, ~0, QueryTriggerInteraction.Ignore))
                    SetAim(below.point, true);
                return;
            }
            if (hit.normal.y >= 0.3f)
            {
                SetAim(hit.point, true);
                return;
            }
            // zed: kruh k jejimu patu
            if (Physics.Raycast(hit.point + hit.normal * 0.5f, Vector3.down, out var foot, 30f, ~0, QueryTriggerInteraction.Ignore))
                SetAim(foot.point, true);
            return;
        }
        if (aimValid.Value) aimValid.Value = false;
    }

    void SetAim(Vector3 point, bool valid)
    {
        if ((aim.Value - point).sqrMagnitude > 0.0004f) aim.Value = point;
        if (aimValid.Value != valid) aimValid.Value = valid;
    }

    void Strike()
    {
        if (aimValid.Value)
            StrikeServerRpc(aim.Value);
        casting.Value = false;
        phase = Phase.Descending;
        phaseTimer = 4f;
    }

    void TickDescending()
    {
        phaseTimer -= Time.deltaTime;
        controller.Move(Vector3.down * 7f * Time.deltaTime);
        if (controller.isGrounded || phaseTimer <= 0f)
            Finish();
    }

    public void Cancel()
    {
        if (phase == Phase.Idle) return;
        Finish();
    }

    void Finish()
    {
        phase = Phase.Idle;
        if (playerCamera != null)
            playerCamera.transform.localPosition = savedCameraLocalPosition;
        if (fpc != null)
        {
            fpc.AbilityActive = false;
            fpc.ResetVertical();
        }
        if (IsSpawned && IsOwner && casting.Value)
            casting.Value = false;
        if (IsSpawned && IsOwner)
            EndServerRpc();
    }

    // ---------------- server ----------------

    // Behem kazani (vylet, cela hlaska, sestup) je Sindel nezranitelny.
    [ServerRpc]
    void BeginServerRpc(float seconds)
    {
        GetComponent<Health>().ServerInvulnerable(riseTime + Mathf.Clamp(seconds, 0f, 25f) + 4f);
        BeginClientRpc();
    }

    [ServerRpc]
    void EndServerRpc()
    {
        GetComponent<Health>().ServerClearInvulnerable();
    }

    [ServerRpc]
    void StrikeServerRpc(Vector3 point)
    {
        var match = MatchManager.Instance;
        if (ability == null || (match != null && (match.IsOver || match.IsLobby))) return;
        if (Vector3.Distance(point, transform.position) > ability.range + 10f) return;

        var recorder = GetComponent<PotgRecorder>();
        if (recorder != null)
            recorder.ServerNoteUltimate();

        Combat.Explode(gameObject, point + Vector3.up * 0.3f, ability.radius, ability.power, StrikeEdge);
        StrikeClientRpc(point, ability.radius);
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void BeginClientRpc()
    {
        Fx.Sparks(transform.position + Vector3.up, HolyColor);
        ProceduralSfx.Play(ProceduralSfx.LeapStart, transform.position, 0.8f);
    }

    [ClientRpc]
    void StrikeClientRpc(Vector3 point, float radius)
    {
        Fx.PlayGlobal(ProceduralSfx.Explosion, 0.9f);
        Fx.Explosion(point, radius);
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.PI * 2f;
            Fx.Sparks(point + new Vector3(Mathf.Cos(a), 0.3f, Mathf.Sin(a)) * radius * 0.7f, HolyColor);
        }
        HolyBeam.Spawn(point, radius);
    }

    void UpdateVisual()
    {
        if (!casting.Value)
            return;

        float seconds = Mathf.Max(0.5f, castSeconds.Value);
        float progress = Mathf.Clamp01((Time.time - castStartLocal) / seconds);
        float radius = ability != null ? ability.radius : 5f;

        if (circle == null) BuildVisual();

        // Kruh vidi jen Sindel a spoluhraci.
        bool show = aimValid.Value && SeenByLocalPlayer();
        circle.SetActive(show);
        if (show)
        {
            // Kruh houstne a na konci rychle pulzuje.
            float pulse = progress > 0.75f ? 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 30f, (progress - 0.75f) * 4f)) : 1f;
            circle.transform.position = aim.Value + Vector3.up * 0.04f;
            circle.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
            core.transform.localScale = new Vector3(progress, 1.5f, progress);
            var color = Color.Lerp(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.6f, 0.15f), progress);
            if (circleMaterial.HasProperty("_BaseColor")) circleMaterial.SetColor("_BaseColor", color);
            else circleMaterial.color = color;
            circleLight.intensity = (1.5f + progress * 6f) * pulse;
            circleLight.range = radius * 1.8f;
        }

        if (haloLight != null)
            haloLight.intensity = 2.5f + Mathf.Sin(Time.time * 6f) * 0.7f;
    }

    bool SeenByLocalPlayer()
    {
        if (IsOwner) return true;
        var network = NetworkManager.Singleton;
        var local = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        return local != null && Combat.SameTeam(local.gameObject, gameObject);
    }

    void BuildVisual()
    {
        circle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        circle.name = "FX_EzekielCircle";
        DestroyImmediate(circle.GetComponent<Collider>());
        Fx.Paint(circle, HolyColor);
        circleMaterial = circle.GetComponent<Renderer>().material;

        core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        core.name = "Core";
        DestroyImmediate(core.GetComponent<Collider>());
        core.transform.SetParent(circle.transform, false);
        core.transform.localPosition = Vector3.up * 0.5f;
        Fx.Paint(core, Color.white);

        var lightObject = new GameObject("Light");
        lightObject.transform.SetParent(circle.transform, false);
        lightObject.transform.localPosition = Vector3.up * 80f;   // (meritko kruhu je ploche - svetlo ~1.5 m nad zemi)
        circleLight = lightObject.AddComponent<Light>();
        circleLight.type = LightType.Point;
        circleLight.color = HolyColor;
        circleLight.shadows = LightShadows.None;

        halo = Fx.CreateAura(transform, HolyColor);
        halo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        halo.Play();
        var glow = new GameObject("FX_HolyGlow");
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = new Vector3(0f, 2.2f, 0f);
        haloLight = glow.AddComponent<Light>();
        haloLight.type = LightType.Point;
        haloLight.color = HolyColor;
        haloLight.range = 6f;
        haloLight.shadows = LightShadows.None;
    }

    void HideVisual()
    {
        if (circle != null) Destroy(circle);
        circle = null;
        core = null;
        if (halo != null)
        {
            halo.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(halo.gameObject, 1f);
        }
        halo = null;
        if (haloLight != null) Destroy(haloLight.gameObject);
        haloLight = null;
    }
}

// Sloup svetla z nebe pri uderu: zablesk, rozsiri se a vybledne.
public class HolyBeam : MonoBehaviour
{
    float age;
    float radius;
    Renderer beamRenderer;
    Light flash;
    const float Lifetime = 0.9f;

    public static void Spawn(Vector3 point, float radius)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "HolyBeam";
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = point + Vector3.up * 40f;
        var beam = go.AddComponent<HolyBeam>();
        beam.radius = radius;
        beam.beamRenderer = go.GetComponent<Renderer>();
        Fx.Paint(go, new Color(1f, 0.95f, 0.75f));
        var material = beam.beamRenderer.material;
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.5f) * 6f);

        var lightObject = new GameObject("Flash");
        lightObject.transform.position = point + Vector3.up * 3f;
        lightObject.transform.SetParent(go.transform, true);
        beam.flash = lightObject.AddComponent<Light>();
        beam.flash.type = LightType.Point;
        beam.flash.color = EzekielAbility.HolyColor;
        beam.flash.range = radius * 4f;
        beam.flash.intensity = 25f;
        beam.flash.shadows = LightShadows.None;
        beam.Update();
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / Lifetime);
        // rychle naskoci do plne sirky, pak se zuzi a zmizi
        float width = radius * 2f * (t < 0.15f ? Mathf.Lerp(0.3f, 1f, t / 0.15f) : Mathf.Lerp(1f, 0.05f, (t - 0.15f) / 0.85f));
        transform.localScale = new Vector3(width, 40f, width);
        if (flash != null) flash.intensity = 25f * (1f - t);
        if (t >= 1f) Destroy(gameObject);
    }
}
