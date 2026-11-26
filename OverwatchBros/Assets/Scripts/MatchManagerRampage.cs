using Unity.Netcode;
using UnityEngine;

// RAMPAGE (lebka v galerii Grand Theft Domasov v tunelu, jako rampage v GTA): kdo ji sebere, ma 30 s +50 % poskozeni
// a ukol zabit 5 nepratel. Kazde zabiti prodlouzi cas o 5 s. Pet zabiti = "RAMPAGE COMPLETE!" a plne zdravi
// (zbytek posileni mu zustane); kdyz cas dojde nebo zemre, "RAMPAGE FAILED". Hlida server, hlasky posila vsem.
public partial class MatchManager
{
    public const float RampageSeconds = 30f, RampageExtend = 5f, RampageDamage = 1.5f;
    public const int RampageKillsNeeded = 5;

    const byte RampageStart = 0, RampageKill = 1, RampageComplete = 2, RampageFailed = 3;

    PlayerHero rampageHero;
    float rampageUntil;
    int rampageKills;

    public static readonly Color RampageColor = new Color(1f, 0.22f, 0.15f);

    // Mistni hrac (HUD - BuffUI): do kdy mu rampage bezi.
    public static float LocalRampageUntil;
    // Kdo ma prave rampage (u kazdeho hrace - aura a zaznam pro POTG / killcam); MaxValue = nikdo.
    public static ulong RampageHolderId = ulong.MaxValue;
    static ParticleSystem rampageAura;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRampageStatics()
    {
        LocalRampageUntil = 0f;
        RampageHolderId = ulong.MaxValue;
        rampageAura = null;
    }

    // Cervena aura kolem hrace s rampage (vidi ji vsichni; vlastnik v 1. osobe ne).
    static void SetRampageAura(NetworkObject player, bool on)
    {
        if (rampageAura != null)
        {
            rampageAura.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Object.Destroy(rampageAura.gameObject, 2f);
            rampageAura = null;
        }
        if (!on || player == null || player.IsOwner) return;
        rampageAura = Fx.CreateAura(player.transform, RampageColor);
        rampageAura.Play();
    }

    // Napis ve hre (jen mistni hrac) a do zaznamu (POTG / killcam ho ukaze, kdyz se prehrava z oci tohoto hrace).
    static void RampageAnnounce(ulong playerId, bool local, string title, string subtitle, Color color, float seconds, bool headline)
    {
        ReplayLog.Announce(playerId, title, subtitle, color, seconds, headline);
        if (!local) return;
        if (headline) CaptureUI.Headline(title, subtitle, color, seconds);
        else CaptureUI.Announce(title, color, seconds);
    }

    // Sebrani lebky (ApplyPickup). Kdyz uz nekdo rampage ma, lebka zustane lezet.
    bool ServerStartRampage(PlayerHero hero)
    {
        if (hero == null || rampageHero != null) return false;
        rampageHero = hero;
        rampageKills = 0;
        rampageUntil = Time.time + RampageSeconds;
        hero.ServerDamageBuff(RampageDamage, RampageSeconds);
        RampageClientRpc(hero.NetworkObjectId, RampageStart, RampageSeconds, 0);
        return true;
    }

    // Z ServerPickupTick: konec casu, smrt, konec kola.
    void ServerRampageTick()
    {
        if (rampageHero == null) return;
        var health = rampageHero.GetComponent<Health>();
        bool gone = !rampageHero.IsSpawned || health == null || health.currentHealth.Value <= 0f;
        if (gone || IsLobby || IsOver || Time.time >= rampageUntil)
            ServerEndRampage(false);
    }

    // Z ReportKill: zabiti hrace s rampage.
    void ServerRampageOnKill(GameObject killer)
    {
        if (rampageHero == null || killer == null || killer != rampageHero.gameObject) return;
        rampageKills++;
        if (rampageKills >= RampageKillsNeeded)
        {
            var health = rampageHero.GetComponent<Health>();
            if (health != null && !health.HealBlocked) Combat.HealedLine(health, health.Heal(health.maxHealth));
            ServerEndRampage(true);
            return;
        }
        rampageUntil += RampageExtend;
        rampageHero.ServerDamageBuff(RampageDamage, rampageUntil - Time.time);
        RampageClientRpc(rampageHero.NetworkObjectId, RampageKill, rampageUntil - Time.time, rampageKills);
    }

    void ServerEndRampage(bool complete)
    {
        if (rampageHero == null) return;
        ulong id = rampageHero.NetworkObjectId;
        int kills = rampageKills;
        rampageHero = null;
        if (IsSpawned) RampageClientRpc(id, complete ? RampageComplete : RampageFailed, 0f, kills);
    }

    // Novy zapas / kolo (ServerResetPickups).
    void ServerResetRampage()
    {
        rampageHero = null;
        rampageKills = 0;
    }

    [ClientRpc]
    void RampageClientRpc(ulong playerId, byte phase, float remaining, int kills)
    {
        if (ReplayPlayer.Active) return;
        var network = NetworkManager.Singleton;
        if (network == null) return;
        var localPlayer = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        bool local = localPlayer != null && localPlayer.NetworkObjectId == playerId;
        string name = "Někdo";
        if (network.SpawnManager.SpawnedObjects.TryGetValue(playerId, out var player))
        {
            var hero = player.GetComponent<PlayerHero>();
            if (hero != null) name = hero.DisplayName;
        }

        var red = RampageColor;
        var gold = new Color(1f, 0.82f, 0.2f);
        network.SpawnManager.SpawnedObjects.TryGetValue(playerId, out var holder);
        switch (phase)
        {
            case RampageStart:
                RampageHolderId = playerId;
                SetRampageAura(holder, true);
                if (local) LocalRampageUntil = Time.time + remaining;
                else CaptureUI.Announce($"{name} spustil RAMPAGE!", red, 3f);
                RampageAnnounce(playerId, local, "RAMPAGE!", $"Zabij {RampageKillsNeeded} nepřátel  ·  +50 % poškození  ·  každé zabití +{RampageExtend:0} s", red, 3.5f, true);
                if (local) Fx.PlayGlobal(ProceduralSfx.PotgSlowHit, 0.8f);
                break;
            case RampageKill:
                if (local)
                {
                    LocalRampageUntil = Time.time + remaining;
                    Fx.PlayGlobal(ProceduralSfx.CaptureUnlock, 0.7f);
                }
                RampageAnnounce(playerId, local, $"RAMPAGE  {kills}/{RampageKillsNeeded}   +{RampageExtend:0} s", "", red, 2f, false);
                break;
            case RampageComplete:
                RampageHolderId = ulong.MaxValue;
                SetRampageAura(null, false);
                if (local)
                {
                    LocalRampageUntil = 0f;
                    Fx.PlayGlobal(ProceduralSfx.CaptureWon, 0.9f);
                }
                else CaptureUI.Announce($"{name}: RAMPAGE COMPLETE!", gold, 3f);
                RampageAnnounce(playerId, local, "RAMPAGE COMPLETE!", "Plné zdraví", gold, 4f, true);
                break;
            case RampageFailed:
                RampageHolderId = ulong.MaxValue;
                SetRampageAura(null, false);
                if (local) LocalRampageUntil = 0f;
                RampageAnnounce(playerId, local, "RAMPAGE FAILED", $"{kills}/{RampageKillsNeeded} zabití", red, 3f, true);
                break;
        }
    }
}
