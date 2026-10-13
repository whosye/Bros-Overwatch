using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

// Opt-in regression check using the imported meshes and actual humanoid animations.
public static class CharacterHairVerification
{
    [MenuItem("BrosOverwatch/Overit vlasy postav")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run hair verification outside Play Mode.");
        const string output = "Temp/HairBindingQA";
        Directory.CreateDirectory(output);
        var report = new StringBuilder();
        int failed = 0;
        void Check(bool success, string message)
        {
            report.AppendLine((success ? "PASS " : "FAIL ") + message);
            if (!success) failed++;
        }

        var clips = Resources.Load<CharacterAnimSet>("Characters/CharacterAnimations");
        var poses = new[] { clips.idle, clips.walk, clips.jog, clips.sprint, clips.jump, clips.death,
            clips.meleeAttack, clips.shoot, clips.reload, clips.blockPose, clips.dashPose, clips.sitIdle, clips.hitChest };
        foreach (string hero in new[] { "Mirek", "Anna", "Sniper", "Max", "Bard", "Ayran", "Honza", "Viktor" })
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefab/Characters/Char_{hero}.prefab"));
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.position = new Vector3(500, 500, 500);
            var cameraObject = new GameObject("Hair verification camera");
            cameraObject.transform.SetParent(go.transform);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.nearClipPlane = 0.01f; camera.farClipPlane = 5;
            var target = new RenderTexture(64, 64, 24);
            camera.targetTexture = target;
            var graph = PlayableGraph.Create("Hair verification");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try
            {
                var animator = go.GetComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                var restRotation = head.rotation;
                var restPosition = head.position;
                var rest = new Dictionary<SkinnedMeshRenderer, Vector3[]>();
                bool fits = true, bonesValid = true;
                foreach (var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!AssetDatabase.GetAssetPath(skin.sharedMesh).Contains("/Hair/")) continue;
                    rest[skin] = HeadVertices(skin, head);
                    var bounds = new Bounds(); bool first = true;
                    foreach (var vertex in rest[skin])
                    {
                        Vector3 point = go.transform.InverseTransformPoint(head.TransformPoint(vertex));
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                        else bounds.Encapsulate(point);
                    }
                    bool beard = skin.name.Contains("Beard");
                    fits &= beard ? bounds.center.y < 1.68f && bounds.center.z > 0
                        : bounds.max.y > 1.76f && bounds.center.z < 0.06f;
                    foreach (var bone in skin.bones) bonesValid &= bone != null && bone.IsChildOf(go.transform);
                    bonesValid &= skin.rootBone != null && skin.rootBone.IsChildOf(go.transform);
                }
                Check(rest.Count > 0 && fits, hero + ": hair sits on the scalp; beard sits below the eyes");
                Check(bonesValid, hero + ": all hair bones belong to the body rig");

                var animationOutput = AnimationPlayableOutput.Create(graph, "Pose", animator);
                float drift = 0, rotation = 0, movement = 0;
                bool boundsContainHair = true;
                int samples = 0;
                foreach (var clip in poses)
                {
                    if (!clip) { Check(false, hero + ": missing animation"); continue; }
                    var playable = AnimationClipPlayable.Create(graph, clip);
                    animationOutput.SetSourcePlayable(playable);
                    graph.Play();
                    foreach (float fraction in new[] { 0.15f, 0.5f, 0.85f })
                    {
                        playable.SetTime(clip.length * fraction); graph.Evaluate(0);
                        // Render a frame so Unity refreshes its native world bounds after
                        // the manually evaluated pose, as it does in normal gameplay.
                        camera.transform.position = head.position + new Vector3(0, 0.1f, 2);
                        camera.transform.LookAt(head);
                        camera.Render();
                        rotation = Mathf.Max(rotation, Quaternion.Angle(restRotation, head.rotation));
                        movement = Mathf.Max(movement, Vector3.Distance(restPosition, head.position));
                        foreach (var entry in rest)
                        {
                            var actual = HeadVertices(entry.Key, head);
                            for (int i = 0; i < actual.Length; i++)
                            {
                                drift = Mathf.Max(drift, Vector3.Distance(actual[i], entry.Value[i]));
                                boundsContainHair &= entry.Key.bounds.Contains(head.TransformPoint(actual[i]));
                            }
                        }
                        samples++;
                    }
                    animationOutput.SetSourcePlayable(Playable.Null);
                    graph.DestroyPlayable(playable);
                }
                Check(samples == 39 && rotation > 20 && movement > 0.1f, hero + ": 39 moving poses across 13 gameplay animations evaluated");
                Check(drift < 0.025f, hero + ": hair follows the animated head (maximum local drift " + drift.ToString("F5") + " m)");
                Check(boundsContainHair, hero + ": culling bounds contain the hair in every animated pose");
            }
            finally { graph.Destroy(); Object.DestroyImmediate(go); Object.DestroyImmediate(target); }
        }
        File.WriteAllText(output + "/verification.txt", report.ToString());
        if (failed > 0) throw new Exception("Hair verification: " + failed + " failed checks. See " + output + "/verification.txt");
        Debug.Log("[CharacterHairVerification] All checks passed.");
    }

    static Vector3[] HeadVertices(SkinnedMeshRenderer skin, Transform head)
    {
        var mesh = new Mesh();
        try
        {
            skin.BakeMesh(mesh);
            var vertices = mesh.vertices;
            var matrix = head.worldToLocalMatrix * skin.transform.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            return vertices;
        }
        finally { Object.DestroyImmediate(mesh); }
    }
}
