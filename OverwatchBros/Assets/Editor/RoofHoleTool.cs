using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Dira do sedlove strechy (hranol z ProBuilderu, napr. HlavniChata/DRUHEPATRO/Prism):
// BrosOverwatch > Dekorace > Dira do strechy. Vyber strechu (hranol, nebo uz prestavenou "StrechaSDirami")
// a stredem okna Scene zamir na misto diry. Puvodni hranol se jen skryje (vzhled i kolize) a misto nej vznikne
// skupina "StrechaSDirami": dve sikme desky se skutecnou tloustkou (stejny material), stity na koncich
// a v deskach obdelnikove diry s ramem z tramu. Kolize = desky, takze dirou jde proskocit i strilet.
// Dalsi diry: znovu stejny prikaz. Velikost dir / tloustka: Inspector komponenty Roof Holes > Prestavet strechu.
public static class RoofHoleTool
{
    const float Margin = 0.35f;   // dira nejmene tak daleko od okraje desky

    [MenuItem("BrosOverwatch/Dekorace/Dira do strechy (vyber strechu, mir na misto diry)")]
    static void AddHole()
    {
        var selected = Selection.activeGameObject;
        if (selected == null) { Debug.LogWarning("[Strecha] Vyber v Hierarchy strechu (napr. HlavniChata/DRUHEPATRO/Prism)."); return; }

        var roof = selected.GetComponentInParent<RoofHoles>();
        Transform source = roof != null ? roof.source : selected.transform;
        if (!Shape.TryRead(source, out var shape))
        {
            Debug.LogWarning("[Strecha] Vybrany objekt nevypada jako sedlova strecha (hranol s hrebenem nahore).");
            return;
        }

        var view = SceneView.lastActiveSceneView;
        if (view == null) { Debug.LogWarning("[Strecha] Otevri okno Scene a zamir jeho stred na misto diry."); return; }
        var ray = new Ray(view.camera.transform.position, view.camera.transform.forward);
        if (!shape.Hit(ray, out int side, out float along, out float down))
        {
            Debug.LogWarning("[Strecha] Stred okna Scene nemiri na tuhle strechu - natoc pohled tak, aby stred mířil na misto diry.");
            return;
        }

        if (roof == null)
        {
            var group = new GameObject("StrechaSDirami");
            Undo.RegisterCreatedObjectUndo(group, "Dira do strechy");
            group.transform.SetParent(source.parent, false);
            roof = group.AddComponent<RoofHoles>();
            roof.source = source;
            // puvodni strecha: pryc vzhled i kolize (zustava jako predloha)
            foreach (var r in source.GetComponentsInChildren<Renderer>())
            {
                Undo.RecordObject(r, "Dira do strechy");
                r.enabled = false;
            }
            foreach (var c in source.GetComponentsInChildren<Collider>())
            {
                Undo.RecordObject(c, "Dira do strechy");
                c.enabled = false;
            }
        }

        Undo.RecordObject(roof, "Dira do strechy");
        float halfU = roof.holeSize.x * 0.5f + Margin, halfV = roof.holeSize.y * 0.5f + Margin;
        roof.holes.Add(new RoofHoles.Hole
        {
            side = side,
            along = Mathf.Clamp(along, halfU, Mathf.Max(halfU, shape.Length - halfU)),
            down = Mathf.Clamp(down, halfV, Mathf.Max(halfV, shape.Slant(side) - halfV)),
        });
        Rebuild(roof);
        Selection.activeGameObject = roof.gameObject;
        Debug.Log($"[Strecha] Dira {roof.holes.Count} ({roof.holeSize.x:0.0} x {roof.holeSize.y:0.0} m). Dalsi: znovu stejny prikaz; velikost v Inspectoru (Roof Holes). Uloz scenu (Ctrl+S).");
    }

    // ---------------- zebrik do diry ----------------

    // Svisly zebrik od podlahy pod dirou skrz diru (bocnice metr nad strechu), oblast lezeni pro hrace.
    // Vyber StrechaSDirami a stredem okna Scene zamir na diru (nebo kamkoli - vezme se nejblizsi dira).
    [MenuItem("BrosOverwatch/Dekorace/Zebrik do diry ve strese (vyber strechu s dirou, mir na diru)")]
    static void AddLadder()
    {
        var roof = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<RoofHoles>() : null;
        if (roof == null || roof.holes.Count == 0 || roof.source == null || !Shape.TryRead(roof.source, out var shape))
        {
            Debug.LogWarning("[Strecha] Vyber v Hierarchy strechu s dirou (StrechaSDirami).");
            return;
        }
        // nejblizsi dira k mistu, kam miri stred okna Scene
        int pick = 0;
        var view = SceneView.lastActiveSceneView;
        if (view != null && shape.Hit(new Ray(view.camera.transform.position, view.camera.transform.forward), out int side, out float along, out float down))
        {
            float best = float.MaxValue;
            for (int i = 0; i < roof.holes.Count; i++)
            {
                var h = roof.holes[i];
                float d = (h.side == side ? 0f : 1000f) + new Vector2(h.along - along, h.down - down).sqrMagnitude;
                if (d < best) { best = d; pick = i; }
            }
        }
        var hole = roof.holes[pick];
        shape.Frame(hole.side, out var origin, out var u, out var v, out var n);
        Vector3 center = origin + u * hole.along + v * hole.down;   // povrch strechy uprostred diry
        Vector3 downhill = new Vector3(v.x, 0f, v.z).normalized;

        // zebrik o kus vys po svahu (hrac stoji pod nim smerem dolu a vejde se do diry), celem do svahu
        // Volny svisly prostor v dire (vodorovne po svahu): dolni hrana je o spodek desky uzsi (deska je sikma).
        // Hrac (polomer 0,5 m) stoupa uprostred volneho prostoru, zebrik je 0,45 m pred nim smerem do svahu.
        float cos = Mathf.Max(0.05f, new Vector2(v.x, v.z).magnitude), sin = Mathf.Abs(v.y);
        float halfOpen = roof.holeSize.y * 0.5f * cos;                 // pul diry vodorovne
        float shift = roof.thickness * sin;                           // o tolik zuzi dolni hranu spodek desky
        float climberOffset = shift * 0.5f;                           // stred volneho prostoru (do svahu)
        if (halfOpen - shift * 0.5f < 0.55f)
            Debug.LogWarning($"[Strecha] Dira je na sikme strese vodorovne jen {2f * (halfOpen - shift * 0.5f):0.00} m - hrac (1 m) se jen tak tak protahne. Zvets Hole Size (napr. 1,8 x 1,8) a Prestavet strechu.");
        const float LadderAhead = 0.45f;
        Vector3 foot = center - downhill * (climberOffset + LadderAhead);
        float top = center.y + (climberOffset + LadderAhead) * sin / cos;   // strecha je u zebriku o kus vys
        float ground;
        Physics.SyncTransforms();
        if (!FloorBelow(foot + Vector3.down * (roof.thickness + 0.3f), roof.transform, out ground))
        {
            Debug.LogWarning("[Strecha] Pod dirou jsem nenasel podlahu.");
            return;
        }

        string name = $"Zebrik_{pick + 1}";
        var old = roof.transform.Find(name);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var g = new GameObject(name).transform;
        Undo.RegisterCreatedObjectUndo(g.gameObject, "Zebrik do strechy");
        g.SetParent(roof.transform, true);
        var rotation = Quaternion.LookRotation(-downhill, Vector3.up);   // dopredu = do svahu (kam hrac vyleze)
        Vector3 P(float x, float y, float z) => new Vector3(foot.x, 0f, foot.z) + rotation * new Vector3(x, 0f, z) + Vector3.up * y;

        // Kopie zebriku, ktery je na mape (rozhledna): stejny vzhled i lezeni. Natoci se do svahu, postavi na podlahu
        // a natahne na vysku (podlaha -> strecha).
        var template = FindMapLadder(out var templateZone);
        if (template == null)
        {
            Debug.LogWarning("[Strecha] Na mape jsem nenasel zebrik (ZebrikRozhledny s Lezeni) - neni co zkopirovat.");
            Undo.DestroyObjectImmediate(g.gameObject);
            return;
        }
        float zoneHeight = templateZone.size.y * templateZone.transform.lossyScale.y;
        Vector3 zc = templateZone.transform.position;
        float srcGround = zc.y - zoneHeight * 0.5f, srcTop = srcGround + zoneHeight - 0.6f;
        var srcPivot = new Vector3(zc.x, srcGround, zc.z);
        var srcRotation = Quaternion.Euler(0f, templateZone.transform.eulerAngles.y, 0f);
        float stretch = Mathf.Max(0.1f, (top - ground) / Mathf.Max(0.5f, srcTop - srcGround));

        // g = cil: pata oblasti lezeni na podlaze, natoceni do svahu, vyska natazena
        g.SetPositionAndRotation(P(0f, ground, -LadderAhead), rotation);
        g.localScale = new Vector3(1f, stretch, 1f);
        var copy = Object.Instantiate(template.gameObject, g);
        copy.name = "Zebrik";
        var inverse = Quaternion.Inverse(srcRotation);
        copy.transform.localPosition = inverse * (template.position - srcPivot);
        copy.transform.localRotation = inverse * template.rotation;
        copy.transform.localScale = template.lossyScale;
        foreach (var t in copy.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

        // oblast lezeni: od podlahy az 1 m nad strechu (natazenim by nad strechou byla moc nizko - ze strechy by
        // nesla chytit a slezt dolu)
        foreach (var zone in copy.GetComponentsInChildren<Ladder>(true))
        {
            float zoneTop = top + 1.0f;
            Vector3 zp = zone.transform.position;
            zone.transform.position = new Vector3(zp.x, (ground + zoneTop) * 0.5f, zp.z);
            float scaleY = Mathf.Max(0.01f, zone.transform.lossyScale.y);
            zone.size = new Vector3(zone.size.x, (zoneTop - ground) / scaleY, zone.size.z);
        }

        Selection.activeGameObject = g.gameObject;
        EditorSceneManager.MarkSceneDirty(roof.gameObject.scene);
        Debug.Log($"[Strecha] {name}: z {ground:0.00} m na strechu ({top:0.00} m). Uloz scenu (Ctrl+S).");

        // kontrola: projde hrac (valec 0,35 m) od podlahy az nad strechu? Vypise, co prekazi.
        Physics.SyncTransforms();
        Vector3 climb = P(0f, ground + 1.0f, -LadderAhead);   // (oblast lezeni kopie je tady)
        var blockers = new System.Collections.Generic.List<string>();
        foreach (var hit in Physics.SphereCastAll(climb, 0.5f, Vector3.up, top + 1.2f - (ground + 1.0f), ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(g) || hit.distance <= 0f) continue;
            string path = hit.collider.name;
            for (var t = hit.collider.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            blockers.Add($"{path} (ve vysce {climb.y + hit.distance:0.0} m)");
        }
        if (Physics.CheckSphere(climb, 0.49f, ~0, QueryTriggerInteraction.Ignore))
            blockers.Add("neco primo v miste, kde hrac u zebriku stoji");
        if (blockers.Count > 0)
            Debug.LogWarning($"[Strecha] {name}: cestu nahoru blokuje:\n  " + string.Join("\n  ", blockers));
        else
            Debug.Log($"[Strecha] {name}: cesta nahoru je volna.");
    }

    // Zebrik z mapy (ZebrikRozhledny...) a jeho oblast lezeni.
    static Transform FindMapLadder(out Ladder zone)
    {
        zone = null;
        foreach (var ladder in Object.FindObjectsByType<Ladder>(FindObjectsInactive.Include))
        {
            var group = ladder.transform.parent;
            if (group == null || !group.name.StartsWith("ZebrikRozhledny")) continue;
            zone = ladder;
            return group;
        }
        return null;
    }

    static void Part(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation, Material material)
    {
        var go = MapBuildKit.Box(parent, name, center, size, rotation, material, false);
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    // Prvni vodorovna plocha pod bodem (bez strechy samotne a hracu).
    static bool FloorBelow(Vector3 from, Transform roof, out float y)
    {
        y = 0f;
        var hits = Physics.RaycastAll(from, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(roof) || hit.normal.y < 0.7f) continue;
            y = hit.point.y;
            return true;
        }
        return false;
    }

    // ---------------- stavba ----------------

    public static void Rebuild(RoofHoles roof)
    {
        if (roof.source == null || !Shape.TryRead(roof.source, out var shape))
        {
            Debug.LogWarning("[Strecha] Chybi puvodni strecha (Source) - neni z ceho stavet.");
            return;
        }
        for (int i = roof.transform.childCount - 1; i >= 0; i--)
            if (!roof.transform.GetChild(i).name.StartsWith("Zebrik"))   // zebriky zustavaji
                Undo.DestroyObjectImmediate(roof.transform.GetChild(i).gameObject);

        var sourceRenderer = roof.source.GetComponentInChildren<Renderer>(true);
        var material = sourceRenderer != null ? sourceRenderer.sharedMaterial : null;
        var beam = MapBuildKit.Mat("Assets/Materials/Dekorace/StrechaRam.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.45f, 0.32f, 0.2f), Vector2.one, 0.2f);

        foreach (int side in new[] { -1, 1 })
            BuildSlope(roof, shape, side, material, beam);

        // stity (trojuhelniky na koncich - drive soucast hranolu)
        for (int end = 0; end < 2; end++)
        {
            var points = shape.Gable(end);
            var mesh = MapBuildKit.FlatMesh("StitStrechy", points, new[] { new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(1f, 0f) });
            var gable = MapBuildKit.MeshObject(roof.transform, "Stit", mesh, Vector3.zero, Quaternion.identity, Vector3.one, material);
            gable.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(gable, StaticEditorFlags.BatchingStatic);
        }
        EditorSceneManager.MarkSceneDirty(roof.gameObject.scene);
    }

    // Jeden svah: obdelnik [0..L] x [0..S] bez der, poskladany z kvadru (pasy mezi hranami der).
    static void BuildSlope(RoofHoles roof, Shape shape, int side, Material material, Material beam)
    {
        float length = shape.Length, slant = shape.Slant(side);
        shape.Frame(side, out Vector3 origin, out Vector3 u, out Vector3 v, out Vector3 n);
        var rotation = Quaternion.LookRotation(v, n);
        float t = Mathf.Max(0.05f, roof.thickness);
        Vector2 hs = roof.holeSize * 0.5f;

        var rects = new List<Rect>();
        foreach (var h in roof.holes)
            if (h.side == side)
                rects.Add(Rect.MinMaxRect(Mathf.Max(0f, h.along - hs.x), Mathf.Max(0f, h.down - hs.y), Mathf.Min(length, h.along + hs.x), Mathf.Min(slant, h.down + hs.y)));

        // rezy podel hrebene v hranach der
        var cutsU = new List<float> { 0f, length };
        foreach (var r in rects) { cutsU.Add(r.xMin); cutsU.Add(r.xMax); }
        cutsU.Sort();
        for (int i = 0; i < cutsU.Count - 1; i++)
        {
            float u0 = cutsU[i], u1 = cutsU[i + 1];
            if (u1 - u0 < 0.01f) continue;
            float mid = (u0 + u1) * 0.5f;
            // diry, ktere tento pas protinaji -> mezery po svahu
            var gaps = new List<Vector2>();
            foreach (var r in rects) if (mid > r.xMin && mid < r.xMax) gaps.Add(new Vector2(r.yMin, r.yMax));
            gaps.Sort((a, b) => a.x.CompareTo(b.x));
            float vStart = 0f;
            foreach (var g in gaps)
            {
                Piece(roof.transform, origin, u, v, n, rotation, u0, u1, vStart, g.x, t, material);
                vStart = Mathf.Max(vStart, g.y);
            }
            Piece(roof.transform, origin, u, v, n, rotation, u0, u1, vStart, slant, t, material);
        }

        // ram kolem der (tramy, mirne nad deskou)
        foreach (var r in rects)
        {
            float w = 0.12f;
            Vector3 P(float a, float b) => origin + u * a + v * b + n * (w * 0.5f - t * 0.5f + 0.02f);
            Frame(roof.transform, P(r.xMin, r.yMin), P(r.xMax, r.yMin), w, beam);
            Frame(roof.transform, P(r.xMin, r.yMax), P(r.xMax, r.yMax), w, beam);
            Frame(roof.transform, P(r.xMin, r.yMin), P(r.xMin, r.yMax), w, beam);
            Frame(roof.transform, P(r.xMax, r.yMin), P(r.xMax, r.yMax), w, beam);
        }
    }

    static void Piece(Transform parent, Vector3 origin, Vector3 u, Vector3 v, Vector3 n, Quaternion rotation,
        float u0, float u1, float v0, float v1, float thickness, Material material)
    {
        if (u1 - u0 < 0.01f || v1 - v0 < 0.01f) return;
        Vector3 center = origin + u * ((u0 + u1) * 0.5f) + v * ((v0 + v1) * 0.5f) - n * (thickness * 0.5f);
        MapBuildKit.Box(parent, "DeskaStrechy", center, new Vector3(u1 - u0, thickness, v1 - v0), rotation, material, true);
    }

    static void Frame(Transform parent, Vector3 a, Vector3 b, float width, Material material)
    {
        var go = MapBuildKit.Beam(parent, "RamDiry", a, b, width, material, false);
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    // ---------------- tvar strechy z hranolu ----------------

    struct Shape
    {
        Transform t;
        int alongAxis, acrossAxis;   // 0 = x, 2 = z (lokalne v hranolu)
        float a0, a1, yTop, yBottom, cRidge, cMin, cMax;

        Vector3 Local(float a, float y, float c)
        {
            var p = new Vector3(0f, y, 0f);
            p[alongAxis] = a;
            p[acrossAxis] = c;
            return p;
        }

        Vector3 W(float a, float y, float c) => t.TransformPoint(Local(a, y, c));

        public float Length => Vector3.Distance(W(a0, yTop, cRidge), W(a1, yTop, cRidge));
        public float Slant(int side) => Vector3.Distance(W(a0, yTop, cRidge), W(a0, yBottom, side < 0 ? cMin : cMax));

        public void Frame(int side, out Vector3 origin, out Vector3 u, out Vector3 v, out Vector3 n)
        {
            origin = W(a0, yTop, cRidge);
            u = (W(a1, yTop, cRidge) - origin).normalized;
            v = (W(a0, yBottom, side < 0 ? cMin : cMax) - origin).normalized;
            n = Vector3.Cross(u, v).normalized;
            if (n.y < 0f) n = -n;
        }

        public Vector3[] Gable(int end)
        {
            float a = end == 0 ? a0 : a1;
            return new[] { W(a, yBottom, cMin), W(a, yTop, cRidge), W(a, yBottom, cMax) };
        }

        // Kam miri paprsek: svah (-1 / +1) a poloha na nem (m podel hrebene, m dolu po svahu).
        public bool Hit(Ray ray, out int side, out float along, out float down)
        {
            side = 0; along = down = 0f;
            float best = float.MaxValue;
            foreach (int s in new[] { -1, 1 })
            {
                Frame(s, out var origin, out var u, out var v, out var n);
                var plane = new Plane(n, origin);
                if (!plane.Raycast(ray, out float distance) || distance >= best) continue;
                Vector3 p = ray.GetPoint(distance) - origin;
                float a = Vector3.Dot(p, u), d = Vector3.Dot(p, v);
                if (a < 0f || a > Length || d < 0f || d > Slant(s)) continue;
                best = distance; side = s; along = a; down = d;
            }
            return side != 0;
        }

        public static bool TryRead(Transform source, out Shape shape)
        {
            shape = default;
            var filter = source != null ? source.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null) return false;
            var vertices = filter.sharedMesh.vertices;
            if (vertices.Length < 6) return false;
            Bounds b = filter.sharedMesh.bounds;
            float top = b.max.y, bottom = b.min.y, eps = Mathf.Max(0.01f, b.size.y * 0.02f);
            // hreben = vrcholy nahore; jeho smer = osa, podel ktere se nejvic lisi
            float rxMin = float.MaxValue, rxMax = float.MinValue, rzMin = float.MaxValue, rzMax = float.MinValue;
            int count = 0;
            foreach (var p in vertices)
            {
                if (p.y < top - eps) continue;
                count++;
                rxMin = Mathf.Min(rxMin, p.x); rxMax = Mathf.Max(rxMax, p.x);
                rzMin = Mathf.Min(rzMin, p.z); rzMax = Mathf.Max(rzMax, p.z);
            }
            if (count < 2) return false;
            bool alongX = rxMax - rxMin >= rzMax - rzMin;
            shape.t = source;
            shape.alongAxis = alongX ? 0 : 2;
            shape.acrossAxis = alongX ? 2 : 0;
            shape.a0 = alongX ? b.min.x : b.min.z;
            shape.a1 = alongX ? b.max.x : b.max.z;
            shape.cRidge = alongX ? (rzMin + rzMax) * 0.5f : (rxMin + rxMax) * 0.5f;
            shape.cMin = alongX ? b.min.z : b.min.x;
            shape.cMax = alongX ? b.max.z : b.max.x;
            shape.yTop = top;
            shape.yBottom = bottom;
            // hreben musi byt uvnitr sirky (jinak to neni sedlova strecha)
            return shape.cRidge > shape.cMin + 0.1f && shape.cRidge < shape.cMax - 0.1f && b.size.y > 0.2f;
        }
    }
}

// Inspector: tlacitka pro prestaveni a odebrani posledni diry.
[CustomEditor(typeof(RoofHoles))]
public class RoofHolesEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var roof = (RoofHoles)target;
        EditorGUILayout.Space();
        if (GUILayout.Button("Prestavet strechu", GUILayout.Height(26)))
            RoofHoleTool.Rebuild(roof);
        if (roof.holes.Count > 0 && GUILayout.Button("Odebrat posledni diru"))
        {
            Undo.RecordObject(roof, "Odebrat diru");
            roof.holes.RemoveAt(roof.holes.Count - 1);
            RoofHoleTool.Rebuild(roof);
        }
        EditorGUILayout.HelpBox("Dalsi diru prida BrosOverwatch > Dekorace > Dira do strechy (stred okna Scene na misto diry).", MessageType.None);
    }
}
