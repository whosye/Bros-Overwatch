using System.Collections.Generic;
using UnityEngine;

// V buildu se cast materialu vykresli ruzove (shader chybi nebo ho URP v prehravaci nepodporuje) - typicky
// vychozi material ProBuilderu na objektech mapy. Po nacteni sceny takove materialy nahradime obycejnym Lit materialem.
// V editoru, kde jsou shadery v poradku, se nic nemeni.
public static class MaterialFallback
{
    static readonly Color MapColor = new Color(0.78f, 0.76f, 0.72f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Apply()
    {
        var replacements = new Dictionary<Material, Material>();
        int count = 0;

        foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;

            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material != null && !IsBroken(material)) continue;

                Material replacement = null;
                if (material == null || !replacements.TryGetValue(material, out replacement))
                {
                    replacement = Fx.NewLit(MapColor);
                    if (material != null)
                    {
                        replacement.name = material.name + " (fallback)";
                        replacements[material] = replacement;
                    }
                }

                materials[i] = replacement;
                changed = true;
                count++;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }

        if (count > 0)
            Debug.Log($"[MaterialFallback] Nahrazeno {count} materialu s nepodporovanym shaderem.");
    }

    static bool IsBroken(Material material)
    {
        var shader = material.shader;
        return shader == null || !shader.isSupported || shader.name == "Hidden/InternalErrorShader";
    }
}
