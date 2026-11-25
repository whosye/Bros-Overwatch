using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Snezna rolba (objekt "SneznaRolba" ve scene, drive traktor "TraktorSVlekem" - muze stat kdekoli, komponenta se prida
// sama). Kdyz ji nekdo ridi (MatchManager.tractorDriver), rolba jede s nim: ridic stoji v kabine, jeho pozice a natoceni
// (synchronizovane jako u kazdeho hrace) urcuji polohu rolby. Pohyb resi kapsle ridice (CharacterController), takze
// rolba projede vsude, kam projde hrac - dvere, schody, rampy. Behem jizdy nema rolba kolize (ridic by se o ne zasekl) -
// kdo je v ceste, toho server prejede. Odstavena stoji, kde ji ridic nechal (MatchManager.tractorPose).
public class Tractor : MonoBehaviour
{
    public const float MaxSpeed = 7f;
    public const float ReverseSpeed = 3f;
    public const float RunOverSpeed = 1.5f;   // (i couvani - zpet jede jen 3 m/s)
    public const float RunOverDamage = 50f;

    // kabina v mistnich souradnicich (+X = dopredu, radlice)
    static readonly Vector3 CabLocal = new Vector3(-0.05f, 0f, 0f);
    // obrys rolby i s radlici a frezou (mistni souradnice) - kdo je uvnitr, je prejety
    static readonly Vector3 HullMin = new Vector3(-1.45f, -0.5f, -0.8f), HullMax = new Vector3(1.35f, 2.2f, 0.8f);
    // jak daleko do strany od kabiny se vystupuje
    public const float ExitSide = 1.3f;

    public static Tractor Instance { get; private set; }
    public static string PromptText { get; private set; } = "";

    public Vector3 CabPosition => transform.TransformPoint(CabLocal);
    // spolujezdec stoji na plosince za kabinou
    public Vector3 SeatPosition => transform.TransformPoint(new Vector3(-0.85f, 0.58f, 0f));

    Vector3 homePosition;
    Quaternion homeRotation;
    readonly List<Collider> colliders = new List<Collider>();
    readonly List<Transform> wheels = new List<Transform>();
    AudioSource engine;
    ParticleSystem exhaust;
    ParticleSystem snow;

    ParticleSystem BuildSnow()
    {
        var go = new GameObject("FX_Snih");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(-1.15f, 0.35f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.startColor = new Color(0.97f, 0.98f, 1f, 0.95f);
        main.gravityModifier = 0.8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 30f;
        shape.radius = 0.4f;
        shape.rotation = new Vector3(-30f, -90f, 0f);   // dozadu a nahoru
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        go.GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;
        return ps;
    }
    Vector3 lastPosition;
    float wheelAngle;
    bool driven;

    // Rolba na Domasove a v kopii mapy pro Tlacenici (tam se jmenuje "..._Kulisa" - PushSetup). Plati vzdy ta
    // z prave zapnute mapy (Instance se nastavi v OnEnable) - stav jizdy v MatchManageru je jeden, mapa hraje vzdy jen jedna.
    static readonly string[] RolbaNames = { "SneznaRolba", "TraktorSVlekem", "SneznaRolba_Kulisa", "TraktorSVlekem_Kulisa" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        PromptText = "";
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (System.Array.IndexOf(RolbaNames, t.name) < 0 || !t.gameObject.scene.IsValid()) continue;
            if (t.GetComponent<Tractor>() == null) t.gameObject.AddComponent<Tractor>();
            // vrata garaze v kopii mapy (PushSetup z nich RolbaGate odebral) - jen na mape, kde je i rolba
            var map = t.root;
            foreach (var gate in map.GetComponentsInChildren<Transform>(true))
                if (gate.name == "VrataRolby" && gate.GetComponent<RolbaGate>() == null)
                    gate.gameObject.AddComponent<RolbaGate>();
        }
    }

    void OnEnable() => Instance = this;

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        PromptText = "";
    }

    void Awake()
    {
        homePosition = transform.position;
        homeRotation = transform.rotation;
        GetComponentsInChildren(true, colliders);
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name.Contains("Kolo")) wheels.Add(t);
        lastPosition = transform.position;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Je bod (chodidla hrace) v obrysu traktoru?
    public bool Contains(Vector3 world)
    {
        Vector3 local = transform.InverseTransformPoint(world);
        return local.x > HullMin.x && local.x < HullMax.x && local.y > HullMin.y && local.y < HullMax.y && local.z > HullMin.z && local.z < HullMax.z;
    }

    // Dotyka se obrys rolby (predek s radlici, zadek s frezou) daneho kvadru? Body na prednim a zadnim celu.
    public bool HullTouches(Bounds bounds)
    {
        for (int face = 0; face < 2; face++)
        {
            float x = face == 0 ? HullMax.x : HullMin.x;
            for (int i = 0; i <= 4; i++)
                for (int j = 0; j <= 3; j++)
                {
                    var local = new Vector3(x, Mathf.Lerp(0.2f, 1.8f, j / 3f), Mathf.Lerp(HullMin.z, HullMax.z, i / 4f));
                    if (bounds.Contains(transform.TransformPoint(local))) return true;
                }
        }
        return false;
    }

    // Poloha traktoru, kdyz ridic stoji (chodidla) na 'feet' a diva se natocenim 'yaw' (traktor jede ve smeru pohledu).
    public static void PoseFor(Vector3 feet, float yaw, out Vector3 position, out Quaternion rotation)
    {
        rotation = Quaternion.Euler(0f, yaw - 90f, 0f);
        position = feet - rotation * CabLocal;
    }

    void Update()
    {
        var match = MatchManager.Instance;
        // (v lobby se nejezdi - i kdyby v MatchManageru zustal ridic z minule hry)
        var driver = match != null && match.IsSpawned && !match.IsLobby ? Driver(match) : null;

        if (driver != null)
        {
            PoseFor(driver.transform.position, driver.transform.eulerAngles.y, out var position, out var rotation);
            transform.SetPositionAndRotation(position, rotation);
        }
        else if (match != null && match.IsSpawned && match.tractorMoved.Value)
        {
            var pose = match.tractorPose.Value;
            transform.SetPositionAndRotation(new Vector3(pose.x, pose.y, pose.z), Quaternion.Euler(0f, pose.w, 0f));
        }
        else
            transform.SetPositionAndRotation(homePosition, homeRotation);

        bool nowDriven = driver != null;
        if (nowDriven != driven)
        {
            driven = nowDriven;
            foreach (var c in colliders)
                if (c != null) c.enabled = !driven && !wrecked;
            if (driven)
            {
                StartEngine();
                PlayStartSong();
            }
        }

        UpdateHitbox(driver, match);
        UpdateWreck(match != null && match.IsSpawned && match.tractorDestroyed.Value);

        float speed = Time.deltaTime > 0f ? Vector3.Distance(transform.position, lastPosition) / Time.deltaTime : 0f;
        float forward = Vector3.Dot(transform.position - lastPosition, transform.right) >= 0f ? 1f : -1f;
        lastPosition = transform.position;
        UpdateEffects(speed, forward);
        UpdatePrompt(match);
    }

    // ---------------- zivoty: kolize za jizdy, vrak ----------------

    // Za jizdy ma rolba vypnute kolize (ridic by se o ne zasekl), strely by jí prolétaly. Proto kvadr obrysu rolby,
    // ktery je za jizdy pripnuty k postave ridice: zbrane, projektily i vybuchy ho berou jako zasah ridice
    // (jeho Health) a Combat.DamagePlayer poskozeni prevede na rolbu. Ridicovy vlastni strely ho ignoruji (vlastni
    // NetworkObject), posadka v nem do niceho nenarazi (IgnoreCollision).
    BoxCollider hitbox;

    void UpdateHitbox(NetworkObject driver, MatchManager match)
    {
        if (driver == null)
        {
            if (hitbox != null) Destroy(hitbox.gameObject);
            hitbox = null;
            return;
        }
        if (hitbox == null || hitbox.transform.parent != driver.transform)
        {
            if (hitbox != null) Destroy(hitbox.gameObject);
            var go = new GameObject("RolbaHitbox");
            go.transform.SetParent(driver.transform, false);
            hitbox = go.AddComponent<BoxCollider>();
            hitbox.center = (HullMin + HullMax) * 0.5f;
            hitbox.size = HullMax - HullMin;
        }
        hitbox.transform.SetPositionAndRotation(transform.position, transform.rotation);
        hitbox.transform.localScale = Vector3.one;
        // posadka (a ostatni hraci - rolba je pri jizde neprojde, ty prejede server) do kvadru nenarazi
        foreach (var fpc in FirstPersonController.All)
        {
            if (fpc == null) continue;
            var body = fpc.GetComponent<CharacterController>();
            if (body != null) Physics.IgnoreCollision(hitbox, body, true);
        }
    }

    // Znicena rolba zmizi (vybuch a kour ukaze TractorDestroyedClientRpc), dokud se po 1:20 neobnovi u garaze.
    bool wrecked;
    readonly List<Renderer> wreckRenderers = new List<Renderer>();

    void UpdateWreck(bool destroyed)
    {
        if (destroyed == wrecked) return;
        wrecked = destroyed;
        if (wreckRenderers.Count == 0) GetComponentsInChildren(true, wreckRenderers);
        foreach (var r in wreckRenderers)
            if (r != null && !(r is ParticleSystemRenderer)) r.enabled = !destroyed;
        foreach (var c in colliders)
            if (c != null) c.enabled = !destroyed && !driven;
        if (destroyed)
        {
            if (engine != null) engine.Stop();
            if (exhaust != null) exhaust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (snow != null) snow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    NetworkObject Driver(MatchManager match)
    {
        ulong id = match.tractorDriver.Value;
        if (id == ulong.MaxValue) return null;
        var network = NetworkManager.Singleton;
        return network != null && network.SpawnManager != null && network.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj) ? obj : null;
    }

    // ---------------- vyzva u traktoru (mistni hrac) ----------------

    void UpdatePrompt(MatchManager match)
    {
        PromptText = "";
        if (match == null || !match.IsSpawned || match.IsLobby || match.IsOver || ReplayPlayer.Active) return;
        var network = NetworkManager.Singleton;
        var local = network != null && network.LocalClient != null ? network.LocalClient.PlayerObject : null;
        if (local == null) return;
        var fpc = local.GetComponent<FirstPersonController>();
        if (fpc == null || fpc.IsDead) return;
        bool pressed = GameSettings.CursorLocked && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;

        int left = Mathf.CeilToInt(match.tractorLeft.Value);
        if (match.IsDriving(local.NetworkObjectId))
        {
            PromptText = $"[R] Vystoupit z frézy  ·  karburátor vydrží {left} s";
            if (pressed) match.TractorRpc();
            return;
        }
        if (match.IsRiding(local.NetworkObjectId))
        {
            PromptText = $"[R] Seskočit z frézy  ·  jede ještě {left} s";
            if (pressed) match.TractorRpc();
            return;
        }

        bool nearCab = Vector3.Distance(local.transform.position, CabPosition) <= 4f;
        bool nearSeat = Vector3.Distance(local.transform.position, SeatPosition) <= 4f;
        if (!nearCab && !nearSeat) return;
        if (match.TractorRunning)
        {
            var driverObj = Driver(match);
            bool friend = driverObj != null && Combat.SameTeam(driverObj.gameObject, local.gameObject);
            if (!friend) { PromptText = "Frézu řídí nepřítel"; return; }
            if (match.tractorPassenger.Value != ulong.MaxValue) { PromptText = "Na fréze už někdo stojí"; return; }
            PromptText = "[R] Naskočit na frézu (střílíš za jízdy)";
            if (pressed && !fpc.InputBlocked) match.TractorRpc();
            return;
        }
        if (!nearCab) return;
        if (match.tractorDestroyed.Value)
        {
            PromptText = $"Fréza je zničená - nová bude za {Mathf.CeilToInt(match.tractorRespawnLeft.Value)} s";
            return;
        }
        if (!match.HasCarburetor(local.NetworkObjectId))
        {
            PromptText = "Fréza nejde nastartovat - chybí Honzův karburátor";
            return;
        }
        PromptText = "[R] Nastartovat sněžnou frézu (Honzův karburátor)";
        if (pressed && !fpc.InputBlocked) match.TractorRpc();
    }

    // ---------------- motor, kour, kola ----------------

    // Pri nastartovani rolby hraje pisnicka (Resources/Audio/rolba_tomasek) - vsem hracum na mape stejne nahlas
    // (ne prostorove). Startuje se na kazdem pocitaci zvlast, kdyz rolba dostane ridice.
    static AudioClip startSong;

    void PlayStartSong()
    {
        if (startSong == null) startSong = Resources.Load<AudioClip>("Audio/rolba_tomasek");
        if (startSong != null) ProceduralSfx.Play(startSong, CabPosition, 0.9f, 1000f, spatial: false);
    }

    void StartEngine()
    {
        if (engine == null)
        {
            engine = gameObject.AddComponent<AudioSource>();
            engine.clip = EngineClip;
            engine.loop = true;
            engine.spatialBlend = 1f;
            engine.rolloffMode = AudioRolloffMode.Linear;
            engine.minDistance = 4f;
            engine.maxDistance = 45f;
            engine.dopplerLevel = 0f;
            engine.playOnAwake = false;
        }
        if (exhaust == null)
        {
            var go = new GameObject("FX_Vyfuk");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(-0.45f, 1.9f, 0.3f);
            exhaust = go.AddComponent<ParticleSystem>();
            exhaust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = exhaust.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startColor = new Color(0.18f, 0.18f, 0.2f, 0.6f);
            main.gravityModifier = -0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = exhaust.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.05f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var size = exhaust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 2.2f)));
            var color = exhaust.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            go.GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;
        }
        if (snow == null) snow = BuildSnow();
        exhaust.Play();
        snow.Play();
        if (!engine.isPlaying) engine.Play();
        Fx.Sparks(CabPosition + Vector3.up * 1.5f, new Color(0.3f, 0.3f, 0.32f));
    }

    void UpdateEffects(float speed, float direction)
    {
        float load = Mathf.Clamp01(speed / MaxSpeed);
        if (engine != null)
        {
            engine.volume = Mathf.MoveTowards(engine.volume, driven ? (0.55f + load * 0.35f) * GameSettings.EffectsVolume : 0f, Time.deltaTime * 1.5f);
            engine.pitch = Mathf.Lerp(0.8f, 1.5f, load) + (driven ? Mathf.PerlinNoise(Time.time * 3f, 0f) * 0.05f : 0f);
            if (!driven && engine.volume <= 0.001f && engine.isPlaying) engine.Stop();
        }
        if (exhaust != null)
        {
            var emission = exhaust.emission;
            emission.rateOverTime = driven ? 10f + load * 30f : 0f;
        }
        if (snow != null)
        {
            // freza za jizdy vyhazuje snih
            var emission = snow.emission;
            emission.rateOverTime = driven && speed > 0.5f ? 25f + load * 90f : 0f;
        }

        // kola: otaceni podle ujete drahy (zadni velka, predni mala - stejna obvodova rychlost)
        wheelAngle += direction * speed * Time.deltaTime * Mathf.Rad2Deg;
        foreach (var wheel in wheels)
        {
            if (wheel == null) continue;
            float radius = Mathf.Max(0.2f, wheel.lossyScale.x * 0.5f);
            wheel.localRotation = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, -wheelAngle / radius, 0f);
        }

        // motor chvenim trese kabinou, i kdyz stoji
        if (driven && speed < 0.5f)
            transform.position += Vector3.up * Mathf.Sin(Time.time * 45f) * 0.008f;
    }

    // Hukot motoru: nizke pulzujici bruceni naftaku (generovane, bez nahravky).
    static AudioClip engineClip;
    static AudioClip EngineClip
    {
        get
        {
            var collected = CollectedAudioBank.Pick("world_groomer_engine");
            if (collected != null) return collected;
            if (engineClip != null) return engineClip;
            const int rate = 44100;
            const float seconds = 1f;
            int count = (int)(rate * seconds);
            var data = new float[count];
            var random = new System.Random(7);
            float noise = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                // dvouvalec: 24 zapaleni za sekundu, kazde jako tlumeny "puf"
                float phase = (t * 24f) % 1f;
                float puff = Mathf.Exp(-phase * 7f);
                noise = Mathf.Lerp(noise, (float)(random.NextDouble() * 2.0 - 1.0), 0.15f);
                float hum = Mathf.Sin(2f * Mathf.PI * 48f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 96f * t) * 0.2f;
                data[i] = (hum * 0.6f + noise * 0.8f) * (0.35f + puff * 0.65f) * 0.5f;
            }
            engineClip = AudioClip.Create("tractor_engine", count, 1, rate, false);
            engineClip.SetData(data, 0);
            return engineClip;
        }
    }
}
