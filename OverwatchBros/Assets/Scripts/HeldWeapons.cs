using UnityEngine;

// Zbrane drzene v rukou (1 nebo 2 podle WeaponDefinition.dualWield). Majitel je vidi u kamery (first person),
// ostatni na tele hrace. Model se sklada z kostek (placeholder), nebo se pouzije WeaponDefinition.heldPrefab.
public partial class HeldWeapons : MonoBehaviour
{
    PlayerHero heroComponent;
    PlayerHero CurrentHeroComponent => heroComponent != null ? heroComponent : (heroComponent = GetComponent<PlayerHero>());
    class Hand
    {
        public Transform holder;
        public RevolverVisual revolver;
        public float side;
        public Vector3 restPosition;
        public Vector3 restEuler;
        public float swingTime = 99f;

        // Ruka v pohledu z prvni osoby: pest drzi zbran, predlokti vede od lokte (mimo obraz) k pesti.
        public Transform forearm;
        public Vector3 grip;

        // Zbran obema rukama: leva ruka drzi predpazbi (mistni bod v drzaku zbrane).
        public bool twoHanded;
        public Vector3 foregrip;
        public bool foregripContact;   // foregrip je bod na povrchu zbrane (znacka "Foregrip"), dlan se k nemu prilozi
        public Transform foregripMarker;   // znacka "Foregrip" (muze se hybat - napr. hlaven granatometu)
        // postava se zbrani obema rukama: poza strelby (mistni vuci pateri) - klid je idlePosition / idleRotation
        public bool hasAimPose;
        public Vector3 aimPosition;
        public Quaternion aimRotation;
        public Transform forearm2;

        // Zbrane v rukou postavy: poza v klidu a v bloku (mistni vuci kosti ruky).
        public Vector3 idlePosition, blockPosition;
        public Quaternion idleRotation, blockRotation;
        public bool hasBlockPose;

        // Luk: sip na tetive (pri natahovani se posouva dozadu).
        public Transform arrow;
        public Vector3 arrowRest, arrowBack;
        // konec sipu u tetivy (mistni v drzaku) - tam pri natahovani drzi prava ruka
        public Vector3 nockLocal;
        public bool hasNock;
        // luk v leve ruce postavy (ostatni hraci): leva paze drzi madlo, prava tahne tetivu (IK po animaci)
        public bool bowBody;
    }

    // Jak daleko se sip pri plnem natazeni posune dozadu (m) a jak dlouho po vystrelu chybi.
    const float DrawLength = 0.24f;
    // luk v leve ruce u kamery: pri plnem natazeni je prava ruka stazena k obliceji (pod obrazem, jako Hanzo),
    // sip vede od zamerovace dolu
    const float CameraDrawLength = 0.34f;
    static float CameraDrawFor(float charge) => CameraDrawLength * Mathf.SmoothStep(0f, 1f, charge);
    const float ArrowHiddenAfterShot = 0.3f;
    float lastCharge;
    float arrowHiddenUntil;

    WeaponShooting shooting;
    FirstPersonController fpc;
    CharacterVisual visual;
    int builtVersion;

    WeaponDefinition builtWeapon;
    bool builtAsOwner;
    bool built;
    GameObject root;
    Hand[] hands = new Hand[0];
    Hand[] bodyHands = new Hand[0];

    // Rychlopalba (Mirek, PTM): luk v ruce zari modre (jako Hanzovy Storm Arrows) - jen svitici material luku.
    static readonly Color StormColor = new Color(0.25f, 0.65f, 1f, 1f);
    RapidFireAbility rapidAbility;
    System.Collections.Generic.List<Material> stormMaterials;
    bool stormShown;

    // Posileny vystrel (Sindel): zbran v ruce rudne, kdyz je dalsi vystrel posileny (majitel), nebo kratce
    // pri posilenem vystrelu (ostatni hraci).
    System.Collections.Generic.List<Material> powerMaterials;
    bool powerShown;

    void UpdatePowerGlow()
    {
        var weapon = CurrentWeapon;
        if (weapon == null || weapon.powerShotEvery <= 0 || ghost) { powerShown = false; return; }
        bool on = Time.time < powerFlashUntil || (shooting != null && shooting.IsOwner && shooting.Bot == null && shooting.PowerShotReady);
        if (!on && !powerShown) return;
        if (powerMaterials == null)
        {
            powerMaterials = new System.Collections.Generic.List<Material>();
            foreach (var group in new[] { hands, bodyHands })
                foreach (var hand in group)
                    if (hand.holder != null)
                        foreach (var renderer in hand.holder.GetComponentsInChildren<Renderer>(true))
                            if (!(renderer is ParticleSystemRenderer))
                                foreach (var material in renderer.materials)
                                {
                                    material.EnableKeyword("_EMISSION");
                                    powerMaterials.Add(material);
                                }
        }
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 18f);
        Color emission = on ? weapon.powerShotColor * (1.6f * pulse) : Color.black;
        foreach (var material in powerMaterials)
            if (material != null) material.SetColor("_EmissionColor", emission);
        powerShown = on;
    }

    // Odveta (Pova): sekyrky zhnou podle hnevu z bloku (vidi vsichni). Slabe oranzove -> od pripravenosti pulzuji
    // (cim vic hnevu, tim rychleji) -> pri ~60 % hnevu z cepeli slehaji plameny. V okne na Odvetu (tesne po bloku)
    // zablesknou naplno. Puvodni emise materialu se po vyprchani vrati.
    static readonly Color RageGlowColor = new Color(1f, 0.38f, 0.06f, 1f);
    BlockAbility rageBlock;
    System.Collections.Generic.List<Material> rageMaterials;
    System.Collections.Generic.List<(Texture map, Color color, bool keyword)> rageOriginals;
    System.Collections.Generic.List<ParticleSystem> rageFlames;
    bool rageShown;
    float rageLevel;

    void UpdateRageGlow()
    {
        if (rageBlock == null) rageBlock = GetComponent<BlockAbility>();
        float target = !ghost && rageBlock != null && rageBlock.HasCounter ? rageBlock.Rage / BlockAbility.MaxRage : 0f;
        rageLevel = Mathf.MoveTowards(rageLevel, target, Time.deltaTime * 2.5f);
        bool on = rageLevel > 0.01f;
        if (!on && !rageShown) return;

        if (rageMaterials == null)
        {
            rageMaterials = new System.Collections.Generic.List<Material>();
            rageOriginals = new System.Collections.Generic.List<(Texture map, Color color, bool keyword)>();
            rageFlames = new System.Collections.Generic.List<ParticleSystem>();
            for (int g = 0; g < 2; g++)
                foreach (var hand in g == 0 ? hands : bodyHands)
                {
                    if (hand.holder == null) continue;
                    // plameny jen u sekyrek postavy (vidi ostatni / 3. osoba) - u kamery by zakryvaly vyhled
                    bool flames = g == 1 || (visual == null || !visual.HasModel) && !builtAsOwner;
                    var bounds = new Bounds();
                    bool any = false;
                    foreach (var renderer in hand.holder.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer is ParticleSystemRenderer) continue;
                        if (any) bounds.Encapsulate(renderer.bounds); else { bounds = renderer.bounds; any = true; }
                        foreach (var material in renderer.materials)
                        {
                            rageOriginals.Add((material.HasProperty("_EmissionMap") ? material.GetTexture("_EmissionMap") : null,
                                material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black,
                                material.IsKeywordEnabled("_EMISSION")));
                            rageMaterials.Add(material);
                        }
                    }
                    // plameny u cepele: horni cast zbrane (stred zbrane posunuty od uchopu dal)
                    if (any && flames)
                    {
                        Vector3 center = hand.holder.InverseTransformPoint(bounds.center);
                        Vector3 head = center * 1.35f;
                        rageFlames.Add(CreateRageFlame(hand.holder, head, bounds.extents.magnitude));
                    }
                }
            if (rageMaterials.Count == 0) { rageMaterials = null; return; }   // zbrane jeste nejsou postavene - zkusit znovu
        }

        bool ready = rageBlock != null && rageBlock.CounterReady;
        bool window = false;   // (drive okno po bloku - ted sekyrky drzi energii do prvniho zasahu)
        float pulse = ready ? 0.75f + 0.25f * Mathf.Sin(Time.time * Mathf.Lerp(5f, 16f, rageLevel)) : 1f;
        // (zare pres texturu sekyrek - detaily zustanou, jen rozzhavene; s hnevem jasnejsi)
        float intensity = on ? Mathf.Lerp(0.12f, 1.6f, rageLevel) * pulse * (window ? 1.4f : 1f) : 0f;
        for (int i = 0; i < rageMaterials.Count; i++)
        {
            var material = rageMaterials[i];
            if (material == null) continue;
            var original = rageOriginals[i];
            if (on)
            {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionMap"))
                {
                    var baseMap = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap")
                        : material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                    material.SetTexture("_EmissionMap", baseMap != null ? baseMap : Texture2D.whiteTexture);
                }
                material.SetColor("_EmissionColor", RageGlowColor * intensity);
            }
            else
            {
                if (material.HasProperty("_EmissionMap")) material.SetTexture("_EmissionMap", original.map);
                material.SetColor("_EmissionColor", original.color);
                if (!original.keyword) material.DisableKeyword("_EMISSION");
            }
        }
        // plameny od ~55 % hnevu, s hnevem silni
        float flame = Mathf.Clamp01((rageLevel - 0.55f) / 0.45f);
        foreach (var ps in rageFlames)
        {
            if (ps == null) continue;
            var emission = ps.emission;
            emission.rateOverTime = flame > 0f ? Mathf.Lerp(8f, 45f, flame) * (window ? 1.5f : 1f) : 0f;
            if (flame > 0f && !ps.isPlaying) ps.Play();
            else if (flame <= 0f && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        rageShown = on;
    }

    static ParticleSystem CreateRageFlame(Transform parent, Vector3 localPosition, float size)
    {
        var go = new GameObject("FX_RageFlame");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        float s = Mathf.Clamp(size, 0.1f, 0.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(s * 0.25f, s * 0.5f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.3f, 1f), new Color(1f, 0.3f, 0.02f, 1f));
        main.gravityModifier = -0.6f;   // plameny stoupaji
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = s * 0.35f;
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.9f, 0.55f), 0f), new GradientColorKey(new Color(1f, 0.3f, 0f), 0.6f), new GradientColorKey(new Color(0.3f, 0.05f, 0f), 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(gradient);
        var sizeOver = ps.sizeOverLifetime;
        sizeOver.enabled = true;
        sizeOver.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = Fx.ParticleMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ps;
    }

    void UpdateStorm()
    {
        if (builtModel != HeldModel.Bow || hands.Length == 0 || hands[0].holder == null) return;
        if (rapidAbility == null) rapidAbility = GetComponent<RapidFireAbility>();
        bool on = !ghost && rapidAbility != null && rapidAbility.enabled && rapidAbility.IsActive;
        if (!on && !stormShown) return;

        var holder = hands[0].holder;
        if (stormMaterials == null)
        {
            stormMaterials = new System.Collections.Generic.List<Material>();
            stormOriginals = new System.Collections.Generic.List<(Texture map, Color color, bool keyword)>();
            foreach (var renderer in holder.GetComponentsInChildren<Renderer>(true))
                if (!(renderer is ParticleSystemRenderer))
                    foreach (var material in renderer.materials)
                    {
                        // puvodni emise (Black Bow ma vlastni emisni texturu se svitivymi detaily) - po salve se vrati
                        stormOriginals.Add((material.HasProperty("_EmissionMap") ? material.GetTexture("_EmissionMap") : null,
                            material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black,
                            material.IsKeywordEnabled("_EMISSION")));
                        stormMaterials.Add(material);
                    }
        }
        float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 14f);
        // (jen luk sam - zare a svetlo kolem by hraci zakryvaly vyhled)
        for (int i = 0; i < stormMaterials.Count; i++)
        {
            var material = stormMaterials[i];
            if (material == null) continue;
            var original = stormOriginals[i];
            if (on)
            {
                // emisni textura by modrou nasobila (u tmave textury by nebyla videt) - po dobu salvy bila
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionMap")) material.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                material.SetColor("_EmissionColor", StormColor * (1.1f * pulse));
            }
            else
            {
                if (material.HasProperty("_EmissionMap")) material.SetTexture("_EmissionMap", original.map);
                material.SetColor("_EmissionColor", original.color);
                if (!original.keyword) material.DisableKeyword("_EMISSION");
            }
        }
        stormShown = on;
    }
    System.Collections.Generic.List<(Texture map, Color color, bool keyword)> stormOriginals;
    FirstPersonArms.Arm bodySupportArm;   // leva paze postavy u zbrane obema rukama
    FirstPersonArms.Arm bodyMainArm;      // prava paze (rukojet) u zbrane obema rukama

    // Puska obema rukama u postavy: nizka pohotovost sikmo pres telo (jako Soldier), pripevnena k pateri
    // (hybe se s telem). False = model nema kosti pateri.
    bool AttachAtHip(Hand hand)
    {
        var spine = visual.GetBone(HumanBodyBones.Spine);
        var hips = visual.GetBone(HumanBodyBones.Hips);
        if (spine == null || hips == null) return false;
        var body = visual.ModelRoot;
        Vector3 hipLocal = body.InverseTransformPoint(hips.position);
        // strelba: puska pred telem ve vysce pasu / hrudi, hlaven dopredu (kam postava strili)
        hand.holder.position = body.TransformPoint(new Vector3(0.16f, hipLocal.y + 0.42f, 0.32f));
        hand.holder.rotation = body.rotation * Quaternion.Euler(-2f, -4f, 0f);
        hand.holder.SetParent(spine, true);
        hand.aimPosition = hand.holder.localPosition;
        hand.aimRotation = hand.holder.localRotation;
        hand.holder.SetParent(null, true);

        // klid: nizka pohotovost jako Soldier - rukojet u praveho boku, hlaven sikmo pres telo doleva dopredu a dolu
        hand.holder.position = body.TransformPoint(new Vector3(0.17f, hipLocal.y + 0.2f, 0.17f));
        hand.holder.rotation = body.rotation * Quaternion.Euler(14f, -62f, 0f);   // vic pres telo - leva ruka dosahne na hlaven
        hand.holder.SetParent(spine, true);
        hand.idlePosition = hand.holder.localPosition;
        hand.idleRotation = hand.holder.localRotation;
        hand.hasAimPose = true;
        return true;
    }

    // Luk u postavy: pripevneny k hrudi (hybe se s telem). Natahovani: leva paze napnuta dopredu ve vysce ramen,
    // luk skoro svisle; klid: luk spusteny v leve ruce u boku, sip sikmo dolu dopredu.
    void AttachBowAtChest(Hand hand)
    {
        var chest = visual.GetBone(HumanBodyBones.Chest);
        var shoulder = visual.GetBone(HumanBodyBones.LeftUpperArm);
        var elbow = visual.GetBone(HumanBodyBones.LeftLowerArm);
        var wrist = visual.GetBone(HumanBodyBones.LeftHand);
        var hips = visual.GetBone(HumanBodyBones.Hips);
        var body = visual.ModelRoot;
        Vector3 shoulderLocal = shoulder != null ? body.InverseTransformPoint(shoulder.position) : new Vector3(-0.18f, 1.45f, 0f);
        float hipY = hips != null ? body.InverseTransformPoint(hips.position).y : shoulderLocal.y - 0.5f;
        float reach = shoulder != null && elbow != null && wrist != null
            ? Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, wrist.position) : 0.5f;

        // natahovani: madlo pred levym ramenem (paze skoro napnuta), luk lehce naklopeny hornim ramenem doprava
        Quaternion aim = body.rotation * Quaternion.Euler(-2f, 0f, -12f);
        hand.holder.rotation = aim;
        hand.holder.position = body.TransformPoint(new Vector3(shoulderLocal.x * 0.5f, shoulderLocal.y - 0.03f, shoulderLocal.z + reach * 0.95f))
            - aim * hand.grip;
        hand.holder.SetParent(chest, true);
        hand.aimPosition = hand.holder.localPosition;
        hand.aimRotation = hand.holder.localRotation;
        hand.holder.SetParent(null, true);

        // klid: luk spusteny v leve ruce u boku, sip miri sikmo dolu dopredu
        Quaternion rest = body.rotation * Quaternion.Euler(40f, -12f, -8f);
        hand.holder.rotation = rest;
        hand.holder.position = body.TransformPoint(new Vector3(shoulderLocal.x - 0.06f, hipY + 0.02f, 0.16f)) - rest * hand.grip;
        hand.holder.SetParent(chest, true);
        hand.idlePosition = hand.holder.localPosition;
        hand.idleRotation = hand.holder.localRotation;
        hand.hasAimPose = true;
    }

    float lastDrawn;   // posledni sila natazeni luku (poza natahovani na postave)

    // Luk u postavy: po vystrelu chvili v poze natahovani, pak zpet do klidu.
    const float BowAimHoldSeconds = 0.5f;

    void SolveBodyBow(Hand hand, float aimK)
    {
        var body = visual.ModelRoot;
        hand.holder.localPosition = Vector3.Lerp(hand.idlePosition, hand.aimPosition, aimK);
        hand.holder.localRotation = Quaternion.Slerp(hand.idleRotation, hand.aimRotation, aimK);
        // naklon luku do strany pri natahovani (CharacterAnimSet.bowTilt) - kolem osy sipu (+Z drzaku), ve madle
        var clips = visual.HeroClips;
        if (clips != null && Mathf.Abs(clips.bowTilt) > 0.01f && aimK > 0.001f)
        {
            Vector3 pivot = hand.holder.TransformPoint(hand.grip);
            hand.holder.rotation = hand.holder.rotation * Quaternion.AngleAxis(-clips.bowTilt * aimK, Vector3.forward);
            hand.holder.position += pivot - hand.holder.TransformPoint(hand.grip);
        }

        // leva paze drzi madlo (loket dolu a ven)
        FirstPersonArms.SolveArm(bodySupportArm, hand.holder.TransformPoint(hand.grip), hand.holder,
            body.TransformDirection(new Vector3(-0.6f, -1f, -0.2f)), false, true);

        // prava ruka drzi tetivu za konec sipu a tahne ji s nim dozadu (loket vysoko za telem, jako lukostrelec)
        float charge = ghost ? GhostCharge : shooting != null ? shooting.ChargeFraction : 0f;
        bool arrowShown = hand.arrow == null || hand.arrow.gameObject.activeSelf;
        if (bodyMainArm != null && hand.hasNock && charge > 0.01f && arrowShown)
        {
            Vector3 nock = hand.holder.TransformPoint(hand.nockLocal + Vector3.back * (DrawLength * Mathf.SmoothStep(0f, 1f, charge)));
            FirstPersonArms.SolveArm(bodyMainArm, nock, hand.holder, body.TransformDirection(new Vector3(0.7f, -0.2f, -1f)), false);
        }
    }

#if UNITY_EDITOR
    // Scene okno (za behu, zapnute Gizmos): kde zbran drzi ruce - zelena = Grip (rukojet), modra = Foregrip
    // (predpazbi, druha ruka), zluta = konec sipu na tetive (tam tahne prava ruka).
    void OnDrawGizmos()
    {
        foreach (var list in new[] { hands, bodyHands })
            foreach (var hand in list)
            {
                if (hand == null || hand.holder == null || !hand.holder.gameObject.activeInHierarchy) continue;
                DrawPoint(hand.holder.TransformPoint(hand.grip), Color.green, "Grip");
                if (hand.twoHanded) DrawPoint(hand.holder.TransformPoint(hand.foregrip), new Color(0.2f, 0.5f, 1f), "Foregrip");
                if (hand.hasNock) DrawPoint(hand.holder.TransformPoint(hand.nockLocal), Color.yellow, "Nock");
                // osy drzaku: kam zbran miri (modra Z) a kde je "nahoru" (zelena Y)
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(hand.holder.position, hand.holder.forward * 0.25f);
                Gizmos.color = Color.green;
                Gizmos.DrawRay(hand.holder.position, hand.holder.up * 0.12f);
            }
    }

    static void DrawPoint(Vector3 point, Color color, string label)
    {
        Gizmos.color = color;
        Gizmos.DrawSphere(point, 0.015f);
        Gizmos.DrawWireSphere(point, 0.04f);
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.Label(point + Vector3.up * 0.05f, label);
    }
#endif

    // Puska obema rukama: po vystrelu chvili v poze strelby, pak zpet do klidu (plynule).
    float lastShotTime = -100f;
    float aimBlend;
    const float AimHoldSeconds = 1.2f;

    // ---------------- uspavaci pistole (David, Shift): na chvili v leve ruce u kamery ----------------

    Transform dartPistol;
    float dartShownAt = -100f;

    bool DartShowing => dartPistol != null && Time.time - dartShownAt >= 0f && Time.time - dartShownAt < DartShowSeconds;
    // leva ruka v prvni osobe podpira zbran zespodu (David, Viktor) - u kamery bez strelby od pasu
    bool LeftSupportOnly => builtWeapon != null && builtWeapon.firstPersonTwoHanded;
    const float DartShowSeconds = 0.75f;
    static readonly Vector3 DartGrip = new Vector3(0f, -0.06f, 0.02f);
    // posun modelu pistolky (Resources/Weapons/DartPistol) vuci ruce: ruka zustava v DartGrip, model se posune
    // (y = nahoru, z = dopredu, x = doprava; v metrech)
    static readonly Vector3 DartModelOffset = new Vector3(0f, 0.02f, 0f);

    public void ShowDartPistol()
    {
        if (root == null) return;
        if (dartPistol == null) dartPistol = BuildDartPistol(root.transform);
        dartShownAt = Time.time;
    }

    // Uspavaci pistole: model ze Sketchfabu (Resources/Weapons/DartPistol, postavi Editor/DartPistolSetup - rukojet v bode
    // DartGrip, hlaven +Z). Kdyz chybi: tmave telo, dlouha hlaven, CO2 bombicka pod hlavni, v hlavni sipka s oranzovou spickou.
    static Transform BuildDartPistol(Transform parent)
    {
        var holder = new GameObject("DartPistol").transform;
        holder.SetParent(parent, false);
        var model = Resources.Load<GameObject>("Weapons/DartPistol");
        if (model != null)
        {
            var instance = Instantiate(model, holder);
            instance.transform.localPosition = DartModelOffset;
            instance.transform.localRotation = Quaternion.identity;
            foreach (var collider in instance.GetComponentsInChildren<Collider>())
                Destroy(collider);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            holder.gameObject.SetActive(false);
            return holder;
        }
        var dark = new Color(0.16f, 0.17f, 0.2f);
        var steel = new Color(0.62f, 0.64f, 0.68f);
        var teal = new Color(0.2f, 0.55f, 0.6f);
        Part(holder, new Vector3(0f, 0.02f, 0.06f), Vector3.zero, new Vector3(0.045f, 0.06f, 0.16f), dark);          // telo
        Part(holder, new Vector3(0f, 0.035f, 0.21f), Vector3.zero, new Vector3(0.026f, 0.026f, 0.2f), steel);       // hlaven
        Part(holder, new Vector3(0f, -0.005f, 0.17f), Vector3.zero, new Vector3(0.03f, 0.03f, 0.12f), teal);        // CO2 bombicka
        Part(holder, new Vector3(0f, -0.055f, 0.0f), new Vector3(-14f, 0f, 0f), new Vector3(0.04f, 0.1f, 0.05f), dark);   // rukojet
        Part(holder, new Vector3(0f, 0.035f, 0.325f), Vector3.zero, new Vector3(0.012f, 0.012f, 0.03f), new Color(1f, 0.55f, 0.1f)); // spicka sipky
        holder.gameObject.SetActive(false);
        return holder;
    }

    void UpdateDartPistol()
    {
        if (dartPistol == null || root == null) return;
        float t = (Time.time - dartShownAt) / DartShowSeconds;
        bool show = t >= 0f && t < 1f && root.activeSelf;
        if (dartPistol.gameObject.activeSelf != show) dartPistol.gameObject.SetActive(show);
        bool leftUsed = false;
        foreach (var h in hands) if (h.side < 0f || h.twoHanded) leftUsed = true;
        if (!show)
        {
            if (fpArms != null && !leftUsed && t >= 1f && t < 1.2f) fpArms.ResetArm(false);
            return;
        }
        // leva ruka jen podpira pusku (prvni osoba): pistolku vezme ona, pak se vrati na predpazbi
        if (LeftSupportOnly)
        {
            leftUsed = false;
            foreach (var h in hands) if (h.side < 0f) leftUsed = true;
        }
        // vytahnout zleva, vystrel (zpetny raz), schovat
        float raise = t < 0.2f ? Mathf.SmoothStep(0f, 1f, t / 0.2f) : t > 0.75f ? Mathf.SmoothStep(1f, 0f, (t - 0.75f) / 0.25f) : 1f;
        float kick = t > 0.2f && t < 0.4f ? Mathf.Sin((t - 0.2f) / 0.2f * Mathf.PI) : 0f;
        dartPistol.localPosition = Vector3.Lerp(new Vector3(-0.32f, -0.5f, 0.35f), new Vector3(-0.2f, -0.16f, 0.5f), raise) + new Vector3(0f, 0.02f, -0.05f) * kick;
        dartPistol.localRotation = Quaternion.Euler(-12f * kick, 4f, 0f);
        if (fpArms != null && !leftUsed)
            fpArms.Solve(false, dartPistol.TransformPoint(DartGrip), dartPistol, root.transform, false, true);
    }

    // Po animaci: leva ruka postavy podpira zbran zespodu (pro ostatni hrace, POTG, killcam).
    // ---------------- Mosinovo gesto pri zesileni (E) v prvni osobe ----------------
    // Leva pest s natazenym ukazovackem se zvedne do obrazu vlevo nahore, dvakrat pumpne a zmizi (jako Lucio).
    Transform ampHand;
    float ampBlend;
    bool ampPointing;
    BardAbility bardAbility;

    void UpdateAmpGesture()
    {
        if (root == null || fpArms == null || ghost) return;
        if (bardAbility == null) bardAbility = GetComponent<BardAbility>();
        // (krátke gesto na zacatku zesileni: nahoru, dvakrat pumpne, dolu - casovani v BardAbility)
        ampBlend = bardAbility != null && root.activeSelf ? bardAbility.AmpGestureWeight : 0f;
        if (ampBlend <= 0.001f)
        {
            if (ampPointing)
            {
                fpArms.PointLeftIndex(false);
                fpArms.ResetArm(false);
                ampPointing = false;
            }
            return;
        }

        if (ampHand == null)
        {
            ampHand = new GameObject("AmpHand").transform;
            ampHand.SetParent(root.transform, false);
        }
        float pump = bardAbility.AmpGesturePump;
        Vector3 hidden = new Vector3(-0.45f, -0.6f, 0.4f);
        Vector3 raised = new Vector3(-0.3f, -0.02f + 0.07f * pump, 0.55f);
        float k = Mathf.SmoothStep(0f, 1f, ampBlend);
        ampHand.localPosition = Vector3.Lerp(hidden, raised, k);
        // prsty (a ukazovacek) miri vzhuru, dlan ke stredu obrazu; na uder se pest lehce naklopi dopredu
        ampHand.localRotation = Quaternion.Euler(-80f + 12f * pump, 8f, 0f);
        fpArms.Solve(false, ampHand.position, ampHand, root.transform, false, true);
        if (!ampPointing)
        {
            fpArms.PointLeftIndex(true);
            ampPointing = true;
        }
    }

    // Kosa (Max) pri behu: plynule se natoci do "nesene" polohy vuci telu - nasada sikmo, cepel dovnitr pres telo
    // (CarryRotation; otaci se kolem rukojeti, porad v dlani). Pri uderu a schopnostech podle animace.
    float carryBlend;
    const float CarrySpeed = 2.5f;   // od jake rychlosti (m/s) se kosa srovnava
    // Jak Max nese kosu pri behu (stupne, pro kosu v prave ruce; v leve zrcadlove):
    const float CarryBladeYaw = -10f;  // cepel rukojeti otocena dovnitr pres telo (0 = dopredu, -90 = k leve strane tela)
    const float CarryLean = -30f;      // nasada sikmo: horni konec vyklopeny ven do strany (0 = svisle)
    const float CarryTilt = -10f;       // horni konec lehce dopredu (+) / dozadu (-)

    static Quaternion CarryRotation(float side) =>
        Quaternion.AngleAxis(CarryTilt, Vector3.right) * Quaternion.AngleAxis(CarryLean * side, Vector3.forward)
        * Quaternion.AngleAxis(CarryBladeYaw * side, Vector3.up);

    void UpdateCarry()
    {
        if (builtModel != HeldModel.Scythe || visual == null || visual.ModelRoot == null) return;
        bool carry = visual.MoveSpeed > CarrySpeed && !visual.ActionPlaying;
        carryBlend = Mathf.MoveTowards(carryBlend, carry ? 1f : 0f, Time.deltaTime * (carry ? 3f : 8f));
        float k = Mathf.SmoothStep(0f, 1f, carryBlend);
        foreach (var hand in bodyHands)
        {
            if (hand.holder == null || hand.twoHanded || hand.hasBlockPose || !hand.holder.gameObject.activeInHierarchy) continue;
            hand.holder.localPosition = hand.idlePosition;
            hand.holder.localRotation = hand.idleRotation;
            if (k <= 0.001f) continue;
            Vector3 pivot = hand.holder.TransformPoint(hand.grip);
            Quaternion want = visual.ModelRoot.rotation * CarryRotation(hand.side);
            hand.holder.rotation = Quaternion.Slerp(hand.holder.rotation, want, k);
            hand.holder.position += pivot - hand.holder.TransformPoint(hand.grip);
        }
    }

    void LateUpdate()
    {
        UpdateCarry();
        UpdateDartPistol();
        UpdateAmpGesture();
        UpdateBodyRevolver();
        UpdateBodyDetonator();
        UpdateBodyDart();
        UpdateBodyRecoil();
        if (bodySupportArm == null || visual == null || visual.ModelRoot == null) return;
        if (builtModel == HeldModel.Bow)
        {
            float charge = ghost ? GhostCharge : shooting != null ? shooting.ChargeFraction : 0f;
            bool drawing = charge > 0.01f || Time.time - lastShotTime < BowAimHoldSeconds;
            aimBlend = Mathf.MoveTowards(aimBlend, drawing ? 1f : 0f, Time.deltaTime * (drawing ? 7f : 2.5f));
            // postava: animace natahovani podle sily natazeni (po vystrelu chvili drzi posledni pozu)
            if (charge > 0.01f) lastDrawn = charge;
            visual.SetBowDraw(lastDrawn, Mathf.SmoothStep(0f, 1f, aimBlend));
            foreach (var hand in bodyHands)
                if (hand.holder != null && hand.bowBody && hand.holder.gameObject.activeInHierarchy)
                    SolveBodyBow(hand, Mathf.SmoothStep(0f, 1f, aimBlend));
            return;
        }
        bool aiming = Time.time - lastShotTime < AimHoldSeconds;
        aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, Time.deltaTime * (aiming ? 8f : 2.5f));
        float k = Mathf.SmoothStep(0f, 1f, aimBlend);
        foreach (var hand in bodyHands)
        {
            if (hand.holder == null || !hand.twoHanded || !hand.holder.gameObject.activeInHierarchy) continue;
            var body = visual.ModelRoot;
            if (hand.hasAimPose)
            {
                hand.holder.localPosition = Vector3.Lerp(hand.idlePosition, hand.aimPosition, k);
                hand.holder.localRotation = Quaternion.Slerp(hand.idleRotation, hand.aimRotation, k);
            }
            if (bodyMainArm != null)
                FirstPersonArms.SolveArm(bodyMainArm, hand.holder.TransformPoint(hand.grip), hand.holder,
                    body.TransformDirection(new Vector3(0.15f, -1f, -0.35f)), false);   // lokty u tela (dolu a lehce dozadu)

            // puska v prave ruce podle animace strelby (Viktor): leva ruka na predpazbi jen pri mireni,
            // pri behu bez strelby se plynule pusti a bezi podle animace
            float support = !hand.hasAimPose && bodyMainArm == null ? visual.AimHoldWeight : 1f;
            if (support <= 0.001f) continue;
            var arm = bodySupportArm;
            Quaternion upper = arm.upper.localRotation, lower = arm.lower.localRotation, wrist = arm.hand.localRotation;
            FirstPersonArms.SolveArm(arm, hand.holder.TransformPoint(hand.foregrip), hand.holder,
                body.TransformDirection(new Vector3(-0.15f, -1f, -0.35f)), true);
            if (support < 0.999f)
            {
                arm.upper.localRotation = Quaternion.Slerp(upper, arm.upper.localRotation, support);
                arm.lower.localRotation = Quaternion.Slerp(lower, arm.lower.localRotation, support);
                arm.hand.localRotation = Quaternion.Slerp(wrist, arm.hand.localRotation, support);
            }
        }
    }
    HeldModel builtModel;
    int nextHand;
    float swingDuration = 0.3f;
    FirstPersonArms fpArms;
    BlockAbility block;
    ScopeZoom scope;
    float blockBlend;
    float runBlend;
    float runPhase;
    Vector3 lastPosition;
    float reloadElapsed = 99f;
    float reloadDuration = 1f;

    // David (WeaponDefinition.monsterCanReload, jen prvni osoba): leva ruka vytahne zelenou plechovku Monsteru,
    // napraha se a svihne s ni do leveho boku zbrane. Naraz = konec prebijeni (naboje pribydou prave tehdy),
    // plechovka se rozplyne v jemnou zelenou mlhu a ruka se vrati na predpazbi.
    Transform reloadCan;
    bool reloadCanceled;
    float canAfter = 99f;                 // cas od narazu (navrat ruky na predpazbi)
    const float CanReturnSeconds = 0.4f;
    const float CanPalmDepth = 0.018f;    // stred plechovky (polomer 3,3 cm) za bodem dlane
    // poloha plechovky (vuci kamere): dole mimo obraz, zvednuta vlevo dole v obraze, napraha vlevo vzadu
    static readonly Vector3 CanLowPos = new Vector3(-0.22f, -0.60f, 0.28f), CanLowTurn = new Vector3(35f, 20f, 0f);
    static readonly Vector3 CanUpPos = new Vector3(-0.08f, -0.17f, 0.42f), CanUpTurn = new Vector3(-6f, 28f, 10f);
    static readonly Vector3 CanBackPos = new Vector3(-0.15f, -0.13f, 0.36f), CanBackTurn = new Vector3(-14f, 40f, 28f);
    // zbran se natoci levym bokem ke kamere (tam plechovka prileti)
    static readonly Vector3 CanGunOffset = new Vector3(-0.03f, 0.03f, -0.02f), CanGunTurn = new Vector3(-4f, -10f, -14f);

    // Honza (WeaponDefinition.magazineSlapReload, jen prvni osoba): leva ruka prasti do bedny zasobniku (odpadne i s granaty),
    // sjede dolu pro novou, nasadi ji zespodu a natahne radici packu (kohout). Casy jako podil doby prebiti.
    Transform slapMag, slapLever, slapFalling;
    Vector3 slapMagLocalPos;
    Quaternion slapMagLocalRot, slapLeverRest;
    Vector3 slapMagCenter, slapMagExtents;   // bedna v drzaku zbrane (bez granatu)
    Vector3 slapKnobLocal;                   // kulicka packy (v souradnicich packy)
    bool slapStruck, slapInserted, slapArmUsed;
    Vector3 slapFallVelocity, slapFallSpin;
    float slapFallAge;
    Vector3 slapReleaseAt;                   // kde ruka pustila packu
    const float SlapStrike = 0.24f, SlapInsert = 0.66f, SlapPull = 0.74f, SlapRelease = 0.86f, SlapLeave = 0.92f;
    const float SlapLeverAngle = 35f;        // packa kulickou dozadu (kladne kolem jeji osy X)

    bool SlapReload => builtWeapon != null && builtWeapon.magazineSlapReload && fpArms != null && root != null
        && hands.Length == 1 && hands[0].holder != null && slapMag != null;

    bool MonsterReload => builtWeapon != null && builtWeapon.monsterCanReload && fpArms != null && root != null
        && hands.Length == 1 && hands[0].twoHanded && LeftSupportOnly;
    float throwElapsed = 99f;
    const float ThrowDuration = 0.45f;
    float tauntElapsed = 99f;
    public const float TauntDuration = 0.8f;

    void Awake()
    {
        shooting = GetComponent<WeaponShooting>();
        fpc = GetComponent<FirstPersonController>();
    }

    // ---------------- duch pro prehravani POTG (bez site) ----------------
    bool ghost;
    WeaponDefinition ghostWeapon;
    HeroDefinition ghostHero;
    Transform ghostCamera;
    bool ghostFirstPerson;
    public bool GhostDead, GhostThirdPerson, GhostBlocking, GhostUltCasting;
    public float GhostUltWindup, GhostCharge;

    public void SetGhost(HeroDefinition hero, bool firstPerson, Transform camera)
    {
        ghost = true;
        ghostHero = hero;
        ghostWeapon = hero != null ? hero.weapon : null;
        ghostFirstPerson = firstPerson && camera != null;
        ghostCamera = camera;
        built = false;
    }

    WeaponDefinition CurrentWeapon => ghost ? ghostWeapon : shooting != null ? shooting.weapon : null;

    HeroDefinition CurrentHero
    {
        get
        {
            if (ghost) return ghostHero;
            var playerHero = GetComponent<PlayerHero>();
            return playerHero != null ? playerHero.Hero : null;
        }
    }

    float swingStrength = 1f;

    // Posileny vystrel: ostatni hraci vidi rude zablesknuti zbrane (majitel ji vidi rudou uz pred vystrelem).
    float powerFlashUntil;
    public void FlashPower() => powerFlashUntil = Time.time + 0.25f;

    // Zbrane pryc z rukou (uvodni scenka: Viktor odlozi pusku a pije pivo). Ruce pak ridi jen animace.
    public void HideWeapons()
    {
        foreach (var hand in hands) if (hand != null && hand.holder != null) hand.holder.gameObject.SetActive(false);
        foreach (var hand in bodyHands) if (hand != null && hand.holder != null) hand.holder.gameObject.SetActive(false);
        if (visual != null) { visual.GripRight = false; visual.GripLeft = false; visual.HoldShootPose = false; visual.AimOnScope = false; }
        bodySupportArm = null;
        bodyMainArm = null;
        enabled = false;
    }

    public void Swing() => Swing(1f);

    // Pohyblive dily zbrane pri vystrelu (Honzuv granatomet): vicko na usti se odklopi (MuzzleCap), granaty v bedne
    // se posunou o misto (GrenadeFeed) - u kamery i na postave.
    void AnimateShotParts()
    {
        foreach (var hand in hands) AnimateShotParts(hand);
        foreach (var hand in bodyHands) AnimateShotParts(hand);
    }

    static void AnimateShotParts(Hand hand)
    {
        if (hand == null || hand.holder == null) return;
        foreach (var cap in hand.holder.GetComponentsInChildren<MuzzleCap>()) cap.Flap();
        foreach (var feed in hand.holder.GetComponentsInChildren<GrenadeFeed>()) feed.Feed();
        FlashMuzzles(hand, false);
    }

    // Zablesky u usti (MuzzleFlash): bezne vystrely, nebo granatomet.
    static void FlashMuzzles(Hand hand, bool grenade)
    {
        if (hand == null || hand.holder == null) return;
        foreach (var flash in hand.holder.GetComponentsInChildren<MuzzleFlash>())
            if (flash.grenade == grenade) flash.Flash();
    }

    // ---------------- Viktor: granatomet M203 (FlashAbility) ----------------
    // Vystrel granatu: zablesk a dym z usti granatometu, silny raz a pak se hlaven granatometu posune dopredu a zpet
    // (nabiti) - leva ruka na ni jede s ni (znacka Foregrip je pod hlavni).
    float grenadeLaunchAt = -100f;
    Transform launcherBarrel;
    Vector3 launcherBarrelLocal;
    const float LaunchSeconds = 0.75f, LaunchPumpFrom = 0.22f, LaunchPumpTo = 0.6f, LaunchPumpDistance = 0.07f;

    public void PlayGrenadeLaunch()
    {
        grenadeLaunchAt = Time.time;
        foreach (var hand in hands) FlashMuzzles(hand, true);
        foreach (var hand in bodyHands) FlashMuzzles(hand, true);
    }

    // Usti granatometu u kamery (vlastni prvni osoba) - odtud granat vyleti.
    public bool TryGetGrenadeMuzzle(out Vector3 position)
    {
        position = Vector3.zero;
        if (root == null || !root.activeInHierarchy || hands.Length == 0 || hands[0] == null || hands[0].holder == null) return false;
        foreach (var flash in hands[0].holder.GetComponentsInChildren<MuzzleFlash>())
            if (flash.grenade) { position = flash.transform.position; return true; }
        return false;
    }

    // Raz zbrane po vystrelu granatu (k poze ruky u kamery).
    void AnimateLaunch(float s, ref Vector3 position, ref Vector3 euler)
    {
        float kick = Mathf.Exp(-s * 10f) * Mathf.Min(1f, s * 50f);
        position += new Vector3(0f, 0.02f, -0.09f) * kick;
        euler.x -= 10f * kick;
        // pri nabijeni se zbran lehce natoci, at je hlaven granatometu videt
        float pump = Mathf.Sin(Mathf.Clamp01((s - LaunchPumpFrom) / (LaunchPumpTo - LaunchPumpFrom)) * Mathf.PI);
        euler += new Vector3(-3f, -4f, -8f) * pump;
    }

    // Hlaven granatometu: posun dopredu a zpet (nabiti) po vystrelu granatu.
    void UpdateLauncherBarrel(Hand hand)
    {
        if (launcherBarrel == null || hand == null || hand.holder == null) return;
        float s = Time.time - grenadeLaunchAt;
        float d = s > LaunchPumpFrom && s < LaunchPumpTo
            ? Mathf.Sin((s - LaunchPumpFrom) / (LaunchPumpTo - LaunchPumpFrom) * Mathf.PI) * LaunchPumpDistance : 0f;
        launcherBarrel.localPosition = launcherBarrelLocal;
        if (d > 0f) launcherBarrel.position += hand.holder.forward * d;
    }

    // ---------------- Viktor: prebiti zasobnikem (WeaponDefinition.magazineSwapReload, prvni osoba) ----------------
    // Leva ruka z predpazbi chyti zasobnik, vytahne ho dolu (z obrazu), sjede pro novy, zasune ho zespodu a vrati se.
    Transform swapMag;
    Vector3 swapMagLocalPos;
    Quaternion swapMagLocalRot;
    Vector3 swapMagCenter, swapMagExtents;
    bool swapArmUsed, swapInserted;
    const float SwapGrab = 0.16f, SwapOut = 0.36f, SwapBack = 0.54f, SwapInsert = 0.8f, SwapHome = 0.95f;

    bool SwapReload => builtWeapon != null && builtWeapon.magazineSwapReload && fpArms != null && root != null
        && hands.Length == 1 && hands[0] != null && hands[0].holder != null && swapMag != null;
    bool SwapHandBusy => SwapReload && reloadElapsed < reloadDuration && !reloadCanceled;

    void FindSwapParts(Hand hand, WeaponDefinition weapon)
    {
        swapMag = null;
        launcherBarrel = null;
        if (hand == null || hand.holder == null) return;
        foreach (var part in hand.holder.GetComponentsInChildren<Transform>(true))
        {
            if (part.name == "M203_Barrel" && launcherBarrel == null)
            {
                launcherBarrel = part;
                launcherBarrelLocal = part.localPosition;
            }
            if (weapon.magazineSwapReload && swapMag == null && part.name.IndexOf("Magazine", System.StringComparison.OrdinalIgnoreCase) >= 0
                && part.GetComponentInChildren<MeshFilter>() != null)
                swapMag = part;
        }
        if (swapMag == null) return;
        swapMagLocalPos = swapMag.localPosition;
        swapMagLocalRot = swapMag.localRotation;
        var box = HolderBounds(hand.holder, swapMag, false);
        swapMagCenter = box.center;
        swapMagExtents = box.extents;
    }

    void SwapRestore()
    {
        if (swapMag == null) return;
        if (!swapMag.gameObject.activeSelf) swapMag.gameObject.SetActive(true);
        swapMag.localPosition = swapMagLocalPos;
        swapMag.localRotation = swapMagLocalRot;
    }

    void PlaceSwapMag(Transform holder, Vector3 delta)
    {
        var parent = swapMag.parent;
        Vector3 restWorld = parent != null ? parent.TransformPoint(swapMagLocalPos) : swapMagLocalPos;
        if (!swapMag.gameObject.activeSelf) swapMag.gameObject.SetActive(true);
        swapMag.localRotation = swapMagLocalRot;
        swapMag.position = restWorld + holder.TransformVector(delta);
    }

    // Vraci true, kdyz prave ridi levou ruku (jinak ruka podpira zbran jako obvykle).
    bool SolveSwapHand(Hand hand)
    {
        if (!SwapHandBusy)
        {
            if (swapArmUsed)
            {
                swapArmUsed = false;
                SwapRestore();
            }
            return false;
        }
        if (!swapArmUsed) swapInserted = false;
        swapArmUsed = true;

        float t = reloadElapsed / reloadDuration;
        var h = hand.holder;
        var rootT = root.transform;
        float depth = FirstPersonArms.WrapPalmDepth;
        Quaternion support = FirstPersonArms.HandFrame(h, -1f, true, true);
        Vector3 supportGrip = h.TransformPoint(SupportPoint(hand)) + support * Vector3.down * depth;
        Quaternion grab = Quaternion.LookRotation(h.forward, -h.right);   // dlan zleva na zasobnik, prsty kolem nej
        Vector3 side = swapMagCenter + new Vector3(-swapMagExtents.x, -swapMagExtents.y * 0.3f, 0f);
        Vector3 low = rootT.TransformPoint(new Vector3(-0.25f, -0.75f, 0.35f));
        Vector3 outDelta = new Vector3(0f, -0.32f, -0.02f);   // vytazeny zasobnik (posun v drzaku zbrane)

        Vector3 grip;
        Quaternion frame;
        if (t < SwapGrab)
        {
            // z predpazbi na zasobnik
            float u = Mathf.SmoothStep(0f, 1f, t / SwapGrab);
            grip = Vector3.Lerp(supportGrip, h.TransformPoint(side) + h.right * depth, u) + h.TransformVector(new Vector3(-0.04f, -0.02f, 0f)) * Mathf.Sin(u * Mathf.PI);
            frame = Quaternion.Slerp(support, grab, u);
            SwapRestore();
        }
        else if (t < SwapOut)
        {
            // vytazeni dolu (zrychluje)
            float u = (t - SwapGrab) / (SwapOut - SwapGrab);
            Vector3 delta = outDelta * (u * u);
            PlaceSwapMag(h, delta);
            grip = h.TransformPoint(side + delta) + h.right * depth;
            frame = grab;
        }
        else if (t < SwapBack)
        {
            // stary pryc (uz mimo obraz), ruka dolu pro novy
            if (swapMag.gameObject.activeSelf) swapMag.gameObject.SetActive(false);
            float u = Mathf.SmoothStep(0f, 1f, (t - SwapOut) / (SwapBack - SwapOut));
            grip = Vector3.Lerp(h.TransformPoint(side + outDelta) + h.right * depth, low, u);
            frame = grab;
        }
        else if (t < SwapInsert)
        {
            // novy zespodu do sachty (zpomaluje)
            float u = (t - SwapBack) / (SwapInsert - SwapBack);
            float e = 1f - (1f - u) * (1f - u) * (1f - u);
            Vector3 delta = Vector3.Lerp(outDelta, Vector3.zero, e);
            PlaceSwapMag(h, delta);
            grip = Vector3.Lerp(low, h.TransformPoint(side + delta) + h.right * depth, Mathf.Min(1f, u * 3f));
            frame = grab;
        }
        else
        {
            if (!swapInserted)
            {
                swapInserted = true;
                SwapRestore();
                if (!ghost) ProceduralSfx.Play(ProceduralSfx.Empty, swapMag.position, 0.7f, 10f, false, SfxKind.Weapon);
            }
            // zpet na predpazbi
            float u = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SwapInsert, SwapHome, t));
            grip = Vector3.Lerp(h.TransformPoint(side) + h.right * depth, supportGrip, u) + h.TransformVector(new Vector3(-0.04f, -0.02f, 0f)) * Mathf.Sin(u * Mathf.PI);
            frame = Quaternion.Slerp(grab, support, u);
        }
        fpArms.SolveFrame(false, grip, frame, rootT);
        return true;
    }

    public void Swing(float strength)
    {
        swingStrength = strength;
        lastShotTime = Time.time;   // (puska obema rukama: poza strelby)
        AnimateShotParts();
        if (!ghost)
            ReplayLog.Swing(this);

        if (hands.Length == 0)
        {
            PlayBodyAttack(CurrentWeapon);
            return;
        }

        var weapon = CurrentWeapon;
        swingDuration = weapon != null && weapon.IsMelee ? Mathf.Clamp(0.9f / Mathf.Max(0.5f, weapon.fireRate), 0.18f, 0.4f) : 0.14f;

        var hand = hands[nextHand % hands.Length];
        hand.swingTime = 0f;
        nextHand++;

        PlayBodyAttack(weapon);
    }

    // Animace prebijeni: zbran u kamery (majitel) a horni polovina tela postavy (ostatni).
    public void PlayReload(float duration)
    {
        if (!ghost)
            ReplayLog.Reload(this, duration);
        reloadDuration = Mathf.Max(0.05f, duration);
        reloadElapsed = 0f;
        reloadCanceled = false;
        slapStruck = slapInserted = false;
        SlapRestore();
        canAfter = 99f;

        if (visual != null && visual.HasModel)
        {
            if (!UsesRevolver) visual.PlayReload(reloadDuration);
        }
    }

    public void StopReload()
    {
        if (!ghost)
            ReplayLog.StopReload(this);
        reloadElapsed = reloadDuration;
        reloadCanceled = true;   // (bez narazu plechovky)
        SlapRestore();
        SwapRestore();
        canAfter = 99f;
        if (reloadCan != null) reloadCan.gameObject.SetActive(false);

        if (visual != null && visual.HasModel)
            visual.StopReload();
    }

    // Hod (Honzova past): zbran u kamery uhne dolu a stranou, postava mavne rukou.
    // Sindeluv "Say what again?!": pistoli prudce namiri dopredu, naklopi ji na bok, chvili drzi a vrati.
    public void PlayTaunt()
    {
        ReplayLog.Taunt(this);
        tauntElapsed = 0f;
        if (visual != null && visual.HasModel)
            visual.PlayAttack(false, 0.5f);
    }

    // ---------------- Honza: hod miny levou rukou (prvni osoba) ----------------
    // Leva ruka vyjede zleva zdola s minou na dlani a spodnim obloukem ji hodi dopredu (pusti v MineReleaseSeconds -
    // tehdy se objevi letici mina, MineAbility), pak ruka odjede dolu z obrazu.
    public const float MineReleaseSeconds = 0.12f;
    const float MineThrowSeconds = 0.45f;
    const float MineHeldDiameter = 0.3f;   // v ruce o kus mensi nez ve svete (36 cm) - tesne u kamery
    Transform heldMine;
    float mineThrowAt = -100f;
    bool mineArmUsed;

    public void PlayMineThrow()
    {
        if (root == null || fpArms == null) return;
        if (heldMine == null) heldMine = BuildHeldMine(root.transform);
        mineThrowAt = Time.time;
    }

    static Transform BuildHeldMine(Transform parent)
    {
        var holder = new GameObject("HeldMine").transform;
        holder.SetParent(parent, false);
        var model = Resources.Load<GameObject>("Weapons/HonzaMine");
        if (model != null)
        {
            var instance = Instantiate(model, holder);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale *= MineHeldDiameter / 0.36f;
        }
        else
        {
            // (bez modelu: tmavy disk s rudou kulickou jako mina ve svete)
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.transform.SetParent(holder, false);
            body.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            body.transform.localScale = new Vector3(MineHeldDiameter, 0.02f, MineHeldDiameter);
            Fx.Paint(body, new Color(0.16f, 0.17f, 0.19f));
            Part(holder, new Vector3(0f, 0.05f, 0f), Vector3.zero, Vector3.one * 0.08f, new Color(1f, 0.2f, 0.12f));
        }
        foreach (var collider in holder.GetComponentsInChildren<Collider>()) Destroy(collider);
        foreach (var renderer in holder.GetComponentsInChildren<Renderer>())
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        holder.gameObject.SetActive(false);
        return holder;
    }

    // Leva ruka pri hodu miny (po konci ji jednou vrati do klidu mimo obraz).
    void SolveMineThrowHand()
    {
        float s = Time.time - mineThrowAt;
        if (fpArms == null || root == null || s < 0f || s >= MineThrowSeconds)
        {
            if (heldMine != null && heldMine.gameObject.activeSelf) heldMine.gameObject.SetActive(false);
            if (mineArmUsed && fpArms != null)
            {
                mineArmUsed = false;
                fpArms.ResetArm(false);
            }
            return;
        }
        mineArmUsed = true;
        var rootT = root.transform;

        // drah ruky (vuci kamere): zdola zleva, napraha vlevo dole v obraze, spodnim obloukem dopredu, pusteni, dolu pryc
        Vector3 low = new Vector3(-0.30f, -0.70f, 0.20f), wind = new Vector3(-0.20f, -0.30f, 0.32f);
        Vector3 release = new Vector3(-0.07f, -0.13f, 0.56f), follow = new Vector3(-0.03f, -0.07f, 0.60f);
        const float windAt = 0.07f, followAt = 0.2f;
        Vector3 local;
        float lift;   // 0 = prsty dopredu, 1 = prsty dopredu nahoru (pri pusteni)
        if (s < windAt)
        {
            float u = 1f - (1f - s / windAt) * (1f - s / windAt);
            local = Vector3.Lerp(low, wind, u);
            lift = 0f;
        }
        else if (s < MineReleaseSeconds)
        {
            float u = (s - windAt) / (MineReleaseSeconds - windAt);
            local = Vector3.Lerp(wind, release, u * u) + new Vector3(0f, -0.04f, 0f) * Mathf.Sin(u * Mathf.PI);
            lift = u;
        }
        else if (s < followAt)
        {
            float u = (s - MineReleaseSeconds) / (followAt - MineReleaseSeconds);
            local = Vector3.Lerp(release, follow, Mathf.SmoothStep(0f, 1f, u));
            lift = 1f;
        }
        else
        {
            float u = Mathf.SmoothStep(0f, 1f, (s - followAt) / (MineThrowSeconds - followAt));
            local = Vector3.Lerp(follow, low, u);
            lift = 1f - u;
        }

        // dlan nahoru (mina na ni lezi), prsty dopredu, pri hodu se zvednou
        Vector3 fingers = Vector3.Lerp(new Vector3(0.25f, -0.1f, 1f), new Vector3(0.1f, 0.55f, 1f), lift).normalized;
        Vector3 back = Vector3.Lerp(new Vector3(0.2f, -1f, 0.15f), new Vector3(0.1f, -1f, 0.7f), lift).normalized;
        Quaternion frame = rootT.rotation * Quaternion.LookRotation(fingers, back);
        Vector3 grip = rootT.TransformPoint(local);
        fpArms.SolveFrame(false, grip, frame, rootT);

        // mina na dlani (spodkem na kuzi, kopuli od dlane), do pusteni
        bool holding = s < MineReleaseSeconds;
        if (heldMine.gameObject.activeSelf != holding) heldMine.gameObject.SetActive(holding);
        if (holding)
        {
            Vector3 palm = frame * Vector3.down;
            heldMine.position = grip - palm * FirstPersonArms.WrapPalmDepth;
            heldMine.rotation = Quaternion.LookRotation(frame * Vector3.forward, palm);
        }
    }

    // ---------------- Honza: detonator v leve ruce pri odpaleni min (prvni osoba) ----------------
    // Leva ruka zdola zvedne detonator do obrazu (vlevo), zmackne spoust (cuknuti, cvaknuti), chvili ho drzi a stahne ho.
    const float DetonateSeconds = 0.7f, DetonateUp = 0.12f, DetonateSqueeze = 0.13f, DetonateDown = 0.5f;
    Transform detonator;
    float detonateAt = -100f;
    bool detonateArmUsed, detonateClicked;

    public void PlayDetonate()
    {
        if (root == null || fpArms == null) return;
        if (detonator == null) detonator = BuildDetonator(root.transform);
        detonateAt = Time.time;
        detonateClicked = false;
    }

    static Transform BuildDetonator(Transform parent)
    {
        var holder = new GameObject("Detonator").transform;
        holder.SetParent(parent, false);
        var model = Resources.Load<GameObject>("Weapons/HonzaDetonator");
        if (model != null)
        {
            var instance = Instantiate(model, holder);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
        }
        else
        {
            // (bez modelu: zluta rukojet s cervenou spousti a cudlikem)
            Part(holder, Vector3.zero, Vector3.zero, new Vector3(0.035f, 0.14f, 0.045f), new Color(0.8f, 0.5f, 0.1f));
            Part(holder, new Vector3(0f, 0.02f, 0.03f), Vector3.zero, new Vector3(0.01f, 0.05f, 0.012f), new Color(0.7f, 0.07f, 0.04f));
            Part(holder, new Vector3(0f, 0.08f, 0f), Vector3.zero, new Vector3(0.015f, 0.02f, 0.015f), new Color(0.7f, 0.07f, 0.04f));
        }
        foreach (var collider in holder.GetComponentsInChildren<Collider>()) Destroy(collider);
        foreach (var renderer in holder.GetComponentsInChildren<Renderer>())
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        holder.gameObject.SetActive(false);
        return holder;
    }

    // Leva ruka s detonatorem (po konci ji jednou vrati do klidu mimo obraz).
    void SolveDetonatorHand()
    {
        float s = Time.time - detonateAt;
        if (fpArms == null || root == null || detonator == null || s < 0f || s >= DetonateSeconds)
        {
            if (detonator != null && detonator.gameObject.activeSelf) detonator.gameObject.SetActive(false);
            if (detonateArmUsed && fpArms != null)
            {
                detonateArmUsed = false;
                fpArms.ResetArm(false);
            }
            return;
        }
        detonateArmUsed = true;
        if (!detonator.gameObject.activeSelf) detonator.gameObject.SetActive(true);
        var rootT = root.transform;

        // poloha (vuci kamere): zdola zleva do obrazu vlevo, spici mirne ke stredu
        Vector3 low = new Vector3(-0.30f, -0.70f, 0.25f), hold = new Vector3(-0.19f, -0.20f, 0.42f);
        float up = s < DetonateUp ? 1f - (1f - s / DetonateUp) * (1f - s / DetonateUp)
            : s < DetonateDown ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (s - DetonateDown) / (DetonateSeconds - DetonateDown));
        Vector3 local = Vector3.Lerp(low, hold, up);
        Quaternion turn = Quaternion.Slerp(Quaternion.Euler(35f, 30f, -30f), Quaternion.Euler(10f, 15f, -12f), up);

        // zmacknuti: kratke cuknuti dopredu dolu a cvak
        float d = s - DetonateSqueeze;
        float squeeze = d < 0f ? 0f : Mathf.Exp(-d * 14f) * Mathf.Min(1f, d * 50f);
        local += new Vector3(0f, -0.012f, 0.01f) * squeeze;
        turn = turn * Quaternion.Euler(8f * squeeze, 0f, 0f);
        if (d >= 0f && !detonateClicked)
        {
            detonateClicked = true;
            if (!ghost) ProceduralSfx.Play(ProceduralSfx.Empty, rootT.TransformPoint(local), 0.8f, 10f, false, SfxKind.Weapon);
        }

        detonator.localPosition = local;
        detonator.localRotation = turn;
        // pest kolem tela detonatoru: prsty dopredu pres spoust, hrbet ruky vlevo, palec nahore u cudliku
        Quaternion frame = Quaternion.LookRotation(detonator.forward, -detonator.right);
        fpArms.SolveFrame(false, detonator.position, frame, rootT);
    }

    // ---------------- Honza ze treti osoby: hod miny a detonator v leve ruce postavy ----------------

    // Mina: postava mavne rukou jako pri hodu pasti (jen postava - v prvni osobe minu hazi leva ruka u kamery).
    public void PlayBodyThrow()
    {
        if (visual != null && visual.HasModel)
            visual.PlayAttack(true, ThrowDuration);
    }

    // Tezka zbran na postave (WeaponDefinition.heavyRecoil): po vystrelu se prava ruka se zbrani kratce cukne dozadu
    // podel hlavne (IK po animaci - ruka si drzi natoceni, loket zustava, kde byl).
    float bodyRecoilAt = -100f;
    FirstPersonArms.Arm bodyRecoilArm;
    const float BodyRecoilSeconds = 0.35f, BodyRecoilDistance = 0.06f;

    void UpdateBodyRecoil()
    {
        float d = Time.time - bodyRecoilAt;
        if (d < 0f || d > BodyRecoilSeconds || visual == null || !visual.HasModel || visual.BodyAnimator == null || !visual.BodyVisible) return;
        if (bodyHands.Length == 0 || bodyHands[0] == null || bodyHands[0].holder == null) return;
        float k = Mathf.Exp(-d * 12f) * Mathf.Min(1f, d * 40f);
        if (k < 0.01f) return;
        if (bodyRecoilArm == null || bodyRecoilArm.upper == null)
            bodyRecoilArm = FirstPersonArms.BuildArm(visual.BodyAnimator, true);
        var arm = bodyRecoilArm;
        if (arm == null) return;

        var gun = bodyHands[0].holder;
        FirstPersonArms.PalmPose(arm, out var palm, out var frame);
        Vector3 target = palm - gun.forward * BodyRecoilDistance * k + Vector3.up * 0.01f * k;
        Vector3 pole = arm.lower.position - (arm.upper.position + arm.hand.position) * 0.5f;
        FirstPersonArms.SolveArmFrame(arm, target, Quaternion.AngleAxis(-4f * k, gun.right) * frame, pole, false);
    }

    // Leva ruka postavy (treti osoba) pro predmety v ruce: detonator (Honza), uspavaci pistolka (David).
    FirstPersonArms.Arm bodyLeftArm;

    // Leva ruka postavy k bodu 'target' s pesti 'frame' (prsty z, hrbet ruky y) - IK po animaci, plynule podle 'weight'.
    // Vrati polohu dlane a natoceni ruky (pro predmet v ni). False = postava nema humanoidni model.
    bool SolveBodyLeftHand(Vector3 target, Quaternion frame, float weight, out Vector3 palm, out Quaternion handFrame)
    {
        palm = Vector3.zero;
        handFrame = Quaternion.identity;
        if (bodyLeftArm == null || bodyLeftArm.upper == null)
            bodyLeftArm = FirstPersonArms.BuildArm(visual.BodyAnimator, false);
        var arm = bodyLeftArm;
        if (arm == null) return false;
        var body = visual.ModelRoot;
        Quaternion upper = arm.upper.localRotation, lower = arm.lower.localRotation, wrist = arm.hand.localRotation;
        FirstPersonArms.SolveArmFrame(arm, target, frame, body.TransformDirection(new Vector3(-0.6f, -1f, -0.2f)), false);
        if (weight < 0.999f)
        {
            arm.upper.localRotation = Quaternion.Slerp(upper, arm.upper.localRotation, weight);
            arm.lower.localRotation = Quaternion.Slerp(lower, arm.lower.localRotation, weight);
            arm.hand.localRotation = Quaternion.Slerp(wrist, arm.hand.localRotation, weight);
        }
        FirstPersonArms.PalmPose(arm, out palm, out handFrame);
        return true;
    }

    // Predmet v dlani: 'grip' = bod predmetu (v jeho souradnicich), kde ho drzi pest; svetova velikost 1.
    static void PlaceInPalm(Transform prop, Vector3 palm, Quaternion handFrame, Vector3 grip)
    {
        Vector3 fingers = handFrame * Vector3.forward, back = handFrame * Vector3.up;
        if (!prop.gameObject.activeSelf) prop.gameObject.SetActive(true);
        prop.rotation = Quaternion.LookRotation(fingers, Vector3.Cross(back, fingers));
        var parentScale = prop.parent != null ? prop.parent.lossyScale : Vector3.one;
        prop.localScale = new Vector3(1f / Mathf.Max(0.0001f, parentScale.x), 1f / Mathf.Max(0.0001f, parentScale.y), 1f / Mathf.Max(0.0001f, parentScale.z));
        prop.position = palm - prop.rotation * grip;
    }

    // Detonator: postava zvedne levou ruku s detonatorem pred sebe, zmackne ho a ruku zase spusti (IK po animaci).
    float bodyDetonateAt = -100f;
    Transform bodyDetonator;

    public void PlayBodyDetonate() => bodyDetonateAt = Time.time;

    void UpdateBodyDetonator()
    {
        float s = Time.time - bodyDetonateAt;
        bool active = s >= 0f && s < DetonateSeconds && visual != null && visual.HasModel && visual.BodyAnimator != null && visual.BodyVisible;
        if (!active)
        {
            if (bodyDetonator != null && bodyDetonator.gameObject.activeSelf) bodyDetonator.gameObject.SetActive(false);
            return;
        }
        if (bodyDetonator == null)
        {
            bodyDetonator = BuildDetonator(transform);
            bodyDetonator.name = "BodyDetonator";
        }

        // zvednuti (jako v prvni osobe), cuknuti pri zmacknuti, spusteni
        float weight = s < DetonateUp ? 1f - (1f - s / DetonateUp) * (1f - s / DetonateUp)
            : s < DetonateDown ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (s - DetonateDown) / (DetonateSeconds - DetonateDown));
        float d = s - DetonateSqueeze;
        float squeeze = d < 0f ? 0f : Mathf.Exp(-d * 14f) * Mathf.Min(1f, d * 50f);

        // ruka pred levym ramenem, kousek nad nim a ke stredu tela; pest: prsty dopredu, hrbet ruky vlevo
        var body = visual.ModelRoot;
        if (!visual.BodyAnimator) return;
        var shoulder = visual.BodyAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        if (shoulder == null) return;
        Vector3 target = shoulder.position + body.forward * 0.34f + body.up * 0.14f + body.right * 0.08f
                         + (body.forward * 0.015f - body.up * 0.012f) * squeeze;
        Quaternion frame = Quaternion.LookRotation(body.forward, -body.right) * Quaternion.Euler(8f * squeeze, 0f, 0f);
        // detonator v dlani (i behem zvedani / spousteni drzi v ruce)
        if (SolveBodyLeftHand(target, frame, weight, out var palm, out var handFrame))
            PlaceInPalm(bodyDetonator, palm, handFrame, Vector3.zero);
    }

    // Uspavaci sipka (David): postava zvedne levou ruku s pistolkou ve smeru vystrelu, vystreli (cuknuti) a ruku spusti.
    // Casovani jako v prvni osobe (UpdateDartPistol).
    float bodyDartAt = -100f;
    Vector3 bodyDartDirection = Vector3.forward;
    Transform bodyDart;

    public void PlayBodyDart(Vector3 direction)
    {
        bodyDartAt = Time.time;
        if (direction.sqrMagnitude > 0.001f) bodyDartDirection = direction.normalized;
    }

    void UpdateBodyDart()
    {
        float t = (Time.time - bodyDartAt) / DartShowSeconds;
        bool active = t >= 0f && t < 1f && visual != null && visual.HasModel && visual.BodyAnimator != null && visual.BodyVisible;
        if (!active)
        {
            if (bodyDart != null && bodyDart.gameObject.activeSelf) bodyDart.gameObject.SetActive(false);
            return;
        }
        if (bodyDart == null)
        {
            bodyDart = BuildDartPistol(transform);
            bodyDart.name = "BodyDartPistol";
        }
        var shoulder = visual.BodyAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        if (shoulder == null) return;

        float raise = t < 0.2f ? Mathf.SmoothStep(0f, 1f, t / 0.2f) : t > 0.75f ? Mathf.SmoothStep(1f, 0f, (t - 0.75f) / 0.25f) : 1f;
        float kick = t > 0.2f && t < 0.4f ? Mathf.Sin((t - 0.2f) / 0.2f * Mathf.PI) : 0f;

        // ruka natazena pred levym ramenem ve smeru vystrelu (trochu ke stredu tela); pest: prsty po hlavni, hrbet ruky vlevo
        var body = visual.ModelRoot;
        Vector3 aim = bodyDartDirection;
        Vector3 side = Vector3.Cross(Vector3.up, aim);
        if (side.sqrMagnitude < 0.01f) side = body.right;
        side.Normalize();
        Vector3 target = shoulder.position + aim * 0.42f + body.up * 0.04f + side * 0.08f - aim * 0.05f * kick;
        Quaternion frame = Quaternion.LookRotation(aim, -side) * Quaternion.Euler(-12f * kick, 0f, 0f);
        if (SolveBodyLeftHand(target, frame, raise, out var palm, out var handFrame))
            PlaceInPalm(bodyDart, palm, handFrame, DartGrip);
    }

    public void PlayThrow()
    {
        throwElapsed = 0f;
        if (visual != null && visual.HasModel)
            visual.PlayAttack(true, ThrowDuration);
    }

    // Povuv hak (sekera na retezu): ruka se sekerou se na 'windup' s trhne dozadu a sekera se zatrese, pak vymrsti dopredu.
    float hookWindupStart = -100f, hookWindup = 0.15f;
    const float HookSnap = 0.12f;
    bool hookThrown;

    public void PlayHookWindup(float windup)
    {
        hookWindupStart = Time.time;
        hookWindup = Mathf.Max(0.05f, windup);
        if (visual != null && visual.HasModel)
            visual.PlayAttack(true, windup + 0.25f);
    }

    // Zatimco sekera leti na retezu, v ruce neni (u kamery i na postave).
    public void SetHookThrown(bool thrown)
    {
        hookThrown = thrown;
        ApplyHookThrown();
    }

    void ApplyHookThrown()
    {
        foreach (var list in new[] { hands, bodyHands })
        {
            if (list == null || list.Length == 0) continue;
            var hand = list[list.Length - 1];
            if (hand == null || hand.holder == null) continue;
            foreach (var r in hand.holder.GetComponentsInChildren<Renderer>(true))
                r.enabled = !hookThrown;
        }
    }

    // Odkud vychazi retez: ruka, ktera sekeru hazi (jak ji prave vidi tento klient), nebo null.
    public Transform HookHand
    {
        get
        {
            if (root != null && root.activeInHierarchy && hands.Length > 0 && hands[hands.Length - 1].holder != null)
                return hands[hands.Length - 1].holder;
            if (bodyHands.Length > 0 && bodyHands[bodyHands.Length - 1].holder != null && bodyHands[bodyHands.Length - 1].holder.gameObject.activeInHierarchy)
                return bodyHands[bodyHands.Length - 1].holder;
            return null;
        }
    }

    void PlayBodyAttack(WeaponDefinition weapon)
    {
        // zbran obema rukama (strelba od pasu): postava pri vystrelu nezveda ruce, zbran zustava u boku
        // (s vlastni animaci strelby hrdiny se prehraje ta - recoil)
        if (weapon != null && weapon.twoHanded && !weapon.IsMelee && !(visual != null && visual.HoldShootPose)) return;
        // luk: ruce vede IK (natahovani tetivy), animace strelby z pistole by je jen strhla
        if (builtModel == HeldModel.Bow && bodySupportArm != null) return;
        // tezka zbran: postava nezvedne ruku jako s pistoli, prava ruka se zbrani se jen cukne dozadu (UpdateBodyRecoil)
        if (weapon != null && weapon.heavyRecoil && !weapon.IsMelee)
        {
            bodyRecoilAt = Time.time;
            return;
        }
        if (visual != null && visual.HasModel)
            visual.PlayAttack(weapon != null && weapon.IsMelee, weapon != null ? Mathf.Clamp(1.25f / Mathf.Max(0.5f, weapon.fireRate), 0.25f, 0.7f) : 0.4f);
    }

    void Update()
    {
        if (!ghost && (shooting == null || !shooting.IsSpawned)) return;

        if (visual == null)
            visual = GetComponent<CharacterVisual>();

        bool owner = ghost ? ghostFirstPerson : shooting.IsOwner && shooting.Bot == null;
        bool visualChanged = visual != null && visual.Version != builtVersion;
        if (!built || builtWeapon != CurrentWeapon || builtAsOwner != owner || visualChanged)
            Rebuild(owner);

        bool dead = ghost ? GhostDead : fpc != null && fpc.IsDead;
        // (F6 nahled ze 3. osoby: vlastni postava se zbrani jako ji vidi ostatni)
        bool thirdPerson = ghost ? GhostThirdPerson : fpc != null && (fpc.ThirdPerson || (ThirdPersonPreview.Active && fpc.IsOwner && fpc.Bot == null));

        UpdateSlapFalling();
        if (reloadElapsed < reloadDuration)
        {
            reloadElapsed += Time.deltaTime;
            // David: konec prebiti = naraz plechovky do zbrane
            if (reloadElapsed >= reloadDuration && !reloadCanceled && MonsterReload) CanImpact();
        }
        else if (canAfter < CanReturnSeconds)
            canAfter += Time.deltaTime;
        if (throwElapsed < ThrowDuration)
            throwElapsed += Time.deltaTime;
        if (tauntElapsed < TauntDuration)
            tauntElapsed += Time.deltaTime;

        if (builtModel == HeldModel.Bow)
        {
            UpdateArrows();
            UpdateStorm();
        }
        UpdatePowerGlow();
        UpdateRageGlow();

        // Zbrane u kamery (majitel v 1. osobe) a zbrane v rukou postavy (ostatni, majitel ve 3. osobe).
        // (zivi hraci jsou behem prehravani POTG skryti, hraji misto nich duchove)
        bool hiddenByReplay = !ghost && (ReplayPlayer.Active || (CurrentHeroComponent != null && CurrentHeroComponent.IsSpawned
            && (CurrentHeroComponent.IsUntargetable || CurrentHeroComponent.HiddenFromLocal)));
        bool cameraHandsVisible = !dead && !(owner && thirdPerson) && !hiddenByReplay;
        float scopeBlend = 0f;
        if (owner && CurrentWeapon != null && CurrentWeapon.HasScopedShot)
        {
            if (ghost && ghostCamera != null && ghostCamera.TryGetComponent<Camera>(out var replayCamera))
                scopeBlend = ScopeZoom.BlendForFov(replayCamera.fieldOfView, 60f, CurrentWeapon.scopeFov);
            else if (!ghost)
            {
                if (scope == null) scope = GetComponent<ScopeZoom>();
                if (scope != null && scope.isActiveAndEnabled) scopeBlend = scope.ScopeBlend;
            }
        }
        // Move the first-person rifle out of the lens as the optic settles in.
        // Body weapons remain visible to other players, and weapon animations keep advancing.
        if (root != null)
            root.transform.localPosition = owner ? Vector3.down * (0.9f * Mathf.SmoothStep(0f, 1f, scopeBlend)) : Vector3.zero;
        if (root != null && root.activeSelf != cameraHandsVisible)
            root.SetActive(cameraHandsVisible);

        // (behem tance ultimatky - Mosino - ma ruce volne)
        bool bodyVisible = !dead && (!owner || thirdPerson) && !hiddenByReplay && !(visual != null && visual.UltimateActive);
        float bodyBlock = visual != null ? visual.BlockBlend : 0f;
        foreach (var hand in bodyHands)
        {
            if (hand.holder == null) continue;

            if (hand.holder.gameObject.activeSelf != bodyVisible)
                hand.holder.gameObject.SetActive(bodyVisible);

            if (hand.hasBlockPose)
            {
                hand.holder.localPosition = Vector3.Lerp(hand.idlePosition, hand.blockPosition, bodyBlock);
                hand.holder.localRotation = Quaternion.Slerp(hand.idleRotation, hand.blockRotation, bodyBlock);
            }
        }

        if (block == null)
            block = GetComponent<BlockAbility>();
        bool blockingNow = ghost ? GhostBlocking : block != null && block.IsBlocking;
        blockBlend = Mathf.MoveTowards(blockBlend, blockingNow ? 1f : 0f, Time.deltaTime * 8f);

        if (root == null || !cameraHandsVisible) return;

        // Pri behu se zbrane houpou do rytmu kroku (sila podle rychlosti).
        // Rychlost z posunu (CharacterController.velocity ma jen posledni, svisly pohyb).
        float speed = 0f;
        Vector3 position3 = transform.position;
        if (owner && (ghost || (fpc != null && fpc.Controller != null)) && Time.deltaTime > 0f)
        {
            Vector3 delta = position3 - lastPosition;
            delta.y = 0f;
            speed = delta.magnitude > 3f ? 0f : delta.magnitude / Time.deltaTime;
            if (!ghost && !fpc.Controller.isGrounded) speed *= 0.3f;
        }

        lastPosition = position3;

        runBlend = Mathf.Lerp(runBlend, Mathf.Clamp(speed / 5f, 0f, 1.6f), 1f - Mathf.Exp(-8f * Time.deltaTime));
        runPhase += speed * 1.5f * Time.deltaTime;
        float sway = Swung(builtModel) ? 1.5f : 1f;

        foreach (var hand in hands)
        {
            float bob = Mathf.Sin(Time.time * 1.6f + hand.side) * 0.004f;
            Vector3 position = hand.restPosition + new Vector3(0f, bob, 0f);
            // posun zbrane v prvni osobe podle zbrane (napr. velky granatomet vic do rohu)
            if (builtAsOwner && builtWeapon != null) position += builtWeapon.firstPersonOffset;
            Vector3 euler = hand.restEuler;

            float p = runPhase + (hand.side > 0f ? 0f : Mathf.PI);
            float k = runBlend * sway;
            position += new Vector3(Mathf.Sin(p * 0.5f) * 0.03f, -Mathf.Abs(Mathf.Sin(p)) * 0.035f, Mathf.Sin(p * 2f) * 0.012f) * k;
            euler += new Vector3(Mathf.Sin(p * 2f) * 4f, Mathf.Sin(p * 0.5f) * 5f, Mathf.Sin(p * 0.5f) * -8f) * k;

            float sinceReach = Time.time - reachStart;
            if (sinceReach >= 0f && sinceReach < reachWindup + ReachStrike + ReachRecover)
                AnimateReach(sinceReach, ref position, ref euler);
            else if (hand.swingTime < swingDuration)
            {
                hand.swingTime += Time.deltaTime;
                float t = Mathf.Clamp01(hand.swingTime / swingDuration);
                Animate(t, ref position, ref euler);
            }

            if (UsesRevolver) euler *= 0.2f;

            if (reloadElapsed < reloadDuration)
                AnimateReload(reloadElapsed / reloadDuration, hand.side, ref position, ref euler);
            else if (canAfter < CanReturnSeconds && MonsterReload)
                AnimateCanAfter(canAfter, ref position, ref euler);
            float sinceLaunch = Time.time - grenadeLaunchAt;
            if (sinceLaunch >= 0f && sinceLaunch < LaunchSeconds)
                AnimateLaunch(sinceLaunch, ref position, ref euler);

            if (tauntElapsed < TauntDuration)
            {
                // rychle vyrazit (15 %), drzet, pomalu vratit (od 60 %)
                float t = tauntElapsed / TauntDuration;
                float u = t < 0.15f ? Mathf.SmoothStep(0f, 1f, t / 0.15f) : t < 0.6f ? 1f : Mathf.SmoothStep(1f, 0f, (t - 0.6f) / 0.4f);
                float shake = t > 0.15f && t < 0.6f ? Mathf.Sin(tauntElapsed * 70f) * 0.006f : 0f;
                position += new Vector3(-0.12f * hand.side + shake, 0.06f, 0.16f) * u;
                euler += new Vector3(-6f, -8f * hand.side, 78f * hand.side) * u;
            }

            if (throwElapsed < ThrowDuration)
            {
                float u = Mathf.Sin(Mathf.Clamp01(throwElapsed / ThrowDuration) * Mathf.PI);
                position += new Vector3(0.10f * hand.side, -0.22f, -0.06f) * u;
                euler += new Vector3(28f, 10f * hand.side, -18f * hand.side) * u;
            }

            float sinceHook = Time.time - hookWindupStart;
            if (sinceHook >= 0f && sinceHook < hookWindup + HookSnap && hand == hands[hands.Length - 1])
            {
                Vector3 backPos = new Vector3(0.03f * hand.side, 0.08f, -0.22f), backRot = new Vector3(-38f, 0f, 10f * hand.side);
                Vector3 outPos = new Vector3(-0.04f * hand.side, 0.02f, 0.20f), outRot = new Vector3(18f, 0f, 0f);
                if (sinceHook < hookWindup)
                {
                    float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sinceHook / (hookWindup * 0.6f)));
                    float shake = Mathf.Sin(sinceHook * 95f) * 0.012f * u;
                    position += backPos * u + new Vector3(shake, shake * 0.5f, 0f);
                    euler += backRot * u + new Vector3(0f, 0f, shake * 300f);
                }
                else
                {
                    float u = Mathf.Clamp01((sinceHook - hookWindup) / HookSnap);
                    position += Vector3.Lerp(Vector3.Lerp(backPos, outPos, Mathf.Min(1f, u * 3f)), Vector3.zero, Mathf.SmoothStep(0f, 1f, u));
                    euler += Vector3.Lerp(Vector3.Lerp(backRot, outRot, Mathf.Min(1f, u * 3f)), Vector3.zero, Mathf.SmoothStep(0f, 1f, u));
                }
            }

            // Priprava ultimatky: zbran se lehce zvedne a naklopi nahoru (luk se misto toho natahuje).
            bool ultCasting = ghost ? GhostUltCasting : fpc != null && fpc.UltCasting;
            if (ultCasting && builtModel != HeldModel.Bow)
            {
                float raise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((ghost ? GhostUltWindup : fpc.UltWindup) * 2.5f));
                position += new Vector3(-0.04f * hand.side, 0.08f, -0.05f) * raise;
                euler.x -= 22f * raise;
                euler.z += 8f * hand.side * raise;
            }

            // Luk pri natahovani: v leve ruce (jako Hanzo) se vysune ke stredu a nahoru, at napnuty sip vede
            // od zamerovace dolu a i s pravou rukou zustane v obraze; jinak se pritahne k telu a lehce zvedne.
            if (builtModel == HeldModel.Bow)
            {
                float charge = ghost ? GhostCharge : shooting.ChargeFraction;
                if (hand.side < 0f)
                {
                    float drawn = Mathf.SmoothStep(0f, 1f, charge);
                    position += new Vector3(0.05f, 0.06f, 0.08f) * drawn;
                    euler += new Vector3(-4f, -4f, 10f) * drawn;
                }
                else
                {
                    position += new Vector3(-0.03f, 0.03f, -0.10f) * charge;
                    euler.z += 6f * charge;
                }
            }

            Quaternion rotation = Quaternion.Euler(euler);
            if (UsesRevolver && reloadElapsed >= reloadDuration && tauntElapsed >= TauntDuration && throwElapsed >= ThrowDuration && !ultCasting)
                rotation = Quaternion.LookRotation(Vector3.forward * 30f - position - new Vector3(0f, -0.015f, 0.36f)) * rotation;

            // Blok: sekyry se zkrizi pred hracem.
            if (blockBlend > 0.001f)
            {
                position = Vector3.Lerp(position, new Vector3(0.10f * hand.side, -0.22f, 0.62f), blockBlend);
                rotation = Quaternion.Slerp(rotation, Quaternion.Euler(-6f, 0f, 38f * hand.side), blockBlend);
            }

            hand.holder.localPosition = position;
            hand.holder.localRotation = rotation;
            UpdateRevolverParts(hand);
            UpdateLauncherBarrel(hand);

            if (fpArms != null)
            {
                // (rameno se posune k rukojeti, kdyz na ni paze nedosahne - kratsi paze postavy, velka zbran; luk v natazene ruce)
                fpArms.Solve(hand.side > 0f, hand.holder.TransformPoint(hand.grip), hand.holder, root.transform, false, true);
                // (leva ruka pusti predpazbi, kdyz drzi uspavaci pistolku - jen u podpory v prvni osobe)
                // leva ruka na predpazbi: bez konecku prstu (vykukovaly pred bokem zbrane); s pistolkou zase cela
                // (s plechovkou zase cela - prsty ji obemknou)
                // Honza: leva ruka pri prebiti (bedna a kohout)
                if (hand.side > 0f && !hand.twoHanded) SolveSlapHand(hand);
                // Honza: hod miny levou rukou (ma prednost i pred prebijenim)
                if (hand.side > 0f && !hand.twoHanded) SolveMineThrowHand();
                // Honza: detonator pri odpaleni min (ma prednost pred hodem i prebijenim)
                if (hand.side > 0f && !hand.twoHanded) SolveDetonatorHand();
                if (hand.twoHanded && LeftSupportOnly)
                    fpArms.SetFingertipsHidden(false, !DartShowing && !(reloadCan != null && reloadCan.gameObject.activeSelf) && !SwapHandBusy);
                // pistolku drzi leva ruka - plechovka mezitim pryc
                if (hand.twoHanded && LeftSupportOnly && DartShowing && reloadCan != null && reloadCan.gameObject.activeSelf)
                    reloadCan.gameObject.SetActive(false);
                if (hand.twoHanded && !(DartShowing && LeftSupportOnly))
                {
                    // podpora jen v prvni osobe: dlan prilozena zespodu k predpazbi, prsty za zbrani
                    // (Davidovo prebijeni: ruka s plechovkou)
                    if (LeftSupportOnly)
                    {
                        if (!SolveCanHand(hand) && !SolveSwapHand(hand))
                            fpArms.Solve(false, hand.holder.TransformPoint(SupportPoint(hand)), hand.holder, root.transform, true, false, true);
                    }
                    else
                        fpArms.Solve(false, hand.holder.TransformPoint(hand.foregrip), hand.holder, root.transform, true);
                }
                if (UsesRevolver && hand.revolver != null)
                {
                    if (reloadElapsed < reloadDuration)
                        fpArms.Solve(false, hand.revolver.SupportPoint.position, hand.revolver.SupportPoint, root.transform, true, true);
                    else fpArms.ResetArm(false);
                }

                // luk: pri natahovani drzi prava ruka tetivu za konec sipu a tahne ji s nim dozadu
                if (builtModel == HeldModel.Bow && hand.side < 0f && hand.hasNock)
                {
                    float charge = ghost ? GhostCharge : shooting != null ? shooting.ChargeFraction : 0f;
                    bool arrowShown = hand.arrow == null || hand.arrow.gameObject.activeSelf;
                    if (charge > 0.01f && arrowShown)
                    {
                        Vector3 nock = hand.holder.TransformPoint(hand.nockLocal + Vector3.back * CameraDrawFor(charge));
                        fpArms.Solve(true, nock, hand.holder, root.transform, false, true);
                    }
                    else
                        fpArms.ResetArm(true);
                }
            }

            if (hand.forearm2 != null)
            {
                Vector3 fist = root.transform.InverseTransformPoint(hand.holder.TransformPoint(hand.foregrip));
                Vector3 elbow = new Vector3(-0.40f, -0.62f, 0.25f);
                Vector3 along = fist - elbow;
                hand.forearm2.localPosition = (fist + elbow) * 0.5f;
                hand.forearm2.localRotation = Quaternion.FromToRotation(Vector3.up, along.normalized);
                hand.forearm2.localScale = new Vector3(0.095f, along.magnitude * 0.5f, 0.095f);
            }

            if (hand.forearm != null)
            {
                Vector3 fist = root.transform.InverseTransformPoint(hand.holder.TransformPoint(hand.grip));
                Vector3 elbow = new Vector3(0.50f * hand.side, -0.62f, 0.22f);
                Vector3 along = fist - elbow;
                hand.forearm.localPosition = (fist + elbow) * 0.5f;
                hand.forearm.localRotation = Quaternion.FromToRotation(Vector3.up, along.normalized);
                hand.forearm.localScale = new Vector3(0.095f, along.magnitude * 0.5f, 0.095f);
            }
        }
    }

    // Sip se natahuje s tetivou; po vystrelu chvili chybi, pak se objevi novy.
    void UpdateArrows()
    {
        float charge = ghost ? GhostCharge : shooting != null ? shooting.ChargeFraction : 0f;
        bool rapidDrawing = !ghost && shooting != null && shooting.RapidDrawing;
        // vystrel = natazeni spadne (u rychlopalby z plneho natazeni na zacatek dalsiho)
        bool released = rapidDrawing ? lastCharge > 0.6f && charge < lastCharge - 0.5f : lastCharge > 0.05f && charge <= 0.001f;
        if (released)
            arrowHiddenUntil = Time.time + (rapidDrawing ? 0.05f : ArrowHiddenAfterShot);
        lastCharge = charge;

        bool show = Time.time >= arrowHiddenUntil;
        float draw = DrawLength * Mathf.SmoothStep(0f, 1f, charge);
        // v prvni osobe se sip na luku objevi az pri natahovani (i kratke - hned vystrel)
        foreach (var hand in hands) DrawArrow(hand, hand.side < 0f ? CameraDrawFor(charge) : draw, show && charge > 0.001f);
        foreach (var hand in bodyHands) DrawArrow(hand, draw, show);
    }

    static void DrawArrow(Hand hand, float draw, bool show)
    {
        if (hand == null || hand.arrow == null) return;
        if (hand.arrow.gameObject.activeSelf != show)
            hand.arrow.gameObject.SetActive(show);
        hand.arrow.localPosition = hand.arrowRest + hand.arrowBack * draw;
    }

    // Luk v leve ruce (prvni osoba): sip z modelu luku sedi tam, kde ho ma model (pro svisly luk v prave ruce).
    // Presadi se na operku sipu tesne nad rukojeti (na levou stranu madla), osou ve smeru mireni, konec na tetive.
    const float ArrowRestAbove = 0.06f;   // operka nad stredem rukojeti (m)
    const float BraceHeight = 0.17f;      // tetiva za madlem (m) - tam lezi konec sipu

    public static void SnapArrowToRest(Transform holder, Vector3 grip)
    {
        Transform arrow = null;
        foreach (var child in holder.GetComponentsInChildren<Transform>(true))
            if (child != holder && child.name.IndexOf("arrow", System.StringComparison.OrdinalIgnoreCase) >= 0) { arrow = child; break; }
        if (arrow == null) return;
        var filter = arrow.GetComponentInChildren<MeshFilter>(true);
        if (filter == null || filter.sharedMesh == null) return;

        // osa sipu = nejdelsi rozmer jeho meshe
        var bounds = filter.sharedMesh.bounds;
        Vector3 size = bounds.size;
        Vector3 axisLocal = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
        float lengthLocal = Vector3.Dot(size, axisLocal);
        Vector3 axisWorld = filter.transform.TransformVector(axisLocal * lengthLocal);
        float length = axisWorld.magnitude;
        Vector3 axis = holder.InverseTransformDirection(axisWorld).normalized;
        if (axis.z < 0f) axis = -axis;   // hrot dopredu (v puvodnim modelu miri sip dopredu)

        // natocit osou ve smeru mireni (+Z drzaku)
        Quaternion turn = holder.rotation * Quaternion.FromToRotation(axis, Vector3.forward) * Quaternion.Inverse(holder.rotation);
        arrow.rotation = turn * arrow.rotation;

        // stred sipu: nad rukojeti, kousek vlevo od madla; konec sipu na tetive za madlem
        Vector3 rest = grip + new Vector3(-0.02f, ArrowRestAbove, 0f);
        Vector3 center = new Vector3(rest.x, rest.y, rest.z - BraceHeight + length * 0.5f);
        Vector3 currentCenter = filter.transform.TransformPoint(bounds.center);
        arrow.position += holder.TransformPoint(center) - currentCenter;
    }

    static void FindArrow(Hand hand)
    {
        foreach (var child in hand.holder.GetComponentsInChildren<Transform>(true))
        {
            if (child == hand.holder || child.name.IndexOf("arrow", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            hand.arrow = child;
            hand.arrowRest = child.localPosition;
            // smer "dozadu" (k hraci) v prostoru rodice sipu, vcetne meritka
            hand.arrowBack = child.parent.InverseTransformVector(hand.holder.TransformVector(Vector3.back));

            // zadni konec sipu (nejmensi z v drzaku) = misto, kde se drzi tetiva
            bool any = false;
            Vector3 nock = Vector3.zero;
            foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
            {
                var b = renderer.bounds;
                Vector3 center = hand.holder.InverseTransformPoint(b.center);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    float z = hand.holder.InverseTransformPoint(corner).z;
                    if (!any || z < nock.z) { nock = new Vector3(center.x, center.y, z); any = true; }
                }
            }
            hand.nockLocal = nock;
            hand.hasNock = any;
            return;
        }
    }

    // Maxuv skok s uderem (Shift): napraah dozadu nahoru po dobu 'windup', mohutny sek dopredu a dolu, navrat.
    float reachStart = -100f, reachWindup = 0.4f;
    const float ReachStrike = 0.15f, ReachRecover = 0.3f;

    public void PlayReach(float windup)
    {
        reachStart = Time.time;
        reachWindup = Mathf.Max(0.05f, windup);
    }

    const float ReachRaise = 0.3f;   // jak rychle se kosa zvedne do napraahu

    void AnimateReach(float t, ref Vector3 position, ref Vector3 euler)
    {
        Vector3 raisedPos = new Vector3(-0.05f, 0.12f, -0.12f), raisedRot = new Vector3(-45f, 20f, 0f);
        Vector3 struckPos = new Vector3(0f, -0.08f, 0.28f), struckRot = new Vector3(65f, -10f, 0f);
        Vector3 dp, de;
        if (t < reachWindup)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / Mathf.Min(reachWindup, ReachRaise));
            dp = raisedPos * k;
            de = raisedRot * k;
        }
        else if (t < reachWindup + ReachStrike)
        {
            float k = Mathf.SmoothStep(0f, 1f, (t - reachWindup) / ReachStrike);
            dp = Vector3.Lerp(raisedPos, struckPos, k);
            de = Vector3.Lerp(raisedRot, struckRot, k);
        }
        else
        {
            float k = Mathf.SmoothStep(0f, 1f, (t - reachWindup - ReachStrike) / ReachRecover);
            dp = Vector3.Lerp(struckPos, Vector3.zero, k);
            de = Vector3.Lerp(struckRot, Vector3.zero, k);
        }
        position += dp;
        euler += de;
    }

    // Kosa: vodorovny sek pred sebou podle Maxovy animace utoku (Mixamo): napraah nahoru dozadu pres prave rameno,
    // rychly sek zprava doleva s mirnym klesanim, chvile dotazeni vlevo a navrat. Kosa se polozi na bok
    // (cepel naplocho dopredu) a cela se otoci kolem hrace.
    static readonly Vector3 SlashPivot = new Vector3(0f, -0.3f, 0f);

    static void SlashPose(float angle, float lift, out Vector3 position, out Vector3 euler)
    {
        Vector3 start = new Vector3(0.30f, -0.18f + lift, 0.42f);
        position = SlashPivot + Quaternion.Euler(0f, angle, 0f) * (start - SlashPivot);
        euler = new Vector3(5f - lift * 120f, angle, 90f);   // nahore cepel spickou nahoru, dole naplocho
    }

    void Animate(float t, ref Vector3 position, ref Vector3 euler)
    {
        if (builtModel == HeldModel.Scythe)
        {
            const float Windup = 0.18f, Strike = 0.5f, Hold = 0.62f;
            float angle, lift, blend;
            if (t < Windup)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / Windup);
                blend = u; angle = Mathf.Lerp(60f, 80f, u); lift = 0.10f * u;
            }
            else if (t < Strike)
            {
                // zrychluje do stredu, dobrzdi vlevo
                float u = Mathf.SmoothStep(0f, 1f, (t - Windup) / (Strike - Windup));
                blend = 1f; angle = Mathf.Lerp(80f, -75f, u); lift = Mathf.Lerp(0.10f, -0.04f, u);
            }
            else
            {
                blend = t < Hold ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - Hold) / (1f - Hold));
                angle = -75f; lift = -0.04f;
            }

            SlashPose(angle, lift, out Vector3 slashPosition, out Vector3 slashEuler);
            position = Vector3.Lerp(position, slashPosition, blend);
            euler = new Vector3(Mathf.LerpAngle(euler.x, slashEuler.x, blend), Mathf.LerpAngle(euler.y, slashEuler.y, blend),
                Mathf.LerpAngle(euler.z, slashEuler.z, blend));
            return;
        }

        if (Swung(builtModel))
        {
            // Zamah: nahoru dozadu, rychle dopredu a dolu, navrat.
            float angle;
            if (t < 0.25f) angle = Mathf.Lerp(0f, -35f, t / 0.25f);
            else if (t < 0.55f) angle = Mathf.Lerp(-35f, 100f, Mathf.SmoothStep(0f, 1f, (t - 0.25f) / 0.3f));
            else angle = Mathf.Lerp(100f, 0f, (t - 0.55f) / 0.45f);

            euler.x += angle;
            position += new Vector3(0f, -0.06f, 0.16f) * Mathf.Clamp01(angle / 100f);
        }
        else if (builtWeapon != null && builtWeapon.heavyRecoil)
        {
            // tezka zbran: jen cuknuti dozadu (hlaven se skoro nezvedne)
            float kick = Mathf.Sin(t * Mathf.PI) * swingStrength;
            position += new Vector3(0f, 0.004f, -0.07f) * kick;
            euler.x -= 2f * kick;
        }
        else
        {
            float kick = Mathf.Sin(t * Mathf.PI) * swingStrength;
            position += new Vector3(0f, 0.01f, -0.1f) * kick;
            euler.x -= 9f * kick;
        }
    }

    void AnimateReload(float t, float side, ref Vector3 position, ref Vector3 euler)
    {
        if (UsesRevolver)
        {
            RevolverVisual.ReloadPose(t, out var offset, out var turn);
            position += offset;
            euler += turn;
            return;
        }
        if (SwapReload)
        {
            // Viktor: zbran zustava v obraze, natoci se zasobnikem ke kamere; pri vytazeni a zasunuti poskoci
            float tilt = Mathf.Min(Mathf.SmoothStep(0f, 1f, t / 0.12f), Mathf.SmoothStep(0f, 1f, (1f - t) / 0.12f));
            position += new Vector3(-0.04f, 0.05f, -0.03f) * tilt;
            euler += new Vector3(-8f, -10f, -20f) * tilt;
            float pulled = SlapKick(t, SwapGrab + 0.04f), inserted = SlapKick(t, SwapInsert);
            position += new Vector3(0f, -0.012f, 0f) * pulled + new Vector3(0f, 0.02f, 0f) * inserted;
            euler += new Vector3(3f, 0f, 0f) * pulled + new Vector3(-5f, 0f, 0f) * inserted;
            return;
        }

        if (SlapReload)
        {
            // Honza: zbran se natoci levym bokem (bednou) ke kamere; pri uderu, nasazeni a pusteni packy poskoci
            float tilt = Mathf.Min(Mathf.SmoothStep(0f, 1f, t / 0.12f), Mathf.SmoothStep(0f, 1f, (1f - t) / 0.12f));
            position += new Vector3(-0.05f, 0.04f, -0.02f) * tilt;
            euler += new Vector3(-4f, -8f, -15f) * tilt;
            float strike = SlapKick(t, SlapStrike), insert = SlapKick(t, SlapInsert), release = SlapKick(t, SlapRelease);
            position += new Vector3(0.025f, -0.01f, 0f) * strike + new Vector3(0f, 0.02f, 0f) * insert + new Vector3(0f, 0f, -0.01f) * release;
            euler += new Vector3(0f, 0f, 7f) * strike + new Vector3(-5f, 0f, 0f) * insert + new Vector3(-2f, 0f, 0f) * release;
            return;
        }

        if (MonsterReload)
        {
            // David: zbran zustava v obraze, jen se natoci levym bokem ke kamere (zpet az po narazu, AnimateCanAfter)
            float tilt = Mathf.SmoothStep(0f, 1f, t / 0.15f);
            position += CanGunOffset * tilt;
            euler += CanGunTurn * tilt;
            return;
        }

        // Plynuly prechod do polohy prebijeni a zpet.
        float blend = Mathf.Min(Mathf.SmoothStep(0f, 1f, t / 0.18f), Mathf.SmoothStep(0f, 1f, (1f - t) / 0.18f));

        if (builtWeapon != null && builtWeapon.reloadAnimation)
        {
            // Granatomet: hlaven nahoru a ke stredu obrazu, pak tri "zasunuti" granatu.
            position += new Vector3(-0.12f * side, -0.05f, -0.10f) * blend;
            euler += new Vector3(-48f, -28f * side, 22f * side) * blend;

            float u = Mathf.InverseLerp(0.2f, 0.8f, t);
            if (u > 0f && u < 1f)
            {
                float push = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 3f));
                position.y -= 0.03f * push;
                euler.x += 7f * push;
            }
        }
        else
        {
            // Ostatni zbrane: jen se skloni dolu a vrati.
            position += new Vector3(0f, -0.30f, -0.05f) * blend;
            euler.x += 35f * blend;
        }
    }

    // ---------------- Honza: prebiti bednou ----------------

    // Kratky naskok a dozneni (rana do zbrane) od okamziku 'at' (podil doby prebiti).
    float SlapKick(float t, float at)
    {
        float d = (t - at) * reloadDuration;
        return d < 0f ? 0f : Mathf.Exp(-d * 16f) * Mathf.Min(1f, d * 60f);
    }

    void FindSlapParts(Hand hand)
    {
        slapMag = slapLever = null;
        if (hand == null || hand.holder == null) return;
        foreach (var part in hand.holder.GetComponentsInChildren<Transform>(true))
        {
            if (slapMag == null && part.name.StartsWith("Magazine")) slapMag = part;
            else if (slapLever == null && part.name.StartsWith("CockingLever")) slapLever = part;
        }
        if (slapMag == null) return;
        slapMagLocalPos = slapMag.localPosition;
        slapMagLocalRot = slapMag.localRotation;

        // rozmer bedny v drzaku zbrane (bez granatu)
        var box = HolderBounds(hand.holder, slapMag, true);
        slapMagCenter = box.center;
        slapMagExtents = box.extents;

        if (slapLever != null)
        {
            slapLeverRest = slapLever.localRotation;
            // kulicka = horni ctvrtina packy
            var lever = HolderBounds(hand.holder, slapLever, false);
            Vector3 knob = new Vector3(lever.center.x, lever.max.y - lever.size.y * 0.23f, lever.center.z);
            slapKnobLocal = slapLever.InverseTransformPoint(hand.holder.TransformPoint(knob));
        }
    }

    // Obal siti dilu v souradnicich drzaku zbrane (volitelne bez granatu).
    static Bounds HolderBounds(Transform holder, Transform part, bool skipGrenades)
    {
        var bounds = new Bounds();
        bool any = false;
        foreach (var filter in part.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            if (skipGrenades)
            {
                bool grenade = false;
                for (var t = filter.transform; t != null && t != part; t = t.parent)
                    if (t.name.StartsWith("Grenade_")) { grenade = true; break; }
                if (grenade) continue;
            }
            var b = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 p = holder.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        return bounds;
    }

    // Bedna a packa zpet na sva mista (konec nebo preruseni prebiti).
    void SlapRestore()
    {
        if (slapMag != null)
        {
            if (!slapMag.gameObject.activeSelf) slapMag.gameObject.SetActive(true);
            slapMag.localPosition = slapMagLocalPos;
            slapMag.localRotation = slapMagLocalRot;
        }
        if (slapLever != null) slapLever.localRotation = slapLeverRest;
    }

    // Bedna ve svetovych souradnicich posunuta v drzaku zbrane o 'delta' (a natocena kolem osy hlavne).
    void PlaceSlapMagazine(Transform holder, Vector3 delta, float roll)
    {
        var parent = slapMag.parent;
        Vector3 restWorld = parent != null ? parent.TransformPoint(slapMagLocalPos) : slapMagLocalPos;
        Quaternion restRot = (parent != null ? parent.rotation : Quaternion.identity) * slapMagLocalRot;
        slapMag.position = restWorld + holder.TransformVector(delta);
        slapMag.rotation = Quaternion.AngleAxis(roll, holder.forward) * restRot;
    }

    void SlapStrikeNow(Transform holder)
    {
        slapStruck = true;
        // padajici kopie bedny (i s granaty); original se schova a "nova" pak prijede zespodu
        // (i se skutecnou velikosti - model zbrane je v prefabu zmenseny, pod kamerou by kopie byla ~20x vetsi)
        var copy = Instantiate(slapMag.gameObject, root.transform, true);
        foreach (var feed in copy.GetComponentsInChildren<GrenadeFeed>(true)) Destroy(feed);
        slapFalling = copy.transform;
        slapFallAge = 0f;
        // (v souradnicich kamery: rana zleva -> doprava pod zbran a dolu, roztoci se)
        slapFallVelocity = root.transform.InverseTransformDirection(holder.TransformDirection(new Vector3(0.45f, -0.9f, 0.1f)));
        slapFallSpin = new Vector3(Random.Range(-120f, 120f), Random.Range(-90f, 90f), -260f);
        slapMag.gameObject.SetActive(false);
        if (!ghost) ProceduralSfx.Play(ProceduralSfx.BulletImpact, slapMag.position, 0.4f, 15f, false, SfxKind.Weapon);
    }

    void SlapInsertNow(Transform holder)
    {
        slapInserted = true;
        if (!slapMag.gameObject.activeSelf) slapMag.gameObject.SetActive(true);
        slapMag.localPosition = slapMagLocalPos;
        slapMag.localRotation = slapMagLocalRot;
        if (!ghost) ProceduralSfx.Play(ProceduralSfx.BulletImpact, slapMag.position, 0.45f, 15f, false, SfxKind.Weapon);
    }

    void UpdateSlapFalling()
    {
        if (slapFalling == null) return;
        slapFallAge += Time.deltaTime;
        slapFallVelocity += Vector3.down * 9.81f * Time.deltaTime;
        slapFalling.localPosition += slapFallVelocity * Time.deltaTime;
        slapFalling.localRotation = Quaternion.Euler(slapFallSpin * Time.deltaTime) * slapFalling.localRotation;
        if (slapFallAge > 0.9f)
        {
            Destroy(slapFalling.gameObject);
            slapFalling = null;
        }
    }

    // Leva ruka pri Honzove prebiti. Po konci prebiti ji jednou vrati do klidu (mimo obraz).
    void SolveSlapHand(Hand hand)
    {
        bool reloading = reloadElapsed < reloadDuration && !reloadCanceled;
        if (!SlapReload || !reloading)
        {
            if (slapArmUsed)
            {
                slapArmUsed = false;
                fpArms.ResetArm(false);
                SlapRestore();
            }
            return;
        }
        slapArmUsed = true;

        float t = reloadElapsed / reloadDuration;
        var h = hand.holder;
        var rootT = root.transform;
        float depth = FirstPersonArms.WrapPalmDepth;

        // ruka: pest dlani k bedne (zleva), dlan nahoru (nese bednu), dlan shora na kulicku packy
        Quaternion fist = Quaternion.LookRotation(h.forward, -h.right);
        Quaternion carry = FirstPersonArms.HandFrame(h, -1f, true, false);
        Quaternion grab = Quaternion.LookRotation(h.forward, (h.up - h.right * 0.5f).normalized);

        Vector3 side = slapMagCenter + new Vector3(-slapMagExtents.x, 0f, 0f);   // levy bok bedny
        Vector3 bottom = slapMagCenter + new Vector3(0f, -slapMagExtents.y, 0f);
        Vector3 strikeAt = h.TransformPoint(side) + h.right * depth;
        Vector3 windup = h.TransformPoint(side + new Vector3(-0.16f, 0.05f, -0.04f));
        Vector3 low = rootT.TransformPoint(new Vector3(-0.28f, -0.75f, 0.30f));
        Vector3 bringFrom = new Vector3(-0.05f, -0.42f, -0.04f);   // nova bedna zespodu (posun v drzaku)

        Vector3 grip;
        Quaternion frame;
        if (t < SlapStrike - 0.08f)
        {
            // ruka zespodu k bedne, napraha
            float u = Mathf.SmoothStep(0f, 1f, t / (SlapStrike - 0.08f));
            grip = Vector3.Lerp(low, windup, u);
            frame = fist;
        }
        else if (t < SlapStrike)
        {
            // uder (zrychluje)
            float u = (t - (SlapStrike - 0.08f)) / 0.08f;
            grip = Vector3.Lerp(windup, strikeAt, u * u);
            frame = fist;
        }
        else if (t < 0.42f)
        {
            if (!slapStruck) SlapStrikeNow(h);
            // dotazeni rany a dolu pro novou bednu
            float u = Mathf.SmoothStep(0f, 1f, (t - SlapStrike) / (0.42f - SlapStrike));
            grip = Vector3.Lerp(strikeAt + h.TransformVector(new Vector3(0.03f, -0.02f, 0f)), low, u);
            frame = Quaternion.Slerp(fist, carry, u);
        }
        else if (t < SlapInsert)
        {
            if (!slapStruck) SlapStrikeNow(h);
            // nova bedna zespodu (zpomaluje ke konci), ruka ji nese
            float u = (t - 0.42f) / (SlapInsert - 0.42f);
            float e = 1f - (1f - u) * (1f - u) * (1f - u);
            Vector3 delta = Vector3.Lerp(bringFrom, Vector3.zero, e);
            if (!slapMag.gameObject.activeSelf) slapMag.gameObject.SetActive(true);
            PlaceSlapMagazine(h, delta, Mathf.Lerp(-25f, 0f, e));
            grip = h.TransformPoint(bottom + delta) + h.up * depth;
            frame = carry;
        }
        else
        {
            if (!slapInserted) SlapInsertNow(h);
            Vector3 boxGrip = h.TransformPoint(bottom) + h.up * depth;
            float angle = 0f;
            if (t >= SlapPull && t < SlapRelease) angle = SlapLeverAngle * Mathf.SmoothStep(0f, 1f, (t - SlapPull) / (SlapRelease - SlapPull));
            else if (t >= SlapRelease && t < SlapLeave) angle = SlapLeverAngle * (1f - Mathf.SmoothStep(0f, 1f, (t - SlapRelease) / (SlapLeave - SlapRelease)));
            if (slapLever != null) slapLever.localRotation = slapLeverRest * Quaternion.Euler(angle, 0f, 0f);
            Vector3 knob = slapLever != null ? slapLever.TransformPoint(slapKnobLocal) : h.TransformPoint(slapMagCenter + Vector3.up * 0.1f);

            if (t < SlapPull)
            {
                // od bedny na kulicku packy
                float u = Mathf.SmoothStep(0f, 1f, (t - SlapInsert) / (SlapPull - SlapInsert));
                grip = Vector3.Lerp(boxGrip, knob, u) + h.up * 0.05f * Mathf.Sin(u * Mathf.PI);
                frame = Quaternion.Slerp(carry, grab, u);
            }
            else if (t < SlapRelease)
            {
                // natazeni: ruka drzi kulicku
                grip = knob;
                frame = grab;
                slapReleaseAt = rootT.InverseTransformPoint(knob);
            }
            else
            {
                // pusti (packa odskoci zpet) a odejde dolu mimo obraz
                float u = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SlapRelease + 0.02f, 1f, t));
                grip = Vector3.Lerp(rootT.TransformPoint(slapReleaseAt) + h.up * 0.02f, low, u);
                frame = grab;
                if (t >= SlapLeave && slapLever != null) slapLever.localRotation = slapLeverRest;
            }
        }
        fpArms.SolveFrame(false, grip, frame, rootT);
    }

    // Po narazu plechovky: zbran poskoci od rany (doprava) a srovna se zpet do klidu.
    void AnimateCanAfter(float a, ref Vector3 position, ref Vector3 euler)
    {
        float back = 1f - Mathf.SmoothStep(0f, 1f, a / CanReturnSeconds);
        float kick = Mathf.Exp(-a * 18f) * Mathf.Min(1f, a * 60f);
        position += CanGunOffset * back + new Vector3(0.025f, 0.008f, 0f) * kick;
        euler += CanGunTurn * back + new Vector3(-3f, 4f, -7f) * kick;
    }

    // Bod predpazbi, kam leva dlan v prvni osobe podpira zbran (v drzaku zbrane).
    static Vector3 SupportPoint(Hand hand)
    {
        // znacka muze jet s dilem zbrane (hlaven granatometu) - pak aktualni poloha
        if (hand.foregripMarker != null && hand.holder != null) return hand.holder.InverseTransformPoint(hand.foregripMarker.position);
        return hand.foregripContact ? hand.foregrip : hand.foregrip + new Vector3(0f, 0.015f, 0f);
    }

    // Stred plechovky v okamziku narazu (v drzaku zbrane): u leveho boku zbrane kousek za predpazbim.
    static Vector3 CanHitPoint(Hand hand) => SupportPoint(hand) + new Vector3(-0.052f, 0.035f, -0.05f);

    void CanImpact()
    {
        canAfter = 0f;
        var hand = hands[0];
        Vector3 at = hand.holder != null ? hand.holder.TransformPoint(CanHitPoint(hand)) : root.transform.position;
        if (reloadCan != null) reloadCan.gameObject.SetActive(false);
        CanMist(at);
        // (v zaznamu uz zvuk je - killcam ho prehraje sam)
        if (!ghost) ProceduralSfx.Play(ProceduralSfx.CanBurst, at, 0.55f, 15f, false, SfxKind.Weapon);
    }

    // Jemna zelena mlha z rozplynute plechovky - drzi se u kamery (jako zbran), pomalu se rozplyva.
    void CanMist(Vector3 position)
    {
        var go = new GameObject("FX_MonsterMist");
        go.transform.SetParent(root.transform, false);
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 1f, 0.45f, 0.55f), new Color(0.2f, 0.85f, 0.3f, 0.4f));
        main.gravityModifier = -0.03f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)70), new ParticleSystem.Burst(0.08f, (short)30) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.04f;

        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.drag = 2.5f;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.04f;
        noise.frequency = 1.5f;

        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(0.75f, 1f, 0.6f), 0f), new GradientColorKey(new Color(0.3f, 0.9f, 0.35f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(gradient);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.8f)));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = Fx.ParticleMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
    }

    // Leva ruka pri Davidove prebijeni (a navrat po narazu). Vraci false, kdyz se zrovna neprebiji (ruka na predpazbi).
    bool SolveCanHand(Hand hand)
    {
        bool reloading = reloadElapsed < reloadDuration && !reloadCanceled;
        bool returning = !reloading && canAfter < CanReturnSeconds;
        if (!MonsterReload || (!reloading && !returning))
        {
            if (reloadCan != null && reloadCan.gameObject.activeSelf) reloadCan.gameObject.SetActive(false);
            return false;
        }

        var rootT = root.transform;
        // ruka na predpazbi (zacatek a konec) - stejne jako bez prebijeni
        Quaternion supportFrame = FirstPersonArms.HandFrame(hand.holder, -1f, true, true);
        Vector3 supportGrip = hand.holder.TransformPoint(SupportPoint(hand)) + supportFrame * Vector3.down * FirstPersonArms.WrapPalmDepth;

        // plechovka v okamziku narazu (vuci kamere)
        Vector3 hitPos = rootT.InverseTransformPoint(hand.holder.TransformPoint(CanHitPoint(hand)));
        Quaternion hitRot = Quaternion.Inverse(rootT.rotation) * hand.holder.rotation * Quaternion.Euler(0f, 0f, 8f);

        Vector3 canPos;
        Quaternion canRot;
        float handBlend = 1f;   // 0 = ruka na predpazbi, 1 = ruka drzi plechovku
        bool showCan = true;
        if (reloading)
        {
            float t = reloadElapsed / reloadDuration;
            if (t < 0.22f)
            {
                // ruka pusti predpazbi a sjede dolu mimo obraz (tam vezme plechovku)
                handBlend = Mathf.SmoothStep(0f, 1f, t / 0.22f);
                canPos = CanLowPos;
                canRot = Quaternion.Euler(CanLowTurn);
                showCan = handBlend > 0.9f;
            }
            else if (t < 0.55f)
            {
                // zvedne plechovku do obrazu (vlevo dole)
                float u = Mathf.SmoothStep(0f, 1f, (t - 0.22f) / 0.33f);
                canPos = Vector3.Lerp(CanLowPos, CanUpPos, u) + Vector3.left * 0.05f * Mathf.Sin(u * Mathf.PI);
                canRot = Quaternion.Slerp(Quaternion.Euler(CanLowTurn), Quaternion.Euler(CanUpTurn), u);
            }
            else if (t < 0.82f)
            {
                // chvili ukaze, pak napraha
                float u = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.82f, t));
                canPos = Vector3.Lerp(CanUpPos, CanBackPos, u);
                canRot = Quaternion.Slerp(Quaternion.Euler(CanUpTurn), Quaternion.Euler(CanBackTurn), u);
            }
            else
            {
                // svih do boku zbrane - zrychluje az do narazu
                float u = (t - 0.82f) / 0.18f;
                u *= u;
                canPos = Vector3.Lerp(CanBackPos, hitPos, u) + Vector3.up * 0.04f * Mathf.Sin(u * Mathf.PI);
                canRot = Quaternion.Slerp(Quaternion.Euler(CanBackTurn), hitRot, u);
            }
        }
        else
        {
            // po narazu: ruka chvili v miste rany, pak zpet na predpazbi
            canPos = hitPos;
            canRot = hitRot;
            showCan = false;
            handBlend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, CanReturnSeconds, canAfter));
        }

        // leva ruka drzi plechovku: hrbet ruky ven (vlevo), dlan k plechovce, prsty kolem ni
        Vector3 canWorld = rootT.TransformPoint(canPos);
        Quaternion canWorldRot = rootT.rotation * canRot;
        Quaternion fist = Quaternion.LookRotation(canWorldRot * Vector3.forward, canWorldRot * Vector3.left);
        Vector3 fistGrip = canWorld - canWorldRot * Vector3.right * CanPalmDepth;
        // prechod mezi predpazbim a plechovkou obloukem ven a dolu (at ruka neprojede zbrani)
        Vector3 grip = Vector3.Lerp(supportGrip, fistGrip, handBlend)
            + rootT.TransformDirection(new Vector3(-0.04f, -0.04f, 0f)) * Mathf.Sin(handBlend * Mathf.PI);
        fpArms.SolveFrame(false, grip, Quaternion.Slerp(supportFrame, fist, handBlend), rootT);

        if (reloadCan == null)
        {
            var go = new GameObject("ReloadMonster");
            go.transform.SetParent(rootT, false);
            MonsterCan.Build(go.transform, true);
            go.transform.localScale = Vector3.one;   // skutecna velikost (v letu je zvetsena)
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            reloadCan = go.transform;
        }
        if (reloadCan.gameObject.activeSelf != showCan) reloadCan.gameObject.SetActive(showCan);
        reloadCan.localPosition = canPos;
        reloadCan.localRotation = canRot;
        return true;
    }

    void Rebuild(bool owner)
    {
        foreach (var old in bodyHands)
            if (old.holder != null)
                Destroy(old.holder.gameObject);

        if (root != null)
            Destroy(root);

        root = null;
        fpArms = null;
        reloadCan = null;   // (byla pod root)
        heldMine = null;
        mineArmUsed = false;
        detonator = null;
        detonateArmUsed = false;
        slapMag = slapLever = null;
        slapArmUsed = false;
        swapMag = null;
        swapArmUsed = false;
        launcherBarrel = null;
        if (slapFalling != null) Destroy(slapFalling.gameObject);
        slapFalling = null;
        ampPointing = false;   // (nove ruce maji sevrenou pest)
        hands = new Hand[0];
        bodyHands = new Hand[0];
        if (dartPistol != null) { Destroy(dartPistol.gameObject); dartPistol = null; }
        bodySupportArm = null;
        bodyMainArm = null;
        if (visual != null) { visual.GripRight = false; visual.GripLeft = false; visual.HoldShootPose = false; visual.AimOnScope = false; visual.TwistShoulders = false; }
        stormMaterials = null;
        stormShown = false;
        powerMaterials = null;
        powerShown = false;
        rageMaterials = null;
        rageFlames = null;
        rageShown = false;
        built = true;
        builtVersion = visual != null ? visual.Version : 0;
        builtWeapon = CurrentWeapon;
        builtAsOwner = owner;
        nextHand = 0;

        var weapon = builtWeapon;
        if (weapon == null) return;

        var model = weapon.heldModel;
        if (model == HeldModel.Auto)
            model = weapon.IsMelee ? HeldModel.Axe : HeldModel.Gun;
        if (model == HeldModel.None) return;

        builtModel = model;

        int count = weapon.dualWield ? 2 : 1;
        bool hasModel = visual != null && visual.HasModel;

        // Zbrane u kamery: majitel vzdy, ostatni jen kdyz postava nema model (kapsle).
        if (owner || !hasModel)
        {
            Transform parent = owner && ghost ? ghostCamera
                : owner && fpc != null && fpc.playerCamera != null ? fpc.playerCamera.transform : transform;
            root = new GameObject("HeldWeapons");
            root.transform.SetParent(parent, false);

            hands = new Hand[count];
            for (int i = 0; i < count; i++)
            {
                // Jedna zbran je vpravo, dve jsou po obou stranach. Luk (v prvni osobe) drzi leva ruka jako Hanzo.
                bool bowLeft = owner && count == 1 && model == HeldModel.Bow;
                float side = count == 1 ? (bowLeft ? -1f : 1f) : (i == 0 ? 1f : -1f);

                var hand = new Hand { side = side };
                var holder = new GameObject(i == 0 ? "RightHand" : "LeftHand").transform;
                holder.SetParent(root.transform, false);
                hand.holder = holder;

                hand.restPosition = owner
                    ? (model == HeldModel.Scythe ? new Vector3(0.34f * side, -0.45f, 0.50f)
                        : model == HeldModel.Axe ? new Vector3(0.36f * side, -0.40f, 0.55f) : new Vector3(0.30f * side, -0.28f, 0.50f))
                    : new Vector3(0.62f * side, 0.15f, 0.30f);
                hand.restEuler = model == HeldModel.Scythe ? new Vector3(10f, 0f, -12f * side)
                    : model == HeldModel.Axe ? new Vector3(15f, 0f, -9f * side) : Vector3.zero;
                if (bowLeft)
                {
                    // luk v natazene leve ruce zleva zdola, sklopeny sikmo pres obraz (horni rameno doprava nahoru)
                    hand.restPosition = new Vector3(-0.04f, -0.22f, 0.55f);
                    hand.restEuler = new Vector3(4f, 6f, -62f);
                }

                BuildModel(holder, weapon, model, side);
                if (bowLeft) SnapArrowToRest(holder, GripInWeapon(weapon, model));
                FindArrow(hand);
                holder.localPosition = hand.restPosition;
                holder.localRotation = Quaternion.Euler(hand.restEuler);
                hands[i] = hand;

                hand.grip = GripInWeapon(weapon, model);

                // jedna velka zbran obema rukama: vic u stredu a niz, leva ruka na predpazbi
                if ((weapon.twoHanded || (owner && weapon.firstPersonTwoHanded)) && count == 1 && model == HeldModel.Gun)
                {
                    hand.twoHanded = true;
                    hand.foregrip = ForegripInWeapon(holder, hand.grip);
                    // podpora jen v prvni osobe: presny spodek predpazbi ze znacky v prefabu (kdyz ji model ma)
                    if (owner && weapon.firstPersonTwoHanded)
                        foreach (var marker in holder.GetComponentsInChildren<Transform>(true))
                            if (marker.name == "Foregrip")
                            {
                                hand.foregrip = holder.InverseTransformPoint(marker.position);
                                hand.foregripContact = true;
                                hand.foregripMarker = marker;
                                break;
                            }
                    if (owner && weapon.twoHanded && !weapon.firstPersonTwoHanded)
                    {
                        // strelba od pasu: zbran nize u praveho boku, hlaven sikmo nahoru ke stredu obrazovky
                        hand.restPosition = new Vector3(0.22f, -0.42f, 0.34f);
                        hand.restEuler = new Vector3(-7f, -7f, 4f);
                        holder.localPosition = hand.restPosition;
                        holder.localRotation = Quaternion.Euler(hand.restEuler);
                    }
                }
            }

            // Ruce z prvni osoby: skutecne paze modelu postavy, jinak jednoducha pest s rukavem.
            if (owner)
            {
                var currentHero = CurrentHero;
                var prefab = currentHero != null ? currentHero.characterPrefab : null;
                fpArms = FirstPersonArms.Create(root.transform, prefab);
                // leva ruka podpira predpazbi: prsty jen mirne pokrcene (vedou pod zbrani a nahoru po jeji druhe strane,
                // schovane za ni), palec skoro natazeny podel boku zbrane
                if (fpArms != null && weapon.firstPersonTwoHanded && count == 1 && model == HeldModel.Gun)
                {
                    fpArms.CurlHand(false, -0.55f, -0.15f);
                    // puska: ukazovacek prave ruky natazeny dopredu na spoust (ne v pesti kolem rukojeti)
                    fpArms.PoseIndexFinger(true, -0.15f, -0.35f, -0.25f);
                }
                // Viktor: zasobnik pro prebiti, hlaven granatometu
                if (count == 1) FindSwapParts(hands[0], weapon);
                // Honza: bedna zasobniku a kohout pro prebiti
                if (weapon.magazineSlapReload && count == 1) FindSlapParts(hands[0]);
                if (fpArms == null)
                    foreach (var hand in hands)
                        BuildArm(hand, weapon, model);
            }
        }

        // Zbrane v rukou postavy (pripevnene ke kostem, nese je animace).
        if (hasModel)
        {
            visual.EvaluateIdlePose();
            bodyHands = new Hand[count];
            for (int i = 0; i < count; i++)
            {
                // luk jako u kamery: v leve ruce (jako Hanzo), prava ruka tahne tetivu
                bool bowBody = count == 1 && model == HeldModel.Bow && visual.BodyAnimator != null
                    && visual.GetBone(HumanBodyBones.Chest) != null;
                float side = count == 1 ? (bowBody ? -1f : 1f) : (i == 0 ? 1f : -1f);
                var hand = new Hand { side = side };
                var holder = new GameObject(i == 0 ? "BodyRightHand" : "BodyLeftHand").transform;
                hand.holder = holder;
                BuildModel(holder, weapon, model, side);
                if (bowBody) SnapArrowToRest(holder, GripInWeapon(weapon, model));
                FindArrow(hand);
                bodyHands[i] = hand;
                // prsty postavy sevrene kolem zbrane (prava, u dvou zbrani i leva)
                if (side > 0f) visual.GripRight = true;
                else visual.GripLeft = true;

                if (bowBody)
                {
                    hand.bowBody = true;
                    visual.TwistShoulders = true;   // pri natahovani trup celem dopredu (luk na hrudi)
                    hand.grip = GripInWeapon(weapon, model);
                    AttachBowAtChest(hand);
                    bodySupportArm = FirstPersonArms.BuildArm(visual.BodyAnimator, false);   // madlo
                    bodyMainArm = FirstPersonArms.BuildArm(visual.BodyAnimator, true);       // tetiva
                    visual.GripRight = true;
                    continue;
                }

                // zbran obema rukama s vlastni animaci strelby hrdiny (napr. z Mixama): postava drzi zbran stale namirenou
                // (prvni snimek animace), vystrel prehraje celou animaci; puska v prave ruce srovnana podle pozy strelby,
                // leva ruka na predpazbi (IK po animaci, viz LateUpdate)
                bool scopeAim = CurrentHero != null && CurrentHero.scopeAimPose;
                bool hipFire = CurrentHero != null && CurrentHero.hipFire;
                if ((weapon.twoHanded || scopeAim) && !hipFire && count == 1 && model == HeldModel.Gun && visual.BodyAnimator != null && visual.HasHeroShoot)
                {
                    visual.AimOnScope = scopeAim && !weapon.twoHanded;   // namiri jen pri mireni dalekohledem (a chvili po vystrelu)
                    hand.twoHanded = true;
                    hand.grip = GripInWeapon(weapon, model);
                    hand.foregrip = ForegripInWeapon(holder, hand.grip, 0.42f, 0.38f);
                    visual.HoldShootPose = true;
                    visual.EvaluateShootPose();
                    AttachToBone(hand, weapon, model, true);
                    visual.EvaluateIdlePose();
                    bodySupportArm = FirstPersonArms.BuildArm(visual.BodyAnimator, false);
                    visual.GripLeft = true;
                }
                // zbran obema rukama: puska v pohotovosti pred pravym bokem (pripevnena k trupu), obe ruce na ni
                // (prava na rukojeti, leva zespodu na predpazbi - IK po animaci, viz LateUpdate)
                else if (weapon.twoHanded && count == 1 && model == HeldModel.Gun && visual.BodyAnimator != null && AttachAtHip(hand))
                {
                    hand.twoHanded = true;
                    hand.grip = GripInWeapon(weapon, model);
                    hand.foregrip = ForegripInWeapon(holder, hand.grip, 0.42f, 0.38f);   // ruce od sebe, leva daleko pod hlavni
                    bodyMainArm = FirstPersonArms.BuildArm(visual.BodyAnimator, true);
                    bodySupportArm = FirstPersonArms.BuildArm(visual.BodyAnimator, false);
                    visual.GripLeft = true;
                }
                else if (UsesRevolver && visual.BodyAnimator != null && visual.HasHeroShoot)
                {
                    hand.grip = GripInWeapon(weapon, model);
                    visual.HoldShootPose = true;
                    visual.EvaluateShootPose(true);
                    AttachToBone(hand, weapon, model, true);
                    // Keep wrist IK from rotating the prop a second time.
                    holder.SetParent(visual.ModelRoot, true);
                    bodyMainArm = FirstPersonArms.BuildArm(visual.BodyAnimator, true);
                    bodySupportArm = FirstPersonArms.BuildArm(visual.BodyAnimator, false);
                    visual.EvaluateIdlePose();
                }
                else
                    AttachToBone(hand, weapon, model);
            }

            // Poza sekyr v bloku (jen sekyry): spocita se z pozy s rukama vpredu.
            if (model == HeldModel.Axe)
            {
                visual.EvaluateBlockPose();
                foreach (var hand in bodyHands)
                    ComputeBlockPose(hand, weapon, model);

                visual.EvaluateIdlePose();
            }
        }
        if (hookThrown) ApplyHookThrown();
    }

    void BuildArm(Hand hand, WeaponDefinition weapon, HeldModel model)
    {
        var hero = CurrentHero;
        Color sleeve = hero != null ? hero.sleeveColor : new Color(0.25f, 0.27f, 0.32f);
        Color skin = hero != null ? hero.skinColor : new Color(0.87f, 0.67f, 0.55f);

        var fist = Limb(PrimitiveType.Sphere, hand.holder, skin);
        fist.localPosition = hand.grip;
        fist.localScale = new Vector3(0.105f, 0.10f, 0.125f);

        var cuff = Limb(PrimitiveType.Cylinder, root.transform, sleeve);
        hand.forearm = cuff;

        if (hand.twoHanded)
        {
            var support = Limb(PrimitiveType.Sphere, hand.holder, skin);
            support.localPosition = hand.foregrip;
            support.localScale = new Vector3(0.10f, 0.095f, 0.12f);
            hand.forearm2 = Limb(PrimitiveType.Cylinder, root.transform, sleeve);
        }
    }

    // Predpazbi: kus pred rukojeti smerem k hlavni (podle delky modelu), kousek pod osou zbrane.
    public static Vector3 ForegripInWeapon(Transform holder, Vector3 grip, float fraction = 0.45f, float maxForward = 0.32f)
    {
        float front = grip.z + 0.25f;
        bool any = false;
        foreach (var renderer in holder.GetComponentsInChildren<Renderer>())
        {
            var b = renderer.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                float z = holder.InverseTransformPoint(corner).z;
                if (!any || z > front) { front = z; any = true; }
            }
        }
        float forward = Mathf.Clamp((front - grip.z) * fraction, 0.15f, maxForward);
        return new Vector3(grip.x, grip.y + 0.01f, grip.z + forward);
    }

    static Transform Limb(PrimitiveType type, Transform parent, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        DestroyImmediate(go.GetComponent<Collider>());
        go.name = "Arm";
        go.transform.SetParent(parent, false);

        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = Fx.NewLit(color);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    public static Vector3 GripInWeapon(WeaponDefinition weapon, HeldModel model)
    {
        if (model == HeldModel.Bow) return new Vector3(0f, 0f, 0.10f);

        return Swung(model) ? new Vector3(0f, 0.12f, 0f)
            : weapon.IsProjectile ? new Vector3(0f, -0.10f, 0.12f)
            : new Vector3(0f, -0.07f, 0.05f);
    }

    void ComputeBlockPose(Hand hand, WeaponDefinition weapon, HeldModel model)
    {
        bool right = hand.side > 0f;
        var bone = visual.GetHand(right);
        if (bone == null || hand.holder == null) return;

        var grip = visual.GetGrip(right);
        Vector3 gripWorld = grip != null ? grip.position : bone.position;

        Quaternion rotation = visual.ModelRoot.rotation * Quaternion.Euler(-6f, 0f, 38f * hand.side);
        Vector3 position = gripWorld - rotation * GripInWeapon(weapon, model);

        hand.blockPosition = bone.InverseTransformPoint(position);
        hand.blockRotation = Quaternion.Inverse(bone.rotation) * rotation;
        hand.hasBlockPose = true;
    }

    // Ostatni hraci maji zbran v ruce postavy: pripevni se ke kosti ruky v klidove poze, dal ji ruka nese pri kazde animaci.
    // level: zbran vodorovne dopredu (puska v poze strelby), jinak lehce sklopena podle typu zbrane.
    void AttachToBone(Hand hand, WeaponDefinition weapon, HeldModel model, bool level = false)
    {
        bool right = hand.side > 0f;
        var bone = visual.GetHand(right);
        if (bone == null) return;

        var grip = visual.GetGrip(right);
        Vector3 gripWorld = grip != null ? grip.position : bone.position;
        hand.grip = GripInWeapon(weapon, model);   // (jen pro zobrazeni v Scene okne)

        Quaternion rotation = visual.ModelRoot.rotation * (level ? Quaternion.identity : BoneAttachRotation(model, hand.side));
        hand.holder.position = gripWorld - rotation * GripInWeapon(weapon, model);
        hand.holder.rotation = rotation;
        hand.holder.SetParent(bone, true);

        hand.idlePosition = hand.holder.localPosition;
        hand.idleRotation = hand.holder.localRotation;
    }

    // Natoceni zbrane v ruce postavy vuci telu (v klidove poze), pak ji nese kost ruky. Pouziva i editor (Ukazka animaci se zbrani).
    public static Quaternion BoneAttachRotation(HeldModel model, float side)
    {
        return Quaternion.Euler(model == HeldModel.Scythe ? new Vector3(20f, 0f, -10f * side)
            : model == HeldModel.Axe ? new Vector3(35f, 0f, -10f * side) : new Vector3(10f, 0f, 0f));
    }

    public static void BuildModel(Transform holder, WeaponDefinition weapon, HeldModel model, float side)
    {
        if (weapon.heldPrefab != null)
        {
            var instance = Instantiate(weapon.heldPrefab, holder);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            if (weapon.heldScale > 0.01f && !Mathf.Approximately(weapon.heldScale, 1f))
                instance.transform.localScale *= weapon.heldScale;
            foreach (var collider in instance.GetComponentsInChildren<Collider>())
                DestroyImmediate(collider);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return;
        }

        var wood = new Color(0.45f, 0.28f, 0.12f);
        var steel = new Color(0.78f, 0.80f, 0.84f);
        var dark = new Color(0.17f, 0.18f, 0.21f);

        if (model == HeldModel.Bow)
        {
            // Luk z rovnych dilu: madlo, dve ramena a tetiva.
            Part(holder, new Vector3(0f, 0f, 0.10f), Vector3.zero, new Vector3(0.04f, 0.22f, 0.05f), dark);
            Part(holder, new Vector3(0f, 0.30f, 0.04f), new Vector3(-22f, 0f, 0f), new Vector3(0.03f, 0.44f, 0.035f), wood);
            Part(holder, new Vector3(0f, -0.30f, 0.04f), new Vector3(22f, 0f, 0f), new Vector3(0.03f, 0.44f, 0.035f), wood);
            Part(holder, new Vector3(0f, 0f, -0.045f), Vector3.zero, new Vector3(0.008f, 1.0f, 0.008f), steel);
            // Sip pripraveny na tetive.
            Part(holder, new Vector3(0f, 0f, 0.28f), Vector3.zero, new Vector3(0.015f, 0.015f, 0.68f), weapon.projectileColor);
            holder.GetChild(holder.childCount - 1).name = "Arrow";
        }
        else if (model == HeldModel.Scythe)
        {
            BuildScythe(holder);
        }
        else if (model == HeldModel.Axe)
        {
            Part(holder, new Vector3(0f, 0.25f, 0f), Vector3.zero, new Vector3(0.045f, 0.56f, 0.045f), wood);
            Part(holder, new Vector3(0f, 0.47f, 0.10f), Vector3.zero, new Vector3(0.03f, 0.20f, 0.20f), steel);
            Part(holder, new Vector3(0f, 0.47f, -0.035f), Vector3.zero, new Vector3(0.055f, 0.10f, 0.07f), dark);
            Part(holder, new Vector3(0f, 0.02f, 0f), Vector3.zero, new Vector3(0.06f, 0.05f, 0.06f), dark);
        }
        else if (weapon.IsProjectile)
        {
            Part(holder, new Vector3(0f, 0f, 0.3f), Vector3.zero, new Vector3(0.13f, 0.13f, 0.75f), new Color(0.22f, 0.27f, 0.20f));
            Part(holder, new Vector3(0f, 0f, 0.68f), Vector3.zero, new Vector3(0.17f, 0.17f, 0.07f), weapon.projectileColor);
            Part(holder, new Vector3(0f, -0.12f, 0.12f), Vector3.zero, new Vector3(0.05f, 0.14f, 0.06f), dark);
        }
        else
        {
            Part(holder, new Vector3(0f, 0f, 0.2f), Vector3.zero, new Vector3(0.07f, 0.10f, 0.40f), dark);
            Part(holder, new Vector3(0f, 0.02f, 0.46f), Vector3.zero, new Vector3(0.035f, 0.035f, 0.22f), steel);
            Part(holder, new Vector3(0f, -0.09f, 0.05f), new Vector3(15f, 0f, 0f), new Vector3(0.05f, 0.12f, 0.06f), dark);
            Part(holder, new Vector3(0f, 0.055f, 0.2f), Vector3.zero, new Vector3(0.075f, 0.02f, 0.2f), weapon.projectileColor);
        }
    }

    // Zbrane, kterymi se mava (sekyra, kosa): stejne drzeni a zamah.
    static bool Swung(HeldModel model) => model == HeldModel.Axe || model == HeldModel.Scythe;

    // Kosa (Max) v prostoru drzaku: nasada svisle (+Y), ruka drzi bod (0; 0,12; 0), nasada od -0,55 do 0,95 m.
    // Nahore cepel: vybiha dopredu (+Z) a zahyba se dolu (matna tmava ocel).
    static Mesh scytheBlade;

    static void BuildScythe(Transform holder)
    {
        var wood = new Color(0.30f, 0.17f, 0.10f);
        var wrap = new Color(0.10f, 0.08f, 0.10f);
        var brass = new Color(0.62f, 0.48f, 0.22f);
        const float bottom = -0.55f, top = 0.95f;

        // nasada, omotavky, kovani
        Part(holder, new Vector3(0f, (bottom + top) * 0.5f, 0f), Vector3.zero, new Vector3(0.04f, top - bottom, 0.04f), wood);
        Part(holder, new Vector3(0f, 0.12f, 0f), Vector3.zero, new Vector3(0.052f, 0.20f, 0.052f), wrap);
        Part(holder, new Vector3(0f, -0.40f, 0f), Vector3.zero, new Vector3(0.052f, 0.18f, 0.052f), wrap);
        Part(holder, new Vector3(0f, top - 0.03f, 0f), Vector3.zero, new Vector3(0.06f, 0.08f, 0.06f), brass);
        Part(holder, new Vector3(0f, bottom + 0.02f, 0f), Vector3.zero, new Vector3(0.055f, 0.04f, 0.055f), brass);

        if (scytheBlade == null) BuildScytheMeshes();
        var blade = new GameObject("Cepel", typeof(MeshFilter), typeof(MeshRenderer));
        blade.transform.SetParent(holder, false);
        blade.GetComponent<MeshFilter>().sharedMesh = scytheBlade;
        var bladeRenderer = blade.GetComponent<MeshRenderer>();
        bladeRenderer.sharedMaterial = Fx.NewLit(new Color(0.20f, 0.20f, 0.24f));
        bladeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // (ostri bez samostatneho dilu: tenky pas na povrchu cepele blikal - z-fighting)

        // cela kosa matna: bez odlesku a odrazu okoli (jinak se ostri a kovani leskly)
        foreach (var r in holder.GetComponentsInChildren<Renderer>())
            foreach (var m in r.sharedMaterials)
                Matte(m);
    }

    static void Matte(Material m)
    {
        if (m == null) return;
        m.SetFloat("_Smoothness", 0f);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_SpecularHighlights", 0f);
        m.SetFloat("_EnvironmentReflections", 0f);
        m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        m.DisableKeyword("_EMISSION");
    }

    // Cepel: krivka hrbetu z vrsku nasady dopredu a dolu; sirka se zuzuje k hrotu, ostri pod hrbetem.
    static void BuildScytheMeshes()
    {
        const int n = 20;
        const float baseY = 0.93f, reach = 0.78f, drop = 0.32f, width = 0.15f;
        var spine = new Vector3[n + 1];
        var edgeLine = new Vector3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;
            float z = reach * Mathf.Sin(t * Mathf.PI * 0.5f);
            float y = baseY - drop * t * t;
            spine[i] = new Vector3(0f, y, z);
            float w = width * Mathf.Pow(1f - t, 0.7f) + 0.006f;
            // ostri: pod hrbetem a trochu dovnitr ohybu (k nasade)
            Vector3 tangent = i < n ? Vector3.zero : Vector3.zero;
            edgeLine[i] = spine[i] + new Vector3(0f, -w, -w * 0.35f * t);
        }

        scytheBlade = Wedge("KosaCepel", spine, edgeLine, 0.022f, 0.004f);

    }

    // Klinovity pas mezi dvema krivkami: horni hrana tlusta 'thick', spodni (ostri) tenka. Uzavrene teleso,
    // kazda plocha jednou, s vlastnimi vrcholy a otocena ven (sdilene vrcholy s opacne otocenymi plochami davaly
    // nulove normaly -> chyba v osvetleni a prepalena bila zare).
    static Mesh Wedge(string name, Vector3[] upper, Vector3[] lower, float thick, float edgeThick = 0.001f)
    {
        int n = upper.Length;
        var ring = new Vector3[n][];
        for (int i = 0; i < n; i++)
            ring[i] = new[]
            {
                upper[i] + Vector3.right * thick * 0.5f, upper[i] - Vector3.right * thick * 0.5f,
                lower[i] - Vector3.right * edgeThick, lower[i] + Vector3.right * edgeThick,
            };

        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            Vector3 center = (a + b + c + d) * 0.25f;
            if (Vector3.Dot(normal, center - inside) < 0f) { var t = b; b = d; d = t; }   // otocit ven
            int k = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            triangles.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
        }

        for (int i = 0; i < n - 1; i++)
        {
            var r0 = ring[i];
            var r1 = ring[i + 1];
            Vector3 inside = (r0[0] + r0[1] + r0[2] + r0[3] + r1[0] + r1[1] + r1[2] + r1[3]) / 8f;
            for (int e = 0; e < 4; e++)
            {
                int f = (e + 1) % 4;
                Quad(r0[e], r1[e], r1[f], r0[f], inside);
            }
        }
        // zaslepeni koncu
        Vector3 startInside = (ring[0][0] + ring[0][1] + ring[0][2] + ring[0][3]) * 0.25f + (upper[1] - upper[0]).normalized * 0.01f;
        Quad(ring[0][0], ring[0][1], ring[0][2], ring[0][3], startInside);
        Vector3 endInside = (ring[n - 1][0] + ring[n - 1][1] + ring[n - 1][2] + ring[n - 1][3]) * 0.25f - (upper[n - 1] - upper[n - 2]).normalized * 0.01f;
        Quad(ring[n - 1][0], ring[n - 1][1], ring[n - 1][2], ring[n - 1][3], endInside);

        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void Part(Transform parent, Vector3 position, Vector3 euler, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;

        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = Fx.NewLit(color);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
