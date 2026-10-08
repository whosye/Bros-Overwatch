using System;
using Unity.Netcode;
using UnityEngine;

public class Health : NetworkBehaviour
{
    public float maxHealth = 100f;
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>();

    // Docasny stit nad zdravim (Bardova ultimatka): poskozeni bere nejdriv z nej, sam postupne vyprchava.
    public NetworkVariable<float> shield = new NetworkVariable<float>();
    public static readonly Color ShieldColor = new Color(0.45f, 0.85f, 1f, 1f);
    public float Effective => currentHealth.Value + shield.Value;
    float shieldDecay;
    float shieldHoldUntil;

    public void ServerAddShield(float amount, float seconds)
    {
        if (!IsServer || currentHealth.Value <= 0f || amount <= 0f) return;
        shield.Value = Mathf.Max(shield.Value, amount);
        // Prvni sekundu drzi, pak behem zbytku vyprcha.
        shieldHoldUntil = Time.time + 1f;
        shieldDecay = shield.Value / Mathf.Max(0.5f, seconds - 1f);
    }

    // Samoleceni: kdo 'RegenDelay' s nedostal poskozeni, zacne sam regenerovat 'RegenPerSecond' HP za sekundu.
    public const float RegenDelay = 10f;
    public const float RegenPerSecond = 10f;
    float lastHurtTime = -1000f;

    void Update()
    {
        if (!IsServer) return;

        if (shield.Value > 0f && Time.time >= shieldHoldUntil)
            shield.Value = Mathf.Max(0f, shield.Value - shieldDecay * Time.deltaTime);

        float hp = currentHealth.Value;
        if (hp > 0f && hp < maxHealth && Time.time - lastHurtTime >= RegenDelay && !HealBlocked
            && (MatchManager.Instance == null || !MatchManager.Instance.IsLobby))
            currentHealth.Value = Mathf.Min(maxHealth, hp + RegenPerSecond * Time.deltaTime);
    }

    public event Action OnDeath;

    BlockAbility block;
    PlayerHero hero;
    FirstPersonController controller;

    void Awake()
    {
        block = GetComponent<BlockAbility>();
        hero = GetComponent<PlayerHero>();
        controller = GetComponent<FirstPersonController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            currentHealth.Value = maxHealth;

        currentHealth.OnValueChanged += HandleHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= HandleHealthChanged;
    }

    void HandleHealthChanged(float previous, float current)
    {
        if (current <= 0f)
        {
            Debug.Log($"{gameObject.name} zemřel.");
            OnDeath?.Invoke();
        }
    }

    // Docasne snizeni prijateho poskozeni (Tramal): poskozeni * 'damageTakenScale' do 'damageReductionUntil'.
    float damageReductionUntil, damageTakenScale = 1f;

    public void ServerDamageReduction(float scale, float seconds)
    {
        if (!IsServer) return;
        damageTakenScale = Mathf.Clamp01(scale);
        damageReductionUntil = Time.time + seconds;
    }

    public void TakeDamage(float amount)
    {
        if (!IsServer) return;
        if (currentHealth.Value <= 0f) return;
        if (MatchManager.Instance != null && MatchManager.Instance.IsLobby) return;
        if (hero != null && hero.IsJoining) return;
        if (Time.time < invulnerableUntil) return;

        if (block != null)
            amount = block.FilterDamage(amount);
        if (Time.time < damageReductionUntil)
            amount *= damageTakenScale;

        if (amount <= 0f) return;
        lastHurtTime = Time.time;

        if (shield.Value > 0f)
        {
            float absorbed = Mathf.Min(shield.Value, amount);
            shield.Value -= absorbed;
            amount -= absorbed;
            if (amount <= 0f) return;
        }

        currentHealth.Value = Mathf.Max(0f, currentHealth.Value - amount);
        if (currentHealth.Value <= 0f) shield.Value = 0f;
        Debug.Log($"{gameObject.name}: {currentHealth.Value}/{maxHealth} HP");

        // Uspany hrac se zasahem probudi (Annina uspavaci sipka).
        if (controller != null && currentHealth.Value > 0f)
            controller.ServerWake();
    }

    // Jista smrt (pad mimo mapu): bez ohledu na blok.
    public void Kill()
    {
        if (!IsServer || currentHealth.Value <= 0f) return;
        if (MatchManager.Instance != null && MatchManager.Instance.IsLobby) return;
        if (hero != null && hero.IsJoining) return;

        currentHealth.Value = 0f;
        shield.Value = 0f;
    }

    // Vrati, kolik HP opravdu vylecil (0 = plne zdravi, mrtvy nebo zablokovane leceni).
    public float Heal(float amount)
    {
        if (!IsServer) return 0f;
        if (currentHealth.Value <= 0f || amount <= 0f || HealBlocked) return 0f;

        float before = currentHealth.Value;
        currentHealth.Value = Mathf.Min(maxHealth, currentHealth.Value + amount);
        return currentHealth.Value - before;
    }

    // Nezranitelnost na chvili (Flankeruv navrat v case) a nastaveni zdravi na danou hodnotu (jen server).
    float invulnerableUntil;

    public void ServerInvulnerable(float seconds)
    {
        if (IsServer)
            invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + seconds);
    }

    public void ServerClearInvulnerable()
    {
        if (IsServer)
            invulnerableUntil = 0f;
    }

    public void ServerRestore(float value)
    {
        if (!IsServer || currentHealth.Value <= 0f) return;
        currentHealth.Value = Mathf.Clamp(value, currentHealth.Value, maxHealth);
    }

    // Annin biotický granat: nepratelum chvili nejde nic vylecit (jen server).
    float healBlockedUntil;
    public bool HealBlocked => Time.time < healBlockedUntil;

    public void ServerBlockHealing(float seconds)
    {
        if (IsServer)
            healBlockedUntil = Mathf.Max(healBlockedUntil, Time.time + seconds);
    }

    public void ResetHealth()
    {
        if (!IsServer) return;
        currentHealth.Value = maxHealth;
        shield.Value = 0f;
        healBlockedUntil = 0f;

        if (block != null)
            block.ResetEnergy();
    }
}
