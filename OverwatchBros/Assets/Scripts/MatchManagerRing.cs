using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// Prsten z tajne jeskyne (PickupSpot Ring): kdo ho sebere, nese ho, dokud zije. Na nositeli neni nic poznat
// (jen on sam dostane hlasku). Klavesou T si ho nasadi / sundá (vidi to jen on: zsedly "svet stinu").
//
// Glum: prsten si pamatuje, jak dlouho byl celkem nasazeny (i u vice nositelu - nuluje se, az se vrati do jeskyne).
// Po 'GollumThreshold' s ho zacne honit Glum a cim dele je prsten nasazeny, tim je Glum nastvanejsi: rychlejsi,
// silnejsi a utoci casteji. Honi nositele, i kdyz si ho zrovna sundal (narusta jen pri nasazeni). Jde za nim po
// navigacni siti, kde to nejde (jeskyne), primo - i skrz zdi. Zasahnout nejde.
//
// Smrt nositele: prsten spadne na zem a lezi tam 'DropSeconds' s - kdo na nej slapne, sebere ho (i s Glumem v patach).
// Kdyz ho nikdo nevezme (nebo ho dřív sebere Glum), zmizi a za 'ReturnSeconds' s se objevi zpatky v jeskyni.
public partial class MatchManager
{
    public const float GollumThreshold = 10f;     // s nasazeni, nez prijde Glum (drive 15)
    const float GollumFullAnger = 50f;            // za kolik dalsich s nasazeni je Glum nejvic nastvany (drive 75)
    // Krivka: zpocatku mirny, ke konci prudce horsi - nejvic nastvany Glum je skoro nehratelny (nejde utect,
    // zabije za par ran, skoro nejde zahnat a hned je zpatky).
    static float Fury(float anger, float power) => Mathf.Pow(Mathf.Clamp01(anger), power);
    const float DropSeconds = 5f;
    const float ReturnSeconds = 20f;
    const float GollumReach = 1.6f;

    public NetworkVariable<ulong> ringHolder = new NetworkVariable<ulong>(Nobody);
    public NetworkVariable<bool> ringWorn = new NetworkVariable<bool>(false);

    // spadly prsten (vidi vsichni)
    public NetworkVariable<bool> ringDropped = new NetworkVariable<bool>(false);
    public NetworkVariable<Vector3> ringDropPos = new NetworkVariable<Vector3>();

    // Glum (server ho simuluje, klienti kresli - RingWorldView)
    public NetworkVariable<bool> gollumActive = new NetworkVariable<bool>(false);
    public NetworkVariable<Vector3> gollumPos = new NetworkVariable<Vector3>();
    public NetworkVariable<float> gollumYaw = new NetworkVariable<float>();
    public NetworkVariable<float> gollumAnger = new NetworkVariable<float>();   // 0..1

    int ringSpotIndex = -1;
    float ringWornTotal;
    float ringDropEnd;
    Vector3 lastHolderPos;
    float nextGollumHit, nextGollumPath, nextGollumSync;
    readonly System.Collections.Generic.List<Vector3> gollumPath = new System.Collections.Generic.List<Vector3>();
    int gollumCorner;
    Vector3 gollumServerPos;

    public bool HasRing(ulong playerId) => playerId != Nobody && ringHolder.Value == playerId;
    public bool WearsRing(ulong playerId) => HasRing(playerId) && ringWorn.Value;

    static NetworkObject LocalPlayerObject
    {
        get
        {
            var network = NetworkManager.Singleton;
            return network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        }
    }

    public static bool LocalHasRing => Instance != null && LocalPlayerObject != null && Instance.HasRing(LocalPlayerObject.NetworkObjectId);
    public static bool LocalWearsRing => Instance != null && LocalPlayerObject != null && Instance.WearsRing(LocalPlayerObject.NetworkObjectId);

    void ServerResetRing()
    {
        ringHolder.Value = Nobody;
        ringWorn.Value = false;
        ringDropped.Value = false;
        gollumActive.Value = false;
        gollumAnger.Value = 0f;
        ringWornTotal = 0f;
        gollumScare = 0f;
        gollumFleeUntil = 0f;
    }

    void ServerGiveRing(PlayerHero hero, int spotIndex)
    {
        ringHolder.Value = hero.NetworkObjectId;
        ringWorn.Value = false;
        ringSpotIndex = spotIndex;
        ringWornTotal = 0f;   // z jeskyne cisty
        lastHolderPos = hero.transform.position;
    }

    // Nositel s nasazenym prstenem nekoho zranil: prsten mu sklouzne (uz neni neviditelny).
    public void ServerRingSlips(PlayerHero attacker)
    {
        if (!IsServer || attacker == null || !ringWorn.Value || !HasRing(attacker.NetworkObjectId)) return;
        ringWorn.Value = false;
        RingSlippedClientRpc(attacker.NetworkObjectId);
    }

    [ClientRpc]
    void RingSlippedClientRpc(ulong playerId)
    {
        var local = LocalPlayerObject;
        if (local != null && local.NetworkObjectId == playerId)
            CaptureUI.Announce("Prsten ti sklouzl z prstu - zase tě vidí", PickupSpot.KindColor(PickupKind.Ring), 2.5f);
    }

    // Po sundani (i kdyz sklouzne) jde prsten znovu nasadit az za RingCooldown s. Kazdy (server i klienti) si
    // hlida prechod nasazeny -> sundany sam (server pro kontrolu, klient pro odpocet v HUD).
    public const float RingCooldown = 2f;
    bool ringWasWorn;
    float ringRemovedAt = -100f;
    public float RingCooldownLeft => Mathf.Max(0f, ringRemovedAt + RingCooldown - Time.time);
    public static float LocalRingCooldown => Instance != null && LocalHasRing ? Instance.RingCooldownLeft : 0f;

    void RingCooldownTick()
    {
        bool worn = IsSpawned && ringWorn.Value;
        if (ringWasWorn && !worn) ringRemovedAt = Time.time;
        if (IsSpawned && ringHolder.Value == Nobody) ringRemovedAt = -100f;   // novy nositel zacina bez cekani
        ringWasWorn = worn;
    }

    // T: nasadit / sundat (jen nositel, jen zivy; nasadit az po cooldownu).
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ToggleRingRpc(RpcParams rpcParams = default)
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out var client)) return;
        var player = client.PlayerObject;
        if (player == null || !HasRing(player.NetworkObjectId)) return;
        var health = player.GetComponent<Health>();
        if (health == null || health.currentHealth.Value <= 0f) return;
        if (!ringWorn.Value && RingCooldownLeft > 0.15f) return;   // (maly prostor na zpozdeni site)
        ringWorn.Value = !ringWorn.Value;
    }

    int RingSpotIndex()
    {
        var spots = PickupSpot.Sorted;
        if (ringSpotIndex >= 0 && ringSpotIndex < spots.Count && spots[ringSpotIndex].kind == PickupKind.Ring) return ringSpotIndex;
        return spots.FindIndex(s => s != null && s.kind == PickupKind.Ring);
    }

    // Prsten zmizel ze sveta (nikdo ho nesebral / vzal ho Glum): za ReturnSeconds zpatky v jeskyni.
    void ServerRingVanish()
    {
        ringDropped.Value = false;
        ringHolder.Value = Nobody;
        ringWorn.Value = false;
        ringWornTotal = 0f;
        gollumActive.Value = false;
        int index = RingSpotIndex();
        if (index >= 0 && index < pickupRespawnAt.Length)
            pickupRespawnAt[index] = Time.time + ReturnSeconds;
    }

    // 10x za sekundu (z ServerPickupTick): smrt nositele -> prsten na zem; sebrani ze zeme; konec lezeni.
    void ServerRingTick()
    {
        if (ringHolder.Value != Nobody)
        {
            var holder = SpawnedPlayer(ringHolder.Value);
            var health = holder != null ? holder.GetComponent<Health>() : null;
            if (health != null && health.currentHealth.Value > 0f)
            {
                lastHolderPos = holder.transform.position;
                return;
            }
            // nositel mrtvy / odesel: prsten spadne, kde byl
            ringHolder.Value = Nobody;
            ringWorn.Value = false;
            ringDropPos.Value = GroundAt(lastHolderPos);
            ringDropped.Value = true;
            ringDropEnd = Time.time + DropSeconds;
            return;
        }

        if (!ringDropped.Value) return;
        if (Time.time >= ringDropEnd || IsOver) { ServerRingVanish(); return; }

        foreach (var client in PlayerSlots())
        {
            var player = client.PlayerObject;
            if (player == null) continue;
            var health = player.GetComponent<Health>();
            var hero = player.GetComponent<PlayerHero>();
            if (health == null || hero == null || health.currentHealth.Value <= 0f || hero.IsJoining || BotBrain.IsBot(player.gameObject)) continue;
            Vector3 d = player.transform.position - ringDropPos.Value;
            float dy = d.y;
            d.y = 0f;
            if (d.magnitude > 1.2f || dy < -0.6f || dy > 2.5f) continue;
            ringDropped.Value = false;
            ringHolder.Value = player.NetworkObjectId;
            ringWorn.Value = false;
            lastHolderPos = player.transform.position;
            RingPickedClientRpc(player.NetworkObjectId);
            break;
        }
    }

    [ClientRpc]
    void RingPickedClientRpc(ulong playerId)
    {
        var local = LocalPlayerObject;
        if (local != null && local.NetworkObjectId == playerId)
            CaptureUI.Announce("Sebral jsi Prsten  ·  [T] nasadit / sundat", PickupSpot.KindColor(PickupKind.Ring), 3.5f);
    }

    // ---------------- Glum (kazdy snimek na serveru) ----------------

    void ServerGollumUpdate()
    {
        if (IsLobby) return;
        bool holderAlive = ringHolder.Value != Nobody;
        if (holderAlive && ringWorn.Value && !IsOver) ringWornTotal += Time.deltaTime;

        bool wanted = (holderAlive || ringDropped.Value) && ringWornTotal >= GollumThreshold && !IsOver;
        if (!wanted)
        {
            if (gollumActive.Value) gollumActive.Value = false;
            return;
        }

        float anger = Mathf.Clamp01((ringWornTotal - GollumThreshold) / GollumFullAnger);
        Vector3 target;
        Health victim = null;
        PlayerHero victimHero = null;
        if (holderAlive)
        {
            var holder = SpawnedPlayer(ringHolder.Value);
            if (holder == null) return;
            target = holder.transform.position;
            victim = holder.GetComponent<Health>();
            victimHero = holder.GetComponent<PlayerHero>();
        }
        else target = ringDropPos.Value;

        // zahnany: chvili utika pryc od cile, pak zmizi; po case se vrati odjinud
        if (Time.time < gollumFleeUntil)
        {
            if (gollumActive.Value && Time.time - gollumFleeStart < 2.5f)
            {
                Vector3 away = gollumServerPos - target;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                away.Normalize();
                gollumServerPos = GroundAt(gollumServerPos + away * Mathf.Lerp(6f, 12f, anger) * Time.deltaTime, gollumServerPos.y);
                gollumYaw.Value = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
                gollumPos.Value = gollumServerPos;
            }
            else if (gollumActive.Value)
                gollumActive.Value = false;
            return;
        }

        if (!gollumActive.Value)
        {
            gollumScare = 0f;
            if (!BotNavigation.Ready) BotNavigation.EnsureBuilt();   // cesta po mape (i bez botu)
            gollumServerPos = SpawnPointNear(target);
            gollumJumpT = -1f;
            gollumAir.Value = false;
            gollumProgressDist = float.MaxValue;
            gollumPath.Clear();
            nextGollumPath = 0f;
            gollumActive.Value = true;
            GollumAppearedClientRpc(ringHolder.Value);
            nextGollumLine = Time.time + 0.6f;   // (az si klienti postavu zobrazi)
        }

        // pohyb: po ctyrech po navigacni siti, jinak primo (i skrz zdi), vzdy pri zemi; pres prekazky a na
        // vyvysena mista skace; kdyz se nehne z mista, skoci kus k cili (nikde neuvizne)
        float speed = Mathf.Lerp(4.5f, 13.5f, Fury(anger, 1.4f));   // hrac bezi 9,5
        float dist = Vector3.Distance(new Vector3(gollumServerPos.x, 0f, gollumServerPos.z), new Vector3(target.x, 0f, target.z));
        if (gollumJumpT >= 0f)
            GollumJumpStep();
        else
        {
            Vector3 next = NextGollumStep(target);
            Vector3 to = next - gollumServerPos;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0004f && dist > GollumReach * 0.7f)
            {
                Vector3 dir = to.normalized;
                gollumYaw.Value = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                float dy = target.y - gollumServerPos.y;
                bool onPath = gollumPath.Count > 0;
                if (onPath && Blocked(gollumServerPos, dir))
                    StartGollumJump(LandingAhead(gollumServerPos, dir, 2.2f));            // prekazka (plot, bedna...)
                else if (onPath && gollumCorner < gollumPath.Count && gollumPath[gollumCorner].y - gollumServerPos.y > 0.6f
                         && to.magnitude < 2.5f)
                    StartGollumJump(gollumPath[gollumCorner]);                              // schod / vyvyseni na ceste
                else if (dist < 7f && Mathf.Abs(dy) > 1.5f)
                    StartGollumJump(GroundAt(target, target.y));                            // cil na strese / dole
                else
                {
                    Vector3 step = dir * Mathf.Min(to.magnitude, speed * Time.deltaTime);
                    gollumServerPos = GroundAt(gollumServerPos + step, gollumServerPos.y);
                }
            }

            // uviznuti: za 1,2 s se k cili skoro nepriblizil -> skok kus k nemu
            if (Time.time >= nextGollumStuckCheck)
            {
                if (dist > GollumReach && gollumProgressDist - dist < 0.4f && gollumJumpT < 0f)
                {
                    Vector3 toward = new Vector3(target.x - gollumServerPos.x, 0f, target.z - gollumServerPos.z).normalized;
                    Vector3 landing = dist < 6f ? GroundAt(target, target.y) : LandingAhead(gollumServerPos, toward, 5f, Mathf.Max(3.5f, target.y - gollumServerPos.y + 2f));
                    StartGollumJump(landing);
                    gollumPath.Clear();
                    nextGollumPath = Time.time + 0.6f;
                }
                gollumProgressDist = dist;
                nextGollumStuckCheck = Time.time + 1.2f;
            }
        }
        // daleko pozadu (napr. nositel v jine casti mapy): priskoci za nej, at honicka nekonci
        if (dist > Mathf.Lerp(60f, 25f, anger)) { gollumServerPos = SpawnPointNear(target); gollumJumpT = -1f; gollumAir.Value = false; }

        // obcas promluvi (nastvany casteji); hned po objeveni taky
        if (Time.time >= nextGollumLine)
        {
            nextGollumLine = Time.time + Random.Range(Mathf.Lerp(12f, 6f, anger), Mathf.Lerp(20f, 11f, anger));
            GollumSpeakClientRpc(Random.Range(0, 1000));
        }

        if (Time.time >= nextGollumSync)
        {
            nextGollumSync = Time.time + 0.05f;
            gollumPos.Value = gollumServerPos;
            gollumAnger.Value = anger;
        }

        // spadly prsten sebere Glum -> zmizi (a vrati se do jeskyne)
        if (!holderAlive)
        {
            if (dist < 1.2f) ServerRingVanish();
            return;
        }

        // utok na nositele
        if (victim != null && victim.currentHealth.Value > 0f && dist < GollumReach
            && Mathf.Abs(target.y - gollumServerPos.y) < 2.2f && Time.time >= nextGollumHit)
        {
            nextGollumHit = Time.time + Mathf.Lerp(1.2f, 0.35f, Fury(anger, 1.3f));
            if (victimHero != null) victimHero.ServerDeathCause = "env:glum";
            Combat.DamagePlayer(null, victim, Mathf.Lerp(12f, 85f, Fury(anger, 1.6f)), "env:glum");
            if (victimHero != null && victim.currentHealth.Value > 0f) victimHero.ServerDeathCause = "";
            GollumHitClientRpc(target + Vector3.up);
        }
    }

    // ---------------- zahaneni ----------------
    // Poskozeni Glumovi se scita (4 s bez zasahu = znovu od nuly). Kdyz dosahne meze, uteče: klidny Glum se zaplasi
    // snadno a na dlouho, nastvany vydrzi mnohem vic a vrati se brzy.
    float gollumScare, gollumLastHit, gollumFleeUntil, gollumFleeStart;

    public static float ScareNeeded(float anger) => Mathf.Lerp(40f, 900f, Fury(anger, 1.7f));
    static float FleeSeconds(float anger) => Mathf.Lerp(15f, 2.5f, Fury(anger, 0.8f));

    public void ServerDamageGollum(GameObject attacker, float amount)
    {
        if (!IsServer || !gollumActive.Value || Time.time < gollumFleeUntil || amount <= 0f) return;
        float anger = gollumAnger.Value;
        if (Time.time - gollumLastHit > 4f) gollumScare = 0f;
        gollumLastHit = Time.time;
        gollumScare += amount;
        GollumHurtClientRpc(gollumServerPos + Vector3.up * 1.1f);
        if (gollumScare < ScareNeeded(anger)) return;

        gollumScare = 0f;
        gollumFleeStart = Time.time;
        gollumFleeUntil = Time.time + FleeSeconds(anger);
        gollumJumpT = -1f;
        gollumAir.Value = false;
        GollumFledClientRpc(ringHolder.Value);
    }

    float nextGollumLine;

    [ClientRpc]
    void GollumSpeakClientRpc(int seed) => RingWorldView.Speak(seed);

    [ClientRpc]
    void GollumHurtClientRpc(Vector3 position)
    {
        Fx.Sparks(position, new Color(0.9f, 0.8f, 0.6f));
    }

    [ClientRpc]
    void GollumFledClientRpc(ulong holderId)
    {
        ProceduralSfx.Play(ProceduralSfx.Hurt, gollumPos.Value + Vector3.up, 1f);
        var local = LocalPlayerObject;
        if (local != null && local.NetworkObjectId == holderId)
            CaptureUI.Announce("Glum utekl… ale vrátí se", new Color(0.7f, 0.75f, 0.55f), 2.5f);
    }

    // ---------------- skoky ----------------
    public NetworkVariable<bool> gollumAir = new NetworkVariable<bool>(false);   // ve vzduchu (poza skoku)
    float gollumJumpT = -1f, gollumJumpDuration, gollumJumpHeight;
    Vector3 gollumJumpFrom, gollumJumpTo;
    float nextGollumStuckCheck, gollumProgressDist = float.MaxValue;

    void StartGollumJump(Vector3 landing)
    {
        gollumJumpFrom = gollumServerPos;
        gollumJumpTo = landing;
        float flat = Vector3.Distance(new Vector3(landing.x, 0f, landing.z), new Vector3(gollumServerPos.x, 0f, gollumServerPos.z));
        float rise = landing.y - gollumServerPos.y;
        gollumJumpHeight = 0.9f + Mathf.Max(0f, rise) + flat * 0.08f;
        gollumJumpDuration = Mathf.Clamp(0.4f + flat * 0.07f + Mathf.Abs(rise) * 0.05f, 0.45f, 1.1f);
        gollumJumpT = 0f;
        gollumAir.Value = true;
    }

    void GollumJumpStep()
    {
        gollumJumpT += Time.deltaTime / gollumJumpDuration;
        float t = Mathf.Clamp01(gollumJumpT);
        Vector3 p = Vector3.Lerp(gollumJumpFrom, gollumJumpTo, t);
        p.y += Mathf.Sin(t * Mathf.PI) * gollumJumpHeight;
        gollumServerPos = p;
        if (t >= 1f)
        {
            gollumJumpT = -1f;
            gollumAir.Value = false;
            gollumServerPos = GroundAt(gollumJumpTo, gollumJumpTo.y);
        }
    }

    // Prekazka tesne pred nim ve vysce kolen (ne hraci, ne on sam)?
    static bool Blocked(Vector3 feet, Vector3 dir)
    {
        foreach (var hit in Physics.RaycastAll(feet + Vector3.up * 0.45f, dir, 0.9f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
            if (hit.collider.GetComponentInParent<GollumHitbox>() != null) continue;
            return true;
        }
        return false;
    }

    // Kam dopadne skok o 'distance' dopredu: nejvyssi povrch do 'climb' m nad soucasnou vyskou.
    static Vector3 LandingAhead(Vector3 feet, Vector3 dir, float distance, float climb = 3.5f)
    {
        Vector3 p = feet + dir * distance;
        foreach (var hit in SortedHits(new Vector3(p.x, feet.y + climb, p.z), climb + 6f))
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
            if (hit.collider.GetComponentInParent<GollumHitbox>() != null) continue;
            return hit.point;
        }
        return p;
    }

    Vector3 NextGollumStep(Vector3 target)
    {
        if (Time.time >= nextGollumPath)
        {
            nextGollumPath = Time.time + 0.5f;
            gollumPath.Clear();
            gollumCorner = 0;
            if (BotNavigation.Ready
                && NavMesh.SamplePosition(gollumServerPos, out var from, 2.5f, BotNavigation.Filter)
                && NavMesh.SamplePosition(target, out var to, 2.5f, BotNavigation.Filter))
            {
                var path = new NavMeshPath();
                if (NavMesh.CalculatePath(from.position, to.position, BotNavigation.Filter, path) && path.status == NavMeshPathStatus.PathComplete)
                {
                    gollumPath.AddRange(path.corners);
                    gollumCorner = gollumPath.Count > 1 ? 1 : 0;
                }
            }
        }
        while (gollumCorner < gollumPath.Count)
        {
            Vector3 c = gollumPath[gollumCorner] - gollumServerPos;
            c.y = 0f;
            if (c.magnitude > 0.5f) return gollumPath[gollumCorner];
            gollumCorner++;
        }
        return target;
    }

    // Kde se Glum objevi: kus za cilem (15-25 m) na navigacni siti; v jeskyni (bez site) 10 m za nim pri zemi.
    Vector3 SpawnPointNear(Vector3 target)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector2 r = Random.insideUnitCircle.normalized * Random.Range(15f, 25f);
            Vector3 candidate = target + new Vector3(r.x, 0f, r.y);
            if (BotNavigation.Ready && NavMesh.SamplePosition(candidate, out var hit, 4f, BotNavigation.Filter) && Mathf.Abs(hit.position.y - target.y) < 4f)
                return hit.position;
        }
        Vector2 back = Random.insideUnitCircle.normalized * 10f;
        return GroundAt(target + new Vector3(back.x, 0f, back.y), target.y);
    }

    // Zem pod bodem (prvni pevny povrch pod nim, ne hraci); kdyz nic, puvodni vyska.
    static Vector3 GroundAt(Vector3 p, float fallbackY = float.NaN)
    {
        foreach (var hit in SortedHits(p + Vector3.up * 1.6f, 8f))
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
            if (hit.collider.GetComponentInParent<GollumHitbox>() != null) continue;   // Glum sam sebe
            return hit.point;
        }
        return float.IsNaN(fallbackY) ? p : new Vector3(p.x, fallbackY, p.z);
    }

    [ClientRpc]
    void GollumAppearedClientRpc(ulong holderId)
    {
        var local = LocalPlayerObject;
        if (local != null && local.NetworkObjectId == holderId)
            CaptureUI.Announce("Něco tě sleduje… chce svůj poklad", new Color(0.7f, 0.75f, 0.55f), 3.5f);
    }

    [ClientRpc]
    void GollumHitClientRpc(Vector3 position)
    {
        ProceduralSfx.Play(ProceduralSfx.Hit, position, 0.9f);
        Fx.Sparks(position, new Color(0.65f, 0.7f, 0.5f));
    }
}
