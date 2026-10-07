using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Sniperovo odkopnuti (E): rychly kop pred sebe. Nepratele v dosahu 'range' m (v kuzelu pred hracem) zrani o 'power',
// odhodi je (sila 'knockback', asi 6 m) a na 'duration' sekund zpomali na polovinu. Zachrana na blizko:
// odkopne dotireneho rvace, aby sniper mel cas utect hakem.
// Animace: z pohledu prvni osoby vykopne do obrazu noha s botou, ostatni vidi kop na modelu postavy.
public class KickAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    public const float KickSeconds = 0.42f;
    const float SlowFactor = 0.5f;

    FirstPersonController fpc;
    float nextUseTime;

    public float CooldownRemaining => Mathf.Max(0f, nextUseTime - Time.time);

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
    }

    void Update()
    {
        if (!IsOwner || ability == null) return;
        if (!Keyboard.current.eKey.wasPressedThisFrame || !GameSettings.CursorLocked) return;
        if (fpc.InputBlocked || Time.time < nextUseTime) return;

        nextUseTime = Time.time + ability.Cooldown;

        FirstPersonKick.Play(fpc.playerCamera);
        PlayBodyKick();
        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.6f);
        GetComponent<PlayerHero>().SayAbility(ability);

        var eye = fpc.playerCamera.transform;
        KickServerRpc(transform.position, eye.forward);
    }

    void PlayBodyKick()
    {
        var visual = GetComponent<CharacterVisual>();
        if (visual != null)
            visual.PlayKick(KickSeconds);
    }

    // ---------------- server ----------------

    [ServerRpc]
    void KickServerRpc(Vector3 origin, Vector3 forward)
    {
        KickClientRpc();

        var match = MatchManager.Instance;
        if (ability == null || (match != null && (match.IsOver || match.IsLobby))) return;

        Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
        flat = flat.sqrMagnitude > 0.01f ? flat.normalized : transform.forward;

        // Kop dosahne kus pred hrace (kuzel cca 70 stupnu), zdmi neprochazi.
        Vector3 center = origin + Vector3.up * 1.0f;
        var hit = new HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(center + flat * (ability.range * 0.5f), ability.range * 0.75f, ~0, QueryTriggerInteraction.Ignore))
        {
            var victim = col.GetComponentInParent<Health>();
            if (victim == null || !hit.Add(victim) || victim.currentHealth.Value <= 0f) continue;
            if (victim.gameObject == gameObject || Combat.SameTeam(gameObject, victim.gameObject)) continue;

            Vector3 to = Vector3.ProjectOnPlane(victim.transform.position - origin, Vector3.up);
            if (to.magnitude > ability.range + 0.6f) continue;
            if (to.sqrMagnitude > 0.04f && Vector3.Angle(flat, to) > 35f) continue;
            if (!Combat.HasLineOfSight(center, col)) continue;

            Combat.DamagePlayer(gameObject, victim, ability.power);
            if (victim.currentHealth.Value <= 0f) continue;

            var controller = victim.GetComponent<FirstPersonController>();
            if (controller != null)
            {
                controller.ServerKnockback((flat + Vector3.up * 0.35f).normalized * ability.knockback);
                controller.ServerSlow(ability.duration, SlowFactor);
            }

            var victimHero = victim.GetComponent<PlayerHero>();
            if (victimHero != null)
                victimHero.ServerNoteAttacker(gameObject);

            KickHitClientRpc(victim.transform.position + Vector3.up * 1.1f);
        }
    }

    [ClientRpc]
    void KickClientRpc()
    {
        if (!IsOwner)
            PlayBodyKick();
    }

    [ClientRpc]
    void KickHitClientRpc(Vector3 point)
    {
        Fx.BulletImpact(point, Color.white, 1.5f);
        ProceduralSfx.Play(ProceduralSfx.Hit, point, 1f);
    }
}

// Noha s botou, ktera z pohledu prvni osoby vykopne zespodu do obrazu (jen majitel).
public class FirstPersonKick : MonoBehaviour
{
    float age = -1f;
    Transform leg;

    public static void Play(Camera camera)
    {
        if (camera == null) return;
        if (!ReplayPlayer.Active)
            ReplayLog.FirstPersonKick();

        var kick = camera.GetComponent<FirstPersonKick>();
        if (kick == null)
            kick = camera.gameObject.AddComponent<FirstPersonKick>();
        kick.Begin();
    }

    void Begin()
    {
        if (leg == null)
            leg = BuildLeg(transform);
        age = 0f;
        leg.gameObject.SetActive(true);
    }

    static Transform BuildLeg(Transform camera)
    {
        // Kycel (pivot) pod kamerou; noha miri dolu, pri kopu se zvedne dopredu do obrazu.
        var hip = new GameObject("KickLeg").transform;
        hip.SetParent(camera, false);
        hip.localPosition = new Vector3(0.12f, -0.95f, 0.15f);

        var pants = new Color(0.22f, 0.24f, 0.28f);
        var boot = new Color(0.12f, 0.1f, 0.09f);
        Part(hip, "Stehno", new Vector3(0f, -0.25f, 0f), new Vector3(0.2f, 0.5f, 0.2f), pants);
        Part(hip, "Lytko", new Vector3(0f, -0.72f, 0.02f), new Vector3(0.17f, 0.48f, 0.17f), pants);
        Part(hip, "Bota", new Vector3(0f, -1.0f, 0.12f), new Vector3(0.19f, 0.14f, 0.36f), boot);
        Part(hip, "Podrazka", new Vector3(0f, -1.075f, 0.12f), new Vector3(0.2f, 0.035f, 0.38f), new Color(0.05f, 0.05f, 0.05f));
        hip.gameObject.SetActive(false);
        return hip;
    }

    static void Part(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        Fx.Paint(go, color);
        var renderer = go.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void LateUpdate()
    {
        if (age < 0f || leg == null) return;

        age += Time.deltaTime;
        float t = age / KickAbility.KickSeconds;
        if (t >= 1f)
        {
            age = -1f;
            leg.gameObject.SetActive(false);
            return;
        }

        // Nadechnuti (noha trochu dozadu), rychly kop vzhuru dopredu, drzeni a navrat.
        float angle;
        if (t < 0.18f) angle = Mathf.Lerp(0f, -15f, t / 0.18f);
        else if (t < 0.4f) angle = Mathf.Lerp(-15f, 100f, Mathf.SmoothStep(0f, 1f, (t - 0.18f) / 0.22f));
        else if (t < 0.6f) angle = 100f;
        else angle = Mathf.Lerp(100f, 0f, (t - 0.6f) / 0.4f);

        leg.localRotation = Quaternion.Euler(-angle, 0f, 0f);
    }
}
