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
        if (!weapon.IsMelee && currentAmmo <= 0 && (fpc == null || !fpc.CannotAct))
        {
            Reload();
            return;
        }

        if (fpc != null && (fpc.InputBlocked || fpc.RushActive || fpc.BlockActive)) return;
        if (!GameSettings.CursorLocked) return;

        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            Reload();
            return;
        }

        if (Mouse.current.leftButton.isPressed && Time.time >= nextFireTime)
            Fire();
    }

    void Fire()
    {
        bool melee = weapon.IsMelee;

        if (!melee && currentAmmo <= 0)
        {
            nextFireTime = Time.time + 0.35f;
            ProceduralSfx.Play(ProceduralSfx.Empty, transform.position, 0.6f);
            return;
        }

        nextFireTime = Time.time + 1f / weapon.fireRate;
        if (!melee)
            currentAmmo--;

        ProceduralSfx.Play(melee ? ProceduralSfx.Dash : ProceduralSfx.Gunshot, playerCamera.transform.position, melee ? 0.6f : 0.7f);
        ShotFxServerRpc(melee);
        held.Swing();

        if (weapon.IsProjectile)
        {
            // Projektil: server ho spusti a simuluje, klienti dostanou vizual.
            Vector3 direction = playerCamera.transform.forward;
            FireProjectileServerRpc(playerCamera.transform.position + direction * 0.6f, direction);
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
        Target target = collider.GetComponent<Target>();
        if (target != null)
        {
            target.TakeDamage(weapon.damage);
            HudUI.NotifyHit(false);
        }

        // Balvan (Honzova ulti) jde rozstrilet.
        var boulder = collider.GetComponentInParent<BoulderHitbox>();
        if (boulder != null && boulder.Owner != null && boulder.Owner.NetworkObject != NetworkObject)
        {
            RequestBoulderDamageServerRpc(boulder.Owner.NetworkObject, weapon.damage);
            ProceduralSfx.Play(ProceduralSfx.Hit, transform.position, 0.8f);
        }

        NetworkObject hitNetworkObject = collider.GetComponentInParent<NetworkObject>();
        if (hitNetworkObject != null && hitNetworkObject != NetworkObject)
        {
            RequestDamageServerRpc(hitNetworkObject, weapon.damage);
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
    void FireProjectileServerRpc(Vector3 origin, Vector3 direction)
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
        Vector3 velocity = direction * serverWeapon.projectileSpeed;

        ProjectileSim.Spawn(this, serverWeapon, heroId, id, origin, velocity);
        SpawnProjectileVisualClientRpc(id, origin, velocity, heroId);
    }

    [ClientRpc]
    void SpawnProjectileVisualClientRpc(int id, Vector3 origin, Vector3 velocity, int heroId)
    {
        var projectileWeapon = WeaponOfHero(heroId);
        if (projectileWeapon != null)
            ProjectileVisual.Spawn(id, origin, velocity, projectileWeapon);
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
        if (weapon.IsMelee || reloading || currentAmmo == weapon.maxAmmo) return;

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
