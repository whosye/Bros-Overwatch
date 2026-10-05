using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Sniperuv hak (Shift): vystreli lano tam, kam miri (nejdal 'range' m), a plynule se k tomu mistu pritahne
// (na strechu, na rozhlednu, pryc z boje). Hak se chyti jen sten a predmetu, ne hracu; kdyz nic netrefi, nic se nestane
// a cooldown nezacne.
public class GrappleAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    static readonly Color RopeColor = new Color(0.15f, 0.15f, 0.17f, 1f);

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
        if (!Keyboard.current.leftShiftKey.wasPressedThisFrame || !GameSettings.CursorLocked) return;
        if (fpc.InputBlocked || Time.time < nextUseTime) return;

        var eye = fpc.playerCamera.transform;
        if (!TryFindAnchor(eye.position, eye.forward, out Vector3 anchor, out Vector3 normal))
        {
            ProceduralSfx.Play(ProceduralSfx.Empty, transform.position, 0.5f);
            return;
        }

        nextUseTime = Time.time + ability.Cooldown;

        // Cil pritazeni: kousek od steny (a nad hranu, kdyz se chyti shora), at hrac nezustane v ni.
        Vector3 destination = anchor + normal * 0.7f - Vector3.up * 0.9f;
        if (normal.y > 0.6f)
            destination = anchor + Vector3.up * 0.15f;

        float seconds = Mathf.Clamp(Vector3.Distance(transform.position, destination) / Mathf.Max(1f, ability.speed), 0.15f, 1.2f);
        fpc.OwnerPull(destination, seconds);

        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.6f);
        GetComponent<PlayerHero>().SayAbility(ability);
        RopeServerRpc(anchor, seconds);
    }

    bool TryFindAnchor(Vector3 origin, Vector3 direction, out Vector3 point, out Vector3 normal)
    {
        var hits = Physics.RaycastAll(origin, direction, ability.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() == NetworkObject) continue;

            // Hrace ani pohyblive veci (balvan) hak nechyti.
            if (ProjectileSim.IsLiveTarget(hit.collider))
                break;

            point = hit.point;
            normal = hit.normal;
            return true;
        }

        point = normal = Vector3.zero;
        return false;
    }

    [ServerRpc]
    void RopeServerRpc(Vector3 anchor, float seconds)
    {
        RopeClientRpc(anchor, Mathf.Clamp(seconds, 0.1f, 1.5f));
    }

    [ClientRpc]
    void RopeClientRpc(Vector3 anchor, float seconds)
    {
        var go = new GameObject("GrappleRope");
        var line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.startWidth = line.endWidth = 0.035f;
        line.material = Fx.ParticleMaterial;
        line.startColor = line.endColor = RopeColor;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        StartCoroutine(Rope(line, anchor, seconds));

        Fx.Sparks(anchor, new Color(0.8f, 0.8f, 0.8f));
    }

    IEnumerator Rope(LineRenderer line, Vector3 anchor, float seconds)
    {
        float end = Time.time + seconds + 0.1f;
        while (Time.time < end && line != null)
        {
            // Lano vede od ruky (u majitele od kamery vpravo dole).
            Vector3 hand = transform.position + Vector3.up * 1.3f + transform.right * 0.3f;
            if (IsOwner && fpc != null && fpc.playerCamera != null)
                hand = fpc.playerCamera.transform.position + fpc.playerCamera.transform.right * 0.25f - fpc.playerCamera.transform.up * 0.25f;
            line.SetPosition(0, hand);
            line.SetPosition(1, anchor);
            yield return null;
        }
        if (line != null)
            Destroy(line.gameObject);
    }
}
