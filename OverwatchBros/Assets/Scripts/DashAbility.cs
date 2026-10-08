using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using System.Collections;

// Viktoruv uskok (Left Shift; u starsich hrdinu na Q): kratky rychly uskok ve smeru pohybu (bez pohybu dopredu).
// Uskok na Shiftu zaroven prebije zbran (jako Cassidyho kotoul).
public class DashAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    const float PressBuffer = 0.4f;

    CharacterController controller;
    FirstPersonController fpc;
    float nextDashTime;
    float bufferedUntil;
    bool isDashing;
    bool onShift;

    public float CooldownRemaining => Mathf.Max(0f, nextDashTime - Time.time);
    public bool IsActive => isDashing;

    public void Configure(AbilityDefinition definition, bool useShift = false)
    {
        ability = definition;
        onShift = useShift;
    }

    public string StatusText()
    {
        if (ability == null) return "";

        string key = onShift ? "SHIFT" : "Q";
        float remaining = nextDashTime - Time.time;
        return remaining > 0f
            ? $"[{key}] {ability.abilityName}: {remaining:0.0}s"
            : $"[{key}] {ability.abilityName}: PŘIPRAVENO";
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        fpc = GetComponent<FirstPersonController>();
    }

    void Update()
    {
        if (!IsOwner || ability == null) return;

        // Stisk se chvili pamatuje, aby zmacknuti tesne pred koncem cooldownu nepropadlo.
        if (HeroInput.Pressed(this, onShift ? HeroInput.Key.Shift : HeroInput.Key.Q) && HeroInput.Locked(this))
            bufferedUntil = Time.time + PressBuffer;

        if (Time.time > bufferedUntil || Time.time < nextDashTime || isDashing) return;
        if (fpc.InputBlocked || fpc.Rooted) return;

        bufferedUntil = 0f;
        StartCoroutine(DashRoutine());
    }

    // Smer podle drzenych klaves pohybu; kdyz hrac stoji, uskoci dopredu.
    Vector3 DashDirection()
    {
        Vector2 input = Vector2.zero;
        if (HeroInput.MoveKey(this, 'w')) input.y += 1f;
        if (HeroInput.MoveKey(this, 's')) input.y -= 1f;
        if (HeroInput.MoveKey(this, 'd')) input.x += 1f;
        if (HeroInput.MoveKey(this, 'a')) input.x -= 1f;

        Vector3 direction = transform.right * input.x + transform.forward * input.y;
        return direction.sqrMagnitude > 0.01f && onShift ? direction.normalized : transform.forward;
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
        nextDashTime = Time.time + ability.Cooldown;

        Vector3 direction = DashDirection();

        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.7f);
        DashFxServerRpc();
        GetComponent<PlayerHero>().SayAbility(ability);

        // Vyskok (Mirek): k uskoku se prida i odraz nahoru ('knockback' = rychlost nahoru).
        if (ability.knockback > 0f)
            fpc.AddImpulse(Vector3.up * ability.knockback);

        if (onShift)
        {
            var shooting = GetComponent<WeaponShooting>();
            if (shooting != null)
                shooting.InstantReload();
        }

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
            if (fpc.CannotAct) break;

            controller.Move(direction * speed * Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        isDashing = false;
    }
}
