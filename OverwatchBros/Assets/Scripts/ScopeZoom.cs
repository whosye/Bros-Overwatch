using UnityEngine;
using UnityEngine.InputSystem;

// Zamerovaci dalekohled: drzenim praveho tlacitka se obraz priblizi na zorne pole zbrane (scopeFov)
// a citlivost mysi se umerne snizi. Funguje jen u zbrani s dalekohledem a kdyz prave tlacitko nema jinou schopnost.
public class ScopeZoom : MonoBehaviour
{
    const float ZoomSpeed = 20f;

    FirstPersonController fpc;
    PlayerHero hero;
    float baseFov = -1f;

    public bool Zoomed { get; private set; }
    public float ScopeBlend { get; private set; }
    public float BaseFov => baseFov > 0f ? baseFov : 60f;

    public static float BlendForFov(float fov, float normalFov, float scopeFov)
    {
        return scopeFov > 0f && normalFov > scopeFov
            ? Mathf.Clamp01((normalFov - fov) / (normalFov - scopeFov)) : 0f;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
    }

    void Update()
    {
        // bot: pribliseni jen jako stav (nabijeni rany, pomalejsi chuze), bez zmeny kamery
        if (fpc != null && fpc.Bot != null && hero != null && fpc.IsSpawned && fpc.IsOwner)
        {
            var botDefinition = hero.Hero;
            bool canScope = botDefinition != null && botDefinition.weapon != null && botDefinition.rmbAbility == null
                && botDefinition.blockAbility == null && botDefinition.weapon.scopeFov > 0f;
            Zoomed = canScope && !fpc.ShootBlocked && fpc.Bot.IsHeld(HeroInput.Key.RightMouse);
            fpc.ScopeSpeedScale = Zoomed && botDefinition.weapon.HasScopedShot ? botDefinition.weapon.scopedMoveSpeed : 1f;
            ScopeBlend = 0f;
            return;
        }

        if (fpc == null || hero == null || fpc.playerCamera == null || !fpc.IsSpawned || !fpc.IsOwner || fpc.Bot != null)
        {
            Zoomed = false;
            ScopeBlend = 0f;
            return;
        }

        var cam = fpc.playerCamera;
        if (baseFov < 0f)
            baseFov = cam.fieldOfView;

        var definition = hero.Hero;
        float scopeFov = definition != null && definition.weapon != null && definition.rmbAbility == null && definition.blockAbility == null
            ? definition.weapon.scopeFov : 0f;

        Zoomed = scopeFov > 0f && GameSettings.CursorLocked && !fpc.ShootBlocked && !fpc.ThirdPerson
            && !ReplayPlayer.Active && !PotgIntro.Active
            && Mouse.current != null && Mouse.current.rightButton.isPressed;

        // Sniper pri nabijeni rany chodi pomaleji.
        fpc.ScopeSpeedScale = Zoomed && definition.weapon.HasScopedShot ? definition.weapon.scopedMoveSpeed : 1f;

        float targetFov = Zoomed ? scopeFov : baseFov;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-ZoomSpeed * Time.deltaTime));
        fpc.LookScale = cam.fieldOfView / baseFov;
        ScopeBlend = definition != null && definition.weapon != null && definition.weapon.HasScopedShot
            ? BlendForFov(cam.fieldOfView, baseFov, scopeFov) : 0f;
    }

    void OnDisable()
    {
        Zoomed = false;
        ScopeBlend = 0f;
        if (fpc == null) return;
        fpc.ScopeSpeedScale = 1f;
        fpc.LookScale = 1f;
        if (fpc.IsOwner && fpc.Bot == null && fpc.playerCamera != null && baseFov > 0f)
            fpc.playerCamera.fieldOfView = baseFov;
    }
}
