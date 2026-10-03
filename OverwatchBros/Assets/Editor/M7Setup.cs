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

        // Kdyz se skripty prelozi behem Play modu (nebo tesne pred nim), nastaveni se preskoci.
        // Proto se zkusi znovu hned po navratu z Play modu.
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += RunIfNeeded;
        };
    }

    static int retries;
    static double nextTry;

    static bool IsReady()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        bool prefabReady = prefab != null && prefab.GetComponent<PlayerHero>() != null && prefab.GetComponent<LeapStrikeAbility>() != null
            && prefab.GetComponent<RushAbility>() != null && prefab.GetComponent<BlockAbility>() != null
            && prefab.GetComponent<MineAbility>() != null && prefab.GetComponent<TrapAbility>() != null && prefab.GetComponent<BoulderAbility>() != null
            && prefab.GetComponent<HealFieldAbility>() != null && prefab.GetComponent<FlashAbility>() != null && prefab.GetComponent<VisorAbility>() != null
            && prefab.GetComponent<PotgRecorder>() != null && prefab.GetComponent<HookAbility>() != null
            && prefab.GetComponent<ScoutArrowAbility>() != null && prefab.GetComponent<RapidFireAbility>() != null && prefab.GetComponent<StormAbility>() != null;
        bool heroesReady = AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Ayran.asset") != null
            && AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Honza.asset") != null
            && AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Mirek.asset") != null;
        var ayranWeapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Data/Ayran_Weapon.asset");
        var ayranHero = AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Ayran.asset");
        bool weaponsReady = ayranWeapon != null && ayranWeapon.weaponName != "Těžký revolver"
            && ayranHero != null && ayranHero.secondaryAbility != null && ayranHero.blockAbility != null
            && ayranHero.secondaryAbility.duration <= 1.5f && ayranHero.altAbility != null;
        var honzaHero = AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Honza.asset");
        bool honzaReady = honzaHero != null && honzaHero.ability != null && honzaHero.secondaryAbility != null && honzaHero.altAbility != null
            && honzaHero.weapon != null && honzaHero.weapon.weaponName != "Raketomet" && honzaHero.weapon.reloadAnimation;
        var viktorHero = AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Viktor.asset");
        bool ultsReady = UltCostSet("Assets/Data/LeapStrike_Data.asset") && UltCostSet("Assets/Data/Boulder_Data.asset") && UltCostSet("Assets/Data/Visor_Data.asset");
        bool viktorReady = viktorHero == null || (viktorHero.abilityKind == AbilityKind.Visor && viktorHero.secondaryAbility != null
            && viktorHero.altAbility != null && viktorHero.rmbAbility != null);
        return prefabReady && heroesReady && weaponsReady && honzaReady && viktorReady && ultsReady && IconsReady() && AssetDatabase.LoadAssetAtPath<Material>(LitMaterialPath) != null && HeldAxeSetup.IsReady() && CharacterSetup.IsReady() && AyranAvatarSetup.IsReady() && AyranSoundSetup.IsReady();
    }

    public static void RunIfNeeded()
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
        CreateLitMaterial();
        CreateHeroAssets();
        AssignIcons();
        AyranSoundSetup.Setup();
        SetupPlayerPrefab();
        HeldAxeSetup.Setup();
        CharacterSetup.Setup();
        AyranAvatarSetup.Setup();

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

    const string LitMaterialPath = "Assets/Resources/Fx/Lit.mat";

    static void CreateLitMaterial()
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(LitMaterialPath) != null) return;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) return;

        var material = new Material(shader);
        material.SetFloat("_Smoothness", 0.2f);
        AssetDatabase.CreateAsset(material, LitMaterialPath);
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

    static void ConfigureAxes(WeaponDefinition w)
    {
        w.weaponName = "Sekyrky";
        w.fireMode = FireMode.Melee;
        w.damage = 35f;
        w.fireRate = 2.2f;
        w.maxAmmo = 0;
        w.range = 2.8f;
        w.meleeRadius = 0.7f;
        w.dualWield = true;
        w.heldModel = HeldModel.Axe;
        w.projectileColor = new Color(0.95f, 0.55f, 0.20f, 1f);
    }

    // Ayran mel puvodne revolver; prevedeme ho na sekyry (jen kdyz je asset jeste v puvodnim stavu, at se nepreplsou rucni upravy).
    static void MigrateAyranToAxes(WeaponDefinition weapon)
    {
        if (weapon == null || weapon.weaponName != "Těžký revolver") return;

        ConfigureAxes(weapon);
        EditorUtility.SetDirty(weapon);
    }

    static readonly string[,] Icons =
    {
        { "Assets/Data/LeapStrike_Data.asset", "Assets/Resources/Icons/ayran_leap.png" },
        { "Assets/Data/Rush_Data.asset", "Assets/Resources/Icons/ayran_rush.png" },
        { "Assets/Data/Block_Data.asset", "Assets/Resources/Icons/ayran_block.png" },
        { "Assets/Data/Dash_Data.asset", "Assets/Resources/Icons/dash.png" },
        { "Assets/Data/Hook_Data.asset", "Assets/Resources/Icons/ayran_hook.png" },
        { "Assets/Data/Lunge_Data.asset", "Assets/Resources/Icons/mirek_lunge.png" },
        { "Assets/Data/Scout_Data.asset", "Assets/Resources/Icons/mirek_scout.png" },
        { "Assets/Data/RapidFire_Data.asset", "Assets/Resources/Icons/mirek_rapid.png" },
        { "Assets/Data/Storm_Data.asset", "Assets/Resources/Icons/mirek_storm.png" },
        { "Assets/Data/Mine_Data.asset", "Assets/Resources/Icons/honza_mine.png" },
        { "Assets/Data/Trap_Data.asset", "Assets/Resources/Icons/honza_trap.png" },
        { "Assets/Data/Boulder_Data.asset", "Assets/Resources/Icons/honza_boulder.png" },
        { "Assets/Data/Visor_Data.asset", "Assets/Resources/Icons/viktor_visor.png" },
        { "Assets/Data/HealField_Data.asset", "Assets/Resources/Icons/viktor_heal.png" },
        { "Assets/Data/Flash_Data.asset", "Assets/Resources/Icons/viktor_flash.png" },
    };

    static bool IconsReady()
    {
        for (int i = 0; i < Icons.GetLength(0); i++)
        {
            if (!File.Exists(Icons[i, 1])) continue;
            var ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(Icons[i, 0]);
            if (ability != null && ability.icon == null) return false;
        }

        return true;
    }

    static void AssignIcons()
    {
        for (int i = 0; i < Icons.GetLength(0); i++)
        {
            var ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(Icons[i, 0]);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Icons[i, 1]);
            if (ability == null || icon == null || ability.icon != null) continue;

            ability.icon = icon;
            EditorUtility.SetDirty(ability);
        }
    }

    // Honzuv granatomet: granaty leti obloukem, odrazi se a vybuchnou po chvili nebo o hrace.
    static void ConfigureGrenades(WeaponDefinition w)
    {
        w.weaponName = "Granátomet";
        w.damage = 60f;
        w.fireRate = 1.5f;
        w.maxAmmo = 5;
        w.reloadTime = 1.8f;
        w.reloadAnimation = true;
        w.range = 140f;
        w.fireMode = FireMode.Projectile;
        w.projectileSpeed = 24f;
        w.projectileGravity = 14f;
        w.projectileRadius = 0.22f;
        w.projectileBounce = 0.45f;
        w.projectileFuse = 0.9f;
        w.explosionRadius = 3.2f;
        w.projectileColor = new Color(1f, 0.55f, 0.10f, 1f);
        w.projectileTrail = true;
    }

    static void ConfigureDash(AbilityDefinition a)
    {
        a.charges = 2;
        a.range = 12f;
        a.duration = 0.45f;
        a.power = 20f;
        a.radius = 2.2f;
        a.healRatio = 1f;
    }

    static bool UltCostSet(string path)
    {
        var ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(path);
        return ability == null || ability.ultCost > 0f;
    }

    // Ultimatky se nabijeji hrou (poskozeni / leceni), ne cooldownem. Cena = zhruba kolik poskozeni je potreba zpusobit.
    static void SetUltCost(string path, float cost)
    {
        var ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(path);
        if (ability == null || ability.ultCost > 0f) return;

        ability.ultCost = cost;
        EditorUtility.SetDirty(ability);
    }

    static void CreateHeroAssets()
    {
        var pistol = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Data/Pistol_Data.asset");
        var dash = AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/Data/Dash_Data.asset");

        var ayranWeapon = LoadOrCreate<WeaponDefinition>("Assets/Data/Ayran_Weapon.asset", ConfigureAxes);
        MigrateAyranToAxes(ayranWeapon);

        var leap = LoadOrCreate<AbilityDefinition>("Assets/Data/LeapStrike_Data.asset", a =>
        {
            a.abilityName = "Ohnivý dopad";
            a.cooldown = 25f;
            a.power = 60f;
            a.duration = 5f;
            a.radius = 7f;
        });

        var rush = LoadOrCreate<AbilityDefinition>("Assets/Data/Rush_Data.asset", a =>
        {
            a.abilityName = "Modrý plamen";
            a.cooldown = 8f;
            ConfigureDash(a);
        });

        // Drive to byl beh drzenim Shiftu (duration v sekundach); ted je to jeden vypad.
        if (rush.duration > 1.5f)
        {
            ConfigureDash(rush);
            EditorUtility.SetDirty(rush);
        }

        var block = LoadOrCreate<AbilityDefinition>("Assets/Data/Block_Data.asset", a =>
        {
            a.abilityName = "Zkřížené sekyry";
            a.blockAbsorb = 0.8f;
            a.blockSpeed = 0.5f;
            a.regenDelay = 5f;
            a.regenTime = 6f;
        });

        // Honza (inspirace: Junkrat). Cooldowny: naloz 8 s za naboj, past 10 s; balvan se nabiji hrou (ultCost).
        var rocket = LoadOrCreate<WeaponDefinition>("Assets/Data/Rocket_Data.asset", ConfigureGrenades);
        if (rocket.weaponName == "Raketomet")
        {
            ConfigureGrenades(rocket);
            EditorUtility.SetDirty(rocket);
        }

        // Granatomet vznikl driv bez animace prebijeni.
        if (!rocket.reloadAnimation)
        {
            rocket.reloadAnimation = true;
            rocket.reloadTime = 1.8f;
            EditorUtility.SetDirty(rocket);
        }

        var mine = LoadOrCreate<AbilityDefinition>("Assets/Data/Mine_Data.asset", a =>
        {
            a.abilityName = "Nálož";
            a.cooldown = 8f;
            a.charges = 2;
            a.power = 60f;
            a.radius = 4.5f;
            a.knockback = 12f;
            a.speed = 17f;
        });

        var trap = LoadOrCreate<AbilityDefinition>("Assets/Data/Trap_Data.asset", a =>
        {
            a.abilityName = "Past na medvědy";
            a.cooldown = 10f;
            a.power = 40f;
            a.radius = 0.9f;
            a.duration = 2f;
        });

        var boulder = LoadOrCreate<AbilityDefinition>("Assets/Data/Boulder_Data.asset", a =>
        {
            a.abilityName = "Balvan";
            a.cooldown = 30f;
            a.power = 150f;
            a.radius = 7f;
            a.knockback = 14f;
            a.duration = 10f;
            a.speed = 11f;
        });

        var honza = LoadOrCreate<HeroDefinition>("Assets/Resources/Heroes/Honza.asset", h =>
        {
            h.heroName = "Honza";
            h.color = new Color(0.45f, 0.80f, 0.40f);
            h.maxHealth = 110f;
            h.weapon = rocket;
        });

        // Honza vznikl driv bez schopnosti.
        if (honza.ability == null && honza.secondaryAbility == null && honza.altAbility == null)
        {
            honza.abilityKind = AbilityKind.Boulder;
            honza.ability = boulder;
            honza.secondaryAbilityKind = AbilityKind.Mine;
            honza.secondaryAbility = mine;
            honza.altAbilityKind = AbilityKind.Trap;
            honza.altAbility = trap;
            honza.deathGrenades = 4;
            EditorUtility.SetDirty(honza);
        }

        // Viktor (inspirace: Soldier 76 / Cassidy). Cooldowny: uskok 6 s, lecive pole 15 s, oslepujici granat 10 s;
        // takticky zamerovac se nabiji hrou (ultCost).
        var visor = LoadOrCreate<AbilityDefinition>("Assets/Data/Visor_Data.asset", a =>
        {
            a.abilityName = "Taktický zaměřovač";
            a.cooldown = 30f;
            a.duration = 6f;
            a.radius = 30f;   // uhel kuzelu ve stupnich
        });

        var healField = LoadOrCreate<AbilityDefinition>("Assets/Data/HealField_Data.asset", a =>
        {
            a.abilityName = "Léčivé pole";
            a.cooldown = 15f;
            a.power = 20f;    // zivotu za sekundu
            a.radius = 5f;
            a.duration = 5f;
        });

        var flash = LoadOrCreate<AbilityDefinition>("Assets/Data/Flash_Data.asset", a =>
        {
            a.abilityName = "Oslepující granát";
            a.cooldown = 10f;
            a.power = 15f;
            a.radius = 4f;
            a.range = 7f;
            a.speed = 22f;
            a.duration = 0.8f;   // delka omraceni
        });

        var viktor = LoadOrCreate<HeroDefinition>("Assets/Resources/Heroes/Viktor.asset", h =>
        {
            h.heroName = "Viktor";
            h.color = new Color(0.45f, 0.65f, 0.95f);
            h.maxHealth = 100f;
            h.weapon = pistol;
        });

        // Viktor mel driv jen Dash na Q: ted je uskok na Shiftu a na Q ultimatni schopnost.
        if (viktor.abilityKind != AbilityKind.Visor && viktor.secondaryAbility == null && viktor.altAbility == null && viktor.rmbAbility == null)
        {
            viktor.abilityKind = AbilityKind.Visor;
            viktor.ability = visor;
            viktor.secondaryAbilityKind = AbilityKind.Dash;
            viktor.secondaryAbility = dash;
            viktor.altAbilityKind = AbilityKind.HealField;
            viktor.altAbility = healField;
            viktor.rmbAbilityKind = AbilityKind.Flash;
            viktor.rmbAbility = flash;
            EditorUtility.SetDirty(viktor);

            if (dash != null && dash.abilityName != "Úskok")
            {
                dash.abilityName = "Úskok";
                EditorUtility.SetDirty(dash);
            }
        }

        LoadOrCreate<HeroDefinition>("Assets/Resources/Heroes/Ayran.asset", h =>
        {
            h.heroName = "Ayran";
            h.color = new Color(0.95f, 0.55f, 0.20f);
            h.maxHealth = 120f;
            h.weapon = ayranWeapon;
            h.abilityKind = AbilityKind.LeapStrike;
            h.ability = leap;
            h.secondaryAbilityKind = AbilityKind.Rush;
            h.secondaryAbility = rush;
            h.blockAbility = block;
        });

        SetUltCost("Assets/Data/LeapStrike_Data.asset", 350f);
        SetUltCost("Assets/Data/Boulder_Data.asset", 500f);
        SetUltCost("Assets/Data/Visor_Data.asset", 450f);

        CreateMirek();

        // Ayran vznikl driv bez druhe schopnosti.
        var ayran = AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Ayran.asset");
        if (ayran != null && ayran.secondaryAbility == null && ayran.secondaryAbilityKind == AbilityKind.None)
        {
            ayran.secondaryAbilityKind = AbilityKind.Rush;
            ayran.secondaryAbility = rush;
            EditorUtility.SetDirty(ayran);
        }

        if (ayran != null && ayran.blockAbility == null)
        {
            ayran.blockAbility = block;
            EditorUtility.SetDirty(ayran);
        }

        // Sekera na retezu na E (cooldown 8 s).
        var hook = LoadOrCreate<AbilityDefinition>("Assets/Data/Hook_Data.asset", a =>
        {
            a.abilityName = "Sekera na řetězu";
            a.cooldown = 8f;
            a.power = 30f;
            a.range = 18f;
            a.duration = 0.5f;   // omraceni po dotazeni
        });

        if (ayran != null && ayran.altAbility == null)
        {
            ayran.altAbilityKind = AbilityKind.Hook;
            ayran.altAbility = hook;
            EditorUtility.SetDirty(ayran);
        }
    }

    // Mirek: lukostrelec. Luk se natahuje drzenim (bez zasobniku), Shift = vyskok, E = pruzkumny sip,
    // prave tlacitko = rychlopalba, Q = smrst (nabiji se hrou). Pasivne druhy skok a vytazeni na hranu.
    static void CreateMirek()
    {
        var bow = LoadOrCreate<WeaponDefinition>("Assets/Data/Bow_Data.asset", w =>
        {
            w.weaponName = "Luk";
            w.fireMode = FireMode.Projectile;
            w.heldModel = HeldModel.Bow;
            w.damage = 75f;
            w.minChargeDamage = 20f;
            w.chargeTime = 1f;
            w.fireRate = 2.5f;
            w.maxAmmo = 0;
            w.range = 160f;
            w.projectileSpeed = 110f;
            w.minChargeSpeed = 55f;
            w.projectileGravity = 9f;
            w.projectileRadius = 0.22f;
            w.explosionRadius = 0f;
            w.projectileColor = new Color(0.55f, 0.85f, 1f, 1f);
            w.projectileTrail = true;
        });

        var lunge = LoadOrCreate<AbilityDefinition>("Assets/Data/Lunge_Data.asset", a =>
        {
            a.abilityName = "Výskok";
            a.cooldown = 5f;
            a.power = 7f;        // delka uskoku v metrech
            a.duration = 0.22f;
            a.knockback = 5f;    // odraz nahoru
        });

        var scout = LoadOrCreate<AbilityDefinition>("Assets/Data/Scout_Data.asset", a =>
        {
            a.abilityName = "Průzkumný šíp";
            a.cooldown = 12f;
            a.duration = 6f;
            a.radius = 10f;
            a.range = 70f;
            a.speed = 60f;
        });

        var rapid = LoadOrCreate<AbilityDefinition>("Assets/Data/RapidFire_Data.asset", a =>
        {
            a.abilityName = "Rychlopalba";
            a.cooldown = 10f;
            a.power = 35f;
            a.charges = 5;
            a.duration = 5f;
        });

        var storm = LoadOrCreate<AbilityDefinition>("Assets/Data/Storm_Data.asset", a =>
        {
            a.abilityName = "Smršť";
            a.cooldown = 30f;
            a.ultCost = 550f;
            a.power = 120f;      // poskozeni za sekundu
            a.radius = 3f;
            a.speed = 12f;
            a.duration = 6f;
        });

        LoadOrCreate<HeroDefinition>("Assets/Resources/Heroes/Mirek.asset", h =>
        {
            h.heroName = "Mirek";
            h.color = new Color(0.60f, 0.45f, 0.90f);
            h.maxHealth = 100f;
            h.weapon = bow;
            h.abilityKind = AbilityKind.Storm;
            h.ability = storm;
            h.secondaryAbilityKind = AbilityKind.Dash;
            h.secondaryAbility = lunge;
            h.altAbilityKind = AbilityKind.ScoutArrow;
            h.altAbility = scout;
            h.rmbAbilityKind = AbilityKind.RapidFire;
            h.rmbAbility = rapid;
            h.doubleJump = true;
            h.ledgeClimb = true;
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
            AddIfMissing<RushAbility>(contents);
            AddIfMissing<BlockAbility>(contents);
            AddIfMissing<MineAbility>(contents);
            AddIfMissing<TrapAbility>(contents);
            AddIfMissing<BoulderAbility>(contents);
            AddIfMissing<HealFieldAbility>(contents);
            AddIfMissing<FlashAbility>(contents);
            AddIfMissing<VisorAbility>(contents);
            AddIfMissing<PotgRecorder>(contents);
            AddIfMissing<HookAbility>(contents);
            AddIfMissing<ScoutArrowAbility>(contents);
            AddIfMissing<RapidFireAbility>(contents);
            AddIfMissing<StormAbility>(contents);
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
