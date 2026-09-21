using Unity.Netcode;
using UnityEngine;

public class PlayerRespawn : NetworkBehaviour
{
    public float respawnDelay = 2f;

    CharacterController controller;
    Health health;
    PlayerTeam team;
    FirstPersonController fpc;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();
        team = GetComponent<PlayerTeam>();
        fpc = GetComponent<FirstPersonController>();
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
        if (MatchManager.Instance != null && MatchManager.Instance.IsOver) return;

        Teleport();
        health.ResetHealth();
        Debug.Log("Respawn.");
    }

    // Vola MatchManager pri restartu zapasu (na vlastniku hrace).
    public void ResetToSpawn()
    {
        CancelInvoke(nameof(Respawn));
        Teleport();
    }

    void Teleport()
    {
        Transform spawnPoint = FindSpawnPoint();

        var leap = GetComponent<LeapStrikeAbility>();
        if (leap != null)
            leap.Cancel();

        controller.enabled = false;
        if (spawnPoint != null)
            transform.position = spawnPoint.position;
        controller.enabled = true;

        if (fpc != null)
            fpc.ResetVertical();
    }

    Transform FindSpawnPoint()
    {
        string spawnPointName = $"SpawnPoint_Team{team.teamId.Value}";
        GameObject spawnPointObject = GameObject.Find(spawnPointName);
        return spawnPointObject != null ? spawnPointObject.transform : null;
    }
}
