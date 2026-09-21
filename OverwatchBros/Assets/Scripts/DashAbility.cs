using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using System.Collections;

public class DashAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    CharacterController controller;
    FirstPersonController fpc;
    float nextDashTime;
    bool isDashing;

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    public string StatusText()
    {
        if (ability == null) return "";

        float remaining = nextDashTime - Time.time;
        return remaining > 0f
            ? $"[Q] {ability.abilityName}: {remaining:0.0}s"
            : $"[Q] {ability.abilityName}: PŘIPRAVENO";
    }

    void Start()
    {
        controller = GetComponent<CharacterController>();
        fpc = GetComponent<FirstPersonController>();
    }

    void Update()
    {
        if (!IsOwner || ability == null) return;
        if (fpc != null && fpc.InputBlocked) return;

        if (Keyboard.current.qKey.wasPressedThisFrame && Time.time >= nextDashTime && !isDashing)
            StartCoroutine(DashRoutine());
    }

    [ServerRpc]
    void DashFxServerRpc()
    {
        DashFxClientRpc();
    }

    [ClientRpc]
    void DashFxClientRpc()
    {
        if (IsOwner) return;
        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.7f);
    }

    IEnumerator DashRoutine()
    {
        isDashing = true;
        nextDashTime = Time.time + ability.cooldown;

        Vector3 direction = transform.forward;

        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.7f);
        DashFxServerRpc();

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
            if (fpc != null && fpc.CannotAct) break;

            controller.Move(direction * speed * Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        isDashing = false;
    }
}
