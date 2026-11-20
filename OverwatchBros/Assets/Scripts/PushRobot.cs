using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Robot rezimu Tlacenice: obri Honza (model hrdiny Honza, 'giantScale' x) a dva Yarisy - kazdy tym ma svuj
// (cargo = tym 0 / modri, cargo1 = tym 1 / cerveni; auta jsou dobarvena barvou tymu).
// Polohy se pocitaji z MatchManageru (pushRobot, pushCar0/1) - u vsech stejne, mezi aktualizacemi serveru se Honza
// posouva podle rychlosti (pushRobotRate) a tlaceny Yaris jede s nim. Mimo zapas stoji Honza uprostred mezi auty.
//  - animace: chuze pri tlaceni (pomalu, obri kroky) a pri behu k druhemu Yarisu, jinak stani; pri tlaceni ma ruce
//    pred sebou
//  - zvuky: dupani obra, skripani tlaceneho Yarisu, houkacka pri checkpointu, obcas hlaska Honzy
//  - obr i Yarisy maji kolize (kryt). Kdo by se ocitl v ceste, toho to pomalu odsune do strany (mistni hrac
//    u sebe, boti na serveru) - CharacterController.Move, takze ne skrz zed. Nezranitelny (nema Health).
// Postavi ho PushSetup (BrosOverwatch > Tlacenice); model obra se vklada az pri spusteni.
public class PushRobot : MonoBehaviour
{
    public PushRoute route;
    public Transform cargo;     // Yaris tymu 0 - pivot na zemi uprostred auta, +z = k zakladne tymu 1
    public Transform cargo1;    // Yaris tymu 1
    public Transform giant;     // pivot obra (na zemi), CapsuleCollider
    public float giantScale = 3f;
    public AudioClip horn;
    public AudioClip[] voice;

    public static PushRobot Instance { get; private set; }
    // Co prave ukazuje (pro HUD): s Yarisu obou tymu a Honzy.
    public static readonly float[] DisplayCars = { -1f, -1f };
    public static float DisplayRobot { get; private set; } = -1f;

    const float WalkReference = 1.4f;   // jak rychle jde postava pri animaci chuze rychlosti 1 (m/s, bez zvetseni)

    Transform model;
    Animator animator;
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    AnimationClipPlayable idlePlayable, walkPlayable;
    float walkWeight, pushPose, lastPhase, walkTime, idleTime;
    Transform leftUpper, leftLower, leftHand, rightUpper, rightLower, rightHand;

    Transform[] cars;
    BoxCollider[] carBoxes;
    readonly float[] carYaw = new float[2];   // jak je auto natocene vuci trase (z editoru - rucni otoceni plati)
    const float TeamTint = 0.85f;            // jak silne je auto dobarvene barvou tymu
    AudioSource[] creaks;
    CapsuleCollider giantCapsule;
    float displayRobot = -1f, lastRobotValue = float.NaN, robotReceived;
    readonly float[] displayCar = { -1f, -1f };
    Vector3 facing;
    float nextVoice;

    void Awake()
    {
        Instance = this;
        cars = new[] { cargo, cargo1 };
        if (route == null) route = PushRoute.Active;
        for (int team = 0; team < 2; team++)
        {
            if (cars[team] == null || route == null) continue;
            Vector3 forward = cars[team].forward;
            forward.y = 0f;
            carYaw[team] = forward.sqrMagnitude > 1e-4f
                ? Vector3.SignedAngle(route.Forward(MatchManager.PushCarStart(route, team)), forward, Vector3.up) : 0f;
        }
        carBoxes = new BoxCollider[2];
        creaks = new AudioSource[2];
        for (int team = 0; team < 2; team++)
        {
            if (cars[team] == null) continue;
            carBoxes[team] = cars[team].GetComponentInChildren<BoxCollider>();
            Tint(cars[team], UiKit.TeamColor(team));
            var source = cars[team].gameObject.AddComponent<AudioSource>();
            source.clip = CreakClip;
            source.loop = true;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 3f;
            source.maxDistance = 40f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            source.Play();
            creaks[team] = source;
        }
        if (giant != null) giantCapsule = giant.GetComponent<CapsuleCollider>();
        BuildGiant();
        BuildZone();
        nextVoice = Time.time + Random.Range(20f, 35f);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (graph.IsValid()) graph.Destroy();
    }

    void OnDisable()
    {
        DisplayCars[0] = DisplayCars[1] = -1f;
        DisplayRobot = -1f;
        if (creaks != null)
            foreach (var source in creaks)
                if (source != null) source.volume = 0f;
    }

    // Auto v barve tymu (zachova texturu, jen ji dobarvi).
    void Tint(Transform car, Color team)
    {
        var block = new MaterialPropertyBlock();
        foreach (var renderer in car.GetComponentsInChildren<Renderer>(true))
        {
            var material = renderer.sharedMaterial;
            if (material == null) continue;
            renderer.GetPropertyBlock(block);
            foreach (var property in new[] { "_BaseColor", "_Color", "baseColorFactor" })
                if (material.HasProperty(property))
                    block.SetColor(property, Color.Lerp(material.GetColor(property), material.GetColor(property) * team, TeamTint));
            renderer.SetPropertyBlock(block);
        }
    }

    // ---------------- obr ----------------

    void BuildGiant()
    {
        if (giant == null) return;
        var hero = Resources.Load<HeroDefinition>("Heroes/Honza");
        var prefab = hero != null ? hero.characterPrefab : null;
        if (prefab == null)
        {
            Debug.LogWarning("[Tlacenice] Model Honzy (Resources/Heroes/Honza) nenalezen - obr bude bez modelu.");
            return;
        }

        model = Instantiate(prefab, giant, false).transform;
        model.name = "ObriHonza";
        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.identity;
        model.localScale = Vector3.one * giantScale;
        foreach (var collider in model.GetComponentsInChildren<Collider>(true))
            Destroy(collider);
        foreach (var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            skinned.updateWhenOffscreen = false;
            skinned.localBounds = new Bounds(Vector3.zero, Vector3.one * 5f);
        }

        animator = model.GetComponent<Animator>();
        if (animator == null) return;
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var common = Resources.Load<CharacterAnimSet>("Characters/CharacterAnimations");
        var own = hero.animations;
        AnimationClip idle = own != null && own.idle != null ? own.idle : common != null ? common.idle : null;
        AnimationClip walk = own != null && own.walk != null ? own.walk : common != null ? common.walk : null;
        if (idle == null) return;

        graph = PlayableGraph.Create("ObriHonza");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
        mixer = AnimationMixerPlayable.Create(graph, 2);
        idlePlayable = AnimationClipPlayable.Create(graph, idle);
        walkPlayable = AnimationClipPlayable.Create(graph, walk != null ? walk : idle);
        idlePlayable.SetSpeed(0);   // cas se posouva rucne (Animate)
        walkPlayable.SetSpeed(0);
        graph.Connect(idlePlayable, 0, mixer, 0);
        graph.Connect(walkPlayable, 0, mixer, 1);
        mixer.SetInputWeight(0, 1f);
        mixer.SetInputWeight(1, 0f);
        output.SetSourcePlayable(mixer);
        graph.Play();

        if (animator.isHuman)
        {
            leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
    }

    // ---------------- poloha ----------------

    void Update()
    {
        if (route == null) route = PushRoute.Active;
        if (route == null || giant == null) return;
        float dt = Time.deltaTime;

        var match = MatchManager.Instance;
        bool live = match != null && match.IsSpawned && match.IsPushMode && !match.IsLobby;
        float robotTarget = route.Center, rate = 0f;
        int owner = -1, state = MatchManager.PushIdle;
        var carTarget = new[] { MatchManager.PushCarStart(route, 0), MatchManager.PushCarStart(route, 1) };
        if (live)
        {
            float raw = match.PushRobotOf(route);
            if (raw != lastRobotValue)
            {
                lastRobotValue = raw;
                robotReceived = Time.time;
            }
            rate = match.pushRobotRate.Value;
            robotTarget = Mathf.Clamp(raw + rate * Mathf.Min(Time.time - robotReceived, 0.35f), 0f, route.Length);
            owner = match.PushOwner;
            state = match.pushState.Value;
            for (int team = 0; team < 2; team++) carTarget[team] = match.PushCarOf(route, team);
        }

        // Honza (dopocitany mezi aktualizacemi serveru)
        if (displayRobot < 0f || Mathf.Abs(robotTarget - displayRobot) > 6f) displayRobot = robotTarget;
        else displayRobot = Mathf.MoveTowards(displayRobot, robotTarget, (Mathf.Abs(rate) + 0.4f) * 1.6f * dt);
        DisplayRobot = displayRobot;
        bool pushing = live && state == MatchManager.PushPushing && owner >= 0;

        // Yarisy: tlaceny jede s Honzou, druhy stoji, kde ho nechali
        for (int team = 0; team < 2; team++)
        {
            float target = pushing && owner == team ? displayRobot + MatchManager.PushDirection(team) * route.robotOffset : carTarget[team];
            if (displayCar[team] < 0f || Mathf.Abs(target - displayCar[team]) > 6f) displayCar[team] = target;
            else displayCar[team] = Mathf.MoveTowards(displayCar[team], target, (Mathf.Abs(rate) + 0.4f) * 1.6f * dt);
            DisplayCars[team] = displayCar[team];
            if (cars[team] == null) continue;
            Vector3 back = route.Point(displayCar[team] - 2.5f), front = route.Point(displayCar[team] + 2.5f);
            Vector3 along = front - back;
            along.y = 0f;
            cars[team].SetPositionAndRotation(route.Point(displayCar[team]),
                Quaternion.LookRotation(along.sqrMagnitude > 1e-4f ? along.normalized : route.Forward(displayCar[team]), Vector3.up)
                * Quaternion.Euler(0f, carYaw[team], 0f));
        }

        // obr: pri tlaceni k Yarisu, pri behu ve smeru behu, na startu bokem (mezi auty)
        Vector3 previous = giant.position;
        giant.position = route.Point(displayRobot);
        Vector3 forward = route.Forward(displayRobot);
        if (pushing) facing = forward * MatchManager.PushDirection(owner);
        else if (Mathf.Abs(rate) > 0.01f) facing = forward * Mathf.Sign(rate);
        else if (owner < 0) facing = route.Right(displayRobot);
        else if (facing == Vector3.zero) facing = forward;
        if (facing.sqrMagnitude > 1e-4f)
            giant.rotation = Quaternion.RotateTowards(giant.rotation, Quaternion.LookRotation(facing, Vector3.up), 150f * dt);

        bool moving = Mathf.Abs(rate) > 0.01f;
        float speed = Mathf.Abs(rate);
        if (!moving && dt > 0f) speed = Mathf.Min(MatchManager.PushWalkSpeed, Vector3.Distance(previous, giant.position) / dt);
        Animate(dt, moving || speed > 0.2f, speed);
        pushPose = Mathf.MoveTowards(pushPose, pushing ? 1f : 0f, dt * 2.5f);

        Sounds(dt, pushing, owner, rate, live);
        NudgePlayers();
        UpdateZone(live, owner, state);
    }

    // ---------------- zona tlaceni (kruh na zemi) ----------------

    // Kde se pocita "u Honzy" (MatchManager.PushRadius kolem Honzy a Yarisu, ktery prave tlaci): obrys na zemi
    // v barve tymu, ktery ho ovlada (sporny oranzove, nikdo bile).
    LineRenderer zone;
    const int ZonePoints = 72;

    void BuildZone()
    {
        var go = new GameObject("ZonaTlaceni");
        go.transform.SetParent(transform, false);
        zone = go.AddComponent<LineRenderer>();
        zone.useWorldSpace = true;
        zone.loop = true;
        zone.positionCount = ZonePoints;
        zone.widthMultiplier = 0.18f;
        zone.numCornerVertices = 2;
        zone.alignment = LineAlignment.TransformZ;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // pasek lezi na zemi
        zone.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        zone.receiveShadows = false;
        var shader = Shader.Find("Sprites/Default");
        if (shader != null) zone.sharedMaterial = new Material(shader);
    }

    void UpdateZone(bool live, int owner, int state)
    {
        if (zone == null || route == null) return;
        // tlaceny Yaris patri do zony, kdyz u nej Honza stoji
        Vector3 a = giant.position, b = a;
        if (owner >= 0 && cars[owner] != null)
        {
            float behind = displayCar[owner] - MatchManager.PushDirection(owner) * route.robotOffset;
            if (Mathf.Abs(displayRobot - behind) < 0.3f) b = cars[owner].position;
        }
        Vector3 axis = b - a;
        axis.y = 0f;
        Vector3 dir = axis.sqrMagnitude > 1e-4f ? axis.normalized : giant.forward;
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        float r = MatchManager.PushRadius;
        int half = ZonePoints / 2;
        for (int i = 0; i < ZonePoints; i++)
        {
            // pulkruh kolem Yarisu (b), pak pulkruh kolem Honzy (a)
            bool atCar = i < half;
            float t = (atCar ? i : i - half) / (float)(half - 1) * Mathf.PI;
            Vector3 center = atCar ? b : a;
            Vector3 normal = atCar ? side : -side;
            Vector3 point = center + (normal * Mathf.Cos(t) + (atCar ? dir : -dir) * Mathf.Sin(t)) * r;
            point.y = Mathf.Lerp(a.y, b.y, 0.5f) + 0.07f;
            zone.SetPosition(i, point);
        }
        Color color = !live ? new Color(1f, 1f, 1f, 0.35f)
            : state == MatchManager.PushContested ? new Color(1f, 0.62f, 0.12f, 0.85f)
            : owner >= 0 ? new Color(UiKit.TeamColor(owner).r, UiKit.TeamColor(owner).g, UiKit.TeamColor(owner).b, 0.8f)
            : new Color(1f, 1f, 1f, 0.6f);
        zone.startColor = zone.endColor = color;
    }

    void Animate(float dt, bool walking, float speed)
    {
        if (!graph.IsValid()) return;
        walkWeight = Mathf.MoveTowards(walkWeight, walking ? 1f : 0f, dt * 3f);
        mixer.SetInputWeight(0, 1f - walkWeight);
        mixer.SetInputWeight(1, walkWeight);
        // Skutecny krok obra (3x delsi) by pri tlaceni dal jeden krok za ~2,5 s - vypada to jako zamrzla poza.
        // Krokuje proto rychleji (nohy trochu kloužou, ale je videt chuze), pri behu k Yarisu rychleji.
        float clipSpeed = Mathf.Clamp(0.55f + speed / (WalkReference * giantScale) * 1.4f, 0.55f, 1.4f);
        // Cas animaci se posouva rucne a dokola (klipy nemusi mit zapnute opakovani - pak by po prvnim kroku zamrzly).
        var clip = walkPlayable.GetAnimationClip();
        var idleClip = idlePlayable.GetAnimationClip();
        if (idleClip != null && idleClip.length > 0f)
        {
            idleTime = (idleTime + dt) % idleClip.length;
            idlePlayable.SetTime(idleTime);
        }
        if (clip == null || clip.length <= 0f) return;
        walkTime = (walkTime + dt * (walking ? clipSpeed : 0.3f)) % clip.length;
        walkPlayable.SetTime(walkTime);

        // dupani: dva kroky na cyklus chuze
        float phase = walkTime / clip.length;
        if (walkWeight > 0.5f && ((lastPhase < 0.5f && phase >= 0.5f) || phase < lastPhase))
            ProceduralSfx.PlayTracked(StompClip, giant.position, 0.9f, 55f, true, Random.Range(0.85f, 1.05f));
        lastPhase = phase;
    }

    // Pri tlaceni ma obr ruce natazene pred sebe (opreny o Yaris) - pres animaci chuze.
    void LateUpdate()
    {
        if (pushPose <= 0.001f || leftUpper == null || rightUpper == null || giant == null) return;
        Vector3 forward = giant.forward;
        AimArm(leftUpper, leftLower, leftHand, (forward * 0.92f - giant.up * 0.28f - giant.right * 0.12f).normalized);
        AimArm(rightUpper, rightLower, rightHand, (forward * 0.92f - giant.up * 0.28f + giant.right * 0.12f).normalized);
    }

    void AimArm(Transform upper, Transform lower, Transform hand, Vector3 direction)
    {
        if (upper == null || lower == null || hand == null) return;
        Vector3 current = lower.position - upper.position;
        upper.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(current, direction), pushPose) * upper.rotation;
        current = hand.position - lower.position;
        lower.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(current, direction), pushPose) * lower.rotation;
    }

    // ---------------- zvuky ----------------

    void Sounds(float dt, bool pushing, int owner, float rate, bool live)
    {
        for (int team = 0; team < 2; team++)
        {
            var source = creaks[team];
            if (source == null) continue;
            source.volume = Mathf.MoveTowards(source.volume, pushing && owner == team ? 0.55f : 0f, dt * 1.5f);
            source.pitch = 0.8f + Mathf.Abs(rate) * 0.25f;
        }
        if (live && Mathf.Abs(rate) > 0f && Time.time >= nextVoice)
            Say();
    }

    public void Horn(int team)
    {
        var car = team == 1 ? cargo1 : cargo;
        if (horn != null && car != null)
            ProceduralSfx.PlayTracked(horn, car.position + Vector3.up, 1f, 90f);
    }

    // Hlaska obriho Honzy (hlubsi hlas).
    public void Say()
    {
        nextVoice = Time.time + Random.Range(22f, 40f);
        if (voice == null || voice.Length == 0 || giant == null) return;
        var clip = voice[Random.Range(0, voice.Length)];
        if (clip != null)
            ProceduralSfx.PlayTracked(clip, giant.position + Vector3.up * 5f, 1f, 70f, true, 0.78f);
    }

    // ---------------- dotazy pro boty ----------------

    // Je bod (vodorovne) v obrysu Honzy nebo nektereho Yarisu, rozsirenem o 'margin'?
    public bool Occupies(Vector3 point, float margin)
    {
        if (giant != null && giantCapsule != null)
        {
            Vector3 d = point - giant.position;
            d.y = 0f;
            if (d.magnitude < giantCapsule.radius + margin) return true;
        }
        if (carBoxes == null) return false;
        foreach (var box in carBoxes)
        {
            if (box == null) continue;
            Transform t = box.transform;
            Vector3 scale = t.lossyScale;
            Vector3 half = Vector3.Scale(box.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * 0.5f;
            Vector3 local = Quaternion.Inverse(t.rotation) * (point - t.TransformPoint(box.center));
            if (Mathf.Abs(local.x) < half.x + margin && Mathf.Abs(local.z) < half.z + margin) return true;
        }
        return false;
    }

    // Nejblizsi kryt (Honza nebo Yaris) k bodu.
    public Vector3 NearestCover(Vector3 point)
    {
        Vector3 best = giant != null ? giant.position : transform.position;
        float bestDistance = Vector3.Distance(point, best);
        if (cars != null)
            foreach (var car in cars)
                if (car != null && Vector3.Distance(point, car.position) < bestDistance)
                {
                    bestDistance = Vector3.Distance(point, car.position);
                    best = car.position;
                }
        return best;
    }

    // ---------------- hraci v ceste ----------------

    // Mistni hrac (a na serveru boti): kdyz je v obrysu obra nebo Yarisu, posune se ven do strany.
    void NudgePlayers()
    {
        var network = Unity.Netcode.NetworkManager.Singleton;
        if (network == null || !network.IsListening) return;
        var local = network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        if (local != null) Nudge(local.GetComponent<CharacterController>());
        if (network.IsServer)
            foreach (var bot in BotBrain.All)
                if (bot != null) Nudge(bot.GetComponent<CharacterController>());
    }

    void Nudge(CharacterController controller)
    {
        if (controller == null || !controller.enabled) return;
        Vector3 feet = controller.transform.position;
        float radius = controller.radius;

        // obr: svisly valec
        if (giantCapsule != null)
        {
            float r = giantCapsule.radius + radius;
            Vector3 d = feet - giant.position;
            if (d.y > -1f && d.y < giantCapsule.height)
            {
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < r)
                {
                    Vector3 away = dist > 0.05f ? d / dist : giant.right;
                    Push(controller, away * (r - dist + 0.03f), giant.right);
                    feet = controller.transform.position;
                }
            }
        }

        // Yarisy: kvadr (kdo stoji na strese, toho nechat)
        foreach (var box in carBoxes)
        {
            if (box == null) continue;
            Transform t = box.transform;
            Vector3 scale = t.lossyScale;
            Vector3 half = Vector3.Scale(box.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * 0.5f;
            Vector3 center = t.TransformPoint(box.center);
            Quaternion rotation = t.rotation;
            Vector3 local = Quaternion.Inverse(rotation) * (feet - center);
            if (local.y <= -half.y - 0.5f || local.y >= half.y - 0.25f) continue;
            float px = half.x + radius - Mathf.Abs(local.x);
            float pz = half.z + radius - Mathf.Abs(local.z);
            if (px <= 0f || pz <= 0f) continue;
            // ven nejkratsi cestou (vetsinou do strany)
            Vector3 push = px <= pz + 0.6f
                ? rotation * new Vector3(Mathf.Sign(local.x == 0f ? 1f : local.x) * (px + 0.03f), 0f, 0f)
                : rotation * new Vector3(0f, 0f, Mathf.Sign(local.z == 0f ? 1f : local.z) * (pz + 0.03f));
            Push(controller, push, rotation * Vector3.right);
            feet = controller.transform.position;
        }
    }

    // Posun pres CharacterController (zdi ho zastavi). Kdyz to tam nejde (zed za hracem), zkusit druhou stranu.
    static void Push(CharacterController controller, Vector3 delta, Vector3 sideways)
    {
        Vector3 before = controller.transform.position;
        float wanted = delta.magnitude;
        controller.Move(delta);
        Vector3 moved = controller.transform.position - before;
        moved.y = 0f;
        if (moved.magnitude >= wanted * 0.5f) return;
        Vector3 other = sideways.normalized * (Vector3.Dot(delta, sideways) >= 0f ? -1f : 1f);
        controller.Move(other * Mathf.Max(wanted, 0.15f));
    }

    // ---------------- generovane zvuky ----------------

    static AudioClip stomp, creakClip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        stomp = creakClip = null;
        DisplayCars[0] = DisplayCars[1] = -1f;
        DisplayRobot = -1f;
        Instance = null;
    }

    // Hluboke zaduneni kroku obra.
    static AudioClip StompClip => stomp ??= Generate("obr_krok", 0.7f, (t, r) =>
    {
        float body = Mathf.Sin(2f * Mathf.PI * (38f + 30f * Mathf.Exp(-t * 18f)) * t) * Mathf.Exp(-t * 6f);
        float thud = ((float)r.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 28f) * 0.45f;
        return (body * 0.9f + thud) * 0.9f;
    });

    // Skripani a vrzani tlaceneho auta (smycka 2 s).
    static AudioClip CreakClip => creakClip ??= Generate("yaris_skripani", 2f, (t, r) =>
    {
        float wobble = 1f + 0.35f * Mathf.Sin(2f * Mathf.PI * 0.5f * t) + 0.15f * Mathf.Sin(2f * Mathf.PI * 1.7f * t);
        float squeal = Mathf.Sin(2f * Mathf.PI * 310f * wobble * t) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 1f * t)) * 0.25f;
        float grind = ((float)r.NextDouble() * 2f - 1f) * 0.18f;
        float rumble = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.3f;
        float loop = Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((2f - t) / 0.05f);
        return (squeal + grind + rumble) * loop;
    });

    static AudioClip Generate(string name, float seconds, System.Func<float, System.Random, float> sample)
    {
        const int Rate = 44100;
        int count = Mathf.CeilToInt(seconds * Rate);
        var data = new float[count];
        var random = new System.Random(name.GetHashCode());
        for (int i = 0; i < count; i++)
            data[i] = Mathf.Clamp(sample(i / (float)Rate, random), -1f, 1f);
        var clip = AudioClip.Create(name, count, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
