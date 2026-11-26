using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Nazvy tymu a vysledek zapasu pro kazdeho hrace.
//  - Nazev tymu: v lobby kliknutim na nazev (LobbyUI). Host prejmenuje kterykoli tym, hrac jen svuj. Bez nazvu
//    "TÝM 1" / "TÝM 2" (jako v lobby - drive se ve hre psalo "TÝM 0" / "TÝM 1"). Host si nazvy pamatuje (PlayerPrefs).
//  - Konec zapasu (kazdy rezim): kazdemu velke VITEZSTVI / PORAZKA podle jeho tymu (Tlacenice ma vlastni text
//    s metry, MatchManagerPush.PushOverClientRpc).
public partial class MatchManager
{
    public const int TeamNameLimit = 16;
    const string TeamNamePrefKey = "match.teamName";

    public NetworkVariable<FixedString64Bytes> teamName0 = new NetworkVariable<FixedString64Bytes>();
    public NetworkVariable<FixedString64Bytes> teamName1 = new NetworkVariable<FixedString64Bytes>();

    // Nazev tymu k zobrazeni (velkymi pismeny).
    public static string TeamName(int team)
    {
        string custom = Instance != null ? Instance.CustomTeamName(team) : "";
        return string.IsNullOrWhiteSpace(custom) ? $"TÝM {team + 1}" : custom.ToUpperInvariant();
    }

    public string CustomTeamName(int team)
    {
        if (!IsSpawned || team < 0 || team > 1) return "";
        return (team == 0 ? teamName0 : teamName1).Value.ToString();
    }

    // Tym mistniho hrace (-1 = neni / divak).
    public static int LocalTeam()
    {
        var network = NetworkManager.Singleton;
        var player = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        var team = player != null ? player.GetComponent<PlayerTeam>() : null;
        return team != null ? team.teamId.Value : -1;
    }

    // Muze mistni hrac prejmenovat tym? (host kterykoli, hrac jen svuj)
    public static bool CanRenameTeam(int team)
    {
        var network = NetworkManager.Singleton;
        if (network == null || Instance == null || !Instance.IsSpawned) return false;
        return network.IsServer || LocalTeam() == team;
    }

    // Vycisteni nazvu: bez znacek <> (rich text), mezery jednou, nejvys TeamNameLimit znaku.
    public static string CleanTeamName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var builder = new System.Text.StringBuilder();
        bool space = false;
        foreach (char c in name.Trim())
        {
            if (c == '<' || c == '>' || char.IsControl(c)) continue;
            if (char.IsWhiteSpace(c)) { if (!space) builder.Append(' '); space = true; continue; }
            space = false;
            builder.Append(c);
            if (builder.Length >= TeamNameLimit) break;
        }
        return builder.ToString().Trim();
    }

    // (server) nazvy z minula - host si je pamatuje
    void ServerLoadTeamNames()
    {
        teamName0.Value = new FixedString64Bytes(CleanTeamName(PlayerPrefs.GetString(TeamNamePrefKey + "0", "")));
        teamName1.Value = new FixedString64Bytes(CleanTeamName(PlayerPrefs.GetString(TeamNamePrefKey + "1", "")));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RenameTeamRpc(int team, FixedString64Bytes name, RpcParams rpcParams = default)
    {
        if (team < 0 || team > 1) return;
        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender != NetworkManager.ServerClientId)
        {
            // hrac jen svuj tym
            if (!NetworkManager.ConnectedClients.TryGetValue(sender, out var client) || client.PlayerObject == null) return;
            var playerTeam = client.PlayerObject.GetComponent<PlayerTeam>();
            if (playerTeam == null || playerTeam.teamId.Value != team) return;
        }
        string clean = CleanTeamName(name.ToString());
        (team == 0 ? teamName0 : teamName1).Value = new FixedString64Bytes(clean);
        PlayerPrefs.SetString(TeamNamePrefKey + team, clean);
        PlayerPrefs.Save();
    }

    // ---------------- vysledek zapasu ----------------

    public static string Verdict(int winner, int local) =>
        winner < 0 ? "REMÍZA" : local < 0 ? $"{TeamName(winner)} VYHRÁL" : local == winner ? "VÍTĚZSTVÍ!" : "PORÁŽKA";

    public static Color VerdictColor(int winner, int local) =>
        winner < 0 ? new Color(1f, 0.82f, 0.15f) : local >= 0 && local != winner ? new Color(0.85f, 0.25f, 0.25f) : UiKit.TeamColor(winner);

    // Kdo vyhral, s poznamkou, jestli je to vas tym.
    public static string WinnerLine(int winner, int local) =>
        winner < 0 ? "zápas skončil nerozhodně"
            : $"vyhrál {TeamName(winner)}{(local == winner ? " – váš tým" : local >= 0 ? " – soupeř" : "")}";

    [ClientRpc]
    void MatchResultClientRpc(int winner)
    {
        int local = LocalTeam();
        CaptureUI.Headline(Verdict(winner, local), WinnerLine(winner, local), VerdictColor(winner, local), 7f);
    }
}
