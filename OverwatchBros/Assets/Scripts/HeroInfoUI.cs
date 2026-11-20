using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Karta hrdiny: zbran, schopnosti (klavesa, ikona, cooldown / ultimatka, popis) a pasivni vlastnosti.
// V lobby se otevre kliknutim na hrdinu, ve hre klavesou F2 (vlastni hrdina). Sipkami jde listovat dalsi hrdiny.
// Zavrit: F2, Esc nebo tlacitko. Popisy jsou v assetech (HeroDefinition / WeaponDefinition / AbilityDefinition.description).
public class HeroInfoUI
{
    const int MaxRows = 8;
    const float Width = 1180f, RowHeight = 92f, RowGap = 8f, RowsTop = -215f;

    GameObject panel;
    TextMeshProUGUI title, subtitle, passives, footer;
    Image titleLine;
    readonly Row[] rows = new Row[MaxRows];
    int heroIndex;

    class Row
    {
        public GameObject root;
        public Image keyBack;
        public TextMeshProUGUI key, name, stats, description;
        public RawImage icon;
    }

    public bool IsOpen => panel != null && panel.activeSelf;

    public void Build(Transform canvas)
    {
        var center = new Vector2(0.5f, 0.5f);
        var top = new Vector2(0.5f, 1f);

        var background = UiKit.MakeImage(canvas, "HeroInfo", new Color(0.03f, 0.04f, 0.07f, 0.95f), center, Vector2.zero, new Vector2(Width, 820f));
        background.raycastTarget = true;
        panel = background.gameObject;

        titleLine = UiKit.MakeImage(panel.transform, "Linka", UiKit.Accent, top, Vector2.zero, new Vector2(Width, 6f));
        title = UiKit.MakeText(panel.transform, "Jmeno", "", 60, TextAlignmentOptions.Center, top, new Vector2(0f, -20f), new Vector2(760f, 74f));
        title.fontStyle = FontStyles.Bold | FontStyles.Italic;
        subtitle = UiKit.MakeText(panel.transform, "Popis", "", 24, TextAlignmentOptions.Center, top, new Vector2(0f, -96f), new Vector2(Width - 80f, 60f),
            new Color(0.88f, 0.90f, 0.95f));
        subtitle.textWrappingMode = TextWrappingModes.Normal;
        passives = UiKit.MakeText(panel.transform, "Pasivni", "", 21, TextAlignmentOptions.Center, top, new Vector2(0f, -160f), new Vector2(Width - 80f, 46f), UiKit.Muted);
        passives.textWrappingMode = TextWrappingModes.Normal;

        UiKit.MakeButton(panel.transform, "<", top, new Vector2(-470f, -26f), new Vector2(80f, 64f), () => Step(-1), UiKit.ButtonBase, 34f);
        UiKit.MakeButton(panel.transform, ">", top, new Vector2(470f, -26f), new Vector2(80f, 64f), () => Step(1), UiKit.ButtonBase, 34f);

        for (int i = 0; i < MaxRows; i++)
        {
            var row = new Row();
            var back = UiKit.MakeImage(panel.transform, "Radek", new Color(1f, 1f, 1f, 0.045f), top,
                new Vector2(0f, RowsTop - i * (RowHeight + RowGap)), new Vector2(Width - 60f, RowHeight));
            row.root = back.gameObject;
            var left = new Vector2(0f, 0.5f);

            row.keyBack = UiKit.MakeImage(back.transform, "Klavesa", UiKit.ButtonBase, left, new Vector2(14f, 0f), new Vector2(96f, 52f));
            row.key = UiKit.MakeText(row.keyBack.transform, "Text", "", 20, TextAlignmentOptions.Center, center, Vector2.zero, new Vector2(96f, 52f));
            row.key.fontStyle = FontStyles.Bold;

            var iconGo = new GameObject("Ikona", typeof(RectTransform), typeof(RawImage));
            iconGo.transform.SetParent(back.transform, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = left;
            iconRect.anchoredPosition = new Vector2(124f, 0f);
            iconRect.sizeDelta = new Vector2(60f, 60f);
            row.icon = iconGo.GetComponent<RawImage>();
            row.icon.raycastTarget = false;

            var topLeft = new Vector2(0f, 1f);
            row.name = UiKit.MakeText(back.transform, "Nazev", "", 26, TextAlignmentOptions.Left, topLeft, new Vector2(200f, -8f), new Vector2(400f, 32f));
            row.name.fontStyle = FontStyles.Bold;
            row.stats = UiKit.MakeText(back.transform, "Hodnoty", "", 19, TextAlignmentOptions.Right, new Vector2(1f, 1f), new Vector2(-16f, -10f), new Vector2(520f, 28f), UiKit.Muted);
            row.description = UiKit.MakeText(back.transform, "Popis", "", 19, TextAlignmentOptions.TopLeft, topLeft, new Vector2(200f, -40f), new Vector2(Width - 290f, 50f),
                new Color(0.86f, 0.88f, 0.93f));
            row.description.textWrappingMode = TextWrappingModes.Normal;
            row.description.overflowMode = TextOverflowModes.Ellipsis;
            rows[i] = row;
        }

        footer = UiKit.MakeText(panel.transform, "Ovladani", "", 22, TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(Width, 30f), UiKit.Muted);
        UiKit.MakeButton(panel.transform, "ZAVŘÍT", new Vector2(1f, 0f), new Vector2(-24f, 16f), new Vector2(180f, 52f), Close, new Color(0.45f, 0.18f, 0.18f, 1f), 24f);

        panel.SetActive(false);
    }

    public void Open(int hero)
    {
        if (panel == null || HeroRegistry.All.Length == 0) return;
        heroIndex = Mathf.Clamp(hero, 0, HeroRegistry.All.Length - 1);
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
        Fill();
    }

    public void Close()
    {
        if (panel != null) panel.SetActive(false);
    }

    // allowed = karta smi byt otevrena (lobby nebo hra); currentHero = co otevre F2.
    public void Tick(bool allowed, int currentHero)
    {
        if (panel == null) return;
        if (!allowed)
        {
            if (panel.activeSelf) panel.SetActive(false);
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.f2Key.wasPressedThisFrame)
        {
            if (panel.activeSelf) Close();
            else Open(currentHero);
        }
        else if (panel.activeSelf && keyboard.escapeKey.wasPressedThisFrame)
            Close();
        else if (panel.activeSelf && (keyboard.leftArrowKey.wasPressedThisFrame))
            Step(-1);
        else if (panel.activeSelf && (keyboard.rightArrowKey.wasPressedThisFrame))
            Step(1);
    }

    void Step(int delta)
    {
        int count = HeroRegistry.All.Length;
        if (count == 0) return;
        heroIndex = (heroIndex + delta + count) % count;
        Fill();
    }

    // ---------------- obsah ----------------

    internal struct Entry
    {
        public string key, name, stats, description;
        public Texture icon;
        public bool ult;
    }

    void Fill()
    {
        var hero = HeroRegistry.Get(heroIndex);
        if (hero == null) return;

        title.text = hero.heroName.ToUpperInvariant();
        title.color = hero.color.maxColorComponent > 0.2f ? Color.Lerp(hero.color, Color.white, 0.25f) : Color.white;
        titleLine.color = hero.color.a > 0f ? hero.color : UiKit.Accent;
        subtitle.text = hero.description ?? "";
        passives.text = Passives(hero);

        var entries = Entries(hero);
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            bool used = i < entries.Count;
            row.root.SetActive(used);
            if (!used) continue;
            var e = entries[i];
            row.key.text = e.key;
            row.keyBack.color = e.ult ? UiKit.Accent : UiKit.ButtonBase;
            row.name.text = e.name;
            row.name.color = e.ult ? new Color(1f, 0.82f, 0.4f) : Color.white;
            row.stats.text = e.stats;
            row.description.text = string.IsNullOrEmpty(e.description) ? "<i>(bez popisu)</i>" : e.description;
            row.icon.texture = e.icon;
            row.icon.enabled = e.icon != null;
        }

        footer.text = $"<color=#F28C1A>šipky</color> další hrdinové ({heroIndex + 1}/{HeroRegistry.All.Length})   ·   zavřít: <color=#F28C1A>F2</color> nebo <color=#F28C1A>Esc</color>";
    }

    internal static List<Entry> Entries(HeroDefinition hero)
    {
        var list = new List<Entry>();
        var weapon = hero.weapon;
        if (weapon != null)
            list.Add(new Entry { key = "LEVÉ TL.", name = weapon.weaponName, stats = WeaponStats(weapon), description = weapon.description });

        if (hero.secondaryAbility != null)
            list.Add(AbilityEntry(hero.secondaryAbilityKind == AbilityKind.Mine ? "SHIFT\n<size=70%>+ PRAVÉ</size>" : "SHIFT", hero.secondaryAbility));
        if (hero.altAbility != null)
            list.Add(AbilityEntry("E", hero.altAbility));
        if (hero.blockAbility != null)
        {
            var e = AbilityEntry("PRAVÉ TL.", hero.blockAbility);
            e.stats = $"blokuje {hero.blockAbility.blockAbsorb * 100f:0} % poškození  ·  pohyb {hero.blockAbility.blockSpeed * 100f:0} %";
            list.Add(e);
        }
        if (hero.rmbAbility != null)
            list.Add(AbilityEntry("PRAVÉ TL.", hero.rmbAbility));
        else if (hero.blockAbility == null && weapon != null && weapon.scopeFov > 0f)
            list.Add(new Entry
            {
                key = "PRAVÉ TL.",
                name = weapon.HasScopedShot ? "Odstřel" : "Zaměřovač",
                stats = weapon.HasScopedShot
                    ? $"{weapon.scopedMinDamage:0}–{weapon.scopedMaxDamage:0} dmg  ·  nabití {weapon.scopedChargeTime:0.#} s{(weapon.boltCycleTime > 0f ? $"  ·  závěr {weapon.boltCycleTime:0.#} s" : "")}  ·  hlava ×{weapon.headshotMultiplier:0.#}"
                    : "přiblížení",
                description = weapon.HasScopedShot
                    ? "Drž pravé tlačítko: přiblížíš a rána se nabíjí do plné síly (ukazatel u zaměřovače). Plně nabitá rána do hlavy dá mnohem víc." + (weapon.boltCycleTime > 0f ? " Po každé ráně se přetahuje závěr." : "")
                    : "Drž pravé tlačítko pro přiblížení - přesnější míření na dálku.",
            });

        if (hero.ability != null)
        {
            var e = AbilityEntry("Q", hero.ability);
            e.ult = hero.ability.ultCost > 0f;
            list.Add(e);
        }
        return list;
    }

    static Entry AbilityEntry(string key, AbilityDefinition ability) => new Entry
    {
        key = key,
        name = ability.abilityName,
        icon = ability.icon,
        stats = AbilityStats(ability),
        description = ability.description,
    };

    static string AbilityStats(AbilityDefinition ability)
    {
        var parts = new List<string>();
        if (ability.ultCost > 0f) parts.Add($"<color=#F2C14E>ULTIMÁTKA</color>  ·  nabití {ability.ultCost:0}");
        else if (ability.cooldown > 0f) parts.Add($"cooldown {ability.cooldown:0.#} s");
        if (ability.charges > 1) parts.Add($"{ability.charges} nabití");
        return string.Join("  ·  ", parts);
    }

    static string WeaponStats(WeaponDefinition weapon)
    {
        var parts = new List<string>();
        string damage = weapon.burstCount > 1 ? $"{weapon.burstCount}× {weapon.damage:0} dmg" : $"{weapon.damage:0} dmg";
        if (weapon.allyHeal > 0f) damage += $", léčí +{weapon.allyHeal:0}";
        parts.Add(damage);
        if (weapon.IsMelee)
        {
            parts.Add($"{weapon.fireRate:0.#} úderů/s");
            parts.Add($"dosah {weapon.range:0.#} m");
            if (weapon.comboFinisherDamage > 0f) parts.Add($"3. úder {weapon.comboFinisherDamage:0}");
        }
        else
        {
            parts.Add($"{weapon.fireRate:0.#}/s");
            if (weapon.explosionRadius > 0f) parts.Add($"výbuch {weapon.explosionRadius:0.#} m");
            if (weapon.IsCharged) parts.Add($"natažení {weapon.chargeTime:0.#} s");
            if (weapon.HasAmmo) parts.Add($"zásobník {weapon.maxAmmo}");
            parts.Add(weapon.IsProjectile ? "projektil" : "okamžitý zásah");
        }
        return string.Join("  ·  ", parts);
    }

    internal static string Passives(HeroDefinition hero)
    {
        var parts = new List<string> { $"<color=#FFFFFF>{hero.maxHealth:0} HP</color>" };
        if (Mathf.Abs(hero.moveSpeed - 1f) > 0.01f) parts.Add($"rychlost {(hero.moveSpeed - 1f) * 100f:+0;-0} %");
        if (hero.lifesteal > 0f) parts.Add($"léčí se z {hero.lifesteal * 100f:0} % poškození");
        if (hero.takedownCooldownCut > 0f) parts.Add($"zabití zkrátí cooldowny o {hero.takedownCooldownCut * 100f:0} %");
        if (hero.doubleJump) parts.Add("dvojitý skok");
        if (hero.ledgeClimb) parts.Add("vyšplhá na hrany");
        if (hero.deathGrenades > 0) parts.Add($"po smrti {hero.deathGrenades} granáty");
        if (hero.healingScale < 0.999f) parts.Add($"léčení {hero.healingScale * 100f:0} %");
        return "PASIVNĚ:  " + string.Join("  ·  ", parts);
    }
}
