using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Flankeruv navrat v case (E): vrati se tam, kde byl pred 'duration' sekundami, a ziska zpet nejvyssi zdravi z te doby.
// Cesta zpet trva chvilku (prochazi i zdmi) a mezitim je nezranitelny.
public class RecallAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    const float SampleInterval = 0.1f;
    const float RewindSeconds = 0.6f;

    CharacterController controller;
    FirstPersonController fpc;
    Health health;
    float nextUseTime;
    bool rewinding;

    // majitel: kde byl; server: jake mel zdravi
    readonly List<(float time, Vector3 position)> path = new List<(float, Vector3)>();
    readonly List<(float time, float hp)> healthHistory = new List<(float, float)>();
    float nextSample;

    public float CooldownRemaining => Mathf.Max(0f, nextUseTime - Time.time);
    public bool IsActive => rewinding;

    float History => ability != null ? Mathf.Max(0.5f, ability.duration) : 3f;

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
        path.Clear();
        healthHistory.Clear();
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        fpc = GetComponent<FirstPersonController>();
        health = GetComponent<Health>();
    }

    void Update()
    {
        if (ability == null) return;

        if (Time.time >= nextSample)
        {
            nextSample = Time.time + SampleInterval;
            if (IsOwner && !rewinding)
                Record(path, (Time.time, transform.position));
            if (IsServer)
                Record(healthHistory, (Time.time, health.currentHealth.Value));
        }

        if (!IsOwner || rewinding) return;
        if (!HeroInput.Pressed(this, HeroInput.Key.E) || !HeroInput.Locked(this)) return;
        if (fpc.InputBlocked || Time.time < nextUseTime || path.Count < 2) return;

        nextUseTime = Time.time + ability.Cooldown;
        GetComponent<PlayerHero>().SayAbility(ability);
        RecallServerRpc();
        StartCoroutine(Rewind());
    }

    void Record<T>(List<(float time, T value)> list, (float, T) sample)
    {
        list.Add(sample);
        float oldest = Time.time - History;
        while (list.Count > 0 && list[0].time < oldest)
            list.RemoveAt(0);
    }

    // Prehraje zaznamenanou cestu pozpatku (vlastnik ridi svuj pohyb).
    IEnumerator Rewind()
    {
        rewinding = true;
        fpc.AbilityActive = true;
        ProceduralSfx.Play(ProceduralSfx.UltCharge, transform.position, 0.5f);

        var points = new List<Vector3>();
        for (int i = path.Count - 1; i >= 0; i--)
            points.Add(path[i].position);
        path.Clear();

        controller.enabled = false;
        float elapsed = 0f;
        while (elapsed < RewindSeconds && !fpc.IsDead)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / RewindSeconds) * (points.Count - 1);
            int i = Mathf.Min(points.Count - 2, Mathf.FloorToInt(t));
            transform.position = Vector3.Lerp(points[i], points[i + 1], t - i);
            yield return null;
        }
        if (!fpc.IsDead)
            transform.position = points[points.Count - 1];
        controller.enabled = true;

        fpc.ClearForces();
        fpc.AbilityActive = false;
        rewinding = false;
    }

    void OnDisable()
    {
        if (rewinding && fpc != null)
        {
            controller.enabled = true;
            fpc.AbilityActive = false;
            rewinding = false;
        }
    }

    // ---------------- server ----------------

    [ServerRpc]
    void RecallServerRpc()
    {
        var match = MatchManager.Instance;
        if (ability == null || health.currentHealth.Value <= 0f || (match != null && (match.IsOver || match.IsLobby))) return;

        float best = health.currentHealth.Value;
        foreach (var sample in healthHistory)
            best = Mathf.Max(best, sample.hp);
        healthHistory.Clear();

        health.ServerInvulnerable(RewindSeconds + 0.1f);
        health.ServerRestore(best);
        RecallClientRpc(transform.position + Vector3.up);
    }

    [ClientRpc]
    void RecallClientRpc(Vector3 point)
    {
        Fx.Sparks(point, new Color(0.4f, 0.75f, 1f));
        if (!IsOwner)
            ProceduralSfx.Play(ProceduralSfx.UltCharge, point, 0.5f);
    }
}
