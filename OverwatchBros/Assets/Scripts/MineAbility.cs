using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Honzova naloz (jako Junkratova mina): Left Shift ji hodi (prilepi se na prvni povrch), prave tlacitko mysi odpali
// vsechny nalozene naloze naraz. Vybuch zrani nepratele ('power', dosah 'radius') a odhodi je ('knockback');
// Honzu sameho jen odhodi, takze slouzi i jako skok. Ma 'charges' naboju, kazdy se dobiji 'cooldown' sekund;
// venku muze byt nejvys tolik nalozi, kolik je naboju (dalsi hod nahradi nejstarsi).
public class MineAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    const float Gravity = 18f;
    const float Size = 0.36f;

    class ServerMine
    {
        public int id;
        public Vector3 position;
        public Vector3 velocity;
        public bool stuck;
    }

    class MineVisual
    {
        public GameObject root;
        public Light blinker;
        public Vector3 velocity;
        public bool flying;
    }

    FirstPersonController fpc;

    // vlastnik
    int charges;
    float rechargeAt;
    int minesOut;
    float bufferedUntil;
    const float PressBuffer = 0.4f;

    // server
    readonly List<ServerMine> serverMines = new List<ServerMine>();
    int nextId;

    // vizual (vsichni klienti)
    readonly Dictionary<int, MineVisual> visuals = new Dictionary<int, MineVisual>();

    int MaxCharges => ability != null ? Mathf.Max(1, ability.charges) : 1;

    public int Charges => charges;
    public int MinesOut => minesOut;
    public bool IsActive => minesOut > 0;
    public float CooldownRemaining => charges > 0 ? 0f : Mathf.Max(0f, rechargeAt - Time.time);

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
        charges = MaxCharges;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
    }

    public override void OnNetworkDespawn()
    {
        foreach (var visual in visuals.Values)
            if (visual.root != null)
                Destroy(visual.root);
        visuals.Clear();
    }

    // Hrdina se zmenil: naloze zmizi.
    void OnDisable()
    {
        Cancel();
    }

    void Update()
    {
        if (IsOwner)
            OwnerUpdate();

        if (IsServer && serverMines.Count > 0)
            ServerFly();

        UpdateVisuals();
    }

    void OwnerUpdate()
    {
        if (ability == null) return;

        if (charges < MaxCharges && Time.time >= rechargeAt)
        {
            charges++;
            rechargeAt = Time.time + ability.Cooldown;
        }

        if (!GameSettings.CursorLocked || fpc.CannotAct) return;

        if (minesOut > 0 && Mouse.current.rightButton.wasPressedThisFrame)
            DetonateServerRpc();

        // Stisk se chvili pamatuje: zmacknuti tesne pred dobitim naboje se nezahodi.
        if (Keyboard.current.leftShiftKey.wasPressedThisFrame)
        {
            bufferedUntil = Time.time + PressBuffer;

            // Bez naboje: cvaknuti, at je jasne, ze stisk prosel, jen neni cim hazet.
            if (charges <= 0 && !fpc.InputBlocked)
                ProceduralSfx.Play(ProceduralSfx.Empty, transform.position, 0.7f);
        }

        if (Time.time > bufferedUntil || fpc.InputBlocked || charges <= 0) return;
        bufferedUntil = 0f;

        if (charges == MaxCharges)
            rechargeAt = Time.time + ability.Cooldown;
        charges--;
        minesOut++;

        var eye = fpc.playerCamera.transform;
        Vector3 direction = (eye.forward + Vector3.up * 0.12f).normalized;
        ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.5f);
        GetComponent<PlayerHero>().SayAbility(ability);
        ThrowServerRpc(eye.position + eye.forward * 0.6f, direction);
    }

    // Vola PlayerRespawn (vlastnik): po smrti / restartu naloze zmizi bez vybuchu.
    public void Cancel()
    {
        if (minesOut <= 0) return;

        minesOut = 0;
        if (IsSpawned && IsOwner && NetworkManager != null && !NetworkManager.ShutdownInProgress)
            CancelServerRpc();
    }

    // ---------------- server ----------------

    [ServerRpc]
    void ThrowServerRpc(Vector3 origin, Vector3 direction)
    {
        var match = MatchManager.Instance;
        bool blocked = ability == null || direction.sqrMagnitude < 0.01f || (match != null && (match.IsOver || match.IsLobby));
        if (blocked)
        {
            EndClientRpc(-1, origin, false);
            return;
        }

        // Vic nalozi nez naboju venku byt nemuze: nejstarsi zmizi.
        while (serverMines.Count >= MaxCharges)
        {
            var oldest = serverMines[0];
            serverMines.RemoveAt(0);
            EndClientRpc(oldest.id, oldest.position, false);
        }

        var mine = new ServerMine { id = ++nextId, position = origin, velocity = direction.normalized * ability.speed };
        serverMines.Add(mine);
        ThrownClientRpc(mine.id, origin, mine.velocity);
    }

    void ServerFly()
    {
        float dt = Time.deltaTime;

        for (int i = serverMines.Count - 1; i >= 0; i--)
        {
            var mine = serverMines[i];
            if (mine.stuck) continue;

            mine.velocity.y -= Gravity * dt;
            Vector3 step = mine.velocity * dt;
            float distance = step.magnitude;
            if (distance <= 0f) continue;

            var hits = Physics.SphereCastAll(mine.position, Size * 0.4f, step / distance, distance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                // Lepi se jen na svet, hraci a balvany ji propousti.
                if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
                if (hit.collider.GetComponentInParent<BoulderHitbox>() != null) continue;

                Vector3 normal = hit.distance > 0f ? hit.normal : -step / distance;
                mine.position = (hit.distance > 0f ? hit.point : mine.position) + normal * 0.04f;
                mine.stuck = true;
                StuckClientRpc(mine.id, mine.position, normal);
                break;
            }

            if (mine.stuck) continue;

            mine.position += step;
            if (mine.position.y < -60f)
            {
                serverMines.RemoveAt(i);
                EndClientRpc(mine.id, mine.position, false);
            }
        }
    }

    [ServerRpc]
    void DetonateServerRpc()
    {
        if (ability == null) return;

        var mines = new List<ServerMine>(serverMines);
        serverMines.Clear();

        foreach (var mine in mines)
        {
            Vector3 center = mine.position + Vector3.up * 0.1f;
            Combat.Explode(gameObject, center, ability.radius, ability.power, 0.5f);
            Combat.Knockback(gameObject, center, ability.radius, ability.knockback, 1f);
            EndClientRpc(mine.id, mine.position, true);
        }
    }

    [ServerRpc]
    void CancelServerRpc()
    {
        foreach (var mine in serverMines)
            EndClientRpc(mine.id, mine.position, false);
        serverMines.Clear();
    }

    // ---------------- vizual ----------------

    [ClientRpc]
    void ThrownClientRpc(int id, Vector3 origin, Vector3 velocity)
    {
        var visual = BuildVisual();
        visual.root.transform.position = origin;
        visual.velocity = velocity;
        visual.flying = true;
        visuals[id] = visual;
    }

    [ClientRpc]
    void StuckClientRpc(int id, Vector3 position, Vector3 normal)
    {
        if (!visuals.TryGetValue(id, out var visual) || visual.root == null) return;

        visual.flying = false;
        visual.root.transform.position = position;
        visual.root.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
        ProceduralSfx.Play(ProceduralSfx.Hit, position, 0.35f);
    }

    [ClientRpc]
    void EndClientRpc(int id, Vector3 position, bool exploded)
    {
        if (visuals.TryGetValue(id, out var visual))
        {
            if (visual.root != null)
                Destroy(visual.root);
            visuals.Remove(id);
        }

        if (IsOwner)
            minesOut = Mathf.Max(0, minesOut - 1);

        if (!exploded || ability == null) return;

        Fx.Explosion(position, ability.radius);
        ProceduralSfx.Play(ProceduralSfx.Explosion, position, 0.9f);
    }

    void UpdateVisuals()
    {
        if (visuals.Count == 0) return;

        float blink = Mathf.Repeat(Time.time * 2.5f, 1f) < 0.5f ? 2.2f : 0.3f;
        foreach (var visual in visuals.Values)
        {
            if (visual.root == null) continue;

            if (visual.flying)
            {
                visual.velocity.y -= Gravity * Time.deltaTime;
                visual.root.transform.position += visual.velocity * Time.deltaTime;
                visual.root.transform.Rotate(360f * Time.deltaTime, 0f, 140f * Time.deltaTime, Space.Self);
            }

            visual.blinker.intensity = blink;
        }
    }

    MineVisual BuildVisual()
    {
        var visual = new MineVisual { root = new GameObject("MineVisual") };

        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(visual.root.transform, false);
        body.transform.localScale = new Vector3(Size, 0.045f, Size);
        Fx.Paint(body, new Color(0.16f, 0.17f, 0.19f));

        var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(cap.GetComponent<Collider>());
        cap.transform.SetParent(visual.root.transform, false);
        cap.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        cap.transform.localScale = Vector3.one * 0.13f;
        Fx.Paint(cap, new Color(1f, 0.2f, 0.12f));

        var lightObject = new GameObject("Blink");
        lightObject.transform.SetParent(visual.root.transform, false);
        lightObject.transform.localPosition = new Vector3(0f, 0.15f, 0f);
        visual.blinker = lightObject.AddComponent<Light>();
        visual.blinker.type = LightType.Point;
        visual.blinker.color = new Color(1f, 0.25f, 0.15f);
        visual.blinker.range = 2.5f;

        return visual;
    }
}
