using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Annina uspavaci sipka (Shift): rychla sipka letici rovne. Prvniho zasazeneho nepritele trochu zrani ('power')
// a uspi ho na 'duration' sekund (nemuze se hybat, strilet ani pouzivat schopnosti). Jakykoli zasah ho probudi.
// Spoluhrace a zdi neprochazi: sipka se zastavi o prvni prekazku, spoluhraci ji propousti.
public class SleepDartAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    static readonly Color DartColor = new Color(0.55f, 0.45f, 1f, 1f);

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
        if (!HeroInput.Pressed(this, HeroInput.Key.Shift) || !HeroInput.Locked(this)) return;
        if (fpc.InputBlocked || Time.time < nextUseTime) return;

        nextUseTime = Time.time + ability.Cooldown;

        var eye = fpc.playerCamera.transform;
        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.45f);
        GetComponent<PlayerHero>().SayAbility(ability);
        FireServerRpc(eye.position, eye.forward);
    }

    // ---------------- server ----------------

    [ServerRpc]
    void FireServerRpc(Vector3 origin, Vector3 direction)
    {
        var match = MatchManager.Instance;
        if (ability == null || direction.sqrMagnitude < 0.01f || (match != null && (match.IsOver || match.IsLobby))) return;

        direction.Normalize();

        // Prvni prekazka nebo nepritel na draze sipky.
        float distance = ability.range;
        Health victim = null;
        var hits = Physics.SphereCastAll(origin, 0.15f, direction, ability.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && (owner == NetworkObject || Combat.SameTeam(gameObject, owner.gameObject))) continue;

            distance = Mathf.Max(0.3f, hit.distance);
            victim = hit.collider.GetComponentInParent<Health>();
            break;
        }

        Vector3 point = origin + direction * distance;
        float flight = distance / Mathf.Max(1f, ability.speed);
        FiredClientRpc(origin + direction * 0.5f, point, flight);
        StartCoroutine(Arrive(victim, point, flight));
    }

    IEnumerator Arrive(Health victim, Vector3 point, float delay)
    {
        yield return new WaitForSeconds(delay);

        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) yield break;

        if (victim != null && victim.currentHealth.Value > 0f)
        {
            Combat.DamagePlayer(gameObject, victim, ability.power, Combat.AbilitySource(ability));

            var controller = victim.GetComponent<FirstPersonController>();
            if (controller != null && victim.currentHealth.Value > 0f)
            {
                controller.ServerSleep(ability.duration, gameObject);

                var recorder = GetComponent<PotgRecorder>();
                if (recorder != null)
                    recorder.ServerAddStun();
            }
        }

        HitClientRpc(point);
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void FiredClientRpc(Vector3 from, Vector3 to, float seconds)
    {
        var dart = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        DestroyImmediate(dart.GetComponent<Collider>());
        dart.name = "SleepDart";
        dart.transform.localScale = new Vector3(0.05f, 0.18f, 0.05f);
        dart.transform.rotation = Quaternion.FromToRotation(Vector3.up, to - from);
        Fx.Paint(dart, DartColor);
        dart.AddComponent<FlashGrenadeVisual>().Init(from, to, seconds);
    }

    [ClientRpc]
    void HitClientRpc(Vector3 point)
    {
        Fx.Sparks(point, DartColor);
        ProceduralSfx.Play(ProceduralSfx.Hit, point, 0.6f);
    }
}

// Ukazatel "Zzz" nad hlavou uspaneho hrace (vsichni ho vidi; zmizi po probuzeni nebo po case).
public class SleepMarker : MonoBehaviour
{
    float until;
    TextMeshPro text;

    public static void Show(Transform player, float seconds)
    {
        ReplayLog.SleepShow(player, seconds);
        var marker = player.GetComponentInChildren<SleepMarker>();
        if (marker == null)
        {
            var go = new GameObject("SleepMarker");
            go.transform.SetParent(player, false);
            go.transform.localPosition = new Vector3(0f, 2.35f, 0f);
            marker = go.AddComponent<SleepMarker>();
            marker.text = go.AddComponent<TextMeshPro>();
            marker.text.text = "Zzz";
            marker.text.fontSize = 6f;
            marker.text.alignment = TextAlignmentOptions.Center;
            marker.text.color = new Color(0.7f, 0.65f, 1f);
            marker.text.fontStyle = FontStyles.Bold;
        }
        marker.until = Time.time + seconds;
        marker.gameObject.SetActive(true);
    }

    public static void Hide(Transform player)
    {
        ReplayLog.SleepHide(player);
        var marker = player.GetComponentInChildren<SleepMarker>();
        if (marker != null)
            marker.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (Time.time >= until)
        {
            gameObject.SetActive(false);
            return;
        }

        // Natocit ke kamere a lehce pohupovat.
        var cam = Camera.main;
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        transform.localPosition = new Vector3(0f, 2.35f + Mathf.Sin(Time.time * 3f) * 0.08f, 0f);
    }
}
