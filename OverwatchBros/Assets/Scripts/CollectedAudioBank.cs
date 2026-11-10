using System;
using System.Collections.Generic;
using UnityEngine;

// Exported from Sound/sounds_dir by tools/sound-collector/export_to_unity.py.
// Existing weapon/ability/voice assets are deliberately never modified by this bank.
public sealed class CollectedAudioBank : ScriptableObject
{
    [Serializable]
    public sealed class Cue
    {
        public string id;
        public AudioClip[] clips;
    }

    public int collectionRevision;
    public Cue[] cues;
    static CollectedAudioBank bank;
    static Dictionary<string, AudioClip[]> lookup;
    static readonly Dictionary<string, int> last = new Dictionary<string, int>();
    static readonly Dictionary<string, AudioClip> byName = new Dictionary<string, AudioClip>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        bank = null;
        lookup = null;
        last.Clear();
        byName.Clear();
    }

    static void EnsureLoaded()
    {
        if (lookup != null) return;
        lookup = new Dictionary<string, AudioClip[]>();
        bank = Resources.Load<CollectedAudioBank>("CollectedAudioBank");
        if (bank == null || bank.cues == null) return;
        foreach (var cue in bank.cues)
        {
            if (cue == null || string.IsNullOrEmpty(cue.id) || cue.clips == null) continue;
            var valid = Array.FindAll(cue.clips, clip => clip != null);
            lookup[cue.id] = valid;
            foreach (var clip in valid) byName[clip.name] = clip;
        }
    }

    public static AudioClip Pick(string id, AudioClip fallback = null, int actor = 0)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(id) || !lookup.TryGetValue(id, out var clips) || clips.Length == 0) return fallback;
        string key = id + ":" + actor;
        int index = last.TryGetValue(key, out int previous) && clips.Length > 1
            ? (previous + 1 + UnityEngine.Random.Range(0, clips.Length - 1)) % clips.Length
            : UnityEngine.Random.Range(0, clips.Length);
        last[key] = index;
        return clips[index];
    }

    // Replay and networked movement use the already chosen variant, not a new random draw.
    public static AudioClip Find(string name)
    {
        EnsureLoaded();
        return !string.IsNullOrEmpty(name) && byName.TryGetValue(name, out var clip) ? clip : null;
    }

    public static AudioClip ForActor(Component actor, string cue, AudioClip fallback = null) =>
        Pick(cue, fallback, actor != null ? actor.GetEntityId().GetHashCode() : 0);

    public static AudioClip Ability(AbilityDefinition ability, string cue, AudioClip fallback)
    {
        // A manually assigned recording always takes priority over collection replacements.
        return ability != null && ability.sound != null ? ability.sound : Pick(cue, fallback);
    }

    public static string WeaponCue(WeaponDefinition weapon)
    {
        if (weapon == null) return "";
        switch (weapon.name)
        {
            case "Pistol_Data": return "viktor";
            case "Rocket_Data": return "honza";
            case "SniperRifle_Data": return "kondulic";
            case "AnnaRifle_Data": return "david";
            case "BardGun_Data": return "mosino";
            case "SindelPistol_Data": return "sindel";
            case "Scythe_Data": return "max";
            default: return weapon.IsMelee ? "pova" : "";
        }
    }

    public static bool Has(string id)
    {
        EnsureLoaded();
        return lookup.TryGetValue(id, out var clips) && clips.Length > 0;
    }

    public static AudioClip SmallExplosion() => Pick("combat_explosion_small") ?? Pick("honza_mine.explode")
        ?? Pick("sindel_grenade") ?? ProceduralSfx.Explosion;
}
