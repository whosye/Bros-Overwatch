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
    public bool ShiftReserved { get; set; }
    public bool ThirdPerson => AbilityActive || RushActive;
    public bool IsDead => health != null && health.currentHealth.Value <= 0f;
    public bool MatchOver => MatchManager.Instance != null && MatchManager.Instance.IsOver;
    public bool InLobby => MatchManager.Instance != null && MatchManager.Instance.IsLobby;
    // Hrac se pripojil do rozehraneho zapasu a jeste si vybira tym / hrdinu.
    public bool Joining => hero != null && hero.IsJoining;
    public bool CannotAct => IsDead || MatchOver || InLobby || Joining || Stunned;
    public bool InputBlocked => CannotAct || AbilityActive;

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

    // Omraceni (oslepujici granat): hrac se chvili nemuze hybat, utocit ani pouzivat schopnosti; rozbehle schopnosti se prerusi.
    float stunnedUntil;
    public bool Stunned => Time.time < stunnedUntil;

    public void ServerStun(float seconds, GameObject attacker = null)
    {
        if (!IsServer) return;

        var attackerObject = attacker != null ? attacker.GetComponent<NetworkObject>() : null;
        StunClientRpc(seconds, attackerObject != null ? attackerObject.OwnerClientId : ulong.MaxValue);
        if (hero != null)
            hero.Say(VoiceKind.Snare);
    }

    [ClientRpc]
    void StunClientRpc(float seconds, ulong attackerClientId)
    {
        // Zvuk omraceni slysi vsichni v okoli; ten, kdo trefil, dostane navic potvrzeni (i kdyz je daleko).
        ProceduralSfx.Play(ProceduralSfx.Stun, transform.position + Vector3.up * 1.5f, 1f);
        if (NetworkManager.LocalClientId == attackerClientId && Camera.main != null)
            ProceduralSfx.Play(ProceduralSfx.StunConfirm, Camera.main.transform.position, 0.9f);

        if (!IsOwner || IsDead) return;

        stunnedUntil = Time.time + seconds;
        externalVelocity = Vector3.zero;
        HudUI.NotifyFlash(seconds);
    }

    public void ClearForces()
    {
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

        float sensitivity = mouseSensitivity * GameSettings.Sensitivity;

        transform.Rotate(Vector3.up * mouseDelta.x * sensitivity * Time.deltaTime);

        cameraPitch -= mouseDelta.y * sensitivity * Time.deltaTime;
        cameraPitch = Mathf.Clamp(cameraPitch, -80f, 80f);
        playerCamera.transform.localEulerAngles = new Vector3(cameraPitch, 0f, 0f);
    }

    void HandleMovement()
    {
        if (AbilityActive || Joining) return;

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

        float speed = (running ? runSpeed : walkSpeed) * SpeedMultiplier;
        var flags = controller.Move((move * speed + externalVelocity) * Time.deltaTime);

        // Odhozeni postupne odezni: na zemi rychle (treni), ve vzduchu pomalu; naraz do zdi ho zastavi.
        if (externalVelocity.sqrMagnitude > 0f)
        {
            if ((flags & CollisionFlags.Sides) != 0)
                externalVelocity *= 0.5f;
            externalVelocity = Vector3.MoveTowards(externalVelocity, Vector3.zero, (isGrounded && verticalVelocity <= 0f ? 28f : 5f) * Time.deltaTime);
        }

        if (jump && isGrounded)
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

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
