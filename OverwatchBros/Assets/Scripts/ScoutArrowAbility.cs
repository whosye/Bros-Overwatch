using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Mirkuv pruzkumny sip (klavesa E): balistika plne natazeneho luku (nejdal 'range' m). Kde se zabodne, tam na 'duration'
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
        var hero = GetComponent<PlayerHero>();
        var bow = hero != null && hero.Hero != null ? hero.Hero.weapon : null;
        if (bow == null || !bow.IsCharged) return;

        // Same launch point, velocity and semi-implicit gravity steps as ProjectileSim.
        // Preserve the scout arrow's world-only collision and its separate reveal range.
        float dt = Time.fixedDeltaTime;
        var path = TraceFlight(origin + direction * 0.6f, direction * bow.projectileSpeed,
            bow.projectileGravity, Mathf.Min(0.05f, bow.projectileRadius), ability.range, dt);
        Vector3 point = path[path.Length - 1];
        float flight = (path.Length - 1) * dt;
        ShotClientRpc(path, dt, ability.duration);
        StartCoroutine(Arm(point, flight));
    }

    static Vector3[] TraceFlight(Vector3 position, Vector3 velocity, float gravity, float radius, float range, float dt)
    {
        var path = new List<Vector3> { position };
        float traveled = 0f;
        for (int i = 0; i < Mathf.CeilToInt(10f / dt) && traveled < range; i++)
        {
            velocity.y -= gravity * dt;
            Vector3 step = velocity * dt;
            float distance = Mathf.Min(step.magnitude, range - traveled);
            if (distance <= 0.0001f) break;
            Vector3 direction = step.normalized;
            var hits = Physics.SphereCastAll(position, radius, direction, distance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
                if (hit.collider.GetComponentInParent<BoulderHitbox>() != null) continue;
                path.Add(hit.distance > 0f ? hit.point : position);
                return path.ToArray();
            }
            position += direction * distance;
            traveled += distance;
            path.Add(position);
        }
        if (path.Count == 1) path.Add(position);
        return path.ToArray();
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
    void ShotClientRpc(Vector3[] path, float stepSeconds, float seconds)
    {
        if (path == null || path.Length < 2) return;
        float flight = (path.Length - 1) * stepSeconds;
        if (IsOwner)
            activeUntil = Time.time + flight + seconds;
        else
            ProceduralSfx.Play(ProceduralSfx.BowRelease, path[0], 0.5f);

        var go = new GameObject("ScoutArrow");
        go.AddComponent<ScoutArrowVisual>().Init(path, stepSeconds, seconds, ability != null ? ability.radius : 10f, PulseColor);
    }
}

// Pruzkumny sip (jen efekt): doleti na misto, zabodne se a po dobu pusobeni vysila rozpinajici se kruhy.
public class ScoutArrowVisual : MonoBehaviour
{
    Vector3[] path;
    Vector3 to;
    float stepSeconds;
    float flight, seconds, radius, age;
    Color color;
    Transform arrow, pulse;
    Material pulseMaterial;
    Light glow;
    AudioSource flightAudio;

    public void Init(Vector3[] flightPath, float fixedStep, float activeSeconds, float zoneRadius, Color pulseColor)
    {
        path = flightPath;
        stepSeconds = Mathf.Max(0.001f, fixedStep);
        to = path[path.Length - 1];
        flight = (path.Length - 1) * stepSeconds;
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

        transform.position = path[0];
        flightAudio = ProceduralSfx.PlayArrowFlight(transform);
        FaceSegment(0);
    }

    void Update()
    {
        age += Time.deltaTime;

        if (age < flight)
        {
            float frame = age / stepSeconds;
            int segment = Mathf.Min(Mathf.FloorToInt(frame), path.Length - 2);
            transform.position = Vector3.Lerp(path[segment], path[segment + 1], frame - segment);
            FaceSegment(segment);
            return;
        }

        transform.position = to;
        FaceSegment(path.Length - 2);
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

    void FaceSegment(int segment)
    {
        Vector3 direction = path[segment + 1] - path[segment];
        if (direction.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(direction);
    }

    void OnDestroy()
    {
        if (pulseMaterial != null) Destroy(pulseMaterial);
    }

    void OnDisable()
    {
        if (flightAudio != null) flightAudio.Stop();
    }
}
