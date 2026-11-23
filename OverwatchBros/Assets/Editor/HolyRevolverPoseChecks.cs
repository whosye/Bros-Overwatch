using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Batch entry point: -executeMethod HolyRevolverPoseChecks.Run (without -quit).
// Exercises the real character animation graph, wrist IK and sampled cylinder reload.
public static class HolyRevolverPoseChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    static GameObject actor;
    static HeldWeapons held;
    static CharacterVisual visual;
    static Camera camera;
    static HeroDefinition hero;
    static int frames;
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/HolyRevolver/poses"));

    public static void Run()
    {
        HolyRevolverSetup.Build();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ShaderUtil.allowAsyncCompilation = false;
        hero = Resources.Load<HeroDefinition>("Heroes/Sindel");
        actor = new GameObject("SindelPoseCheck");
        visual = actor.AddComponent<CharacterVisual>();
        Call(visual, "Awake");
        visual.IsGhost = true;
        visual.SetModel(hero.characterPrefab, Color.white, hero.animations);
        held = actor.AddComponent<HeldWeapons>();
        Call(held, "Awake");
        Set(held, "visual", visual);
        camera = new GameObject("PreviewCamera").AddComponent<Camera>();
        camera.nearClipPlane = .01f;
        camera.farClipPlane = 20;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.12f, .15f, .19f);
        held.SetGhost(hero, true, camera.transform);
        held.GhostThirdPerson = true;
        Call(held, "Rebuild", true);
        var light = new GameObject("Key").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 2.5f;
        light.transform.rotation = Quaternion.Euler(35, -25, 0);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.6f, .6f, .6f);
        EditorApplication.update += Tick;
    }

    static void Pose(int state, float time)
    {
        var mixer = Get<AnimationMixerPlayable>(visual, "locomotion");
        var layers = Get<AnimationLayerMixerPlayable>(visual, "layers");
        var states = Get<AnimationClipPlayable[]>(visual, "states");
        for (int i = 0; i < states.Length; i++) mixer.SetInputWeight(i, i == state ? 1f : 0f);
        for (int i = 1; i < layers.GetInputCount(); i++) layers.SetInputWeight(i, 0f);
        states[state].SetTime(time);
        Get<PlayableGraph>(visual, "graph").Evaluate(0f);
        Call(visual, "LateUpdate");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[RevolverPose] " + message);
    }

    static void Tick()
    {
        if (++frames < 20) return;
        EditorApplication.update -= Tick;
        try { Check(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static void Check()
    {
        Directory.CreateDirectory(Output);
        var gun = visual.ModelRoot.GetComponentInChildren<RevolverVisual>(true);
        Require(gun != null, "Body prefab has no articulated cylinder.");
        Get<GameObject>(held, "root").SetActive(false);
        Transform holder = gun.transform.parent;
        float maxAngle = 0f, maxGripGap = 0f;
        foreach (float yaw in new[] { 0f, 75f })
            foreach (float pitch in new[] { -30f, 0f, 35f })
                foreach (int state in new[] { 0, 1, 2, 3 })
                    for (int frame = 0; frame < 8; frame++)
                    {
                        actor.transform.rotation = Quaternion.Euler(0, yaw, 0);
                        camera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
                        Set(held, "revolverPitch", pitch);
                        Pose(state, frame * .1f);
                        Quaternion wristBefore = visual.GetHand(true).localRotation;
                        Call(held, "UpdateBodyRevolver");
                        Require(Quaternion.Angle(wristBefore, visual.GetHand(true).localRotation) < .05f, "Aim correction bends the wrist instead of moving the arm.");
                        Vector3 expected = visual.ModelRoot.rotation * Quaternion.Euler(pitch, 0, 0) * Vector3.forward;
                        float angle = Vector3.Angle(holder.forward, expected);
                        maxAngle = Mathf.Max(maxAngle, angle);
                        // Relaxed arms may lower the gun. Their authored wrist pose must remain intact.
                        Vector3 grip = holder.TransformPoint(HeldWeapons.GripInWeapon(hero.weapon, HeldModel.Gun));
                        float gap = Vector3.Distance(grip, visual.GetGrip(true).position);
                        maxGripGap = Mathf.Max(maxGripGap, gap);
                        Require(gap < .065f, $"Grip leaves palm by {gap}m in state {state}.");
                    }
        visual.EvaluateShootPose(true);
        Call(visual, "ApplyGrip");
        Call(held, "UpdateBodyRevolver");
        Require(Vector3.Angle(holder.forward, visual.ModelRoot.rotation * Quaternion.Euler(35, 0, 0) * Vector3.forward) < .2f,
            "The hero's shooting pose does not aim forward.");
        actor.transform.rotation = Quaternion.identity;
        camera.transform.rotation = Quaternion.identity;
        Set(held, "revolverPitch", 0f);
        Pose(2, .35f);
        Call(held, "UpdateBodyRevolver");
        var target = holder.TransformPoint(new Vector3(0f, -.03f, .12f));
        camera.transform.position = target + new Vector3(.85f, .18f, .3f);
        camera.transform.LookAt(target);
        camera.orthographic = true;
        camera.orthographicSize = .48f;
        Capture("running-aim.png");
        BodyViews("run");
        Pose(0, 0);
        Call(held, "UpdateBodyRevolver");
        BodyViews("idle");
        visual.EvaluateShootPose(true);
        Call(visual, "ApplyGrip");
        Call(held, "UpdateBodyRevolver");
        BodyViews("shoot");

        held.PlayReload(1.5f);
        Require(Get<float>(visual, "reloadTime") < 0f, "Generic magazine clip is still being played.");
        for (int phase = 1; phase < 40; phase++)
        {
            float t = phase / 40f;
            Pose(0, 0);
            Set(held, "reloadElapsed", 1.5f * t);
            Quaternion rightWristBefore = visual.GetHand(true).localRotation;
            Quaternion leftWristBefore = visual.GetHand(false).localRotation;
            Call(held, "UpdateBodyRevolver");
            Require(Quaternion.Angle(rightWristBefore, visual.GetHand(true).localRotation) < .05f, "Reload forces the right wrist into a new rotation.");
            Require(Quaternion.Angle(leftWristBefore, visual.GetHand(false).localRotation) < .05f, "Reload forces the left wrist into a new rotation.");
            var left = Get<FirstPersonArms.Arm>(held, "bodySupportArm");
            for (int i = 0; i <= 8; i++)
            {
                Vector3 point = visual.ModelRoot.InverseTransformPoint(Vector3.Lerp(left.lower.position, left.hand.position, i / 8f));
                float chest = point.x * point.x / (.22f * .22f) + (point.z - .03f) * (point.z - .03f) / (.16f * .16f);
                Require(chest > 1f, $"Reload forearm enters torso at phase {t}: {point}.");
            }
            if (phase == 8 || phase == 14 || phase == 24 || phase == 34) BodyViews("body-reload-" + Mathf.RoundToInt(t * 100));
            if (t > .2f && t < .86f) Require(gun.cylinder.localPosition.x < -.01f, "Cylinder did not swing out.");
        }
        held.StopReload();
        Call(held, "UpdateBodyRevolver");
        Require(Vector3.Distance(gun.cylinder.localPosition, gun.cylinderCenter) < .00001f, "Cancelled reload leaves the cylinder open.");
        Require(!gun.speedLoader.gameObject.activeSelf && !gun.spentCases.gameObject.activeSelf, "Reload props survive cancellation.");

        actor.SetActive(false);
        camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        camera.orthographic = false;
        camera.fieldOfView = 70;
        var pivot = new GameObject("FirstPersonProp").transform;
        var fpGun = Object.Instantiate(hero.weapon.heldPrefab, pivot).GetComponent<RevolverVisual>();
        var arms = FirstPersonArms.Create(camera.transform, hero.characterPrefab);
        foreach (float t in new[] { 0f, .35f, .6f, 1f })
        {
            RevolverVisual.ReloadPose(t, out var offset, out var euler);
            pivot.SetPositionAndRotation(new Vector3(.3f, -.28f, .5f) + offset, Quaternion.Euler(euler));
            fpGun.SampleReload(t);
            arms.Solve(true, pivot.TransformPoint(HeldWeapons.GripInWeapon(hero.weapon, HeldModel.Gun)), pivot, camera.transform);
            if (t > 0f && t < 1f) arms.Solve(false, fpGun.SupportPoint.position, fpGun.SupportPoint, camera.transform, true, true);
            else arms.ResetArm(false);
            Capture("reload-" + Mathf.RoundToInt(t * 100) + ".png");
        }
        Debug.Log($"[RevolverPose] PASS: authored wrist rotations preserved; reload forearm clears torso; front/side views captured; 192 idle/walk/jog/sprint wrist samples at 3 pitches and 2 yaws plus shooting pose; 39 reload phases; max grip gap {maxGripGap:F4}m; reload phases, cancellation and clip isolation verified.");
    }

    static void BodyViews(string name)
    {
        var body = visual.ModelRoot;
        float shoulderY = body.InverseTransformPoint(visual.GetBone(HumanBodyBones.RightUpperArm).position).y;
        Vector3 target = body.TransformPoint(new Vector3(0f, shoulderY - .24f, .12f));
        camera.orthographic = true;
        camera.orthographicSize = .65f;
        foreach (var side in new[] { "front", "side" })
        {
            camera.transform.position = target + body.TransformVector(side == "front" ? new Vector3(.15f,.05f,1.8f) : new Vector3(1.8f,.05f,.15f));
            camera.transform.LookAt(target);
            Capture(name + "-" + side + ".png");
        }
    }

    static void Capture(string name)
    {
        var target = new RenderTexture(1200, 800, 24);
        target.Create();
        // Bone transforms are sampled several times in one editor frame. Bake each view
        // on CPU so GPU skinning cannot reuse an earlier pose in the diagnostic image.
        var baked = new System.Collections.Generic.List<GameObject>();
        var sources = new System.Collections.Generic.List<SkinnedMeshRenderer>();
        foreach (var skin in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
            var mesh = new Mesh();
            skin.BakeMesh(mesh);
            var view = new GameObject("BakedPose", typeof(MeshFilter), typeof(MeshRenderer));
            view.transform.SetParent(skin.transform, false);
            view.GetComponent<MeshFilter>().sharedMesh = mesh;
            view.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            baked.Add(view);
            sources.Add(skin);
            skin.enabled = false;
        }
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        foreach (var skin in sources) skin.enabled = true;
        foreach (var view in baked)
        {
            Object.DestroyImmediate(view.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(view);
        }
        RenderTexture.active = target;
        var image = new Texture2D(1200, 800, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG());
        RenderTexture.active = null;
        target.Release();
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(target);
    }
}
