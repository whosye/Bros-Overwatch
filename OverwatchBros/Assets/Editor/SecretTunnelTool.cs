using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tajna odbocka v tunelu (Domasov): v podlaze hlavni chodby je poklop (jen vypada jako kryt - nema kolizi,
// hrac jim propadne o 4 m niz). Dole vede dlouha chodba: na vychod, zatacka na sever, sikma rampa o 3 m niz,
// zatacka na zapad a na konci mistnost (pro budouci buff). Zpatky nahoru se leze po zebriku v sachte pod poklopem.
// Boti o odbocce nevi (BotNoNav). Jen jednou: kdyz uz "TajnaOdbocka" existuje, nic nedela. Ctrl+Z vrati vse.
//
// Rozmery (svet, m): podlaha hlavni chodby -4.0; dole chodba A -8.0, po rampe -11.0; vnitrni vyska 2.7-3 m.
public static class SecretTunnelTool
{
    const string RootName = "TajnaOdbocka";
    const float T = 0.3f;   // tloustka sten a podlah

    // poklop v podlaze hlavni chodby (u vychodni steny, mezi lampami z 16.7 a 28.1)
    const float HoleX0 = 0.2f, HoleX1 = 1.5f, HoleZ0 = 21.8f, HoleZ1 = 23.2f;
    const float TopFloor = -4.0f;

    static Material concrete, concreteDark, lamp, rail;
    static Transform root;

    // Vse najednou: odbocka, prodlouzeni, jeskyne a vstup bazenem (kazdy krok preskoci, co uz je hotove).
    [MenuItem("BrosOverwatch/Mapa/Tajna jeskyne - postavit vse (vstup bazenem)")]
    static void BuildAll()
    {
        Build();
        Extend();
        CaveStyleTool.Run();
        PoolEntrance();
        Ring();
    }

    // Zlaty prsten na konci jeskyne (PickupSpot Ring: neviditelnost pro nepratele). Na MistoProBuff.
    [MenuItem("BrosOverwatch/Mapa/Tajna odbocka - prsten na konci")]
    static void Ring()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Tajny tunel] Nejdriv vypni hru."); return; }
        var rootGo = GameObject.Find("Map-Domasov/Tunel/" + RootName);
        var spot = rootGo != null ? rootGo.transform.Find("MistoProBuff") : null;
        if (spot == null) { Debug.LogWarning("[Tajny tunel] Nenasel jsem TajnaOdbocka/MistoProBuff - nejdriv postav odbocku."); return; }
        GameObject go = null;
        foreach (var p in rootGo.GetComponentsInChildren<PickupSpot>(true))
            if (p.kind == PickupKind.Ring) go = p.gameObject;
        bool created = go == null;
        if (created)
        {
            go = new GameObject("Pickup_Prsten");
            Undo.RegisterCreatedObjectUndo(go, "Prsten");
            go.transform.SetParent(rootGo.transform, false);
            go.transform.position = spot.position;
            var pickup = go.AddComponent<PickupSpot>();
            pickup.kind = PickupKind.Ring;
        }

        // hudba: u prstenu (kdo ho uvidi zblizka) se prepne na tema prstenu
        var music = rootGo.GetComponent<ZoneMusic>();
        var theme = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Mapa/Jeskyne_RingTheme.mp3");
        bool musicSet = false;
        if (music != null && theme != null && (music.focus != go.transform || music.focusClip != theme))
        {
            Undo.RecordObject(music, "Prsten");
            music.focus = go.transform;
            music.focusClip = theme;
            musicSet = true;
        }
        if (music == null) Debug.LogWarning("[Tajny tunel] Jeskyne jeste nema hudbu (pust Tajna odbocka - jeskyne) - tema prstenu nenastaveno.");
        else if (theme == null) Debug.LogWarning("[Tajny tunel] Nenasel jsem Assets/Audio/Mapa/Jeskyne_RingTheme.mp3.");

        if (created || musicSet) EditorSceneManager.MarkSceneDirty(rootGo.scene);
        Selection.activeGameObject = go;
        Debug.Log($"[Tajny tunel] {(created ? "Prsten polozen na konec jeskyne." : "Prsten uz tam je.")}{(musicSet ? " Hudba u prstenu nastavena." : "")} Ulozit: Ctrl+S.", go);
    }

    [MenuItem("BrosOverwatch/Mapa/Tajna odbocka v tunelu")]
    static void Build()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Tajny tunel] Nejdriv vypni hru."); return; }

        var tunnel = GameObject.Find("Map-Domasov/Tunel");
        var floor = GameObject.Find("Map-Domasov/Tunel/Corridors/Floor_1");
        if (tunnel == null || floor == null) { Debug.LogWarning("[Tajny tunel] Nenasel jsem Map-Domasov/Tunel/Corridors/Floor_1."); return; }
        if (tunnel.transform.Find(RootName) != null)
        {
            Debug.LogWarning("[Tajny tunel] Odbocka uz existuje - nic nemenim (smaz Tunel/TajnaOdbocka, kdyz ji chces postavit znovu).", tunnel);
            return;
        }

        concrete = floor.GetComponent<Renderer>().sharedMaterial;
        var ceilingObject = GameObject.Find("Map-Domasov/Tunel/Corridors/Ceiling_1");
        concreteDark = ceilingObject != null ? ceilingObject.GetComponent<Renderer>().sharedMaterial : concrete;
        lamp = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Tunnel/Tunnel_Lamp.mat") ?? concreteDark;
        rail = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Tunnel/Tunnel_Rail.mat") ?? concreteDark;

        Undo.SetCurrentGroupName("Tajna odbocka v tunelu");
        int group = Undo.GetCurrentGroup();

        // podlaha uz rozriznuta z driva (odbocka smazana, dira zustala) - jen postavit odbocku znovu
        bool alreadyCut = floor.transform.parent.Find("Floor_1_u_poklopu") != null;
        if (!alreadyCut && !CutHole(floor)) return;

        var rootGo = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(rootGo, "Tajna odbocka");
        rootGo.transform.SetParent(tunnel.transform, false);
        rootGo.transform.position = Vector3.zero;
        rootGo.AddComponent<BotNoNav>();
        root = rootGo.transform;

        BuildHatch();
        BuildShaft();
        BuildTunnel();
        BuildLadder();
        BuildLights();

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(tunnel.scene);
        Selection.activeGameObject = rootGo;
        Debug.Log("[Tajny tunel] Hotovo: poklop v hlavni chodbe (x 0.2-1.5, z 21.8-23.2), dole ~55 m chodby a mistnost na konci. "
            + "Ulozit scenu: Ctrl+S.", rootGo);
    }

    // ---------------- prodlouzeni ----------------
    // Z mistnosti na konci (x 0-4, z 40.5-45.5) dal: D na zapad x -18..0 (z 42-44, podlaha -11), E na sever
    // x -18..-16 (z 42-62, rampa z 48 (-11) -> z 56 (-12.5)), F na vychod x -16..-6 (z 60-62, -12.5) a nova
    // koncova mistnost "Svatyne" x -6..-1, z 58.5-63.5 (podlaha -12.5, strop -9). Zabijeci hranice je az -15.
    const float D_Floor = -11f, D_Ceil = -8f, Deep = -12.5f, DeepCeil = -9f;

    [MenuItem("BrosOverwatch/Mapa/Tajna odbocka - prodlouzit")]
    static void Extend()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Tajny tunel] Nejdriv vypni hru."); return; }
        var rootGo = GameObject.Find("Map-Domasov/Tunel/" + RootName);
        if (rootGo == null) { Debug.LogWarning("[Tajny tunel] Nejdriv postav tajnou odbocku."); return; }
        if (rootGo.transform.Find("D_Podlaha") != null) { Debug.LogWarning("[Tajny tunel] Prodlouzeni uz existuje - nic nemenim.", rootGo); return; }
        var westWall = rootGo.transform.Find("Mistnost_Zed_W");
        if (westWall == null) { Debug.LogWarning("[Tajny tunel] Nenasel jsem Mistnost_Zed_W (upravena rucne?) - nic nemenim.", rootGo); return; }

        var any = rootGo.transform.Find("A_Podlaha");
        concrete = any != null ? any.GetComponent<Renderer>().sharedMaterial : null;
        if (concrete == null) { Debug.LogWarning("[Tajny tunel] Nenasel jsem A_Podlaha - nic nemenim."); return; }
        var ceilingPart = rootGo.transform.Find("A_Strop_E");
        concreteDark = ceilingPart != null ? ceilingPart.GetComponent<Renderer>().sharedMaterial : concrete;
        lamp = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Tunnel/Tunnel_Lamp.mat") ?? concreteDark;

        Undo.SetCurrentGroupName("Prodlouzeni tajne odbocky");
        int group = Undo.GetCurrentGroup();
        root = rootGo.transform;

        // pruchod v zapadni stene puvodni mistnosti (z 42-44)
        Undo.RecordObject(westWall, "Prodlouzeni");
        westWall.position = new Vector3(-0.15f, (D_Floor + D_Ceil) * 0.5f, 41.1f);
        westWall.localScale = new Vector3(0.3f, D_Ceil - D_Floor, 1.8f);
        Box("Mistnost_Zed_W_2", -0.3f, 0f, D_Floor, D_Ceil, 44.0f, 45.8f, concrete);

        // D
        Box("D_Podlaha", -18.3f, 0f, D_Floor - T, D_Floor, 41.7f, 44.3f, concrete);
        Box("D_Strop", -18.3f, 0f, D_Ceil, D_Ceil + T, 41.7f, 44.3f, concreteDark);
        Box("D_Zed_S", -18.3f, -0.3f, D_Floor, D_Ceil, 41.7f, 42.0f, concrete);
        Box("D_Zed_N", -16.0f, -0.3f, D_Floor, D_Ceil, 44.0f, 44.3f, concrete);

        // E
        Box("E_Podlaha", -18.3f, -15.7f, D_Floor - T, D_Floor, 44.0f, 48.1f, concrete);
        Ramp("E_Rampa", -18.3f, -15.7f, 48.0f, D_Floor, 56.0f, Deep);
        Box("E_Podlaha_Dole", -18.3f, -15.7f, Deep - T, Deep, 55.9f, 62.3f, concrete);
        Box("E_Strop", -18.3f, -15.7f, D_Ceil, D_Ceil + T, 44.0f, 62.3f, concreteDark);
        Box("E_Zed_W", -18.3f, -18.0f, Deep - T, D_Ceil, 41.7f, 62.3f, concrete);
        Box("E_Zed_E", -16.0f, -15.7f, Deep - T, D_Ceil, 44.3f, 60.0f, concrete);
        Box("E_Zed_E_NadF", -16.0f, -15.7f, DeepCeil, D_Ceil, 60.0f, 62.0f, concrete);
        Box("E_Zed_N", -18.3f, -15.7f, Deep - T, D_Ceil, 62.0f, 62.3f, concrete);

        // F
        Box("F_Podlaha", -16.0f, -5.7f, Deep - T, Deep, 59.7f, 62.3f, concrete);
        Box("F_Strop", -16.0f, -5.7f, DeepCeil, DeepCeil + T, 59.7f, 62.3f, concreteDark);
        Box("F_Zed_S", -15.7f, -6.3f, Deep, DeepCeil, 59.7f, 60.0f, concrete);
        Box("F_Zed_N", -15.7f, -6.3f, Deep, DeepCeil, 62.0f, 62.3f, concrete);

        // koncova mistnost
        Box("Svatyne_Podlaha", -6.3f, -0.7f, Deep - T, Deep, 58.2f, 63.8f, concrete);
        Box("Svatyne_Strop", -6.3f, -0.7f, DeepCeil, DeepCeil + T, 58.2f, 63.8f, concreteDark);
        Box("Svatyne_Zed_E", -1.0f, -0.7f, Deep, DeepCeil, 58.2f, 63.8f, concrete);
        Box("Svatyne_Zed_S", -6.0f, -1.0f, Deep, DeepCeil, 58.2f, 58.5f, concrete);
        Box("Svatyne_Zed_N", -6.0f, -1.0f, Deep, DeepCeil, 63.5f, 63.8f, concrete);
        Box("Svatyne_Zed_W_1", -6.3f, -6.0f, Deep, DeepCeil, 58.2f, 60.0f, concrete);
        Box("Svatyne_Zed_W_2", -6.3f, -6.0f, Deep, DeepCeil, 62.0f, 63.8f, concrete);

        Lamp("Svetlo_D", new Vector3(-12.0f, D_Ceil, 43.0f), 1.0f, 7f);
        Lamp("Svetlo_E", new Vector3(-17.0f, D_Ceil, 46.5f), 1.0f, 8f);
        Lamp("Svetlo_E_Dole", new Vector3(-17.0f, D_Ceil, 59.0f), 1.0f, 8f);
        Lamp("Svetlo_F", new Vector3(-14.0f, DeepCeil, 61.0f), 0.9f, 6f);
        Lamp("Svetlo_Svatyne", new Vector3(-3.5f, DeepCeil, 61.0f), 1.6f, 6f);

        // misto pro buff az na novem konci
        var spot = root.Find("MistoProBuff");
        if (spot != null)
        {
            Undo.RecordObject(spot, "Prodlouzeni");
            spot.position = new Vector3(-2.0f, Deep, 61.0f);
        }

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(rootGo.scene);
        Debug.Log("[Tajny tunel] Prodlouzeno o ~55 m (D, E s rampou, F, Svatyne). Pak pust Tajna odbocka - jeskyne. Ulozit: Ctrl+S.", rootGo);
    }

    // ---------------- vstup bazenem (misto poklopu) ----------------
    // Poklop v hlavni chodbe se zaslepi (podlaha jako predtim). Do jeskyne se jde bazenem u chaty: kdo je na hladine
    // (skokem nebo po novem zebriku zvenku), propadne se dolu do chodby A (SecretPassage). Ven: zebrik v sachte -
    // nahore to hrace vynese vedle bazenu.

    [MenuItem("BrosOverwatch/Mapa/Tajna odbocka - vstup bazenem")]
    static void PoolEntrance()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Tajny tunel] Nejdriv vypni hru."); return; }
        // hledat podle jmena (i neaktivni mapa / jina cesta v hierarchii)
        GameObject rootGo = null, pool = null;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t.gameObject.scene.name == null || EditorUtility.IsPersistent(t)) continue;
            if (t.name == RootName && rootGo == null) rootGo = t.gameObject;
            if (t.name == "Bazen" && t.Find("Stena") != null && pool == null) pool = t.gameObject;
        }
        if (rootGo == null) { Debug.LogWarning("[Tajny tunel] Nenasel jsem " + RootName + " - nejdriv postav tajnou odbocku (Tajna odbocka v tunelu)."); return; }
        if (pool == null) { Debug.LogWarning("[Tajny tunel] Nenasel jsem bazen (objekt Bazen s ditetem Stena)."); return; }
        if (rootGo.GetComponent<SecretPassage>() != null) { Debug.LogWarning("[Tajny tunel] Vstup bazenem uz existuje - nic nemenim.", rootGo); return; }
        var wall = pool.transform.Find("Stena");
        var water = pool.transform.Find("Voda");
        if (wall == null || wall.GetComponent<Collider>() == null) { Debug.LogWarning("[Tajny tunel] Bazen nema Stena s kolizi.", pool); return; }

        Undo.SetCurrentGroupName("Vstup bazenem");
        int group = Undo.GetCurrentGroup();

        // 1) poklop pryc, dira v podlaze hlavni chodby zaslepena
        var hatch = rootGo.transform.Find("Poklop");
        if (hatch != null) Undo.DestroyObjectImmediate(hatch.gameObject);
        var stripT = rootGo.transform.parent != null ? rootGo.transform.parent.Find("Corridors/Floor_1_u_poklopu") : null;
        var strip = stripT != null ? stripT.gameObject : null;
        if (strip != null)
        {
            var fill = Object.Instantiate(strip, strip.transform.parent);
            Undo.RegisterCreatedObjectUndo(fill, "Vstup bazenem");
            fill.name = "Floor_1_zaslepeni";
            Bounds sb = strip.GetComponent<Collider>().bounds;
            Place(fill.transform, HoleX0, HoleX1, sb.min.y, sb.max.y, HoleZ0, HoleZ1);
        }
        else Debug.LogWarning("[Tajny tunel] Nenasel jsem Floor_1_u_poklopu - diru v chodbe zaslep rucne (x 0.2-1.5, z 21.8-23.2).");

        // 2) bazen: stred, vrsek, polomery
        Bounds wb = wall.GetComponent<Collider>().bounds;
        Vector3 center = new Vector3(wb.center.x, 0f, wb.center.z);
        float top = wb.max.y, outer = wb.extents.x;
        float inner = water != null ? water.GetComponent<Renderer>().bounds.extents.x : outer - 0.2f;
        float ground = wb.min.y;
        Vector3 dir = Vector3.back;   // zebrik a vychod na jizni strane bazenu

        // zebrik zvenku na okraj (chyti jen toho, kdo se diva k bazenu a jde dopredu)
        var ladderGo = new GameObject("Zebrik");
        Undo.RegisterCreatedObjectUndo(ladderGo, "Vstup bazenem");
        ladderGo.transform.SetParent(pool.transform, true);
        float ladderTop = top + 0.9f;
        ladderGo.transform.SetPositionAndRotation(center + dir * (outer + 0.35f) + Vector3.up * (ground + ladderTop) * 0.5f,
            Quaternion.LookRotation(-dir));
        ladderGo.transform.localScale = new Vector3(1f / pool.transform.lossyScale.x, 1f / pool.transform.lossyScale.y, 1f / pool.transform.lossyScale.z);
        var ladder = ladderGo.AddComponent<Ladder>();
        ladder.size = new Vector3(1.0f, ladderTop - ground, 0.9f);
        ladder.grabFacing = -dir;
        var ironRenderer = pool.transform.Find("Oblouk");
        var iron = ironRenderer != null ? ironRenderer.GetComponent<Renderer>().sharedMaterial : null;
        if (iron == null) iron = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Tunnel/Tunnel_Rail.mat");
        var keep = root;
        root = ladderGo.transform;
        Vector3 basePos = center + dir * (outer + 0.12f);
        Vector3 sideDir = Vector3.Cross(Vector3.up, dir);
        foreach (float s in new[] { -0.3f, 0.3f })
        {
            Vector3 p = basePos + sideDir * s;
            var rail = Box("Madlo", p.x - 0.03f, p.x + 0.03f, ground, top + 0.9f, p.z - 0.03f, p.z + 0.03f, iron, false);
            rail.transform.SetParent(ladderGo.transform, true);
        }
        for (float y = ground + 0.3f; y < top + 0.1f; y += 0.35f)
        {
            Vector3 a = basePos - sideDir * 0.3f, b = basePos + sideDir * 0.3f;
            Box("Pricka", Mathf.Min(a.x, b.x) - 0.02f, Mathf.Max(a.x, b.x) + 0.02f, y, y + 0.04f, Mathf.Min(a.z, b.z) - 0.02f, Mathf.Max(a.z, b.z) + 0.02f, iron, false)
                .transform.SetParent(ladderGo.transform, true);
        }
        root = keep;

        // 3) prechod: bazen -> jeskyne (chodba A), sachta nahore -> vedle bazenu
        var passage = Undo.AddComponent<SecretPassage>(rootGo);
        passage.poolCenter = center;
        passage.poolRadius = inner - 0.1f;
        passage.poolTop = top;
        passage.arrival = new Vector3(3.0f, A_Floor + 0.05f, 22.8f);
        passage.arrivalYaw = 90f;
        // jen kdo vyleze po zebriku az pod strop sachty (skokem z chodby A se tam nedostane - vyskok 1,2 m)
        passage.exitMin = new Vector3(HoleX0, -6.6f, HoleZ0);
        passage.exitMax = new Vector3(HoleX1, TopFloor, HoleZ1);
        passage.exitPoint = center + dir * (outer + 1.6f) + Vector3.up * (ground + 0.05f);
        passage.exitYaw = Quaternion.LookRotation(dir).eulerAngles.y;

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(rootGo.scene);
        Selection.activeGameObject = ladderGo;
        Debug.Log($"[Tajny tunel] Vstup bazenem hotov: poklop zaslepen, zebrik k bazenu na jizni strane, vychod vedle nej. Ulozit: Ctrl+S.", rootGo);
    }

    // ---------------- dira v podlaze hlavni chodby ----------------

    // Floor_1 (jeden dlouhy kvadr) se rozdeli na kus pred dirou, za ni a pruh vedle ni. Barva bez textury - spoje nejsou videt.
    static bool CutHole(GameObject floor)
    {
        Bounds b = floor.GetComponent<Collider>().bounds;
        if (b.min.z > HoleZ0 || b.max.z < HoleZ1 || b.min.x > HoleX0 || b.max.x < HoleX1)
        {
            Debug.LogWarning("[Tajny tunel] Floor_1 uz nepokryva misto poklopu (upravena rucne?) - nic nemenim.", floor);
            return false;
        }

        float y0 = b.min.y, y1 = b.max.y;
        var north = Object.Instantiate(floor, floor.transform.parent);
        Undo.RegisterCreatedObjectUndo(north, "Tajna odbocka");
        north.name = "Floor_1_sever";
        Place(north.transform, b.min.x, b.max.x, y0, y1, HoleZ1, b.max.z);

        var strip = Object.Instantiate(floor, floor.transform.parent);
        Undo.RegisterCreatedObjectUndo(strip, "Tajna odbocka");
        strip.name = "Floor_1_u_poklopu";
        Place(strip.transform, b.min.x, HoleX0, y0, y1, HoleZ0, HoleZ1);
        if (b.max.x > HoleX1 + 0.01f)
        {
            var strip2 = Object.Instantiate(floor, floor.transform.parent);
            Undo.RegisterCreatedObjectUndo(strip2, "Tajna odbocka");
            strip2.name = "Floor_1_u_poklopu_2";
            Place(strip2.transform, HoleX1, b.max.x, y0, y1, HoleZ0, HoleZ1);
        }

        Undo.RecordObject(floor.transform, "Tajna odbocka");
        Place(floor.transform, b.min.x, b.max.x, y0, y1, b.min.z, HoleZ0);
        return true;
    }

    // Osove zarovnany kvadr (kolize = velikost 1, meritko = rozmery) do zadanych svetovych mezi.
    static void Place(Transform t, float x0, float x1, float y0, float y1, float z0, float z1)
    {
        t.position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        Vector3 parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3((x1 - x0) / parentScale.x, (y1 - y0) / parentScale.y, (z1 - z0) / parentScale.z);
    }

    // ---------------- stavebni dily ----------------

    static GameObject Box(string name, float x0, float x1, float y0, float y1, float z0, float z1, Material material, bool solid = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(go, "Tajna odbocka");
        go.name = name;
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        go.transform.localScale = new Vector3(x1 - x0, y1 - y0, z1 - z0);
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!solid) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.isStatic = solid;
        return go;
    }

    // ---------------- poklop ----------------

    // Kryt vypada jako servisni poklop (tmavsi deska v kovovem ramu), ale nema kolizi - kdo na nej slapne, propadne.
    static void BuildHatch()
    {
        var hatch = new GameObject("Poklop");
        Undo.RegisterCreatedObjectUndo(hatch, "Tajna odbocka");
        hatch.transform.SetParent(root, false);
        var keep = root;
        root = hatch.transform;
        float y = TopFloor;
        Box("Deska", HoleX0 + 0.04f, HoleX1 - 0.04f, y - 0.02f, y + 0.005f, HoleZ0 + 0.04f, HoleZ1 - 0.04f, concreteDark, false);
        Box("Ram_Z", HoleX0, HoleX1, y - 0.02f, y + 0.012f, HoleZ0, HoleZ0 + 0.05f, rail, false);
        Box("Ram_S", HoleX0, HoleX1, y - 0.02f, y + 0.012f, HoleZ1 - 0.05f, HoleZ1, rail, false);
        Box("Ram_W", HoleX0, HoleX0 + 0.05f, y - 0.02f, y + 0.012f, HoleZ0, HoleZ1, rail, false);
        Box("Ram_E", HoleX1 - 0.05f, HoleX1, y - 0.02f, y + 0.012f, HoleZ0, HoleZ1, rail, false);
        Box("Pant_1", HoleX0 + 0.05f, HoleX0 + 0.12f, y, y + 0.02f, HoleZ0 + 0.25f, HoleZ0 + 0.45f, rail, false);
        Box("Pant_2", HoleX0 + 0.05f, HoleX0 + 0.12f, y, y + 0.02f, HoleZ1 - 0.45f, HoleZ1 - 0.25f, rail, false);
        root = keep;
    }

    // ---------------- sachta pod poklopem ----------------

    const float A_Floor = -8.0f, A_Ceil = -5.3f;   // chodba A (dole pod poklopem)

    static void BuildShaft()
    {
        // mezi spodkem podlahy hlavni chodby (-4.3) a stropem chodby A (-5.0): steny kolem diry
        float y0 = A_Ceil, y1 = TopFloor - T;
        Box("Sachta_W", HoleX0 - T, HoleX0, y0, y1, HoleZ0 - T, HoleZ1 + T, concrete);
        Box("Sachta_E", HoleX1, HoleX1 + T, y0, y1, HoleZ0 - T, HoleZ1 + T, concrete);
        Box("Sachta_S", HoleX0, HoleX1, y0, y1, HoleZ0 - T, HoleZ0, concrete);
        Box("Sachta_N", HoleX0, HoleX1, y0, y1, HoleZ1, HoleZ1 + T, concrete);
    }

    // ---------------- chodby ----------------
    // A: na vychod x 0.2-16, z 21.8-23.8 (podlaha -8)
    // B: na sever x 14-16, z 21.8-44; rampa z 26 (-8) -> z 36 (-11); strop porad -5.3 (smerem dolu vyssi sal)
    // C: na zapad x 4-14, z 42-44 (podlaha -11, strop -8)
    // mistnost: x 0-4, z 40.5-45.5 (podlaha -11, strop -8)

    const float Low = -11.0f, LowCeil = -8.0f;

    static void BuildTunnel()
    {
        // A
        Box("A_Podlaha", -0.1f, 16.3f, A_Floor - T, A_Floor, 21.5f, 24.1f, concrete);
        Box("A_Strop_W", -0.1f, HoleX0, A_Ceil, A_Ceil + T, 21.5f, 24.1f, concreteDark);
        Box("A_Strop_S", HoleX0, HoleX1, A_Ceil, A_Ceil + T, 21.5f, HoleZ0, concreteDark);
        Box("A_Strop_N", HoleX0, HoleX1, A_Ceil, A_Ceil + T, HoleZ1, 24.1f, concreteDark);
        Box("A_Strop_E", HoleX1, 16.3f, A_Ceil, A_Ceil + T, 21.5f, 24.1f, concreteDark);
        Box("A_Zed_Konec", -0.1f, 0.2f, A_Floor, A_Ceil, 21.8f, 23.8f, concrete);
        Box("A_Zed_S", -0.1f, 16.3f, Low - T, A_Ceil, 21.5f, 21.8f, concrete);
        Box("A_Zed_N", -0.1f, 14.0f, A_Floor, A_Ceil, 23.8f, 24.1f, concrete);

        // B
        Box("B_Podlaha", 13.7f, 16.3f, A_Floor - T, A_Floor, 23.8f, 26.1f, concrete);
        Ramp("B_Rampa", 13.7f, 16.3f, 26.0f, A_Floor, 36.0f, Low);
        Box("B_Podlaha_Dole", 13.7f, 16.3f, Low - T, Low, 35.9f, 44.3f, concrete);
        Box("B_Strop", 13.7f, 16.3f, A_Ceil, A_Ceil + T, 23.8f, 44.3f, concreteDark);
        Box("B_Zed_E", 16.0f, 16.3f, Low - T, A_Ceil, 21.5f, 44.3f, concrete);
        Box("B_Zed_W", 13.7f, 14.0f, Low - T, A_Ceil, 24.1f, 42.0f, concrete);
        Box("B_Zed_W_NadC", 13.7f, 14.0f, LowCeil, A_Ceil, 42.0f, 44.0f, concrete);
        Box("B_Zed_N", 13.7f, 16.3f, Low - T, A_Ceil, 44.0f, 44.3f, concrete);

        // C
        Box("C_Podlaha", 3.7f, 14.0f, Low - T, Low, 41.7f, 44.3f, concrete);
        Box("C_Strop", 3.7f, 14.0f, LowCeil, LowCeil + T, 41.7f, 44.3f, concreteDark);
        Box("C_Zed_S", 4.3f, 13.7f, Low, LowCeil, 41.7f, 42.0f, concrete);
        Box("C_Zed_N", 4.3f, 13.7f, Low, LowCeil, 44.0f, 44.3f, concrete);

        // mistnost na konci
        Box("Mistnost_Podlaha", -0.3f, 4.3f, Low - T, Low, 40.2f, 45.8f, concrete);
        Box("Mistnost_Strop", -0.3f, 4.3f, LowCeil, LowCeil + T, 40.2f, 45.8f, concreteDark);
        Box("Mistnost_Zed_W", -0.3f, 0.0f, Low, LowCeil, 40.2f, 45.8f, concrete);
        Box("Mistnost_Zed_S", 0.0f, 4.3f, Low, LowCeil, 40.2f, 40.5f, concrete);
        Box("Mistnost_Zed_N", 0.0f, 4.3f, Low, LowCeil, 45.5f, 45.8f, concrete);
        Box("Mistnost_Zed_E_1", 4.0f, 4.3f, Low, LowCeil, 40.5f, 42.0f, concrete);
        Box("Mistnost_Zed_E_2", 4.0f, 4.3f, Low, LowCeil, 44.0f, 45.5f, concrete);
        var spot = new GameObject("MistoProBuff");
        Undo.RegisterCreatedObjectUndo(spot, "Tajna odbocka");
        spot.transform.SetParent(root, false);
        spot.transform.position = new Vector3(1.6f, Low, 43.0f);
    }

    // Sikma deska: horni plocha od (zFrom, yFrom) do (zTo, yTo), sirka x0-x1.
    static void Ramp(string name, float x0, float x1, float zFrom, float yFrom, float zTo, float yTo)
    {
        float dz = zTo - zFrom, dy = yTo - yFrom;
        float length = Mathf.Sqrt(dz * dz + dy * dy) + 0.3f;
        float angle = Mathf.Atan2(-dy, dz) * Mathf.Rad2Deg;   // klesa smerem k +z
        var go = Box(name, x0, x1, 0f, T, 0f, length, concrete);
        var rotation = Quaternion.Euler(angle, 0f, 0f);
        Vector3 topMid = new Vector3((x0 + x1) * 0.5f, (yFrom + yTo) * 0.5f, (zFrom + zTo) * 0.5f);
        go.transform.SetPositionAndRotation(topMid - rotation * Vector3.up * (T * 0.5f), rotation);
    }

    // ---------------- zebrik zpet nahoru ----------------

    // U zapadni steny sachty (poklop je vedle): oblast lezeni od podlahy chodby A az nad podlahu hlavni chodby.
    static void BuildLadder()
    {
        var go = new GameObject("Zebrik");
        Undo.RegisterCreatedObjectUndo(go, "Tajna odbocka");
        go.transform.SetParent(root, false);
        // oblast 1 m od steny (hrac ma polomer 0,5) a az nad podlahu chodby; chyti jen toho, kdo se diva ke stene
        // a jde dopredu - kdo poklopem propadne, spadne az dolu
        float x = HoleX0 + 0.5f, zMid = (HoleZ0 + HoleZ1) * 0.5f;
        float bottom = A_Floor, top = TopFloor + 1.0f;
        go.transform.position = new Vector3(x, (bottom + top) * 0.5f, zMid);
        var ladder = go.AddComponent<Ladder>();
        ladder.size = new Vector3(1.0f, top - bottom, 1.2f);
        ladder.grabFacing = Vector3.left;

        // vzhled: dve madla a pricky na zapadni stene sachty / chodby A (bez kolize - leze se oblasti)
        var keep = root;
        root = go.transform;
        float wx = HoleX0 + 0.02f;
        Box("Madlo_1", wx, wx + 0.05f, bottom, TopFloor - 0.05f, zMid - 0.3f, zMid - 0.25f, rail, false);
        Box("Madlo_2", wx, wx + 0.05f, bottom, TopFloor - 0.05f, zMid + 0.25f, zMid + 0.3f, rail, false);
        for (float y = bottom + 0.3f; y < TopFloor - 0.1f; y += 0.35f)
            Box("Pricka", wx, wx + 0.12f, y, y + 0.04f, zMid - 0.25f, zMid + 0.25f, rail, false);
        root = keep;
    }

    // ---------------- svetla ----------------

    // Sporo a do zluta (ne jako hlavni tunel), at je to "zapomenuta" chodba.
    static void BuildLights()
    {
        Lamp("Svetlo_A", new Vector3(8.0f, A_Ceil, 22.8f), 1.3f, 7f);
        Lamp("Svetlo_B_Rampa", new Vector3(15.0f, A_Ceil, 30.0f), 1.1f, 8f);
        Lamp("Svetlo_B_Dole", new Vector3(15.0f, A_Ceil, 40.5f), 1.0f, 8f);
        Lamp("Svetlo_C", new Vector3(9.0f, LowCeil, 43.0f), 0.9f, 6f);
        Lamp("Svetlo_Mistnost", new Vector3(2.0f, LowCeil, 43.0f), 1.6f, 6f);
    }

    static void Lamp(string name, Vector3 ceiling, float intensity, float range)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Tajna odbocka");
        go.transform.SetParent(root, false);
        go.transform.position = ceiling + Vector3.down * 0.35f;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.78f, 0.5f);
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;

        var keep = root;
        root = go.transform;
        Box("Lampa", ceiling.x - 0.2f, ceiling.x + 0.2f, ceiling.y - 0.08f, ceiling.y, ceiling.z - 0.2f, ceiling.z + 0.2f, lamp, false);
        root = keep;
    }
}
