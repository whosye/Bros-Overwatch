using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Viktorovo lecive pole (klavesa E): polozi na zem pole o polomeru 'radius', ktere 'duration' sekund leci jeho
// i spoluhrace ('power' zivotu za sekundu). Pole zustava na miste, kde ho polozil.
public class HealFieldAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    const float TickInterval = 0.25f;
    static readonly Color FieldColor = new Color(1f, 0.85f, 0.25f, 1f);

    FirstPersonController fpc;

    // vlastnik
    float nextUseTime;
    float activeUntil;

    // server
    bool fieldActive;
    Vector3 fieldPosition;
    float fieldEnd;
    float nextTick;

    // vizual
    GameObject visual;

    public float CooldownRemaining => Mathf.Max(0f, nextUseTime - Time.time);
    public bool IsActive => Time.time < activeUntil;

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
    }

    public override void OnNetworkDespawn()
    {
        if (visual != null)
            Destroy(visual);
    }

    void Update()
    {
        if (IsOwner)
            OwnerUpdate();

        if (IsServer && fieldActive)
            ServerTick();
    }

    void OwnerUpdate()
    {
        if (ability == null) return;
        if (!Keyboard.current.eKey.wasPressedThisFrame || !GameSettings.CursorLocked) return;
        if (fpc.InputBlocked || Time.time < nextUseTime) return;

        nextUseTime = Time.time + ability.Cooldown;
        activeUntil = Time.time + ability.duration;
        ProceduralSfx.Play(ProceduralSfx.Reload, transform.position, 0.6f);
        GetComponent<PlayerHero>().SayAbility(ability);
        PlaceServerRpc();
    }

    // ---------------- server ----------------

    [ServerRpc]
    void PlaceServerRpc()
    {
        var match = MatchManager.Instance;
        if (ability == null || (match != null && (match.IsOver || match.IsLobby))) return;

        // Pole lezi na zemi pod hracem.
        Vector3 point = transform.position;
        foreach (var hit in Physics.RaycastAll(transform.position + Vector3.up, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
            if (hit.collider.GetComponentInParent<BoulderHitbox>() != null) continue;
            point = hit.point;
            break;
        }

        fieldActive = true;
        fieldPosition = point;
        fieldEnd = Time.time + ability.duration;
        nextTick = Time.time;
        PlacedClientRpc(point, ability.duration);
    }

    void ServerTick()
    {
        if (Time.time >= fieldEnd)
        {
            fieldActive = false;
            return;
        }

        if (Time.time < nextTick) return;
        nextTick = Time.time + TickInterval;

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;

        var healed = new System.Collections.Generic.HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(fieldPosition + Vector3.up * 0.8f, ability.radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var target = col.GetComponentInParent<Health>();
            if (target == null || !healed.Add(target) || target.currentHealth.Value <= 0f) continue;
            if (target.gameObject != gameObject && !Combat.SameTeam(gameObject, target.gameObject)) continue;

            float before = target.currentHealth.Value;
            target.Heal(ability.power * TickInterval);

            var recorder = GetComponent<PotgRecorder>();
            if (recorder != null)
                recorder.ServerAddHealing(target.currentHealth.Value - before, target.gameObject == gameObject);

            // Leceni spoluhracu nabiji ultimatku (vlastni leceni ne).
            if (target.gameObject != gameObject)
            {
                var hero = GetComponent<PlayerHero>();
                hero.ServerAddUltCharge(target.currentHealth.Value - before);
                hero.ServerAddHealingStat(target.currentHealth.Value - before);
            }
        }
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void PlacedClientRpc(Vector3 point, float seconds)
    {
        if (visual != null)
            Destroy(visual);

        float radius = ability != null ? ability.radius : 5f;
        visual = new GameObject("HealField");
        visual.transform.position = point + Vector3.up * 0.03f;

        // Pruhledny kruh na zemi.
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(visual.transform, false);
        disc.transform.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);
        var discRenderer = disc.GetComponent<Renderer>();
        var material = new Material(Fx.ParticleMaterial) { mainTexture = null };
        material.color = new Color(FieldColor.r, FieldColor.g, FieldColor.b, 0.16f);
        discRenderer.sharedMaterial = material;
        discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Vysilac uprostred.
        var emitter = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(emitter.GetComponent<Collider>());
        emitter.transform.SetParent(visual.transform, false);
        emitter.transform.localPosition = new Vector3(0f, 0.14f, 0f);
        emitter.transform.localScale = new Vector3(0.22f, 0.14f, 0.22f);
        Fx.Paint(emitter, FieldColor);

        var lightObject = new GameObject("Glow");
        lightObject.transform.SetParent(visual.transform, false);
        lightObject.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        var glow = lightObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = FieldColor;
        glow.range = radius * 1.4f;
        glow.intensity = 2f;

        ProceduralSfx.Play(ProceduralSfx.Spawn, point, 0.6f);
        Destroy(visual, seconds);
    }
}
