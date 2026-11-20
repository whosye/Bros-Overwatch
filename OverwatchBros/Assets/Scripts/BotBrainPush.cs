using UnityEngine;

// Boti v rezimu Tlacenice (MatchManagerPush): cilem je vzdy obri Honza - kdo u nej stoji sam, tlaci svuj Yaris;
// kdyz ho ma souper, jdou k nemu, aby byl sporny a prevzali ho. V ostatnich rezimech se tohle nevola.
//  - misto v zone u Honzy: nikdy ne uvnitr Yarisu ani Honzy (nejsou v navigacni siti - bot by v nich poskakoval),
//    a radeji za autem / za Honzou, na strane od nepritele (kryt)
//  - strelec na dalku kryje spoluhrace z mista s vyhledem, kdyz uz u Honzy nekdo je a souper zrovna netlaci
//  - nouze (prodlouzeni nebo poslednich 20 s, kdyz jeho tym prohrava, a kdyz souper tlaci a nikdo z jeho tymu
//    u Honzy neni): vsichni k Honzovi, drzi se v zone a nenechaji se odlakat; pro lekarnicku odbehnou az pod 20 %
public partial class BotBrain
{
    bool PushObjective(MatchManager match, out Vector3 goal, out float leash, out bool holdPoint)
    {
        goal = Vector3.zero;
        leash = 0f;
        holdPoint = false;
        if (team == null || PushRoute.Active == null)
        {
            PushDiagnose(PushRoute.Active == null ? "trasa tlacenice neni aktivni (mapa?)" : "bot nema tym");
            return false;
        }

        Vector3 robot = MatchManager.PushRobotPosition;
        int myTeam = team.teamId.Value;
        int state = match.pushState.Value;
        int owner = match.PushOwner;
        bool urgent = PushUrgent(match, myTeam);
        bool enemyPushing = owner == 1 - myTeam && (state == MatchManager.PushPushing || state == MatchManager.PushWalking)
            && match.PushersOf(myTeam) == 0;
        bool near = Vector3.Distance(Flat(transform.position), Flat(robot)) < MatchManager.PushRadius - 1f;

        leash = urgent ? 9f : 16f;
        holdPoint = near && state != MatchManager.PushLocked;

        // strelec na dalku: kdyz uz u Honzy nekdo z jeho tymu je (a neni nouze), kryje ho z mista s vyhledem
        var weapon = hero != null && hero.Hero != null ? hero.Hero.weapon : null;
        if (!urgent && !enemyPushing && weapon != null && CurrentRole(weapon) == Role.Ranged && match.PushersOf(myTeam) > 0 && !near)
        {
            RangedBand(weapon, out float minR, out float maxR);
            leash = maxR + 5f;
            holdPoint = false;
            goal = FiringSpot(watchSpot, robot, Mathf.Min(minR, 12f), Mathf.Min(maxR, 28f));
            return true;
        }

        // misto v zone: Honza se hybe, misto se obcas zmeni (driv, kdyz uz je mimo zonu nebo v aute)
        var robotView = PushRobot.Instance;
        bool spotBad = Vector3.Distance(objectiveSpot, robot) > MatchManager.PushRadius - 1f
            || (robotView != null && robotView.Occupies(objectiveSpot, 0.7f));
        if (Time.time >= nextObjectiveSpot || spotBad || Vector3.Distance(transform.position, objectiveSpot) < 1.2f)
        {
            nextObjectiveSpot = Time.time + Random.Range(3f, 6f);
            objectiveSpot = PushSpot(robot, urgent);
        }
        goal = objectiveSpot;
        PushDiagnose(null);
        return true;
    }

    // Diagnostika (jednou za bota): jestli ma cil Honzu a jestli stoji na navigacni siti.
    bool pushDiagnosed;

    float pushDiagnosedAt = -1000f;

    // Jednou za 30 s: do konzole i do Temp/PushBots.txt (cil, navigacni sit, jestli vede cesta k Honzovi).
    void PushDiagnose(string problem)
    {
        if (pushDiagnosed && Time.time - pushDiagnosedAt < 30f) return;
        if (pushDiagnosed == false && Time.time < 1f) return;
        pushDiagnosed = true;
        pushDiagnosedAt = Time.time;
        Vector3 robot = MatchManager.PushRobotPosition;
        bool ready = BotNavigation.Ready;
        bool onMesh = false, robotOnMesh = false;
        string pathInfo = "-";
        if (ready)
        {
            onMesh = UnityEngine.AI.NavMesh.SamplePosition(transform.position, out var from, 3f, BotNavigation.Filter);
            robotOnMesh = UnityEngine.AI.NavMesh.SamplePosition(robot, out var to, 8f, BotNavigation.Filter);
            if (onMesh && robotOnMesh)
            {
                var navPath = new UnityEngine.AI.NavMeshPath();
                UnityEngine.AI.NavMesh.CalculatePath(from.position, to.position, BotNavigation.Filter, navPath);
                Vector3 last = navPath.corners.Length > 0 ? navPath.corners[navPath.corners.Length - 1] : from.position;
                pathInfo = $"{navPath.status}, {navPath.corners.Length} rohu, konci {last:0} ({Vector3.Distance(last, to.position):0} m od Honzy)";
                // necela cesta: co stoji tesne za jejim koncem (smerem k Honzovi)
                if (navPath.status != UnityEngine.AI.NavMeshPathStatus.PathComplete)
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var c in Physics.OverlapSphere(last + Vector3.up, 2.5f, ~0, QueryTriggerInteraction.Ignore))
                        if (c.GetComponentInParent<PlayerHero>() == null && names.Count < 8)
                            names.Add($"{c.name}{(c.GetComponentInParent<ZoneGate>() != null ? " (brana)" : "")}");
                    pathInfo += $", u konce: {(names.Count == 0 ? "nic" : string.Join(", ", names))}";
                }
            }
        }
        var tri = ready ? UnityEngine.AI.NavMesh.CalculateTriangulation() : default;
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        if (tri.vertices != null)
            foreach (var v in tri.vertices) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        string line = $"[Boti] Tlacenice {System.DateTime.Now:HH:mm:ss}: {name} {(problem ?? "jde k Honzovi")} | stav {match_state()} | Honza {robot:0}, bot {transform.position:0}, cil {objectiveSpot:0} | "
            + $"sit {(ready ? "ano" : "NENI")} ({(tri.vertices != null ? tri.vertices.Length : 0)} vrcholu, x {min.x:0}..{max.x:0}, z {min.z:0}..{max.z:0}) | "
            + $"bot na siti {(onMesh ? "ano" : "NE")}, Honza u site {(robotOnMesh ? "ano" : "NE")}, cesta {pathInfo} | pohyb {Move}, rooted {(fpc != null && fpc.Rooted ? "ano" : "ne")}";
        Debug.Log(line);
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../Temp/PushBots.txt"), line + "\n"); }
        catch (System.Exception) { }
    }

    static string match_state()
    {
        var match = MatchManager.Instance;
        return match == null ? "?" : $"faze {match.roundPhase.Value}, Honza {match.pushState.Value}, vlastnik {match.PushOwner}";
    }

    // Misto u Honzy: v zone, mimo Honzu a auta, radeji za nimi (na strane od nepritele, ktereho bot vidi / tusi).
    Vector3 PushSpot(Vector3 robot, bool urgent)
    {
        var robotView = PushRobot.Instance;
        float radius = MatchManager.PushRadius * (urgent ? 0.4f : 0.55f);
        bool threat = target != null;
        Vector3 threatPos = threat ? (targetVisible ? target.transform.position : lastSeenPos) : Vector3.zero;
        Vector3 best = robot;
        float bestScore = float.MinValue;
        for (int i = 0; i < 10; i++)
        {
            Vector3 candidate = AroundPoint(robot, radius);
            if (robotView != null && robotView.Occupies(candidate, 0.7f)) continue;
            if (Vector3.Distance(Flat(candidate), Flat(robot)) > MatchManager.PushRadius - 1f) continue;
            float score = Random.value * 0.3f - Vector3.Distance(candidate, transform.position) * 0.02f;
            if (threat && robotView != null)
            {
                // kryt: Honza nebo auto mezi botem a nepritelem
                Vector3 cover = robotView.NearestCover(candidate);
                Vector3 fromCover = Flat(candidate - cover), toThreat = Flat(threatPos - cover);
                if (fromCover.sqrMagnitude > 0.01f && toThreat.sqrMagnitude > 0.01f)
                    score -= Vector3.Dot(fromCover.normalized, toThreat.normalized) * 1.2f;
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        return bestScore > float.MinValue ? best : AroundPoint(robot, radius);
    }

    // Nouze: prodlouzeni nebo poslednich 20 s, kdyz jeho tym nevede (nebo je stejne).
    static bool PushUrgent(MatchManager match, int myTeam)
    {
        int phase = match.roundPhase.Value;
        bool losing = match.PushBest(myTeam) <= match.PushBest(1 - myTeam) + 0.1f;
        if (phase == MatchManager.RoundOvertime) return losing;
        return phase == MatchManager.RoundAttack && match.secondsLeft.Value <= 20 && losing;
    }

    // Pro HealthGoal: v nouzi u Honzy vydrzi, dokud ma aspon 20 % zivotu (pak teprve pro lekarnicku).
    bool PushHoldDespiteLowHealth(MatchManager match)
    {
        if (!match.IsPushMode || team == null || health == null) return false;
        if (health.currentHealth.Value <= health.maxHealth * 0.2f) return false;
        if (!PushUrgent(match, team.teamId.Value)) return false;
        return Vector3.Distance(Flat(transform.position), Flat(MatchManager.PushRobotPosition)) < MatchManager.PushRadius + 4f;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
