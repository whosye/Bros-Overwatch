using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Ukazka animaci postavy se zbrani v ruce bez spousteni hry: BrosOverwatch > Ukazka animaci se zbrani.
// Postavi hrdinu (s jeho zbrani) do sceny stranou nad mapou a posuvnikem se projizdi animace - ze sady hrdiny,
// ze spolecne sady, nebo libovolny klip (napr. z Mixama). Zbran se do ruky usadi stejne jako ve hre (HeldWeapons).
// V okne Scene jsou videt body uchopu: zelena = Grip (rukojet), modra = Foregrip (predpazbi, leva ruka).
// Nic se neuklada do sceny; po zavreni okna postava zmizi.
public class WeaponPosePreview : EditorWindow
{
    static readonly string[] SlotNames =
    {
        "Klid (idle)", "Chůze (walk)", "Klus (jog)", "Sprint", "Skok (jump)", "Smrt (death)",
        "Útok zblízka (meleeAttack)", "Střelba (shoot)", "Přebíjení (reload)", "Blok (blockPose)", "Výpad (dashPose)",
        "Ultimátka (ultimate)",
    };
    static readonly System.Func<CharacterAnimSet, AnimationClip>[] SlotFields =
    {
        s => s.idle, s => s.walk, s => s.jog, s => s.sprint, s => s.jump, s => s.death,
        s => s.meleeAttack, s => s.shoot, s => s.reload, s => s.blockPose, s => s.dashPose,
        s => s.ultimate,
    };
    const int ShootSlot = 7;
    static readonly Vector3 Spot = new Vector3(0f, 300f, 0f);   // stranou nad mapou
    const float FrameStep = 1f / 30f;

    HeroDefinition hero;
    WeaponDefinition weaponOverride;
    int slot = ShootSlot;
    AnimationClip customClip;
    float time;
    bool playing;
    float speed = 1f;
    bool curlFingers = true;
    bool leftHandIk = true;
    bool attachAtFrame;   // false = zbran srovnana podle klidove pozy (jako ve hre)

    CharacterAnimSet shared;
    GameObject root, character;
    Animator animator;
    PlayableGraph graph;
    AnimationPlayableOutput output;
    AnimationClipPlayable playable;
    AnimationClip playableClip;
    HumanPoseHandler poseHandler;
    HumanPose pose;

    Transform holder;
    HeldModel model;
    bool leftHanded, hasForegrip;
    Vector3 grip, foregrip;
    FirstPersonArms.Arm supportArm;

    double lastTick;

    [MenuItem("BrosOverwatch/Ukázka animací se zbraní")]
    static void Open() => GetWindow<WeaponPosePreview>("Animace se zbraní");

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        Clear();
    }

    void OnPlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode) Clear();
    }

    // ---------------- okno ----------------

    void OnGUI()
    {
        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Ukázka funguje jen mimo Play mód.", MessageType.Info);
            return;
        }

        EditorGUI.BeginChangeCheck();
        hero = (HeroDefinition)EditorGUILayout.ObjectField("Hrdina", hero, typeof(HeroDefinition), false);
        weaponOverride = (WeaponDefinition)EditorGUILayout.ObjectField(new GUIContent("Zbraň", "Prázdné = zbraň hrdiny"),
            weaponOverride, typeof(WeaponDefinition), false);
        if (EditorGUI.EndChangeCheck() || (hero != null && root == null))
            Build();

        if (hero == null)
        {
            EditorGUILayout.HelpBox("Vyber hrdinu (Assets/Resources/Heroes).", MessageType.Info);
            return;
        }
        if (animator == null)
        {
            EditorGUILayout.HelpBox("Hrdina nemá model postavy s Humanoid avatarem (characterPrefab).", MessageType.Warning);
            return;
        }

        EditorGUILayout.Space();
        EditorGUI.BeginChangeCheck();
        slot = EditorGUILayout.Popup("Animace ze sady", slot, SlotNames);
        customClip = (AnimationClip)EditorGUILayout.ObjectField(new GUIContent("Vlastní klip", "Např. animace z Mixama. Když je vyplněný, použije se místo sady."),
            customClip, typeof(AnimationClip), false);
        bool clipChanged = EditorGUI.EndChangeCheck();

        var clip = CurrentClip;
        if (clip == null)
        {
            EditorGUILayout.HelpBox("Tahle animace v sadě chybí.", MessageType.Warning);
            return;
        }
        if (!clip.humanMotion)
            EditorGUILayout.HelpBox("Klip není Humanoid - v importu FBX nastav Rig > Animation Type = Humanoid.", MessageType.Warning);
        EditorGUILayout.LabelField("Klip", $"{clip.name}  ({clip.length:0.00} s, {Mathf.RoundToInt(clip.length * clip.frameRate)} snímků)");
        if (clipChanged)
        {
            time = Mathf.Min(time, clip.length);
            if (attachAtFrame) Attach();
        }

        EditorGUILayout.Space();
        EditorGUI.BeginChangeCheck();
        time = EditorGUILayout.Slider("Čas", time, 0f, clip.length);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("|◀", GUILayout.Width(40))) time = 0f;
            if (GUILayout.Button("◀", GUILayout.Width(40))) time = Mathf.Max(0f, time - FrameStep);
            if (GUILayout.Button(playing ? "❚❚ Pauza" : "▶ Přehrát")) { playing = !playing; lastTick = EditorApplication.timeSinceStartup; }
            if (GUILayout.Button("▶", GUILayout.Width(40))) time = Mathf.Min(clip.length, time + FrameStep);
            if (GUILayout.Button("▶|", GUILayout.Width(40))) time = clip.length;
        }
        speed = EditorGUILayout.Slider("Rychlost", speed, 0.05f, 2f);
        bool timeChanged = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space();
        EditorGUI.BeginChangeCheck();
        curlFingers = EditorGUILayout.Toggle(new GUIContent("Sevřít prsty", "Jako ve hře: prsty v pěst kolem rukojeti"), curlFingers);
        using (new EditorGUI.DisabledScope(!hasForegrip))
            leftHandIk = EditorGUILayout.Toggle(new GUIContent("Levá ruka na předpažbí (IK)", "Jen zbraně obouruč"), leftHandIk);
        bool attachChanged = false;
        bool newAttach = EditorGUILayout.Toggle(new GUIContent("Srovnat zbraň podle tohoto snímku",
            "Vypnuto = jako ve hře (natočení zbraně se spočítá v klidové póze). Zapnuto = zbraň míří dopředu v aktuálním snímku."), attachAtFrame);
        if (newAttach != attachAtFrame) { attachAtFrame = newAttach; attachChanged = true; }
        if (attachAtFrame && GUILayout.Button("Znovu srovnat zbraň v tomto snímku")) attachChanged = true;
        bool optionsChanged = EditorGUI.EndChangeCheck();

        if (attachChanged) Attach();
        else if (clipChanged || timeChanged || optionsChanged) Evaluate();

        EditorGUILayout.Space();
        if (GUILayout.Button("Zaměřit v okně Scene")) FrameScene();
        EditorGUILayout.HelpBox("Ve Scene okně: zelená = Grip (rukojeť), modrá = Foregrip (předpažbí), tyrkysová čára = kam zbraň míří.", MessageType.None);
    }

    void Tick()
    {
        if (!playing || root == null) return;
        var clip = CurrentClip;
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastTick);
        lastTick = now;
        if (clip == null || clip.length <= 0f) return;
        time = Mathf.Repeat(time + dt * speed, clip.length);
        Evaluate();
        Repaint();
    }

    // ---------------- postava a zbran ----------------

    AnimationClip SlotClip(int index)
    {
        if (shared == null) shared = Resources.Load<CharacterAnimSet>("Characters/CharacterAnimations");
        var clip = hero != null && hero.animations != null ? SlotFields[index](hero.animations) : null;
        if (clip == null && shared != null) clip = SlotFields[index](shared);
        return clip;
    }

    AnimationClip CurrentClip => customClip != null ? customClip : SlotClip(slot);

    void Build()
    {
        Clear();
        if (hero == null || hero.characterPrefab == null) return;

        root = new GameObject("WeaponPosePreview");
        root.transform.position = Spot;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        DestroyImmediate(ground.GetComponent<Collider>());
        ground.name = "Ground";
        ground.transform.SetParent(root.transform, false);
        ground.transform.localPosition = new Vector3(0f, -0.01f, 0f);
        ground.transform.localScale = new Vector3(2.5f, 0.01f, 2.5f);

        character = Instantiate(hero.characterPrefab, root.transform);
        character.name = hero.characterPrefab.name;
        character.transform.localPosition = Vector3.zero;
        character.transform.localRotation = Quaternion.identity;
        foreach (var collider in character.GetComponentsInChildren<Collider>()) DestroyImmediate(collider);

        animator = character.GetComponent<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            animator = null;
            HideAll();
            return;
        }
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;

        graph = PlayableGraph.Create("WeaponPosePreview");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        output = AnimationPlayableOutput.Create(graph, "Animation", animator);
        poseHandler = new HumanPoseHandler(animator.avatar, character.transform);

        BuildWeapon();
        HideAll();
        Attach();
        FrameScene();
    }

    void BuildWeapon()
    {
        var weapon = weaponOverride != null ? weaponOverride : hero.weapon;
        if (weapon == null) return;
        model = weapon.heldModel == HeldModel.Auto ? (weapon.IsMelee ? HeldModel.Axe : HeldModel.Gun) : weapon.heldModel;
        if (model == HeldModel.None) return;

        // luk drzi leva ruka (jako ve hre), ostatni prava
        leftHanded = model == HeldModel.Bow;
        holder = new GameObject("Weapon").transform;
        holder.SetParent(root.transform, false);
        HeldWeapons.BuildModel(holder, weapon, model, leftHanded ? -1f : 1f);
        grip = HeldWeapons.GripInWeapon(weapon, model);
        if (leftHanded) HeldWeapons.SnapArrowToRest(holder, grip);

        hasForegrip = weapon.twoHanded && !weapon.dualWield && model == HeldModel.Gun;
        if (hasForegrip)
        {
            foregrip = HeldWeapons.ForegripInWeapon(holder, grip, 0.42f, 0.38f);
            supportArm = FirstPersonArms.BuildArm(animator, false);
        }
    }

    // Usadi zbran do ruky (jako HeldWeapons.AttachToBone): v referencni poze se natoci vuci telu a pripevni ke kosti ruky.
    void Attach()
    {
        if (holder == null || animator == null) { Evaluate(); return; }

        holder.SetParent(root.transform, true);
        // jako ve hre: zbran obema rukama s vlastni strelbou hrdiny se srovna vodorovne v prvnim snimku strelby
        bool heroShoot = hasForegrip && hero.animations != null && hero.animations.shoot != null;
        if (attachAtFrame) Sample(CurrentClip, time, curlFingers);
        else if (heroShoot) Sample(hero.animations.shoot, 0f, curlFingers);
        else Sample(SlotClip(0), 0f, curlFingers);
        bool level = attachAtFrame || heroShoot;

        var hand = animator.GetBoneTransform(leftHanded ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var gripBone = animator.GetBoneTransform(leftHanded ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        if (hand != null)
        {
            Vector3 gripWorld = gripBone != null ? gripBone.position : hand.position;
            Quaternion rotation = character.transform.rotation
                * (level && model == HeldModel.Gun ? Quaternion.identity : HeldWeapons.BoneAttachRotation(model, leftHanded ? -1f : 1f));
            holder.rotation = rotation;
            holder.position = gripWorld - rotation * grip;
            holder.SetParent(hand, true);
        }
        Evaluate();
    }

    void Evaluate()
    {
        if (animator == null) return;
        Sample(CurrentClip, time, curlFingers);

        if (leftHandIk && hasForegrip && supportArm != null && holder != null)
            FirstPersonArms.SolveArm(supportArm, holder.TransformPoint(foregrip), holder,
                character.transform.TransformDirection(new Vector3(-0.15f, -1f, -0.35f)), true);

        SceneView.RepaintAll();
    }

    void Sample(AnimationClip clip, float t, bool fingers)
    {
        if (!graph.IsValid() || clip == null) return;
        if (clip != playableClip || !playable.IsValid())
        {
            if (playable.IsValid()) playable.Destroy();
            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(true);
            output.SetSourcePlayable(playable);
            playableClip = clip;
        }
        playable.SetTime(t);
        graph.Evaluate(0f);

        if (!fingers || poseHandler == null) return;
        // sevreni prstu jako CharacterVisual.ApplyGrip (prsty -0,85, palec -0,35)
        poseHandler.GetHumanPose(ref pose);
        for (int i = 0; i < HumanTrait.MuscleCount && i < pose.muscles.Length; i++)
        {
            string name = HumanTrait.MuscleName[i];
            if (!name.Contains("Stretched") || !name.StartsWith(leftHanded ? "Left" : "Right") && !(hasForegrip && name.StartsWith("Left"))) continue;
            if (name.Contains("Thumb")) pose.muscles[i] = -0.35f;
            else if (name.Contains("Index") || name.Contains("Middle") || name.Contains("Ring") || name.Contains("Little")) pose.muscles[i] = -0.85f;
        }
        poseHandler.SetHumanPose(ref pose);
    }

    void HideAll()
    {
        if (root == null) return;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags = HideFlags.DontSave;
    }

    void FrameScene()
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null || root == null) return;
        view.Frame(new Bounds(Spot + Vector3.up * 1f, new Vector3(1.6f, 2f, 1.6f)), false);
    }

    void Clear()
    {
        playing = false;
        if (graph.IsValid()) graph.Destroy();
        poseHandler?.Dispose();
        poseHandler = null;
        playableClip = null;
        if (root != null) DestroyImmediate(root);
        root = null;
        character = null;
        animator = null;
        holder = null;
        supportArm = null;
        hasForegrip = false;
    }

    // ---------------- Scene okno: body uchopu ----------------

    void OnSceneGUI(SceneView view)
    {
        if (holder == null || Event.current.type != EventType.Repaint) return;

        DrawPoint(holder.TransformPoint(grip), Color.green, "Grip");
        if (hasForegrip) DrawPoint(holder.TransformPoint(foregrip), new Color(0.2f, 0.5f, 1f), "Foregrip");
        Handles.color = Color.cyan;
        Handles.DrawLine(holder.position, holder.position + holder.forward * 0.4f, 2f);
    }

    static void DrawPoint(Vector3 point, Color color, string label)
    {
        Handles.color = color;
        Handles.SphereHandleCap(0, point, Quaternion.identity, 0.03f, EventType.Repaint);
        Handles.Label(point + Vector3.up * 0.05f, label);
    }
}
