using System;
using UnityEngine;

public static class HeroRegistry
{
    static HeroDefinition[] all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        all = null;
    }

    public static HeroDefinition[] All
    {
        get
        {
            if (all == null)
            {
                all = Resources.LoadAll<HeroDefinition>("Heroes");
                Array.Sort(all, (a, b) => string.CompareOrdinal(a.name, b.name));
            }
            return all;
        }
    }

    public static HeroDefinition Get(int index)
    {
        var list = All;
        return index >= 0 && index < list.Length ? list[index] : null;
    }
}
