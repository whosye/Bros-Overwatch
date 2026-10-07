using System.IO;
using UnityEditor;
using UnityEngine;

// Jednorazove (a idempotentne) nastaveni M7: hrdinske assety, material efektu a komponenty na Player.prefab.
// Spusti se samo po kompilaci; jde ho spustit i rucne: BrosOverwatch > Setup M7.
[InitializeOnLoad]
public static class M7Setup
{
    const string PlayerPrefabPath = "Assets/Prefab/Player.prefab";
    const float WalkSpeed = 6f;
    const float RunSpeed = 9.5f;

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
            && prefab.GetComponent<ScoutArrowAbility>() != null && prefab.GetComponent<RapidFireAbility>() != null && prefab.GetComponent<StormAbility>() != null
            && prefab.GetComponent<SleepDartAbility>() != null && prefab.GetComponent<BioticGrenadeAbility>() != null
            && prefab.GetComponent<NanoBoostAbility>() != null && prefab.GetComponent<ScopeZoom>() != null
            && prefab.GetComponent<GrappleAbility>() != null && prefab.GetComponent<KickAbility>() != null && prefab.GetComponent<InfraAbility>() != null
            && prefab.GetComponent<BlinkAbility>() != null && prefab.GetComponent<RecallAbility>() != null && prefab.GetComponent<PulseBombAbility>() != null
            && prefab.GetComponent<BardAbility>() != null
            && prefab.GetComponent<SindelAbility>() != null && prefab.GetComponent<EzekielAbility>() != null
            && prefab.GetComponent<FirstPersonController>() != null && prefab.GetComponent<FirstPersonController>().walkSpeed >= WalkSpeed;
        bool heroesReady = AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Ayran.asset") != null
            && AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Honza.asset") != null
            && AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Mirek.asset") != null
            && AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Anna.asset") != null
            && AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Sniper.asset") != null
            && AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Flanker.asset") != null
            && AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Bard.asset") != null
            && AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Bard.asset").rmbAbility != null
            && AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Sindel.asset") != null
            && AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/Data/SinnerMark_Data.asset") == null;
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
        var flankerPistols = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Data/FlankerPistols_Data.asset");
        if (flankerPistols != null && !flankerPistols.dualWield) return false;
        bool viktorReady = viktorHero == null || (viktorHero.abilityKind == AbilityKind.Visor && viktorHero.secondaryAbility != null
            && viktorHero.altAbility != null && viktorHero.rmbAbility != null);
        return prefabReady && heroesReady && weaponsReady && honzaReady && viktorReady && ultsReady && IconsReady() && AssetDatabase.LoadAssetAtPath<Material>(LitMaterialPath) != null && HeldAxeSetup.IsReady() && WeaponModelSetup.IsReady() && WeaponSoundSetup.IsReady() && CharacterSetup.IsReady() && AyranAvatarSetup.IsReady() && AyranSoundSetup.IsReady();
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
        WeaponModelSetup.Setup();
        WeaponSoundSetup.Setup();
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
        { "Assets/Data/SleepDart_Data.asset", "Assets/Resources/Icons/anna_sleep.png" },
        { "Assets/Data/BioticGrenade_Data.asset", "Assets/Resources/Icons/anna_grenade.png" },
        { "Assets/Data/NanoBoost_Data.asset", "Assets/Resources/Icons/anna_nano.png" },
        { "Assets/Data/Grapple_Data.asset", "Assets/Resources/Icons/sniper_grapple.png" },
        { "Assets/Data/Kick_Data.asset", "Assets/Resources/Icons/sniper_kick.png" },
        { "Assets/Data/Infra_Data.asset", "Assets/Resources/Icons/sniper_infra.png" },
        { "Assets/Data/Blink_Data.asset", "Assets/Resources/Icons/flanker_blink.png" },
        { "Assets/Data/Recall_Data.asset", "Assets/Resources/Icons/flanker_recall.png" },
        { "Assets/Data/PulseBomb_Data.asset", "Assets/Resources/Icons/flanker_bomb.png" },
        { "Assets/Data/Crossfade_Data.asset", "Assets/Resources/Icons/bard_crossfade.png" },
        { "Assets/Data/Amp_Data.asset", "Assets/Resources/Icons/bard_amp.png" },
        { "Assets/Data/Concert_Data.asset", "Assets/Resources/Icons/bard_concert.png" },
        { "Assets/Data/Soundwave_Data.asset", "Assets/Resources/Icons/bard_wave.png" },
        { "Assets/Data/RighteousLeap_Data.asset", "Assets/Resources/Icons/sindel_leap.png" },
        { "Assets/Data/HolyGrenade_Data.asset", "Assets/Resources/Icons/sindel_grenade.png" },
        { "Assets/Data/Ezekiel_Data.asset", "Assets/Resources/Icons/sindel_ezekiel.png" },
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
            a.abilityName = "Pulzní granát";
            a.cooldown = 10f;
            a.power = 50f;
            a.radius = 3f;
            a.range = 40f;
            a.speed = 45f;
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
            h.heroName = "Pova";
            h.color = new Color(0.95f, 0.55f, 0.20f);
            h.maxHealth = 180f;
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
        CreateAnna();
        CreateSniper();
        CreateFlanker();
        CreateBard();
        CreateSindel();

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

    // Anna (inspirace: Ana z Overwatche): lecitelka s puskou. Sipky spoluhrace leci a nepratele zrani, prave tlacitko
    // = dalekohled, Shift = uspavaci sipka, E = biotický granat, Q = posileni spoluhrace (nabiji se hrou i lecenim).
    static void CreateAnna()
    {
        var rifle = LoadOrCreate<WeaponDefinition>("Assets/Data/AnnaRifle_Data.asset", w =>
        {
            w.weaponName = "Biotická puška";
            w.fireMode = FireMode.Projectile;
            w.heldModel = HeldModel.Gun;
            w.damage = 35f;
            w.allyHeal = 45f;
            w.scopeFov = 32f;
            w.fireRate = 1.4f;
            w.maxAmmo = 10;
            w.reloadTime = 1.6f;
            w.range = 150f;
            w.projectileSpeed = 125f;
            w.projectileGravity = 0f;
            w.projectileRadius = 0.12f;
            w.explosionRadius = 0f;
            w.projectileColor = new Color(0.55f, 1f, 0.75f, 1f);
            w.projectileTrail = true;
        });

        var sleep = LoadOrCreate<AbilityDefinition>("Assets/Data/SleepDart_Data.asset", a =>
        {
            a.abilityName = "Uspávací šipka";
            a.cooldown = 12f;
            a.power = 5f;        // drobne poskozeni pri zasahu
            a.duration = 3f;     // jak dlouho cil spi (zasah ho probudi)
            a.range = 50f;
            a.speed = 90f;
        });

        var grenade = LoadOrCreate<AbilityDefinition>("Assets/Data/BioticGrenade_Data.asset", a =>
        {
            a.abilityName = "Biotický granát";
            a.cooldown = 10f;
            a.power = 45f;       // leceni spoluhracu i poskozeni nepratel
            a.radius = 4f;
            a.duration = 3f;     // blokace leceni nepratel
            a.range = 25f;
            a.speed = 30f;
        });

        var nano = LoadOrCreate<AbilityDefinition>("Assets/Data/NanoBoost_Data.asset", a =>
        {
            a.abilityName = "Posílení";
            a.cooldown = 30f;
            a.ultCost = 400f;
            a.power = 50f;       // okamzite vyleceni posileneho
            a.duration = 8f;
            a.range = 45f;
        });

        LoadOrCreate<HeroDefinition>("Assets/Resources/Heroes/Anna.asset", h =>
        {
            h.heroName = "Anna";
            h.color = new Color(0.35f, 0.75f, 0.85f);
            h.maxHealth = 100f;
            h.weapon = rifle;
            h.abilityKind = AbilityKind.NanoBoost;
            h.ability = nano;
            h.secondaryAbilityKind = AbilityKind.SleepDart;
            h.secondaryAbility = sleep;
            h.altAbilityKind = AbilityKind.BioticGrenade;
            h.altAbility = grenade;
            h.sleeveColor = new Color(0.25f, 0.33f, 0.4f);
        });
    }

    // Sniper (inspirace: Widowmaker, ale s obranou na blizko). Bez pribliseni slabsi samopal (prohraje s Viktorem na
    // stredni vzdalenost), s pribliseni (prave tlacitko) se nabiji odstrel 30-65 dmg, plne nabity do hlavy x2 (zabije kazdeho).
    // Shift = hak (pritahne se na misto), E = odkopnuti, Q = infravize. Mene zdravi (90 HP).
    static void CreateSniper()
    {
        var rifle = LoadOrCreate<WeaponDefinition>("Assets/Data/SniperRifle_Data.asset", w =>
        {
            w.weaponName = "Odstřelovačka";
            w.fireMode = FireMode.Hitscan;
            w.heldModel = HeldModel.Gun;
            w.damage = 7f;
            w.fireRate = 8f;
            w.maxAmmo = 16;
            w.reloadTime = 1.8f;
            w.range = 250f;
            w.falloffStart = 12f;
            w.falloffEnd = 30f;
            w.falloffMin = 0.25f;
            w.maxDamageRange = 40f;
            w.scopeFov = 25f;
            w.scopedChargeTime = 1.2f;
            w.scopedMinDamage = 30f;
            w.scopedMaxDamage = 65f;
            w.headshotMultiplier = 2f;
            w.scopedAmmoCost = 2;
            w.scopedMoveSpeed = 0.5f;
            w.projectileColor = new Color(1f, 0.35f, 0.3f, 1f);
        });

        var grapple = LoadOrCreate<AbilityDefinition>("Assets/Data/Grapple_Data.asset", a =>
        {
            a.abilityName = "Hák";
            a.cooldown = 8f;
            a.range = 25f;
            a.speed = 22f;       // rychlost pritahovani (m/s)
        });

        var kick = LoadOrCreate<AbilityDefinition>("Assets/Data/Kick_Data.asset", a =>
        {
            a.abilityName = "Odkopnutí";
            a.cooldown = 7f;
            a.power = 25f;
            a.range = 2.6f;
            a.knockback = 13f;   // odhozeni zhruba 6 m
            a.duration = 1.5f;   // zpomaleni na polovinu
        });

        var infra = LoadOrCreate<AbilityDefinition>("Assets/Data/Infra_Data.asset", a =>
        {
            a.abilityName = "Infravize";
            a.cooldown = 30f;
            a.ultCost = 450f;
            a.duration = 10f;
        });

        LoadOrCreate<HeroDefinition>("Assets/Resources/Heroes/Sniper.asset", h =>
        {
            h.heroName = "Sniper";
            h.color = new Color(0.75f, 0.3f, 0.35f);
            h.maxHealth = 90f;
            h.weapon = rifle;
            h.abilityKind = AbilityKind.Infra;
            h.ability = infra;
            h.secondaryAbilityKind = AbilityKind.Grapple;
            h.secondaryAbility = grapple;
            h.altAbilityKind = AbilityKind.Kick;
            h.altAbility = kick;
            h.sleeveColor = new Color(0.2f, 0.18f, 0.22f);
        });
    }

    // Flanker (inspirace: Tracer): rychla, krehka (75 HP). Dve pistole (rychla palba, kratky dosah), Shift = premisteni
    // (3 nabiti), E = navrat v case (poloha i zdravi pred 3 s), Q = lepiva pulzni bomba.
    static void CreateFlanker()
    {
        var pistols = LoadOrCreate<WeaponDefinition>("Assets/Data/FlankerPistols_Data.asset", w =>
        {
            w.weaponName = "Dvojité pistole";
            w.fireMode = FireMode.Hitscan;
            w.heldModel = HeldModel.Gun;
            w.damage = 6f;
            w.fireRate = 18f;
            w.maxAmmo = 36;
            w.reloadTime = 1.2f;
            w.range = 60f;
            w.falloffStart = 10f;
            w.falloffEnd = 22f;
            w.falloffMin = 0.3f;
            w.maxDamageRange = 35f;
            w.projectileColor = new Color(0.4f, 0.75f, 1f, 1f);
        });

        var blink = LoadOrCreate<AbilityDefinition>("Assets/Data/Blink_Data.asset", a =>
        {
            a.abilityName = "Přemístění";
            a.cooldown = 3f;     // obnoveni jednoho nabiti
            a.charges = 3;
            a.power = 7f;        // delka premisteni v metrech
        });

        var recall = LoadOrCreate<AbilityDefinition>("Assets/Data/Recall_Data.asset", a =>
        {
            a.abilityName = "Návrat v čase";
            a.cooldown = 12f;
            a.duration = 3f;     // o kolik sekund zpet
        });

        var bomb = LoadOrCreate<AbilityDefinition>("Assets/Data/PulseBomb_Data.asset", a =>
        {
            a.abilityName = "Pulzní bomba";
            a.cooldown = 30f;
            a.ultCost = 350f;
            a.power = 150f;      // poskozeni ve stredu vybuchu
            a.radius = 3f;
            a.duration = 1.5f;   // odpocet po prilepeni
            a.speed = 20f;       // rychlost hodu
        });

        LoadOrCreate<HeroDefinition>("Assets/Resources/Heroes/Flanker.asset", h =>
        {
            h.heroName = "Flanker";
            h.color = new Color(0.95f, 0.6f, 0.2f);
            h.maxHealth = 75f;
            h.weapon = pistols;
            h.abilityKind = AbilityKind.PulseBomb;
            h.ability = bomb;
            h.secondaryAbilityKind = AbilityKind.Blink;
            h.secondaryAbility = blink;
            h.altAbilityKind = AbilityKind.Recall;
            h.altAbility = recall;
            h.sleeveColor = new Color(0.55f, 0.32f, 0.12f);
        });

        // Dve pistole (doplneno pozdeji).
        if (!pistols.dualWield)
        {
            pistols.dualWield = true;
            EditorUtility.SetDirty(pistols);
        }
    }

    // Bard (inspirace: Lucio): podpurce s aurou. Shift prepina leceni / zrychleni spoluhracu v okoli, E auru na chvili
    // zesili, Q = Koncert (docasny stit vsem spoluhracum v okoli). Strili zvukove strely.
    static void CreateBard()
    {
        var gun = LoadOrCreate<WeaponDefinition>("Assets/Data/BardGun_Data.asset", w =>
        {
            w.weaponName = "Zvukomet";
            w.fireMode = FireMode.Projectile;
            w.heldModel = HeldModel.Gun;
            w.damage = 16f;
            w.fireRate = 4.5f;
            w.maxAmmo = 20;
            w.reloadTime = 1.5f;
            w.range = 100f;
            w.projectileSpeed = 50f;
            w.projectileGravity = 0f;
            w.projectileRadius = 0.18f;
            w.explosionRadius = 0f;
            w.projectileColor = new Color(0.75f, 0.5f, 1f, 1f);
            w.projectileTrail = true;
        });

        var crossfade = LoadOrCreate<AbilityDefinition>("Assets/Data/Crossfade_Data.asset", a =>
        {
            a.abilityName = "Přepnutí rytmu";
            a.cooldown = 1f;
            a.power = 16f;       // leceni za sekundu (sobe polovina)
            a.radius = 12f;      // dosah aury
        });

        var amp = LoadOrCreate<AbilityDefinition>("Assets/Data/Amp_Data.asset", a =>
        {
            a.abilityName = "Zesílení";
            a.cooldown = 12f;
            a.power = 2.5f;      // nasobek leceni
            a.duration = 3f;
        });

        var concert = LoadOrCreate<AbilityDefinition>("Assets/Data/Concert_Data.asset", a =>
        {
            a.abilityName = "Koncert";
            a.cooldown = 30f;
            a.ultCost = 450f;
            a.power = 120f;      // stit kazdemu spoluhraci
            a.duration = 6f;     // za jak dlouho stit vyprcha
            a.radius = 15f;
        });

        var wave = LoadOrCreate<AbilityDefinition>("Assets/Data/Soundwave_Data.asset", a =>
        {
            a.abilityName = "Basový úder";
            a.cooldown = 5f;
            a.power = 15f;       // poskozeni nepratel
            a.range = 7f;        // dosah vlny
            a.knockback = 12f;   // odhozeni nepratel (zhruba 6 m)
            a.radius = 3.5f;     // jak blizko musi byt zem / zed pro odraz
            a.speed = 12f;       // sila vlastniho odrazu
        });

        var bard = LoadOrCreate<HeroDefinition>("Assets/Resources/Heroes/Bard.asset", h =>
        {
            h.heroName = "Bard";
            h.color = new Color(0.6f, 0.4f, 0.95f);
            h.maxHealth = 100f;
            h.weapon = gun;
            h.abilityKind = AbilityKind.Concert;
            h.ability = concert;
            h.secondaryAbilityKind = AbilityKind.Crossfade;
            h.secondaryAbility = crossfade;
            h.altAbilityKind = AbilityKind.Amp;
            h.altAbility = amp;
            h.sleeveColor = new Color(0.3f, 0.22f, 0.45f);
        });

        // Pohyblivost (doplneno pozdeji): dvojity skok a basovy uder.
        if (bard.rmbAbility == null || !bard.doubleJump)
        {
            bard.doubleJump = true;
            bard.rmbAbilityKind = AbilityKind.Soundwave;
            bard.rmbAbility = wave;
            EditorUtility.SetDirty(bard);
        }
    }

    // Sindel (Jules z Pulp Fiction): cerny oblek, tezka pistole (zasah do hlavy x2). Shift = spravedlivy skok se vznasenim,
    // E = svaty granat (hod obloukem, plosny vybuch), Q = Ezechiel 25:17 (kazani ve vzduchu, na konci hlasky
    // uder svetla do kruhu). Delka ultimatky = delka nahravky v Audio/Sindel/ability_Q.
    static void CreateSindel()
    {
        var pistol = LoadOrCreate<WeaponDefinition>("Assets/Data/SindelPistol_Data.asset", w =>
        {
            w.weaponName = "Devítka";
            w.fireMode = FireMode.Hitscan;
            w.heldModel = HeldModel.Gun;
            w.damage = 38f;
            w.fireRate = 2.4f;
            w.maxAmmo = 8;
            w.reloadTime = 1.5f;
            w.range = 120f;
            w.falloffStart = 25f;
            w.falloffEnd = 45f;
            w.falloffMin = 0.5f;
            w.headshotMultiplier = 2f;
            w.projectileColor = new Color(1f, 0.85f, 0.45f, 1f);
        });

        var leap = LoadOrCreate<AbilityDefinition>("Assets/Data/RighteousLeap_Data.asset", a =>
        {
            a.abilityName = "Spravedlivý skok";
            a.cooldown = 7f;
            a.power = 10f;       // rychlost vyskoku (m/s)
            a.duration = 2.2f;   // vyskok + vznaseni celkem
        });

        var grenade = LoadOrCreate<AbilityDefinition>("Assets/Data/HolyGrenade_Data.asset", a =>
        {
            a.abilityName = "Svatý granát";
            a.cooldown = 10f;
            a.power = 70f;       // poskozeni ve stredu vybuchu
            a.radius = 3.5f;
            a.speed = 20f;       // rychlost hodu
        });

        var ezekiel = LoadOrCreate<AbilityDefinition>("Assets/Data/Ezekiel_Data.asset", a =>
        {
            a.abilityName = "Ezechiel 25:17";
            a.cooldown = 30f;
            a.ultCost = 450f;
            a.power = 220f;      // poskozeni ve stredu kruhu (k okraji 35 %)
            a.radius = 5f;
            a.duration = 7f;     // jen kdyz chybi nahravka (jinak delka hlasky)
            a.range = 120f;      // jak daleko muze mirit
        });

        var sindel = LoadOrCreate<HeroDefinition>("Assets/Resources/Heroes/Sindel.asset", h =>
        {
            h.heroName = "Šindel";
            h.color = new Color(0.85f, 0.7f, 0.3f);
            h.maxHealth = 100f;
            h.weapon = pistol;
            h.abilityKind = AbilityKind.Ezekiel;
            h.ability = ezekiel;
            h.secondaryAbilityKind = AbilityKind.RighteousLeap;
            h.secondaryAbility = leap;
            h.altAbilityKind = AbilityKind.HolyGrenade;
            h.altAbility = grenade;
            h.sleeveColor = new Color(0.07f, 0.07f, 0.08f);
        });

        // Na E byl puvodne "Oznaceni hrisnika" - nahrazen granatem.
        if (sindel.altAbility != grenade)
        {
            sindel.altAbilityKind = AbilityKind.HolyGrenade;
            sindel.altAbility = grenade;
            EditorUtility.SetDirty(sindel);
        }
        // Vyssi let (25 m) - delsi dosah mireni.
        if (ezekiel.range < 120f)
        {
            ezekiel.range = 120f;
            EditorUtility.SetDirty(ezekiel);
        }
        if (AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/Data/SinnerMark_Data.asset") != null)
            AssetDatabase.DeleteAsset("Assets/Data/SinnerMark_Data.asset");
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
            AddIfMissing<SleepDartAbility>(contents);
            AddIfMissing<BioticGrenadeAbility>(contents);
            AddIfMissing<NanoBoostAbility>(contents);
            AddIfMissing<ScopeZoom>(contents);
            AddIfMissing<GrappleAbility>(contents);
            AddIfMissing<KickAbility>(contents);
            AddIfMissing<InfraAbility>(contents);
            AddIfMissing<BlinkAbility>(contents);
            AddIfMissing<RecallAbility>(contents);
            AddIfMissing<PulseBombAbility>(contents);
            AddIfMissing<BardAbility>(contents);
            AddIfMissing<SindelAbility>(contents);
            AddIfMissing<EzekielAbility>(contents);
            AddIfMissing<HeroVoice>(contents);

            // Rychlejsi pohyb (puvodne chuze 5, sprint 8 m/s).
            var movement = contents.GetComponent<FirstPersonController>();
            if (movement != null && movement.walkSpeed < WalkSpeed)
            {
                movement.walkSpeed = WalkSpeed;
                movement.runSpeed = Mathf.Max(movement.runSpeed, RunSpeed);
            }

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
