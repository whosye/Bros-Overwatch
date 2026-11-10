using System.Globalization;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum AudioSurfaceKind : byte
{
    Stone, Wood, SolidWood, Tile, Gravel, Grass, Dirt, Sand, Carpet, Rubber, Metal, Water, Glass, Soft, Body, Hay
}

// Optional explicit override for meshes whose visual material does not describe their sound.
public sealed class AudioSurface : MonoBehaviour
{
    public AudioSurfaceKind kind;
    static List<Renderer> carpets;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOverlays() => carpets = null;

    public static void RefreshOverlays()
    {
        carpets = new List<Renderer>();
        foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            // Living-room rugs intentionally have no collider; keep their physical geometry untouched.
            if (renderer.bounds.size.y > 0.15f) continue;
            bool carpet = TryText(renderer.name, out var named) && named == AudioSurfaceKind.Carpet;
            foreach (var material in renderer.sharedMaterials)
                if (material != null && TryText(material.name, out var surface) && surface == AudioSurfaceKind.Carpet) carpet = true;
            if (carpet) carpets.Add(renderer);
        }
    }

    public static AudioSurfaceKind AtFeet(Component actor)
    {
        Vector3 feet = actor.transform.position;
        var controller = actor.GetComponent<CharacterController>();
        if (controller != null) feet.y = controller.bounds.min.y;
        // Water visuals are often triggers / have no collider. Match the actual current zones too.
        if (WaterCurrent.IsWater(feet)) return AudioSurfaceKind.Water;
        if (carpets == null) RefreshOverlays();
        foreach (var carpet in carpets)
        {
            if (carpet == null || !carpet.enabled || !carpet.gameObject.activeInHierarchy) continue;
            var bounds = carpet.bounds;
            if (feet.x >= bounds.min.x && feet.x <= bounds.max.x && feet.z >= bounds.min.z && feet.z <= bounds.max.z
                && feet.y >= bounds.min.y - 0.15f && feet.y <= bounds.max.y + 0.3f) return AudioSurfaceKind.Carpet;
        }
        var hits = Physics.RaycastAll(feet + Vector3.up * 0.45f, Vector3.down, 1.3f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        AudioSurfaceKind result = AudioSurfaceKind.Stone;
        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(actor.transform) || hit.collider.GetComponentInParent<PlayerHero>() != null) continue;
            if (hit.distance >= nearest) continue;
            nearest = hit.distance;
            result = Of(hit.collider, hit.point);
        }
        return result;
    }

    public static AudioSurfaceKind Of(Collider collider, Vector3 point)
    {
        if (collider == null) return AudioSurfaceKind.Stone;
        if (collider.GetComponentInParent<Health>() != null || collider.GetComponentInParent<PlayerHero>() != null)
            return AudioSurfaceKind.Body;
        var explicitSurface = collider.GetComponentInParent<AudioSurface>();
        if (explicitSurface != null) return explicitSurface.kind;
        // Domasov's lawn is a grid of generically named Tile_* meshes, not ceramic tiles.
        if ((collider.name.StartsWith("Tile_") || collider.name == "Ground" || collider.name == "Travnik")
            && HasAncestor(collider.transform, "Map-Domasov")) return AudioSurfaceKind.Grass;
        var terrain = collider.GetComponent<Terrain>();
        if (terrain != null && terrain.terrainData != null)
        {
            var data = terrain.terrainData;
            Vector3 local = point - terrain.transform.position;
            int x = Mathf.Clamp((int)(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp((int)(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            float[,,] weights = data.GetAlphamaps(x, z, 1, 1);
            int layer = 0;
            for (int i = 1; i < weights.GetLength(2); i++) if (weights[0, 0, i] > weights[0, 0, layer]) layer = i;
            if (layer < data.terrainLayers.Length && data.terrainLayers[layer] != null)
                return FromText(data.terrainLayers[layer].name, AudioSurfaceKind.Grass);
            return AudioSurfaceKind.Grass;
        }
        // Prefer the local renderer and material over broad ancestor names (a cottage also has tile/carpet).
        var renderer = collider.GetComponent<Renderer>() ?? collider.GetComponentInParent<Renderer>();
        if (renderer != null)
            foreach (var material in renderer.sharedMaterials)
                if (material != null && TryText(material.name + " " + (material.mainTexture != null ? material.mainTexture.name : ""), out var surface))
                    return surface;
        for (var t = collider.transform; t != null; t = t.parent)
            if (TryText(t.name, out var surface)) return surface;
        return AudioSurfaceKind.Stone;
    }

    static AudioSurfaceKind FromText(string text, AudioSurfaceKind fallback) => TryText(text, out var result) ? result : fallback;

    static bool HasAncestor(Transform child, string name)
    {
        for (var t = child; t != null; t = t.parent) if (t.name == name) return true;
        return false;
    }

    static bool TryText(string text, out AudioSurfaceKind surface)
    {
        var plain = new StringBuilder();
        foreach (char c in (text ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(char.ToLowerInvariant(c));
        string s = plain.ToString();
        bool Has(params string[] words) { foreach (var word in words) if (s.Contains(word)) return true; return false; }
        surface = AudioSurfaceKind.Stone;
        if (Has("seno", "hay", "straw", "slama")) surface = AudioSurfaceKind.Hay;
        else if (Has("carpet", "rug", "koberec", "rohoz", "hide", "kuze")) surface = AudioSurfaceKind.Carpet;
        else if (Has("rubber", "guma", "pneum")) surface = AudioSurfaceKind.Rubber;
        else if (Has("water", "voda", "potok", "jezirko")) surface = AudioSurfaceKind.Water;
        else if (Has("glass", "sklo", "sklen")) surface = AudioSurfaceKind.Glass;
        else if (Has("gravel", "sterk", "pebble", "oblaz")) surface = AudioSurfaceKind.Gravel;
        else if (Has("sand", "pisek", "piscit")) surface = AudioSurfaceKind.Sand;
        else if (Has("grass", "trava", "travnik", "lawn", "meadow", "foliage", "leaves", "listi", "hedge", "thuja", "conifer")) surface = AudioSurfaceKind.Grass;
        else if (Has("dirt", "soil", "hlina", "mud", "zemina", "sawdust", "piliny")) surface = AudioSurfaceKind.Dirt;
        else if (Has("tile", "dlaz", "ceram", "keram", "terracotta")) surface = AudioSurfaceKind.Tile;
        else if (Has("beam", "tram", "crate", "bedna", "furniture", "nabytek")) surface = AudioSurfaceKind.SolidWood;
        else if (Has("wood", "drevo", "dreven", "plank", "board", "birch", "barrel", "shoji", "prkn", "palub", "timber", "sindel", "shingle", "bark", "kura")) surface = AudioSurfaceKind.Wood;
        else if (Has("metal", "iron", "steel", "kov", "plech", "zelezo", "ocel", "chain", "mosaz", "brass", "alumin", "radlice", "powdercoat")) surface = AudioSurfaceKind.Metal;
        else if (Has("cloth", "fabric", "textil", "polstar", "soft")) surface = AudioSurfaceKind.Soft;
        else if (!Has("stone", "concrete", "brick", "rock", "beton", "kamen", "cihla", "asphalt", "slate", "plaster", "omitka")) return false;
        return true;
    }

    public static string Steps(AudioSurfaceKind surface)
    {
        switch (surface)
        {
            case AudioSurfaceKind.Wood: return "steps_wood";
            case AudioSurfaceKind.SolidWood: return "steps_solid_wood";
            case AudioSurfaceKind.Tile: return "steps_tile";
            case AudioSurfaceKind.Gravel: return "steps_gravel";
            case AudioSurfaceKind.Grass: return "steps_grass";
            case AudioSurfaceKind.Hay: return CollectedAudioBank.Has("steps_hay.step") ? "steps_hay" : "steps_grass";
            case AudioSurfaceKind.Dirt: return "steps_dirt";
            case AudioSurfaceKind.Sand: return "steps_sand";
            case AudioSurfaceKind.Carpet: case AudioSurfaceKind.Soft: return "steps_carpet";
            case AudioSurfaceKind.Rubber: return "steps_rubber";
            case AudioSurfaceKind.Metal: return "steps_metal";
            case AudioSurfaceKind.Water: return "steps_water";
            default: return "steps_stone";
        }
    }

    public static string Impact(AudioSurfaceKind surface, bool slash)
    {
        if (surface == AudioSurfaceKind.Body) return "state_body_hit";
        if (slash)
        {
            if (surface == AudioSurfaceKind.Wood || surface == AudioSurfaceKind.SolidWood) return "combat_slash_wood";
            if (surface == AudioSurfaceKind.Metal) return "combat_slash_metal";
            if (surface == AudioSurfaceKind.Carpet || surface == AudioSurfaceKind.Soft || surface == AudioSurfaceKind.Rubber || surface == AudioSurfaceKind.Hay) return "combat_slash_soft";
            return "combat_slash_stone";
        }
        switch (surface)
        {
            case AudioSurfaceKind.Wood: case AudioSurfaceKind.SolidWood: return "combat_bullet_wood";
            case AudioSurfaceKind.Metal: return "combat_bullet_metal";
            case AudioSurfaceKind.Glass: return "combat_bullet_glass";
            case AudioSurfaceKind.Grass: return "combat_bullet_leaves";
            case AudioSurfaceKind.Dirt: case AudioSurfaceKind.Sand: case AudioSurfaceKind.Gravel: return "combat_bullet_ground";
            case AudioSurfaceKind.Carpet: case AudioSurfaceKind.Soft: case AudioSurfaceKind.Rubber: case AudioSurfaceKind.Hay: return "combat_bullet_soft";
            case AudioSurfaceKind.Water: return "combat_bullet_water";
            default: return "combat_bullet_stone";
        }
    }
}
