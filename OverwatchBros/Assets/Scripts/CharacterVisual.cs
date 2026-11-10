using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// 3D model postavy hrdiny (HeroDefinition.characterPrefab) a jeho animace (chuze, beh, skok, smrt, utok).
// Ostatni hraci ho vidi na tele hrace; majitel jen kdyz je kamera ve 3. osobe (Ohnivy dopad). Animace bezi pres Playables
// (bez Animator Controlleru) a vychazi z rychlosti pohybu, takze funguje stejne pro majitele i pro ostatni po siti.
// (pred HeldWeapons: sevreni prstu prepocita celou pozu - pak teprve leva ruka na predpazbi, jinak by ji stahlo)
[DefaultExecutionOrder(-50)]
public class CharacterVisual : MonoBehaviour
{
    PlayerHero hero;
    const int Idle = 0, Walk = 1, Jog = 2, Sprint = 3, Jump = 4, Death = 5, StateCount = 6;

    public int Version { get; private set; }

    // Duch pro prehravani "play of the game" (bez site): stav nastavuje ReplayPlayer.
    public bool IsGhost;
    public float SleepUntil;   // uspani: do te doby lezi na zemi
    public bool GhostHidden, GhostDead, GhostFrenzy, GhostBlocking;

    public void SetFeetOffset(float y)
    {
        feetLocalY = y;
    }
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
    CharacterAnimSet heroClips;   // vlastni animace hrdiny (prepisuji vyplnena policka spolecne sady)

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
        gripHandler?.Dispose();
    }

    public void SetModel(GameObject prefab, Color tint, CharacterAnimSet animations = null)
    {
        if (prefab == null)
        {
            ClearModel();
            return;
        }

        if (prefab != currentPrefab || animations != heroClips || ModelRoot == null)
        {
            ClearModel();

            if (clips == null)
                clips = Resources.Load<CharacterAnimSet>("Characters/CharacterAnimations");
            heroClips = animations;

            currentPrefab = prefab;
            if (controller != null)
                feetLocalY = controller.center.y - fpc.standHeight * 0.5f;

            var instance = Instantiate(prefab, transform);
            instance.name = "CharacterModel";
            instance.transform.localPosition = new Vector3(0f, feetLocalY, 0f);
            instance.transform.localRotation = Quaternion.identity;
            ModelRoot = instance.transform;

            foreach (var collider in instance.GetComponentsInChildren<Collider>())
                DestroyImmediate(collider);

            renderers = instance.GetComponentsInChildren<Renderer>();
            // Modely podle fotky maji po importu male bounds a updateWhenOffscreen (jinak by postava mizela). Ten ale
            // kazdy snimek prepocitava bounds a skinuje i postavy mimo obraz. Pevne velke bounds (5 m kolem korenove
            // kosti) pokryji kazdou pozu, takze postava nemizi a mimo obraz je zadarmo.
            foreach (var skinned in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!skinned.updateWhenOffscreen) continue;
                skinned.updateWhenOffscreen = false;
                skinned.localBounds = new Bounds(Vector3.zero, Vector3.one * 5f);
            }
            CollectFaceDetail(instance);
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

    // Stinova forma (Max): vsechny casti modelu docasne pruhledne tmave fialove (svitici), pak puvodni materialy.
    static Material shadowMaterial;
    Material[][] savedMaterials;

    public void SetShadow(bool on)
    {
        if (on)
        {
            if (shadowMaterial == null)
            {
                shadowMaterial = new Material(Fx.ParticleMaterial) { mainTexture = null };
                var tint = new Color(0.22f, 0.04f, 0.38f, 0.55f);
                shadowMaterial.color = tint;
                if (shadowMaterial.HasProperty("_BaseColor")) shadowMaterial.SetColor("_BaseColor", tint);
            }
            if (savedMaterials == null)
            {
                savedMaterials = new Material[renderers.Length][];
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null) continue;
                    savedMaterials[i] = renderers[i].sharedMaterials;
                    var shadows = new Material[savedMaterials[i].Length];
                    for (int k = 0; k < shadows.Length; k++) shadows[k] = shadowMaterial;
                    renderers[i].sharedMaterials = shadows;
                }
            }
        }
        else if (savedMaterials != null)
        {
            for (int i = 0; i < renderers.Length && i < savedMaterials.Length; i++)
                if (renderers[i] != null && savedMaterials[i] != null)
                    renderers[i].sharedMaterials = savedMaterials[i];
            savedMaterials = null;
        }
    }

    // ---------------- detaily obliceje (modely podle fotky) ----------------
    // Zuby, rasy a oci ve stinu nejsou videt - stin nevrhaji. Zuby a rasy se navic na dalku skryji
    // (u bezne postavy je to pres 20 % trojuhelniku). Vzdalenost se pocita i s priblizenim zamerovacem.
    const float FaceDetailDistance = 15f;
    readonly System.Collections.Generic.List<Renderer> farHiddenDetail = new System.Collections.Generic.List<Renderer>();
    bool faceDetailHidden;

    void CollectFaceDetail(GameObject instance)
    {
        farHiddenDetail.Clear();
        faceDetailHidden = false;
        foreach (var skinned in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            string part = skinned.name;
            bool hideFar = part == "AvatarTeethUpper" || part == "AvatarTeethLower" || part == "AvatarEyelashes";
            if (!hideFar && part != "AvatarLeftEyeball" && part != "AvatarRightEyeball") continue;

            skinned.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (hideFar) farHiddenDetail.Add(skinned);
        }
    }

    void UpdateFaceDetail()
    {
        if (farHiddenDetail.Count == 0) return;
        var cam = Camera.main;
        if (cam == null) return;

        float distance = Vector3.Distance(cam.transform.position, ModelRoot.position) * cam.fieldOfView / 60f;
        bool hide = distance > FaceDetailDistance;
        if (hide == faceDetailHidden) return;

        faceDetailHidden = hide;
        // (forceRenderingOff: 'enabled' ridi viditelnost cele postavy v Update)
        foreach (var detail in farHiddenDetail)
            if (detail != null) detail.forceRenderingOff = hide;
    }

    public void ClearModel()
    {
        DestroyGraph();
        gripHandler?.Dispose();   // (sevreni prstu se navaze na novy model)
        gripHandler = null;

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

    public Transform GetBone(HumanBodyBones bone)
    {
        return animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
    }

    public Animator BodyAnimator => animator != null && animator.isHuman ? animator : null;

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
        for (int i = 0; i < AbilityAnimCount; i++)
            layers.SetInputWeight(AbilityLayer + i, 0f);
        graph.Evaluate(0f);
    }

    // Hrdina ma vlastni animaci strelby (HeroDefinition.animations.shoot), napr. z Mixama.
    public bool HasHeroShoot => heroClips != null && heroClips.shoot != null;

    // Zbran stale namirena: horni polovina tela drzi prvni snimek animace strelby (mezi vystrely),
    // vystrel prehraje celou animaci (recoil). Nastavuje HeldWeapons (Viktor - puska obema rukama).
    public bool HoldShootPose;
    // Zbran namirena jen pri mireni dalekohledem (FirstPersonController.scoped) a chvili po vystrelu - ne pri staní (Kondulic).
    public bool AimOnScope;

    // Poza strelby (prvni snimek), podle ni se usadi zbran do ruky.
    public void EvaluateShootPose()
    {
        if (!graph.IsValid()) return;

        EvaluateIdlePose();
        shootPlayable.SetTime(0f);
        layers.SetInputWeight(2, 1f);
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

    // Klip z vlastni sady hrdiny, jinak ze spolecne, jinak klid.
    AnimationClip Pick(System.Func<CharacterAnimSet, AnimationClip> field)
    {
        var clip = heroClips != null ? field(heroClips) : null;
        if (clip == null && clips != null) clip = field(clips);
        return clip != null ? clip : null;   // (smazany asset -> opravdovy null, at funguje ??)
    }

    void BuildGraph()
    {
        var idle = Pick(s => s.idle);
        if (animator == null || idle == null) return;

        graph = PlayableGraph.Create("CharacterGraph");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(graph, "Animation", animator);

        locomotion = AnimationMixerPlayable.Create(graph, StateCount);
        var stateClips = new[] { idle, Pick(s => s.walk), Pick(s => s.jog), Pick(s => s.sprint), Pick(s => s.jump), Pick(s => s.death) };
        for (int i = 0; i < StateCount; i++)
        {
            var clip = stateClips[i] != null ? stateClips[i] : idle;
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

        layers = AnimationLayerMixerPlayable.Create(graph, AbilityLayer + AbilityAnimCount);
        graph.Connect(locomotion, 0, layers, 0);
        layers.SetInputWeight(0, 1f);

        meleePlayable = AnimationClipPlayable.Create(graph, Pick(s => s.meleeAttack) ?? idle);
        shootPlayable = AnimationClipPlayable.Create(graph, Pick(s => s.shoot) ?? idle);
        meleePlayable.SetSpeed(0);
        shootPlayable.SetSpeed(0);
        graph.Connect(meleePlayable, 0, layers, 1);
        graph.Connect(shootPlayable, 0, layers, 2);
        layers.SetInputWeight(1, 0f);
        layers.SetInputWeight(2, 0f);
        layers.SetLayerMaskFromAvatarMask(1, upperBody);
        layers.SetLayerMaskFromAvatarMask(2, upperBody);

        // Poza bloku: ruce vpredu (drzeny na posledni snimek kratke animace), sekyry se v nich zkrizi.
        var blockClip = Pick(s => s.blockPose) ?? idle;
        blockPlayable = AnimationClipPlayable.Create(graph, blockClip);
        blockPlayable.SetSpeed(0);
        blockPlayable.SetTime(Mathf.Max(0f, blockClip.length - 0.02f));
        graph.Connect(blockPlayable, 0, layers, 3);
        layers.SetInputWeight(3, 0f);
        layers.SetLayerMaskFromAvatarMask(3, upperBody);
        blockWeight = 0f;

        // Poza vypadu (Modry plamen): cele telo, zmrazeny snimek uderu - prava ruka vpredu, leva u tela.
        var dashClip = Pick(s => s.dashPose) ?? idle;
        dashPlayable = AnimationClipPlayable.Create(graph, dashClip);
        dashPlayable.SetSpeed(0);
        graph.Connect(dashPlayable, 0, layers, 4);
        layers.SetInputWeight(4, 0f);
        dashWeight = 0f;

        // Prebijeni: horni polovina tela.
        var reloadClip = Pick(s => s.reload) ?? idle;
        reloadPlayable = AnimationClipPlayable.Create(graph, reloadClip);
        reloadPlayable.SetSpeed(0);
        graph.Connect(reloadPlayable, 0, layers, 5);
        layers.SetInputWeight(5, 0f);
        layers.SetLayerMaskFromAvatarMask(5, upperBody);
        reloadTime = -1f;

        // Ultimatka pres cele telo (Mosino: tanec), ve smycce; bez klipu se nepouziva.
        ultimateClip = Pick(s => s.ultimate);
        ultimatePlayable = AnimationClipPlayable.Create(graph, ultimateClip ?? idle);
        ultimatePlayable.SetSpeed(0);
        graph.Connect(ultimatePlayable, 0, layers, 6);
        layers.SetInputWeight(6, 0f);
        ultimateWeight = 0f;
        ultimateUntil = -1f;

        // Schopnosti pres cele telo (Maxuv skok s uderem, otocka): jednorazove, jen usek klipu; bez klipu se nepouziva.
        for (int i = 0; i < AbilityAnimCount; i++)
        {
            var anim = abilityAnims[i];
            anim.clip = Pick(i == (int)AbilityAnim.Shift ? s => s.shiftAbility : i == (int)AbilityAnim.RightMouse ? s => s.rmbAbility : s => s.eAbility);
            anim.playable = AnimationClipPlayable.Create(graph, anim.clip ?? idle);
            anim.playable.SetSpeed(0);
            graph.Connect(anim.playable, 0, layers, AbilityLayer + i);
            layers.SetInputWeight(AbilityLayer + i, 0f);
            anim.time = -1f;
        }

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

    public void StopReload()
    {
        reloadTime = -1f;
    }

    // ---------------- ultimatka pres cele telo (Mosinuv tanec) ----------------

    AnimationClip ultimateClip;
    AnimationClipPlayable ultimatePlayable;
    float ultimateWeight, ultimateTime, ultimateUntil = -1f;

    public bool HasUltimateAnimation => ultimateClip != null;
    public bool UltimateActive => ultimateUntil > Time.time;

    // Prehraje animaci ultimatky (ve smycce) na 'seconds' sekund; StopUltimate ji ukonci driv.
    public void PlayUltimate(float seconds)
    {
        if (!IsGhost)
            ReplayLog.Ultimate(this, seconds);
        if (!graph.IsValid() || ultimateClip == null) return;
        ultimateUntil = Time.time + seconds;
        ultimateTime = 0f;
    }

    // Libovolna animace pres cele telo ve smycce (vrstva ultimatky) na 'seconds' s - napr. sezeni v uvodni scence.
    // 'instant' = hned naplno (bez prolnuti).
    public void PlayFullBody(AnimationClip clip, float seconds, bool instant = false)
    {
        if (!graph.IsValid() || clip == null) return;
        if (clip != ultimateClip)
        {
            graph.Disconnect(layers, 6);
            if (ultimatePlayable.IsValid()) ultimatePlayable.Destroy();
            ultimatePlayable = AnimationClipPlayable.Create(graph, clip);
            ultimatePlayable.SetSpeed(0);
            graph.Connect(ultimatePlayable, 0, layers, 6);
            ultimateClip = clip;
        }
        ultimateUntil = Time.time + seconds;
        ultimateTime = 0f;
        if (instant)
        {
            ultimateWeight = 1f;
            ultimatePlayable.SetTime(0f);
            layers.SetInputWeight(6, 1f);
            graph.Evaluate(0f);
        }
    }

    public void StopUltimate()
    {
        if (!IsGhost && ultimateUntil > Time.time)
            ReplayLog.Ultimate(this, 0f);
        ultimateUntil = -1f;
    }

    void UpdateUltimate(float dt, bool dead)
    {
        bool on = !dead && ultimateUntil > Time.time;
        ultimateWeight = Mathf.MoveTowards(ultimateWeight, on ? 1f : 0f, dt * 6f);
        if (ultimateWeight > 0f && ultimateClip != null)
        {
            ultimateTime = Mathf.Repeat(ultimateTime + dt, Mathf.Max(0.05f, ultimateClip.length));
            ultimatePlayable.SetTime(ultimateTime);
        }
        layers.SetInputWeight(6, ultimateWeight);
    }

    // ---------------- schopnosti pres cele telo (Maxuv skok s uderem na Shift, otocka na PTM) ----------------

    public enum AbilityAnim { Shift, RightMouse, E }
    const int AbilityLayer = 7, AbilityAnimCount = 3;

    class OneShot
    {
        public AnimationClip clip;
        public AnimationClipPlayable playable;
        public float time = -1f, from, to, duration = 1f;
    }
    readonly OneShot[] abilityAnims = { new OneShot(), new OneShot(), new OneShot() };

    public bool HasAbilityAnimation(AbilityAnim which) => abilityAnims[(int)which].clip != null;
    public float AbilityClipLength(AbilityAnim which) => abilityAnims[(int)which].clip != null ? abilityAnims[(int)which].clip.length : 0f;

    // Prehraje usek klipu od 'from' do 'to' (sekundy klipu) za 'duration' sekund.
    public void PlayAbility(AbilityAnim which, float from, float to, float duration)
    {
        if (!IsGhost)
            ReplayLog.Ability(this, (int)which, from, to, duration);
        var anim = abilityAnims[(int)which];
        if (!graph.IsValid() || anim.clip == null) return;
        anim.from = from;
        anim.to = Mathf.Min(to, anim.clip.length);
        anim.duration = Mathf.Max(0.05f, duration);
        anim.time = 0f;
    }

    void UpdateAbilities(float dt, bool dead)
    {
        for (int i = 0; i < AbilityAnimCount; i++)
        {
            var anim = abilityAnims[i];
            float weight = 0f;
            if (anim.time >= 0f && !dead)
            {
                anim.time += dt;
                float t = anim.time / anim.duration;
                if (t >= 1f)
                    anim.time = -1f;
                else
                {
                    weight = Mathf.Clamp01(t / 0.08f) * Mathf.Clamp01((1f - t) / 0.2f);
                    anim.playable.SetTime(Mathf.Lerp(anim.from, anim.to, t));
                }
            }
            else
                anim.time = -1f;
            layers.SetInputWeight(AbilityLayer + i, weight);
        }
    }

    public void PlayReload(float duration)
    {
        if (!graph.IsValid()) return;

        reloadDuration = Mathf.Max(0.1f, duration);
        reloadTime = 0f;
    }

    // ---------------- kop (sniperovo odkopnuti) ----------------
    // Knihovna animaci kop nema, tak se po animaci natoci kosti prave nohy (a trup lehce dozadu).
    float kickTime = -1f, kickDuration = 0.4f;

    public void PlayKick(float duration)
    {
        if (!IsGhost)
            ReplayLog.Kick(this, duration);
        if (animator == null || !animator.isHuman) return;
        kickDuration = Mathf.Max(0.1f, duration);
        kickTime = 0f;
    }

    // ---------------- sevreni prstu kolem zbrane (po animaci) ----------------

    // Ktera ruka drzi zbran (nastavuje HeldWeapons): prsty se po animaci sevrou v pest kolem rukojeti.
    public bool GripRight, GripLeft;
    HumanPoseHandler gripHandler;
    HumanPose gripPose;
    int[] gripRightMuscles, gripLeftMuscles;
    float[] gripRightValues, gripLeftValues;

    bool AnyRendererVisible()
    {
        foreach (var r in renderers)
            if (r != null && r.enabled && r.isVisible) return true;
        return false;
    }

    void ApplyGrip()
    {
        if ((!GripRight && !GripLeft) || animator == null || !animator.isHuman || ModelRoot == null) return;
        // (cela poza se prepocitava pres svaly - drahe; prsty postavy mimo obraz nikdo nevidi)
        if (!AnyRendererVisible()) return;
        if (gripHandler == null)
        {
            gripHandler = new HumanPoseHandler(animator.avatar, ModelRoot);
            CollectGripMuscles("Right", out gripRightMuscles, out gripRightValues);
            CollectGripMuscles("Left", out gripLeftMuscles, out gripLeftValues);
        }
        gripHandler.GetHumanPose(ref gripPose);
        if (GripRight) for (int i = 0; i < gripRightMuscles.Length; i++) gripPose.muscles[gripRightMuscles[i]] = gripRightValues[i];
        if (GripLeft) for (int i = 0; i < gripLeftMuscles.Length; i++) gripPose.muscles[gripLeftMuscles[i]] = gripLeftValues[i];
        gripHandler.SetHumanPose(ref gripPose);
    }

    // Svaly prstu dane ruky: prsty sevrene (Stretched -0,85), palec pritisknuty (-0,35).
    static void CollectGripMuscles(string side, out int[] indices, out float[] values)
    {
        var list = new System.Collections.Generic.List<int>();
        var vals = new System.Collections.Generic.List<float>();
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
        {
            string name = HumanTrait.MuscleName[i];
            if (!name.StartsWith(side) || !name.Contains("Stretched")) continue;
            if (name.Contains("Thumb")) { list.Add(i); vals.Add(-0.35f); }
            else if (name.Contains("Index") || name.Contains("Middle") || name.Contains("Ring") || name.Contains("Little")) { list.Add(i); vals.Add(-0.85f); }
        }
        indices = list.ToArray();
        values = vals.ToArray();
    }

    void LateUpdate()
    {
        LateKick();
        ApplyGrip();
        GroundDeathPose();
    }

    // Lezici telo na zem: animace smrti (a uspani) drzi boky ve vysce pasu (svisly pohyb korene se z klipu nebere),
    // takze mrtvy lezel ve vzduchu. Po animaci se model posune dolu, aby nejnizsi kost byla tesne nad zemi.
    static readonly HumanBodyBones[] GroundBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
    };
    Transform[] groundBones;
    Animator groundBonesFor;
    float poseDrop;

    void GroundDeathPose()
    {
        if (ModelRoot == null || animator == null || !animator.isHuman || !graph.IsValid()) return;
        float weight = Mathf.Clamp01(stateWeights[Death]);
        if (weight < 0.01f && poseDrop <= 0f) return;

        if (groundBonesFor != animator)
        {
            groundBonesFor = animator;
            groundBones = new Transform[GroundBones.Length];
            for (int i = 0; i < GroundBones.Length; i++)
                groundBones[i] = animator.GetBoneTransform(GroundBones[i]);
        }

        Vector3 basePosition = transform.TransformPoint(new Vector3(0f, feetLocalY, 0f));
        float lowest = float.MaxValue;
        foreach (var bone in groundBones)
            if (bone != null) lowest = Mathf.Min(lowest, bone.position.y);
        if (lowest == float.MaxValue) return;

        lowest += poseDrop;   // (kosti uz jsou posunute o minuly posun)
        poseDrop = Mathf.Max(0f, lowest - basePosition.y - 0.1f) * weight;
        if (weight < 0.01f) poseDrop = 0f;
        ModelRoot.position = basePosition + Vector3.down * poseDrop;
    }

    void LateKick()
    {
        if (kickTime < 0f || animator == null || ModelRoot == null) return;

        kickTime += Time.deltaTime;
        float t = kickTime / kickDuration;
        if (t >= 1f)
        {
            kickTime = -1f;
            return;
        }

        // Stehno: nadechnuti dozadu, rychle vykopnuti dopredu, drzeni, navrat. Koleno se pri nadechnuti pokrci.
        float thigh, knee;
        if (t < 0.18f) { thigh = Mathf.Lerp(0f, -20f, t / 0.18f); knee = Mathf.Lerp(0f, 70f, t / 0.18f); }
        else if (t < 0.4f) { float k = Mathf.SmoothStep(0f, 1f, (t - 0.18f) / 0.22f); thigh = Mathf.Lerp(-20f, 95f, k); knee = Mathf.Lerp(70f, 0f, k); }
        else if (t < 0.6f) { thigh = 95f; knee = 0f; }
        else { float k = (t - 0.6f) / 0.4f; thigh = Mathf.Lerp(95f, 0f, k); knee = Mathf.Lerp(0f, 0f, k); }

        Vector3 right = ModelRoot.right;
        var upper = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        var lower = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        if (upper != null)
            upper.rotation = Quaternion.AngleAxis(-thigh, right) * upper.rotation;
        if (lower != null)
            lower.rotation = Quaternion.AngleAxis(knee, right) * lower.rotation;
        if (spine != null)
            spine.rotation = Quaternion.AngleAxis(-thigh * 0.12f, right) * spine.rotation;   // zaklon
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

        bool owner = fpc != null && fpc.IsSpawned && fpc.IsOwner && fpc.Bot == null;
        // Behem prehravani POTG jsou zivi hraci skryti (misto nich hraji duchove).
        if (hero == null && !IsGhost) hero = GetComponent<PlayerHero>();
        bool hidden = hero != null && hero.IsSpawned && hero.IsHidden;   // Max uvnitr obeti
        bool visible = IsGhost ? !GhostHidden : (!owner || fpc.ThirdPerson) && !ReplayPlayer.Active && !hidden;
        foreach (var r in renderers)
            if (r != null && r.enabled != visible)
                r.enabled = visible;
        UpdateFaceDetail();

        if (bodyCapsule != null && bodyCapsule.enabled)
            bodyCapsule.enabled = false;

        if (!graph.IsValid()) return;

        float dt = Time.deltaTime;
        UpdateSpeed(dt);

        bool dead = IsGhost ? GhostDead : health != null && health.currentHealth.Value <= 0f;
        // uspany (Davidova sipka): spadne na zem jako mrtvy, po probuzeni vstane
        bool down = dead || Time.time < SleepUntil;
        if (down && !wasDead)
            stateTimes[Death] = 0f;
        wasDead = down;

        bool grounded = IsGrounded();

        // Modry plamen: vypad v utocne poze.
        bool frenzy = !dead && (IsGhost ? GhostFrenzy : rush != null && rush.enabled && rush.IsSpawned && rush.IsRushing);

        for (int i = 0; i < StateCount; i++)
        {
            target[i] = 0f;
            rate[i] = 1f;
        }

        if (down)
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

        bool blocking = !dead && (IsGhost ? GhostBlocking : block != null && block.IsBlocking);
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
        UpdateUltimate(dt, dead);
        UpdateAbilities(dt, dead);
    }

    // Ktery okamzik animace uderu se pouzije jako poza vypadu (0-1) a naklon tela ve stupnich.
    public static float DashPoseTime = 0.55f;
    public static float DashLean = 20f;

    public float BlockBlend => blockWeight;

    // Namirena zbran (HoldShootPose): ve stoji a chvili po vystrelu; pri behu bez strelby se pusti (bezi cele telo).
    const float AimAfterShot = 1f;
    float aimWeight, lastShotAt = -100f;
    public float AimHoldWeight => aimWeight;

    void UpdateAttack(float dt, bool dead, bool frenzy)
    {
        float melee = 0f, shoot = 0f;
        bool hold = HoldShootPose && !dead && !frenzy;
        if (attackTime >= 0f && !attackMelee) lastShotAt = Time.time;
        bool recentShot = Time.time - lastShotAt < AimAfterShot;
        bool aiming = hold && (AimOnScope
            ? recentShot || (fpc != null && fpc.IsSpawned && fpc.scoped.Value)
            : smoothedSpeed < 0.4f || recentShot);
        aimWeight = Mathf.MoveTowards(aimWeight, aiming ? 1f : 0f, dt * (aiming ? 8f : 3f));

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
                else shoot = hold ? Mathf.Max(envelope, aimWeight) : envelope;
            }
        }

        // mezi vystrely zbran namirena (prvni snimek animace strelby)
        if (hold && attackTime < 0f)
        {
            shootPlayable.SetTime(0f);
            shoot = aimWeight;
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
