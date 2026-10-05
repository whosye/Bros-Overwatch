using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Sindel - Shift a E v jedne komponente:
//   Shift (Spravedlivy skok): vyskoci rychlosti 'power' m/s a kus nad zemi se vznasi (pomaly pad), celkem 'duration' s;
//   E (Svaty granat): hodi granat obloukem rychlosti 'speed'. Vybuchne hned o nepritele, jinak chvili po dopadu:
//     'power' poskozeni ve stredu, k okraji 'radius' mene. Neprochazi zdmi, nezrani vlastni tym.
public class SindelAbility : NetworkBehaviour
{
    public AbilityDefinition leap;      // Shift
    public AbilityDefinition grenade;   // E

    public static readonly Color GrenadeColor = new Color(1f, 0.82f, 0.35f, 1f);
    const float Gravity = 14f;
    const float Lift = 3.5f;          // pridana rychlost nahoru (oblouk)
    const float Fuse = 0.6f;          // po dopadu
    const float MaxFlight = 3f;
    const float Edge = 0.35f;

    FirstPersonController fpc;
    PlayerHero hero;
    Health health;
    float nextLeap, nextGrenade;

    public float LeapRemaining => Mathf.Max(0f, nextLeap - Time.time);
    public float GrenadeRemaining => Mathf.Max(0f, nextGrenade - Time.time);

    public void Configure(AbilityDefinition shift, AbilityDefinition e)
    {
        leap = shift;
        grenade = e;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
        health = GetComponent<Health>();
    }

    void Update()
    {
        if (!IsOwner || !GameSettings.CursorLocked || fpc.InputBlocked || health.currentHealth.Value <= 0f) return;

        if (leap != null && Keyboard.current.leftShiftKey.wasPressedThisFrame && Time.time >= nextLeap && !fpc.Rooted)
        {
            nextLeap = Time.time + leap.Cooldown;
            fpc.OwnerHover(leap.power, leap.duration);
            ProceduralSfx.Play(ProceduralSfx.LeapStart, transform.position, 0.5f);
            hero.SayAbility(leap);
            LeapServerRpc();
        }

        if (grenade != null && Keyboard.current.eKey.wasPressedThisFrame && Time.time >= nextGrenade)
        {
            nextGrenade = Time.time + grenade.Cooldown;
            var eye = fpc.playerCamera.transform;
            ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.5f);
            hero.SayAbility(grenade);
            var held = GetComponent<HeldWeapons>();
            if (held != null) held.PlayThrow();
            ThrowServerRpc(eye.position + eye.forward * 0.5f, eye.forward);
        }
    }

    // ---------------- server ----------------

    [ServerRpc]
    void LeapServerRpc()
    {
        LeapClientRpc(transform.position + Vector3.up * 0.3f);
    }

    [ServerRpc]
    void ThrowServerRpc(Vector3 origin, Vector3 direction)
    {
        var match = MatchManager.Instance;
        if (grenade == null || direction.sqrMagnitude < 0.01f || (match != null && (match.IsOver || match.IsLobby))) return;
        if ((origin - transform.position).sqrMagnitude > 9f) origin = transform.position + Vector3.up * 1.5f;

        Vector3 velocity = direction.normalized * grenade.speed + Vector3.up * Lift;

        // Let po oblouku, dokud granat do neceho nenarazi.
        Vector3 position = origin;
        float flight = 0f;
        bool direct = false;
        const float step = 0.02f;
        while (flight < MaxFlight)
        {
            Vector3 next = position + velocity * step + Vector3.down * (0.5f * Gravity * step * step);
            Vector3 delta = next - position;
            if (Physics.SphereCast(position, 0.12f, delta.normalized, out var hit, delta.magnitude, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<NetworkObject>() != NetworkObject)
            {
                flight += step * (hit.distance / Mathf.Max(0.0001f, delta.magnitude));
                position = hit.point + hit.normal * 0.12f;
                var victim = hit.collider.GetComponentInParent<PlayerHero>();
                direct = victim != null && !Combat.SameTeam(gameObject, victim.gameObject);
                break;
            }
            position = next;
            velocity += Vector3.down * Gravity * step;
            flight += step;
        }

        float fuse = direct ? 0f : Fuse;
        ThrownClientRpc(origin, direction.normalized * grenade.speed + Vector3.up * Lift, flight, position, fuse);
        StartCoroutine(Detonate(position, flight + fuse));
    }

    IEnumerator Detonate(Vector3 point, float delay)
    {
        yield return new WaitForSeconds(delay);

        var match = MatchManager.Instance;
        if (match == null || (!match.IsOver && !match.IsLobby))
            Combat.Explode(gameObject, point, grenade.radius, grenade.power, Edge);

        DetonatedClientRpc(point, grenade.radius);
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void LeapClientRpc(Vector3 point)
    {
        Fx.Sparks(point, EzekielAbility.HolyColor);
        if (!IsOwner)
            ProceduralSfx.Play(ProceduralSfx.LeapStart, point, 0.5f);
    }

    [ClientRpc]
    void ThrownClientRpc(Vector3 origin, Vector3 velocity, float flight, Vector3 landing, float fuse)
    {
        if (!IsOwner)
        {
            var held = GetComponent<HeldWeapons>();
            if (held != null) held.PlayThrow();
        }

        // Zlaty granat s krizkem nahore (jako "svaty granat").
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(ball.GetComponent<Collider>());
        ball.name = "HolyGrenade";
        ball.transform.localScale = Vector3.one * 0.2f;
        Fx.Paint(ball, GrenadeColor);
        foreach (var size in new[] { new Vector3(0.12f, 0.7f, 0.12f), new Vector3(0.5f, 0.12f, 0.12f) })
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(ball.transform, false);
            bar.transform.localPosition = new Vector3(0f, size.y > 0.5f ? 0.75f : 0.85f, 0f);
            bar.transform.localScale = size;
            Fx.Paint(bar, new Color(0.95f, 0.9f, 0.8f));
        }
        ball.AddComponent<HolyGrenadeFlight>().Init(origin, velocity, Gravity, flight, landing, fuse);
    }

    [ClientRpc]
    void DetonatedClientRpc(Vector3 point, float radius)
    {
        Fx.Explosion(point, radius);
        Fx.Sparks(point + Vector3.up * 0.3f, EzekielAbility.HolyColor);
        ProceduralSfx.Play(ProceduralSfx.Explosion, point, 0.9f);
    }
}

// Let svateho granatu (jen vizual): stejny oblouk jako na serveru, po dopadu lezi a blika, pak zmizi.
public class HolyGrenadeFlight : MonoBehaviour
{
    Vector3 origin, velocity, landing;
    float gravity, flight, fuse, age;
    Light blink;

    public void Init(Vector3 start, Vector3 startVelocity, float g, float flightTime, Vector3 end, float fuseTime)
    {
        origin = start;
        velocity = startVelocity;
        gravity = g;
        flight = flightTime;
        landing = end;
        fuse = fuseTime;
        transform.position = start;

        blink = gameObject.AddComponent<Light>();
        blink.type = LightType.Point;
        blink.color = SindelAbility.GrenadeColor;
        blink.range = 3f;
        blink.shadows = LightShadows.None;
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age < flight)
        {
            // dorovnani na presne misto dopadu (drobne rozdily kroku simulace)
            Vector3 p = origin + velocity * age + Vector3.down * (0.5f * gravity * age * age);
            float k = flight > 0f ? age / flight : 1f;
            transform.position = Vector3.Lerp(p, landing, k * k);
            transform.Rotate(new Vector3(400f, 0f, 250f) * Time.deltaTime, Space.World);
            blink.intensity = 1.5f;
        }
        else
        {
            transform.position = landing;
            blink.intensity = Mathf.Repeat(age * 8f, 1f) < 0.5f ? 4f : 0.5f;
        }

        if (age >= flight + fuse + 0.05f)
            Destroy(gameObject);
    }
}
