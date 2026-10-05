using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Annin biotický granat (E): hod dopredu (nejdal 'range' m), po dopadu vybuchne. Spoluhrace v okruhu 'radius'
// (i Annu) vyleci o 'power' HP, nepratele o 'power' zrani a na 'duration' sekund jim zablokuje leceni.
// Neprochazi zdmi.
public class BioticGrenadeAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    static readonly Color GrenadeColor = new Color(0.55f, 1f, 0.65f, 1f);
    static readonly Color HarmColor = new Color(0.75f, 0.35f, 1f, 1f);

    FirstPersonController fpc;
    float nextUseTime;

    public float CooldownRemaining => Mathf.Max(0f, nextUseTime - Time.time);

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
    }

    void Update()
    {
        if (!IsOwner || ability == null) return;
        if (!Keyboard.current.eKey.wasPressedThisFrame || !GameSettings.CursorLocked) return;
        if (fpc.InputBlocked || Time.time < nextUseTime) return;

        nextUseTime = Time.time + ability.Cooldown;

        var eye = fpc.playerCamera.transform;
        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.5f);
        GetComponent<PlayerHero>().SayAbility(ability);
        ThrowServerRpc(eye.position, eye.forward);
    }

    // ---------------- server ----------------

    [ServerRpc]
    void ThrowServerRpc(Vector3 origin, Vector3 direction)
    {
        var match = MatchManager.Instance;
        if (ability == null || direction.sqrMagnitude < 0.01f || (match != null && (match.IsOver || match.IsLobby))) return;

        direction.Normalize();

        // Granat leti rovne a vybuchne o prvni prekazku nebo hrace (i spoluhrace), nejdal po 'range' metrech.
        float distance = ability.range;
        var hits = Physics.SphereCastAll(origin, 0.14f, direction, ability.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && owner == NetworkObject) continue;

            distance = Mathf.Max(0.3f, hit.distance - 0.1f);
            break;
        }

        Vector3 point = origin + direction * distance;
        float flight = distance / Mathf.Max(1f, ability.speed);
        ThrownClientRpc(origin + direction * 0.5f, point, flight);
        StartCoroutine(Detonate(point, flight));
    }

    IEnumerator Detonate(Vector3 point, float delay)
    {
        yield return new WaitForSeconds(delay);

        var match = MatchManager.Instance;
        if (match == null || (!match.IsOver && !match.IsLobby))
        {
            var touched = new HashSet<Health>();
            foreach (var col in Physics.OverlapSphere(point, ability.radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var dummy = col.GetComponentInParent<Target>();
                if (dummy != null)
                    dummy.TakeDamage(ability.power);

                var target = col.GetComponentInParent<Health>();
                if (target == null || !touched.Add(target) || target.currentHealth.Value <= 0f) continue;
                if (!Combat.HasLineOfSight(point + Vector3.up * 0.3f, col)) continue;

                bool friend = target.gameObject == gameObject || Combat.SameTeam(gameObject, target.gameObject);
                if (friend)
                {
                    Combat.HealPlayer(gameObject, target, ability.power);
                }
                else
                {
                    Combat.DamagePlayer(gameObject, target, ability.power);
                    target.ServerBlockHealing(ability.duration);
                }
            }
        }

        DetonatedClientRpc(point);
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void ThrownClientRpc(Vector3 from, Vector3 to, float seconds)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(ball.GetComponent<Collider>());
        ball.name = "BioticGrenade";
        ball.transform.localScale = Vector3.one * 0.18f;
        Fx.Paint(ball, GrenadeColor);
        ball.AddComponent<FlashGrenadeVisual>().Init(from, to, seconds);
    }

    [ClientRpc]
    void DetonatedClientRpc(Vector3 point)
    {
        float radius = ability != null ? ability.radius : 4f;
        Fx.BulletImpact(point, GrenadeColor, radius);
        Fx.Sparks(point, GrenadeColor);
        Fx.Sparks(point + Vector3.up * 0.4f, HarmColor);
        ProceduralSfx.Play(ProceduralSfx.Hit, point, 1f);

        var lightObject = new GameObject("BioticLight");
        lightObject.transform.position = point + Vector3.up * 0.5f;
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = GrenadeColor;
        light.range = radius * 2.5f;
        light.intensity = 6f;
        Destroy(lightObject, 0.35f);
    }
}
