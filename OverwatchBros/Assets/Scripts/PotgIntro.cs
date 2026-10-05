using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;

// Uvodni scenka pred "play of the game" (jako v Overwatchi): hrdina na tmave scene se svetly, kratka akce
// a dynamicky detail na obliceji s vyrazem (blendshapy MetaPerson / ARKit). Hraje se vysoko nad mapou ve vlastni
// kamere, behem uvodni karty (5 s).
//
// Pova (asset Ayran): Tomasek si v klidu sedi na bedne s pivem, kamera od nej odjede a odhali Povu, ktery prichazi
// v modrem plameni. Tomasek se otoci a vykuli oci - Pova se rozmachne a Tomasek odleti jako po odpalu (pivo zvlast).
// Pova se otoci ke kamere, prudky najezd na oblicej v mihotavem svetle ohne, drsne se zamraci - a daleko za nim
// zablika hvezdicka, jak Tomasek mizi v dalce.
// Ostatni hrdinove maji obecnou: prijdou a sebevedome se usklibnou.
[DefaultExecutionOrder(1000)]   // az po animaci postavy (vyraz a natoceni hlavy se nastavuji pres ni)
public class PotgIntro : MonoBehaviour
{
    static PotgIntro instance;

    public static bool Active => instance != null && instance.running;

    // Scena je daleko nad mapou, at do zaberu nic nezasahuje.
    static readonly Vector3 Stage = new Vector3(0f, 900f, 0f);

    bool running;
    float time;
    bool pova;
    bool preview;

    GameObject root;
    Camera view;
    readonly List<Behaviour> disabledLive = new List<Behaviour>();
    readonly List<Light> stageLights = new List<Light>();
    readonly List<float> baseIntensity = new List<float>();

    // hlavni hrdina
    Transform hero;
    CharacterVisual visual;
    HeldWeapons held;
    HeroVoice voice;
    HeroDefinition definition;
    Transform head;
    float feetOffset = -1f;
    readonly Faces heroFace = new Faces();

    Vector3 shake;
    bool spoke;
    Light fireLight;
    ParticleSystem aura;

    // ---------------- Tomasek (jen Povova scenka) ----------------
    class Puppet
    {
        public GameObject go;
        public Animator animator;
        PlayableGraph graph;
        AnimationClipPlayable playable;
        AnimationClip current;

        public void Pose(AnimationClip clip, float seconds)
        {
            if (animator == null || clip == null) return;
            if (!graph.IsValid())
            {
                graph = PlayableGraph.Create("IntroPuppet");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                AnimationPlayableOutput.Create(graph, "Out", animator);
            }
            if (clip != current)
            {
                if (playable.IsValid()) playable.Destroy();
                playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                graph.GetOutput(0).SetSourcePlayable(playable);
                current = clip;
            }
            playable.SetTime(clip.isLooping ? Mathf.Repeat(seconds, clip.length) : Mathf.Min(seconds, clip.length - 0.01f));
            graph.Evaluate(0f);
        }

        public void Destroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }

    Puppet tomasek;
    Transform tomasekHead;
    readonly Faces tomasekFace = new Faces();
    Transform beer;
    Vector3 seat;                 // kde Tomasek sedi (koren modelu)
    Quaternion seatRotation;
    bool launched, twinkled, beerFlying;
    float launchTime;
    Vector3 beerVelocity;
    Transform star;
    Light starLight;
    float starTime = -1f;
    CharacterAnimSet clips;

    static readonly Vector3 TomasekSeat = new Vector3(0f, 0f, 1.6f);
    static readonly Vector3 PovaFrom = new Vector3(-4.3f, 0f, 4.4f);
    static readonly Vector3 PovaTo = new Vector3(-0.95f, 0f, 2.05f);
    static readonly Vector3 LaunchVelocity = new Vector3(1.3f, 4.2f, 7.5f);
    const float LaunchGravity = 3.6f;

    const float PovaWalkStart = 0.95f, PovaWalkEnd = 2.1f;
    const float SwingAt = 2.12f, ImpactAt = 2.4f;
    const float TurnStart = 2.5f, TurnEnd = 2.85f;
    const float FlightSeconds = 1.0f;

    // obecna scenka
    const float GenericWalkEnd = 1.6f;

    float WhipStart => pova ? 2.68f : 2.45f;
    float WhipEnd => WhipStart + 0.4f;

    // ---------------- verejne ----------------

    // Vraci false, kdyz scenku nejde postavit (hrdina bez modelu) - pak se ukaze obycejna cerna karta.
    public static bool Play(int heroIndex)
    {
        var definition = HeroRegistry.Get(heroIndex);
        if (definition == null || definition.characterPrefab == null) return false;

        if (instance == null)
            instance = new GameObject("ReplayIntro").AddComponent<PotgIntro>();
        try
        {
            instance.preview = false;
            return instance.Begin(definition);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            instance.End();
            return false;
        }
    }

    public static void Stop()
    {
        if (instance != null)
            instance.End();
    }

    // Nahled (editor, bez hry): scenka se po dohrani sama ukonci.
    public static bool Preview(string heroAssetName)
    {
        var heroes = HeroRegistry.All;
        for (int i = 0; i < heroes.Length; i++)
            if (heroes[i] != null && heroes[i].name == heroAssetName && Play(i))
            {
                instance.preview = true;
                Debug.Log("[POTG] Ukazka uvodu: " + heroes[i].heroName);
                return true;
            }
        Debug.LogWarning("[POTG] Ukazka uvodu: hrdina " + heroAssetName + " nema model nebo neexistuje.");
        return false;
    }

    // ---------------- stavba ----------------

    bool Begin(HeroDefinition heroDefinition)
    {
        End();
        definition = heroDefinition;
        pova = heroDefinition.name == "Ayran";
        time = 0f;
        spoke = launched = twinkled = beerFlying = swungDone = false;
        starTime = -1f;
        shake = Vector3.zero;
        clips = Resources.Load<CharacterAnimSet>("Characters/CharacterAnimations");

        foreach (var player in FindObjectsByType<FirstPersonController>())
        {
            var controller = player.GetComponent<CharacterController>();
            if (controller != null)
            {
                feetOffset = controller.center.y - player.standHeight * 0.5f;
                break;
            }
        }

        root = new GameObject("ReplayIntroStage");
        BuildStage();
        BuildHero();
        if (visual == null || !visual.HasModel)
        {
            End();
            return false;
        }
        if (pova)
            BuildTomasek();
        BuildCamera();

        running = true;
        Animate(0f);
        return true;
    }

    void End()
    {
        running = false;
        if (tomasek != null) tomasek.Destroy();
        tomasek = null;
        if (root != null) Destroy(root);
        root = null;
        hero = null;
        visual = null;
        heroFace.Clear();
        tomasekFace.Clear();
        stageLights.Clear();
        baseIntensity.Clear();

        foreach (var b in disabledLive)
            if (b != null) b.enabled = true;
        disabledLive.Clear();
        view = null;
    }

    void OnDestroy()
    {
        End();
        if (instance == this) instance = null;
    }

    void BuildStage()
    {
        // Tmava leskla podlaha (kolize kvuli animaci postavy - stoji na zemi).
        var floor = Prim(PrimitiveType.Cylinder, root.transform, Stage + Vector3.down * 0.05f, new Vector3(18f, 0.05f, 18f), new Color(0.05f, 0.05f, 0.07f));
        floor.GetComponent<Renderer>().sharedMaterial.SetFloat("_Smoothness", 0.85f);

        var ground = new GameObject("Ground").AddComponent<BoxCollider>();
        ground.transform.SetParent(root.transform, false);
        ground.transform.position = Stage + Vector3.down * 0.5f;
        ground.size = new Vector3(40f, 1f, 40f);

        // Svitici kruh na scene.
        var ring = Prim(PrimitiveType.Cylinder, root.transform, Stage + new Vector3(pova ? -0.4f : 0f, 0.01f, pova ? 1.8f : 1.5f),
            new Vector3(pova ? 3.6f : 2.6f, 0.005f, pova ? 3.6f : 2.6f), new Color(1f, 0.45f, 0.1f));
        var ringMaterial = ring.GetComponent<Renderer>().sharedMaterial;
        ringMaterial.EnableKeyword("_EMISSION");
        ringMaterial.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.05f) * 2.2f);

        // Svetla: hlavni zepredu, dve barevna obrysova zezadu, slabe doplnkove.
        Light(new Vector3(-2.2f, 3.2f, -1.8f), new Color(1f, 0.92f, 0.85f), 10f, 10f);
        Light(new Vector3(-2.8f, 2.2f, 5f), new Color(1f, 0.45f, 0.12f), 18f, 9f);
        Light(new Vector3(2.8f, 2.4f, 4.6f), new Color(0.25f, 0.55f, 1f), 18f, 9f);
        Light(new Vector3(2f, 0.6f, -2f), new Color(0.6f, 0.7f, 1f), 2f, 6f);

        // Stoupajici jiskry kolem.
        var embers = new GameObject("Embers").AddComponent<ParticleSystem>();
        embers.transform.SetParent(root.transform, false);
        embers.transform.position = Stage + new Vector3(0f, 0f, 2f);
        embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = embers.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.07f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.2f), new Color(1f, 0.3f, 0.05f));
        main.gravityModifier = -0.15f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = embers.emission;
        emission.rateOverTime = 60f;
        var shape = embers.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(9f, 0.5f, 7f);
        embers.GetComponent<ParticleSystemRenderer>().material = Fx.ParticleMaterial;
        embers.Play();

        // Svetlo ohne pro detail obliceje.
        fireLight = new GameObject("FireLight").AddComponent<Light>();
        fireLight.transform.SetParent(root.transform, false);
        fireLight.type = LightType.Point;
        fireLight.color = new Color(1f, 0.5f, 0.15f);
        fireLight.range = 4f;
        fireLight.intensity = 0f;
        fireLight.shadows = LightShadows.None;
    }

    void Light(Vector3 offset, Color color, float intensity, float range)
    {
        var light = new GameObject("Light").AddComponent<Light>();
        light.transform.SetParent(root.transform, false);
        light.transform.position = Stage + offset;
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
        stageLights.Add(light);
        baseIntensity.Add(intensity);
    }

    static GameObject Prim(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = scale;
        Fx.Paint(go, color);
        return go;
    }

    void BuildHero()
    {
        var go = new GameObject("ReplayIntroHero");
        go.transform.SetParent(root.transform, false);
        hero = go.transform;
        if (pova)
            hero.SetPositionAndRotation(Stage + PovaFrom, Quaternion.LookRotation(Flat(PovaTo - PovaFrom)));
        else
            hero.SetPositionAndRotation(Stage + new Vector3(0f, 0f, 5f), Quaternion.Euler(0f, 180f, 0f));

        visual = go.AddComponent<CharacterVisual>();
        visual.IsGhost = true;
        visual.SetFeetOffset(feetOffset);
        visual.SetModel(definition.characterPrefab, definition.tintCharacter ? definition.color : Color.white);
        held = go.AddComponent<HeldWeapons>();
        held.SetGhost(definition, false, null);
        voice = go.AddComponent<HeroVoice>();

        if (!visual.HasModel) return;
        head = visual.GetBone(HumanBodyBones.Head);
        heroFace.Collect(visual.ModelRoot);

        if (pova)
        {
            aura = Fx.CreateAura(hero, new Color(0.3f, 0.55f, 1f));
            aura.Play();
        }
    }

    // Tomasek (model Honzy) sedi na bedne s pivem.
    void BuildTomasek()
    {
        HeroDefinition honza = null;
        foreach (var h in HeroRegistry.All)
            if (h != null && h.name == "Honza") honza = h;
        if (honza == null || honza.characterPrefab == null || clips == null || clips.sitIdle == null) return;

        var go = Instantiate(honza.characterPrefab, root.transform);
        go.name = "Tomasek";
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        seat = Stage + TomasekSeat;
        seatRotation = Quaternion.Euler(0f, 180f, 0f);
        go.transform.SetPositionAndRotation(seat, seatRotation);

        var animator = go.GetComponentInChildren<Animator>();
        if (animator == null) { Destroy(go); return; }
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        tomasek = new Puppet { go = go, animator = animator };
        tomasek.Pose(clips.sitIdle, 0f);
        tomasekHead = animator.GetBoneTransform(HumanBodyBones.Head);
        tomasekFace.Collect(go.transform);

        // Bedna pod zadkem (podle polohy boku v sedici poze).
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Vector3 hipsAt = hips != null ? hips.position : seat + new Vector3(0f, 0.55f, 0.1f);
        float top = Mathf.Max(0.25f, hipsAt.y - Stage.y - 0.1f);
        var crate = Prim(PrimitiveType.Cube, root.transform, new Vector3(hipsAt.x, Stage.y + top * 0.5f, hipsAt.z + 0.05f),
            new Vector3(0.62f, top, 0.52f), new Color(0.45f, 0.3f, 0.16f));
        crate.name = "Bedna";
        foreach (float y in new[] { 0.25f, -0.25f })
            Prim(PrimitiveType.Cube, crate.transform, crate.transform.position + new Vector3(0f, top * y, -0.262f),
                new Vector3(0.64f, 0.05f, 0.01f), new Color(0.3f, 0.2f, 0.1f)).transform.SetParent(crate.transform, true);

        // Pivo v prave ruce: sklenice se zlatym pivem a penou.
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand != null)
        {
            beer = new GameObject("Pivo").transform;
            beer.SetParent(hand, false);
            beer.localPosition = new Vector3(0.02f, -0.08f, 0.05f);
            beer.rotation = Quaternion.identity;
            var glass = Prim(PrimitiveType.Cylinder, beer, beer.position, new Vector3(0.08f, 0.07f, 0.08f), new Color(0.95f, 0.65f, 0.12f));
            glass.transform.SetParent(beer, true);
            var foam = Prim(PrimitiveType.Cylinder, beer, beer.position + Vector3.up * 0.075f, new Vector3(0.082f, 0.012f, 0.082f), new Color(0.98f, 0.97f, 0.92f));
            foam.transform.SetParent(beer, true);
        }

        // Hvezdicka, ktera zablika, kdyz Tomasek zmizi v dalce.
        star = Prim(PrimitiveType.Sphere, root.transform, Stage, Vector3.zero, Color.white).transform;
        var starMaterial = star.GetComponent<Renderer>().sharedMaterial;
        starMaterial.EnableKeyword("_EMISSION");
        starMaterial.SetColor("_EmissionColor", new Color(1f, 0.95f, 0.8f) * 6f);
        starLight = star.gameObject.AddComponent<Light>();
        starLight.type = LightType.Point;
        starLight.color = new Color(1f, 0.95f, 0.8f);
        starLight.range = 6f;
        starLight.intensity = 0f;
        star.gameObject.SetActive(false);
    }

    void BuildCamera()
    {
        Camera live = null;
        foreach (var cam in FindObjectsByType<Camera>())
            if (cam.enabled && cam.gameObject.activeInHierarchy && cam.targetTexture == null)
            {
                // (kamera menu nic ze sceny nekresli - jeji nastaveni se nekopiruje)
                if (cam.GetComponent<MenuCamera>() == null && (live == null || cam == Camera.main)) live = cam;
                cam.enabled = false;
                disabledLive.Add(cam);
            }
        foreach (var listener in FindObjectsByType<AudioListener>())
            if (listener.enabled)
            {
                listener.enabled = false;
                disabledLive.Add(listener);
            }

        var camObject = new GameObject("Camera");
        camObject.transform.SetParent(root.transform, false);
        camObject.tag = "MainCamera";
        view = camObject.AddComponent<Camera>();
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color(0.02f, 0.02f, 0.035f);
        view.nearClipPlane = 0.05f;
        view.farClipPlane = 200f;
        var data = view.GetUniversalAdditionalCameraData();
        if (live != null)
        {
            view.cullingMask = live.cullingMask;
            var liveData = live.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = liveData.renderPostProcessing;
            data.antialiasing = liveData.antialiasing;
        }
        else
        {
            // Ukazka z menu (zadna herni kamera): aspon post-processing, at svetla a ohen zari.
            data.renderPostProcessing = true;
        }
        camObject.AddComponent<AudioListener>();
    }

    // ---------------- choreografie ----------------

    void Update()
    {
        if (!running) return;
        time += Time.unscaledDeltaTime;
        Animate(time);

        if (preview && time > 5.6f)
        {
            preview = false;
            End();
        }
    }

    void Animate(float t)
    {
        if (hero == null || view == null) return;

        shake = Vector3.Lerp(shake, Vector3.zero, 1f - Mathf.Exp(-11f * Time.unscaledDeltaTime));
        Vector3 jitter = shake.sqrMagnitude > 1e-6f
            ? new Vector3(Mathf.Sin(t * 91f) * shake.x, Mathf.Sin(t * 77f + 1f) * shake.y, 0f) : Vector3.zero;

        Vector3 widePos;
        Quaternion wideRot;
        float wideFov;
        if (pova)
            AnimatePovaScene(t, out widePos, out wideRot, out wideFov);
        else
            AnimateGeneric(t, out widePos, out wideRot, out wideFov);

        // Prudky najezd na oblicej (s naklonem) a pomale dotlaceni.
        float rootY = -feetOffset;
        Vector3 chest = hero.position - Vector3.up * rootY + Vector3.up * 1.15f;
        Vector3 face = head != null ? head.position + Vector3.up * 0.06f : chest + Vector3.up * 0.55f;
        float push = Mathf.Clamp01((t - WhipEnd) / 2.2f);
        Vector3 closePos = face + hero.forward * Mathf.Lerp(0.72f, 0.56f, push) + hero.right * 0.12f + Vector3.up * 0.02f;
        Quaternion closeRot = Quaternion.LookRotation(face - closePos) * Quaternion.Euler(0f, 0f, pova ? Mathf.Lerp(9f, 5f, push) : -4f);
        float closeFov = pova ? 30f : 32f;

        float whip = EaseOutCubic(Mathf.Clamp01((t - WhipStart) / (WhipEnd - WhipStart)));
        view.transform.SetPositionAndRotation(Vector3.Lerp(widePos, closePos, whip) + jitter, Quaternion.Slerp(wideRot, closeRot, whip));
        view.fieldOfView = Mathf.Lerp(wideFov, closeFov, whip);

        // Mihotave oranzove svetlo ohne na obliceji v detailu.
        if (fireLight != null)
        {
            float on = Mathf.Clamp01((t - (WhipStart - 0.1f)) / 0.3f);
            fireLight.transform.position = face + hero.forward * 0.6f - hero.right * 0.5f - Vector3.up * 0.25f;
            fireLight.intensity = on * (2.2f + Mathf.PerlinNoise(t * 9f, 0.3f) * 2.2f);
        }

        // Hlaska v detailu (Pova drsna hlaska po zabiti, ostatni po oziveni).
        if (!spoke && t >= WhipEnd + 0.15f)
        {
            spoke = true;
            var kind = pova ? VoiceKind.Kill : VoiceKind.Spawn;
            if (HeroVoice.HasLines(definition, kind, 0))
                voice.Play(definition, kind, 0, Random.Range(0, 1000));
        }
    }

    void AnimateGeneric(float t, out Vector3 widePos, out Quaternion wideRot, out float wideFov)
    {
        float rootY = -feetOffset;
        float walk = Mathf.Clamp01(t / GenericWalkEnd);
        hero.position = Stage + new Vector3(0f, 0f, Mathf.Lerp(5f, 1.5f, walk)) + Vector3.up * rootY;

        Vector3 chest = hero.position - Vector3.up * rootY + Vector3.up * 1.15f;
        widePos = Vector3.Lerp(Stage + new Vector3(1.3f, 0.9f, -2.6f), Stage + new Vector3(0.9f, 1.0f, -1.6f), Smooth(Mathf.Clamp01(t / WhipStart)));
        wideRot = Quaternion.LookRotation(chest - widePos);
        wideFov = 50f;
    }

    void AnimatePovaScene(float t, out Vector3 widePos, out Quaternion wideRot, out float wideFov)
    {
        float rootY = -feetOffset;

        // Pova prichazi k Tomaskovi; po uderu se otoci ke kamere.
        float walk = Mathf.Clamp01((t - PovaWalkStart) / (PovaWalkEnd - PovaWalkStart));
        hero.position = Vector3.Lerp(Stage + PovaFrom, Stage + PovaTo, Smooth(walk)) + Vector3.up * rootY;
        Quaternion toTomasek = Quaternion.LookRotation(Flat(TomasekSeat - PovaTo));
        Quaternion toCamera = Quaternion.LookRotation(Flat(new Vector3(0.6f, 0f, -2.5f) - PovaTo));
        float turn = Smooth(Mathf.Clamp01((t - TurnStart) / (TurnEnd - TurnStart)));
        hero.rotation = walk < 1f ? Quaternion.LookRotation(Flat(PovaTo - PovaFrom)) : Quaternion.Slerp(toTomasek, toCamera, turn);
        if (walk >= 1f && t < TurnStart)
            hero.rotation = Quaternion.Slerp(Quaternion.LookRotation(Flat(PovaTo - PovaFrom)), toTomasek, Mathf.Clamp01((t - PovaWalkEnd) / 0.08f));

        // Rozmach a odpal.
        if (!swungDone && t >= SwingAt)
        {
            swungDone = true;
            held.Swing();
            ProceduralSfx.Play(ProceduralSfx.Dash, hero.position + Vector3.up, 1f);
        }
        if (!launched && t >= ImpactAt)
            Launch(t);

        // Tomasek: sedi, pak let s rotaci, pak hvezdicka v dalce.
        if (tomasek != null && tomasek.go != null)
        {
            if (!launched)
            {
                tomasek.Pose(clips.sitIdle, t);
            }
            else
            {
                float dt = t - launchTime;
                tomasek.Pose(clips.hitChest != null ? clips.hitChest : clips.sitIdle, dt * 1.2f);
                Vector3 p = seat + LaunchVelocity * dt + Vector3.down * (0.5f * LaunchGravity * dt * dt);
                tomasek.go.transform.position = p;
                tomasek.go.transform.rotation = Quaternion.AngleAxis(dt * 760f, new Vector3(1f, 0.35f, 0.2f).normalized) * seatRotation;
                tomasek.go.transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(dt / FlightSeconds));

                if (!twinkled && dt >= FlightSeconds)
                {
                    twinkled = true;
                    tomasek.go.SetActive(false);
                    if (star != null)
                    {
                        star.position = p + Vector3.up * 0.5f;
                        star.gameObject.SetActive(true);
                        starTime = t;
                        Fx.Sparks(star.position, Color.white);
                        Fx.PlayGlobal(ProceduralSfx.StunConfirm, 0.7f);
                    }
                }
            }
        }

        // Pivo leti zvlast a toci se.
        if (beerFlying && beer != null)
        {
            beerVelocity += Vector3.down * 9f * Time.unscaledDeltaTime;
            beer.position += beerVelocity * Time.unscaledDeltaTime;
            beer.Rotate(new Vector3(540f, 120f, 300f) * Time.unscaledDeltaTime, Space.World);
            if (beer.position.y < Stage.y - 2f) beer.gameObject.SetActive(false);
        }

        // Hvezdicka: rychle zablika a zhasne.
        if (starTime >= 0f && star != null)
        {
            float s = Mathf.Clamp01((t - starTime) / 0.6f);
            float pulse = Mathf.Sin(s * Mathf.PI) * (1f + 0.3f * Mathf.Sin(t * 60f));
            star.localScale = Vector3.one * 0.35f * pulse;
            star.Rotate(0f, 0f, 400f * Time.unscaledDeltaTime);
            starLight.intensity = 6f * pulse;
            if (s >= 1f) star.gameObject.SetActive(false);
        }

        // Svetla pri uderu vzplanou a pak se vrati.
        float flare = launched ? 1f + 1.1f * Mathf.Exp(-(t - launchTime) * 4f) : 1f;
        for (int i = 0; i < stageLights.Count; i++)
            stageLights[i].intensity = baseIntensity[i] * flare;

        // Kamera: detail na sediciho Tomaska, ktery se odjezdem rozsiri na celou scenu.
        Vector3 tomasekHeadPos = tomasekHead != null && !launched ? tomasekHead.position : Stage + TomasekSeat + Vector3.up * 1.25f;
        Vector3 closeOnTomasek = tomasekHeadPos + new Vector3(0.18f, -0.02f, -1.05f);
        Vector3 wide = Stage + new Vector3(0.9f, 1.35f, -3.1f);
        Vector3 middle = Stage + new Vector3(-0.45f, 1.0f, 1.9f);
        float pull = Smooth(Mathf.Clamp01((t - 0.75f) / 1.25f));
        widePos = Vector3.Lerp(closeOnTomasek, wide, pull);
        wideRot = Quaternion.Slerp(Quaternion.LookRotation(tomasekHeadPos - closeOnTomasek), Quaternion.LookRotation(middle - widePos), pull);
        wideFov = Mathf.Lerp(34f, 52f, pull);
    }

    bool swungDone;

    void Launch(float t)
    {
        launched = true;
        launchTime = t;

        Vector3 hit = tomasekHead != null ? tomasekHead.position + Vector3.down * 0.45f : Stage + TomasekSeat + Vector3.up;
        Fx.Sparks(hit, new Color(1f, 0.55f, 0.15f));
        Fx.BulletImpact(hit, new Color(1f, 0.5f, 0.1f), 3f);
        Fx.Explosion(Stage + TomasekSeat + Vector3.up * 0.3f, 1.6f);
        ProceduralSfx.Play(ProceduralSfx.Hit, hit, 1f);
        ProceduralSfx.Play(ProceduralSfx.Explosion, hit, 0.8f);
        shake = new Vector3(0.09f, 0.07f, 0f);
        if (aura != null) aura.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        if (beer != null)
        {
            beer.SetParent(root.transform, true);
            beerVelocity = new Vector3(-0.6f, 3.4f, 2.2f);
            beerFlying = true;
            Fx.Sparks(beer.position, new Color(1f, 0.75f, 0.2f));   // kapky piva
        }
    }

    void LateUpdate()
    {
        if (!running || hero == null) return;

        // Vyraz hlavniho hrdiny: v detailu nabehne behem 0.35 s.
        float k = Mathf.Clamp01((time - (WhipStart + 0.2f)) / 0.35f);
        if (pova)
        {
            heroFace.Set("browDownLeft", 100f * k);
            heroFace.Set("browDownRight", 100f * k);
            heroFace.Set("eyeSquintLeft", 70f * k);
            heroFace.Set("eyeSquintRight", 70f * k);
            heroFace.Set("noseSneerLeft", 45f * k);
            heroFace.Set("noseSneerRight", 35f * k);
            heroFace.Set("mouthFrownLeft", 45f * k);
            heroFace.Set("mouthFrownRight", 45f * k);
            heroFace.Set("mouthPressLeft", 50f * k);
            heroFace.Set("mouthPressRight", 50f * k);
            heroFace.Set("jawForward", 25f * k);
            heroFace.Set("cheekSquintLeft", 30f * k);
            heroFace.Set("cheekSquintRight", 30f * k);
        }
        else
        {
            heroFace.Set("mouthSmileLeft", 65f * k);
            heroFace.Set("mouthSmileRight", 20f * k);
            heroFace.Set("browOuterUpLeft", 35f * k);
            heroFace.Set("eyeSquintLeft", 25f * k);
            heroFace.Set("eyeSquintRight", 25f * k);
            heroFace.Set("cheekSquintLeft", 30f * k);
        }

        // Mrknuti na konci, at oblicej neni jako socha.
        float blink = Mathf.Clamp01(1f - Mathf.Abs(time - 4.35f) / 0.08f);
        heroFace.Set("eyeBlinkLeft", 100f * blink);
        heroFace.Set("eyeBlinkRight", 100f * blink);

        if (head != null)
        {
            // Pova: skloni bradu (zamraceny pohled zpod obocí), na konci ji vyzyvave zvedne.
            float chinUp = pova ? Smooth(Mathf.Clamp01((time - 4.1f) / 0.25f)) : 0f;
            float tilt = (pova ? Mathf.Lerp(10f, -6f, chinUp) : -5f) * k;
            head.rotation = Quaternion.AngleAxis(tilt, hero.right) * head.rotation;
        }

        // Tomasek: vsimne si Povy - otoci hlavu a vykuli oci; pri odpalu bolestiva grimasa.
        if (tomasek != null && tomasek.go != null && tomasek.go.activeSelf)
        {
            float notice = Smooth(Mathf.Clamp01((time - 1.55f) / 0.25f));
            if (!launched)
            {
                tomasekFace.Set("eyeWideLeft", 100f * notice);
                tomasekFace.Set("eyeWideRight", 100f * notice);
                tomasekFace.Set("browInnerUp", 90f * notice);
                tomasekFace.Set("browOuterUpLeft", 60f * notice);
                tomasekFace.Set("browOuterUpRight", 60f * notice);
                tomasekFace.Set("jawOpen", 35f * notice);
                tomasekFace.Set("mouthFunnel", 30f * notice);
                if (tomasekHead != null)
                    tomasekHead.rotation = Quaternion.AngleAxis(-38f * notice, Vector3.up) * tomasekHead.rotation;
            }
            else
            {
                tomasekFace.Set("eyeWideLeft", 0f);
                tomasekFace.Set("eyeWideRight", 0f);
                tomasekFace.Set("eyeBlinkLeft", 100f);
                tomasekFace.Set("eyeBlinkRight", 100f);
                tomasekFace.Set("jawOpen", 70f);
                tomasekFace.Set("mouthStretchLeft", 60f);
                tomasekFace.Set("mouthStretchRight", 60f);
                tomasekFace.Set("browDownLeft", 70f);
                tomasekFace.Set("browDownRight", 70f);
            }
        }
    }

    // ---------------- pomocne ----------------

    // Blendshapy obliceje modelu podle jmena (ARKit): jmeno -> (renderer, index).
    class Faces
    {
        readonly Dictionary<string, List<(SkinnedMeshRenderer renderer, int index)>> shapes =
            new Dictionary<string, List<(SkinnedMeshRenderer, int)>>();

        public void Clear() => shapes.Clear();

        public void Collect(Transform model)
        {
            shapes.Clear();
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    int dot = name.LastIndexOf('.');
                    string key = (dot >= 0 ? name.Substring(dot + 1) : name).ToLowerInvariant();
                    if (!shapes.TryGetValue(key, out var list))
                        shapes[key] = list = new List<(SkinnedMeshRenderer, int)>();
                    list.Add((renderer, i));
                }
            }
        }

        public void Set(string name, float weight)
        {
            if (!shapes.TryGetValue(name.ToLowerInvariant(), out var list)) return;
            foreach (var (renderer, index) in list)
                if (renderer != null)
                    renderer.SetBlendShapeWeight(index, weight);
        }
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v : Vector3.forward;
    }

    static float Smooth(float x) => x * x * (3f - 2f * x);
    static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);
}
