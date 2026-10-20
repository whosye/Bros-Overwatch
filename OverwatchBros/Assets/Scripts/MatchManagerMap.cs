using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Vyber mapy v lobby (voli host, vsichni ho vidi): puvodni mapa s chatami, nebo Hanamura.
// Kazda mapa je ve scene pod vlastnim korenovym objektem (Map-Domasov, Map_Hanamura); mapa se da hrat, jen kdyz ve scene je.
// Pri startu zapasu se vybrana mapa zapne a ostatni vypnou (u vsech stejne) - spawny, body (CapturePoint_N), KillPlane
// i navigacni sit botu se pak hledaji jen v ni (GameObject.Find najde jen aktivni objekty).
public partial class MatchManager
{
    public const int MapChaty = 0;
    public const int MapHanamura = 1;

    public static readonly string[] MapNames = { "Chaty", "Hanamura" };
    // Korenovy objekt mapy ve scene (prazdny = puvodni mapa, ta je ve scene vzdy).
    static readonly string[] MapRoots = { "", "Map_Hanamura" };
    const string DomasovRoot = "Map-Domasov";
    public static readonly Vector3 HanamuraOrigin = new Vector3(1000f, 0f, 0f);   // Hanamura stoji stranou od chat

    public NetworkVariable<int> mapIndex = new NetworkVariable<int>(MapChaty);

    public int MapIndex => Mathf.Clamp(mapIndex.Value, 0, MapNames.Length - 1);
    public string MapName => MapNames[MapIndex];

    public void SetMap(int index)
    {
        if (IsServer && IsLobby)
            mapIndex.Value = Mathf.Clamp(index, 0, MapNames.Length - 1);
    }

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

    static int appliedMap = MapChaty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetAppliedMap() => appliedMap = MapChaty;

    // Po nacteni sceny vzdy chaty (kdyby byla scena ulozena se zapnutou Hanamurou z prohlizeni v editoru).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InitialMap()
    {
        appliedMap = -1;
        ApplyMap(MapChaty);
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
        if (!MapReady(index)) index = MapChaty;
        var domasov = FindRoot(DomasovRoot);
        var hanamura = FindRoot(MapRoots[MapHanamura]);
        bool changed = appliedMap != index
            || (domasov != null && domasov.activeSelf != (index == MapChaty))
            || (hanamura != null && hanamura.activeSelf != (index == MapHanamura));
        appliedMap = index;
        if (!changed) return;

        if (domasov != null) domasov.SetActive(index == MapChaty);
        if (hanamura != null) hanamura.SetActive(index == MapHanamura);
        Physics.SyncTransforms();

        // co si systemy pamatuji z predchozi mapy
        ResetKillHeight();
        SpawnZone.ResetCache();
        BotNavigation.Invalidate();
        BotBrain.ForgetMap();
        Debug.Log($"[Mapa] Hraje se na mape {MapNames[index]}.");
    }

    // Oblast aktivni mapy (navigacni sit botu).
    public static Bounds ActiveMapBounds => appliedMap == MapHanamura
        ? new Bounds(HanamuraOrigin + new Vector3(0f, 20f, 75f), new Vector3(240f, 120f, 280f))
        : new Bounds(new Vector3(0f, 20f, 30f), new Vector3(600f, 200f, 600f));

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
