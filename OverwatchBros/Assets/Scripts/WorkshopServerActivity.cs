using System.Collections.Generic;
using UnityEngine;

/// <summary>Independent disk/network activity; keeps the room's actual lighting steady.</summary>
[DisallowMultipleComponent]
public sealed class WorkshopServerActivity : MonoBehaviour
{
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    Renderer[] indicators;
    Color[] colors;
    MaterialPropertyBlock properties;
    float nextTick;

    void OnEnable()
    {
        var found = new List<Renderer>();
        foreach (var renderer in GetComponentsInChildren<Renderer>())
            if (renderer.name == "Stav_LED") found.Add(renderer);
        indicators = found.ToArray();
        colors = new Color[indicators.Length];
        properties = new MaterialPropertyBlock();
        for (int i = 0; i < indicators.Length; i++)
            colors[i] = indicators[i].sharedMaterial.GetColor(BaseColor);
        nextTick = 0;
    }

    void Update()
    {
        if (Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + 0.08f;
        for (int i = 0; i < indicators.Length; i++)
        {
            if (!indicators[i]) continue;
            // Each drive has its own activity sequence; amber power/status stays mostly lit.
            uint tick = (uint)Mathf.FloorToInt(Time.unscaledTime * (5.3f + i % 5 * 0.67f));
            uint hash = (tick + (uint)(i + 1) * 374761393u) * 668265263u;
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            float level = i % 4 == 3 ? ((hash & 31) == 0 ? 0.2f : 1f) : ((hash & 7) < 3 ? 1f : 0.055f);
            indicators[i].GetPropertyBlock(properties);
            Color color = colors[i] * level;
            color.a = 1f;
            properties.SetColor(BaseColor, color);
            indicators[i].SetPropertyBlock(properties);
        }
    }

    void OnDisable()
    {
        if (indicators == null) return;
        foreach (var indicator in indicators) if (indicator) indicator.SetPropertyBlock(null);
    }
}
