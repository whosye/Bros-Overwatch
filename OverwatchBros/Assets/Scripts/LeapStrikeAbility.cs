using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

// Ayranova ultimatni schopnost: vyskoci do vzduchu, drzi se (ability.duration s) a kurzorem vybira misto dopadu.
// Kdyz misto nepotvrdi vcas, schopnost se vyplytva. Dopad rozda AOE damage (ability.radius, ability.power) protivnikum.
public class LeapStrikeAbility : NetworkBehaviour, ICooldownCut
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
    Renderer markerRenderer;
    Vector3 launchPosition;
    ParticleSystem flame;

    readonly NetworkVariable<bool> airborne = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool IsActive => phase != Phase.Idle;
    public bool IsAirborne => airborne.Value;

    public float CooldownRemaining => UsesCharge ? 0f : Mathf.Max(0f, nextUseTime - Time.time);

    // (ultimatka nabijena hrou se nezkracuje - jen klasicky cooldown)
    public void CutCooldown(float fraction)
    {
        if (!UsesCharge && nextUseTime > Time.time)
            nextUseTime = Time.time + (nextUseTime - Time.time) * (1f - Mathf.Clamp01(fraction));
    }

    // Ultimatka: s nastavenou cenou (ultCost) se nabiji hrou, jinak plati cooldown.
    PlayerHero hero;
    bool UsesCharge => hero != null && hero.UsesUltCharge;
    bool CanUse => UsesCharge ? hero.UltReady : Time.time >= nextUseTime;

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
            ? $"[Q] {ability.abilityName}: {remaining:0.0}s"
            : $"[Q] {ability.abilityName}: PŘIPRAVENO";
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
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
            if (HeroInput.Pressed(this, HeroInput.Key.Q) && CanUse
                && !fpc.InputBlocked && !fpc.RushActive && !fpc.BlockActive && !fpc.Rooted && HeroInput.Locked(this))
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

    void UpdateThirdPersonCamera()
    {
        ThirdPersonCamera.Place(transform, NetworkObject, playerCamera, savedCameraLocalPosition, thirdPersonCameraOffset);
    }

    void Begin()
    {
        nextUseTime = Time.time + ability.Cooldown;
        hero.SpendUlt();
        fpc.AbilityActive = true;
        fpc.ResetVertical();

        phase = Phase.Ascending;
        launchPosition = transform.position;
        targetY = transform.position.y + hoverHeight;

        savedCameraLocalPosition = playerCamera.transform.localPosition;

        airborne.Value = true;
        // Faze 1 hlasky: vzlet do vzduchu.
        GetComponent<PlayerHero>().SayAbility(ability, 1);
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

        bool confirm = HeroInput.Pressed(this, HeroInput.Key.LeftMouse) || HeroInput.Pressed(this, HeroInput.Key.Q);

        if (confirm && aimValid)
        {
            HideMarker();
            phase = Phase.Diving;
            phaseTimer = 5f;
            DiveStartServerRpc(transform.position);
            // Faze 2 hlasky: hrac potvrdil dopad a Ayran se rite k zemi.
            GetComponent<PlayerHero>().SayAbility(ability, 2);
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

        bool clamped = false;
        if (aimValid)
        {
            Vector3 offset = aimPoint - launchPosition;
            offset.y = 0f;

            // Dolet je omezeny: mirime-li dal, bod dopadu se prichyti na hranu dosahu.
            if (offset.magnitude > ability.range)
            {
                clamped = true;
                aimValid = false;

                Vector3 flat = offset.normalized * ability.range;
                Vector3 probe = new Vector3(launchPosition.x + flat.x, origin.y, launchPosition.z + flat.z);
                var down = Physics.RaycastAll(probe, Vector3.down, 300f, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(down, (a, b) => a.distance.CompareTo(b.distance));

                foreach (var hit in down)
                {
                    var owner = hit.collider.GetComponentInParent<NetworkObject>();
                    if (owner != null && owner == NetworkObject) continue;

                    if (hit.normal.y >= 0.3f)
                    {
                        aimPoint = hit.point;
                        aimValid = true;
                    }
                    break;
                }
            }
        }

        if (marker == null)
        {
            marker = Fx.CreateMarker();
            markerRenderer = marker.GetComponent<Renderer>();
        }

        markerRenderer.material.color = clamped ? new Color(1f, 0.85f, 0.10f) : new Color(1f, 0.25f, 0.05f);
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

        var recorder = GetComponent<PotgRecorder>();
        if (recorder != null)
            recorder.ServerNoteUltimate();

        // Vybuch nejde skrz zdi a nezrani vlastni tym (viz Combat.Explode).
        Combat.Explode(gameObject, position, ability.radius, ability.power, 0.5f, null, Combat.AbilitySource(ability));

        ImpactFxClientRpc(position);
    }

    [ClientRpc]
    void ImpactFxClientRpc(Vector3 position)
    {
        Fx.Explosion(position, ability.radius);
    }

    // Zvuk schopnosti se prehraje, kdyz se zacne padat dolu (potvrzeni miření), ne az pri dopadu.
    [ServerRpc]
    void DiveStartServerRpc(Vector3 position)
    {
        DiveStartFxClientRpc(position);
    }

    [ClientRpc]
    void DiveStartFxClientRpc(Vector3 position)
    {
        // Ultimatku slysi vsichni po cele mape.
        Fx.PlayGlobal(ability.sound, 1f);
    }
}
