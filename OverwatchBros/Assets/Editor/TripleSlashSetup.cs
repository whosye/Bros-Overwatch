using UnityEditor;
using UnityEngine;

// Povuv Trojsek = zakladni utok (leve tlacitko), zapina ho Ayran_Weapon.comboRotation (ted vypnuty - Pova seka
// obycejne sekyrkami; komponenta na prefabu obsluhuje i Drtivy skok na Shiftu). Spousti se samo (po nacteni
// skriptu a po skonceni hry): prida TripleSlashAbility na prefab hrace (Assets/Prefab/Player.prefab), kdyz chybi.
// (Drivejsi pokus mel Trojsek na Shiftu - kdyby tam u Povy zustal, vrati se Modry plamen.)
// Animace (Mixamo): Assets/Models/Animations/Mixamo/Ayran/Ayran_Combo1-3.fbx (combo2 = zrcadlovy combo1).
[InitializeOnLoad]
public static class TripleSlashSetup
{
    const string PlayerPrefab = "Assets/Prefab/Player.prefab";
    const string HeroPath = "Assets/Resources/Heroes/Ayran.asset";
    const string RushPath = "Assets/Data/Rush_Data.asset";
    const string SlamPath = "Assets/Data/GroundSlam_Data.asset";
    const string SlamDoneKey = "BrosOverwatch.Pova.GroundSlam.v1";
    const string CounterPath = "Assets/Data/Counter_Data.asset";
    const string CounterDoneKey = "BrosOverwatch.Pova.Counter.v1";

    static TripleSlashSetup()
    {
        EditorApplication.delayCall += Run;
        EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Run; };
    }

    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        if (prefab != null && prefab.GetComponent<TripleSlashAbility>() == null)
        {
            var contents = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                if (contents.GetComponent<TripleSlashAbility>() == null) contents.AddComponent<TripleSlashAbility>();
                PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefab);
                Debug.Log("[Trojsek] TripleSlashAbility pridana na " + PlayerPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        // Drtivy skok (Shift): data a prirazeni Povovi misto Modreho plamene - jednou (pak se rucni zmena neprepisuje)
        var slam = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(SlamPath);
        if (slam == null)
        {
            slam = ScriptableObject.CreateInstance<AbilityDefinition>();
            slam.abilityName = "Drtivý skok";
            slam.description = "Vyskočíš na místo v zaměřovači (až 16 m) a dopadneš úderem do země: nepřátele kolem zraníš, "
                + "zpomalíš a nadhodíš.";
            slam.icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Icons/ayran_rush.png");
            slam.cooldown = 8f;
            slam.power = 40f;
            slam.radius = 3.5f;
            slam.range = 16f;
            slam.duration = 0.6f;   // (M7Setup vyzaduje u Povovy druhe schopnosti <= 1,5)
            slam.charges = 1;
            var rush = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(RushPath);
            if (rush != null) slam.voiceLines = rush.voiceLines;
            AssetDatabase.CreateAsset(slam, SlamPath);
        }
        var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>(HeroPath);
        if (hero != null && !EditorPrefs.GetBool(SlamDoneKey, false)
            && (hero.secondaryAbilityKind == AbilityKind.Rush || hero.secondaryAbilityKind == AbilityKind.TripleSlash || hero.secondaryAbility == null))
        {
            hero.secondaryAbilityKind = AbilityKind.GroundSlam;
            hero.secondaryAbility = slam;
            EditorUtility.SetDirty(hero);
            EditorPrefs.SetBool(SlamDoneKey, true);
            Debug.Log("[Trojsek] Pova ma na Shiftu Drtivy skok.");
        }
        // Odveta (leve tlacitko po bloku): data a prirazeni Povovi - jednou (pak se rucni zmena neprepisuje)
        var counter = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(CounterPath);
        if (counter == null)
        {
            counter = ScriptableObject.CreateInstance<AbilityDefinition>();
            counter.abilityName = "Odveta";
            counter.description = "Blokem (pravé tlačítko) sbíráš hněv z vyblokovaného poškození a sekyrky začnou žhnout. Nabité sekyrky drží energii, dokud nezasáhneš nepřítele: první zásah levým ji uvolní úderem do kříže - čím víc hněvu, tím silnější, nepřátele odhodí. Sek do prázdna energii nespotřebuje.";
            counter.icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Icons/ayran_block.png");
            counter.cooldown = 5f;      // po uvolneni nabitych sekyrek
            counter.power = 20f;      // zaklad, + 0,7 x hnev (BlockAbility)
            counter.range = 3.4f;     // dosah seku
            counter.radius = 3.4f;
            counter.duration = 1.2f;  // okno po pusteni bloku
            counter.charges = 1;
            AssetDatabase.CreateAsset(counter, CounterPath);
        }
        if (hero != null && hero.counterAbility == null && !EditorPrefs.GetBool(CounterDoneKey, false))
        {
            hero.counterAbility = counter;
            EditorUtility.SetDirty(hero);
            EditorPrefs.SetBool(CounterDoneKey, true);
            Debug.Log("[Odveta] Pova ma Odvetu (leve tlacitko po bloku).");
        }
        AssetDatabase.SaveAssets();
    }
}
