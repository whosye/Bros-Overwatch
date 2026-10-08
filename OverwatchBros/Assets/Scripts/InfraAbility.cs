using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Sniperova ultimatni schopnost (Q): Infravize. Vsichni nepratele jsou na 'duration' sekund odhaleni - sniperuv tym
// je vidi pres zdi (stejne jako po zasahu pruzkumnym sipem). Ultimatka se nabiji hrou; zvuk je slyset pres celou mapu.
public class InfraAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    FirstPersonController fpc;
    PlayerHero hero;
    float nextUseTime;
    float activeUntil;

    bool UsesCharge => hero != null && hero.UsesUltCharge;
    bool CanUse => UsesCharge ? hero.UltReady : Time.time >= nextUseTime;
    public float CooldownRemaining => UsesCharge ? 0f : Mathf.Max(0f, nextUseTime - Time.time);
    public bool IsActive => Time.time < activeUntil;

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
    }

    void Update()
    {
        if (!IsOwner || ability == null) return;
        if (!HeroInput.Pressed(this, HeroInput.Key.Q) || !HeroInput.Locked(this)) return;
        if (fpc.InputBlocked || !CanUse) return;

        nextUseTime = Time.time + ability.Cooldown;
        activeUntil = Time.time + ability.duration;
        hero.SpendUlt();
        hero.SayAbility(ability);
        RevealServerRpc();
    }

    [ServerRpc]
    void RevealServerRpc()
    {
        var match = MatchManager.Instance;
        if (ability == null || (match != null && (match.IsOver || match.IsLobby))) return;

        foreach (var client in MatchManager.PlayerSlots())
        {
            var player = client.PlayerObject;
            if (player == null || player.gameObject == gameObject || Combat.SameTeam(gameObject, player.gameObject)) continue;

            var other = player.GetComponent<PlayerHero>();
            var health = player.GetComponent<Health>();
            if (other != null && health != null && health.currentHealth.Value > 0f)
                other.ServerReveal(ability.duration);
        }

        var recorder = GetComponent<PotgRecorder>();
        if (recorder != null)
            recorder.ServerNoteUltimate();

        RevealedClientRpc(ability.duration);
    }

    [ClientRpc]
    void RevealedClientRpc(float seconds)
    {
        // Ultimatka je slyset pres celou mapu; sniperuv tym dostane cervenou "infra" barvu na chvili.
        Fx.PlayGlobal(ProceduralSfx.UltCharge, 0.5f);

        var local = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClient?.PlayerObject : null;
        if (local != null && Combat.SameTeam(gameObject, local.gameObject))
            HudUI.NotifyTint(new Color(1f, 0.25f, 0.15f, 0.3f), 0.8f);
    }
}
