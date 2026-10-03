using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Mirkova rychlopalba (prave tlacitko mysi): pristich 'charges' sipu behem 'duration' sekund leti hned plnou rychlosti
// bez natahovani, kazdy za 'power' poskozeni. Strili se drzenim leveho tlacitka. Cooldown bezi od konce.
public class RapidFireAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    // Rozestup mezi sipy pri rychlopalbe.
    public float interval = 0.22f;

    FirstPersonController fpc;

    bool active;
    int shotsLeft;
    float endTime;
    float nextUseTime;

    public bool IsActive => active;
    public int ShotsLeft => active ? shotsLeft : 0;
    public float Damage => ability != null ? ability.power : 0f;
    public float CooldownRemaining => active ? 0f : Mathf.Max(0f, nextUseTime - Time.time);

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
    }

    void OnDisable()
    {
        active = false;
    }

    void Update()
    {
        if (!IsOwner || ability == null) return;

        if (active)
        {
            if (Time.time >= endTime || shotsLeft <= 0 || fpc.IsDead)
                End();
            return;
        }

        if (!Mouse.current.rightButton.wasPressedThisFrame || !GameSettings.CursorLocked) return;
        if (fpc.InputBlocked || Time.time < nextUseTime) return;

        active = true;
        shotsLeft = Mathf.Max(1, ability.charges);
        endTime = Time.time + ability.duration;
        ProceduralSfx.Play(ProceduralSfx.Reload, transform.position, 0.7f);
        GetComponent<PlayerHero>().SayAbility(ability);
    }

    // Vola WeaponShooting po kazdem sipu vystrelenem v rychlopalbe.
    public void ConsumeShot()
    {
        if (!active) return;

        shotsLeft--;
        if (shotsLeft <= 0)
            End();
    }

    void End()
    {
        active = false;
        nextUseTime = Time.time + (ability != null ? ability.Cooldown : 0f);
    }

    // Vola PlayerRespawn / reset kola.
    public void Cancel()
    {
        active = false;
    }
}
