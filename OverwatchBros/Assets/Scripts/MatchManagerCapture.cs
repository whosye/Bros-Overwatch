using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Herni rezim "Dobyvani bodu" (druha cast MatchManageru).
// Na mape se postupne objevi 3 body (barevne ohraniceny obdelnik). Bod je nejdriv zamceny; po odemceni ho zabira tym,
// jehoz hrac v nem stoji. Kdyz jsou v obdelniku hraci obou tymu, je bod sporny a nezabira se nic - souper se musi
// vyradit. Tym, ktery bod zabere na 100 %, ziska bod do skore; pak se objevi dalsi bod jinde. Hra bezi dal
// bez preruseni: hraci zustavaji, kde jsou, a na novem miste u dalsiho bodu se ozivi az po smrti.
// Vyhrava tym, ktery ziska 2 body ze 3.
public partial class MatchManager
{
    public const int ModeDeathmatch = 0;
    public const int ModeCapture = 1;

    public const int StateLocked = 0, StateFree = 1, StateTeam0 = 2, StateTeam1 = 3, StateContested = 4;

    public const int CapturePointCount = 3;
    public const int CapturePointsToWin = 2;
    // Vychozi sirka x delka obdelniku v metrech. U bodu umistenych ve scene (CapturePoint_N) urcuje velikost
    // Scale objektu (X = sirka, Z = delka) a natoceni jeho rotace kolem Y.
    public static readonly Vector2 CaptureSize = new Vector2(10f, 7f);

    const float FirstLockSeconds = 12f;
    const float NextLockSeconds = 10f;
    const float SpawnDistance = 30f;

    public NetworkVariable<int> gameMode = new NetworkVariable<int>(ModeDeathmatch);
    public NetworkVariable<int> captureSeconds = new NetworkVariable<int>(20);
    public NetworkVariable<int> pointIndex = new NetworkVariable<int>(0);
    public NetworkVariable<int> pointState = new NetworkVariable<int>(StateLocked);
    public NetworkVariable<Vector3> pointPosition = new NetworkVariable<Vector3>();
    public NetworkVariable<float> pointYaw = new NetworkVariable<float>();
    public NetworkVariable<Vector2> pointSize = new NetworkVariable<Vector2>(new Vector2(10f, 7f));
    public NetworkVariable<float> lockRemaining = new NetworkVariable<float>();
    public NetworkVariable<float> progress0 = new NetworkVariable<float>();
    public NetworkVariable<float> progress1 = new NetworkVariable<float>();

    public bool IsCapture => gameMode.Value == ModeCapture;

    // Mista oziveni u aktualniho bodu (plati na vsech klientech; posila se i v RPC, aby dorazila vcas).
    bool customSpawns;
    readonly Vector3[] customSpawn = new Vector3[2];

    // server: predem vybrana mista bodu a oziveni
    readonly Vector3[] points = new Vector3[CapturePointCount];
    readonly Vector3[,] pointSpawns = new Vector3[CapturePointCount, 2];
    readonly float[] pointYaws = new float[CapturePointCount];
    readonly Vector2[] pointSizes = new Vector2[CapturePointCount];
    float captureYaw;
    float lockTimer;

    // ---------------- nastaveni v lobby ----------------

    public void SetGameMode(int mode)
    {
        if (IsServer && IsLobby)
            gameMode.Value = mode == ModeCapture ? ModeCapture : ModeDeathmatch;
    }

    public void SetCaptureSeconds(int seconds)
    {
        if (IsServer)
            captureSeconds.Value = Mathf.Clamp(seconds, 5, 120);
    }

    // Kam se ma hrac daneho tymu ozivit. False = pouzij puvodni spawn ze sceny.
    public bool TryGetSpawn(int team, out Vector3 position)
    {
        position = customSpawn[Mathf.Clamp(team, 0, 1)];
        return customSpawns;
    }

    // ---------------- server: prubeh ----------------

    void Update()
    {
        if (IsSpawned && IsServer)
            ServerCaptureTick();

        CapturePointView.Sync(this);
    }

    // Vola ResetRound pri startu zapasu: vybere mista a pripravi prvni bod.
    void ServerBeginCapture()
    {
        ChooseCapturePoints();
        ServerApplyPoint(0, FirstLockSeconds);
    }

    void ServerApplyPoint(int index, float lockSeconds)
    {
        pointIndex.Value = index;
        pointPosition.Value = points[index];
        pointYaw.Value = pointYaws[index];
        pointSize.Value = pointSizes[index];
        progress0.Value = 0f;
        progress1.Value = 0f;
        lockTimer = lockSeconds;
        lockRemaining.Value = lockSeconds;
        pointState.Value = StateLocked;

        customSpawns = true;
        customSpawn[0] = pointSpawns[index, 0];
        customSpawn[1] = pointSpawns[index, 1];
    }

    void ServerCaptureTick()
    {
        if (!IsCapture || IsLobby || IsOver) return;

        if (lockTimer > 0f)
        {
            lockTimer -= Time.deltaTime;
            float shown = Mathf.Max(0f, Mathf.Ceil(lockTimer));
            if (!Mathf.Approximately(shown, lockRemaining.Value))
                lockRemaining.Value = shown;

            if (lockTimer > 0f) return;
            pointState.Value = StateFree;
        }

        // Kdo stoji v obdelniku.
        int inside0 = 0, inside1 = 0;
        Quaternion toLocal = Quaternion.Euler(0f, -pointYaw.Value, 0f);
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var player = client.PlayerObject;
            if (player == null) continue;

            var health = player.GetComponent<Health>();
            var hero = player.GetComponent<PlayerHero>();
            var team = player.GetComponent<PlayerTeam>();
            if (health == null || team == null || health.currentHealth.Value <= 0f || (hero != null && hero.IsJoining)) continue;

            Vector3 local = toLocal * (player.transform.position - pointPosition.Value);
            if (Mathf.Abs(local.x) > pointSize.Value.x * 0.5f || Mathf.Abs(local.z) > pointSize.Value.y * 0.5f) continue;
            if (local.y < -1.5f || local.y > 4f) continue;

            if (team.teamId.Value == 0) inside0++;
            else inside1++;
        }

        int state = StateFree;
        if (inside0 > 0 && inside1 > 0) state = StateContested;
        else if (inside0 > 0) state = StateTeam0;
        else if (inside1 > 0) state = StateTeam1;

        if (pointState.Value != state)
            pointState.Value = state;

        float step = Time.deltaTime / Mathf.Max(1f, captureSeconds.Value);
        if (state == StateTeam0)
        {
            progress0.Value = Mathf.Min(1f, progress0.Value + step);
            if (progress0.Value >= 1f) ServerPointWon(0);
        }
        else if (state == StateTeam1)
        {
            progress1.Value = Mathf.Min(1f, progress1.Value + step);
            if (progress1.Value >= 1f) ServerPointWon(1);
        }
    }

    void ServerPointWon(int team)
    {
        PointWonClientRpc(team, pointPosition.Value);
        AddScore(team, 1);
        if (matchOver.Value) return;

        int next = pointIndex.Value + 1;
        if (next >= CapturePointCount) return;

        // Dalsi bod jinde. Hra se neprerusuje: hraci zustanou, kde jsou, a na novych mistech u bodu
        // se ozivi az ti, kdo zemrou.
        ServerApplyPoint(next, NextLockSeconds);
        SpawnsClientRpc(customSpawns, customSpawn[0], customSpawn[1]);
    }

    // Zabrani bodu slysi vsichni stejne hlasite, at jsou kdekoliv.
    [ClientRpc]
    void PointWonClientRpc(int team, Vector3 position)
    {
        var listener = Camera.main != null ? Camera.main.transform.position : position;
        ProceduralSfx.Play(ProceduralSfx.CaptureWon, listener, 1f);
        Fx.Sparks(position + Vector3.up, UiKit.TeamColor(team));
        Fx.Sparks(position + Vector3.up * 2f, UiKit.TeamColor(team));
    }

    // Pozde pripojeny hrac si mista oziveni vyzada.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSpawnsRpc()
    {
        SpawnsClientRpc(customSpawns, customSpawn[0], customSpawn[1]);
    }

    [ClientRpc]
    void SpawnsClientRpc(bool custom, Vector3 spawn0, Vector3 spawn1)
    {
        customSpawns = custom;
        customSpawn[0] = spawn0;
        customSpawn[1] = spawn1;
    }

    // ---------------- server: vyber mist na mape ----------------

    // Tri mista pro body a u kazdeho misto oziveni pro oba tymy. Kdyz jsou ve scene objekty CapturePoint_1..3,
    // pouziji se ony; jinak se hledaji rovna volna mista zhruba stejne daleko od obou zakladen.
    // Oziveni u bodu N: objekty SpawnPoint_N_Team0 a SpawnPoint_N_Team1 (kdyz chybi, vybere se misto samo).
    void ChooseCapturePoints()
    {
        var base0 = GameObject.Find("SpawnPoint_Team0");
        var base1 = GameObject.Find("SpawnPoint_Team1");
        Vector3 a = base0 != null ? base0.transform.position : Vector3.zero;
        Vector3 b = base1 != null ? base1.transform.position : new Vector3(40f, 0f, 0f);

        Vector3 axis = a - b;
        axis.y = 0f;
        float length = Mathf.Max(10f, axis.magnitude);
        axis = axis.sqrMagnitude > 0.01f ? axis.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, axis);
        Vector3 middle = (a + b) * 0.5f;
        float groundY = (GroundHeight(a) + GroundHeight(b)) * 0.5f;

        // Obdelnik lezi napric spojnici zakladen (delsi stranou), at se do nej z obou stran vchazi stejne.
        captureYaw = Mathf.Atan2(-side.z, side.x) * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.Euler(0f, captureYaw, 0f);

        for (int i = 0; i < CapturePointCount; i++)
        {
            pointYaws[i] = captureYaw;
            pointSizes[i] = CaptureSize;
        }

        var chosen = new List<Vector3>();
        for (int i = 1; i <= CapturePointCount; i++)
        {
            var manual = GameObject.Find($"CapturePoint_{i}");
            if (manual == null) continue;

            // Objekt muze viset nad zemi: obdelnik si sedne na prvni povrch pod nim.
            Vector3 placed = manual.transform.position;
            foreach (var hit in SortedHits(placed + Vector3.up * 0.5f, 40f))
            {
                if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
                placed = hit.point;
                break;
            }
            chosen.Add(placed);

            // Velikost podle Scale (X, Z), natoceni podle rotace. Nezmeneny Scale 1,1,1 = vychozi velikost.
            Vector3 scale = manual.transform.lossyScale;
            pointYaws[chosen.Count - 1] = manual.transform.eulerAngles.y;
            if (scale.x > 1.01f || scale.z > 1.01f)
                pointSizes[chosen.Count - 1] = new Vector2(Mathf.Clamp(scale.x, 2f, 60f), Mathf.Clamp(scale.z, 2f, 60f));
        }

        if (chosen.Count < CapturePointCount)
        {
            chosen.Clear();
            for (int i = 0; i < CapturePointCount; i++)
            {
                pointYaws[i] = captureYaw;
                pointSizes[i] = CaptureSize;
            }

            // Kandidati: mrizka kolem stredu mapy.
            var candidates = new List<Vector3>();
            float reach = length * 0.7f;
            for (float along = -length * 0.25f; along <= length * 0.25f + 0.01f; along += 3f)
                for (float across = -reach; across <= reach + 0.01f; across += 3f)
                {
                    Vector3 probe = middle + axis * along + side * across;
                    if (!TryGround(probe, groundY, out Vector3 ground)) continue;
                    if (!AreaIsFlatAndFree(ground, rotation)) continue;

                    float d0 = Vector3.Distance(ground, a), d1 = Vector3.Distance(ground, b);
                    if (Mathf.Abs(d0 - d1) > length * 0.3f || Mathf.Min(d0, d1) < length * 0.25f) continue;

                    candidates.Add(ground);
                }

            // Prvni co nejbliz stredu, dalsi co nejdal od uz vybranych.
            while (chosen.Count < CapturePointCount && candidates.Count > 0)
            {
                int best = 0;
                float bestValue = float.MinValue;
                for (int i = 0; i < candidates.Count; i++)
                {
                    float value;
                    if (chosen.Count == 0)
                    {
                        value = -Vector3.Distance(candidates[i], middle);
                    }
                    else
                    {
                        value = float.MaxValue;
                        foreach (var taken in chosen)
                            value = Mathf.Min(value, Vector3.Distance(candidates[i], taken));
                    }

                    if (value > bestValue)
                    {
                        bestValue = value;
                        best = i;
                    }
                }

                // Body moc blizko u sebe uz nema smysl pridavat.
                if (chosen.Count > 0 && bestValue < 12f) break;

                chosen.Add(candidates[best]);
                candidates.RemoveAt(best);
            }

            // Nouzove: stred a mista vedle nej.
            Vector3[] fallback = { middle, middle + side * 14f, middle - side * 14f };
            for (int i = 0; chosen.Count < CapturePointCount; i++)
            {
                Vector3 spot = fallback[i % fallback.Length];
                if (TryGround(spot, groundY, out Vector3 ground)) spot = ground;
                else spot.y = groundY;
                chosen.Add(spot);
            }
        }

        for (int i = 0; i < CapturePointCount; i++)
        {
            points[i] = chosen[i];

            // Mista oziveni u bodu: objekty ve scene SpawnPoint_<cislo bodu>_Team<tym> (napr. SpawnPoint_1_Team0),
            // jinak se vyberou sama kus od bodu smerem k vlastni zakladne.
            pointSpawns[i, 0] = SceneSpawn(i + 1, 0, out Vector3 manual0) ? manual0 : FindSpawnNear(chosen[i], axis, side, groundY, a);
            pointSpawns[i, 1] = SceneSpawn(i + 1, 1, out Vector3 manual1) ? manual1 : FindSpawnNear(chosen[i], -axis, side, groundY, b);
            Debug.Log($"[Capture] bod {i + 1}: {points[i]}  spawn tymu 0: {pointSpawns[i, 0]}  spawn tymu 1: {pointSpawns[i, 1]}");
        }
    }

    static bool SceneSpawn(int point, int team, out Vector3 position)
    {
        var marker = GameObject.Find($"SpawnPoint_{point}_Team{team}");
        position = marker != null ? marker.transform.position : Vector3.zero;
        return marker != null;
    }

    static float GroundHeight(Vector3 position)
    {
        foreach (var hit in SortedHits(position + Vector3.up * 2f, 30f))
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;
            return hit.point.y;
        }

        return position.y;
    }

    static RaycastHit[] SortedHits(Vector3 from, float distance)
    {
        var hits = Physics.RaycastAll(from, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        return hits;
    }

    // Zem pod danym mistem: rovna, ve vysce terenu (ne strecha budovy).
    static bool TryGround(Vector3 position, float groundY, out Vector3 ground)
    {
        ground = position;
        foreach (var hit in SortedHits(new Vector3(position.x, groundY + 40f, position.z), 90f))
        {
            if (hit.collider.GetComponentInParent<NetworkObject>() != null) continue;

            ground = hit.point;
            return hit.normal.y > 0.95f && Mathf.Abs(hit.point.y - groundY) < 1.5f;
        }

        return false;
    }

    static bool AreaIsFlatAndFree(Vector3 center, Quaternion rotation)
    {
        float halfX = CaptureSize.x * 0.5f, halfZ = CaptureSize.y * 0.5f;
        foreach (var corner in new[] { new Vector3(halfX, 0f, halfZ), new Vector3(-halfX, 0f, halfZ), new Vector3(halfX, 0f, -halfZ), new Vector3(-halfX, 0f, -halfZ) })
        {
            if (!TryGround(center + rotation * corner, center.y, out Vector3 ground)) return false;
            if (Mathf.Abs(ground.y - center.y) > 0.4f) return false;
        }

        // Nad plochou nesmi nic stat (zdi, bedny).
        return !Physics.CheckBox(center + Vector3.up * 1.4f, new Vector3(halfX + 0.5f, 1f, halfZ + 0.5f), rotation, ~0, QueryTriggerInteraction.Ignore);
    }

    // Misto oziveni tymu u bodu: kus od bodu smerem k vlastni zakladne, na volne zemi.
    static Vector3 FindSpawnNear(Vector3 point, Vector3 towardBase, Vector3 side, float groundY, Vector3 fallback)
    {
        foreach (float distance in new[] { SpawnDistance, SpawnDistance - 5f, SpawnDistance + 5f, SpawnDistance - 10f, SpawnDistance - 14f })
            foreach (float lateral in new[] { 0f, 6f, -6f, 12f, -12f })
            {
                Vector3 probe = point + towardBase * distance + side * lateral;
                if (!TryGround(probe, groundY, out Vector3 ground)) continue;
                if (Physics.CheckSphere(ground + Vector3.up * 1.6f, 1.2f, ~0, QueryTriggerInteraction.Ignore)) continue;

                // Kolem musi byt rovna zem (hraci se ozivuji kousek vedle sebe; ne na hrane srazu).
                bool solid = true;
                for (int i = 0; i < 8 && solid; i++)
                {
                    float angle = i * Mathf.PI / 4f;
                    Vector3 around = ground + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 3f;
                    solid = TryGround(around, ground.y, out Vector3 near) && Mathf.Abs(near.y - ground.y) < 0.4f;
                }
                if (!solid) continue;

                return ground + Vector3.up * 0.1f;
            }

        return fallback;
    }
}
