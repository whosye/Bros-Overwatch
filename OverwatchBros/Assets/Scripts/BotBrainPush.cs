using UnityEngine;

// Boti v rezimu Tlacenice (MatchManagerPush): cilem je robot. Kdo muze tlacit, stoji u nej (za obrem / vedle Yarisu);
// kdyz robota tlaci soupeř nebo je sporny, jdou k nemu ho prevzit / zastavit. Strelec na dalku kryje tlacici
// spoluhrace z mista s vyhledem na robota. V ostatnich rezimech se tohle nevola.
public partial class BotBrain
{
    bool PushObjective(MatchManager match, out Vector3 goal, out float leash, out bool holdPoint)
    {
        goal = Vector3.zero;
        leash = 0f;
        holdPoint = false;
        if (team == null || PushRoute.Active == null) return false;

        Vector3 robot = MatchManager.PushRobotPosition;
        int myTeam = team.teamId.Value;
        int state = match.pushState.Value;
        bool near = Vector3.Distance(Flat(transform.position), Flat(robot)) < MatchManager.PushRadius - 0.8f;

        // priprava: stoji ve spawnu (drzi je server), potom hned k robotovi
        leash = 16f;
        holdPoint = near && state != MatchManager.PushLocked;

        // strelec na dalku: kdyz uz u robota nekdo z jeho tymu je, kryje ho z mista s vyhledem
        var weapon = hero != null && hero.Hero != null ? hero.Hero.weapon : null;
        if (weapon != null && CurrentRole(weapon) == Role.Ranged && match.PushersOf(myTeam) > 0 && !near)
        {
            RangedBand(weapon, out float minR, out float maxR);
            leash = maxR + 5f;
            holdPoint = false;
            goal = FiringSpot(watchSpot, robot, Mathf.Min(minR, 12f), Mathf.Min(maxR, 28f));
            return true;
        }

        // misto u robota: za obrem ve smeru, kterym tlaci jeho tym (nebo kolem, kdyz se o nej bojuje)
        if (Time.time >= nextObjectiveSpot || Vector3.Distance(objectiveSpot, robot) > MatchManager.PushRadius - 1f
            || Vector3.Distance(transform.position, objectiveSpot) < 1.2f)
        {
            nextObjectiveSpot = Time.time + Random.Range(3f, 6f);
            objectiveSpot = AroundPoint(robot, 3f);
        }
        goal = objectiveSpot;
        return true;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
