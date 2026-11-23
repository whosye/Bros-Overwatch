using UnityEngine;

public partial class HeldWeapons
{
    bool UsesRevolver => builtWeapon != null && builtWeapon.revolverReload;
    float revolverPitch;

    void UpdateRevolverParts(Hand hand)
    {
        if (!UsesRevolver || hand.holder == null) return;
        if (hand.revolver == null) hand.revolver = hand.holder.GetComponentInChildren<RevolverVisual>(true);
        if (hand.revolver != null)
            hand.revolver.SampleReload(reloadElapsed < reloadDuration ? reloadElapsed / reloadDuration : 1f);
    }

    void UpdateBodyRevolver()
    {
        if (!UsesRevolver || visual == null || visual.ModelRoot == null || bodyMainArm == null) return;
        var body = visual.ModelRoot;
        float pitch = ghost ? (ghostFirstPerson && ghostCamera != null ? Mathf.DeltaAngle(0f, ghostCamera.localEulerAngles.x) : 0f)
            : shooting != null && shooting.IsOwner && shooting.playerCamera != null
                ? Mathf.DeltaAngle(0f, shooting.playerCamera.transform.localEulerAngles.x)
                : shooting != null ? shooting.presentationPitch.Value : 0f;
        revolverPitch = Mathf.LerpAngle(revolverPitch, pitch, 1f - Mathf.Exp(-18f * Time.deltaTime));
        foreach (var hand in bodyHands)
        {
            if (hand.holder == null || !hand.holder.gameObject.activeInHierarchy) continue;
            var palm = visual.GetGrip(true);
            var wrist = visual.GetHand(true);
            if (palm == null || wrist == null) continue;
            bool reloading = reloadElapsed < reloadDuration;
            float reloadBlend = reloading ? RevolverVisual.ReloadBlend(reloadElapsed / reloadDuration) : 0f;
            if (reloadBlend > 0f)
            {
                // Both hands work in front of the torso. Keep the authored wrist flexion;
                // solve only shoulder/elbow positions, never impose a camera-space wrist frame.
                Vector3 target = bodyMainArm.upper.position + body.TransformVector(new Vector3(-.06f, -.20f, .43f));
                PlaceRevolverArm(bodyMainArm, target, body.TransformDirection(new Vector3(.8f, -.4f, .35f)), reloadBlend);
            }
            else if (throwElapsed >= ThrowDuration && tauntElapsed >= TauntDuration && !visual.UltimateActive)
            {
                Quaternion natural = wrist.rotation * hand.idleRotation;
                Vector3 forearm = (wrist.position - bodyMainArm.lower.position).normalized;
                Vector3 aim = body.rotation * Quaternion.Euler(revolverPitch, 0f, 0f) * Vector3.forward;
                // Raised arms can aim as one unit. A relaxed arm at the hip stays relaxed,
                // instead of bending its wrist backwards to keep the gun horizontal.
                if (Vector3.Dot(forearm, body.forward) > .25f && Vector3.Angle(natural * Vector3.forward, aim) < 75f)
                {
                    Quaternion turn = Quaternion.RotateTowards(Quaternion.identity,
                        Quaternion.FromToRotation(natural * Vector3.forward, aim), 55f);
                    bodyMainArm.upper.rotation = turn * bodyMainArm.upper.rotation;
                }
            }
            Quaternion rotation = wrist.rotation * hand.idleRotation;
            hand.holder.SetPositionAndRotation(palm.position - rotation * hand.grip, rotation);
            UpdateRevolverParts(hand);
            if (reloadBlend > 0f && hand.revolver != null && bodySupportArm != null)
            {
                Vector3 supportWrist = hand.revolver.SupportPoint.position - body.forward * .055f;
                // The working palm may pass the centreline, but the elbow stays outside
                // the left side and in front of the chest throughout the approach.
                Vector3 local = body.InverseTransformPoint(supportWrist);
                local.z = Mathf.Max(local.z, .38f);
                supportWrist = body.TransformPoint(local);
                PlaceRevolverArm(bodySupportArm, supportWrist,
                    body.TransformDirection(new Vector3(-1f, -.25f, .65f)), reloadBlend);
            }
        }
    }

    static void PlaceRevolverArm(FirstPersonArms.Arm arm, Vector3 destination, Vector3 pole, float blend)
    {
        if (arm == null) return;
        Vector3 start = arm.upper.position;
        Vector3 target = Vector3.Lerp(arm.hand.position, destination, blend);
        Quaternion wristLocal = arm.hand.localRotation;
        Vector3 delta = target - start;
        float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(arm.upperLength - arm.lowerLength) + .01f,
            (arm.upperLength + arm.lowerLength) * .94f);
        Vector3 forward = delta.normalized;
        Vector3 bend = Vector3.ProjectOnPlane(pole, forward).normalized;
        if (bend.sqrMagnitude < .01f) bend = Vector3.ProjectOnPlane(Vector3.forward, forward).normalized;
        float along = (arm.upperLength * arm.upperLength - arm.lowerLength * arm.lowerLength + distance * distance) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0f, arm.upperLength * arm.upperLength - along * along));
        Vector3 elbow = start + forward * along + bend * height;
        arm.upper.rotation = Quaternion.FromToRotation(arm.lower.position - start, elbow - start) * arm.upper.rotation;
        Vector3 end = start + forward * distance;
        arm.lower.rotation = Quaternion.FromToRotation(arm.hand.position - arm.lower.position, end - arm.lower.position) * arm.lower.rotation;
        arm.hand.localRotation = wristLocal;
    }
}
