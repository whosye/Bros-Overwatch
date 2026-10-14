using Unity.Netcode;
using UnityEngine;

// Honzuv karburator a traktor u chaty (Tractor). Kdo sebere karburator (balicek), ma ho 'CarburetorSeconds' s
// u sebe (pri smrti o nej prijde). S nim muze u traktoru klavesou R nastartovat (karburator se tim spotrebuje)
// a traktor ridit (WASD, ridi vlastnik - FirstPersonController). Spoluhrac ridice muze klavesou R naskocit na vlek
// a strilet z nej vlastni zbrani. Oba jsou v traktoru nezranitelni. Traktor jede 'TractorSeconds' s, pak se karburator
// rozbije a oba vystoupi (traktor zustane stat, kde ho nechali). Kdo je traktoru v ceste, toho prejede: poskozeni
// podle rychlosti, zabiti se pripise ridici. Ridic muze vystoupit driv (R) - jizda tim konci.
public partial class MatchManager
{
    public const float CarburetorSeconds = 60f;
    public const float TractorSeconds = 60f;
    const ulong Nobody = ulong.MaxValue;
    const float TractorUseDistance = 4f;

    // kdo ma karburator a kolik mu zbyva (server zapisuje, klienti ctou - HUD a vyzva u traktoru)
    public NetworkVariable<ulong> carburetorHolder = new NetworkVariable<ulong>(Nobody);
    public NetworkVariable<float> carburetorLeft = new NetworkVariable<float>(0f);

    // kdo traktor ridi; kde traktor stoji, kdyz ho uz nekdo odstavil (xyz + natoceni)
    public NetworkVariable<ulong> tractorDriver = new NetworkVariable<ulong>(Nobody);
    public NetworkVariable<ulong> tractorPassenger = new NetworkVariable<ulong>(Nobody);
    public NetworkVariable<float> tractorLeft = new NetworkVariable<float>(0f);   // kolik jizdy zbyva
    public NetworkVariable<bool> tractorMoved = new NetworkVariable<bool>(false);
    public NetworkVariable<Vector4> tractorPose = new NetworkVariable<Vector4>();

    float carburetorEnd;
    float tractorEnd, nextTractorSync;
    float nextCarburetorSync;
    Vector3 lastTractorPos;
    bool hasLastTractorPos;
    readonly System.Collections.Generic.Dictionary<ulong, float> runOverAt = new System.Collections.Generic.Dictionary<ulong, float>();

    public bool HasCarburetor(ulong playerId) => carburetorHolder.Value == playerId && carburetorLeft.Value > 0f;
    public bool IsDriving(ulong playerId) => tractorDriver.Value == playerId;
    public bool IsRiding(ulong playerId) => playerId != Nobody && tractorPassenger.Value == playerId;
    public bool TractorRunning => tractorDriver.Value != Nobody;

    // Mistni hrac: do kdy ma karburator (pro HUD).
    public static float LocalCarburetorUntil
    {
        get
        {
            var match = Instance;
            var network = NetworkManager.Singleton;
            var local = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
            if (match == null || local == null || !match.HasCarburetor(local.NetworkObjectId)) return 0f;
            return Time.time + match.carburetorLeft.Value;
        }
    }

    void ServerResetTractor()
    {
        if (!IsServer) return;
        carburetorHolder.Value = Nobody;
        carburetorLeft.Value = 0f;
        tractorDriver.Value = Nobody;
        tractorPassenger.Value = Nobody;
        tractorLeft.Value = 0f;
        tractorMoved.Value = false;
        hasLastTractorPos = false;
        runOverAt.Clear();
    }

    // Balicek: karburator dostane hrac, ktery na nej vstoupil.
    void ServerGiveCarburetor(PlayerHero hero)
    {
        carburetorHolder.Value = hero.NetworkObjectId;
        carburetorEnd = Time.time + CarburetorSeconds;
        carburetorLeft.Value = CarburetorSeconds;
        nextCarburetorSync = Time.time + 0.5f;
    }

    // R u traktoru: nastartovat (s karburatorem) / vystoupit.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TractorRpc(RpcParams rpcParams = default)
    {
        if (IsLobby || IsOver) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out var client)) return;
        var player = client.PlayerObject;
        if (player == null) return;

        // ridic vystoupi (jizda konci)
        if (tractorDriver.Value == player.NetworkObjectId)
        {
            ServerParkTractor(false);
            return;
        }
        // spolujezdec seskoci
        if (tractorPassenger.Value == player.NetworkObjectId)
        {
            tractorPassenger.Value = Nobody;
            return;
        }

        var tractor = Tractor.Instance;
        var health = player.GetComponent<Health>();
        if (tractor == null || health == null || health.currentHealth.Value <= 0f) return;

        // naskocit na vlek (jen spoluhrac ridice)
        if (tractorDriver.Value != Nobody)
        {
            var driver = SpawnedPlayer(tractorDriver.Value);
            if (tractorPassenger.Value != Nobody || driver == null || !Combat.SameTeam(driver.gameObject, player.gameObject)) return;
            if (Vector3.Distance(player.transform.position, tractor.SeatPosition) > TractorUseDistance + 2.5f
                && Vector3.Distance(player.transform.position, tractor.CabPosition) > TractorUseDistance + 2.5f) return;
            tractorPassenger.Value = player.NetworkObjectId;
            return;
        }
        if (!HasCarburetor(player.NetworkObjectId)) return;
        // rezerva na zpozdeni pohybu po siti
        if (Vector3.Distance(player.transform.position, tractor.CabPosition) > TractorUseDistance + 1.5f) return;

        carburetorHolder.Value = Nobody;   // karburator se namontuje do traktoru
        carburetorLeft.Value = 0f;
        tractorDriver.Value = player.NetworkObjectId;
        tractorPassenger.Value = Nobody;
        tractorEnd = Time.time + TractorSeconds;
        tractorLeft.Value = TractorSeconds;
        nextTractorSync = Time.time + 0.5f;
        hasLastTractorPos = false;
        TractorStartedClientRpc(player.NetworkObjectId);
    }

    [ClientRpc]
    void TractorStartedClientRpc(ulong driverId)
    {
        var tractor = Tractor.Instance;
        if (tractor == null) return;
        ProceduralSfx.Play(ProceduralSfx.Explosion, tractor.CabPosition, 0.35f);
        string name = NameOfPlayer(driverId);
        CaptureUI.Announce($"{name} nastartoval rolbu!", new Color(0.85f, 0.25f, 0.15f), 2.5f);
    }

    static string NameOfPlayer(ulong networkObjectId)
    {
        var network = NetworkManager.Singleton;
        if (network != null && network.SpawnManager != null && network.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var obj))
        {
            var hero = obj.GetComponent<PlayerHero>();
            if (hero != null) return hero.DisplayName;
        }
        return "Někdo";
    }

    // Ridic vystoupil / zemrel / odpojil se, nebo dosla jizda (karburator se rozbil): oba vystoupi, traktor zustane stat.
    void ServerParkTractor(bool broke)
    {
        if (broke && tractorDriver.Value != Nobody)
            TractorBrokeClientRpc();
        var tractor = Tractor.Instance;
        if (tractor != null)
        {
            Vector3 p = tractor.transform.position;
            tractorPose.Value = new Vector4(p.x, p.y, p.z, tractor.transform.eulerAngles.y);
            tractorMoved.Value = true;
        }
        tractorDriver.Value = Nobody;
        tractorPassenger.Value = Nobody;
        tractorLeft.Value = 0f;
        hasLastTractorPos = false;
    }

    [ClientRpc]
    void TractorBrokeClientRpc()
    {
        var tractor = Tractor.Instance;
        if (tractor == null) return;
        ProceduralSfx.Play(ProceduralSfx.Explosion, tractor.CabPosition, 0.5f);
        Fx.Sparks(tractor.CabPosition + Vector3.up * 2f, new Color(0.3f, 0.3f, 0.32f));
        CaptureUI.Announce("Honzův karburátor se rozbil - rolba stojí", new Color(0.95f, 0.45f, 0.15f), 3f);
    }

    void ServerTractorTick()
    {
        // karburator: vyprsi nebo o nej drzitel prijde smrti
        if (carburetorHolder.Value != Nobody)
        {
            var holder = SpawnedPlayer(carburetorHolder.Value);
            var holderHealth = holder != null ? holder.GetComponent<Health>() : null;
            if (holderHealth == null || holderHealth.currentHealth.Value <= 0f || Time.time >= carburetorEnd || IsOver)
            {
                carburetorHolder.Value = Nobody;
                carburetorLeft.Value = 0f;
            }
            else if (Time.time >= nextCarburetorSync)
            {
                nextCarburetorSync = Time.time + 0.5f;
                carburetorLeft.Value = Mathf.Max(0f, carburetorEnd - Time.time);
            }
        }

        if (tractorDriver.Value == Nobody) return;
        var driver = SpawnedPlayer(tractorDriver.Value);
        var driverHealth = driver != null ? driver.GetComponent<Health>() : null;
        var tractor = Tractor.Instance;
        if (driverHealth == null || driverHealth.currentHealth.Value <= 0f || tractor == null || IsOver)
        {
            ServerParkTractor(false);
            return;
        }
        if (Time.time >= tractorEnd)
        {
            ServerParkTractor(true);
            return;
        }
        if (Time.time >= nextTractorSync)
        {
            nextTractorSync = Time.time + 0.5f;
            tractorLeft.Value = Mathf.Max(0f, tractorEnd - Time.time);
        }

        // v traktoru jsou nezranitelni (obnovuje se kazdy snimek)
        driverHealth.ServerInvulnerable(0.3f);
        NetworkObject passenger = null;
        if (tractorPassenger.Value != Nobody)
        {
            passenger = SpawnedPlayer(tractorPassenger.Value);
            var passengerHealth = passenger != null ? passenger.GetComponent<Health>() : null;
            if (passengerHealth == null || passengerHealth.currentHealth.Value <= 0f) tractorPassenger.Value = Nobody;
            else passengerHealth.ServerInvulnerable(0.3f);
        }

        // prejeti: kdo je v obrysu traktoru (s vlekem), kdyz jede
        Vector3 pos = tractor.transform.position;
        float speed = hasLastTractorPos && Time.deltaTime > 0f ? Vector3.Distance(pos, lastTractorPos) / Time.deltaTime : 0f;
        lastTractorPos = pos;
        hasLastTractorPos = true;
        if (speed < Tractor.RunOverSpeed) return;

        foreach (var client in PlayerSlots())
        {
            var player = client.PlayerObject;
            if (player == null || player == driver || player == passenger) continue;
            var health = player.GetComponent<Health>();
            if (health == null || health.currentHealth.Value <= 0f) continue;
            if (!tractor.Contains(player.transform.position)) continue;
            if (runOverAt.TryGetValue(player.NetworkObjectId, out float last) && Time.time - last < 1.2f) continue;   // jedno najeti = jeden zasah
            runOverAt[player.NetworkObjectId] = Time.time;

            Vector3 away = player.transform.position - pos;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : tractor.transform.right;
            var movement = player.GetComponent<FirstPersonController>();
            bool ally = Combat.SameTeam(driver.gameObject, player.gameObject);
            if (!ally)
            {
                // najeti predkem (radlice) i zadkem (freza) - pevne poskozeni za kazde najeti
                Combat.DamagePlayer(driver.gameObject, health, Tractor.RunOverDamage, "env:rolba");
            }
            if (movement != null && health.currentHealth.Value > 0f)
                movement.ServerKnockback(away * (ally ? 4f : speed * 1.3f) + Vector3.up * (ally ? 2f : 6f));
            TractorHitClientRpc(player.transform.position);
        }
    }

    [ClientRpc]
    void TractorHitClientRpc(Vector3 position)
    {
        ProceduralSfx.Play(ProceduralSfx.Hit, position, 1f);
        Fx.Sparks(position + Vector3.up, new Color(0.85f, 0.25f, 0.15f));
    }

    NetworkObject SpawnedPlayer(ulong id)
    {
        return NetworkManager.SpawnManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj) ? obj : null;
    }
}
