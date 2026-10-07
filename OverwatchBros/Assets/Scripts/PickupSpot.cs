using System.Collections.Generic;
using UnityEngine;

public enum PickupKind
{
    Health,       // lekarnicka: +75 HP (jen zraneny)
    BigHealth,    // velka lekarnicka: plne zdravi (jen zraneny)
    Speed,        // pivo: rychlost
    Power,        // slivovice: posileni (jako Annina ultimatka, kratce)
    Shield,       // stit: docasny stit nad zdravim
    Invulnerable, // dedova slivovice (spiz velke chaty): nesmrtelnost
}

// Buffy z balicku, ktere zobrazuje HUD mistniho hrace (BuffUI). Nastavuje je MatchManager, kdyz balicek sebere
// mistni hrac (ucinek sam hlida server).
public static class PickupBuffs
{
    public const float InvulnerableSeconds = 15f;
    public static float InvulnerableUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => InvulnerableUntil = 0f;
}

// Misto s balickem na mape (znacka ve scene, rozmisti ji editorovy skript). Vizual si postavi samo za behu;
// jestli je balicek k dispozici, rika MatchManager (bitova maska sdilena se vsemi hraci).
public class PickupSpot : MonoBehaviour
{
    public PickupKind kind = PickupKind.Health;

    public static readonly List<PickupSpot> All = new List<PickupSpot>();
    static bool sorted;

    // Poradi podle jmena - stejne u vsech hracu (scena je u vsech stejna).
    public static List<PickupSpot> Sorted
    {
        get
        {
            if (!sorted)
            {
                All.RemoveAll(s => s == null);
                All.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                sorted = true;
            }
            return All;
        }
    }

    public int Index => Sorted.IndexOf(this);

    public float RespawnSeconds => kind switch
    {
        PickupKind.Health => 12f,
        PickupKind.BigHealth => 20f,
        PickupKind.Speed => 30f,
        PickupKind.Power => 45f,
        PickupKind.Invulnerable => 60f,
        _ => 30f,
    };

    public Color Color => KindColor(kind);

    public static Color KindColor(PickupKind kind) => kind switch
    {
        PickupKind.Health => new Color(0.95f, 0.25f, 0.25f),
        PickupKind.BigHealth => new Color(1f, 0.2f, 0.2f),
        PickupKind.Speed => new Color(1f, 0.75f, 0.2f),
        PickupKind.Power => new Color(0.75f, 0.35f, 1f),
        PickupKind.Invulnerable => new Color(1f, 0.72f, 0.25f),
        _ => new Color(0.45f, 0.85f, 1f),
    };

    public static string KindName(PickupKind kind) => kind switch
    {
        PickupKind.Health => "Lékárnička",
        PickupKind.BigHealth => "Velká lékárnička",
        PickupKind.Speed => "Pivo",
        PickupKind.Power => "Slivovice",
        PickupKind.Invulnerable => "Dědova slivovice",
        _ => "Štít",
    };

    Transform item;
    Light glow;
    Renderer[] itemRenderers;
    bool shown = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        All.Clear();
        sorted = false;
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        sorted = false;
    }

    void OnDisable()
    {
        All.Remove(this);
        sorted = false;
    }

    void Start()
    {
        BuildVisual();
    }

    void Update()
    {
        var match = MatchManager.Instance;
        bool available = match == null || match.PickupAvailable(Index);
        if (available != shown)
        {
            shown = available;
            foreach (var r in itemRenderers) if (r != null) r.enabled = available;
            if (glow != null) glow.enabled = available;
        }
        if (item != null && available)
        {
            item.localPosition = new Vector3(0f, 0.75f + Mathf.Sin(Time.time * 2f + Index) * 0.08f, 0f);
            item.localRotation = Quaternion.Euler(0f, Time.time * 70f + Index * 40f, 0f);
        }
    }

    // Znacka v editoru (vizual se stavi az ve hre).
    void OnDrawGizmos()
    {
        Gizmos.color = Color;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.75f, 0.4f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.02f, new Vector3(1.2f, 0.04f, 1.2f));
    }

    // ---------------- vizual ----------------

    void BuildVisual()
    {
        var color = Color;

        // Podstavec: tmavy kotouc se svitivym okrajem (vidi se i kdyz je balicek sebrany - misto se pozna).
        var pad = Part(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.03f, 0f), new Vector3(1.1f, 0.03f, 1.1f), new Color(0.12f, 0.13f, 0.15f));
        var ring = Part(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.035f, 0f), new Vector3(1.25f, 0.02f, 1.25f), color, 1.5f);
        ring.transform.SetSiblingIndex(0);

        item = new GameObject("Predmet").transform;
        item.SetParent(transform, false);
        float big = kind == PickupKind.BigHealth ? 1.35f : 1f;

        switch (kind)
        {
            case PickupKind.Health:
            case PickupKind.BigHealth:
                // Bila krabicka s cervenym krizem na obou stranach.
                Part(PrimitiveType.Cube, item, Vector3.zero, new Vector3(0.5f, 0.34f, 0.32f) * big, Color.white);
                foreach (float side in new[] { -1f, 1f })
                {
                    Part(PrimitiveType.Cube, item, new Vector3(0f, 0f, 0.165f * side * big), new Vector3(0.26f, 0.08f, 0.01f) * big, color, 1.2f);
                    Part(PrimitiveType.Cube, item, new Vector3(0f, 0f, 0.165f * side * big), new Vector3(0.08f, 0.26f, 0.01f) * big, color, 1.2f);
                }
                Part(PrimitiveType.Cube, item, new Vector3(0f, 0.2f * big, 0f), new Vector3(0.22f, 0.05f, 0.06f) * big, new Color(0.2f, 0.2f, 0.22f));
                break;
            case PickupKind.Speed:
                // Pullitr piva s penou a uchem.
                Part(PrimitiveType.Cylinder, item, Vector3.zero, new Vector3(0.26f, 0.2f, 0.26f), new Color(0.95f, 0.62f, 0.12f), 0.6f);
                Part(PrimitiveType.Cylinder, item, new Vector3(0f, 0.21f, 0f), new Vector3(0.28f, 0.04f, 0.28f), new Color(0.98f, 0.97f, 0.92f));
                Part(PrimitiveType.Cube, item, new Vector3(0.18f, 0.02f, 0f), new Vector3(0.05f, 0.24f, 0.05f), new Color(0.85f, 0.9f, 0.9f));
                break;
            case PickupKind.Power:
                // Lahev slivovice: telo, hrdlo, zatka a etiketa.
                Part(PrimitiveType.Cylinder, item, new Vector3(0f, -0.05f, 0f), new Vector3(0.2f, 0.2f, 0.2f), new Color(0.55f, 0.25f, 0.75f), 0.8f);
                Part(PrimitiveType.Cylinder, item, new Vector3(0f, 0.22f, 0f), new Vector3(0.08f, 0.1f, 0.08f), new Color(0.55f, 0.25f, 0.75f), 0.8f);
                Part(PrimitiveType.Cylinder, item, new Vector3(0f, 0.34f, 0f), new Vector3(0.06f, 0.03f, 0.06f), new Color(0.35f, 0.2f, 0.1f));
                Part(PrimitiveType.Cylinder, item, new Vector3(0f, -0.04f, 0f), new Vector3(0.205f, 0.08f, 0.205f), new Color(0.95f, 0.92f, 0.8f));
                break;
            case PickupKind.Invulnerable:
                // Dedova slivovice: vysoka lahev z cireho skla, zlatava palenka, cervena zatka, rucne psana etiketa,
                // vedle stamprlicka.
                Part(PrimitiveType.Cylinder, item, new Vector3(0f, -0.02f, 0f), new Vector3(0.18f, 0.24f, 0.18f), new Color(0.85f, 0.95f, 0.88f), 0.2f);
                Part(PrimitiveType.Cylinder, item, new Vector3(0f, -0.06f, 0f), new Vector3(0.185f, 0.18f, 0.185f), new Color(1f, 0.75f, 0.3f), 0.9f);
                Part(PrimitiveType.Cylinder, item, new Vector3(0f, 0.3f, 0f), new Vector3(0.07f, 0.1f, 0.07f), new Color(0.85f, 0.95f, 0.88f), 0.2f);
                Part(PrimitiveType.Cylinder, item, new Vector3(0f, 0.42f, 0f), new Vector3(0.075f, 0.03f, 0.075f), new Color(0.75f, 0.1f, 0.08f));
                Part(PrimitiveType.Cube, item, new Vector3(0f, -0.02f, 0.092f), new Vector3(0.12f, 0.12f, 0.01f), new Color(0.96f, 0.93f, 0.82f));
                Part(PrimitiveType.Cylinder, item, new Vector3(0.2f, -0.18f, 0f), new Vector3(0.07f, 0.05f, 0.07f), new Color(1f, 0.8f, 0.4f), 0.6f);
                break;
            default:
                // Modry sestiuhelnikovy stit.
                var shield = Part(PrimitiveType.Cylinder, item, Vector3.zero, new Vector3(0.5f, 0.04f, 0.5f), color, 1.6f);
                shield.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var boss = Part(PrimitiveType.Cylinder, item, new Vector3(0f, 0f, 0.03f), new Vector3(0.2f, 0.03f, 0.2f), Color.white, 1f);
                boss.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                break;
        }

        itemRenderers = item.GetComponentsInChildren<Renderer>();

        glow = new GameObject("Svetlo").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        glow.type = LightType.Point;
        glow.color = color;
        glow.range = 3.5f;
        glow.intensity = 1.6f;
        glow.shadows = LightShadows.None;
    }

    static GameObject Part(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color, float emission = 0f)
    {
        var go = GameObject.CreatePrimitive(type);
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        Fx.Paint(go, color);
        var renderer = go.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (emission > 0f)
        {
            var material = renderer.material;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
        }
        return go;
    }
}
