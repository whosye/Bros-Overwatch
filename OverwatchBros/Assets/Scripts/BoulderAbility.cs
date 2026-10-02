using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Honzova ultimatni schopnost (Q): pusti pred sebe balvan, ktery se sam vali dopredu a ridi se mysi (mezernik = skok).
// Kamera jede za balvanem, Honza zatim stoji bezbranny. Levym tlacitkem (nebo znovu Q, nebo po 'duration' s) balvan
// vybuchne: 'power' poskozeni v okruhu 'radius' + odhozeni. Nepratele ho muzou rozstrilet (boulderHealth).
public class BoulderAbility : NetworkBehaviour
{
    public AbilityDefinition ability;
    public float boulderHealth = 100f;
    public float boulderRadius = 0.6f;
    public float jumpSpeed = 7f;
    public float gravity = 20f;
    public float cameraDistance = 5f;
    public float cameraHeight = 1.9f;

    static readonly Color Rock = new Color(0.42f, 0.38f, 0.34f);

    FirstPersonController fpc;
    Camera playerCamera;

    // vlastnik
    bool active;
    float elapsed;
    float nextUseTime;
    float verticalSpeed;
    Vector3 savedCameraLocalPosition;
    CharacterController roller;

    // server
    float hp;

    // vsichni
    GameObject boulder;
    Transform rock;
    Vector3 lastPosition;

    readonly NetworkVariable<bool> rolling = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    readonly NetworkVariable<Vector3> position = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool IsActive => active;
    public bool IsRolling => rolling.Value;
    public Vector3 BoulderPosition => boulder != null ? boulder.transform.position : position.Value;
    public float CooldownRemaining => Mathf.Max(0f, nextUseTime - Time.time);
    public float TimeLeft => ability != null ? Mathf.Max(0f, ability.duration - elapsed) : 0f;

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
    }

    public override void OnNetworkSpawn()
    {
        playerCamera = fpc.playerCamera;
        rolling.OnValueChanged += OnRollingChanged;
        if (rolling.Value && !IsOwner)
            ShowBoulder(position.Value);
    }

    public override void OnNetworkDespawn()
    {
        rolling.OnValueChanged -= OnRollingChanged;
        if (boulder != null)
            Destroy(boulder);
    }

    void OnDisable()
    {
        if (active)
            End();
    }

    void OnRollingChanged(bool previous, bool current)
    {
        if (current && IsServer)
            hp = boulderHealth;

        if (IsOwner) return;

        if (current)
        {
            ShowBoulder(position.Value);
            ProceduralSfx.Play(ProceduralSfx.LeapStart, position.Value, 0.6f);
        }
        else if (boulder != null)
        {
            Destroy(boulder);
            boulder = null;
        }
    }

    void Update()
    {
        if (IsOwner)
            OwnerUpdate();
        else if (boulder != null)
            FollowRemote();
    }

    void OwnerUpdate()
    {
        if (ability == null) return;

        if (!active)
        {
            if (Keyboard.current.qKey.wasPressedThisFrame && Time.time >= nextUseTime && GameSettings.CursorLocked
                && !fpc.InputBlocked && !fpc.RushActive && !fpc.BlockActive)
                Begin();
            return;
        }

        if (fpc.CannotAct)
        {
            End();
            return;
        }

        Tick();
    }

    void Begin()
    {
        active = true;
        elapsed = 0f;
        verticalSpeed = 0f;
        savedCameraLocalPosition = playerCamera.transform.localPosition;

        fpc.AbilityActive = true;
        fpc.ResetVertical();

        // Balvan se objevi kousek pred hracem (ne za zdi).
        Vector3 chest = transform.position + Vector3.up * 1f;
        float reach = 1.7f;
        foreach (var hit in Physics.SphereCastAll(chest, boulderRadius * 0.9f, transform.forward, reach, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() == NetworkObject) continue;
            reach = Mathf.Min(reach, Mathf.Max(0f, hit.distance - 0.05f));
        }

        Vector3 start = transform.position + transform.forward * reach + Vector3.up * 0.1f;
        ShowBoulder(start);

        roller = boulder.AddComponent<CharacterController>();
        roller.radius = boulderRadius;
        roller.height = boulderRadius * 2f;
        roller.center = new Vector3(0f, boulderRadius, 0f);
        roller.slopeLimit = 60f;
        roller.stepOffset = 0.45f;
        Physics.IgnoreCollision(roller, fpc.Controller);

        position.Value = start;
        rolling.Value = true;
        ProceduralSfx.Play(ProceduralSfx.LeapStart, start, 0.6f);
        GetComponent<PlayerHero>().SayAbility(ability);
    }

    void Tick()
    {
        elapsed += Time.deltaTime;

        if (roller.isGrounded && verticalSpeed < 0f)
            verticalSpeed = -2f;
        if (roller.isGrounded && Keyboard.current.spaceKey.wasPressedThisFrame)
            verticalSpeed = jumpSpeed;
        verticalSpeed -= gravity * Time.deltaTime;

        // Vali se porad dopredu smerem, kam se hrac diva.
        Vector3 forward = transform.forward;
        roller.Move((forward * ability.speed + Vector3.up * verticalSpeed) * Time.deltaTime);
        Spin();
        position.Value = boulder.transform.position;

        // Honza sam stoji; kdyby byl ve vzduchu, spadne na zem.
        fpc.Controller.Move(Vector3.down * 9f * Time.deltaTime);

        PlaceCamera();

        bool trigger = elapsed > 0.3f && (Mouse.current.leftButton.wasPressedThisFrame || Keyboard.current.qKey.wasPressedThisFrame);
        if (trigger || elapsed >= ability.duration)
        {
            ExplodeServerRpc(boulder.transform.position);
            End();
            return;
        }

        if (boulder.transform.position.y < -40f)
            End();
    }

    // Kamera za balvanem, ale ne za zdi.
    void PlaceCamera()
    {
        Vector3 target = boulder.transform.position + Vector3.up * (boulderRadius + 0.4f);
        Vector3 desired = target + Vector3.up * (cameraHeight - 1f) - transform.forward * cameraDistance;
        Vector3 direction = desired - target;
        float distance = direction.magnitude;
        direction /= distance;

        float allowed = distance;
        foreach (var hit in Physics.SphereCastAll(target, 0.25f, direction, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance <= 0f) continue;
            if (hit.collider.GetComponentInParent<BoulderHitbox>() != null) continue;
            if (hit.collider.GetComponentInParent<NetworkObject>() == NetworkObject) continue;

            allowed = Mathf.Min(allowed, hit.distance);
        }

        playerCamera.transform.position = target + direction * Mathf.Max(0.3f, allowed - 0.05f);
    }

    // Ukonci schopnost (vybuch, cas, smrt, rozstrileni). Cooldown bezi od konce.
    void End()
    {
        if (!active) return;

        active = false;
        nextUseTime = Time.time + (ability != null ? ability.cooldown : 0f);

        if (boulder != null)
            Destroy(boulder);
        boulder = null;
        roller = null;

        if (playerCamera != null)
            playerCamera.transform.localPosition = savedCameraLocalPosition;

        fpc.AbilityActive = false;
        fpc.ResetVertical();

        var shooting = GetComponent<WeaponShooting>();
        if (shooting != null)
            shooting.HoldFire(0.4f);

        if (IsSpawned && IsOwner)
            rolling.Value = false;
    }

    // Vola PlayerRespawn / reset kola: balvan zmizi bez vybuchu a bez cooldownu.
    public void Cancel()
    {
        if (!active) return;

        End();
        nextUseTime = 0f;
    }

    // ---------------- server ----------------

    [ServerRpc]
    void ExplodeServerRpc(Vector3 point)
    {
        if (ability == null) return;

        var recorder = GetComponent<PotgRecorder>();
        if (recorder != null)
            recorder.ServerNoteUltimate();

        Vector3 center = point + Vector3.up * boulderRadius;
        Combat.Explode(gameObject, center, ability.radius, ability.power, 0.3f);
        Combat.Knockback(gameObject, center, ability.radius, ability.knockback, 1f);
        ExplodeFxClientRpc(center);
    }

    [ClientRpc]
    void ExplodeFxClientRpc(Vector3 center)
    {
        Fx.Explosion(center, ability != null ? ability.radius : 6f);
        ProceduralSfx.Play(ProceduralSfx.Explosion, center, 1f);
    }

    // Balvan dostal zasah (jen server). Vlastni tym ho neposkodi.
    public void ServerDamage(GameObject attacker, float amount)
    {
        if (!IsServer || !rolling.Value || hp <= 0f || amount <= 0f) return;
        if (attacker == gameObject || Combat.SameTeam(attacker, gameObject)) return;

        hp -= amount;
        if (hp <= 0f)
            DestroyedClientRpc(BoulderPosition);
    }

    [ClientRpc]
    void DestroyedClientRpc(Vector3 point)
    {
        Fx.Sparks(point + Vector3.up * boulderRadius, Rock);
        Fx.BulletImpact(point + Vector3.up * boulderRadius, Rock, 2.5f);
        ProceduralSfx.Play(ProceduralSfx.Hit, point, 1f);

        if (IsOwner)
            End();
    }

    // ---------------- vizual ----------------

    void FollowRemote()
    {
        boulder.transform.position = Vector3.Lerp(boulder.transform.position, position.Value, 1f - Mathf.Exp(-18f * Time.deltaTime));
        Spin();
    }

    // Otaceni podle ujete vzdalenosti.
    void Spin()
    {
        Vector3 moved = boulder.transform.position - lastPosition;
        lastPosition = boulder.transform.position;
        moved.y = 0f;
        if (moved.sqrMagnitude < 1e-8f) return;

        Vector3 axis = Vector3.Cross(Vector3.up, moved.normalized);
        rock.Rotate(axis, moved.magnitude / boulderRadius * Mathf.Rad2Deg, Space.World);
    }

    void ShowBoulder(Vector3 at)
    {
        if (boulder != null)
            Destroy(boulder);

        boulder = new GameObject("Boulder");
        boulder.transform.position = at;
        lastPosition = at;
        boulder.AddComponent<BoulderHitbox>().Owner = this;

        rock = new GameObject("Rock").transform;
        rock.SetParent(boulder.transform, false);
        rock.localPosition = new Vector3(0f, boulderRadius, 0f);

        AddLump(Vector3.zero, boulderRadius * 2f, Rock);

        // Hrboly, aby bylo videt, ze se vali.
        var random = new System.Random(7);
        for (int i = 0; i < 9; i++)
        {
            var direction = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f).normalized;
            float shade = 0.75f + (float)random.NextDouble() * 0.4f;
            AddLump(direction * boulderRadius * 0.72f, boulderRadius * (0.55f + (float)random.NextDouble() * 0.35f),
                new Color(Rock.r * shade, Rock.g * shade, Rock.b * shade));
        }

        // U vlastnika je kolizi CharacterController (viz Begin), u ostatnich koule, do ktere jde strilet.
        if (!IsOwner)
        {
            var sphere = boulder.AddComponent<SphereCollider>();
            sphere.center = new Vector3(0f, boulderRadius, 0f);
            sphere.radius = boulderRadius;
        }
    }

    void AddLump(Vector3 localPosition, float diameter, Color color)
    {
        var lump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(lump.GetComponent<Collider>());
        lump.transform.SetParent(rock, false);
        lump.transform.localPosition = localPosition;
        lump.transform.localScale = Vector3.one * diameter;
        Fx.Paint(lump, color);
    }
}
