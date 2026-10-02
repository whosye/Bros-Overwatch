using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Viktoruv oslepujici granat (prave tlacitko mysi): kratky hod dopredu (nejdal 'range' m), po dopadu vybuchne
// a nepratele v okruhu 'radius' na 'duration' sekund omraci (nemuzou se hybat, utocit ani pouzivat schopnosti)
// a trochu zrani ('power'). Neprochazi zdmi a spoluhrace ani Viktora nezasahne.
public class FlashAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

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
        if (!Mouse.current.rightButton.wasPressedThisFrame || !GameSettings.CursorLocked) return;
        if (fpc.InputBlocked || Time.time < nextUseTime) return;

        nextUseTime = Time.time + ability.cooldown;

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

        // Granat leti rovne a vybuchne o prvni prekazku nebo nepritele, nejdal po 'range' metrech.
        float distance = ability.range;
        var hits = Physics.SphereCastAll(origin, 0.12f, direction, ability.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && (owner == NetworkObject || Combat.SameTeam(gameObject, owner.gameObject))) continue;

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
            var hit = new HashSet<Health>();
            foreach (var col in Physics.OverlapSphere(point, ability.radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var dummy = col.GetComponentInParent<Target>();
                if (dummy != null)
                    dummy.TakeDamage(ability.power);

                var victim = col.GetComponentInParent<Health>();
                if (victim == null || !hit.Add(victim) || victim.currentHealth.Value <= 0f) continue;
                if (victim.gameObject == gameObject || Combat.SameTeam(gameObject, victim.gameObject)) continue;
                if (!Combat.HasLineOfSight(point, col)) continue;

                Combat.DamagePlayer(gameObject, victim, ability.power);

                var controller = victim.GetComponent<FirstPersonController>();
                if (controller != null && victim.currentHealth.Value > 0f)
                {
                    controller.ServerStun(ability.duration, gameObject);

                    var recorder = GetComponent<PotgRecorder>();
                    if (recorder != null)
                        recorder.ServerAddStun();
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
        ball.name = "FlashGrenade";
        ball.transform.localScale = Vector3.one * 0.16f;
        Fx.Paint(ball, new Color(0.92f, 0.94f, 1f));
        ball.AddComponent<FlashGrenadeVisual>().Init(from, to, seconds);
    }

    [ClientRpc]
    void DetonatedClientRpc(Vector3 point)
    {
        Fx.BulletImpact(point, Color.white, 4f);
        Fx.Sparks(point, Color.white);
        ProceduralSfx.Play(ProceduralSfx.Hit, point, 1f);
        ProceduralSfx.Play(ProceduralSfx.Gunshot, point, 0.8f);

        // Bily zablesk.
        var lightObject = new GameObject("FlashLight");
        lightObject.transform.position = point;
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Color.white;
        light.range = (ability != null ? ability.radius : 4f) * 2.5f;
        light.intensity = 9f;
        Destroy(lightObject, 0.18f);
    }
}

// Letici granat (jen efekt): doleti z bodu do bodu za dany cas a zmizi.
public class FlashGrenadeVisual : MonoBehaviour
{
    Vector3 from, to;
    float seconds, age;

    public void Init(Vector3 start, Vector3 end, float flight)
    {
        from = start;
        to = end;
        seconds = Mathf.Max(0.02f, flight);
        transform.position = start;
    }

    void Update()
    {
        age += Time.deltaTime;
        transform.position = Vector3.Lerp(from, to, age / seconds);
        if (age >= seconds)
            Destroy(gameObject);
    }
}
