using UnityEngine;
using UnityEngine.InputSystem;
public class PlayerMovement : MonoBehaviour
{
    public float moveSpeed = 5f;
    void Start() { }
    void Update()
    {
        Vector3 direction = Vector3.zero;

        if (Keyboard.current.upArrowKey.isPressed) direction += Vector3.forward;
        if (Keyboard.current.downArrowKey.isPressed) direction += Vector3.back;
        if (Keyboard.current.leftArrowKey.isPressed) direction += Vector3.left;
        if (Keyboard.current.rightArrowKey.isPressed) direction += Vector3.right;

        transform.Translate(direction * moveSpeed * Time.deltaTime, Space.World);
    } 
}
    