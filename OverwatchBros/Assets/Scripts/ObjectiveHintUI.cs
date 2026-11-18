using TMPro;
using UnityEngine;

// "Co mam delat" (testeri: nevis, co mas ve hre delat):
//  - pod horni listou stale kratky ukol podle rezimu a role (dobij bod B / bran bod B / zabijej nepratele...),
//  - po vstupu do zapasu na par sekund napoveda ovladani dole uprostred (F1, F2, Tab, Esc, schopnosti).
public class ObjectiveHintUI
{
    TextMeshProUGUI objective, controls;
    float controlsUntil;
    bool wasPlaying;

    public void Build(Transform canvas)
    {
        objective = UiKit.MakeText(canvas, "Ukol", "", 21, TextAlignmentOptions.Center, new Vector2(0.5f, 1f),
            new Vector2(0f, -128f), new Vector2(900f, 26f), new Color(0.92f, 0.94f, 0.98f));
        objective.fontStyle = FontStyles.Bold;
        objective.outlineWidth = 0.18f;
        objective.outlineColor = new Color32(0, 0, 0, 200);

        controls = UiKit.MakeText(canvas, "Ovladani", "", 22, TextAlignmentOptions.Center, new Vector2(0.5f, 0f),
            new Vector2(0f, 215f), new Vector2(1400f, 60f), new Color(0.9f, 0.92f, 0.96f));
        controls.outlineWidth = 0.18f;
        controls.outlineColor = new Color32(0, 0, 0, 200);
        controls.text = "<color=#F28C1A>LEVÉ TL.</color> zbraň   ·   <color=#F28C1A>SHIFT · E · PRAVÉ TL.</color> schopnosti   ·   "
            + "<color=#F28C1A>Q</color> ultimátka (až se nabije)\n"
            + "<color=#F28C1A>F2</color> co umí tvůj hrdina   ·   <color=#F28C1A>F1</color> změna hrdiny (na základně)   ·   "
            + "<color=#F28C1A>Tab</color> tabulka   ·   <color=#F28C1A>Esc</color> nastavení";
        objective.gameObject.SetActive(false);
        controls.gameObject.SetActive(false);
    }

    public void Tick(PlayerHero local, MatchManager match, bool playing)
    {
        if (objective == null) return;
        bool show = playing && match != null && local != null && !ReplayPlayer.Active;
        if (show && !wasPlaying) controlsUntil = Time.unscaledTime + 14f;   // nove v zapase: napoveda ovladani
        wasPlaying = show;

        string text = show ? Objective(local, match) : "";
        if (objective.gameObject.activeSelf != (text.Length > 0)) objective.gameObject.SetActive(text.Length > 0);
        if (text.Length > 0) objective.text = text;

        bool controlsOn = show && Time.unscaledTime < controlsUntil;
        if (controls.gameObject.activeSelf != controlsOn) controls.gameObject.SetActive(controlsOn);
        if (controlsOn) controls.alpha = Mathf.Clamp01((controlsUntil - Time.unscaledTime) / 1.5f);
    }

    static string Objective(PlayerHero local, MatchManager match)
    {
        var team = local.GetComponent<PlayerTeam>();
        int myTeam = team != null ? team.teamId.Value : -1;
        if (!match.IsAttackMode)
            return $"ÚKOL: zabíjej nepřátele  ·  první tým na {match.scoreToWinSynced.Value} zabití vyhrává";

        int point = Mathf.Clamp(match.pointIndex.Value, 0, MatchManager.PointNames.Length - 1);
        string name = MatchManager.PointNames[point];
        bool attacking = myTeam == match.attackTeam.Value;
        switch (match.roundPhase.Value)
        {
            case MatchManager.RoundIntermission:
                return "Výměna stran  ·  teď hraješ za druhou stranu";
            case MatchManager.RoundOvertime:
                return "";   // (horni lista ukazuje PRODLOUZENI)
        }
        // pri cekani na odemceni / priprave rika, co delat, panel s odpoctem (CaptureUI)
        if (match.pointState.Value == MatchManager.StateLocked) return "";
        return attacking ? $"ÚKOL: dobij bod {name}  ·  stůj na něm, víc vás = rychleji"
                         : $"ÚKOL: braň bod {name}  ·  stačí na něm stát, aby se nezabíral";
    }
}
