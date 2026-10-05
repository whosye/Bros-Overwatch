using UnityEditor;
using UnityEngine;

// Postavy hrdinu podle fotky (MetaPerson export, Assets/Models/<Hrdina>/<Hrdina>Avatar.fbx, licence non-commercial):
// nastavi Humanoid import, vytahne textury z FBX, vytvori prefab a priradi ho hrdinovi misto obecne postavy.
public static class AyranAvatarSetup
{
    class Entry
    {
        public string hero;
        public Color sleeve;   // barva rukavu v nahradnich rukach z prvni osoby
        public Color skin;

        public string Fbx => $"Assets/Models/{hero}/{hero}Avatar.fbx";
        public string TextureDir => $"Assets/Models/{hero}/Textures";
        public string PrefabPath => $"Assets/Prefab/Characters/Char_{hero}_Avatar.prefab";
        public string HeroPath => $"Assets/Resources/Heroes/{hero}.asset";
        public bool Present => System.IO.File.Exists(Fbx);
    }

    static readonly Entry[] Entries =
    {
        // Cervena mikina.
        new Entry { hero = "Ayran", sleeve = new Color(0.62f, 0.13f, 0.12f, 1f), skin = new Color(0.80f, 0.60f, 0.48f) },
        // Svetle modre tricko s potiskem (kratky rukav).
        new Entry { hero = "Honza", sleeve = new Color(0.70f, 0.80f, 0.92f, 1f), skin = new Color(0.82f, 0.63f, 0.52f) },
        // Cerne tricko (kratky rukav).
        new Entry { hero = "Viktor", sleeve = new Color(0.10f, 0.10f, 0.12f, 1f), skin = new Color(0.80f, 0.62f, 0.50f) },
        // Cerny oblek (Jules z Pulp Fiction).
        new Entry { hero = "Sindel", sleeve = new Color(0.07f, 0.07f, 0.08f, 1f), skin = new Color(0.88f, 0.72f, 0.60f) },
    };

    public static bool IsReady()
    {
        foreach (var entry in Entries)
            if (!IsReady(entry)) return false;

        return true;
    }

    static bool IsReady(Entry entry)
    {
        if (!entry.Present) return true;

        var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>(entry.HeroPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
        return hero != null && prefab != null && hero.characterPrefab == prefab && !hero.tintCharacter && hero.sleeveColor == entry.sleeve
            && !PrefabIsStale(entry);
    }

    // Otisk souboru modelu (velikost + hash obsahu): podle nej se pozna, ze uzivatel model vymenil nebo upravil.
    // Zamerne ne cas zmeny souboru - ten se meni i pri kopirovani projektu nebo stazeni z gitu.
    static readonly System.Collections.Generic.Dictionary<string, string> stampCache = new System.Collections.Generic.Dictionary<string, string>();

    static string Stamp(Entry entry)
    {
        var info = new System.IO.FileInfo(entry.Fbx);
        string key = $"{entry.Fbx}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        if (stampCache.TryGetValue(key, out string cached)) return cached;

        using (var md5 = System.Security.Cryptography.MD5.Create())
        using (var stream = System.IO.File.OpenRead(entry.Fbx))
        {
            string hash = System.BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            string stamp = $"{info.Length}:{hash}";
            stampCache[key] = stamp;
            return stamp;
        }
    }

    // Otisk v soucasnem tvaru "velikost:32 znaku hashe". Starsi tvar (s casem zmeny) se bere jako neznamy.
    static bool IsCurrentStamp(string stamp)
    {
        if (string.IsNullOrEmpty(stamp)) return false;

        int colon = stamp.IndexOf(':');
        return colon > 0 && stamp.Length - colon - 1 == 32;
    }

    // Prefab postavy je zastaraly, kdyz byl postaveny z jineho souboru (model nekdo nahradil novym)
    // nebo se soubor modelu od te doby zmenil.
    static bool PrefabIsStale(Entry entry)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath) == null) return false;

        var importer = AssetImporter.GetAtPath(entry.Fbx) as ModelImporter;
        if (importer == null) return false;

        // Bez ulozeneho otisku (prefab z doby pred touhle kontrolou) se pozna jen vymena souboru za jiny.
        bool usesModel = System.Array.IndexOf(AssetDatabase.GetDependencies(entry.PrefabPath, false), entry.Fbx) >= 0;
        return !usesModel || (IsCurrentStamp(importer.userData) && importer.userData != Stamp(entry));
    }

    public static void Setup()
    {
        foreach (var entry in Entries)
            Setup(entry);
    }

    static void Setup(Entry entry)
    {
        if (!entry.Present) return;

        var importer = AssetImporter.GetAtPath(entry.Fbx) as ModelImporter;
        if (importer == null) return;

        // Modely upravene v Blenderu si s sebou casto nesou kameru a svetlo ze sceny - ty do postavy nepatri.
        if (importer.animationType != ModelImporterAnimationType.Human || importer.importAnimation
            || importer.importCameras || importer.importLights)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
        }

        bool stale = PrefabIsStale(entry);

        // Novy nebo upraveny model muze mit nove textury (napr. pridany predmet): vytahnout znovu.
        if (!AssetDatabase.IsValidFolder(entry.TextureDir) || stale)
        {
            importer.ExtractTextures(entry.TextureDir);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        // Textury, ktere v modelu nejsou vlozene (napr. z Blenderu) a uzivatel je dodal do slozky Textures,
        // si materialy najdou podle nazvu az pri novem importu modelu.
        if (stale)
            AssetDatabase.ImportAsset(entry.Fbx, ImportAssetOptions.ForceUpdate);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
        if (prefab == null || stale)
        {
            // Prefab se prepise na stejnem miste, takze odkaz u hrdiny zustava platny.
            var rebuilt = BuildPrefab(entry);
            if (rebuilt != null)
            {
                prefab = rebuilt;
                importer.userData = Stamp(entry);
                EditorUtility.SetDirty(importer);
                AssetDatabase.WriteImportSettingsIfDirty(entry.Fbx);
            }
        }
        if (prefab == null) return;

        // Prvni beh s touhle kontrolou: jen si zapamatovat otisk soucasneho modelu.
        if (!IsCurrentStamp(importer.userData))
        {
            importer.userData = Stamp(entry);
            EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(entry.Fbx);
        }

        var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>(entry.HeroPath);
        if (hero != null && (hero.characterPrefab != prefab || hero.tintCharacter || hero.sleeveColor != entry.sleeve))
        {
            hero.characterPrefab = prefab;
            hero.tintCharacter = false;
            hero.sleeveColor = entry.sleeve;
            hero.skinColor = entry.skin;
            EditorUtility.SetDirty(hero);
        }
    }

    static GameObject BuildPrefab(Entry entry)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Fbx);
        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(entry.Fbx);
        if (model == null || avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogWarning($"[AyranAvatarSetup] Model {entry.hero} se jeste importuje (nebo nema platny Humanoid avatar), zkusim to znovu.");
            return null;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Prefab/Characters"))
            AssetDatabase.CreateFolder("Assets/Prefab", "Characters");

        var root = Object.Instantiate(model);
        root.name = $"Char_{entry.hero}_Avatar";
        try
        {
            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Pojistka: kamery a svetla z exportu do postavy nepatri.
            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                Object.DestroyImmediate(camera.gameObject);
            foreach (var light in root.GetComponentsInChildren<Light>(true))
                Object.DestroyImmediate(light.gameObject);

            // Model ma po importu obcas kosti jako prvni v hierarchii a rendery maji male bounds; pri animaci by mizel.
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.updateWhenOffscreen = true;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, entry.PrefabPath);
            Debug.Log($"[AyranAvatarSetup] Prefab postavy {entry.hero} vytvoren.");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
