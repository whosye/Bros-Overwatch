using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Maxova ultimatka (Q): Vpad do stinu. Zamiri na nepritele do 'range' m a zmizi v nem - je neviditelny
// a nezranitelny, kamera jede za obeti. Po 'duration' s (nebo driv levym tlacitkem) vyrazi ven: 'power' + 25 %
// max. zdravi obeti poskozeni, objevi se kousek za ni. Kdyz obet mezitim zemre, Max se jen objevi.
public class TrespassAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    const float MaxHealthShare = 0.25f;
    const float AimRadius = 0.6f;

    FirstPersonController fpc;
    PlayerHero hero;
    Health health;

    // server
    NetworkObject serverTarget;
    float serverEnd;
    bool serverActive;

    // vlastnik
    NetworkObject ownerTarget;
    float ownerEnd;
    bool ownerActive;
    float nextUseTime;
    Vector3 savedCameraLocalPosition;

    public bool IsActive => ownerActive || serverActive;
    public bool ServerActive => serverActive;
    public float CooldownRemaining => Mathf.Max(0f, nextUseTime - Time.time);

    public void Configure(AbilityDefinition definition) => ability = definition;

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
        health = GetComponent<Health>();
    }

    bool CanUse => hero.UsesUltCharge ? hero.UltReady : Time.time >= nextUseTime;

    void Update()
    {
        if (IsServer && serverActive) ServerTick();
        if (!IsOwner || ability == null) return;

        if (ownerActive)
        {
            OwnerTick();
            return;
        }

        if (!HeroInput.Pressed(this, HeroInput.Key.Q) || !CanUse || !HeroInput.Locked(this) || fpc.InputBlocked) return;

        var target = FindTarget();
        if (target == null)
        {
            ProceduralSfx.Play(ProceduralSfx.Empty, transform.position, 0.6f);   // nikdo na dosah
            return;
        }

        nextUseTime = Time.time + ability.Cooldown;
        hero.SpendUlt();
        hero.SayAbility(ability);
        ownerTarget = target;
        ownerEnd = Time.time + Duration;
        ownerActive = true;
        fpc.AbilityActive = true;
        savedCameraLocalPosition = fpc.playerCamera.transform.localPosition;
        BeginServerRpc(target);
    }

    float Duration => ability != null && ability.duration > 0.2f ? ability.duration : 2f;

    NetworkObject FindTarget()
    {
        var cam = fpc.playerCamera.transform;
        float range = ability.range > 0f ? ability.range : 10f;
        var hits = Physics.SphereCastAll(cam.position, AimRadius, cam.forward, range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner == NetworkObject) continue;
            var other = hit.collider.GetComponentInParent<PlayerHero>();
            if (other == null) return null;   // zed v ceste
            var otherHealth = other.GetComponent<Health>();
            if (otherHealth == null || otherHealth.currentHealth.Value <= 0f || other.IsUntargetable || Combat.SameTeam(gameObject, other.gameObject)) continue;
            return other.NetworkObject;
        }
        return null;
    }

    // ---------------- vlastnik: "uvnitr" obeti ----------------

    void OwnerTick()
    {
        bool targetGone = ownerTarget == null || !ownerTarget.IsSpawned
            || ownerTarget.GetComponent<Health>().currentHealth.Value <= 0f;
        if (fpc.IsDead)
        {
            OwnerFinish(false);
            return;
        }

        if (!targetGone)
        {
            // Max se vznasi uvnitr obeti (bez kolize), kamera ve 3. osobe za ni
            transform.position = ownerTarget.transform.position;
            ThirdPersonCamera.PlaceOrbit(transform, NetworkObject, fpc.playerCamera, savedCameraLocalPosition, 4.5f, 1.4f);
        }

        bool click = HeroInput.Pressed(this, HeroInput.Key.LeftMouse) && Time.time > ownerEnd - Duration + 0.3f;
        if (targetGone || Time.time >= ownerEnd || click)
            OwnerFinish(!targetGone);
    }

    void OwnerFinish(bool strike)
    {
        ownerActive = false;
        fpc.AbilityActive = false;
        fpc.playerCamera.transform.localPosition = savedCameraLocalPosition;
        fpc.ResetVertical();

        // vyrazit kousek za obeti (nebo zustat, kde je)
        if (ownerTarget != null && ownerTarget.IsSpawned)
        {
            Vector3 behind = ownerTarget.transform.position - ownerTarget.transform.forward * 1.3f;
            var controller = GetComponent<CharacterController>();
            if (Physics.CheckCapsule(behind + Vector3.up * 0.6f, behind + Vector3.up * 1.5f, 0.4f, ~0, QueryTriggerInteraction.Ignore))
                behind = ownerTarget.transform.position + ownerTarget.transform.right * 1.2f;
            controller.enabled = false;
            transform.position = behind;
            controller.enabled = true;
        }
        if (IsSpawned) EndServerRpc(strike);
        ownerTarget = null;
    }

    public void Cancel()
    {
        if (ownerActive) OwnerFinish(false);
    }

    // ---------------- server ----------------

    [ServerRpc]
    void BeginServerRpc(NetworkObjectReference targetRef)
    {
        var match = MatchManager.Instance;
        if (ability == null || !targetRef.TryGet(out NetworkObject target) || (match != null && (match.IsOver || match.IsLobby))) return;
        float range = (ability.range > 0f ? ability.range : 10f) + 3f;
        if (Vector3.Distance(target.transform.position, transform.position) > range) return;

        serverTarget = target;
        serverEnd = Time.time + Duration + 0.5f;
        serverActive = true;
        health.ServerInvulnerable(Duration + 0.6f);
        hero.isHidden.Value = true;

        var recorder = GetComponent<PotgRecorder>();
        if (recorder != null) recorder.ServerNoteUltimate();
        BeginClientRpc(target.NetworkObjectId);
    }

    void ServerTick()
    {
        // pojistka: kdyby vlastnik konec neposlal
        if (Time.time >= serverEnd + 1f)
            ServerEnd(false);
    }

    [ServerRpc]
    void EndServerRpc(bool strike)
    {
        ServerEnd(strike);
    }

    void ServerEnd(bool strike)
    {
        if (!serverActive) return;
        serverActive = false;
        hero.isHidden.Value = false;
        health.ServerClearInvulnerable();

        var target = serverTarget;
        serverTarget = null;
        if (target == null || !target.IsSpawned) return;
        var victim = target.GetComponent<Health>();
        var match = MatchManager.Instance;
        if (strike && victim != null && victim.currentHealth.Value > 0f && (match == null || (!match.IsOver && !match.IsLobby)))
            Combat.DamagePlayer(gameObject, victim, ability.power + victim.maxHealth * MaxHealthShare, Combat.AbilitySource(ability));
        BurstClientRpc(target.transform.position);
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void BeginClientRpc(ulong targetId)
    {
        Fx.Sparks(transform.position + Vector3.up, MaxAbility.ShadowColor);
        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.9f);

        // obet: tmavy fialovy okraj a varovani
        var network = NetworkManager.Singleton;
        var local = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        if (local != null && local.NetworkObjectId == targetId)
        {
            HudUI.NotifyTint(new Color(0.35f, 0.05f, 0.5f, 0.55f), Duration);
            CaptureUI.Announce($"{hero.DisplayName} je v tobě!", MaxAbility.ShadowColor, Duration);
        }
    }

    [ClientRpc]
    void BurstClientRpc(Vector3 position)
    {
        Fx.Explosion(position + Vector3.up * 0.8f, 2f);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            Fx.Sparks(position + new Vector3(Mathf.Cos(a), 1.2f, Mathf.Sin(a)), MaxAbility.ShadowColor);
        }
        ProceduralSfx.Play(ProceduralSfx.Explosion, position, 0.8f);
    }
}
