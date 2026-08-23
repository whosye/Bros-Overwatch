using UnityEngine;

public class PlayerRespawn : MonoBehaviour
{
    public Transform spawnPoint;
    public float respawnDelay = 2f;

    CharacterController controller;
    Health health;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();
    }

    void OnEnable()
    {
        health.OnDeath += HandleDeath;
    }

    void OnDisable()
    {
        health.OnDeath -= HandleDeath;
    }

    void HandleDeath()
    {
        Invoke(nameof(Respawn), respawnDelay);
    }

    void Respawn()
    {
        controller.enabled = false;
        transform.position = spawnPoint.position;
        controller.enabled = true;
        health.ResetHealth();
        Debug.Log("Respawn.");
    }
}
