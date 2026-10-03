using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Zmena hrdiny behem zapasu: F1 otevre maly vyber, kliknutim na hrdinu se hrac prevteli. Jde to jen na zakladne
// vlastniho tymu (barevny kruh na zemi kolem mista oziveni) nebo kdyz je hrac mrtvy a ceka na oziveni.
// Zabiti a smrti mu zustavaji, nabiti ultimatky se vynuluje. Zavrit jde znovu F1 nebo Esc.
public class HeroPickerUI
{
    GameObject panel;
    TextMeshProUGUI hint;
    TextMeshProUGUI zoneLabel;
    float deniedUntil;
    readonly List<Button> buttons = new List<Button>();

    public bool IsOpen => panel != null && panel.activeSelf;

    public void Build(Transform canvas)
    {
        var center = new Vector2(0.5f, 0.5f);

        var background = UiKit.MakeImage(canvas, "HeroPicker", new Color(0.03f, 0.04f, 0.07f, 0.92f), center, Vector2.zero, new Vector2(Mathf.Max(1240f, HeroRegistry.All.Length * 330f + 120f), 420f));
        background.raycastTarget = true;
        panel = background.gameObject;

        UiKit.MakeText(panel.transform, "Title", "ZMĚNA HRDINY", 52, TextAlignmentOptions.Center, center,
            new Vector2(0f, 150f), new Vector2(1100f, 70f), UiKit.Accent);

        var heroes = HeroRegistry.All;
        float spacing = 330f;
        float startX = -(heroes.Length - 1) * spacing / 2f;
        for (int i = 0; i < heroes.Length; i++)
        {
            int index = i;
            string label = $"{heroes[i].heroName}\n<size=50%>{heroes[i].maxHealth:0} HP  ·  {(heroes[i].weapon != null ? heroes[i].weapon.weaponName : "")}</size>";
            var button = UiKit.MakeButton(panel.transform, label, center, new Vector2(startX + i * spacing, 20f), new Vector2(310f, 130f),
                () => Choose(index), UiKit.ButtonBase, 40f);
            buttons.Add(button);
        }

        hint = UiKit.MakeText(panel.transform, "Hint", "", 26, TextAlignmentOptions.Center, center,
            new Vector2(0f, -130f), new Vector2(1160f, 80f), UiKit.Muted);

        panel.SetActive(false);

        // Napoveda dole uprostred: na zakladne, ze jde menit hrdinu; jinde proc to nejde.
        zoneLabel = UiKit.MakeText(canvas, "SpawnZoneLabel", "", 28, TextAlignmentOptions.Center, new Vector2(0.5f, 0f),
            new Vector2(0f, 150f), new Vector2(1200f, 40f));
        zoneLabel.gameObject.SetActive(false);
    }

    PlayerHero local;

    static bool CanSwap(PlayerHero hero)
    {
        var health = hero.GetComponent<Health>();
        var team = hero.GetComponent<PlayerTeam>();
        if (health != null && health.currentHealth.Value <= 0f) return true;
        return team != null && SpawnZone.Contains(team.teamId.Value, hero.transform.position);
    }

    // allowed = hrac je v bezicim zapase (ne v lobby, ne na konci, ne pri vyberu po pripojeni).
    public void Tick(PlayerHero localHero, bool allowed)
    {
        if (panel == null) return;

        local = localHero;

        if (!allowed || localHero == null)
        {
            if (panel.activeSelf)
                panel.SetActive(false);
            if (zoneLabel.gameObject.activeSelf)
                zoneLabel.gameObject.SetActive(false);
            return;
        }

        bool canSwap = CanSwap(localHero);

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.f1Key.wasPressedThisFrame)
            {
                if (panel.activeSelf) panel.SetActive(false);
                else if (canSwap) panel.SetActive(true);
                else deniedUntil = Time.unscaledTime + 2.5f;
            }
            else if (panel.activeSelf && keyboard.escapeKey.wasPressedThisFrame)
            {
                panel.SetActive(false);
            }
        }

        // Kdyz hrac ze zakladny odejde s otevrenym vyberem, vyber se zavre.
        if (panel.activeSelf && !canSwap)
            panel.SetActive(false);

        bool denied = Time.unscaledTime < deniedUntil;
        bool showLabel = !panel.activeSelf && (denied || canSwap);
        if (zoneLabel.gameObject.activeSelf != showLabel)
            zoneLabel.gameObject.SetActive(showLabel);
        if (showLabel)
        {
            zoneLabel.text = denied && !canSwap
                ? "Hrdinu jde měnit jen na základně (barevný kruh u místa oživení)"
                : "ZÁKLADNA  ·  <color=#F28C1A>F1</color> změna hrdiny";
            zoneLabel.color = denied && !canSwap ? new Color(1f, 0.45f, 0.4f) : UiKit.Muted;
        }

        if (!panel.activeSelf) return;

        int current = localHero.heroId.Value;
        for (int i = 0; i < buttons.Count; i++)
        {
            UiKit.SetButtonColor(buttons[i], i == current ? UiKit.Accent : UiKit.ButtonBase);
            buttons[i].interactable = i != current;
        }

        hint.text = "Zabití a smrti ti zůstávají, nabití ultimátky se vynuluje.\n"
            + "Zavřít: F1 nebo Esc";
    }

    void Choose(int index)
    {
        if (local != null)
        {
            PlayerHero.PreferredHero = index;
            PlayerPrefs.SetInt("preferredHero", index);
            local.SwapHero(index);
        }

        panel.SetActive(false);
    }
}
