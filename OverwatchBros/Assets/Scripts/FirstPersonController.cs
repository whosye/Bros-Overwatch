using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : NetworkBehaviour
{
    public Camera playerCamera;

    public float walkSpeed = 5f;
    public float runSpeed = 8f;
    public float jumpHeight = 1.2f;
    public float gravity = -9.81f;
    public float mouseSensitivity = 2f;
    public float standHeight = 2f;
    public float crouchHeight = 1f;
    // Vyska oci nad chodidly (model postavy meri ~1.8 m).
    public float eyeHeight = 1.62f;
    public float crouchEyeHeight = 1.0f;
    public float footstepDistance = 2.2f;

    CharacterController controller;
    Health health;
    PlayerHero hero;
    float verticalVelocity;
    float cameraPitch;
    float stepAccumulator;

    public CharacterController Controller => controller;
    public bool AbilityActive { get; set; }

    // Schopnost, u ktere se dá normalne ovladat pohyb (Modry plamen): kamera ve 3. osobe, zrychleni, Shift neni sprint.
    public bool RushActive { get; set; }

    // Blok (zkrizene sekery): pomalejsi pohyb, nejde utocit.
    public bool BlockActive { get; set; }
    public float SpeedMultiplier { get; set; } = 1f;
    // Zpomaleni pri pribliseni (sniper) a docasne zpomaleni od schopnosti (odkopnuti).
    public float ScopeSpeedScale { get; set; } = 1f;
    float slowUntil;
    float slowFactor = 1f;
    float SlowScale => Time.time < slowUntil ? slowFactor : 1f;

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
    public void OwnerPull(Vector3 destination, float seconds)
    {
        if (!IsOwner || IsDead) return;

        pulling = true;
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
            verticalVelocity = Mathf.Min(Mathf.Max(verticalVelocity, 0f) + impulse.y, 13f);
    }

    // Vyskok se vznasenim (Sindeluv Shift): hned nahoru, po vrcholu pomaly pad, dokud nevyprsi 'seconds'.
    float hoverUntil;

    public void OwnerHover(float upSpeed, float seconds)
    {
        if (!IsOwner || IsDead) return;
        verticalVelocity = Mathf.Max(verticalVelocity, upSpeed);
        hoverUntil = Time.time + seconds;
    }

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
        HudUI.NotifySleep(seconds);
    }

    [ClientRpc]
    void WakeClientRpc()
    {
        SleepMarker.Hide(transform);
        if (!IsOwner) return;

        stunnedUntil = 0f;
        HudUI.EndOverlay();
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
        if (blind)
            HudUI.NotifyFlash(seconds);
    }

    // Pritazeni hakem: hrace to plynule dotahne na dane misto (zdi ho zastavi).
    bool pulling;
    Vector3 pullTarget;
    float pullSpeed;
    float pullTimeLeft;

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

        pullTimeLeft -= Time.deltaTime;
        Vector3 toTarget = pullTarget - transform.position;
        float step = pullSpeed * Time.deltaTime;

        if (IsDead || pullTimeLeft <= 0f || toTarget.magnitude <= Mathf.Max(0.25f, step))
        {
            pulling = false;
            verticalVelocity = 0f;
            return false;
        }

        controller.Move(toTarget.normalized * step);
        return true;
    }

    public void ClearForces()
    {
        pulling = false;
        mantling = false;
        externalVelocity = Vector3.zero;
        rootedUntil = 0f;
        stunnedUntil = 0f;
    }

    public void ResetVertical()
    {
        verticalVelocity = 0f;
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();
        hero = GetComponent<PlayerHero>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            playerCamera.gameObject.SetActive(false);
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        playerCamera.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
    }

    void Update()
    {
        if (!IsOwner) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            Cursor.lockState = CursorLockMode.None;

        HandleLook();
        HandleMovement();
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
        mantleTarget = top.point + Vector3.up * 0.05f;
        mantleTimeLeft = 0.6f;
        verticalVelocity = 0f;
        externalVelocity = Vector3.zero;
        return true;
    }

    bool TickMantle()
    {
        if (!mantling) return false;

        mantleTimeLeft -= Time.deltaTime;
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
            verticalVelocity = -2f;

        Vector2 input = Vector2.zero;
        bool running = false;
        bool jump = false;
        bool crouch = false;

        if (!frozen)
        {
            if (Keyboard.current.wKey.isPressed) input.y += 1;
            if (Keyboard.current.sKey.isPressed) input.y -= 1;
            if (Keyboard.current.dKey.isPressed) input.x += 1;
            if (Keyboard.current.aKey.isPressed) input.x -= 1;

            running = Keyboard.current.leftShiftKey.isPressed && !ShiftReserved;
            jump = Keyboard.current.spaceKey.wasPressedThisFrame;
            crouch = Keyboard.current.leftCtrlKey.isPressed;

            // V pasti: neda se chodit ani skakat (strilet ano).
            if (Rooted)
            {
                input = Vector2.zero;
                jump = false;
            }
        }

        Vector3 move = transform.right * input.x + transform.forward * input.y;
        move = Vector3.ClampMagnitude(move, 1f);

        float speed = (running ? runSpeed : walkSpeed) * SpeedMultiplier * ScopeSpeedScale * SlowScale * BardAbility.SpeedScaleFor(gameObject);
        var flags = controller.Move((move * speed + externalVelocity) * Time.deltaTime);

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
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        else if (jump && DoubleJump && !airJumpUsed)
        {
            // Druhy skok ve vzduchu.
            airJumpUsed = true;
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.35f);
        }

        // Ve vzduchu a s pohybem dopredu se hrdina chyti hrany a vytahne se na ni.
        if (LedgeClimb && !isGrounded && !frozen && input.y > 0f && TryStartMantle())
            return;

        if (Time.time < hoverUntil && verticalVelocity <= 0f && !isGrounded)
            verticalVelocity = Mathf.Max(verticalVelocity + gravity * 0.1f * Time.deltaTime, -1.2f);
        else
            verticalVelocity += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);

        // Kapsle se pri drepu zkracuje shora: spodek zustava u chodidel, jinak by se postava zaborila do zeme.
        float height = crouch ? crouchHeight : standHeight;
        if (!Mathf.Approximately(controller.height, height))
        {
            controller.height = height;
            controller.center = new Vector3(0f, height * 0.5f, 0f);
        }

        // Kameru ve 3. osobe ridi schopnosti, tady jen vyska oci v 1. osobe.
        if (!ThirdPerson)
        {
            Vector3 eye = playerCamera.transform.localPosition;
            float targetEye = crouch ? crouchEyeHeight : eyeHeight;
            playerCamera.transform.localPosition = new Vector3(0f, Mathf.MoveTowards(eye.y, targetEye, 5f * Time.deltaTime), 0f);
        }

        if (isGrounded && move.sqrMagnitude > 0.01f)
        {
            stepAccumulator += speed * move.magnitude * Time.deltaTime;
            if (stepAccumulator >= footstepDistance)
            {
                stepAccumulator = 0f;
                ProceduralSfx.Play(ProceduralSfx.Footstep, transform.position, 0.3f);
            }
        }
    }
}
