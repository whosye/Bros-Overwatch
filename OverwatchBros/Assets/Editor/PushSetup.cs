using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Rezim Tlacenice: nova mapa "Domasov (Tlacenice)" = KOPIE Domasova (Map-Domasov + volne objekty Domasova v koreni sceny),
// posunuta o MatchManager.DomasovPushOrigin. Puvodni Domasov se tu nemeni ani o jeden objekt.
//  1. Zkopirovat Domasov - jen jednou. Kdyz kopie existuje, nic se nedeje (rucni upravy v kopii zustanou).
//  2. Postavit trasu, robota a spawny - po castech (Trasa, Uklid, Spawny, Robot), kazda cast jen jednou
//     (znacky v Map_DomasovPush/Tlacenice/_Postaveno). Co pak rucne upravis nebo smazes, zustane.
//  3. QA audit trasy - projede Yaris a obra po cele trase (obe strany), hlasi kolize, nerovnosti a mista,
//     kde se neda robota obejit. Zprava v Temp/PushRouteAudit.txt.
// Nahled: prepne v editoru Domasov <-> kopii (pred ulozenim sceny se sam vrati na Domasov).
[InitializeOnLoad]
public static class PushSetup
{
    const string SourceName = "Map-Domasov";
    const string CopyName = "Map_DomasovPush";
    const string GroupName = "Tlacenice";
    const string MarksName = "_Postaveno";

    // Objekty Domasova, ktere lezi v koreni sceny (ne pod Map-Domasov).
    static readonly string[] LooseRoots =
    {
        "max gauc scena (1)", "MaxNaGauci (1)", "SindelLasagne", "Zidle", "Zidle (1)", "Zidle (2)",
        "Obraz_IMG20241109213445", "Obraz_MotorkarHonda",
    };

    static Vector3 Origin => MatchManager.DomasovPushOrigin;

    // Trasa (x, z v souradnicich puvodniho Domasova): konec u zakladny tymu 0 (JV stodola) -> roh -> roh -> konec u
    // zakladny tymu 1 (SZ stodola). 32 + 64 + 32 m, otocena o 180 st. kolem stredu sama na sebe.
    static readonly Vector2[] RoutePoints = { new Vector2(23.5f, -23f), new Vector2(-8.5f, -23f), new Vector2(-8.5f, 41f), new Vector2(-40.5f, 41f) };
    // Predsunute spawny po 1. a 2. checkpointu (zakladni = SpawnPoint_Team0/1 kopie).
    static readonly Vector2[][] ForwardSpawns =
    {
        new[] { new Vector2(3f, -9f), new Vector2(4f, 18f) },      // tym 0: za drevnikem, travnik u velke chaty
        new[] { new Vector2(-20f, 27f), new Vector2(-21f, 0f) },   // tym 1: za malou chatou, terasa male chaty
    };

    // Koridor, ktery musi byt pro Yaris a obra volny (od trasy na kazdou stranu, vyska nad zemi).
    const float CorridorHalf = 3.0f, CorridorBottom = 0.35f, CorridorTop = 5.7f;
    const float GiantRadius = 0.85f, GiantHeight = 5.4f;
    // Tyhle casti mapy se nikdy nepresouvaji (jen se nahlasi).
    static readonly string[] Protected = { "HlavniChata", "MensiChata", "MalaChataVylepseni", "Rozhledna", "Tunel", "HraniceProHrace", "Stodola", "PavoukuvWorkshop" };

    static PushSetup()
    {
        // Nahled kopie se nesmi ulozit: hra i editorove nastroje Domasova cekaji ve scene zapnuty Map-Domasov.
        EditorSceneManager.sceneSaving += (scene, path) => RestoreDomasov();
    }

    // ---------------- 1. kopie ----------------

    [MenuItem("BrosOverwatch/Tlacenice/1. Zkopirovat Domasov do nove mapy (jen jednou)")]
    static void CopyMap()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Tlacenice] Nejdriv ukonci Play."); return; }
        if (FindRoot(CopyName) != null)
        {
            Debug.LogWarning($"[Tlacenice] Kopie {CopyName} uz existuje - neprepisuji ji (rucni upravy zustavaji). Pokracuj krokem 2.");
            return;
        }
        var source = FindRoot(SourceName);
        if (source == null) { Debug.LogError($"[Tlacenice] Ve scene neni {SourceName}."); return; }

        Undo.IncrementCurrentGroup();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Tlacenice - kopie Domasova");
        var previousSelection = Selection.objects;

        var report = new StringBuilder();
        report.AppendLine($"Kopie Domasova -> {CopyName}, posun {Origin}");
        var copy = Duplicate(source);
        if (copy == null) { Debug.LogError("[Tlacenice] Map-Domasov se nepodarilo zkopirovat."); return; }
        copy.name = CopyName;
        copy.transform.position = source.transform.position + Origin;
        report.AppendLine($"  + {SourceName}: {CountChildren(copy.transform)} objektu");

        foreach (var name in LooseRoots)
        {
            var go = FindRoot(name);
            if (go == null) { report.AppendLine($"  - {name}: ve scene neni (preskoceno)"); continue; }
            GameObject dup = null;
            try { dup = Duplicate(go); }
            catch (System.Exception e) { report.AppendLine($"  - {name}: nejde zkopirovat ({e.Message})"); }
            if (dup == null) { report.AppendLine($"  - {name}: nepodarilo se zkopirovat"); continue; }
            dup.name = name;
            Undo.SetTransformParent(dup.transform, copy.transform, "Tlacenice");
            dup.transform.position = go.transform.position + Origin;
            report.AppendLine($"  + {name}");
        }

        Fixups(copy.transform, report);
        copy.SetActive(false);   // hra si mapu zapina sama (MatchManager.ApplyMap)
        Selection.objects = previousSelection;
        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(copy.scene);
        System.IO.File.WriteAllText("Temp/PushCopy.txt", report.ToString());
        Debug.Log($"[Tlacenice] Hotovo: kopie {CopyName} (ve scene vypnuta). Ted krok 2. Ulož scenu (Ctrl+S).\n{report}");
    }

    // Jako Ctrl+D (vnorene prefaby zustanou napojene). Kdyz to nejde, obycejna kopie.
    static GameObject Duplicate(GameObject go)
    {
        Selection.objects = new Object[] { go };
        Unsupported.DuplicateGameObjectsUsingPasteboard();
        var result = Selection.activeGameObject;
        if (result != null && result != go) return result;
        var clone = Object.Instantiate(go, go.transform.parent);
        Undo.RegisterCreatedObjectUndo(clone, "Tlacenice");
        return clone;
    }

    // Co v kopii nesmi sahat na puvodni Domasov.
    static void Fixups(Transform copy, StringBuilder report)
    {
        report.AppendLine("Upravy kopie:");
        // provoz Yarisu: vypnout (z auta bude naklad robota - krok 2)
        foreach (var traffic in copy.GetComponentsInChildren<YarisTraffic>(true))
        {
            Undo.RecordObject(traffic, "Tlacenice");
            traffic.enabled = false;
            report.AppendLine($"  provoz Yarisu vypnut ({PathOf(traffic.transform, copy)})");
        }
        foreach (var boundary in copy.GetComponentsInChildren<TrafficBoundary>(true))
        {
            Undo.RecordObject(boundary, "Tlacenice");
            boundary.car = null;
        }
        // rolba: jen kulisa (Tractor se pripojuje podle jmena a jeho stav je jeden pro celou hru)
        foreach (var t in copy.GetComponentsInChildren<Transform>(true))
            if (t.name == "SneznaRolba" || t.name == "TraktorSVlekem")
            {
                Undo.RecordObject(t.gameObject, "Tlacenice");
                t.name += "_Kulisa";
                report.AppendLine($"  {PathOf(t, copy)}: prejmenovano (kulisa, ne traktor)");
            }
        foreach (var gate in copy.GetComponentsInChildren<RolbaGate>(true))
        {
            report.AppendLine($"  {PathOf(gate.transform, copy)}: odebrano RolbaGate (vrata zustanou zavrena)");
            Undo.DestroyObjectImmediate(gate);
        }
        // tajny pruchod ma souradnice sveta
        foreach (var passage in copy.GetComponentsInChildren<SecretPassage>(true))
        {
            Undo.RecordObject(passage, "Tlacenice");
            passage.poolCenter += Origin;
            passage.arrival += Origin;
            passage.exitMin += Origin;
            passage.exitMax += Origin;
            passage.exitPoint += Origin;
            report.AppendLine($"  {PathOf(passage.transform, copy)}: souradnice posunuty");
        }
        // spawn stodoly: SpawnBarn se pri startu pridava jen aktivnim stodolam - kopii ho dat rovnou
        foreach (var t in copy.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("Stodola") || t.GetComponent<SpawnBarn>() != null) continue;
            var floor = t.Find("Podlaha");
            if (floor == null || floor.GetComponent<MeshFilter>() == null) continue;
            Undo.AddComponent<SpawnBarn>(t.gameObject);
            report.AppendLine($"  {PathOf(t, copy)}: SpawnBarn");
        }
    }

    // ---------------- 2. trasa, uklid, spawny, robot ----------------

    [MenuItem("BrosOverwatch/Tlacenice/2. Postavit trasu, robota a spawny")]
    static void Build()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[Tlacenice] Nejdriv ukonci Play."); return; }
        var copyGo = FindRoot(CopyName);
        if (copyGo == null) { Debug.LogWarning("[Tlacenice] Nejdriv krok 1 (kopie Domasova)."); return; }

        Undo.IncrementCurrentGroup();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Tlacenice - stavba");
        var report = new StringBuilder();
        bool wasActive = copyGo.activeSelf;
        copyGo.SetActive(true);
        Physics.SyncTransforms();
        try
        {
            var copy = copyGo.transform;
            var group = copy.Find(GroupName);
            if (group == null)
            {
                group = new GameObject(GroupName).transform;
                Undo.RegisterCreatedObjectUndo(group.gameObject, "Tlacenice");
                group.SetParent(copy, false);
            }

            var routeTransform = group.Find("Trasa");
            var route = routeTransform != null ? routeTransform.GetComponent<PushRoute>() : null;
            if (!IsDone(group, "Trasa") || route == null)
            {
                route = BuildRoute(group, report);
                MarkDone(group, "Trasa");
            }
            Physics.SyncTransforms();

            if (!IsDone(group, "Uklid"))
            {
                ClearCorridor(copy, group, route, report);
                MarkDone(group, "Uklid");
            }
            if (!IsDone(group, "Spawny"))
            {
                BuildSpawns(copy, group, route, report);
                MarkDone(group, "Spawny");
            }
            if (route.spawns == null && group.Find("Spawny") != null)
            {
                Undo.RecordObject(route, "Tlacenice");
                route.spawns = group.Find("Spawny");
            }
            if (!IsDone(group, "Robot"))
            {
                BuildRobot(copy, group, route, report);
                MarkDone(group, "Robot");
            }
            if (report.Length == 0) report.AppendLine("Vse uz bylo postavene (znacky v Tlacenice/_Postaveno) - nic se nemenilo.");
        }
        finally
        {
            copyGo.SetActive(wasActive);
        }
        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(copyGo.scene);
        System.IO.File.WriteAllText("Temp/PushSetup.txt", report.ToString());
        Debug.Log($"[Tlacenice] Krok 2 hotov. Ted krok 3 (QA audit) a uloz scenu (Ctrl+S).\n{report}");
    }

    static PushRoute BuildRoute(Transform group, StringBuilder report)
    {
        var old = group.Find("Trasa");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var go = new GameObject("Trasa");
        Undo.RegisterCreatedObjectUndo(go, "Tlacenice");
        go.transform.SetParent(group, false);
        go.transform.position = Origin + new Vector3(-8.5f, 0f, 9f);
        var route = go.AddComponent<PushRoute>();
        report.AppendLine("Trasa:");
        for (int i = 0; i < RoutePoints.Length; i++)
        {
            Vector3 at = Origin + new Vector3(RoutePoints[i].x, 0f, RoutePoints[i].y);
            var node = new GameObject($"Bod_{i}");
            node.transform.SetParent(go.transform, false);
            node.transform.position = GroundAt(at, 1.2f, out var ground) ? ground : at;
            report.AppendLine($"  Bod_{i}: {node.transform.position}");
        }
        report.AppendLine($"  delka {route.Length:0.0} m, stred {route.Center:0.0} m, checkpointy +-{string.Join(" / ", route.checkpoints)} m od stredu");
        return route;
    }

    // Co zasahuje do koridoru trasy: posunout kousek stranou (na volne misto, na zem), nejde-li, vypnout.
    // Budovy a velke objekty se nehybou - jen se nahlasi.
    static void ClearCorridor(Transform copy, Transform group, PushRoute route, StringBuilder report)
    {
        report.AppendLine("Uklid koridoru trasy (jen v kopii):");
        var moves = new Dictionary<Transform, int>();
        var blocked = new HashSet<string>();
        int changes = 0;
        float length = route.Length;
        for (int pass = 0; pass < 4; pass++)
        {
            bool any = false;
            for (float s = -route.robotOffset - 1f; s <= length + route.robotOffset + 1f; s += 0.75f)   // i za konci (obr za Yarisem)
            {
                Vector3 p = route.Point(s);
                var rotation = Quaternion.LookRotation(route.Forward(s), Vector3.up);
                Vector3 center = p + Vector3.up * ((CorridorBottom + CorridorTop) * 0.5f);
                Vector3 half = new Vector3(CorridorHalf, (CorridorTop - CorridorBottom) * 0.5f, 0.45f);
                foreach (var collider in Physics.OverlapBox(center, half, rotation, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (collider == null || !collider.transform.IsChildOf(copy) || collider.transform.IsChildOf(group)) continue;
                    if (collider.bounds.max.y < p.y + CorridorBottom + 0.05f) continue;
                    var unit = Unit(collider.transform, copy);
                    string path = PathOf(unit, copy);
                    if (IsProtected(path) || Footprint(unit) > 12f)
                    {
                        if (blocked.Add(path)) report.AppendLine($"  ! BLOKUJE (budova / velky objekt, nehybu): {path}  u s = {s:0} m");
                        continue;
                    }
                    moves.TryGetValue(unit, out int count);
                    if (count < 2 && MoveAside(unit, route, copy, out float shift))
                    {
                        moves[unit] = count + 1;
                        report.AppendLine($"  posunuto: {path} o {shift:0.0} m stranou (s = {s:0} m)");
                    }
                    else
                    {
                        Undo.RecordObject(unit.gameObject, "Tlacenice");
                        unit.gameObject.SetActive(false);
                        report.AppendLine($"  vypnuto (neni kam posunout): {path} (s = {s:0} m)");
                    }
                    changes++;
                    any = true;
                    Physics.SyncTransforms();
                }
            }
            if (!any) break;
        }
        if (changes == 0 && blocked.Count == 0) report.AppendLine("  koridor je volny");
    }

    // Nejvyssi rodic objektu, ktery je jeste "jedna vec" (do 9 m) - strom, kryt, bedna, kus plotu.
    static Transform Unit(Transform t, Transform copy)
    {
        Transform unit = t;
        while (unit.parent != null && unit.parent != copy && unit.parent.parent != copy)
        {
            Bounds b = WorldBounds(unit.parent);
            if (b.size.x > 9f || b.size.z > 9f) break;
            unit = unit.parent;
        }
        return unit;
    }

    static float Footprint(Transform unit)
    {
        Bounds b = WorldBounds(unit);
        return Mathf.Max(b.size.x, b.size.z);
    }

    static bool MoveAside(Transform unit, PushRoute route, Transform copy, out float shift)
    {
        shift = 0f;
        Bounds b = WorldBounds(unit);
        float s = route.Closest(b.center);
        Vector3 p = route.Point(s), right = route.Right(s);
        float lat = Vector3.Dot(b.center - p, right);
        float extent = Mathf.Abs(b.extents.x * right.x) + Mathf.Abs(b.extents.z * right.z);
        float first = lat >= 0f ? 1f : -1f;
        foreach (float sign in new[] { first, -first })
        {
            float need = sign * (CorridorHalf + 0.5f + extent) - lat;
            if (Mathf.Abs(need) > 9f) continue;
            Vector3 delta = right * need;
            Vector3 target = b.center + delta;
            // novy kus trasy (v rohu) nesmi byt blizko
            float s2 = route.Closest(target);
            Vector3 q = route.Point(s2);
            float side = Vector3.Dot(target - q, route.Right(s2));
            float extent2 = Mathf.Abs(b.extents.x * route.Right(s2).x) + Mathf.Abs(b.extents.z * route.Right(s2).z);
            if (Mathf.Abs(side) - extent2 < CorridorHalf + 0.3f) continue;
            if (Occupied(target, b.extents, unit)) continue;

            Undo.RecordObject(unit, "Tlacenice - uklid trasy");
            unit.position += delta;
            // na zem (stejna vyska nad zemi jako predtim)
            if (GroundAt(b.center, 1f, out var before) && GroundAt(target, 1f, out var after))
                unit.position += Vector3.up * (after.y - before.y);
            shift = Mathf.Abs(need);
            return true;
        }
        return false;
    }

    // Je na miste neco jineho (mimo zem a sam objekt)?
    static bool Occupied(Vector3 center, Vector3 extents, Transform self)
    {
        Vector3 half = new Vector3(extents.x * 0.95f, Mathf.Max(0.1f, extents.y * 0.9f - 0.1f), extents.z * 0.95f);
        foreach (var c in Physics.OverlapBox(center + Vector3.up * 0.15f, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.transform.IsChildOf(self)) continue;
            if (c.bounds.max.y < center.y - extents.y + 0.2f) continue;   // zem pod nim
            return true;
        }
        return false;
    }

    static void BuildSpawns(Transform copy, Transform group, PushRoute route, StringBuilder report)
    {
        report.AppendLine("Spawny:");
        var spawns = group.Find("Spawny");
        if (spawns == null)
        {
            spawns = new GameObject("Spawny").transform;
            Undo.RegisterCreatedObjectUndo(spawns.gameObject, "Tlacenice");
            spawns.SetParent(group, false);
        }
        Undo.RecordObject(route, "Tlacenice");
        route.spawns = spawns;

        for (int team = 0; team < 2; team++)
            for (int level = 0; level <= ForwardSpawns[team].Length; level++)
            {
                string name = $"Spawn_T{team}_{level}";
                if (spawns.Find(name) != null) continue;
                Vector3 at;
                if (level == 0)
                {
                    var marker = copy.Find($"SpawnPoint_Team{team}");
                    at = marker != null ? marker.position : route.Point(team == 0 ? 0f : route.Length);
                }
                else at = Origin + new Vector3(ForwardSpawns[team][level - 1].x, 0f, ForwardSpawns[team][level - 1].y);

                Vector3 spot = FreeSpot(at, out bool found);
                var go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Tlacenice");
                go.transform.SetParent(spawns, false);
                go.transform.position = spot;
                float moved = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(spot.x, spot.z));
                report.AppendLine($"  {name}: {spot - Origin} (Domasov souradnice){(moved > 0.1f ? $", posunuto o {moved:0.0} m na volne misto" : "")}{(found ? "" : "  ! VOLNE MISTO NENALEZENO - zkontroluj")}");
            }
    }

    // Volne misto na zemi pro hrace (kapsle) co nejblize 'at'.
    static Vector3 FreeSpot(Vector3 at, out bool found)
    {
        for (int ring = 0; ring <= 8; ring++)
        {
            int steps = ring == 0 ? 1 : 12;
            for (int i = 0; i < steps; i++)
            {
                float angle = i * Mathf.PI * 2f / steps;
                Vector3 candidate = at + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ring;
                if (!GroundAt(candidate, 2.6f, out var ground) || Mathf.Abs(ground.y - at.y) > 3f) continue;
                if (Physics.CheckCapsule(ground + Vector3.up * 0.6f, ground + Vector3.up * 1.75f, 0.45f, ~0, QueryTriggerInteraction.Ignore)) continue;
                found = true;
                return ground;
            }
        }
        found = false;
        return at;
    }

    static void BuildRobot(Transform copy, Transform group, PushRoute route, StringBuilder report)
    {
        report.AppendLine("Robot:");
        var old = group.Find("Robot");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        float center = route.Center;
        var root = new GameObject("Robot");
        Undo.RegisterCreatedObjectUndo(root, "Tlacenice");
        root.transform.SetParent(group, false);
        root.transform.position = route.Point(center);
        var robot = root.AddComponent<PushRobot>();
        root.AddComponent<BotNoNav>();   // obr a Yaris se hybou - do navigacni site botu nepatri
        robot.route = route;

        // Yaris: auto z provozu v kopii (ten samy model)
        var pivot = new GameObject("Yaris").transform;
        pivot.SetParent(root.transform, false);
        pivot.SetPositionAndRotation(route.Point(center), Quaternion.LookRotation(route.Forward(center), Vector3.up));
        var car = copy.Find("Props/ToyotaYaris");
        if (car != null)
        {
            float yaw = 0f;
            foreach (var traffic in car.GetComponents<YarisTraffic>())
            {
                yaw = traffic.modelYawOffset;
                Undo.DestroyObjectImmediate(traffic);
            }
            Vector3 scale = car.lossyScale;
            Undo.SetTransformParent(car, pivot, "Tlacenice");
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            car.localRotation = rotation;
            car.localScale = scale;
            var box = car.GetComponent<BoxCollider>();
            Vector3 bottom = box != null ? new Vector3(box.center.x, box.center.y - box.size.y * 0.5f, box.center.z) : Vector3.zero;
            car.localPosition = -(rotation * Vector3.Scale(scale, bottom));
            var rb = car.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(car.gameObject);
            rb.isKinematic = true;
            rb.useGravity = false;
            if (car.GetComponent<AudioSurface>() == null) Undo.AddComponent<AudioSurface>(car.gameObject).kind = AudioSurfaceKind.Metal;
            if (box != null)
            {
                // obr stoji tesne za autem (ruce na kufru), obchazi ho kousek od boku
                Vector3 size = rotation * Vector3.Scale(scale, box.size);
                float halfLength = Mathf.Abs(size.z) * 0.5f, halfWidth = Mathf.Abs(size.x) * 0.5f;
                Undo.RecordObject(route, "Tlacenice");
                route.robotOffset = halfLength + GiantRadius + 0.4f;
                route.sideOffset = halfWidth + GiantRadius + 0.7f;
                route.halfWidth = Mathf.Max(route.halfWidth, halfWidth + 0.5f);
                report.AppendLine($"  rozestup obra: {route.robotOffset:0.0} m za stredem auta, obchazi {route.sideOffset:0.0} m od stredu");
            }
            foreach (var t in car.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            report.AppendLine($"  Yaris: {PathOf(car, copy)} (presunut z Props, provoz odebran){(box != null ? $", velikost {Vector3.Scale(scale, box.size)}" : "  ! bez BoxCollideru")}");
        }
        else
        {
            var placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.name = "YarisNahrada";
            placeholder.transform.SetParent(pivot, false);
            placeholder.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            placeholder.transform.localScale = new Vector3(1.7f, 1.5f, 3.9f);
            var rb = placeholder.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            report.AppendLine("  ! Props/ToyotaYaris v kopii neni - misto auta kvadr (vymen za model)");
        }
        robot.cargo = pivot;

        // obr: kolize (kapsle), model Honzy se vlozi az pri spusteni (vzdy aktualni prefab postavy)
        var giant = new GameObject("Obr").transform;
        giant.SetParent(root.transform, false);
        giant.SetPositionAndRotation(route.Point(center) + route.Right(center) * route.sideOffset,
            Quaternion.LookRotation(-route.Right(center), Vector3.up));
        var capsule = giant.gameObject.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, GiantHeight * 0.5f, 0f);
        capsule.height = GiantHeight;
        capsule.radius = GiantRadius;
        var giantBody = giant.gameObject.AddComponent<Rigidbody>();
        giantBody.isKinematic = true;
        giantBody.useGravity = false;
        giant.gameObject.AddComponent<AudioSurface>().kind = AudioSurfaceKind.Body;
        robot.giant = giant;
        robot.giantScale = 3f;

        // zvuky
        robot.horn = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Traffic/YarisHorn.wav");
        var voice = new List<AudioClip>();
        foreach (var folder in new[] { "idle", "spawn", "kill", "ability_Q", "ability_E", "attack", "headshot" })
        {
            string path = $"Assets/Audio/Honza/{folder}";
            if (!AssetDatabase.IsValidFolder(path)) continue;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { path }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip != null) voice.Add(clip);
            }
        }
        robot.voice = voice.ToArray();
        report.AppendLine($"  obr: kapsle {GiantRadius} x {GiantHeight} m, model Honzy 3x (vklada se pri spusteni), hlasek {voice.Count}, houkacka {(robot.horn != null ? "ano" : "NE")}");
    }

    // ---------------- 3. QA audit ----------------

    [MenuItem("BrosOverwatch/Tlacenice/3. QA audit trasy (projede robota po cele trase)")]
    static void Audit()
    {
        var copyGo = FindRoot(CopyName);
        var group = copyGo != null ? copyGo.transform.Find(GroupName) : null;
        var route = group != null && group.Find("Trasa") != null ? group.Find("Trasa").GetComponent<PushRoute>() : null;
        var robot = group != null && group.Find("Robot") != null ? group.Find("Robot").GetComponent<PushRobot>() : null;
        if (route == null || robot == null || robot.cargo == null)
        {
            Debug.LogWarning("[Tlacenice] Nejdriv kroky 1 a 2 (kopie, trasa, robot).");
            return;
        }

        bool wasActive = copyGo.activeSelf;
        copyGo.SetActive(true);
        Physics.SyncTransforms();
        var report = new StringBuilder();
        try
        {
            var ignore = new HashSet<Collider>(robot.GetComponentsInChildren<Collider>(true));
            var box = robot.cargo.GetComponentInChildren<BoxCollider>();
            Vector3 carSize = box != null ? Vector3.Scale(box.size, Abs(box.transform.lossyScale)) : new Vector3(1.7f, 1.5f, 3.9f);
            var carHits = new Dictionary<string, List<float>>();
            var giantHits = new Dictionary<string, List<float>>();
            var crossHits = new Dictionary<string, List<float>>();
            var ground = new List<float>();
            var noWalk = new List<float>();
            var drops = new List<float>();
            float length = route.Length;
            int samples = 0;

            for (float s = 0f; s <= length + 0.001f; s += 0.25f)
            {
                samples++;
                Vector3 p = route.Point(s);
                Vector3 along = route.Point(s + 2.5f) - route.Point(s - 2.5f);
                along.y = 0f;
                var rotation = Quaternion.LookRotation(along.sqrMagnitude > 1e-4f ? along.normalized : route.Forward(s), Vector3.up);
                Vector3 right = rotation * Vector3.right;

                // Yaris (spodnich 30 cm se nepocita - obrubniky, prah)
                float carHeight = carSize.y - 0.3f;
                Vector3 carCenter = p + Vector3.up * (0.3f + carHeight * 0.5f);
                foreach (var c in Physics.OverlapBox(carCenter, new Vector3(carSize.x * 0.5f + 0.05f, carHeight * 0.5f, carSize.z * 0.5f + 0.05f), rotation, ~0, QueryTriggerInteraction.Ignore))
                    if (!ignore.Contains(c) && c.bounds.max.y > p.y + 0.3f) Note(carHits, PathOf(c.transform, copyGo.transform), s);

                // obr za Yarisem (tym 0 i tym 1) a pri obchazeni vedle Yarisu
                foreach (int side in new[] { 1, -1 })
                {
                    MatchManager.PushRobotPose(route, s, side, side, 1f, out var giantPos, out _, out _);
                    foreach (var c in GiantOverlap(giantPos))
                        if (!ignore.Contains(c)) Note(giantHits, PathOf(c.transform, copyGo.transform), s);
                }
                foreach (var c in GiantOverlap(p + route.Right(s) * route.sideOffset))
                    if (!ignore.Contains(c)) Note(crossHits, PathOf(c.transform, copyGo.transform), s);

                // zem pod koly
                foreach (var corner in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) })
                {
                    Vector3 wheel = p + rotation * Vector3.Scale(corner, new Vector3(carSize.x * 0.45f, 0f, carSize.z * 0.4f));
                    if (!GroundAt(wheel, 1f, out var hit, ignore) || Mathf.Abs(hit.y - p.y) > 0.3f) { ground.Add(s); break; }
                }

                // da se Yaris obejit (hrac vlevo nebo vpravo)? a neni vedle trasy sraz?
                bool leftFree = PlayerFits(p - right * (carSize.x * 0.5f + 0.9f), ignore);
                bool rightFree = PlayerFits(p + right * (carSize.x * 0.5f + 0.9f), ignore);
                if (!leftFree && !rightFree) noWalk.Add(s);
                foreach (float sign in new[] { -1f, 1f })
                    if (!GroundAt(p + right * sign * 3.5f, 1.5f, out var edge, ignore) || edge.y < p.y - 1.2f) { drops.Add(s); break; }
            }

            report.AppendLine($"QA trasy Tlacenice: delka {length:0.0} m, {samples} mist po 0,25 m, Yaris {carSize}, obr kapsle {GiantRadius} x {GiantHeight} m");
            Section(report, "Yaris narazi do", carHits);
            Section(report, "Obr (za Yarisem) narazi do", giantHits);
            Section(report, "Obr pri obchazeni Yarisu narazi do (jen vzhled - obr je kinematicky, nezasekne se)", crossHits);
            report.AppendLine(ground.Count == 0 ? "Zem pod koly: rovna po cele trase." : $"! Nerovnost / dira pod koly: {Ranges(ground)}");
            report.AppendLine(noWalk.Count == 0 ? "Obejiti: Yaris jde vsude obejit aspon z jedne strany." : $"! Yaris nejde obejit (ani vlevo, ani vpravo): {Ranges(noWalk)}");
            report.AppendLine(drops.Count == 0 ? "Okoli trasy: zadny sraz do 3,5 m (robot nikoho neshodi)." : $"! Sraz / bez zeme vedle trasy: {Ranges(drops)}");
            bool ok = carHits.Count == 0 && giantHits.Count == 0 && ground.Count == 0 && noWalk.Count == 0;
            System.IO.File.WriteAllText("Temp/PushRouteAudit.txt", report.ToString());
            if (ok) Debug.Log($"[Tlacenice] QA OK - robot projede celou trasu bez zaseknuti, da se kolem nej chodit.\n{report}");
            else Debug.LogWarning($"[Tlacenice] QA nasel problemy (Temp/PushRouteAudit.txt):\n{report}");
        }
        finally
        {
            copyGo.SetActive(wasActive);
        }
    }

    static IEnumerable<Collider> GiantOverlap(Vector3 feet)
    {
        foreach (var c in Physics.OverlapCapsule(feet + Vector3.up * (GiantRadius + 0.35f), feet + Vector3.up * (GiantHeight - GiantRadius), GiantRadius, ~0, QueryTriggerInteraction.Ignore))
            if (c.bounds.max.y > feet.y + 0.35f) yield return c;
    }

    static bool PlayerFits(Vector3 at, HashSet<Collider> ignore)
    {
        if (!GroundAt(at, 1f, out var ground, ignore) || Mathf.Abs(ground.y - at.y) > 0.6f) return false;
        foreach (var c in Physics.OverlapCapsule(ground + Vector3.up * 0.55f, ground + Vector3.up * 1.6f, 0.4f, ~0, QueryTriggerInteraction.Ignore))
            if (!ignore.Contains(c)) return false;
        return true;
    }

    static void Note(Dictionary<string, List<float>> hits, string name, float s)
    {
        if (!hits.TryGetValue(name, out var list)) hits[name] = list = new List<float>();
        list.Add(s);
    }

    static void Section(StringBuilder report, string title, Dictionary<string, List<float>> hits)
    {
        if (hits.Count == 0) { report.AppendLine($"{title}: nic"); return; }
        report.AppendLine($"! {title}:");
        foreach (var pair in hits) report.AppendLine($"    {pair.Key}  (s = {Ranges(pair.Value)})");
    }

    // 1.0, 1.25, 1.5, 7.0 -> "1-1,5 m, 7 m"
    static string Ranges(List<float> values)
    {
        values.Sort();
        var parts = new List<string>();
        int start = 0;
        for (int i = 1; i <= values.Count; i++)
        {
            if (i < values.Count && values[i] - values[i - 1] <= 0.6f) continue;
            parts.Add(values[i - 1] - values[start] < 0.01f ? $"{values[start]:0.#} m" : $"{values[start]:0.#}-{values[i - 1]:0.#} m");
            start = i;
        }
        return string.Join(", ", parts);
    }

    // ---------------- kontrola: puvodni rezimy a mapy beze zmeny ----------------

    [MenuItem("BrosOverwatch/Tlacenice/Kontrola - puvodni rezimy a Domasov beze zmeny")]
    static void Check()
    {
        var errors = new List<string>();
        void Require(bool ok, string message) { if (!ok) errors.Add(message); }

        // deathmatch a utok: stejne jako pred tlacenici
        foreach (int map in new[] { MatchManager.MapDomasov, MatchManager.MapHanamura })
            foreach (int mode in new[] { MatchManager.ModeDeathmatch, MatchManager.ModeAttack })
                Require(MatchManager.MapSupportsMode(map, mode), $"{MatchManager.MapNames[map]} ztratil podporu rezimu {mode}.");
        Require(MatchManager.MapSupportsMode(MatchManager.MapDust2, MatchManager.ModeDeathmatch), "Dust II ztratil deathmatch.");
        Require(!MatchManager.MapSupportsMode(MatchManager.MapDust2, MatchManager.ModeAttack), "Dust II nesmi mit utok.");
        Require(MatchManager.NextMapForMode(MatchManager.MapHanamura, MatchManager.ModeAttack) == MatchManager.MapDomasov, "Utok: po Hanamure ma prijit Domasov.");
        Require(MatchManager.NextMapForMode(MatchManager.MapHanamura, MatchManager.ModeDeathmatch) == MatchManager.MapDust2, "Deathmatch: po Hanamure ma prijit Dust II.");
        Require(MatchManager.NextMapForMode(MatchManager.MapDust2, MatchManager.ModeDeathmatch) == MatchManager.MapDomasov, "Deathmatch: nova mapa se nesmi nabizet.");
        // tlacenice jen na nove mape, nova mapa jen pro tlacenici
        for (int map = 0; map < MatchManager.MapNames.Length; map++)
        {
            Require(MatchManager.MapSupportsMode(map, MatchManager.ModePush) == (map == MatchManager.MapDomasovPush), $"Tlacenice na mape {MatchManager.MapNames[map]}.");
            if (map == MatchManager.MapDomasovPush)
            {
                Require(!MatchManager.MapSupportsMode(map, MatchManager.ModeDeathmatch), "Nova mapa nesmi mit deathmatch.");
                Require(!MatchManager.MapSupportsMode(map, MatchManager.ModeAttack), "Nova mapa nesmi mit utok.");
            }
        }
        Require(MatchManager.NextMapForMode(MatchManager.MapDomasov, MatchManager.ModePush) == MatchManager.MapDomasovPush, "Tlacenice nenabizi svou mapu.");

        // puvodni Domasov: nic z tlacenice v nem neni, kopie je samostatny koren
        var source = FindRoot(SourceName);
        var copy = FindRoot(CopyName);
        Require(source != null, "Ve scene chybi Map-Domasov.");
        if (source != null)
        {
            Require(source.GetComponentInChildren<PushRoute>(true) == null && source.GetComponentInChildren<PushRobot>(true) == null, "V puvodnim Domasovu je neco z tlacenice.");
            Require(source.transform.Find("Props/ToyotaYaris") != null, "Puvodni Domasov prisel o Yaris z provozu.");
            Require(source.GetComponentInChildren<YarisTraffic>(true) != null && source.GetComponentInChildren<YarisTraffic>(true).enabled, "Provoz Yarisu v Domasovu neni zapnuty.");
            Require(source.GetComponentInChildren<RolbaGate>(true) != null, "Puvodni Domasov prisel o vrata rolby.");
        }
        if (copy != null)
        {
            Require(!copy.transform.IsChildOf(source != null ? source.transform : copy.transform) || source == null, "Kopie je uvnitr Domasova.");
            Require(copy.GetComponentInChildren<RolbaGate>(true) == null, "V kopii zustaly RolbaGate (prevzala by vrata Domasova).");
            foreach (var t in copy.GetComponentsInChildren<Transform>(true))
                if (t.name == "SneznaRolba" || t.name == "TraktorSVlekem") { errors.Add($"V kopii je {t.name} - traktor by se mohl pripojit ke kopii."); break; }
            Require(Vector3.Distance(copy.transform.position, (source != null ? source.transform.position : Vector3.zero) + Origin) < 0.01f, "Kopie neni posunuta o DomasovPushOrigin.");
        }
        foreach (var name in LooseRoots)
            if (FindRoot(name) == null) errors.Add($"Volny objekt Domasova {name} ve scene chybi (kopie ho nemela smazat).");

        if (errors.Count == 0) Debug.Log("[Tlacenice] Kontrola OK: deathmatch a utok na puvodnich mapach beze zmeny, tlacenice jen na nove mape, puvodni Domasov nedotceny.");
        else Debug.LogError("[Tlacenice] Kontrola nasla problemy:\n  " + string.Join("\n  ", errors));
    }

    // ---------------- nahled ----------------

    [MenuItem("BrosOverwatch/Tlacenice/Nahled - prepnout Domasov <-> Tlacenice")]
    static void TogglePreview()
    {
        var copy = FindRoot(CopyName);
        var source = FindRoot(SourceName);
        if (copy == null || source == null) { Debug.LogWarning("[Tlacenice] Kopie (krok 1) nebo Map-Domasov ve scene neni."); return; }
        bool showCopy = !copy.activeSelf;
        copy.SetActive(showCopy);
        source.SetActive(!showCopy);
        if (showCopy && SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(Origin + new Vector3(-8.5f, 0f, 9f), Quaternion.Euler(55f, 0f, 0f), 110f);
        Debug.Log(showCopy
            ? "[Tlacenice] Nahled: zobrazena kopie (Tlacenice), Domasov skryty. Pred ulozenim sceny se Domasov sam vrati."
            : "[Tlacenice] Nahled: zpet na Domasov.");
    }

    static void RestoreDomasov()
    {
        var copy = FindRoot(CopyName);
        var source = FindRoot(SourceName);
        if (copy == null || source == null || source.activeSelf || !copy.activeSelf) return;
        copy.SetActive(false);
        source.SetActive(true);
        Debug.Log("[Tlacenice] Scena se uklada s Domasovem (nahled kopie vypnut).");
    }

    // ---------------- pomocne ----------------

    static GameObject FindRoot(string name)
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == name) return root;
        return null;
    }

    static bool IsDone(Transform group, string feature) => group.Find(MarksName + "/" + feature) != null;

    static void MarkDone(Transform group, string feature)
    {
        var marks = group.Find(MarksName);
        if (marks == null)
        {
            marks = new GameObject(MarksName).transform;
            Undo.RegisterCreatedObjectUndo(marks.gameObject, "Tlacenice");
            marks.SetParent(group, false);
        }
        var mark = new GameObject(feature);
        Undo.RegisterCreatedObjectUndo(mark, "Tlacenice");
        mark.transform.SetParent(marks, false);
    }

    // Prvni zem pod bodem (shora od 'startAbove' m nad nim): vodorovna plocha.
    static bool GroundAt(Vector3 at, float startAbove, out Vector3 point, HashSet<Collider> ignore = null)
    {
        point = at;
        var hits = Physics.RaycastAll(at + Vector3.up * startAbove, Vector3.down, startAbove + 4f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        foreach (var hit in hits)
        {
            if (ignore != null && ignore.Contains(hit.collider)) continue;
            if (hit.distance >= best) continue;
            best = hit.distance;
            point = hit.point;
            found = hit.normal.y > 0.7f;
        }
        return found;
    }

    static Bounds WorldBounds(Transform t)
    {
        bool any = false;
        var bounds = new Bounds(t.position, Vector3.zero);
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        foreach (var c in t.GetComponentsInChildren<Collider>())
        {
            if (!any) { bounds = c.bounds; any = true; }
            else bounds.Encapsulate(c.bounds);
        }
        return bounds;
    }

    static bool IsProtected(string path)
    {
        foreach (var name in Protected)
            if (path.Contains(name)) return true;
        return false;
    }

    static string PathOf(Transform t, Transform root)
    {
        string path = t.name;
        for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    static int CountChildren(Transform t) => t.GetComponentsInChildren<Transform>(true).Length;

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
}
