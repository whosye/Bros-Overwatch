using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Ayranuv Modry plamen (Left Shift): jeden plynuly vypad dopredu (delka 'range' m za 'duration' s), kamera ve 3. osobe,
// postava v utocne poze a za ni zustava modra ohniva stopa. Kazdeho nepritele v dosahu 'radius' po ceste zasahne
// jednou ('power') a Ayran si o zpusobene poskozeni (x healRatio) leci. Pak cooldown.
public class RushAbility : NetworkBehaviour
{
    public AbilityDefinition ability;
    public Vector3 thirdPersonCameraOffset = new Vector3(0f, 2.0f, -5.2f);
    public float pressBuffer = 0.4f;

    static readonly Color FlameColor = new Color(0.25f, 0.55f, 1f, 1f);

    FirstPersonController fpc;
    Health health;
    Camera playerCamera;

    bool active;
    float elapsed;
    float blockedTime;
    Vector3 dashDirection;
    readonly HashSet<Health> hitThisDash = new HashSet<Health>();
    readonly HashSet<Target> hitDummies = new HashSet<Target>();
    // Naboje: vypad jde pouzit tolikrat za sebou, kolik je naboju ('charges'); kazdy se dobiji 'cooldown' sekund.
    int charges = 1;
    float rechargeAt;
    float bufferedUntil;

    int MaxCharges => ability != null ? Mathf.Max(1, ability.charges) : 1;
    public int Charges => charges;
    public bool HasSeveralCharges => MaxCharges > 1;
    Vector3 savedCameraLocalPosition;
    ParticleSystem aura;

    readonly NetworkVariable<bool> rushing = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool IsActive => active;

    // Synchronizovano po siti: Modry plamen prave bezi (i u ostatnich hracu).
    public bool IsRushing => rushing.Value;

    public float CooldownRemaining => charges > 0 ? 0f : Mathf.Max(0f, rechargeAt - Time.time);

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
        charges = MaxCharges;
    }

    public string StatusText()
    {
        if (ability == null) return "";

        if (active)
            return $"[SHIFT] {ability.abilityName}: AKTIVNÍ";

        float remaining = CooldownRemaining;
        return remaining > 0f
            ? $"[SHIFT] {ability.abilityName}: {remaining:0.0}s"
            : $"[SHIFT] {ability.abilityName}: PŘIPRAVENO";
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        health = GetComponent<Health>();
    }

    public override void OnNetworkSpawn()
    {
        playerCamera = fpc.playerCamera;
        rushing.OnValueChanged += OnRushingChanged;
        if (rushing.Value)
            SetAura(true);
    }

    public override void OnNetworkDespawn()
    {
        rushing.OnValueChanged -= OnRushingChanged;
        if (aura != null)
            Destroy(aura.gameObject);
    }

    // Hrdina se zmenil (nebo se komponenta vypnula): schopnost se okamzite ukonci.
    void OnDisable()
    {
        if (active)
            Stop();
    }

    void OnRushingChanged(bool previous, bool current)
    {
        if (current)
        {
            hitThisDash.Clear();
            hitDummies.Clear();
        }

        SetAura(current);
        if (current && !IsOwner)
            PlayStartSound(0.9f);
    }

    void PlayStartSound(float volume)
    {
        if (ability != null && ability.sound != null)
            Fx.PlaySpatial(ability.sound, transform.position, volume);
        else
            ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, volume);
    }

    void SetAura(bool on)
    {
        if (on && aura == null)
            aura = Fx.CreateAura(transform, FlameColor);

        if (aura == null) return;

        if (on)
            aura.Play();
        else
            aura.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void Update()
    {
        if (IsOwner)
            OwnerUpdate();

        if (IsServer && rushing.Value)
            ServerTick();
    }

    void OwnerUpdate()
    {
        if (ability == null) return;

        if (charges < MaxCharges && Time.time >= rechargeAt)
        {
            charges++;
            rechargeAt = Time.time + ability.Cooldown;
        }

        if (!active)
        {
            // Stisk se chvili pamatuje: zmacknuti tesne pred koncem cooldownu se nezahodi. Vypad jde spustit i z bloku.
            if (Keyboard.current.leftShiftKey.wasPressedThisFrame && GameSettings.CursorLocked)
                bufferedUntil = Time.time + pressBuffer;

            if (Time.time <= bufferedUntil && charges > 0 && !fpc.InputBlocked && !fpc.RushActive && !fpc.Rooted)
            {
                bufferedUntil = 0f;
                Begin();
            }
            return;
        }

        if (fpc.CannotAct)
        {
            Stop();
            return;
        }

        TickDash();
    }

    // Jeden plynuly vypad dopredu: delka 'range', trva 'duration', na konci zpomali.
    void TickDash()
    {
        float duration = Mathf.Max(0.1f, ability.duration);
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

        // Plna rychlost prvnich 75 % casu, pak dojezd na 35 %; zakladni rychlost je dopocitana tak, aby vysla delka 'range'.
        float baseSpeed = ability.range / (duration * 0.91875f);
        float speed = baseSpeed * (t < 0.75f ? 1f : Mathf.Lerp(1f, 0.35f, (t - 0.75f) / 0.25f));

        Vector3 before = transform.position;
        var flags = fpc.Controller.Move(dashDirection * speed * Time.deltaTime + Vector3.down * 6f * Time.deltaTime);

        // Naraz do zdi vypad ukonci; o nepritele se jen zarazi (jinak by vypad tesne u nej hned skoncil).
        Vector3 moved = transform.position - before;
        moved.y = 0f;
        bool blocked = (flags & CollisionFlags.Sides) != 0 && moved.magnitude < speed * Time.deltaTime * 0.3f
            && !TargetAhead();
        blockedTime = blocked ? blockedTime + Time.deltaTime : 0f;

        ThirdPersonCamera.Place(transform, NetworkObject, playerCamera, savedCameraLocalPosition, thirdPersonCameraOffset);

        if (elapsed >= duration || blockedTime > 0.08f)
            Stop();
    }

    bool TargetAhead()
    {
        Vector3 center = transform.position + Vector3.up * 1f + dashDirection * 0.6f;
        foreach (var col in Physics.OverlapSphere(center, 0.9f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (col.GetComponentInParent<Target>() != null) return true;

            var other = col.GetComponentInParent<Health>();
            if (other != null && other != health) return true;
        }

        return false;
    }

    void Begin()
    {
        if (charges == MaxCharges)
            rechargeAt = Time.time + ability.Cooldown;
        charges--;

        active = true;
        elapsed = 0f;
        blockedTime = 0f;
        savedCameraLocalPosition = playerCamera.transform.localPosition;

        dashDirection = transform.forward;
        dashDirection.y = 0f;
        dashDirection.Normalize();

        fpc.RushActive = true;
        fpc.AbilityActive = true;
        fpc.ResetVertical();

        rushing.Value = true;
        PlayStartSound(1f);
        GetComponent<PlayerHero>().SayAbility(ability);
    }

    void Stop()
    {
        active = false;

        if (playerCamera != null)
            playerCamera.transform.localPosition = savedCameraLocalPosition;

        fpc.RushActive = false;
        fpc.AbilityActive = false;
        fpc.ResetVertical();

        if (IsSpawned && IsOwner)
            rushing.Value = false;
    }

    // Vola PlayerRespawn / reset kola: zrusi schopnost bez cooldownu navic.
    public void Cancel()
    {
        if (!active) return;

        Stop();
        charges = MaxCharges;
    }

    // ---------------- server: poškození + leceni ----------------

    // Kazdeho nepritele, kolem ktereho vypad proleti, zasahne jednou; o zpusobene poskozeni se Ayran leci.
    void ServerTick()
    {
        if (ability == null) return;

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;
        if (health.currentHealth.Value <= 0f) return;

        Vector3 center = transform.position + Vector3.up * 1f;
        float dealt = 0f;

        foreach (var col in Physics.OverlapSphere(center, ability.radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var dummy = col.GetComponentInParent<Target>();
            if (dummy != null && hitDummies.Add(dummy))
                dummy.TakeDamage(ability.power);

            var other = col.GetComponentInParent<Health>();
            if (other == null || other == health || hitThisDash.Contains(other)) continue;
            if (other.currentHealth.Value <= 0f) continue;
            if (Combat.SameTeam(gameObject, other.gameObject)) continue;
            if (!Combat.HasLineOfSight(center, col)) continue;

            hitThisDash.Add(other);
            float before = other.currentHealth.Value;
            Combat.DamagePlayer(gameObject, other, ability.power);
            float applied = before - other.currentHealth.Value;
            if (applied > 0f)
            {
                dealt += applied;
                HitFxClientRpc(other.transform.position + Vector3.up * 1f);
            }
        }

        if (dealt > 0f)
            health.Heal(dealt * ability.healRatio);
    }

    [ClientRpc]
    void HitFxClientRpc(Vector3 position)
    {
        Fx.BulletImpact(position, FlameColor, 1.3f);
    }
}
