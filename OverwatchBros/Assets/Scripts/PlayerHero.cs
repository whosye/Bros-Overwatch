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

    public HeroDefinition Hero { get; private set; }
    public string DisplayName => playerName.Value.Length > 0 ? playerName.Value.ToString() : $"Hráč {OwnerClientId}";

    Health health;
    WeaponShooting shooting;
    DashAbility dash;
    LeapStrikeAbility leap;
    HeroVoice voice;

    void Awake()
    {
        health = GetComponent<Health>();
        shooting = GetComponent<WeaponShooting>();
        dash = GetComponent<DashAbility>();
        leap = GetComponent<LeapStrikeAbility>();
        voice = GetComponent<HeroVoice>();
    }

    public override void OnNetworkSpawn()
    {
        heroId.OnValueChanged += OnHeroChanged;
        health.OnDeath += OnDeath;

        if (heroId.Value >= 0)
            Apply(heroId.Value);

        if (IsOwner)
        {
            SetNameServerRpc(PreferredName);
            RequestHeroServerRpc(PreferredHero);
        }
    }

    public override void OnNetworkDespawn()
    {
        heroId.OnValueChanged -= OnHeroChanged;
        health.OnDeath -= OnDeath;
    }

    public void SelectHero(int index)
    {
        PreferredHero = index;
        if (IsOwner && IsSpawned)
            RequestHeroServerRpc(index);
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

    [ServerRpc]
    void RequestHeroServerRpc(int index)
    {
        // Behem zapasu se hrdina menit nedá (jen prvni vyber pri pripojeni).
        bool alreadyChosen = heroId.Value >= 0;
        if (alreadyChosen && MatchManager.Instance != null && !MatchManager.Instance.IsLobby)
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

        health.maxHealth = definition.maxHealth;
        if (IsServer)
            health.ResetHealth();

        var bodyRenderer = GetComponent<Renderer>();
        if (bodyRenderer != null)
            bodyRenderer.material.color = definition.color;

        if (shooting != null)
            shooting.SetWeapon(definition.weapon);

        if (dash != null)
        {
            dash.Configure(definition.ability);
            dash.enabled = definition.abilityKind == AbilityKind.Dash;
        }

        if (leap != null)
        {
            leap.Configure(definition.ability);
            leap.enabled = definition.abilityKind == AbilityKind.LeapStrike;
        }

        if (firstTime)
        {
            ProceduralSfx.Play(ProceduralSfx.Spawn, transform.position, 0.5f);
            if (voice != null)
                voice.PlaySpawn(definition);
        }
    }

    public string AbilityStatus()
    {
        if (Hero == null) return "";

        switch (Hero.abilityKind)
        {
            case AbilityKind.Dash: return dash != null ? dash.StatusText() : "";
            case AbilityKind.LeapStrike: return leap != null ? leap.StatusText() : "";
            default: return "";
        }
    }

    void OnDeath()
    {
        ProceduralSfx.Play(ProceduralSfx.Death, transform.position, 0.8f);
        if (voice != null)
            voice.PlayDeath(Hero);
    }

    // Vola server, kdyz tenhle hrac nekoho zabil.
    public void NotifyKill()
    {
        if (!IsServer) return;
        KillClientRpc();
    }

    [ClientRpc]
    void KillClientRpc()
    {
        if (voice != null)
            voice.PlayKill(Hero);
    }
}
