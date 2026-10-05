using System.Collections.Generic;
using UnityEngine;

public enum VoiceKind
{
    Spawn,     // vyber hrdiny / oziveni
    Kill,      // zabil protihrace
    Death,     // zemrel
    Hurt,      // dostal poskozeni
    Idle,      // nahodna hlaska pri chozeni
    Ability,   // pouzil schopnost (kazda schopnost ma vlastni hlasky)
    Snare      // nekdo ho znehybnil nebo zpomalil (past apod.)
}

// Hlasove hlasky hrdiny. Nahravky se berou z HeroDefinition (spawn / kill / death / hurt / idle) a z AbilityDefinition
// (voiceLines, pro kazdou schopnost zvlast). Kdyz je v seznamu vic nahravek, vybere se nahodne jedna.
// Prazdny seznam = nic se neprehraje. Spousti je PlayerHero.Say, ktery hlasku rozesle vsem hracum.
public class HeroVoice : MonoBehaviour
{
    public float volume = 1f;

    AudioSource source;
    int playingPriority = -1;
    bool playingUlt;

    // Vsichni hrdinove na tomto klientovi - aby nemluvilo prilis mnoho postav naraz.
    static readonly List<HeroVoice> all = new List<HeroVoice>();
    static AudioListener listener;

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    bool IsSpeaking => source != null && source.isPlaying;

    // Kolik jinych hrdinu prave mluvi tak, ze je tento hrac slysi (ultimatky pres celou mapu, ostatni do dosahu hlasu).
    int OthersAudible()
    {
        if (listener == null)
            listener = FindAnyObjectByType<AudioListener>();
        Vector3 ear = listener != null ? listener.transform.position : transform.position;

        int count = 0;
        foreach (var other in all)
        {
            if (other == this || !other.IsSpeaking) continue;
            bool global = other.source.spatialBlend < 0.5f;
            if (global || Vector3.Distance(other.source.transform.position, ear) <= other.source.maxDistance)
                count++;
        }
        return count;
    }

    // Dulezitejsi hlaska prerusi mene dulezitou; opacne se mene dulezita preskoci.
    static int Priority(VoiceKind kind)
    {
        switch (kind)
        {
            case VoiceKind.Death: return 6;
            case VoiceKind.Kill: return 5;
            case VoiceKind.Ability: return 4;
            case VoiceKind.Snare: return 3;
            case VoiceKind.Spawn: return 2;
            case VoiceKind.Hurt: return 1;
            default: return 0;
        }
    }

    // slot: 0 = schopnost na Q, 1 = Shift, 2 = blok, 3 = treti schopnost (E), 4 = ctvrta schopnost (prave tlacitko)
    public static AudioClip[] Lines(HeroDefinition hero, VoiceKind kind, int slot)
    {
        if (hero == null) return null;

        switch (kind)
        {
            case VoiceKind.Spawn: return hero.spawnLines;
            case VoiceKind.Kill: return hero.killLines;
            case VoiceKind.Death: return hero.deathLines;
            case VoiceKind.Hurt: return hero.hurtLines;
            case VoiceKind.Idle: return hero.idleLines;
            case VoiceKind.Snare: return hero.snareLines;
            case VoiceKind.Ability:
                var ability = AbilityInSlot(hero, slot);
                return ability != null ? ability.voiceLines : null;
        }

        return null;
    }

    public static bool HasLines(HeroDefinition hero, VoiceKind kind, int slot)
    {
        var lines = Lines(hero, kind, slot);
        return lines != null && lines.Length > 0;
    }

    public static AbilityDefinition AbilityInSlot(HeroDefinition hero, int slot)
    {
        switch (slot)
        {
            case 0: return hero.ability;
            case 1: return hero.secondaryAbility;
            case 2: return hero.blockAbility;
            case 3: return hero.altAbility;
            case 4: return hero.rmbAbility;
        }

        return null;
    }

    public static int SlotOf(HeroDefinition hero, AbilityDefinition ability)
    {
        if (hero == null || ability == null) return -1;

        for (int slot = 0; slot < 5; slot++)
            if (AbilityInSlot(hero, slot) == ability) return slot;

        return -1;
    }

    // Pocet beznych hlasek (bez fazi step_N), mezi kterymi se nahodne vybira.
    public static int NormalLineCount(HeroDefinition hero, VoiceKind kind, int slot)
    {
        var lines = Lines(hero, kind, slot);
        return lines == null ? 0 : System.Array.FindAll(lines, c => c != null && !c.name.ToLowerInvariant().StartsWith("step_")).Length;
    }

    // Nahravky pojmenovane step_1, step_2, ... jsou faze schopnosti (napr. Ayranuv skok: vzlet / dopad).
    // pick < 0 = konkretni faze; jinak nahodny vyber z ostatnich nahravek (kdyz zadne nejsou, hraje faze 1).
    static AudioClip Pick(AudioClip[] lines, int pick)
    {
        if (pick < 0)
            return System.Array.Find(lines, c => c != null && c.name.ToLowerInvariant() == "step_" + (-pick));

        var normal = System.Array.FindAll(lines, c => c != null && !c.name.ToLowerInvariant().StartsWith("step_"));
        if (normal.Length > 0)
            return normal[pick % normal.Length];

        return System.Array.Find(lines, c => c != null && c.name.ToLowerInvariant() == "step_1") ?? lines[pick % lines.Length];
    }

    // 'pick' je nahodne cislo od serveru, aby vsichni hraci slyseli stejnou nahravku.
    public void Play(HeroDefinition hero, VoiceKind kind, int slot, int pick)
    {
        var lines = Lines(hero, kind, slot);
        if (lines == null || lines.Length == 0) return;

        var clip = Pick(lines, pick);
        if (clip == null) return;

        int priority = Priority(kind);
        bool busy = source != null && source.isPlaying;
        bool ult = kind == VoiceKind.Ability && slot == 0;
        if (busy && (priority < playingPriority || (priority == playingPriority && priority < 4))) return;
        // Hlasku k ultimatce neprerusi jina schopnost ani zabiti; jen dalsi faze ultimatky nebo smrt.
        if (busy && playingUlt && !ult && kind != VoiceKind.Death) return;

        // Proti zvukovemu chaosu, kdyz mluvi vic postav u sebe: mene dulezite hlasky (spawn, chuze, zraneni, past)
        // se preskoci, kdyz uz mluvi nekdo jiny; schopnosti a zabiti hraji nejvys dve naraz. Ultimatka a smrt vzdy.
        if (!ult && kind != VoiceKind.Death)
        {
            int others = OthersAudible();
            if (priority < 4 ? others > 0 : others >= 2) return;
        }

        if (source == null)
        {
            var go = new GameObject("Voice");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            source = go.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 4f;
            source.maxDistance = 30f;
            source.playOnAwake = false;
        }

        source.Stop();
        source.clip = clip;
        source.volume = volume;
        // Hlaska k ultimatce (Q) je slyset pres celou mapu, ostatni jen v okoli.
        source.spatialBlend = kind == VoiceKind.Ability && slot == 0 ? 0f : 1f;
        source.Play();
        playingPriority = priority;
        playingUlt = ult;
    }
}
