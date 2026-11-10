using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Mirkova ultimatni schopnost (Q): Smrst. Vystreli pevne tocici se teleso (koule o polomeru 'radius' obehnana
// prstenci), ktere leti rovne dopredu rychlosti 'speed' po dobu 'duration' sekund a prochazi i zdmi.
// Nepratelum, kterych se dotyka, ubira 'power' zivotu za sekundu.
public class StormAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    const float TickInterval = 0.2f;

    // Delka valce smrsti jako nasobek polomeru (radius 6 -> 18 m). Valec zacina v bode vystrelu a miri dopredu.
    public const float LengthFactor = 3f;
    static readonly Color StormColor = new Color(0.55f, 0.75f, 1f, 1f);

    FirstPersonController fpc;
    PlayerHero hero;

    float nextUseTime;

    // server
    bool stormActive;
    Vector3 stormPosition, stormDirection;
    float stormEnd;
    float nextTick;

    // Server: vir prave leti (kvuli nabijeni ultimatky a hodnoceni akci).
    public bool IsStormActive => stormActive;

    bool UsesCharge => hero != null && hero.UsesUltCharge;
    bool CanUse => UsesCharge ? hero.UltReady : Time.time >= nextUseTime;
    public float CooldownRemaining => UsesCharge ? 0f : Mathf.Max(0f, nextUseTime - Time.time);

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
    }

    void Update()
    {
        if (IsOwner)
            OwnerUpdate();

        if (IsServer && stormActive)
            ServerTick();
    }

    const float WindupSeconds = 2f;
    readonly UltWindup windup = new UltWindup();
    WeaponShooting shooting;

    void OwnerUpdate()
    {
        if (ability == null) return;

        if (shooting == null)
            shooting = GetComponent<WeaponShooting>();

        // Priprava: 2 s se sam natahuje luk az na maximum (bez drzeni tlacitka), pak vyleti smrst.
        if (windup.Active)
        {
            bool done = windup.Tick(fpc);
            if (shooting != null)
                shooting.ForcedCharge = windup.Active ? windup.Progress : 0f;
            if (done)
                Fire();
            return;
        }

        if (!HeroInput.Pressed(this, HeroInput.Key.Q) || !HeroInput.Locked(this)) return;
        if (fpc.InputBlocked || !CanUse) return;

        windup.Begin(fpc, WindupSeconds);
        hero.SayAbility(ability);
        Fx.PlayGlobal(ProceduralSfx.UltCharge, 0.5f);
        WindupServerRpc();
    }

    void Fire()
    {
        if (shooting != null)
            shooting.ForcedCharge = 0f;

        nextUseTime = Time.time + ability.Cooldown;
        hero.SpendUlt();

        // Smrst leti presne tam, kam miri zamerovac (i nahoru nebo dolu).
        var eye = fpc.playerCamera.transform;
        Vector3 direction = eye.forward;
        ProceduralSfx.PlayForActor(this, ProceduralSfx.Dash, transform.position, 0.9f);
        LaunchServerRpc(eye.position + direction * 2.5f, direction);
    }

    // Vola PlayerRespawn / reset kola.
    public void Cancel()
    {
        if (!windup.Active) return;

        windup.Cancel(fpc);
        if (shooting != null)
            shooting.ForcedCharge = 0f;
    }

    void OnDisable()
    {
        if (windup.Active && fpc != null)
            Cancel();
    }

    [ServerRpc]
    void WindupServerRpc()
    {
        WindupClientRpc();
    }

    [ClientRpc]
    void WindupClientRpc()
    {
        if (!IsOwner)
            Fx.PlayGlobal(ProceduralSfx.UltCharge, 0.5f);
    }

    // ---------------- server ----------------

    [ServerRpc]
    void LaunchServerRpc(Vector3 origin, Vector3 direction)
    {
        var match = MatchManager.Instance;
        if (ability == null || direction.sqrMagnitude < 0.01f || (match != null && (match.IsOver || match.IsLobby))) return;

        stormActive = true;
        stormPosition = origin;
        stormDirection = direction.normalized;
        stormEnd = Time.time + ability.duration;
        nextTick = Time.time;

        var recorder = GetComponent<PotgRecorder>();
        if (recorder != null)
            recorder.ServerNoteUltimate();

        LaunchedClientRpc(origin, stormDirection, ability.speed, ability.duration, ability.radius);
    }

    void ServerTick()
    {
        stormPosition += stormDirection * ability.speed * Time.deltaTime;

        if (Time.time >= stormEnd)
        {
            stormActive = false;
            return;
        }

        if (Time.time < nextTick) return;
        nextTick = Time.time + TickInterval;

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;

        // Zamerne bez kontroly vyhledu: smrst prochazi zdmi.
        var hit = new HashSet<Health>();
        Vector3 center = stormPosition;
        float length = ability.radius * LengthFactor;
        Vector3 tail = center + stormDirection * ability.radius;
        Vector3 head = center + stormDirection * Mathf.Max(ability.radius, length - ability.radius);
        foreach (var col in Physics.OverlapCapsule(tail, head, ability.radius * 1.1f, ~0, QueryTriggerInteraction.Ignore))
        {
            var boulder = col.GetComponentInParent<BoulderHitbox>();
            if (boulder != null)
                boulder.Damage(gameObject, ability.power * TickInterval);

            var victim = col.GetComponentInParent<Health>();
            if (victim == null || !hit.Add(victim) || victim.currentHealth.Value <= 0f) continue;
            if (victim.gameObject == gameObject || Combat.SameTeam(gameObject, victim.gameObject)) continue;

            Combat.DamagePlayer(gameObject, victim, ability.power * TickInterval, Combat.AbilitySource(ability));
        }
    }

    // ---------------- vizual ----------------

    // Barva smrsti podle tymu toho, kdo ji vypustil: tym 0 modra, tym 1 cervena (jako barvy tymu v UI).
    Color TeamStormColor()
    {
        var team = GetComponent<PlayerTeam>();
        if (team == null) return StormColor;
        Color c = UiKit.TeamColor(team.teamId.Value);
        return Color.Lerp(c, Color.white, 0.15f);   // trochu svetlejsi - jadro sviti
    }

    [ClientRpc]
    void LaunchedClientRpc(Vector3 origin, Vector3 direction, float speed, float seconds, float radius)
    {
        var go = new GameObject("Storm");
        go.transform.position = origin;
        var hero = GetComponent<PlayerHero>();
        var model = hero != null && hero.Hero != null ? hero.Hero.characterPrefab : null;
        go.AddComponent<StormVisual>().Init(direction, speed, seconds, radius, TeamStormColor(), model);
        ProceduralSfx.PlayForActor(this, ProceduralSfx.LeapStart, origin, 1f);
    }
}

// Letici smrst (jen efekt): pevne teleso viditelne z dalky - svitici jadro, kolem nej tri prstence z hranolu,
// ktere se toci proti sobe, a za nim stopa castic. Pohybuje se stejne jako serverova smrst a prochazi zdmi.
public class StormVisual : MonoBehaviour
{
    Vector3 direction;
    float speed, seconds, age;
    Transform body;
    readonly Transform[] rings = new Transform[5];
    ParticleSystem trail;
    Light glow;
    float fullScale = 1f;

    public void Init(Vector3 moveDirection, float moveSpeed, float lifetime, float radius, Color color, GameObject headModel = null)
    {
        direction = moveDirection;
        speed = moveSpeed;
        seconds = lifetime;

        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction);

        float length = radius * StormAbility.LengthFactor;

        // Telo zacina v bode vystrelu a tahne se dopredu (stejne jako zasahova oblast na serveru).
        body = new GameObject("Body").transform;
        body.SetParent(transform, false);
        body.localPosition = new Vector3(0f, 0f, length * 0.5f);

        // Jadro: dlouhy valec se zakulacenymi konci, osou ve smeru letu.
        var core = Solid(PrimitiveType.Capsule, body, Vector3.zero, new Vector3(radius * 1.25f, length * 0.5f, radius * 1.25f),
            Color.Lerp(color, Color.white, 0.55f));
        core.localRotation = Quaternion.Euler(90f, 0f, 0f);
        core.name = "Core";

        // Prstence z hranolu kolem jadra.
        var dark = new Color(color.r * 0.45f, color.g * 0.5f, color.b * 0.75f);
        for (int r = 0; r < rings.Length; r++)
        {
            var ring = new GameObject("Ring").transform;
            ring.SetParent(body, false);
            ring.localPosition = new Vector3(0f, 0f, (r / (rings.Length - 1f) - 0.5f) * (length - radius * 1.2f));
            rings[r] = ring;

            const int pieces = 10;
            float ringRadius = radius * (r % 2 == 0 ? 0.9f : 1.05f);
            for (int i = 0; i < pieces; i++)
            {
                float angle = i * Mathf.PI * 2f / pieces;
                var piece = Solid(PrimitiveType.Cube, ring, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * ringRadius,
                    new Vector3(radius * 0.42f, radius * 0.16f, radius * 0.30f), i % 2 == 0 ? color : dark);
                piece.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
            }
        }

        // Stopa castic za telesem.
        var trailObject = new GameObject("Trail");
        trailObject.transform.SetParent(transform, false);
        trail = trailObject.AddComponent<ParticleSystem>();
        trail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = trail.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.3f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, color);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 500;

        var emission = trail.emission;
        emission.rateOverTime = 160f;

        var shape = trail.shape;
        // Castice se sypou po cele delce valce.
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(radius * 1.4f, radius * 1.4f, length);
        shape.position = new Vector3(0f, 0f, length * 0.5f);
        emission.rateOverTime = 260f;

        var fade = trail.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        trailObject.GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;
        trail.Play();

        var lightObject = new GameObject("Glow");
        lightObject.transform.SetParent(transform, false);
        glow = lightObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = color;
        glow.range = length * 1.5f;
        lightObject.transform.localPosition = new Vector3(0f, 0f, length * 0.5f);
        glow.intensity = 6f;

        // Celo smrsti: obri hlava hrdiny (Mirek) z jeho modelu, obliceje ve smeru letu.
        if (headModel != null) AddHead(headModel, radius, length, core.GetComponent<Renderer>().sharedMaterial);

        body.localScale = Vector3.zero;
    }

    // Dily obliceje avataru z fotky (MetaPerson): hlava, oci a bryle (bez zubu, ras a pokryvky hlavy).
    static readonly string[] HeadParts = { "avatarhead", "avatarlefteyeball", "avatarrighteyeball", "glasses" };

    static bool IsHeadPart(string name) => System.Array.IndexOf(HeadParts, name.ToLowerInvariant()) >= 0;

    // Oblicej z modelu (sit v zakladni poze) ze stejneho materialu jako jadro smrsti, zapusteny do predniho
    // vrchliku jadra - vystupuje z nej jen lehce (nos, celo, bryle). Oblicej dopredu (+z = smer letu).
    void AddHead(GameObject model, float radius, float length, Material material)
    {
        Transform root = model.transform;
        var holder = new GameObject("Hlava").transform;
        holder.SetParent(body, false);
        bool any = false;
        Bounds bounds = default;
        foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skin.sharedMesh == null || !IsHeadPart(skin.name)) continue;
            var part = new GameObject(skin.name).transform;
            part.SetParent(holder, false);
            part.localPosition = root.InverseTransformPoint(skin.transform.position);
            part.localRotation = Quaternion.Inverse(root.rotation) * skin.transform.rotation;
            Vector3 rs = root.lossyScale, ss = skin.transform.lossyScale;
            part.localScale = new Vector3(ss.x / Mathf.Max(1e-4f, rs.x), ss.y / Mathf.Max(1e-4f, rs.y), ss.z / Mathf.Max(1e-4f, rs.z));
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = skin.sharedMesh;
            var renderer = part.gameObject.AddComponent<MeshRenderer>();
            var materials = new Material[Mathf.Max(1, skin.sharedMesh.subMeshCount)];
            for (int m = 0; m < materials.Length; m++) materials[m] = material;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Bounds b = skin.sharedMesh.bounds;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                Vector3 p = part.localPosition + part.localRotation * Vector3.Scale(part.localScale, corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        if (!any) { DestroyImmediate(holder.gameObject); return; }
        // hlava zhruba na prumer jadra (0,625 x polomer je polomer jadra); spicka nosu kousek pred vrchlikem
        float coreRadius = radius * 0.625f;
        float scale = coreRadius * 1.9f / Mathf.Max(0.05f, bounds.size.y);
        holder.localScale = Vector3.one * scale;
        float tip = length * 0.5f + coreRadius * 0.18f;
        holder.localPosition = new Vector3(-bounds.center.x * scale, -bounds.center.y * scale, tip - bounds.max.z * scale);
    }

    static Transform Solid(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        Fx.Paint(go, color);
        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    // Nahled v editoru (StormPreview): smrst stoji, plna velikost, prstence se toci.
    public void PreviewTick(float dt)
    {
        age += dt;
        body.localScale = Vector3.one * fullScale;
        for (int r = 0; r < rings.Length; r++)
            rings[r].Rotate(0f, 0f, (r % 2 == 1 ? -300f : 220f) * dt, Space.Self);
        glow.intensity = 5f + Mathf.Sin(age * 14f) * 1.5f;
    }

    void Update()
    {
        age += Time.deltaTime;
        transform.position += direction * speed * Time.deltaTime;

        // Rychly nabeh na plnou velikost, na konci se smrskne.
        float grow = Mathf.Clamp01(age / 0.25f);
        float shrink = Mathf.Clamp01((seconds - age) / 0.4f);
        body.localScale = Vector3.one * fullScale * Mathf.Min(grow, shrink);

        // Prstence se toci kolem osy letu, prostredni proti smeru krajnich; jadro pulzuje.
        for (int r = 0; r < rings.Length; r++)
            rings[r].Rotate(0f, 0f, (r % 2 == 1 ? -300f : 220f) * Time.deltaTime, Space.Self);
        glow.intensity = 5f + Mathf.Sin(age * 14f) * 1.5f;

        if (age >= seconds && trail.isEmitting)
        {
            trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            glow.enabled = false;
        }

        if (age >= seconds + 1.2f)
            Destroy(gameObject);
    }
}
