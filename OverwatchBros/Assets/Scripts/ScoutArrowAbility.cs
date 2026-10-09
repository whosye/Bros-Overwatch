using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Mirkuv pruzkumny sip (klavesa E): vystreli sip rovne dopredu (nejdal 'range' m). Kde se zabodne, tam na 'duration'
// sekund odhaluje nepratele v okruhu 'radius': cely Mirkuv tym je vidi i pres zdi (jmenovka se zivoty).
public class ScoutArrowAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    static readonly Color PulseColor = new Color(0.45f, 0.85f, 1f, 1f);

    FirstPersonController fpc;

    float nextUseTime;
    float activeUntil;

    // server
    bool zoneActive;
    Vector3 zonePosition;
    float zoneEnd;
    float nextScan;

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

    void Update()
    {
        if (IsOwner)
            OwnerUpdate();

        if (IsServer && zoneActive)
            ServerScan();
    }

    void OwnerUpdate()
    {
        if (ability == null) return;
        if (!HeroInput.Pressed(this, HeroInput.Key.E) || !HeroInput.Locked(this)) return;
        if (fpc.InputBlocked || Time.time < nextUseTime) return;

        nextUseTime = Time.time + ability.Cooldown;

        var eye = fpc.playerCamera.transform;
        ProceduralSfx.Play(ProceduralSfx.BowRelease, transform.position, 0.5f, 30f, spatial: fpc.Bot != null);
        GetComponent<PlayerHero>().SayAbility(ability);
        ShootServerRpc(eye.position, eye.forward);
    }

    // ---------------- server ----------------

    [ServerRpc]
    void ShootServerRpc(Vector3 origin, Vector3 direction)
    {
        var match = MatchManager.Instance;
        if (ability == null || direction.sqrMagnitude < 0.01f || (match != null && (match.IsOver || match.IsLobby))) return;

        direction.Normalize();

        // Sip leti rovne a zabodne se do prvni prekazky (hrace proleti).
        float distance = ability.range;
        var hits = Physics.RaycastAll(origin, direction, ability.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
            if (hit.collider.GetComponentInParent<BoulderHitbox>() != null) continue;

            distance = Mathf.Max(0.3f, hit.distance - 0.05f);
            break;
        }

        Vector3 point = origin + direction * distance;
        float flight = distance / Mathf.Max(1f, ability.speed);
        ShotClientRpc(origin + direction * 0.5f, point, flight, ability.duration);
        StartCoroutine(Arm(point, flight));
    }

    IEnumerator Arm(Vector3 point, float delay)
    {
        yield return new WaitForSeconds(delay);

        zoneActive = true;
        zonePosition = point;
        zoneEnd = Time.time + ability.duration;
        nextScan = 0f;
    }

    void ServerScan()
    {
        if (Time.time >= zoneEnd)
        {
            zoneActive = false;
            return;
        }

        if (Time.time < nextScan) return;
        nextScan = Time.time + 0.2f;

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;

        foreach (var client in MatchManager.PlayerSlots())
        {
            var player = client.PlayerObject;
            if (player == null || player.gameObject == gameObject) continue;
            if (Combat.SameTeam(gameObject, player.gameObject)) continue;

            var health = player.GetComponent<Health>();
            if (health == null || health.currentHealth.Value <= 0f) continue;
            if (Vector3.Distance(player.transform.position + Vector3.up, zonePosition) > ability.radius) continue;

            var hero = player.GetComponent<PlayerHero>();
            if (hero != null)
                hero.ServerReveal(0.5f);
        }
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void ShotClientRpc(Vector3 from, Vector3 to, float flight, float seconds)
    {
        if (IsOwner)
            activeUntil = Time.time + flight + seconds;
        else
            ProceduralSfx.Play(ProceduralSfx.BowRelease, from, 0.5f);

        var go = new GameObject("ScoutArrow");
        go.AddComponent<ScoutArrowVisual>().Init(from, to, flight, seconds, ability != null ? ability.radius : 10f, PulseColor);
    }
}

// Pruzkumny sip (jen efekt): doleti na misto, zabodne se a po dobu pusobeni vysila rozpinajici se kruhy.
public class ScoutArrowVisual : MonoBehaviour
{
    Vector3 from, to;
    float flight, seconds, radius, age;
    Color color;
    Transform arrow, pulse;
    Material pulseMaterial;
    Light glow;
    AudioSource flightAudio;

    public void Init(Vector3 start, Vector3 end, float flightSeconds, float activeSeconds, float zoneRadius, Color pulseColor)
    {
        from = start;
        to = end;
        flight = Mathf.Max(0.02f, flightSeconds);
        seconds = activeSeconds;
        radius = zoneRadius;
        color = pulseColor;

        var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(shaft.GetComponent<Collider>());
        shaft.transform.SetParent(transform, false);
        shaft.transform.localScale = new Vector3(0.05f, 0.05f, 0.8f);
        Fx.Paint(shaft, color);
        arrow = shaft.transform;

        var ring = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(ring.GetComponent<Collider>());
        ring.transform.SetParent(transform, false);
        pulseMaterial = new Material(Fx.ParticleMaterial) { mainTexture = null };
        var ringRenderer = ring.GetComponent<Renderer>();
        ringRenderer.sharedMaterial = pulseMaterial;
        ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pulse = ring.transform;
        ring.SetActive(false);

        transform.position = from;
        flightAudio = ProceduralSfx.PlayArrowFlight(transform);
        if ((to - from).sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(to - from);
    }

    void Update()
    {
        age += Time.deltaTime;

        if (age < flight)
        {
            transform.position = Vector3.Lerp(from, to, age / flight);
            return;
        }

        transform.position = to;
        if (flightAudio != null && flightAudio.isPlaying) flightAudio.Stop();

        if (glow == null)
        {
            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = color;
            glow.range = 6f;
            glow.intensity = 2f;
            pulse.gameObject.SetActive(true);
            ProceduralSfx.Play(ProceduralSfx.CaptureUnlock, to, 0.8f);
        }

        // Kruhy: kazdou sekundu se z mista rozepne pruhledna koule az na okraj odhalovane oblasti.
        float t = Mathf.Repeat(age - flight, 1f);
        pulse.localScale = Vector3.one * Mathf.Lerp(0.5f, radius * 2f, t);
        pulse.rotation = Quaternion.identity;
        pulseMaterial.color = new Color(color.r, color.g, color.b, 0.16f * (1f - t));

        if (age >= flight + seconds)
            Destroy(gameObject);
    }

    void OnDisable()
    {
        if (flightAudio != null) flightAudio.Stop();
    }
}
