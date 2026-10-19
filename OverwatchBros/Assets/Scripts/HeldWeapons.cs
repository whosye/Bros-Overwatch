using UnityEngine;

// Zbrane drzene v rukou (1 nebo 2 podle WeaponDefinition.dualWield). Majitel je vidi u kamery (first person),
// ostatni na tele hrace. Model se sklada z kostek (placeholder), nebo se pouzije WeaponDefinition.heldPrefab.
public class HeldWeapons : MonoBehaviour
{
    PlayerHero heroComponent;
    PlayerHero CurrentHeroComponent => heroComponent != null ? heroComponent : (heroComponent = GetComponent<PlayerHero>());
    class Hand
    {
        public Transform holder;
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
            foreach (var renderer in holder.GetComponentsInChildren<Renderer>(true))
                if (!(renderer is ParticleSystemRenderer))
                    foreach (var material in renderer.materials)
                    {
                        material.EnableKeyword("_EMISSION");
                        stormMaterials.Add(material);
                    }
        }
        float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 14f);
        // (jen luk sam - zare a svetlo kolem by hraci zakryvaly vyhled)
        Color emission = on ? StormColor * (1.1f * pulse) : Color.black;
        foreach (var material in stormMaterials)
            if (material != null) material.SetColor("_EmissionColor", emission);
        stormShown = on;
    }
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

    // Puska obema rukama: po vystrelu chvili v poze strelby, pak zpet do klidu (plynule).
    float lastShotTime = -100f;
    float aimBlend;
    const float AimHoldSeconds = 1.2f;

    // ---------------- uspavaci pistole (David, Shift): na chvili v leve ruce u kamery ----------------

    Transform dartPistol;
    float dartShownAt = -100f;
    const float DartShowSeconds = 0.75f;
    static readonly Vector3 DartGrip = new Vector3(0f, -0.06f, 0.02f);

    public void ShowDartPistol()
    {
        if (root == null) return;
        if (dartPistol == null) dartPistol = BuildDartPistol(root.transform);
        dartShownAt = Time.time;
    }

    // Uspavaci pistole: tmave telo, dlouha hlaven, CO2 bombicka pod hlavni, v hlavni sipka s oranzovou spickou.
    static Transform BuildDartPistol(Transform parent)
    {
        var holder = new GameObject("DartPistol").transform;
        holder.SetParent(parent, false);
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
        // vytahnout zleva, vystrel (zpetny raz), schovat
        float raise = t < 0.2f ? Mathf.SmoothStep(0f, 1f, t / 0.2f) : t > 0.75f ? Mathf.SmoothStep(1f, 0f, (t - 0.75f) / 0.25f) : 1f;
        float kick = t > 0.2f && t < 0.4f ? Mathf.Sin((t - 0.2f) / 0.2f * Mathf.PI) : 0f;
        dartPistol.localPosition = Vector3.Lerp(new Vector3(-0.32f, -0.5f, 0.35f), new Vector3(-0.2f, -0.2f, 0.5f), raise) + new Vector3(0f, 0.02f, -0.05f) * kick;
        dartPistol.localRotation = Quaternion.Euler(-12f * kick, 4f, 0f);
        if (fpArms != null && !leftUsed)
            fpArms.Solve(false, dartPistol.TransformPoint(DartGrip), dartPistol, root.transform, false, true);
    }

    // Po animaci: leva ruka postavy podpira zbran zespodu (pro ostatni hrace, POTG, killcam).
    void LateUpdate()
    {
        UpdateDartPistol();
        if (bodySupportArm == null || visual == null || visual.ModelRoot == null) return;
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
            FirstPersonArms.SolveArm(bodySupportArm, hand.holder.TransformPoint(hand.foregrip), hand.holder,
                body.TransformDirection(new Vector3(-0.15f, -1f, -0.35f)), true);
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

    public void Swing()
    {
        lastShotTime = Time.time;   // (puska obema rukama: poza strelby)
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
        // kosa: kazdy sek z druhe strany (zprava doleva, zleva doprava, ...)
        if (builtModel == HeldModel.Scythe) slashDir = -slashDir;

        PlayBodyAttack(weapon);
    }

    // Animace prebijeni: zbran u kamery (majitel) a horni polovina tela postavy (ostatni).
    public void PlayReload(float duration)
    {
        if (!ghost)
            ReplayLog.Reload(this, duration);
        reloadDuration = Mathf.Max(0.05f, duration);
        reloadElapsed = 0f;

        if (visual != null && visual.HasModel)
            visual.PlayReload(reloadDuration);
    }

    public void StopReload()
    {
        if (!ghost)
            ReplayLog.StopReload(this);
        reloadElapsed = reloadDuration;

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

    public void PlayThrow()
    {
        throwElapsed = 0f;
        if (visual != null && visual.HasModel)
            visual.PlayAttack(true, ThrowDuration);
    }

    void PlayBodyAttack(WeaponDefinition weapon)
    {
        // zbran obema rukama (strelba od pasu): postava pri vystrelu nezveda ruce, zbran zustava u boku
        if (weapon != null && weapon.twoHanded && !weapon.IsMelee) return;
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
        bool thirdPerson = ghost ? GhostThirdPerson : fpc != null && fpc.ThirdPerson;

        if (reloadElapsed < reloadDuration)
            reloadElapsed += Time.deltaTime;
        if (throwElapsed < ThrowDuration)
            throwElapsed += Time.deltaTime;
        if (tauntElapsed < TauntDuration)
            tauntElapsed += Time.deltaTime;

        if (builtModel == HeldModel.Bow)
        {
            UpdateArrows();
            UpdateStorm();
        }

        // Zbrane u kamery (majitel v 1. osobe) a zbrane v rukou postavy (ostatni, majitel ve 3. osobe).
        // (zivi hraci jsou behem prehravani POTG skryti, hraji misto nich duchove)
        bool hiddenByReplay = !ghost && (ReplayPlayer.Active || (CurrentHeroComponent != null && CurrentHeroComponent.IsSpawned && CurrentHeroComponent.IsUntargetable));
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

        bool bodyVisible = !dead && (!owner || thirdPerson) && !hiddenByReplay;
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

            if (reloadElapsed < reloadDuration)
                AnimateReload(reloadElapsed / reloadDuration, hand.side, ref position, ref euler);

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

            // Blok: sekyry se zkrizi pred hracem.
            if (blockBlend > 0.001f)
            {
                position = Vector3.Lerp(position, new Vector3(0.10f * hand.side, -0.22f, 0.62f), blockBlend);
                rotation = Quaternion.Slerp(rotation, Quaternion.Euler(-6f, 0f, 38f * hand.side), blockBlend);
            }

            hand.holder.localPosition = position;
            hand.holder.localRotation = rotation;

            if (fpArms != null)
            {
                fpArms.Solve(hand.side > 0f, hand.holder.TransformPoint(hand.grip), hand.holder, root.transform, false, builtModel == HeldModel.Bow);   // (luk v natazene ruce)
                if (hand.twoHanded)
                    fpArms.Solve(false, hand.holder.TransformPoint(hand.foregrip), hand.holder, root.transform, true);

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

    static void SnapArrowToRest(Transform holder, Vector3 grip)
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

    int slashDir = -1;

    // Maxuv Dlouhy rez (jako Kaynovo W): napraah dozadu nahoru po dobu 'windup', mohutny sek dopredu a dolu, navrat.
    float reachStart = -100f, reachWindup = 0.4f;
    const float ReachStrike = 0.15f, ReachRecover = 0.3f;

    public void PlayReach(float windup)
    {
        reachStart = Time.time;
        reachWindup = Mathf.Max(0.05f, windup);
    }

    // Nabijeni (drzeni PTM): kosa se zvedne do napraahu a zustane, dokud se nepusti (ReleaseReach = sek).
    const float ReachRaise = 0.3f;
    public void BeginReachHold()
    {
        reachStart = Time.time;
        reachWindup = 999f;
    }

    public void ReleaseReach()
    {
        reachWindup = Mathf.Max(0.05f, Time.time - reachStart);
    }

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

    // Kosa: vodorovny sek pred sebou. Kosa se polozi na bok (cepel naplocho dopredu) a cela se otoci kolem hrace
    // z jedne strany na druhou; strany se stridaji.
    static readonly Vector3 SlashPivot = new Vector3(0f, -0.3f, 0f);

    static void SlashPose(int dir, float angle, out Vector3 position, out Vector3 euler)
    {
        Vector3 start = new Vector3(0.30f * dir, -0.18f, 0.42f);
        position = SlashPivot + Quaternion.Euler(0f, angle, 0f) * (start - SlashPivot);
        euler = new Vector3(5f, angle, 90f * dir);
    }

    void Animate(float t, ref Vector3 position, ref Vector3 euler)
    {
        if (builtModel == HeldModel.Scythe)
        {
            int dir = slashDir;
            float from = 60f * dir, to = -60f * dir;
            float angle, blend;
            if (t < 0.15f) { blend = Mathf.SmoothStep(0f, 1f, t / 0.15f); angle = from; }
            else if (t < 0.6f) { blend = 1f; angle = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.45f)); }
            else { blend = 1f - Mathf.SmoothStep(0f, 1f, (t - 0.6f) / 0.4f); angle = to; }

            SlashPose(dir, angle, out Vector3 slashPosition, out Vector3 slashEuler);
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
        else
        {
            float kick = Mathf.Sin(t * Mathf.PI);
            position += new Vector3(0f, 0.01f, -0.1f) * kick;
            euler.x -= 9f * kick;
        }
    }

    void AnimateReload(float t, float side, ref Vector3 position, ref Vector3 euler)
    {
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

    void Rebuild(bool owner)
    {
        foreach (var old in bodyHands)
            if (old.holder != null)
                Destroy(old.holder.gameObject);

        if (root != null)
            Destroy(root);

        root = null;
        fpArms = null;
        hands = new Hand[0];
        bodyHands = new Hand[0];
        if (dartPistol != null) { Destroy(dartPistol.gameObject); dartPistol = null; }
        bodySupportArm = null;
        bodyMainArm = null;
        if (visual != null) { visual.GripRight = false; visual.GripLeft = false; }
        stormMaterials = null;
        stormShown = false;
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
                if (weapon.twoHanded && count == 1 && model == HeldModel.Gun)
                {
                    hand.twoHanded = true;
                    hand.foregrip = ForegripInWeapon(holder, hand.grip);
                    if (owner)
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
                float side = count == 1 ? 1f : (i == 0 ? 1f : -1f);
                var hand = new Hand { side = side };
                var holder = new GameObject(i == 0 ? "BodyRightHand" : "BodyLeftHand").transform;
                hand.holder = holder;
                BuildModel(holder, weapon, model, side);
                FindArrow(hand);
                bodyHands[i] = hand;
                // prsty postavy sevrene kolem zbrane (prava, u dvou zbrani i leva)
                if (side > 0f) visual.GripRight = true;
                else visual.GripLeft = true;

                // zbran obema rukama: puska v pohotovosti pred pravym bokem (pripevnena k trupu), obe ruce na ni
                // (prava na rukojeti, leva zespodu na predpazbi - IK po animaci, viz LateUpdate)
                if (weapon.twoHanded && count == 1 && model == HeldModel.Gun && visual.BodyAnimator != null && AttachAtHip(hand))
                {
                    hand.twoHanded = true;
                    hand.grip = GripInWeapon(weapon, model);
                    hand.foregrip = ForegripInWeapon(holder, hand.grip, 0.42f, 0.38f);   // ruce od sebe, leva daleko pod hlavni
                    bodyMainArm = FirstPersonArms.BuildArm(visual.BodyAnimator, true);
                    bodySupportArm = FirstPersonArms.BuildArm(visual.BodyAnimator, false);
                    visual.GripLeft = true;
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
    static Vector3 ForegripInWeapon(Transform holder, Vector3 grip, float fraction = 0.45f, float maxForward = 0.32f)
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

    static Vector3 GripInWeapon(WeaponDefinition weapon, HeldModel model)
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
    void AttachToBone(Hand hand, WeaponDefinition weapon, HeldModel model)
    {
        bool right = hand.side > 0f;
        var bone = visual.GetHand(right);
        if (bone == null) return;

        var grip = visual.GetGrip(right);
        Vector3 gripWorld = grip != null ? grip.position : bone.position;

        Vector3 euler = model == HeldModel.Scythe ? new Vector3(20f, 0f, -10f * hand.side)
            : model == HeldModel.Axe ? new Vector3(35f, 0f, -10f * hand.side) : new Vector3(10f, 0f, 0f);
        Quaternion rotation = visual.ModelRoot.rotation * Quaternion.Euler(euler);
        hand.holder.position = gripWorld - rotation * GripInWeapon(weapon, model);
        hand.holder.rotation = rotation;
        hand.holder.SetParent(bone, true);

        hand.idlePosition = hand.holder.localPosition;
        hand.idleRotation = hand.holder.localRotation;
    }

    static void BuildModel(Transform holder, WeaponDefinition weapon, HeldModel model, float side)
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
