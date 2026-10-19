using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD behem hry ve stylu Overwatch: zivoty vlevo dole, schopnosti s ikonami a cooldownem vpravo dole,
// munice nad nimi a cervene okraje obrazovky pri zasahu. Cele se stavi kodem (viz MatchUI).
public class HudUI
{
    const int MaxSlots = 4;
    const float HealthBarWidth = 380f;
    const float HealthPerSegment = 25f;

    class Slot
    {
        public GameObject root;
        public Image background;
        public RawImage icon;
        public Image cooldown;
        public Image readyBar;
        public TextMeshProUGUI timer;
        public TextMeshProUGUI key;
        public TextMeshProUGUI count;
    }

    GameObject root;
    RawImage vignette;
    TextMeshProUGUI healthText;
    TextMeshProUGUI nameText;
    TextMeshProUGUI ammoText;
    Image healthFill;
    Image shieldFill;
    RectTransform healthBar;
    readonly List<GameObject> separators = new List<GameObject>();
    float separatorsFor = -1f;
    readonly Slot[] slots = new Slot[MaxSlots];

    // Portret hrdiny vlevo vedle zivotu: Resources/Portraits/<hero asset>.png, jinak zastupny ctverec s inicialami.
    RawImage portrait;
    Image portraitFrame;
    TextMeshProUGUI portraitInitials;
    HeroDefinition portraitFor;

    // Ultimatka dole uprostred (kulata); nabita na 100 % - modra zare kolem.
    Slot ultSlot;
    Image ultGlow;
    static readonly Color UltGlowColor = new Color(0.3f, 0.6f, 1f, 1f);
    static Sprite circleSprite, glowSprite;
    readonly List<PlayerHero.AbilitySlot> abilities = new List<PlayerHero.AbilitySlot>();

    // Potvrzeni zasahu: krizek kolem zamerovace, pri zabiti cerveny a k nemu lebka.
    const float HitDuration = 0.28f;
    const float KillDuration = 1.1f;
    static float hitTime = -10f;
    static float killTime = -10f;
    readonly Image[] hitLines = new Image[4];
    RectTransform hitRoot;
    RawImage killIcon;

    // Oslepeni (omraceni granatem): bila obrazovka, ktera postupne mizi.
    static float flashStart = -10f;
    static float flashDuration = 1f;
    Image flashImage;

    static Color flashColor = Color.white;

    public static void NotifyFlash(float seconds)
    {
        ReplayLog.HudFlash(seconds);
        flashStart = Time.unscaledTime;
        flashDuration = Mathf.Max(0.2f, seconds) + 0.5f;
        flashColor = Color.white;
    }

    // Uspani: tmava obrazovka, ktera na konci rychle zmizi.
    public static void NotifySleep(float seconds)
    {
        ReplayLog.HudSleep(seconds);
        flashStart = Time.unscaledTime;
        flashDuration = Mathf.Max(0.2f, seconds);
        flashColor = new Color(0.02f, 0.03f, 0.09f);
    }

    // Kratky barevny zablesk (napr. posileni od Anny).
    public static void NotifyTint(Color color, float seconds)
    {
        ReplayLog.HudTint(color, seconds);
        flashStart = Time.unscaledTime;
        flashDuration = Mathf.Max(0.1f, seconds);
        flashColor = color;
    }

    public static void EndOverlay()
    {
        ReplayLog.HudEnd();
        flashStart = -10f;
    }

    // Znacka cile taktickeho zamerovace (nastavuje VisorAbility kazdy snimek, kdy ma cil).
    public static Vector3 LockPoint;
    public static int LockFrame = -1;
    RectTransform lockMarker;
    GameObject chargeBar;

    // Rychlopalba: zbyvajici sipy jako modre kosoctverce pod zamerovacem (po kazdem vystrelu jeden zmizi).
    Image beatRing, beatTarget;   // Bard: kruh se stahuje k zamerovaci, na uderu ho trefi
    const int MaxRapidPips = 10;
    readonly Image[] rapidPips = new Image[MaxRapidPips];
    TextMeshProUGUI testLabel;
    Image chargeFill;

    public static void NotifyHit(bool kill)
    {
        ReplayLog.HudHit(kill);
        hitTime = Time.unscaledTime;
        if (kill)
            killTime = Time.unscaledTime;
    }

    static Sprite whiteSprite;
    float lastHealth = -1f;
    float flash;
    object trackedHero;

    // Data, ze kterych se HUD kresli: zivy hrac, nebo prehravani "play of the game" (ReplayPlayer).
    class HudData
    {
        public object key;
        public HeroDefinition hero;
        public int team = -1;
        public float health, maxHealth = 100f;
        public float shield;
        public readonly List<PlayerHero.AbilitySlot> slots = new List<PlayerHero.AbilitySlot>();
        public float charge;
        public WeaponDefinition weapon;
        public int ammo, maxAmmo;
        public bool reloading;
        public bool locked;
        public Vector3 lockPoint;
        public Camera camera;
        public int rapidShots = -1;   // zbyvajici sipy rychlopalby (-1 = neni aktivni)
        public bool beat;             // Bard: ukazat takt
    }

    readonly HudData liveData = new HudData();
    readonly HudData replayData = new HudData();
    readonly HudData killcamData = new HudData();

    // Killcam: jen jmeno hrdiny a zdravi vraha (jeho schopnosti a naboje v zaznamu obeti nejsou).
    public void TickKillcam(int heroIndex, int team, float health, float maxHealth)
    {
        if (root == null || !root.activeSelf) return;
        var hero = HeroRegistry.Get(heroIndex);
        if (hero == null) return;

        var d = killcamData;
        d.key = this;
        d.hero = hero;
        d.team = team;
        d.health = health;
        d.maxHealth = maxHealth;
        d.shield = 0f;
        d.slots.Clear();
        UpdateHealth(d);
        UpdateAbilities(d);   // (prazdne - schovaji se)
        ammoText.text = "";
        if (chargeBar.activeSelf) chargeBar.SetActive(false);
    }

    static readonly Color Dark = new Color(0.05f, 0.07f, 0.10f, 0.72f);
    static readonly Color HealthColor = new Color(0.95f, 0.96f, 1f, 0.95f);
    static readonly Color LowColor = new Color(1f, 0.30f, 0.25f, 0.95f);

    static Sprite White
    {
        get
        {
            if (whiteSprite == null)
                whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), new Vector2(0.5f, 0.5f));
            return whiteSprite;
        }
    }

    public void Build(Transform canvas)
    {
        var center = new Vector2(0.5f, 0.5f);

        var rootImage = UiKit.MakeImage(canvas, "Hud", new Color(0f, 0f, 0f, 0f), center, Vector2.zero, Vector2.zero);
        UiKit.Stretch(rootImage.rectTransform);
        root = rootImage.gameObject;

        // Cervene okraje pri zasahu.
        var vignetteObject = new GameObject("DamageVignette", typeof(RectTransform), typeof(RawImage));
        vignetteObject.transform.SetParent(root.transform, false);
        vignette = vignetteObject.GetComponent<RawImage>();
        vignette.texture = VignetteTexture();
        vignette.raycastTarget = false;
        vignette.color = new Color(0.85f, 0.05f, 0.05f, 0f);
        UiKit.Stretch(vignette.rectTransform);

        // Portret hrdiny vlevo dole, zivoty vedle nej.
        var bottomLeft = new Vector2(0f, 0f);
        portraitFrame = UiKit.MakeImage(root.transform, "Portrait", Dark, bottomLeft, new Vector2(44f, 50f), new Vector2(124f, 124f));
        var portraitObject = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
        portraitObject.transform.SetParent(portraitFrame.transform, false);
        portrait = portraitObject.GetComponent<RawImage>();
        portrait.raycastTarget = false;
        var portraitRect = portrait.rectTransform;
        portraitRect.anchorMin = portraitRect.anchorMax = portraitRect.pivot = center;
        portraitRect.sizeDelta = new Vector2(112f, 112f);
        portraitInitials = UiKit.MakeText(portraitFrame.transform, "Initials", "", 48, TextAlignmentOptions.Center, center, Vector2.zero, new Vector2(112f, 112f));
        portraitInitials.fontStyle = FontStyles.Bold;

        const float hx = 188f;   // zivoty a jmeno vpravo od portretu
        nameText = UiKit.MakeText(root.transform, "HeroName", "", 26, TextAlignmentOptions.BottomLeft, bottomLeft,
            new Vector2(hx + 2f, 150f), new Vector2(700f, 36f), UiKit.Muted);
        healthText = UiKit.MakeText(root.transform, "Health", "", 64, TextAlignmentOptions.BottomLeft, bottomLeft,
            new Vector2(hx, 84f), new Vector2(500f, 76f));

        var barBackground = UiKit.MakeImage(root.transform, "HealthBar", Dark, bottomLeft, new Vector2(hx, 52f), new Vector2(HealthBarWidth + 6f, 30f));
        healthBar = barBackground.rectTransform;
        healthFill = UiKit.MakeImage(healthBar, "Fill", HealthColor, bottomLeft, new Vector2(3f, 3f), new Vector2(HealthBarWidth, 24f));
        healthFill.rectTransform.pivot = new Vector2(0f, 0f);
        // Docasny stit (Bardova ultimatka): modra cast pres pravy konec baru.
        shieldFill = UiKit.MakeImage(healthBar, "Shield", new Color(Health.ShieldColor.r, Health.ShieldColor.g, Health.ShieldColor.b, 0.85f),
            bottomLeft, new Vector2(3f, 3f), new Vector2(0f, 24f));
        shieldFill.rectTransform.pivot = new Vector2(0f, 0f);

        // Schopnosti vpravo dole (zprava doleva), munice nad nimi.
        var bottomRight = new Vector2(1f, 0f);
        for (int i = 0; i < MaxSlots; i++)
            slots[i] = BuildSlot(root.transform, bottomRight, new Vector2(-64f - i * 116f, 86f));

        ammoText = UiKit.MakeText(root.transform, "Ammo", "", 56, TextAlignmentOptions.BottomRight, bottomRight,
            new Vector2(-64f, 206f), new Vector2(600f, 70f));

        // Ultimatka dole uprostred: kulata, vetsi; pod ni zare (pri plnem nabiti pulzuje modre).
        var bottomCenter = new Vector2(0.5f, 0f);
        ultGlow = UiKit.MakeImage(root.transform, "UltGlow", new Color(UltGlowColor.r, UltGlowColor.g, UltGlowColor.b, 0f), bottomCenter,
            new Vector2(0f, 40f), new Vector2(190f, 190f));
        ultGlow.sprite = GlowSprite;
        ultSlot = BuildSlot(root.transform, bottomCenter, new Vector2(0f, 75f));
        ultSlot.root.transform.localScale = Vector3.one * 1.2f;
        ultSlot.background.sprite = CircleSprite;
        ultSlot.cooldown.sprite = CircleSprite;
        ultSlot.readyBar.gameObject.SetActive(false);

        // Krizek zasahu: ctyri sikme carky kolem zamerovace.
        var hitObject = new GameObject("HitMarker", typeof(RectTransform));
        hitObject.transform.SetParent(root.transform, false);
        hitRoot = hitObject.GetComponent<RectTransform>();
        hitRoot.anchorMin = hitRoot.anchorMax = hitRoot.pivot = center;
        hitRoot.anchoredPosition = Vector2.zero;
        for (int i = 0; i < 4; i++)
        {
            float angle = 45f + i * 90f;
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            var line = UiKit.MakeImage(hitRoot, "Line", Color.white, center, direction * 17f, new Vector2(15f, 3.5f));
            line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            line.raycastTarget = false;
            hitLines[i] = line;
        }
        hitObject.SetActive(false);

        var killObject = new GameObject("KillIcon", typeof(RectTransform), typeof(RawImage));
        killObject.transform.SetParent(root.transform, false);
        killIcon = killObject.GetComponent<RawImage>();
        killIcon.texture = Resources.Load<Texture2D>("Icons/kill_skull");
        killIcon.raycastTarget = false;
        var killRect = killIcon.rectTransform;
        killRect.anchorMin = killRect.anchorMax = killRect.pivot = center;
        killRect.anchoredPosition = new Vector2(0f, -58f);
        killRect.sizeDelta = new Vector2(46f, 46f);
        killObject.SetActive(false);

        // Znacka zamereneho cile: zluty kosoctverec.
        var lockObject = new GameObject("LockMarker", typeof(RectTransform));
        lockObject.transform.SetParent(root.transform, false);
        lockMarker = lockObject.GetComponent<RectTransform>();
        lockMarker.anchorMin = lockMarker.anchorMax = lockMarker.pivot = center;
        lockMarker.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var yellow = new Color(1f, 0.85f, 0.15f, 0.95f);
        UiKit.MakeImage(lockMarker, "T", yellow, center, new Vector2(0f, 22f), new Vector2(48f, 4f));
        UiKit.MakeImage(lockMarker, "B", yellow, center, new Vector2(0f, -22f), new Vector2(48f, 4f));
        UiKit.MakeImage(lockMarker, "L", yellow, center, new Vector2(-22f, 0f), new Vector2(4f, 48f));
        UiKit.MakeImage(lockMarker, "R", yellow, center, new Vector2(22f, 0f), new Vector2(4f, 48f));
        lockObject.SetActive(false);

        testLabel = UiKit.MakeText(root.transform, "TestCooldowns", "TESTOVACÍ COOLDOWNY 1 s", 22, TextAlignmentOptions.BottomRight,
            new Vector2(1f, 0f), new Vector2(-64f, 286f), new Vector2(500f, 30f), new Color(0.49f, 0.99f, 0.6f, 0.9f));
        testLabel.gameObject.SetActive(false);

        var chargeBackground = UiKit.MakeImage(root.transform, "ChargeBar", new Color(0f, 0f, 0f, 0.55f), center, new Vector2(0f, -42f), new Vector2(160f, 10f));
        chargeBar = chargeBackground.gameObject;
        chargeFill = UiKit.MakeImage(chargeBar.transform, "Fill", Color.white, new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(0f, 6f));
        chargeBar.SetActive(false);

        beatTarget = UiKit.MakeImage(root.transform, "BeatTarget", new Color(1f, 1f, 1f, 0.35f), center, Vector2.zero, new Vector2(34f, 34f));
        beatTarget.sprite = RingSprite;
        beatTarget.raycastTarget = false;
        beatRing = UiKit.MakeImage(root.transform, "BeatRing", new Color(1f, 1f, 1f, 0.5f), center, Vector2.zero, new Vector2(110f, 110f));
        beatRing.sprite = RingSprite;
        beatRing.raycastTarget = false;
        beatTarget.gameObject.SetActive(false);
        beatRing.gameObject.SetActive(false);

        for (int i = 0; i < MaxRapidPips; i++)
        {
            var pip = UiKit.MakeImage(root.transform, "RapidPip", new Color(0.35f, 0.75f, 1f, 0.95f), center, new Vector2(0f, -64f), new Vector2(11f, 11f));
            pip.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            pip.gameObject.AddComponent<Outline>().effectColor = new Color(0.02f, 0.1f, 0.25f, 0.9f);
            pip.gameObject.SetActive(false);
            rapidPips[i] = pip;
        }

        flashImage = UiKit.MakeImage(root.transform, "Flash", new Color(1f, 1f, 1f, 0f), center, Vector2.zero, Vector2.zero);
        UiKit.Stretch(flashImage.rectTransform);
        flashImage.gameObject.SetActive(false);

        root.SetActive(false);
    }

    void UpdateFlashAndLock(HudData d)
    {
        float since = Time.unscaledTime - flashStart;
        bool flashing = since >= 0f && since < flashDuration;
        if (flashImage.gameObject.activeSelf != flashing)
            flashImage.gameObject.SetActive(flashing);
        if (flashing)
        {
            float t = since / flashDuration;
            float alpha = flashColor.a < 1f ? flashColor.a : 0.92f;
            flashImage.color = new Color(flashColor.r, flashColor.g, flashColor.b, alpha * (1f - t * t));
        }

        bool locked = false;
        if (d.locked && d.camera != null)
        {
            Vector3 screen = d.camera.WorldToScreenPoint(d.lockPoint);
            if (screen.z > 0.2f && RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)root.transform, screen, null, out Vector2 point))
            {
                locked = true;
                lockMarker.anchoredPosition = point;
            }
        }

        if (lockMarker.gameObject.activeSelf != locked)
            lockMarker.gameObject.SetActive(locked);
    }

    void UpdateHitMarker()
    {
        float sinceHit = Time.unscaledTime - hitTime;
        float sinceKill = Time.unscaledTime - killTime;

        bool showHit = sinceHit >= 0f && sinceHit < HitDuration;
        if (hitRoot.gameObject.activeSelf != showHit)
            hitRoot.gameObject.SetActive(showHit);

        if (showHit)
        {
            float t = sinceHit / HitDuration;
            bool kill = sinceKill >= 0f && sinceKill < HitDuration;
            var color = kill ? new Color(1f, 0.22f, 0.18f) : Color.white;
            color.a = 1f - t * t;
            foreach (var line in hitLines)
                line.color = color;
            hitRoot.localScale = Vector3.one * Mathf.Lerp(kill ? 1.7f : 1.35f, 1f, Mathf.Clamp01(t * 3f));
        }

        bool showKill = killIcon.texture != null && sinceKill >= 0f && sinceKill < KillDuration;
        if (killIcon.gameObject.activeSelf != showKill)
            killIcon.gameObject.SetActive(showKill);

        if (showKill)
        {
            float t = sinceKill / KillDuration;
            killIcon.color = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - t) * 3f));
            killIcon.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, Mathf.Clamp01(t * 6f));
        }
    }

    Slot BuildSlot(Transform parent, Vector2 anchor, Vector2 position)
    {
        var slot = new Slot();
        var center = new Vector2(0.5f, 0.5f);

        slot.background = UiKit.MakeImage(parent, "Ability", Dark, anchor, position, new Vector2(100f, 100f));
        slot.root = slot.background.gameObject;

        var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
        iconObject.transform.SetParent(slot.root.transform, false);
        slot.icon = iconObject.GetComponent<RawImage>();
        slot.icon.raycastTarget = false;
        var iconRect = slot.icon.rectTransform;
        iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = center;
        iconRect.sizeDelta = new Vector2(76f, 76f);

        // Tmavy vysec ukazuje, kolik cooldownu zbyva.
        slot.cooldown = UiKit.MakeImage(slot.root.transform, "Cooldown", new Color(0f, 0f, 0f, 0.62f), center, Vector2.zero, new Vector2(100f, 100f));
        slot.cooldown.sprite = White;
        slot.cooldown.type = Image.Type.Filled;
        slot.cooldown.fillMethod = Image.FillMethod.Radial360;
        slot.cooldown.fillOrigin = (int)Image.Origin360.Top;
        slot.cooldown.fillClockwise = false;

        slot.timer = UiKit.MakeText(slot.root.transform, "Timer", "", 44, TextAlignmentOptions.Center, center, Vector2.zero, new Vector2(100f, 100f));
        slot.readyBar = UiKit.MakeImage(slot.root.transform, "Ready", UiKit.Accent, new Vector2(0.5f, 0f), new Vector2(0f, -8f), new Vector2(100f, 5f));
        slot.key = UiKit.MakeText(slot.root.transform, "Key", "", 22, TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0f, -40f), new Vector2(140f, 30f), new Color(1f, 1f, 1f, 0.9f));

        slot.count = UiKit.MakeText(slot.root.transform, "Count", "", 26, TextAlignmentOptions.TopRight, new Vector2(1f, 1f), new Vector2(-6f, -2f), new Vector2(60f, 32f));

        slot.root.SetActive(false);
        return slot;
    }

    public void SetVisible(bool visible)
    {
        if (root != null && root.activeSelf != visible)
            root.SetActive(visible);
    }

    public void Tick(PlayerHero hero)
    {
        if (root == null || !root.activeSelf || hero == null || hero.Hero == null) return;

        var health = hero.GetComponent<Health>();
        var weapon = hero.GetComponent<WeaponShooting>();
        var team = hero.GetComponent<PlayerTeam>();
        var fpc = hero.GetComponent<FirstPersonController>();

        var d = liveData;
        d.key = hero;
        d.hero = hero.Hero;
        d.team = team != null ? team.teamId.Value : -1;
        d.health = health != null ? health.currentHealth.Value : 0f;
        d.maxHealth = health != null ? health.maxHealth : 100f;
        d.shield = health != null ? health.shield.Value : 0f;
        hero.GetAbilitySlots(d.slots);
        d.charge = weapon != null ? weapon.ChargeFraction : 0f;
        d.weapon = weapon != null ? weapon.weapon : null;
        d.ammo = weapon != null ? weapon.CurrentAmmo : 0;
        d.maxAmmo = weapon != null ? weapon.MaxAmmo : 0;
        d.reloading = weapon != null && weapon.IsReloading;
        d.camera = fpc != null ? fpc.playerCamera : null;
        d.locked = LockFrame >= Time.frameCount - 1;
        d.lockPoint = LockPoint;
        var rapid = hero.GetComponent<RapidFireAbility>();
        d.rapidShots = rapid != null && rapid.enabled && rapid.IsActive ? rapid.ShotsLeft : -1;
        var bardHud = hero.GetComponent<BardAbility>();
        d.beat = bardHud != null && bardHud.isActiveAndEnabled && bardHud.crossfade != null;
        Render(d);
    }

    // HUD hrace, ktery predvedl "play of the game", podle zaznamu.
    public void TickReplay(ReplayFrame frame, Camera camera)
    {
        if (root == null || !root.activeSelf || frame == null) return;

        var hero = HeroRegistry.Get(frame.hudHero);
        if (hero == null) return;

        var d = replayData;
        d.key = this;
        d.hero = hero;
        d.team = frame.hudTeam;
        d.health = frame.health;
        d.maxHealth = frame.maxHealth;
        d.slots.Clear();
        if (frame.slots != null)
            foreach (var s in frame.slots)
                d.slots.Add(new PlayerHero.AbilitySlot
                {
                    key = ReplaySlot.Keys[Mathf.Clamp(s.key, 0, ReplaySlot.Keys.Length - 1)],
                    ability = HeroVoice.AbilityInSlot(hero, s.abilitySlot),
                    remaining = s.remaining, active = s.active, charge = s.charge, count = s.count, fullOnly = s.fullOnly,
                });
        d.charge = frame.charge;
        d.weapon = hero.weapon;
        d.ammo = frame.ammo;
        d.maxAmmo = frame.maxAmmo;
        d.reloading = frame.reloading;
        d.camera = camera;
        d.locked = frame.locked;
        d.lockPoint = frame.lockPoint;
        Render(d);
    }

    void UpdateRapidPips(HudData d)
    {
        int count = Mathf.Clamp(d.rapidShots, 0, MaxRapidPips);
        const float spacing = 18f;
        float start = -(count - 1) * spacing * 0.5f;
        float pulse = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 10f);
        for (int i = 0; i < MaxRapidPips; i++)
        {
            bool used = d.rapidShots >= 0 && i < count;
            if (rapidPips[i].gameObject.activeSelf != used)
                rapidPips[i].gameObject.SetActive(used);
            if (!used) continue;
            rapidPips[i].rectTransform.anchoredPosition = new Vector2(start + i * spacing, -64f);
            rapidPips[i].color = new Color(0.35f, 0.75f, 1f, 0.95f * pulse);
        }
    }

    void UpdateBeat(HudData d)
    {
        if (beatRing.gameObject.activeSelf != d.beat) beatRing.gameObject.SetActive(d.beat);
        if (beatTarget.gameObject.activeSelf != d.beat) beatTarget.gameObject.SetActive(d.beat);
        if (!d.beat) return;
        // kruh se stahuje k zamerovaci a na uderu ho dosahne; v okne taktu zezlutne
        float toBeat = 1f - BardAbility.BeatPhase;
        bool on = BardAbility.OnBeat;
        var gold = new Color(1f, 0.85f, 0.3f);
        beatRing.rectTransform.sizeDelta = Vector2.one * (34f + 76f * toBeat);
        beatRing.color = on ? new Color(gold.r, gold.g, gold.b, 0.9f) : new Color(1f, 1f, 1f, 0.15f + 0.35f * (1f - toBeat));
        // trefa do taktu: cilovy kruh blikne a zvetsi se
        float hit = Mathf.Clamp01(1f - (Time.time - BardAbility.LastLocalBeatShot) / 0.35f);
        beatTarget.rectTransform.sizeDelta = Vector2.one * (34f + 18f * hit);
        beatTarget.color = Color.Lerp(new Color(1f, 1f, 1f, 0.35f), gold, hit);
    }

    void Render(HudData d)
    {
        UpdateHealth(d);
        UpdateRapidPips(d);
        UpdateBeat(d);
        UpdateVignette(d);
        UpdateAbilities(d);
        UpdateHitMarker();
        UpdateFlashAndLock(d);

        if (testLabel.gameObject.activeSelf != AbilityDefinition.TestCooldowns)
            testLabel.gameObject.SetActive(AbilityDefinition.TestCooldowns);

        // Natazeni luku: pruh pod zamerovacem.
        float charge = d.charge;
        bool showCharge = charge > 0.01f;
        if (chargeBar.activeSelf != showCharge)
            chargeBar.SetActive(showCharge);
        if (showCharge)
        {
            chargeFill.rectTransform.sizeDelta = new Vector2(156f * charge, 6f);
            chargeFill.color = charge >= 0.999f ? UiKit.Accent : Color.white;
        }

        if (d.weapon != null)
        {
            ammoText.text = !d.weapon.HasAmmo
                ? $"<size=50%>{d.weapon.weaponName}</size>"
                : d.reloading
                    ? "<size=50%>PŘEBÍJÍM…</size>"
                    : $"{d.ammo}<size=50%> / {d.maxAmmo}</size>";
        }
    }

    void UpdateHealth(HudData d)
    {
        float max = Mathf.Max(1f, d.maxHealth);
        float current = Mathf.Clamp(d.health, 0f, max);
        float fraction = current / max;

        nameText.text = d.team >= 0
            ? $"{d.hero.heroName.ToUpperInvariant()}  ·  <color=#{ColorUtility.ToHtmlStringRGB(UiKit.TeamColor(d.team))}>TÝM {d.team}</color>"
            : d.hero.heroName.ToUpperInvariant();
        healthText.text = d.shield >= 0.5f
            ? $"{Mathf.CeilToInt(current)}<color=#{ColorUtility.ToHtmlStringRGB(Health.ShieldColor)}>+{Mathf.CeilToInt(d.shield)}</color><size=45%> / {max:0}</size>"
            : $"{Mathf.CeilToInt(current)}<size=45%> / {max:0}</size>";
        healthText.color = fraction < 0.3f ? LowColor : Color.white;

        healthFill.rectTransform.sizeDelta = new Vector2(HealthBarWidth * fraction, 24f);
        healthFill.color = fraction < 0.3f ? LowColor : HealthColor;

        // Stit: zobrazi se pres bar od konce zdravi (kdyz se nevejde, pres jeho pravou cast).
        float shieldWidth = HealthBarWidth * Mathf.Clamp01(d.shield / max);
        float shieldStart = Mathf.Min(HealthBarWidth * fraction, HealthBarWidth - shieldWidth);
        shieldFill.rectTransform.anchoredPosition = new Vector2(3f + shieldStart, 3f);
        shieldFill.rectTransform.sizeDelta = new Vector2(shieldWidth, 24f);

        // Dilky po 25 zivotech jako v Overwatch.
        if (!Mathf.Approximately(separatorsFor, max))
        {
            separatorsFor = max;
            foreach (var old in separators) Object.Destroy(old);
            separators.Clear();

            for (float at = HealthPerSegment; at < max - 0.5f; at += HealthPerSegment)
            {
                var line = UiKit.MakeImage(healthBar, "Segment", new Color(0.05f, 0.07f, 0.10f, 1f), new Vector2(0f, 0f),
                    new Vector2(3f + HealthBarWidth * at / max - 1.5f, 3f), new Vector2(3f, 24f));
                line.rectTransform.pivot = new Vector2(0f, 0f);
                separators.Add(line.gameObject);
            }
        }
    }

    void UpdateVignette(HudData d)
    {
        float max = Mathf.Max(1f, d.maxHealth);
        float current = d.health;

        if (d.key != trackedHero)
        {
            trackedHero = d.key;
            lastHealth = current;
            flash = 0f;
        }

        if (current < lastHealth - 0.01f)
            flash = Mathf.Clamp01(Mathf.Max(flash, 0.35f) + (lastHealth - current) / max * 3f);
        lastHealth = current;

        flash = Mathf.MoveTowards(flash, 0f, Time.unscaledDeltaTime * 1.4f);

        // Malo zivotu: okraje zustavaji lehce cervene a pulzuji.
        float fraction = current / max;
        float low = current > 0f && fraction < 0.3f
            ? (0.3f - fraction) / 0.3f * (0.50f + 0.15f * Mathf.Sin(Time.unscaledTime * 5f))
            : 0f;

        var color = vignette.color;
        color.a = Mathf.Clamp01(Mathf.Max(flash * 0.7f, low));
        vignette.color = color;
    }

    void UpdateAbilities(HudData d)
    {
        UpdatePortrait(d);

        // ultimatka (Q) ma vlastni slot uprostred, ostatni schopnosti zustavaji vpravo
        abilities.Clear();
        PlayerHero.AbilitySlot? ult = null;
        foreach (var s in d.slots)
        {
            if (s.key == "Q" && ult == null) ult = s;
            else abilities.Add(s);
        }

        bool hasUlt = ult.HasValue;
        if (ultSlot.root.activeSelf != hasUlt)
            ultSlot.root.SetActive(hasUlt);
        bool ultReady = hasUlt && RenderSlot(ultSlot, ult.Value);
        float glow = ultReady ? 0.55f + 0.3f * Mathf.Sin(Time.unscaledTime * 4f) : 0f;
        ultGlow.color = new Color(UltGlowColor.r, UltGlowColor.g, UltGlowColor.b, Mathf.MoveTowards(ultGlow.color.a, glow, Time.unscaledDeltaTime * 3f));
        ultGlow.gameObject.SetActive(hasUlt);
        if (hasUlt) ultSlot.background.color = ultReady ? new Color(0.08f, 0.16f, 0.32f, 0.9f) : ultSlot.background.color;

        for (int i = 0; i < MaxSlots; i++)
        {
            var slot = slots[i];
            bool used = i < abilities.Count;
            if (slot.root.activeSelf != used)
                slot.root.SetActive(used);
            if (!used) continue;
            RenderSlot(slot, abilities[i]);
        }
    }

    // Vykresli jeden slot schopnosti; vraci, jestli je schopnost pripravena.
    bool RenderSlot(Slot slot, PlayerHero.AbilitySlot info)
    {
        {
            var icon = info.ability != null ? info.ability.icon : null;
            slot.icon.texture = icon;
            slot.icon.enabled = icon != null;
            slot.key.text = info.key;
            slot.count.text = info.count > 0 ? info.count.ToString() : "";

            bool charging = info.charge >= 0f && info.charge < 0.999f;
            bool onCooldown = info.remaining > 0.05f && !info.active;
            bool ready = !onCooldown && !info.active && !(info.charge >= 0f && info.charge <= 0.01f) && !(info.fullOnly && charging);

            float total = info.ability != null ? Mathf.Max(0.1f, info.ability.Cooldown) : 1f;
            slot.cooldown.fillAmount = onCooldown ? Mathf.Clamp01(info.remaining / total) : (charging ? 1f - info.charge : 0f);

            if (onCooldown)
                slot.timer.text = info.remaining >= 10f ? Mathf.CeilToInt(info.remaining).ToString() : info.remaining.ToString("0.0");
            else if (charging && !info.active)
                slot.timer.text = $"<size=60%>{Mathf.RoundToInt(info.charge * 100f)}%</size>";
            else
                slot.timer.text = "";

            slot.background.color = info.active ? new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.85f) : Dark;
            slot.icon.color = info.active ? Color.white : (ready ? Color.white : new Color(1f, 1f, 1f, 0.35f));
            slot.readyBar.enabled = ready;
            return ready;
        }
    }

    void UpdatePortrait(HudData d)
    {
        if (d.hero == portraitFor) return;
        portraitFor = d.hero;
        var picture = d.hero != null ? Resources.Load<Texture2D>("Portraits/" + d.hero.name) : null;
        portrait.texture = picture;
        portrait.enabled = picture != null;
        // zastupny portret: barva hrdiny a inicialy (dokud neni obrazek)
        string heroName = d.hero != null ? d.hero.heroName : "";
        portraitInitials.text = picture == null && heroName.Length > 0 ? heroName.Substring(0, Mathf.Min(2, heroName.Length)).ToUpperInvariant() : "";
        Color heroColor = d.hero != null ? d.hero.color : Color.gray;
        portraitFrame.color = picture == null ? new Color(heroColor.r * 0.7f, heroColor.g * 0.7f, heroColor.b * 0.7f, 0.9f) : Dark;
    }

    // Kruh (pro kulaty slot ultimatky) a mekka zare kolem nej - generovane, bez textur v projektu.
    static Sprite CircleSprite => circleSprite != null ? circleSprite : (circleSprite = RadialSprite(128, 0.98f, 1f));
    static Sprite GlowSprite => glowSprite != null ? glowSprite : (glowSprite = RadialSprite(128, 0.35f, 1f));
    static Sprite ringSprite;

    // Tenky prstenec (takt Barda).
    static Sprite RingSprite
    {
        get
        {
            if (ringSprite != null) return ringSprite;
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.92f) / 0.06f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            texture.SetPixels(pixels);
            texture.Apply();
            return ringSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }
    }

    static Sprite RadialSprite(int size, float solidTo, float fadeTo)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = d <= solidTo ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(solidTo, fadeTo, d));
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
    }

    static Texture2D VignetteTexture()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half, dy = (y - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.35f;
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1f, d)));
            }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
