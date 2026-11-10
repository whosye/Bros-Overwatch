using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Druha verze rozvrzeni chat (navrh s novou zapadni casti):
//   - sever mapy za sikmou carou se orizne (objekty se vypnou, podel cary novy zivy plot a neviditelna stena);
//   - na zapad pribude kopcovita louka obehnana zivym plotem: start utocniku (SZ roh) a rozhledna s bodem A;
//   - do mapy se z ni vchazi tremi dirami v zapadni rade stromu (dve na severu, jedna u male chaty) - do dobyti A
//     je v nich modra stena, kterou projdou jen obranci (spawn obrancu pro A je v male chate);
//   - tunel: nova vetev od rozhledny (rampa) pod zapadni hranici k hlavni chodbe pod senem (otevre se po A),
//     sever puvodniho tunelu se zazdi; karburator lezi v nove vetvi;
//   - potok (reka z toboganu) zmizi, zelena skluzavka se otoci do bazenu;
//   - snezna rolba stoji v kulne u male chaty.
//   - (kolo 2) tobogan: puvodni skluzavka s rekou presunuta do nove casti (na zapade, reka na jih a sadem k jezirku).
// Spousti se z ChataKonceptSetup (po prvni verzi); kazda cast jednou (znacka _Prvek_V2_*), rucni upravy zustanou.
public static class ChataNovaCast
{
    const string MatDir = "Assets/Materials/Koncept/";
    const string MeshPath = "Assets/Models/Koncept/NovaCastTeren.asset";

    // nova cast: obrys (x, z) - SZ roh, napojeni na zapadni hranici mapy, JZ...
    static readonly Vector2[] Area =
    {
        new Vector2(-140f, 100f), new Vector2(-51.2f, 70.9f), new Vector2(-51.2f, -21.5f), new Vector2(-57f, -21.5f), new Vector2(-140f, 21f),
    };
    // orez severu: cara z SZ rohu nove casti az k mistu nad silnici, pak rovne na vychod
    static readonly Vector2 CutWest = new Vector2(-140f, 100f), CutBend = new Vector2(8f, 51.5f);
    const float CutEastX = 45f;

    static readonly Vector3 TowerTarget = new Vector3(-82f, 0f, 38f);     // stred rozhledny (bod A)
    static readonly Vector3 AttackStart = new Vector3(-128f, 0f, 86f);
    static readonly Vector3 AttackAfterA = new Vector3(-75f, 0f, 50f);
    const float WestWallX = -51.2f;
    static readonly (float a, float b)[] Holes = { (46f, 53f), (35.5f, 38.5f), (-2f, 2f) };   // diry v zapadni hranici (z)

    // tunel: nova chodba z 30..33 od x -72 k hlavni chodbe (x -1.5); rampa od povrchu x -72..-68, z 18..30
    const float TunZ0 = 30f, TunZ1 = 33f, TunX0 = -72f, TunFloor = -4f, TunCeil = -0.5f;
    static readonly Rect RampRect = Rect.MinMaxRect(-72f, 18f, -68f, 30f);

    static Material hedge, lawn, rock, bark, plank, post, hay, logs, roof, shed, barrier, concrete, concreteDark, lamp, metal;
    static Material[] leaves;

    // kopce (x, z, polomer, vyska) - mirne (svahy do ~25 st., vsude se da chodit)
    static readonly Vector4[] Hills =
    {
        new Vector4(-116f, 36f, 7f, 1.6f), new Vector4(-132f, 28f, 8f, 1.8f), new Vector4(-96f, 22f, 7f, 1.6f),
        new Vector4(-60f, 22f, 6f, 1.4f), new Vector4(-80f, 14f, 6f, 1.4f),
    };
    // tobogan: plosina skluzavky (skluz na jih) a osa reky od konce skluzu k jezirku
    static readonly Vector2 SlideFootAt = new Vector2(-131f, 54.5f);
    static readonly Vector2[] River =
    {
        new Vector2(-131f, 49.6f), new Vector2(-127.5f, 40f), new Vector2(-121f, 27f), new Vector2(-111.5f, 16f),
        new Vector2(-99.5f, 8f), new Vector2(-87.5f, 3f), new Vector2(-76.5f, -1f),
    };
    // hreben (leva, vyvysena cesta k bodu A s posedem) a uvoz (prava, zahloubena cesta)
    static readonly Vector2 RidgeA = new Vector2(-106f, 82f), RidgeB = new Vector2(-72f, 66f);
    const float RidgeHeight = 4.2f, RidgeWidth = 8f;
    static readonly Vector2[] Lane = { new Vector2(-134f, 62f), new Vector2(-120f, 54f), new Vector2(-110f, 42f), new Vector2(-99f, 34f) };
    const float LaneDepth = 1.8f, LaneWidth = 3.6f;
    // rovne plochy: dvur rozhledny (ohrazeny plotem) a dvur statku pred stodolou (spawn utocniku, palisada)
    static readonly Rect Yard = Rect.MinMaxRect(-95f, 27f, -69f, 51f);
    static readonly Rect Farm = Rect.MinMaxRect(-141f, 68f, -108f, 101f);
    const float CourtSouthZ = 70f, CourtEastX = -114f, CourtWestX = -139.3f;
    static readonly Vector2 PosedAt = new Vector2(-84f, 71.65f);       // na hrebeni
    static readonly Vector2 ShedAt = new Vector2(-108f, 62.5f);        // otevreny senik u stredni cesty
    const float ShedYaw = 122f;
    // vyrovnana mista pod stavbami (x, z, polomer): nic nevisi ve vzduchu ani nezapada do svahu
    static readonly Vector3[] Pads =
    {
        new Vector3(-86.3f, 72.7f, 5f),     // posed se schody
        new Vector3(-108f, 62.5f, 6.5f),    // senik
        new Vector3(-98f, 74.5f, 3.5f),     // seno na svahu hrebene
        new Vector3(-63f, 57f, 3.5f),       // seno u der
        new Vector3(-90f, 13f, 3.2f),       // kurnik
        new Vector3(-100f, 20f, 3f),        // polena
    };

    public static bool Apply(Transform map, Transform root, System.Func<Transform, string, System.Action, bool> feature)
    {
        if (!LoadMaterials()) return false;
        bool changed = false;
        changed |= feature(root, "V2_Uklid", () => CleanupV1(map, root));
        changed |= feature(root, "V2_Rozhledna", () => MoveTower(map));
        changed |= feature(root, "V2_Orez", () => CutNorth(map, root));
        changed |= feature(root, "V2_NovaCast", () => NewArea(root));
        changed |= feature(root, "V2_Diry", () => HedgeHoles(map, root));
        changed |= feature(root, "V2_Tunel", () => TunnelBranch(map, root));
        changed |= feature(root, "V2_Tobogan", () => Slide(map));
        changed |= feature(root, "V2_BodySpawny", () => PointsAndSpawns(map));
        changed |= feature(root, "V2_RolbaKulna", () => GroomerShed(map, root));
        changed |= feature(root, "V3_SekceA", () => SectionA(map, root));
        changed |= feature(root, "V4_SekceA_Kolo1", () => SectionA(map, root));
        changed |= feature(root, "V6_ToboganZapad", () => SlideWest(map, root));
        changed |= feature(root, "V6_SekceA_Reka", () => SectionA(map, root));
        changed |= feature(root, "V5_Zebriky", () => FixLadders(map));
        changed |= feature(root, "V7_DiryKridla", () => HoleWings(root));
        changed |= feature(root, "V7_DiryZivyPlot", () => HoleHedges(map, root));
        changed |= feature(root, "V7_TunelVstup", () => TunnelEntrance(root));
        changed |= feature(root, "V8_OrezSmazat", () => RemoveCutArea(map));
        changed |= feature(root, "V8_OrezTravnik", () => ClipGroundCovers(map));
        changed |= feature(root, "V9_HraniceNadTunelem", () => BorderAboveTunnel(root));
        changed |= feature(root, "V10_ZemOprava", () => RepairGroundTiles(map));
        changed |= feature(root, "V11_GarazRolby", () => GroomerGarage(root));
        changed |= feature(root, "V12_GarazDvere", () => GroomerGarageDoor(root));
        changed |= feature(root, "V13_OknaKuchyne", () => SwapKitchenWindows(map));
        changed |= feature(root, "V14_KenneyKolize", () => RefitKenneyColliders(map));
        changed |= feature(root, "V15_OblicejPostavicky", () => DecorTools.HangingFigureFace());
        changed |= feature(root, "V16_ZvukZhrouceni", () =>
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(DecorTools.CollapseSoundPath);
            foreach (var f in Object.FindObjectsByType<FoldingFloor>(FindObjectsInactive.Include))
            {
                Undo.RecordObject(f, "Zvuk zhrouceni");
                f.collapseSound = clip;
                EditorUtility.SetDirty(f);
            }
            Debug.Log($"[Koncept] Sklopna podlaha: zvuk zhrouceni = {(clip != null ? clip.name : "NENALEZEN")} (Povova ultimatka, cast 2).");
        });
        return changed;
    }

    // Kenney dekorace (KenneySceneDressing.Place): kolizni kvadr je na rodici, model je dite. Kdyz se model rucne
    // posune / otoci (napr. kuchyn hlavni chaty), kvadr zustane na puvodnim miste a neviditelne blokuje.
    // Kvadr se znovu spocita z modelu (v lokalnich osach rodice).
    static void RefitKenneyColliders(Transform map)
    {
        var kenney = map.Find("KenneyDekorace");
        if (kenney == null) return;
        var fixedNames = new List<string>();
        foreach (var box in kenney.GetComponentsInChildren<BoxCollider>(true))
        {
            var t = box.transform;
            if (t.GetComponent<Renderer>() != null) continue;
            var renderers = t.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) continue;
            bool any = false;
            Vector3 mn = Vector3.one * float.MaxValue, mx = Vector3.one * float.MinValue;
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = t.InverseTransformPoint(r.transform.TransformPoint(corner));
                    mn = Vector3.Min(mn, local);
                    mx = Vector3.Max(mx, local);
                    any = true;
                }
            }
            if (!any) continue;
            Vector3 center = (mn + mx) * 0.5f, size = mx - mn;
            if ((center - box.center).magnitude < 0.05f && (size - box.size).magnitude < 0.05f) continue;
            Undo.RecordObject(box, "Kenney kolize");
            box.center = center;
            box.size = size;
            fixedNames.Add(Path(map, t));
        }
        Debug.Log($"[Koncept] Kenney kolize srovnany s modely: {fixedNames.Count}" + (fixedNames.Count > 0 ? " (" + string.Join(", ", fixedNames) + ")" : ""));
    }

    // Kuchyn hlavni chaty, vychodni zed (x 26,5..27): prohozeni maleho okna (u rohu, z -13,5..-12,25) a velkeho
    // (z -9,25..-7). Okraje useku zdi zustavaji: velke okno je ted u rohu (z -13,5..-11,25), pilir -11,25..-8,25,
    // male okno z -8,25..-7. Puvodni kusy zdi (ProBuilder) se jen vypnou, nove jsou kvadry s kolizi ze stejne omitky.
    // Venkovni ram, parapet a okenice se postavi znovu (stejne rozmery a materialy jako puvodni); co dalsiho
    // pod okny visi (truhliky), se posune s oknem.
    static void SwapKitchenWindows(Transform map)
    {
        var house = map.Find("HlavniChata");
        if (house == null) { Debug.LogWarning("[Koncept] HlavniChata ve scene neni."); return; }
        var lintelSmall = house.Find("PRIZEMI/kuchyn/Cube (5)");
        var pier = house.Find("PRIZEMI/kuchyn/Cube (4)");
        var lintelBig = house.Find("PRIZEMI/kuchyn/okno (6)/Cube (7)");
        if (lintelSmall == null || pier == null || lintelBig == null)
        {
            Debug.LogWarning("[Koncept] Okna kuchyne: kusy zdi nenalezeny (Cube (5), Cube (4), okno (6)/Cube (7)) - nic nemenim.");
            return;
        }
        var plaster = pier.GetComponent<Renderer>().sharedMaterial;

        // venkovni skupiny Okno (podle okenic)
        Transform smallGroup = null, bigGroup = null;
        var okna = house.Find("Exterier/Okna");
        if (okna != null)
            foreach (Transform g in okna)
            {
                if (g.name != "Okno" || !g.gameObject.activeSelf) continue;
                foreach (Transform c in g)
                {
                    if (c.name != "Okenice") continue;
                    Vector3 p = c.position;
                    if (Mathf.Abs(p.x - 27.19f) > 0.1f) continue;
                    if (Mathf.Abs(p.z + 13.66f) < 0.1f || Mathf.Abs(p.z + 11.96f) < 0.1f) smallGroup = g;
                    if (Mathf.Abs(p.z + 9.97f) < 0.1f || Mathf.Abs(p.z + 6.4f) < 0.1f) bigGroup = g;
                }
            }
        Material boards = null, sill = null;
        foreach (var g in new[] { smallGroup, bigGroup })
            if (g != null)
                foreach (var r in g.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name == "Ram" && boards == null) boards = r.sharedMaterial;
                    if (r.name == "Parapet" && sill == null) sill = r.sharedMaterial;
                }
        if (boards == null) boards = shed;
        if (sill == null) sill = boards;

        // truhliky a jine veci pod okny (mimo zed a skupiny oken): posunou se s oknem
        const float oldSmallZ = -12.81f, oldBigZ = -8.18f, newBigZ = -12.375f, newSmallZ = -7.625f;
        var moves = new List<(Transform t, float dz)>();
        foreach (var r in map.GetComponentsInChildren<Renderer>(true))
        {
            var t = r.transform;
            if (t.IsChildOf(house.Find("PRIZEMI")) || (smallGroup != null && t.IsChildOf(smallGroup)) || (bigGroup != null && t.IsChildOf(bigGroup))) continue;
            Vector3 c = r.bounds.center;
            if (c.x < 27.0f || c.x > 27.9f || c.y < 0.3f || c.y > 1.25f) continue;
            float dz;
            if (Mathf.Abs(c.z - oldSmallZ) < 0.8f) dz = newSmallZ - oldSmallZ;
            else if (Mathf.Abs(c.z - oldBigZ) < 1.3f) dz = newBigZ - oldBigZ;
            else continue;
            moves.Add((t, dz));
        }
        int moved = 0;
        foreach (var (t, dz) in moves)
        {
            bool parentMoves = moves.Exists(m => m.t != t && t.IsChildOf(m.t));
            if (parentMoves) continue;
            Undo.RecordObject(t, "Okna kuchyne");
            t.position += new Vector3(0f, 0f, dz);
            moved++;
        }

        // zed: vypnout puvodni kusy, postavit nove
        foreach (var t in new[] { lintelSmall, pier, lintelBig })
        {
            Undo.RecordObject(t.gameObject, "Okna kuchyne");
            t.gameObject.SetActive(false);
        }
        var wall = house.Find("PRIZEMI/kuchyn/ZedOknaProhozena");
        if (wall != null) Undo.DestroyObjectImmediate(wall.gameObject);
        wall = MapBuildKit.Group(house.Find("PRIZEMI/kuchyn"), "ZedOknaProhozena");
        const float wx = 26.75f, wd = 0.5f;
        Box(wall, "Preklad_Velke", new Vector3(wx, 3.5f, -12.375f), new Vector3(wd, 0.5f, 2.25f), plaster);
        Box(wall, "Pilir", new Vector3(wx, 2.375f, -9.75f), new Vector3(wd, 2.75f, 3f), plaster);
        Box(wall, "Preklad_Male", new Vector3(wx, 3.25f, -7.625f), new Vector3(wd, 1f, 1.25f), plaster);

        // venkovni ram, parapet, okenice
        foreach (var g in new[] { smallGroup, bigGroup })
            if (g != null)
            {
                Undo.RecordObject(g.gameObject, "Okna kuchyne");
                g.gameObject.SetActive(false);
            }
        var outside = okna != null ? okna : house;
        var oldNew = outside.Find("OknaKuchyneProhozena");
        if (oldNew != null) Undo.DestroyObjectImmediate(oldNew.gameObject);
        var group = MapBuildKit.Group(outside, "OknaKuchyneProhozena");
        KitchenWindow(group, newBigZ, 2.25f, 1.005f, 3.195f, boards, sill);
        KitchenWindow(group, newSmallZ, 1.0f, 1.005f, 2.745f, boards, sill);
        Debug.Log($"[Koncept] Okna kuchyne prohozena: velke u rohu (z {newBigZ}), male (z {newSmallZ}); posunuto doplnku pod okny: {moved}.");
    }

    // Okno na vychodni zdi (ven = +x): ram, parapet a otevrene okenice (jako HouseSetup.Window).
    static void KitchenWindow(Transform parent, float z, float width, float bottom, float top, Material boards, Material sill)
    {
        var g = new GameObject("Okno").transform;
        g.SetParent(parent, false);
        float h = top - bottom;
        const float t = 0.11f, d = 0.12f, ox = 27.14f;
        Vector3 c = new Vector3(ox + 0.03f, bottom + h * 0.5f, z);
        MapBuildKit.Box(g, "Ram", c + Vector3.up * (h * 0.5f + t * 0.5f), new Vector3(d, t, width + 2f * t), boards, false);
        MapBuildKit.Box(g, "Ram", c - Vector3.up * (h * 0.5f + t * 0.5f), new Vector3(d, t, width + 2f * t), boards, false);
        MapBuildKit.Box(g, "Ram", c + Vector3.forward * (width * 0.5f + t * 0.5f), new Vector3(d, h, t), boards, false);
        MapBuildKit.Box(g, "Ram", c - Vector3.forward * (width * 0.5f + t * 0.5f), new Vector3(d, h, t), boards, false);
        MapBuildKit.Box(g, "Parapet", new Vector3(ox + 0.1f, bottom - 0.04f, z), new Vector3(0.25f, 0.07f, width + 0.25f), sill, false);
        foreach (float side in new[] { -1f, 1f })
            MapBuildKit.Box(g, "Okenice", c + Vector3.forward * side * (width * 0.5f + 0.1f + width * 0.25f) + Vector3.right * 0.02f,
                new Vector3(0.05f, h + 0.05f, width * 0.5f), boards, false);
    }

    // ---------------- materialy ----------------

    static Material Load(string path) => AssetDatabase.LoadAssetAtPath<Material>(path);

    static bool LoadMaterials()
    {
        hedge = Load("Assets/Materials/Garden/Hedge.mat")
            ?? MapBuildKit.Mat(MatDir + "ZivyPlot.mat", "Assets/Materials/Garden/Leaves.png", new Color(0.78f, 0.9f, 0.72f), new Vector2(0.9f, 0.9f), 0.05f);
        lawn = Load("Assets/Materials/Garden/Lawn.mat")
            ?? MapBuildKit.Mat(MatDir + "Louka.mat", "Assets/Materials/Garden/Lawn.png", Color.white, new Vector2(0.5f, 0.5f), 0.05f);
        rock = MapBuildKit.Mat(MatDir + "Kamen.mat", null, new Color(0.52f, 0.51f, 0.5f), Vector2.one, 0.15f);
        bark = Load("Assets/Materials/Forest/Forest_Bark.mat") ?? Load("Assets/Materials/Garden/Bark.mat");
        leaves = new[]
        {
            Load("Assets/Materials/Forest/Forest_Broadleaf_0.mat"), Load("Assets/Materials/Forest/Forest_Broadleaf_1.mat"),
            Load("Assets/Materials/Forest/Forest_Broadleaf_2.mat"),
        };
        plank = Load(MatDir + "Plot.mat");
        post = Load(MatDir + "Sloupek.mat");
        hay = Load(MatDir + "Seno.mat");
        logs = Load(MatDir + "Polena.mat");
        roof = Load(MatDir + "StrechaKulny.mat");
        shed = Load(MatDir + "Kulna.mat");
        metal = Load(MatDir + "Mriz.mat");
        concrete = Load("Assets/Materials/Tunnel/Tunnel_Concrete.mat");
        concreteDark = Load("Assets/Materials/Tunnel/Tunnel_ConcreteDark.mat");
        lamp = Load("Assets/Materials/Tunnel/Tunnel_Lamp.mat");

        barrier = Load(MatDir + "ModraStena.mat");
        if (barrier == null)
        {
            MapBuildKit.EnsureFolder(MatDir.TrimEnd('/'));
            barrier = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.3f, 0.62f, 1f, 0.32f) };
            AssetDatabase.CreateAsset(barrier, MatDir + "ModraStena.mat");
        }
        bool ok = hedge != null && lawn != null && rock != null && bark != null && plank != null && post != null && hay != null
            && logs != null && roof != null && shed != null && metal != null && concrete != null && concreteDark != null && lamp != null;
        foreach (var l in leaves) ok &= l != null;
        if (!ok) Debug.LogWarning("[Koncept] Nektere materialy chybi (Garden / Forest / Tunnel) - nova cast se postavi pozdeji.");
        return ok;
    }

    // ---------------- geometrie ----------------

    static float CutZ(float x) => x >= CutBend.x ? CutBend.y : Mathf.Lerp(CutWest.y, CutBend.y, (x - CutWest.x) / (CutBend.x - CutWest.x));

    static bool Inside(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        return inside;
    }

    static float EdgeDistance(Vector2 p, Vector2[] poly)
    {
        float best = float.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 a = poly[j], b = poly[i], ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }

    static float RectDistance(Rect r, float x, float z) =>
        Mathf.Max(Mathf.Max(r.xMin - x, x - r.xMax), Mathf.Max(r.yMin - z, z - r.yMax));

    // Vyska nove casti v (x, z) bez vyrovnanych mist: kopce, hreben, uvoz; u okraju, dvora statku,
    // dvora rozhledny a rampy tunelu rovina.
    static float RawH(float x, float z)
    {
        var p = new Vector2(x, z);
        float h = 0f;
        foreach (var hill in Hills)
        {
            float d2 = (x - hill.x) * (x - hill.x) + (z - hill.y) * (z - hill.y);
            h += hill.w * Mathf.Exp(-d2 / (hill.z * hill.z));
        }
        float ridge = SegmentDistance(p, RidgeA, RidgeB);
        h += RidgeHeight * Mathf.Exp(-ridge * ridge / (RidgeWidth * RidgeWidth));
        float lane = float.MaxValue;
        for (int i = 0; i < Lane.Length - 1; i++) lane = Mathf.Min(lane, SegmentDistance(p, Lane[i], Lane[i + 1]));
        h -= LaneDepth * Mathf.Exp(-lane * lane / (LaneWidth * LaneWidth));
        h += 0.7f * Mathf.Exp(-(lane - 5.5f) * (lane - 5.5f) / 6f);   // valy po stranach uvozu

        float fade = Inside(p, Area) ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 12f, EdgeDistance(p, Area))) : 0f;
        fade *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 7f, RectDistance(Farm, x, z)));
        fade *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 6f, RectDistance(Yard, x, z)));
        fade *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 8f, RectDistance(RampRect, x, z)));
        fade *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.5f, 7f, RiverDistance(p, true)));   // rovina pod skluzavkou a rekou
        return 0.06f + h * fade - RiverDip(p);
    }

    // Vyska nove casti v (x, z): pod stavbami (Pads) rovina ve vysce jejich stredu, do 4 m plynule do okoli.
    public static float H(float x, float z)
    {
        float h = RawH(x, z);
        foreach (var pad in Pads)
        {
            float d = Vector2.Distance(new Vector2(x, z), new Vector2(pad.x, pad.y));
            if (d >= pad.z + 4f) continue;
            h = Mathf.Lerp(RawH(pad.x, pad.y), h, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(pad.z, pad.z + 4f, d)));
        }
        return h;
    }

    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material, bool collider = true, float yaw = 0f)
    {
        var go = MapBuildKit.Box(parent, name, center, size, Quaternion.Euler(0f, yaw, 0f), material, collider);
        GameObjectUtility.SetStaticEditorFlags(go, 0);
        return go;
    }

    static void Invisible(Transform parent, string name, Vector3 center, Vector3 size, float yaw = 0f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(center, Quaternion.Euler(0f, yaw, 0f));
        go.AddComponent<BoxCollider>().size = size;
    }

    // Zivy plot podel useku a-b (vyska 3,2 m) a neviditelna stena nad nim.
    static void HedgeLine(Transform parent, Vector2 a, Vector2 b)
    {
        Vector2 d = b - a;
        float length = d.magnitude;
        float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        int pieces = Mathf.Max(1, Mathf.CeilToInt(length / 6f));
        for (int i = 0; i < pieces; i++)
        {
            Vector2 m = Vector2.Lerp(a, b, (i + 0.5f) / pieces);
            float baseY = Mathf.Min(0f, H(m.x, m.y) - 0.1f);
            Box(parent, "ZivyPlot", new Vector3(m.x, baseY + 1.45f, m.y), new Vector3(1.4f, 3.5f, length / pieces + 0.6f), hedge, true, yaw);
        }
        Vector2 c = (a + b) * 0.5f;
        Invisible(parent, "Stena", new Vector3(c.x, 14f, c.y), new Vector3(0.6f, 28f, length), yaw);
    }

    // ---------------- 1. uklid prvni verze (sever) ----------------

    static void CleanupV1(Transform map, Transform root)
    {
        foreach (var path in new[]
        {
            "Hranice/Plot_Sever", "Hranice/Plot_Sever_Stena", "Hranice/Plot_Sever_StenaTobogan", "Hranice/Branka_Silnice",
            "Hranice/Branka_Zapad", "Hranice/Tobogan_Pruchod", "Kryty", "TunelMrize/Mriz_Rozhledna",
        })
        {
            var t = root.Find(path);
            if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
        }

        // posun rozhledny nesmi prestavet tunel a zahradu (pocitaji se od ni): zapamatovat soucasny posun
        if (map.Find("_TowerShiftPin") == null)
        {
            Vector3 shift = MapBuildKit.TowerShift(map);
            var pin = new GameObject("_TowerShiftPin").transform;
            pin.SetParent(map, false);
            pin.localPosition = shift;
            Undo.RegisterCreatedObjectUndo(pin.gameObject, "Koncept");
        }
    }

    // ---------------- 2. rozhledna ----------------

    static void MoveTower(Transform map)
    {
        var tower = map.Find("Rozhledna");
        if (tower == null) { Debug.LogWarning("[Koncept] Rozhledna ve scene neni."); return; }
        var renderers = tower.GetComponentsInChildren<Renderer>();
        bool any = false;
        Bounds b = default;
        foreach (var r in renderers)
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return;
        Vector3 shift = new Vector3(TowerTarget.x - b.center.x, H(TowerTarget.x, TowerTarget.z) - 0.06f, TowerTarget.z - b.center.z);
        Undo.RecordObject(tower, "Koncept");
        tower.position += shift;
        Debug.Log($"[Koncept] Rozhledna presunuta o {shift} (novy stred {TowerTarget}).");
    }

    // ---------------- 3. orez severu ----------------

    static readonly string[] CutSkip = { "KonceptUtoku", "Rozhledna", "Tunel", "PavoukuvWorkshop", "HraniceProHrace", "ProvozYaris", "Props", "_TowerShiftPin" };

    static void CutNorth(Transform map, Transform root)
    {
        int disabled = 0;
        foreach (var t in map.GetComponentsInChildren<Transform>(true))
        {
            if (t == map || !t.gameObject.activeSelf) continue;
            if (t.GetComponent<Renderer>() == null && t.GetComponent<Collider>() == null) continue;
            string path = Path(map, t);
            bool skip = false;
            foreach (var s in CutSkip) if (path.Contains(s)) { skip = true; break; }
            if (skip) continue;

            Bounds b;
            var r = t.GetComponent<Renderer>();
            var c = t.GetComponent<Collider>();
            if (r != null) b = r.bounds; else b = c.bounds;
            if (b.size.x > 40f || b.size.z > 40f || b.max.y < -0.6f) continue;
            bool north = true;
            foreach (float x in new[] { b.min.x, b.max.x })
                if (b.min.z < CutZ(x) + 0.3f) north = false;
            if (!north) continue;
            Undo.RecordObject(t.gameObject, "Koncept");
            t.gameObject.SetActive(false);
            disabled++;
        }

        var g = MapBuildKit.Group(root, "Orez");
        HedgeLine(g, CutWest, CutBend);
        HedgeLine(g, CutBend, new Vector2(CutEastX, CutBend.y));
        Debug.Log($"[Koncept] Sever orezan: vypnuto {disabled} objektu za sikmou carou, podel ni zivy plot.");
    }

    // Plocha za orezem (sever) je nevyuzita - smazat: pasy zeme (Tunel/Ground_Tiles) se zkrati k zivemu plotu
    // (v sikmem useku po 2 m pruzich), sever tunelu za zazdenim (z 47) se zkrati / vypne a vse ostatni, co stoji
    // cele za carou, se vypne. Bod A, rozhledna, Mirkuv workshop a hranice mapy zustanou.
    const float TunnelSealZ = 47.2f;
    static readonly string[] RemoveSkip = { "KonceptUtoku", "Rozhledna", "PavoukuvWorkshop", "HraniceProHrace", "_TowerShiftPin" };

    static void RemoveCutArea(Transform map)
    {
        int trimmed = 0, disabled = 0;
        var tiles = map.Find("Tunel/Ground_Tiles");
        if (tiles != null)
        {
            var list = new List<Transform>();
            foreach (Transform t in tiles) if (t.gameObject.activeSelf && t.GetComponent<Renderer>() != null) list.Add(t);
            foreach (var tile in list)
            {
                Bounds b = tile.GetComponent<Renderer>().bounds;
                float limitHigh = Mathf.Max(CutZ(b.min.x), CutZ(b.max.x)) + 0.8f;
                if (b.max.z <= limitHigh) continue;
                // v sikmem useku rozdelit na pruhy po 2 m, kazdy zkratit zvlast (zem konci pod plotem)
                int strips = Mathf.Abs(CutZ(b.min.x) - CutZ(b.max.x)) > 0.5f ? Mathf.CeilToInt(b.size.x / 2f) : 1;
                float w = b.size.x / strips;
                for (int i = 0; i < strips; i++)
                {
                    Transform part = tile;
                    if (i > 0)
                    {
                        part = Object.Instantiate(tile.gameObject, tile.parent).transform;
                        part.name = tile.name + "_pruh" + i;
                        Undo.RegisterCreatedObjectUndo(part.gameObject, "Koncept");
                    }
                    else Undo.RecordObject(tile, "Koncept");
                    float x0 = b.min.x + i * w, x1 = x0 + w;
                    if (strips > 1)
                    {
                        Vector3 s = part.localScale;
                        part.localScale = new Vector3(s.x * w / b.size.x, s.y, s.z);
                        part.position = new Vector3(part.position.x + (x0 + x1) * 0.5f - b.center.x, part.position.y, part.position.z);
                    }
                    if (TrimZ(part, Mathf.Max(CutZ(x0), CutZ(x1)) + 0.8f)) trimmed++; else disabled++;
                }
            }
        }

        // tunel za zazdenim (nepristupny)
        var tunnel = map.Find("Tunel");
        if (tunnel != null)
            foreach (var r in tunnel.GetComponentsInChildren<Renderer>(false))
            {
                if ((tiles != null && r.transform.IsChildOf(tiles)) || r.transform.name.EndsWith("_cast")) continue;
                if (r.bounds.max.z <= TunnelSealZ + 0.2f) continue;
                if (TrimZ(r.transform, TunnelSealZ)) trimmed++; else disabled++;
            }

        // vse ostatni cele za carou orezu
        var crossing = new List<string>();
        foreach (var t in map.GetComponentsInChildren<Transform>(false))
        {
            if (t == map || (tunnel != null && t.IsChildOf(tunnel))) continue;
            var r = t.GetComponent<Renderer>();
            var c = t.GetComponent<Collider>();
            if (r == null && c == null) continue;
            string path = Path(map, t);
            bool skip = false;
            foreach (var s in RemoveSkip) if (path.Contains(s)) { skip = true; break; }
            if (skip) continue;
            Bounds b = r != null ? r.bounds : c.bounds;
            bool north = b.min.z > CutZ(b.min.x) + 0.8f && b.min.z > CutZ(b.max.x) + 0.8f;
            if (north)
            {
                Undo.RecordObject(t.gameObject, "Koncept");
                t.gameObject.SetActive(false);
                disabled++;
            }
            else if (b.max.z > Mathf.Max(CutZ(b.min.x), CutZ(b.max.x)) + 3f && b.size.y < 30f && crossing.Count < 15)
                crossing.Add(path);
        }
        Debug.Log($"[Koncept] Plocha za orezem smazana: zkraceno {trimmed}, vypnuto {disabled} objektu."
            + (crossing.Count > 0 ? " Pres caru jeste zasahuje: " + string.Join(", ", crossing) : ""));
    }

    // Ploche povrchy pres caru orezu (travnik, louka, sterk, okraje cest): mesh se orizne podel cary (+0,8 m pod plot),
    // cast za ni zmizi. Cara = max ze dvou primek (sikma a vodorovna z = 51,5), oblast pred ni = sjednoceni dvou polorovin.
    static void ClipGroundCovers(Transform map)
    {
        int clipped = 0;
        var skipped = new List<string>();
        foreach (var filter in map.GetComponentsInChildren<MeshFilter>(false))
        {
            var t = filter.transform;
            var r = t.GetComponent<MeshRenderer>();
            if (r == null || filter.sharedMesh == null) continue;
            string path = Path(map, t);
            if (path.Contains("KonceptUtoku") || path.Contains("Tunel/") || path.Contains("LesniHranice") || path.Contains("Rozhledna")) continue;
            Bounds b = r.bounds;
            if (b.size.y > 1f) continue;                                                       // jen ploche povrchy
            if (b.max.z <= Mathf.Max(CutZ(b.min.x), CutZ(b.max.x)) + 0.8f) continue;            // cele pred carou
            var mesh = filter.sharedMesh;
            if (!mesh.isReadable) { skipped.Add(path); continue; }
            var clippedMesh = ClipMesh(mesh, t);
            Undo.RecordObject(filter, "Koncept");
            filter.sharedMesh = clippedMesh;
            if (t.TryGetComponent<MeshCollider>(out var mc) && mc.sharedMesh == mesh)
            {
                Undo.RecordObject(mc, "Koncept");
                mc.sharedMesh = clippedMesh;
            }
            clipped++;
        }
        Debug.Log($"[Koncept] Travnik a cesty za orezem oriznuty: {clipped} povrchu."
            + (skipped.Count > 0 ? " Nejde precist (nechano): " + string.Join(", ", skipped) : ""));
    }

    struct ClipVertex { public Vector3 p, n; public Vector2 uv; }

    static Mesh ClipMesh(Mesh mesh, Transform t)
    {
        var verts = mesh.vertices;
        var normals = mesh.normals;
        var uvs = mesh.uv;
        bool hasN = normals != null && normals.Length == verts.Length, hasUv = uvs != null && uvs.Length == verts.Length;
        // primky v lokalnich souradnicich se pocitaji ve svete: hodnota > 0 = za carou
        float Slanted(Vector3 w) => w.z - (Mathf.LerpUnclamped(CutWest.y, CutBend.y, (w.x - CutWest.x) / (CutBend.x - CutWest.x)) + 0.8f);
        float Flat(Vector3 w) => w.z - (CutBend.y + 0.8f);

        var outV = new List<Vector3>();
        var outN = new List<Vector3>();
        var outUv = new List<Vector2>();
        var outTris = new List<int>();
        var world = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++) world[i] = t.TransformPoint(verts[i]);

        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            var tris = mesh.GetTriangles(sub);
            for (int i = 0; i < tris.Length; i += 3)
            {
                var poly = new List<ClipVertex>();
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[i + k];
                    poly.Add(new ClipVertex { p = world[v], n = hasN ? normals[v] : Vector3.up, uv = hasUv ? uvs[v] : Vector2.zero });
                }
                // cast pred sikmou carou + cast pred vodorovnou, ktera je za sikmou (disjunktni)
                Emit(Clip(poly, w => Slanted(w)));
                Emit(Clip(Clip(poly, w => Flat(w)), w => -Slanted(w)));
            }
        }

        void Emit(List<ClipVertex> poly)
        {
            if (poly.Count < 3) return;
            int start = outV.Count;
            foreach (var cv in poly)
            {
                outV.Add(t.InverseTransformPoint(cv.p));
                outN.Add(cv.n);
                outUv.Add(cv.uv);
            }
            for (int k = 1; k < poly.Count - 1; k++) outTris.AddRange(new[] { start, start + k, start + k + 1 });
        }

        var result = new Mesh { name = mesh.name + "_orez", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        result.SetVertices(outV);
        result.SetNormals(outN);
        result.SetUVs(0, outUv);
        result.SetTriangles(outTris, 0);
        result.RecalculateBounds();
        if (mesh.tangents != null && mesh.tangents.Length > 0) result.RecalculateTangents();
        return result;
    }

    // Sutherland-Hodgman: ponecha cast mnohouhelniku, kde side(p) <= 0.
    static List<ClipVertex> Clip(List<ClipVertex> poly, System.Func<Vector3, float> side)
    {
        var result = new List<ClipVertex>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            float da = side(a.p), db = side(b.p);
            if (da <= 0f) result.Add(a);
            if ((da <= 0f) != (db <= 0f))
            {
                float s = da / (da - db);
                result.Add(new ClipVertex { p = Vector3.Lerp(a.p, b.p, s), n = Vector3.Lerp(a.n, b.n, s).normalized, uv = Vector2.Lerp(a.uv, b.uv, s) });
            }
        }
        return result;
    }

    // Oprava zeme: V8 delil pasy zeme na pruhy chybne (dalsi pruh se kopiroval z uz zuzeneho) - v zemi zustaly diry
    // a u vylezu z tunelu u B se propadalo mimo mapu. Pasy se vrati do puvodni podoby (podle TunnelSetup) a znovu
    // rozdeli: kazdy pruh se nastavi primo (sirka 2 m, od puvodniho jizniho okraje k zivemu plotu orezu).
    static readonly (string name, float x0, float x1, float z0, float z1)[] GroundTiles =
    {
        ("Tile_0", -51.2f, -34f, -35.1f, 114.9f), ("Tile_1", -34f, -26f, -35.1f, 84f), ("Tile_3", -26f, -9.5f, -35.1f, 114.9f),
        ("Tile_5", -9.5f, -1.5f, 37f, 114.9f), ("Tile_6", -1.5f, 17f, -35.1f, 114.9f), ("Tile_8", 17f, 25f, -16.8f, 114.9f),
        ("Tile_9", 25f, 38.8f, -35.1f, 114.9f),
    };

    static void RepairGroundTiles(Transform map)
    {
        var tiles = map.Find("Tunel/Ground_Tiles");
        if (tiles == null) return;
        var junk = new List<GameObject>();
        foreach (Transform t in tiles) if (t.name.Contains("_pruh")) junk.Add(t.gameObject);
        foreach (var go in junk) Undo.DestroyObjectImmediate(go);

        int pieces = 0;
        foreach (var (name, x0, x1, z0, z1) in GroundTiles)
        {
            var tile = tiles.Find(name);
            if (tile == null) continue;
            float bendLeft = CutZ(x0), bendRight = CutZ(x1);
            int strips = Mathf.Abs(bendLeft - bendRight) > 0.5f ? Mathf.CeilToInt((x1 - x0) / 2f) : 1;
            float w = (x1 - x0) / strips;
            var template = Object.Instantiate(tile.gameObject);   // neporuseny vzor pro dalsi pruhy
            for (int i = 0; i < strips; i++)
            {
                Transform part = tile;
                if (i > 0)
                {
                    part = Object.Instantiate(template, tiles).transform;
                    part.name = name + "_pruh" + i;
                    Undo.RegisterCreatedObjectUndo(part.gameObject, "Koncept");
                }
                else Undo.RecordObject(tile, "Koncept");
                float sx0 = x0 + i * w, sx1 = sx0 + w;
                float limit = Mathf.Min(z1, Mathf.Max(CutZ(sx0), CutZ(sx1)) + 0.8f);
                if (limit <= z0 + 0.1f) { part.gameObject.SetActive(false); continue; }
                part.gameObject.SetActive(true);
                part.rotation = Quaternion.identity;
                part.position = new Vector3((sx0 + sx1) * 0.5f, -0.15f, (z0 + limit) * 0.5f);
                Vector3 ps = part.parent != null ? part.parent.lossyScale : Vector3.one;
                part.localScale = new Vector3(w / ps.x, 0.3f / ps.y, (limit - z0) / ps.z);
                pieces++;
            }
            Object.DestroyImmediate(template);
        }
        Debug.Log($"[Koncept] Zem opravena: {pieces} pasu bez der, konci u ziveho plotu orezu.");
    }

    // Zkrati objekt (kvadr srovnany s osami) ve smeru z tak, aby koncil na maxZ; kdyz cely lezi za maxZ, vypne ho.
    // Vraci true, kdyz zkratil (false = vypnul nebo neslo).
    static bool TrimZ(Transform t, float maxZ)
    {
        var r = t.GetComponent<Renderer>();
        if (r == null) return false;
        Bounds b = r.bounds;
        if (b.min.z >= maxZ - 0.05f)
        {
            Undo.RecordObject(t.gameObject, "Koncept");
            t.gameObject.SetActive(false);
            return false;
        }
        // lokalni osa, ktera miri podel z
        Vector3[] axes = { t.right, t.up, t.forward };
        int k = 0;
        for (int i = 1; i < 3; i++) if (Mathf.Abs(axes[i].z) > Mathf.Abs(axes[k].z)) k = i;
        if (Mathf.Abs(axes[k].z) < 0.99f) return false;   // sikmy objekt - nechat
        Undo.RecordObject(t, "Koncept");
        Vector3 s = t.localScale;
        s[k] *= (maxZ - b.min.z) / Mathf.Max(0.001f, b.size.z);
        t.localScale = s;
        Bounds nb = r.bounds;
        t.position += new Vector3(0f, 0f, b.min.z - nb.min.z);
        return true;
    }

    static string Path(Transform map, Transform t)
    {
        var parts = new List<string>();
        while (t != null && t != map) { parts.Add(t.name); t = t.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    // ---------------- 4. nova cast ----------------

    static void NewArea(Transform root)
    {
        var g = MapBuildKit.Group(root, "NovaCast");
        BuildTerrain(g);

        // obvodovy zivy plot (vychodni strana = puvodni rada stromu se zdi)
        var fence = MapBuildKit.Group(g, "Ohraniceni");
        HedgeLine(fence, Area[4], Area[0]);                       // zapad
        HedgeLine(fence, Area[4], Area[3]);                       // jih (sikmo)
        HedgeLine(fence, Area[3], Area[2]);                       // napojeni na hranici mapy
        // (sever = orezova cara, postavena v CutNorth)

        Covers(MapBuildKit.Group(g, "Kryty"));
        Trees(MapBuildKit.Group(g, "Stromy"));
    }

    static void BuildTerrain(Transform parent)
    {
        const float step = 1f, x0 = -142f, x1 = -49f, z0 = -24f, z1 = 102f;
        int nx = Mathf.RoundToInt((x1 - x0) / step), nz = Mathf.RoundToInt((z1 - z0) / step);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float x = x0 + i * step, z = z0 + j * step;
                vertices.Add(new Vector3(x, H(x, z), z));
                uvs.Add(new Vector2(x, z));
            }
        var triangles = new List<int>();
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                float cx = x0 + (i + 0.5f) * step, cz = z0 + (j + 0.5f) * step;
                var c = new Vector2(cx, cz);
                bool keep = Inside(c, Area) || (EdgeDistance(c, Area) < 2.5f && cx > -142f);
                if (!keep) continue;
                if (cx > RampRect.xMin && cx < RampRect.xMax && cz > RampRect.yMin && cz < RampRect.yMax) continue;   // otvor rampy tunelu
                int v = j * (nx + 1) + i;
                triangles.AddRange(new[] { v, v + nx + 1, v + 1, v + 1, v + nx + 1, v + nx + 2 });
            }
        var mesh = new Mesh { name = "NovaCastTeren", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        MapBuildKit.EnsureFolder("Assets/Models/Koncept");
        if (AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath) != null) AssetDatabase.DeleteAsset(MeshPath);
        AssetDatabase.CreateAsset(mesh, MeshPath);

        var go = new GameObject("Teren");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = lawn;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    static Vector3 On(float x, float z, float lift = 0f) => new Vector3(x, H(x, z) + lift, z);

    static void Rock(Transform parent, float x, float z, Vector3 size, int seed)
    {
        Vector3 at = On(x, z, size.y * 0.12f);   // spodek zapusteny (ani na svahu nevisi)
        if (!Free(at + Vector3.up * size.y * 0.25f, size * 0.3f, seed * 53f, "Kamen")) return;
        var mesh = MapBuildKit.BlobMesh(60 + seed % 6);
        var go = MapBuildKit.MeshObject(parent, "Kamen", mesh, at, Quaternion.Euler(0f, seed * 53f, 0f), size * 0.5f, rock);
        GameObjectUtility.SetStaticEditorFlags(go, 0);
        go.AddComponent<MeshCollider>().sharedMesh = mesh;   // kolize presne podle tvaru (pres vrsek se nestrili skrz)
    }

    static void Covers(Transform g)
    {
        // kulna se strechou (vyvysene misto utocniku - nahoru pres bednu)
        Vector3 k = On(-102f, 52f);
        Box(g, "Kulna", k + Vector3.up * 1.15f, new Vector3(5f, 2.3f, 4f), shed, true, 15f);
        Box(g, "Kulna_Strecha", k + Vector3.up * 2.4f, new Vector3(5.8f, 0.2f, 4.8f), roof, true, 15f);
        Box(g, "Kulna_Bedna", On(-99.3f, 54.2f, 0.55f), new Vector3(1.1f, 1.1f, 1.1f), shed, true, 15f);
        // senik (dve patra baliku) a hromada dreva
        Vector3 s = On(-112f, 30f);
        Box(g, "Senik_1", s + Vector3.up * 0.75f, new Vector3(3.4f, 1.5f, 1.6f), hay, true, -20f);
        Box(g, "Senik_2", s + new Vector3(0.4f, 2.0f, 0f), new Vector3(1.7f, 1.0f, 1.6f), hay, true, -20f);
        Vector3 p = On(-92f, 64f);
        for (int i = 0; i < 3; i++)
            Box(g, "Poleno", p + new Vector3(0f, 0.3f + i * 0.5f, i == 1 ? 0.2f : 0f), new Vector3(4.2f - i * 0.6f, 0.5f, 1.6f - i * 0.3f), logs, true, 30f);
        // kamenna zidka pred rozhlednou (kryt utocniku pri utoku na bod A) s mezerou uprostred
        foreach (var (x0, x1) in new[] { (-93f, -86f), (-82f, -75f) })
            for (float x = x0; x < x1 - 0.1f; x += 1.75f)
                Box(g, "Zidka", On(x + 0.875f, 51f, 0.45f), new Vector3(1.75f, 0.9f, 0.7f), rock);
        // baliky sena u der do mapy (kryt pri prechodu)
        Box(g, "Seno_Dira_S", On(-62f, 44f, 0.6f), new Vector3(1.6f, 1.2f, 3.2f), hay);
        Box(g, "Seno_Dira_J", On(-62f, 5f, 0.6f), new Vector3(1.6f, 1.2f, 2.6f), hay);
        // balvany
        Rock(g, -108f, 60f, new Vector3(3.2f, 2.2f, 2.8f), 1);
        Rock(g, -96f, 26f, new Vector3(2.6f, 1.6f, 2.2f), 2);
        Rock(g, -122f, 50f, new Vector3(2.4f, 1.8f, 2.6f), 3);
        Rock(g, -70f, 62f, new Vector3(2.8f, 1.7f, 2.4f), 4);
        Rock(g, -86f, 12f, new Vector3(2.2f, 1.5f, 2.6f), 5);
        Rock(g, -118f, 12f, new Vector3(3f, 2f, 2.6f), 6);
        Rock(g, -132f, 72f, new Vector3(2.4f, 1.6f, 2.2f), 7);
    }

    // Strom: kmen s kolizi, koruna bez kolize. Spodek koruny nad 2,8 m - hrac pod stromem neni schovany v listi.
    static void Tree(Transform g, float x, float z, float scale, int seed)
    {
        var random = new System.Random(seed);
        Vector3 ground = On(x, z);
        if (!Free(ground + Vector3.up * 1.5f, new Vector3(0.4f, 1f, 0.4f), 0f, "Strom")) return;
        var sizes = new float[3];
        float biggest = 0f;
        for (int i = 0; i < 3; i++)
        {
            sizes[i] = (2.4f + (float)random.NextDouble()) * scale;
            biggest = Mathf.Max(biggest, sizes[i]);
        }
        float height = Mathf.Max(4.2f * scale, 2.8f + 0.6f * biggest);
        var trunk = MapBuildKit.Beam(g, "Kmen", ground - Vector3.up * 0.4f, ground + Vector3.up * (height + 0.4f), 0.45f * scale, bark, true);
        GameObjectUtility.SetStaticEditorFlags(trunk, 0);
        for (int i = 0; i < 3; i++)
        {
            Vector3 offset = new Vector3(((float)random.NextDouble() - 0.5f) * 2.4f * scale, height + (float)random.NextDouble() * 1.2f,
                ((float)random.NextDouble() - 0.5f) * 2.4f * scale);
            var crown = MapBuildKit.MeshObject(g, "Koruna", MapBuildKit.BlobMesh(40 + (seed + i) % 6), ground + offset,
                Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), new Vector3(sizes[i], sizes[i] * 0.8f, sizes[i]), leaves[(seed + i) % leaves.Length]);
            GameObjectUtility.SetStaticEditorFlags(crown, 0);
        }
    }

    static void Trees(Transform g)
    {
        var spots = new (float x, float z, float s)[]
        {
            (-132f, 62f, 1.1f), (-128f, 30f, 1.0f), (-110f, 82f, 1.2f), (-66f, 12f, 1.0f), (-75f, -6f, 1.1f), (-100f, 6f, 1.0f),
            (-60f, 64f, 0.9f), (-122f, 90f, 0.9f), (-134f, 28f, 1.0f), (-90f, -4f, 0.9f), (-70f, 74f, 1.0f), (-106f, 34f, 0.8f),
        };
        int seed = 11;
        foreach (var (x, z, s) in spots)
            if (Inside(new Vector2(x, z), Area) && EdgeDistance(new Vector2(x, z), Area) > 3f)
                Tree(g, x, z, s, seed++);
    }

    // ---------------- 5. diry v zapadni hranici ----------------

    static void HedgeHoles(Transform map, Transform root)
    {
        var g = MapBuildKit.Group(root, "Diry");

        // stromy a kere v dirach pryc
        var forest = map.Find("LesniHranice");
        int cleared = 0;
        if (forest != null)
            foreach (var t in forest.GetComponentsInChildren<Transform>(true))
            {
                if (t.parent == null || t.parent.parent != forest) continue;   // jednotlive stromy (LesniHranice/Skupina/Strom)
                if (t.parent.name == "HraniceProHrace" || t.parent.name == "PrujezdyProAuto") continue;
                Vector3 p = t.position;
                if (p.x < -61f || p.x > -46f) continue;
                foreach (var (a, b) in Holes)
                    if (p.z > a - 2f && p.z < b + 2f)
                    {
                        Undo.RecordObject(t.gameObject, "Koncept");
                        t.gameObject.SetActive(false);
                        cleared++;
                        break;
                    }
            }

        // neviditelna zed na zapadni hranici: rozdelit kolem der
        var walls = forest != null ? forest.Find("HraniceProHrace") : null;
        if (walls != null)
            foreach (var wall in walls.GetComponentsInChildren<BoxCollider>(true))
            {
                Bounds wb = wall.bounds;
                if (Mathf.Abs(wb.center.x - WestWallX) > 3f || wb.size.z < 20f) continue;
                Undo.RecordObject(wall, "Koncept");
                wall.enabled = false;
                var cuts = new List<(float a, float b)>(Holes);
                cuts.Sort((x, y) => x.a.CompareTo(y.a));
                float cursor = wb.min.z;
                var parts = MapBuildKit.Group(g, "HraniceRozdelena");
                foreach (var (a, b) in cuts)
                {
                    if (a > cursor) Invisible(parts, "Hranice", new Vector3(wb.center.x, wb.center.y, (cursor + a) * 0.5f), new Vector3(wb.size.x, wb.size.y, a - cursor));
                    cursor = Mathf.Max(cursor, b);
                }
                if (cursor < wb.max.z) Invisible(parts, "Hranice", new Vector3(wb.center.x, wb.center.y, (cursor + wb.max.z) * 0.5f), new Vector3(wb.size.x, wb.size.y, wb.max.z - cursor));
            }

        // v kazde dire: modra stena (do dobyti A projdou jen obranci), drevene sloupky po stranach
        int n = 0;
        foreach (var (a, b) in Holes)
        {
            var gate = new GameObject("Dira_" + (++n)).transform;
            gate.SetParent(g, false);
            var zg = gate.gameObject.AddComponent<ZoneGate>();
            zg.unlockAfter = 1;
            zg.defendersPass = true;
            var closed = new GameObject("Zavreno").transform;
            closed.SetParent(gate, false);
            float mid = (a + b) * 0.5f, width = b - a;
            Box(closed, "ModraStena", new Vector3(-53.5f, 1.6f, mid), new Vector3(0.12f, 3.2f, width), barrier, false);
            Invisible(closed, "Stena", new Vector3(-53.5f, 12f, mid), new Vector3(0.6f, 24f, width));
            var posts = MapBuildKit.Group(g, "Sloupky_" + n);
            Box(posts, "Sloupek", new Vector3(-53.5f, 1.4f, a - 0.15f), new Vector3(0.3f, 2.8f, 0.3f), post);
            Box(posts, "Sloupek", new Vector3(-53.5f, 1.4f, b + 0.15f), new Vector3(0.3f, 2.8f, 0.3f), post);
        }
        Debug.Log($"[Koncept] Diry v zapadni hranici: {Holes.Length} (vypnuto {cleared} stromu / keru).");
    }

    // Modra stena v dire stoji 2,3 m pred hranici mapy (x -53,5 vs -51,2): kolem jejiho konce se dalo projit
    // mezerou mezi ni a hranici. Na obou koncich kazde diry neviditelne "kridlo" od steny az za hranici
    // (soucast Zavreno - otevre se s branou a obrance pousti stejne jako stena).
    static void HoleWings(Transform root)
    {
        var holes = root.Find("Diry");
        if (holes == null) return;
        int added = 0;
        foreach (Transform gate in holes)
        {
            if (!gate.name.StartsWith("Dira_")) continue;
            var closed = gate.Find("Zavreno");
            var wall = closed != null ? closed.Find("Stena") : null;
            if (wall == null || !wall.TryGetComponent<BoxCollider>(out var box)) continue;
            Bounds b = box.bounds;
            float x0 = b.min.x - 0.3f, x1 = WestWallX + 0.6f;
            foreach (float z in new[] { b.min.z, b.max.z })
            {
                Invisible(closed, "Kridlo", new Vector3((x0 + x1) * 0.5f, b.center.y, z), new Vector3(x1 - x0, b.size.y, 0.4f));
                added++;
            }
        }
        Debug.Log($"[Koncept] Diry v hranici: {added} kridel mezi modrou stenou a hranici mapy (uz se nedaji obejit).");
    }

    // Kolem der byly vykacene stromy siroko (otvor v rade stromu je vetsi nez brana) - mezi branou a stromy
    // zela mezera. Na obou stranach kazde diry zivy plot pres celou sirku rady stromu (x -58,8 .. -51)
    // od posledniho stromu az k hrane brany: pruchod je presne tak siroky jako brana.
    static void HoleHedges(Transform map, Transform root)
    {
        var forest = map.Find("LesniHranice");
        var holes = root.Find("Diry");
        if (forest == null || holes == null) return;
        var trees = new List<float>();
        foreach (var t in forest.GetComponentsInChildren<Transform>(false))
        {
            if (t.parent == null || t.parent.parent != forest || t.parent.name == "HraniceProHrace") continue;
            if (t.position.x < -61f || t.position.x > -46f) continue;
            trees.Add(t.position.z);
        }
        var g = MapBuildKit.Group(holes, "ZivyPlotDiry");
        const float xa = -58.8f, xb = -51f;
        int pieces = 0;
        foreach (var (a, b) in Holes)
        {
            float low = a - 3f, high = b + 3f;   // kdyby stromy chybely, aspon 3 m plotu
            // nejblizsi strom pod a nad dirou
            float nearLow = float.MinValue, nearHigh = float.MaxValue;
            foreach (float z in trees)
            {
                if (z < a && z > nearLow) nearLow = z;
                if (z > b && z < nearHigh) nearHigh = z;
            }
            if (nearLow > a - 12f) low = nearLow;
            if (nearHigh < b + 12f) high = nearHigh;
            foreach (var (z0, z1) in new[] { (low, a), (b, high) })
            {
                if (z1 - z0 < 0.2f) continue;
                Box(g, "ZivyPlot", new Vector3((xa + xb) * 0.5f, 1.45f, (z0 + z1) * 0.5f), new Vector3(xb - xa, 3.5f, z1 - z0), hedge);
                pieces++;
            }
        }
        Debug.Log($"[Koncept] Diry v hranici: {pieces} kusu ziveho plotu od stromu az k branam.");
    }

    // Neviditelna hranice mapy (x -51,2) sahala od -2 m - nova chodba tunelu (podlaha -4 m) pod ni vede a hrac
    // (2 m) se o ni hlavou zarazil. Spodek hranice az tesne pod zem (-0,3 m).
    static void BorderAboveTunnel(Transform root)
    {
        var parts = root.Find("Diry/HraniceRozdelena");
        if (parts == null) return;
        int fixedCount = 0;
        foreach (var box in parts.GetComponentsInChildren<BoxCollider>(true))
        {
            Bounds b = box.bounds;
            if (b.min.y > -0.35f) continue;
            float top = b.max.y, bottom = -0.3f;
            Undo.RecordObject(box, "Koncept");
            Undo.RecordObject(box.transform, "Koncept");
            var t = box.transform;
            t.position = new Vector3(t.position.x, (top + bottom) * 0.5f, t.position.z);
            box.center = Vector3.zero;
            box.size = new Vector3(box.size.x, (top - bottom) / Mathf.Max(0.001f, Mathf.Abs(t.lossyScale.y)), box.size.z);
            fixedCount++;
        }
        Debug.Log($"[Koncept] Hranice mapy nad tunelem: spodek zvednut na -0,3 m u {fixedCount} kusu (chodba z A do B je volna).");
    }

    // Garaz snezne rolby: ulicka mezi zapadni stenou kulny (x -16,1) a radou tuji (x -19,1), kde rolba stoji
    // (radlici k jihu). Zapadni a zadni stena, strecha a na jihu vrata - zavrena, prorazit je jde jen rolbou (RolbaGate).
    static void GroomerGarage(Transform root)
    {
        var old = root.Find("GarazRolby");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var g = MapBuildKit.Group(root, "GarazRolby");
        const float xw = -18.75f, xe = -16.2f, zs = 22.8f, zn = 28.6f, h = 2.6f;
        Box(g, "Stena_Z", new Vector3(xw, h * 0.5f, (zs + zn) * 0.5f), new Vector3(0.2f, h, zn - zs + 0.2f), shed);
        Box(g, "Stena_S", new Vector3((xw + xe) * 0.5f + 0.05f, h * 0.5f, zn), new Vector3(xe - xw + 0.3f, h, 0.2f), shed);
        Box(g, "Stena_V", new Vector3(xe + 0.05f, h * 0.5f, zs + 0.35f), new Vector3(0.2f, h, 0.7f), shed);   // mezera k rohu kulny
        Box(g, "Strecha", new Vector3((xw + xe) * 0.5f, h + 0.1f, (zs + zn) * 0.5f - 0.2f), new Vector3(xe - xw + 0.6f, 0.15f, zn - zs + 0.9f), roof);
        // ram vrat
        foreach (float x in new[] { xw, xe })
            Box(g, "Sloupek", new Vector3(x, h * 0.5f, zs), new Vector3(0.22f, h, 0.22f), post);
        Box(g, "Preklad", new Vector3((xw + xe) * 0.5f, h - 0.1f, zs), new Vector3(xe - xw + 0.2f, 0.2f, 0.22f), post);

        // vrata: dve kridla z prken se vzperou (kolize = cele kridlo)
        var gate = new GameObject("VrataRolby").transform;
        gate.SetParent(g, false);
        gate.position = new Vector3((xw + xe) * 0.5f, 0f, zs);
        float inner0 = xw + 0.11f, inner1 = xe - 0.11f, leafW = (inner1 - inner0) * 0.5f, leafH = h - 0.22f;
        for (int i = 0; i < 2; i++)
        {
            float cx = inner0 + leafW * (i + 0.5f);
            var leaf = Box(gate, "Kridlo_" + (i + 1), new Vector3(cx, leafH * 0.5f + 0.02f, zs), new Vector3(leafW - 0.02f, leafH, 0.1f), plank);
            float angle = Mathf.Atan2(leafW - 0.2f, leafH - 0.3f) * Mathf.Rad2Deg * (i == 0 ? 1f : -1f);
            var brace = MapBuildKit.Box(leaf.transform, "Vzpera", new Vector3(cx, leafH * 0.5f + 0.02f, zs - 0.07f),
                new Vector3(0.12f, Mathf.Sqrt((leafW - 0.2f) * (leafW - 0.2f) + (leafH - 0.3f) * (leafH - 0.3f)), 0.04f),
                Quaternion.Euler(0f, 0f, angle), post, false);
            GameObjectUtility.SetStaticEditorFlags(brace, 0);
            foreach (float y in new[] { 0.3f, leafH - 0.25f })
            {
                var bar = MapBuildKit.Box(leaf.transform, "Prkno", new Vector3(cx, y, zs - 0.07f), new Vector3(leafW - 0.1f, 0.14f, 0.04f), Quaternion.identity, post, false);
                GameObjectUtility.SetStaticEditorFlags(bar, 0);
            }
        }
        gate.gameObject.AddComponent<RolbaGate>();
        Debug.Log("[Koncept] Garaz rolby: zapadni a zadni stena, strecha a vrata (prorazi je jen rolba).");
    }

    // Do garaze rolby se nedalo vejit (vrata jdou jen prorazit). Zadni (severni) stena dostane otevrene dvere
    // 1,2 x 2,15 m - hrac dojde k rolbe zezadu, rolba ven vyjede vraty.
    static void GroomerGarageDoor(Transform root)
    {
        var g = root.Find("GarazRolby");
        if (g == null) return;
        var wall = g.Find("Stena_S");
        if (wall != null) Undo.DestroyObjectImmediate(wall.gameObject);
        const float x0 = -18.9f, x1 = -16.05f, z = 28.6f, h = 2.6f, d0 = -18.15f, d1 = -16.85f, dh = 2.15f;
        Box(g, "Stena_S1", new Vector3((x0 + d0) * 0.5f, h * 0.5f, z), new Vector3(d0 - x0, h, 0.2f), shed);
        Box(g, "Stena_S2", new Vector3((d1 + x1) * 0.5f, h * 0.5f, z), new Vector3(x1 - d1, h, 0.2f), shed);
        Box(g, "Stena_S_Preklad", new Vector3((d0 + d1) * 0.5f, (dh + h) * 0.5f, z), new Vector3(d1 - d0, h - dh, 0.2f), shed);
        foreach (float x in new[] { d0, d1 })
            Box(g, "Zarubne", new Vector3(x, dh * 0.5f, z), new Vector3(0.1f, dh, 0.26f), post);
        Debug.Log("[Koncept] Garaz rolby: dvere v zadni stene (k rolbe se da dojit).");
    }

    // ---------------- 6. tunel ----------------

    static void TunnelBranch(Transform map, Transform root)
    {
        var g = MapBuildKit.Group(root, "TunelVetev");

        // sever puvodniho tunelu (od stare rozhledny) zazdit
        Box(g, "Zazdeni", new Vector3(0f, (TunFloor + TunCeil) * 0.5f, 47f), new Vector3(3.6f, TunCeil - TunFloor + 0.4f, 0.4f), concrete);

        // otvor v zapadni stene hlavni chodby (x -1.6) v miste napojeni
        var tunnel = map.Find("Tunel/Walls");
        if (tunnel != null)
            foreach (Transform w in tunnel)
            {
                var r = w.GetComponent<Renderer>();
                if (r == null || !w.gameObject.activeSelf) continue;
                Bounds wb = r.bounds;
                if (Mathf.Abs(wb.center.x + 1.6f) > 0.4f || wb.min.z > TunZ0 || wb.max.z < TunZ1) continue;
                Undo.RecordObject(w.gameObject, "Koncept");
                w.gameObject.SetActive(false);
                foreach (var (a, b) in new[] { (wb.min.z, TunZ0), (TunZ1, wb.max.z) })
                {
                    if (b - a < 0.05f) continue;
                    var part = Object.Instantiate(w.gameObject, w.parent);
                    part.name = w.name + "_cast";
                    part.SetActive(true);
                    part.transform.position = new Vector3(w.position.x, w.position.y, (a + b) * 0.5f);
                    Vector3 s = part.transform.localScale;
                    float worldPerLocal = wb.size.z / Mathf.Max(0.001f, s.z);
                    part.transform.localScale = new Vector3(s.x, s.y, (b - a) / worldPerLocal);
                    Undo.RegisterCreatedObjectUndo(part, "Koncept");
                }
                break;
            }

        // nova chodba z 30..33 od x -72 k hlavni chodbe
        float x1 = -1.75f, len = x1 - TunX0, cx = (TunX0 + x1) * 0.5f, cz = (TunZ0 + TunZ1) * 0.5f;
        float height = TunCeil - TunFloor;
        Box(g, "Podlaha", new Vector3(cx, TunFloor - 0.15f, cz), new Vector3(len, 0.3f, TunZ1 - TunZ0 + 0.6f), concreteDark);
        Box(g, "Strop", new Vector3(cx, TunCeil + 0.15f, cz), new Vector3(len, 0.3f, TunZ1 - TunZ0 + 0.6f), concreteDark);
        Box(g, "Stena_S", new Vector3(cx, TunFloor + height * 0.5f, TunZ1 + 0.15f), new Vector3(len, height, 0.3f), concrete);
        float southStart = RampRect.xMax;   // jizni stena az za rampou (rampa vusti do chodby z jihu)
        Box(g, "Stena_J", new Vector3((southStart + x1) * 0.5f, TunFloor + height * 0.5f, TunZ0 - 0.15f), new Vector3(x1 - southStart, height, 0.3f), concrete);
        Box(g, "Stena_Z", new Vector3(TunX0 - 0.15f, TunFloor + height * 0.5f, cz), new Vector3(0.3f, height, TunZ1 - TunZ0 + 0.6f), concrete);

        // rampa z povrchu (x -72..-68, z 18 -> 30)
        float top = H(-70f, 18f);
        Vector3 low = new Vector3(-70f, TunFloor, RampRect.yMax), high = new Vector3(-70f, top, RampRect.yMin);
        Vector3 along = low - high;
        var rot = Quaternion.LookRotation(along.normalized, Vector3.up);
        var ramp = MapBuildKit.Box(g, "Rampa", (low + high) * 0.5f - rot * Vector3.up * 0.15f, new Vector3(RampRect.width, 0.3f, along.magnitude + 0.3f), rot, concreteDark, true);
        GameObjectUtility.SetStaticEditorFlags(ramp, 0);
        foreach (float x in new[] { RampRect.xMin - 0.15f, RampRect.xMax + 0.15f })
            Box(g, "Rampa_Stena", new Vector3(x, (TunFloor + top + 0.4f) * 0.5f, (RampRect.yMin + RampRect.yMax) * 0.5f),
                new Vector3(0.3f, top + 0.4f - TunFloor, RampRect.height), concrete);
        Box(g, "Rampa_Celo", new Vector3(-70f, (top + 0.4f + TunCeil) * 0.5f, RampRect.yMax + 0.15f), new Vector3(RampRect.width + 0.6f, top + 0.4f - TunCeil, 0.3f), concrete);

        // svetla
        for (float x = TunX0 + 4f; x < x1; x += 12f)
        {
            Box(g, "Lampa", new Vector3(x, TunCeil - 0.08f, cz), new Vector3(0.6f, 0.1f, 0.3f), lamp, false);
            var light = new GameObject("Svetlo").AddComponent<Light>();
            light.transform.SetParent(g, false);
            light.transform.position = new Vector3(x, TunCeil - 0.6f, cz);
            light.type = LightType.Point;
            light.range = 9f;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.86f, 0.62f);
            light.shadows = LightShadows.None;
        }

        // mriz za rampou: otevre se po bodu A
        var gate = new GameObject("Mriz_Rozhledna_Nova").transform;
        gate.SetParent(g, false);
        gate.gameObject.AddComponent<ZoneGate>().unlockAfter = 1;
        var closed = new GameObject("Zavreno").transform;
        closed.SetParent(gate, false);
        float gx = RampRect.xMax + 2f;
        for (float z = TunZ0 + 0.2f; z < TunZ1; z += 0.3f)
            Box(closed, "Tyc", new Vector3(gx, (TunFloor + TunCeil) * 0.5f, z), new Vector3(0.06f, height, 0.06f), metal, false);
        Invisible(closed, "Kolize", new Vector3(gx, (TunFloor + TunCeil) * 0.5f, cz), new Vector3(0.3f, height + 0.4f, TunZ1 - TunZ0 + 0.4f));

        // karburator do nove vetve
        var carburetor = FindDeep(map, "Balicek_Karburator");
        if (carburetor != null)
        {
            Undo.RecordObject(carburetor, "Koncept");
            float lift = Mathf.Clamp(carburetor.position.y - (carburetor.position.y > -2f ? 0f : TunFloor), 0f, 1.2f);
            carburetor.position = new Vector3(-35f, TunFloor + lift, cz);
        }
    }

    static Transform FindDeep(Transform parent, string name)
    {
        foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // ---------------- 7. tobogan ----------------

    static void Slide(Transform map)
    {
        foreach (var path in new[] { "Hratelnost/Doplnky/Potok", "Hratelnost/Doplnky/ProudVody", "Hratelnost/Doplnky/Jezirko", "Hratelnost/Doplnky/ProudPotoka" })
        {
            var t = map.Find(path);
            if (t == null) continue;
            Undo.RecordObject(t.gameObject, "Koncept");
            t.gameObject.SetActive(false);
        }
        // zelena skluzavka: otocit, at skluz miri do bazenu (na jih)
        var slide = map.Find("Hratelnost/Doplnky/Skluzavka");
        if (slide != null)
        {
            Undo.RecordObject(slide, "Koncept");
            slide.rotation = Quaternion.Euler(0f, slide.eulerAngles.y + 180f, 0f);
        }
    }

    // ---------------- 8. body a spawny ----------------

    static void SetMarker(Transform map, string name, Vector3 position, Vector3? scale = null)
    {
        var t = FindDeep(map, name);
        if (t == null)
        {
            t = new GameObject(name).transform;
            t.SetParent(map, true);
            Undo.RegisterCreatedObjectUndo(t.gameObject, "Koncept");
        }
        else Undo.RecordObject(t, "Koncept");
        t.position = position;
        if (scale.HasValue)
        {
            Vector3 ps = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(scale.Value.x / ps.x, scale.Value.y / ps.y, scale.Value.z / ps.z);
        }
    }

    static void PointsAndSpawns(Transform map)
    {
        SetMarker(map, "CapturePoint_1", On(TowerTarget.x, TowerTarget.z, 0.5f), new Vector3(14f, 1f, 14f));
        SetMarker(map, "SpawnPoint_Utok_A", On(AttackStart.x, AttackStart.z, 0.1f));
        SetMarker(map, "SpawnPoint_Utok_B", On(AttackAfterA.x, AttackAfterA.z, 0.1f));
        // obranci pro A se oziveji v male chate (prizemi) a do nove casti projdou dirami
        SetMarker(map, "SpawnPoint_Obrana_A", new Vector3(-37.3f, 2.6f, -1.8f));
        // team deathmatch: zakladna severniho tymu byla za orezem - na sever male chaty
        SetMarker(map, "SpawnPoint_Team1", new Vector3(-20f, 0.1f, 45f));
    }

    // ---------------- 9. kulna pro rolbu ----------------

    static void GroomerShed(Transform map, Transform root)
    {
        var g = MapBuildKit.Group(root, "KulnaRolby");
        const float x0 = -23f, x1 = -13f, z0 = 22f, z1 = 28f, h = 3.2f;
        Box(g, "Stena_S", new Vector3((x0 + x1) * 0.5f, h * 0.5f, z1 - 0.1f), new Vector3(x1 - x0, h, 0.2f), shed);
        Box(g, "Stena_J", new Vector3((x0 + x1) * 0.5f, h * 0.5f, z0 + 0.1f), new Vector3(x1 - x0, h, 0.2f), shed);
        Box(g, "Stena_Z", new Vector3(x0 + 0.1f, h * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.2f, h, z1 - z0), shed);
        Box(g, "Strecha", new Vector3((x0 + x1) * 0.5f + 0.3f, h + 0.12f, (z0 + z1) * 0.5f), new Vector3(x1 - x0 + 1.2f, 0.24f, z1 - z0 + 1f), roof);
        Box(g, "Tram", new Vector3(x1 - 0.1f, h - 0.25f, (z0 + z1) * 0.5f), new Vector3(0.25f, 0.5f, z1 - z0), post);

        var groomer = FindDeep(map, "SneznaRolba");
        if (groomer != null)
        {
            Undo.RecordObject(groomer, "Koncept");
            groomer.position = new Vector3(-17.5f, groomer.position.y, 25f);
            groomer.rotation = Quaternion.Euler(0f, 90f, 0f);
        }
    }

    // ---------------- 10. sekce A (jako prvni bod v Overwatchi) ----------------
    // Kolo 2. Spawn utocniku = stodola s dvorem ohrazenym palisadou (3,4 m; branky: hlavni na jih, bocni k uvozu,
    // vychodni na hreben) - z plosiny rozhledny neni videt do vrat stodoly. K bodu tri cesty: stred kolem otevreneho
    // seniku, vlevo po hrebeni kolem posedu, vpravo zahloubenym uvozem. Bod = ohrazeny dvur rozhledny.
    // Pravidla: co je videt, ma kolizi podle tvaru (kameny a kere mesh); vse stoji na vyrovnane zemi (Pads);
    // na nizke veci (bedny, polena, studna) se vyskoci, na vysoke (seno 1,5 m a vic, palisada, kulny) ne.

    static void SectionA(Transform map, Transform root)
    {
        var area = root.Find("NovaCast");
        if (area == null) return;
        // teren podle noveho tvaru (stare kryty a stromy stoji na puvodnim terenu - pryc)
        foreach (var name in new[] { "Teren", "Kryty", "Stromy", "SekceA" })
        {
            var old = area.Find(name);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }
        BuildTerrain(area);

        var g = MapBuildKit.Group(area, "SekceA");
        Barn(MapBuildKit.Group(g, "Stodola"));
        Courtyard(MapBuildKit.Group(g, "DvurStatku"));
        HayShed(MapBuildKit.Group(g, "Senik"));
        Posed(MapBuildKit.Group(g, "Posed"));
        TunnelShed(MapBuildKit.Group(g, "KulnaTunelu"));
        YardFence(MapBuildKit.Group(g, "Dvur"));

        var c = MapBuildKit.Group(g, "Kryty");
        HayStack(c, -98f, 74.5f, 115f);      // svah hrebene - kryt pred rozhlednou (podel cesty)
        HayStack(c, -63f, 57f, 75f);         // u der (obranci)
        Rock(c, -114f, 54f, new Vector3(3.6f, 3.6f, 3.2f), 3);
        Rock(c, -104f, 48f, new Vector3(4.2f, 3.8f, 3.6f), 2);
        Rock(c, -89f, 60f, new Vector3(3.8f, 3.6f, 3.2f), 4);
        Rock(c, -66f, 26f, new Vector3(4f, 3.8f, 3.4f), 5);
        Rock(c, -113f, 33f, new Vector3(3.4f, 3.2f, 3f), 6);
        LogPile(c, -100f, 20f, 35f);
        for (int i = 0; i < 4; i++)
        {
            LanePoint(0.14f + i * 0.22f, out Vector2 p, out Vector2 n);
            Vector2 b = p + n * 5.6f;
            Bush(c, b.x, b.y, 1.5f, 17 + i * 5);                                  // severni val uvozu
        }
        foreach (var (x, z) in new[] { (-58f, 50f), (-58.5f, 37f), (-58f, 0f) })
            Bush(c, x, z, 1.6f, (int)(-x + z) + 3);                                // pred dirami do mapy

        var trees = MapBuildKit.Group(g, "Stromy");
        var spots = new (float x, float z, float s)[]
        {
            (-136f, 60f, 1.0f), (-135.5f, 47f, 1.0f), (-131f, 36f, 0.9f), (-124f, 24f, 1.0f), (-112f, 26f, 0.9f),
            (-66f, 6f, 1.0f), (-88f, -1f, 0.9f), (-62f, 66f, 0.9f), (-76f, 12f, 0.8f), (-74f, 73f, 1.0f),
        };
        int seed = 31;
        foreach (var (x, z, sc) in spots)
            if (Inside(new Vector2(x, z), Area) && EdgeDistance(new Vector2(x, z), Area) > 3f && RiverDistance(new Vector2(x, z), true) > 4.5f)
                Tree(trees, x, z, sc, seed++);

        Paths(MapBuildKit.Group(g, "Cesty"));
        Orchard(MapBuildKit.Group(g, "Sad"));

        // spawny a bod na novou vysku terenu
        SetMarker(map, "SpawnPoint_Utok_A", On(-129f, 87f, 0.1f));
        SetMarker(map, "SpawnPoint_Utok_B", On(-75f, 49f, 0.1f));
        SetMarker(map, "CapturePoint_1", On(TowerTarget.x, TowerTarget.z, 0.5f), new Vector3(14f, 1f, 14f));
        Debug.Log("[Koncept] Sekce A (kolo 2): stodola s ohrazenym dvorem, senik, hreben s posedem, uvoz, dvur rozhledny.");
        AuditPending = true;
    }

    // Misto je volne (krome terenu)? Jinak se prvek vynecha a zapise do logu - nic se neprekryva.
    static bool Free(Vector3 center, Vector3 half, float yaw, string what)
    {
        Physics.SyncTransforms();
        foreach (var c in Physics.OverlapBox(center, half, Quaternion.Euler(0f, yaw, 0f), ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.gameObject.name == "Teren") continue;
            string parent = c.transform.parent != null ? c.transform.parent.name + "/" : "";
            Debug.Log($"[Koncept] {what} u ({center.x:0.0}, {center.z:0.0}) vynechan - prekazi {parent}{c.name}.");
            return false;
        }
        return true;
    }

    // Bod na ose uvozu (t = 0..1 po delce) a normala k severovychodu (ke stredni ceste).
    static void LanePoint(float t, out Vector2 p, out Vector2 n)
    {
        float total = 0f;
        for (int i = 0; i < Lane.Length - 1; i++) total += Vector2.Distance(Lane[i], Lane[i + 1]);
        float want = t * total;
        for (int i = 0; i < Lane.Length - 1; i++)
        {
            float len = Vector2.Distance(Lane[i], Lane[i + 1]);
            if (want <= len || i == Lane.Length - 2)
            {
                Vector2 dir = (Lane[i + 1] - Lane[i]) / len;
                p = Lane[i] + dir * Mathf.Min(want, len);
                n = new Vector2(-dir.y, dir.x);
                return;
            }
            want -= len;
        }
        p = Lane[0];
        n = Vector2.up;
    }

    // ---------------- stodola a dvur statku (spawn utocniku) ----------------

    // Stodola: spawn utocniku. Vrata na jih a na vychod - obe do ohrazeneho dvora.
    static void Barn(Transform g)
    {
        const float x0 = -136f, x1 = -122f, z0 = 82f, z1 = 91f, h = 5f, t = 0.3f;
        float y = H(-129f, 87f) - 0.1f;
        Box(g, "Podlaha", new Vector3((x0 + x1) * 0.5f, y + 0.05f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.2f, z1 - z0), plank);
        Box(g, "Stena_S", new Vector3((x0 + x1) * 0.5f, y + h * 0.5f, z1 - t * 0.5f), new Vector3(x1 - x0, h, t), shed);
        Box(g, "Stena_Z", new Vector3(x0 + t * 0.5f, y + h * 0.5f, (z0 + z1) * 0.5f), new Vector3(t, h, z1 - z0), shed);
        // jizni stena s vraty x -133..-129
        Box(g, "Stena_J1", new Vector3((x0 - 133f) * 0.5f, y + h * 0.5f, z0 + t * 0.5f), new Vector3(-133f - x0, h, t), shed);
        Box(g, "Stena_J2", new Vector3((-129f + x1) * 0.5f, y + h * 0.5f, z0 + t * 0.5f), new Vector3(x1 + 129f, h, t), shed);
        Box(g, "Preklad_J", new Vector3(-131f, y + 3.6f + (h - 3.6f) * 0.5f, z0 + t * 0.5f), new Vector3(4f, h - 3.6f, t), shed);
        // vychodni stena s vraty z 84..88
        Box(g, "Stena_V1", new Vector3(x1 - t * 0.5f, y + h * 0.5f, (z0 + 84f) * 0.5f), new Vector3(t, h, 84f - z0), shed);
        Box(g, "Stena_V2", new Vector3(x1 - t * 0.5f, y + h * 0.5f, (88f + z1) * 0.5f), new Vector3(t, h, z1 - 88f), shed);
        Box(g, "Preklad_V", new Vector3(x1 - t * 0.5f, y + 3.6f + (h - 3.6f) * 0.5f, 86f), new Vector3(t, h - 3.6f, 4f), shed);
        // sedlova strecha (hreben podel x)
        float half = (z1 - z0) * 0.5f + 0.6f, angle = 28f;
        float rise = half * Mathf.Tan(angle * Mathf.Deg2Rad), slant = half / Mathf.Cos(angle * Mathf.Deg2Rad);
        for (int side = -1; side <= 1; side += 2)
        {
            var slab = MapBuildKit.Box(g, "Strecha", new Vector3((x0 + x1) * 0.5f, y + h + rise * 0.5f, (z0 + z1) * 0.5f + side * half * 0.5f),
                new Vector3(x1 - x0 + 1.2f, 0.25f, slant + 0.2f), Quaternion.Euler(side * angle, 0f, 0f), roof, true);
            GameObjectUtility.SetStaticEditorFlags(slab, 0);
        }
        // stity (oboustranny trojuhelnik, s kolizi - nestrili se skrz)
        for (int end = 0; end < 2; end++)
        {
            float x = end == 0 ? x0 : x1;
            var points = new[] { new Vector3(x, y + h, z0), new Vector3(x, y + h + rise, (z0 + z1) * 0.5f), new Vector3(x, y + h, z1) };
            var gable = MapBuildKit.MeshObject(g, "Stit", MapBuildKit.FlatMesh("StitStodoly", points,
                new[] { new Vector2(0f, 0f), new Vector2(half, rise), new Vector2(half * 2f, 0f) }), Vector3.zero, Quaternion.identity, Vector3.one, shed);
            GameObjectUtility.SetStaticEditorFlags(gable, 0);
            gable.AddComponent<MeshCollider>().sharedMesh = gable.GetComponent<MeshFilter>().sharedMesh;
        }
        // uvnitr: seno, bedny, tram a tri svetla (stodola nesmi byt tmava)
        Box(g, "Seno_Uvnitr", new Vector3(-134f, y + 0.8f, 89f), new Vector3(2.6f, 1.6f, 2.6f), hay);
        Box(g, "Seno_Uvnitr", new Vector3(-134.2f, y + 2.1f, 89.2f), new Vector3(1.8f, 1.0f, 1.8f), hay);
        Box(g, "Bedna", new Vector3(-124f, y + 0.5f, 90f), new Vector3(1f, 1f, 1f), shed);
        Box(g, "Bedna", new Vector3(-125.2f, y + 0.45f, 90.1f), new Vector3(0.9f, 0.9f, 0.9f), shed);
        Box(g, "Tram", new Vector3((x0 + x1) * 0.5f, y + h - 0.2f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.3f, 0.3f), post);
        foreach (float lx in new[] { -133f, -129f, -125f })
        {
            var light = new GameObject("Svetlo").AddComponent<Light>();
            light.transform.SetParent(g, false);
            light.transform.position = new Vector3(lx, y + 3.9f, 86.5f);
            light.type = LightType.Point;
            light.range = 10f;
            light.intensity = 2.4f;
            light.color = new Color(1f, 0.86f, 0.62f);
            light.shadows = LightShadows.None;
        }
    }

    // Dvur pred stodolou: palisada 3,4 m (neprelezi se, neprostreli). Branky: bocni (u zapadniho plotu, k uvozu),
    // hlavni (5 m, na jih ke stredni ceste), vychodni (na hreben). Z rozhledny je videt nanejvys do branek.
    static void Courtyard(Transform g)
    {
        float y = H(-125f, 78f);
        Palisade(g, new Vector2(CourtWestX, CourtSouthZ), new Vector2(CourtEastX, CourtSouthZ), y, new[] { (0.5f, 2.7f), (11.3f, 16.3f) }, 1);
        Palisade(g, new Vector2(CourtEastX, CourtSouthZ), new Vector2(CourtEastX, CutZ(CourtEastX)), y, new[] { (14f, 17.5f) }, -1);

        Wagon(g, On(-119f, 76f), 0f);
        Box(g, "Koryto", On(-137.5f, 80f, 0.4f), new Vector3(0.9f, 0.8f, 2.6f), rock);
        Box(g, "Bedna", On(-123.5f, 80.6f, 0.5f), new Vector3(1f, 1f, 1f), shed, true, 12f);
        LogPile(g, -118f, 90.2f, 90f);
    }

    // Palisada z prken od a do b (vyska 3,4 m) s brankami 'gaps' (vzdalenost od a); nad brankou 'header' preklad.
    static void Palisade(Transform g, Vector2 a, Vector2 b, float y, (float a, float b)[] gaps, int header)
    {
        const float h = 3.4f;
        Vector2 d = b - a;
        float len = d.magnitude;
        Vector2 dir = d / len;
        float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
        Vector3 At(float s, float up) { Vector2 m = a + dir * s; return new Vector3(m.x, y + up, m.y); }

        var spans = new List<(float s0, float s1)>();
        float cursor = 0f;
        foreach (var (ga, gb) in gaps) { spans.Add((cursor, ga)); cursor = gb; }
        spans.Add((cursor, len));
        foreach (var (s0, s1) in spans)
        {
            if (s1 - s0 < 0.05f) continue;
            int pieces = Mathf.Max(1, Mathf.CeilToInt((s1 - s0) / 2f));
            float piece = (s1 - s0) / pieces;
            for (int i = 0; i < pieces; i++)
            {
                float m0 = s0 + i * piece;
                Box(g, "Palisada", At(m0 + piece * 0.5f, h * 0.5f - 0.15f), new Vector3(0.24f, h + 0.3f, piece + 0.01f), plank, true, yaw);
                if (i > 0) Box(g, "Sloup", At(m0, 1.85f), new Vector3(0.3f, 3.7f, 0.3f), post, true, yaw);
            }
        }
        for (int i = 0; i < gaps.Length; i++)
        {
            var (ga, gb) = gaps[i];
            foreach (float s in new[] { ga, gb })
                Box(g, "SloupBrany", At(s, 2f), new Vector3(0.45f, 4f, 0.45f), post, true, yaw);
            if (i == header)
                Box(g, "Preklad", At((ga + gb) * 0.5f, 4.15f), new Vector3(0.35f, 0.35f, gb - ga + 0.6f), post, true, yaw);
        }
    }

    // Vuz se senem. Kolize jen korba se senem (0,75 - 2,4 m): pod vozem se strili mezi koly.
    static void Wagon(Transform g, Vector3 at, float yaw)
    {
        var w = new GameObject("VuzSeSenem").transform;
        w.SetParent(g, false);
        w.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        Part(w, "Korba", new Vector3(0f, 0.85f, 0f), new Vector3(2.1f, 0.2f, 4.4f), plank);
        Part(w, "Bocnice", new Vector3(-1f, 1.15f, 0f), new Vector3(0.1f, 0.5f, 4.4f), plank);
        Part(w, "Bocnice", new Vector3(1f, 1.15f, 0f), new Vector3(0.1f, 0.5f, 4.4f), plank);
        Part(w, "Seno", new Vector3(0f, 1.75f, 0.1f), new Vector3(1.9f, 1.3f, 3.9f), hay);
        Part(w, "Oj", new Vector3(0f, 0.6f, 3.2f), new Vector3(0.12f, 0.12f, 2.2f), post);
        foreach (float x in new[] { -1.15f, 1.15f })
            foreach (float z in new[] { -1.4f, 1.4f })
            {
                var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(wheel.GetComponent<Collider>());
                wheel.name = "Kolo";
                wheel.transform.SetParent(w, false);
                wheel.transform.localPosition = new Vector3(x, 0.5f, z);
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                wheel.transform.localScale = new Vector3(1f, 0.08f, 1f);
                wheel.GetComponent<Renderer>().sharedMaterial = post;
            }
        var box = w.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 1.6f, 0.05f);
        box.size = new Vector3(2.2f, 1.7f, 4.45f);
    }

    static void Part(Transform parent, string name, Vector3 local, Vector3 size, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.AddComponent<MeshFilter>().sharedMesh = MapBuildKit.BoxMesh(size, true);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    // ---------------- louka: senik, posed, kryty ----------------

    // Otevreny senik u stredni cesty: strecha na sloupech (okap 3,8 m), vzadu rada sena (kryt), uvnitr stin pred rozhlednou.
    static void HayShed(Transform g)
    {
        Vector3 o = On(ShedAt.x, ShedAt.y);
        var rot = Quaternion.Euler(0f, ShedYaw, 0f);
        Vector3 W(float x, float y, float z) => o + rot * new Vector3(x, y, z);
        const float halfW = 2.6f, halfL = 4.6f, eave = 3.8f, ridge = 5.1f;
        foreach (float x in new[] { -halfW, halfW })
        {
            foreach (float z in new[] { -halfL, 0f, halfL })
                Box(g, "Sloup", W(x, eave * 0.5f, z), new Vector3(0.28f, eave, 0.28f), post, true, ShedYaw);
            Box(g, "Vaznice", W(x, eave - 0.15f, 0f), new Vector3(0.26f, 0.3f, halfL * 2f + 0.3f), post, true, ShedYaw);
        }
        float angle = Mathf.Atan2(ridge - eave, halfW) * Mathf.Rad2Deg;
        float slant = Mathf.Sqrt(halfW * halfW + (ridge - eave) * (ridge - eave)) + 0.6f;
        for (int side = -1; side <= 1; side += 2)
        {
            var slab = MapBuildKit.Box(g, "Strecha", W(side * halfW * 0.5f, (eave + ridge) * 0.5f + 0.12f, 0f),
                new Vector3(slant, 0.18f, halfL * 2f + 1.2f), rot * Quaternion.Euler(0f, 0f, -side * angle), roof, true);
            GameObjectUtility.SetStaticEditorFlags(slab, 0);
        }
        // seno: plna rada vzadu (1,5 m - kryt, nevyleze se), nad ni druha vrstva (pod strechou se nestoji), balik u cela
        Box(g, "Seno", W(-1.55f, 0.72f, 0f), new Vector3(1.7f, 1.5f, 8.6f), hay, true, ShedYaw);
        Box(g, "Seno", W(-1.6f, 2.12f, -2f), new Vector3(1.5f, 1.3f, 4.2f), hay, true, ShedYaw);
        Box(g, "Seno", W(1f, 0.72f, 3.3f), new Vector3(1.5f, 1.5f, 1.8f), hay, true, ShedYaw + 8f);
    }

    // Posed na hrebeni: plosina 3,2 m se zabradlim a strisskou, plne schody (stupne az na zem) po hrebeni na zapad.
    // Chodi se po neviditelne rampe pres hrany stupnu (o jednotlive schody se postava nezasekne).
    static void Posed(Transform g)
    {
        Vector2 back = (RidgeA - RidgeB).normalized;
        float yaw = Mathf.Atan2(back.x, back.y) * Mathf.Rad2Deg;
        var rot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 o = On(PosedAt.x, PosedAt.y);
        Vector3 W(float x, float y, float z) => o + rot * new Vector3(x, y, z);
        const float top = 3.2f, half = 1.3f, rise = 0.25f, tread = 0.32f, width = 1.1f;

        foreach (float x in new[] { -1.1f, 1.1f })
            foreach (float z in new[] { -1.1f, 1.1f })
                Box(g, "Noha", W(x, top * 0.5f - 0.2f, z), new Vector3(0.26f, top + 0.4f, 0.26f), post, true, yaw);
        Box(g, "Plosina", W(0f, top - 0.1f, 0f), new Vector3(half * 2f, 0.2f, half * 2f), plank, true, yaw);
        Box(g, "Zabradli", W(0f, top + 0.5f, -half + 0.05f), new Vector3(half * 2f, 1f, 0.1f), plank, true, yaw);
        Box(g, "Zabradli", W(-half + 0.05f, top + 0.5f, 0f), new Vector3(0.1f, 1f, half * 2f), plank, true, yaw);
        Box(g, "Zabradli", W(half - 0.05f, top + 0.5f, 0f), new Vector3(0.1f, 1f, half * 2f), plank, true, yaw);
        foreach (float x in new[] { -half + 0.08f, half - 0.08f })
            foreach (float z in new[] { -half + 0.08f, half - 0.08f })
                Box(g, "Sloupek", W(x, top + 1.3f, z), new Vector3(0.12f, 2.6f, 0.12f), post, true, yaw);
        Box(g, "Stribska", W(0f, top + 2.67f, 0f), new Vector3(half * 2f + 0.6f, 0.15f, half * 2f + 0.6f), roof, true, yaw);

        int n = Mathf.CeilToInt(top / rise);
        float stepRise = top / n;
        for (int i = 0; i < n; i++)
        {
            float h = top - i * stepRise;   // i = 0 u plosiny
            Box(g, "Stupen", W(0f, h * 0.5f - 0.1f, half + (i + 0.5f) * tread), new Vector3(width, h + 0.2f, tread), plank, true, yaw);
        }
        Vector3 low = W(0f, 0f, half + (n + 1) * tread), high = W(0f, top, half + tread);
        Vector3 along = (high - low).normalized;
        Vector3 normal = Vector3.Cross(along, rot * Vector3.right);
        if (normal.y < 0f) normal = -normal;
        var ramp = new GameObject("RampaSchodu");
        ramp.transform.SetParent(g, false);
        ramp.transform.SetPositionAndRotation((low + high) * 0.5f - normal * 0.15f, Quaternion.LookRotation(along, normal));
        ramp.AddComponent<BoxCollider>().size = new Vector3(width, 0.3f, Vector3.Distance(low, high) + 0.3f);
    }

    // Stoh sena ve dvou vrstvach (1,5 m + 1,3 m): spodni se nevyleze, cely stoh je plny kryt.
    static void HayStack(Transform g, float x, float z, float yaw)
    {
        Vector3 b = On(x, z);
        if (!Free(b + Vector3.up * 1.4f, new Vector3(1.1f, 1.2f, 2.4f), yaw, "Seno")) return;
        Box(g, "Seno", b + Vector3.up * 0.7f, new Vector3(2.4f, 1.5f, 5f), hay, true, yaw);
        Box(g, "Seno", b + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 2.1f, -0.5f), new Vector3(2.2f, 1.3f, 3.4f), hay, true, yaw);
    }

    static void Bush(Transform g, float x, float z, float size, int seed)
    {
        Vector3 at = On(x, z, size * 0.3f);
        if (!Free(at + Vector3.up * size * 0.3f, Vector3.one * size * 0.5f, 0f, "Ker")) return;
        var mesh = MapBuildKit.BlobMesh(40 + Mathf.Abs(seed) % 6);
        var go = MapBuildKit.MeshObject(g, "Ker", mesh, at, Quaternion.Euler(0f, seed * 37f, 0f), new Vector3(size, size * 0.8f, size),
            leaves[Mathf.Abs(seed) % leaves.Length]);
        GameObjectUtility.SetStaticEditorFlags(go, 0);
        go.AddComponent<MeshCollider>().sharedMesh = mesh;   // kolize presne podle tvaru keru
    }

    // Hromada polen: vodorovne kmeny ve trech vrstvach (1,4 m - da se na ni vyskocit).
    static void LogPile(Transform g, float x, float z, float yaw)
    {
        if (!Free(On(x, z, 0.75f), new Vector3(1f, 0.6f, 2.1f), yaw, "Polena")) return;
        var pile = new GameObject("Polena").transform;
        pile.SetParent(g, false);
        pile.SetPositionAndRotation(On(x, z), Quaternion.Euler(0f, yaw, 0f));
        int[] perRow = { 4, 3, 2 };
        for (int row = 0; row < perRow.Length; row++)
            for (int i = 0; i < perRow[row]; i++)
            {
                var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(log.GetComponent<Collider>());
                log.name = "Kmen";
                log.transform.SetParent(pile, false);
                log.transform.localPosition = new Vector3((i - (perRow[row] - 1) * 0.5f) * 0.52f, 0.26f + row * 0.45f, 0f);
                log.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                log.transform.localScale = new Vector3(0.5f, 2.2f, 0.5f);
                log.GetComponent<Renderer>().sharedMaterial = logs;
            }
        var box = pile.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.7f, 0f);
        box.size = new Vector3(2.1f, 1.4f, 4.4f);
    }

    // Plot kolem dvora rozhledny (1,1 m - pres nej se strili a da se preskocit) se vstupy ze vsech stran.
    // Useky, ktere by prosly rozhlednou, jejim schodistem nebo kulnickou tunelu, se vynechaji.
    static void YardFence(Transform g)
    {
        float x0 = Yard.xMin, x1 = Yard.xMax, z0 = Yard.yMin, z1 = Yard.yMax;
        void Line(Vector2 a, Vector2 b, (float a, float b)[] gaps)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            Vector2 dir = d / len;
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            for (float s = 0f; s < len - 0.01f; s += 2.5f)
            {
                float e = Mathf.Min(len, s + 2.5f), mid = (s + e) * 0.5f;
                bool open = false;
                foreach (var (ga, gb) in gaps) if (mid > ga && mid < gb) open = true;
                if (open) continue;
                Vector2 m = a + dir * mid, p0 = a + dir * s;
                float y = H(m.x, m.y);
                if (InsideTunnelShed(m, 0.5f)) continue;
                if (!Free(new Vector3(m.x, y + 0.6f, m.y), new Vector3(0.1f, 0.45f, (e - s) * 0.5f - 0.15f), yaw, "Plot dvora")) continue;
                Box(g, "Latka", new Vector3(m.x, y + 0.75f, m.y), new Vector3(0.08f, 0.12f, e - s), plank, false, yaw);
                Box(g, "Latka", new Vector3(m.x, y + 0.35f, m.y), new Vector3(0.08f, 0.12f, e - s), plank, false, yaw);
                Box(g, "Kolik", new Vector3(p0.x, y + 0.55f, p0.y), new Vector3(0.14f, 1.1f, 0.14f), post, false, yaw);
                Invisible(g, "Kolize", new Vector3(m.x, y + 0.55f, m.y), new Vector3(0.15f, 1.1f, e - s), yaw);
            }
        }
        // vstupy: S (z hrebene), SZ roh (stred), Z (z uvozu), V (od der - obranci), J (od kulnicky tunelu)
        Line(new Vector2(x0, z1), new Vector2(x1, z1), new[] { (0f, 4f), (14f, 18f) });
        Line(new Vector2(x0, z0), new Vector2(x0, z1), new[] { (5f, 9f), (20f, 24f) });
        Line(new Vector2(x1, z0), new Vector2(x1, z1), new[] { (12f, 17f) });
        Line(new Vector2(x0, z0), new Vector2(x1, z0), new[] { (10f, 14f), (17f, 21f) });

        // uvnitr: vuz se senem, bedny, studna se strisskou, seno
        Wagon(g, On(-91f, 46f), 80f);
        Box(g, "Bedny", On(-77f, 47.5f, 0.55f), new Vector3(1.1f, 1.1f, 1.1f), shed, true, 20f);
        Box(g, "Bedny", On(-75.6f, 47.2f, 0.45f), new Vector3(0.9f, 0.9f, 0.9f), shed, true, -10f);
        Vector3 w = On(-73f, 33f);
        Box(g, "Studna", w + Vector3.up * 0.45f, new Vector3(1.8f, 0.9f, 1.8f), rock);
        Box(g, "Studna_Strisska", w + Vector3.up * 2.2f, new Vector3(2.2f, 0.12f, 1.6f), roof);
        foreach (float dx in new[] { -0.8f, 0.8f })
            Box(g, "Studna_Sloupek", w + new Vector3(dx, 1.5f, 0f), new Vector3(0.12f, 1.3f, 0.12f), post);
        Box(g, "Seno_Dvur", On(-92f, 30f, 0.72f), new Vector3(2.8f, 1.5f, 1.4f), hay, true, 10f);
    }

    static bool InsideTunnelShed(Vector2 p, float margin) =>
        p.x > RampRect.xMin - 0.3f - margin && p.x < RampRect.xMax + 0.3f + margin && p.y > RampRect.yMin - 2.3f - margin && p.y < RampRect.yMax + 0.3f + margin;

    // Vstup do tunelu: kulnicka postavena znovu (vyssi dvere, mriz u vstupu) a plot dvora, ktery vedl skrz ni, pryc.
    static void TunnelEntrance(Transform root)
    {
        var section = root.Find("NovaCast/SekceA");
        if (section == null) return;
        var old = section.Find("KulnaTunelu");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        TunnelShed(MapBuildKit.Group(section, "KulnaTunelu"));
        var yard = section.Find("Dvur");
        int removed = 0;
        if (yard != null)
        {
            var parts = new List<GameObject>();
            foreach (Transform t in yard)
                if ((t.name == "Latka" || t.name == "Kolik" || t.name == "Kolize") && InsideTunnelShed(new Vector2(t.position.x, t.position.z), 0.5f))
                    parts.Add(t.gameObject);
            foreach (var go in parts) { Undo.DestroyObjectImmediate(go); removed++; }
        }
        Debug.Log($"[Koncept] Vstup do tunelu: dvere 2,3 m, mriz u vstupu (otevre se po A); z kulnicky odstraneno {removed} kusu plotu.");
    }

    // Kulnicka nad vstupem do tunelu (dvere na jih), cela rampa pod strechou.
    static void TunnelShed(Transform g)
    {
        float xa = RampRect.xMin - 0.3f, xb = RampRect.xMax + 0.3f, za = RampRect.yMin - 2.3f, zb = RampRect.yMax + 0.3f;
        float y = H(-70f, 16f) - 0.05f;
        const float h = 2.6f, t = 0.3f;
        Box(g, "Stena_Z", new Vector3(xa + t * 0.5f, y + h * 0.5f, (za + zb) * 0.5f), new Vector3(t, h, zb - za), shed);
        Box(g, "Stena_V", new Vector3(xb - t * 0.5f, y + h * 0.5f, (za + zb) * 0.5f), new Vector3(t, h, zb - za), shed);
        Box(g, "Stena_S", new Vector3((xa + xb) * 0.5f, y + h * 0.5f, zb - t * 0.5f), new Vector3(xb - xa, h, t), shed);
        // dvere 2,4 x 2,3 m (hrac je 2 m vysoky - drivejsi 1,9 m vysoke dvere ho nepustily)
        const float door = 2.4f, doorH = 2.3f;
        float dx0 = -70f - door * 0.5f, dx1 = -70f + door * 0.5f;
        Box(g, "Celo_1", new Vector3((xa + dx0) * 0.5f, y + h * 0.5f, za + t * 0.5f), new Vector3(dx0 - xa, h, t), shed);
        Box(g, "Celo_2", new Vector3((dx1 + xb) * 0.5f, y + h * 0.5f, za + t * 0.5f), new Vector3(xb - dx1, h, t), shed);
        Box(g, "Preklad", new Vector3(-70f, y + (doorH + h) * 0.5f, za + t * 0.5f), new Vector3(door, h - doorH, t), shed);

        // mriz ve dverich: do dobyti bodu A zavrena (tunel do mapy je pro utocniky zavreny uz u vstupu)
        var gate = new GameObject("Mriz_Vstup").transform;
        gate.SetParent(g, false);
        gate.gameObject.AddComponent<ZoneGate>().unlockAfter = 1;
        var closed = new GameObject("Zavreno").transform;
        closed.SetParent(gate, false);
        float gz = za - 0.12f;
        for (float x = dx0 + 0.15f; x < dx1 - 0.05f; x += 0.25f)
            Box(closed, "Tyc", new Vector3(x, y + doorH * 0.5f, gz), new Vector3(0.06f, doorH, 0.06f), metal, false);
        foreach (float yy in new[] { 0.25f, doorH - 0.2f })
            Box(closed, "Pricka", new Vector3(-70f, y + yy, gz), new Vector3(door, 0.08f, 0.08f), metal, false);
        Invisible(closed, "Kolize", new Vector3(-70f, y + doorH * 0.5f, gz), new Vector3(door, doorH, 0.25f));
        Box(g, "Strecha", new Vector3((xa + xb) * 0.5f, y + h + 0.12f, (za + zb) * 0.5f), new Vector3(xb - xa + 0.6f, 0.24f, zb - za + 0.6f), roof);
        Box(g, "Podlaha", new Vector3(-70f, y + 0.05f, (za + RampRect.yMin) * 0.5f), new Vector3(xb - xa - 0.6f, 0.1f, RampRect.yMin - za), concreteDark);
        var light = new GameObject("Svetlo").AddComponent<Light>();
        light.transform.SetParent(g, false);
        light.transform.position = new Vector3(-70f, y + 2.1f, za + 3f);
        light.type = LightType.Point;
        light.range = 8f;
        light.intensity = 1.3f;
        light.color = new Color(1f, 0.82f, 0.55f);
        light.shadows = LightShadows.None;
    }

    // ---------------- cesty a sad ----------------

    static Material path;

    // Vyslapana cesta (sterk) podel lomene cary - hrac z ni vycte, kudy vedou trasy k bodu.
    static void PathRibbon(Transform g, string name, Vector2[] points, float width)
    {
        if (path == null) path = Load("Assets/Materials/Garden/Gravel.mat") ?? lawn;
        var samples = new List<Vector2>();
        for (int i = 0; i < points.Length - 1; i++)
        {
            float len = Vector2.Distance(points[i], points[i + 1]);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.5f));
            for (int k = 0; k < n; k++) samples.Add(Vector2.Lerp(points[i], points[i + 1], k / (float)n));
        }
        samples.Add(points[points.Length - 1]);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        float along = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            Vector2 prev = samples[Mathf.Max(0, i - 1)], next = samples[Mathf.Min(samples.Count - 1, i + 1)];
            Vector2 tangent = (next - prev).normalized;
            Vector2 side = new Vector2(-tangent.y, tangent.x) * (width * 0.5f);
            if (i > 0) along += Vector2.Distance(samples[i - 1], samples[i]);
            foreach (var sgn in new[] { -1f, 1f })
            {
                Vector2 q = samples[i] + side * sgn;
                vertices.Add(new Vector3(q.x, H(q.x, q.y) + 0.05f, q.y));
                uvs.Add(new Vector2(sgn * width * 0.5f, along));
            }
            if (i > 0)
            {
                int v = vertices.Count - 4;
                triangles.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
            }
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var go = new GameObject(name);
        go.transform.SetParent(g, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = path;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void Paths(Transform g)
    {
        // dvur statku: od vrat stodoly k brankam
        PathRibbon(g, "Cesta_Dvur", new[] { new Vector2(-131f, 81.5f), new Vector2(-130f, 76f), new Vector2(-125.5f, 71.5f), new Vector2(-125.5f, 69f) }, 2.4f);
        PathRibbon(g, "Cesta_Dvur_Bok", new[] { new Vector2(-131.5f, 79f), new Vector2(-136f, 73.5f), new Vector2(-137.7f, 69f) }, 1.6f);
        PathRibbon(g, "Cesta_Dvur_V", new[] { new Vector2(-121.6f, 86f), new Vector2(-114.5f, 85.75f) }, 2f);
        // utocnici: stred (kolem seniku), hreben (kolem posedu), uvoz
        PathRibbon(g, "Cesta_Stred", new[] { new Vector2(-125.5f, 69f), new Vector2(-122f, 64.5f), new Vector2(-112.5f, 58.2f), new Vector2(-100f, 52.5f), new Vector2(-93f, 51.6f) }, 2.4f);
        PathRibbon(g, "Cesta_Hreben", new[] { new Vector2(-114.5f, 85.75f), new Vector2(-106f, 82.4f), new Vector2(-96f, 77.6f), new Vector2(-89.5f, 71.8f),
            new Vector2(-84.5f, 68.6f), new Vector2(-80f, 66f), new Vector2(-78.5f, 58f), new Vector2(-79f, 51.6f) }, 1.8f);
        PathRibbon(g, "Cesta_Uvoz", new[] { new Vector2(-137.7f, 69f), new Vector2(-136.5f, 65.5f), new Vector2(-134f, 62f), new Vector2(-120f, 54f),
            new Vector2(-110f, 42f), new Vector2(-99f, 34f), new Vector2(-95.5f, 34f) }, 2f);
        // obranci: od der ke vstupum dvora (jizni cesta vede kolem kulnicky tunelu)
        PathRibbon(g, "Cesta_Obrana_S", new[] { new Vector2(-54f, 49.5f), new Vector2(-62f, 46f), new Vector2(-67.5f, 41.5f) }, 1.8f);
        PathRibbon(g, "Cesta_Obrana_J", new[] { new Vector2(-54f, 0f), new Vector2(-63f, 10f), new Vector2(-70f, 14f), new Vector2(-75f, 20f), new Vector2(-76f, 26.5f) }, 1.8f);
    }

    // Sad na jihu nove casti (lame dlouhe vyhledy u jizni diry) a kurnik.
    static void Orchard(Transform g)
    {
        Vector3 c = On(-90f, 13f);
        Box(g, "Kurnik", c + Vector3.up * 1.1f, new Vector3(3.2f, 2.2f, 2.6f), shed, true, -12f);
        Box(g, "Kurnik_Strecha", c + Vector3.up * 2.3f, new Vector3(3.8f, 0.18f, 3.2f), roof, true, -12f);
        Box(g, "Kurnik_Lavka", c + Quaternion.Euler(0f, -12f, 0f) * new Vector3(0f, 0.45f, 1.9f), new Vector3(0.5f, 0.06f, 1.4f), plank, false, -12f);
        int seed = 71;
        foreach (float z in new[] { 16f, 10f, 4f })
            foreach (float x in new[] { -124f, -114f, -104f, -94f, -84f })
            {
                float jx = x + (z == 10f ? 5f : 0f);
                var p = new Vector2(jx, z);
                if (!Inside(p, Area) || EdgeDistance(p, Area) < 3.5f || RiverDistance(p, true) < 4f) continue;
                Tree(g, jx, z, 0.72f, seed++);
            }
    }

    // ---------------- kontrola (po kazde stavbe sekce A) ----------------
    // Temp/Koncept_Audit.txt: vyhledy, svahy, co je videt a nema kolizi, kam se utocnik dostane (a kam vyleze),
    // zebriky. Temp/Koncept_Chodnost.png: mapa chodnosti (zelena = dojde, zluta = vyleze nahoru, cervena = nedojde).

    public static bool AuditPending;

    public static void Audit()
    {
        AuditPending = false;
        Physics.SyncTransforms();
        var sb = new System.Text.StringBuilder();
        float towerTop = TopFloor(new Vector3(TowerTarget.x, 40f, TowerTarget.z));
        Vector3 tower = new Vector3(TowerTarget.x, towerTop + 1.6f, TowerTarget.z);
        float posedTop = H(PosedAt.x, PosedAt.y) + 3.2f;

        sb.AppendLine("== Vyhledy (72 smeru ve vysce oci) ==");
        var spots = new (string name, Vector3 at)[]
        {
            ("Vrata stodoly (jih)", On(-131f, 80f, 1.6f)), ("Hlavni brana dvora", On(-125.5f, 68.5f, 1.6f)),
            ("Bocni branka", On(-137.7f, 68.5f, 1.6f)), ("Vychodni brana", On(-112.5f, 85.7f, 1.6f)),
            ("Senik (u cesty)", On(-112f, 58.5f, 1.6f)), ("Posed (plosina)", new Vector3(PosedAt.x, posedTop + 1.6f, PosedAt.y)),
            ("Uvoz (stred)", On(-115f, 48f, 1.6f)), ("Bod A (dvur)", On(-88f, 40f, 1.6f)),
            ($"Plosina rozhledny ({towerTop:0.0} m)", tower), ("Dira sever", On(-56f, 49.5f, 1.6f)), ("Dira jih", On(-56f, 0f, 1.6f)),
        };
        foreach (var (name, at) in spots)
        {
            float longest = 0f, sum = 0f;
            int longDir = 0, over40 = 0;
            for (int i = 0; i < 72; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 5f, 0f) * Vector3.forward;
                float d = Physics.Raycast(at, dir, out var hit, 150f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 150f;
                sum += d;
                if (d > 40f) over40++;
                if (d > longest) { longest = d; longDir = i * 5; }
            }
            sb.AppendLine($"{name}: nejdelsi {longest:0} m (smer {longDir} st.), prumer {sum / 72f:0} m, smeru nad 40 m: {over40}/72");
        }
        foreach (var (from, fromName) in new[] { (tower, "plosina rozhledny"), (On(-88f, 40f, 1.6f), "bod A") })
            foreach (var (to, toName) in new[]
            {
                (On(-131f, 82.6f, 1.4f), "jizni vrata stodoly"), (On(-121.7f, 86f, 1.4f), "vychodni vrata stodoly"),
                (On(-125.5f, 70f, 1.4f), "hlavni brana"), (On(-137.7f, 70f, 1.4f), "bocni branka"), (On(-114f, 85.75f, 1.4f), "vychodni brana"),
            })
            {
                bool blocked = Physics.Linecast(from, to, out var h, ~0, QueryTriggerInteraction.Ignore) && Vector3.Distance(h.point, to) > 1.5f;
                sb.AppendLine($"Z {fromName} na {toName}: {(blocked ? "zakryto" : "VIDET")}");
            }

        sb.AppendLine();
        TerrainAudit(sb);
        sb.AppendLine();
        ShootAudit(sb);
        sb.AppendLine();
        WalkAudit(sb);
        sb.AppendLine();
        LadderAudit(sb);
        System.IO.File.WriteAllText("Temp/Koncept_Audit.txt", sb.ToString());
        Debug.Log("[Koncept] Kontrola sekce A: Temp/Koncept_Audit.txt, Temp/Koncept_Chodnost.png");
    }

    // Nejvyssi podlaha pod bodem, na kterou se vejde hrac (napr. horni plosina rozhledny pod strechou).
    static float TopFloor(Vector3 from)
    {
        // (strecha se nepocita: plosina ma nad sebou strechu, strecha nic)
        float best = 0f, covered = float.NaN;
        foreach (var h in Physics.RaycastAll(from, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.normal.y < 0.7f) continue;
            if (Physics.CheckCapsule(h.point + Vector3.up * 0.45f, h.point + Vector3.up * 1.65f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
            best = Mathf.Max(best, h.point.y);
            if (Physics.Raycast(h.point + Vector3.up * 2.05f, Vector3.up, 4f, ~0, QueryTriggerInteraction.Ignore))
                covered = float.IsNaN(covered) ? h.point.y : Mathf.Max(covered, h.point.y);
        }
        return float.IsNaN(covered) ? best : covered;
    }

    static void TerrainAudit(System.Text.StringBuilder sb)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null) return;
        var v = mesh.vertices;
        var t = mesh.triangles;
        float worst = 0f;
        int steep = 0, total = 0;
        Vector3 worstAt = Vector3.zero;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
            Vector3 m = (a + b + c) / 3f;
            if (!Inside(new Vector2(m.x, m.z), Area)) continue;
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            float angle = Vector3.Angle(n.y < 0f ? -n : n, Vector3.up);
            total++;
            if (angle > 30f) steep++;
            if (angle > worst) { worst = angle; worstAt = m; }
        }
        sb.AppendLine($"== Teren == nejprudsi svah {worst:0} st. u ({worstAt.x:0}, {worstAt.z:0}); trojuhelniku nad 30 st.: {steep} z {total}");
    }

    // Co je videt, ale nema kolizi (strili se skrz): renderery bez kolize, jejichz stred neni v zadne kolizi.
    static void ShootAudit(System.Text.StringBuilder sb)
    {
        sb.AppendLine("== Pruchozi pro strely (videt, bez kolize) ==");
        var map = GameObject.Find("Map-Domasov");
        var section = map != null ? map.transform.Find("KonceptUtoku/NovaCast/SekceA") : null;
        if (section == null) { sb.AppendLine("(sekce A nenalezena)"); return; }
        int count = 0;
        foreach (var r in section.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.GetComponent<Collider>() != null || r.name == "Koruna" || r.name.StartsWith("Cesta")) continue;
            Bounds b = r.bounds;
            if (Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z)) < 0.3f) continue;   // tenke latky a tyce
            if (Physics.CheckSphere(b.center, 0.1f, ~0, QueryTriggerInteraction.Ignore)) continue;
            count++;
            if (count <= 25) sb.AppendLine($"  {Path(section, r.transform)} u ({b.center.x:0.0}, {b.center.y:0.0}, {b.center.z:0.0}), velikost {b.size}");
        }
        sb.AppendLine(count == 0 ? "  nic" : $"  celkem {count}");
    }

    // Chodnost: mrizka 1 m pres novou cast, na kazdem miste vsechny podlahy, kam se vejde hrac (2 m, polomer 0,3).
    // Z podlahy na sousedni se dojde (schod do 0,45 m), vyskoci (do 1,15 m) nebo seskoci; prekazku do 1,15 m preskoci.
    // Prohledava se od spawnu utocniku ve stodole (diry k obrancum jsou pro utocniky zavrene).
    static void WalkAudit(System.Text.StringBuilder sb)
    {
        const float gx0 = -140f, gz0 = -22f;
        const int nx = 89, nz = 123, maxK = 6;
        var floors = new float[nx, nz][];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                float x = gx0 + i + 0.5f, z = gz0 + j + 0.5f;
                var list = new List<float>();
                if (Inside(new Vector2(x, z), Area))
                    foreach (var h in Physics.RaycastAll(new Vector3(x, 45f, z), Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (h.normal.y < 0.7f) continue;
                        if (Physics.CheckCapsule(h.point + Vector3.up * 0.45f, h.point + Vector3.up * 1.65f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        list.Add(h.point.y);
                    }
                list.Sort();
                if (list.Count > maxK) list.RemoveRange(maxK, list.Count - maxK);
                floors[i, j] = list.ToArray();
            }

        var visited = new bool[nx, nz, maxK];
        var queue = new Queue<(int i, int j, int k)>();
        void Seed(float x, float z)
        {
            int i = Mathf.FloorToInt(x - gx0), j = Mathf.FloorToInt(z - gz0);
            var fl = floors[i, j];
            int best = -1;
            for (int k = 0; k < fl.Length; k++)
                if (best < 0 || Mathf.Abs(fl[k] - H(x, z)) < Mathf.Abs(fl[best] - H(x, z))) best = k;
            if (best < 0) return;
            visited[i, j, best] = true;
            queue.Enqueue((i, j, best));
        }
        Seed(-129f, 87f);
        Seed(-127f, 86f);
        int[] di = { 1, -1, 0, 0 }, dj = { 0, 0, 1, -1 };
        while (queue.Count > 0)
        {
            var (i, j, k) = queue.Dequeue();
            float y1 = floors[i, j][k];
            Vector3 p1 = new Vector3(gx0 + i + 0.5f, y1, gz0 + j + 0.5f);
            for (int n = 0; n < 4; n++)
            {
                int a = i + di[n], b = j + dj[n];
                if (a < 0 || b < 0 || a >= nx || b >= nz) continue;
                var fl = floors[a, b];
                for (int m = 0; m < fl.Length; m++)
                {
                    if (visited[a, b, m] || fl[m] - y1 > 1.15f) continue;
                    Vector3 p2 = new Vector3(gx0 + a + 0.5f, fl[m], gz0 + b + 0.5f);
                    if (!Passable(p1, p2)) continue;
                    visited[a, b, m] = true;
                    queue.Enqueue((a, b, m));
                }
            }
        }

        bool Reached(float x, float z, float minAbove, float maxAbove)
        {
            int ci = Mathf.FloorToInt(x - gx0), cj = Mathf.FloorToInt(z - gz0);
            for (int i = ci - 1; i <= ci + 1; i++)
                for (int j = cj - 1; j <= cj + 1; j++)
                {
                    if (i < 0 || j < 0 || i >= nx || j >= nz) continue;
                    float ground = H(gx0 + i + 0.5f, gz0 + j + 0.5f);
                    for (int k = 0; k < floors[i, j].Length; k++)
                    {
                        float above = floors[i, j][k] - ground;
                        if (visited[i, j, k] && above >= minAbove && above <= maxAbove) return true;
                    }
                }
            return false;
        }

        sb.AppendLine("== Chodnost (od spawnu utocniku) ==");
        var targets = new (string name, float x, float z, float lo, float hi)[]
        {
            ("Hlavni brana", -125.5f, 68.5f, -1f, 0.6f), ("Bocni branka", -137.7f, 68.5f, -1f, 0.6f), ("Vychodni brana", -112.5f, 85.75f, -1f, 0.6f),
            ("Senik (uvnitr)", ShedAt.x, ShedAt.y, -1f, 0.6f), ("Hreben u posedu", -84.5f, 68.6f, -1f, 0.6f),
            ("Plosina posedu", PosedAt.x, PosedAt.y, 2.6f, 4f), ("Uvoz", -115f, 48f, -1f, 0.6f), ("Bod A (dvur)", -88f, 40f, -1f, 0.6f),
            ("Bod A (u studny)", -76f, 36f, -1f, 0.6f), ("Kulnicka tunelu (uvnitr)", -70f, 17f, -1f, 0.6f), ("Sad", -95f, 15f, -1f, 0.6f), ("Plosina toboganu", SlideFootAt.x, SlideFootAt.y, 1.8f, 3f), ("U jezirka", -80f, 5f, -1f, 0.6f),
            ("Pred dirou sever", -57f, 49.5f, -1f, 0.6f), ("Pred dirou jih", -57f, 0f, -1f, 0.6f),
        };
        foreach (var (name, x, z, lo, hi) in targets)
            sb.AppendLine($"  {name}: {(Reached(x, z, lo, hi) ? "ano" : "NE")}");

        // kam se da vylezt (navstivene podlahy vic nez 1 m nad terenem), po shlucich
        var done = new bool[nx, nz, maxK];
        var clusters = new List<(int count, float high, Vector3 at, string what)>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
                for (int k = 0; k < floors[i, j].Length; k++)
                {
                    if (!visited[i, j, k] || done[i, j, k] || floors[i, j][k] - H(gx0 + i + 0.5f, gz0 + j + 0.5f) <= 1f) continue;
                    int count = 0;
                    float high = 0f;
                    Vector3 sum = Vector3.zero;
                    var stack = new Stack<(int, int, int)>();
                    stack.Push((i, j, k));
                    done[i, j, k] = true;
                    while (stack.Count > 0)
                    {
                        var (a, b, m) = stack.Pop();
                        float y = floors[a, b][m];
                        Vector3 p = new Vector3(gx0 + a + 0.5f, y, gz0 + b + 0.5f);
                        count++;
                        sum += p;
                        high = Mathf.Max(high, y - H(p.x, p.z));
                        for (int da = -1; da <= 1; da++)
                            for (int db = -1; db <= 1; db++)
                            {
                                int a2 = a + da, b2 = b + db;
                                if (a2 < 0 || b2 < 0 || a2 >= nx || b2 >= nz) continue;
                                for (int m2 = 0; m2 < floors[a2, b2].Length; m2++)
                                {
                                    float y2 = floors[a2, b2][m2];
                                    if (!visited[a2, b2, m2] || done[a2, b2, m2] || Mathf.Abs(y2 - y) > 1.2f
                                        || y2 - H(gx0 + a2 + 0.5f, gz0 + b2 + 0.5f) <= 1f) continue;
                                    done[a2, b2, m2] = true;
                                    stack.Push((a2, b2, m2));
                                }
                            }
                    }
                    Vector3 at = sum / count;
                    string what = "?";
                    if (Physics.Raycast(new Vector3(gx0 + i + 0.5f, floors[i, j][k] + 0.3f, gz0 + j + 0.5f), Vector3.down, out var hit, 0.6f, ~0, QueryTriggerInteraction.Ignore))
                        what = (hit.collider.transform.parent != null ? hit.collider.transform.parent.name + "/" : "") + hit.collider.name;
                    clusters.Add((count, high, at, what));
                }
        clusters.Sort((p, q) => q.count.CompareTo(p.count));
        sb.AppendLine($"  Vylezt jde na {clusters.Count} mist (vic nez 1 m nad terenem):");
        foreach (var cl in clusters)
            sb.AppendLine($"    {cl.what}: {cl.count} m2, az {cl.high:0.0} m nad zemi, u ({cl.at.x:0}, {cl.at.z:0})");

        // misto na zemi (otevrene), kam se utocnik nedostane
        int lost = 0;
        var lostAt = new List<Vector2>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                float x = gx0 + i + 0.5f, z = gz0 + j + 0.5f;
                if (!Inside(new Vector2(x, z), Area) || EdgeDistance(new Vector2(x, z), Area) < 2.5f) continue;
                for (int k = 0; k < floors[i, j].Length; k++)
                    if (Mathf.Abs(floors[i, j][k] - H(x, z)) < 0.35f && !visited[i, j, k])
                    {
                        lost++;
                        if (lostAt.Count < 12 && lostAt.TrueForAll(p => Vector2.Distance(p, new Vector2(x, z)) > 6f)) lostAt.Add(new Vector2(x, z));
                    }
            }
        sb.Append($"  Volna zem, kam utocnik nedojde: {lost} m2");
        foreach (var p in lostAt) sb.Append($"  ({p.x:0}, {p.y:0})");
        sb.AppendLine();

        // obrazek: sever nahore
        const int px = 4;
        var tex = new Texture2D(nx * px, nz * px, TextureFormat.RGB24, false);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                float x = gx0 + i + 0.5f, z = gz0 + j + 0.5f;
                Color col;
                if (!Inside(new Vector2(x, z), Area)) col = new Color(0.08f, 0.08f, 0.08f);
                else if (floors[i, j].Length == 0) col = new Color(0.3f, 0.3f, 0.32f);
                else
                {
                    bool ground = false, groundSeen = false, up = false;
                    for (int k = 0; k < floors[i, j].Length; k++)
                    {
                        float above = floors[i, j][k] - H(x, z);
                        if (above < 1f) { groundSeen = true; ground |= visited[i, j, k]; }
                        else up |= visited[i, j, k];
                    }
                    float shade = Mathf.Clamp01(0.55f + H(x, z) * 0.08f);
                    col = up ? new Color(1f, 0.85f, 0.1f) : ground ? new Color(0.2f, shade, 0.25f) : groundSeen ? new Color(0.85f, 0.15f, 0.15f) : new Color(0.3f, 0.3f, 0.32f);
                }
                for (int a = 0; a < px; a++)
                    for (int b = 0; b < px; b++)
                        tex.SetPixel(i * px + a, j * px + b, col);
            }
        tex.Apply();
        System.IO.File.WriteAllBytes("Temp/Koncept_Chodnost.png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    // Projde hrac z podlahy p1 na sousedni p2? Kapsle v pulce cesty nad vyssi z obou podlah (chuze/schod),
    // pripadne o 1,15 m vys (preskok nizke prekazky).
    static bool Passable(Vector3 p1, Vector3 p2)
    {
        Vector3 mid = (p1 + p2) * 0.5f;
        mid.y = Mathf.Max(p1.y, p2.y);
        if (!Physics.CheckCapsule(mid + Vector3.up * 0.42f, mid + Vector3.up * 1.7f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) return true;
        return !Physics.CheckCapsule(mid + Vector3.up * 1.57f, mid + Vector3.up * 2.85f, 0.3f, ~0, QueryTriggerInteraction.Ignore);
    }

    static void LadderAudit(System.Text.StringBuilder sb)
    {
        sb.AppendLine("== Zebriky ==");
        foreach (var ladder in Object.FindObjectsByType<Ladder>())
        {
            var zone = ladder.transform;
            float bottom = zone.position.y - ladder.size.y * 0.5f;
            float ground = GroundBelow(new Vector3(zone.position.x, bottom + 0.3f, zone.position.z), zone.parent);
            sb.AppendLine($"  {(zone.parent != null ? zone.parent.name : zone.name)} u ({zone.position.x:0}, {zone.position.z:0}): spodek {bottom:0.00} m, zem {ground:0.00} m, mezera {bottom - ground:0.00} m");
        }
    }

    static float GroundBelow(Vector3 p, Transform ignore)
    {
        float best = float.NaN;
        foreach (var h in Physics.RaycastAll(p, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
            if (float.IsNaN(best) || h.point.y > best) best = h.point.y;
        }
        return best;
    }

    // ---------------- 12. zebriky az na zem ----------------
    // Zebrik, ktery po posunu (rozhledna) konci nad zemi, se prodlouzi dolu: bocnice, pricky, oblast lezeni a stena za nim.
    static void FixLadders(Transform map)
    {
        Physics.SyncTransforms();
        foreach (var ladder in map.GetComponentsInChildren<Ladder>(true))
        {
            if (!ladder.gameObject.activeInHierarchy) continue;
            var zone = ladder.transform;
            var group = zone.parent != null ? zone.parent : zone;
            float bottom = zone.position.y - ladder.size.y * 0.5f;
            float ground = GroundBelow(new Vector3(zone.position.x, bottom + 0.3f, zone.position.z), group);
            float gap = bottom - ground;
            if (float.IsNaN(gap) || gap < 0.15f || gap > 12f) continue;

            Undo.RecordObject(ladder, "Koncept");
            Undo.RecordObject(zone, "Koncept");
            ladder.size.y += gap;
            zone.position -= Vector3.up * (gap * 0.5f);

            var parts = new List<Transform>();
            foreach (Transform part in group) parts.Add(part);
            Transform lowestRung = null;
            foreach (var part in parts)
            {
                if (part.name == "Bocnice" && part.TryGetComponent<Renderer>(out var r))
                {
                    Bounds b = r.bounds;
                    var ext = MapBuildKit.Box(group, "Bocnice", new Vector3(b.center.x, (ground + b.min.y) * 0.5f + 0.01f, b.center.z),
                        new Vector3(0.07f, b.min.y - ground + 0.02f, 0.07f), part.rotation, r.sharedMaterial, false);
                    Undo.RegisterCreatedObjectUndo(ext, "Koncept");
                }
                else if (part.name == "Pricka" && (lowestRung == null || part.position.y < lowestRung.position.y)) lowestRung = part;
                else if (part.name == "ZaZebrikem" && part.TryGetComponent<BoxCollider>(out var bc))
                {
                    Undo.RecordObject(bc, "Koncept");
                    Undo.RecordObject(part, "Koncept");
                    bc.size += new Vector3(0f, gap, 0f);
                    part.position -= Vector3.up * (gap * 0.5f);
                }
            }
            if (lowestRung != null)
                for (float y = lowestRung.position.y - 0.3f; y > ground + 0.15f; y -= 0.3f)
                {
                    var rung = Object.Instantiate(lowestRung.gameObject, group);
                    rung.name = "Pricka";
                    rung.transform.SetPositionAndRotation(new Vector3(lowestRung.position.x, y, lowestRung.position.z), lowestRung.rotation);
                    Undo.RegisterCreatedObjectUndo(rung, "Koncept");
                }
            Debug.Log($"[Koncept] Zebrik {group.name}: konci {gap:0.00} m nad zemi - prodlouzen az na zem.");
        }
    }

    // ---------------- 11. tobogan v nove casti (podle nakresu: skluzavka na zapade, reka na jih a sadem k jezirku) ----------------
    // Puvodni skluzavka, koryto, proudy a jezirko (MapPlayabilitySetup) se presunou - zustanou jejich materialy, proudy
    // (WaterCurrent / WaterFlow) i zvuky jizdy. Pod rekou je v terenu rovny pas s korytem a u konce prohlubne jezirko.

    static List<Vector2> riverCurve;

    // Osa reky (hladka krivka pres body River, po ~1,2 m).
    static List<Vector2> RiverCurve
    {
        get
        {
            if (riverCurve != null) return riverCurve;
            riverCurve = new List<Vector2>();
            for (int i = 0; i < River.Length - 1; i++)
            {
                Vector2 p0 = River[Mathf.Max(0, i - 1)], p1 = River[i], p2 = River[i + 1], p3 = River[Mathf.Min(River.Length - 1, i + 2)];
                int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 1.2f));
                for (int k = 0; k < steps; k++)
                {
                    float t = k / (float)steps;
                    riverCurve.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                        + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
                }
            }
            riverCurve.Add(River[River.Length - 1]);
            return riverCurve;
        }
    }

    // Vzdalenost od osy reky (withSlide: i od skluzavky - kvuli rovine pod ni).
    static float RiverDistance(Vector2 p, bool withSlide)
    {
        var curve = RiverCurve;
        float d = withSlide ? SegmentDistance(p, SlideFootAt, River[0]) : float.MaxValue;
        for (int i = 0; i < curve.Count - 1; i++) d = Mathf.Min(d, SegmentDistance(p, curve[i], curve[i + 1]));
        return d;
    }

    // Kolik je teren pod okolim snizeny: koryto (0,35 m) a jezirko (0,4 m, rovne dno do 3,8 m).
    static float RiverDip(Vector2 p)
    {
        float water = RiverDistance(p, false), pond = Vector2.Distance(p, River[River.Length - 1]);
        return Mathf.Max(0.35f * Mathf.Exp(-water * water / 1.69f),
            0.4f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3.8f, 5.2f, pond))));
    }

    static void SlideWest(Transform map, Transform root)
    {
        // uklid po predchozim pokusu (koridor reky za orezem)
        var corridor = root.Find("KorytoZaOrezem");
        if (corridor != null)
        {
            Undo.DestroyObjectImmediate(corridor.gameObject);
            var cut = root.Find("Orez");
            if (cut != null)
                foreach (Transform t in cut)
                    if (!t.gameObject.activeSelf) { Undo.RecordObject(t.gameObject, "Koncept"); t.gameObject.SetActive(true); }
        }

        var extras = map.Find("Hratelnost/Doplnky");
        var slide = extras != null ? extras.Find("Skluzavka") : null;
        var stream = extras != null ? extras.Find("Potok") : null;
        var flows = extras != null ? extras.Find("ProudVody") : null;
        var pond = extras != null ? extras.Find("Jezirko") : null;
        if (slide == null || stream == null || flows == null || pond == null)
        {
            Debug.LogWarning("[Koncept] Skluzavka, potok, proudy nebo jezirko ve scene chybi - tobogan se nepresune.");
            return;
        }
        int restored = 0;
        foreach (var t in new[] { stream, flows, pond })
            foreach (var c in t.GetComponentsInChildren<Transform>(true))
                if (!c.gameObject.activeSelf)
                {
                    Undo.RecordObject(c.gameObject, "Koncept");
                    c.gameObject.SetActive(true);
                    restored++;
                }

        // skluzavka: plosina na SlideFootAt, skluz miri na jih (do zacatku reky), schudky na zapad
        Undo.RecordObject(slide, "Koncept");
        slide.SetPositionAndRotation(On(SlideFootAt.x, SlideFootAt.y), Quaternion.Euler(0f, 180f, 0f));

        var curve = new List<Vector3>();
        foreach (var q in RiverCurve) curve.Add(On(q.x, q.y));

        // koryto: voda (rovna hladina v korytu) a brehy (po terenu)
        foreach (Transform part in stream)
        {
            if (!part.TryGetComponent<MeshFilter>(out var filter)) continue;
            string kind = filter.sharedMesh != null ? filter.sharedMesh.name : part.name;
            Mesh mesh = kind == "Voda" || part.name == "Voda" ? RiverRibbon("Voda", curve, -0.8f, 0.8f, 0.2f, true)
                : kind == "BrehL" ? RiverRibbon("BrehL", curve, -1.25f, -0.78f, 0.04f, false)
                : kind == "BrehP" ? RiverRibbon("BrehP", curve, 0.78f, 1.25f, 0.04f, false) : null;
            if (mesh == null) continue;
            Undo.RecordObject(filter, "Koncept");
            Undo.RecordObject(part, "Koncept");
            part.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            part.localScale = Vector3.one;
            filter.sharedMesh = mesh;
        }

        // proudy po cele rece (posledni metr a pul v jezirku uz nenese)
        var old = new List<GameObject>();
        foreach (Transform z in flows) old.Add(z.gameObject);
        foreach (var z in old) Undo.DestroyObjectImmediate(z);
        float total = 0f;
        for (int i = 1; i < curve.Count; i++) total += Vector3.Distance(curve[i], curve[i - 1]);
        float walked = 0f;
        int zones = 0;
        for (int i = 0; i < curve.Count - 1; i++)
        {
            Vector3 a = curve[i], b = curve[i + 1];
            Vector3 along = b - a;
            along.y = 0f;
            walked += along.magnitude;
            if (walked > total - 1.5f) break;
            var zone = new GameObject("Proud").transform;
            zone.SetParent(flows, false);
            zone.position = (a + b) * 0.5f + Vector3.up * 0.6f;
            zone.rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
            var current = zone.gameObject.AddComponent<WaterCurrent>();
            current.size = new Vector3(2.2f, 1.6f, along.magnitude + 0.3f);
            current.speed = 6f;
            Undo.RegisterCreatedObjectUndo(zone.gameObject, "Koncept");
            zones++;
        }

        // jezirko na konci reky
        Undo.RecordObject(pond, "Koncept");
        pond.position = curve[curve.Count - 1];
        Debug.Log($"[Koncept] Tobogan v nove casti: skluzavka u ({SlideFootAt.x:0}, {SlideFootAt.y:0}), reka {total:0} m ({zones} proudu), "
            + $"jezirko u ({pond.position.x:0}, {pond.position.z:0}); zapnuto {restored} casti.");
    }

    // Pas podel osy mezi bocnimi odsazenimi from..to. level: vsechny vrcholy ve vysce osy (hladina), jinak po terenu.
    static Mesh RiverRibbon(string name, List<Vector3> curve, float from, float to, float lift, bool level)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        float distance = 0f;
        for (int i = 0; i < curve.Count; i++)
        {
            Vector3 back = i > 0 ? curve[i] - curve[i - 1] : curve[1] - curve[0];
            Vector3 ahead = i < curve.Count - 1 ? curve[i + 1] - curve[i] : back;
            back.y = ahead.y = 0f;
            Vector3 tangent = (back.normalized + ahead.normalized).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, tangent);
            if (i > 0) distance += Vector3.Distance(curve[i], curve[i - 1]);
            foreach (float o in new[] { from, to })
            {
                Vector3 v = curve[i] + side * o;
                v.y = (level ? curve[i].y : H(v.x, v.z)) + lift;
                vertices.Add(v);
                uvs.Add(new Vector2(o, distance));
            }
            if (i > 0)
            {
                int a = (i - 1) * 2;
                triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------- nahledy ----------------

    public static void Snapshots(Transform map)
    {
        Shot("Temp/Koncept_Shora.png", new Vector3(-45f, 160f, 30f), Quaternion.Euler(90f, 0f, 0f), true, 92f, 1700, 1500);
        Vector3 chata = new Vector3(-36f, 2f, 4f);
        Shot("Temp/Koncept_Chata_1.png", chata + new Vector3(26f, 16f, 26f), Look(chata + new Vector3(26f, 16f, 26f), chata), false, 0f, 1280, 720);
        Shot("Temp/Koncept_Chata_2.png", chata + new Vector3(-24f, 14f, -26f), Look(chata + new Vector3(-24f, 14f, -26f), chata), false, 0f, 1280, 720);
        Shot("Temp/Koncept_Chata_3.png", chata + new Vector3(30f, 10f, -18f), Look(chata + new Vector3(30f, 10f, -18f), chata), false, 0f, 1280, 720);
        Vector3 west = new Vector3(-95f, 0f, 45f);
        Shot("Temp/Koncept_Zapad_1.png", new Vector3(-48f, 26f, 40f), Look(new Vector3(-48f, 26f, 40f), west), false, 0f, 1280, 720);
        Shot("Temp/Koncept_Zapad_2.png", new Vector3(-100f, 34f, -18f), Look(new Vector3(-100f, 34f, -18f), west + new Vector3(0f, 0f, 15f)), false, 0f, 1280, 720);
        Vector3 gate = On(-125.5f, 66.5f, 1.7f), posed = On(PosedAt.x, PosedAt.y, 3.2f + 1.7f), tower = new Vector3(-82f, 14.5f, 38f);
        Shot("Temp/Koncept_Zapad_3.png", gate, Look(gate, new Vector3(-84f, 3f, 42f)), false, 0f, 1280, 720);                 // z hlavni brany k bodu
        Shot("Temp/Koncept_Zapad_4.png", posed, Look(posed, new Vector3(-82f, 6f, 38f)), false, 0f, 1280, 720);                // z posedu na rozhlednu
        Shot("Temp/Koncept_Zapad_5.png", tower, Look(tower, new Vector3(-124f, 2f, 74f)), false, 0f, 1280, 720);               // z rozhledny na statek
        Shot("Temp/Koncept_Zapad_6.png", new Vector3(-104f, 16f, 58f), Look(new Vector3(-104f, 16f, 58f), new Vector3(-128f, 1f, 80f)), false, 0f, 1280, 720);
        Shot("Temp/Koncept_Zapad_7.png", On(-112f, 47f, 1.7f), Look(On(-112f, 47f, 1.7f), new Vector3(-96f, 1f, 35f)), false, 0f, 1280, 720); // v uvozu
        Shot("Temp/Koncept_Tobogan.png", new Vector3(-104f, 18f, 58f), Look(new Vector3(-104f, 18f, 58f), new Vector3(-114f, 0f, 22f)), false, 0f, 1280, 720);
    }

    static Quaternion Look(Vector3 from, Vector3 to) => Quaternion.LookRotation(to - from, Vector3.up);

    static void Shot(string file, Vector3 position, Quaternion rotation, bool ortho, float size, int w, int h)
    {
        var camObject = new GameObject("KonceptKamera");
        var cam = camObject.AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(position, rotation);
        cam.orthographic = ortho;
        if (ortho) cam.orthographicSize = size;
        else cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 600f;
        cam.clearFlags = CameraClearFlags.Skybox;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        cam.aspect = w / (float)h;
        try
        {
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        catch (System.Exception e) { Debug.LogWarning($"[Koncept] Nahled {file} se nepodaril: {e.Message}"); }
        finally
        {
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camObject);
        }
    }
}
