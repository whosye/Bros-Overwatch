using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Drzi, ktereho hrdinu tenhle hrac hraje (HeroDefinition z Resources/Heroes) a aplikuje ho:
// zbran, zdravi, aktivni schopnost. Vyber hrdiny (a jmeno) posila vlastnik hrace serveru; menit hrdinu jde jen v lobby.
public class PlayerHero : NetworkBehaviour
{
    public static int PreferredHero = 0;
    public static string PreferredName = "Hráč";

    public NetworkVariable<int> heroId = new NetworkVariable<int>(-1);
    public NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>();
    // Bot (BotBrain na hostu) - vidi vsichni hraci (oznaceni v lobby).
    public NetworkVariable<bool> isBot = new NetworkVariable<bool>(false);
    // Neviditelny (Max uvnitr obeti pri ultimatce): skryty model, zbran, jmenovka a bez kolize.
    public NetworkVariable<bool> isHidden = new NetworkVariable<bool>(false);
    public bool IsHidden => isHidden.Value;
    // Necilitelny: neviditelny, nebo Max ve stinove forme (viditelny stin, ale bez jmenovky, kolize a zraneni).
    public bool IsUntargetable => isHidden.Value || (max != null && max.ShadowFormActive);

    void OnHiddenChanged(bool previous, bool current)
    {
        var body = GetComponent<CharacterController>();
        if (body != null && !(IsOwner && current)) body.enabled = !current;
    }

    // Hrac se pripojil do rozehraneho zapasu a jeste si vybira tym a hrdinu (vidi lobby, do hry vstoupi tlacitkem).
    // Do te doby stoji mimo mapu, nejde zranit a nemuze nic delat.
    public NetworkVariable<bool> joining = new NetworkVariable<bool>(false);
    public bool IsJoining => joining.Value;
    bool parked;

    // Statistiky do tabulky hracu (Tab). Pocita server, nuluji se s kazdym novym zapasem.
    public NetworkVariable<int> kills = new NetworkVariable<int>();
    public NetworkVariable<int> deaths = new NetworkVariable<int>();
    public NetworkVariable<float> healing = new NetworkVariable<float>();   // vylecene HP spoluhracum (bez sebe)

    public void ServerAddHealingStat(float amount)
    {
        if (IsServer && amount > 0f) healing.Value += amount;
    }

    public void ServerResetStats()
    {
        if (!IsServer) return;
        kills.Value = 0;
        deaths.Value = 0;
        healing.Value = 0f;
        ultCharge.Value = 0f;
    }

    // ---------------- pad mimo mapu ----------------

    // Kdo hrace naposledy zranil nebo odhodil (kdyz pak spadne z mapy, zabiti se pripise jemu).
    GameObject lastAttacker;
    float lastAttackTime = -100f;
    float nextFallCheck;

    // Kdo hrace v posledni dobe zranil (asistence pri zabiti).
    readonly System.Collections.Generic.Dictionary<GameObject, float> recentAttackers = new System.Collections.Generic.Dictionary<GameObject, float>();
    public const float AssistSeconds = 8f;

    public System.Collections.Generic.List<GameObject> ServerRecentAttackers()
    {
        var list = new System.Collections.Generic.List<GameObject>();
        foreach (var pair in recentAttackers)
            if (pair.Key != null && Time.time - pair.Value <= AssistSeconds)
                list.Add(pair.Key);
        return list;
    }

    public void ServerClearAttackers() => recentAttackers.Clear();

    // Zabiti nebo asistence: zkratit zbyvajici cooldowny (Pova). Cooldowny drzi vlastnik hrace.
    public void ServerTakedownRefund()
    {
        if (!IsServer || Hero == null || Hero.takedownCooldownCut <= 0f) return;
        TakedownRefundClientRpc(Hero.takedownCooldownCut);
    }

    [ClientRpc]
    void TakedownRefundClientRpc(float cut)
    {
        if (!IsOwner) return;
        foreach (var behaviour in GetComponents<ICooldownCut>())
            behaviour.CutCooldown(cut);
    }

    public void ServerNoteAttacker(GameObject attacker)
    {
        if (!IsServer || attacker == null || attacker == gameObject) return;
        recentAttackers[attacker] = Time.time;

        lastAttacker = attacker;
        lastAttackTime = Time.time;
    }

    void ServerCheckFall()
    {
        if (joining.Value || Time.time < nextFallCheck) return;
        if (transform.position.y > MatchManager.KillHeight) return;

        nextFallCheck = Time.time + 1f;

        var match = MatchManager.Instance;
        bool playing = match != null && !match.IsLobby && !match.IsOver;

        // V lobby a po konci zapasu se neumira: hrac se jen vrati na zakladnu.
        if (!playing)
        {
            ReturnToSpawnClientRpc();
            return;
        }

        if (health.currentHealth.Value <= 0f) return;

        if (lastAttacker != null && Time.time - lastAttackTime < 6f)
            Combat.DamagePlayer(lastAttacker, health, 100000f, "env:fall");

        if (health.currentHealth.Value > 0f)
        {
            ServerDeathCause = "env:fall";
            health.Kill();
        }
    }

    [ClientRpc]
    void ReturnToSpawnClientRpc()
    {
        if (!IsOwner) return;

        var respawn = GetComponent<PlayerRespawn>();
        if (respawn != null)
            respawn.ResetToSpawn();
    }

    // ---------------- nabijeni ultimatky (Q) ----------------

    // Ultimatka se nenabiji casem (cooldownem), ale hrou: 1 bod za kazdy bod zpusobeneho poskozeni, 1 bod za kazdy
    // zivot vyleceny spoluhraci a k tomu pomalu sama (UltPassivePerSecond). Cena je u schopnosti (AbilityDefinition.ultCost).
    // Po smrti nabiti zustava, nuluje se pouzitim a s novym zapasem.
    public const float UltPassivePerSecond = 2f;

    public NetworkVariable<float> ultCharge = new NetworkVariable<float>();
    bool ultSpentLocally;

    public float UltCost => Hero != null && Hero.ability != null && !AbilityDefinition.TestCooldowns ? Hero.ability.ultCost : 0f;
    public bool UsesUltCharge => UltCost > 0f;
    public float UltFraction => UsesUltCharge ? Mathf.Clamp01(ultCharge.Value / UltCost) : 1f;
    public bool UltReady => !UsesUltCharge || (ultCharge.Value >= UltCost - 0.01f && !ultSpentLocally);

    // Vola ultimatni schopnost na vlastnikovi, kdyz se pouzije.
    public void SpendUlt()
    {
        if (!UsesUltCharge) return;

        ultSpentLocally = true;
        SpendUltServerRpc();
    }

    [ServerRpc]
    void SpendUltServerRpc()
    {
        ultCharge.Value = 0f;
    }

    void OnUltChargeChanged(float previous, float current)
    {
        if (current < previous)
            ultSpentLocally = false;
    }

    // Dokud ultimatka bezi, dalsi nabiti se nepocita (ani za poskozeni, ktere sama zpusobi).
    bool ServerUltActive()
    {
        return (leap != null && leap.enabled && leap.IsAirborne)
            || (boulder != null && boulder.enabled && boulder.IsRolling)
            || (visor != null && visor.enabled && visor.IsScanning)
            || (storm != null && storm.enabled && storm.IsStormActive)
            || (ezekiel != null && ezekiel.enabled && ezekiel.IsCasting)
            || (trespass != null && trespass.enabled && trespass.ServerActive);
    }

    // ---------------- odhaleni pruzkumnym sipem ----------------

    // Odhaleny hrac je videt souperum i pres zdi (jmenovka se zivoty).
    public NetworkVariable<bool> revealed = new NetworkVariable<bool>(false);

    // Posileni od Anny (ultimatka): vetsi poskozeni, mensi prijimane poskozeni, viditelna aura.
    public NetworkVariable<bool> boosted = new NetworkVariable<bool>(false);
    public bool IsBoosted => boosted.Value;
    float boostUntil;

    public void ServerBoost(float seconds)
    {
        if (!IsServer) return;
        boostUntil = Time.time + seconds;
        boosted.Value = true;
    }
    float revealUntil;

    public void ServerReveal(float seconds)
    {
        if (!IsServer) return;

        revealUntil = Mathf.Max(revealUntil, Time.time + seconds);
        if (!revealed.Value)
            revealed.Value = true;
    }

    public void ServerAddUltCharge(float points)
    {
        if (!IsServer || points <= 0f || !UsesUltCharge || ServerUltActive()) return;

        var match = MatchManager.Instance;
        if (match != null && (match.IsLobby || match.IsOver)) return;

        ultCharge.Value = Mathf.Min(UltCost, ultCharge.Value + points);
    }

    public HeroDefinition Hero { get; private set; }
    public string DisplayName => playerName.Value.Length > 0 ? playerName.Value.ToString() : $"Hráč {OwnerClientId}";

    Health health;
    WeaponShooting shooting;
    DashAbility dash;
    LeapStrikeAbility leap;
    RushAbility rush;
    BlockAbility block;
    MineAbility mine;
    TrapAbility trap;
    BoulderAbility boulder;
    HealFieldAbility healField;
    FlashAbility flash;
    VisorAbility visor;
    HookAbility hook;
    ScoutArrowAbility scout;
    RapidFireAbility rapidFire;
    StormAbility storm;
    SleepDartAbility sleepDart;
    BioticGrenadeAbility bioGrenade;
    NanoBoostAbility nano;
    GrappleAbility grapple;
    KickAbility kick;
    InfraAbility infra;
    BlinkAbility blink;
    RecallAbility recall;
    PulseBombAbility pulseBomb;
    BardAbility bard;
    SindelAbility sindel;
    EzekielAbility ezekiel;
    MaxAbility max;
    TrespassAbility trespass;
    HeroVoice voice;

    void Awake()
    {
        health = GetComponent<Health>();
        shooting = GetComponent<WeaponShooting>();
        dash = GetComponent<DashAbility>();
        leap = GetComponent<LeapStrikeAbility>();
        rush = GetComponent<RushAbility>();
        block = GetComponent<BlockAbility>();
        mine = GetComponent<MineAbility>();
        trap = GetComponent<TrapAbility>();
        boulder = GetComponent<BoulderAbility>();
        healField = GetComponent<HealFieldAbility>();
        flash = GetComponent<FlashAbility>();
        visor = GetComponent<VisorAbility>();
        hook = GetComponent<HookAbility>();
        scout = GetComponent<ScoutArrowAbility>();
        rapidFire = GetComponent<RapidFireAbility>();
        storm = GetComponent<StormAbility>();
        sleepDart = GetComponent<SleepDartAbility>();
        bioGrenade = GetComponent<BioticGrenadeAbility>();
        nano = GetComponent<NanoBoostAbility>();
        grapple = GetComponent<GrappleAbility>();
        kick = GetComponent<KickAbility>();
        infra = GetComponent<InfraAbility>();
        blink = GetComponent<BlinkAbility>();
        recall = GetComponent<RecallAbility>();
        pulseBomb = GetComponent<PulseBombAbility>();
        bard = GetComponent<BardAbility>();
        sindel = GetComponent<SindelAbility>();
        ezekiel = GetComponent<EzekielAbility>();
        max = GetComponent<MaxAbility>();
        trespass = GetComponent<TrespassAbility>();
        voice = GetComponent<HeroVoice>();
    }

    public override void OnNetworkSpawn()
    {
        heroId.OnValueChanged += OnHeroChanged;
        ultCharge.OnValueChanged += OnUltChargeChanged;
        isHidden.OnValueChanged += OnHiddenChanged;
        boosted.OnValueChanged += OnBoostedChanged;
        if (boosted.Value)
            OnBoostedChanged(false, true);
        health.OnDeath += OnDeath;
        health.currentHealth.OnValueChanged += OnHealthChanged;

        if (IsServer && MatchManager.Instance != null && !MatchManager.Instance.IsLobby)
            joining.Value = true;

        if (heroId.Value >= 0)
            Apply(heroId.Value);

        if (BotBrain.IsLocalHuman(this))
        {
            SetNameServerRpc(PreferredName);
            RequestHeroServerRpc(PreferredHero);
        }
    }

    public override void OnNetworkDespawn()
    {
        heroId.OnValueChanged -= OnHeroChanged;
        ultCharge.OnValueChanged -= OnUltChargeChanged;
        isHidden.OnValueChanged -= OnHiddenChanged;
        boosted.OnValueChanged -= OnBoostedChanged;
        NanoAura.Set(transform, false);
        health.OnDeath -= OnDeath;
        health.currentHealth.OnValueChanged -= OnHealthChanged;
    }

    public void SelectHero(int index)
    {
        PreferredHero = index;
        if (IsOwner && IsSpawned)
            RequestHeroServerRpc(index);
    }

    // Vlastnik: dokud si vybira, stoji vysoko nad mapou; po vstupu do hry se objevi na spawnu sveho tymu.
    void Update()
    {
        if (IsServer && IsSpawned)
        {
            ServerCheckFall();

            if (revealed.Value && Time.time >= revealUntil)
                revealed.Value = false;

            if (boosted.Value && (Time.time >= boostUntil || health.currentHealth.Value <= 0f))
                boosted.Value = false;
        }

        // Server: ultimatka se pomalu nabiji i sama (jen zivemu hraci behem zapasu).
        if (IsServer && IsSpawned && UsesUltCharge && !joining.Value && health.currentHealth.Value > 0f && ultCharge.Value < UltCost)
            ServerAddUltCharge(UltPassivePerSecond * Time.deltaTime);

        if (!IsOwner || !IsSpawned) return;

        TickIdleLines();

        if (joining.Value && !parked)
        {
            parked = true;
            MoveTo(new Vector3(OwnerClientId * 6f, 400f, 0f));
        }
        else if (!joining.Value && parked)
        {
            parked = false;
            var respawn = GetComponent<PlayerRespawn>();
            if (respawn != null)
                respawn.ResetToSpawn();
        }
        else if (parked && transform.position.y < 350f)
        {
            // Nekdo jiny hrace presunul (reset kola): vratit zpet mimo mapu.
            MoveTo(new Vector3(OwnerClientId * 6f, 400f, 0f));
        }
    }

    void MoveTo(Vector3 position)
    {
        var body = GetComponent<CharacterController>();
        if (body != null) body.enabled = false;
        transform.position = position;
        if (body != null) body.enabled = true;
    }

    // Tlacitko "Vstoupit do hry" v lobby.
    public void ConfirmJoin()
    {
        if (IsOwner && IsSpawned && joining.Value)
            ConfirmJoinServerRpc();
    }

    [ServerRpc]
    void ConfirmJoinServerRpc()
    {
        if (!joining.Value) return;

        joining.Value = false;
        health.ResetHealth();
    }

    // Vola MatchManager pri startu / restartu zapasu a navratu do lobby: vsichni jsou zase normalne ve hre.
    public void ServerClearJoining()
    {
        if (IsServer && joining.Value)
            joining.Value = false;
    }

    [ServerRpc]
    void SetNameServerRpc(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = $"Hráč {OwnerClientId}";

        if (name.Length > 20)
            name = name.Substring(0, 20);

        playerName.Value = new FixedString32Bytes(name);
    }

    // Zmena hrdiny behem zapasu (F1): skore zustava, nabiti ultimatky se nuluje, hrac se objevi na zakladne.
    public void SwapHero(int index)
    {
        if (IsOwner && IsSpawned)
            SwapHeroServerRpc(index);
    }

    [ServerRpc]
    void SwapHeroServerRpc(int index)
    {
        var match = MatchManager.Instance;
        if (match == null || match.IsLobby || match.IsOver || joining.Value) return;
        if (HeroRegistry.Get(index) == null || index == heroId.Value) return;

        // Zivy hrac musi stat na zakladne sveho tymu (mrtvy ceka na oziveni, ten muze).
        var team = GetComponent<PlayerTeam>();
        bool alive = health.currentHealth.Value > 0f;
        if (alive && team != null && !SpawnZone.Contains(team.teamId.Value, transform.position)) return;

        // Zmena hodnoty spusti Apply u vsech (zbran, schopnosti, model; zivemu hraci plne zdravi noveho hrdiny).
        heroId.Value = index;
        ultCharge.Value = 0f;

        // Mrtvy hrac se ozivi beznym zpusobem uz jako novy hrdina (spawn hlasku rekne pri oziveni).
        if (health.currentHealth.Value > 0f)
        {
            SwapDoneClientRpc();
            Say(VoiceKind.Spawn);   // Apply uz probehlo, takze mluvi novy hrdina
        }
    }

    [ClientRpc]
    void SwapDoneClientRpc()
    {
        if (!IsOwner) return;

        var respawn = GetComponent<PlayerRespawn>();
        if (respawn != null)
            respawn.ResetToSpawn();
    }

    [ServerRpc]
    void RequestHeroServerRpc(int index)
    {
        // Behem zapasu se hrdina menit nedá (jen prvni vyber pri pripojeni).
        bool alreadyChosen = heroId.Value >= 0;
        if (alreadyChosen && !joining.Value && MatchManager.Instance != null && !MatchManager.Instance.IsLobby)
            return;

        if (HeroRegistry.Get(index) == null)
            index = 0;

        heroId.Value = index;
    }

    void OnHeroChanged(int previous, int current)
    {
        Apply(current);
    }

    void Apply(int index)
    {
        var definition = HeroRegistry.Get(index);
        if (definition == null) return;

        bool firstTime = Hero == null;
        Hero = definition;

        // Mrtvemu hraci (zmena hrdiny behem cekani na oziveni) se zdravi nevraci, to udela az oziveni.
        health.maxHealth = definition.maxHealth;
        // (snizeni zdravi na mensi maximum noveho hrdiny neni zasah - zadna hurt hlaska)
        if (IsServer && (firstTime || health.currentHealth.Value > 0f))
        {
            applyingHero = true;
            health.ResetHealth();
            applyingHero = false;
        }

        var bodyRenderer = GetComponent<Renderer>();
        if (bodyRenderer != null)
            bodyRenderer.material.color = definition.color;

        var visual = GetComponent<CharacterVisual>();
        if (visual == null)
            visual = gameObject.AddComponent<CharacterVisual>();
        visual.SetModel(definition.characterPrefab, definition.tintCharacter ? definition.color : Color.white);

        if (shooting != null)
            shooting.SetWeapon(definition.weapon);

        // Uskok muze byt na Q (puvodne) nebo na Shiftu (Viktor).
        bool dashOnShift = definition.secondaryAbilityKind == AbilityKind.Dash && definition.secondaryAbility != null;
        if (dash != null)
        {
            dash.Configure(dashOnShift ? definition.secondaryAbility : definition.ability, dashOnShift);
            dash.enabled = dashOnShift || definition.abilityKind == AbilityKind.Dash;
        }

        if (healField != null)
        {
            healField.Configure(definition.altAbility);
            healField.enabled = definition.altAbilityKind == AbilityKind.HealField && definition.altAbility != null;
        }

        if (flash != null)
        {
            flash.Configure(definition.rmbAbility);
            flash.enabled = definition.rmbAbilityKind == AbilityKind.Flash && definition.rmbAbility != null;
        }

        if (scout != null)
        {
            scout.Configure(definition.altAbility);
            scout.enabled = definition.altAbilityKind == AbilityKind.ScoutArrow && definition.altAbility != null;
        }

        if (rapidFire != null)
        {
            rapidFire.Configure(definition.rmbAbility);
            rapidFire.enabled = definition.rmbAbilityKind == AbilityKind.RapidFire && definition.rmbAbility != null;
        }

        if (storm != null)
        {
            storm.Configure(definition.ability);
            storm.enabled = definition.abilityKind == AbilityKind.Storm && definition.ability != null;
        }

        if (hook != null)
        {
            hook.Configure(definition.altAbility);
            hook.enabled = definition.altAbilityKind == AbilityKind.Hook && definition.altAbility != null;
        }

        if (visor != null)
        {
            visor.Configure(definition.ability);
            visor.enabled = definition.abilityKind == AbilityKind.Visor && definition.ability != null;
        }

        if (leap != null)
        {
            leap.Configure(definition.ability);
            leap.enabled = definition.abilityKind == AbilityKind.LeapStrike;
        }

        bool hasRush = definition.secondaryAbilityKind == AbilityKind.Rush && definition.secondaryAbility != null;
        if (rush != null)
        {
            rush.Configure(definition.secondaryAbility);
            rush.enabled = hasRush;
        }

        if (block != null)
        {
            block.Configure(definition.blockAbility);
            block.enabled = definition.blockAbility != null;
        }

        bool hasMine = definition.secondaryAbilityKind == AbilityKind.Mine && definition.secondaryAbility != null;
        if (mine != null)
        {
            mine.Configure(definition.secondaryAbility);
            mine.enabled = hasMine;
        }

        if (trap != null)
        {
            trap.Configure(definition.altAbility);
            trap.enabled = definition.altAbilityKind == AbilityKind.Trap && definition.altAbility != null;
        }

        if (boulder != null)
        {
            boulder.Configure(definition.ability);
            boulder.enabled = definition.abilityKind == AbilityKind.Boulder && definition.ability != null;
        }

        bool hasSleepDart = definition.secondaryAbilityKind == AbilityKind.SleepDart && definition.secondaryAbility != null;
        if (sleepDart != null)
        {
            sleepDart.Configure(definition.secondaryAbility);
            sleepDart.enabled = hasSleepDart;
        }

        if (bioGrenade != null)
        {
            bioGrenade.Configure(definition.altAbility);
            bioGrenade.enabled = definition.altAbilityKind == AbilityKind.BioticGrenade && definition.altAbility != null;
        }

        if (nano != null)
        {
            nano.Configure(definition.ability);
            nano.enabled = definition.abilityKind == AbilityKind.NanoBoost && definition.ability != null;
        }

        bool hasGrapple = definition.secondaryAbilityKind == AbilityKind.Grapple && definition.secondaryAbility != null;
        if (grapple != null)
        {
            grapple.Configure(definition.secondaryAbility);
            grapple.enabled = hasGrapple;
        }

        if (kick != null)
        {
            kick.Configure(definition.altAbility);
            kick.enabled = definition.altAbilityKind == AbilityKind.Kick && definition.altAbility != null;
        }

        if (infra != null)
        {
            infra.Configure(definition.ability);
            infra.enabled = definition.abilityKind == AbilityKind.Infra && definition.ability != null;
        }

        bool hasBlink = definition.secondaryAbilityKind == AbilityKind.Blink && definition.secondaryAbility != null;
        if (blink != null)
        {
            blink.Configure(definition.secondaryAbility);
            blink.enabled = hasBlink;
        }

        if (recall != null)
        {
            recall.Configure(definition.altAbility);
            recall.enabled = definition.altAbilityKind == AbilityKind.Recall && definition.altAbility != null;
        }

        if (pulseBomb != null)
        {
            pulseBomb.Configure(definition.ability);
            pulseBomb.enabled = definition.abilityKind == AbilityKind.PulseBomb && definition.ability != null;
        }

        bool hasBard = definition.secondaryAbilityKind == AbilityKind.Crossfade && definition.secondaryAbility != null;
        if (bard != null)
        {
            bard.Configure(hasBard ? definition.secondaryAbility : null,
                definition.altAbilityKind == AbilityKind.Amp ? definition.altAbility : null,
                definition.abilityKind == AbilityKind.Concert ? definition.ability : null,
                definition.rmbAbilityKind == AbilityKind.Soundwave ? definition.rmbAbility : null);
            bard.enabled = hasBard;
        }

        bool hasSindelLeap = definition.secondaryAbilityKind == AbilityKind.RighteousLeap && definition.secondaryAbility != null;
        bool hasSindelGrenade = definition.altAbilityKind == AbilityKind.HolyGrenade && definition.altAbility != null;
        if (sindel != null)
        {
            sindel.Configure(hasSindelLeap ? definition.secondaryAbility : null, hasSindelGrenade ? definition.altAbility : null);
            sindel.enabled = hasSindelLeap || hasSindelGrenade;
        }

        if (ezekiel != null)
        {
            ezekiel.Configure(definition.ability);
            ezekiel.enabled = definition.abilityKind == AbilityKind.Ezekiel && definition.ability != null;
        }

        bool hasMaxReap = definition.secondaryAbilityKind == AbilityKind.MaxReap && definition.secondaryAbility != null;
        bool hasMaxStep = definition.altAbilityKind == AbilityKind.ShadowStep && definition.altAbility != null;
        bool hasMaxSlash = definition.rmbAbilityKind == AbilityKind.MaxSlash && definition.rmbAbility != null;
        if (max != null)
        {
            max.Configure(hasMaxReap ? definition.secondaryAbility : null, hasMaxStep ? definition.altAbility : null,
                hasMaxSlash ? definition.rmbAbility : null);
            max.enabled = hasMaxReap || hasMaxStep || hasMaxSlash;
        }

        if (trespass != null)
        {
            trespass.Configure(definition.ability);
            trespass.enabled = definition.abilityKind == AbilityKind.Trespass && definition.ability != null;
        }

        var controller = GetComponent<FirstPersonController>();
        if (controller != null)
        {
            controller.ShiftReserved = hasMaxReap || hasRush || hasMine || dashOnShift || hasSleepDart || hasGrapple || hasBlink || hasBard || hasSindelLeap;
            controller.DoubleJump = definition.doubleJump;
            controller.HeroSpeedScale = Mathf.Max(0.5f, definition.moveSpeed);
            controller.LedgeClimb = definition.ledgeClimb;
        }

        if (firstTime)
        {
            ProceduralSfx.Play(ProceduralSfx.Spawn, transform.position, 0.5f);
            // Spawn hlaska jen v rozbehnutem zapase (v lobby ne; na startu zapasu ji spusti MatchManager).
            var match = MatchManager.Instance;
            if (IsServer && match != null && !match.IsLobby && !match.IsOver)
                Say(VoiceKind.Spawn);
        }
    }

    public struct AbilitySlot
    {
        public string key;
        public AbilityDefinition ability;
        public float remaining;   // zbyvajici cooldown v sekundach
        public bool active;       // schopnost prave bezi
        public float charge;      // 0-1 u schopnosti s barem (blok), jinak -1
        public int count;         // pocet naboju (naloz), 0 = nezobrazovat
        public bool fullOnly;     // schopnost jde pouzit az pri plnem nabiti (ultimatka)
    }

    // Slot ultimatky: bud nabijena hrou (procenta), nebo na cooldown.
    AbilitySlot UltSlot(float cooldownRemaining, bool active)
    {
        return UsesUltCharge
            ? new AbilitySlot { key = "Q", ability = Hero.ability, remaining = 0f, active = active, charge = UltFraction, fullOnly = true }
            : new AbilitySlot { key = "Q", ability = Hero.ability, remaining = cooldownRemaining, active = active, charge = -1f };
    }

    // Schopnosti hrdiny pro HUD, v poradi zprava doleva (ultimatni na Q prvni).
    public void GetAbilitySlots(System.Collections.Generic.List<AbilitySlot> slots)
    {
        slots.Clear();
        if (Hero == null) return;

        if (Hero.abilityKind == AbilityKind.LeapStrike && leap != null && Hero.ability != null)
            slots.Add(UltSlot(leap.CooldownRemaining, leap.IsActive));
        else if (Hero.abilityKind == AbilityKind.Dash && dash != null && Hero.ability != null)
            slots.Add(new AbilitySlot { key = "Q", ability = Hero.ability, remaining = dash.CooldownRemaining, active = dash.IsActive, charge = -1f });
        else if (Hero.abilityKind == AbilityKind.Boulder && boulder != null && Hero.ability != null)
            slots.Add(UltSlot(boulder.CooldownRemaining, boulder.IsActive));

        else if (Hero.abilityKind == AbilityKind.Visor && visor != null && Hero.ability != null)
            slots.Add(UltSlot(visor.CooldownRemaining, visor.IsActive));

        else if (Hero.abilityKind == AbilityKind.Storm && storm != null && Hero.ability != null)
            slots.Add(UltSlot(storm.CooldownRemaining, false));

        else if (Hero.abilityKind == AbilityKind.NanoBoost && nano != null && Hero.ability != null)
            slots.Add(UltSlot(nano.CooldownRemaining, false));

        else if (Hero.abilityKind == AbilityKind.Infra && infra != null && Hero.ability != null)
            slots.Add(UltSlot(infra.CooldownRemaining, infra.IsActive));

        else if (Hero.abilityKind == AbilityKind.PulseBomb && pulseBomb != null && Hero.ability != null)
            slots.Add(UltSlot(pulseBomb.CooldownRemaining, false));

        else if (Hero.abilityKind == AbilityKind.Concert && bard != null && Hero.ability != null)
            slots.Add(UltSlot(bard.UltRemaining, false));

        else if (Hero.abilityKind == AbilityKind.Ezekiel && ezekiel != null && Hero.ability != null)
            slots.Add(UltSlot(ezekiel.CooldownRemaining, ezekiel.IsCasting));

        else if (Hero.abilityKind == AbilityKind.Trespass && trespass != null && Hero.ability != null)
            slots.Add(UltSlot(trespass.CooldownRemaining, trespass.IsActive));

        if (Hero.rmbAbilityKind == AbilityKind.MaxSlash && max != null && Hero.rmbAbility != null)
            slots.Add(new AbilitySlot { key = "PTM", ability = Hero.rmbAbility, remaining = max.SlashRemaining, active = false, charge = -1f });
        if (Hero.altAbilityKind == AbilityKind.ShadowStep && max != null && Hero.altAbility != null)
            slots.Add(new AbilitySlot { key = "E", ability = Hero.altAbility, remaining = max.StepRemaining, active = false, charge = -1f });
        if (Hero.secondaryAbilityKind == AbilityKind.MaxReap && max != null && Hero.secondaryAbility != null)
            slots.Add(new AbilitySlot { key = "SHIFT", ability = Hero.secondaryAbility, remaining = max.ReapRemaining, active = max.Busy, charge = -1f });

        if (Hero.altAbilityKind == AbilityKind.HolyGrenade && sindel != null && Hero.altAbility != null)
            slots.Add(new AbilitySlot { key = "E", ability = Hero.altAbility, remaining = sindel.GrenadeRemaining, active = false, charge = -1f });

        if (Hero.secondaryAbilityKind == AbilityKind.RighteousLeap && sindel != null && Hero.secondaryAbility != null)
            slots.Add(new AbilitySlot { key = "SHIFT", ability = Hero.secondaryAbility, remaining = sindel.LeapRemaining, active = false, charge = -1f });

        if (Hero.altAbilityKind == AbilityKind.Amp && bard != null && Hero.altAbility != null)
            slots.Add(new AbilitySlot { key = "E", ability = Hero.altAbility, remaining = bard.AmpRemaining, active = bard.Amped, charge = -1f });

        if (Hero.rmbAbilityKind == AbilityKind.Soundwave && bard != null && Hero.rmbAbility != null)
            slots.Add(new AbilitySlot { key = "PTM", ability = Hero.rmbAbility, remaining = bard.WaveRemaining, active = false, charge = -1f });

        if (Hero.secondaryAbilityKind == AbilityKind.Crossfade && bard != null && Hero.secondaryAbility != null)
            slots.Add(new AbilitySlot { key = "SHIFT", ability = Hero.secondaryAbility, remaining = bard.ToggleRemaining, active = bard.SpeedMode, charge = -1f });

        if (Hero.altAbilityKind == AbilityKind.Recall && recall != null && Hero.altAbility != null)
            slots.Add(new AbilitySlot { key = "E", ability = Hero.altAbility, remaining = recall.CooldownRemaining, active = recall.IsActive, charge = -1f });

        if (Hero.secondaryAbilityKind == AbilityKind.Blink && blink != null && Hero.secondaryAbility != null)
            slots.Add(new AbilitySlot { key = "SHIFT", ability = Hero.secondaryAbility, remaining = blink.CooldownRemaining, active = false, charge = -1f, count = blink.Charges });

        if (Hero.altAbilityKind == AbilityKind.Kick && kick != null && Hero.altAbility != null)
            slots.Add(new AbilitySlot { key = "E", ability = Hero.altAbility, remaining = kick.CooldownRemaining, active = false, charge = -1f });

        if (Hero.secondaryAbilityKind == AbilityKind.Grapple && grapple != null && Hero.secondaryAbility != null)
            slots.Add(new AbilitySlot { key = "SHIFT", ability = Hero.secondaryAbility, remaining = grapple.CooldownRemaining, active = false, charge = -1f });

        if (Hero.altAbilityKind == AbilityKind.BioticGrenade && bioGrenade != null && Hero.altAbility != null)
            slots.Add(new AbilitySlot { key = "E", ability = Hero.altAbility, remaining = bioGrenade.CooldownRemaining, active = false, charge = -1f });

        if (Hero.secondaryAbilityKind == AbilityKind.SleepDart && sleepDart != null && Hero.secondaryAbility != null)
            slots.Add(new AbilitySlot { key = "SHIFT", ability = Hero.secondaryAbility, remaining = sleepDart.CooldownRemaining, active = false, charge = -1f });

        if (Hero.rmbAbilityKind == AbilityKind.RapidFire && rapidFire != null && Hero.rmbAbility != null)
            slots.Add(new AbilitySlot { key = "PTM", ability = Hero.rmbAbility, remaining = rapidFire.CooldownRemaining, active = rapidFire.IsActive, charge = -1f, count = rapidFire.ShotsLeft });

        if (Hero.altAbilityKind == AbilityKind.ScoutArrow && scout != null && Hero.altAbility != null)
            slots.Add(new AbilitySlot { key = "E", ability = Hero.altAbility, remaining = scout.CooldownRemaining, active = scout.IsActive, charge = -1f });

        if (Hero.rmbAbilityKind == AbilityKind.Flash && flash != null && Hero.rmbAbility != null)
            slots.Add(new AbilitySlot { key = "PTM", ability = Hero.rmbAbility, remaining = flash.CooldownRemaining, active = false, charge = -1f });

        if (Hero.altAbilityKind == AbilityKind.HealField && healField != null && Hero.altAbility != null)
            slots.Add(new AbilitySlot { key = "E", ability = Hero.altAbility, remaining = healField.CooldownRemaining, active = healField.IsActive, charge = -1f });

        if (Hero.secondaryAbilityKind == AbilityKind.Dash && dash != null && Hero.secondaryAbility != null)
            slots.Add(new AbilitySlot { key = "SHIFT", ability = Hero.secondaryAbility, remaining = dash.CooldownRemaining, active = dash.IsActive, charge = -1f });

        if (Hero.blockAbility != null && block != null)
            slots.Add(new AbilitySlot { key = "PTM", ability = Hero.blockAbility, remaining = 0f, active = block.IsBlocking, charge = block.Fraction });

        if (Hero.secondaryAbilityKind == AbilityKind.Rush && rush != null && Hero.secondaryAbility != null)
            slots.Add(new AbilitySlot { key = "SHIFT", ability = Hero.secondaryAbility, remaining = rush.CooldownRemaining, active = rush.IsActive, charge = -1f, count = rush.HasSeveralCharges ? rush.Charges : 0 });

        if (Hero.altAbilityKind == AbilityKind.Hook && hook != null && Hero.altAbility != null)
            slots.Add(new AbilitySlot { key = "E", ability = Hero.altAbility, remaining = hook.CooldownRemaining, active = false, charge = -1f });

        if (Hero.altAbilityKind == AbilityKind.Trap && trap != null && Hero.altAbility != null)
            slots.Add(new AbilitySlot { key = "E", ability = Hero.altAbility, remaining = trap.CooldownRemaining, active = false, charge = -1f });

        if (Hero.secondaryAbilityKind == AbilityKind.Mine && mine != null && Hero.secondaryAbility != null)
            slots.Add(new AbilitySlot { key = "SHIFT", ability = Hero.secondaryAbility, remaining = mine.CooldownRemaining, active = mine.IsActive && mine.Charges > 0, charge = -1f, count = mine.Charges });
    }

    public string AbilityStatus()
    {
        if (Hero == null) return "";

        string primary = StatusOf(Hero.abilityKind);
        string secondary = StatusOf(Hero.secondaryAbilityKind);
        if (primary.Length > 0 && secondary.Length > 0)
            return primary + "    " + secondary;

        return primary.Length > 0 ? primary : secondary;
    }

    string StatusOf(AbilityKind kind)
    {
        switch (kind)
        {
            case AbilityKind.Dash: return dash != null ? dash.StatusText() : "";
            case AbilityKind.LeapStrike: return leap != null ? leap.StatusText() : "";
            case AbilityKind.Rush: return rush != null ? rush.StatusText() : "";
            default: return "";
        }
    }

    float nextHurtSound;
    bool applyingHero;

    // Zvuk zasahu slysi vsichni v okoli (ne casteji nez jednou za chvili, aby prubezne poskozeni nedrncelo).
    void OnHealthChanged(float previous, float current)
    {
        if (current >= previous - 0.01f || current <= 0f || Time.time < nextHurtSound || applyingHero) return;

        // Zmena hrdiny na nekoho s mensim maximem zdravi: zdravi spadne na plne nove maximum - to neni zasah.
        var heroNow = HeroRegistry.Get(heroId.Value);
        if (heroNow != null && current >= heroNow.maxHealth - 0.01f) return;

        // Bez nahravek: kratky generovany zvuk zasahu u vsech. S nahravkami: hlaska nejvys jednou za par sekund.
        if (!HeroVoice.HasLines(Hero, VoiceKind.Hurt, 0))
        {
            nextHurtSound = Time.time + 0.35f;
            ProceduralSfx.Play(ProceduralSfx.Hurt, transform.position, 0.7f);
        }
        else if (IsServer)
        {
            nextHurtSound = Time.time + 3f;
            Say(VoiceKind.Hurt);
        }
    }

    // ---------------- hlasky ----------------

    // Prehraje hlasku u vsech hracu (nahodny vyber dela server). Volat na serveru nebo na vlastnikovi hrace.
    // phase > 0: konkretni faze schopnosti (nahravka step_<phase>), jinak nahodna hlaska.
    public void Say(VoiceKind kind, int slot = 0, int phase = 0)
    {
        if (!IsSpawned || !HeroVoice.HasLines(Hero, kind, slot)) return;

        if (IsServer)
            SayClientRpc((int)kind, slot, PickFor(kind, slot, phase));
        else if (IsOwner)
            SayServerRpc((int)kind, slot, phase);
    }

    // Hlaska ke schopnosti (vola schopnost na vlastnikovi, kdyz se spusti). Viceprazova schopnost posila cislo faze.
    public void SayAbility(AbilityDefinition ability, int phase = 0)
    {
        int slot = HeroVoice.SlotOf(Hero, ability);
        if (slot >= 0)
            Say(VoiceKind.Ability, slot, phase);
    }

    // Zaporne cislo = faze (vsichni prehraji stejnou nahravku), jinak nahodny vyber, ktery nikdy nezopakuje
    // stejnou hlasku dvakrat po sobe (pri dvou nahravkach se tedy pravidelne stridaji). Vola jen server.
    readonly System.Collections.Generic.Dictionary<int, int> lastPick = new System.Collections.Generic.Dictionary<int, int>();

    int PickFor(VoiceKind kind, int slot, int phase)
    {
        if (phase > 0) return -phase;

        int count = HeroVoice.NormalLineCount(Hero, kind, slot);
        if (count <= 1) return Random.Range(0, 100000);

        int key = (int)kind * 10 + slot;
        int pick = lastPick.TryGetValue(key, out int last)
            ? (last + 1 + Random.Range(0, count - 1)) % count
            : Random.Range(0, count);
        lastPick[key] = pick;
        return pick;
    }

    [ServerRpc]
    void SayServerRpc(int kind, int slot, int phase)
    {
        if (kind < 0 || kind > (int)VoiceKind.Snare) return;
        SayClientRpc(kind, slot, PickFor((VoiceKind)kind, slot, Mathf.Clamp(phase, 0, 9)));
    }

    [ClientRpc]
    void SayClientRpc(int kind, int slot, int pick)
    {
        if (voice != null)
            voice.Play(Hero, (VoiceKind)kind, slot, pick);
    }

    // Nahodna hlaska pri chozeni (vlastnik): jednou za 25-50 s chuze.
    float walkTime;
    float nextIdleLine = 30f;
    Vector3 lastWalkPosition;

    void TickIdleLines()
    {
        Vector3 position = transform.position;
        Vector3 delta = position - lastWalkPosition;
        lastWalkPosition = position;
        delta.y = 0f;

        var match = MatchManager.Instance;
        bool playing = match != null && !match.IsLobby && !match.IsOver && health.currentHealth.Value > 0f;
        float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
        if (!playing || speed < 1.5f || speed > 30f) return;

        walkTime += Time.deltaTime;
        if (walkTime < nextIdleLine) return;

        walkTime = 0f;
        nextIdleLine = Random.Range(25f, 50f);
        Say(VoiceKind.Idle);
    }

    // ---------------- potvrzeni zasahu (krizek u zamerovace, lebka pri zabiti) ----------------

    // Vola Combat na serveru, kdyz tenhle hrac nekomu zpusobil poskozeni.
    public void ServerNotifyHit(bool kill)
    {
        if (IsServer && IsSpawned)
            HitClientRpc(kill);
    }

    [ClientRpc]
    void HitClientRpc(bool kill)
    {
        if (BotBrain.IsLocalHuman(this))
            HudUI.NotifyHit(kill);
    }

    void OnBoostedChanged(bool previous, bool current)
    {
        NanoAura.Set(transform, current);
        if (current && BotBrain.IsLocalHuman(this))
            HudUI.NotifyTint(new Color(0.45f, 0.8f, 1f, 0.45f), 0.6f);
    }

    // Pricina smrti bez vraha (auto, pad, kotel...) - nastavuje zdroj tesne pred zabitim; pro kill feed.
    public string ServerDeathCause { get; set; } = "";
    public int ServerKillReportedFrame { get; set; } = -1;

    // Kdyz smrt nikdo nezpusobil (zadne ReportKill v tomhle snimku), ukaze se v kill feedu s pricinou.
    System.Collections.IEnumerator EnvironmentDeathFeed(int frame)
    {
        yield return null;
        var match = MatchManager.Instance;
        if (match != null && !match.IsLobby && ServerKillReportedFrame < frame)
            match.ServerEnvironmentDeath(this, ServerDeathCause);
        ServerDeathCause = "";
    }

    void OnDeath()
    {
        ProceduralSfx.Play(ProceduralSfx.Death, transform.position, 0.8f);
        if (IsServer)
        {
            Say(VoiceKind.Death);
            StartCoroutine(EnvironmentDeathFeed(Time.frameCount));

            var match = MatchManager.Instance;
            if (match != null && !match.IsLobby && !match.IsOver)
                deaths.Value++;
        }

        if (IsServer && Hero != null && Hero.deathGrenades > 0)
            StartCoroutine(DeathGrenades());
    }

    // Pasivni schopnost (Honza): po smrti z nej vypadnou granaty, ktere za chvili vybuchnou.
    const float DeathGrenadeDelay = 1.1f;

    System.Collections.IEnumerator DeathGrenades()
    {
        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) yield break;

        int count = Mathf.Clamp(Hero.deathGrenades, 1, 8);
        Vector3 body = transform.position;
        var points = new Vector3[count];
        float turn = Random.value * Mathf.PI * 2f;

        for (int i = 0; i < count; i++)
        {
            float angle = turn + i * Mathf.PI * 2f / count;
            Vector3 point = body + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Random.Range(0.9f, 2.1f);

            // Granat dopadne na zem pod tim mistem (kdyz je v ceste zed, zustane u tela).
            if (Physics.Linecast(body + Vector3.up, point + Vector3.up, ~0, QueryTriggerInteraction.Ignore))
                point = body;
            foreach (var ground in Physics.RaycastAll(point + Vector3.up, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (ground.collider.GetComponentInParent<NetworkObject>() != null) continue;
                point = ground.point;
                break;
            }

            points[i] = point;
        }

        float radius = Hero.deathGrenadeRadius;
        float damage = Hero.deathGrenadeDamage;
        DeathGrenadesClientRpc(body + Vector3.up, points, radius);

        yield return new WaitForSeconds(DeathGrenadeDelay);

        foreach (var point in points)
            Combat.Explode(gameObject, point + Vector3.up * 0.2f, radius, damage, 0.4f, null, "weapon:explosion");
    }

    [ClientRpc]
    void DeathGrenadesClientRpc(Vector3 from, Vector3[] points, float radius)
    {
        foreach (var point in points)
            DeathGrenadeVisual.Spawn(from, point, DeathGrenadeDelay, radius);
    }

    // Vola server, kdyz tenhle hrac nekoho zabil.
    public void NotifyKill()
    {
        if (!IsServer) return;
        Say(VoiceKind.Kill);
    }
}
