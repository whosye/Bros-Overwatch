using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using System.Collections;

public class DashAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    CharacterController controller;
    float nextDashTime;
    bool isDashing;

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (!IsOwner) return;

        if (Keyboard.current.qKey.wasPressedThisFrame && Time.time >= nextDashTime && !isDashing)
            StartCoroutine(DashRoutine());
    }

    IEnumerator DashRoutine()
    {
        isDashing = true;
        nextDashTime = Time.time + ability.cooldown;

        Vector3 direction = transform.forward;

        Debug.Log($"{ability.abilityName} použit!");

        if (ability.duration <= 0f)
        {
            controller.Move(direction * ability.power);
            isDashing = false;
            yield break;
        }

        float speed = ability.power / ability.duration;
        float elapsed = 0f;

        while (elapsed < ability.duration)
        {
            controller.Move(direction * speed * Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        isDashing = false;
    }
}
