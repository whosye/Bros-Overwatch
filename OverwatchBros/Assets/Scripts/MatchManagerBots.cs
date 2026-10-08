using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Boti (BotBrain): pridava a odebira je host v lobby. Bot je normalni postava hrace vlastnena serverem.
public partial class MatchManager
{
    public const int MaxBots = 10;

    public void ServerAddBot(int heroIndex, int teamIndex)
    {
        if (!IsServer || BotBrain.All.Count >= MaxBots) return;
        var definition = HeroRegistry.Get(heroIndex);
        var prefab = NetworkManager.NetworkConfig.PlayerPrefab;
        if (definition == null || prefab == null) return;

        BotNavigation.EnsureBuilt();

        var go = Instantiate(prefab, new Vector3(0f, 400f, 0f), Quaternion.identity);
        go.AddComponent<BotBrain>();   // jeste pred spawnem: vsechny skripty uz vedi, ze je to bot
        go.GetComponent<NetworkObject>().Spawn(true);

        var hero = go.GetComponent<PlayerHero>();
        hero.heroId.Value = heroIndex;
        hero.isBot.Value = true;
        hero.playerName.Value = new FixedString32Bytes(BotName(definition.heroName));
        hero.ServerClearJoining();

        var team = go.GetComponent<PlayerTeam>();
        if (team != null) team.teamId.Value = Mathf.Clamp(teamIndex, 0, PlayerTeam.TeamCount - 1);

        var respawn = go.GetComponent<PlayerRespawn>();
        if (respawn != null) respawn.ResetToSpawn();
        Debug.Log($"[Boti] Pridan bot {definition.heroName} do tymu {teamIndex}.");
    }

    public void ServerRemoveBots()
    {
        if (!IsServer) return;
        foreach (var bot in BotBrain.All.ToArray())
            if (bot != null && bot.TryGetComponent<NetworkObject>(out var networkObject) && networkObject.IsSpawned)
                networkObject.Despawn(true);
    }

    static string BotName(string heroName)
    {
        string name = "Bot " + heroName;
        return name.Length > 20 ? name.Substring(0, 20) : name;
    }

    // Host: boti se pri resetu kola vrati na spawn jako ostatni hraci (ti to delaji sami u sebe).
    void ServerResetBots()
    {
        foreach (var bot in BotBrain.All)
            if (bot != null && bot.TryGetComponent<PlayerRespawn>(out var respawn))
                respawn.ResetToSpawn();
    }

    // Vsichni hraci ve hre: pripojeni lide i boti (jako ConnectedClientsList, ale i s boty).
    public readonly struct PlayerSlot
    {
        public readonly NetworkObject PlayerObject;
        public PlayerSlot(NetworkObject playerObject) => PlayerObject = playerObject;
    }

    // (pokazde novy seznam - smycky pres hrace se muzou vnorit, napr. zabiti behem smycky)
    public static System.Collections.Generic.List<PlayerSlot> PlayerSlots()
    {
        var slots = new System.Collections.Generic.List<PlayerSlot>();
        foreach (var hero in FindObjectsByType<PlayerHero>())
            if (hero != null && hero.IsSpawned)
                slots.Add(new PlayerSlot(hero.NetworkObject));
        return slots;
    }
}
