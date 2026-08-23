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

    CharacterController controller;
    Health health;
    float verticalVelocity;
    float cameraPitch;

    void Start()
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
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        transform.Rotate(Vector3.up * mouseDelta.x * mouseSensitivity * Time.deltaTime);

        cameraPitch -= mouseDelta.y * mouseSensitivity * Time.deltaTime;
        cameraPitch = Mathf.Clamp(cameraPitch, -80f, 80f);
        playerCamera.transform.localEulerAngles = new Vector3(cameraPitch, 0f, 0f);
    }

    void HandleMovement()
    {
        if (health != null && health.currentHealth.Value <= 0)
        {
            Debug.Log("Player is dead, cannot move. Spawn dead animation here.");
            return;
        }

        bool isGrounded = controller.isGrounded;
        if (isGrounded && verticalVelocity < 0)
            verticalVelocity = -2f;

        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) input.y += 1;
        if (Keyboard.current.sKey.isPressed) input.y -= 1;
        if (Keyboard.current.dKey.isPressed) input.x += 1;
        if (Keyboard.current.aKey.isPressed) input.x -= 1;

        Vector3 move = transform.right * input.x + transform.forward * input.y;
        move = Vector3.ClampMagnitude(move, 1f);

        float speed = Keyboard.current.leftShiftKey.isPressed ? runSpeed : walkSpeed;
        controller.Move(move * speed * Time.deltaTime);

        if (isGrounded && Keyboard.current.spaceKey.wasPressedThisFrame)
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);

        controller.height = Keyboard.current.leftCtrlKey.isPressed ? crouchHeight : standHeight;
    }
}