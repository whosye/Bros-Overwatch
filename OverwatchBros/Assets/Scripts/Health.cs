using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    public float maxHealth = 100f;
    public float currentHealth;

    public event Action OnDeath;

    void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        if (currentHealth <= 0f) return;

        currentHealth -= amount;
        Debug.Log($"{gameObject.name}: {currentHealth}/{maxHealth} HP");

        if (currentHealth <= 0f)
        {
            currentHealth = 0f;
            Debug.Log($"{gameObject.name} zemřel.");
            OnDeath?.Invoke();
        }
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
    }
}