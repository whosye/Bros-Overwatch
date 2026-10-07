using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Ikony aktivnich buffu dole uprostred obrazovky. Kazdy buff: ikona, zbyvajici sekundy, posledni 3 s blika.
// Vlastni obrazek: Assets/Resources/Icons/buff_<jmeno>.png (barevny, zobrazi se tak, jak je);
// dokud chybi, kresli se zlaty zastupny symbol se zkratkou.
public class BuffUI
{
    class Buff
    {
        public string key, shortName;
        public Color color;
        public System.Func<float> until;   // cas konce buffu (Time.time)
        public GameObject root;
        public RawImage icon;
        public Image frame;
        public TextMeshProUGUI label, seconds;
    }

    const float Size = 64f, Gap = 12f;

    Transform panel;
    Buff[] buffs;

    public void Build(Transform canvas)
    {
        var holder = new GameObject("Buffs", typeof(RectTransform));
        holder.transform.SetParent(canvas, false);
        var rect = (RectTransform)holder.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 30f);
        rect.sizeDelta = new Vector2(600f, Size + 30f);
        panel = holder.transform;

        buffs = new[]
        {
            new Buff { key = "respin_joker", shortName = "RJ", color = new Color(1f, 0.82f, 0.2f),
                until = () => AbilityDefinition.FastCooldownUntil },
            new Buff { key = "dedova_slivovice", shortName = "DS", color = new Color(1f, 0.72f, 0.25f),
                until = () => PickupBuffs.InvulnerableUntil },
        };
        foreach (var b in buffs) Create(b);
    }

    void Create(Buff b)
    {
        b.root = new GameObject("Buff_" + b.key, typeof(RectTransform));
        b.root.transform.SetParent(panel, false);
        var rect = (RectTransform)b.root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(Size, Size + 26f);

        b.frame = UiKit.MakeImage(b.root.transform, "Ramecek", new Color(0.05f, 0.07f, 0.10f, 0.8f), new Vector2(0.5f, 0f),
            new Vector2(0f, 26f + Size * 0.5f), new Vector2(Size, Size));
        var outline = b.frame.gameObject.AddComponent<Outline>();
        outline.effectColor = b.color;
        outline.effectDistance = new Vector2(2f, -2f);

        var texture = Resources.Load<Texture2D>("Icons/buff_" + b.key);
        if (texture != null)
        {
            var iconObject = new GameObject("Ikona", typeof(RectTransform), typeof(RawImage));
            iconObject.transform.SetParent(b.frame.transform, false);
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(Size - 12f, Size - 12f);
            b.icon = iconObject.GetComponent<RawImage>();
            b.icon.texture = texture;
            b.icon.color = Color.white;   // barevny obrazek se nebarvi (ramecek ma barvu buffu)
            b.icon.raycastTarget = false;
        }
        else
        {
            // zastupny symbol: zlaty kosoctverec se zkratkou
            var diamond = UiKit.MakeImage(b.frame.transform, "Zastupna", b.color, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f));
            diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            b.label = UiKit.MakeText(b.frame.transform, "Zkratka", b.shortName, 18, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(Size, 24f), new Color(0.1f, 0.08f, 0.02f));
        }

        b.seconds = UiKit.MakeText(b.root.transform, "Sekundy", "", 20, TextAlignmentOptions.Center, new Vector2(0.5f, 0f),
            new Vector2(0f, 11f), new Vector2(Size + 20f, 24f), b.color);
        b.root.SetActive(false);
    }

    public void Tick(bool playing)
    {
        if (buffs == null) return;
        int shown = 0;
        foreach (var b in buffs)
        {
            float left = b.until() - Time.time;
            bool on = playing && left > 0f;
            if (b.root.activeSelf != on) b.root.SetActive(on);
            if (!on) continue;
            shown++;
            b.seconds.text = $"{Mathf.CeilToInt(left)} s";
            // posledni 3 s blika
            float alpha = left < 3f ? 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.time * 6f)) : 1f;
            b.frame.color = new Color(0.05f, 0.07f, 0.10f, 0.8f * alpha);
            b.seconds.alpha = alpha;
        }

        // aktivni buffy vedle sebe, vycentrovane
        float x = -(shown - 1) * (Size + Gap) * 0.5f;
        foreach (var b in buffs)
        {
            if (!b.root.activeSelf) continue;
            ((RectTransform)b.root.transform).anchoredPosition = new Vector2(x, 0f);
            x += Size + Gap;
        }
    }
}
