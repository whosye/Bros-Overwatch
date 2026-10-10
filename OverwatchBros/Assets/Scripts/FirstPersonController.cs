using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : NetworkBehaviour
{
    public Camera playerCamera;

    // Bot (BotBrain) misto klavesnice a mysi - jen na hostu.
    public BotBrain Bot { get; set; }

    public float walkSpeed = 6f;
    public float runSpeed = 9.5f;
    public float jumpHeight = 1.2f;
    public float gravity = -9.81f;
    [Tooltip("Stronger gravity for regular jumps and falls; jump height stays the same.")]
    [Min(1f)] public float movementGravityScale = 2.5f;
    [Tooltip("Extra gravity after the apex of a regular jump.")]
    [Min(1f)] public float fallingGravityScale = 1.2f;
    public float mouseSensitivity = 2f;
    public float standHeight = 2f;
    public float crouchHeight = 1f;
    // Vyska oci nad chodidly (model postavy meri ~1.8 m).
    public float eyeHeight = 1.62f;
    public float crouchEyeHeight = 1.0f;
    public float footstepDistance = 2.2f;

    CharacterController controller;
    WeaponShooting shooting;
    Health health;
    PlayerHero hero;
    float verticalVelocity;
    bool abilityFlight;
    float JumpGravity => gravity * Mathf.Max(1f, movementGravityScale);
    float cameraPitch;
    float stepAccumulator;
    float gaitSpeed;
    float baseEyeHeight;
    float headBobOffset;
    bool crouching;

    public CharacterController Controller => controller;
    public bool AbilityActive { get; set; }

    // Schopnost, u ktere se dá normalne ovladat pohyb (Modry plamen): kamera ve 3. osobe, zrychleni, Shift neni sprint.
    public bool RushActive { get; set; }

    // Blok (zkrizene sekery): pomalejsi pohyb, nejde utocit.
    public bool BlockActive { get; set; }
    public float SpeedMultiplier { get; set; } = 1f;
    // Rychlost hrdiny (HeroDefinition.moveSpeed, napr. Pova rychlejsi nez strelci).
    public float HeroSpeedScale { get; set; } = 1f;
    // Zpomaleni pri pribliseni (sniper) a docasne zpomaleni od schopnosti (odkopnuti).
    public float ScopeSpeedScale { get; set; } = 1f;
    // Zpomaleni od vlastni schopnosti (napr. nabijeni Maxova Dlouheho rezu).
    public float AbilitySpeedScale { get; set; } = 1f;
    float slowUntil;
    float slowFactor = 1f;
    float SlowScale => Time.time < slowUntil ? slowFactor : 1f;

    // Docasne zrychleni (balicek "Pivo" na mape).
    float boostUntil;
    float boostFactor = 1f;
    float BoostScale => Time.time < boostUntil ? boostFactor : 1f;

    // V tunelu (jediny prostor pod zemi: podlaha 4 m pod terenem) se bezi dvojnasobnou rychlosti - rychla bocni cesta.
    public const float TunnelSpeed = 2f;
    const float TunnelDepth = -1.5f;
    public bool InTunnel => controller != null && controller.bounds.min.y < TunnelDepth;
    float TunnelScale => InTunnel ? TunnelSpeed : 1f;

    bool waterRide;   // hrac sjel z toboganu do vody (WaterCurrent)
    float waterArmedUntil;   // hrac byl na plosine toboganu (start jizdy)

    // Jede z toboganu po vode - vidi vsichni (splouchani, WaterSplashFx). Zapisuje vlastnik.
    public NetworkVariable<bool> waterSurfing = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Jizda z toboganu (od skluzu az do konce vody) - podle toho hraje hudba jizdy (WaterSplashFx). Zapisuje vlastnik.
    public NetworkVariable<bool> waterRiding = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public void ServerSpeedBoost(float seconds, float factor)
    {
        if (IsServer)
            SpeedBoostClientRpc(seconds, factor);
    }

    [ClientRpc]
    void SpeedBoostClientRpc(float seconds, float factor)
    {
        if (!IsOwner) return;
        boostUntil = Time.time + seconds;
        boostFactor = Mathf.Clamp(factor, 1f, 2f);
    }

    // Vlastnik sam sobe (Maxuv Stinovy krok / napraah rezu).
    public void OwnerSpeedBoost(float seconds, float factor)
    {
        if (!IsOwner) return;
        boostUntil = Time.time + seconds;
        boostFactor = Mathf.Clamp(factor, 1f, 2f);
    }

    public void OwnerHold(float seconds)
    {
        if (!IsOwner) return;
        rootedUntil = Mathf.Max(rootedUntil, Time.time + seconds);
    }

    public void ServerSlow(float seconds, float factor)
    {
        if (IsServer)
            SlowClientRpc(seconds, factor);
    }

    [ClientRpc]
    void SlowClientRpc(float seconds, float factor)
    {
        if (!IsOwner) return;
        slowUntil = Time.time + seconds;
        slowFactor = Mathf.Clamp(factor, 0.1f, 1f);
    }

    // Vlastni pritazeni (sniperuv hak): hrac se plynule pritahne na misto.
    public void OwnerGrapplePull(Vector3 destination, float seconds, Vector3 anchor, Vector3 normal)
    {
        if (!IsOwner || IsDead) return;

        pulling = true;
        mantling = false;
        grappleAutoMantle = true;
        grappleAnchor = anchor;
        grappleInward = Vector3.ProjectOnPlane(-normal, Vector3.up).normalized;
        if (Mathf.Abs(normal.y) > 0.3f || grappleInward.sqrMagnitude < 0.01f)
            grappleInward = Vector3.ProjectOnPlane(anchor - transform.position, Vector3.up).normalized;
        pullTarget = destination;
        pullTimeLeft = Mathf.Max(0.05f, seconds) + 0.15f;
        pullSpeed = Vector3.Distance(transform.position, destination) / Mathf.Max(0.05f, seconds);
        externalVelocity = Vector3.zero;
        verticalVelocity = 0f;
    }
    public bool ShiftReserved { get; set; }

    // Pasivni schopnosti hrdiny (nastavuje PlayerHero).
    public bool DoubleJump { get; set; }
    public bool LedgeClimb { get; set; }
    bool airJumpUsed;
    bool mantling;
    Vector3 mantleTarget;
    float mantleTimeLeft;
    Vector3[] grappleMantlePath;
    int grappleMantleWaypoint;
    public bool ThirdPerson => AbilityActive || RushActive;
    public bool IsDead => health != null && health.currentHealth.Value <= 0f;
    public bool MatchOver => MatchManager.Instance != null && MatchManager.Instance.IsOver;
    public bool InLobby => MatchManager.Instance != null && MatchManager.Instance.IsLobby;
    // Hrac se pripojil do rozehraneho zapasu a jeste si vybira tym / hrdinu.
    public bool Joining => hero != null && hero.IsJoining;
    public bool CannotAct => IsDead || MatchOver || InLobby || Joining || Stunned;
    public bool InputBlocked => CannotAct || AbilityActive || UltCasting;

    // Priprava ultimatky (viz UltWindup): hrac chodi, ale nestrili a nepouziva schopnosti; UltWindup = postup 0-1.
    public bool UltCasting { get; set; }
    public float UltWindup { get; set; }

    // Odhozeni vybuchem (naloz, balvan) a znehybneni (past). Server je posila vlastnikovi hrace.
    Vector3 externalVelocity;
    float rootedUntil;
    public bool Rooted => Time.time < rootedUntil;

    public void AddImpulse(Vector3 impulse)
    {
        // Vic vybuchu naraz se scita, ale jen po strop (dve naloze = vyssi skok, ne let do nebe).
        externalVelocity = Vector3.ClampMagnitude(externalVelocity + new Vector3(impulse.x, 0f, impulse.z), 20f);
        if (impulse.y > 0f)
        {
            abilityFlight = true;
            verticalVelocity = Mathf.Min(Mathf.Max(verticalVelocity, 0f) + impulse.y, 13f);
        }
    }

    // Vyskok se vznasenim (Sindeluv Shift): hned nahoru, po vrcholu pomaly pad, dokud nevyprsi 'seconds'.
    float hoverUntil;

    public void OwnerHover(float upSpeed, float seconds)
    {
        if (!IsOwner || IsDead) return;
        verticalVelocity = Mathf.Max(verticalVelocity, upSpeed);
        abilityFlight = true;
        hoverUntil = Time.time + seconds;
    }

    // Behem vznaseni se ve vzduchu da pohybovat rychleji (jinak by visel skoro na miste).
    public const float HoverAirSpeed = 1.6f;
    float HoverScale => Time.time < hoverUntil && !controller.isGrounded ? HoverAirSpeed : 1f;

    public void ServerKnockback(Vector3 impulse)
    {
        if (IsServer)
            KnockbackClientRpc(impulse);
    }

    [ClientRpc]
    void KnockbackClientRpc(Vector3 impulse)
    {
        if (!IsOwner || IsDead || AbilityActive) return;
        AddImpulse(impulse);
    }

    public void ServerRoot(float seconds)
    {
        if (!IsServer) return;

        RootClientRpc(seconds);

        // Hlaska "chytili me" (i pro budouci zpomaleni staci zavolat hero.Say(VoiceKind.Snare)).
        if (hero != null)
            hero.Say(VoiceKind.Snare);
    }

    // Podrzeni na miste bez hlasky (utocnici behem pripravy v rezimu utok a obrana). Rozhlizet se a strilet jde.
    public void ServerHold(float seconds)
    {
        if (IsServer)
            RootClientRpc(seconds);
    }

    [ClientRpc]
    void RootClientRpc(float seconds)
    {
        if (!IsOwner) return;

        rootedUntil = Time.time + seconds;
        externalVelocity = Vector3.zero;
    }

    // Nasobek citlivosti mysi (pribliseni dalekohledem ji snizi, aby se dalo presne mirit).
    public float LookScale { get; set; } = 1f;

    // Uspani (Annina sipka): jako omraceni bez oslepeni, ale zasah hrace hned probudi.
    float serverSleepUntil;
    public bool ServerSleeping => IsServer && Time.time < serverSleepUntil;

    public void ServerSleep(float seconds, GameObject attacker)
    {
        if (!IsServer) return;

        serverSleepUntil = Time.time + seconds;
        var attackerObject = attacker != null ? attacker.GetComponent<NetworkObject>() : null;
        SleepClientRpc(seconds, attackerObject != null ? attackerObject.OwnerClientId : ulong.MaxValue);
        if (hero != null)
            hero.Say(VoiceKind.Snare);
    }

    public void ServerWake()
    {
        if (!ServerSleeping) return;

        serverSleepUntil = 0f;
        WakeClientRpc();
    }

    [ClientRpc]
    void SleepClientRpc(float seconds, ulong attackerClientId)
    {
        SleepMarker.Show(transform, seconds);
        if (NetworkManager.LocalClientId == attackerClientId && Camera.main != null)
            ProceduralSfx.Play(ProceduralSfx.StunConfirm, Camera.main.transform.position, 0.9f);

        if (!IsOwner || IsDead) return;

        stunnedUntil = Time.time + seconds;
        externalVelocity = Vector3.zero;
        if (Bot == null) HudUI.NotifySleep(seconds);
    }

    [ClientRpc]
    void WakeClientRpc()
    {
        SleepMarker.Hide(transform);
        if (!IsOwner) return;

        stunnedUntil = 0f;
        if (Bot == null) HudUI.EndOverlay();
    }

    // Omraceni (oslepujici granat): hrac se chvili nemuze hybat, utocit ani pouzivat schopnosti; rozbehle schopnosti se prerusi.
    float stunnedUntil;
    public bool Stunned => Time.time < stunnedUntil;

    // blind = oslepujici granat (bila obrazovka a piskani); hak omracuje bez oslepeni.
    public void ServerStun(float seconds, GameObject attacker = null, bool blind = true)
    {
        if (!IsServer) return;

        var attackerObject = attacker != null ? attacker.GetComponent<NetworkObject>() : null;
        StunClientRpc(seconds, attackerObject != null ? attackerObject.OwnerClientId : ulong.MaxValue, blind);
        if (hero != null)
            hero.Say(VoiceKind.Snare);
    }

    [ClientRpc]
    void StunClientRpc(float seconds, ulong attackerClientId, bool blind)
    {
        // Zvuk omraceni slysi vsichni v okoli; ten, kdo trefil, dostane navic potvrzeni (i kdyz je daleko).
        if (blind)
            ProceduralSfx.Play(ProceduralSfx.Stun, transform.position + Vector3.up * 1.5f, 1f);
        if (NetworkManager.LocalClientId == attackerClientId && Camera.main != null)
            ProceduralSfx.Play(ProceduralSfx.StunConfirm, Camera.main.transform.position, 0.9f);

        if (!IsOwner || IsDead) return;

        stunnedUntil = Time.time + seconds;
        externalVelocity = Vector3.zero;
        if (blind && Bot == null)
            HudUI.NotifyFlash(seconds);
    }

    // Pritazeni hakem: hrace to plynule dotahne na dane misto (zdi ho zastavi).
    bool pulling;
    Vector3 pullTarget;
    float pullSpeed;
    float pullTimeLeft;
    bool grappleAutoMantle;
    Vector3 grappleAnchor;
    Vector3 grappleInward;

    public void ServerPull(Vector3 destination, float seconds)
    {
        if (IsServer)
            PullClientRpc(destination, seconds);
    }

    [ClientRpc]
    void PullClientRpc(Vector3 destination, float seconds)
    {
        if (!IsOwner || IsDead) return;

        pulling = true;
        grappleAutoMantle = false;
        mantling = false;
        pullTarget = destination;
        pullTimeLeft = Mathf.Max(0.05f, seconds) + 0.15f;
        pullSpeed = Vector3.Distance(transform.position, destination) / Mathf.Max(0.05f, seconds);
        externalVelocity = Vector3.zero;
        verticalVelocity = 0f;
    }

    // Vraci true, dokud pritahovani bezi (bezny pohyb se v tu chvili neprovadi).
    bool TickPull()
    {
        if (!pulling) return false;

        if (grappleAutoMantle && !InputBlocked && TryStartGrappleMantle())
            return false;

        pullTimeLeft -= Time.deltaTime;
        Vector3 toTarget = pullTarget - transform.position;
        float step = pullSpeed * Time.deltaTime;

        if (IsDead || (grappleAutoMantle && InputBlocked) || pullTimeLeft <= 0f || toTarget.magnitude <= Mathf.Max(0.25f, step))
        {
            pulling = false;
            grappleAutoMantle = false;
            verticalVelocity = 0f;
            return false;
        }

        controller.Move(toTarget.normalized * step);
        return true;
    }

    public void ClearForces()
    {
        abilityFlight = false;
        pulling = false;
        grappleAutoMantle = false;
        mantling = false;
        externalVelocity = Vector3.zero;
        rootedUntil = 0f;
        stunnedUntil = 0f;
    }

    public void ResetVertical()
    {
        abilityFlight = false;
        verticalVelocity = 0f;
    }

    void Awake()
    {
        baseEyeHeight = eyeHeight;
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();
        hero = GetComponent<PlayerHero>();
    }

    public override void OnNetworkSpawn()
    {
        if (GetComponent<WaterSplashFx>() == null)
            gameObject.AddComponent<WaterSplashFx>();
        if (IsOwner && Bot == null && GetComponent<JokerTvBuff>() == null)
            gameObject.AddComponent<JokerTvBuff>();

        // cizi hrac i bot (na hostu) - kamera se nepouziva (jen jako "oci" bota)
        if (!IsOwner || Bot != null)
        {
            playerCamera.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
            playerCamera.gameObject.SetActive(false);
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        playerCamera.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
    }

    void Update()
    {
        if (!IsOwner) return;

        gaitSpeed = 0f;
        crouching = false;

        if (Bot != null)
        {
            // bot: natoceni a pohled urcuje mozek
            transform.rotation = Quaternion.Euler(0f, Bot.Yaw, 0f);
            cameraPitch = Bot.Pitch;
            playerCamera.transform.localEulerAngles = new Vector3(cameraPitch, 0f, 0f);
            HandleMovement();
            UpdateEyePosition();
            return;
        }

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            Cursor.lockState = CursorLockMode.None;

        HandleLook();
        HandleMovement();
        UpdateEyePosition();
    }

    void UpdateEyePosition()
    {
        // Ability cameras own their position in third person.
        if (ThirdPerson || Joining)
        {
            headBobOffset = 0f;
            return;
        }

        baseEyeHeight = Mathf.MoveTowards(baseEyeHeight, crouching ? crouchEyeHeight : eyeHeight, 5f * Time.deltaTime);
        bool enabled = Bot == null && !CannotAct && GameSettings.CursorLocked && GameSettings.HeadBobEnabled;
        float targetBob = 0f;
        if (enabled && gaitSpeed > 0.1f)
        {
            // One vertical cycle per footstep, with its low point at foot contact.
            float phase = stepAccumulator / Mathf.Max(0.1f, footstepDistance);
            float amplitude = 0.025f * Mathf.Clamp(gaitSpeed / Mathf.Max(0.1f, walkSpeed), 0f, 1.5f);
            targetBob = -Mathf.Cos(phase * Mathf.PI * 2f) * amplitude * GameSettings.HeadBobIntensity;
        }
        headBobOffset = enabled && GameSettings.HeadBobIntensity > 0f
            ? Mathf.Lerp(headBobOffset, targetBob, 1f - Mathf.Exp(-25f * Time.deltaTime)) : 0f;
        // Keep stance height separate so the oscillation cannot accumulate or fight crouching.
        playerCamera.transform.localPosition = new Vector3(0f, baseEyeHeight + headBobOffset, 0f);
    }

    void HandleLook()
    {
        if (!GameSettings.CursorLocked) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        float sensitivity = mouseSensitivity * GameSettings.Sensitivity * LookScale;

        transform.Rotate(Vector3.up * mouseDelta.x * sensitivity * Time.deltaTime);

        cameraPitch -= mouseDelta.y * sensitivity * Time.deltaTime;
        cameraPitch = Mathf.Clamp(cameraPitch, -80f, 80f);
        playerCamera.transform.localEulerAngles = new Vector3(cameraPitch, 0f, 0f);
    }

    // Vytazeni na hranu: pred hracem je zed, nad ni volno a kousek nad hlavou rovna plocha.
    bool TryStartMantle()
    {
        Vector3 feet = transform.position;
        Vector3 forward = transform.forward;

        // Zed ve vysce hrudi.
        if (!Physics.Raycast(feet + Vector3.up * 1.0f, forward, out RaycastHit wall, 0.75f, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (wall.collider.GetComponentInParent<NetworkObject>() != null) return false;

        // Horni plocha prekazky: nejvys 2,3 m nad chodidly a aspon 0,9 m (nizsi schod se prejde sam).
        Vector3 above = feet + forward * (wall.distance + 0.35f) + Vector3.up * 2.6f;
        if (!Physics.Raycast(above, Vector3.down, out RaycastHit top, 1.8f, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (top.normal.y < 0.7f || top.point.y - feet.y < 0.9f) return false;

        // Nahore musi byt misto na stani.
        if (Physics.CheckCapsule(top.point + Vector3.up * 0.45f, top.point + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) return false;

        mantling = true;
        grappleMantlePath = null;
        mantleTarget = top.point + Vector3.up * 0.05f;
        mantleTimeLeft = 0.6f;
        verticalVelocity = 0f;
        externalVelocity = Vector3.zero;
        return true;
    }

    // Only the sniper's own grapple can initiate this automatic climb.
    bool TryStartGrappleMantle()
    {
        Vector3 feet = transform.position;
        Vector3 toAnchor = grappleAnchor - feet;
        if (Vector3.ProjectOnPlane(toAnchor, Vector3.up).magnitude > 1.6f ||
            toAnchor.y > 2.4f || toAnchor.y < -0.3f) return false;

        float radius = controller.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        Vector3 sideways = Vector3.Cross(Vector3.up, grappleInward);
        // A ridge or narrow fascia can block the exact hit line; try either side
        // before giving up, while retaining all capsule/path clearance checks.
        for (int lateral = 0; lateral < 5; lateral++)
        for (int i = 0; i < 5; i++)
        {
            // Prefer a deep landing on roofs, then check narrow wall tops too.
            // A 0.6 m wall is missed entirely by the original 0.7 m first probe.
            // The capsule may overhang an edge; clearance, not wall width, decides safety.
            float inset = i < 3 ? radius + 0.2f + i * 0.35f : radius * (i == 3 ? 0.6f : 0.3f);
            float sideOffset = lateral == 0 ? 0f : ((lateral + 1) / 2) * radius * 0.75f * (lateral % 2 == 1 ? 1f : -1f);
            Vector3 probe = grappleAnchor + grappleInward * inset + sideways * sideOffset;
            probe.y = Mathf.Min(feet.y + 2.5f, grappleAnchor.y + 1.3f);
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit top, probe.y - feet.y + 0.1f,
                    ~0, QueryTriggerInteraction.Ignore)) continue;
            if (top.collider.GetComponentInParent<NetworkObject>() != null ||
                top.normal.y < Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad) ||
                top.point.y < feet.y + 0.05f) continue;

            // Extra clearance on slopes keeps the bottom sphere above the roof.
            Vector3 landing = top.point + Vector3.up * (0.1f + radius * (1f / top.normal.y - 1f));
            if (!GrappleCapsuleClear(landing, Vector3.zero)) continue;
            if (!TryGrappleMantlePath(feet, landing, out Vector3[] path, out float length)) continue;

            pulling = false;
            grappleAutoMantle = false;
            mantling = true;
            mantleTarget = landing;
            grappleMantlePath = path;
            grappleMantleWaypoint = 0;
            mantleTimeLeft = length / 5f + 0.35f;
            abilityFlight = false;
            verticalVelocity = 0f;
            externalVelocity = Vector3.zero;
            return true;
        }
        return false;
    }

    bool TryGrappleMantlePath(Vector3 feet, Vector3 landing, out Vector3[] path, out float length)
    {
        // If we are underneath an eave, first move out from under it, then rise
        // and cross the edge. Every segment must fit the full character capsule.
        Vector3 outward = Vector3.ProjectOnPlane(feet - grappleAnchor, Vector3.up).normalized;
        if (outward.sqrMagnitude < 0.01f) outward = -grappleInward;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Vector3 outside = feet + outward * (attempt * 0.5f);
            Vector3 raised = new Vector3(outside.x, landing.y, outside.z);
            if ((attempt > 0 && !GrappleCapsuleClear(feet, outside - feet)) ||
                !GrappleCapsuleClear(outside, raised - outside) ||
                !GrappleCapsuleClear(raised, landing - raised)) continue;
            path = new[] { outside, raised, landing };
            length = Vector3.Distance(feet, outside) + Vector3.Distance(outside, raised) + Vector3.Distance(raised, landing);
            return true;
        }
        path = null;
        length = 0f;
        return false;
    }

    bool GrappleCapsuleClear(Vector3 position, Vector3 travel)
    {
        Vector3 scale = transform.lossyScale;
        float radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float height = Mathf.Max(controller.height * Mathf.Abs(scale.y), radius * 2f);
        Vector3 center = position + transform.TransformVector(controller.center);
        Vector3 bottom = center - Vector3.up * (height * 0.5f - radius);
        Vector3 top = center + Vector3.up * (height * 0.5f - radius);
        radius = Mathf.Max(0.01f, radius - 0.02f);
        // Ignore our controller and character colliders, but respect all solid obstacles.
        foreach (var overlap in Physics.OverlapCapsule(bottom + travel, top + travel, radius, ~0, QueryTriggerInteraction.Ignore))
            if (!overlap.transform.IsChildOf(transform)) return false;
        if (travel.sqrMagnitude > 0.0001f)
            foreach (var hit in Physics.CapsuleCastAll(bottom, top, radius, travel.normalized,
                         travel.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform)) return false;
        return true;
    }

    bool TickMantle()
    {
        if (!mantling) return false;
        if (CannotAct)
        {
            mantling = false;
            verticalVelocity = 0f;
            return false;
        }

        mantleTimeLeft -= Time.deltaTime;
        if (grappleMantlePath != null)
        {
            while (grappleMantleWaypoint < grappleMantlePath.Length &&
                   Vector3.Distance(transform.position, grappleMantlePath[grappleMantleWaypoint]) < 0.025f)
                grappleMantleWaypoint++;
            if (mantleTimeLeft <= 0f || grappleMantleWaypoint == grappleMantlePath.Length)
            {
                mantling = false;
                grappleMantlePath = null;
                verticalVelocity = 0f;
                return false;
            }
            Vector3 travel = Vector3.ClampMagnitude(grappleMantlePath[grappleMantleWaypoint] - transform.position, 5f * Time.deltaTime);
            controller.Move(travel);
            return true;
        }
        Vector3 toTarget = mantleTarget - transform.position;

        // Nejdriv nahoru, pak dopredu na plochu.
        Vector3 step = toTarget.y > 0.05f
            ? Vector3.up * Mathf.Min(toTarget.y, 7f * Time.deltaTime)
            : Vector3.ClampMagnitude(new Vector3(toTarget.x, 0f, toTarget.z), 5f * Time.deltaTime);

        controller.Move(step);

        if (CannotAct || mantleTimeLeft <= 0f || (mantleTarget - transform.position).magnitude < 0.12f)
        {
            mantling = false;
            verticalVelocity = 0f;
            return false;
        }

        return true;
    }

    void HandleMovement()
    {
        if (TickPull()) return;
        if (AbilityActive || Joining) return;
        if (TickMantle()) return;

        bool frozen = CannotAct;

        bool isGrounded = controller.isGrounded;
        if (isGrounded && verticalVelocity < 0)
        {
            verticalVelocity = -2f;
            abilityFlight = false;
        }

        Vector2 input = Vector2.zero;
        bool running = false;
        bool jump = false;
        bool crouch = false;

        if (!frozen)
        {
            if (Bot != null)
            {
                input = Bot.Move;
                running = Bot.Run;
                jump = Bot.ConsumeJump();
            }
            else
            {
                if (Keyboard.current.wKey.isPressed) input.y += 1;
                if (Keyboard.current.sKey.isPressed) input.y -= 1;
                if (Keyboard.current.dKey.isPressed) input.x += 1;
                if (Keyboard.current.aKey.isPressed) input.x -= 1;

                running = Keyboard.current.leftShiftKey.isPressed && !ShiftReserved;
                jump = Keyboard.current.spaceKey.wasPressedThisFrame;
                crouch = Keyboard.current.leftCtrlKey.isPressed;
            }

            // V pasti: neda se chodit ani skakat (strilet ano).
            if (Rooted)
            {
                input = Vector2.zero;
                jump = false;
            }
        }

        Vector3 move = transform.right * input.x + transform.forward * input.y;
        crouching = crouch;
        move = Vector3.ClampMagnitude(move, 1f);

        // Na zebriku (Ladder) se leze: W nahoru, S dolu; vodorovne jen pomalu, at se z nej hned nesejde.
        bool onLadder = !jump && Ladder.At(transform.position);
        if (onLadder && !isGrounded)
            move *= Ladder.SideSpeedScale;

        if (shooting == null) shooting = GetComponent<WeaponShooting>();
        float drawScale = shooting != null && shooting.IsDrawingBow ? 0.5f : 1f;
        float speed = (running ? runSpeed : walkSpeed) * SpeedMultiplier * HeroSpeedScale * ScopeSpeedScale * AbilitySpeedScale * SlowScale * BoostScale * TunnelScale * HoverScale * BardAbility.SpeedScaleFor(gameObject) * drawScale;
        Vector3 beforeMove = transform.position;
        var flags = controller.Move((move * speed + externalVelocity) * Time.deltaTime);
        Vector3 travelled = transform.position - beforeMove;
        float groundDistance = new Vector2(travelled.x, travelled.z).magnitude;

        // Odhozeni postupne odezni: na zemi rychle (treni), ve vzduchu pomalu; naraz do zdi ho zastavi.
        if (externalVelocity.sqrMagnitude > 0f)
        {
            if ((flags & CollisionFlags.Sides) != 0)
                externalVelocity *= 0.5f;
            externalVelocity = Vector3.MoveTowards(externalVelocity, Vector3.zero, (isGrounded && verticalVelocity <= 0f ? 28f : 5f) * Time.deltaTime);
        }

        if (isGrounded)
            airJumpUsed = false;

        if (jump && isGrounded)
        {
            abilityFlight = false;
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * JumpGravity);
        }
        else if (jump && DoubleJump && !airJumpUsed)
        {
            // Druhy skok ve vzduchu.
            airJumpUsed = true;
            abilityFlight = false;
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * JumpGravity);
            ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.35f);
        }

        // Ve vzduchu a s pohybem dopredu se hrdina chyti hrany a vytahne se na ni.
        if (LedgeClimb && !isGrounded && !frozen && input.y > 0f && TryStartMantle())
            return;

        if (onLadder)
            verticalVelocity = input.y > 0f ? Ladder.ClimbSpeed : input.y < 0f ? -Ladder.ClimbSpeed : 0f;
        else if (Time.time < hoverUntil && verticalVelocity <= 0f && !isGrounded)
            verticalVelocity = Mathf.Max(verticalVelocity + gravity * 0.1f * Time.deltaTime, -1.2f);
        else
        {
            // Keep authored knock-up and hover trajectories on their original gravity.
            float acceleration = abilityFlight ? gravity : JumpGravity;
            if (!abilityFlight && verticalVelocity < 0f)
                acceleration *= Mathf.Max(1f, fallingGravityScale);
            verticalVelocity += acceleration * Time.deltaTime;
        }
        controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);

        // Skluzavka a proud v potoce hrace unasi (WaterCurrent); z toboganu se po vode jede rychleji.
        Vector3 current = WaterCurrent.PushAt(transform.position, ref waterRide, ref waterArmedUntil, out bool surfing);
        if (waterSurfing.Value != surfing) waterSurfing.Value = surfing;
        if (waterRiding.Value != waterRide) waterRiding.Value = waterRide;
        if (current.sqrMagnitude > 0.01f)
            controller.Move(current * Time.deltaTime);

        // Kapsle se pri drepu zkracuje shora: spodek zustava u chodidel, jinak by se postava zaborila do zeme.
        float height = crouch ? crouchHeight : standHeight;
        if (!Mathf.Approximately(controller.height, height))
        {
            controller.height = height;
            controller.center = new Vector3(0f, height * 0.5f, 0f);
        }

        // Sound and camera share distance-based timing, including collision-limited movement.
        if (isGrounded && controller.isGrounded && !jump && !onLadder && !waterRide
            && current.sqrMagnitude <= 0.01f && move.sqrMagnitude > 0.01f && groundDistance > 0.001f)
        {
            gaitSpeed = groundDistance / Mathf.Max(Time.deltaTime, 0.0001f);
            float stride = Mathf.Max(0.1f, footstepDistance);
            stepAccumulator += groundDistance;
            if (stepAccumulator >= stride)
            {
                stepAccumulator %= stride;
                ProceduralSfx.Play(ProceduralSfx.Footstep, transform.position, 0.3f);
            }
        }
    }
}
