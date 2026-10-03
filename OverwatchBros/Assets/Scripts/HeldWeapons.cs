using UnityEngine;

// Zbrane drzene v rukou (1 nebo 2 podle WeaponDefinition.dualWield). Majitel je vidi u kamery (first person),
// ostatni na tele hrace. Model se sklada z kostek (placeholder), nebo se pouzije WeaponDefinition.heldPrefab.
public class HeldWeapons : MonoBehaviour
{
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

        // Zbrane v rukou postavy: poza v klidu a v bloku (mistni vuci kosti ruky).
        public Vector3 idlePosition, blockPosition;
        public Quaternion idleRotation, blockRotation;
        public bool hasBlockPose;
    }

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
    HeldModel builtModel;
    int nextHand;
    float swingDuration = 0.3f;
    FirstPersonArms fpArms;
    BlockAbility block;
    float blockBlend;
    float runBlend;
    float runPhase;
    Vector3 lastPosition;
    float reloadElapsed = 99f;
    float reloadDuration = 1f;

    void Awake()
    {
        shooting = GetComponent<WeaponShooting>();
        fpc = GetComponent<FirstPersonController>();
    }

    public void Swing()
    {
        if (hands.Length == 0)
        {
            PlayBodyAttack(shooting.weapon);
            return;
        }

        var weapon = shooting.weapon;
        swingDuration = weapon != null && weapon.IsMelee ? Mathf.Clamp(0.9f / Mathf.Max(0.5f, weapon.fireRate), 0.18f, 0.4f) : 0.14f;

        var hand = hands[nextHand % hands.Length];
        hand.swingTime = 0f;
        nextHand++;

        PlayBodyAttack(weapon);
    }

    // Animace prebijeni: zbran u kamery (majitel) a horni polovina tela postavy (ostatni).
    public void PlayReload(float duration)
    {
        reloadDuration = Mathf.Max(0.05f, duration);
        reloadElapsed = 0f;

        if (visual != null && visual.HasModel)
            visual.PlayReload(reloadDuration);
    }

    void PlayBodyAttack(WeaponDefinition weapon)
    {
        if (visual != null && visual.HasModel)
            visual.PlayAttack(weapon != null && weapon.IsMelee, weapon != null ? Mathf.Clamp(1.25f / Mathf.Max(0.5f, weapon.fireRate), 0.25f, 0.7f) : 0.4f);
    }

    void Update()
    {
        if (shooting == null || !shooting.IsSpawned) return;

        if (visual == null)
            visual = GetComponent<CharacterVisual>();

        bool owner = shooting.IsOwner;
        bool visualChanged = visual != null && visual.Version != builtVersion;
        if (!built || builtWeapon != shooting.weapon || builtAsOwner != owner || visualChanged)
            Rebuild(owner);

        bool dead = fpc != null && fpc.IsDead;
        bool thirdPerson = fpc != null && fpc.ThirdPerson;

        if (reloadElapsed < reloadDuration)
            reloadElapsed += Time.deltaTime;

        // Zbrane u kamery (majitel v 1. osobe) a zbrane v rukou postavy (ostatni, majitel ve 3. osobe).
        bool cameraHandsVisible = !dead && !(owner && thirdPerson);
        if (root != null && root.activeSelf != cameraHandsVisible)
            root.SetActive(cameraHandsVisible);

        bool bodyVisible = !dead && (!owner || thirdPerson);
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
        blockBlend = Mathf.MoveTowards(blockBlend, block != null && block.IsBlocking ? 1f : 0f, Time.deltaTime * 8f);

        if (root == null || !cameraHandsVisible) return;

        // Pri behu se zbrane houpou do rytmu kroku (sila podle rychlosti).
        // Rychlost z posunu (CharacterController.velocity ma jen posledni, svisly pohyb).
        float speed = 0f;
        Vector3 position3 = transform.position;
        if (owner && fpc != null && fpc.Controller != null && Time.deltaTime > 0f)
        {
            Vector3 delta = position3 - lastPosition;
            delta.y = 0f;
            speed = delta.magnitude > 3f ? 0f : delta.magnitude / Time.deltaTime;
            if (!fpc.Controller.isGrounded) speed *= 0.3f;
        }

        lastPosition = position3;

        runBlend = Mathf.Lerp(runBlend, Mathf.Clamp(speed / 5f, 0f, 1.6f), 1f - Mathf.Exp(-8f * Time.deltaTime));
        runPhase += speed * 1.5f * Time.deltaTime;
        float sway = builtModel == HeldModel.Axe ? 1.5f : 1f;

        foreach (var hand in hands)
        {
            float bob = Mathf.Sin(Time.time * 1.6f + hand.side) * 0.004f;
            Vector3 position = hand.restPosition + new Vector3(0f, bob, 0f);
            Vector3 euler = hand.restEuler;

            float p = runPhase + (hand.side > 0f ? 0f : Mathf.PI);
            float k = runBlend * sway;
            position += new Vector3(Mathf.Sin(p * 0.5f) * 0.03f, -Mathf.Abs(Mathf.Sin(p)) * 0.035f, Mathf.Sin(p * 2f) * 0.012f) * k;
            euler += new Vector3(Mathf.Sin(p * 2f) * 4f, Mathf.Sin(p * 0.5f) * 5f, Mathf.Sin(p * 0.5f) * -8f) * k;

            if (hand.swingTime < swingDuration)
            {
                hand.swingTime += Time.deltaTime;
                float t = Mathf.Clamp01(hand.swingTime / swingDuration);
                Animate(t, ref position, ref euler);
            }

            if (reloadElapsed < reloadDuration)
                AnimateReload(reloadElapsed / reloadDuration, hand.side, ref position, ref euler);

            // Luk se pri natahovani pritahne k telu a lehce zvedne.
            if (builtModel == HeldModel.Bow)
            {
                float charge = shooting.ChargeFraction;
                position += new Vector3(-0.03f, 0.03f, -0.10f) * charge;
                euler.z += 6f * charge;
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
                fpArms.Solve(hand.side > 0f, hand.holder.TransformPoint(hand.grip), hand.holder, root.transform);

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

    void Animate(float t, ref Vector3 position, ref Vector3 euler)
    {
        if (builtModel == HeldModel.Axe)
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
        built = true;
        builtVersion = visual != null ? visual.Version : 0;
        builtWeapon = shooting.weapon;
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
            Transform parent = owner && fpc != null && fpc.playerCamera != null ? fpc.playerCamera.transform : transform;
            root = new GameObject("HeldWeapons");
            root.transform.SetParent(parent, false);

            hands = new Hand[count];
            for (int i = 0; i < count; i++)
            {
                // Jedna zbran je vpravo, dve jsou po obou stranach.
                float side = count == 1 ? 1f : (i == 0 ? 1f : -1f);

                var hand = new Hand { side = side };
                var holder = new GameObject(i == 0 ? "RightHand" : "LeftHand").transform;
                holder.SetParent(root.transform, false);
                hand.holder = holder;

                hand.restPosition = owner
                    ? (model == HeldModel.Axe ? new Vector3(0.36f * side, -0.40f, 0.55f) : new Vector3(0.30f * side, -0.28f, 0.50f))
                    : new Vector3(0.62f * side, 0.15f, 0.30f);
                hand.restEuler = model == HeldModel.Axe ? new Vector3(15f, 0f, -9f * side) : Vector3.zero;

                BuildModel(holder, weapon, model, side);
                holder.localPosition = hand.restPosition;
                holder.localRotation = Quaternion.Euler(hand.restEuler);
                hands[i] = hand;

                hand.grip = GripInWeapon(weapon, model);
            }

            // Ruce z prvni osoby: skutecne paze modelu postavy, jinak jednoducha pest s rukavem.
            if (owner)
            {
                var playerHero = GetComponent<PlayerHero>();
                var prefab = playerHero != null && playerHero.Hero != null ? playerHero.Hero.characterPrefab : null;
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
                AttachToBone(hand, weapon, model);
                bodyHands[i] = hand;
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
        var playerHero = GetComponent<PlayerHero>();
        var hero = playerHero != null ? playerHero.Hero : null;
        Color sleeve = hero != null ? hero.sleeveColor : new Color(0.25f, 0.27f, 0.32f);
        Color skin = hero != null ? hero.skinColor : new Color(0.87f, 0.67f, 0.55f);

        var fist = Limb(PrimitiveType.Sphere, hand.holder, skin);
        fist.localPosition = hand.grip;
        fist.localScale = new Vector3(0.105f, 0.10f, 0.125f);

        var cuff = Limb(PrimitiveType.Cylinder, root.transform, sleeve);
        hand.forearm = cuff;
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

        return model == HeldModel.Axe ? new Vector3(0f, 0.12f, 0f)
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

        Vector3 euler = model == HeldModel.Axe ? new Vector3(35f, 0f, -10f * hand.side) : new Vector3(10f, 0f, 0f);
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
