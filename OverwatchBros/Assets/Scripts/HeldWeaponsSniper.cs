using UnityEngine;

public partial class HeldWeapons
{
    static readonly Vector3 SniperCameraRest = new Vector3(0.21f, -0.205f, 0.51f);
    static readonly Vector3 SniperCameraAngle = new Vector3(-3f, -9f, 4f);

    float sniperBoltStart = -100f;
    float sniperBoltDuration = 1f;
    bool sniperScopedShot;
    float sniperAim, sniperAbilityBlend;

    public bool GhostScoped;

    static bool HasSniperVisual(WeaponDefinition weapon) =>
        weapon != null && weapon.heldPrefab != null && weapon.heldPrefab.TryGetComponent<SniperRifleVisual>(out _);

    float SniperCycle => Mathf.Clamp01((Time.time - sniperBoltStart) / sniperBoltDuration);
    float SniperReload => Mathf.Clamp01(reloadElapsed / reloadDuration);

    // Let the camera return to the optic just before the hands finish settling.
    public bool SniperHandling => HasSniperVisual(CurrentWeapon)
        && (reloadDuration - reloadElapsed > 0.16f || sniperBoltStart + sniperBoltDuration - Time.time > 0.13f);

    public void PlayBoltCycle(float duration)
    {
        if (!HasSniperVisual(CurrentWeapon))
        {
            PlayReload(duration);
            return;
        }

        sniperBoltStart = Time.time;
        sniperBoltDuration = Mathf.Max(0.05f, duration);
        if (!ghost)
            ReplayLog.BoltCycle(this, duration);
    }

    void ResetSniperBolt() => sniperBoltStart = -100f;

    void MoveSniper(float scale, ref Vector3 position, ref Vector3 angles)
    {
        float sinceShot = Time.time - lastShotTime;
        float recoil = SniperRifleVisual.Ease(0f, 0.025f, sinceShot)
            * (1f - SniperRifleVisual.Ease(0.025f, sniperScopedShot ? 0.29f : 0.12f, sinceShot));
        float cycle = SniperRifleVisual.BoltPose(SniperCycle);
        float reload = SniperRifleVisual.ReloadPose(SniperReload);
        position += scale * (new Vector3(0f, 0.017f, -0.074f) * recoil * (sniperScopedShot ? 1f : 0.43f)
            + new Vector3(0.021f, -0.045f, -0.032f) * cycle
            + new Vector3(-0.035f, -0.065f, -0.055f) * reload);
        angles += scale * (new Vector3(-9f, 1.8f, 1f) * recoil * (sniperScopedShot ? 1f : 0.44f)
            + new Vector3(-6f, -7f, -14f) * cycle + new Vector3(-15f, -16f, 32f) * reload);
    }

    void PoseSniperAtCamera(Hand hand, float scopeBlend, ref Vector3 position, ref Vector3 angles)
    {
        float raise = SniperRifleVisual.Ease(0f, 0.41f, scopeBlend);
        Vector3 sight = hand.holder.InverseTransformPoint(hand.sniper.Sight.position);
        position = Vector3.Lerp(position, Vector3.forward * 0.33f - sight, raise);
        angles *= 1f - raise;
        MoveSniper(1f, ref position, ref angles);

        // Bring the working hand and the moving part into the frame during handling.
        position += new Vector3(-0.085f, 0.04f, 0.055f) * SniperRifleVisual.BoltPose(SniperCycle)
            + new Vector3(0f, 0.145f, 0.12f) * SniperRifleVisual.ReloadPose(SniperReload);
    }

    bool AttachSniperToBody(Hand hand)
    {
        if (hand.sniper == null || visual.BodyAnimator == null) return false;
        var spine = visual.GetBone(HumanBodyBones.Spine);
        var mainArm = FirstPersonArms.BuildArm(visual.BodyAnimator, true);
        var supportArm = FirstPersonArms.BuildArm(visual.BodyAnimator, false);
        if (spine == null || mainArm == null || supportArm == null) return false;

        hand.holder.SetParent(spine, true);
        bodyMainArm = mainArm;
        bodySupportArm = supportArm;
        hand.twoHanded = true;
        visual.GripLeft = true;
        return true;
    }

    void PoseSniperOnBody(Hand hand)
    {
        var rifle = hand.sniper;
        rifle.SampleParts(SniperCycle, SniperReload);
        if (bodyMainArm == null || bodySupportArm == null) return;

        bool scoped = ghost ? GhostScoped : fpc != null && fpc.IsSpawned && fpc.scoped.Value;
        float target = scoped && !SniperHandling ? 1f : Time.time - lastShotTime < 0.33f ? 0.63f : 0f;
        sniperAim = Mathf.MoveTowards(sniperAim, target, Time.deltaTime * (target > sniperAim ? 9f : 5f));
        float aim = Mathf.SmoothStep(0f, 1f, sniperAim);
        sniperAbilityBlend = Mathf.MoveTowards(sniperAbilityBlend, visual.ActionPlaying ? 1f : 0f, 12f * Time.deltaTime);

        var body = visual.ModelRoot;
        var shoulder = bodyMainArm.upper.position;
        var offset = Vector3.Lerp(new Vector3(-0.035f, -0.29f, 0.18f), new Vector3(-0.055f, -0.115f, 0.27f), aim);
        float pitch = ghost
            ? ghostFirstPerson && ghostCamera != null ? Mathf.DeltaAngle(0f, ghostCamera.localEulerAngles.x) : 0f
            : shooting != null ? shooting.presentationPitch.Value : 0f;
        var angles = Vector3.Lerp(new Vector3(13f, -44f, 7f),
            new Vector3(Mathf.Clamp(pitch, -55f, 55f), -7f, 0f), aim);
        MoveSniper(0.62f, ref offset, ref angles);
        float reload = SniperRifleVisual.ReloadPose(SniperReload);
        offset += new Vector3(-0.02f, 0.075f, 0.14f) * reload;
        angles.y += 15f * reload;
        var rotation = body.rotation * Quaternion.Euler(angles);
        var grip = shoulder + body.TransformVector(offset);

        // Keep the rifle with the animated right hand when an ability takes over the arms.
        var palm = visual.GetGrip(true);
        if (palm != null && sniperAbilityBlend > 0f)
        {
            grip = Vector3.Lerp(grip, palm.position, sniperAbilityBlend);
            rotation = Quaternion.Slerp(rotation, body.rotation * Quaternion.Euler(28f, -35f, 12f), sniperAbilityBlend);
        }
        hand.holder.SetPositionAndRotation(grip - rotation * hand.grip, rotation);

        SolveSniperArm(bodyMainArm, rifle.TriggerHand(SniperCycle), hand.holder, body, false, 1f - sniperAbilityBlend);
        SolveSniperArm(bodySupportArm, rifle.SupportHand(SniperReload), hand.holder, body, true, 1f - sniperAbilityBlend);
    }

    static void SolveSniperArm(FirstPersonArms.Arm arm, Vector3 target, Transform holder, Transform body, bool support, float weight)
    {
        if (weight <= 0f) return;
        Quaternion upper = arm.upper.localRotation, lower = arm.lower.localRotation, wrist = arm.hand.localRotation;
        FirstPersonArms.SolveArm(arm, target, holder,
            body.TransformDirection(new Vector3(arm.side * 0.65f, -1f, -0.28f)), support);
        arm.upper.localRotation = Quaternion.Slerp(upper, arm.upper.localRotation, weight);
        arm.lower.localRotation = Quaternion.Slerp(lower, arm.lower.localRotation, weight);
        arm.hand.localRotation = Quaternion.Slerp(wrist, arm.hand.localRotation, weight);
    }
}
