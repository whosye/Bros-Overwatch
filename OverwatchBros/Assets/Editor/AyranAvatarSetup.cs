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
        return hero != null && prefab != null && hero.characterPrefab == prefab && !hero.tintCharacter && hero.sleeveColor == entry.sleeve;
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

        if (importer.animationType != ModelImporterAnimationType.Human || importer.importAnimation)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.SaveAndReimport();
        }

        if (!AssetDatabase.IsValidFolder(entry.TextureDir))
        {
            importer.ExtractTextures(entry.TextureDir);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.PrefabPath);
        if (prefab == null)
            prefab = BuildPrefab(entry);
        if (prefab == null) return;

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
