using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Vyber mapy v lobby. Host muze zvolit jen mapu podporujici aktualni rezim.
// Pri startu zapasu se vybrana mapa zapne a ostatni vypnou (u vsech stejne) - spawny, body (CapturePoint_N), KillPlane
// i navigacni sit botu se pak hledaji jen v ni (GameObject.Find najde jen aktivni objekty).
public partial class MatchManager
{
    public const int MapDomasov = 0;
    public const int MapHanamura = 1;
    public const int MapDust2 = 2;
    public const int MapDomasovPush = 3;   // kopie Domasova jen pro rezim Tlacenice (PushSetup)
    public const int MapSapa = 4;

    public static readonly string[] MapNames = { "Domašov", "Hanamura", "Dust II", "Domašov (Tlačenice)", "Sapa" };
    // Korenovy objekt mapy ve scene (prazdny = puvodni mapa, ta je ve scene vzdy).
    static readonly string[] MapRoots = { "", "Map_Hanamura", "Map_Dust2", "Map_DomasovPush", "Map_Sapa" };
    const string DomasovRoot = "Map-Domasov";
    public static readonly Vector3 HanamuraOrigin = new Vector3(1000f, 0f, 0f);   // Hanamura stoji stranou od chat
    // Cela mapa i KillPlane lezi nad nulou, mimo vyskovy bonus tunelu na Domasove.
    public static readonly Vector3 Dust2Origin = new Vector3(2000f, 50f, 0f);
    // Kopie Domasova pro Tlacenici: stejna vyska (tunel pod zemi funguje stejne), jen o 3 km stranou.
    public static readonly Vector3 DomasovPushOrigin = new Vector3(3000f, 0f, 0f);
    // Importovana mapa vcetne pozadi je nad nulou, mimo vyskovy bonus tunelu.
    public static readonly Vector3 SapaOrigin = new Vector3(4000f, 200f, 0f);

    public NetworkVariable<int> mapIndex = new NetworkVariable<int>(MapDomasov);

    public int MapIndex => Mathf.Clamp(mapIndex.Value, 0, MapNames.Length - 1);
    public string MapName => MapLabel(MapIndex);

    // Jak mapy vidi hrac: kopie Domasova pro Tlacenici je proste "Domasov" - v nabidce map neni zvlast,
    // tlacitko Domasov v rezimu Tlacenice vybere ji (MapFor).
    public static string MapLabel(int map) => MapNames[ListedMap(Mathf.Clamp(map, 0, MapNames.Length - 1))];
    public static bool MapListed(int map) => map != MapDomasovPush;
    public static int ListedMap(int map) => map == MapDomasovPush ? MapDomasov : map;
    public static int MapFor(int listed, int mode) => listed == MapDomasov && mode == ModePush ? MapDomasovPush : listed;
    public static bool MapPlayable(int listed, int mode) => MapReady(MapFor(listed, mode)) && MapSupportsMode(MapFor(listed, mode), mode);

    public void SetMap(int index)
    {
        if (!IsServer || !IsLobby) return;
        index = Mathf.Clamp(index, 0, MapNames.Length - 1);
        if (MapSupportsMode(index, gameMode.Value)) mapIndex.Value = index;
    }

    // Tlacenice jde hrat jen na sve mape a ta je jen pro ni.
    public static bool MapSupportsMode(int map, int mode) => map >= 0 && map < MapNames.Length
        && (map == MapDomasovPush ? mode == ModePush
            : mode == ModeDeathmatch || (mode == ModeAttack && map != MapDust2));

    public static int NextMapForMode(int current, int mode)
    {
        for (int step = 1; step <= MapNames.Length; step++)
        {
            int candidate = (Mathf.Clamp(current, 0, MapNames.Length - 1) + step) % MapNames.Length;
            if (MapSupportsMode(candidate, mode)) return candidate;
        }
        return MapDomasov;
    }

    public bool SelectedMapSupportsMode => MapSupportsMode(MapIndex, gameMode.Value);

    // Je mapa ve scene postavena (da se na ni hrat)?
    public static bool MapReady(int index)
    {
        if (index < 0 || index >= MapRoots.Length) return false;
        if (string.IsNullOrEmpty(MapRoots[index])) return true;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == MapRoots[index]) return true;
        return false;
    }

    public bool SelectedMapReady => MapReady(MapIndex);

    // ---------------- prepnuti mapy ----------------

    static int appliedMap = MapDomasov;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetAppliedMap() => appliedMap = MapDomasov;

    // Po nacteni sceny vzdy chaty (kdyby byla scena ulozena se zapnutou Hanamurou z prohlizeni v editoru).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InitialMap()
    {
        appliedMap = -1;
        ApplyMap(MapDomasov);
    }

    static GameObject FindRoot(string name)
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == name) return root;
        return null;
    }

    // Zapne mapu 'index' a ostatni vypne. Vola se u vsech: pri startu zapasu, po pripojeni do bezici hry a po odpojeni (zpet na chaty).
    public static void ApplyMap(int index)
    {
        if (!MapReady(index)) index = MapDomasov;
        bool changed = appliedMap != index;
        // Nejdriv obnovit prostredi predchozi mapy, potom zapnout nove.
        // Jinak OnDisable stare mapy prepise svetla prave zapnute mapy.
        for (int map = 0; map < MapNames.Length; map++)
        {
            if (map == index) continue;
            var root = FindRoot(map == MapDomasov ? DomasovRoot : MapRoots[map]);
            if (root == null) continue;
            if (root.activeSelf) { root.SetActive(false); changed = true; }
        }
        var selected = FindRoot(index == MapDomasov ? DomasovRoot : MapRoots[index]);
        if (selected != null && !selected.activeSelf) { selected.SetActive(true); changed = true; }
        appliedMap = index;
        if (!changed) return;

        Physics.SyncTransforms();

        // co si systemy pamatuji z predchozi mapy
        ResetKillHeight();
        SpawnZone.ResetCache();
        MapSpawnPoints.ResetCache();
        BotNavigation.Invalidate();
        BotBrain.ForgetMap();
        Debug.Log($"[Mapa] Hraje se na mape {MapNames[index]}.");
    }

    // Oblast aktivni mapy (navigacni sit botu).
    public static Bounds ActiveMapBounds
    {
        get
        {
            if ((appliedMap == MapDust2 || appliedMap == MapSapa) && MapSpawnPoints.TryGetBounds(out var bounds)) return bounds;
            if (appliedMap == MapHanamura)
                return new Bounds(HanamuraOrigin + new Vector3(0f, 20f, 75f), new Vector3(240f, 120f, 280f));
            if (appliedMap == MapDomasovPush)
                return new Bounds(DomasovPushOrigin + new Vector3(0f, 20f, 30f), new Vector3(600f, 200f, 600f));
            return new Bounds(new Vector3(0f, 20f, 30f), new Vector3(600f, 200f, 600f));
        }
    }

    [ClientRpc]
    void MapClientRpc(int index) => ApplyMap(index);

    // Server pri startu zapasu: mapa u sebe i u vsech klientu (pred presunem hracu na spawny).
    void ServerApplyMap()
    {
        ApplyMap(MapIndex);
        MapClientRpc(MapIndex);
        // boti: sit pro novou mapu hned (ne az pri prvnim hledani cesty)
        if (BotBrain.All.Count > 0)
            BotNavigation.EnsureBuilt();
    }
}
