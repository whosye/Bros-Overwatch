using Unity.Netcode;
using UnityEngine;

// Balicky na mape (PickupSpot): server hlida, kdo na balicek vstoupil, da mu ucinek a balicek na chvili zmizi.
// Ktere balicky jsou k dispozici, se sdili bitovou maskou (az 64 mist).
public partial class MatchManager
{
    const float PickupRadius = 1.2f;
    const float PickupTickInterval = 0.1f;

    public NetworkVariable<ulong> pickupMask = new NetworkVariable<ulong>(ulong.MaxValue);

    float[] pickupRespawnAt = new float[64];
    float nextPickupTick;

    public bool PickupAvailable(int index) => index < 0 || index >= 64 || (pickupMask.Value & (1UL << index)) != 0;

    // Na zacatku kola jsou vsechny balicky zpatky.
    void ServerResetPickups()
    {
        if (!IsServer) return;
        pickupMask.Value = ulong.MaxValue;
        System.Array.Clear(pickupRespawnAt, 0, pickupRespawnAt.Length);
    }

    void ServerPickupTick()
    {
        if (Time.time < nextPickupTick) return;
        nextPickupTick = Time.time + PickupTickInterval;

        var spots = PickupSpot.Sorted;
        ulong mask = pickupMask.Value;

        // Obnoveni sebranych.
        for (int i = 0; i < spots.Count && i < 64; i++)
            if ((mask & (1UL << i)) == 0 && Time.time >= pickupRespawnAt[i])
                mask |= 1UL << i;

        if (!IsLobby && !IsOver)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var player = client.PlayerObject;
                if (player == null) continue;
                var health = player.GetComponent<Health>();
                var hero = player.GetComponent<PlayerHero>();
                if (health == null || hero == null || health.currentHealth.Value <= 0f || hero.IsJoining) continue;

                Vector3 feet = player.transform.position;
                for (int i = 0; i < spots.Count && i < 64; i++)
                {
                    if ((mask & (1UL << i)) == 0) continue;
                    Vector3 spot = spots[i].transform.position;
                    Vector3 flat = feet - spot;
                    float dy = flat.y;
                    flat.y = 0f;
                    if (flat.magnitude > PickupRadius || dy < -spots[i].reachBelow || dy > 2.5f) continue;
                    if (!ApplyPickup(spots[i], health, hero)) continue;

                    mask &= ~(1UL << i);
                    pickupRespawnAt[i] = Time.time + spots[i].RespawnSeconds;
                    PickupTakenClientRpc(i, spot, player.NetworkObjectId);
                }
            }
        }

        if (mask != pickupMask.Value)
            pickupMask.Value = mask;
    }

    // Vraci false, kdyz balicek hraci nic neda (lekarnicka pri plnem zdravi) - pak zustane lezet.
    bool ApplyPickup(PickupSpot spot, Health health, PlayerHero hero)
    {
        switch (spot.kind)
        {
            case PickupKind.Health:
                if (health.currentHealth.Value >= health.maxHealth - 0.5f || health.HealBlocked) return false;
                health.Heal(spot.healAmount > 0f ? spot.healAmount : 75f);
                return true;
            case PickupKind.BigHealth:
                if (health.currentHealth.Value >= health.maxHealth - 0.5f || health.HealBlocked) return false;
                health.Heal(health.maxHealth);
                return true;
            case PickupKind.Speed:
                var movement = hero.GetComponent<FirstPersonController>();
                if (movement != null) movement.ServerSpeedBoost(8f, 1.35f);
                return true;
            case PickupKind.Power:
                hero.ServerBoost(4f);
                return true;
            case PickupKind.Invulnerable:
                health.ServerInvulnerable(PickupBuffs.InvulnerableSeconds);
                return true;
            case PickupKind.Tramal:
                // plne zdravi (sebere se i pri plnem zdravi - jde o buff) a kratce mensi poskozeni
                if (!health.HealBlocked) health.Heal(health.maxHealth);
                health.ServerDamageReduction(PickupBuffs.TramalDamageTaken, PickupBuffs.TramalSeconds);
                return true;
            default:
                health.ServerAddShield(60f, 10f);
                return true;
        }
    }

    [ClientRpc]
    void PickupTakenClientRpc(int index, Vector3 position, ulong playerId)
    {
        var spots = PickupSpot.Sorted;
        var kind = index >= 0 && index < spots.Count ? spots[index].kind : PickupKind.Health;

        // Mistni hrac: buff do HUD a hlaska.
        var local = NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null ? NetworkManager.Singleton.LocalClient.PlayerObject : null;
        if (local != null && local.NetworkObjectId == playerId && kind == PickupKind.Invulnerable)
        {
            PickupBuffs.InvulnerableUntil = Time.time + PickupBuffs.InvulnerableSeconds;
            CaptureUI.Announce("Vypil jsi dědovu slivovici", PickupSpot.KindColor(kind), 3f);
        }
        if (local != null && local.NetworkObjectId == playerId && kind == PickupKind.Tramal)
        {
            PickupBuffs.TramalUntil = Time.time + PickupBuffs.TramalSeconds;
            CaptureUI.Announce("Užil jsi Tramal", PickupSpot.KindColor(kind), 3f);
        }
        Fx.Sparks(position + Vector3.up * 0.8f, PickupSpot.KindColor(kind));
        bool heal = kind == PickupKind.Health || kind == PickupKind.BigHealth || kind == PickupKind.Tramal;
        ProceduralSfx.Play(heal ? ProceduralSfx.Spawn : ProceduralSfx.CaptureUnlock, position, 0.8f);
    }
}
