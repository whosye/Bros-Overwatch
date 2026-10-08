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
        if (Mathf.Abs(hit.normal.y) > 0.5f)
        {
            Debug.LogWarning("[Dekorace] Obraz se vesi na zed - stred okna Scene musi mirit na zed.");
            return;
        }

        string name = System.IO.Path.GetFileNameWithoutExtension(texturePath);
        var root = NewRoot(name, hit.point + hit.normal * 0.025f, Quaternion.LookRotation(-hit.normal, Vector3.up));
        BuildPicture(root, name, texture, PictureHeight);
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
