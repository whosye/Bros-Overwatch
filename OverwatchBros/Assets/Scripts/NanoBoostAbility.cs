using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Davidova (drive Annina) ultimatni schopnost (Q): Posileni. Hodi plechovku Monsteru spoluhraci v zamerovaci (do 'range' m,
// s primym vyhledem); kdyz ho trefi, dostane na 'duration' sekund posileni: dava o 50 % vetsi poskozeni, dostava
// o polovinu mensi a hned se vyleci o 'power' HP.
// Kdyz v zamerovaci neni spoluhrac, ultimatka se nespotrebuje. Na sebe ji Anna pouzit nemuze (jako v Overwatchi).
public class NanoBoostAbility : NetworkBehaviour
{
    public AbilityDefinition ability;

    public const float DamageMultiplier = 1.5f;
    public const float DamageTakenMultiplier = 0.5f;

    FirstPersonController fpc;
    PlayerHero hero;
    float nextUseTime;
    float nextHint;

    bool UsesCharge => hero != null && hero.UsesUltCharge;
    bool CanUse => UsesCharge ? hero.UltReady : Time.time >= nextUseTime;
    public float CooldownRemaining => UsesCharge ? 0f : Mathf.Max(0f, nextUseTime - Time.time);

    public void Configure(AbilityDefinition definition)
    {
        ability = definition;
    }

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        hero = GetComponent<PlayerHero>();
    }

    void Update()
    {
        if (!IsOwner || ability == null) return;
        if (!HeroInput.Pressed(this, HeroInput.Key.Q) || !HeroInput.Locked(this)) return;
        if (fpc.InputBlocked || !CanUse) return;

        var target = FindAlly();
        if (target == null)
        {
            // Nikdo v zamerovaci: kratky "prazdny" zvuk, ultimatka zustava.
            if (Time.time >= nextHint)
            {
                nextHint = Time.time + 0.5f;
                ProceduralSfx.PlayForActor(this, ProceduralSfx.Empty, transform.position, 0.6f);
            }
            return;
        }

        nextUseTime = Time.time + ability.Cooldown;
        hero.SpendUlt();
        hero.SayAbility(ability);
        BoostServerRpc(target.NetworkObject);
    }

    // Spoluhrac nejblize stredu zamerovace (siroky paprsek, at se netrefuje pixelove presne).
    PlayerHero FindAlly()
    {
        var eye = fpc.playerCamera.transform;
        var hits = Physics.SphereCastAll(eye.position, 0.9f, eye.forward, ability.range, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var other = hit.collider.GetComponentInParent<PlayerHero>();
            if (other == null || other == hero) continue;
            if (!Combat.SameTeam(gameObject, other.gameObject)) continue;

            var health = other.GetComponent<Health>();
            if (health == null || health.currentHealth.Value <= 0f) continue;
            if (!Combat.HasLineOfSight(eye.position, hit.collider)) continue;
            return other;
        }
        return null;
    }

    // ---------------- server ----------------

    [ServerRpc]
    void BoostServerRpc(NetworkObjectReference targetRef)
    {
        var match = MatchManager.Instance;
        if (ability == null || (match != null && (match.IsOver || match.IsLobby))) return;
        if (!targetRef.TryGet(out NetworkObject targetObject)) return;

        var target = targetObject.GetComponent<PlayerHero>();
        var health = targetObject.GetComponent<Health>();
        if (target == null || health == null || target == hero || health.currentHealth.Value <= 0f) return;
        if (!Combat.SameTeam(gameObject, target.gameObject)) return;
        if (Vector3.Distance(transform.position, target.transform.position) > ability.range + 5f) return;

        // plechovka leti k spoluhraci - posileni dostane, az ho trefi
        float flight = Mathf.Clamp(Vector3.Distance(transform.position, target.transform.position) / CanSpeed, 0.2f, 1f);
        ThrowCanClientRpc(target.NetworkObjectId, flight);
        StartCoroutine(ArriveAfter(target, health, flight));
    }

    const float CanSpeed = 22f;

    System.Collections.IEnumerator ArriveAfter(PlayerHero target, Health health, float flight)
    {
        yield return new WaitForSeconds(flight);
        var match = MatchManager.Instance;
        if (target == null || health == null || !target.IsSpawned || health.currentHealth.Value <= 0f) yield break;
        if (match != null && (match.IsOver || match.IsLobby)) yield break;

        target.ServerBoost(ability.duration);
        Combat.HealPlayer(gameObject, health, ability.power);

        var recorder = GetComponent<PotgRecorder>();
        if (recorder != null)
            recorder.ServerNoteUltimate();

        BoostedClientRpc(target.transform.position, target.NetworkObjectId);
    }

    [ClientRpc]
    void ThrowCanClientRpc(ulong targetId, float flight)
    {
        var held = GetComponent<HeldWeapons>();
        if (held != null) held.PlayThrow();
        var network = NetworkManager.Singleton;
        if (network == null || network.SpawnManager == null || !network.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var target)) return;
        // z ruky (u vlastnika kousek pred kamerou, jinak z vysky ramene postavy)
        Vector3 from = IsOwner && fpc != null && fpc.playerCamera != null && !BotBrain.IsBot(gameObject)
            ? fpc.playerCamera.transform.TransformPoint(new Vector3(0.25f, -0.2f, 0.6f))
            : transform.position + Vector3.up * 1.5f + transform.right * 0.3f;
        MonsterCan.Throw(from, target.transform, flight);
    }

    [ClientRpc]
    void BoostedClientRpc(Vector3 position, ulong targetId)
    {
        // z posileneho hrace hraje hudba (Resources/Audio/monster_boost), dokud posileni trva
        var network = NetworkManager.Singleton;
        if (network != null && network.SpawnManager != null && network.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var target))
            MonsterBoostMusic.Play(target.GetComponent<PlayerHero>());

        // Ultimatka je slyset pres celou mapu.
        Fx.PlayGlobal(ProceduralSfx.UltCharge, 0.5f);
        Fx.Sparks(position + Vector3.up * 1.2f, NanoAura.AuraColor);
    }
}

// Hudba z posileneho hrace (Davidova ultimatka): ostatni ji slysi prostorove z nej, on sam vystredene.
// Konci (ztisi se), kdyz posileni skonci nebo hrac zemre.
public class MonsterBoostMusic : MonoBehaviour
{
    PlayerHero hero;
    Health health;
    AudioSource source;
    float endingSince = -1f;
    static AudioClip clip;

    public static void Play(PlayerHero hero)
    {
        if (hero == null) return;
        if (clip == null) clip = Resources.Load<AudioClip>("Audio/monster_boost");
        if (clip == null) return;
        var music = hero.GetComponentInChildren<MonsterBoostMusic>();
        if (music == null)
        {
            var go = new GameObject("MonsterBoostMusic");
            go.transform.SetParent(hero.transform, false);
            go.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            music = go.AddComponent<MonsterBoostMusic>();
            music.hero = hero;
            music.health = hero.GetComponent<Health>();
            music.source = go.AddComponent<AudioSource>();
            music.source.playOnAwake = false;
            music.source.rolloffMode = AudioRolloffMode.Linear;
            music.source.minDistance = 4f;
            music.source.maxDistance = 40f;
            music.source.dopplerLevel = 0f;
        }
        music.source.clip = clip;
        music.source.spatialBlend = ProceduralSfx.IsLocalPlayer(hero) ? 0f : 1f;
        music.source.volume = 0.85f;
        music.source.time = 0f;
        music.source.Play();
        music.endingSince = -1f;
    }

    void Update()
    {
        bool dead = hero == null || (health != null && health.currentHealth.Value <= 0f);
        if (dead || PotgUI.IsShowing)
        {
            Destroy(gameObject);   // pri smrti (a na zacatku POTG) hned ticho
            return;
        }
        bool boosted = hero.IsBoosted;
        // (kratka rezerva - stav posileni prijde po siti o chvili pozdeji nez hudba)
        if ((boosted || source.time < 1f) && source.isPlaying) return;

        if (endingSince < 0f) endingSince = Time.time;
        source.volume = Mathf.MoveTowards(source.volume, 0f, Time.deltaTime * 1.7f);
        if (source.volume <= 0.001f || !source.isPlaying)
            Destroy(gameObject);
    }
}

// Letici plechovka Monsteru (jen vzhled): obloukem k cili (sleduje ho, kdyz se hybe), toci se, pri dopadu jiskry.
public class MonsterCan : MonoBehaviour
{
    Vector3 from;
    Transform target;
    float flight, age;
    static Material bodyMaterial, lidMaterial;

    public static void Throw(Vector3 from, Transform target, float flight)
    {
        var go = new GameObject("MonsterCan");
        go.transform.position = from;
        var can = go.AddComponent<MonsterCan>();
        can.from = from;
        can.target = target;
        can.flight = Mathf.Max(0.1f, flight);
        Build(go.transform);
    }

    // Plechovka 500 ml: telo s texturou (Resources/Textures/monster_can), stribrna vicka.
    static void Build(Transform root)
    {
        if (bodyMaterial == null)
        {
            bodyMaterial = Fx.NewLit(Color.white);
            var texture = Resources.Load<Texture2D>("Textures/monster_can");
            if (texture != null)
            {
                bodyMaterial.mainTexture = texture;
                if (bodyMaterial.HasProperty("_BaseMap")) bodyMaterial.SetTexture("_BaseMap", texture);
                bodyMaterial.mainTextureScale = new Vector2(2f, 1f);   // predek plechovky dvakrat dokola
                if (bodyMaterial.HasProperty("_BaseMap")) bodyMaterial.SetTextureScale("_BaseMap", new Vector2(2f, 1f));
            }
            if (bodyMaterial.HasProperty("_Smoothness")) bodyMaterial.SetFloat("_Smoothness", 0.75f);
            if (bodyMaterial.HasProperty("_Metallic")) bodyMaterial.SetFloat("_Metallic", 0.4f);
            lidMaterial = Fx.NewLit(new Color(0.78f, 0.8f, 0.83f));
            if (lidMaterial.HasProperty("_Metallic")) lidMaterial.SetFloat("_Metallic", 0.8f);
        }
        Part(root, Vector3.zero, new Vector3(0.066f, 0.084f, 0.066f), bodyMaterial);           // telo (vyska 16,8 cm)
        Part(root, new Vector3(0f, 0.086f, 0f), new Vector3(0.058f, 0.004f, 0.058f), lidMaterial);  // horni vicko
        Part(root, new Vector3(0f, -0.086f, 0f), new Vector3(0.058f, 0.004f, 0.058f), lidMaterial); // dno
        root.localScale = Vector3.one * 1.6f;   // o kus vetsi, at je v letu videt
    }

    static void Part(Transform root, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(root, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = Mathf.Clamp01(age / flight);
        Vector3 to = target != null ? target.position + Vector3.up * 1.2f : from;
        Vector3 position = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * Mathf.Min(1.5f, Vector3.Distance(from, to) * 0.15f);
        transform.position = position;
        transform.rotation = Quaternion.Euler(age * 720f, age * 260f, 0f);   // toci se v letu
        if (t >= 1f)
        {
            Fx.Sparks(position, NanoAura.AuraColor);
            ProceduralSfx.Play(ProceduralSfx.Hit, position, 0.6f);
            Destroy(gameObject);
        }
    }
}

// Viditelna aura posileneho hrace: modre svetlo a stoupajici jiskry kolem tela.
public class NanoAura : MonoBehaviour
{
    public static readonly Color AuraColor = new Color(0.45f, 0.8f, 1f, 1f);

    Light glow;
    ParticleSystem particles;

    public static void Set(Transform player, bool on)
    {
        ReplayLog.NanoAura(player, on);
        var aura = player.GetComponentInChildren<NanoAura>(true);
        if (!on)
        {
            if (aura != null)
                Destroy(aura.gameObject);
            return;
        }
        if (aura != null) return;

        var go = new GameObject("NanoAura");
        go.transform.SetParent(player, false);
        go.transform.localPosition = new Vector3(0f, 1f, 0f);
        aura = go.AddComponent<NanoAura>();
        aura.Build();
    }

    void Build()
    {
        glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = AuraColor;
        glow.range = 4f;
        glow.intensity = 3f;

        particles = gameObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(AuraColor, Color.white);
        main.gravityModifier = -0.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.rateOverTime = 45f;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.45f;
        shape.radiusThickness = 0f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;
        particles.Play();
    }

    void Update()
    {
        if (glow != null)
            glow.intensity = 2.6f + Mathf.Sin(Time.time * 9f) * 0.8f;
    }
}
