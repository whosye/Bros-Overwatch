using System.IO;
using UnityEditor;
using UnityEngine;

// Jednorazove (a idempotentne) nastaveni M7: hrdinske assety, material efektu a komponenty na Player.prefab.
// Spusti se samo po kompilaci; jde ho spustit i rucne: BrosOverwatch > Setup M7.
[InitializeOnLoad]
public static class M7Setup
{
    const string PlayerPrefabPath = "Assets/Prefab/Player.prefab";

    static M7Setup()
    {
        EditorApplication.delayCall += RunIfNeeded;
    }

    static int retries;
    static double nextTry;

    static bool IsReady()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        bool prefabReady = prefab != null && prefab.GetComponent<PlayerHero>() != null && prefab.GetComponent<LeapStrikeAbility>() != null;
        bool heroesReady = AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Ayran.asset") != null;
        return prefabReady && heroesReady;
    }

    static void RunIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (IsReady()) return;

        Run();

        // Skripty se po prvni kompilaci importuji asynchronne, tak to par sekund zkousime znovu.
        if (!IsReady() && retries < 15)
        {
            retries++;
            nextTry = EditorApplication.timeSinceStartup + 3.0;
            EditorApplication.update -= Retry;
            EditorApplication.update += Retry;
        }
    }

    static void Retry()
    {
        if (EditorApplication.timeSinceStartup < nextTry) return;

        EditorApplication.update -= Retry;
        RunIfNeeded();
    }

    [MenuItem("BrosOverwatch/Setup M7")]
    public static void Run()
    {
        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Resources/Heroes");
        EnsureFolder("Assets/Resources/Fx");

        CreateFxMaterial();
        CreateHeroAssets();
        SetupPlayerPrefab();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (IsReady())
            Debug.Log("[M7Setup] Hotovo: hrdinove (Viktor, Ayran), material efektu a komponenty na Player.prefab.");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static void CreateFxMaterial()
    {
        const string path = "Assets/Resources/Fx/Particle.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;

        var shader = Shader.Find("Sprites/Default");
        if (shader == null) return;

        AssetDatabase.CreateAsset(new Material(shader), path);
    }

    static T LoadOrCreate<T>(string path, System.Action<T> init) where T : ScriptableObject
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;

        var created = ScriptableObject.CreateInstance<T>();
        init(created);
        AssetDatabase.CreateAsset(created, path);
        return created;
    }

    static void CreateHeroAssets()
    {
        var pistol = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Data/Pistol_Data.asset");
        var dash = AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/Data/Dash_Data.asset");

        var ayranWeapon = LoadOrCreate<WeaponDefinition>("Assets/Data/Ayran_Weapon.asset", w =>
        {
            w.weaponName = "Těžký revolver";
            w.damage = 28f;
            w.fireRate = 2.5f;
            w.maxAmmo = 6;
            w.range = 100f;
        });

        var leap = LoadOrCreate<AbilityDefinition>("Assets/Data/LeapStrike_Data.asset", a =>
        {
            a.abilityName = "Ohnivý dopad";
            a.cooldown = 25f;
            a.power = 60f;
            a.duration = 5f;
            a.radius = 7f;
        });

        LoadOrCreate<HeroDefinition>("Assets/Resources/Heroes/Viktor.asset", h =>
        {
            h.heroName = "Viktor";
            h.color = new Color(0.45f, 0.65f, 0.95f);
            h.maxHealth = 100f;
            h.weapon = pistol;
            h.abilityKind = AbilityKind.Dash;
            h.ability = dash;
        });

        LoadOrCreate<HeroDefinition>("Assets/Resources/Heroes/Ayran.asset", h =>
        {
            h.heroName = "Ayran";
            h.color = new Color(0.95f, 0.55f, 0.20f);
            h.maxHealth = 120f;
            h.weapon = ayranWeapon;
            h.abilityKind = AbilityKind.LeapStrike;
            h.ability = leap;
        });
    }

    static void SetupPlayerPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
        {
            Debug.LogWarning("[M7Setup] Player.prefab nenalezen, preskakuji.");
            return;
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            foreach (var existing in contents.GetComponentsInChildren<Component>(true))
            {
                if (existing == null)
                {
                    Debug.LogWarning("[M7Setup] Skripty se jeste importuji (na prefabu chybi skript), zkusim to po dalsi kompilaci. Nic neulozeno.");
                    return;
                }
            }

            AddIfMissing<PlayerHero>(contents);
            AddIfMissing<LeapStrikeAbility>(contents);
            AddIfMissing<HeroVoice>(contents);

            foreach (var behaviour in contents.GetComponents<MonoBehaviour>())
            {
                var script = behaviour == null ? null : MonoScript.FromMonoBehaviour(behaviour);
                if (script == null || string.IsNullOrEmpty(script.name))
                {
                    Debug.LogWarning("[M7Setup] Skripty se jeste nezaregistrovaly, zkusim to po dalsi kompilaci. Nic neulozeno.");
                    return;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            Debug.Log("[M7Setup] Player.prefab aktualizovan (PlayerHero, LeapStrikeAbility, HeroVoice).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    static void AddIfMissing<T>(GameObject target) where T : Component
    {
        if (target.GetComponent<T>() == null)
            target.AddComponent<T>();
    }
}
