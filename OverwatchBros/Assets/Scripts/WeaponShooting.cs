using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class WeaponShooting : NetworkBehaviour
{
    public WeaponDefinition weapon;
    public Camera playerCamera;

    int currentAmmo;
    float nextFireTime;
    bool ammoInitialized;
    bool reloading;
    float reloadEndTime;
    FirstPersonController fpc;

    public int CurrentAmmo => currentAmmo;

    // Velikost zasobniku; schopnost ji muze docasne zvetsit (Viktorova ultimatka: 30 naboju).
    public int MaxAmmo => weapon == null ? 0 : ammoOverride > 0 ? ammoOverride : weapon.maxAmmo;
    int ammoOverride;

    // Zvetsi zasobnik a rovnou ho naplni; 0 vrati normalni velikost (naboje navic propadnou).
    public void SetMagazineOverride(int ammo)
    {
        ammoOverride = ammo;
        if (weapon == null || !weapon.HasAmmo) return;

        if (ammo > 0)
            InstantReload();
        else
            currentAmmo = Mathf.Min(currentAmmo, weapon.maxAmmo);
    }
    public bool IsReloading => reloading;

    HeldWeapons held;

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        held = GetComponent<HeldWeapons>();
        if (held == null)
            held = gameObject.AddComponent<HeldWeapons>();
    }

    void Start()
    {
        if (!ammoInitialized && weapon != null)
        {
            currentAmmo = MaxAmmo;
            ammoInitialized = true;
        }
    }

    // Uskok (Viktor) zbran rovnou prebije.
    public void InstantReload()
    {
        if (weapon == null || !weapon.HasAmmo) return;

        // Prebijeni, ktere zrovna bezi, se ukonci i vizualne (jinak by zbran zustala sklopena mimo obraz).
        if (reloading)
        {
            held.StopReload();
            StopReloadServerRpc();
        }

        reloading = false;
        currentAmmo = MaxAmmo;
    }

    [ServerRpc]
    void StopReloadServerRpc()
    {
        StopReloadClientRpc();
    }

    [ClientRpc]
    void StopReloadClientRpc()
    {
        if (!IsOwner)
            held.StopReload();
    }

    VisorAbility visor;

    // Smer strely: normalne stred obrazovky, s aktivnim taktickym zamerovacem primo na zamereny cil.
    Vector3 AimDirection()
    {
        if (visor == null)
            visor = GetComponent<VisorAbility>();

        if (visor != null && visor.TryGetAim(out Vector3 point))
            return (point - playerCamera.transform.position).normalized;

        return playerCamera.transform.forward;
    }

    // Chvili nestrilet (napr. hned po odpaleni balvanu levym tlacitkem).
    public void HoldFire(float seconds)
    {
        nextFireTime = Mathf.Max(nextFireTime, Time.time + seconds);
    }

    public void SetWeapon(WeaponDefinition newWeapon)
    {
        if (newWeapon == null) return;

        scopedCharge = 0f;
        weapon = newWeapon;
        ammoOverride = 0;
        currentAmmo = newWeapon.maxAmmo;
        ammoInitialized = true;
        reloading = false;
    }

    // Bot (BotBrain) misto mysi a klavesnice - jen na hostu.
    public BotBrain Bot { get; set; }
    bool FireHeld => Bot != null ? Bot.FireHeld : Mouse.current.leftButton.isPressed;
    bool FirePressed => Bot != null ? Bot.FirePressedThisFrame : Mouse.current.leftButton.wasPressedThisFrame;

    void Update()
    {
        if (!IsOwner) return;
        if (weapon == null) return;

        // Prebijeni dobehne, i kdyz hrac zrovna nemuze strilet (schopnost, blok).
        if (reloading)
        {
            if (Time.time < reloadEndTime) return;

            reloading = false;
            currentAmmo = MaxAmmo;
            ProceduralSfx.Play(ProceduralSfx.Empty, transform.position, 0.8f);
        }

        // Prazdny zasobnik se prebije sam.
        if (weapon.HasAmmo && currentAmmo <= 0 && (fpc == null || !fpc.CannotAct))
        {
            Reload();
            return;
        }

        if ((fpc != null && (fpc.InputBlocked || fpc.RushActive || fpc.BlockActive)) || (Bot == null && !GameSettings.CursorLocked))
        {
            charging = false;
            return;
        }

        // Luk: drzenim se natahuje, pustenim vystreli.
        if (weapon.IsCharged)
        {
            UpdateCharged();
            return;
        }

        // u packy kotle je R "zatopit" (Boiler), ne nabijeni
        if (Bot == null && Keyboard.current.rKey.wasPressedThisFrame && !Boiler.LocalCanUse)
        {
            Reload();
            return;
        }

        // Sniper: s pribliseni se rana nabiji a strili se po jedne; bez nej je to obycejny samopal.
        if (weapon.HasScopedShot)
        {
            if (scope == null)
                scope = GetComponent<ScopeZoom>();
            if (scope != null && scope.Zoomed)
            {
                UpdateScoped();
                return;
            }
            scopedCharge = 0f;
        }

        if (FireHeld && Time.time >= nextFireTime)
            Fire();
    }

    // ---------------- odstrel s pribliseni ----------------

    ScopeZoom scope;
    float scopedCharge;

    // Jak hlava vysoko: horni cast kapsle hrace.
    const float HeadHeight = 0.38f;

    void UpdateScoped()
    {
        scopedCharge = Mathf.Clamp01(scopedCharge + Time.deltaTime / Mathf.Max(0.05f, weapon.scopedChargeTime));

        if (!FirePressed || Time.time < nextFireTime) return;

        int cost = Mathf.Max(1, weapon.scopedAmmoCost);
        if (weapon.HasAmmo && currentAmmo < cost)
        {
            nextFireTime = Time.time + 0.35f;
            ProceduralSfx.Play(ProceduralSfx.Empty, transform.position, 0.6f);
            if (currentAmmo <= 0) Reload();
            return;
        }

        float charge = scopedCharge;
        bool full = charge >= 0.999f;
        scopedCharge = 0f;
        nextFireTime = Time.time + 0.6f;
        if (weapon.HasAmmo)
            currentAmmo -= cost;

        PlayShot(playerCamera.transform.position, true, 1f);
        ShotFxServerRpc(false, true);
        held.Swing();

        Vector3 origin = playerCamera.transform.position;
        Vector3 direction = playerCamera.transform.forward;
        Vector3 end = origin + direction * weapon.range;
        var hits = Physics.RaycastAll(origin, direction, weapon.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && owner == NetworkObject) continue;

            // i plne nabity odstrel slabne se vzdalenosti
            float damage = Mathf.Lerp(weapon.scopedMinDamage, weapon.scopedMaxDamage, charge)
                * weapon.ScopedRangeFactor(Vector3.Distance(origin, hit.point));
            bool headshot = full && weapon.headshotMultiplier > 1f && hit.collider.GetComponentInParent<Health>() != null
                && hit.point.y >= hit.collider.bounds.max.y - HeadHeight;
            if (headshot)
            {
                damage *= weapon.headshotMultiplier;
                ProceduralSfx.Play(ProceduralSfx.StunConfirm, playerCamera.transform.position, 1f);
            }

            ApplyHit(hit.collider, hit.point, full ? 2f : 1.2f, damage);
            end = hit.point;
            break;
        }

        // Stopa vystrelu (vidi ji vsichni, at je jasne, odkud sniper strili).
        Vector3 muzzle = origin + playerCamera.transform.right * 0.15f - playerCamera.transform.up * 0.12f;
        Fx.Tracer(muzzle, end, weapon.projectileColor, full);
        TracerServerRpc(muzzle, end, full);
    }

    [ServerRpc]
    void TracerServerRpc(Vector3 from, Vector3 to, bool full)
    {
        TracerClientRpc(from, to, full);
    }

    [ClientRpc]
    void TracerClientRpc(Vector3 from, Vector3 to, bool full)
    {
        if (IsOwner) return;
        Fx.Tracer(from, to, weapon != null ? weapon.projectileColor : Color.white, full);
    }

    // ---------------- luk ----------------

    bool charging;
    float chargeStart;
    RapidFireAbility rapid;

    // Jak moc je luk natazeny (0-1), pro HUD a model zbrane.
    public float ChargeFraction => Mathf.Max(Mathf.Max(ForcedCharge, scopedCharge), charging && weapon != null && weapon.chargeTime > 0f
        ? Mathf.Clamp01((Time.time - chargeStart) / weapon.chargeTime)
        : 0f);

    // Natazeni ridi schopnost (priprava Mirkovy ultimatky), ne hrac.
    public float ForcedCharge { get; set; }

    void UpdateCharged()
    {
        if (rapid == null)
            rapid = GetComponent<RapidFireAbility>();

        bool down = FireHeld;

        // Rychlopalba: sipy leti hned plnou rychlosti, jeden za druhym.
        if (rapid != null && rapid.enabled && rapid.IsActive)
        {
            charging = false;
            if (down && Time.time >= nextFireTime)
            {
                nextFireTime = Time.time + rapid.interval;
                FireArrow(1f, true);
                rapid.ConsumeShot();
            }
            return;
        }

        if (down)
        {
            if (!charging && Time.time >= nextFireTime)
            {
                charging = true;
                chargeStart = Time.time;
            }
        }
        else if (charging)
        {
            float charge = ChargeFraction;
            charging = false;
            nextFireTime = Time.time + 1f / Mathf.Max(0.1f, weapon.fireRate);
            FireArrow(charge, false);
        }
    }

    void FireArrow(float charge, bool rapidShot)
    {
        ProceduralSfx.Play(ProceduralSfx.Dash, playerCamera.transform.position, 0.45f + 0.3f * charge);
        ShotFxServerRpc(true, false);
        held.Swing();

        Vector3 direction = playerCamera.transform.forward;
        FireProjectileServerRpc(playerCamera.transform.position + direction * 0.6f, direction, charge, rapidShot);
    }

    void Fire()
    {
        bool melee = weapon.IsMelee;

        if (weapon.HasAmmo && currentAmmo <= 0)
        {
            nextFireTime = Time.time + 0.35f;
            ProceduralSfx.Play(ProceduralSfx.Empty, transform.position, 0.6f);
            return;
        }

        nextFireTime = Time.time + 1f / weapon.fireRate;
        if (weapon.HasAmmo)
            currentAmmo--;

        if (melee) ProceduralSfx.Play(ProceduralSfx.Dash, playerCamera.transform.position, 0.6f);
        else PlayShot(playerCamera.transform.position, false, 0.8f);
        ShotFxServerRpc(melee, false);
        held.Swing();

        if (weapon.IsProjectile)
        {
            // Projektil: server ho spusti a simuluje, klienti dostanou vizual.
            Vector3 direction = playerCamera.transform.forward;
            FireProjectileServerRpc(playerCamera.transform.position + direction * 0.6f, direction, 1f, false);
            return;
        }

        if (melee)
        {
            SwingMelee();
            return;
        }

        // Vlastni telo strelu nezastavi (kamera je uvnitr kapsle hrace).
        var hits = Physics.RaycastAll(playerCamera.transform.position, AimDirection(), weapon.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && owner == NetworkObject) continue;

            // Zasah do hlavy u bezne hitscan zbrane (napr. Sindelova pistole; sniper ma vlastni odstrel s pribliseni).
            if (weapon.headshotMultiplier > 1f && !weapon.HasScopedShot && hit.collider.GetComponentInParent<Health>() != null
                && hit.point.y >= hit.collider.bounds.max.y - HeadHeight)
            {
                float body = DamageFor(hit.collider, hit.point);
                ProceduralSfx.Play(ProceduralSfx.StunConfirm, playerCamera.transform.position, 0.8f);
                ApplyHit(hit.collider, hit.point, 1.3f, body * weapon.headshotMultiplier);
                break;
            }

            ApplyHit(hit.collider, hit.point, 1f);
            break;
        }
    }

    // Uder: siroky oblouk pred hracem (~110 st.) a k tomu 0,5 m tolerance do stran - zasahne vsechny nepratele
    // v dosahu, na ktere je volny vyhled (ne pres zed). Kdyz nikoho, zasah do zdi pred hracem (jiskry).
    const float MeleeSlack = 0.5f;
    const float ComboWindow = 1f;
    int comboStep;
    float lastSwing = -10f;

    void SwingMelee()
    {
        Vector3 origin = playerCamera.transform.position;
        Vector3 forward = playerCamera.transform.forward;

        // kombo (Max): treti uder po sobe je silnejsi, sirsi a odhodi
        bool finisher = false;
        if (weapon.comboFinisherDamage > 0f)
        {
            comboStep = Time.time - lastSwing <= ComboWindow ? comboStep + 1 : 0;
            finisher = comboStep >= 2;
            if (finisher)
            {
                comboStep = -1;
                nextFireTime = Mathf.Max(nextFireTime, Time.time + 0.35f);   // po finisheru kratka pauza
            }
        }
        lastSwing = Time.time;
        float arc = finisher ? weapon.comboFinisherArc : weapon.meleeArc;
        var struck = new System.Collections.Generic.HashSet<Object>();
        bool any = false;
        foreach (var col in Physics.OverlapSphere(origin, weapon.range + MeleeSlack, ~0, QueryTriggerInteraction.Ignore))
        {
            var owner = col.GetComponentInParent<NetworkObject>();
            if (owner != null && owner == NetworkObject) continue;
            Object victim = col.GetComponentInParent<Health>();
            if (victim == null) victim = col.GetComponentInParent<Target>();
            if (victim == null) victim = col.GetComponentInParent<BoulderHitbox>();
            if (victim == null) victim = col.GetComponentInParent<TrapHitbox>();
            if (victim == null || struck.Contains(victim)) continue;
            if (victim is Health h && Combat.SameTeam(gameObject, h.gameObject)) continue;

            Vector3 point = col.ClosestPoint(origin);
            Vector3 to = point - origin;
            float along = Vector3.Dot(to, forward);
            if (along < -0.2f || to.magnitude > weapon.range + MeleeSlack) continue;
            float lateral = (to - forward * along).magnitude;
            if (Vector3.Angle(forward, to) > arc && lateral > MeleeSlack) continue;

            // ne pres zed
            if (Physics.Linecast(origin, point, out RaycastHit block, ~0, QueryTriggerInteraction.Ignore)
                && block.collider != col && block.collider.GetComponentInParent<NetworkObject>() != NetworkObject
                && block.collider.GetComponentInParent<NetworkObject>() != owner)
                continue;

            struck.Add(victim);
            if (finisher)
            {
                ApplyHit(col, point, 2f, weapon.comboFinisherDamage);
                if (weapon.comboFinisherKnockback > 0f && victim is Health pushed && pushed.TryGetComponent<NetworkObject>(out var pushedObject))
                {
                    Vector3 away = new Vector3(to.x, 0f, to.z).normalized;
                    KnockbackServerRpc(pushedObject, away * weapon.comboFinisherKnockback + Vector3.up * 3f);
                }
            }
            else
                ApplyHit(col, point, 1.4f);
            any = true;
        }
        if (any) return;

        var hits = Physics.SphereCastAll(origin, weapon.meleeRadius, playerCamera.transform.forward, weapon.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            var owner = hit.collider.GetComponentInParent<NetworkObject>();
            if (owner != null && owner == NetworkObject) continue;

            Vector3 point = hit.distance > 0f ? hit.point : hit.collider.ClosestPoint(origin);
            ApplyHit(hit.collider, point, 1.4f);
            return;
        }
    }

    // Poskozeni podle vzdalenosti.
    float DamageFor(Collider collider, Vector3 point)
    {
        return weapon.DamageAt(Vector3.Distance(playerCamera.transform.position, point));
    }

    // Spolecny zasah pro hitscan i melee: damage, zvuk zasahu a maly vybuch v miste dopadu.
    // damageOverride >= 0: pevne poskozeni (odstrel s pribliseni), jinak podle zbrane a vzdalenosti.
    void ApplyHit(Collider collider, Vector3 point, float fxScale, float damageOverride = -1f)
    {
        // Strelba na dalku je slabsi (jen zbrane s nastavenym poklesem poskozeni).
        float damage = damageOverride >= 0f ? damageOverride : DamageFor(collider, point);

        // Mimo ucinny dosah strela nic nezpusobi (jen dopad na povrchu, bez zvuku zasahu).
        if (damage <= 0f)
        {
            Fx.BulletImpact(point, weapon.projectileColor, fxScale);
            ImpactFxServerRpc(point, fxScale);
            return;
        }

        Target target = collider.GetComponent<Target>();
        if (target != null)
        {
            target.TakeDamage(damage);
            if (Bot == null) HudUI.NotifyHit(false);
        }

        // Balvan (Honzova ulti) jde rozstrilet.
        var boulder = collider.GetComponentInParent<BoulderHitbox>();
        if (boulder != null && boulder.Owner != null && boulder.Owner.NetworkObject != NetworkObject)
        {
            RequestBoulderDamageServerRpc(boulder.Owner.NetworkObject, damage);
            ProceduralSfx.Play(ProceduralSfx.Hit, transform.position, 0.8f);
        }

        // Nepratelska past na medvedy jde rozstrilet.
        var trap = collider.GetComponentInParent<TrapHitbox>();
        if (trap != null && trap.Owner != null && !trap.IsFriendly(gameObject))
        {
            RequestTrapDamageServerRpc(trap.Owner.NetworkObject, damage);
            ProceduralSfx.Play(ProceduralSfx.Hit, transform.position, 0.8f);
            if (Bot == null) HudUI.NotifyHit(false);
        }

        NetworkObject hitNetworkObject = collider.GetComponentInParent<NetworkObject>();
        if (hitNetworkObject != null && hitNetworkObject != NetworkObject)
        {
            RequestDamageServerRpc(hitNetworkObject, damage);
            ProceduralSfx.Play(ProceduralSfx.Hit, transform.position, 0.8f);
        }

        Fx.BulletImpact(point, weapon.projectileColor, fxScale);
        ImpactFxServerRpc(point, fxScale);
    }

    [ServerRpc]
    void ImpactFxServerRpc(Vector3 point, float fxScale)
    {
        ImpactFxClientRpc(point, fxScale);
    }

    [ClientRpc]
    void ImpactFxClientRpc(Vector3 point, float fxScale)
    {
        if (IsOwner) return;

        var playerHero = GetComponent<PlayerHero>();
        Color tint = playerHero != null && playerHero.Hero != null && playerHero.Hero.weapon != null
            ? playerHero.Hero.weapon.projectileColor
            : Color.white;
        Fx.BulletImpact(point, tint, Mathf.Clamp(fxScale, 0.5f, 3f));
    }

    // Zvuk vystrelu: vlastni nahravka zbrane (WeaponDefinition.fireSound), jinak generovany zastupny.
    void PlayShot(Vector3 position, bool scoped, float volume)
    {
        var clip = weapon != null ? weapon.ShotSound(scoped) : null;
        if (clip != null)
            ProceduralSfx.Play(clip, position, volume, WeaponDefinition.FireSoundRange);
        else
            ProceduralSfx.Play(ProceduralSfx.Gunshot, position, volume * 0.85f);
    }

    [ServerRpc]
    void ShotFxServerRpc(bool melee, bool scoped)
    {
        if (!melee) BotBrain.HearShot(transform.position, gameObject);   // boti v okoli strelbu slysi
        ShotFxClientRpc(melee, scoped);
    }

    [ClientRpc]
    void ShotFxClientRpc(bool melee, bool scoped)
    {
        if (IsOwner) return;
        held.Swing();
        if (melee) ProceduralSfx.Play(ProceduralSfx.Dash, transform.position, 0.6f);
        else PlayShot(transform.position, scoped, 0.9f);
    }

    [ServerRpc]
    void KnockbackServerRpc(NetworkObjectReference targetRef, Vector3 impulse)
    {
        if (!targetRef.TryGet(out NetworkObject targetObject) || Combat.SameTeam(gameObject, targetObject.gameObject)) return;
        if ((targetObject.transform.position - transform.position).sqrMagnitude > 49f) return;
        var movement = targetObject.GetComponent<FirstPersonController>();
        if (movement != null) movement.ServerKnockback(Vector3.ClampMagnitude(impulse, 12f));
    }

    [ServerRpc]
    void RequestDamageServerRpc(NetworkObjectReference targetRef, float amount)
    {
        if (MatchManager.Instance != null && MatchManager.Instance.IsOver) return;
        if (!targetRef.TryGet(out NetworkObject targetObject)) return;

        Health targetHealth = targetObject.GetComponent<Health>();
        if (targetHealth == null) return;

        Combat.DamagePlayer(gameObject, targetHealth, amount, weapon != null && weapon.IsMelee ? "weapon:melee" : "weapon:gun");
    }

    [ServerRpc]
    void RequestBoulderDamageServerRpc(NetworkObjectReference ownerRef, float amount)
    {
        if (!ownerRef.TryGet(out NetworkObject ownerObject)) return;

        var boulder = ownerObject.GetComponent<BoulderAbility>();
        if (boulder != null)
            boulder.ServerDamage(gameObject, amount);
    }

    [ServerRpc]
    void RequestTrapDamageServerRpc(NetworkObjectReference ownerRef, float amount)
    {
        if (!ownerRef.TryGet(out NetworkObject ownerObject)) return;

        var trap = ownerObject.GetComponent<TrapAbility>();
        if (trap != null)
            trap.ServerDamage(gameObject, amount);
    }

    static int projectileCounter;

    [ServerRpc]
    void FireProjectileServerRpc(Vector3 origin, Vector3 direction, float charge, bool rapidShot)
    {
        var match = MatchManager.Instance;
        if (match != null && (match.IsOver || match.IsLobby)) return;

        var playerHero = GetComponent<PlayerHero>();
        var serverWeapon = playerHero != null && playerHero.Hero != null ? playerHero.Hero.weapon : weapon;
        if (serverWeapon == null || !serverWeapon.IsProjectile) return;
        if (direction.sqrMagnitude < 0.01f) return;

        direction.Normalize();
        int id = ++projectileCounter;
        int heroId = playerHero != null ? playerHero.heroId.Value : -1;
        // Luk: rychlost i poskozeni podle natazeni; sip z rychlopalby leti naplno a dava poskozeni schopnosti.
        float speed = serverWeapon.projectileSpeed;
        float damage = serverWeapon.damage;
        if (serverWeapon.IsCharged)
        {
            charge = Mathf.Clamp01(charge);
            var hero = playerHero != null ? playerHero.Hero : null;
            bool rapidOk = rapidShot && hero != null && hero.rmbAbilityKind == AbilityKind.RapidFire && hero.rmbAbility != null;

            speed = rapidOk ? serverWeapon.projectileSpeed : Mathf.Lerp(serverWeapon.minChargeSpeed, serverWeapon.projectileSpeed, charge);
            damage = rapidOk ? hero.rmbAbility.power : Mathf.Lerp(serverWeapon.minChargeDamage, serverWeapon.damage, charge);
        }

        Vector3 velocity = direction * speed;

        ProjectileSim.Spawn(this, serverWeapon, heroId, id, origin, velocity, damage);
        SpawnProjectileVisualClientRpc(id, origin, velocity, heroId);
    }

    [ClientRpc]
    void SpawnProjectileVisualClientRpc(int id, Vector3 origin, Vector3 velocity, int heroId)
    {
        var projectileWeapon = WeaponOfHero(heroId);
        if (projectileWeapon == null) return;

        // Strelec vidi svuj sip vyletet od luku (vpravo dole), ne primo ze stredu obrazovky.
        Vector3 offset = Vector3.zero;
        if (IsOwner && Bot == null && projectileWeapon.IsCharged && playerCamera != null && (fpc == null || !fpc.ThirdPerson))
            offset = playerCamera.transform.right * 0.28f - playerCamera.transform.up * 0.22f;

        ProjectileVisual.Spawn(id, origin, velocity, projectileWeapon, offset);
    }

    // Vola serverova simulace projektilu, kdyz projektil skoncil (zasah / vybuch / vyprsel dostrel).
    public void NotifyProjectileEnd(int id, Vector3 position, int kind, int heroId)
    {
        if (!IsServer) return;
        ProjectileEndClientRpc(id, position, kind, heroId);
    }

    [ClientRpc]
    void ProjectileEndClientRpc(int id, Vector3 position, int kind, int heroId)
    {
        ProjectileVisual.End(id, position, kind, WeaponOfHero(heroId));
    }

    WeaponDefinition WeaponOfHero(int heroId)
    {
        var hero = HeroRegistry.Get(heroId);
        return hero != null && hero.weapon != null ? hero.weapon : weapon;
    }

    // Prebiti trva weapon.reloadTime; naboje pribydou az na konci.
    void Reload()
    {
        if (!weapon.HasAmmo || reloading || currentAmmo == MaxAmmo) return;

        float duration = Mathf.Max(0.05f, weapon.reloadTime);
        reloading = true;
        reloadEndTime = Time.time + duration;

        ProceduralSfx.Play(ProceduralSfx.Reload, transform.position, 0.6f);
        held.PlayReload(duration);
        ReloadFxServerRpc(duration);
    }

    [ServerRpc]
    void ReloadFxServerRpc(float duration)
    {
        ReloadFxClientRpc(duration);
    }

    [ClientRpc]
    void ReloadFxClientRpc(float duration)
    {
        if (IsOwner) return;

        held.PlayReload(Mathf.Clamp(duration, 0.05f, 10f));
        ProceduralSfx.Play(ProceduralSfx.Reload, transform.position, 0.6f);
    }
}
