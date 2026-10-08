using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Nastavi importy modelu z Quaternius (Humanoid), vytvori sadu animaci a prefab postavy pro kazdeho hrdinu
// (telo + vlasy napojene na stejnou kostru) a priradi je HeroDefinition.characterPrefab.
public static class CharacterSetup
{
    const string MaleFbx = "Assets/Models/Characters/Superhero_Male_FullBody.fbx";
    const string AnimFbx = "Assets/Models/Animations/UAL1_Standard.fbx";
    const string HairDir = "Assets/Models/Characters/Hair/";
    const string PrefabDir = "Assets/Prefab/Characters";
    const string AnimSetPath = "Assets/Resources/Characters/CharacterAnimations.asset";
    const string LightTexture = "Assets/Models/Characters/Textures/T_Superhero_Male_Ligh.png";

    class Look
    {
        public string hero;
        public bool lightSkin;
        public string[] hair;
    }

    static readonly Look[] Looks =
    {
        new Look { hero = "Ayran", lightSkin = false, hair = new[] { "Hair_Buzzed", "Hair_Beard" } },
        new Look { hero = "Viktor", lightSkin = true, hair = new[] { "Hair_SimpleParted" } },
        new Look { hero = "Honza", lightSkin = false, hair = new[] { "Hair_Long" } },
        new Look { hero = "Mirek", lightSkin = true, hair = new[] { "Hair_Long", "Hair_Beard" } },
        new Look { hero = "Anna", lightSkin = true, hair = new[] { "Hair_Buns" } },
        new Look { hero = "Sniper", lightSkin = true, hair = new[] { "Hair_Buzzed" } },
        new Look { hero = "Max", lightSkin = true, hair = new[] { "Hair_Buzzed", "Hair_Beard" } },
        new Look { hero = "Bard", lightSkin = false, hair = new[] { "Hair_SimpleParted", "Hair_Beard" } },
    };

    static bool Present => System.IO.File.Exists(MaleFbx) && System.IO.File.Exists(AnimFbx);

    public static bool IsReady()
    {
        if (!Present) return true;

        if (!ImporterOk(MaleFbx) || !ImporterOk(AnimFbx)) return false;
        var animSet = AssetDatabase.LoadAssetAtPath<CharacterAnimSet>(AnimSetPath);
        if (animSet == null || animSet.blockPose == null || animSet.dashPose == null || animSet.reload == null
            || animSet.sitIdle == null || animSet.hitChest == null) return false;

        foreach (var look in Looks)
        {
            var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>($"Assets/Resources/Heroes/{look.hero}.asset");
            if (hero == null || hero.characterPrefab == null) return false;
        }

        return true;
    }

    static bool ImporterOk(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        return importer != null && importer.animationType == ModelImporterAnimationType.Human
            && importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel && importer.bakeAxisConversion;
    }

    static void ConfigureImporter(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null || ImporterOk(path)) return;

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.bakeAxisConversion = true;
        importer.SaveAndReimport();
    }

    public static void Setup()
    {
        if (!Present) return;

        ConfigureImporter(MaleFbx);
        ConfigureImporter(AnimFbx);
        if (!ImporterOk(MaleFbx) || !ImporterOk(AnimFbx)) return;

        var animSet = CreateAnimSet();
        if (animSet == null) return;

        EnsureFolder("Assets/Prefab");
        EnsureFolder(PrefabDir);

        foreach (var look in Looks)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Char_{look.hero}.prefab");
            if (prefab == null)
                prefab = BuildPrefab(look);

            var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>($"Assets/Resources/Heroes/{look.hero}.asset");
            if (hero != null && prefab != null && hero.characterPrefab == null)
            {
                hero.characterPrefab = prefab;
                EditorUtility.SetDirty(hero);
            }
        }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    static AnimationClip Clip(string shortName)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AnimFbx))
            if (asset is AnimationClip clip && clip.name == "Armature|" + shortName)
                return clip;

        return null;
    }

    static CharacterAnimSet CreateAnimSet()
    {
        var existing = AssetDatabase.LoadAssetAtPath<CharacterAnimSet>(AnimSetPath);
        if (existing != null)
        {
            // Sada z drivejska bez pozy bloku.
            if (existing.blockPose == null)
            {
                existing.blockPose = Clip("Pistol_Aim_Neutral");
                EditorUtility.SetDirty(existing);
            }

            if (existing.dashPose == null)
            {
                existing.dashPose = Clip("Punch_Cross");
                EditorUtility.SetDirty(existing);
            }

            if (existing.reload == null)
            {
                existing.reload = Clip("Pistol_Reload");
                EditorUtility.SetDirty(existing);
            }

            if (existing.sitIdle == null || existing.hitChest == null)
            {
                existing.sitIdle = Clip("Sitting_Idle_Loop");
                existing.hitChest = Clip("Hit_Chest");
                EditorUtility.SetDirty(existing);
            }

            return existing;
        }

        var idle = Clip("Idle_Loop");
        if (idle == null)
        {
            Debug.LogWarning("[CharacterSetup] Animace se jeste importuji, zkusim to znovu.");
            return null;
        }

        var set = ScriptableObject.CreateInstance<CharacterAnimSet>();
        set.idle = idle;
        set.walk = Clip("Walk_Loop");
        set.jog = Clip("Jog_Fwd_Loop");
        set.sprint = Clip("Sprint_Loop");
        set.jump = Clip("Jump_Loop");
        set.death = Clip("Death01");
        set.meleeAttack = Clip("Sword_Attack");
        set.shoot = Clip("Pistol_Shoot");
        set.reload = Clip("Pistol_Reload");
        set.blockPose = Clip("Pistol_Aim_Neutral");
        set.dashPose = Clip("Punch_Cross");
        set.sitIdle = Clip("Sitting_Idle_Loop");
        set.hitChest = Clip("Hit_Chest");

        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Resources/Characters");
        AssetDatabase.CreateAsset(set, AnimSetPath);
        return set;
    }

    static GameObject BuildPrefab(Look look)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(MaleFbx);
        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(MaleFbx);
        if (fbx == null || avatar == null || !avatar.isValid) return null;

        var root = Object.Instantiate(fbx);
        root.name = "Char_" + look.hero;
        try
        {
            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var bones = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;

            if (look.lightSkin)
                ApplyLightSkin(root, look.hero);

            foreach (var hairName in look.hair)
            {
                var hairFbx = AssetDatabase.LoadAssetAtPath<GameObject>(HairDir + hairName + ".fbx");
                if (hairFbx == null) continue;

                var hair = Object.Instantiate(hairFbx);
                foreach (var smr in hair.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mapped = new Transform[smr.bones.Length];
                    for (int i = 0; i < mapped.Length; i++)
                        mapped[i] = smr.bones[i] != null && bones.TryGetValue(smr.bones[i].name, out var b) ? b : null;

                    smr.bones = mapped;
                    if (smr.rootBone != null && bones.TryGetValue(smr.rootBone.name, out var rootBone))
                        smr.rootBone = rootBone;

                    smr.transform.SetParent(root.transform, false);
                }

                Object.DestroyImmediate(hair);
            }

            return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Char_{look.hero}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // Svetla varianta tela: kopie materialu s jinou texturou.
    static void ApplyLightSkin(GameObject root, string hero)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(LightTexture);
        if (texture == null) return;

        string materialPath = "Assets/Models/Characters/Mat_Male_Light.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr.name != "SuperHero_Male") continue;

            if (material == null)
            {
                material = new Material(smr.sharedMaterial);
                material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            smr.sharedMaterial = material;
        }
    }
}
