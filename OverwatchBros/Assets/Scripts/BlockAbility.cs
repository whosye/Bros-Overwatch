using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Blok pravym tlacitkem (Ayran: zkrizene sekery). Drzeni tlacitka zablokuje 'blockAbsorb' (80 %) prichoziho poskozeni,
// pohyb se zpomali na 'blockSpeed' (50 %). Zablokovana cast poskozeni ubira z baru, jehoz velikost je plne zdravi hrdiny;
// kdyz se bar vycerpa, blok prestane platit. Bar se zacne sam obnovovat 'regenDelay' s po poslednim poskozeni.
public class BlockAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    static readonly Color SparkColor = new Color(0.85f, 0.9f, 1f, 1f);

    FirstPersonController fpc;
    Health health;

    bool active;
    float nextVoiceTime;
    float lastDamageTime = -100f;
    float nextFxTime;

    readonly NetworkVariable<bool> blocking = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    readonly NetworkVariable<float> energy = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ---- Odveta (Pova, HeroDefinition.counterAbility): zablokovane poskozeni plni hnev (0-MaxRage) a sekyrky zhnou.
    // Nabite sekyrky drzi energii, dokud Pova nezasahne nepritele: prvni sek levym, ktery nekoho trefi, ji uvolni -
    // uder do krize za 'counter.power' + RageDamage x hnev, nepratele odhodi, hnev (a zhnuti sekyrek) spadne na 0.
    // Sek do prazdna energii nespotrebuje.
    public AbilityDefinition counter;
    public const float MaxRage = 100f;
    const float MinRage = 10f;            // mene hnevu = Odveta nejde
    const float RageGain = 0.7f;          // kolik hnevu da 1 bod vyblokovaneho poskozeni (plny hnev = ~143 vyblokovano)
    public const float RageDamage = 0.7f;       // bonusove poskozeni za 1 bod hnevu (plny hnev = +70)
    const float CounterHalfAngle = 70f;
    const float CounterSwing = 0.45f;     // tak dlouho po Odvete nejde znovu blokovat (animace seku)
    static readonly Color RageColor = new Color(1f, 0.45f, 0.1f, 1f);

    readonly NetworkVariable<float> rage = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    float swingUntil;                // vlastnik: probiha sek Odvety
    bool readyCued, fullCued;
    float nextCounterVoice;
    float nextCounterServer;

    public float Rage => rage.Value;
    public bool HasCounter => enabled && counter != null;
    public bool CounterReady => HasCounter && rage.Value >= MinRage && Time.time >= counterCooldownUntil;
    // cooldown po uvolneni (counter.cooldown, napr. 5 s) - hnev se mezitim dal sbira, jen nejde uvolnit
    public float CounterCooldownRemaining => Mathf.Max(0f, counterCooldownUntil - Time.time);
    float counterCooldownUntil;   // vsichni klienti (nastavi CounterFxClientRpc), server ma serverCounterReadyAt
    float serverCounterReadyAt;
    float CounterCooldown => counter != null ? counter.Cooldown : 0f;
    // vlastnik: kliknuti levym ted spusti Odvetu (WeaponShooting pak nesekne obycejne)
    public bool CounterBusy => CounterWindow || Time.time < swingUntil;
    public bool CounterWindow => CounterReady && Time.time >= swingUntil;

    public void ConfigureCounter(AbilityDefinition definition)
    {
        counter = definition;
    }

    public bool IsBlocking => enabled && ability != null && blocking.Value;
    public float Energy => energy.Value;
    public float MaxEnergy => health != null ? health.maxHealth : 100f;
    public float Fraction => Mathf.Clamp01(energy.Value / Mathf.Max(1f, MaxEnergy));

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        health = GetComponent<Health>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            energy.Value = MaxEnergy;
            rage.Value = 0f;
        }
        blocking.OnValueChanged += OnBlockingChanged;
    }

    public override void OnNetworkDespawn()
    {
        blocking.OnValueChanged -= OnBlockingChanged;
    }

    void OnBlockingChanged(bool previous, bool now)
    {
    }

    void OnDisable()
    {
        if (active)
            StopOwner();
    }

    // Vola Health.ResetHealth (server): plny bar na zacatku zapasu, po respawnu a po zmene hrdiny.
    public void ResetEnergy()
    {
        if (!IsServer || !IsSpawned) return;

        energy.Value = MaxEnergy;
        lastDamageTime = -100f;
        rage.Value = 0f;
    }

    void Update()
    {
        if (IsOwner)
            OwnerUpdate();

        if (IsServer)
            ServerRegen();
    }

    void OwnerUpdate()
    {
        if (ability == null) return;

        // Odveta pripravena (hnev presahl minimum) / plny hnev: kratky zvuk jen pro hrace za Povu
        if (fpc.Bot == null && counter != null)
        {
            bool ready = CounterReady, full = rage.Value >= MaxRage * 0.98f;
            if (ready && !readyCued) Fx.PlayGlobal(ProceduralSfx.StunConfirm, 0.45f);
            if (full && !fullCued) Fx.PlayGlobal(ProceduralSfx.UltCharge, 0.35f);
            readyCued = ready;
            fullCued = full;
        }

        // Odveta: levym tlacitkem behem bloku nebo chvili po nem
        if (CounterWindow && HeroInput.Held(this, HeroInput.Key.LeftMouse) && HeroInput.Locked(this)
            && !fpc.InputBlocked && !fpc.RushActive && health.currentHealth.Value > 0f)
            StartCounter();

        bool wants = HeroInput.Held(this, HeroInput.Key.RightMouse) && HeroInput.Locked(this)
            && !fpc.InputBlocked && !fpc.RushActive && energy.Value > 0.01f && Time.time >= swingUntil;

        if (wants && !active)
            StartOwner();
        else if (!wants && active)
            StopOwner();
    }

    void StartCounter()
    {
        swingUntil = Time.time + CounterSwing;
        if (active) StopOwner();
        var eye = fpc.playerCamera != null ? fpc.playerCamera.transform : transform;
        Vector3 forward = eye.forward; forward.y = 0f;
        forward = forward.sqrMagnitude > 0.001f ? forward.normalized : transform.forward;
        PlayCounterSwing(rage.Value / MaxRage);
        CounterServerRpc(transform.position, forward);
        var hero = GetComponent<PlayerHero>();
        if (hero != null && counter != null && Time.time >= nextCounterVoice)
        {
            nextCounterVoice = Time.time + 6f;
            hero.SayAbility(counter);
        }
    }

    // animace a zvuk seku (vlastnik hned, ostatni pres server)
    void PlayCounterSwing(float strength)
    {
        if (TryGetComponent<HeldWeapons>(out var held)) held.Swing();
        ProceduralSfx.PlayForActor(this, ProceduralSfx.Dash, transform.position + Vector3.up, 0.5f + 0.4f * strength);
    }

    [ServerRpc]
    void CounterServerRpc(Vector3 position, Vector3 forward)
    {
        if (counter == null || !enabled || Time.time < nextCounterServer || Time.time < serverCounterReadyAt) return;
        if (rage.Value < MinRage || health.currentHealth.Value <= 0f) return;
        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;
        nextCounterServer = Time.time + CounterSwing * 0.8f;
        if ((position - transform.position).sqrMagnitude > 9f) position = transform.position;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.01f ? forward.normalized : transform.forward;

        float strength = rage.Value / MaxRage;
        float damage = counter.power + rage.Value * RageDamage;
        float reach = counter.range > 0.5f && counter.range < 10f ? counter.range : 3.4f;
        bool anyHit = false;

        Vector3 origin = position + Vector3.up;
        var hit = new System.Collections.Generic.HashSet<Health>();
        foreach (var col in Physics.OverlapSphere(origin, reach + 0.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 to = col.bounds.center - origin;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.magnitude > reach + 0.4f || (flat.magnitude > 0.5f && Vector3.Angle(forward, flat) > CounterHalfAngle)) continue;

            var dummy = col.GetComponentInParent<Target>();
            if (dummy != null) { dummy.TakeDamage(damage); anyHit = true; CounterHitClientRpc(col.bounds.center, strength); }
            var victim = col.GetComponentInParent<Health>();
            if (victim == null || victim == health || !hit.Add(victim) || victim.currentHealth.Value <= 0f) continue;
            if (Combat.SameTeam(gameObject, victim.gameObject)) continue;
            if (!Combat.HasLineOfSight(origin, col)) continue;
            Combat.DamagePlayer(gameObject, victim, damage, Combat.AbilitySource(counter));
            anyHit = true;
            if (victim.currentHealth.Value > 0f && victim.TryGetComponent<FirstPersonController>(out var controller))
            {
                Vector3 away = flat.sqrMagnitude > 0.01f ? flat.normalized : forward;
                controller.ServerKnockback(away * Mathf.Lerp(5f, 9f, strength) + Vector3.up * 2.5f);
            }
            var victimHero = victim.GetComponent<PlayerHero>();
            if (victimHero != null) victimHero.ServerNoteAttacker(gameObject);
            CounterHitClientRpc(col.bounds.center, strength);
        }
        // sek do prazdna: energie zustava v sekyrkach (bez krize), ostatni vidi jen mavnuti
        if (!anyHit) { CounterMissClientRpc(strength); return; }
        rage.Value = 0f;
        serverCounterReadyAt = Time.time + Mathf.Max(0f, CounterCooldown - 0.3f);   // (rezerva na zpozdeni site)
        CounterFxClientRpc(position, forward, strength);
    }

    [ClientRpc]
    void CounterFxClientRpc(Vector3 position, Vector3 forward, float strength)
    {
        counterCooldownUntil = Time.time + CounterCooldown;
        if (!IsOwner) PlayCounterSwing(strength);
        float reach = counter != null && counter.range > 0.5f && counter.range < 10f ? counter.range : 3.4f;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 feet = SlashGlow.Ground(position, 0f);
        Vector3 pivot = feet + Vector3.up * 1.25f;
        var rim = new Color(1f, 0.2f, 0f, 0f);

        // 1) VELKE ZHAVE X pred Povou (kolmo na pohled - z 1. osoby je videt cele): dva tahy cepeli pres sebe,
        //    kazdy = siroka zare + zhave jadro + bily brit. Druhy tah o chvilku pozdeji.
        Vector3 xCenter = pivot + forward * reach * 0.6f;
        float size = Mathf.Lerp(1.15f, 1.7f, strength);
        for (int i = 0; i < 2; i++)
        {
            float k = i == 0 ? 1f : -1f;
            Vector3 a = xCenter + (-right * k + Vector3.up) * size * 0.7f;
            Vector3 b = xCenter + (right * k - Vector3.up) * size * 0.7f;
            float delay = i * 0.09f;
            Stroke(a, b, forward, Mathf.Lerp(0.38f, 0.55f, strength), new Color(1f, 0.3f, 0.04f, Mathf.Lerp(0.55f, 0.8f, strength)), rim, 0.06f, 0.75f, delay);
            Stroke(a, b, forward, Mathf.Lerp(0.1f, 0.15f, strength), SlashGlow.Hot, new Color(1f, 0.45f, 0.1f, 0f), 0.06f, 0.65f, delay);
            Stroke(a, b, forward, 0.04f, new Color(1f, 0.97f, 0.9f, 1f), new Color(1f, 0.7f, 0.4f, 0f), 0.05f, 0.45f, delay);
            for (int s = 1; s <= 4; s++) Fx.Sparks(Vector3.Lerp(a, b, s / 5f), RageColor);
        }

        // 2) obloukove seky kolem Povy (doprovod - je videt hlavne z boku)
        float from = -CounterHalfAngle - 15f, to = CounterHalfAngle + 15f;
        for (int i = 0; i < 2; i++)
        {
            float tilt = (i == 0 ? 1f : -1f) * 38f * Mathf.Deg2Rad;
            Vector3 side = (right * Mathf.Cos(tilt) + Vector3.up * Mathf.Sin(tilt)).normalized;
            float delay = i * 0.09f;
            SlashGlow.Arc(pivot, forward, side, reach * 0.62f, to, from, reach * Mathf.Lerp(0.3f, 0.42f, strength),
                new Color(1f, 0.3f, 0.05f, Mathf.Lerp(0.3f, 0.5f, strength)), rim, 0.07f, 0.5f, 0f, 0f, delay);
            SlashGlow.Arc(pivot, forward, side, reach * 0.92f, to, from, Mathf.Lerp(0.07f, 0.13f, strength),
                SlashGlow.Hot, new Color(1f, 0.4f, 0.1f, 0f), 0.07f, 0.45f, 0f, 0f, delay);
        }

        // 3) zem: vypalene X (dohoriva), razova vlna, ohnivy vybuch ve stredu krize
        Vector3 groundCenter = SlashGlow.Ground(feet + forward * reach * 0.6f, 0.05f);
        float scar = reach * Mathf.Lerp(0.8f, 1.1f, strength);
        int seed = Random.Range(0, 100000);
        for (int i = 0; i < 2; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(i == 0 ? 45f : -45f, Vector3.up) * forward;
            SlashGlow.Crack(groundCenter - dir * scar * 0.5f, dir, scar, Mathf.Lerp(0.14f, 0.22f, strength), 0.1f, 1.8f, seed + i);
        }
        SlashGlow.Shockwave(groundCenter, reach * Mathf.Lerp(0.8f, 1.25f, strength), 0.45f, 0.3f, 0.8f);
        Fx.Explosion(xCenter, Mathf.Lerp(0.6f, 1.2f, strength));
        Fx.BulletImpact(xCenter, RageColor, Mathf.Lerp(2f, 3.2f, strength));
        ProceduralSfx.Play(ProceduralSfx.Explosion, xCenter, 0.45f + 0.35f * strength);
        ProceduralSfx.Play(ProceduralSfx.SurfaceSlash, xCenter, 1f);

        // 4) hrac za Povu: kratky oranzovy zablesk obrazovky
        if (IsOwner && fpc != null && fpc.Bot == null)
            HudUI.NotifyTint(new Color(1f, 0.45f, 0.1f, Mathf.Lerp(0.18f, 0.32f, strength)), 0.35f);
    }

    // rovny tah cepeli z 'a' do 'b' (pas kolmo na smer tahu i pohledu), na koncich zuzeny
    static void Stroke(Vector3 a, Vector3 b, Vector3 forward, float halfWidth, Color core, Color rim, float reveal, float life, float delay)
    {
        const int n = 14;
        Vector3 dir = (b - a).normalized;
        Vector3 side = Vector3.Cross(forward, dir).normalized;
        var points = new Vector3[n];
        var sides = new Vector3[n];
        var widths = new float[n];
        for (int i = 0; i < n; i++)
        {
            float s = i / (float)(n - 1);
            points[i] = Vector3.Lerp(a, b, s);
            sides[i] = side;
            widths[i] = halfWidth * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * s)), 0.55f) + 0.01f;
        }
        SlashGlow.Ribbon(points, sides, widths, core, rim, reveal, life, delay);
    }

    [ClientRpc]
    void CounterMissClientRpc(float strength)
    {
        if (!IsOwner) PlayCounterSwing(strength);
    }

    [ClientRpc]
    void CounterHitClientRpc(Vector3 at, float strength)
    {
        Fx.BulletImpact(at, RageColor, Mathf.Lerp(1.2f, 2f, strength));
        Fx.Sparks(at + Vector3.up * 0.3f, RageColor);
        ProceduralSfx.Play(ProceduralSfx.Hit, at, Mathf.Lerp(0.7f, 1f, strength));
    }

    void StartOwner()
    {
        active = true;
        fpc.BlockActive = true;
        fpc.SpeedMultiplier = ability.blockSpeed;
        blocking.Value = true;

        // Blok se zapina casto, hlaska jen jednou za cas.
        if (Time.time >= nextVoiceTime)
        {
            nextVoiceTime = Time.time + 8f;
            GetComponent<PlayerHero>().SayAbility(ability);
        }
    }

    void StopOwner()
    {
        active = false;
        fpc.BlockActive = false;
        fpc.SpeedMultiplier = 1f;

        if (IsSpawned && IsOwner)
            blocking.Value = false;
    }

    // ---------------- server ----------------

    // Vola Health.TakeDamage (server). Vraci poskozeni, ktere po bloku zbyva.
    public float FilterDamage(float amount)
    {
        if (!IsServer || amount <= 0f) return amount;

        lastDamageTime = Time.time;

        if (!enabled || ability == null || !blocking.Value || energy.Value <= 0f)
            return amount;

        float absorbed = Mathf.Min(amount * ability.blockAbsorb, energy.Value);
        energy.Value = Mathf.Max(0f, energy.Value - absorbed);
        if (counter != null) rage.Value = Mathf.Min(MaxRage, rage.Value + absorbed * RageGain);   // Odveta: hnev z vyblokovaneho

        bool broken = energy.Value <= 0.001f;
        if (broken)
            energy.Value = 0f;

        if (broken || Time.time >= nextFxTime)
        {
            nextFxTime = Time.time + 0.15f;
            BlockFxClientRpc(broken);
        }

        return amount - absorbed;
    }

    void ServerRegen()
    {
        if (!enabled || ability == null) return;
        if (Time.time - lastDamageTime < ability.regenDelay) return;

        float max = MaxEnergy;
        if (energy.Value >= max) return;

        float perSecond = max / Mathf.Max(0.5f, ability.regenTime);
        energy.Value = Mathf.Min(max, energy.Value + perSecond * Time.deltaTime);
    }

    [ClientRpc]
    void BlockFxClientRpc(bool broken)
    {
        Vector3 position = transform.position + transform.forward * 0.7f + Vector3.up * 0.4f;
        Fx.Sparks(position, SparkColor);
        ProceduralSfx.PlayForActor(this, broken ? ProceduralSfx.Explosion : ProceduralSfx.Hit, position, broken ? 0.4f : 0.7f);

        if (broken)
            Fx.BulletImpact(position, SparkColor, 1.6f);
    }
}
