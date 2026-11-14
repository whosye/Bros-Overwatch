using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Dekorace vkladane rucne (jako FurnitureTools): v okne Scene se podivej na misto (stred okna) a v menu
// BrosOverwatch > Dekorace vyber, co tam ma byt.
//  - Obrazy: fotky z Assets/Materials/Obrazy (nebo obrazek vybrany v okne Project) v drevenem ramu na zdi.
//  - Dreveny jeseter: vyrezavany model ryby na podstavci (na podlahu, stul, policku).
//  - Klokani kuze: na zed (pověsí se) nebo na podlahu (koberec).
// Vse jde vratit pres Ctrl+Z; pak ulozit scenu (Ctrl+S).
public static class DecorTools
{
    const string PictureFolder = "Assets/Materials/Obrazy/";
    const string DecorFolder = "Assets/Materials/Dekorace/";
    const float PictureHeight = 0.8f;

    // ---------------- obrazy ----------------

    [MenuItem("BrosOverwatch/Dekorace/Obraz - Parta")]
    static void PictureParty() => PlacePicture(PictureFolder + "Obraz_Parta.jpg");

    [MenuItem("BrosOverwatch/Dekorace/Obraz - Bryle")]
    static void PictureGoggles() => PlacePicture(PictureFolder + "Obraz_Bryle.jpg");

    [MenuItem("BrosOverwatch/Dekorace/Obraz - Doktor z hor")]
    static void PictureDoctor() => PlacePicture(PictureFolder + "Obraz_DoktorZHor.jpg");

    [MenuItem("BrosOverwatch/Dekorace/Obraz - Tomasek glory")]
    static void PictureTomasek() => PlacePicture(PictureFolder + "Obraz_TomasekGlory.jpg");

    [MenuItem("BrosOverwatch/Dekorace/Obraz - Krozcek")]
    static void PictureKrozcek() => PlacePicture(PictureFolder + "Obraz_Krozcek.jpg");

    [MenuItem("BrosOverwatch/Dekorace/Obraz - Spanek na gauci")]
    static void PictureCouch() => PlacePicture(PictureFolder + "Obraz_Gauc.jpg");

    [MenuItem("BrosOverwatch/Dekorace/Obraz - Lasagne")]
    static void PictureLasagne() => PlacePicture(PictureFolder + "Obraz_Lasagne.jpg");

    // Gauc se spacim kamaradem (jako na fotce Obraz_Gauc): opreny o zed primo pod obrazem "Spanek na gauci".
    // Najde obraz Obraz_Gauc ve scene; kdyz tam neni, miri stred okna Scene na zed.
    [MenuItem("BrosOverwatch/Dekorace/Gauc se spacim (pod obraz Spanek na gauci)")]
    static void CouchWithSleeper()
    {
        Vector3 wallPoint, into;
        var picture = GameObject.Find("Obraz_Gauc");
        if (picture != null)
        {
            var t = picture.transform;
            into = new Vector3(t.forward.x, 0f, t.forward.z).normalized;
            bool standing = t.Find("Obraz") != null;
            wallPoint = standing ? t.position + Vector3.up * 1.15f + into * 0.06f : t.position + into * 0.025f;
        }
        else
        {
            if (!Aim(out var hit, out _)) return;
            if (Mathf.Abs(hit.normal.y) > 0.5f)
            {
                Debug.LogWarning("[Dekorace] Obraz Obraz_Gauc ve scene neni - stred okna Scene musi mirit na zed.");
                return;
            }
            into = -new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            wallPoint = hit.point;
        }
        Physics.SyncTransforms();
        if (!Physics.Raycast(wallPoint - into * 0.6f, Vector3.down, out var floor, 6f, ~0, QueryTriggerInteraction.Ignore))
        {
            Debug.LogWarning("[Dekorace] Pod obrazem nenalezena podlaha.");
            return;
        }
        var root = NewRoot("GaucSeSpacim", new Vector3(wallPoint.x, floor.point.y, wallPoint.z), Quaternion.LookRotation(into, Vector3.up));
        BuildCouch(root);
        Done(root);
    }

    // Lokalne: +z do zdi (operadlo u zdi), sedi se smerem -z, gauc podel osy x. Spaci lezi na brise, hlava na +x.
    static void BuildCouch(Transform root)
    {
        var fabric = MapBuildKit.Mat(DecorFolder + "GaucLatka.mat", null, new Color(0.74f, 0.67f, 0.57f), Vector2.one, 0.05f);
        var pillow = MapBuildKit.Mat(DecorFolder + "GaucPolstar.mat", null, new Color(0.62f, 0.55f, 0.44f), Vector2.one, 0.05f);
        var sweater = MapBuildKit.Mat(DecorFolder + "SpaciSvetr.mat", null, new Color(0.12f, 0.42f, 0.66f), Vector2.one, 0.1f);
        var jeans = MapBuildKit.Mat(DecorFolder + "SpaciDziny.mat", null, new Color(0.13f, 0.18f, 0.3f), Vector2.one, 0.15f);
        var black = MapBuildKit.Mat(DecorFolder + "SpaciCerna.mat", null, new Color(0.06f, 0.06f, 0.07f), Vector2.one, 0.2f);
        var skin = MapBuildKit.Mat(DecorFolder + "SpaciKuze.mat", null, new Color(0.86f, 0.66f, 0.55f), Vector2.one, 0.25f);
        var belt = MapBuildKit.Mat(DecorFolder + "SpaciPasek.mat", null, new Color(0.3f, 0.18f, 0.1f), Vector2.one, 0.4f);

        const float length = 2.3f, depth = 0.92f, front = -0.06f - depth, mid = -0.06f - depth * 0.5f;
        // sedacka (vse s kolizi = presne podle vzhledu)
        Part(root, "Spodek", new Vector3(0f, 0.19f, mid), new Vector3(length, 0.3f, depth), fabric, true);
        const int seats = 3;
        float seatWidth = length / seats;
        for (int i = 0; i < seats; i++)
        {
            float x = -length * 0.5f + seatWidth * (i + 0.5f);
            Part(root, "Sedak", new Vector3(x, 0.42f, front + 0.38f), new Vector3(seatWidth - 0.03f, 0.16f, 0.72f), fabric, true);
            Part(root, "Operadlo", new Vector3(x, 0.72f, -0.18f), new Vector3(seatWidth - 0.03f, 0.6f, 0.24f), fabric, true);
        }
        foreach (float side in new[] { -1f, 1f })
            Part(root, "Podrucka", new Vector3(side * (length * 0.5f + 0.11f), 0.34f, mid), new Vector3(0.22f, 0.6f, depth), fabric, true);
        Part(root, "Polstar", new Vector3(length * 0.5f - 0.25f, 0.62f, -0.36f), new Vector3(0.42f, 0.32f, 0.14f), pillow, false);

        // spaci na brise, hlava u prave podrucky zabořena do ruky
        const float top = 0.5f, z = front + 0.4f;
        foreach (float dz in new[] { -0.09f, 0.09f })
        {
            Part(root, "Noha", new Vector3(-0.5f, top + 0.08f, z + dz), new Vector3(0.92f, 0.15f, 0.15f), jeans, true);
            Part(root, "Ponozka", new Vector3(-1.0f, top + 0.08f, z + dz), new Vector3(0.1f, 0.13f, 0.12f), black, false);
        }
        Part(root, "Zadek", new Vector3(-0.02f, top + 0.11f, z), new Vector3(0.22f, 0.2f, 0.36f), jeans, true);
        Part(root, "Pasek", new Vector3(0.1f, top + 0.12f, z), new Vector3(0.05f, 0.2f, 0.37f), belt, false);
        Part(root, "Trup", new Vector3(0.4f, top + 0.12f, z), new Vector3(0.6f, 0.22f, 0.4f), sweater, true);
        Part(root, "Ruce", new Vector3(0.78f, top + 0.08f, z), new Vector3(0.2f, 0.14f, 0.5f), sweater, true);
        foreach (float dz in new[] { -0.22f, 0.22f })
            Part(root, "Dlan", new Vector3(0.8f, top + 0.08f, z + dz + Mathf.Sign(dz) * 0.04f), new Vector3(0.12f, 0.09f, 0.09f), skin, false);

        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(head.GetComponent<Collider>());
        head.name = "Hlava";
        head.transform.SetParent(root, false);
        head.transform.localPosition = new Vector3(0.82f, top + 0.24f, z - 0.02f);
        head.transform.localScale = new Vector3(0.22f, 0.21f, 0.2f);
        head.GetComponent<MeshRenderer>().sharedMaterial = skin;
        GameObjectUtility.SetStaticEditorFlags(head, 0);
        // cerna ksiltovka nasazena dozadu
        var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(cap.GetComponent<Collider>());
        cap.name = "Ksiltovka";
        cap.transform.SetParent(root, false);
        cap.transform.localPosition = new Vector3(0.84f, top + 0.29f, z - 0.02f);
        cap.transform.localScale = new Vector3(0.23f, 0.15f, 0.21f);
        cap.GetComponent<MeshRenderer>().sharedMaterial = black;
        GameObjectUtility.SetStaticEditorFlags(cap, 0);
        Part(root, "Ksilt", new Vector3(0.72f, top + 0.28f, z - 0.02f), new Vector3(0.12f, 0.02f, 0.16f), black, false);
    }

    // Drevena lavice z vybraneho kvadru (napr. zidka pred barem): puvodni kvadr si necha kolizi, jen se skryje
    // jeho vzhled a misto nej se postavi drevena lavice stejneho rozmeru - prkna sedaku nahore (lehce presahuji),
    // tmavsi korpus a obklad z vodorovnych prken na bocich. Predek = strana k oknu Scene (obklad je ale dokola).
    [MenuItem("BrosOverwatch/Dekorace/Lavice z vybraneho kvadru (vyber kvadr v Hierarchy)")]
    static void BenchFromSelected()
    {
        var target = Selection.activeGameObject;
        var filter = target != null ? target.GetComponent<MeshFilter>() : null;
        var renderer = target != null ? target.GetComponent<MeshRenderer>() : null;
        if (filter == null || filter.sharedMesh == null || renderer == null)
        {
            Debug.LogWarning("[Dekorace] Nejdriv v Hierarchy vyber kvadr (objekt s MeshRendererem), ze ktereho ma byt lavice.");
            return;
        }
        var t = target.transform;
        Bounds b = filter.sharedMesh.bounds;
        Vector3 size = Vector3.Scale(b.size, t.lossyScale);
        size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        Vector3 center = t.TransformPoint(b.center);
        Vector3 axis = size.x >= size.z ? t.right : t.forward;
        axis.y = 0f;
        axis.Normalize();
        float length = Mathf.Max(size.x, size.z), width = Mathf.Min(size.x, size.z), height = size.y;
        var rotation = Quaternion.LookRotation(Vector3.Cross(axis, Vector3.up), Vector3.up);   // lokalni +x = delka lavice

        Undo.RecordObject(renderer, "Lavice z kvadru");
        renderer.enabled = false;   // kolize puvodniho kvadru zustava

        var root = new GameObject(target.name + "_Lavice").transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Lavice z kvadru");
        root.SetParent(t.parent, false);
        root.SetPositionAndRotation(new Vector3(center.x, center.y - height * 0.5f, center.z), rotation);

        var seat = MapBuildKit.Mat(DecorFolder + "LaviceSedak.mat", "Assets/Materials/Wood/WoodPlanks.png", new Color(0.8f, 0.6f, 0.4f), Vector2.one, 0.3f);
        var body = MapBuildKit.Mat(DecorFolder + "LaviceKorpus.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.5f, 0.34f, 0.21f), Vector2.one, 0.25f);

        const float seatThick = 0.05f, cladThick = 0.025f;
        float bodyHeight = height - seatThick;
        // korpus (uvnitr obkladu)
        Part(root, "Korpus", new Vector3(0f, bodyHeight * 0.5f, 0f), new Vector3(length - cladThick * 2f, bodyHeight, width - cladThick * 2f), body, false);
        // sedak: prkna po delce, mirny presah dopredu i do stran
        int planks = Mathf.Max(2, Mathf.RoundToInt(width / 0.15f));
        float plankWidth = (width + 0.04f) / planks;
        for (int i = 0; i < planks; i++)
            Part(root, "Prkno", new Vector3(0f, height - seatThick * 0.5f, -width * 0.5f - 0.02f + plankWidth * (i + 0.5f)),
                new Vector3(length + 0.04f, seatThick, plankWidth - 0.008f), seat, false);
        // obklad: vodorovna prkna na delsich stranach, svisla na celech
        int rows = Mathf.Max(2, Mathf.RoundToInt(bodyHeight / 0.14f));
        float rowHeight = (bodyHeight - 0.06f) / rows;
        foreach (float side in new[] { -1f, 1f })
        {
            for (int i = 0; i < rows; i++)
                Part(root, "Obklad", new Vector3(0f, 0.06f + rowHeight * (i + 0.5f), side * (width * 0.5f - cladThick * 0.5f)),
                    new Vector3(length, rowHeight - 0.01f, cladThick), seat, false);
            Part(root, "Sokl", new Vector3(0f, 0.03f, side * (width * 0.5f - cladThick - 0.01f)), new Vector3(length - 0.04f, 0.06f, cladThick), body, false);
            Part(root, "Celo", new Vector3(side * (length * 0.5f - cladThick * 0.5f), bodyHeight * 0.5f, 0f), new Vector3(cladThick, bodyHeight, width), seat, false);
        }
        Done(root);
    }

    // ---------------- stul s vycepem, soudek, kavovar; lasagne ----------------

    // Stul (delkou ke kamere bokem) s pivnim vycepem, vedle soudek piva s hadici k vycepu, na druhem konci kavovar.
    // Na stole dva balicky: pivo z vycepu (+15 % poskozeni) a kava (kratsi cooldowny, rychlejsi pohyb).
    [MenuItem("BrosOverwatch/Dekorace/Stul s vycepem, soudkem a kavovarem (mir na podlahu)")]
    static void BarTable()
    {
        if (!Aim(out var hit, out var cam)) return;
        if (hit.normal.y < 0.7f)
        {
            // miri na zed / nabytek: stul se postavi na podlahu pod tim mistem, 0,6 m od zdi
            Vector3 from = hit.point + new Vector3(hit.normal.x, 0f, hit.normal.z).normalized * 0.6f;
            if (!Physics.Raycast(from, Vector3.down, out hit, 10f, ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f)
            {
                Debug.LogWarning("[Dekorace] Pod mistem, kam miri stred okna Scene, neni podlaha - zamir na podlahu.");
                return;
            }
        }
        Vector3 away = hit.point - cam.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
        var root = NewRoot("StulVycep", hit.point, Quaternion.LookRotation(away.normalized, Vector3.up));   // lokalne -z = predek (ke kamere)

        var wood = MapBuildKit.Mat(DecorFolder + "StulVycepDrevo.mat", "Assets/Materials/Wood/WoodPlanks.png", new Color(0.62f, 0.42f, 0.26f), Vector2.one, 0.3f);
        var chrome = MapBuildKit.Mat(DecorFolder + "Chrom.mat", null, new Color(0.82f, 0.84f, 0.87f), Vector2.one, 0.9f);
        var steel = MapBuildKit.Mat(DecorFolder + "SoudekOcel.mat", null, new Color(0.62f, 0.64f, 0.67f), Vector2.one, 0.65f);
        var black = MapBuildKit.Mat(DecorFolder + "CernyPlast.mat", null, new Color(0.07f, 0.07f, 0.08f), Vector2.one, 0.45f);
        var hose = MapBuildKit.Mat(DecorFolder + "Hadice.mat", null, new Color(0.15f, 0.15f, 0.17f), Vector2.one, 0.3f);
        var label = MapBuildKit.Mat(DecorFolder + "VycepZnak.mat", null, new Color(0.85f, 0.15f, 0.1f), Vector2.one, 0.4f);
        var white = MapBuildKit.Mat(DecorFolder + "Porcelan.mat", null, new Color(0.95f, 0.95f, 0.93f), Vector2.one, 0.7f);

        const float L = 1.8f, D = 0.7f, H = 0.92f;
        // stul (kolize = deska + nohy + polička)
        Part(root, "Deska", new Vector3(0f, H - 0.025f, 0f), new Vector3(L, 0.05f, D), wood, true);
        foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                Part(root, "Noha", new Vector3(x * (L * 0.5f - 0.06f), (H - 0.05f) * 0.5f, z * (D * 0.5f - 0.06f)), new Vector3(0.07f, H - 0.05f, 0.07f), wood, true);
        Part(root, "Policka", new Vector3(0f, 0.22f, 0f), new Vector3(L - 0.1f, 0.03f, D - 0.1f), wood, true);
        Part(root, "Luby", new Vector3(0f, H - 0.09f, D * 0.5f - 0.04f), new Vector3(L - 0.12f, 0.08f, 0.02f), wood, false);

        // vycep: podstavec, sloup, hlava se dvema kohouty a drzadly, odkapavaci miska
        const float tapX = 0.3f;
        Part(root, "VycepPodstavec", new Vector3(tapX, H + 0.01f, 0.1f), new Vector3(0.16f, 0.02f, 0.16f), chrome, false);
        Cyl(root, "VycepSloup", new Vector3(tapX, H + 0.23f, 0.1f), 0.09f, 0.42f, chrome, true);
        Part(root, "VycepHlava", new Vector3(tapX, H + 0.45f, 0.06f), new Vector3(0.36f, 0.07f, 0.1f), chrome, true);
        Part(root, "VycepZnak", new Vector3(tapX, H + 0.3f, 0.054f), new Vector3(0.07f, 0.09f, 0.01f), label, false);
        foreach (float dx in new[] { -0.11f, 0.11f })
        {
            Part(root, "Kohout", new Vector3(tapX + dx, H + 0.4f, 0.0f), new Vector3(0.03f, 0.06f, 0.05f), chrome, false);
            Part(root, "Drzadlo", new Vector3(tapX + dx, H + 0.55f, 0.03f), new Vector3(0.025f, 0.14f, 0.025f), black, false);
        }
        Part(root, "OdkapMiska", new Vector3(tapX, H + 0.015f, -0.04f), new Vector3(0.4f, 0.03f, 0.14f), chrome, false);

        // soudek vedle stolu (na podlaze) s hadici k vycepu
        Vector3 keg = new Vector3(L * 0.5f + 0.32f, 0f, 0.05f);
        Cyl(root, "Soudek", keg + new Vector3(0f, 0.3f, 0f), 0.4f, 0.6f, steel, true);
        foreach (float y in new[] { 0.08f, 0.52f })
            Cyl(root, "Obruc", keg + new Vector3(0f, y, 0f), 0.42f, 0.03f, steel, false);
        Cyl(root, "Narazec", keg + new Vector3(0f, 0.63f, 0f), 0.08f, 0.06f, black, false);
        var hosePart = MapBuildKit.Beam(root, "Hadice", root.TransformPoint(keg + new Vector3(0f, 0.66f, 0f)), root.TransformPoint(new Vector3(L * 0.5f - 0.05f, H + 0.04f, 0.15f)), 0.025f, hose);
        GameObjectUtility.SetStaticEditorFlags(hosePart, 0);
        hosePart = MapBuildKit.Beam(root, "Hadice", root.TransformPoint(new Vector3(L * 0.5f - 0.05f, H + 0.04f, 0.15f)), root.TransformPoint(new Vector3(tapX + 0.05f, H + 0.04f, 0.15f)), 0.025f, hose);
        GameObjectUtility.SetStaticEditorFlags(hosePart, 0);

        // kavovar: cerne telo, chromova hlava s pakou, odkapavac, salek; vedle mlynek
        const float cofX = -0.55f;
        Part(root, "Kavovar", new Vector3(cofX, H + 0.2f, 0.12f), new Vector3(0.32f, 0.4f, 0.3f), black, true);
        Part(root, "KavovarVrch", new Vector3(cofX, H + 0.41f, 0.12f), new Vector3(0.33f, 0.02f, 0.31f), chrome, false);
        Part(root, "KavovarPanel", new Vector3(cofX, H + 0.32f, -0.031f), new Vector3(0.26f, 0.1f, 0.01f), chrome, false);
        Cyl(root, "Paka", new Vector3(cofX, H + 0.22f, -0.06f), 0.07f, 0.04f, chrome, false);
        Part(root, "PakaRukojet", new Vector3(cofX + 0.08f, H + 0.22f, -0.06f), new Vector3(0.12f, 0.025f, 0.025f), black, false);
        Part(root, "KavovarOdkap", new Vector3(cofX, H + 0.01f, -0.06f), new Vector3(0.26f, 0.02f, 0.1f), chrome, false);
        Cyl(root, "Salek", new Vector3(cofX, H + 0.05f, -0.06f), 0.07f, 0.06f, white, false);
        Cyl(root, "Mlynek", new Vector3(cofX - 0.3f, H + 0.14f, 0.15f), 0.13f, 0.28f, black, true);
        Cyl(root, "MlynekZasobnik", new Vector3(cofX - 0.3f, H + 0.34f, 0.15f), 0.15f, 0.12f, chrome, false);

        // balicky na stole (pred vycepem a kavovarem)
        Pickup(root, PickupKind.Beer, "Balicek_PivoVycep", new Vector3(tapX, H, -0.22f), H + 0.4f);
        Pickup(root, PickupKind.Coffee, "Balicek_Kava", new Vector3(cofX, H, -0.24f), H + 0.4f);
        Done(root);
    }

    // Lasagne v alobalove vanicce (balicek: plne zdravi + stit). Stred okna Scene na stul / linku / zem.
    [MenuItem("BrosOverwatch/Dekorace/Lasagne (mir na stul nebo linku)")]
    static void Lasagne() => PlaceBuff(PickupKind.Lasagne, "Lasagne");

    [MenuItem("BrosOverwatch/Dekorace/Buff Klokani kuze - +100 stitu (mir na stul, linku nebo zem)")]
    static void KangarooSkin() => PlaceBuff(PickupKind.KangarooSkin, "KlokaniKuze");

    [MenuItem("BrosOverwatch/Dekorace/Buff Kava (mir na stul nebo linku)")]
    static void Coffee() => PlaceBuff(PickupKind.Coffee, "Kava");

    [MenuItem("BrosOverwatch/Dekorace/Buff Bila latka na podtacku (mir na stul nebo linku)")]
    static void WhitePowder() => PlaceBuff(PickupKind.WhitePowder, "BilaLatka");

    [MenuItem("BrosOverwatch/Dekorace/Buff Pivo z vycepu (mir na stul nebo linku)")]
    static void Beer() => PlaceBuff(PickupKind.Beer, "PivoVycep");

    // Samotny balicek s buffem na miste, kam miri stred okna Scene (vodorovna plocha).
    static void PlaceBuff(PickupKind kind, string name)
    {
        if (!Aim(out var hit, out _)) return;
        if (hit.normal.y < 0.7f)
        {
            Debug.LogWarning("[Dekorace] Balicek se poklada na vodorovnou plochu - stred okna Scene musi mirit na stul, linku nebo zem.");
            return;
        }
        // jak vysoko je plocha nad podlahou (hrac stoji na podlaze a sahne nahoru)
        float above = 0f;
        if (Physics.Raycast(hit.point + Vector3.down * 0.05f, Vector3.down, out var floor, 3f, ~0, QueryTriggerInteraction.Ignore))
            above = hit.point.y - floor.point.y;
        var root = NewRoot(name, hit.point, Quaternion.identity);
        Pickup(root, kind, "Balicek_" + name, Vector3.zero, Mathf.Max(0.5f, above + 0.4f));
        Done(root);
    }

    // ---------------- sklopna podlaha ----------------

    // Vybrany kvadr podlahy patra (v Hierarchy) se udela sklopnou rampou (FoldingFloor): kloub na horni hrane
    // strany u listy v obyvaku (PRVNIPATRO/strop/Cube (11)), nebo - kdyz lista neni - strany nejbliz kamere okna
    // Scene. Uhel se spocita tak, aby volny okraj dosedl na podlahu pod nim. Sklopi se po dobyti bodu 3
    // (zmenis v Inspectoru: Unlock After).
    [MenuItem("BrosOverwatch/Dekorace/Sklopna podlaha z vybraneho kvadru (rampa do patra)")]
    static void MakeFoldingFloor() => MakeFoldingFloor(Selection.activeGameObject, null);

    // Sklopna podlaha s kloubem na druhe strane (kdyz se sklapi na spatnou stranu): vyber SklopnaPodlaha_...
    [MenuItem("BrosOverwatch/Dekorace/Sklopna podlaha - prehodit stranu kloubu")]
    static void FlipFoldingFloor()
    {
        var floor = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<FoldingFloor>() : null;
        if (floor == null || floor.transform.childCount == 0)
        {
            Debug.LogWarning("[Dekorace] Vyber sklopnou podlahu (SklopnaPodlaha_...).");
            return;
        }
        var pivot = floor.transform;
        Undo.RecordObject(pivot, "Prehodit kloub");
        pivot.localRotation = floor.closedLocalRotation;   // nejdriv nahoru
        Vector3 worldAxis = pivot.TransformDirection(floor.axis);
        Vector3 into = Vector3.Cross(worldAxis, Vector3.up).normalized;   // od stareho kloubu do desky = nova strana kloubu
        var target = pivot.GetChild(0).gameObject;
        Undo.SetTransformParent(target.transform, pivot.parent, "Prehodit kloub");
        int unlock = floor.unlockAfter;
        Undo.DestroyObjectImmediate(pivot.gameObject);
        MakeFoldingFloor(target, new Vector3(Mathf.Round(into.x), 0f, Mathf.Round(into.z)), unlock);
    }

    static void MakeFoldingFloor(GameObject target, Vector3? forcedSide, int unlockAfter = 3)
    {
        var renderer = target != null ? target.GetComponent<Renderer>() : null;
        if (renderer == null)
        {
            Debug.LogWarning("[Dekorace] Nejdriv v Hierarchy vyber kvadr podlahy, ktery se ma sklapet.");
            return;
        }
        if (target.GetComponentInParent<FoldingFloor>() != null)
        {
            Debug.LogWarning("[Dekorace] Tahle podlaha uz sklopna je.");
            return;
        }
        Bounds b = renderer.bounds;

        // strana kloubu: u listy, jinak nejbliz kamere
        Vector3 reference;
        var lath = GameObject.Find("Map-Domasov/HlavniChata/PRVNIPATRO/strop/Cube (11)");
        if (lath != null && lath.GetComponent<Renderer>() != null) reference = lath.GetComponent<Renderer>().bounds.center;
        else if (SceneView.lastActiveSceneView != null) reference = SceneView.lastActiveSceneView.camera.transform.position;
        else reference = b.center;
        var sides = new[] { Vector3.left, Vector3.right, Vector3.back, Vector3.forward };
        Vector3 hingeSide = sides[0];
        float best = float.MaxValue;
        if (forcedSide.HasValue) { hingeSide = forcedSide.Value; best = -1f; }
        if (!forcedSide.HasValue)
        foreach (var s in sides)
        {
            Vector3 face = b.center + Vector3.Scale(s, b.extents);
            float d = Mathf.Abs(Vector3.Dot(reference - face, s));
            if (d < best) { best = d; hingeSide = s; }
        }
        Vector3 into = -hingeSide;                                   // od kloubu do desky
        float length = Mathf.Abs(Vector3.Dot(b.size, into)), thickness = b.size.y;
        Vector3 hinge = b.center + Vector3.Scale(hingeSide, b.extents);
        hinge.y = b.max.y;

        // podlaha pod volnym okrajem
        Physics.SyncTransforms();
        Vector3 freeEdge = hinge + into * (length - 0.15f);
        float floorY = float.NaN;
        foreach (var h in Physics.RaycastAll(new Vector3(freeEdge.x, b.min.y - 0.02f, freeEdge.z), Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore))
            if (float.IsNaN(floorY) || h.point.y > floorY) floorY = h.point.y;
        if (float.IsNaN(floorY))
        {
            Debug.LogWarning("[Dekorace] Pod podlahou nenalezena zem - rampa nema kam dopadnout.");
            return;
        }
        float drop = b.max.y - floorY;
        // spodni hrana volneho konce dosedne: L sin(a) + t cos(a) = drop
        float r = Mathf.Sqrt(length * length + thickness * thickness);
        float angle = (Mathf.Asin(Mathf.Clamp(drop / r, -1f, 1f)) - Mathf.Atan2(thickness, length)) * Mathf.Rad2Deg;
        angle = Mathf.Clamp(angle, 5f, 85f);

        var pivot = new GameObject("SklopnaPodlaha_" + target.name).transform;
        Undo.RegisterCreatedObjectUndo(pivot.gameObject, "Sklopna podlaha");
        pivot.SetParent(target.transform.parent, false);
        pivot.SetPositionAndRotation(hinge, Quaternion.identity);
        Undo.SetTransformParent(target.transform, pivot, "Sklopna podlaha");
        foreach (var t in target.GetComponentsInChildren<Transform>(true))
        {
            Undo.RecordObject(t.gameObject, "Sklopna podlaha");
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);   // pohybuje se - nesmi byt staticka
        }
        var floor = Undo.AddComponent<FoldingFloor>(pivot.gameObject);
        floor.axis = Vector3.Cross(Vector3.up, into);
        floor.openAngle = angle;
        floor.unlockAfter = unlockAfter;
        floor.closedLocalRotation = pivot.localRotation;
        floor.collapseSound = AssetDatabase.LoadAssetAtPath<AudioClip>(CollapseSoundPath);
        FillHideWhenOpen(floor);
        var body = pivot.GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

        Selection.activeGameObject = pivot.gameObject;
        EditorSceneManager.MarkSceneDirty(pivot.gameObject.scene);
        string steep = angle > 40f ? " POZOR: na 40 stupnu uz se do rampy nevyjde - vyber delsi kus podlahy." : "";
        Debug.Log($"[Dekorace] Sklopna podlaha: kloub na strane {hingeSide}, delka {length:0.0} m, vyska {drop:0.0} m, uhel {angle:0} st. "
            + $"Sklopi se po dobyti bodu {floor.unlockAfter} (Unlock After v Inspectoru).{steep}");
    }

    // Test ve hre: spusti shyb postavicky a zhrouceni stropu (vsechny sklopne podlahy); podruhe je vrati nahoru.
    [MenuItem("BrosOverwatch/Test - shyb postavicky a zhrouceni stropu (v Play modu)")]
    static void TestFoldingFloors()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[Dekorace] Test funguje jen ve hre - nejdriv zmackni Play, pak tenhle prikaz.");
            return;
        }
        var floors = Object.FindObjectsByType<FoldingFloor>();
        if (floors.Length == 0) { Debug.LogWarning("[Dekorace] Ve scene neni sklopna podlaha."); return; }
        foreach (var f in floors) f.TestCollapse();
        Debug.Log(floors[0].TestDown ? "[Dekorace] Test: postavicka dela shyb, pak se zhrouti strop." : "[Dekorace] Test: podlaha zpet nahoru.");
    }

    // Zvuk zhrouceni (postavicka s listou sleti dolu): Povova (Ayranova) ultimatka, cast 2.
    internal const string CollapseSoundPath = "Assets/Audio/Ayran/ability_Q/step_2.wav";

    // Nahled v editoru: vybrana sklopna podlaha dolu / nahoru (pred ulozenim scenu vrat nahoru!).
    [MenuItem("BrosOverwatch/Dekorace/Sklopna podlaha - nahled dolu nebo nahoru")]
    static void PreviewFoldingFloor()
    {
        var floor = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<FoldingFloor>() : null;
        if (floor == null)
        {
            Debug.LogWarning("[Dekorace] Vyber sklopnou podlahu (SklopnaPodlaha_...).");
            return;
        }
        FillHideWhenOpen(floor);
        Undo.RecordObject(floor.transform, "Nahled sklopne podlahy");
        bool down = Quaternion.Angle(floor.transform.localRotation, floor.closedLocalRotation) > 1f;
        floor.transform.localRotation = down ? floor.closedLocalRotation : floor.closedLocalRotation * Quaternion.AngleAxis(floor.openAngle, floor.axis);
        Debug.Log(down ? "[Dekorace] Sklopna podlaha nahore (takhle ji uloz)." : "[Dekorace] Sklopna podlaha dole - nahled. Pred ulozenim ji vrat nahoru (stejny prikaz nebo Ctrl+Z).");
    }

    // Postavicka visici jen za pravou ruku na liste pod stropem obyvaku (strop/Cube (11)). Je videt, jen dokud
    // neni podlaha sklopena (prida se do Hide When Open sklopnych podlah). Vlozi se doprostred listy, celem ke
    // kamere okna Scene; podel listy ji posunes (W).
    [MenuItem("BrosOverwatch/Dekorace/Postavicka visici na liste (zmizi se sklopenim podlahy)")]
    static void HangingFigure()
    {
        var lath = GameObject.Find("Map-Domasov/HlavniChata/PRVNIPATRO/strop/Cube (11)");
        var lathRenderer = lath != null ? lath.GetComponent<Renderer>() : null;
        if (lathRenderer == null)
        {
            Debug.LogWarning("[Dekorace] Lista PRVNIPATRO/strop/Cube (11) ve scene neni.");
            return;
        }
        Bounds b = lathRenderer.bounds;
        Vector3 grip = new Vector3(b.center.x, b.min.y, b.center.z);
        Vector3 face = Vector3.back;
        if (SceneView.lastActiveSceneView != null)
        {
            face = SceneView.lastActiveSceneView.camera.transform.position - grip;
            face.y = 0f;
            // kolmo na listu (lista vede podel delsi vodorovne osy)
            face = b.size.x < b.size.z ? new Vector3(Mathf.Sign(face.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(face.z));
        }
        var root = NewRoot("VisiciPostavicka", grip, Quaternion.LookRotation(face, Vector3.up));   // lokalne: +z dopredu, +x prava ruka

        var shirt = MapBuildKit.Mat(DecorFolder + "VisiciTriko.mat", null, new Color(0.78f, 0.22f, 0.16f), Vector2.one, 0.1f);
        var pants = MapBuildKit.Mat(DecorFolder + "SpaciDziny.mat", null, new Color(0.13f, 0.18f, 0.3f), Vector2.one, 0.15f);
        var skin = MapBuildKit.Mat(DecorFolder + "SpaciKuze.mat", null, new Color(0.86f, 0.66f, 0.55f), Vector2.one, 0.25f);
        var dark = MapBuildKit.Mat(DecorFolder + "SpaciCerna.mat", null, new Color(0.06f, 0.06f, 0.07f), Vector2.one, 0.2f);
        var hair = MapBuildKit.Mat(DecorFolder + "VisiciVlasy.mat", null, new Color(0.3f, 0.2f, 0.12f), Vector2.one, 0.2f);

        GameObject Limb(string name, Vector3 from, Vector3 to, float thick, Material mat, bool collider)
        {
            var go = MapBuildKit.Beam(root, name, root.TransformPoint(from), root.TransformPoint(to), thick, mat, collider);
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            return go;
        }
        void Ball(string name, Vector3 local, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // prava ruka nahore na liste (prsty pres listu), paze natazena k ramenu
        Part(root, "PravaDlan", new Vector3(0.06f, -0.02f, 0f), new Vector3(0.1f, 0.12f, 0.08f), skin, false);
        Part(root, "Prsty", new Vector3(0.06f, 0.05f, 0.02f), new Vector3(0.09f, 0.03f, 0.1f), skin, false);
        Limb("PravePredlokti", new Vector3(0.07f, -0.07f, 0f), new Vector3(0.12f, -0.42f, 0f), 0.08f, skin, false);
        Limb("PravaPaze", new Vector3(0.12f, -0.42f, 0f), new Vector3(0.18f, -0.78f, 0f), 0.11f, shirt, false);
        // hlava vedle zvednute paze, mirne zaklonena (diva se nahoru na ruku)
        Ball("Hlava", new Vector3(-0.04f, -0.72f, 0.02f), new Vector3(0.22f, 0.25f, 0.23f), skin);
        Ball("Vlasy", new Vector3(-0.04f, -0.66f, -0.01f), new Vector3(0.23f, 0.17f, 0.23f), hair);
        Limb("Krk", new Vector3(-0.02f, -0.84f, 0f), new Vector3(-0.01f, -0.92f, 0f), 0.09f, skin, false);
        // trup (lehce vykloneny od zvednute ruky)
        var torso = MapBuildKit.Box(root, "Trup", root.TransformPoint(new Vector3(-0.01f, -1.2f, 0f)), new Vector3(0.4f, 0.58f, 0.22f),
            root.rotation * Quaternion.Euler(0f, 0f, -6f), shirt, true);
        GameObjectUtility.SetStaticEditorFlags(torso, 0);
        // leva ruka visi volne
        Limb("LevaPaze", new Vector3(-0.22f, -0.95f, 0f), new Vector3(-0.29f, -1.27f, 0.03f), 0.1f, shirt, false);
        Limb("LevePredlokti", new Vector3(-0.29f, -1.27f, 0.03f), new Vector3(-0.31f, -1.58f, 0.08f), 0.08f, skin, false);
        Part(root, "LevaDlan", new Vector3(-0.31f, -1.64f, 0.09f), new Vector3(0.07f, 0.11f, 0.09f), skin, false);
        // nohy volne dolu, jedna mirne pokrcena dopredu
        Limb("PraveStehno", new Vector3(0.1f, -1.48f, 0f), new Vector3(0.12f, -1.92f, 0.04f), 0.15f, pants, true);
        Limb("PraveLytko", new Vector3(0.12f, -1.92f, 0.04f), new Vector3(0.13f, -2.36f, 0.02f), 0.12f, pants, true);
        Part(root, "PravaBota", new Vector3(0.13f, -2.42f, 0.06f), new Vector3(0.11f, 0.08f, 0.26f), dark, false);
        Limb("LeveStehno", new Vector3(-0.1f, -1.48f, 0f), new Vector3(-0.1f, -1.9f, 0.12f), 0.15f, pants, true);
        Limb("LeveLytko", new Vector3(-0.1f, -1.9f, 0.12f), new Vector3(-0.11f, -2.3f, -0.02f), 0.12f, pants, true);
        Part(root, "LevaBota", new Vector3(-0.11f, -2.36f, 0.02f), new Vector3(0.11f, 0.08f, 0.26f), dark, false);

        // zmizi se sklopenim podlahy
        int linked = 0;
        foreach (var floor in Object.FindObjectsByType<FoldingFloor>(FindObjectsInactive.Include))
        {
            Undo.RecordObject(floor, "Visici postavicka");
            var list = new List<GameObject>(floor.hideWhenOpen ?? new GameObject[0]);
            if (!list.Contains(lath)) list.Add(lath);
            list.Add(root.gameObject);
            floor.hideWhenOpen = list.ToArray();
            EditorUtility.SetDirty(floor);
            linked++;
        }
        Done(root);
        if (linked == 0)
            Debug.LogWarning("[Dekorace] Ve scene neni sklopna podlaha - postavicka nezmizi. Vytvor ji (nebo pretahni postavicku do Hide When Open).");
    }

    // Fotka obliceje (Dekorace/VisiciOblicej.png, ovalny vyrez) na hlavu visici postavicky: dite "Hlava", takze se pri
    // houpani i shybu hybe presne s hlavou. Celem dopredu (lokalni +z postavicky).
    [MenuItem("BrosOverwatch/Dekorace/Postavicka visici na liste - oblicej z fotky")]
    internal static void HangingFigureFace()
    {
        var figure = GameObject.Find("Map-Domasov/Dekorace/VisiciPostavicka");
        var head = figure != null ? figure.transform.Find("Hlava") : null;
        if (head == null)
        {
            Debug.LogWarning("[Dekorace] Visici postavicka (Dekorace/VisiciPostavicka s hlavou) ve scene neni.");
            return;
        }
        var material = MapBuildKit.Mat(DecorFolder + "VisiciOblicej.mat", DecorFolder + "VisiciOblicej.png", Color.white, Vector2.one, 0.15f, alphaClip: true);
        if (material == null)
        {
            Debug.LogWarning("[Dekorace] Fotka obliceje se jeste importuje - zkus to za chvili znovu.");
            return;
        }
        var old = head.Find("Oblicej");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.name = "Oblicej";
        Undo.RegisterCreatedObjectUndo(quad, "Oblicej postavicky");
        quad.transform.SetParent(head, false);
        // hlava je koule s meritkem (0,22 x 0,25 x 0,23): oblicej 0,19 x 0,25 m tesne pred jejim celem
        global::HangingFigure.PlaceFace(quad.transform);   // na zatylek (kamera je za postavickou)
        var renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Selection.activeGameObject = quad;
        EditorSceneManager.MarkSceneDirty(quad.scene);
        Debug.Log("[Dekorace] Visici postavicka ma oblicej z fotky (Hlava/Oblicej). Uloz scenu (Ctrl+S).");
    }

    // Lista pod hranou stropu v obyvaku (PRVNIPATRO/strop/Cube (11)) zmizi, kdyz se podlaha sklopi.
    static void FillHideWhenOpen(FoldingFloor floor)
    {
        if (floor.hideWhenOpen != null && floor.hideWhenOpen.Length > 0) return;
        var lath = GameObject.Find("Map-Domasov/HlavniChata/PRVNIPATRO/strop/Cube (11)");
        if (lath == null) return;
        Undo.RecordObject(floor, "Sklopna podlaha - lista");
        floor.hideWhenOpen = new[] { lath };
        EditorUtility.SetDirty(floor);
        Debug.Log("[Dekorace] Sklopna podlaha: lista strop/Cube (11) zmizi pri sklopeni.");
    }

    // ---------------- vybaveni pokoje ----------------

    [MenuItem("BrosOverwatch/Dekorace/Lekarnicka (mir na zem nebo stul)")]
    static void SmallHealth() => PlaceBuff(PickupKind.Health, "Lekarnicka");

    // Pokoj se pozna podle zdi kolem mista, kam miri stred okna Scene (paprsky na 4 strany). Nabytek se stavi ke
    // stenam: jen tam, kde je za nim plna zed (ne dvere, u vysokych kusu ani okno) a kde nic nestoji.
    // Postel s nocnim stolkem, skrin, komoda, knihovna, stul se dvema zidlemi, uprostred koberec - co se vejde.
    // Vse s kolizi podle vzhledu (drobnosti bez kolize). Ctrl+Z vrati cely pokoj.
    [MenuItem("BrosOverwatch/Dekorace/Vybavit pokoj nabytkem (mir na podlahu pokoje)")]
    static void FurnishRoom()
    {
        if (!Aim(out var hit, out _)) return;
        if (hit.normal.y < 0.7f)
        {
            Debug.LogWarning("[Dekorace] Stred okna Scene musi mirit na podlahu pokoje.");
            return;
        }
        Physics.SyncTransforms();
        var room = new RoomInfo { floorY = hit.point.y };
        Vector3 o = hit.point + Vector3.up * 1.2f;
        room.xMax = o.x + WallDistance(o, Vector3.right);
        room.xMin = o.x - WallDistance(o, Vector3.left);
        room.zMax = o.z + WallDistance(o, Vector3.forward);
        room.zMin = o.z - WallDistance(o, Vector3.back);
        if (room.xMax - room.xMin < 1.8f || room.zMax - room.zMin < 1.8f)
        {
            Debug.LogWarning("[Dekorace] Pokoj je moc maly (nebo stred okna Scene nemiri do pokoje).");
            return;
        }

        var root = NewRoot("VybavaPokoje", new Vector3((room.xMin + room.xMax) * 0.5f, room.floorY, (room.zMin + room.zMax) * 0.5f), Quaternion.identity);
        var m = new RoomMats
        {
            wood = MapBuildKit.Mat(DecorFolder + "SkrinkaDrevo.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.55f, 0.38f, 0.24f), Vector2.one, 0.35f),
            dark = MapBuildKit.Mat(DecorFolder + "DrevoTmave.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.36f, 0.23f, 0.14f), Vector2.one, 0.3f),
            linen = MapBuildKit.Mat(DecorFolder + "Povleceni.mat", null, new Color(0.93f, 0.92f, 0.88f), Vector2.one, 0.05f),
            blanket = MapBuildKit.Mat(DecorFolder + "Perina.mat", null, new Color(0.35f, 0.45f, 0.62f), Vector2.one, 0.05f),
            metal = MapBuildKit.Mat(DecorFolder + "Uchytka.mat", null, new Color(0.75f, 0.75f, 0.78f), Vector2.one, 0.8f),
            rug = MapBuildKit.Mat(DecorFolder + "KobecPokoj.mat", null, new Color(0.55f, 0.16f, 0.14f), Vector2.one, 0.05f),
            shade = MapBuildKit.Mat(DecorFolder + "Stinitko.mat", null, new Color(0.95f, 0.88f, 0.7f), Vector2.one, 0.1f),
        };
        var bookColors = new[] { new Color(0.6f, 0.15f, 0.12f), new Color(0.15f, 0.3f, 0.5f), new Color(0.2f, 0.42f, 0.22f), new Color(0.75f, 0.6f, 0.25f), new Color(0.3f, 0.2f, 0.35f) };
        m.books = new Material[bookColors.Length];
        for (int i = 0; i < bookColors.Length; i++)
            m.books[i] = MapBuildKit.Mat(DecorFolder + "Kniha_" + i + ".mat", null, bookColors[i], Vector2.one, 0.2f);

        var placed = new List<string>();
        // postel (celo u zdi) a vedle ni nocni stolek
        if (TryPlace(room, 1.5f, 2.1f, 1.0f, null, out var bed))
        {
            Bed(Item(root, "Postel", bed), m);
            placed.Add("postel");
            foreach (float side in new[] { 1f, -1f })
            {
                if (TryPlace(room, 0.45f, 0.4f, 0.55f, bed.Next(side, 0.75f + 0.05f + 0.225f), out var stand))
                {
                    Nightstand(Item(root, "NocniStolek", stand), m);
                    placed.Add("nocni stolek");
                    break;
                }
            }
        }
        if (TryPlace(room, 1.0f, 0.6f, 2.0f, null, out var wardrobe)) { Wardrobe(Item(root, "Skrin", wardrobe), m); placed.Add("skrin"); }
        if (TryPlace(room, 1.2f, 0.5f, 0.85f, null, out var dresser)) { Dresser(Item(root, "Komoda", dresser), m); placed.Add("komoda"); }
        if (TryPlace(room, 0.9f, 0.32f, 1.8f, null, out var shelf)) { Bookshelf(Item(root, "Knihovna", shelf), m); placed.Add("knihovna"); }
        if (TryPlace(room, 1.2f, 1.25f, 0.95f, null, out var desk)) { TableWithChairs(Item(root, "StulSeZidlemi", desk), m); placed.Add("stul se zidlemi"); }

        // koberec uprostred (kdyz tam nic nestoji)
        float rw = Mathf.Min(2.2f, (room.xMax - room.xMin) - 1.6f), rd = Mathf.Min(1.6f, (room.zMax - room.zMin) - 1.6f);
        Vector3 mid = new Vector3((room.xMin + room.xMax) * 0.5f, room.floorY, (room.zMin + room.zMax) * 0.5f);
        if (rw > 0.8f && rd > 0.8f && !Blocked(room, mid + Vector3.up * 0.2f, new Vector3(rw * 0.5f, 0.15f, rd * 0.5f), Quaternion.identity))
        {
            var rug = MapBuildKit.Box(root, "Koberec", mid + Vector3.up * 0.006f, new Vector3(rw, 0.012f, rd), Quaternion.identity, m.rug, false);
            GameObjectUtility.SetStaticEditorFlags(rug, 0);
            placed.Add("koberec");
        }

        Done(root);
        Debug.Log($"[Dekorace] Pokoj {room.xMax - room.xMin:0.0} x {room.zMax - room.zMin:0.0} m vybaven: {(placed.Count > 0 ? string.Join(", ", placed) : "nic se nevešlo")}.");
    }

    class RoomInfo { public float floorY, xMin, xMax, zMin, zMax; }

    class RoomMats { public Material wood, dark, linen, blanket, metal, rug, shade; public Material[] books; }

    // Misto u zdi: stred pudorysu na podlaze, natoceni (lokalne +z = ke zdi), zed a poloha podel ni.
    struct Spot
    {
        public Vector3 center;
        public Quaternion rotation;
        public int wall;
        public float along, width;
        // misto hned vedle (na stejne zdi), 'side' = smer podel zdi, 'offset' = vzdalenost stredu
        public Spot? Next(float side, float offset) => new Spot { wall = wall, along = along + side * offset, width = -1f };
    }

    static float WallDistance(Vector3 origin, Vector3 dir)
    {
        // nejblizsi ze tri paprsku vedle sebe (aby dvere nebo okno nevedly za zed)
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        float best = 12f;
        foreach (float k in new[] { -0.5f, 0f, 0.5f })
            if (Physics.Raycast(origin + side * k, dir, out var h, 12f, ~0, QueryTriggerInteraction.Ignore))
                best = Mathf.Min(best, h.distance);
        return best;
    }

    // Zdi pokoje: 0 = +x, 1 = -x, 2 = +z, 3 = -z. Vraci bod na zdi pro polohu 'along' a smer do pokoje.
    static void Wall(RoomInfo r, int wall, float along, out Vector3 point, out Vector3 inward, out float min, out float max)
    {
        switch (wall)
        {
            case 0: point = new Vector3(r.xMax, r.floorY, along); inward = Vector3.left; min = r.zMin; max = r.zMax; break;
            case 1: point = new Vector3(r.xMin, r.floorY, along); inward = Vector3.right; min = r.zMin; max = r.zMax; break;
            case 2: point = new Vector3(along, r.floorY, r.zMax); inward = Vector3.back; min = r.xMin; max = r.xMax; break;
            default: point = new Vector3(along, r.floorY, r.zMin); inward = Vector3.forward; min = r.xMin; max = r.xMax; break;
        }
    }

    // Najde misto u zdi pro kus 'width' (podel zdi) x 'depth' x 'height'. Bez 'only' zkousi vsechny zdi (od nejdelsi)
    // a polohy od rohu ke stredu.
    static bool TryPlace(RoomInfo r, float width, float depth, float height, Spot? only, out Spot spot)
    {
        spot = default;
        var walls = new List<int> { 0, 1, 2, 3 };
        walls.Sort((a, b) => WallLength(r, b).CompareTo(WallLength(r, a)));
        foreach (int w in walls)
        {
            if (only.HasValue && only.Value.wall != w) continue;
            Wall(r, w, 0f, out _, out var inward, out float min, out float max);
            float lo = min + width * 0.5f + 0.04f, hi = max - width * 0.5f - 0.04f;
            if (hi < lo) continue;
            var candidates = new List<float>();
            if (only.HasValue) candidates.Add(only.Value.along);
            else
                for (float s = lo; s <= hi + 0.001f; s += 0.2f) candidates.Add(s);
            candidates.Sort((a, b) => Mathf.Min(a - lo, hi - a).CompareTo(Mathf.Min(b - lo, hi - b)));
            foreach (float s in candidates)
            {
                if (s < lo - 0.001f || s > hi + 0.001f) continue;
                Wall(r, w, s, out var point, out _, out _, out _);
                var rotation = Quaternion.LookRotation(-inward, Vector3.up);
                Vector3 center = point + inward * (depth * 0.5f + 0.03f);
                if (Blocked(r, center + Vector3.up * (height * 0.5f + 0.04f), new Vector3(width * 0.5f, Mathf.Max(0.02f, height * 0.5f - 0.02f), depth * 0.5f), rotation)) continue;
                if (!SolidBehind(center, rotation, inward, width, depth, height)) continue;
                spot = new Spot { center = center, rotation = rotation, wall = w, along = s, width = width };
                return true;
            }
        }
        return false;
    }

    static float WallLength(RoomInfo r, int wall) => wall < 2 ? r.zMax - r.zMin : r.xMax - r.xMin;

    // Neco v kvadru stoji? (podlaha a tenke veci na ni se nepocitaji)
    static bool Blocked(RoomInfo r, Vector3 center, Vector3 half, Quaternion rotation)
    {
        foreach (var c in Physics.OverlapBox(center, half, rotation, ~0, QueryTriggerInteraction.Ignore))
            if (c.bounds.max.y > r.floorY + 0.06f) return true;
        return false;
    }

    // Za kusem nabytku musi byt plna zed v cele vysce (ne dvere, u vysokych kusu ani okno).
    static bool SolidBehind(Vector3 center, Quaternion rotation, Vector3 inward, float width, float depth, float height)
    {
        Vector3 right = rotation * Vector3.right;
        var heights = new List<float> { 0.35f };
        for (float h = 0.95f; h < Mathf.Min(height, 2.2f); h += 0.6f) heights.Add(h);
        if (height > 1.2f) heights.Add(Mathf.Min(height - 0.1f, 2.2f));
        foreach (float h in heights)
            foreach (float t in new[] { -width * 0.5f + 0.1f, 0f, width * 0.5f - 0.1f })
                if (!Physics.Raycast(center + Vector3.up * h + right * t, -inward, depth * 0.5f + 0.4f, ~0, QueryTriggerInteraction.Ignore))
                    return false;
        return true;
    }

    static Transform Item(Transform root, string name, Spot spot)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        t.SetPositionAndRotation(spot.center, spot.rotation);
        Physics.SyncTransforms();
        return t;
    }

    // Postel: celo u zdi (+z), nozni cast k pokoji. 1,5 x 2,1 m.
    static void Bed(Transform t, RoomMats m)
    {
        Part(t, "Ram", new Vector3(0f, 0.2f, 0f), new Vector3(1.5f, 0.22f, 2.05f), m.wood, true);
        Part(t, "Matrace", new Vector3(0f, 0.4f, -0.02f), new Vector3(1.4f, 0.18f, 1.95f), m.linen, true);
        Part(t, "Celo", new Vector3(0f, 0.5f, 1.0f), new Vector3(1.5f, 1.0f, 0.06f), m.dark, true);
        Part(t, "NozniCast", new Vector3(0f, 0.3f, -1.0f), new Vector3(1.5f, 0.6f, 0.05f), m.dark, true);
        foreach (float x in new[] { -0.35f, 0.35f })
            Part(t, "Polstar", new Vector3(x, 0.54f, 0.72f), new Vector3(0.55f, 0.1f, 0.38f), m.linen, false);
        Part(t, "Perina", new Vector3(0f, 0.515f, -0.28f), new Vector3(1.44f, 0.06f, 1.38f), m.blanket, false);
    }

    static void Nightstand(Transform t, RoomMats m)
    {
        Part(t, "Stolek", new Vector3(0f, 0.27f, 0f), new Vector3(0.45f, 0.54f, 0.4f), m.wood, true);
        Part(t, "Suplik", new Vector3(0f, 0.4f, -0.205f), new Vector3(0.39f, 0.16f, 0.01f), m.dark, false);
        Part(t, "Uchytka", new Vector3(0f, 0.4f, -0.215f), new Vector3(0.08f, 0.015f, 0.015f), m.metal, false);
        Cyl(t, "LampaNoha", new Vector3(0.05f, 0.64f, 0.03f), 0.04f, 0.2f, m.metal, false);
        Cyl(t, "LampaStinitko", new Vector3(0.05f, 0.78f, 0.03f), 0.2f, 0.14f, m.shade, false);
    }

    static void Wardrobe(Transform t, RoomMats m)
    {
        Part(t, "Skrin", new Vector3(0f, 1.0f, 0f), new Vector3(1.0f, 2.0f, 0.6f), m.wood, true);
        Part(t, "Rimsa", new Vector3(0f, 2.02f, -0.01f), new Vector3(1.05f, 0.05f, 0.64f), m.dark, false);
        Part(t, "Spara", new Vector3(0f, 1.02f, -0.302f), new Vector3(0.015f, 1.86f, 0.01f), m.dark, false);
        foreach (float x in new[] { -0.06f, 0.06f })
            Part(t, "Uchytka", new Vector3(x, 1.05f, -0.315f), new Vector3(0.015f, 0.14f, 0.02f), m.metal, false);
    }

    static void Dresser(Transform t, RoomMats m)
    {
        Part(t, "Komoda", new Vector3(0f, 0.42f, 0f), new Vector3(1.2f, 0.84f, 0.5f), m.wood, true);
        Part(t, "Deska", new Vector3(0f, 0.855f, -0.01f), new Vector3(1.24f, 0.03f, 0.53f), m.dark, false);
        for (int i = 0; i < 3; i++)
        {
            float y = 0.15f + i * 0.25f;
            Part(t, "Suplik", new Vector3(0f, y, -0.255f), new Vector3(1.12f, 0.21f, 0.01f), m.dark, false);
            Part(t, "Uchytka", new Vector3(0f, y, -0.265f), new Vector3(0.16f, 0.02f, 0.015f), m.metal, false);
        }
        Cyl(t, "Vaza", new Vector3(0.35f, 0.97f, 0f), 0.12f, 0.2f, m.books[1], false);
    }

    static void Bookshelf(Transform t, RoomMats m)
    {
        const float w = 0.9f, d = 0.32f, h = 1.8f;
        Part(t, "Zada", new Vector3(0f, h * 0.5f, d * 0.5f - 0.01f), new Vector3(w, h, 0.02f), m.dark, true);
        foreach (float x in new[] { -1f, 1f })
            Part(t, "Bok", new Vector3(x * (w * 0.5f - 0.01f), h * 0.5f, 0f), new Vector3(0.02f, h, d), m.wood, true);
        var random = new System.Random(7);
        for (int i = 0; i < 5; i++)
        {
            float y = 0.02f + i * 0.44f;
            Part(t, "Police", new Vector3(0f, y, 0f), new Vector3(w - 0.04f, 0.025f, d), m.wood, true);
            if (i == 4) break;
            float x = -w * 0.5f + 0.04f;
            while (x < w * 0.5f - 0.12f)
            {
                float bw = 0.03f + (float)random.NextDouble() * 0.04f, bh = 0.22f + (float)random.NextDouble() * 0.12f;
                if (random.NextDouble() < 0.12) { x += 0.08f; continue; }
                Part(t, "Kniha", new Vector3(x + bw * 0.5f, y + 0.0125f + bh * 0.5f, 0.02f), new Vector3(bw - 0.004f, bh, 0.22f), m.books[random.Next(m.books.Length)], false);
                x += bw;
            }
        }
    }

    // Stul u zdi (0,7 m hluboky) a pred nim dve zidle celem ke stolu. Pudorys 1,2 x 1,25 m.
    static void TableWithChairs(Transform t, RoomMats m)
    {
        const float tz = 0.275f;
        Part(t, "Deska", new Vector3(0f, 0.735f, tz), new Vector3(1.2f, 0.04f, 0.7f), m.wood, true);
        foreach (float x in new[] { -0.55f, 0.55f })
            foreach (float z in new[] { tz - 0.3f, tz + 0.3f })
                Part(t, "Noha", new Vector3(x, 0.36f, z), new Vector3(0.05f, 0.72f, 0.05f), m.dark, true);
        foreach (float x in new[] { -0.3f, 0.3f })
        {
            const float cz = -0.36f;
            Part(t, "Sedak", new Vector3(x, 0.45f, cz), new Vector3(0.42f, 0.04f, 0.42f), m.wood, true);
            foreach (float lx in new[] { -0.18f, 0.18f })
                foreach (float lz in new[] { -0.18f, 0.18f })
                    Part(t, "ZidleNoha", new Vector3(x + lx, 0.215f, cz + lz), new Vector3(0.035f, 0.43f, 0.035f), m.dark, true);
            Part(t, "Operadlo", new Vector3(x, 0.72f, cz - 0.19f), new Vector3(0.42f, 0.5f, 0.035f), m.wood, true);
        }
    }

    // ---------------- vodni dymka ----------------

    // Vodni dymka: sklenena vaza s vodou, kovova tyc s talirkem, hlineny korunek s alobalem a uhliky, tyrkysova
    // hlava, hadice s naustkem v oblouku dolu vedle. Asi 0,7 m vysoka. Stred okna Scene na zem nebo stul.
    [MenuItem("BrosOverwatch/Dekorace/Vodni dymka (mir na zem nebo stul)")]
    static void Hookah()
    {
        if (!Aim(out var hit, out var cam)) return;
        if (hit.normal.y < 0.7f)
        {
            Vector3 from = hit.point + new Vector3(hit.normal.x, 0f, hit.normal.z).normalized * 0.4f;
            if (!Physics.Raycast(from, Vector3.down, out hit, 10f, ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f)
            {
                Debug.LogWarning("[Dekorace] Dymka se stavi na vodorovnou plochu - zamir na zem nebo stul.");
                return;
            }
        }
        Vector3 toCam = cam.position - hit.point;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 0.01f) toCam = Vector3.back;
        var root = NewRoot("VodniDymka", hit.point, Quaternion.LookRotation(-toCam.normalized, Vector3.up));   // lokalne -z ke kamere

        var glass = MapBuildKit.Mat(DecorFolder + "DymkaSklo.mat", null, new Color(0.62f, 0.8f, 0.82f), Vector2.one, 0.95f);
        var water = MapBuildKit.Mat(DecorFolder + "DymkaVoda.mat", null, new Color(0.45f, 0.68f, 0.75f), Vector2.one, 0.95f);
        var metal = MapBuildKit.Mat(DecorFolder + "DymkaKov.mat", null, new Color(0.13f, 0.62f, 0.72f), Vector2.one, 0.85f);
        var chrome = MapBuildKit.Mat(DecorFolder + "Chrom.mat", null, new Color(0.82f, 0.84f, 0.87f), Vector2.one, 0.9f);
        var clay = MapBuildKit.Mat(DecorFolder + "DymkaKorunek.mat", null, new Color(0.62f, 0.32f, 0.18f), Vector2.one, 0.3f);
        var foil = MapBuildKit.Mat(DecorFolder + "DymkaAlobal.mat", null, new Color(0.88f, 0.88f, 0.9f), Vector2.one, 0.75f);
        var coal = MapBuildKit.Mat(DecorFolder + "DymkaUhlik.mat", null, new Color(0.08f, 0.07f, 0.07f), Vector2.one, 0.1f,
            emission: new Color(1f, 0.28f, 0.05f) * 0.6f);
        var hose = MapBuildKit.Mat(DecorFolder + "Hadice.mat", null, new Color(0.15f, 0.15f, 0.17f), Vector2.one, 0.3f);

        // vaza: siroke dno, voda uvnitr, zuzene hrdlo
        Cyl(root, "Vaza", new Vector3(0f, 0.1f, 0f), 0.24f, 0.2f, glass, true);
        Cyl(root, "Voda", new Vector3(0f, 0.075f, 0f), 0.245f, 0.13f, water, false);
        Cyl(root, "VazaRameno", new Vector3(0f, 0.215f, 0f), 0.18f, 0.03f, glass, false);
        Cyl(root, "Hrdlo", new Vector3(0f, 0.25f, 0f), 0.09f, 0.05f, glass, false);
        // tyc s prirubou, talirek na popel, ventil a pripojka hadice
        Cyl(root, "Priruba", new Vector3(0f, 0.285f, 0f), 0.11f, 0.03f, metal, false);
        Cyl(root, "Tyc", new Vector3(0f, 0.45f, 0f), 0.035f, 0.32f, metal, true);
        Cyl(root, "Talirek", new Vector3(0f, 0.53f, 0f), 0.2f, 0.012f, chrome, false);
        Part(root, "Ventil", new Vector3(-0.05f, 0.34f, 0f), new Vector3(0.06f, 0.02f, 0.02f), chrome, false);
        Part(root, "Pripojka", new Vector3(0.05f, 0.36f, 0f), new Vector3(0.06f, 0.025f, 0.025f), metal, false);
        // korunek s alobalem a uhliky, nad nim stinitko
        Cyl(root, "Korunek", new Vector3(0f, 0.6f, 0f), 0.08f, 0.08f, clay, false);
        Cyl(root, "Alobal", new Vector3(0f, 0.643f, 0f), 0.085f, 0.006f, foil, false);
        foreach (float a in new[] { 0f, 120f, 240f })
        {
            var p = Quaternion.Euler(0f, a, 0f) * new Vector3(0.022f, 0f, 0f);
            Part(root, "Uhlik", new Vector3(p.x, 0.655f, p.z), new Vector3(0.025f, 0.018f, 0.025f), coal, false);
        }

        // hadice: oblouk od pripojky ven a dolu, naustek na konci
        Vector3 start = new Vector3(0.08f, 0.36f, 0f);
        Vector3 control = new Vector3(0.38f, 0.5f, -0.1f);
        Vector3 end = new Vector3(0.42f, 0.04f, -0.32f);
        Vector3 prev = start;
        const int segments = 10;
        for (int i = 1; i <= segments; i++)
        {
            float u = i / (float)segments;
            Vector3 point = (1f - u) * (1f - u) * start + 2f * (1f - u) * u * control + u * u * end;
            var seg = MapBuildKit.Beam(root, "Hadice", root.TransformPoint(prev), root.TransformPoint(point), 0.022f, hose);
            GameObjectUtility.SetStaticEditorFlags(seg, 0);
            prev = point;
        }
        var tip = MapBuildKit.Beam(root, "Naustek", root.TransformPoint(end), root.TransformPoint(end + new Vector3(0.02f, -0.005f, -0.16f)), 0.026f, metal);
        GameObjectUtility.SetStaticEditorFlags(tip, 0);
        Done(root);
    }

    // Balicek (PickupSpot). Jmeno s polohou - balicky se ve hre radi podle jmena, musi byt jedinecne.
    static void Pickup(Transform root, PickupKind kind, string name, Vector3 local, float reachBelow)
    {
        Vector3 world = root.TransformPoint(local);
        var spot = new GameObject($"{name}_{Mathf.RoundToInt(world.x * 10f)}_{Mathf.RoundToInt(world.z * 10f)}");
        spot.transform.SetParent(root, false);
        spot.transform.position = world;
        var pickup = spot.AddComponent<PickupSpot>();
        pickup.kind = kind;
        pickup.reachBelow = reachBelow;
    }

    // Valec (lokalni osa y), prumer a vyska v metrech; kolize = valec.
    static void Cyl(Transform root, string name, Vector3 local, float diameter, float height, Material material, bool collider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(root, false);
        go.transform.localPosition = local;
        go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // Libovolny obrazek: vyber ho v okne Project (png/jpg) a pak tenhle prikaz.
    [MenuItem("BrosOverwatch/Dekorace/Obraz - vybrany obrazek z Projectu")]
    static void PictureSelected()
    {
        var texture = Selection.activeObject as Texture2D;
        if (texture == null)
        {
            Debug.LogWarning("[Dekorace] Nejdriv vyber obrazek (png/jpg) v okne Project.");
            return;
        }
        PlacePicture(AssetDatabase.GetAssetPath(texture));
    }

    static void PlacePicture(string texturePath)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            Debug.LogWarning($"[Dekorace] Obrazek {texturePath} nenalezen.");
            return;
        }
        if (!Aim(out var hit, out var cam)) return;
        string name = System.IO.Path.GetFileNameWithoutExtension(texturePath);
        if (Mathf.Abs(hit.normal.y) > 0.5f)
        {
            PlaceStanding(name, texture, hit, cam);   // stred okna miri na zem / podlahu: obraz na stojanu
            return;
        }

        var root = NewRoot(name, hit.point + hit.normal * 0.025f, Quaternion.LookRotation(-hit.normal, Vector3.up));
        BuildPicture(root, name, texture, PictureHeight);
        Done(root);
    }

    // Obraz na stojanu (kdyz stred okna Scene miri na zem): celem ke kamere, spodek 0,35 m nad zemi, vysoky 1,6 m.
    // Za obrazem drevena deska s kolizi (neprostreli se, neprojde se), dve nohy a patky.
    static void PlaceStanding(string name, Texture2D texture, RaycastHit hit, Transform cam)
    {
        const float height = 1.6f, lift = 0.35f;
        float width = height * texture.width / Mathf.Max(1f, texture.height);
        float bar = 0.06f * Mathf.Max(1f, height / 1.2f);
        Vector3 toCam = cam.position - hit.point;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 0.01f) toCam = -cam.forward;
        toCam.y = 0f;
        var root = NewRoot(name, hit.point, Quaternion.LookRotation(-toCam.normalized, Vector3.up));   // lokalni +z od kamery

        var picture = new GameObject("Obraz").transform;
        picture.SetParent(root, false);
        picture.localPosition = new Vector3(0f, lift + height * 0.5f, 0f);
        BuildPicture(picture, name, texture, height);

        var wood = MapBuildKit.Mat(DecorFolder + "RamObrazu.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.42f, 0.28f, 0.17f), Vector2.one, 0.3f);
        Part(root, "Deska", new Vector3(0f, lift + height * 0.5f, 0.035f), new Vector3(width + bar * 2f, height + bar * 2f, 0.04f), wood, true);
        foreach (float x in new[] { -width * 0.5f, width * 0.5f })
        {
            Part(root, "Noha", new Vector3(x, (lift + 0.1f) * 0.5f, 0.035f), new Vector3(0.08f, lift + 0.1f, 0.08f), wood, true);
            Part(root, "Patka", new Vector3(x, 0.03f, 0.035f), new Vector3(0.1f, 0.06f, 0.6f), wood, true);
        }
        Done(root);
    }

    // Platno s obrazkem a dreveny ram kolem (root: lokalni +z miri do zdi).
    static void BuildPicture(Transform root, string name, Texture2D texture, float height)
    {
        float width = height * texture.width / Mathf.Max(1f, texture.height);

        // platno (Quad je videt ze strany -Z = od zdi do mistnosti)
        var canvas = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(canvas.GetComponent<Collider>());
        canvas.name = "Platno";
        canvas.transform.SetParent(root, false);
        canvas.transform.localPosition = new Vector3(0f, 0f, -0.012f);
        canvas.transform.localScale = new Vector3(width, height, 1f);
        canvas.GetComponent<MeshRenderer>().sharedMaterial = PictureMaterial(name, texture);

        // dreveny ram
        var frame = MapBuildKit.Mat(DecorFolder + "RamObrazu.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.42f, 0.28f, 0.17f), Vector2.one, 0.3f);
        float bar = 0.06f * Mathf.Max(1f, height / 1.2f), depth = 0.04f;
        FrameBar(root, new Vector3(0f, (height + bar) * 0.5f, 0f), new Vector3(width + bar * 2f, bar, depth), frame);
        FrameBar(root, new Vector3(0f, -(height + bar) * 0.5f, 0f), new Vector3(width + bar * 2f, bar, depth), frame);
        FrameBar(root, new Vector3((width + bar) * 0.5f, 0f, 0f), new Vector3(bar, height, depth), frame);
        FrameBar(root, new Vector3(-(width + bar) * 0.5f, 0f, 0f), new Vector3(bar, height, depth), frame);
    }

    // ---------------- diplom MUDr. Tomaska a skrinka s Tramalem ----------------

    // Obri diplom na zdi a pod nim skrinka; na skrince lezi Tramal (balicek PickupKind.Tramal:
    // plne zdravi, 25 s o 20 % mensi poskozeni, obnovi se za 30 s).
    [MenuItem("BrosOverwatch/Dekorace/Diplom MUDr. Tomasek + skrinka s Tramalem (mir na zed)")]
    static void DiplomaWithTramal()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PictureFolder + "Plakat_DiplomTomasek.png");
        if (texture == null || !Aim(out var hit, out var cam)) return;
        if (Mathf.Abs(hit.normal.y) > 0.5f)
        {
            Debug.LogWarning("[Dekorace] Diplom se vesi na zed - stred okna Scene musi mirit na zed.");
            return;
        }
        Vector3 outward = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
        if (!Physics.Raycast(hit.point + outward * 0.6f + Vector3.up * 0.5f, Vector3.down, out var floor, 6f, ~0, QueryTriggerInteraction.Ignore))
        {
            Debug.LogWarning("[Dekorace] Pod mistem na zdi nenalezena podlaha.");
            return;
        }
        float floorY = floor.point.y;
        Vector3 wallPoint = new Vector3(hit.point.x, floorY, hit.point.z);
        var facing = Quaternion.LookRotation(-outward, Vector3.up);   // lokalni +z do zdi

        var root = NewRoot("DiplomTomasek", wallPoint, facing);

        // obri plakat: 1,8 m vysoky, spodek 1,1 m nad podlahou
        const float posterHeight = 1.8f, posterBottom = 1.1f;
        var poster = new GameObject("Diplom").transform;
        poster.SetParent(root, false);
        poster.localPosition = new Vector3(0f, posterBottom + posterHeight * 0.5f, -0.025f);
        BuildPicture(poster, "Plakat_DiplomTomasek", texture, posterHeight);

        // skrinka pod nim (lekarnicka): korpus, dvirka, uchytky
        var wood = MapBuildKit.Mat(DecorFolder + "SkrinkaDrevo.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.55f, 0.38f, 0.24f), Vector2.one, 0.35f);
        var metal = MapBuildKit.Mat(DecorFolder + "Uchytka.mat", null, new Color(0.75f, 0.75f, 0.78f), Vector2.one, 0.8f);
        const float w = 0.9f, d = 0.45f, h = 0.9f;
        Part(root, "Skrinka", new Vector3(0f, h * 0.5f, -d * 0.5f - 0.01f), new Vector3(w, h, d), wood, true);
        Part(root, "Deska", new Vector3(0f, h + 0.015f, -d * 0.5f - 0.02f), new Vector3(w + 0.04f, 0.03f, d + 0.03f), wood, false);
        foreach (float x in new[] { -0.22f, 0.22f })
        {
            Part(root, "Dvirka", new Vector3(x, h * 0.5f, -d - 0.02f), new Vector3(w * 0.5f - 0.03f, h - 0.1f, 0.015f), wood, false);
            Part(root, "Uchytka", new Vector3(x * 0.2f, h * 0.6f, -d - 0.035f), new Vector3(0.02f, 0.12f, 0.02f), metal, false);
        }
        // cerveny kriz na dvirkach (lekarnicka)
        var red = MapBuildKit.Mat(DecorFolder + "KrizCerveny.mat", null, new Color(0.8f, 0.1f, 0.1f), Vector2.one, 0.3f);
        Part(root, "Kriz", new Vector3(0f, h * 0.78f, -d - 0.03f), new Vector3(0.16f, 0.05f, 0.01f), red, false);
        Part(root, "Kriz", new Vector3(0f, h * 0.78f, -d - 0.03f), new Vector3(0.05f, 0.16f, 0.01f), red, false);

        // Tramal na skrince (hrac stoji na podlaze, balicek je ve vysce skrinky)
        var spot = new GameObject("Balicek_Tramal");
        spot.transform.SetParent(root, false);
        spot.transform.localPosition = new Vector3(0f, h + 0.03f, -d * 0.5f - 0.02f);
        var pickup = spot.AddComponent<PickupSpot>();
        pickup.kind = PickupKind.Tramal;
        pickup.reachBelow = h + 0.4f;

        Done(root);
    }

    // Material obrazu (vlastni - MapBuildKit.Mat by u jpg povazoval fotku za normalovou mapu).
    static Material PictureMaterial(string name, Texture2D texture)
    {
        string path = PictureFolder + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        material.SetTexture("_BaseMap", texture);
        material.mainTexture = texture;
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0.15f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void FrameBar(Transform root, Vector3 local, Vector3 size, Material material)
    {
        var go = MapBuildKit.Box(root, "Ram", root.TransformPoint(local), size, root.rotation, material, false);
        GameObjectUtility.SetStaticEditorFlags(go, 0);
    }

    // ---------------- klokani kuze ----------------

    [MenuItem("BrosOverwatch/Dekorace/Klokani kuze (zed nebo podlaha)")]
    static void KangarooHide()
    {
        if (!Aim(out var hit, out var cam)) return;
        var material = MapBuildKit.Mat(DecorFolder + "KlokaniKuze.mat", DecorFolder + "KlokaniKuze.png", Color.white, Vector2.one, 0.05f,
            alphaClip: true, doubleSided: true);
        const float height = 1.5f, width = height * 768f / 1024f;

        bool wall = Mathf.Abs(hit.normal.y) < 0.5f;
        Quaternion rotation;
        Vector3 position;
        if (wall)
        {
            // na zdi: hlava nahoru, srst do mistnosti
            rotation = Quaternion.LookRotation(-hit.normal, Vector3.up);
            position = hit.point + hit.normal * 0.02f;
        }
        else
        {
            // koberec: na podlaze, hlava od kamery
            Vector3 away = hit.point - cam.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            rotation = Quaternion.LookRotation(Vector3.down, away.normalized);
            position = hit.point + Vector3.up * 0.012f;
        }

        var root = NewRoot("KlokaniKuze", position, rotation);
        var hide = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(hide.GetComponent<Collider>());
        hide.name = "Kuze";
        hide.transform.SetParent(root, false);
        hide.transform.localScale = new Vector3(width, height, 1f);
        hide.GetComponent<MeshRenderer>().sharedMaterial = material;
        hide.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Done(root);
    }

    // ---------------- dreveny jeseter ----------------

    [MenuItem("BrosOverwatch/Dekorace/Dreveny jeseter (na podlahu, stul, policku)")]
    static void WoodenSturgeon()
    {
        if (!Aim(out var hit, out var cam)) return;
        Vector3 foot = hit.point;
        if (hit.normal.y < 0.7f && Physics.Raycast(hit.point + hit.normal * 0.6f, Vector3.down, out var floor, 10f, ~0, QueryTriggerInteraction.Ignore))
            foot = floor.point;

        // bokem ke kamere (ryba je videt z profilu)
        Vector3 toCamera = cam.position - foot;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude < 0.01f) toCamera = Vector3.back;
        var root = NewRoot("DrevenyJeseter", foot, Quaternion.LookRotation(Vector3.Cross(Vector3.up, toCamera.normalized), Vector3.up));

        var wood = MapBuildKit.Mat(DecorFolder + "DrevoJeseter.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.86f, 0.66f, 0.44f), new Vector2(1f, 3f), 0.35f);
        var dark = MapBuildKit.Mat(DecorFolder + "DrevoTmave.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.36f, 0.23f, 0.14f), Vector2.one, 0.3f);

        // podstavec: prkno a dva sloupky
        const float baseTop = 0.04f, postHeight = 0.16f;
        Part(root, "Podstavec", new Vector3(0f, baseTop * 0.5f, 0f), new Vector3(0.24f, baseTop, 0.95f), dark, true);
        foreach (float z in new[] { -0.22f, 0.2f })
            Part(root, "Sloupek", new Vector3(0f, baseTop + postHeight * 0.5f, z), new Vector3(0.03f, postHeight, 0.03f), dark, false);

        // ryba (mesh se ulozi jako asset), delka 1,2 m, celem po lokalni +z
        var mesh = SturgeonMesh();
        var fish = new GameObject("Jeseter");
        fish.transform.SetParent(root, false);
        fish.transform.localPosition = new Vector3(0f, baseTop + postHeight + 0.03f, 0f);
        fish.AddComponent<MeshFilter>().sharedMesh = mesh;
        fish.AddComponent<MeshRenderer>().sharedMaterial = wood;

        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.18f, 0f);
        box.size = new Vector3(0.3f, 0.36f, 1.25f);
        Done(root);
    }

    static void Part(Transform root, string name, Vector3 local, Vector3 size, Material material, bool collider)
    {
        var go = MapBuildKit.Box(root, name, root.TransformPoint(local), size, root.rotation, material, collider);
        GameObjectUtility.SetStaticEditorFlags(go, 0);
    }

    // Jeseter: telo jako protazeny sploštely kuzel (sirsi nez vyssi, plochejsi bricho), spicaty plochy rypec,
    // rady kostenych stitku (hrbet a boky), prsni a hrbetni ploutev a nesoumerna ocasni ploutev (horni lalok delsi).
    // Lokalni osy: +z = rypec, +y = hrbet. Delka 1,2 m.
    static Mesh SturgeonMesh()
    {
        const float length = 1.2f;
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        // profil: (t od rypce 0 po koren ocasu 1, polovicni sirka, polovicni vyska)
        var profile = new (float t, float w, float h)[]
        {
            (0.00f, 0.004f, 0.003f), (0.04f, 0.030f, 0.012f), (0.10f, 0.052f, 0.022f), (0.18f, 0.072f, 0.036f),
            (0.27f, 0.088f, 0.050f), (0.38f, 0.094f, 0.056f), (0.50f, 0.088f, 0.053f), (0.62f, 0.074f, 0.045f),
            (0.74f, 0.054f, 0.034f), (0.85f, 0.034f, 0.024f), (0.93f, 0.022f, 0.018f), (1.00f, 0.016f, 0.015f),
        };
        const int ring = 16;
        float Z(float t) => length * (0.5f - t);   // rypec vpredu (+z), ocas vzadu
        for (int i = 0; i < profile.Length; i++)
        {
            var (t, w, h) = profile[i];
            // rypec mirne nahoru, ocas taky (jeseter ma lehce prohnuty profil)
            float lift = 0.02f * Mathf.Pow(1f - Mathf.Clamp01(t / 0.2f), 2f) + 0.025f * Mathf.Pow(Mathf.Clamp01((t - 0.8f) / 0.2f), 2f);
            for (int k = 0; k <= ring; k++)
            {
                float a = k * Mathf.PI * 2f / ring;
                float s = Mathf.Sin(a);
                float y = s >= 0f ? h * s : h * 0.45f * s;   // ploche bricho
                // hrbet do mirne hrany
                if (s > 0f) y += h * 0.25f * Mathf.Pow(Mathf.Cos(a - Mathf.PI * 0.5f), 8f);
                vertices.Add(new Vector3(w * Mathf.Cos(a), y + lift, Z(t)));
                uvs.Add(new Vector2((float)k / ring, t * 3f));
            }
        }
        for (int i = 0; i < profile.Length - 1; i++)
            for (int k = 0; k < ring; k++)
            {
                int a = i * (ring + 1) + k, b = a + ring + 1;
                triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        // zaslepeni konce u ocasu
        int capCenter = vertices.Count;
        vertices.Add(new Vector3(0f, 0.025f, Z(1f)));
        uvs.Add(new Vector2(0.5f, 3f));
        int lastRing = (profile.Length - 1) * (ring + 1);
        for (int k = 0; k < ring; k++)
            triangles.AddRange(new[] { capCenter, lastRing + k + 1, lastRing + k });

        // kostene stitky: hrbet (11) a boky (2 x 14) - male kosoctverecne hrbolky
        for (int i = 0; i < 11; i++)
        {
            float t = 0.22f + i * 0.055f;
            Sample(profile, t, out float w, out float h);
            Scute(vertices, uvs, triangles, new Vector3(0f, h * 1.25f + 0.002f, Z(t)), Vector3.up, 0.018f, 0.012f);
        }
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 14; i++)
            {
                float t = 0.2f + i * 0.048f;
                Sample(profile, t, out float w, out float h);
                Scute(vertices, uvs, triangles, new Vector3(side * w * 0.98f, h * 0.15f, Z(t)), new Vector3(side, 0.15f, 0f).normalized, 0.012f, 0.008f);
            }

        // ploutve (tenke hranoly z trojuhelniku): prsni po stranach, hrbetni a ritni vzadu
        foreach (float side in new[] { -1f, 1f })
            Fin(vertices, uvs, triangles, new[] { new Vector3(side * 0.08f, -0.012f, Z(0.27f)), new Vector3(side * 0.19f, -0.03f, Z(0.40f)), new Vector3(side * 0.07f, -0.015f, Z(0.38f)) }, 0.006f);
        Fin(vertices, uvs, triangles, new[] { new Vector3(0f, 0.035f, Z(0.70f)), new Vector3(0f, 0.11f, Z(0.80f)), new Vector3(0f, 0.03f, Z(0.82f)) }, 0.006f, true);
        Fin(vertices, uvs, triangles, new[] { new Vector3(0f, -0.012f, Z(0.72f)), new Vector3(0f, -0.06f, Z(0.81f)), new Vector3(0f, -0.010f, Z(0.82f)) }, 0.006f, true);

        // ocas: horni lalok dlouhy a sikmo nahoru, dolni kratky
        Fin(vertices, uvs, triangles, new[] { new Vector3(0f, 0.03f, Z(0.97f)), new Vector3(0f, 0.17f, Z(1.17f)), new Vector3(0f, 0.005f, Z(1.03f)) }, 0.008f, true);
        Fin(vertices, uvs, triangles, new[] { new Vector3(0f, 0.0f, Z(0.98f)), new Vector3(0f, -0.08f, Z(1.07f)), new Vector3(0f, 0.012f, Z(1.03f)) }, 0.008f, true);

        // vousky pod rypcem (4)
        foreach (float x in new[] { -0.012f, -0.004f, 0.004f, 0.012f })
            Fin(vertices, uvs, triangles, new[] { new Vector3(x, -0.004f, Z(0.07f)), new Vector3(x * 1.4f, -0.035f, Z(0.075f)), new Vector3(x, -0.004f, Z(0.08f)) }, 0.003f, true);

        var mesh = new Mesh { name = "DrevenyJeseter" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return MapBuildKit.SaveMesh(mesh, "DrevenyJeseter");
    }

    static void Sample((float t, float w, float h)[] profile, float t, out float w, out float h)
    {
        for (int i = 0; i < profile.Length - 1; i++)
            if (t <= profile[i + 1].t)
            {
                float k = Mathf.InverseLerp(profile[i].t, profile[i + 1].t, t);
                w = Mathf.Lerp(profile[i].w, profile[i + 1].w, k);
                h = Mathf.Lerp(profile[i].h, profile[i + 1].h, k);
                return;
            }
        w = profile[profile.Length - 1].w;
        h = profile[profile.Length - 1].h;
    }

    // Kostěny stitek: nizky jehlan s kosoctverecnou zakladnou (delsi po delce ryby).
    static void Scute(List<Vector3> v, List<Vector2> uv, List<int> tri, Vector3 center, Vector3 normal, float size, float height)
    {
        Vector3 along = Vector3.forward;
        Vector3 across = Vector3.Cross(normal, along).normalized;
        int s = v.Count;
        v.Add(center + normal * height);
        v.Add(center + along * size * 1.4f);
        v.Add(center + across * size);
        v.Add(center - along * size * 1.4f);
        v.Add(center - across * size);
        for (int i = 0; i < 5; i++) uv.Add(new Vector2(i * 0.2f, center.z));
        for (int i = 0; i < 4; i++)
        {
            int a = s + 1 + i, b = s + 1 + (i + 1) % 4;
            tri.AddRange(new[] { s, b, a });
            tri.AddRange(new[] { s, a, b });   // oboustranne (stitek nikdy nezmizi pri pohledu z boku)
        }
    }

    // Ploutev: trojuhelnik vytazeny do tloustky 'thickness' (bocni ploutve do vysky, svisle do stran).
    static void Fin(List<Vector3> v, List<Vector2> uv, List<int> tri, Vector3[] p, float thickness, bool vertical = false)
    {
        Vector3 offset = (vertical ? Vector3.right : Vector3.up) * thickness * 0.5f;
        int s = v.Count;
        for (int i = 0; i < 3; i++) { v.Add(p[i] + offset); uv.Add(new Vector2(p[i].z * 3f, p[i].y * 3f)); }
        for (int i = 0; i < 3; i++) { v.Add(p[i] - offset); uv.Add(new Vector2(p[i].z * 3f, p[i].y * 3f)); }
        tri.AddRange(new[] { s, s + 1, s + 2, s + 3, s + 5, s + 4 });
        tri.AddRange(new[] { s, s + 2, s + 1, s + 3, s + 4, s + 5 });   // obe strany (ploutev je tenka)
        for (int i = 0; i < 3; i++)
        {
            int a = s + i, b = s + (i + 1) % 3, c = a + 3, d = b + 3;
            tri.AddRange(new[] { a, c, b, b, c, d });
        }
    }

    // ---------------- gril na drevo ----------------

    // Gril na drevo s horicim ohnem. Vybrany kvadr (zidany podstavec) = gril se postavi na jeho vrch; nic vybraneho =
    // stred okna Scene na zem a postavi se i kamenny podstavec. Ocelova vana, polena, zhave uhliky, plameny, rost
    // na nozkach nad ohnem a svetlo. Animaci (vlneni plamenu, blikani, jiskry, kour) dela za hry GrillFire.
    [MenuItem("BrosOverwatch/Dekorace/Gril na drevo (vyber podstavec, nebo mir na zem)")]
    static void WoodGrill()
    {
        Transform root;
        float length, width, baseY = 0f;
        var target = Selection.activeGameObject;
        var filter = target != null ? target.GetComponent<MeshFilter>() : null;
        var targetRenderer = target != null ? target.GetComponent<Renderer>() : null;
        if (filter != null && filter.sharedMesh != null && targetRenderer != null)
        {
            var t = target.transform;
            Bounds b = filter.sharedMesh.bounds;
            Vector3 size = Vector3.Scale(b.size, t.lossyScale);
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            Vector3 center = t.TransformPoint(b.center);
            Vector3 axis = size.x >= size.z ? t.right : t.forward;
            axis.y = 0f;
            axis.Normalize();
            length = Mathf.Max(size.x, size.z);
            width = Mathf.Min(size.x, size.z);
            var rotation = Quaternion.LookRotation(Vector3.Cross(axis, Vector3.up), Vector3.up);   // lokalni +x = delka
            root = NewRoot("GrilNaDrevo", new Vector3(center.x, targetRenderer.bounds.max.y, center.z), rotation);
        }
        else
        {
            if (!Aim(out var hit, out var cam)) return;
            if (hit.normal.y < 0.7f)
            {
                Debug.LogWarning("[Dekorace] Bez vyberu se gril stavi na zem - zamir stred okna Scene na zem (nebo vyber podstavec).");
                return;
            }
            Vector3 toCam = cam.position - hit.point;
            toCam.y = 0f;
            if (toCam.sqrMagnitude < 0.01f) toCam = Vector3.back;
            root = NewRoot("GrilNaDrevo", hit.point, Quaternion.LookRotation(-toCam.normalized, Vector3.up));
            length = 1.3f;
            width = 0.85f;
            baseY = 0.8f;
            var stone = MapBuildKit.Mat(DecorFolder + "GrilKamen.mat", "Assets/Materials/Stone/StoneWall.png", new Color(0.85f, 0.82f, 0.76f), new Vector2(1.5f, 1f), 0.15f);
            Part(root, "Podstavec", new Vector3(0f, baseY * 0.5f, 0f), new Vector3(length, baseY, width), stone, true);
        }

        var steel = MapBuildKit.Mat(DecorFolder + "GrilOcel.mat", null, new Color(0.13f, 0.13f, 0.14f), Vector2.one, 0.45f);
        var rust = MapBuildKit.Mat(DecorFolder + "GrilVana.mat", null, new Color(0.24f, 0.16f, 0.12f), Vector2.one, 0.2f);
        var ash = MapBuildKit.Mat(DecorFolder + "GrilPopel.mat", null, new Color(0.22f, 0.21f, 0.2f), Vector2.one, 0.05f);
        var log = MapBuildKit.Mat(DecorFolder + "GrilPoleno.mat", "Assets/Materials/Wood/WoodBeam.png", new Color(0.42f, 0.3f, 0.2f), Vector2.one, 0.1f);
        var charred = MapBuildKit.Mat(DecorFolder + "GrilOhorele.mat", null, new Color(0.07f, 0.06f, 0.05f), Vector2.one, 0.05f,
            emission: new Color(0.9f, 0.18f, 0.02f) * 0.35f);
        var ember = MapBuildKit.Mat(DecorFolder + "GrilUhlik.mat", null, new Color(0.15f, 0.05f, 0.02f), Vector2.one, 0.1f,
            emission: new Color(1f, 0.3f, 0.04f) * 1.6f);
        var flameOuter = MapBuildKit.Mat(DecorFolder + "GrilPlamen.mat", null, new Color(1f, 0.45f, 0.1f), Vector2.one, 0f,
            emission: new Color(4f, 1.3f, 0.2f));
        var flameInner = MapBuildKit.Mat(DecorFolder + "GrilPlamenJadro.mat", null, new Color(1f, 0.85f, 0.4f), Vector2.one, 0f,
            emission: new Color(4.5f, 3.2f, 0.9f));

        // ocelova vana s okrajem
        float tl = length * 0.82f, tw = width * 0.78f;
        const float rim = 0.12f, wall = 0.025f;
        Part(root, "VanaDno", new Vector3(0f, baseY + 0.01f, 0f), new Vector3(tl, 0.02f, tw), rust, false);
        foreach (float s in new[] { -1f, 1f })
        {
            Part(root, "VanaBok", new Vector3(0f, baseY + rim * 0.5f, s * (tw - wall) * 0.5f), new Vector3(tl, rim, wall), rust, false);
            Part(root, "VanaCelo", new Vector3(s * (tl - wall) * 0.5f, baseY + rim * 0.5f, 0f), new Vector3(wall, rim, tw - wall * 2f), rust, false);
        }
        Part(root, "Popel", new Vector3(0f, baseY + 0.03f, 0f), new Vector3(tl - wall * 2f, 0.02f, tw - wall * 2f), ash, false);

        // zhave uhliky (pulzuji)
        var embers = new GameObject("Uhliky").transform;
        embers.SetParent(root, false);
        var random = new System.Random(11);
        float R() => (float)random.NextDouble();
        for (int i = 0; i < 22; i++)
        {
            var local = new Vector3((R() - 0.5f) * (tl - 0.1f), baseY + 0.045f + R() * 0.02f, (R() - 0.5f) * (tw - 0.1f));
            float s = 0.04f + R() * 0.05f;
            var go = MapBuildKit.Box(embers, "Uhlik", root.TransformPoint(local), new Vector3(s, s * 0.6f, s * (0.8f + R() * 0.5f)),
                root.rotation * Quaternion.Euler(0f, R() * 180f, R() * 20f), i % 3 == 0 ? charred : ember, false);
            GameObjectUtility.SetStaticEditorFlags(go, 0);
        }

        // polena: dve dole napric, dve navrch nakoso
        float logL = Mathf.Min(tl - 0.08f, 0.6f);
        Vector3 P(float x, float y, float z) => root.TransformPoint(new Vector3(x, baseY + y, z));
        MapBuildKit.Beam(root, "Poleno", P(-logL * 0.5f, 0.07f, -tw * 0.18f), P(logL * 0.5f, 0.07f, -tw * 0.12f), 0.08f, log);
        MapBuildKit.Beam(root, "Poleno", P(-logL * 0.5f, 0.07f, tw * 0.14f), P(logL * 0.5f, 0.07f, tw * 0.2f), 0.075f, log);
        MapBuildKit.Beam(root, "PolenoOhorele", P(-logL * 0.32f, 0.12f, -tw * 0.32f), P(logL * 0.05f, 0.15f, tw * 0.32f), 0.07f, charred);
        MapBuildKit.Beam(root, "PolenoOhorele", P(logL * 0.32f, 0.12f, -tw * 0.3f), P(-logL * 0.02f, 0.155f, tw * 0.3f), 0.07f, charred);

        // plameny: vnejsi oranzove kuzele, uvnitr nizsi zlute jadro
        var flames = new GameObject("Plameny").transform;
        flames.SetParent(root, false);
        flames.localPosition = new Vector3(0f, baseY + 0.06f, 0f);
        int count = Mathf.Clamp(Mathf.RoundToInt(tl / 0.12f), 4, 9);
        for (int i = 0; i < count; i++)
        {
            float x = (i + 0.5f) / count - 0.5f;
            var local = new Vector3(x * (tl - 0.14f), 0f, (R() - 0.5f) * tw * 0.4f);
            float r = 0.06f + R() * 0.05f, h = 0.22f + R() * 0.2f * (1f - Mathf.Abs(x));
            var outer = MapBuildKit.MeshObject(flames, "Plamen", MapBuildKit.ConeMesh(), flames.TransformPoint(local),
                root.rotation * Quaternion.Euler(0f, R() * 360f, 0f), new Vector3(r, h, r), flameOuter, false);
            GameObjectUtility.SetStaticEditorFlags(outer, 0);
            var inner = MapBuildKit.MeshObject(flames, "PlamenJadro", MapBuildKit.ConeMesh(), flames.TransformPoint(local + new Vector3(0f, 0.005f, 0f)),
                root.rotation * Quaternion.Euler(0f, R() * 360f, 0f), new Vector3(r * 0.6f, h * 0.6f, r * 0.6f), flameInner, false);
            GameObjectUtility.SetStaticEditorFlags(inner, 0);
        }

        // rost na ctyrech nozkach nad ohnem
        float gy = 0.34f, gl = tl + 0.02f, gw = tw + 0.02f;
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                Part(root, "NozkaRostu", new Vector3(sx * (gl * 0.5f - 0.02f), baseY + gy * 0.5f, sz * (gw * 0.5f - 0.02f)), new Vector3(0.025f, gy, 0.025f), steel, false);
        foreach (float s in new[] { -1f, 1f })
        {
            Part(root, "RamRostu", new Vector3(0f, baseY + gy, s * gw * 0.5f), new Vector3(gl, 0.025f, 0.02f), steel, false);
            Part(root, "RamRostu", new Vector3(s * gl * 0.5f, baseY + gy, 0f), new Vector3(0.02f, 0.025f, gw), steel, false);
        }
        int rods = Mathf.RoundToInt(gl / 0.05f);
        for (int i = 1; i < rods; i++)
            Part(root, "Pricka", new Vector3(-gl * 0.5f + gl * i / rods, baseY + gy, 0f), new Vector3(0.008f, 0.008f, gw), steel, false);
        // rukojeti na celech
        foreach (float s in new[] { -1f, 1f })
            Part(root, "Rukojet", new Vector3(s * (gl * 0.5f + 0.05f), baseY + gy, 0f), new Vector3(0.1f, 0.02f, 0.14f), steel, false);

        // svetlo ohne
        var lightGo = new GameObject("SvetloOhne");
        lightGo.transform.SetParent(root, false);
        lightGo.transform.localPosition = new Vector3(0f, baseY + 0.5f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.55f, 0.2f);
        light.range = 6f;
        light.intensity = 2.2f;
        light.shadows = LightShadows.None;

        var fire = root.gameObject.AddComponent<GrillFire>();
        fire.flames = flames;
        fire.embers = embers;
        fire.fireLight = light;
        fire.baseIntensity = light.intensity;
        fire.size = Mathf.Max(0.4f, tl);
        fire.fireboxDepth = tw;

        // kolize = vana + rost (plameny a jiskry prochazi)
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, baseY + gy * 0.5f + 0.01f, 0f);
        box.size = new Vector3(gl, gy + 0.02f, gw);
        Done(root);
    }

    // ---------------- spolecne ----------------

    static bool Aim(out RaycastHit hit, out Transform cam)
    {
        hit = default;
        cam = null;
        var view = SceneView.lastActiveSceneView;
        if (view == null)
        {
            Debug.LogWarning("[Dekorace] Otevri okno Scene a podivej se na misto, kam ma dekorace prijit.");
            return false;
        }
        cam = view.camera.transform;
        Physics.SyncTransforms();
        if (!Physics.Raycast(cam.position, cam.forward, out hit, 200f, ~0, QueryTriggerInteraction.Ignore))
        {
            Debug.LogWarning("[Dekorace] Stred okna Scene nemiri na zadny povrch.");
            return false;
        }
        return true;
    }

    static Transform NewRoot(string name, Vector3 position, Quaternion rotation)
    {
        Transform parent = null;
        var map = GameObject.Find("Map-Domasov");
        if (map != null)
        {
            parent = map.transform.Find("Dekorace");
            if (parent == null)
            {
                var group = new GameObject("Dekorace");
                Undo.RegisterCreatedObjectUndo(group, "Vlozit dekoraci");
                group.transform.SetParent(map.transform, false);
                parent = group.transform;
            }
        }
        var root = new GameObject(name).transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Vlozit dekoraci");
        if (parent != null) root.SetParent(parent, false);
        root.SetPositionAndRotation(position, rotation);
        return root;
    }

    static void Done(Transform root)
    {
        Selection.activeGameObject = root.gameObject;
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Debug.Log($"[Dekorace] {root.name} vlozen(a). Posun/otoc podle potreby (W/E) a uloz scenu (Ctrl+S).");
    }
}
