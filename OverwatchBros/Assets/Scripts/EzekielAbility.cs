using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Sindelova ultimatka (Q): "Ezechiel 25:17". Vznese se vysoko do vzduchu, pres celou mapu hraje hlaska a na zemi, kam miri,
// sviti kruh - vidi ho jen Sindel sam (a pak killcam / POTG ze zaznamu); ostatni slysi hlasku a vidi zariciho Sindela na nebi.
// Mireni ignoruje zdi a strechy: kruh lezi tam, kde paprsek z kamery protne nejspodnejsi podlahu (ne patro), a krome
// plneho kruhu je videt i cerchovany obrys pres vse (i kdyz je kruh v dome). Ve vzduchu se da pomalu pohybovat.
// Na konci hlasky ("...when I lay my vengeance upon thee!") do kruhu uderi sloup svetla: 'power' ve stredu, k okraji
// mene ('radius'). Sloup zasahne kazdeho ve svislem valci nad i pod kruhem (vsechna patra, sklepy). Doba = delka nahravky
// ultimatky (Audio/Sindel/ability_Q), bez nahravky 'duration'.
// Behem kazani nejde Sindel prerusit (omraceni, uspani, hak... - FirstPersonController.Unstoppable), ale ma jen stit
// CastShield HP navic - da se zabit.
public class EzekielAbility : NetworkBehaviour
{
    enum Phase { Idle, Rising, Preaching, Descending }

    public AbilityDefinition ability;

    public float hoverHeight = 25f;
    public float riseTime = 1.4f;
    const float HoverSpeed = 7f;      // rychlost letu ve vysce (WASD)
    public float cameraDistance = 6.5f;
    public float cameraLift = 1.2f;
    const float StrikeEdge = 0.35f;
    const float BeamBelow = 300f;    // sloup zasahuje vse pod kruhem
    const float BeamHeight = 300f;   // i nad nim (vsechna patra)
    const float CastShield = 3000f;  // stit po dobu kazani
    const float FloorSearchDepth = 120f;
    public static readonly Color HolyColor = new Color(1f, 0.85f, 0.45f, 1f);

    CharacterController controller;
    FirstPersonController fpc;
    PlayerHero hero;
    Camera playerCamera;

    Phase phase = Phase.Idle;
    float phaseTimer;
    float castEnd;
    float targetY;
    float aimFloorY;   // vyska podlahy, se kterou se paprsek mireni naposled protnul
    Vector3 savedCameraLocalPosition;
    float nextUseTime;

    // Sdileny stav pro vsechny (kruh na zemi, aura).
    readonly NetworkVariable<bool> casting = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<Vector3> aim = new NetworkVariable<Vector3>(Vector3.zero,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<bool> aimValid = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<float> castSeconds = new NetworkVariable<float>(6f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public bool IsCasting => casting.Value;
    public bool AimValid => aimValid.Value;
    public Vector3 Aim => aim.Value;
    public float CastProgress => casting.Value ? Mathf.Clamp01((Time.time - castStartLocal) / Mathf.Max(0.5f, castSeconds.Value)) : 0f;
    public float Radius => ability != null ? ability.radius : 5f;
    bool UsesCharge => hero != null && hero.UsesUltCharge;
    bool CanUse => UsesCharge ? hero.UltReady : Time.time >= nextUseTime;
    public float CooldownRemaining => UsesCharge ? 0f : Mathf.Max(0f, nextUseTime - Time.time);

    // vizual (vsichni)
    GameObject circle, core;
    Material circleMaterial;
    Light circleLight;
    ParticleSystem halo;
    Light haloLight;
    float castStartLocal;

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
    }

    public override void OnNetworkSpawn()
    {
        playerCamera = fpc.playerCamera;
        casting.OnValueChanged += OnCastingChanged;
    }

    public override void OnNetworkDespawn()
    {
        casting.OnValueChanged -= OnCastingChanged;
        HideVisual();
    }

    void OnDisable()
    {
        Cancel();
        HideVisual();
    }

    void OnCastingChanged(bool previous, bool current)
    {
        if (current) castStartLocal = Time.time;
        else HideVisual();
    }

    // Doba kazani = nejdelsi nahravka ultimatky (bez fazi step_N).
    float CastDuration()
    {
        float longest = 0f;
        if (ability != null && ability.voiceLines != null)
            foreach (var clip in ability.voiceLines)
                if (clip != null && !clip.name.ToLowerInvariant().StartsWith("step_"))
                    longest = Mathf.Max(longest, clip.length);
        float seconds = longest > 0.5f ? longest : (ability != null ? ability.duration : 6f);
        return Mathf.Clamp(seconds, 2.5f, 25f);
    }

    void Update()
    {
        UpdateVisual();

        if (!IsOwner || ability == null) return;

        if (phase == Phase.Idle)
        {
            if (HeroInput.Pressed(this, HeroInput.Key.Q) && CanUse && HeroInput.Locked(this)
                && !fpc.InputBlocked && !fpc.Rooted)
                Begin();
            return;
        }

        // behem kazani ho nic neprerusi (jen smrt nebo konec zapasu); pri sestupu uz ano
        bool stop = casting.Value ? fpc.IsDead || fpc.MatchOver || fpc.InLobby || fpc.Joining : fpc.CannotAct;
        if (stop)
        {
            Cancel();
            return;
        }

        switch (phase)
        {
            case Phase.Rising: TickRising(); break;
            case Phase.Preaching: TickPreaching(); break;
            case Phase.Descending: TickDescending(); break;
        }

        if (phase != Phase.Idle)
            ThirdPersonCamera.PlaceOrbit(transform, NetworkObject, playerCamera, savedCameraLocalPosition, cameraDistance, cameraLift);
    }

    void Begin()
    {
        nextUseTime = Time.time + ability.Cooldown;
        hero.SpendUlt();
        fpc.AbilityActive = true;
        fpc.ResetVertical();
        savedCameraLocalPosition = playerCamera.transform.localPosition;

        float seconds = CastDuration();
        castEnd = Time.time + seconds;
        targetY = transform.position.y + hoverHeight;
        aimFloorY = LowestFloorAt(transform.position.x, transform.position.z, transform.position.y + 2f, out float startFloor) ? startFloor : transform.position.y;
        phase = Phase.Rising;

        castSeconds.Value = seconds;
        aimValid.Value = false;
        casting.Value = true;
        hero.SayAbility(ability);
        BeginServerRpc(seconds);
    }

    void TickRising()
    {
        float step = hoverHeight / Mathf.Max(0.05f, riseTime) * Time.deltaTime;
        float newY = Mathf.MoveTowards(transform.position.y, targetY, step);
        var flags = controller.Move(Vector3.up * (newY - transform.position.y));
        UpdateAim();

        if ((flags & CollisionFlags.Above) != 0 || Mathf.Abs(transform.position.y - targetY) < 0.1f)
            phase = Phase.Preaching;
        if (Time.time >= castEnd)
            Strike();
    }

    void TickPreaching()
    {
        // Pomaly let ve vysce (WASD podle smeru kamery).
        Vector2 input = Vector2.zero;
        if (HeroInput.MoveKey(this, 'w')) input.y += 1f;
        if (HeroInput.MoveKey(this, 's')) input.y -= 1f;
        if (HeroInput.MoveKey(this, 'd')) input.x += 1f;
        if (HeroInput.MoveKey(this, 'a')) input.x -= 1f;

        // Pri pohledu skoro kolmo dolu (miri pod sebe) je vodorovna slozka smeru kamery nulova - pak "dopredu"
        // urcuje horni hrana obrazovky.
        var cam = playerCamera.transform;
        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.04f) forward = Vector3.ProjectOnPlane(cam.up, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 move = (forward * input.y + right * input.x);
        if (move.sqrMagnitude > 1f) move.Normalize();
        // drobne vlneni nahoru a dolu
        float bob = Mathf.Sin(Time.time * 2.2f) * 0.25f;
        controller.Move(move * HoverSpeed * Time.deltaTime + Vector3.up * (targetY + bob - transform.position.y) * Mathf.Min(1f, Time.deltaTime * 3f));

        UpdateAim();
        if (Time.time >= castEnd)
            Strike();
    }

    void UpdateAim()
    {
        // Z kamery (krizek uprostred obrazovky ukazuje, kam kruh dopadne). Zdi, strechy ani hraci paprsek nezastavi:
        // hleda se misto, kde protne nejspodnejsi podlahu (par kroku - podlaha pod novym mistem muze byt jinde vysoko).
        Vector3 origin = playerCamera.transform.position;
        Vector3 direction = playerCamera.transform.forward;
        bool found = false;
        Vector3 point = Vector3.zero;
        if (direction.y < -0.02f)
        {
            float floorY = aimFloorY;
            for (int i = 0; i < 5; i++)
            {
                float distance = (floorY - origin.y) / direction.y;
                if (distance <= 0f) { found = false; break; }
                point = origin + direction * distance;
                if (!LowestFloorAt(point.x, point.z, origin.y + 5f, out float y)) { found = false; break; }
                found = true;
                bool settled = Mathf.Abs(y - floorY) < 0.05f;
                floorY = y;
                if (settled) break;
            }
            if (found)
            {
                float distance = (floorY - origin.y) / direction.y;
                point = origin + direction * Mathf.Max(0f, distance);
                point.y = floorY;
                aimFloorY = floorY;
                Vector3 flat = point - transform.position;
                flat.y = 0f;
                found = flat.magnitude <= ability.range;
            }
        }
        if (found) SetAim(point, true);
        else if (aimValid.Value) aimValid.Value = false;
    }

    // Vyska nejnizsi vodorovne plochy v miste x, z (prizemi / zem pod patry a strechami), hledano shora od 'top'.
    static bool LowestFloorAt(float x, float z, float top, out float floorY)
    {
        floorY = 0f;
        bool found = false;
        foreach (var hit in Physics.RaycastAll(new Vector3(x, top, z), Vector3.down, FloorSearchDepth, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.normal.y < 0.3f || hit.collider.GetComponentInParent<PlayerHero>() != null) continue;
            if (!found || hit.point.y < floorY) floorY = hit.point.y;
            found = true;
        }
        return found;
    }

    void SetAim(Vector3 point, bool valid)
    {
        if ((aim.Value - point).sqrMagnitude > 0.0004f) aim.Value = point;
        if (aimValid.Value != valid) aimValid.Value = valid;
    }

    void Strike()
    {
        if (aimValid.Value)
            StrikeServerRpc(aim.Value);
        CastOverServerRpc();
        casting.Value = false;
        phase = Phase.Descending;
        phaseTimer = 4f;
    }

    void TickDescending()
    {
        phaseTimer -= Time.deltaTime;
        controller.Move(Vector3.down * 7f * Time.deltaTime);
        if (controller.isGrounded || phaseTimer <= 0f)
            Finish();
    }

    public void Cancel()
    {
        if (phase == Phase.Idle) return;
        Finish();
    }

    void Finish()
    {
        phase = Phase.Idle;
        if (playerCamera != null)
            playerCamera.transform.localPosition = savedCameraLocalPosition;
        if (fpc != null)
        {
            fpc.AbilityActive = false;
            fpc.ResetVertical();
        }
        if (IsSpawned && IsOwner && casting.Value)
            casting.Value = false;
        if (IsSpawned && IsOwner)
            EndServerRpc();
    }

    // ---------------- server ----------------

    // Behem kazani ma Sindel velky stit (da se ale zabit); po uderu stit zmizi.
    [ServerRpc]
    void BeginServerRpc(float seconds)
    {
        GetComponent<Health>().ServerHoldShield(CastShield, riseTime + Mathf.Clamp(seconds, 0f, 25f) + 0.5f);
        BeginClientRpc();
    }

    [ServerRpc]
    void CastOverServerRpc()
    {
        GetComponent<Health>().ServerClearShield();
    }

    [ServerRpc]
    void EndServerRpc()
    {
        var health = GetComponent<Health>();
        if (health.shield.Value > 1000f) health.ServerClearShield();   // (preruseno pred uderem)
    }

    [ServerRpc]
    void StrikeServerRpc(Vector3 point)
    {
        var match = MatchManager.Instance;
        if (ability == null || (match != null && (match.IsOver || match.IsLobby))) return;
        Vector3 flat = point - transform.position;
        flat.y = 0f;
        if (flat.magnitude > ability.range + 10f) return;

        var recorder = GetComponent<PotgRecorder>();
        if (recorder != null)
            recorder.ServerNoteUltimate();

        BeamDamage(point);
        StrikeClientRpc(point, ability.radius);
    }

    // Sloup svetla zasahne vse ve svislem valci nad kruhem (od kousek pod zemi az k nebi) - i skrz stropy a patra,
    // takze kdyz kruh lezi v prizemi, dostanou to i nepratele v patre nad nim. Poskozeni podle vodorovne
    // vzdalenosti od osy sloupu: 'power' uprostred, 'StrikeEdge' na okraji.
    void BeamDamage(Vector3 point)
    {
        float radius = ability.radius;
        Vector3 bottom = point + Vector3.down * (BeamBelow - radius);
        Vector3 top = point + Vector3.up * (BeamHeight - radius);
        var damaged = new System.Collections.Generic.HashSet<Health>();
        foreach (var col in Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 flat = col.bounds.center - point;
            flat.y = 0f;
            float scaled = ability.power * Mathf.Lerp(1f, StrikeEdge, Mathf.Clamp01(flat.magnitude / radius));

            var dummy = col.GetComponentInParent<Target>();
            if (dummy != null)
                dummy.TakeDamage(scaled);

            var boulder = col.GetComponentInParent<BoulderHitbox>();
            if (boulder != null)
                boulder.Damage(gameObject, scaled);

            var trap = col.GetComponentInParent<TrapHitbox>();
            if (trap != null)
                trap.Damage(gameObject, scaled);

            var health = col.GetComponentInParent<Health>();
            if (health == null || !damaged.Add(health)) continue;
            if (health.gameObject == gameObject || Combat.SameTeam(gameObject, health.gameObject)) continue;

            // u hrace podle jeho pozice (ne podle kusu tela, ktery do valce zasahl)
            Vector3 feet = health.transform.position - point;
            feet.y = 0f;
            Combat.DamagePlayer(gameObject, health, ability.power * Mathf.Lerp(1f, StrikeEdge, Mathf.Clamp01(feet.magnitude / radius)), Combat.AbilitySource(ability));
        }
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void BeginClientRpc()
    {
        Fx.Sparks(transform.position + Vector3.up, HolyColor);
        ProceduralSfx.PlayForActor(this, ProceduralSfx.LeapStart, transform.position, 0.8f);
    }

    [ClientRpc]
    void StrikeClientRpc(Vector3 point, float radius)
    {
        Fx.PlayGlobal(ProceduralSfx.Explosion, 0.9f);
        Fx.Explosion(point, radius);
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.PI * 2f;
            Fx.Sparks(point + new Vector3(Mathf.Cos(a), 0.3f, Mathf.Sin(a)) * radius * 0.7f, HolyColor);
        }
        HolyBeam.Spawn(point, radius);
    }

    void UpdateVisual()
    {
        if (!casting.Value)
            return;

        float seconds = Mathf.Max(0.5f, castSeconds.Value);
        float progress = Mathf.Clamp01((Time.time - castStartLocal) / seconds);
        float radius = ability != null ? ability.radius : 5f;

        if (circle == null) BuildVisual();

        // Kruh vidi jen Sindel sam.
        bool show = aimValid.Value && SeenByLocalPlayer();
        circle.SetActive(show);
        if (show)
        {
            // Kruh houstne a na konci rychle pulzuje.
            float pulse = progress > 0.75f ? 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 30f, (progress - 0.75f) * 4f)) : 1f;
            circle.transform.position = aim.Value + Vector3.up * 0.04f;
            circle.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
            core.transform.localScale = new Vector3(progress, 1.5f, progress);
            var color = Color.Lerp(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.6f, 0.15f), progress);
            if (circleMaterial.HasProperty("_BaseColor")) circleMaterial.SetColor("_BaseColor", color);
            else circleMaterial.color = color;
            circleLight.intensity = (1.5f + progress * 6f) * pulse;
            circleLight.range = radius * 1.8f;
        }

        if (haloLight != null)
            haloLight.intensity = 2.5f + Mathf.Sin(Time.time * 6f) * 0.7f;
    }

    bool SeenByLocalPlayer() => IsOwner && !BotBrain.IsBot(gameObject);

    void BuildVisual()
    {
        circle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        circle.name = "FX_EzekielCircle";   // (zaznam ho kresli sam podle mireni - EzekielCircleVisual - at je i u ostatnich)
        DestroyImmediate(circle.GetComponent<Collider>());
        Fx.Paint(circle, HolyColor);
        circleMaterial = circle.GetComponent<Renderer>().material;

        core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        core.name = "Core";
        DestroyImmediate(core.GetComponent<Collider>());
        core.transform.SetParent(circle.transform, false);
        core.transform.localPosition = Vector3.up * 0.5f;
        Fx.Paint(core, Color.white);

        EzekielCircleVisual.AddDashedOutline(circle.transform);

        var lightObject = new GameObject("Light");
        lightObject.transform.SetParent(circle.transform, false);
        lightObject.transform.localPosition = Vector3.up * 80f;   // (meritko kruhu je ploche - svetlo ~1.5 m nad zemi)
        circleLight = lightObject.AddComponent<Light>();
        circleLight.type = LightType.Point;
        circleLight.color = HolyColor;
        circleLight.shadows = LightShadows.None;

        halo = Fx.CreateAura(transform, HolyColor);
        halo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        halo.Play();
        var glow = new GameObject("FX_HolyGlow");
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = new Vector3(0f, 2.2f, 0f);
        haloLight = glow.AddComponent<Light>();
        haloLight.type = LightType.Point;
        haloLight.color = HolyColor;
        haloLight.range = 6f;
        haloLight.shadows = LightShadows.None;
    }

    void HideVisual()
    {
        if (circle != null) Destroy(circle);
        circle = null;
        core = null;
        if (halo != null)
        {
            halo.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(halo.gameObject, 1f);
        }
        halo = null;
        if (haloLight != null) Destroy(haloLight.gameObject);
        haloLight = null;
    }
}

// Kruh dopadu Sindelovy ultimatky pro killcam a POTG (ze zaznamu mireni).
public static class EzekielCircleVisual
{
    public static GameObject Create(Transform parent)
    {
        var circle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        circle.name = "HolyCircle";
        Object.DestroyImmediate(circle.GetComponent<Collider>());
        circle.transform.SetParent(parent, false);
        Fx.Paint(circle, EzekielAbility.HolyColor);
        AddDashedOutline(circle.transform);
        var lightObject = new GameObject("Light");
        lightObject.transform.SetParent(circle.transform, false);
        lightObject.transform.localPosition = Vector3.up * 80f;
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = EzekielAbility.HolyColor;
        light.shadows = LightShadows.None;
        light.intensity = 4f;
        return circle;
    }

    static Mesh dashedMesh;
    static Material overlayMaterial;

    // Cerchovany prstenec (v meritku kruhu - polomer 0,5) kresleny pres zdi a strechy.
    public static void AddDashedOutline(Transform circle)
    {
        if (dashedMesh == null) dashedMesh = BuildDashedRing(0.5f, 0.035f, 28, 0.6f);
        if (overlayMaterial == null)
        {
            var shader = Shader.Find("BrosOverwatch/Overlay Color");
            overlayMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            var color = new Color(1f, 0.85f, 0.4f, 0.9f);
            if (overlayMaterial.HasProperty("_BaseColor")) overlayMaterial.SetColor("_BaseColor", color);
            overlayMaterial.color = color;
            overlayMaterial.renderQueue = 4000;
        }
        var ring = new GameObject("DashedOutline");
        ring.transform.SetParent(circle, false);
        ring.transform.localPosition = Vector3.up * 1.2f;
        ring.AddComponent<MeshFilter>().sharedMesh = dashedMesh;
        var renderer = ring.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = overlayMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // 'dashes' oblouku po obvodu, kazdy zabira 'fill' sveho dilku; plochy v rovine XZ.
    static Mesh BuildDashedRing(float radius, float width, int dashes, float fill)
    {
        const int steps = 4;
        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();
        float inner = radius - width * 0.5f, outer = radius + width * 0.5f;
        for (int d = 0; d < dashes; d++)
        {
            float a0 = d / (float)dashes * Mathf.PI * 2f;
            float a1 = a0 + fill / dashes * Mathf.PI * 2f;
            int start = vertices.Count;
            for (int s = 0; s <= steps; s++)
            {
                float a = Mathf.Lerp(a0, a1, s / (float)steps);
                vertices.Add(new Vector3(Mathf.Cos(a) * inner, 0f, Mathf.Sin(a) * inner));
                vertices.Add(new Vector3(Mathf.Cos(a) * outer, 0f, Mathf.Sin(a) * outer));
            }
            for (int s = 0; s < steps; s++)
            {
                int i = start + s * 2;
                triangles.AddRange(new[] { i, i + 1, i + 2, i + 1, i + 3, i + 2 });
            }
        }
        var mesh = new Mesh { name = "DashedRing" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));
        return mesh;
    }

    public static void Place(GameObject circle, Vector3 point, float radius, float progress)
    {
        circle.transform.position = point + Vector3.up * 0.04f;
        circle.transform.rotation = Quaternion.identity;
        Vector3 parentScale = circle.transform.parent != null ? circle.transform.parent.lossyScale : Vector3.one;
        circle.transform.localScale = new Vector3(radius * 2f / Mathf.Max(0.01f, parentScale.x), 0.02f / Mathf.Max(0.01f, parentScale.y), radius * 2f / Mathf.Max(0.01f, parentScale.z));
        var color = Color.Lerp(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.6f, 0.15f), progress);
        var renderer = circle.GetComponent<Renderer>();
        if (renderer.material.HasProperty("_BaseColor")) renderer.material.SetColor("_BaseColor", color);
        else renderer.material.color = color;
    }
}

// Sloup svetla z nebe pri uderu: zablesk, rozsiri se a vybledne.
public class HolyBeam : MonoBehaviour
{
    float age;
    float radius;
    Renderer beamRenderer;
    Light flash;
    const float Lifetime = 0.9f;

    public static void Spawn(Vector3 point, float radius)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "HolyBeam";
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = point + Vector3.up * 40f;
        var beam = go.AddComponent<HolyBeam>();
        beam.radius = radius;
        beam.beamRenderer = go.GetComponent<Renderer>();
        Fx.Paint(go, new Color(1f, 0.95f, 0.75f));
        var material = beam.beamRenderer.material;
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.5f) * 6f);

        var lightObject = new GameObject("Flash");
        lightObject.transform.position = point + Vector3.up * 3f;
        lightObject.transform.SetParent(go.transform, true);
        beam.flash = lightObject.AddComponent<Light>();
        beam.flash.type = LightType.Point;
        beam.flash.color = EzekielAbility.HolyColor;
        beam.flash.range = radius * 4f;
        beam.flash.intensity = 25f;
        beam.flash.shadows = LightShadows.None;
        beam.Update();
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / Lifetime);
        // rychle naskoci do plne sirky, pak se zuzi a zmizi
        float width = radius * 2f * (t < 0.15f ? Mathf.Lerp(0.3f, 1f, t / 0.15f) : Mathf.Lerp(1f, 0.05f, (t - 0.15f) / 0.85f));
        transform.localScale = new Vector3(width, 40f, width);
        if (flash != null) flash.intensity = 25f * (1f - t);
        if (t >= 1f) Destroy(gameObject);
    }
}
