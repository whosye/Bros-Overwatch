using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

// Ayranova ultimatni schopnost: vyskoci do vzduchu, drzi se (ability.duration s) a kurzorem vybira misto dopadu.
// Kdyz misto nepotvrdi vcas, schopnost se vyplytva. Dopad rozda AOE damage (ability.radius, ability.power) protivnikum.
public class LeapStrikeAbility : NetworkBehaviour
{
    enum Phase { Idle, Ascending, Aiming, Diving, Fizzling }

    public AbilityDefinition ability;

    public float hoverHeight = 14f;
    public float ascendTime = 0.7f;
    public float diveSpeed = 48f;
    public float maxAimDistance = 150f;
    public Vector3 thirdPersonCameraOffset = new Vector3(0f, 2.4f, -6.5f);

    CharacterController controller;
    FirstPersonController fpc;
    Camera playerCamera;

    Phase phase = Phase.Idle;
    float nextUseTime;
    float phaseTimer;
    float targetY;
    Vector3 aimPoint;
    bool aimValid;
    Vector3 savedCameraLocalPosition;

    GameObject marker;
    ParticleSystem flame;

    readonly NetworkVariable<bool> airborne = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool IsActive => phase != Phase.Idle;

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    public string StatusText()
    {
        if (ability == null) return "";

        switch (phase)
        {
            case Phase.Ascending:
                return $"{ability.abilityName}: VZLET...";
            case Phase.Aiming:
                return $"{ability.abilityName}: MIŘ a klikni! {Mathf.Max(0f, phaseTimer):0.0}s";
            case Phase.Diving:
                return $"{ability.abilityName}: DOPAD!";
            case Phase.Fizzling:
                return $"{ability.abilityName}: vyplýtváno";
        }

        float remaining = nextUseTime - Time.time;
        return remaining > 0f
            ? $"[E] {ability.abilityName}: {remaining:0.0}s"
            : $"[E] {ability.abilityName}: PŘIPRAVENO";
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        fpc = GetComponent<FirstPersonController>();
    }

    public override void OnNetworkSpawn()
    {
        playerCamera = fpc.playerCamera;
        airborne.OnValueChanged += OnAirborneChanged;
        if (airborne.Value)
            SetFlame(true);
    }

    public override void OnNetworkDespawn()
    {
        airborne.OnValueChanged -= OnAirborneChanged;
        if (marker != null)
            Destroy(marker);
    }

    void OnAirborneChanged(bool previous, bool current)
    {
        SetFlame(current);
        if (current && !IsOwner)
            ProceduralSfx.Play(ProceduralSfx.LeapStart, transform.position, 1f);
    }

    void SetFlame(bool on)
    {
        if (on && flame == null)
            flame = Fx.CreateFlameTrail(transform, controller.bounds.min.y - transform.position.y);

        if (flame == null) return;

        if (on)
            flame.Play();
        else
            flame.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void Update()
    {
        if (!IsOwner || ability == null) return;

        if (phase == Phase.Idle)
        {
            if (Keyboard.current.eKey.wasPressedThisFrame && Time.time >= nextUseTime
                && !fpc.InputBlocked && GameSettings.CursorLocked)
                Begin();
            return;
        }

        if (fpc.CannotAct)
        {
            Cancel();
            return;
        }

        switch (phase)
        {
            case Phase.Ascending: TickAscending(); break;
            case Phase.Aiming: TickAiming(); break;
            case Phase.Diving: TickDiving(); break;
            case Phase.Fizzling: TickFizzling(); break;
        }

        if (phase != Phase.Idle)
            UpdateThirdPersonCamera();
    }

    Vector3 HeadPosition => transform.TransformPoint(savedCameraLocalPosition);

    // Kamera za zady hrace, ale nikdy ne za zdi/stropem (uvnitr budovy se privede blize k hraci).
    void UpdateThirdPersonCamera()
    {
        Vector3 head = HeadPosition;
        Vector3 desired = transform.TransformPoint(thirdPersonCameraOffset);
        Vector3 direction = desired - head;
        float distance = direction.magnitude;
        if (distance < 0.01f) return;
        direction /= distance;

        float allowed = distance;
        var hits = Physics.SphereCastAll(head, 0.25f, direction, distance, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && owner == NetworkObject) continue;
            if (hit.distance <= 0f) continue;

            allowed = Mathf.Min(allowed, hit.distance);
        }

        playerCamera.transform.position = head + direction * Mathf.Max(0.15f, allowed - 0.05f);
    }

    void Begin()
    {
        nextUseTime = Time.time + ability.cooldown;
        fpc.AbilityActive = true;
        fpc.ResetVertical();

        phase = Phase.Ascending;
        targetY = transform.position.y + hoverHeight;

        savedCameraLocalPosition = playerCamera.transform.localPosition;

        airborne.Value = true;
        ProceduralSfx.Play(ProceduralSfx.LeapStart, transform.position, 1f);
    }

    void TickAscending()
    {
        float step = hoverHeight / Mathf.Max(0.05f, ascendTime) * Time.deltaTime;
        float newY = Mathf.MoveTowards(transform.position.y, targetY, step);
        var flags = controller.Move(Vector3.up * (newY - transform.position.y));

        // Uvnitr budovy se vzlet zastavi u stropu a miri se z dosazene vysky.
        bool hitCeiling = (flags & CollisionFlags.Above) != 0;

        if (hitCeiling || Mathf.Abs(transform.position.y - targetY) < 0.1f)
        {
            phase = Phase.Aiming;
            phaseTimer = ability.duration;
        }
    }

    void TickAiming()
    {
        phaseTimer -= Time.deltaTime;

        UpdateAim();

        bool confirm = Mouse.current.leftButton.wasPressedThisFrame || Keyboard.current.eKey.wasPressedThisFrame;

        if (confirm && aimValid)
        {
            HideMarker();
            phase = Phase.Diving;
            phaseTimer = 5f;
            return;
        }

        if (phaseTimer <= 0f)
        {
            HideMarker();
            phase = Phase.Fizzling;
            phaseTimer = 6f;
        }
    }

    void UpdateAim()
    {
        aimValid = false;

        // Miri se od hlavy hrace (ne od kamery, ktera muze byt za zdi) ve smeru pohledu.
        Vector3 origin = HeadPosition;
        Vector3 direction = playerCamera.transform.forward;
        var hits = Physics.RaycastAll(origin, direction, maxAimDistance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && owner == NetworkObject) continue;

            // Dopadnout se da jen na plochy podlahoveho typu (zem, podlaha, plosina, schody, strecha), ne na svislou zed nebo strop.
            if (hit.normal.y >= 0.3f)
            {
                aimPoint = hit.point;
                aimValid = true;
            }
            break;
        }

        if (marker == null)
            marker = Fx.CreateMarker();

        marker.SetActive(aimValid);
        if (aimValid)
        {
            float diameter = ability.radius * 2f;
            marker.transform.position = aimPoint + Vector3.up * 0.05f;
            marker.transform.localScale = new Vector3(diameter, 0.05f, diameter);
        }
    }

    void TickDiving()
    {
        phaseTimer -= Time.deltaTime;

        float feetOffset = transform.position.y - controller.bounds.min.y;
        Vector3 destination = aimPoint + Vector3.up * (feetOffset + 0.05f);

        Vector3 next = Vector3.MoveTowards(transform.position, destination, diveSpeed * Time.deltaTime);
        controller.Move(next - transform.position);

        bool arrived = Vector3.Distance(transform.position, destination) < 0.6f;
        if (arrived || phaseTimer <= 0f)
            Impact();
    }

    void Impact()
    {
        Vector3 feet = new Vector3(transform.position.x, controller.bounds.min.y, transform.position.z);
        ImpactServerRpc(feet);
        Finish();
    }

    void TickFizzling()
    {
        phaseTimer -= Time.deltaTime;

        controller.Move(Vector3.down * diveSpeed * 0.5f * Time.deltaTime);

        if (controller.isGrounded || phaseTimer <= 0f)
        {
            ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.6f);
            Finish();
        }
    }

    // Zrusi schopnost (smrt, konec zapasu, restart) bez efektu.
    public void Cancel()
    {
        if (phase == Phase.Idle) return;
        Finish();
    }

    void Finish()
    {
        phase = Phase.Idle;
        HideMarker();

        if (playerCamera != null)
            playerCamera.transform.localPosition = savedCameraLocalPosition;

        fpc.AbilityActive = false;
        fpc.ResetVertical();

        if (IsSpawned && IsOwner)
            airborne.Value = false;
    }

    void HideMarker()
    {
        if (marker != null)
            marker.SetActive(false);
    }

    [ServerRpc]
    void ImpactServerRpc(Vector3 position)
    {
        if (MatchManager.Instance != null && MatchManager.Instance.IsOver) return;

        var myTeam = GetComponent<PlayerTeam>();
        var damaged = new HashSet<Health>();

        foreach (var col in Physics.OverlapSphere(position, ability.radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var target = col.GetComponentInParent<Target>();
            if (target != null)
                target.TakeDamage(ability.power);

            var health = col.GetComponentInParent<Health>();
            if (health == null || !damaged.Add(health)) continue;

            var team = health.GetComponent<PlayerTeam>();
            if (myTeam != null && team != null && team.teamId.Value == myTeam.teamId.Value) continue;

            // Vybuch nejde skrz zdi: musi byt primy vyhled z mista dopadu na cil.
            if (!HasLineOfSight(position + Vector3.up * 0.6f, col)) continue;

            float distance = Vector3.Distance(position, health.transform.position);
            float damage = ability.power * Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(distance / ability.radius));

            bool wasAlive = health.currentHealth.Value > 0f;
            health.TakeDamage(damage);

            if (wasAlive && health.currentHealth.Value <= 0f && MatchManager.Instance != null)
                MatchManager.Instance.ReportKill(gameObject);
        }

        ImpactFxClientRpc(position);
    }

    static bool HasLineOfSight(Vector3 from, Collider target)
    {
        Vector3 to = target.bounds.center;
        if (Vector3.Distance(from, to) < 1.5f) return true;
        if (!Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)) return true;

        if (hit.collider == target) return true;

        var targetOwner = target.GetComponentInParent<NetworkObject>();
        return targetOwner != null && hit.collider.GetComponentInParent<NetworkObject>() == targetOwner;
    }

    [ClientRpc]
    void ImpactFxClientRpc(Vector3 position)
    {
        Fx.Explosion(position, ability.radius);
        ProceduralSfx.Play(ProceduralSfx.Explosion, position, 1f);
    }
}
