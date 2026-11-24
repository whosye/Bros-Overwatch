using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Povova sekera na retezu (klavesa E, "Chain Hook"):
//   - napraah 'Windup' s: ruka se sekerou se trhne dozadu a sekera se zatrese,
//   - sekera leti rovne rychlosti 'FlySpeed' (nejdal 'range' m), za ni se odviji retez z clanku,
//   - zasah nepritele: sekera se zakousne ('power' poskozeni, jiskry, zvuk), po 'BiteSeconds' s se cil pritahne
//     rychlosti 'PullSpeed' na 'PullDistance' m pred Povu a je omracen jeste 'duration' s po dotazeni,
//   - minuti / zed: sekera se zastavi, cinkne a rychleji ('RetractSpeed') se navine zpet, retez zplihne a vlni se,
//   - cooldown bezi az od chvile, kdy sekera zacvakne zpet do ruky.
// Spoluhrace sekera proleti, zed ji zastavi.
// Let (zasah / minuti) pocita hazejici hrac podle toho, co sam vidi (jako u strelby) - server zasah jen overi
// (vzdalenost, dosah, zed mezi) a provede poskozeni, pritazeni a omraceni. Driv let pocital server a klientum
// kvuli zpozdeni site sekera "projizdela" pres cile, ktere u nich zasahla.
public class HookAbility : NetworkBehaviour, ICooldownCut
{
    public AbilityDefinition ability;

    public const float Windup = 0.07f;
    public const float FlySpeed = 65f;
    public const float BiteSeconds = 0.1f;
    public const float PullSpeed = 30f;
    public const float RetractSpeed = 60f;
    const float HookRadius = 0.35f;       // zed / prekazka sekeru zastavi (tenky paprsek)
    const float PlayerHitRadius = 1.1f;   // hrace sekera chyti i pri mene presnem mireni
    const float PullDistance = 2f;        // jak daleko pred Povou pritazeny skonci
    const float SafetySeconds = 3f;       // kdyby zprava od serveru neprisla, hak se po teto dobe uvolni

    FirstPersonController fpc;

    // vlastnik
    float nextUseTime;
    bool active;
    float activeUntil;
    float cutFactor = 1f;

    // server
    bool serverBusy;

    // vsichni klienti
    HookVisual visual;

    public bool IsActive => active && Time.time < activeUntil;
    public float CooldownRemaining => IsActive ? 0f : Mathf.Max(0f, nextUseTime - Time.time);

    public void CutCooldown(float fraction)
    {
        if (IsActive)
        {
            // hak je jeste venku: zkraceni se uplatni na cooldown, ktery zacne po navratu
            cutFactor *= 1f - Mathf.Clamp01(fraction);
            nextUseTime = activeUntil + (nextUseTime - activeUntil) * (1f - Mathf.Clamp01(fraction));
            return;
        }
        if (nextUseTime > Time.time)
            nextUseTime = Time.time + (nextUseTime - Time.time) * (1f - Mathf.Clamp01(fraction));
    }

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
    }

    public override void OnDestroy()
    {
        if (visual != null) Destroy(visual.gameObject);
        base.OnDestroy();
    }

    void Update()
    {
        if (!IsOwner || ability == null) return;
        if (active && Time.time >= activeUntil) active = false;
        if (!HeroInput.Pressed(this, HeroInput.Key.E) || !HeroInput.Locked(this)) return;
        if (IsActive || fpc.InputBlocked || fpc.RushActive || fpc.BlockActive || Time.time < nextUseTime) return;

        active = true;
        activeUntil = Time.time + SafetySeconds;
        cutFactor = 1f;
        nextUseTime = activeUntil + ability.Cooldown;

        var eye = fpc.playerCamera.transform;
        GetComponent<PlayerHero>().SayAbility(ability);
        ThrowServerRpc(eye.position, eye.forward);
        // hazejici vidi sekeru hned (neceka na odpoved serveru)
        SpawnVisual(eye.position, eye.forward.normalized, ability.range);
        if (ownerFly != null) StopCoroutine(ownerFly);
        ownerFly = StartCoroutine(OwnerFly(eye.position, eye.forward.normalized));
    }

    Coroutine ownerFly;

    // Vlastnik: let sekery podle vlastniho pohledu; vysledek posle serveru.
    IEnumerator OwnerFly(Vector3 origin, Vector3 direction)
    {
        yield return new WaitForSeconds(Windup);

        var ownHealth = GetComponent<Health>();
        float range = ability.range;
        float travelled = 0f;
        Health victim = null;
        Vector3 point = origin;
        while (true)
        {
            Vector3 from = origin + direction * travelled;
            if (ownHealth != null && ownHealth.currentHealth.Value <= 0f) { point = from; break; }
            float step = Mathf.Min(FlySpeed * Time.deltaTime, range - travelled);
            if (Sweep(from, direction, step, out victim, out point)) break;
            travelled += step;
            if (travelled >= range - 0.001f) { point = origin + direction * range; break; }
            yield return null;
        }
        ownerFly = null;
        if (victim != null && victim.NetworkObject != null)
        {
            // sekera se u hazejiciho zakousne hned (server pak jen potvrdi)
            float pull = Vector3.Distance(victim.transform.position, PullDestination(victim)) / PullSpeed;
            if (visual != null) visual.OnHit(victim.transform, point, pull < 0.05f ? 0f : pull);
            ResolveServerRpc(victim.NetworkObject, true, point);
        }
        else
        {
            if (visual != null) visual.OnMiss(point);
            ResolveServerRpc(default, false, point);
        }
    }

    // Vlastnik: hak se vrati za 'returnIn' s, od te chvile bezi cooldown.
    void OwnerReturnsIn(float returnIn)
    {
        if (!IsOwner || ability == null) return;
        active = true;
        activeUntil = Time.time + Mathf.Max(0.05f, returnIn);
        nextUseTime = activeUntil + ability.Cooldown * cutFactor;
    }

    // ---------------- server ----------------

    [ServerRpc]
    void ThrowServerRpc(Vector3 origin, Vector3 direction)
    {
        var match = MatchManager.Instance;
        if (ability == null || serverBusy || direction.sqrMagnitude < 0.01f || (match != null && (match.IsOver || match.IsLobby)))
        {
            CancelClientRpc();
            return;
        }

        direction.Normalize();
        serverBusy = true;
        serverThrow = ++serverThrowCount;
        serverOrigin = origin;
        LaunchClientRpc(origin, direction, ability.range);
        StartCoroutine(ServerTimeout(serverThrow));
    }

    int serverThrow, serverThrowCount;   // aktualni hod (0 = zadny ceka na vysledek)
    Vector3 serverOrigin;

    // Kdyby vysledek od vlastnika neprisel (odpojeni), hak se uvolni.
    IEnumerator ServerTimeout(int id)
    {
        yield return new WaitForSeconds(Windup + (ability != null ? ability.range : 20f) / FlySpeed + 1.5f);
        if (serverThrow != id) yield break;
        serverThrow = 0;
        MissClientRpc(transform.position + Vector3.up * 1.25f);
        serverBusy = false;
    }

    // Vysledek letu od vlastnika: server ho overi (jinak minuti v danem miste) a dokonci.
    [ServerRpc]
    void ResolveServerRpc(NetworkObjectReference victimRef, bool hasVictim, Vector3 point)
    {
        if (serverThrow == 0 || ability == null) return;
        serverThrow = 0;

        float range = ability.range;
        Vector3 origin = serverOrigin;
        // bod musi lezet v dosahu hodu (s rezervou na pohyb hrace)
        if ((point - origin).sqrMagnitude > (range + 2f) * (range + 2f))
            point = origin + (point - origin).normalized * range;

        Health victim = null;
        if (hasVictim && victimRef.TryGet(out NetworkObject victimObject))
        {
            var health = victimObject.GetComponent<Health>();
            var other = victimObject.GetComponent<PlayerHero>();
            var col = victimObject.GetComponent<Collider>();
            bool valid = health != null && health.gameObject != gameObject && health.currentHealth.Value > 0f
                && !Combat.SameTeam(gameObject, health.gameObject)
                && (other == null || !other.IsUntargetable)
                // cil u serveru nejvys par metru od mista zasahu u hazejiciho (zpozdeni site, pohyb)
                && (health.transform.position + Vector3.up - point).sqrMagnitude < 4.5f * 4.5f
                && (health.transform.position - origin).sqrMagnitude < (range + 4f) * (range + 4f)
                && (col == null || Combat.HasLineOfSight(origin, col) || Combat.HasLineOfSight(point, col));
            if (valid) victim = health;
        }
        StartCoroutine(Finish(victim, point));
    }

    IEnumerator Finish(Health victim, Vector3 point)
    {
        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) victim = null;

        if (victim == null)
        {
            MissClientRpc(point);
            yield return new WaitForSeconds(Vector3.Distance(transform.position, point) / RetractSpeed);
            serverBusy = false;
            yield break;
        }

        Combat.DamagePlayer(gameObject, victim, ability.power, Combat.AbilitySource(ability));

        var controller = victim.GetComponent<FirstPersonController>();
        bool alive = victim.currentHealth.Value > 0f && controller != null;
        float pull = 0f;
        if (alive)
        {
            pull = Vector3.Distance(victim.transform.position, PullDestination(victim)) / PullSpeed;
            if (pull < 0.05f) pull = 0f;   // uz stoji tesne pred Povou
            controller.ServerStun(BiteSeconds + pull + ability.duration, gameObject, false);
        }

        HitClientRpc(victim.NetworkObject, point, alive ? pull : -1f);

        yield return new WaitForSeconds(BiteSeconds);

        if (alive && pull > 0f && victim != null && victim.currentHealth.Value > 0f)
            controller.ServerPull(PullDestination(victim), pull);   // (cil podle toho, kde Pova stoji ted)

        yield return new WaitForSeconds(pull + 0.2f);
        serverBusy = false;
    }

    // Kousek pred Povou ve smeru k pritahovanemu (ve vysce Povovych nohou).
    Vector3 PullDestination(Health victim)
    {
        Vector3 toVictim = victim.transform.position - transform.position;
        toVictim.y = 0f;
        Vector3 flat = toVictim.sqrMagnitude > 0.01f ? toVictim.normalized : transform.forward;
        return transform.position + flat * PullDistance;
    }

    // Jeden krok letu sekery: vrati true, kdyz se o neco zastavila (victim = koho zasekla, jinak zed).
    bool Sweep(Vector3 from, Vector3 direction, float step, out Health victim, out Vector3 point)
    {
        victim = null;
        point = from + direction * step;
        if (step <= 0f) return false;

        float reach = step;
        bool blocked = false;

        // 1) tenky paprsek: kde sekeru zastavi prekazka (nebo koho trefi primo)
        var hits = Physics.SphereCastAll(from, HookRadius, direction, step, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && (owner == NetworkObject || Combat.SameTeam(gameObject, owner.gameObject))) continue;
            if (hit.collider.GetComponentInParent<BoulderHitbox>() != null) continue;

            var health = hit.collider.GetComponentInParent<Health>();
            if (health != null)
            {
                var other = health.GetComponent<PlayerHero>();
                if (other != null && other.IsUntargetable) continue;   // (Max ve stinu: sekera proleti)
            }

            point = hit.distance > 0f ? hit.point : from;
            reach = hit.distance;
            blocked = true;
            if (health != null && health.currentHealth.Value > 0f)
                victim = health;
            break;
        }

        // 2) sirsi paprsek jen na nepratele pred prekazkou (velkorysejsi hitbox)
        if (victim == null)
        {
            var wide = Physics.SphereCastAll(from, PlayerHitRadius, direction, step, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(wide, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in wide)
            {
                if (hit.distance > reach) break;
                var health = hit.collider.GetComponentInParent<Health>();
                if (health == null || health.gameObject == gameObject || health.currentHealth.Value <= 0f) continue;
                if (Combat.SameTeam(gameObject, health.gameObject)) continue;
                var other = health.GetComponent<PlayerHero>();
                if (other != null && other.IsUntargetable) continue;
                if (!Combat.HasLineOfSight(from, hit.collider)) continue;
                victim = health;
                point = hit.collider.bounds.center;
                blocked = true;
                break;
            }
        }

        return blocked;
    }

    // ---------------- klienti ----------------

    [ClientRpc]
    void CancelClientRpc()
    {
        if (!IsOwner) return;
        active = false;
        nextUseTime = 0f;
        if (ownerFly != null) { StopCoroutine(ownerFly); ownerFly = null; }
        if (visual != null) Destroy(visual.gameObject);
    }

    [ClientRpc]
    void LaunchClientRpc(Vector3 origin, Vector3 direction, float range)
    {
        if (IsOwner) return;   // hazejici uz sekeru spustil sam
        SpawnVisual(origin, direction, range);
    }

    void SpawnVisual(Vector3 origin, Vector3 direction, float range)
    {
        if (visual != null) Destroy(visual.gameObject);

        var held = GetComponent<HeldWeapons>();
        if (held != null) held.PlayHookWindup(Windup);

        // Na retezu leti stejna sekera, jakou hrdina drzi v ruce.
        var playerHero = GetComponent<PlayerHero>();
        var weapon = playerHero != null && playerHero.Hero != null ? playerHero.Hero.weapon : null;
        visual = HookVisual.Spawn(transform, held, origin, direction, range, weapon != null ? weapon.heldPrefab : null);
    }

    [ClientRpc]
    void HitClientRpc(NetworkObjectReference victimRef, Vector3 point, float pull)
    {
        Transform victim = victimRef.TryGet(out NetworkObject victimObject) ? victimObject.transform : null;
        if (visual != null && !IsOwner) visual.OnHit(victim, point, pull);   // (hazejici uz zobrazil sam)

        // zakousnuti, (pritazeni), kratke navinuti z ~2 m
        OwnerReturnsIn(BiteSeconds + Mathf.Max(0f, pull) + 0.05f + (PullDistance + 1f) / RetractSpeed);
    }

    [ClientRpc]
    void MissClientRpc(Vector3 point)
    {
        if (visual != null) visual.OnMiss(point);
        OwnerReturnsIn(Vector3.Distance(transform.position + Vector3.up * 1.25f, point) / RetractSpeed);
    }
}

// Sekera na retezu (jen efekt, kazdy klient si ji prehraje sam podle zprav ze serveru).
public class HookVisual : MonoBehaviour
{
    enum State { Windup, Out, Bite, Pull, Retract }

    const float LinkSpacing = 0.075f;
    const int MaxLinks = 400;
    static readonly Color SparkColor = new Color(1f, 0.75f, 0.35f);

    static Mesh linkMesh;
    static Material linkMaterial;

    Transform owner;
    HeldWeapons held;
    Vector3 origin, direction;
    float range;

    State state = State.Windup;
    float stateTime;
    bool reelingAudio;
    AudioSource reelSource;
    Vector3 hookPos;
    Vector3 launchHand;

    Transform victim;
    Vector3 victimOffset;
    float pullTime;

    float sag, wave;   // zplihnuti a vlneni retezu pri navijeni

    Transform tip;
    Transform axe;
    readonly List<Transform> links = new List<Transform>();
    Vector3[] points = new Vector3[0];

    public static HookVisual Spawn(Transform owner, HeldWeapons held, Vector3 origin, Vector3 direction, float range, GameObject axePrefab = null)
    {
        var go = new GameObject("HookVisual");
        var visual = go.AddComponent<HookVisual>();
        visual.owner = owner;
        visual.held = held;
        visual.origin = origin;
        visual.direction = direction.normalized;
        visual.range = range;

        visual.tip = new GameObject("Tip").transform;
        visual.tip.SetParent(go.transform, false);

        if (axePrefab != null)
        {
            var model = Instantiate(axePrefab, visual.tip);
            foreach (var collider in model.GetComponentsInChildren<Collider>())
                DestroyImmediate(collider);
            visual.axe = model.transform;
        }
        else
        {
            // Nahradni sekera z kostek: topurko a hlava.
            visual.axe = new GameObject("Axe").transform;
            visual.axe.SetParent(visual.tip, false);
            Block(visual.axe, new Vector3(0f, 0.25f, 0f), new Vector3(0.045f, 0.56f, 0.045f), new Color(0.45f, 0.28f, 0.12f));
            Block(visual.axe, new Vector3(0f, 0.47f, 0.10f), new Vector3(0.03f, 0.20f, 0.20f), new Color(0.78f, 0.80f, 0.84f));
        }

        // Sekera drzi retez koncem topurka, hlavou miri dopredu.
        visual.axe.localPosition = Vector3.zero;
        visual.axe.localRotation = Quaternion.Euler(90f, 0f, 0f);
        visual.tip.gameObject.SetActive(false);   // behem napraahu je sekera jeste v ruce

        visual.hookPos = visual.Hand();
        return visual;
    }

    public void OnHit(Transform target, Vector3 point, float pull)
    {
        if (state == State.Windup) Launch();
        victim = target;
        hookPos = point;
        victimOffset = victim != null ? Vector3.ClampMagnitude(point - victim.position, 1.6f) : Vector3.zero;
        pullTime = pull;
        SetState(State.Bite);

        Fx.Sparks(point, SparkColor);
        ProceduralSfx.Play(ProceduralSfx.BulletImpact, point, 1f);
        ProceduralSfx.Play(ProceduralSfx.Hit, point, 0.8f);
        ProceduralSfx.Play(ProceduralSfx.Empty, point, 1f);
    }

    public void OnMiss(Vector3 point)
    {
        if (state == State.Windup) Launch();
        if (state != State.Out) return;
        hookPos = point;

        // narazila do zdi (ne jen dolet na konci dosahu): cinknuti a jiskry
        if (Vector3.Distance(origin, point) < range - 0.3f)
        {
            Fx.Sparks(point, SparkColor);
            ProceduralSfx.Play(ProceduralSfx.Hit, point, 1f);
        }
        sag = 0.35f;
        wave = 0.14f;
        SetState(State.Retract);
    }

    void SetState(State next)
    {
        if (!reelingAudio && (next == State.Pull || next == State.Retract))
        {
            reelingAudio = true;
            reelSource = ProceduralSfx.PlayTracked(CollectedAudioBank.Pick("pova_hook.reel"), Hand(), 0.55f, 22f,
                spatial: !ProceduralSfx.IsLocalPlayer(owner));
            if (reelSource != null) reelSource.transform.SetParent(owner, true);
        }
        state = next;
        stateTime = 0f;
    }

    void Launch()
    {
        launchHand = Hand();
        hookPos = launchHand;
        tip.gameObject.SetActive(true);
        if (held != null) held.SetHookThrown(true);
        ProceduralSfx.PlayForActor(owner, CollectedAudioBank.Pick("pova_hook.throw", ProceduralSfx.Dash), launchHand, 0.6f);
        SetState(State.Out);
    }

    void Update()
    {
        if (owner == null)
        {
            Destroy(gameObject);
            return;
        }

        stateTime += Time.deltaTime;
        Vector3 hand = Hand();

        switch (state)
        {
            case State.Windup:
                if (stateTime >= HookAbility.Windup) Launch();
                break;

            case State.Out:
            {
                // let po primce ze stredu pohledu; prvni metry se sekera dostava z ruky na tu primku
                float d = Mathf.Min(HookAbility.FlySpeed * stateTime, range);
                Vector3 onLine = origin + direction * d;
                hookPos = Vector3.Lerp(launchHand, onLine, Mathf.Clamp01(d / 3f));
                // pojistka, kdyby zprava ze serveru neprisla
                if (stateTime > range / HookAbility.FlySpeed + 1f) { sag = 0.3f; wave = 0.12f; SetState(State.Retract); }
                break;
            }

            case State.Bite:
            {
                Vector3 anchor = victim != null ? victim.position + victimOffset : hookPos;
                float shake = 0.07f * (1f - Mathf.Clamp01(stateTime / HookAbility.BiteSeconds));
                hookPos = anchor + Random.insideUnitSphere * shake;
                if (stateTime >= HookAbility.BiteSeconds)
                {
                    if (pullTime > 0f && victim != null) SetState(State.Pull);
                    else { wave = 0.1f; SetState(State.Retract); }
                }
                break;
            }

            case State.Pull:
                if (victim != null) hookPos = victim.position + victimOffset;
                if (victim == null || stateTime >= pullTime + 0.05f)
                {
                    wave = 0.1f;
                    SetState(State.Retract);
                }
                break;

            case State.Retract:
                hookPos = Vector3.MoveTowards(hookPos, hand, HookAbility.RetractSpeed * Time.deltaTime);
                sag = Mathf.MoveTowards(sag, 0f, 0.5f * Time.deltaTime);
                if ((hookPos - hand).sqrMagnitude < 0.15f * 0.15f)
                {
                    if (!CollectedAudioBank.Has("pova_hook.stop"))
                    {
                        ProceduralSfx.Play(ProceduralSfx.Empty, hand, 0.9f);
                        ProceduralSfx.Play(ProceduralSfx.Hit, hand, 0.35f);
                    }
                    Destroy(gameObject);
                    return;
                }
                break;
        }

        PlaceHook(hand);
        DrawChain(hand, hookPos, state != State.Windup);
    }

    void OnDestroy()
    {
        if (reelSource != null) { reelSource.Stop(); Destroy(reelSource.gameObject); }
        if (reelingAudio && owner != null)
            ProceduralSfx.PlayForActor(owner, CollectedAudioBank.Pick("pova_hook.stop"), Hand(), 0.55f);
        if (held != null) held.SetHookThrown(false);
    }

    Vector3 Hand()
    {
        var holder = held != null ? held.HookHand : null;
        if (holder != null) return holder.position;
        return owner != null ? owner.position + Vector3.up * 1.25f + owner.forward * 0.4f : hookPos;
    }

    void PlaceHook(Vector3 hand)
    {
        tip.position = hookPos;
        Vector3 away = state == State.Out ? direction : hookPos - hand;
        if (away.sqrMagnitude < 0.0001f) away = direction;

        // za letu se sekera mirne kroti kolem sve osy
        float roll = state == State.Out ? stateTime * 540f : 0f;
        tip.rotation = Quaternion.LookRotation(away) * Quaternion.Euler(0f, 0f, roll);
    }

    // Retez z clanku od ruky k sekere; sousedni clanky jsou otocene o 90 st. Pri navijeni se vlni
    // (sinusovy posun, ktery slabne ke konci u sekery) a po minuti chvili visi (zplihne).
    void DrawChain(Vector3 a, Vector3 b, bool show)
    {
        float length = Vector3.Distance(a, b);
        int count = show ? Mathf.Clamp(Mathf.FloorToInt(length / LinkSpacing), 0, MaxLinks) : 0;

        while (links.Count < count) links.Add(NewLink());
        for (int i = 0; i < links.Count; i++)
            if (links[i].gameObject.activeSelf != i < count) links[i].gameObject.SetActive(i < count);
        if (count == 0) return;

        if (points.Length < count) points = new Vector3[Mathf.Max(count, points.Length * 2)];

        Vector3 dir = length > 0.0001f ? (b - a) / length : direction;
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        side = side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.right;

        float time = Time.time;
        for (int i = 0; i < count; i++)
        {
            float t = (i + 0.5f) * LinkSpacing / length;
            float arch = Mathf.Sin(Mathf.PI * t);
            float envelope = arch * (1f - 0.75f * t);
            Vector3 offset = Vector3.down * (sag * arch)
                + side * (wave * envelope * Mathf.Sin(t * length * 6f - time * 28f));
            points[i] = a + dir * (t * length) + offset;
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 tangent = count > 1 ? points[Mathf.Min(i + 1, count - 1)] - points[Mathf.Max(i - 1, 0)] : dir;
            if (tangent.sqrMagnitude < 0.000001f) tangent = dir;
            links[i].SetPositionAndRotation(points[i],
                Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(0f, 0f, (i & 1) == 0 ? 0f : 90f));
        }
    }

    Transform NewLink()
    {
        if (linkMesh == null) linkMesh = BuildLinkMesh();
        if (linkMaterial == null) linkMaterial = Fx.NewLit(new Color(0.42f, 0.43f, 0.47f));

        var go = new GameObject("Link");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = linkMesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = linkMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go.transform;
    }

    // Ovalny clanek (protahly torus): delka podel lokalni osy Z, krouzek lezi v rovine XZ.
    static Mesh BuildLinkMesh()
    {
        const int U = 12, V = 6;
        const float Rx = 0.026f, Rz = 0.05f, r = 0.009f;

        var vertices = new Vector3[U * V];
        var normals = new Vector3[U * V];
        for (int i = 0; i < U; i++)
        {
            float u = i * Mathf.PI * 2f / U;
            Vector3 center = new Vector3(Mathf.Cos(u) * Rx, 0f, Mathf.Sin(u) * Rz);
            Vector3 outward = new Vector3(Mathf.Cos(u) * Rz, 0f, Mathf.Sin(u) * Rx).normalized;
            for (int j = 0; j < V; j++)
            {
                float v = j * Mathf.PI * 2f / V;
                Vector3 n = outward * Mathf.Cos(v) + Vector3.up * Mathf.Sin(v);
                vertices[i * V + j] = center + n * r;
                normals[i * V + j] = n;
            }
        }

        var triangles = new int[U * V * 6];
        int k = 0;
        for (int i = 0; i < U; i++)
        {
            int i2 = (i + 1) % U;
            for (int j = 0; j < V; j++)
            {
                int j2 = (j + 1) % V;
                int a = i * V + j, b = i2 * V + j, c = i2 * V + j2, d = i * V + j2;
                triangles[k++] = a; triangles[k++] = d; triangles[k++] = b;
                triangles[k++] = b; triangles[k++] = d; triangles[k++] = c;
            }
        }

        var mesh = new Mesh { name = "ChainLink" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    static void Block(Transform parent, Vector3 position, Vector3 scale, Color color)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(block.GetComponent<Collider>());
        block.transform.SetParent(parent, false);
        block.transform.localPosition = position;
        block.transform.localScale = scale;
        Fx.Paint(block, color);
    }
}
