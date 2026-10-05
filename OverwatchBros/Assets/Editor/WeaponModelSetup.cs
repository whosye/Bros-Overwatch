using UnityEditor;
using UnityEngine;

// Prefaby zbrani v ruce z modelu v Assets/Models/Weapons (low-poly sci-fi zbrane) a jejich prirazeni hrdinum.
// Kazdy model ma znacky Grip (kde ho drzi ruka), Muzzle (usti hlavne) a Top (smer nahoru) - podle nich se zbran
// sama otoci (hlaven dopredu = +Z, nahoru = +Y), zmensi a posune tak, aby rukojet byla v pocatku (v dlani).
// Prirazuje se jen zbranim, ktere vlastni model jeste nemaji (rucni zmenu v inspektoru to neprepise).
public static class WeaponModelSetup
{
    const string ModelDir = "Assets/Models/Weapons/";
    const string PrefabDir = "Assets/Prefab/Weapons";

    struct Gun
    {
        public string model;      // jmeno FBX
        public string weapon;     // WeaponDefinition (prazdne = jen prefab do zasoby)
        public float reach;       // vzdalenost rukojet -> usti v metrech
        public bool projectile;   // model letici strely (WeaponDefinition.projectilePrefab) misto zbrane v ruce
        public Vector3 hand;      // kam se posune rukojet (HeldWeapons drzi zbran pesti v GripInWeapon)
    }

    // Pest v HeldWeapons.GripInWeapon je o kus niz a vpredu od pocatku; rukojet modelu (vrsek rukojeti u spouste)
    // se posune tak, aby ji pest svirala.
    static readonly Vector3 HitscanHand = new Vector3(0f, -0.03f, 0.05f);
    static readonly Vector3 ProjectileHand = new Vector3(0f, -0.06f, 0.12f);
    static readonly Vector3 BowHand = new Vector3(0f, 0f, 0.10f);

    static readonly Gun[] Guns =
    {
        new Gun { model = "Rifle", weapon = "Assets/Data/Pistol_Data.asset", reach = 0.62f, hand = HitscanHand },   // Viktor
        new Gun { model = "SniperRifle", weapon = "Assets/Data/SniperRifle_Data.asset", reach = 0.85f, hand = HitscanHand },    // Sniper
        new Gun { model = "LongPistolSmall", weapon = "Assets/Data/FlankerPistols_Data.asset", reach = 0.24f, hand = HitscanHand }, // Flanker
        new Gun { model = "LongPistol", weapon = "Assets/Data/AnnaRifle_Data.asset", reach = 0.5f, hand = ProjectileHand },        // Anna
        new Gun { model = "RayGun", weapon = "Assets/Data/BardGun_Data.asset", reach = 0.26f, hand = ProjectileHand },             // Bard
        new Gun { model = "LightningGun", weapon = "Assets/Data/Rocket_Data.asset", reach = 0.4f, hand = ProjectileHand },         // Honza
        new Gun { model = "Pistol", weapon = "Assets/Data/SindelPistol_Data.asset", reach = 0.2f, hand = HitscanHand },                                            // do zasoby
        new Gun { model = "Bow", weapon = "Assets/Data/Bow_Data.asset", reach = 0.45f, hand = BowHand },                     // Mirek (luk 0.9 m)
        new Gun { model = "ArrowProjectile", weapon = "Assets/Data/Bow_Data.asset", reach = 0.42f, projectile = true }, // Mirkuv letici sip
    };

    static string ModelPath(Gun gun) => ModelDir + gun.model + ".fbx";
    // v2: vlastni nepruhledne materialy (puvodni z FBX maji alfu 0 a v Unity jsou pruhledne).
    const string Version = "_v3";
    static string PrefabPath(Gun gun) => $"{PrefabDir}/Held_{gun.model}{Version}.prefab";
    static string OldPrefabPath(Gun gun) => $"{PrefabDir}/Held_{gun.model}.prefab";
    const string MaterialDir = "Assets/Models/Weapons/Materials";

    public static bool IsReady()
    {
        foreach (var gun in Guns)
        {
            if (!System.IO.File.Exists(ModelPath(gun))) continue;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(gun)) == null) return false;
            if (string.IsNullOrEmpty(gun.weapon)) continue;
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(gun.weapon);
            var current = weapon == null ? null : gun.projectile ? weapon.projectilePrefab : weapon.heldPrefab;
            if (weapon != null && (current == null || IsOld(current))) return false;
        }
        return true;
    }

    public static void Setup()
    {
        EnsureFolder("Assets/Prefab");
        EnsureFolder(PrefabDir);
        EnsureFolder(MaterialDir);

        foreach (var gun in Guns)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(gun));
            if (model == null) continue;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(gun));
            if (prefab == null)
                prefab = BuildPrefab(gun, model);
            if (prefab == null || string.IsNullOrEmpty(gun.weapon)) continue;

            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(gun.weapon);
            if (weapon == null) continue;
            var current = gun.projectile ? weapon.projectilePrefab : weapon.heldPrefab;
            if (current == null || IsOld(current))
            {
                if (gun.projectile) weapon.projectilePrefab = prefab;
                else weapon.heldPrefab = prefab;
                EditorUtility.SetDirty(weapon);
            }
        }

        // Stare (pruhledne) prefaby uz nic nepouziva.
        foreach (var gun in Guns)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(OldPrefabPath(gun)) != null)
                AssetDatabase.DeleteAsset(OldPrefabPath(gun));
    }

    // Starsi verze prefabu z tohoto skriptu (rucne prirazene modely se neprepisuji).
    static bool IsOld(GameObject prefab)
    {
        string path = AssetDatabase.GetAssetPath(prefab);
        return path.StartsWith(PrefabDir + "/Held_") && !path.EndsWith(Version + ".prefab");
    }

    // Nepruhledna kopie materialu z FBX (stejna barva, alfa 1).
    static Material Opaque(Gun gun, Material source)
    {
        if (source == null) return null;
        string path = $"{MaterialDir}/{gun.model}_{source.name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) return source;

        Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
        color.a = 1f;
        var material = new Material(shader);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.4f);
        material.SetFloat("_Metallic", 0.25f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static GameObject BuildPrefab(Gun gun, GameObject model)
    {
        var root = new GameObject("Held_" + gun.model);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        try
        {
            var grip = Find(instance.transform, "Grip");
            var muzzle = Find(instance.transform, "Muzzle");
            var top = Find(instance.transform, "Top");
            if (grip == null || muzzle == null || top == null)
            {
                Debug.LogWarning($"[WeaponModelSetup] {gun.model}: chybi znacky Grip/Muzzle/Top, preskakuji.");
                return null;
            }

            Vector3 forward = muzzle.position - grip.position;
            Vector3 up = top.position - grip.position;
            float reach = forward.magnitude;
            if (reach < 1e-4f)
            {
                Debug.LogWarning($"[WeaponModelSetup] {gun.model}: znacky jsou na stejnem miste.");
                return null;
            }

            // Otoceni (hlaven -> +Z, nahoru -> +Y) a zmenseni kolem pocatku, pak posun rukojeti do pocatku.
            var rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up));
            float scale = gun.reach / reach;
            var t = instance.transform;
            t.SetPositionAndRotation(rotation * t.position * scale, rotation * t.rotation);
            t.localScale *= scale;
            t.position -= grip.position - gun.hand;

            foreach (var collider in instance.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(collider);

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = Opaque(gun, materials[i]);
                renderer.sharedMaterials = materials;
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(gun));
            Debug.Log($"[WeaponModelSetup] Prefab {gun.model} vytvoren (rukojet->usti {reach:0.###} -> {gun.reach} m).");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static Transform Find(Transform parent, string name)
    {
        foreach (var child in parent.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
