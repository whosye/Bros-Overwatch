using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// 3D model postavy hrdiny (HeroDefinition.characterPrefab) a jeho animace (chuze, beh, skok, smrt, utok).
// Ostatni hraci ho vidi na tele hrace; majitel jen kdyz je kamera ve 3. osobe (Ohnivy dopad). Animace bezi pres Playables
// (bez Animator Controlleru) a vychazi z rychlosti pohybu, takze funguje stejne pro majitele i pro ostatni po siti.
public class CharacterVisual : MonoBehaviour
{
    const int Idle = 0, Walk = 1, Jog = 2, Sprint = 3, Jump = 4, Death = 5, StateCount = 6;

    public int Version { get; private set; }
    public Transform ModelRoot { get; private set; }
    public bool HasModel => ModelRoot != null;

    FirstPersonController fpc;
    Health health;
    CharacterController controller;
    Renderer bodyCapsule;

    GameObject currentPrefab;
    Renderer[] renderers = new Renderer[0];
    Animator animator;
    CharacterAnimSet clips;

    PlayableGraph graph;
    AnimationMixerPlayable locomotion;
    AnimationLayerMixerPlayable layers;
    readonly AnimationClipPlayable[] states = new AnimationClipPlayable[StateCount];
    readonly float[] stateWeights = new float[StateCount];
    readonly float[] stateTimes = new float[StateCount];
    AnimationClipPlayable meleePlayable, shootPlayable;
    AvatarMask upperBody;

    readonly float[] target = new float[StateCount];
    readonly float[] rate = new float[StateCount];

    RushAbility rush;
    BlockAbility block;
    AnimationClipPlayable blockPlayable;
    float blockWeight;
    AnimationClipPlayable dashPlayable;
    float dashWeight;
    AnimationClipPlayable reloadPlayable;
    float reloadTime = -1f, reloadDuration = 1f;

    float attackTime = -1f, attackDuration, attackClipLength;
    bool attackMelee;

    Vector3 lastPosition;
    float smoothedSpeed;
    bool wasDead;
    float feetLocalY = -1f;

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        health = GetComponent<Health>();
        controller = GetComponent<CharacterController>();
        bodyCapsule = GetComponent<Renderer>();
        rush = GetComponent<RushAbility>();
        block = GetComponent<BlockAbility>();
        lastPosition = transform.position;
    }

    void OnDestroy()
    {
        DestroyGraph();
    }

    public void SetModel(GameObject prefab, Color tint)
    {
        if (prefab == null)
        {
            ClearModel();
            return;
        }

        if (prefab != currentPrefab || ModelRoot == null)
        {
            ClearModel();

            if (clips == null)
                clips = Resources.Load<CharacterAnimSet>("Characters/CharacterAnimations");

            currentPrefab = prefab;
            if (controller != null)
                feetLocalY = controller.center.y - fpc.standHeight * 0.5f;

            var instance = Instantiate(prefab, transform);
            instance.name = "CharacterModel";
            instance.transform.localPosition = new Vector3(0f, feetLocalY, 0f);
            instance.transform.localRotation = Quaternion.identity;
            ModelRoot = instance.transform;

            foreach (var collider in instance.GetComponentsInChildren<Collider>())
                Destroy(collider);

            renderers = instance.GetComponentsInChildren<Renderer>();
            animator = instance.GetComponent<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            BuildGraph();
            Version++;
        }

        ApplyTint(tint);

        if (bodyCapsule != null)
            bodyCapsule.enabled = false;
    }

    public void ClearModel()
    {
        DestroyGraph();

        if (ModelRoot != null)
            Destroy(ModelRoot.gameObject);

        ModelRoot = null;
        currentPrefab = null;
        renderers = new Renderer[0];
        Version++;

        if (bodyCapsule != null)
            bodyCapsule.enabled = true;
    }

    void ApplyTint(Color tint)
    {
        SkinnedMeshRenderer body = null;
        foreach (var smr in ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (body == null || smr.sharedMesh.bounds.size.sqrMagnitude > body.sharedMesh.bounds.size.sqrMagnitude)
                body = smr;

        if (body == null) return;

        var material = body.material;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.Lerp(Color.white, tint, 0.35f));
    }

    // ---------------- ruce (pro zbrane) ----------------

    public Transform GetHand(bool right)
    {
        return animator != null && animator.isHuman
            ? animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand)
            : null;
    }

    public Transform GetGrip(bool right)
    {
        return animator != null && animator.isHuman
            ? animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal)
            : null;
    }

    // Nastavi klidovou pozu, aby se podle ni dalo spocitat, kam v ruce zbran patri.
    public void EvaluateIdlePose()
    {
        if (!graph.IsValid()) return;

        for (int i = 0; i < StateCount; i++)
        {
            stateWeights[i] = i == Idle ? 1f : 0f;
            locomotion.SetInputWeight(i, stateWeights[i]);
        }

        states[Idle].SetTime(0f);
        layers.SetInputWeight(1, 0f);
        layers.SetInputWeight(2, 0f);
        layers.SetInputWeight(3, 0f);
        layers.SetInputWeight(4, 0f);
        graph.Evaluate(0f);
    }

    // Poza bloku (ruce vpredu), podle ni se pocita, kde sekyry v bloku v rukou drzi.
    public void EvaluateBlockPose()
    {
        if (!graph.IsValid()) return;

        EvaluateIdlePose();
        layers.SetInputWeight(3, 1f);
        graph.Evaluate(0f);
    }

    // ---------------- animace ----------------

    void BuildGraph()
    {
        if (animator == null || clips == null || clips.idle == null) return;

        graph = PlayableGraph.Create("CharacterGraph");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Animation", animator);

        locomotion = AnimationMixerPlayable.Create(graph, StateCount);
        var stateClips = new[] { clips.idle, clips.walk, clips.jog, clips.sprint, clips.jump, clips.death };
        for (int i = 0; i < StateCount; i++)
        {
            var clip = stateClips[i] != null ? stateClips[i] : clips.idle;
            states[i] = AnimationClipPlayable.Create(graph, clip);
            states[i].SetSpeed(0);
            graph.Connect(states[i], 0, locomotion, i);
            locomotion.SetInputWeight(i, i == Idle ? 1f : 0f);
            stateWeights[i] = i == Idle ? 1f : 0f;
            stateTimes[i] = 0f;
        }

        upperBody = new AvatarMask();
        foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
        {
            if (part == AvatarMaskBodyPart.LastBodyPart) continue;
            bool active = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head
                || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers;
            upperBody.SetHumanoidBodyPartActive(part, active);
        }

        layers = AnimationLayerMixerPlayable.Create(graph, 6);
        graph.Connect(locomotion, 0, layers, 0);
        layers.SetInputWeight(0, 1f);

        meleePlayable = AnimationClipPlayable.Create(graph, clips.meleeAttack != null ? clips.meleeAttack : clips.idle);
        shootPlayable = AnimationClipPlayable.Create(graph, clips.shoot != null ? clips.shoot : clips.idle);
        meleePlayable.SetSpeed(0);
        shootPlayable.SetSpeed(0);
        graph.Connect(meleePlayable, 0, layers, 1);
        graph.Connect(shootPlayable, 0, layers, 2);
        layers.SetInputWeight(1, 0f);
        layers.SetInputWeight(2, 0f);
        layers.SetLayerMaskFromAvatarMask(1, upperBody);
        layers.SetLayerMaskFromAvatarMask(2, upperBody);

        // Poza bloku: ruce vpredu (drzeny na posledni snimek kratke animace), sekyry se v nich zkrizi.
        var blockClip = clips.blockPose != null ? clips.blockPose : clips.idle;
        blockPlayable = AnimationClipPlayable.Create(graph, blockClip);
        blockPlayable.SetSpeed(0);
        blockPlayable.SetTime(Mathf.Max(0f, blockClip.length - 0.02f));
        graph.Connect(blockPlayable, 0, layers, 3);
        layers.SetInputWeight(3, 0f);
        layers.SetLayerMaskFromAvatarMask(3, upperBody);
        blockWeight = 0f;

        // Poza vypadu (Modry plamen): cele telo, zmrazeny snimek uderu - prava ruka vpredu, leva u tela.
        var dashClip = clips.dashPose != null ? clips.dashPose : clips.idle;
        dashPlayable = AnimationClipPlayable.Create(graph, dashClip);
        dashPlayable.SetSpeed(0);
        graph.Connect(dashPlayable, 0, layers, 4);
        layers.SetInputWeight(4, 0f);
        dashWeight = 0f;

        // Prebijeni: horni polovina tela.
        var reloadClip = clips.reload != null ? clips.reload : clips.idle;
        reloadPlayable = AnimationClipPlayable.Create(graph, reloadClip);
        reloadPlayable.SetSpeed(0);
        graph.Connect(reloadPlayable, 0, layers, 5);
        layers.SetInputWeight(5, 0f);
        layers.SetLayerMaskFromAvatarMask(5, upperBody);
        reloadTime = -1f;

        output.SetSourcePlayable(layers);
        graph.Play();
        graph.Evaluate(0f);
    }

    void DestroyGraph()
    {
        if (graph.IsValid())
            graph.Destroy();

        if (upperBody != null)
            Destroy(upperBody);

        upperBody = null;
    }

    public void PlayReload(float duration)
    {
        if (!graph.IsValid()) return;

        reloadDuration = Mathf.Max(0.1f, duration);
        reloadTime = 0f;
    }

    public void PlayAttack(bool melee, float duration)
    {
        if (!graph.IsValid()) return;

        attackMelee = melee;
        attackDuration = Mathf.Max(0.1f, duration);
        attackTime = 0f;
        attackClipLength = (melee ? meleePlayable : shootPlayable).GetAnimationClip().length;
    }

    void Update()
    {
        if (ModelRoot == null) return;

        bool owner = fpc != null && fpc.IsSpawned && fpc.IsOwner;
        bool visible = !owner || fpc.ThirdPerson;
        foreach (var r in renderers)
            if (r != null && r.enabled != visible)
                r.enabled = visible;

        if (bodyCapsule != null && bodyCapsule.enabled)
            bodyCapsule.enabled = false;

        if (!graph.IsValid()) return;

        float dt = Time.deltaTime;
        UpdateSpeed(dt);

        bool dead = health != null && health.currentHealth.Value <= 0f;
        if (dead && !wasDead)
            stateTimes[Death] = 0f;
        wasDead = dead;

        bool grounded = IsGrounded();

        // Modry plamen: vypad v utocne poze.
        bool frenzy = !dead && rush != null && rush.enabled && rush.IsSpawned && rush.IsRushing;

        for (int i = 0; i < StateCount; i++)
        {
            target[i] = 0f;
            rate[i] = 1f;
        }

        if (dead)
        {
            target[Death] = 1f;
        }
        else if (!grounded)
        {
            target[Jump] = 1f;
        }
        else if (frenzy)
        {
            target[Sprint] = 1f;
            rate[Sprint] = Mathf.Clamp(smoothedSpeed / 6.5f, 1f, 2.2f);
        }
        else if (smoothedSpeed < 0.4f)
        {
            target[Idle] = 1f;
        }
        else if (smoothedSpeed < 3.4f)
        {
            target[Walk] = 1f;
            rate[Walk] = Mathf.Clamp(smoothedSpeed / 1.8f, 0.6f, 2f);
        }
        else if (smoothedSpeed < 6.6f)
        {
            target[Jog] = 1f;
            rate[Jog] = Mathf.Clamp(smoothedSpeed / 4.2f, 0.6f, 1.8f);
        }
        else
        {
            target[Sprint] = 1f;
            rate[Sprint] = Mathf.Clamp(smoothedSpeed / 6.5f, 0.6f, 1.6f);
        }

        float blend = 1f - Mathf.Exp(-12f * dt);
        for (int i = 0; i < StateCount; i++)
        {
            stateWeights[i] = Mathf.Lerp(stateWeights[i], target[i], blend);
            locomotion.SetInputWeight(i, stateWeights[i]);

            var clip = states[i].GetAnimationClip();
            float length = Mathf.Max(0.05f, clip.length);
            stateTimes[i] += dt * rate[i];

            if (i == Death)
                stateTimes[i] = Mathf.Min(stateTimes[i], length - 0.02f);
            else
                stateTimes[i] = Mathf.Repeat(stateTimes[i], length);

            states[i].SetTime(stateTimes[i]);
        }

        bool blocking = !dead && block != null && block.IsBlocking;
        blockWeight = Mathf.MoveTowards(blockWeight, blocking ? 1f : 0f, dt * 8f);
        layers.SetInputWeight(3, blockWeight);

        UpdateAttack(dt, dead, frenzy);

        // Behem bloku nema utocna vrstva prednost.
        if (blockWeight > 0.01f)
        {
            layers.SetInputWeight(1, 0f);
            layers.SetInputWeight(2, 0f);
        }

        // Vypad: zmrazena poza pres cele telo a naklon dopredu (kolem chodidel).
        dashWeight = Mathf.MoveTowards(dashWeight, frenzy ? 1f : 0f, dt * (frenzy ? 16f : 7f));
        dashPlayable.SetTime(DashPoseTime * dashPlayable.GetAnimationClip().length);
        layers.SetInputWeight(4, dashWeight);
        ModelRoot.localRotation = Quaternion.Euler(DashLean * dashWeight, 0f, 0f);
        // Prebijeni: animace se roztahne na dobu prebiti; pri smrti, bloku a vypadu se nepouzije.
        float reloadWeight = 0f;
        if (reloadTime >= 0f)
        {
            reloadTime += dt;
            float t = reloadTime / reloadDuration;
            if (t >= 1f || dead)
            {
                reloadTime = -1f;
            }
            else
            {
                reloadPlayable.SetTime(t * reloadPlayable.GetAnimationClip().length);
                reloadWeight = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01((1f - t) / 0.12f);
            }
        }

        if (blockWeight > 0.01f)
            reloadWeight = 0f;

        if (dashWeight > 0.01f)
        {
            layers.SetInputWeight(1, 0f);
            layers.SetInputWeight(2, 0f);
            layers.SetInputWeight(3, 0f);
            reloadWeight = 0f;
        }

        layers.SetInputWeight(5, reloadWeight);
    }

    // Ktery okamzik animace uderu se pouzije jako poza vypadu (0-1) a naklon tela ve stupnich.
    public static float DashPoseTime = 0.55f;
    public static float DashLean = 20f;

    public float BlockBlend => blockWeight;

    void UpdateAttack(float dt, bool dead, bool frenzy)
    {
        float melee = 0f, shoot = 0f;

        if (frenzy)
            attackTime = -1f;

        if (attackTime >= 0f && !dead)
        {
            attackTime += dt;
            float t = attackTime / attackDuration;
            if (t >= 1f)
            {
                attackTime = -1f;
            }
            else
            {
                float envelope = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01((1f - t) / 0.2f);
                var playable = attackMelee ? meleePlayable : shootPlayable;
                playable.SetTime(t * attackClipLength);

                if (attackMelee) melee = envelope;
                else shoot = envelope;
            }
        }

        layers.SetInputWeight(1, melee);
        layers.SetInputWeight(2, shoot);
    }

    static float attackClipLengthOf(AnimationClipPlayable playable)
    {
        return playable.GetAnimationClip().length;
    }

    void UpdateSpeed(float dt)
    {
        Vector3 position = transform.position;
        Vector3 delta = position - lastPosition;
        lastPosition = position;

        if (dt <= 0f) return;

        // Teleport (respawn, reset kola) neni beh.
        float raw = delta.magnitude > 3f ? 0f : new Vector2(delta.x, delta.z).magnitude / dt;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, raw, 1f - Mathf.Exp(-10f * dt));
    }

    bool IsGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * (feetLocalY + 0.2f);
        return Physics.Raycast(origin, Vector3.down, 0.55f, ~0, QueryTriggerInteraction.Ignore);
    }
}
