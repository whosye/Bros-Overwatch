using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Batch entry point: imports, references, semantic pools, variation, surface detection and scene audit.
public static class CollectedAudioValidation
{
    static int assertions;

    static void Check(bool pass, string message)
    {
        assertions++;
        if (!pass) throw new InvalidOperationException("[CollectedAudio] " + message);
    }

    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var bank = Resources.Load<CollectedAudioBank>("CollectedAudioBank");
            Check(bank != null, "Bank missing / script reference invalid.");
            Check(bank.collectionRevision >= 341, "Old user selection revision.");
            Check(bank.cues.Length >= 70, "Expected semantic event pools.");
            var ids = new HashSet<string>();
            int clips = 0;
            foreach (var cue in bank.cues)
            {
                Check(ids.Add(cue.id), "Duplicate cue " + cue.id);
                Check(cue.clips != null && cue.clips.Length > 0, "Empty cue " + cue.id);
                foreach (var clip in cue.clips)
                {
                    clips++;
                    Check(clip != null, "Missing imported clip " + cue.id);
                    Check(clip.length > 0.01f && clip.frequency == 44100, "Invalid decoded audio " + clip.name);
                    Check(AssetDatabase.GetAssetPath(clip).StartsWith("Assets/Audio/Collected/"), "Unexpected overwrite of old audio.");
                    Check(CollectedAudioBank.Find(clip.name) == clip, "Replay cannot find variant " + clip.name);
                    if (cue.id.EndsWith(".step"))
                        Check(!clip.name.Contains("land") && !clip.name.Contains("splash") && !clip.name.Contains("entry") && !clip.name.Contains("exit"), "Landing mixed into walking " + clip.name);
                    if (cue.id.EndsWith(".swing"))
                        Check(!clip.name.Contains("hit") && !clip.name.Contains("stab"), "Impact mixed into swing " + clip.name);
                }
                if (cue.clips.Length > 1)
                {
                    AudioClip previous = CollectedAudioBank.Pick(cue.id, actor: 123);
                    for (int i = 0; i < 30; i++)
                    {
                        AudioClip next = CollectedAudioBank.Pick(cue.id, actor: 123);
                        Check(next != previous, "Immediate repeat " + cue.id);
                        previous = next;
                    }
                }
            }
            foreach (string material in new[] { "wood", "solid_wood", "stone", "tile", "gravel", "grass", "dirt", "sand", "carpet", "rubber", "metal" })
            {
                Check(CollectedAudioBank.Has("steps_" + material + ".step"), "Missing footsteps " + material);
                Check(CollectedAudioBank.Has("steps_" + material + ".land"), "Missing landings " + material);
            }
            foreach (string hero in new[] { "viktor", "honza", "kondulic", "david", "mosino", "sindel" })
                Check(CollectedAudioBank.Has(hero + "_reload"), "Missing assembled reload " + hero);

            var custom = AudioClip.Create("test_custom", 4410, 1, 44100, false);
            var ability = ScriptableObject.CreateInstance<AbilityDefinition>();
            ability.sound = custom;
            Check(CollectedAudioBank.Ability(ability, "viktor_ult", ProceduralSfx.UltCharge) == custom, "Custom recording lost precedence.");
            UnityEngine.Object.DestroyImmediate(ability);
            UnityEngine.Object.DestroyImmediate(custom);

            CheckSurface("Wood_Planks", AudioSurfaceKind.Wood);
            CheckSurface("Wood_Beam", AudioSurfaceKind.SolidWood);
            CheckSurface("FurnitureWood", AudioSurfaceKind.SolidWood);
            CheckSurface("Koberec", AudioSurfaceKind.Carpet);
            CheckSurface("Tiles", AudioSurfaceKind.Tile);
            CheckSurface("Gravel", AudioSurfaceKind.Gravel);
            CheckSurface("GrassBlades", AudioSurfaceKind.Grass);
            CheckSurface("Soil", AudioSurfaceKind.Dirt);
            CheckSurface("Rubber", AudioSurfaceKind.Rubber);
            CheckSurface("Metal", AudioSurfaceKind.Metal);
            CheckSurface("Stone_Wall", AudioSurfaceKind.Stone);
            CheckSurface("Glass", AudioSurfaceKind.Glass);

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            var surfaces = new Dictionary<AudioSurfaceKind, int>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (collider.isTrigger) continue;
                    var kind = AudioSurface.Of(collider, collider.bounds.center);
                    surfaces[kind] = surfaces.TryGetValue(kind, out int count) ? count + 1 : 1;
                }
            var summary = new List<string> { "PASS", "Assertions: " + assertions, "Cues: " + bank.cues.Length,
                "Clip references: " + clips, "Selection revision: " + bank.collectionRevision, "Scene surface audit:" };
            foreach (var entry in surfaces) summary.Add(entry.Key + ": " + entry.Value);
            string output = Path.GetFullPath("../tools/sound-collector/research/UNITY_AUDIO_VALIDATION.txt");
            File.WriteAllLines(output, summary);
            Debug.Log("[CollectedAudio] PASS " + assertions + " assertions; " + bank.cues.Length + " cues; " + clips + " references.");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    static void CheckSurface(string name, AudioSurfaceKind expected)
    {
        var go = new GameObject(name);
        var collider = go.AddComponent<BoxCollider>();
        Check(AudioSurface.Of(collider, Vector3.zero) == expected, "Surface " + name + " should be " + expected);
        var explicitSurface = go.AddComponent<AudioSurface>();
        explicitSurface.kind = AudioSurfaceKind.Sand;
        Check(AudioSurface.Of(collider, Vector3.zero) == AudioSurfaceKind.Sand, "Explicit override ignored.");
        UnityEngine.Object.DestroyImmediate(go);
    }
}
