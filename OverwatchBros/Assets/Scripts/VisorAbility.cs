using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Viktorova ultimatni schopnost (Q): takticky zamerovac. Po dobu 'duration' sekund jeho strely samy miri na nepritele,
// ktery je nejbliz stredu obrazovky (v kuzelu 'radius' stupnu) a na ktereho ma primy vyhled. Cil ukazuje zluta znacka.
public class VisorAbility : NetworkBehaviour
{
    public AbilityDefinition ability;
    public float maxDistance = 70f;

    FirstPersonController fpc;

    bool active;
    float endTime;
    float nextUseTime;
    bool hasTarget;
    Vector3 targetPoint;

    PlayerHero[] players = new PlayerHero[0];
    Target[] dummies = new Target[0];
    float nextScan;

    readonly NetworkVariable<bool> scanning = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool IsActive => active;
    public bool IsScanning => scanning.Value;
    public float CooldownRemaining => UsesCharge ? 0f : Mathf.Max(0f, nextUseTime - Time.time);

    // Ultimatka: s nastavenou cenou (ultCost) se nabiji hrou, jinak plati cooldown.
    PlayerHero hero;
    bool UsesCharge => hero != null && hero.UsesUltCharge;
    bool CanUse => UsesCharge ? hero.UltReady : Time.time >= nextUseTime;

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
    }

    public override void OnNetworkSpawn()
    {
        scanning.OnValueChanged += OnScanningChanged;
    }

    public override void OnNetworkDespawn()
    {
        scanning.OnValueChanged -= OnScanningChanged;
    }

    void OnDisable()
    {
        if (active)
            End(false);
    }

    // Ostatni slysi, ze Viktor zapnul zamerovac.
    void OnScanningChanged(bool previous, bool current)
    {
        // Zabiti behem zamerovace (a kratce po nem) se pocitaji jako zabiti ultimatkou.
        if (IsServer)
        {
            var recorder = GetComponent<PotgRecorder>();
            if (recorder != null)
                recorder.ServerNoteUltimate();
        }

        if (current && !IsOwner)
            ProceduralSfx.Play(ProceduralSfx.LeapStart, transform.position, 0.7f);
    }

    void Update()
    {
        if (!IsOwner || ability == null) return;

        if (!active)
        {
            if (Keyboard.current.qKey.wasPressedThisFrame && CanUse && GameSettings.CursorLocked && !fpc.InputBlocked)
                Begin();
            return;
        }

        if (fpc.IsDead || fpc.MatchOver || fpc.InLobby || Time.time >= endTime)
        {
            End(true);
            return;
        }

        FindTarget();
    }

    void Begin()
    {
        active = true;
        hero.SpendUlt();
        endTime = Time.time + ability.duration;
        hasTarget = false;
        nextScan = 0f;
        scanning.Value = true;

        ProceduralSfx.Play(ProceduralSfx.LeapStart, transform.position, 0.7f);
        GetComponent<PlayerHero>().SayAbility(ability);
    }

    void End(bool startCooldown)
    {
        active = false;
        hasTarget = false;
        if (startCooldown && ability != null)
            nextUseTime = Time.time + ability.cooldown;

        if (IsSpawned && IsOwner)
            scanning.Value = false;
    }

    // Vola PlayerRespawn / reset kola.
    public void Cancel()
    {
        if (active)
            End(false);
    }

    // Vola WeaponShooting: kam ma strela letet. False = zadny cil, strili se normalne.
    public bool TryGetAim(out Vector3 point)
    {
        point = targetPoint;
        return active && hasTarget;
    }

    void FindTarget()
    {
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.5f;
            players = FindObjectsByType<PlayerHero>();
            dummies = FindObjectsByType<Target>();
        }

        var eye = fpc.playerCamera.transform;
        float best = ability.radius;
        hasTarget = false;

        foreach (var player in players)
        {
            if (player == null || player.gameObject == gameObject || !player.IsSpawned || player.IsJoining) continue;

            var health = player.GetComponent<Health>();
            if (health == null || health.currentHealth.Value <= 0f) continue;
            if (Combat.SameTeam(gameObject, player.gameObject)) continue;

            Consider(eye, player.transform.position + Vector3.up * 1.2f, player.NetworkObject, null, ref best);
        }

        foreach (var dummy in dummies)
        {
            if (dummy == null) continue;

            var collider = dummy.GetComponentInChildren<Collider>();
            if (collider != null)
                Consider(eye, collider.bounds.center, null, collider, ref best);
        }

        if (hasTarget)
        {
            HudUI.LockPoint = targetPoint;
            HudUI.LockFrame = Time.frameCount;
        }
    }

    void Consider(Transform eye, Vector3 point, NetworkObject owner, Collider collider, ref float best)
    {
        Vector3 direction = point - eye.position;
        float distance = direction.magnitude;
        if (distance < 0.3f || distance > maxDistance) return;

        float angle = Vector3.Angle(eye.forward, direction);
        if (angle >= best) return;

        // Primy vyhled: prvni vec v ceste (krome me) musi byt cil.
        var hits = Physics.RaycastAll(eye.position, direction / distance, distance + 0.5f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var hitOwner = hit.collider.GetComponentInParent<NetworkObject>();
            if (hitOwner != null && hitOwner == NetworkObject) continue;

            bool isTarget = owner != null ? hitOwner == owner : hit.collider == collider;
            if (!isTarget) return;
            break;
        }

        best = angle;
        hasTarget = true;
        targetPoint = point;
    }
}
