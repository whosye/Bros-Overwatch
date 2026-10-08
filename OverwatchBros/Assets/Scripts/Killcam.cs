using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Killcam: kdyz hrace nekdo zabije, chvili po smrti se mu prehraje posledni okamziky z pohledu toho, kdo ho zabil
// (jako play of the game). Zaznam bere z vlastniho nahravani (ReplayCapture) - v nem jsou polohy vsech hracu,
// takze to funguje i u botu a nic se neposila po siti. Pohled vraha: jeho poloha a natoceni, pohled dolu/nahoru
// smerem k obeti. Skonci sam, pri oziveni nebo mezernikem.
public class Killcam : MonoBehaviour
{
    public const float StartDelay = 0.35f;   // po smrti (dohrat zasah)
    public const float Footage = 3.5f;       // kolik sekund pred smrti se ukaze
    public const float RespawnDelay = 4.5f;  // oziveni az po killcamu (PlayerRespawn)

    static Killcam instance;
    float startAt = -1f;
    ulong killerId, victimId;
    string killerName;
    Color killerColor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    // Vola kill feed (MatchManager) u mistniho hrace, kdyz zemrel on.
    public static void OnLocalDeath(NetworkObject killer, NetworkObject victim)
    {
        if (killer == null || victim == null || killer == victim) return;
        if (instance == null)
            instance = new GameObject("Killcam").AddComponent<Killcam>();
        var hero = killer.GetComponent<PlayerHero>();
        var team = killer.GetComponent<PlayerTeam>();
        instance.killerId = killer.NetworkObjectId;
        instance.victimId = victim.NetworkObjectId;
        instance.killerName = hero != null ? hero.DisplayName + (hero.Hero != null ? $"  <size=70%>({hero.Hero.heroName})</size>" : "") : "?";
        instance.killerColor = team != null ? UiKit.TeamColor(team.teamId.Value) : Color.white;
        instance.startAt = Time.unscaledTime + StartDelay;
    }

    void Update()
    {
        if (startAt > 0f && Time.unscaledTime >= startAt)
        {
            startAt = -1f;
            Begin();
        }

        if (!ReplayPlayer.IsKillcam) return;

        // konec: dohrano, hrac uz zije (oziveni), konec zapasu nebo mezernik
        var match = MatchManager.Instance;
        bool alive = LocalAlive();
        bool skip = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        if (ReplayPlayer.Finished || alive || skip || match == null || match.IsOver || match.IsLobby)
            ReplayPlayer.Stop();
    }

    void Begin()
    {
        var match = MatchManager.Instance;
        if (match == null || match.IsOver || match.IsLobby || ReplayPlayer.Active || LocalAlive()) return;
        var capture = ReplayCapture.Local;
        var clip = capture != null ? capture.Extract(Footage) : null;
        if (clip == null) return;

        clip.povId = (int)killerId;
        ReplayPlayer.Play(clip, (int)victimId);
        CaptureUI.Announce($"ZABIL TĚ  <color=#{ColorUtility.ToHtmlStringRGB(killerColor)}>{killerName}</color>   <size=60%>(mezerník = přeskočit)</size>",
            Color.white, Footage);
    }

    static bool LocalAlive()
    {
        var network = NetworkManager.Singleton;
        var local = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        var health = local != null ? local.GetComponent<Health>() : null;
        return health != null && health.currentHealth.Value > 0f;
    }
}
