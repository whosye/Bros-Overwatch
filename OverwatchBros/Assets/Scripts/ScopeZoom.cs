using UnityEngine;
using UnityEngine.InputSystem;

// Zamerovaci dalekohled (Anna): drzenim praveho tlacitka se obraz priblizi na zorne pole zbrane (scopeFov)
// a citlivost mysi se umerne snizi. Funguje jen u zbrani s dalekohledem a kdyz prave tlacitko nema jinou schopnost.
public class ScopeZoom : MonoBehaviour
{
    const float ZoomSpeed = 10f;

    FirstPersonController fpc;
    PlayerHero hero;
    float baseFov = -1f;

    public bool Zoomed { get; private set; }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
    }

    void Update()
    {
        if (fpc == null || hero == null || fpc.playerCamera == null || !fpc.IsOwner) return;

        var cam = fpc.playerCamera;
        if (baseFov < 0f)
            baseFov = cam.fieldOfView;

        var definition = hero.Hero;
        float scopeFov = definition != null && definition.weapon != null && definition.rmbAbility == null && definition.blockAbility == null
            ? definition.weapon.scopeFov : 0f;

        Zoomed = scopeFov > 0f && GameSettings.CursorLocked && !fpc.CannotAct
            && Mouse.current != null && Mouse.current.rightButton.isPressed;

        // Sniper pri nabijeni rany chodi pomaleji.
        fpc.ScopeSpeedScale = Zoomed && definition.weapon.HasScopedShot ? definition.weapon.scopedMoveSpeed : 1f;

        float targetFov = Zoomed ? scopeFov : baseFov;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-ZoomSpeed * Time.deltaTime));
        fpc.LookScale = cam.fieldOfView / baseFov;
    }
}
