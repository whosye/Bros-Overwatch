using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Sindel - Shift a E v jedne komponente:
//   Shift (Spravedlivy skok): vyskoci rychlosti 'power' m/s a kus nad zemi se vznasi (pomaly pad), celkem 'duration' s;
//   E (Svaty granat): hodi granat obloukem rychlosti 'speed'. Vybuchne hned o nepritele, jinak chvili po dopadu:
//     'power' poskozeni ve stredu, k okraji 'radius' mene. Neprochazi zdmi, nezrani vlastni tym.
//   prave tlacitko ("Say what again?!"): vykrik do kuzele pred sebe (dosah 'range' m, polovicni uhel 'radius' st.).
//     Zasazene nepratele (s primym vyhledem) na 'duration' s vystrasi: zpomali je na 'speed' (0.6 = 60 %) a nemohou
//     pouzivat schopnosti (strilet muzou). Lehce je odstrci ('knockback') a zrani ('power').
// "Say what again?!" - zlata zvukova vlna (oblouky) letici do kuzele pred Sindelem, s napisem.
public class SayWhatWave : MonoBehaviour
{
    const float Lifetime = 0.55f;
    const int Arcs = 4;
    const int Segments = 18;
    const float HalfAngle = 45f;

    Vector3 origin, direction;
    float range, age;
    LineRenderer[] arcs;
    TMPro.TextMeshPro label;

    public static void Spawn(Vector3 origin, Vector3 direction, float range)
    {
        ReplayLog.SayWhatWave(origin, direction, range);
        if (direction.sqrMagnitude < 0.01f) return;
        var go = new GameObject("FX_SayWhat");
        var wave = go.AddComponent<SayWhatWave>();
        wave.origin = origin;
        wave.direction = direction.normalized;
        wave.range = range;
        wave.arcs = new LineRenderer[Arcs];
        var material = Fx.ParticleMaterial;   // (pruhledny, barva z cary)
        for (int i = 0; i < Arcs; i++)
        {
            var arc = new GameObject("Arc").AddComponent<LineRenderer>();
            arc.transform.SetParent(go.transform, false);
            arc.positionCount = Segments;
            arc.useWorldSpace = true;
            arc.material = material;
            arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arc.numCapVertices = 4;
            wave.arcs[i] = arc;
        }
        var text = new GameObject("Label");
        text.transform.SetParent(go.transform, false);
        wave.label = text.AddComponent<TMPro.TextMeshPro>();
        wave.label.text = "SAY WHAT?!";
        wave.label.fontSize = 5f;
        wave.label.fontStyle = TMPro.FontStyles.Bold;
        wave.label.alignment = TMPro.TextAlignmentOptions.Center;
        wave.label.color = new Color(1f, 0.85f, 0.4f);
        wave.Update();
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / Lifetime);
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        Vector3 up = flat.sqrMagnitude > 0.01f ? Vector3.Cross(direction, Vector3.Cross(Vector3.up, direction)).normalized : Vector3.forward;
        for (int i = 0; i < Arcs; i++)
        {
            // oblouky postupne vyleti a rozsiri se do kuzele
            float local = Mathf.Clamp01(t * 1.6f - i * 0.18f);
            float distance = Mathf.Lerp(0.8f, range, local);
            float half = HalfAngle * Mathf.Deg2Rad;
            float alpha = local <= 0f ? 0f : (1f - local) * 0.9f;
            var arc = arcs[i];
            arc.enabled = alpha > 0.01f;
            if (!arc.enabled) continue;
            arc.startWidth = arc.endWidth = Mathf.Lerp(0.12f, 0.03f, local);
            var color = new Color(1f, 0.82f, 0.35f, alpha);
            arc.startColor = arc.endColor = color;
            for (int s = 0; s < Segments; s++)
            {
                float a = Mathf.Lerp(-half, half, s / (Segments - 1f));
                Vector3 dir = Quaternion.AngleAxis(a * Mathf.Rad2Deg, up) * direction;
                arc.SetPosition(s, origin + dir * distance + Vector3.down * 0.25f);
            }
        }

        // napis vyleti z ust a zmizi
        label.transform.position = origin + direction * Mathf.Lerp(1.2f, 3.5f, t) + Vector3.up * (0.3f + t * 0.4f);
        var cam = Camera.main;
        if (cam != null) label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position);
        label.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.25f, Mathf.Min(1f, t * 3f));
        label.alpha = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;

        if (age >= Lifetime) Destroy(gameObject);
    }
}

// "?!" nad hlavou vystraseneho hrace (vsichni ho vidi; zmizi po case).
public class FearMarker : MonoBehaviour
{
    float until;
    TMPro.TextMeshPro text;

    public static void Show(Transform player, float seconds)
    {
        ReplayLog.FearShow(player, seconds);
        var marker = player.GetComponentInChildren<FearMarker>(true);
        if (marker == null)
        {
            var go = new GameObject("FearMarker");
            go.transform.SetParent(player, false);
            marker = go.AddComponent<FearMarker>();
            marker.text = go.AddComponent<TMPro.TextMeshPro>();
            marker.text.text = "?!";
            marker.text.fontSize = 7f;
            marker.text.alignment = TMPro.TextAlignmentOptions.Center;
            marker.text.color = new Color(0.85f, 0.35f, 1f);
            marker.text.fontStyle = TMPro.FontStyles.Bold;
        }
        marker.until = Time.time + seconds;
        marker.gameObject.SetActive(true);
    }

    void LateUpdate()
    {
        if (Time.time >= until)
        {
            gameObject.SetActive(false);
            return;
        }
        var cam = Camera.main;
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        // trese se strachy
        transform.localPosition = new Vector3(Mathf.Sin(Time.time * 40f) * 0.04f, 2.4f, 0f);
    }
}

public class SindelAbility : NetworkBehaviour
{
    public AbilityDefinition leap;      // Shift
    public AbilityDefinition grenade;   // E
    public AbilityDefinition sayWhat;   // prave tlacitko

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
    float nextLeap, nextGrenade, nextSayWhat;

    public float LeapRemaining => Mathf.Max(0f, nextLeap - Time.time);
    public float GrenadeRemaining => Mathf.Max(0f, nextGrenade - Time.time);
    public float SayWhatRemaining => Mathf.Max(0f, nextSayWhat - Time.time);

    public void Configure(AbilityDefinition shift, AbilityDefinition e, AbilityDefinition rmb = null)
    {
        leap = shift;
        grenade = e;
        sayWhat = rmb;
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
            ProceduralSfx.PlayForActor(this, ProceduralSfx.LeapStart, transform.position, 0.5f);
            hero.SayAbility(leap);
            LeapServerRpc();
        }

        if (grenade != null && HeroInput.Pressed(this, HeroInput.Key.E) && Time.time >= nextGrenade)
        {
            nextGrenade = Time.time + grenade.Cooldown;
            var eye = fpc.playerCamera.transform;
            ProceduralSfx.PlayForActor(this, ProceduralSfx.Dash, transform.position, 0.5f);
            hero.SayAbility(grenade);
            var held = GetComponent<HeldWeapons>();
            if (held != null) held.PlayThrow();
            ThrowServerRpc(eye.position + eye.forward * 0.5f, eye.forward);
        }

        if (sayWhat != null && HeroInput.Pressed(this, HeroInput.Key.RightMouse) && Time.time >= nextSayWhat)
        {
            nextSayWhat = Time.time + sayWhat.Cooldown;
            var eye = fpc.playerCamera.transform;
            hero.SayAbility(sayWhat);
            var held = GetComponent<HeldWeapons>();
            if (held != null) held.PlayTaunt();
            SayWhatServerRpc(eye.position, eye.forward);
        }
    }

    float SayWhatRange => sayWhat != null && sayWhat.range > 0.5f ? sayWhat.range : 9f;
    float SayWhatAngle => sayWhat != null && sayWhat.radius > 5f ? sayWhat.radius : 45f;

    [ServerRpc]
    void SayWhatServerRpc(Vector3 origin, Vector3 direction)
    {
        var match = MatchManager.Instance;
        if (sayWhat == null || direction.sqrMagnitude < 0.01f || (match != null && (match.IsOver || match.IsLobby))) return;
        if ((origin - transform.position).sqrMagnitude > 9f) origin = transform.position + Vector3.up * 1.6f;
        direction.Normalize();

        float range = SayWhatRange;
        float seconds = sayWhat.duration > 0.1f ? sayWhat.duration : 1.5f;
        float slow = sayWhat.speed > 0.05f && sayWhat.speed < 1f ? sayWhat.speed : 0.6f;
        foreach (var other in FindObjectsByType<PlayerHero>())
        {
            if (other.gameObject == gameObject || !other.IsSpawned || other.IsJoining || other.IsUntargetable) continue;
            if (Combat.SameTeam(gameObject, other.gameObject)) continue;
            var otherHealth = other.GetComponent<Health>();
            if (otherHealth == null || otherHealth.currentHealth.Value <= 0f) continue;

            Vector3 chest = other.transform.position + Vector3.up * 1.2f;
            Vector3 to = chest - origin;
            if (to.magnitude > range + 0.5f || Vector3.Angle(direction, to) > SayWhatAngle) continue;
            if (Physics.Linecast(origin, chest, out RaycastHit block, ~0, QueryTriggerInteraction.Ignore))
            {
                var blocker = block.collider.GetComponentInParent<PlayerHero>();
                if (blocker == null) continue;   // zed mezi nimi
            }

            if (sayWhat.power > 0f)
                Combat.DamagePlayer(gameObject, otherHealth, sayWhat.power, Combat.AbilitySource(sayWhat));
            if (otherHealth.currentHealth.Value <= 0f) continue;

            var movement = other.GetComponent<FirstPersonController>();
            if (movement == null) continue;
            movement.ServerSlow(seconds, slow);
            movement.ServerSilence(seconds);
            Vector3 push = to;
            push.y = 0f;
            if (sayWhat.knockback > 0f && push.sqrMagnitude > 0.01f)
                movement.ServerKnockback(push.normalized * sayWhat.knockback + Vector3.up * 1.5f);
        }
        SayWhatClientRpc(origin, direction, range);
    }

    [ClientRpc]
    void SayWhatClientRpc(Vector3 origin, Vector3 direction, float range)
    {
        SayWhatWave.Spawn(origin, direction, range);
        // ostatni vidi gesto s pistoli na postave (vlastnik ho uz prehral)
        if (!IsOwner)
        {
            var held = GetComponent<HeldWeapons>();
            if (held != null) held.PlayTaunt();
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
