using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Robot rezimu Tlacenice: obri Honza (model hrdiny Honza, 'giantScale' x) a pred nim Toyota Yaris (cargo).
// Poloha se pocita z MatchManageru (pushCar, pushSide, obchazeni) - u vsech stejne, mezi aktualizacemi serveru
// se Yaris posouva podle rychlosti tlaceni. Mimo zapas stoji robot uprostred trasy.
//  - animace: chuze pri tlaceni / obchazeni (pomalu, obri kroky), jinak stani; pri tlaceni ma ruce pred sebou
//  - zvuky: dupani obra, skripani tlaceneho Yarisu, houkacka pri checkpointu, obcas hlaska Honzy
//  - obr i Yaris maji kolize (kryt). Kdo by se ocitl v ceste, toho to pomalu odsune do strany (mistni hrac
//    u sebe, boti na serveru) - CharacterController.Move, takze ne skrz zed. Nezranitelny (nema Health).
// Postavi ho PushSetup (BrosOverwatch > Tlacenice); model obra se vklada az pri spusteni.
public class PushRobot : MonoBehaviour
{
    public PushRoute route;
    public Transform cargo;     // pivot Yarisu (na zemi uprostred auta), +z = k zakladne tymu 1
    public Transform giant;     // pivot obra (na zemi), CapsuleCollider
    public float giantScale = 3f;
    public AudioClip horn;
    public AudioClip[] voice;

    public static PushRobot Instance { get; private set; }
    // Co prave ukazuje (pro HUD): s Yarisu a poloha obra.
    public static float DisplayCar { get; private set; } = -1f;
    public static Vector3 DisplayGiant { get; private set; }

    const float WalkReference = 1.4f;   // jak rychle jde postava pri animaci chuze rychlosti 1 (m/s, bez zvetseni)

    Transform model;
    Animator animator;
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    AnimationClipPlayable idlePlayable, walkPlayable;
    float walkWeight, pushPose, lastPhase;
    Transform leftUpper, leftLower, leftHand, rightUpper, rightLower, rightHand;

    float displayS = -1f, lastCarValue = float.NaN, carReceived;
    BoxCollider cargoBox;
    CapsuleCollider giantCapsule;
    AudioSource creak;
    float nextVoice;

    void Awake()
    {
        Instance = this;
        if (cargo != null) cargoBox = cargo.GetComponentInChildren<BoxCollider>();
        if (giant != null) giantCapsule = giant.GetComponent<CapsuleCollider>();
        BuildGiant();
        if (cargo != null)
        {
            creak = cargo.gameObject.AddComponent<AudioSource>();
            creak.clip = CreakClip;
            creak.loop = true;
            creak.spatialBlend = 1f;
            creak.rolloffMode = AudioRolloffMode.Linear;
            creak.minDistance = 3f;
            creak.maxDistance = 40f;
            creak.dopplerLevel = 0f;
            creak.volume = 0f;
            creak.Play();
        }
        nextVoice = Time.time + Random.Range(20f, 35f);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (graph.IsValid()) graph.Destroy();
    }

    void OnDisable()
    {
        DisplayCar = -1f;
        if (creak != null) creak.volume = 0f;
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
        if (route == null || cargo == null || giant == null) return;
        float dt = Time.deltaTime;

        var match = MatchManager.Instance;
        bool live = match != null && match.IsSpawned && match.IsPushMode && !match.IsLobby;
        float center = route.Center;
        float target = center, rate = 0f, crossT = 1f;
        int side = 0, crossFrom = 0;
        if (live)
        {
            float raw = match.pushCar.Value < 0f ? center : match.pushCar.Value;
            if (raw != lastCarValue)
            {
                lastCarValue = raw;
                carReceived = Time.time;
            }
            rate = match.pushRate.Value;
            target = Mathf.Clamp(raw + rate * Mathf.Min(Time.time - carReceived, 0.35f), 0f, route.Length);
            side = match.pushSide.Value;
            crossFrom = match.pushCrossFrom.Value;
            var network = match.NetworkManager;
            crossT = network != null ? match.PushCrossT(route, network.ServerTime.Time) : 1f;
        }

        if (displayS < 0f || Mathf.Abs(target - displayS) > 4f) displayS = target;
        else displayS = Mathf.MoveTowards(displayS, target, (Mathf.Abs(rate) + 0.4f) * 1.6f * dt);
        DisplayCar = displayS;

        // Yaris: natoceny podle trasy mezi prednimi a zadnimi koly
        Vector3 back = route.Point(displayS - 2.5f), front = route.Point(displayS + 2.5f);
        Vector3 along = front - back;
        along.y = 0f;
        cargo.SetPositionAndRotation(route.Point(displayS),
            Quaternion.LookRotation(along.sqrMagnitude > 1e-4f ? along.normalized : route.Forward(displayS), Vector3.up));

        // obr
        MatchManager.PushRobotPose(route, displayS, side, crossFrom, crossT, out var position, out var facing, out bool crossing);
        Vector3 previous = giant.position;
        giant.position = position;
        if (facing.sqrMagnitude > 1e-4f)
            giant.rotation = Quaternion.RotateTowards(giant.rotation, Quaternion.LookRotation(facing, Vector3.up), 120f * dt);
        DisplayGiant = position;

        bool pushing = !crossing && Mathf.Abs(rate) > 0.01f;
        float speed = crossing ? MatchManager.PushWalkSpeed : Mathf.Abs(rate);
        if (dt > 0f && !crossing && !pushing) speed = Mathf.Min(speed, Vector3.Distance(previous, position) / dt);
        Animate(dt, crossing || pushing, speed);
        pushPose = Mathf.MoveTowards(pushPose, pushing ? 1f : 0f, dt * 2.5f);

        Sounds(dt, pushing, rate, live);
        NudgePlayers();
    }

    void Animate(float dt, bool walking, float speed)
    {
        if (!graph.IsValid()) return;
        walkWeight = Mathf.MoveTowards(walkWeight, walking ? 1f : 0f, dt * 3f);
        mixer.SetInputWeight(0, 1f - walkWeight);
        mixer.SetInputWeight(1, walkWeight);
        float clipSpeed = Mathf.Clamp(speed / (WalkReference * giantScale), 0.2f, 1.3f);
        walkPlayable.SetSpeed(walking ? clipSpeed : 0.3f);

        // dupani: dva kroky na cyklus chuze
        var clip = walkPlayable.GetAnimationClip();
        if (clip == null || clip.length <= 0f) return;
        float phase = (float)(walkPlayable.GetTime() / clip.length % 1.0);
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

    void Sounds(float dt, bool pushing, float rate, bool live)
    {
        if (creak != null)
        {
            creak.volume = Mathf.MoveTowards(creak.volume, pushing ? 0.55f : 0f, dt * 1.5f);
            creak.pitch = 0.8f + Mathf.Abs(rate) * 0.25f;
        }
        if (live && (pushing || Mathf.Abs(rate) > 0f) && Time.time >= nextVoice)
        {
            nextVoice = Time.time + Random.Range(22f, 40f);
            Say();
        }
    }

    public void Horn()
    {
        if (horn != null && cargo != null)
            ProceduralSfx.PlayTracked(horn, cargo.position + Vector3.up, 1f, 90f);
    }

    // Hlaska obriho Honzy (hlubsi hlas).
    public void Say()
    {
        if (voice == null || voice.Length == 0 || giant == null) return;
        var clip = voice[Random.Range(0, voice.Length)];
        if (clip != null)
            ProceduralSfx.PlayTracked(clip, giant.position + Vector3.up * 5f, 1f, 70f, true, 0.78f);
        nextVoice = Time.time + Random.Range(22f, 40f);
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
            float height = giantCapsule.height;
            if (d.y > -1f && d.y < height)
            {
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < r)
                {
                    Vector3 away = dist > 0.05f ? d / dist : giant.right;
                    Push(controller, away * (r - dist + 0.03f), giant.right);
                }
            }
        }

        // Yaris: kvadr (kdo stoji na strese, toho nechat)
        if (cargoBox != null)
        {
            Transform t = cargoBox.transform;
            Vector3 scale = t.lossyScale;
            Vector3 half = Vector3.Scale(cargoBox.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * 0.5f;
            Vector3 centerWorld = t.TransformPoint(cargoBox.center);
            Quaternion rotation = t.rotation;
            Vector3 local = Quaternion.Inverse(rotation) * (feet - centerWorld);
            if (local.y > -half.y - 0.5f && local.y < half.y - 0.25f)
            {
                float px = half.x + radius - Mathf.Abs(local.x);
                float pz = half.z + radius - Mathf.Abs(local.z);
                if (px > 0f && pz > 0f)
                {
                    // ven nejkratsi cestou (vetsinou do strany)
                    Vector3 push = px <= pz + 0.6f
                        ? rotation * new Vector3(Mathf.Sign(local.x == 0f ? 1f : local.x) * (px + 0.03f), 0f, 0f)
                        : rotation * new Vector3(0f, 0f, Mathf.Sign(local.z == 0f ? 1f : local.z) * (pz + 0.03f));
                    Push(controller, push, rotation * Vector3.right);
                }
            }
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
        DisplayCar = -1f;
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
