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
    public float footstepDistance = 2.2f;

    CharacterController controller;
    Health health;
    float verticalVelocity;
    float cameraPitch;
    float stepAccumulator;

    public CharacterController Controller => controller;
    public bool AbilityActive { get; set; }
    public bool IsDead => health != null && health.currentHealth.Value <= 0f;
    public bool MatchOver => MatchManager.Instance != null && MatchManager.Instance.IsOver;
    public bool InLobby => MatchManager.Instance != null && MatchManager.Instance.IsLobby;
    public bool CannotAct => IsDead || MatchOver || InLobby;
    public bool InputBlocked => CannotAct || AbilityActive;

    public void ResetVertical()
    {
        verticalVelocity = 0f;
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            playerCamera.gameObject.SetActive(false);
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
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
        if (AbilityActive) return;

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

            running = Keyboard.current.leftShiftKey.isPressed;
            jump = Keyboard.current.spaceKey.wasPressedThisFrame;
            crouch = Keyboard.current.leftCtrlKey.isPressed;
        }

        Vector3 move = transform.right * input.x + transform.forward * input.y;
        move = Vector3.ClampMagnitude(move, 1f);

        float speed = running ? runSpeed : walkSpeed;
        controller.Move(move * speed * Time.deltaTime);

        if (jump && isGrounded)
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);

        controller.height = crouch ? crouchHeight : standHeight;

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
