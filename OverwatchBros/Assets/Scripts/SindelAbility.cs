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
    // Zvuk pred vybuchem (Resources/Audio/sindel_granat): granat vybuchne nejdriv po jeho delce od hodu,
    // zvuk hraje z granatu a konci prave s vybuchem.
    const float FallbackSoundLength = 1.4f;
    static AudioClip armSound;
    static AudioClip ArmSound => armSound != null ? armSound : (armSound = Resources.Load<AudioClip>("Audio/sindel_granat"));
    static float ArmTime => ArmSound != null ? ArmSound.length : FallbackSoundLength;
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
        if (!IsOwner || !HeroInput.Locked(this) || fpc.InputBlocked || health.currentHealth.Value <= 0f) return;

        if (leap != null && HeroInput.Pressed(this, HeroInput.Key.Shift) && Time.time >= nextLeap && !fpc.Rooted)
        {
            nextLeap = Time.time + leap.Cooldown;
            fpc.OwnerHover(leap.power, leap.duration);
            // skok ve smeru, kterym hrac jde (bez pohybu dopredu)
            fpc.AddImpulse(LeapDirection() * 8f);
            ProceduralSfx.Play(ProceduralSfx.LeapStart, transform.position, 0.5f);
            hero.SayAbility(leap);
            LeapServerRpc();
        }

        if (grenade != null && HeroInput.Pressed(this, HeroInput.Key.E) && Time.time >= nextGrenade)
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

    Vector3 LeapDirection()
    {
        Vector2 input = Vector2.zero;
        if (HeroInput.MoveKey(this, 'w')) input.y += 1f;
        if (HeroInput.MoveKey(this, 's')) input.y -= 1f;
        if (HeroInput.MoveKey(this, 'd')) input.x += 1f;
        if (HeroInput.MoveKey(this, 'a')) input.x -= 1f;
        Vector3 direction = transform.right * input.x + transform.forward * input.y;
        return direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
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
        fuse = Mathf.Max(fuse, ArmTime - flight);   // zvuk pred vybuchem musi dohrat
        ThrownClientRpc(origin, direction.normalized * grenade.speed + Vector3.up * Lift, flight, position, fuse);
        StartCoroutine(Detonate(position, flight + fuse));
    }

    IEnumerator Detonate(Vector3 point, float delay)
    {
        yield return new WaitForSeconds(delay);

        var match = MatchManager.Instance;
        if (match == null || (!match.IsOver && !match.IsLobby))
            Combat.Explode(gameObject, point, grenade.radius, grenade.power, Edge, null, Combat.AbilitySource(grenade));

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
        DestroyImmediate(ball.GetComponent<Collider>());
        ball.name = "HolyGrenade";
        ball.transform.localScale = Vector3.one * 0.2f;
        Fx.Paint(ball, GrenadeColor);
        foreach (var size in new[] { new Vector3(0.12f, 0.7f, 0.12f), new Vector3(0.5f, 0.12f, 0.12f) })
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(bar.GetComponent<Collider>());
            bar.transform.SetParent(ball.transform, false);
            bar.transform.localPosition = new Vector3(0f, size.y > 0.5f ? 0.75f : 0.85f, 0f);
            bar.transform.localScale = size;
            Fx.Paint(bar, new Color(0.95f, 0.9f, 0.8f));
        }
        ball.AddComponent<HolyGrenadeFlight>().Init(origin, velocity, Gravity, flight, landing, fuse);

        // zvuk pred vybuchem: hraje z granatu a konci s vybuchem
        var clip = ArmSound;
        if (clip != null)
        {
            var source = ball.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = 1f;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 5f;
            source.maxDistance = 40f;
            source.dopplerLevel = 0f;
            source.PlayDelayed(Mathf.Max(0f, flight + fuse - clip.length));
        }
    }

    [ClientRpc]
    // Svaty granat vybuchne mohutneji nez obycejny: dvojity ohnivy vybuch, zlaty sloup svetla,
    // tlakova vlna po zemi, jiskry do stran a hlasity dvojity vybuch slyset daleko.
    void DetonatedClientRpc(Vector3 point, float radius)
    {
        Fx.Explosion(point, radius * 1.6f);
        Fx.Explosion(point + Vector3.up * 1.2f, radius);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * 0.5f + 0.4f;
            Fx.Sparks(point + new Vector3(Mathf.Cos(angle), 0.4f, Mathf.Sin(angle)) * radius * 0.4f, EzekielAbility.HolyColor);
        }
        Fx.Sparks(point + Vector3.up * 0.3f, Color.white);

        var blast = new GameObject("FX_SvatyVybuch");
        blast.transform.position = point;
        blast.AddComponent<HolyBlast>().Init(radius);

        ProceduralSfx.Play(ProceduralSfx.Explosion, point, 1f, 70f);
        ProceduralSfx.Play(ProceduralSfx.Explosion, point + Vector3.up, 0.8f, 70f);
        ProceduralSfx.Play(ProceduralSfx.Gunshot, point, 0.9f, 70f);
    }
}

// Zlaty sloup svetla a tlakova vlna svateho granatu (jen efekt, sam zmizi).
public class HolyBlast : MonoBehaviour
{
    const float Seconds = 0.9f;
    const int Segments = 48;

    float radius, age;
    Transform pillar;
    Material pillarMaterial;
    LineRenderer ring;
    Light flash;

    public void Init(float blastRadius)
    {
        radius = blastRadius;

        // sloup svetla
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        DestroyImmediate(go.GetComponent<Collider>());
        go.name = "SloupSvetla";
        pillar = go.transform;
        pillar.SetParent(transform, false);
        pillarMaterial = new Material(Fx.ParticleMaterial) { mainTexture = null };
        go.GetComponent<MeshRenderer>().sharedMaterial = pillarMaterial;
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // tlakova vlna po zemi
        ring = gameObject.AddComponent<LineRenderer>();
        ring.loop = true;
        ring.useWorldSpace = false;
        ring.positionCount = Segments;
        ring.alignment = LineAlignment.View;
        ring.material = new Material(Fx.ParticleMaterial) { mainTexture = null };
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // zlaty zablesk
        var lightObject = new GameObject("Zablesk");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = Vector3.up * 2f;
        flash = lightObject.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = SindelAbility.GrenadeColor;
        flash.range = radius * 5f;
        flash.shadows = LightShadows.None;

        Update();
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = age / Seconds;
        if (t >= 1f) { Destroy(gameObject); return; }
        float fade = 1f - t;

        // sloup: rychle vyrazi nahoru, pak se zuzi a vybledne
        float height = Mathf.Lerp(2f, 14f, Mathf.Sqrt(t));
        float width = radius * 0.7f * Mathf.Lerp(1f, 0.15f, t);
        pillar.localPosition = Vector3.up * height * 0.5f;
        pillar.localScale = new Vector3(width, height * 0.5f, width);
        var c = SindelAbility.GrenadeColor;
        pillarMaterial.color = new Color(1f, Mathf.Lerp(0.95f, c.g, t), Mathf.Lerp(0.8f, c.b, t), 0.75f * fade * fade);

        // vlna: rozbehne se do dvojnasobku polomeru vybuchu
        float r = Mathf.Lerp(0.5f, radius * 2f, 1f - fade * fade);
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0.15f, Mathf.Sin(a) * r));
        }
        ring.widthMultiplier = Mathf.Lerp(0.6f, 0.1f, t);
        ring.startColor = ring.endColor = new Color(1f, 0.88f, 0.45f, 0.9f * fade);

        flash.intensity = 25f * fade * fade;
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
