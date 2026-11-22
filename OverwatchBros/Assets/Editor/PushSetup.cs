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
//  3. QA audit trasy - projede Yaris i Honzu po cele trase, hlasi kolize, nerovnosti a mista,
//     kde se neda Yaris obejit. Zprava v Temp/PushRouteAudit.txt.
// Nahled mapy v editoru: BrosOverwatch > Nahled mapy (MapPreviewMenu).
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

    // Trasa pres celou mapu (x, z v souradnicich puvodniho Domasova): konec u zakladny tymu 0 (modri, JV stodola) ->
    // jizne od velke chaty -> sever mezi chatami -> START (roh nad tunelem, Honza a oba Yarisy) -> zapad -> rovne po
    // z 48,3 (narovnany uz pred stodolou) kolem stodoly a dirou v zapadnim plotu (z 46-53; od zdi stodoly i od zivych
    // plotu diry 0,4 m) -> pres rovny dvur rozhledny (1 m od noh vezi) -> konec za dvorem (tym 1, cerveni).
    // Kazdy Yaris to ma do cile stejne daleko.
    static readonly Vector2[] RoutePoints =
    {
        new Vector2(23.5f, -23f), new Vector2(-8.5f, -23f), new Vector2(-8.5f, 41f), new Vector2(-20f, 41f),
        new Vector2(-27f, 48.3f), new Vector2(-97f, 48.3f),
    };
    // Spawny tymu: zakladna a po 1. a 2. checkpointu. Nazev = znacka v kopii (jeji poloha), jinak souradnice.
    static readonly (string marker, Vector2 at)[][] TeamSpawns =
    {
        new[] { ("SpawnPoint_Team0", new Vector2(33.6f, -33.2f)),        // modri: JV stodola
                ((string)null, new Vector2(4f, 18f)),                    //   travnik u velke chaty
                ((string)null, new Vector2(-1f, 47f)) },                 //   travnik vychodne od STARTu
        new[] { ("SpawnPoint_Utok_A", new Vector2(-129.3f, 86.6f)),      // cerveni: stodola statku (SZ)
                ("SpawnPoint_Team1", new Vector2(-38.7f, 54.7f)),        //   stodola u diry v plotu
                ((string)null, new Vector2(-20f, 28f)) },                //   za malou chatou
    };

    // Koridor, ktery musi byt pro Yaris a obra volny (od trasy na kazdou stranu, vyska nad zemi).
    const float CorridorHalf = 3.0f;   // jak blizko trasy se hledaji vstupy do tunelu
    const float GiantRadius = 0.85f, GiantHeight = 5.4f;
    // Tyhle casti mapy se nikdy nepresouvaji (jen se nahlasi).
    static readonly string[] Protected = { "HlavniChata", "MensiChata", "MalaChataVylepseni", "Rozhledna", "Tunel", "HraniceProHrace", "Stodola", "PavoukuvWorkshop" };

    // ---------------- 0. znovu od zacatku ----------------

    [MenuItem("BrosOverwatch/Tlacenice/0. Smazat kopii a zacit znovu")]
    static void ResetCopy()
    {
        var copy = FindRoot(CopyName);
        if (copy == null) { Debug.Log("[Tlacenice] Kopie neni - pokracuj krokem 1."); return; }
        if (!EditorUtility.DisplayDialog("Tlacenice", $"Smazat {CopyName} (kopii Domasova pro Tlacenici) vcetne vsech rucnich uprav v ni?\nPuvodni Domasov zustane, jak je. Jde vratit pres Ctrl+Z.", "Smazat", "Zrusit"))
            return;
        Undo.DestroyObjectImmediate(copy);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Tlacenice] Kopie smazana. Ted kroky 1, 2 a 3.");
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
        // rolba: prejmenovat (editorove nastroje Domasova hledaji "SneznaRolba" - nesmi najit kopii); ve hre ji
        // Tractor najde i pod jmenem _Kulisa a vrata RolbaGate si prida sam (plati rolba a vrata zapnute mapy)
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

            if (!IsDone(group, "PoklopTunelu"))
            {
                CoverTunnelRamps(copy, group, route, report);
                MarkDone(group, "PoklopTunelu");
            }
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
        go.transform.position = Origin + new Vector3(-8.5f, 0f, 41f);
        var route = go.AddComponent<PushRoute>();
        report.AppendLine("Trasa:");
        // body na zemi; kde se teren mezi rohy vlni (zapadni cast), pribude bod kazdych <= 5 m (Yaris jede po zemi)
        var points = new List<Vector3>();
        for (int i = 0; i < RoutePoints.Length; i++)
        {
            Vector3 corner = RouteGround(Origin + new Vector3(RoutePoints[i].x, 0f, RoutePoints[i].y));
            if (i > 0)
            {
                Vector3 from = points[points.Count - 1];
                float segment = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(corner.x, corner.z));
                bool uneven = false;
                for (float d = 1f; d < segment; d += 1f)
                    uneven |= Mathf.Abs(RouteGround(Vector3.Lerp(from, corner, d / segment)).y - Mathf.Lerp(from.y, corner.y, d / segment)) > 0.06f;
                if (uneven)
                {
                    // (u rohu nech 6 m volno - zaobleni zatacky)
                    float startGap = i - 1 > 0 ? 6f : 0f, endGap = i < RoutePoints.Length - 1 ? 6f : 0f;
                    int pieces = Mathf.CeilToInt((segment - startGap - endGap) / 5f);
                    for (int k = 0; k <= pieces && pieces > 0; k++)
                    {
                        float d = startGap + (segment - startGap - endGap) * k / pieces;
                        if (d <= 0.5f || d >= segment - 0.5f) continue;
                        points.Add(RouteGround(Vector3.Lerp(from, corner, d / segment)));
                    }
                }
            }
            points.Add(corner);
        }
        for (int i = 0; i < points.Count; i++)
        {
            var node = new GameObject($"Bod_{i}");
            node.transform.SetParent(go.transform, false);
            node.transform.position = points[i];
        }
        report.AppendLine($"  {points.Count} bodu, vyska {MinY(points) - Origin.y:0.00} az {MaxY(points) - Origin.y:0.00} m");
        report.AppendLine($"  delka {route.Length:0.0} m, START (stred) {route.Center:0.0} m = {route.Point(route.Center) - Origin} (Domasov souradnice)");
        report.AppendLine($"  checkpointy {string.Join(" / ", route.checkpoints)} m od startu Yarisu");
        return route;
    }

    // Zem pod bodem trasy: nejvyssi velka plocha (teren, dlazdice, poklop) - ne bedna, seno ani tunel pod zemi.
    static Vector3 RouteGround(Vector3 at)
    {
        Vector3 best = new Vector3(at.x, Origin.y, at.z);
        float bestY = float.MinValue;
        foreach (var hit in Physics.RaycastAll(at + Vector3.up * 6f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (Ignored(hit.collider) || hit.normal.y < 0.7f) continue;
            // rampa do tunelu (pozdeji ji zakryje poklop) a tunel pod zemi nejsou povrch
            if (hit.collider.name == "Ramp" || hit.point.y < Origin.y - 1f) continue;
            Bounds b = hit.collider.bounds;
            bool ground = b.size.x >= 6f || b.size.z >= 6f || hit.collider.name == "PoklopTunelu";
            if (!ground || b.size.y > 6f && hit.collider.name != "Teren") continue;
            if (hit.point.y > bestY) { bestY = hit.point.y; best = hit.point; }
        }
        return best;
    }

    static float MinY(List<Vector3> points) { float y = float.MaxValue; foreach (var p in points) y = Mathf.Min(y, p.y); return y; }
    static float MaxY(List<Vector3> points) { float y = float.MinValue; foreach (var p in points) y = Mathf.Max(y, p.y); return y; }

    // Co lezi v draze Yarisu (skutecny obrys auta, i v zatackach) nebo Honzy: odsunout stranou tak daleko, aby se dalo
    // pohodlne projit kolem - kdyz to nejde, aspon na pruchod hrace; nejde-li ani to, vypnout.
    // Budovy a velke objekty se nehybou - jen se nahlasi. Co do drahy nezasahuje (napr. kulna vedle), zustava.
    static void ClearCorridor(Transform copy, Transform group, PushRoute route, StringBuilder report)
    {
        report.AppendLine("Uklid drahy Yarisu a obra (jen v kopii):");
        Vector3 car = CarSize(copy, group);
        float carHalfWidth = car.x * 0.5f;
        // jak daleko od stredu trasy ma byt blizsi okraj odsunute veci: pohodlny pruchod, jinak aspon pruchod hrace
        float[] clearances = { carHalfWidth + 2.3f, carHalfWidth + 1.3f };
        var moves = new Dictionary<Transform, int>();
        var blocked = new HashSet<string>();
        int changes = 0;
        float length = route.Length;
        var visualOnly = VisualOnly(copy, group);
        for (int pass = 0; pass < 4; pass++)
        {
            bool any = false;
            for (float s = 0f; s <= length; s += 0.5f)
            {
                foreach (var collider in PathOverlap(route, s, car))
                {
                    if (Ignored(collider) || !collider.transform.IsChildOf(copy) || collider.transform.IsChildOf(group)) continue;
                    if (!collider.gameObject.activeInHierarchy) continue;
                    var unit = Unit(collider.transform, copy);
                    string path = PathOf(unit, copy);
                    if (IsProtected(path) || Footprint(unit) > 12f)
                    {
                        if (blocked.Add(path)) report.AppendLine($"  ! BLOKUJE (budova / velky objekt, nehybu): {path}  u s = {s:0} m");
                        continue;
                    }
                    moves.TryGetValue(unit, out int count);
                    var companions = Companions(unit, visualOnly);
                    if (IsThinBarrier(unit, companions))
                    {
                        Disable(unit, companions);
                        report.AppendLine($"  otevreno (plot / latka v draze): {path}{(companions.Count > 0 ? $" + {companions.Count} kusu vzhledu" : "")} (s = {s:0} m)");
                    }
                    else if (count < 2 && MoveAside(unit, route, clearances, carHalfWidth, out float shift, out bool roomy, true, companions))
                    {
                        moves[unit] = count + 1;
                        report.AppendLine($"  posunuto: {path}{(companions.Count > 0 ? $" (i s {companions.Count} kusy vzhledu)" : "")} o {shift:0.0} m stranou{(roomy ? "" : " (jen prostor na pruchod hrace)")} (s = {s:0} m)");
                    }
                    else
                    {
                        Disable(unit, companions);
                        report.AppendLine($"  vypnuto (neni kam posunout): {path} (s = {s:0} m)");
                    }
                    changes++;
                    any = true;
                    Physics.SyncTransforms();
                }
            }
            if (!any) break;
        }
        // Kusy jen na pohled (bez kolize - late a koliky plotu, kdyz se jeho neviditelna kolize odsunula/vypnula):
        // co lezi v draze Yarisu, vypnout - auto ani Honza nesmi projizdet necim, co je videt.
        int visuals = 0;
        foreach (var renderer in copy.GetComponentsInChildren<Renderer>())
        {
            if (renderer.transform.IsChildOf(group) || renderer.GetComponentInParent<Collider>() != null || renderer.GetComponentInChildren<Collider>() != null) continue;
            if (renderer.GetComponentInParent<ZoneGate>() != null) continue;
            Bounds b = renderer.bounds;
            if (b.size.x > 6f || b.size.z > 6f || b.size.y > 8f) continue;
            float s = route.Closest(b.center);
            Vector3 p = route.Point(s), right = route.Right(s);
            float lat = Mathf.Abs(Vector3.Dot(b.center - p, right)) - (Mathf.Abs(b.extents.x * right.x) + Mathf.Abs(b.extents.z * right.z));
            Vector3 along = route.Forward(s);
            float lon = Vector3.Dot(b.center - p, along);
            if (Mathf.Abs(lon) > 0.6f && (s <= 0.1f || s >= length - 0.1f)) continue;   // za konci trasy
            if (lat > carHalfWidth + 0.2f || b.max.y < p.y + 0.5f || b.min.y > p.y + GiantHeight) continue;
            Undo.RecordObject(renderer.gameObject, "Tlacenice");
            renderer.gameObject.SetActive(false);
            visuals++;
            report.AppendLine($"  vypnuto (jen vizual v draze): {PathOf(renderer.transform, copy)} (s = {s:0} m)");
        }
        if (changes == 0 && blocked.Count == 0 && visuals == 0) report.AppendLine("  draha je volna");

        // Pruchod kolem Yarisu: kde nejde projit ani vlevo, ani vpravo, otevrit stranu, kde nestoji budova
        // (plot otevrit, ostatni odsunout dal od trasy). Co nejde, zustava - audit to ukaze.
        report.AppendLine("Pruchod kolem Yarisu:");
        Physics.SyncTransforms();
        visualOnly = VisualOnly(copy, group);
        int opened = 0;
        var tried = new HashSet<Transform>();
        for (float s = 0f; s <= length; s += 0.5f)
        {
            Vector3 p = route.Point(s);
            Vector3 along = route.Point(s + 2.5f) - route.Point(s - 2.5f);
            along.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, along.sqrMagnitude > 1e-4f ? along.normalized : route.Forward(s));
            var leftUnits = WalkBlockers(p - right * (carHalfWidth + 0.9f), copy, group);
            var rightUnits = WalkBlockers(p + right * (carHalfWidth + 0.9f), copy, group);
            if (leftUnits == null || rightUnits == null) continue;   // aspon z jedne strany se projde
            bool leftOk = Movable(leftUnits, copy), rightOk = Movable(rightUnits, copy);
            if (!leftOk && !rightOk) continue;
            var side = rightOk && (!leftOk || rightUnits.Count <= leftUnits.Count) ? rightUnits : leftUnits;
            foreach (var unit in side)
            {
                if (!tried.Add(unit) || !unit.gameObject.activeInHierarchy) continue;
                string path = PathOf(unit, copy);
                var companions = Companions(unit, visualOnly);
                if (IsThinBarrier(unit, companions))
                {
                    Disable(unit, companions);
                    report.AppendLine($"  otevreno (plot vedle Yarisu): {path} (s = {s:0} m)");
                }
                else if (MoveAside(unit, route, new[] { carHalfWidth + 1.4f }, carHalfWidth, out float shift, out _, false, companions))
                    report.AppendLine($"  posunuto: {path} o {shift:0.0} m dal od trasy (s = {s:0} m)");
                else
                {
                    report.AppendLine($"  nejde uvolnit (zustava): {path} (s = {s:0} m)");
                    continue;
                }
                opened++;
                Physics.SyncTransforms();
            }
        }
        if (opened == 0) report.AppendLine("  vsude se da Yaris obejit (nebo nejde nic uvolnit)");
    }

    // Objekty jen na pohled (renderer bez kolize v sobe i v rodicich) - napr. late a koliky plotu.
    static List<Renderer> VisualOnly(Transform copy, Transform group)
    {
        var list = new List<Renderer>();
        foreach (var renderer in copy.GetComponentsInChildren<Renderer>())
            if (!renderer.transform.IsChildOf(group) && renderer.GetComponentInParent<Collider>() == null && renderer.GetComponentInChildren<Collider>() == null)
                list.Add(renderer);
        return list;
    }

    // Neviditelna kolize (bez rendereru) ma vzhled zvlast - kusy vzhledu, ktere se s ni prekryvaji (kolize = vizual).
    static List<Transform> Companions(Transform unit, List<Renderer> visualOnly)
    {
        var list = new List<Transform>();
        if (unit.GetComponentInChildren<Renderer>() != null) return list;
        Bounds b = WorldBounds(unit);
        b.Expand(0.3f);
        foreach (var renderer in visualOnly)
            if (renderer != null && renderer.gameObject.activeInHierarchy && b.Contains(renderer.bounds.center)
                && renderer.bounds.size.x < 6f && renderer.bounds.size.z < 6f)
                list.Add(renderer.transform);
        return list;
    }

    // Plot, latka, zabradli: tenke a nizke - v draze se otevre (nepresouva se, kus plotu jinde by nedaval smysl).
    static bool IsThinBarrier(Transform unit, List<Transform> companions)
    {
        Bounds b = WorldBounds(unit);
        foreach (var t in companions)
        {
            var r = t.GetComponent<Renderer>();
            if (r != null) b.Encapsulate(r.bounds);
        }
        return Mathf.Min(b.size.x, b.size.z) < 0.5f && b.size.y < 1.6f;
    }

    static void Disable(Transform unit, List<Transform> companions)
    {
        Undo.RecordObject(unit.gameObject, "Tlacenice");
        unit.gameObject.SetActive(false);
        foreach (var t in companions)
        {
            Undo.RecordObject(t.gameObject, "Tlacenice");
            t.gameObject.SetActive(false);
        }
    }

    // Co brani hraci projit vedle Yarisu (null = projde se). Bez zeme = prazdna mnozina (nejde nic delat).
    static HashSet<Transform> WalkBlockers(Vector3 at, Transform copy, Transform group)
    {
        if (!SurfaceAt(at, null, out var ground, out _)) return new HashSet<Transform>();
        var units = new HashSet<Transform>();
        foreach (var c in Physics.OverlapCapsule(ground + Vector3.up * 0.55f, ground + Vector3.up * 1.6f, 0.4f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (Ignored(c) || c.transform.IsChildOf(group)) continue;
            if (!c.transform.IsChildOf(copy)) { units.Add(c.transform); continue; }
            units.Add(Unit(c.transform, copy));
        }
        return units.Count == 0 ? null : units;
    }

    static bool Movable(HashSet<Transform> units, Transform copy)
    {
        if (units.Count == 0) return false;
        foreach (var unit in units)
            if (!unit.IsChildOf(copy) || IsProtected(PathOf(unit, copy)) || Footprint(unit) > 12f) return false;
        return true;
    }

    // Co prekazi Yarisu (obrys auta + 0,3 m, bez spodnich 30 cm) a Honzovi (chodi po cele trase) na miste s.
    static List<Collider> PathOverlap(PushRoute route, float s, Vector3 car)
    {
        var result = new List<Collider>();
        Vector3 p = route.Point(s);
        Vector3 along = route.Point(s + 2.5f) - route.Point(s - 2.5f);
        along.y = 0f;
        var rotation = Quaternion.LookRotation(along.sqrMagnitude > 1e-4f ? along.normalized : route.Forward(s), Vector3.up);
        float height = car.y + 0.2f - 0.3f;
        foreach (var c in Physics.OverlapBox(p + Vector3.up * (0.3f + height * 0.5f), new Vector3(car.x * 0.5f + 0.3f, height * 0.5f, car.z * 0.5f + 0.3f),
            rotation, ~0, QueryTriggerInteraction.Ignore))
            if (c.bounds.max.y > p.y + 0.35f) result.Add(c);
        foreach (var c in Physics.OverlapCapsule(p + Vector3.up * (GiantRadius + 0.6f), p + Vector3.up * (GiantHeight - GiantRadius),
            GiantRadius + 0.3f, ~0, QueryTriggerInteraction.Ignore))
            if (c.bounds.max.y > p.y + 0.35f) result.Add(c);
        return result;
    }

    // Velikost Yarisu ve smeru trasy (sirka, vyska, delka) - z auta v kopii (provoz nebo uz naklad robota).
    static Vector3 CarSize(Transform copy, Transform group)
    {
        var car = group.Find("Robot/Yaris_T0/ToyotaYaris");
        bool cargo = car != null;
        if (car == null) car = copy.Find("Props/ToyotaYaris");
        var box = car != null ? car.GetComponent<BoxCollider>() : null;
        if (box == null) return new Vector3(3.8f, 3.16f, 7.71f);
        float yaw = 0f;
        var traffic = car.GetComponent<YarisTraffic>();
        if (traffic != null) yaw = traffic.modelYawOffset;
        Quaternion rotation = cargo ? car.localRotation : Quaternion.Euler(0f, yaw, 0f);
        return Abs(rotation * Vector3.Scale(box.size, Abs(car.lossyScale)));
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

    // Posun stranou od trasy tak, aby blizsi okraj veci byl 'clearance' od stredu trasy (zkousi se po rade,
    // nejdriv na blizsi stranu). roomy = vysla prvni (nejvetsi) vzdalenost.
    static bool MoveAside(Transform unit, PushRoute route, float[] clearances, float carHalfWidth, out float shift, out bool roomy, bool bothSides = true, List<Transform> companions = null)
    {
        shift = 0f;
        roomy = false;
        Bounds b = WorldBounds(unit);
        float s = route.Closest(b.center);
        Vector3 p = route.Point(s), right = route.Right(s);
        float lat = Vector3.Dot(b.center - p, right);
        float extent = Mathf.Abs(b.extents.x * right.x) + Mathf.Abs(b.extents.z * right.z);
        float first = lat >= 0f ? 1f : -1f;
        for (int i = 0; i < clearances.Length; i++)
            foreach (float sign in bothSides ? new[] { first, -first } : new[] { first })
            {
                float need = sign * (clearances[i] + extent) - lat;
                if (Mathf.Abs(need) > 9f) continue;
                Vector3 delta = right * need;
                Vector3 target = b.center + delta;
                // jiny kus trasy (v rohu) nesmi byt blizko
                float s2 = route.Closest(target);
                Vector3 q = route.Point(s2), right2 = route.Right(s2);
                float side = Vector3.Dot(target - q, right2);
                float extent2 = Mathf.Abs(b.extents.x * right2.x) + Mathf.Abs(b.extents.z * right2.z);
                if (Mathf.Abs(side) - extent2 < Mathf.Min(clearances[i], carHalfWidth + 1.3f) - 0.05f) continue;
                if (Occupied(target, b.extents, unit)) continue;

                Undo.RecordObject(unit, "Tlacenice - uklid trasy");
                // na zem (stejna vyska nad zemi jako predtim)
                if (GroundAt(b.center, 1f, out var before) && GroundAt(target, 1f, out var after))
                    delta += Vector3.up * (after.y - before.y);
                unit.position += delta;
                if (companions != null)
                    foreach (var t in companions)
                    {
                        Undo.RecordObject(t, "Tlacenice - uklid trasy");
                        t.position += delta;
                    }
                shift = Mathf.Abs(need);
                roomy = i == 0;
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
            if (Ignored(c) || c.transform.IsChildOf(self)) continue;
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
            for (int level = 0; level < TeamSpawns[team].Length; level++)
            {
                string name = $"Spawn_T{team}_{level}";
                if (spawns.Find(name) != null) continue;
                var (markerName, coords) = TeamSpawns[team][level];
                var marker = markerName != null ? copy.Find(markerName) : null;
                Vector3 at = marker != null ? marker.position : RouteGround(Origin + new Vector3(coords.x, 0f, coords.y));

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
                bool blocked = false;
                foreach (var c in Physics.OverlapCapsule(ground + Vector3.up * 0.6f, ground + Vector3.up * 1.75f, 0.45f, ~0, QueryTriggerInteraction.Ignore))
                    if (!Ignored(c)) { blocked = true; break; }
                if (blocked) continue;
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

        // Yarisy: auto z provozu v kopii (ten samy model) pro tym 0 a jeho kopie pro tym 1
        var car = copy.Find("Props/ToyotaYaris");
        if (car != null)
        {
            var box = car.GetComponent<BoxCollider>();
            float yaw = 0f;
            foreach (var traffic in car.GetComponents<YarisTraffic>()) yaw = traffic.modelYawOffset;
            if (box != null)
            {
                // Honza stoji tesne za autem (ruce na kufru)
                Vector3 size = Abs(Quaternion.Euler(0f, yaw, 0f) * Vector3.Scale(car.lossyScale, box.size));
                Undo.RecordObject(route, "Tlacenice");
                route.robotOffset = size.z * 0.5f + GiantRadius + 0.4f;
                route.halfWidth = Mathf.Max(route.halfWidth, size.x * 0.5f + 0.5f);
                report.AppendLine($"  Yaris {size} m, Honza {route.robotOffset:0.0} m za stredem auta");
            }
            var second = Object.Instantiate(car.gameObject, car.parent);
            second.name = car.name;
            Undo.RegisterCreatedObjectUndo(second, "Tlacenice");
            robot.cargo = MakeCargo(root.transform, route, 0, car, yaw, copy, report);
            robot.cargo1 = MakeCargo(root.transform, route, 1, second.transform, yaw, copy, report);
        }
        else
        {
            robot.cargo = PlaceholderCargo(root.transform, route, 0);
            robot.cargo1 = PlaceholderCargo(root.transform, route, 1);
            report.AppendLine("  ! Props/ToyotaYaris v kopii neni - misto aut kvadry (vymen za model)");
        }

        // obr: kolize (kapsle), model Honzy se vlozi az pri spusteni (vzdy aktualni prefab postavy)
        var giant = new GameObject("Obr").transform;
        giant.SetParent(root.transform, false);
        giant.SetPositionAndRotation(route.Point(center), Quaternion.LookRotation(route.Right(center), Vector3.up));
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

    // Pivot Yarisu tymu (na zemi uprostred auta, predkem k zakladne soupere - Honza tlaci zezadu) na startu a v nem
    // auto z provozu. Rucni otoceni pivotu v editoru hra zachova (PushRobot si ho pamatuje).
    static Transform MakeCargo(Transform root, PushRoute route, int team, Transform car, float yaw, Transform copy, StringBuilder report)
    {
        float s = MatchManager.PushCarStart(route, team);
        var pivot = new GameObject($"Yaris_T{team}").transform;
        pivot.SetParent(root, false);
        pivot.SetPositionAndRotation(route.Point(s), Quaternion.LookRotation(route.Forward(s) * MatchManager.PushDirection(team), Vector3.up));
        foreach (var traffic in car.GetComponents<YarisTraffic>()) Undo.DestroyObjectImmediate(traffic);
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
        foreach (var t in car.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        report.AppendLine($"  Yaris tymu {team}: start s = {s:0.0} m{(box != null ? "" : "  ! bez BoxCollideru")} (v hre dobarveny barvou tymu)");
        return pivot;
    }

    static Transform PlaceholderCargo(Transform root, PushRoute route, int team)
    {
        float s = MatchManager.PushCarStart(route, team);
        var pivot = new GameObject($"Yaris_T{team}").transform;
        pivot.SetParent(root, false);
        pivot.SetPositionAndRotation(route.Point(s), Quaternion.LookRotation(route.Forward(s), Vector3.up));
        var placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
        placeholder.name = "YarisNahrada";
        placeholder.transform.SetParent(pivot, false);
        placeholder.transform.localPosition = new Vector3(0f, 0.75f, 0f);
        placeholder.transform.localScale = new Vector3(1.7f, 1.5f, 3.9f);
        placeholder.AddComponent<Rigidbody>().isKinematic = true;
        return pivot;
    }

    // ---------------- 3. QA audit ----------------

    [MenuItem("BrosOverwatch/Tlacenice/3. QA audit trasy (projede robota po cele trase)")]
    static void Audit()
    {
        var copyGo = FindRoot(CopyName);
        var group = copyGo != null ? copyGo.transform.Find(GroupName) : null;
        var route = group != null && group.Find("Trasa") != null ? group.Find("Trasa").GetComponent<PushRoute>() : null;
        var robot = group != null && group.Find("Robot") != null ? group.Find("Robot").GetComponent<PushRobot>() : null;
        if (route == null || robot == null || robot.cargo == null || robot.cargo1 == null)
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
            var ground = new List<float>();
            var noWalk = new List<float>();
            var drops = new List<float>();
            var why = new Dictionary<string, List<float>>();   // co presne brani obejiti / chybi zem
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
                    if (!ignore.Contains(c) && !Ignored(c) && c.bounds.max.y > p.y + 0.3f) Note(carHits, PathOf(c.transform, copyGo.transform), s);

                // Honza (chodi po cele trase mezi Yarisy)
                foreach (var c in GiantOverlap(p))
                    if (!ignore.Contains(c)) Note(giantHits, PathOf(c.transform, copyGo.transform), s);

                // zem pod koly
                foreach (var corner in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) })
                {
                    Vector3 wheel = p + rotation * Vector3.Scale(corner, new Vector3(carSize.x * 0.45f, 0f, carSize.z * 0.4f));
                    if (!GroundAt(wheel, 1f, out var hit, ignore) || Mathf.Abs(hit.y - p.y) > 0.3f) { ground.Add(s); break; }
                }

                // da se Yaris obejit (hrac vlevo nebo vpravo)? a neni vedle trasy sraz?
                bool leftFree = PlayerFits(p - right * (carSize.x * 0.5f + 0.9f), ignore, out string leftWhy);
                bool rightFree = PlayerFits(p + right * (carSize.x * 0.5f + 0.9f), ignore, out string rightWhy);
                if (!leftFree && !rightFree)
                {
                    noWalk.Add(s);
                    Note(why, $"obejiti vlevo: {leftWhy}", s);
                    Note(why, $"obejiti vpravo: {rightWhy}", s);
                }
                foreach (float sign in new[] { -1f, 1f })
                    if (!SurfaceAt(p + right * sign * 3.5f, ignore, out var edge, out string groundWhy) || edge.y < p.y - 1.2f)
                    {
                        drops.Add(s);
                        Note(why, $"zem {(sign < 0 ? "vlevo" : "vpravo")} 3,5 m: {groundWhy}, vyska {edge.y - p.y:0.00} m", s);
                        break;
                    }
            }

            report.AppendLine($"QA trasy Tlacenice: delka {length:0.0} m, START {route.Center:0.0} m, {samples} mist po 0,25 m, Yaris {carSize}, Honza kapsle {GiantRadius} x {GiantHeight} m");
            Section(report, "Yaris narazi do", carHits);
            Section(report, "Honza narazi do", giantHits);
            report.AppendLine(ground.Count == 0 ? "Zem pod koly: rovna po cele trase." : $"! Nerovnost / dira pod koly: {Ranges(ground)}");
            report.AppendLine(noWalk.Count == 0 ? "Obejiti: Yaris jde vsude obejit aspon z jedne strany." : $"! Yaris nejde obejit (ani vlevo, ani vpravo): {Ranges(noWalk)}");
            report.AppendLine(drops.Count == 0 ? "Okoli trasy: zadny sraz do 3,5 m (robot nikoho neshodi)." : $"! Sraz / bez zeme vedle trasy: {Ranges(drops)}");
            if (why.Count > 0) Section(report, "Podrobnosti (co tam je)", why);
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
            if (!Ignored(c) && c.bounds.max.y > feet.y + 0.35f) yield return c;
    }

    static bool PlayerFits(Vector3 at, HashSet<Collider> ignore, out string why)
    {
        if (!SurfaceAt(at, ignore, out var ground, out string groundWhy)) { why = $"neni zem ({groundWhy})"; return false; }
        if (Mathf.Abs(ground.y - at.y) > 0.6f) { why = $"zem o {ground.y - at.y:0.00} m jinde ({groundWhy})"; return false; }
        foreach (var c in Physics.OverlapCapsule(ground + Vector3.up * 0.55f, ground + Vector3.up * 1.6f, 0.4f, ~0, QueryTriggerInteraction.Ignore))
            if (!ignore.Contains(c) && !Ignored(c)) { why = $"stoji tam {PathOfScene(c.transform)}"; return false; }
        why = "";
        return true;
    }

    static bool PlayerFits(Vector3 at, HashSet<Collider> ignore) => PlayerFits(at, ignore, out _);

    // Povrch, na kterem se da stat: nejvyssi velka vodorovna plocha (zem, teren, podlaha, poklop) pod bodem -
    // ne strom ani bedna. Ctyri paprsky kolem bodu (skula mezi dlazdicemi neni dira).
    static bool SurfaceAt(Vector3 at, HashSet<Collider> ignore, out Vector3 point, out string what)
    {
        point = at;
        what = "nic";
        float bestY = float.MinValue;
        foreach (var offset in new[] { Vector3.zero, new Vector3(0.2f, 0f, 0f), new Vector3(-0.2f, 0f, 0f), new Vector3(0f, 0f, 0.2f), new Vector3(0f, 0f, -0.2f) })
            foreach (var hit in Physics.RaycastAll(at + offset + Vector3.up * 1.5f, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore))
            {
                if ((ignore != null && ignore.Contains(hit.collider)) || Ignored(hit.collider) || hit.normal.y < 0.7f) continue;
                Bounds b = hit.collider.bounds;
                if (b.size.x < 2.5f && b.size.z < 2.5f) continue;   // drobnost (bedna, kmen)
                if (hit.point.y <= bestY) continue;
                bestY = hit.point.y;
                point = hit.point;
                what = $"{PathOfScene(hit.collider.transform)} (sklon {Vector3.Angle(hit.normal, Vector3.up):0} st.)";
            }
        return bestY > float.MinValue;
    }

    static string PathOfScene(Transform t)
    {
        string path = t.name;
        for (var p = t.parent; p != null && p.parent != null; p = p.parent) path = p.name + "/" + path;
        return path;
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
        foreach (int map in new[] { MatchManager.MapDomasov, MatchManager.MapHanamura, MatchManager.MapSapa })
            foreach (int mode in new[] { MatchManager.ModeDeathmatch, MatchManager.ModeAttack })
                Require(MatchManager.MapSupportsMode(map, mode), $"{MatchManager.MapNames[map]} ztratil podporu rezimu {mode}.");
        Require(MatchManager.MapSupportsMode(MatchManager.MapDust2, MatchManager.ModeDeathmatch), "Dust II ztratil deathmatch.");
        Require(!MatchManager.MapSupportsMode(MatchManager.MapDust2, MatchManager.ModeAttack), "Dust II nesmi mit utok.");
        Require(MatchManager.NextMapForMode(MatchManager.MapHanamura, MatchManager.ModeAttack) == MatchManager.MapSapa, "Utok: po Hanamure ma prijit Sapa.");
        Require(MatchManager.NextMapForMode(MatchManager.MapHanamura, MatchManager.ModeDeathmatch) == MatchManager.MapDust2, "Deathmatch: po Hanamure ma prijit Dust II.");
        Require(MatchManager.NextMapForMode(MatchManager.MapDust2, MatchManager.ModeDeathmatch) == MatchManager.MapSapa, "Deathmatch: po Dust II ma prijit Sapa.");
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

    // Brany zon (ZoneGate - ploty a mrize rezimu Utok) jsou mimo utok otevrene: bez kolize, schovane. V Tlacenici neprekazi.
    static bool Ignored(Collider c) => c == null || c.isTrigger || !c.enabled || c.GetComponentInParent<ZoneGate>() != null;

    // Vstup do tunelu (rampa dolu), pres ktery vede trasa: v kopii zakryt pevnym drevenym poklopem v urovni zeme
    // (Yaris ani obr by nemeli po cem jet a hraci by vedle robota padali do tunelu). Tunel ma dalsi vstupy.
    static void CoverTunnelRamps(Transform copy, Transform group, PushRoute route, StringBuilder report)
    {
        report.AppendLine("Vstupy do tunelu pod trasou:");
        var ramps = copy.Find("Tunel/Ramps");
        if (ramps == null) { report.AppendLine("  (Tunel/Ramps v kopii neni)"); return; }
        var wood = MapBuildKit.Mat("Assets/Materials/Dekorace/PoklopTunelu.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.45f, 0.32f, 0.2f), Vector2.one, 0.25f);
        int covered = 0;
        foreach (Transform ramp in ramps)
        {
            var collider = ramp.GetComponent<Collider>();
            if (collider == null) continue;
            Bounds b = collider.bounds;
            bool underRoute = false;
            for (float s = 0f; s <= route.Length && !underRoute; s += 0.5f)
            {
                Vector3 p = route.Point(s);
                float dx = Mathf.Max(0f, Mathf.Abs(p.x - b.center.x) - b.extents.x);
                float dz = Mathf.Max(0f, Mathf.Abs(p.z - b.center.z) - b.extents.z);
                underRoute = Mathf.Sqrt(dx * dx + dz * dz) < CorridorHalf + 1f;
            }
            if (!underRoute) continue;
            float top = b.max.y + 0.02f;   // (horni hrana rampy = zem)
            var lid = MapBuildKit.Box(group, "PoklopTunelu", new Vector3(b.center.x, top - 0.1f, b.center.z),
                new Vector3(b.size.x + 0.5f, 0.2f, b.size.z + 0.5f), Quaternion.identity, wood, true);
            Undo.RegisterCreatedObjectUndo(lid, "Tlacenice");
            lid.AddComponent<AudioSurface>().kind = AudioSurfaceKind.Wood;
            covered++;
            report.AppendLine($"  zakryto poklopem: {PathOf(ramp, copy)} (x {b.min.x - Origin.x:0.0}..{b.max.x - Origin.x:0.0}, z {b.min.z:0.0}..{b.max.z:0.0})");
        }
        if (covered == 0) report.AppendLine("  zadny");
    }

    // Prvni zem pod bodem (shora od 'startAbove' m nad nim): vodorovna plocha.
    static bool GroundAt(Vector3 at, float startAbove, out Vector3 point, HashSet<Collider> ignore = null) => GroundAt(at, startAbove, out point, ignore, out _);

    static bool GroundAt(Vector3 at, float startAbove, out Vector3 point, HashSet<Collider> ignore, out string what)
    {
        what = "nic";
        point = at;
        var hits = Physics.RaycastAll(at + Vector3.up * startAbove, Vector3.down, startAbove + 4f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        foreach (var hit in hits)
        {
            if ((ignore != null && ignore.Contains(hit.collider)) || Ignored(hit.collider)) continue;
            if (hit.distance >= best) continue;
            best = hit.distance;
            point = hit.point;
            found = hit.normal.y > 0.7f;
            what = $"{PathOfScene(hit.collider.transform)} (sklon {Vector3.Angle(hit.normal, Vector3.up):0} st.)";
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
