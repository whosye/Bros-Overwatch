using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Animace strelby z Mixama byvaji dlouhe sekvence s nekolika vystrely (napr. "Gunplay", 2,5 s). Hra ale pri kazdem
// vystrelu prehraje cely klip strelby stlaceny do zlomku sekundy (CharacterVisual.PlayAttack) - dlouhy klip pak
// skube. Tenhle nastroj klip prehraje na postave hrdiny, najde vystrely (prudke cuknuti prave ruky vuci hrudi =
// zpetny raz) a orizne import klipu kolem prvniho z nich (kousek pred nim az po navrat ruky). Soubor FBX se nemeni,
// jen rozsah klipu v nastaveni importu.
// Pouziti: v okne Project vybrat FBX strelby (<Hrdina>_Shoot.fbx v Assets/Models/Animations/Mixamo/<Hrdina>/),
// BrosOverwatch > Animace > Oriznout strelbu na jeden vystrel. Bez vyberu se vezme Sindel_Shoot.fbx.
public static class ShootClipTrim
{
    const string DefaultPath = "Assets/Models/Animations/Mixamo/Sindel/Sindel_Shoot.fbx";
    const float Fps = 30f;
    const int Before = 5;    // snimku pred vystrelem (zvednuti / napnuti)
    const int After = 12;    // snimku po vystrelu (zpetny raz a navrat)

    [MenuItem("BrosOverwatch/Animace/Oriznout strelbu na jeden vystrel")]
    static void Trim()
    {
        string path = DefaultPath;
        var selected = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
        if (!string.IsNullOrEmpty(selected) && selected.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) path = selected;

        if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) { Debug.LogError("[Strelba] Neni FBX: " + path); return; }

        // nejdriv cely klip (kdyby uz byl oriznuty)
        var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
        var full = importer.defaultClipAnimations;
        if (clips.Length == 0 || full.Length == 0) { Debug.LogError("[Strelba] FBX nema animaci: " + path); return; }
        float fullFirst = full[0].firstFrame, fullLast = full[0].lastFrame;
        if (!Mathf.Approximately(clips[0].firstFrame, fullFirst) || !Mathf.Approximately(clips[0].lastFrame, fullLast))
        {
            clips[0].firstFrame = fullFirst;
            clips[0].lastFrame = fullLast;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        AnimationClip clip = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip c && !c.name.StartsWith("__preview__")) { clip = c; break; }
        if (clip == null) { Debug.LogError("[Strelba] Klip se nenacetl."); return; }

        var character = CharacterFor(path);
        if (character == null) { Debug.LogError("[Strelba] Nenasel jsem humanoidni postavu hrdiny pro prehrani klipu."); return; }

        var instance = (GameObject)Object.Instantiate(character);
        instance.hideFlags = HideFlags.HideAndDontSave;
        List<float> speed, yaw;
        try
        {
            speed = HandSpeed(instance, clip, out yaw);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
        if (speed == null || speed.Count < 4) { Debug.LogError("[Strelba] Postava nema animator s humanoidni kostrou."); return; }

        // vystrely = vyrazne spicky rychlosti ruky
        float mean = 0f, sq = 0f;
        foreach (var s in speed) { mean += s; sq += s * s; }
        mean /= speed.Count;
        float std = Mathf.Sqrt(Mathf.Max(0f, sq / speed.Count - mean * mean));
        float threshold = mean + std * 1.5f;
        var peaks = new List<int>();
        for (int i = 1; i < speed.Count - 1; i++)
            if (speed[i] > threshold && speed[i] >= speed[i - 1] && speed[i] >= speed[i + 1]
                && (peaks.Count == 0 || i - peaks[peaks.Count - 1] >= 6))
                peaks.Add(i);

        int shot = peaks.Count > 0 ? peaks[0] : Mathf.Min(speed.Count - 1, Before);
        int first = Mathf.Max(0, shot - Before);
        int last = Mathf.Min(speed.Count - 1, shot + After);

        var log = new System.Text.StringBuilder();
        for (int i = 0; i < speed.Count; i++) log.Append(speed[i].ToString("0")).Append(i % 20 == 19 ? "\n" : " ");

        // kam paze v okamziku vystrelu miri vuci bokum: o tolik se pri strelbe dotoci horni polovina tela
        float twist = 0f;
        int counted = 0;
        for (int i = Mathf.Max(0, shot - 2); i <= Mathf.Min(yaw.Count - 1, shot + 2); i++) { twist += yaw[i]; counted++; }
        twist = counted > 0 ? twist / counted : 0f;
        if (Mathf.Abs(twist) < 15f) twist = 0f;   // miri v podstate dopredu
        var set = AnimSetFor(path);
        if (set != null)
        {
            Undo.RecordObject(set, "Natoceni strelby");
            set.shootYaw = Mathf.Round(twist);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
        }

        clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
        clips[0].firstFrame = fullFirst + first;
        clips[0].lastFrame = fullFirst + last;
        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        Debug.Log($"[Strelba] {Path.GetFileName(path)}: {speed.Count} snimku, vystrely ve snimcich [{string.Join(", ", peaks)}] " +
                  $"-> klip oriznut na {first}-{last} ({(last - first) / Fps:0.00} s, vystrel ve snimku {shot}). " +
                  $"Paze mirila {twist:0} st. vedle -> natoceni pri strelbe {(set != null ? set.name + ".shootYaw = " + Mathf.Round(twist) : "(sada animaci nenalezena)")}.\n" +
                  $"Rychlost ruky (st./snimek):\n{log}");
    }

    // ---------------- hod (granat) ----------------

    const string DefaultThrowPath = "Assets/Models/Animations/Mixamo/Sindel/Sindel_EAbility.fbx";
    const int ThrowBefore = 14;   // napraZeni pred pustenim
    const int ThrowAfter = 8;     // dokonceni po pusteni

    // Animace hodu (napr. Mixamo Goalie Throw) ma kolem samotneho hodu dlouhe pripravy a dobihani. Pusteni = nejrychlejsi
    // pohyb prave ruky; klip se orizne na napraZeni pred nim a kratke dokonceni po nem.
    [MenuItem("BrosOverwatch/Animace/Oriznout hod na samotny hod")]
    static void TrimThrow()
    {
        string path = DefaultThrowPath;
        var selected = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
        if (!string.IsNullOrEmpty(selected) && selected.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) path = selected;
        if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) { Debug.LogError("[Hod] Neni FBX: " + path); return; }

        var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
        var full = importer.defaultClipAnimations;
        if (clips.Length == 0 || full.Length == 0) { Debug.LogError("[Hod] FBX nema animaci: " + path); return; }
        float fullFirst = full[0].firstFrame, fullLast = full[0].lastFrame;
        if (!Mathf.Approximately(clips[0].firstFrame, fullFirst) || !Mathf.Approximately(clips[0].lastFrame, fullLast))
        {
            clips[0].firstFrame = fullFirst;
            clips[0].lastFrame = fullLast;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        AnimationClip clip = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip c && !c.name.StartsWith("__preview__")) { clip = c; break; }
        var character = CharacterFor(path);
        if (clip == null || character == null) { Debug.LogError("[Hod] Chybi klip nebo postava hrdiny."); return; }

        var instance = (GameObject)Object.Instantiate(character);
        instance.hideFlags = HideFlags.HideAndDontSave;
        List<float> speed;
        try { speed = HandSpeed(instance, clip, out _); }
        finally { Object.DestroyImmediate(instance); }
        if (speed == null || speed.Count < 4) { Debug.LogError("[Hod] Postava nema animator s humanoidni kostrou."); return; }

        // pusteni = nejvetsi rychlost ruky (prumer pres 3 snimky, at to neni jeden skok)
        int release = 1;
        float best = -1f;
        for (int i = 1; i < speed.Count - 1; i++)
        {
            float s = speed[i - 1] + speed[i] + speed[i + 1];
            if (s > best) { best = s; release = i; }
        }
        int first = Mathf.Max(0, release - ThrowBefore);
        int last = Mathf.Min(speed.Count - 1, release + ThrowAfter);

        clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
        clips[0].firstFrame = fullFirst + first;
        clips[0].lastFrame = fullFirst + last;
        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        var log = new System.Text.StringBuilder();
        for (int i = 0; i < speed.Count; i++) log.Append(speed[i].ToString("0")).Append(i % 20 == 19 ? "\n" : " ");
        Debug.Log($"[Hod] {Path.GetFileName(path)}: {speed.Count} snimku, pusteni ve snimku {release} -> klip oriznut na {first}-{last} " +
                  $"({(last - first) / Fps:0.00} s).\nRychlost ruky (st./snimek):\n{log}");
    }

    // ---------------- skok s uderem (Max, Shift) ----------------

    const string DefaultLeapPath = "Assets/Models/Animations/Mixamo/Max/Max_ShiftAbility.fbx";

    // Najde v klipu skoku s uderem odraz (boky se zacnou zvedat), dopad (po zvednuti zbrane nad hlavu ruka nejnize
    // vuci bokum = uder do zeme) a konec (kratce po dopadu) a zapise je do sady animaci hrdiny (shiftFrom/Impact/To).
    [MenuItem("BrosOverwatch/Animace/Zmerit skok s uderem (Shift)")]
    static void MeasureLeap()
    {
        string path = DefaultLeapPath;
        var selected = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
        if (!string.IsNullOrEmpty(selected) && selected.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) path = selected;
        MeasureLeapFor(path);
    }

    // (vola i MixamoAnimationSetup sam, kdyz hrdina dostane novou animaci na Shift)
    public static void MeasureLeapFor(string path)
    {

        AnimationClip clip = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip c && !c.name.StartsWith("__preview__")) { clip = c; break; }
        var character = CharacterFor(path);
        var set = AnimSetFor(path);
        if (clip == null || character == null || set == null) { Debug.LogError("[Skok] Chybi klip, postava nebo sada animaci: " + path); return; }

        var instance = (GameObject)Object.Instantiate(character);
        instance.hideFlags = HideFlags.HideAndDontSave;
        var hipsY = new List<float>();
        var handY = new List<float>();
        try
        {
            var animator = instance.GetComponentInChildren<Animator>();
            var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            var hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (hips == null || hand == null) { Debug.LogError("[Skok] Postava nema humanoidni kostru."); return; }
            int frames = Mathf.Max(2, Mathf.RoundToInt(clip.length * Fps) + 1);
            bool started = AnimationMode.InAnimationMode();
            if (!started) AnimationMode.StartAnimationMode();
            try
            {
                for (int f = 0; f < frames; f++)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(animator.gameObject, clip, Mathf.Min(clip.length, f / Fps));
                    AnimationMode.EndSampling();
                    hipsY.Add(hips.position.y);
                    handY.Add(hand.position.y - hips.position.y);
                }
            }
            finally
            {
                if (!started) AnimationMode.StopAnimationMode();
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        int n = handY.Count;
        // zbran nad hlavou = ruka nejvys vuci bokum; uder = potom ruka nejniz
        int raised = 0;
        for (int i = 1; i < n; i++) if (handY[i] > handY[raised]) raised = i;
        int impact = Mathf.Min(n - 1, raised + 1);
        for (int i = raised + 1; i < n; i++) if (handY[i] < handY[impact]) impact = i;

        // odraz: boky se pred vrcholem skoku zacnou zvedat
        int peak = 0;
        for (int i = 0; i <= impact; i++) if (hipsY[i] > hipsY[peak]) peak = i;
        float baseline = float.MaxValue;
        for (int i = 0; i <= peak; i++) baseline = Mathf.Min(baseline, hipsY[i]);
        int takeoff;
        if (hipsY[peak] - baseline > 0.15f)
        {
            takeoff = 0;
            for (int i = peak; i >= 0; i--)
                if (hipsY[i] <= baseline + (hipsY[peak] - baseline) * 0.1f) { takeoff = i; break; }
        }
        else
            takeoff = Mathf.Max(0, impact - Mathf.RoundToInt(0.9f * Fps));   // boky se v klipu nezvedaji (skok jen v korenovem pohybu)
        if (takeoff >= impact) takeoff = Mathf.Max(0, impact - 10);
        int end = Mathf.Min(n - 1, impact + Mathf.RoundToInt(0.5f * Fps));

        Undo.RecordObject(set, "Casy skoku");
        set.shiftFrom = takeoff / Fps;
        set.shiftImpact = impact / Fps;
        set.shiftTo = end / Fps;
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Skok] {Path.GetFileName(path)} ({clip.length:0.00} s): odraz {set.shiftFrom:0.00} s, uder {set.shiftImpact:0.00} s, " +
                  $"konec {set.shiftTo:0.00} s (zbran nahore {raised / Fps:0.00} s, vrchol skoku {peak / Fps:0.00} s) -> {set.name}.");
    }

    // ---------------- natoceni pri strelbe bez orezu (luk) ----------------

    const string DefaultBowPath = "Assets/Models/Animations/Mixamo/Mirek/Mirek_Shoot.fbx";

    // Animace strelby/natahovani, kde postava stoji bokem (lukostrelec): na poslednim snimku (plne natazeno) se zjisti,
    // kam miri vic natazena paze (u luku leva s lukem) vuci bokum, a zapise se do sady jako shootYaw. Klip se neorezava.
    [MenuItem("BrosOverwatch/Animace/Zmerit natoceni strelby (bez orezu)")]
    static void MeasureYaw()
    {
        string path = DefaultBowPath;
        var selected = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
        if (!string.IsNullOrEmpty(selected) && selected.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) path = selected;
        MeasureYawFor(path);
    }

    // (vola i MixamoAnimationSetup sam, kdyz hrdina dostane novou animaci strelby)
    public static void MeasureYawFor(string path)
    {
        {
            string heroName = Path.GetFileName(Path.GetDirectoryName(path));
            var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>($"Assets/Resources/Heroes/{heroName}.asset");
            if (hero != null && hero.weapon != null && hero.weapon.heldModel == HeldModel.Bow)
            {
                Debug.Log($"[Natoceni] {heroName}: luk - trup se pri natahovani srovnava sam podle ramen, nic se nemeri.");
                return;
            }
        }
        AnimationClip clip = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip c && !c.name.StartsWith("__preview__")) { clip = c; break; }
        var character = CharacterFor(path);
        var set = AnimSetFor(path);
        if (clip == null || character == null || set == null) { Debug.LogError("[Natoceni] Chybi klip, postava nebo sada animaci: " + path); return; }

        var instance = (GameObject)Object.Instantiate(character);
        instance.hideFlags = HideFlags.HideAndDontSave;
        float yaw = 0f;
        string arm = "";
        try
        {
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) { Debug.LogError("[Natoceni] Postava nema humanoidni kostru."); return; }
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            bool started = AnimationMode.InAnimationMode();
            if (!started) AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(animator.gameObject, clip, clip.length);
                AnimationMode.EndSampling();
                Vector3 hipsForward = Vector3.Cross(B(HumanBodyBones.RightUpperLeg).position - B(HumanBodyBones.LeftUpperLeg).position, Vector3.up);
                hipsForward.y = 0f;
                Vector3 left = Vector3.ProjectOnPlane(B(HumanBodyBones.LeftHand).position - B(HumanBodyBones.LeftUpperArm).position, Vector3.up);
                Vector3 right = Vector3.ProjectOnPlane(B(HumanBodyBones.RightHand).position - B(HumanBodyBones.RightUpperArm).position, Vector3.up);
                Vector3 aim = left.sqrMagnitude >= right.sqrMagnitude ? left : right;
                arm = left.sqrMagnitude >= right.sqrMagnitude ? "leva" : "prava";
                yaw = Vector3.SignedAngle(aim, hipsForward, Vector3.up);
            }
            finally
            {
                if (!started) AnimationMode.StopAnimationMode();
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        Undo.RecordObject(set, "Natoceni strelby");
        set.shootYaw = Mathf.Abs(yaw) < 15f ? 0f : Mathf.Round(yaw);
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Natoceni] {Path.GetFileName(path)}: {arm} paze mirila {yaw:0} st. vedle -> {set.name}.shootYaw = {set.shootYaw}.");
    }

    [MenuItem("BrosOverwatch/Animace/Vratit celou strelbu (zrusit oriznuti)")]
    static void Untrim()
    {
        string path = DefaultPath;
        var selected = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
        if (!string.IsNullOrEmpty(selected) && selected.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) path = selected;
        if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) return;
        var clips = importer.clipAnimations;
        var full = importer.defaultClipAnimations;
        if (clips.Length == 0 || full.Length == 0) return;
        clips[0].firstFrame = full[0].firstFrame;
        clips[0].lastFrame = full[0].lastFrame;
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        Debug.Log("[Strelba] " + Path.GetFileName(path) + ": cely klip.");
    }

    // sada animaci hrdiny (slozka <Hrdina>)
    static CharacterAnimSet AnimSetFor(string path)
    {
        string folder = Path.GetDirectoryName(path).Replace('\\', '/');
        string heroName = Path.GetFileName(folder);
        var set = AssetDatabase.LoadAssetAtPath<CharacterAnimSet>($"{folder}/{heroName}_Animations.asset");
        if (set != null) return set;
        var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>($"Assets/Resources/Heroes/{heroName}.asset");
        return hero != null ? hero.animations : null;
    }

    // rychlost otaceni prave ruky vuci hrudi (stupne za snimek) a kam miri prava paze vuci bokum (stupne, + = treba dotocit doprava)
    static List<float> HandSpeed(GameObject instance, AnimationClip clip, out List<float> yaw)
    {
        yaw = new List<float>();
        var animator = instance.GetComponentInChildren<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return null;
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var chest = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        var lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        var upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        var rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        if (hand == null || chest == null || upper == null || leftLeg == null || rightLeg == null) return null;

        int frames = Mathf.Max(2, Mathf.RoundToInt(clip.length * Fps) + 1);
        var result = new List<float>(frames);
        Quaternion previous = Quaternion.identity, previousArm = Quaternion.identity;
        bool started = AnimationMode.InAnimationMode();
        if (!started) AnimationMode.StartAnimationMode();
        try
        {
            for (int f = 0; f < frames; f++)
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(animator.gameObject, clip, Mathf.Min(clip.length, f / Fps));
                AnimationMode.EndSampling();
                Quaternion relative = Quaternion.Inverse(chest.rotation) * hand.rotation;
                Quaternion arm = lower != null ? Quaternion.Inverse(chest.rotation) * lower.rotation : relative;
                result.Add(f == 0 ? 0f : Quaternion.Angle(previous, relative) + Quaternion.Angle(previousArm, arm) * 0.5f);
                Vector3 hipsForward = Vector3.Cross(rightLeg.position - leftLeg.position, Vector3.up);
                Vector3 aim = Vector3.ProjectOnPlane(hand.position - upper.position, Vector3.up);
                hipsForward.y = 0f;
                yaw.Add(aim.sqrMagnitude > 1e-6f && hipsForward.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(aim, hipsForward, Vector3.up) : 0f);
                previous = relative;
                previousArm = arm;
            }
        }
        finally
        {
            if (!started) AnimationMode.StopAnimationMode();
        }
        return result;
    }

    // humanoidni postava hrdiny podle slozky (<Hrdina>), jinak jakakoli postava hrdiny
    static GameObject CharacterFor(string path)
    {
        string heroName = Path.GetFileName(Path.GetDirectoryName(path));
        var hero = AssetDatabase.LoadAssetAtPath<HeroDefinition>($"Assets/Resources/Heroes/{heroName}.asset");
        if (hero != null && hero.characterPrefab != null) return hero.characterPrefab;
        foreach (var guid in AssetDatabase.FindAssets("t:HeroDefinition", new[] { "Assets/Resources/Heroes" }))
        {
            var other = AssetDatabase.LoadAssetAtPath<HeroDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (other != null && other.characterPrefab != null) return other.characterPrefab;
        }
        return null;
    }
}
