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

        if (TryFindSpawn(out Vector3 spawn))
            transform.position = spawn;
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

        var hero = GetComponent<PlayerHero>();
        if (hero != null)
            hero.Say(VoiceKind.Spawn);
    }

    // Vola MatchManager pri restartu zapasu (na vlastniku hrace).
    public void ResetToSpawn()
    {
        CancelInvoke(nameof(Respawn));
        Teleport();
    }

    void Teleport()
    {
        bool hasSpawn = TryFindSpawn(out Vector3 spawn);

        var leap = GetComponent<LeapStrikeAbility>();
        if (leap != null)
            leap.Cancel();

        var rush = GetComponent<RushAbility>();
        if (rush != null)
            rush.Cancel();

        var mine = GetComponent<MineAbility>();
        if (mine != null)
            mine.Cancel();

        var trap = GetComponent<TrapAbility>();
        if (trap != null)
            trap.Cancel();

        var boulder = GetComponent<BoulderAbility>();
        if (boulder != null)
            boulder.Cancel();

        var visor = GetComponent<VisorAbility>();
        if (visor != null)
            visor.Cancel();

        controller.enabled = false;
        if (hasSpawn)
            transform.position = spawn;
        controller.enabled = true;

        if (fpc != null)
        {
            fpc.ResetVertical();
            fpc.ClearForces();
        }
    }

    // Misto oziveni: v dobyvani bodu u aktualniho bodu (kazdy hrac kousek vedle, at nestoji v sobe),
    // jinak zakladna tymu ze sceny.
    bool TryFindSpawn(out Vector3 position)
    {
        var match = MatchManager.Instance;
        if (match != null && match.TryGetSpawn(team.teamId.Value, out position))
        {
            float angle = OwnerClientId * 2.4f;
            position += new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.4f;
            return true;
        }

        string spawnPointName = $"SpawnPoint_Team{team.teamId.Value}";
        GameObject spawnPointObject = GameObject.Find(spawnPointName);
        position = spawnPointObject != null ? spawnPointObject.transform.position : Vector3.zero;
        return spawnPointObject != null;
    }
}
