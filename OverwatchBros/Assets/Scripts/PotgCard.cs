using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Karta "PLAY OF THE GAME" ve stylu Overwatche (kresli ji PotgUI): nadpis s oranzovym sikmym pruhem, jmeno hrace
// v sikmem pruhu barvy tymu, vedle tmavy stitek s hrdinou a pod tim kategorie. Pri vstupu pruhy vyjedou zleva,
// nadpis doskoci ze zvetseni a karta se otrese, pres jmeno prolétne bily zablesk; pred strihem na zaznam karta
// odleti doleva.
public class PotgCard
{
    readonly RectTransform root;
    readonly CanvasGroup group;
    readonly SkewBar stripe, nameBar, heroTag, streak;
    readonly TextMeshProUGUI title, playerName, heroName, category;
    Vector2 home;
    float nameWidth, heroWidth, titleWidth;

    const float BarY = 56f, BarHeight = 84f, TagHeight = 62f;
    const float NamePad = 34f, HeroPad = 24f;

    public PotgCard(Transform parent)
    {
        var go = new GameObject("PotgCard", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        root = (RectTransform)go.transform;
        root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
        root.sizeDelta = new Vector2(10f, 10f);
        group = go.GetComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;

        stripe = SkewBar.Make(root, "PruhNadpis", UiKit.Accent, new Vector2(22f, 152f), new Vector2(700f, 10f), 6f);
        nameBar = SkewBar.Make(root, "PruhJmeno", Color.white, new Vector2(0f, BarY), new Vector2(600f, BarHeight), 26f);
        nameBar.gameObject.AddComponent<RectMask2D>();   // zablesk a jmeno jen v pruhu
        streak = SkewBar.Make(nameBar.transform, "Zablesk", new Color(1f, 1f, 1f, 0f), Vector2.zero, new Vector2(70f, BarHeight), 26f);
        heroTag = SkewBar.Make(root, "StitekHrdina", new Color(0.04f, 0.05f, 0.08f, 0.88f), new Vector2(620f, BarY + (BarHeight - TagHeight) * 0.5f),
            new Vector2(260f, TagHeight), 20f);

        title = Text(root, "Nadpis", "PLAY OF THE GAME", 84f, new Vector2(16f, 212f), new Vector2(1400f, 100f), Color.white, true);
        playerName = Text(nameBar.transform, "Jmeno", "", 56f, new Vector2(NamePad, BarHeight * 0.5f), new Vector2(1200f, BarHeight), Color.white, true);
        heroName = Text(heroTag.transform, "Hrdina", "", 40f, new Vector2(HeroPad, TagHeight * 0.5f), new Vector2(800f, TagHeight), Color.white, true);
        category = Text(root, "Kategorie", "", 34f, new Vector2(28f, 26f), new Vector2(1400f, 46f), UiKit.Muted, false);
        Tick(-1f, 0f);
    }

    static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Vector2 position, Vector2 box, Color color, bool bold)
    {
        var label = UiKit.MakeText(parent, name, text, size, TextAlignmentOptions.Left, Vector2.zero, position, box, color);
        var rect = label.rectTransform;
        rect.pivot = new Vector2(0f, 0.5f);   // zvetseni pri doskoku od leveho okraje
        rect.anchoredPosition = position;
        label.fontStyle = bold ? FontStyles.Bold | FontStyles.Italic : FontStyles.Italic;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        return label;
    }

    public void Set(string player, string hero, string categoryText, Color team)
    {
        playerName.text = (player ?? "").ToUpperInvariant();
        heroName.text = (hero ?? "").ToUpperInvariant();
        heroName.color = team;
        category.text = categoryText ?? "";
        nameBar.color = new Color(team.r * 0.78f, team.g * 0.78f, team.b * 0.78f, 0.96f);

        nameWidth = playerName.GetPreferredValues(playerName.text).x + NamePad + 44f;
        nameBar.rectTransform.sizeDelta = new Vector2(nameWidth, BarHeight);
        heroWidth = string.IsNullOrEmpty(hero) ? 0f : heroName.GetPreferredValues(heroName.text).x + HeroPad + 30f;
        heroTag.gameObject.SetActive(heroWidth > 0f);
        heroTag.rectTransform.anchoredPosition = new Vector2(nameWidth + 18f, BarY + (BarHeight - TagHeight) * 0.5f);
        heroTag.rectTransform.sizeDelta = new Vector2(heroWidth, TagHeight);
        titleWidth = title.GetPreferredValues(title.text).x;
        stripe.rectTransform.sizeDelta = new Vector2(titleWidth + 24f, 10f);
    }

    // Vlevo dole (scenka hrdiny) nebo uprostred obrazovky (cerna karta, hrdina bez scenky).
    public void Place(bool cinematic)
    {
        float total = Mathf.Max(titleWidth + 40f, nameWidth + (heroWidth > 0f ? heroWidth + 18f : 0f) + 30f);
        root.anchorMin = root.anchorMax = cinematic ? Vector2.zero : new Vector2(0.5f, 0.5f);
        home = cinematic ? new Vector2(110f, 64f) : new Vector2(-total * 0.5f, -150f);
        root.anchoredPosition = home;
    }

    public void SetVisible(bool visible) => root.gameObject.SetActive(visible);

    // k = s od vstupu karty (zaporne = jeste neni), away = 0..1 odlet pred strihem na zaznam.
    public void Tick(float k, float away)
    {
        if (k <= 0f)
        {
            group.alpha = 0f;
            stripe.SetFill(0f);
            nameBar.SetFill(0f);
            heroTag.SetFill(0f);
            return;
        }
        group.alpha = 1f - away * away;

        // nadpis doskoci ze zvetseni, oranzovy pruh pod nim vyjede
        title.alpha = Mathf.Clamp01(k / 0.07f);
        title.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(1.8f, 1f, EaseOutBack(Mathf.Clamp01(k / 0.24f)));
        stripe.SetFill(EaseOut((k - 0.04f) / 0.16f));

        // pruh se jmenem, pak stitek s hrdinou a kategorie
        nameBar.SetFill(EaseOut((k - 0.08f) / 0.2f));
        float nameIn = EaseOut((k - 0.16f) / 0.22f);
        playerName.alpha = Mathf.Clamp01((k - 0.16f) / 0.12f);
        playerName.rectTransform.anchoredPosition = new Vector2(NamePad + Mathf.Lerp(-36f, 0f, nameIn), BarHeight * 0.5f);
        heroTag.SetFill(EaseOut((k - 0.22f) / 0.16f));
        heroName.alpha = Mathf.Clamp01((k - 0.3f) / 0.12f);
        category.alpha = Mathf.Clamp01((k - 0.38f) / 0.2f);

        // bily zablesk pres jmeno
        float s = Mathf.Clamp01((k - 0.26f) / 0.34f);
        streak.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-110f, nameWidth + 20f, s), 0f);
        streak.color = new Color(1f, 1f, 1f, 0.7f * Mathf.Sin(s * Mathf.PI));

        // otres pri dopadu nadpisu, odlet doleva pred strihem
        float shake = k > 0.12f ? Mathf.Exp(-(k - 0.12f) * 16f) * 11f : 0f;
        Vector2 jitter = new Vector2(Mathf.Sin(k * 97f), Mathf.Sin(k * 83f + 1f)) * shake;
        root.anchoredPosition = home + jitter + new Vector2(-away * away * away * 1800f, 0f);
    }

    static float EaseOut(float x)
    {
        x = Mathf.Clamp01(x);
        return 1f - (1f - x) * (1f - x) * (1f - x);
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float m = x - 1f;
        return 1f + c3 * m * m * m + c1 * m * m;
    }
}
