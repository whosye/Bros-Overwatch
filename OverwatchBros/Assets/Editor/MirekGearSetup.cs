using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Mirkuv luk, sip a toulec ze Sketchfabu (Assets/Models/Weapons/MirekGear - viz CREDITS.txt). Spousti se samo
// (po nacteni skriptu a po skonceni hry): kdyz prefaby chybi, vytvori je a priradi - luk se sipem na tetive
// (Bow_Data.heldPrefab), letici sip (Bow_Data.projectilePrefab) a toulec na zada (Mirek.backPrefab).
// Existujici prefaby a rucne prirazene modely se neprepisuji; znovu vytvorit: smazat prefab (nebo menu nize).
// Osy se odvodi z tvaru modelu (bez znacek Grip/Muzzle/Top):
//   luk  - nejdelsi osa = ramena (+Y), predek (k cili) = strana madla proti koncum ramen, madlo do (0; 0; 0,10)
//   sip  - nejdelsi osa = +Z, hrot = konec s kratsim sirokym dilem (letky jsou delsi)
//   toulec - nejdelsi osa = +Y (nahoru sirsi konec), +Z = plocha strana k zadum
[InitializeOnLoad]
public static class MirekGearSetup
{
    const string Dir = "Assets/Models/Weapons/MirekGear";
    const string PrefabDir = "Assets/Prefab/Weapons";
    const string BowPrefab = PrefabDir + "/Held_BlackBow.prefab";
    const string ArrowPrefab = PrefabDir + "/Projectile_MirekArrow.prefab";
    const string QuiverPrefab = PrefabDir + "/Back_MirekQuiver.prefab";
    const string WeaponPath = "Assets/Data/Bow_Data.asset";
    const string HeroPath = "Assets/Resources/Heroes/Mirek.asset";

    const float BowHeight = 1.1f;       // luk od konce ke konci (m)
    const float ArrowLength = 0.85f;    // sip (m)
    const float ProjectileTip = 0.42f;  // letici sip: hrot pred pocatkem (jako puvodni ArrowProjectile)
    const float QuiverLength = 0.75f;   // toulec (m)
    static readonly Vector3 BowGrip = new Vector3(0f, 0f, 0.10f);   // HeldWeapons.GripInWeapon(Bow)
    const float ArrowRestAbove = 0.06f, BraceHeight = 0.17f;         // jako HeldWeapons (sip na tetive)

    static MirekGearSetup()
    {
        EditorApplication.delayCall += AutoSetup;
        EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += AutoSetup; };
    }

    static bool rebuildQuiver;

    static void AutoSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!File.Exists(Dir + "/BlackBow.fbx")) return;
        // toulec z drivejsi verze bez sipu -> vytvorit znovu (prirazeni i nastaveni polohy na hrdinovi zustanou)
        var oldQuiver = AssetDatabase.LoadAssetAtPath<GameObject>(QuiverPrefab);
        rebuildQuiver = oldQuiver != null && oldQuiver.transform.Find(QuiverArrowsGroup) == null;
        bool missing = !File.Exists(BowPrefab) || !File.Exists(ArrowPrefab) || !File.Exists(QuiverPrefab) || rebuildQuiver;
        if (!missing) { Assign(false); return; }
        Build();
    }

    [MenuItem("BrosOverwatch/Zbrane/Mirek - luk, sip a toulec znovu vytvorit")]
    static void Rebuild() { Build(); Assign(true); }

    static void Build()
    {
        foreach (var model in new[] { "BlackBow", "Arrow", "Quiver" }) SetupImport(model);
        if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets/Prefab", "Weapons");

        var arrowMat = Material("Arrow");
        if (!File.Exists(BowPrefab)) BuildBow(Material("BlackBow"), arrowMat);
        if (!File.Exists(ArrowPrefab)) BuildArrowProjectile(arrowMat);
        if (!File.Exists(QuiverPrefab) || rebuildQuiver) BuildQuiver(Material("Quiver"), arrowMat);   // (prepis na miste - stejne GUID)
        rebuildQuiver = false;
        AssetDatabase.SaveAssets();
        Assign(false);
    }

    // prirazeni: jen kdyz neni nic, nebo je tam puvodni model (rucni zmenu nechat)
    static void Assign(bool force)
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>(HeroPath);
        var bow = AssetDatabase.LoadAssetAtPath<GameObject>(BowPrefab);
        var arrow = AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPrefab);
        var quiver = AssetDatabase.LoadAssetAtPath<GameObject>(QuiverPrefab);
        bool changed = false;
        if (weapon != null && bow != null && weapon.heldPrefab != bow && (force || weapon.heldPrefab == null || IsOriginal(weapon.heldPrefab, "Held_Bow")))
        { weapon.heldPrefab = bow; weapon.heldScale = 1f; EditorUtility.SetDirty(weapon); changed = true; }
        if (weapon != null && arrow != null && weapon.projectilePrefab != arrow && (force || weapon.projectilePrefab == null || IsOriginal(weapon.projectilePrefab, "Held_ArrowProjectile")))
        { weapon.projectilePrefab = arrow; EditorUtility.SetDirty(weapon); changed = true; }
        if (hero != null && quiver != null && hero.backPrefab == null)
        { hero.backPrefab = quiver; EditorUtility.SetDirty(hero); changed = true; }
        if (changed)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("[MirekGear] Mirek: luk (Black Bow), sip a toulec prirazeny.");
        }
    }

    static bool IsOriginal(GameObject prefab, string prefix) => Path.GetFileName(AssetDatabase.GetAssetPath(prefab)).StartsWith(prefix);

    // ---------------- import a materialy ----------------

    static void SetupImport(string model)
    {
        if (AssetImporter.GetAtPath($"{Dir}/{model}.fbx") is ModelImporter importer)
        {
            bool dirty = false;
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            if (!importer.isReadable) { importer.isReadable = true; dirty = true; }
            if (importer.importAnimation) { importer.importAnimation = false; dirty = true; }
            if (dirty) importer.SaveAndReimport();
        }
        Texture($"{Dir}/{model}_BaseColor.png", TextureImporterType.Default, true);
        Texture($"{Dir}/{model}_Normal.png", TextureImporterType.NormalMap, false);
        Texture($"{Dir}/{model}_MetallicSmoothness.png", TextureImporterType.Default, false);
        Texture($"{Dir}/{model}_Emission.png", TextureImporterType.Default, true);
    }

    static void Texture(string path, TextureImporterType type, bool srgb)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter t)) return;
        if (t.textureType == type && t.sRGBTexture == srgb) return;
        t.textureType = type;
        t.sRGBTexture = srgb;
        t.SaveAndReimport();
    }

    static Material Material(string model)
    {
        string path = $"{Dir}/{model}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{model}_BaseColor.png"));
        material.SetColor("_BaseColor", Color.white);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{model}_Normal.png");
        if (normal != null) { material.SetTexture("_BumpMap", normal); material.EnableKeyword("_NORMALMAP"); }
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{model}_MetallicSmoothness.png");
        if (mask != null)
        {
            material.SetTexture("_MetallicGlossMap", mask);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
        }
        var emission = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{model}_Emission.png");
        if (emission != null)
        {
            material.SetTexture("_EmissionMap", emission);
            material.SetColor("_EmissionColor", Color.white);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------------- prefaby ----------------

    static GameObject Load(string model) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{model}.fbx");

    // Instance modelu pod novym korenem, s materialem a bez kolizi.
    static GameObject Instance(string model, Transform parent, Material material, string name)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load(model), parent);
        instance.name = name;
        foreach (var r in instance.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
        }
        foreach (var c in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        return instance;
    }

    static List<Vector3> Points(GameObject go, System.Func<Transform, bool> include = null)
    {
        var list = new List<Vector3>();
        foreach (var f in go.GetComponentsInChildren<MeshFilter>())
        {
            if (f.sharedMesh == null || (include != null && !include(f.transform))) continue;
            var m = f.transform.localToWorldMatrix;
            foreach (var v in f.sharedMesh.vertices) list.Add(m.MultiplyPoint3x4(v));
        }
        return list;
    }

    static Bounds BoundsOf(List<Vector3> pts)
    {
        var b = new Bounds(pts[0], Vector3.zero);
        foreach (var p in pts) b.Encapsulate(p);
        return b;
    }

    static int LongAxis(Vector3 s) => s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2;
    static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

    // Otoci instanci tak, aby 'forward' a 'up' (v prostoru korene) mirily na +Z a +Y.
    static void Orient(Transform t, Vector3 forward, Vector3 up)
    {
        var rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up));
        t.SetPositionAndRotation(rotation * t.position, rotation * t.rotation);
    }

    // Zmensi kolem pocatku a posune tak, aby bod 'from' (v prostoru korene, po zmenseni) byl v 'to'.
    static void ScaleAndMove(Transform t, float scale, Vector3 from, Vector3 to)
    {
        t.SetPositionAndRotation(t.position * scale, t.rotation);
        t.localScale *= scale;
        t.position += to - from * scale;
    }

    // Sip v prostoru korene: osa +Z hrotem dopredu, delka 'length', stred v 'center'.
    static void PlaceArrow(GameObject arrow, float length, Vector3 center)
    {
        var pts = Points(arrow);
        var b = BoundsOf(pts);
        int axis = LongAxis(b.size);
        Vector3 along = Axis(axis);
        float L = b.size[axis];
        // hrot = konec s kratsim sirokym dilem
        float lo = b.min[axis];
        int slices = 50;
        var width = new float[slices];
        foreach (var p in pts)
        {
            int s = Mathf.Clamp((int)((p[axis] - lo) / L * slices), 0, slices - 1);
            Vector3 off = p - b.center; off[axis] = 0f;
            width[s] = Mathf.Max(width[s], off.magnitude);
        }
        float max = 0f; foreach (var w in width) max = Mathf.Max(max, w);
        int fromLow = 0; while (fromLow < slices && width[fromLow] > max * 0.5f) fromLow++;
        int fromHigh = 0; while (fromHigh < slices && width[slices - 1 - fromHigh] > max * 0.5f) fromHigh++;
        Vector3 forward = fromLow <= fromHigh ? -along : along;   // kratsi siroky usek = hrot
        Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.9f ? Vector3.up : Vector3.forward;
        Orient(arrow.transform, forward, up);
        b = BoundsOf(Points(arrow));
        ScaleAndMove(arrow.transform, length / Mathf.Max(1e-5f, b.size.z), b.center, center);
    }

    static void BuildBow(Material bowMat, Material arrowMat)
    {
        var root = new GameObject("Held_BlackBow");
        try
        {
            var bow = Instance("BlackBow", root.transform, bowMat, "BlackBow");
            bool IsString(Transform t) => t.name.IndexOf("string", System.StringComparison.OrdinalIgnoreCase) >= 0;
            var pts = Points(bow, t => !IsString(t));
            var b = BoundsOf(pts);
            int axis = LongAxis(b.size);
            Vector3 limbs = Axis(axis);
            // hloubka luku = vetsi z ostatnich os; madlo (stred delky) vs konce ramen
            int other1 = (axis + 1) % 3, other2 = (axis + 2) % 3;
            int depth = b.size[other1] >= b.size[other2] ? other1 : other2;
            float mid = b.center[axis], L = b.size[axis];
            float gripDepth = 0f, tipDepth = 0f; int gn = 0, tn = 0;
            foreach (var p in pts)
            {
                float d = Mathf.Abs(p[axis] - mid) / L;
                if (d < 0.05f) { gripDepth += p[depth]; gn++; }
                else if (d > 0.42f) { tipDepth += p[depth]; tn++; }
            }
            gripDepth /= Mathf.Max(1, gn); tipDepth /= Mathf.Max(1, tn);
            Vector3 forward = Axis(depth) * (gripDepth >= tipDepth ? 1f : -1f);   // k cili = od koncu k madlu
            Orient(bow.transform, forward, limbs);

            pts = Points(bow, t => !IsString(t));
            b = BoundsOf(pts);
            Vector3 grip = Vector3.zero; int n = 0;
            foreach (var p in pts) if (Mathf.Abs(p.y - b.center.y) < b.size.y * 0.05f) { grip += p; n++; }
            grip = n > 0 ? grip / n : b.center;
            ScaleAndMove(bow.transform, BowHeight / Mathf.Max(1e-5f, b.size.y), grip, BowGrip);

            // sip na tetive (HeldWeapons hleda dite "Arrow"): stred nad madlem, konec na tetive za madlem
            var arrow = Instance("Arrow", root.transform, arrowMat, "Arrow");
            PlaceArrow(arrow, ArrowLength, new Vector3(-0.02f, BowGrip.y + ArrowRestAbove, BowGrip.z - BraceHeight + ArrowLength * 0.5f));

            PrefabUtility.SaveAsPrefabAsset(root, BowPrefab);
            Debug.Log($"[MirekGear] Luk: ramena podel {"XYZ"[axis]}, k cili {(gripDepth >= tipDepth ? "+" : "-")}{"XYZ"[depth]} -> {BowPrefab}");
        }
        finally { Object.DestroyImmediate(root); }
    }

    static void BuildArrowProjectile(Material arrowMat)
    {
        var root = new GameObject("Projectile_MirekArrow");
        try
        {
            var arrow = Instance("Arrow", root.transform, arrowMat, "Arrow");
            PlaceArrow(arrow, ArrowLength, new Vector3(0f, 0f, ProjectileTip - ArrowLength * 0.5f));
            foreach (var r in arrow.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            PrefabUtility.SaveAsPrefabAsset(root, ArrowPrefab);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // sipy v toulci: hroty dolu, letky nad okrajem, lehce do vejire (x, z = posun v otvoru v m; naklon ve stupnich)
    static readonly (float x, float z, float tiltX, float tiltZ, float above)[] QuiverArrows =
    {
        (0f, 0f, 0f, 0f, 0.26f),
        (0.035f, 0.015f, -3f, -6f, 0.23f),
        (-0.035f, 0.012f, -2f, 6f, 0.24f),
        (0.018f, -0.025f, 5f, -3f, 0.21f),
        (-0.02f, -0.022f, 4f, 4f, 0.25f),
    };

    // nazev skupiny sipu = verze toulce (jina verze -> toulec se sam prestavi)
    const string QuiverArrowsGroup = "Sipy_v2";

    static void BuildQuiver(Material quiverMat, Material arrowMat)
    {
        var root = new GameObject("Back_MirekQuiver");
        try
        {
            var quiver = Instance("Quiver", root.transform, quiverMat, "Quiver");
            var pts = Points(quiver);
            var b = BoundsOf(pts);
            int axis = LongAxis(b.size);
            float lo = b.min[axis], L = b.size[axis];
            // nahoru = sirsi konec (otvor se sipy)
            float wLow = 0f, wHigh = 0f;
            foreach (var p in pts)
            {
                float t = (p[axis] - lo) / L;
                Vector3 off = p - b.center; off[axis] = 0f;
                if (t < 0.2f) wLow = Mathf.Max(wLow, off.magnitude);
                else if (t > 0.8f) wHigh = Mathf.Max(wHigh, off.magnitude);
            }
            Vector3 up = Axis(axis) * (wHigh >= wLow ? 1f : -1f);
            // k zadum = plossi strana mensi z ostatnich os (vic vrcholu u krajni roviny)
            int other1 = (axis + 1) % 3, other2 = (axis + 2) % 3;
            int depth = b.size[other1] <= b.size[other2] ? other1 : other2;
            int nearMax = 0, nearMin = 0;
            foreach (var p in pts)
            {
                if (p[depth] > b.max[depth] - b.size[depth] * 0.05f) nearMax++;
                if (p[depth] < b.min[depth] + b.size[depth] * 0.05f) nearMin++;
            }
            Vector3 toBack = Axis(depth) * (nearMax >= nearMin ? 1f : -1f);
            Orient(quiver.transform, toBack, up);
            b = BoundsOf(Points(quiver));
            ScaleAndMove(quiver.transform, QuiverLength / Mathf.Max(1e-5f, b.size.y), b.center, Vector3.zero);

            // otvor = stred vrcholu u horniho okraje
            pts = Points(quiver);
            b = BoundsOf(pts);
            Vector3 mouth = Vector3.zero; int n = 0;
            foreach (var p in pts) if (p.y > b.max.y - b.size.y * 0.08f) { mouth += p; n++; }
            mouth = n > 0 ? mouth / n : new Vector3(b.center.x, b.max.y, b.center.z);
            mouth.y = b.max.y;
            float radius = Mathf.Min(b.size.x, b.size.z) * 0.5f;

            var group = new GameObject(QuiverArrowsGroup).transform;
            group.SetParent(root.transform, false);
            for (int i = 0; i < QuiverArrows.Length; i++)
            {
                var a = QuiverArrows[i];
                // drzak: +Z drzaku miri dolu do toulce (hrotem), lehce vykloneny
                var holder = new GameObject("Sip" + (i + 1)).transform;
                holder.SetParent(group, false);
                Vector3 at = mouth + new Vector3(Mathf.Clamp(a.x, -radius * 0.6f, radius * 0.6f), 0f, Mathf.Clamp(a.z, -radius * 0.6f, radius * 0.6f));
                holder.localPosition = at;
                holder.localRotation = Quaternion.Euler(a.tiltX, 0f, a.tiltZ) * Quaternion.Euler(90f, 0f, i * 72f);
                // (srovnat pod nenatocenym korenem - PlaceArrow pocita v jeho osach - a pak vlozit do drzaku)
                var arrow = Instance("Arrow", root.transform, arrowMat, "Arrow");
                PlaceArrow(arrow, ArrowLength, Vector3.zero);
                arrow.transform.SetParent(holder, false);
                // obracene (na prani): druhy konec sipu nahoru z toulce
                arrow.transform.localRotation = Quaternion.AngleAxis(180f, Vector3.right) * arrow.transform.localRotation;
                arrow.transform.localPosition = Quaternion.AngleAxis(180f, Vector3.right) * arrow.transform.localPosition;
                // stred sipu pod okrajem tak, aby letky vycnivaly o 'above'
                arrow.transform.localPosition += Vector3.forward * (ArrowLength * 0.5f - a.above);
            }
            PrefabUtility.SaveAsPrefabAsset(root, QuiverPrefab);
            Debug.Log($"[MirekGear] Toulec: delka podel {"XYZ"[axis]}, nahoru {(wHigh >= wLow ? "+" : "-")}, k zadum {(nearMax >= nearMin ? "+" : "-")}{"XYZ"[depth]} -> {QuiverPrefab}");
        }
        finally { Object.DestroyImmediate(root); }
    }
}
