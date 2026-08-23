using Unity.Netcode;
using UnityEngine;

public class PlayerRespawn : NetworkBehaviour
{
    public float respawnDelay = 2f;

    CharacterController controller;
    Health health;
    PlayerTeam team;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();
        team = GetComponent<PlayerTeam>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        Transform spawnPoint = FindSpawnPoint();
        if (spawnPoint != null)
            transform.position = spawnPoint.position;
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
        Transform spawnPoint = FindSpawnPoint();

        controller.enabled = false;
        if (spawnPoint != null)
            transform.position = spawnPoint.position;
        controller.enabled = true;
        health.ResetHealth();
        Debug.Log("Respawn.");
    }

    Transform FindSpawnPoint()
    {
        string spawnPointName = $"SpawnPoint_Team{team.teamId.Value}";
        GameObject spawnPointObject = GameObject.Find(spawnPointName);
        return spawnPointObject != null ? spawnPointObject.transform : null;
    }
}
