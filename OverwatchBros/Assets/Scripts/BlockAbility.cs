using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Blok pravym tlacitkem (Ayran: zkrizene sekery). Drzeni tlacitka zablokuje 'blockAbsorb' (80 %) prichoziho poskozeni,
// pohyb se zpomali na 'blockSpeed' (50 %). Zablokovana cast poskozeni ubira z baru, jehoz velikost je plne zdravi hrdiny;
// kdyz se bar vycerpa, blok prestane platit. Bar se zacne sam obnovovat 'regenDelay' s po poslednim poskozeni.
public class BlockAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    static readonly Color SparkColor = new Color(0.85f, 0.9f, 1f, 1f);

    FirstPersonController fpc;
    Health health;

    bool active;
    float nextVoiceTime;
    float lastDamageTime = -100f;
    float nextFxTime;

    readonly NetworkVariable<bool> blocking = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    readonly NetworkVariable<float> energy = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsBlocking => enabled && ability != null && blocking.Value;
    public float Energy => energy.Value;
    public float MaxEnergy => health != null ? health.maxHealth : 100f;
    public float Fraction => Mathf.Clamp01(energy.Value / Mathf.Max(1f, MaxEnergy));

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        health = GetComponent<Health>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            energy.Value = MaxEnergy;
    }

    void OnDisable()
    {
        if (active)
            StopOwner();
    }

    // Vola Health.ResetHealth (server): plny bar na zacatku zapasu, po respawnu a po zmene hrdiny.
    public void ResetEnergy()
    {
        if (!IsServer || !IsSpawned) return;

        energy.Value = MaxEnergy;
        lastDamageTime = -100f;
    }

    void Update()
    {
        if (IsOwner)
            OwnerUpdate();

        if (IsServer)
            ServerRegen();
    }

    void OwnerUpdate()
    {
        if (ability == null) return;

        bool wants = HeroInput.Held(this, HeroInput.Key.RightMouse) && HeroInput.Locked(this)
            && !fpc.InputBlocked && !fpc.RushActive && energy.Value > 0.01f;

        if (wants && !active)
            StartOwner();
        else if (!wants && active)
            StopOwner();
    }

    void StartOwner()
    {
        active = true;
        fpc.BlockActive = true;
        fpc.SpeedMultiplier = ability.blockSpeed;
        blocking.Value = true;

        // Blok se zapina casto, hlaska jen jednou za cas.
        if (Time.time >= nextVoiceTime)
        {
            nextVoiceTime = Time.time + 8f;
            GetComponent<PlayerHero>().SayAbility(ability);
        }
    }

    void StopOwner()
    {
        active = false;
        fpc.BlockActive = false;
        fpc.SpeedMultiplier = 1f;

        if (IsSpawned && IsOwner)
            blocking.Value = false;
    }

    // ---------------- server ----------------

    // Vola Health.TakeDamage (server). Vraci poskozeni, ktere po bloku zbyva.
    public float FilterDamage(float amount)
    {
        if (!IsServer || amount <= 0f) return amount;

        lastDamageTime = Time.time;

        if (!enabled || ability == null || !blocking.Value || energy.Value <= 0f)
            return amount;

        float absorbed = Mathf.Min(amount * ability.blockAbsorb, energy.Value);
        energy.Value = Mathf.Max(0f, energy.Value - absorbed);

        bool broken = energy.Value <= 0.001f;
        if (broken)
            energy.Value = 0f;

        if (broken || Time.time >= nextFxTime)
        {
            nextFxTime = Time.time + 0.15f;
            BlockFxClientRpc(broken);
        }

        return amount - absorbed;
    }

    void ServerRegen()
    {
        if (!enabled || ability == null) return;
        if (Time.time - lastDamageTime < ability.regenDelay) return;

        float max = MaxEnergy;
        if (energy.Value >= max) return;

        float perSecond = max / Mathf.Max(0.5f, ability.regenTime);
        energy.Value = Mathf.Min(max, energy.Value + perSecond * Time.deltaTime);
    }

    [ClientRpc]
    void BlockFxClientRpc(bool broken)
    {
        Vector3 position = transform.position + transform.forward * 0.7f + Vector3.up * 0.4f;
        Fx.Sparks(position, SparkColor);
        ProceduralSfx.Play(broken ? ProceduralSfx.Explosion : ProceduralSfx.Hit, position, broken ? 0.4f : 0.7f);

        if (broken)
            Fx.BulletImpact(position, SparkColor, 1.6f);
    }
}
