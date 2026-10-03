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
            currentAmmo = weapon.maxAmmo;
            ammoInitialized = true;
        }
    }

    // Uskok (Viktor) zbran rovnou prebije.
    public void InstantReload()
    {
        if (weapon == null || weapon.IsMelee) return;

        reloading = false;
        currentAmmo = weapon.maxAmmo;
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

        weapon = newWeapon;
        currentAmmo = newWeapon.maxAmmo;
        ammoInitialized = true;
        reloading = false;
    }

    void Update()
    {
        if (!IsOwner) return;
        if (weapon == null) return;

        // Prebijeni dobehne, i kdyz hrac zrovna nemuze strilet (schopnost, blok).
        if (reloading)
        {
            if (Time.time < reloadEndTime) return;

            reloading = false;
            currentAmmo = weapon.maxAmmo;
            ProceduralSfx.Play(ProceduralSfx.Empty, transform.position, 0.8f);
        }

        // Prazdny zasobnik se prebije sam.
        if (weapon.HasAmmo && currentAmmo <= 0 && (fpc == null || !fpc.CannotAct))
        {
            Reload();
            return;
        }

        if ((fpc != null && (fpc.InputBlocked || fpc.RushActive || fpc.BlockActive)) || !GameSettings.CursorLocked)
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

        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            Reload();
            return;
        }

        if (Mouse.current.leftButton.isPressed && Time.time >= nextFireTime)
            Fire();
    }

    // ---------------- luk ----------------

    bool charging;
    float chargeStart;
    RapidFireAbility rapid;

    // Jak moc je luk natazeny (0-1), pro HUD a model zbrane.
    public float ChargeFraction => charging && weapon != null && weapon.chargeTime > 0f
        ? Mathf.Clamp01((Time.time - chargeStart) / weapon.chargeTime)
        : 0f;

    void UpdateCharged()
    {
        if (rapid == null)
            rapid = GetComponent<RapidFireAbility>();

        bool down = Mouse.current.leftButton.isPressed;

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
        ShotFxServerRpc(true);
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

        ProceduralSfx.Play(melee ? ProceduralSfx.Dash : ProceduralSfx.Gunshot, playerCamera.transform.position, melee ? 0.6f : 0.7f);
        ShotFxServerRpc(melee);
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

            ApplyHit(hit.collider, hit.point, 1f);
            break;
        }
    }

    // Uder: sweep koulí před hráčem, zasáhne nejbližší cíl v dosahu.
    void SwingMelee()
    {
        Vector3 origin = playerCamera.transform.position;
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

    // Spolecny zasah pro hitscan i melee: damage, zvuk zasahu a maly vybuch v miste dopadu.
    void ApplyHit(Collider collider, Vector3 point, float fxScale)
    {
        // Strelba na dalku je slabsi (jen zbrane s nastavenym poklesem poskozeni).
        float damage = weapon.DamageAt(Vector3.Distance(playerCamera.transform.position, point));

        Target target = collider.GetComponent<Target>();
        if (target != null)
        {
            target.TakeDamage(damage);
            HudUI.NotifyHit(false);
        }

        // Balvan (Honzova ulti) jde rozstrilet.
        var boulder = collider.GetComponentInParent<BoulderHitbox>();
        if (boulder != null && boulder.Owner != null && boulder.Owner.NetworkObject != NetworkObject)
        {
            RequestBoulderDamageServerRpc(boulder.Owner.NetworkObject, damage);
            ProceduralSfx.Play(ProceduralSfx.Hit, transform.position, 0.8f);
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

    [ServerRpc]
    void ShotFxServerRpc(bool melee)
    {
        ShotFxClientRpc(melee);
    }

    [ClientRpc]
    void ShotFxClientRpc(bool melee)
    {
        if (IsOwner) return;
        held.Swing();
        ProceduralSfx.Play(melee ? ProceduralSfx.Dash : ProceduralSfx.Gunshot, transform.position, melee ? 0.6f : 0.8f);
    }

    [ServerRpc]
    void RequestDamageServerRpc(NetworkObjectReference targetRef, float amount)
    {
        if (MatchManager.Instance != null && MatchManager.Instance.IsOver) return;
        if (!targetRef.TryGet(out NetworkObject targetObject)) return;

        Health targetHealth = targetObject.GetComponent<Health>();
        if (targetHealth == null) return;

        Combat.DamagePlayer(gameObject, targetHealth, amount);
    }

    [ServerRpc]
    void RequestBoulderDamageServerRpc(NetworkObjectReference ownerRef, float amount)
    {
        if (!ownerRef.TryGet(out NetworkObject ownerObject)) return;

        var boulder = ownerObject.GetComponent<BoulderAbility>();
        if (boulder != null)
            boulder.ServerDamage(gameObject, amount);
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
        if (IsOwner && projectileWeapon.IsCharged && playerCamera != null && (fpc == null || !fpc.ThirdPerson))
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
        if (!weapon.HasAmmo || reloading || currentAmmo == weapon.maxAmmo) return;

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
