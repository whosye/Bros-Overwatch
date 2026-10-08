using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Flankeruv skok (Shift): okamzite se premisti o 'power' metru ve smeru pohybu (bez pohybu dopredu).
// Ma 'charges' nabiti, kazde se obnovi za 'cooldown' sekund (jedno po druhem). Zdi neprojde - zastavi se o ne.
public class BlinkAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    CharacterController controller;
    FirstPersonController fpc;
    int charges;
    float rechargeAt;

    public int Charges => charges;
    public int MaxCharges => ability != null ? Mathf.Max(1, ability.charges) : 1;
    public float CooldownRemaining => charges > 0 ? 0f : Mathf.Max(0f, rechargeAt - Time.time);

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
        charges = MaxCharges;
        rechargeAt = 0f;
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        fpc = GetComponent<FirstPersonController>();
    }

    void Update()
    {
        if (!IsOwner || ability == null) return;

        // Nabiti se doplnuji jedno po druhem.
        if (charges < MaxCharges && Time.time >= rechargeAt)
        {
            charges++;
            if (charges < MaxCharges)
                rechargeAt = Time.time + ability.Cooldown;
        }

        if (!HeroInput.Pressed(this, HeroInput.Key.Shift) || !HeroInput.Locked(this)) return;
        if (charges <= 0 || fpc.InputBlocked || fpc.Rooted) return;

        if (charges == MaxCharges)
            rechargeAt = Time.time + ability.Cooldown;
        charges--;

        Vector3 from = transform.position;
        controller.Move(Direction() * ability.power);

        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.6f);
        GetComponent<PlayerHero>().SayAbility(ability);
        BlinkServerRpc(from + Vector3.up * 1f, transform.position + Vector3.up * 1f);
    }

    // Smer podle drzenych klaves pohybu (vodorovne); bez pohybu dopredu.
    Vector3 Direction()
    {
        Vector2 input = Vector2.zero;
        if (HeroInput.MoveKey(this, 'w')) input.y += 1f;
        if (HeroInput.MoveKey(this, 's')) input.y -= 1f;
        if (HeroInput.MoveKey(this, 'd')) input.x += 1f;
        if (HeroInput.MoveKey(this, 'a')) input.x -= 1f;

        Vector3 direction = transform.right * input.x + transform.forward * input.y;
        return direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
    }

    [ServerRpc]
    void BlinkServerRpc(Vector3 from, Vector3 to)
    {
        BlinkClientRpc(from, to);
    }

    [ClientRpc]
    void BlinkClientRpc(Vector3 from, Vector3 to)
    {
        // Modra stopa mezi puvodnim a novym mistem.
        Fx.Tracer(from, to, new Color(0.4f, 0.75f, 1f), true);
        Fx.Sparks(from, new Color(0.4f, 0.75f, 1f));
        if (!IsOwner)
            ProceduralSfx.Play(ProceduralSfx.Dash, to, 0.6f);
    }
}
