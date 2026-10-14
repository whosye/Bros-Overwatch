using UnityEngine;

// Ruce v pohledu z prvni osoby: druha kopie modelu postavy u kamery (bez hlavy), jejiz paze se kazdy snimek
// natahnou ke zbranim (dvoukloubova IK) a prsty jsou sevrene v pest. Funguje pro kazdy Humanoid model.
public class FirstPersonArms
{
    public class Arm
    {
        public Transform upper, lower, hand;
        public float upperLength, lowerLength;
        public Quaternion handFrameLocal;
        public float side;
        public Vector3 upperRestLocal;   // vychozi poloha ramene (pro posunuti ramene, kdyz paze nedosahne)
        public Quaternion upperRest, lowerRest, handRest;   // vychozi natoceni (paze mimo obraz)
    }

    GameObject rig;
    readonly Arm[] arms = new Arm[2];

    // Stred dlane vuci zapesti v souradnicich ruky (z = smer prstu, y = hrbet ruky).
    static readonly Vector3 PalmOffset = new Vector3(0f, -0.03f, 0.085f);

    public static FirstPersonArms Create(Transform cameraRoot, GameObject characterPrefab)
    {
        if (characterPrefab == null) return null;

        var rig = Object.Instantiate(characterPrefab, cameraRoot);
        rig.name = "FpArms";
        rig.transform.localPosition = Vector3.zero;
        rig.transform.localRotation = Quaternion.identity;

        var animator = rig.GetComponent<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            Object.Destroy(rig);
            return null;
        }

        animator.runtimeAnimatorController = null;
        animator.Rebind();

        var result = new FirstPersonArms { rig = rig };
        result.arms[0] = BuildArm(animator, true);
        result.arms[1] = BuildArm(animator, false);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (result.arms[0] == null || result.arms[1] == null || head == null)
        {
            Object.Destroy(rig);
            return null;
        }

        CurlFingers(animator, rig.transform);

        // Hlava (a vse nad rameny) se nekresli; ramena jsou kousek pod kamerou a pred ni.
        float shoulderY = result.arms[0].upper.position.y;
        foreach (var renderer in rig.GetComponentsInChildren<Renderer>())
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (renderer.bounds.center.y > shoulderY + 0.05f)
                renderer.enabled = false;

            if (renderer is SkinnedMeshRenderer skinned)
                skinned.updateWhenOffscreen = true;
        }

        foreach (var collider in rig.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(collider);

        // jen paze (po skryti casti nad rameny - orezany model ma stred u ramen)
        TrimToArms(rig, result.arms[0].upper, result.arms[1].upper);

        Vector3 headLocal = rig.transform.InverseTransformPoint(head.position);
        rig.transform.localPosition = -headLocal + new Vector3(0f, -0.10f, 0.10f);

        animator.enabled = false;
        return result;
    }

    public static Arm BuildArm(Animator animator, bool right)
    {
        var upper = animator.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
        var lower = animator.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
        var hand = animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        var middle = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        var index = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
        var little = animator.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
        if (upper == null || lower == null || hand == null) return null;

        // Osy ruky z geometrie: smer prstu a hrbet ruky (kolmo na dlan).
        Vector3 fingers = middle != null ? (middle.position - hand.position).normalized : (hand.position - lower.position).normalized;
        Vector3 back = Vector3.up;
        if (index != null && little != null)
        {
            Vector3 across = (index.position - little.position).normalized;
            back = right ? Vector3.Cross(across, fingers) : Vector3.Cross(fingers, across);
        }

        return new Arm
        {
            upper = upper,
            lower = lower,
            hand = hand,
            side = right ? 1f : -1f,
            upperLength = Vector3.Distance(upper.position, lower.position),
            lowerLength = Vector3.Distance(lower.position, hand.position),
            handFrameLocal = Quaternion.Inverse(hand.rotation) * Quaternion.LookRotation(fingers, back.normalized),
            upperRestLocal = upper.localPosition,
            upperRest = upper.localRotation,
            lowerRest = lower.localRotation,
            handRest = hand.localRotation,
        };
    }

    // Z modelu postavy u kamery nechat jen paze (od ramene po prsty): trup, ramena, limec a vlasy by byly tesne
    // u kamery videt jako "cary" (a pri posunuti ramene by se roztahovaly). Trojuhelnik zustane, kdyz vsechny jeho
    // vrcholy patri vic nez z poloviny kostem paze. Jen u citelnych modelu (Read/Write), jinak se nic nemeni.
    static void TrimToArms(GameObject rig, Transform rightUpper, Transform leftUpper)
    {
        foreach (var skinned in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = skinned.sharedMesh;
            var bones = skinned.bones;
            if (mesh == null || !mesh.isReadable || bones == null || bones.Length == 0) continue;

            var armBone = new bool[bones.Length];
            bool anyArm = false;
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                armBone[i] = bone != null && (bone.IsChildOf(rightUpper) || bone.IsChildOf(leftUpper));
                anyArm |= armBone[i];
            }
            if (!anyArm)
            {
                skinned.enabled = false;   // cast bez pazi (napr. kalhoty, boty)
                continue;
            }

            var weights = mesh.boneWeights;
            if (weights == null || weights.Length != mesh.vertexCount) continue;
            var onArm = new bool[weights.Length];
            for (int v = 0; v < weights.Length; v++)
            {
                var w = weights[v];
                float arm = (armBone[w.boneIndex0] ? w.weight0 : 0f) + (armBone[w.boneIndex1] ? w.weight1 : 0f)
                    + (armBone[w.boneIndex2] ? w.weight2 : 0f) + (armBone[w.boneIndex3] ? w.weight3 : 0f);
                onArm[v] = arm > 0.5f;
            }

            var trimmed = Object.Instantiate(mesh);
            trimmed.name = mesh.name + "_Arms";
            bool kept = false;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var triangles = mesh.GetTriangles(sub);
                var keep = new System.Collections.Generic.List<int>(triangles.Length);
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    if (!onArm[triangles[t]] || !onArm[triangles[t + 1]] || !onArm[triangles[t + 2]]) continue;
                    keep.Add(triangles[t]);
                    keep.Add(triangles[t + 1]);
                    keep.Add(triangles[t + 2]);
                }
                trimmed.SetTriangles(keep, sub);
                kept |= keep.Count > 0;
            }
            if (kept) skinned.sharedMesh = trimmed;
            else
            {
                Object.Destroy(trimmed);
                skinned.enabled = false;
            }
        }

        // pevne (neskinovane) casti: jen to, co je pripojene k pazi (napr. hodinky), zbytek pryc
        foreach (var renderer in rig.GetComponentsInChildren<MeshRenderer>(true))
            if (!renderer.transform.IsChildOf(rightUpper) && !renderer.transform.IsChildOf(leftUpper))
                renderer.enabled = false;
    }

    static void CurlFingers(Animator animator, Transform root)
    {
        var handler = new HumanPoseHandler(animator.avatar, root);
        var pose = new HumanPose();
        handler.GetHumanPose(ref pose);

        for (int i = 0; i < HumanTrait.MuscleCount && i < pose.muscles.Length; i++)
        {
            string name = HumanTrait.MuscleName[i];
            if (!name.Contains("Stretched")) continue;

            if (name.Contains("Thumb"))
                pose.muscles[i] = -0.35f;
            else if (name.Contains("Index") || name.Contains("Middle") || name.Contains("Ring") || name.Contains("Little"))
                pose.muscles[i] = -0.85f;
        }

        handler.SetHumanPose(ref pose);
        handler.Dispose();
    }

    // Natahne pazi tak, aby dlan drzela zbran v miste 'grip' (svetove souradnice); orientace podle drzaku zbrane.
    // underhand: ruka podpira hlaven zespodu (dlan nahoru, hrbet ruky dolu) - predpazbi u zbrane obema rukama.
    public void Solve(bool right, Vector3 grip, Transform holder, Transform cameraRoot, bool underhand = false, bool stretch = false)
    {
        var arm = arms[right ? 0 : 1];
        if (arm == null || rig == null) return;
        // Loket miri dolu a ven od tela.
        // (v prvni osobe je rameno mimo obraz - kdyz paze na zbran nedosahne, rameno se k ni posune)
        SolveArm(arm, grip, holder, cameraRoot.TransformDirection(new Vector3(0.7f * arm.side, -1f, -0.3f)), underhand, underhand || stretch);
    }

    // Vrati paze do vychozi pozy (mimo obraz) - napr. prava ruka luku, kdyz se zrovna nenatahuje.
    public void ResetArm(bool right)
    {
        var arm = arms[right ? 0 : 1];
        if (arm == null || rig == null) return;
        arm.upper.localPosition = arm.upperRestLocal;
        arm.upper.localRotation = arm.upperRest;
        arm.lower.localRotation = arm.lowerRest;
        arm.hand.localRotation = arm.handRest;
    }

    // Dvoukloubova IK jedne paze (i paze postavy ve 3. osobe): dlan na 'grip', loket smerem 'pole'.
    public static void SolveArm(Arm arm, Vector3 grip, Transform holder, Vector3 pole, bool underhand, bool stretch = false)
    {
        if (arm == null || arm.upper == null) return;
        if (stretch) arm.upper.localPosition = arm.upperRestLocal;

        // Prsty miri dopredu pres rukojet, dlan je privracena k rukojeti, palec nahoru.
        // (zespodu: prsty sikmo dopredu a ke zbrani, hrbet ruky dolu)
        Quaternion handFrame = underhand
            ? Quaternion.LookRotation((holder.forward - holder.right * arm.side * 0.35f).normalized, -holder.up)
            : Quaternion.LookRotation(holder.forward, holder.right * arm.side);
        Vector3 wrist = grip - handFrame * PalmOffset;

        Vector3 shoulder = arm.upper.position;
        Vector3 toTarget = wrist - shoulder;
        float reach = arm.upperLength + arm.lowerLength - 0.002f;
        if (stretch && toTarget.magnitude > reach * 0.97f)
        {
            // posunout rameno k cili (lehce pokrcena paze vypada prirozeneji nez napnuta)
            arm.upper.position = shoulder + toTarget.normalized * (toTarget.magnitude - reach * 0.97f);
            shoulder = arm.upper.position;
            toTarget = wrist - shoulder;
        }
        float distance = Mathf.Clamp(toTarget.magnitude, 0.05f, reach);
        Vector3 direction = toTarget.normalized;
        Vector3 target = shoulder + direction * distance;

        Vector3 poleOnPlane = (pole - direction * Vector3.Dot(pole, direction)).normalized;

        float along = (arm.upperLength * arm.upperLength - arm.lowerLength * arm.lowerLength + distance * distance) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0f, arm.upperLength * arm.upperLength - along * along));
        Vector3 elbow = shoulder + direction * along + poleOnPlane * height;

        arm.upper.rotation = Quaternion.FromToRotation(arm.lower.position - arm.upper.position, elbow - shoulder) * arm.upper.rotation;
        arm.lower.rotation = Quaternion.FromToRotation(arm.hand.position - arm.lower.position, target - arm.lower.position) * arm.lower.rotation;
        arm.hand.rotation = handFrame * Quaternion.Inverse(arm.handFrameLocal);
    }
}
