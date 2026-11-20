using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Vylepseni male chaty na Domasove (MensiChata). Kazda cast se postavi jen jednou (znacka _Prvek_<jmeno>
// v Map-Domasov/MalaChataVylepseni) - co pak rucne upravis nebo smazes, zustane.
//  - BocniDvere: druhy vchod do prizemi z vychodni terasy (otvor ve zdi, zarubne, kridla otevrena dovnitr, lucerna).
//  - Balkon: balkon pred otvorem v patre na vychod + zebrik z terasy (treti cesta do chaty, rovnou do patra).
//    Balkon zaroven kryje bocni dvere; pod nim latkova stena, o kterou se zebrik opira.
//  - Zabradli: zabradli kolem otvoru schodiste v patre (kryti nahore), s mezerou na seskok dolu.
//  - Vybaveni: kuchynsky kout a jidelni stul v prizemi, gauc k televizi, v patre palanda, gauc, bedny, lampy.
// Rozmery se meri ze sceny (podlahy, steny, otvory). Kazdy kus nabytku se postavi, jen kdyz ma pod sebou podlahu,
// nic neprekryva a neblokuje dvere, schody ani balkon (jinak se vynecha a zapise do konzole).
[InitializeOnLoad]
public static class SmallCottageUpgradeSetup
{
    const string RootName = "MalaChataVylepseni";
    const string Furniture = "Assets/Models/Kenney/FurnitureKit/";
    static readonly string[] Parts = { "BocniDvere", "Balkon", "Zabradli", "Vybaveni" };

    // bocni dvere ve vychodni zdi prizemi (mezi dvema okny)
    const float DoorZ = -1.78f, DoorHalf = 1f, DoorHeight = 2.5f;
    // predni dvere (sever) a zadni dvere (jih) - pruchody, ktere nabytek nesmi zatarasit
    const float FrontDoorMinX = -40.1f, FrontDoorMaxX = -37.2f;

    static int retries;
    static float floorY, upperY, eastOuter, eastInner, westInner, northInner, southInner, southUp, northUp;
    static float holeEastX, holeNorthZ, holeSouthZ;
    static Bounds stairs;
    static Material wood, planks, iron, glow;
    static readonly List<Bounds> keepClear = new List<Bounds>();

    static SmallCottageUpgradeSetup()
    {
        EditorApplication.delayCall += Run;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += Run;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Run;
        };
    }

    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var mapObject = GameObject.Find("Map-Domasov");
        var cottage = mapObject != null ? mapObject.transform.Find("MensiChata") : null;
        if (cottage == null) return;
        var map = mapObject.transform;

        var root = map.Find(RootName);
        if (root != null)
        {
            bool pending = false;
            foreach (var part in Parts) pending |= root.Find("_Prvek_" + part) == null;
            if (!pending) return;
        }

        // nejdriv musi byt hotova fasada chaty a zadni vchod (jine skripty)
        if (cottage.Find("ExterierMaleChaty/_v2") == null || map.Find("Hratelnost/Doplnky/ZadniVstup") == null || !LoadMaterials())
        {
            if (retries++ < 30) EditorApplication.delayCall += Run;
            return;
        }
        if (!Measure(cottage))
        {
            Debug.LogWarning("[Mala chata] Nepodarilo se zmerit chatu (podlahy, steny) - vylepseni nestavim.");
            return;
        }

        root = MapBuildKit.Group(map, RootName);
        bool changed = false;
        changed |= Feature(root, "BocniDvere", g => SideDoor(g, map, root));
        changed |= Feature(root, "Balkon", g => Balcony(g, cottage, root));
        changed |= Feature(root, "Zabradli", StairRailing);
        changed |= Feature(root, "Vybaveni", g => Furnish(g, map));
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            Debug.Log("[Mala chata] Vylepseni male chaty hotove. Uloz scenu (Ctrl+S).", root);
        }
    }

    static bool Feature(Transform root, string name, System.Func<Transform, bool> build)
    {
        if (root.Find("_Prvek_" + name) != null) return false;
        var g = MapBuildKit.Group(root, name);
        bool ok;
        try { ok = build(g); }
        catch (System.Exception e) { Debug.LogException(e); ok = false; }
        if (!ok)
        {
            Object.DestroyImmediate(g.gameObject);
            return false;
        }
        new GameObject("_Prvek_" + name).transform.SetParent(root, false);
        Physics.SyncTransforms();
        return true;
    }

    static bool LoadMaterials()
    {
        wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/House/DarkBoards.mat");
        planks = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wood/Wood_Planks.mat");
        iron = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Garden/Iron.mat");
        glow = MapBuildKit.Mat("Assets/Materials/House/LanternGlow.mat", null, new Color(1f, 0.86f, 0.6f), Vector2.one, 0.4f,
            emission: new Color(2.4f, 1.6f, 0.75f));
        return wood != null && planks != null && iron != null && glow != null;
    }

    // ---------------- mereni ----------------

    static bool Measure(Transform cottage)
    {
        Physics.SyncTransforms();
        var floor = Down(new Vector3(-38f, 4.6f, -3.5f), 4f);
        var upper = Down(new Vector3(-32.5f, 8f, -4.5f), 4f);
        if (floor == null || upper == null) return false;
        floorY = floor.Value;
        upperY = upper.Value;
        if (upperY - floorY < 3f || upperY - floorY > 5f) return false;

        float y = floorY + 1.5f, u = upperY - 0.25f, uw = upperY + 1.2f;
        var e1 = Cast(new Vector3(-24f, y, DoorZ), Vector3.left, 8f);
        var e2 = Cast(new Vector3(-33f, y, DoorZ), Vector3.right, 6f);
        var w = Cast(new Vector3(-41f, y, 2.5f), Vector3.left, 6f);
        var n = Cast(new Vector3(-42.5f, y, 2.5f), Vector3.forward, 6f);
        var s = Cast(new Vector3(-32.5f, y, -4.5f), Vector3.back, 6f);
        var su = Cast(new Vector3(-32.5f, uw, -4.5f), Vector3.back, 8f);
        var nu = Cast(new Vector3(-32.5f, uw, 0f), Vector3.forward, 8f);
        var he = Cast(new Vector3(-38f, u, -4.5f), Vector3.right, 6f);
        var hn = Cast(new Vector3(-38f, u, -4.5f), Vector3.forward, 8f);
        var hs = Cast(new Vector3(-38f, u, -4.5f), Vector3.back, 8f);
        var stairRenderer = cottage.Find("Stairs")?.GetComponent<Renderer>();
        if (e1 == null || e2 == null || w == null || n == null || s == null || su == null || nu == null || he == null || hn == null || hs == null
            || stairRenderer == null) return false;
        eastOuter = e1.Value.x;
        eastInner = e2.Value.x;
        westInner = w.Value.x;
        northInner = n.Value.z;
        southInner = s.Value.z;
        southUp = su.Value.z;
        northUp = nu.Value.z;
        holeEastX = he.Value.x;
        holeNorthZ = hn.Value.z;
        holeSouthZ = hs.Value.z;
        stairs = stairRenderer.bounds;
        if (eastOuter - eastInner < 0.2f || eastOuter - eastInner > 1f) return false;

        // pruchody, ktere nabytek nesmi zatarasit
        keepClear.Clear();
        float f = floorY, top = floorY + 2.2f, ut = upperY + 2.2f;
        keepClear.Add(MinMax(new Vector3(FrontDoorMinX, f, 2.8f), new Vector3(FrontDoorMaxX, top, northInner + 0.3f)));
        keepClear.Add(MinMax(new Vector3(-38.3f, f, -9.2f), new Vector3(-35.7f, top, -6.2f)));                 // zadni dvere
        keepClear.Add(MinMax(new Vector3(stairs.min.x, f, stairs.min.z - 2f), new Vector3(stairs.max.x + 0.2f, top, stairs.min.z)));
        keepClear.Add(MinMax(new Vector3(stairs.min.x, f, stairs.min.z), new Vector3(stairs.max.x + 0.1f, upperY + 0.1f, stairs.max.z)));
        keepClear.Add(MinMax(new Vector3(eastInner - 2.2f, f, DoorZ - DoorHalf), new Vector3(eastOuter + 2.5f, top, DoorZ + DoorHalf)));
        keepClear.Add(MinMax(new Vector3(stairs.min.x, upperY, stairs.max.z - 0.5f), new Vector3(stairs.max.x + 0.3f, ut, stairs.max.z + 2.2f)));
        keepClear.Add(MinMax(new Vector3(eastInner - 2.2f, upperY, -6.3f), new Vector3(eastInner, ut, -2.4f)));   // k balkonu
        keepClear.Add(MinMax(new Vector3(-40.3f, upperY, northUp - 1.2f), new Vector3(-35.3f, ut, northUp + 0.3f)));  // otvor nad vchodem
        return true;
    }

    static float? Down(Vector3 from, float depth)
    {
        var hits = Physics.RaycastAll(from, Vector3.down, depth, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
            if (hit.normal.y > 0.6f) return hit.point.y;
        return null;
    }

    static Vector3? Cast(Vector3 from, Vector3 direction, float distance) =>
        Physics.Raycast(from, direction, out var hit, distance, ~0, QueryTriggerInteraction.Ignore) ? hit.point : (Vector3?)null;

    static Bounds MinMax(Vector3 min, Vector3 max)
    {
        var b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    static Bounds WorldBounds(MeshFilter filter) => MapBuildKit.WorldBounds(filter.sharedMesh.bounds, filter.transform.localToWorldMatrix);

    static string PathOf(Transform t)
    {
        string path = t.name;
        for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    // Kolize v kvadru (bez triggeru a bez veci pod 'ignore').
    static string Occupied(Bounds b, Transform ignore)
    {
        foreach (var c in Physics.OverlapBox(b.center, b.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            if (ignore == null || !c.transform.IsChildOf(ignore)) return PathOf(c.transform);
        return null;
    }

    // ---------------- bocni dvere ----------------

    static bool SideDoor(Transform g, Transform map, Transform root)
    {
        float cx = (eastOuter + eastInner) * 0.5f, top = floorY + DoorHeight;
        var cut = MinMax(new Vector3(eastInner - 0.15f, floorY + 0.05f, DoorZ - DoorHalf), new Vector3(eastOuter + 0.15f, top, DoorZ + DoorHalf));

        // co se ma prorizout (zed, obklad) a co v otvoru byt nesmi (okna, nabytek)
        var pieces = new List<MeshFilter>();
        foreach (var filter in map.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null || filter.transform.IsChildOf(root)) continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled) continue;
            var b = WorldBounds(filter);
            if (!b.Intersects(cut) || b.max.y < floorY + 0.06f) continue;
            if (b.size.x < 1f && b.size.z > 3f && b.min.z < cut.min.z && b.max.z > cut.max.z) pieces.Add(filter);
            else
            {
                Debug.LogWarning($"[Mala chata] V miste bocnich dveri je {PathOf(filter.transform)} - dvere nestavim.", filter);
                return false;
            }
        }
        if (pieces.Count == 0)
        {
            Debug.LogWarning("[Mala chata] Nenasel jsem vychodni zed prizemi - bocni dvere nestavim.");
            return false;
        }

        // pred dvermi a za nimi musi byt volno
        var inside = MinMax(new Vector3(eastInner - 1.5f, floorY + 0.15f, DoorZ - 0.85f), new Vector3(eastInner - 0.05f, floorY + 2.1f, DoorZ + 0.85f));
        var outside = MinMax(new Vector3(eastOuter + 0.05f, floorY + 0.15f, DoorZ - 0.85f), new Vector3(eastOuter + 1.5f, floorY + 2.1f, DoorZ + 0.85f));
        string blocker = Occupied(inside, root) ?? Occupied(outside, root);
        if (blocker != null)
        {
            Debug.LogWarning($"[Mala chata] Pred bocnimi dvermi stoji {blocker} - dvere nestavim.");
            return false;
        }

        // prorezani: puvodni dil se vypne (nesmaze) a nahradi se dily kolem otvoru
        var walls = MapBuildKit.Group(g, "Zed");
        foreach (var filter in pieces)
        {
            var b = WorldBounds(filter);
            var material = filter.GetComponent<MeshRenderer>().sharedMaterial;
            var collider = filter.GetComponent<Collider>();
            bool collide = collider != null && collider.enabled;
            int layer = filter.gameObject.layer;
            float z0 = DoorZ - DoorHalf, z1 = DoorZ + DoorHalf;
            void Piece(string suffix, Vector3 center, Vector3 size)
            {
                var piece = MapBuildKit.Box(walls, filter.name + suffix, center, size, material, collide);
                piece.layer = layer;
            }
            Piece("_Jih", new Vector3(b.center.x, b.center.y, (b.min.z + z0) * 0.5f), new Vector3(b.size.x, b.size.y, z0 - b.min.z));
            Piece("_Sever", new Vector3(b.center.x, b.center.y, (z1 + b.max.z) * 0.5f), new Vector3(b.size.x, b.size.y, b.max.z - z1));
            if (b.max.y - top > 0.05f)
                Piece("_Nadprazi", new Vector3(b.center.x, (top + b.max.y) * 0.5f, DoorZ), new Vector3(b.size.x, b.max.y - top, z1 - z0));
            if (floorY - b.min.y > 0.05f)
                Piece("_Podprazi", new Vector3(b.center.x, (b.min.y + floorY) * 0.5f, DoorZ), new Vector3(b.size.x, floorY - b.min.y, z1 - z0));
            filter.gameObject.SetActive(false);
            Debug.Log($"[Mala chata] {PathOf(filter.transform)} vypnut a nahrazen dily s otvorem pro bocni dvere.");
        }

        // zarubne (pevne), prah
        float depth = eastOuter - eastInner + 0.1f;
        foreach (float s in new[] { -1f, 1f })
            MapBuildKit.Box(g, "Zarubne", new Vector3(cx, floorY + DoorHeight * 0.5f, DoorZ + s * (DoorHalf - 0.06f)),
                new Vector3(depth, DoorHeight, 0.12f), wood, true);
        MapBuildKit.Box(g, "Zarubne_Horni", new Vector3(cx, top - 0.06f, DoorZ), new Vector3(depth, 0.12f, DoorHalf * 2f), wood, true);
        MapBuildKit.Box(g, "Prah", new Vector3(cx, floorY + 0.012f, DoorZ), new Vector3(depth, 0.024f, DoorHalf * 2f - 0.24f), wood, false);

        // dve kridla otevrena dovnitr, oprena o zed
        float leaf = DoorHalf - 0.12f, leafHeight = DoorHeight - 0.16f;
        foreach (float s in new[] { -1f, 1f })
        {
            float z = DoorZ + s * (DoorHalf + leaf * 0.5f + 0.02f);
            MapBuildKit.Box(g, "Kridlo", new Vector3(eastInner - 0.04f, floorY + 0.02f + leafHeight * 0.5f, z),
                new Vector3(0.06f, leafHeight, leaf), planks, true);
            foreach (float h in new[] { 0.35f, leafHeight - 0.35f })
                MapBuildKit.Box(g, "Kridlo_Pant", new Vector3(eastInner - 0.075f, floorY + h, z), new Vector3(0.015f, 0.08f, leaf - 0.1f), wood, false);
            MapBuildKit.Box(g, "Klika", new Vector3(eastInner - 0.09f, floorY + 1.05f, z + s * (leaf * 0.5f - 0.1f)),
                new Vector3(0.04f, 0.04f, 0.13f), iron, false);
        }

        // lucerna vedle dveri (mezi zarubni a okenici)
        Vector3 lamp = new Vector3(eastOuter + 0.13f, floorY + 2.25f, DoorZ + DoorHalf + 0.25f);
        MapBuildKit.Box(g, "Lucerna_Drzak", lamp + new Vector3(-0.07f, 0.21f, 0f), new Vector3(0.14f, 0.04f, 0.04f), iron, false);
        MapBuildKit.Box(g, "Lucerna", lamp, new Vector3(0.18f, 0.28f, 0.18f), glow, true);
        MapBuildKit.Box(g, "Lucerna_Strisska", lamp + Vector3.up * 0.17f, new Vector3(0.24f, 0.05f, 0.24f), iron, false);
        PointLight(g, "Svetlo_Lucerna", lamp + Vector3.right * 0.35f, 7f, 1.3f);

        Item(g, "rugDoormat", new Vector3(eastOuter + 0.6f, floorY, DoorZ), 0.02f, 90f, false, ignoreKeepClear: true);
        Debug.Log("[Mala chata] Bocni dvere z vychodni terasy hotove.");
        return true;
    }

    // ---------------- balkon a zebrik ----------------

    static bool Balcony(Transform g, Transform cottage, Transform root)
    {
        // otvor v patre na vychodni strane: mezera mezi dily horni vychodni zdi
        var spans = new List<Bounds>();
        foreach (var filter in cottage.GetComponentsInChildren<MeshFilter>())
        {
            var collider = filter.GetComponent<Collider>();
            if (filter.sharedMesh == null || collider == null || !collider.enabled) continue;
            var b = WorldBounds(filter);
            if (b.size.x < 1f && b.size.z > 1f && b.size.y > 2f && b.max.x > eastInner - 0.1f && b.min.x < eastOuter + 0.1f
                && Mathf.Abs(b.min.y - upperY) < 0.4f) spans.Add(b);
        }
        spans.Sort((a, b) => a.min.z.CompareTo(b.min.z));
        float gapMin = 0f, gapMax = 0f;
        for (int i = 1; i < spans.Count; i++)
        {
            float gap = spans[i].min.z - spans[i - 1].max.z;
            if (gap > 2.5f && gap < 6f) { gapMin = spans[i - 1].max.z; gapMax = spans[i].min.z; break; }
        }
        if (gapMax - gapMin < 2.5f)
        {
            Debug.LogWarning("[Mala chata] Nenasel jsem otvor v patre na vychodni strane - balkon nestavim.");
            return false;
        }

        // balkon od otvoru az nad bocni dvere (kryje je)
        float zS = gapMin - 0.15f, zN = Mathf.Max(gapMax + 0.3f, DoorZ + DoorHalf + 0.23f);
        float edge = eastOuter + 1.9f, slabX0 = eastOuter + 0.03f, cz = (zS + zN) * 0.5f, length = zN - zS;
        float ladderZ = zS + 1.6f, latticeEnd = zS + 3.2f;
        var ground = Down(new Vector3(edge + 0.45f, upperY - 0.6f, ladderZ), 6f);
        if (ground == null || upperY - ground.Value < 2.5f)
        {
            Debug.LogWarning("[Mala chata] Pod balkonem neni terasa - balkon nestavim.");
            return false;
        }
        float gY = ground.Value;
        foreach (float z in new[] { zS + 0.1f, latticeEnd, zN - 0.1f })
        {
            var g2 = Down(new Vector3(edge - 0.1f, upperY - 0.6f, z), 6f);
            if (g2 == null || Mathf.Abs(g2.Value - gY) > 0.25f)
            {
                Debug.LogWarning("[Mala chata] Terasa pod balkonem neni rovna - balkon nestavim.");
                return false;
            }
        }
        string blocker = Occupied(MinMax(new Vector3(eastOuter + 0.1f, gY + 0.1f, zS - 0.05f), new Vector3(edge + 0.8f, upperY + 1.15f, zN + 0.05f)), root);
        if (blocker != null)
        {
            Debug.LogWarning($"[Mala chata] V miste balkonu stoji {blocker} - balkon nestavim.");
            return false;
        }

        // podlaha (v urovni patra), tramy, sloupy az na terasu
        MapBuildKit.Box(g, "Podlaha", new Vector3((slabX0 + edge) * 0.5f, upperY - 0.08f, cz), new Vector3(edge - slabX0, 0.16f, length), planks, true);
        MapBuildKit.Box(g, "Tram", new Vector3(edge - 0.1f, upperY - 0.26f, cz), new Vector3(0.18f, 0.2f, length), wood, true);
        for (float z = zS + 0.1f; z <= zN - 0.05f; z += Mathf.Max(0.8f, (length - 0.2f) / 4f))
            MapBuildKit.Box(g, "Tramek", new Vector3((slabX0 + edge) * 0.5f, upperY - 0.23f, z), new Vector3(edge - slabX0, 0.14f, 0.12f), wood, true);
        foreach (float z in new[] { zS + 0.1f, latticeEnd, zN - 0.1f })
            MapBuildKit.Box(g, "Sloup", new Vector3(edge - 0.1f, (gY + upperY - 0.36f) * 0.5f, z), new Vector3(0.18f, upperY - 0.36f - gY, 0.18f), wood, true);

        // zabradli s mezerou u zebriku
        float rail = edge - 0.05f;
        Railing(g, new Vector3(rail, 0f, zS + 0.05f), new Vector3(rail, 0f, ladderZ - 0.5f), upperY);
        Railing(g, new Vector3(rail, 0f, ladderZ + 0.5f), new Vector3(rail, 0f, zN - 0.05f), upperY);
        Railing(g, new Vector3(slabX0 + 0.05f, 0f, zS + 0.05f), new Vector3(rail, 0f, zS + 0.05f), upperY);
        Railing(g, new Vector3(slabX0 + 0.05f, 0f, zN - 0.05f), new Vector3(rail, 0f, zN - 0.05f), upperY);

        // latkova stena pod balkonem (zebrik se o ni opira, pod balkon se za zebrik neleze)
        var lattice = MapBuildKit.Group(g, "Latka");
        float latticeTop = upperY - 0.36f, latticeZ0 = zS + 0.19f, latticeZ1 = latticeEnd - 0.09f;
        for (float z = latticeZ0 + 0.05f; z < latticeZ1; z += 0.22f)
            MapBuildKit.Box(lattice, "Latka_Svisla", new Vector3(edge - 0.12f, (gY + latticeTop) * 0.5f, z), new Vector3(0.04f, latticeTop - gY, 0.05f), planks, true);
        for (float y = gY + 0.15f; y < latticeTop; y += 0.3f)
            MapBuildKit.Box(lattice, "Latka_Vodorovna", new Vector3(edge - 0.08f, y, (latticeZ0 + latticeZ1) * 0.5f), new Vector3(0.04f, 0.05f, latticeZ1 - latticeZ0), planks, true);

        // zebrik z terasy na balkon
        var ladder = MapBuildKit.Group(g, "Zebrik");
        float ladderX = edge + 0.08f, railTop = upperY + 1f;
        foreach (float s in new[] { -0.28f, 0.28f })
            MapBuildKit.Box(ladder, "Bocnice", new Vector3(ladderX, (gY + railTop) * 0.5f, ladderZ + s), new Vector3(0.07f, railTop - gY, 0.07f), wood, true);
        for (float y = gY + 0.3f; y < upperY + 0.05f; y += 0.3f)
            MapBuildKit.Box(ladder, "Pricka", new Vector3(ladderX, y, ladderZ), new Vector3(0.04f, 0.04f, 0.56f), wood, false);
        var zone = new GameObject("Lezeni");
        zone.transform.SetParent(ladder, false);
        zone.transform.position = new Vector3(edge + 0.45f, (gY + upperY + 0.6f) * 0.5f, ladderZ);
        var climb = zone.AddComponent<Ladder>();
        climb.size = new Vector3(0.9f, upperY + 0.6f - gY, 0.9f);
        climb.grabFacing = Vector3.left;   // chyti jen toho, kdo jde k zebriku (ne kolemjdouci)

        // truhlik s kytkami na zabradli (vizual, pevny)
        MapBuildKit.Box(g, "Truhlik", new Vector3(rail + 0.16f, upperY + 0.88f, (ladderZ + 0.5f + zN) * 0.5f), new Vector3(0.22f, 0.18f, 1.1f), wood, true);
        Debug.Log($"[Mala chata] Balkon v patre ({gapMax - gapMin:0.0} m otvor) a zebrik z terasy ({upperY - gY:0.0} m) hotove.");
        return true;
    }

    // Zabradli z bodu a do b (y se ignoruje) na podlaze 'baseY': sloupky po max 1,2 m, madlo a pricka (vse pevne).
    static void Railing(Transform parent, Vector3 a, Vector3 b, float baseY)
    {
        a.y = b.y = 0f;
        Vector3 d = b - a;
        float length = d.magnitude;
        if (length < 0.1f) return;
        var g = MapBuildKit.Group(parent, "Zabradli");
        int n = Mathf.Max(1, Mathf.CeilToInt(length / 1.2f));
        for (int i = 0; i <= n; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / (float)n);
            MapBuildKit.Box(g, "Sloupek", new Vector3(p.x, baseY + 0.5f, p.z), new Vector3(0.09f, 1f, 0.09f), wood, true);
        }
        var rotation = Quaternion.LookRotation(d / length, Vector3.up);
        Vector3 mid = (a + b) * 0.5f;
        MapBuildKit.Box(g, "Madlo", new Vector3(mid.x, baseY + 1.02f, mid.z), new Vector3(0.1f, 0.06f, length + 0.1f), rotation, wood, true, false);
        MapBuildKit.Box(g, "Pricka", new Vector3(mid.x, baseY + 0.52f, mid.z), new Vector3(0.05f, 0.05f, length), rotation, planks, true, false);
    }

    // ---------------- zabradli kolem schodiste v patre ----------------

    static bool StairRailing(Transform g)
    {
        float ex = holeEastX + 0.05f, nz = holeNorthZ + 0.05f;
        if (ex - stairs.max.x < 2f || nz - holeSouthZ < 3f)
        {
            Debug.LogWarning("[Mala chata] Otvor schodiste v patre ma necekany tvar - zabradli nestavim.");
            return false;
        }
        // severni hrana (od schodu k rohu) a vychodni hrana s mezerou na seskok u rohu
        Railing(g, new Vector3(stairs.max.x + 0.3f, 0f, nz), new Vector3(ex, 0f, nz), upperY);
        Railing(g, new Vector3(ex, 0f, holeSouthZ + 0.05f), new Vector3(ex, 0f, nz - 1.6f), upperY);
        Debug.Log("[Mala chata] Zabradli kolem schodiste v patre (mezera na seskok u severniho rohu).");
        return true;
    }

    // ---------------- nabytek ----------------

    static bool Furnish(Transform g, Transform map)
    {
        int placed = 0;
        void Count(GameObject go) { if (go != null) placed++; }

        // Puvodni nabytek chaty (KenneyDekorace) prisel o modely - zbyly z nej jen neviditelne kolize (hrac
        // o ne zakopaval). Takove prazdne kusy se vypnou (nesmazou) a chata se zaridi znovu.
        foreach (string path in new[] { "KenneyDekorace/MalaChataInterier", "KenneyDekorace/TerasaMaleChaty" })
        {
            var group = map.Find(path);
            if (group == null) continue;
            foreach (Transform item in group)
            {
                if (!item.gameObject.activeSelf || item.GetComponentInChildren<Renderer>(true) != null) continue;
                if (item.GetComponent<Collider>() == null) continue;
                item.gameObject.SetActive(false);
                Debug.Log($"[Mala chata] {PathOf(item)} nemel model, jen neviditelnou kolizi - vypnut.", item);
            }
        }
        Physics.SyncTransforms();

        // prizemi: kuchynska linka podel severni zdi (od zapadni zdi k prednim dverim)
        var ground = MapBuildKit.Group(g, "Prizemi");
        float cursor = westInner + 0.03f;
        foreach (var (model, height) in new[] { ("kitchenFridgeSmall", 1.4f), ("kitchenCabinet", 0.9f), ("kitchenSink", 0.9f), ("kitchenStove", 0.9f) })
        {
            float start = cursor;
            var unit = Item(ground, model, new Vector3(cursor + 0.4f, floorY, northInner - 0.4f), height, 180f, true,
                b => new Vector3(start - b.min.x, 0f, northInner - 0.02f - b.max.z));
            if (unit == null) break;
            Count(unit);
            Bounds ub = BoundsOf(unit);
            cursor = ub.max.x;
            if (model == "kitchenCabinet")
                Count(Item(ground, "radio", new Vector3(ub.center.x, ub.max.y, ub.center.z), 0.22f, 200f, false));
            if (model == "kitchenCabinet" || model == "kitchenSink")
                Count(Item(ground, "kitchenCabinetUpper", new Vector3(ub.center.x, floorY + 1.55f, ub.center.z), 0.6f, 180f, true,
                    b => new Vector3(0f, 0f, northInner - 0.02f - b.max.z), needFloor: false));
        }

        // jidelni stul se dvema zidlemi mezi linkou a schody, nad nim lampa
        float tableX = (westInner + FrontDoorMinX) * 0.5f, tableZ = (stairs.max.z + northInner) * 0.5f - 0.3f;
        var table = Item(ground, "table", new Vector3(tableX, floorY, tableZ), 0.78f, 0f, true);
        if (table != null)
        {
            Count(table);
            Bounds tb = BoundsOf(table);
            Count(Item(ground, "chair", new Vector3(tb.center.x, floorY, tb.min.z - 0.35f), 0.95f, 0f, true));
            Count(Item(ground, "chair", new Vector3(tb.center.x, floorY, tb.max.z + 0.35f), 0.95f, 180f, true));
            if (Physics.Raycast(new Vector3(tableX, floorY + 1.5f, tb.center.z), Vector3.up, out var ceiling, 5f, ~0, QueryTriggerInteraction.Ignore))
            {
                var lamp = Item(ground, "lampSquareCeiling", new Vector3(tableX, ceiling.point.y - 0.32f, tb.center.z), 0.32f, 0f, true, needFloor: false);
                if (lamp != null) { Count(lamp); PointLight(lamp.transform, "TepleSvetlo", new Vector3(tableX, ceiling.point.y - 0.6f, tb.center.z), 7f, 1.4f); }
            }
        }

        // obyvak: televize u vychodni zdi (severne od bocnich dveri), naproti gauc (kryti uprostred prizemi)
        float livingZ = DoorZ + DoorHalf + 1.4f;
        var cabinet = Item(ground, "cabinetTelevision", new Vector3(eastInner - 0.4f, floorY, livingZ), 0.6f, -90f, true,
            b => new Vector3(eastInner - 0.03f - b.max.x, 0f, 0f));
        if (cabinet != null)
        {
            Count(cabinet);
            Bounds cb = BoundsOf(cabinet);
            Count(Item(ground, "televisionVintage", new Vector3(cb.center.x, cb.max.y, cb.center.z), 0.55f, -90f, true));
            Count(Item(ground, "rugRectangle", new Vector3(cb.min.x - 1.7f, floorY, cb.center.z), 0.02f, 90f, false));
            Count(Item(ground, "tableCoffee", new Vector3(cb.min.x - 1.5f, floorY, cb.center.z), 0.42f, 90f, true));
            Count(Item(ground, "loungeSofa", new Vector3(cb.min.x - 3.1f, floorY, cb.center.z), 0.85f, 90f, true));
        }

        // loznice v jihovychodnim rohu: postel, nocni stolek s lampickou
        var bed = Item(ground, "bedSingle", new Vector3(eastInner - 0.8f, floorY, southInner + 1.3f), 0.8f, 0f, true,
            b => new Vector3(eastInner - 0.05f - b.max.x, 0f, southInner + 0.03f - b.min.z));
        if (bed != null)
        {
            Count(bed);
            Bounds bb = BoundsOf(bed);
            var nightstand = Item(ground, "sideTableDrawers", new Vector3(bb.min.x - 0.4f, floorY, southInner + 0.35f), 0.6f, 0f, true,
                b => new Vector3(0f, 0f, southInner + 0.03f - b.min.z));
            if (nightstand != null)
            {
                Count(nightstand);
                Bounds nb = BoundsOf(nightstand);
                var lamp = Item(ground, "lampRoundTable", new Vector3(nb.center.x, nb.max.y, nb.center.z), 0.4f, 0f, false);
                if (lamp != null) { Count(lamp); PointLight(lamp.transform, "TepleSvetlo", new Vector3(nb.center.x, nb.max.y + 0.5f, nb.center.z + 0.3f), 5f, 1f); }
            }
        }

        // vesak u prednich dveri, kytka v rohu
        Count(Item(ground, "coatRackStanding", new Vector3(FrontDoorMaxX + 0.55f, floorY, northInner - 0.35f), 1.6f, 0f, true));
        Count(Item(ground, "pottedPlant", new Vector3(eastInner - 0.45f, floorY, northInner - 0.45f), 1f, 0f, true));

        // patro: palanda u jizni zdi, bedny v rohu, gauc s konferencnim stolkem, knihovna
        var upper = MapBuildKit.Group(g, "Patro");
        Count(Item(upper, "bedBunk", new Vector3(holeEastX + 1f, upperY, southUp + 1.2f), 1.9f, 0f, true,
            b => new Vector3(holeEastX + 0.35f - b.min.x, 0f, southUp + 0.03f - b.min.z)));
        var crate = Item(upper, "cardboardBoxClosed", new Vector3(eastInner - 0.45f, upperY, southUp + 0.45f), 0.6f, 0f, true,
            b => new Vector3(eastInner - 0.03f - b.max.x, 0f, southUp + 0.03f - b.min.z));
        if (crate != null)
        {
            Count(crate);
            Bounds cb = BoundsOf(crate);
            Count(Item(upper, "cardboardBoxClosed", new Vector3(cb.min.x - 0.42f, upperY, cb.center.z + 0.05f), 0.55f, 12f, true));
            var top = Item(upper, "cardboardBoxClosed", new Vector3(cb.center.x, cb.max.y, cb.center.z), 0.5f, -20f, true);
            if (top != null)
            {
                Count(top);
                Bounds topBounds = BoundsOf(top);
                var lamp = Item(upper, "lampRoundTable", new Vector3(topBounds.center.x, topBounds.max.y, topBounds.center.z), 0.4f, 0f, false);
                if (lamp != null) { Count(lamp); PointLight(lamp.transform, "TepleSvetlo", new Vector3(topBounds.center.x, topBounds.max.y + 0.5f, topBounds.center.z), 6f, 1.1f); }
            }
        }

        var sofa = Item(upper, "loungeSofa", new Vector3(eastInner - 1.6f, upperY, northUp - 0.5f), 0.85f, 180f, true,
            b => new Vector3(Mathf.Min(0f, eastInner - 0.7f - b.max.x), 0f, northUp - 0.03f - b.max.z));
        if (sofa != null)
        {
            Count(sofa);
            Bounds sb = BoundsOf(sofa);
            Count(Item(upper, "rugRound", new Vector3(sb.center.x, upperY, sb.min.z - 0.8f), 0.02f, 0f, false));
            Count(Item(upper, "tableCoffee", new Vector3(sb.center.x, upperY, sb.min.z - 0.75f), 0.42f, 0f, true));
            var floorLamp = Item(upper, "lampSquareFloor", new Vector3(sb.max.x + 0.32f, upperY, sb.center.z), 1.5f, 0f, true);
            if (floorLamp != null) { Count(floorLamp); PointLight(floorLamp.transform, "TepleSvetlo", new Vector3(sb.max.x + 0.32f, upperY + 1.3f, sb.center.z - 0.4f), 7f, 1.2f); }
        }
        Count(Item(upper, "bookcaseClosedWide", new Vector3(-42.4f, upperY, northUp - 0.3f), 2f, 180f, true,
            b => new Vector3(0f, 0f, northUp - 0.03f - b.max.z)));

        Debug.Log($"[Mala chata] Vybaveni: postaveno {placed} kusu nabytku a doplnku (prizemi i patro).");
        return placed > 0;
    }

    // Model z Kenney Furniture Kit: vyska 'height', paticka uprostred na 'foot', natoceni 'yaw' (0 = celem k +z).
    // 'adjust' muze po natoceni posunout model podle jeho obalky (ke zdi). Kdyz neni volno, model se smaze.
    static GameObject Item(Transform parent, string model, Vector3 foot, float height, float yaw, bool collision,
        System.Func<Bounds, Vector3> adjust = null, bool needFloor = true, bool ignoreKeepClear = false)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Furniture + model + ".glb");
        if (prefab == null) { Debug.LogWarning("[Mala chata] Chybi model " + model); return null; }
        var root = new GameObject(model);
        root.transform.SetParent(parent, false);
        root.transform.position = foot;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.SetParent(root.transform, false);
        Bounds b = BoundsOf(root);
        if (b.size.y > 0.0001f) instance.transform.localScale *= height / b.size.y;
        b = BoundsOf(root);
        instance.transform.position += new Vector3(foot.x - b.center.x, foot.y - b.min.y, foot.z - b.center.z);
        b = BoundsOf(root);
        if (collision)
        {
            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(b.center);
            box.size = b.size;
        }
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (adjust != null) root.transform.position += adjust(BoundsOf(root));
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
        Physics.SyncTransforms();

        b = BoundsOf(root);
        string why = null;
        if (!ignoreKeepClear)
            foreach (var k in keepClear)
                if (k.Intersects(Shrink(b, 0.03f))) { why = "zatarasil by pruchod"; break; }
        if (why == null)
        {
            string hit = Occupied(Shrink(b, 0.04f), root.transform);
            if (hit != null) why = "prekryva " + hit;
        }
        if (why == null && needFloor)
        {
            var below = Down(new Vector3(b.center.x, b.min.y + 0.3f, b.center.z), 0.6f);
            if (below == null || Mathf.Abs(below.Value - b.min.y) > 0.08f) why = "nema pod sebou podlahu";
        }
        if (why != null)
        {
            Object.DestroyImmediate(root);
            Physics.SyncTransforms();
            Debug.Log($"[Mala chata] {model} vynechan: {why}.");
            return null;
        }
        return root;
    }

    static Bounds Shrink(Bounds b, float by)
    {
        Vector3 e = b.extents - Vector3.one * by;
        return new Bounds(b.center, Vector3.Max(e, Vector3.one * 0.005f) * 2f);
    }

    static Bounds BoundsOf(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    static void PointLight(Transform parent, string name, Vector3 at, float range, float intensity)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.transform.SetParent(parent, true);
        light.transform.position = at;
        light.type = LightType.Point;
        light.color = new Color(1f, 0.78f, 0.5f);
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
    }
}
