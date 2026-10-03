using System;
using Unity.Netcode;
using UnityEngine;

public class Health : NetworkBehaviour
{
    public float maxHealth = 100f;
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>();

    public event Action OnDeath;

    BlockAbility block;
    PlayerHero hero;

    void Awake()
    {
        block = GetComponent<BlockAbility>();
        hero = GetComponent<PlayerHero>();
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

    public void TakeDamage(float amount)
    {
        if (!IsServer) return;
        if (currentHealth.Value <= 0f) return;
        if (MatchManager.Instance != null && MatchManager.Instance.IsLobby) return;
        if (hero != null && hero.IsJoining) return;

        if (block != null)
            amount = block.FilterDamage(amount);

        if (amount <= 0f) return;

        currentHealth.Value = Mathf.Max(0f, currentHealth.Value - amount);
        Debug.Log($"{gameObject.name}: {currentHealth.Value}/{maxHealth} HP");
    }

    // Jista smrt (pad mimo mapu): bez ohledu na blok.
    public void Kill()
    {
        if (!IsServer || currentHealth.Value <= 0f) return;
        if (MatchManager.Instance != null && MatchManager.Instance.IsLobby) return;
        if (hero != null && hero.IsJoining) return;

        currentHealth.Value = 0f;
    }

    public void Heal(float amount)
    {
        if (!IsServer) return;
        if (currentHealth.Value <= 0f) return;

        currentHealth.Value = Mathf.Min(maxHealth, currentHealth.Value + amount);
    }

    public void ResetHealth()
    {
        if (!IsServer) return;
        currentHealth.Value = maxHealth;

        if (block != null)
            block.ResetEnergy();
    }
}
