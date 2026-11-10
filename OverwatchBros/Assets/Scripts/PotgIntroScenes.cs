using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Vlastni uvodni scenky POTG pro jednotlive hrdiny (Pova je v PotgIntro.cs). Kazda trva asi 2,5 s akce, pak
// spolecny prudky najezd na oblicej s usklibnutim (PotgIntro.Animate). Nazev = asset hrdiny v Resources/Heroes.
//  Honza   - drzi karburator a fouka z nej prach; za nim vybuchne naloz a Pova (sedi na bedne) odleti. Honza ani nemrkne.
//  Viktor  - stahne si zamerovac na oci, hodi flash do kamery (obraz zbeli); pod nohama mu sviti lecive pole.
//  Mirek   - vystreli z luku mimo zaber; za nim se prozene jeho smrst s oblicejem. Neotoci se, pak pohled do kamery.
//  Anna    - (David) uspavaci sipka do Tomaska na gauci, ten usne a upusti pivo; David sfoukne hlaven.
//  Sniper  - (Kondulic) na vysokem sloupu, hakem se pritahne dolu ke kamere a kopne do ni.
//  Max     - u stolku s lasagnemi zmizi ve stinu, vynori se pred kamerou a otockou kosy rozpuli lasagne.
//  Bard    - (Mosino) u reproduktoru: basovy uder odhodi bedny, svetla se rozblikaji a tanci.
//  Sindel  - vznasi se v kuzelu svetla shora (kazani), dvakrat vystreli a treti posileny vystrel zrudne.
public partial class PotgIntro
{
    string scene = "";
    float sceneWhip = 2.45f;
    float cameraRoll;                 // naklon detailu (Kondulicuv kopanec)
    int sceneEvents;                  // bitova maska jiz probehlych udalosti
    readonly List<GameObject> sceneObjects = new List<GameObject>();   // mimo root (smrst) - uklidit
    readonly List<(Transform t, Vector3 velocity, Vector3 spin)> flying = new List<(Transform, Vector3, Vector3)>();

    // jednotlive scenky
    Transform prop, prop2, visor, grenade, ring, speaker;
    Vector3 visorForward, visorUp;
    // Napiti v detailu (David: bily Monster, Viktor: pivo) levou rukou; casy a tvar napoje nastavi scenka.
    Transform cup, drinkFrame;
    float cupAppear, cupAtMouth, cupDrinkEnd, cupLowered;
    float cupHalf = 0.088f, cupTilt = 100f, cupRadius = 0.036f;
    float cupNose;   // o kolik vys nez pusa (Max: bankovka k nosu)
    FirstPersonArms.Arm drinkArm;
    bool silentCloseup;   // v detailu bez hlasky (pije)
    bool visorAxes;
    Image flash;
    LineRenderer rope;
    float sleepAt = -1f;
    Vector3 heroFrom, heroTo;
    UnityEngine.Light spot;

    bool TomasekNotices => pova || scene == "Honza";

    void ResetScene()
    {
        scene = "";
        sceneWhip = 2.45f;
        cameraRoll = 0f;
        sceneEvents = 0;
        sleepAt = -1f;
        flying.Clear();
        prop = prop2 = visor = grenade = ring = speaker = null;
        visorAxes = false;
        cup = drinkFrame = null;
        cupNose = 0f;
        cheerLeft = cheerRight = null;
        cheerFrame = null;
        tomasekArm = null;
        tomasekGrip = null;
        cutLine = cutGlow = null;
        drinkArm = null;
        silentCloseup = false;
        flash = null;
        rope = null;
        spot = null;
    }

    void EndScene()
    {
        foreach (var (puppet, _) in crowd) puppet.Destroy();
        crowd.Clear();
        foreach (var go in sceneObjects)
            if (go != null) Destroy(go);
        sceneObjects.Clear();
        flying.Clear();
    }

    // Udalost jen jednou: kdyz cas dosahne 'at'.
    bool Once(int id, float t, float at)
    {
        int bit = 1 << id;
        if (t < at || (sceneEvents & bit) != 0) return false;
        sceneEvents |= bit;
        return true;
    }

    float RootY => -feetOffset;
    Vector3 Ground(float x, float z) => Stage + new Vector3(x, 0f, z) + Vector3.up * RootY;
    Vector3 Chest => hero.position - Vector3.up * RootY + Vector3.up * 1.15f;

    static Quaternion Facing(Vector3 from, Vector3 to) => Quaternion.LookRotation(Flat(to - from));
    static readonly Quaternion ToCamera = Quaternion.Euler(0f, 180f, 0f);

    // Zamerovac pres oci: tenky prohnuty pruh (stred + dve bocni krídla zahnuta dozadu kolem obliceje).
    // Lokalne: +z dopredu od obliceje, +x doprava, stred = pred koreny nosu.
    Transform BuildVisor()
    {
        var visorRoot = new GameObject("Zamerovac").transform;
        visorRoot.SetParent(root.transform, false);
        var color = new Color(0.3f, 0.95f, 1f);
        var middle = Glow(visorRoot, PrimitiveType.Cube, Vector3.zero, new Vector3(0.085f, 0.032f, 0.006f), color, 2.2f);
        middle.localPosition = Vector3.zero;
        foreach (float side in new[] { -1f, 1f })
        {
            var wing = Glow(visorRoot, PrimitiveType.Cube, Vector3.zero, new Vector3(0.06f, 0.03f, 0.006f), color, 2.2f);
            wing.localRotation = Quaternion.Euler(0f, side * 38f, 0f);
            wing.localPosition = new Vector3(side * (0.0425f + 0.03f * Mathf.Cos(38f * Mathf.Deg2Rad)), 0f, -0.03f * Mathf.Sin(38f * Mathf.Deg2Rad));
        }
        var band = Prim(PrimitiveType.Cube, visorRoot, Vector3.zero, new Vector3(0.2f, 0.008f, 0.008f), new Color(0.08f, 0.08f, 0.1f)).transform;
        band.localPosition = new Vector3(0f, 0.019f, -0.02f);
        foreach (var r in visorRoot.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return visorRoot;
    }

    // Napoj levou rukou: zvedne ho k puse, zakloni hlavu a napije se (oci zavrene), pak ho spusti a usmeje se.
    void SetupDrink(Transform drink, float appear, float atMouth, float drinkEnd, float lowered, float half, float tilt, float radius)
    {
        cup = drink;
        foreach (var r in cup.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cup.gameObject.SetActive(false);
        drinkFrame = new GameObject("UchopNapoje").transform;
        drinkFrame.SetParent(root.transform, false);
        cupAppear = appear; cupAtMouth = atMouth; cupDrinkEnd = drinkEnd; cupLowered = lowered;
        cupHalf = half; cupTilt = tilt; cupRadius = radius;
        silentCloseup = true;   // v detailu pije - bez hlasky
    }

    void Drink()
    {
        float Appear = cupAppear, AtMouth = cupAtMouth, DrinkEnd = cupDrinkEnd, Lowered = cupLowered;
        if (time < Appear)
        {
            if (cup.gameObject.activeSelf) cup.gameObject.SetActive(false);
            return;
        }
        if (!cup.gameObject.activeSelf)
            cup.gameObject.SetActive(true);
        if (Once(10, time, AtMouth - 0.1f))
            ProceduralSfx.Play(ProceduralSfx.Splash, head.position, 0.12f);   // syceni u pusy
        if (drinkArm == null && visual.BodyAnimator != null)
            drinkArm = FirstPersonArms.BuildArm(visual.BodyAnimator, false);

        float raise = Smooth(Mathf.Clamp01((time - Appear) / (AtMouth - Appear)));
        float drink = Smooth(Mathf.Clamp01((time - AtMouth) / (DrinkEnd - AtMouth)));
        float lower = Smooth(Mathf.Clamp01((time - DrinkEnd) / (Lowered - DrinkEnd)));
        float sip = drink * (1f - lower);

        Vector3 forward = hero.forward, up = hero.up, right = hero.right;
        // hlava se pri piti zakloni
        var back = Quaternion.AngleAxis(-18f * sip, right);
        head.rotation = back * head.rotation;
        Vector3 mouth = head.position + back * (forward * (0.115f + cupNose * 0.3f) + up * (0.005f + cupNose));

        // plechovka: vrskem u pusy, naklani se tim vic, cim dele pije
        float tilt = Mathf.Lerp(Mathf.Lerp(10f, cupTilt * 0.4f, raise), cupTilt, drink);
        tilt = Mathf.Lerp(tilt, 15f, lower);
        Vector3 canUp = Quaternion.AngleAxis(-tilt, right) * up;
        Vector3 atMouth = mouth + forward * (cupRadius * 0.35f) - canUp * cupHalf;
        Vector3 low = head.position - up * 0.6f + forward * 0.3f - right * 0.14f;
        Vector3 after = mouth - up * 0.2f + forward * 0.22f - right * 0.06f;
        Vector3 center = Vector3.Lerp(Vector3.Lerp(low, atMouth, raise), after, lower);
        Vector3 fingers = Vector3.Cross(right, canUp);
        cup.SetPositionAndRotation(center, Quaternion.LookRotation(fingers, canUp));

        // leva ruka drzi plechovku z boku (dlan na plechovce, prsty kolem predku)
        if (drinkArm != null)
        {
            drinkFrame.rotation = Quaternion.LookRotation(fingers, canUp);
            FirstPersonArms.SolveArm(drinkArm, center - right * cupRadius, drinkFrame, -up - right * 0.8f - forward * 0.2f, false);
        }

        // oci zavrene a rty na plechovce, po napiti spokojeny usmev
        heroFace.Set("eyeBlinkLeft", 85f * sip);
        heroFace.Set("eyeBlinkRight", 85f * sip);
        heroFace.Set("mouthFunnel", 55f * sip);
        heroFace.Set("mouthSmileLeft", Mathf.Lerp(65f * (1f - sip), 90f, lower));
        heroFace.Set("mouthSmileRight", Mathf.Lerp(20f * (1f - sip), 70f, lower));
    }

    Transform Glow(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Color color, float emission)
    {
        var go = Prim(type, parent, position, scale, color);
        var material = go.GetComponent<Renderer>().sharedMaterial;
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * emission);
        return go.transform;
    }

    UnityEngine.Light PointLight(Vector3 position, Color color, float intensity, float range)
    {
        var light = new GameObject("SvetloScenky").AddComponent<UnityEngine.Light>();
        light.transform.SetParent(root.transform, false);
        light.transform.position = position;
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
        return light;
    }

    // ---------------- stavba ----------------

    void BuildScene()
    {
        scene = definition.name;
        switch (scene)
        {
            case "Honza":
                hero.SetPositionAndRotation(Ground(0.55f, 1.3f), Quaternion.Euler(0f, 200f, 0f));
                launchVelocity = new Vector3(6.5f, 4.6f, 3.2f);
                BuildTomasek(new Vector3(-1.7f, 0f, 4.0f), Quaternion.Euler(0f, 160f, 0f), false, "Ayran");   // na bedne sedi Pova
                // naloz u bedny: tri cervene patrony s budikem
                for (int i = 0; i < 3; i++)
                    Prim(PrimitiveType.Cylinder, root.transform, Stage + new Vector3(-1.25f + i * 0.07f, 0.13f, 3.55f), new Vector3(0.06f, 0.13f, 0.06f), new Color(0.75f, 0.1f, 0.08f));
                Glow(root.transform, PrimitiveType.Cube, Stage + new Vector3(-1.18f, 0.2f, 3.5f), new Vector3(0.1f, 0.06f, 0.03f), new Color(1f, 0.15f, 0.1f), 3f);
                prop = BuildCarburetor();
                silentCloseup = true;   // hlaska zazni uz pri radosti po odpalu
                sceneWhip = 2.55f;
                break;

            case "Viktor":
                // preskoci zprava doleva pred kamerou; po oslepeni stoji pred kamerou a pije pivo
                hero.SetPositionAndRotation(ViktorLeapFrom, Facing(ViktorLeapFrom, ViktorCamera));
                BuildFlashOverlay();
                SetupDrink(BuildBeer(), ViktorStandAt + 0.05f, ViktorStandAt + 0.45f, 3.05f, 3.4f, 0.075f, 75f, 0.045f);
                sceneWhip = 2.6f;
                break;

            case "Mirek":
                // nejdriv mimo zaber (priskoci az po smrsti)
                hero.SetPositionAndRotation(Ground(-12f, 4f), Facing(Ground(0f, 3f), MirekCamera));
                arrows.Clear();
                sceneWhip = 2.75f;
                break;

            case "Anna":
                hero.SetPositionAndRotation(Ground(0.9f, 1.1f), Facing(Stage + new Vector3(0.9f, 0f, 1.1f), Stage + new Vector3(-1.3f, 0f, 2.7f)));
                BuildTomasek(new Vector3(-1.3f, 0f, 2.7f), Quaternion.Euler(0f, 130f, 0f), true);
                var monster = new GameObject("BilyMonster").transform;
                monster.SetParent(root.transform, false);
                MonsterCan.Build(monster);
                monster.localScale = Vector3.one;   // skutecna velikost (v letu je zvetsena)
                SetupDrink(monster, 3.0f, 3.45f, 4.45f, 4.8f, 0.088f, 100f, 0.036f);
                sceneWhip = 2.5f;
                break;

            case "Sniper":
                // vysoky sloup, nahore Kondulic
                Prim(PrimitiveType.Cube, root.transform, Stage + new Vector3(-2.3f, 1.5f, 4.6f), new Vector3(1f, 3f, 1f), new Color(0.12f, 0.12f, 0.14f));
                heroFrom = Ground(-2.3f, 4.6f) + Vector3.up * 3f;
                heroTo = Ground(0.15f, 1.25f);
                hero.SetPositionAndRotation(heroFrom, Facing(heroFrom, Stage + new Vector3(0.6f, 0f, -2f)));
                PointLight(Stage + new Vector3(-2.3f, 4.5f, 3.6f), new Color(1f, 0.15f, 0.1f), 6f, 5f);   // infra
                var ropeObject = new GameObject("Lano");
                ropeObject.transform.SetParent(root.transform, false);
                rope = ropeObject.AddComponent<LineRenderer>();
                rope.material = Fx.NewLit(new Color(0.15f, 0.15f, 0.15f));
                rope.widthMultiplier = 0.025f;
                rope.positionCount = 2;
                rope.enabled = false;
                sceneWhip = 2.3f;
                break;

            case "Max":
                // Max sedi u stolu, pred nim podtacek s lajnou bileho prasku (stul mezi nim a kamerou)
                hero.SetPositionAndRotation(MaxSeat, Facing(MaxSeat, MaxCamera));
                Prim(PrimitiveType.Cube, root.transform, Stage + new Vector3(-0.35f, 0.37f, 1.75f), new Vector3(1.1f, 0.74f, 0.7f), new Color(0.35f, 0.22f, 0.13f));
                prop = Prim(PrimitiveType.Cylinder, root.transform, Stage + new Vector3(-0.45f, 0.755f, 1.8f), new Vector3(0.22f, 0.006f, 0.22f), new Color(0.12f, 0.12f, 0.14f)).transform;   // podtacek (tmave sklo)
                prop2 = Prim(PrimitiveType.Cube, root.transform, Stage + new Vector3(-0.45f, 0.764f, 1.8f), new Vector3(0.15f, 0.008f, 0.013f), Color.white).transform;   // lajna
                prop2.rotation = Quaternion.Euler(0f, 20f, 0f);
                var note = Prim(PrimitiveType.Cylinder, root.transform, Stage + new Vector3(-0.3f, 0.76f, 1.72f), new Vector3(0.012f, 0.045f, 0.012f), new Color(0.55f, 0.7f, 0.5f)).transform;
                note.rotation = Quaternion.Euler(0f, 35f, 90f);   // srolovana bankovka na stole
                // vedle u stolu sedi Honza a natahuje se, ze si lajnu vysnupe
                BuildTomasek(new Vector3(0.45f, 0f, 1.75f), Quaternion.Euler(0f, -90f, 0f), false, "Honza");
                if (beer != null) beer.gameObject.SetActive(false);
                SetupDrink(BuildBanknote(), 2.2f, 2.6f, 3.15f, 3.45f, 0.045f, 40f, 0.008f);
                cupNose = 0.035f;   // ke nosu, ne k puse
                maxChair = false;
                BuildCutOverlay();
                sceneWhip = 2.55f;
                break;

            case "Bard":
                // breakdance uprostred krouzku ostatnich hrdinu; reprak opodal
                hero.SetPositionAndRotation(MosinoCenter + Vector3.up * RootY, Facing(MosinoCenter, MosinoCamStart));
                speaker = Prim(PrimitiveType.Cube, root.transform, MosinoCenter + new Vector3(3.1f, 1.1f, 1.2f), new Vector3(1.1f, 2.2f, 0.8f), new Color(0.06f, 0.06f, 0.07f)).transform;
                BuildCrowd();
                BuildFlashOverlay();
                sceneWhip = 99f;   // bez detailu - konci pohledem rozbite kamery ze zeme
                break;

            case "Sindel":
                // stul se slivovici: Sindel sedi vlevo, naproti nemu Viktor
                BuildSlivovitzTable();
                hero.SetPositionAndRotation(SindelSeat, Quaternion.Euler(0f, 90f, 0f));
                BuildTomasek(new Vector3(0.85f, 0f, 2.2f), Quaternion.Euler(0f, -90f, 0f), false, "Viktor");
                BuildFlashOverlay();
                SetupDrink(BuildShotGlass(), 1.85f, 2.3f, 2.62f, 2.95f, 0.03f, 115f, 0.024f);
                sindelChair = false;
                sceneWhip = 2.6f;
                break;

            default:
                scene = "";
                break;
        }
    }

    // Honzuv karburator v prave ruce.
    Transform BuildCarburetor()
    {
        var hand = visual.GetHand(true);
        var carb = new GameObject("Karburator").transform;
        carb.SetParent(hand != null ? hand : hero, false);
        carb.localPosition = new Vector3(0f, -0.06f, 0.08f);
        var metal = new Color(0.72f, 0.74f, 0.78f);
        Prim(PrimitiveType.Cube, carb, carb.position, new Vector3(0.13f, 0.1f, 0.1f), metal).transform.SetParent(carb, true);
        Prim(PrimitiveType.Cylinder, carb, carb.position + Vector3.up * 0.085f, new Vector3(0.15f, 0.025f, 0.15f), new Color(0.15f, 0.15f, 0.17f)).transform.SetParent(carb, true);
        return carb;
    }

    // Gauc pod Tomaskem (Davidova scenka) misto bedny.
    void BuildCouch(Vector3 hipsAt, float top)
    {
        var fabric = new Color(0.72f, 0.65f, 0.55f);
        Vector3 back = -(seatRotation * Vector3.forward);
        Vector3 side = seatRotation * Vector3.right;
        Vector3 center = new Vector3(hipsAt.x, Stage.y + top * 0.5f, hipsAt.z) + back * 0.12f;
        var seatBox = Prim(PrimitiveType.Cube, root.transform, center, new Vector3(1.9f, top, 0.8f), fabric);
        seatBox.transform.rotation = seatRotation;
        var backrest = Prim(PrimitiveType.Cube, root.transform, center + back * 0.45f + Vector3.up * (top * 0.5f + 0.3f), new Vector3(1.9f, 0.6f, 0.2f), fabric);
        backrest.transform.rotation = seatRotation;
        foreach (float s in new[] { -1f, 1f })
        {
            var arm = Prim(PrimitiveType.Cube, root.transform, center + side * s * 1.02f + Vector3.up * 0.12f, new Vector3(0.2f, top + 0.25f, 0.8f), fabric);
            arm.transform.rotation = seatRotation;
        }
    }

    void BuildFlashOverlay()
    {
        var canvasObject = new GameObject("Zablesk");
        canvasObject.transform.SetParent(root.transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var image = new GameObject("Bila", typeof(RectTransform), typeof(Image));
        image.transform.SetParent(canvasObject.transform, false);
        var rect = (RectTransform)image.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        flash = image.GetComponent<Image>();
        flash.color = new Color(1f, 1f, 1f, 0f);
        flash.raycastTarget = false;
    }

    // ---------------- choreografie ----------------

    // Vraci false, kdyz hrdina vlastni scenku nema (pak obecna).
    bool AnimateScene(float t, out Vector3 widePos, out Quaternion wideRot, out float wideFov)
    {
        widePos = Vector3.zero;
        wideRot = Quaternion.identity;
        wideFov = 50f;
        if (string.IsNullOrEmpty(scene) || hero == null) return false;

        UpdateFlying();
        switch (scene)
        {
            case "Honza": Honza(t, out widePos, out wideRot, out wideFov); break;
            case "Viktor": Viktor(t, out widePos, out wideRot, out wideFov); break;
            case "Mirek": Mirek(t, out widePos, out wideRot, out wideFov); break;
            case "Anna": David(t, out widePos, out wideRot, out wideFov); break;
            case "Sniper": Kondulic(t, out widePos, out wideRot, out wideFov); break;
            case "Max": Max(t, out widePos, out wideRot, out wideFov); break;
            case "Bard": Mosino(t, out widePos, out wideRot, out wideFov); break;
            case "Sindel": Sindel(t, out widePos, out wideRot, out wideFov); break;
            default: return false;
        }
        return true;
    }

    void Honza(float t, out Vector3 pos, out Quaternion rot, out float fov)
    {
        if (Once(0, t, 0.45f) && prop != null)
            Fx.Sparks(prop.position + Vector3.up * 0.1f, new Color(0.6f, 0.58f, 0.55f));   // fouknuti prachu
        if (Once(1, t, 0.75f) && prop != null)
            Fx.Sparks(prop.position + Vector3.up * 0.1f, new Color(0.6f, 0.58f, 0.55f));
        if (!launched && t >= 1.7f) Launch(t);   // naloz
        UpdateTomasekFlight(t);
        // radost: hlaska jako po zabiti a poskakovani (ruce nahoru - HonzaCheer po animaci)
        if (Once(2, t, CheerStart) && HeroVoice.HasLines(definition, VoiceKind.Kill, 0))
            voice.Play(definition, VoiceKind.Kill, 0, Random.Range(0, 1000));
        float hop = t > CheerStart ? Mathf.Abs(Mathf.Sin((t - CheerStart) * Mathf.PI * 2.6f)) * 0.16f * Mathf.Clamp01((CheerStart + 1.6f - t) / 0.4f) : 0f;
        hero.position = Ground(0.55f, 1.3f) + Vector3.up * hop;

        Vector3 look = Vector3.Lerp(Chest, Stage + new Vector3(-0.6f, 1f, 2.8f), 0.45f);
        pos = Vector3.Lerp(Stage + new Vector3(0.9f, 1.2f, -2.3f), Stage + new Vector3(0.7f, 1.15f, -1.7f), Smooth(Mathf.Clamp01(t / 2.4f)));
        rot = Quaternion.LookRotation(look - pos);
        fov = 50f;
        float flare = launched ? 1f + 1.1f * Mathf.Exp(-(t - launchTime) * 4f) : 1f;
        for (int i = 0; i < stageLights.Count; i++) stageLights[i].intensity = baseIntensity[i] * flare;
    }

    // ---------------- Honza: radost po odpalu Povy ----------------

    const float CheerStart = 1.95f;
    FirstPersonArms.Arm cheerLeft, cheerRight;
    Transform cheerFrame;

    // Po animaci: sleduje letici Povu, pak obe ruce nad hlavu (pesti, pumpuje), smich.
    void HonzaCheer()
    {
        if (time < 1.7f || head == null) return;
        Vector3 forward = hero.forward, up = hero.up, right = hero.right;

        // hlava za letici Povou (nahoru a doprava), pri radosti zpet ke kamere
        float follow = Smooth(Mathf.Clamp01((time - 1.72f) / 0.2f)) * (1f - Smooth(Mathf.Clamp01((time - 2.45f) / 0.3f)));
        head.rotation = Quaternion.AngleAxis(-18f * follow, right) * Quaternion.AngleAxis(-35f * follow, up) * head.rotation;

        float cheer = Smooth(Mathf.Clamp01((time - CheerStart) / 0.25f));
        if (cheer > 0f && visual.BodyAnimator != null)
        {
            if (cheerLeft == null) cheerLeft = FirstPersonArms.BuildArm(visual.BodyAnimator, false);
            if (cheerRight == null) cheerRight = FirstPersonArms.BuildArm(visual.BodyAnimator, true);
            if (cheerFrame == null)
            {
                cheerFrame = new GameObject("RukyNahoru").transform;
                cheerFrame.SetParent(root.transform, false);
            }
            float pump = 0.82f + 0.18f * Mathf.Sin((time - CheerStart) * Mathf.PI * 5.2f);
            foreach (var arm in new[] { cheerLeft, cheerRight })
            {
                if (arm == null) continue;
                Vector3 shoulder = arm.upper.position;
                Vector3 raised = shoulder + up * (0.62f * pump) + right * (arm.side * 0.22f) + forward * 0.08f;
                Vector3 grip = Vector3.Lerp(arm.hand.position, raised, cheer);
                cheerFrame.rotation = Quaternion.LookRotation(up, -forward);   // pest: prsty nahoru, dlan dopredu
                FirstPersonArms.SolveArm(arm, grip, cheerFrame, right * arm.side - forward * 0.3f, false);
            }
        }

        // smich: siroky usmev, otevrena pusa, privrene oci, hlava se pri smichu trese
        float laugh = Smooth(Mathf.Clamp01((time - (CheerStart - 0.1f)) / 0.25f));
        heroFace.Set("mouthSmileLeft", 100f * laugh);
        heroFace.Set("mouthSmileRight", 100f * laugh);
        heroFace.Set("jawOpen", (30f + 15f * Mathf.Abs(Mathf.Sin(time * 14f))) * laugh);
        heroFace.Set("eyeSquintLeft", 55f * laugh);
        heroFace.Set("eyeSquintRight", 55f * laugh);
        heroFace.Set("cheekSquintLeft", 60f * laugh);
        heroFace.Set("cheekSquintRight", 60f * laugh);
        heroFace.Set("browInnerUp", 30f * laugh);
        head.rotation = Quaternion.AngleAxis(Mathf.Sin(time * 16f) * 3f * laugh, right) * head.rotation;
    }

    // ---------------- Viktor: ve skoku vystreli granat do kamery, oslepeni, pak stoji pred kamerou a pije pivo ----------------

    Vector3 ViktorCamera => Stage + new Vector3(0.4f, 1.35f, -2.4f);
    Vector3 ViktorLeapFrom => Ground(3.3f, 3.4f);
    Vector3 ViktorLeapTo => Ground(-2.7f, 2.8f);
    const float ViktorLeapStart = 0.1f, ViktorLeapEnd = 0.95f, ViktorShot = 0.5f, ViktorGrenadeFlight = 0.28f;
    const float ViktorStandAt = 0.95f;          // pod bilou se presune pred kameru (neni videt)
    const float ViktorBlindEnd = 1.45f, ViktorFadeEnd = 2.25f;

    void Viktor(float t, out Vector3 pos, out Quaternion rot, out float fov)
    {
        // skok pres zaber, ve vzduchu vystreli granat do kamery
        if (t < ViktorStandAt)
        {
            float k = Mathf.Clamp01((t - ViktorLeapStart) / (ViktorLeapEnd - ViktorLeapStart));
            hero.position = Vector3.Lerp(ViktorLeapFrom, ViktorLeapTo, Smooth(k)) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.2f;
            hero.rotation = Facing(hero.position, ViktorCamera);
        }
        if (Once(1, t, ViktorShot))
        {
            visual.PlayAttack(false, 0.3f);
            held.Swing(1.3f);
            ProceduralSfx.Play(ProceduralSfx.Gunshot, Chest, 0.8f);
            var hand = visual.GetHand(true);
            grenade = Prim(PrimitiveType.Sphere, root.transform, hand != null ? hand.position : Chest, Vector3.one * 0.09f, new Color(0.2f, 0.25f, 0.2f)).transform;
            grenadeFrom = grenade.position + hero.forward * 0.4f;
        }
        if (grenade != null && grenade.gameObject.activeSelf)
        {
            float k = Mathf.Clamp01((t - ViktorShot) / ViktorGrenadeFlight);
            grenade.position = Vector3.Lerp(grenadeFrom, ViktorCamera + Vector3.forward * 0.3f, k);
            grenade.Rotate(700f * Time.unscaledDeltaTime, 400f * Time.unscaledDeltaTime, 0f);
            if (k >= 1f) grenade.gameObject.SetActive(false);
        }
        // zablesk: obraz zbeli, chvili nic, pak se pomalu vraci
        float blindAt = ViktorShot + ViktorGrenadeFlight;
        if (Once(2, t, blindAt))
        {
            ProceduralSfx.Play(ProceduralSfx.Stun, ViktorCamera, 1f);
            Fx.PlayGlobal(ProceduralSfx.Explosion, 0.35f);
            shake = new Vector3(0.08f, 0.06f, 0f);
        }
        if (flash != null)
        {
            float a = t < blindAt ? 0f : t < ViktorBlindEnd ? 1f : 1f - Smooth(Mathf.Clamp01((t - ViktorBlindEnd) / (ViktorFadeEnd - ViktorBlindEnd)));
            flash.color = new Color(1f, 1f, 1f, a);
        }
        // pod bilou: Viktor stoji pred kamerou bez zbrane (pivo ma v leve ruce - Drink)
        if (Once(3, t, ViktorStandAt))
        {
            hero.position = Ground(0.2f, 1.25f);
            hero.rotation = Facing(hero.position, ViktorCamera);
            held.HideWeapons();
        }

        pos = ViktorCamera;
        Vector3 look = t < ViktorStandAt ? Chest + Vector3.up * 0.1f : Chest + Vector3.up * 0.35f;
        rot = Quaternion.LookRotation(look - pos);
        fov = t < ViktorStandAt ? 55f : 46f;
    }

    Vector3 grenadeFrom;

    // Pullitr piva: zlate pivo, pena, ucho (napoj pro Drink; osa y = osa sklenice).
    Transform BuildBeer()
    {
        var beerRoot = new GameObject("PivoViktor").transform;
        beerRoot.SetParent(root.transform, false);
        var glass = Prim(PrimitiveType.Cylinder, beerRoot, Vector3.zero, new Vector3(0.085f, 0.065f, 0.085f), new Color(0.95f, 0.65f, 0.12f)).transform;
        glass.localPosition = Vector3.zero;
        var foam = Prim(PrimitiveType.Cylinder, beerRoot, Vector3.zero, new Vector3(0.088f, 0.012f, 0.088f), new Color(0.98f, 0.97f, 0.92f)).transform;
        foam.localPosition = new Vector3(0f, 0.07f, 0f);
        var handle = Prim(PrimitiveType.Cube, beerRoot, Vector3.zero, new Vector3(0.02f, 0.09f, 0.02f), new Color(0.85f, 0.9f, 0.9f)).transform;
        handle.localPosition = new Vector3(0.055f, 0f, 0f);
        return beerRoot;
    }

    // ---------------- Mirek: smrst proleti kamerou, priskok, dva sipy zabodnute do kamery ----------------

    static readonly Vector3 MirekCamOffset = new Vector3(0.15f, 1.4f, -2.4f);
    Vector3 MirekCamera => Stage + MirekCamOffset;
    const float StormAt = 0.1f, StormSpeed = 16f, StormRun = 16f;
    const float LeapStart = 1.25f, LeapEnd = 1.7f;
    static readonly float[] ArrowShots = { 1.9f, 2.3f };
    const float ArrowFlight = 0.16f;
    // kam se sipy zabodnou (v souradnicich kamery; z = kousek pred objektivem)
    static readonly Vector3[] ArrowStick = { new Vector3(-0.085f, 0.035f, 0.16f), new Vector3(0.075f, -0.04f, 0.16f) };

    class Arrow { public Transform t; public Vector3 from; public float shotAt; public bool stuck; }
    readonly List<Arrow> arrows = new List<Arrow>();

    void Mirek(float t, out Vector3 pos, out Quaternion rot, out float fov)
    {
        // smrst s Mirkovym oblicejem se rozjede z dalky primo proti kamere a projede ji
        if (Once(0, t, StormAt))
        {
            var go = new GameObject("SmrstUvod");
            go.transform.position = MirekCamera + Vector3.forward * StormRun;
            go.AddComponent<StormVisual>().Init(Vector3.back, StormSpeed, StormRun / StormSpeed + 0.6f, 1.2f, UiKit.TeamColor(0), definition.characterPrefab);
            sceneObjects.Add(go);
            ProceduralSfx.Play(ProceduralSfx.LeapStart, MirekCamera + Vector3.forward * 6f, 1f);
        }
        float passAt = StormAt + (StormRun - 1.2f * StormAbility.LengthFactor) / StormSpeed;   // celo smrsti u kamery
        if (Once(1, t, passAt))
        {
            shake = new Vector3(0.12f, 0.09f, 0f);
            ProceduralSfx.Play(ProceduralSfx.Explosion, MirekCamera, 0.35f);
        }

        // priskok zleva
        Vector3 leapFrom = Ground(-4.2f, 4.6f), leapTo = Ground(0f, 3.0f);
        if (t >= LeapStart)
        {
            float k = Mathf.Clamp01((t - LeapStart) / (LeapEnd - LeapStart));
            hero.position = Vector3.Lerp(leapFrom, leapTo, Smooth(k)) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.3f;
            hero.rotation = Facing(hero.position, MirekCamera);
            if (Once(2, t, LeapEnd))
            {
                shake = new Vector3(0.04f, 0.05f, 0f);
                Fx.Sparks(leapTo - Vector3.up * RootY + Vector3.up * 0.1f, new Color(0.8f, 0.75f, 0.6f));
                ProceduralSfx.Play(ProceduralSfx.Footstep, leapTo, 1f);
            }
        }

        // dva sipy do kamery
        for (int i = 0; i < ArrowShots.Length; i++)
            if (Once(3 + i, t, ArrowShots[i]))
            {
                visual.PlayAttack(false, 0.35f);
                held.Swing(1.4f);
                ProceduralSfx.Play(ProceduralSfx.BowRelease, Chest, 1f);
                var hand = visual.GetHand(true);
                arrows.Add(new Arrow { t = BuildArrow(), from = (hand != null ? hand.position : Chest) + hero.forward * 0.3f, shotAt = t });
            }
        UpdateArrows(t);

        // kamera: nejdriv do dalky (smrst), pak k Mirkovi a mirne se k nemu prisune
        float toHero = Smooth(Mathf.Clamp01((t - (LeapStart - 0.05f)) / 0.4f));
        Vector3 target = Vector3.Lerp(MirekCamera + Vector3.forward * 10f + Vector3.down * 0.1f, Chest + Vector3.up * 0.1f, toHero);
        pos = Vector3.Lerp(MirekCamera, Stage + new Vector3(0.1f, 1.35f, -1.8f), Smooth(Mathf.Clamp01((t - LeapEnd) / (sceneWhip - LeapEnd))));
        rot = Quaternion.LookRotation(target - pos);
        fov = 55f;
    }

    // Sip: drevene telo, kovovy hrot (+z), cervena opereni.
    Transform BuildArrow()
    {
        var arrow = new GameObject("SipDoKamery").transform;
        arrow.SetParent(root.transform, false);
        var shaft = Prim(PrimitiveType.Cylinder, arrow, Vector3.zero, new Vector3(0.012f, 0.25f, 0.012f), new Color(0.55f, 0.38f, 0.2f)).transform;
        shaft.localRotation = Quaternion.Euler(90f, 0f, 0f);
        shaft.localPosition = new Vector3(0f, 0f, -0.25f);
        var tip = Prim(PrimitiveType.Cube, arrow, Vector3.zero, new Vector3(0.022f, 0.006f, 0.05f), new Color(0.2f, 0.2f, 0.22f)).transform;
        tip.localPosition = new Vector3(0f, 0f, 0.015f);
        tip.localRotation = Quaternion.Euler(0f, 0f, 45f);
        foreach (float a in new[] { 0f, 90f })
        {
            var fin = Prim(PrimitiveType.Cube, arrow, Vector3.zero, new Vector3(0.04f, 0.002f, 0.09f), new Color(0.85f, 0.15f, 0.12f)).transform;
            fin.localPosition = new Vector3(0f, 0f, -0.45f);
            fin.localRotation = Quaternion.Euler(0f, 0f, a);
        }
        foreach (var r in arrow.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return arrow;
    }

    void UpdateArrows(float t)
    {
        for (int i = 0; i < arrows.Count; i++)
        {
            var a = arrows[i];
            if (a.stuck || a.t == null || view == null) continue;
            Vector3 stickLocal = ArrowStick[Mathf.Min(i, ArrowStick.Length - 1)];
            Vector3 stick = view.transform.TransformPoint(stickLocal);
            float k = Mathf.Clamp01((t - a.shotAt) / ArrowFlight);
            a.t.position = Vector3.Lerp(a.from, stick, k);
            a.t.rotation = Quaternion.LookRotation(stick - a.from);
            if (k < 1f) continue;

            // zabodnuty do objektivu: drzi se kamery (i v detailu), konec sipu mirne od stredu obrazu, at nezakryva oblicej
            a.stuck = true;
            a.t.SetParent(view.transform, false);
            a.t.localPosition = stickLocal;
            Vector3 back = new Vector3(stickLocal.x * 1.6f, stickLocal.y * 1.6f, 1f).normalized;
            a.t.localRotation = Quaternion.LookRotation(-back, Vector3.up);
            sceneObjects.Add(a.t.gameObject);
            Cracks(stickLocal, i);
            shake = new Vector3(0.05f, 0.04f, 0f);
            ProceduralSfx.Play(ProceduralSfx.BulletImpact, view.transform.position, 1f);
            ProceduralSfx.Play(ProceduralSfx.Hit, view.transform.position, 0.7f);
        }
    }

    // Praskliny "skla objektivu" kolem zasahu (tenke svetle prouzky tesne pred kamerou).
    void Cracks(Vector3 center, int seed, float size = 1f, int lines = 7)
    {
        var group = new GameObject("Praskliny").transform;
        group.SetParent(view.transform, false);
        group.localPosition = center + new Vector3(0f, 0f, -0.004f);
        sceneObjects.Add(group.gameObject);
        var random = new System.Random(seed * 7 + 3);
        for (int i = 0; i < lines; i++)
        {
            float angle = i * 360f / lines + (float)random.NextDouble() * 30f;
            float length = (0.012f + (float)random.NextDouble() * 0.022f) * size;
            var crack = Glow(group, PrimitiveType.Cube, Vector3.zero, new Vector3(length, 0.0007f, 0.0004f), new Color(0.92f, 0.95f, 1f), 0.6f);
            crack.SetParent(group, false);
            crack.localRotation = Quaternion.Euler(0f, 0f, angle);
            crack.localPosition = crack.localRotation * new Vector3(length * 0.5f, 0f, 0f);
            crack.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    void David(float t, out Vector3 pos, out Quaternion rot, out float fov)
    {
        if (Once(0, t, 0.8f))
        {
            visual.PlayAttack(false, 0.4f);
            held.Swing();
            ProceduralSfx.Play(ProceduralSfx.Gunshot, Chest, 0.35f);
            var hand = visual.GetHand(true);
            Vector3 from = hand != null ? hand.position : Chest;
            Vector3 to = tomasekHead != null ? tomasekHead.position + Vector3.down * 0.35f : seat + Vector3.up;
            Fx.Tracer(from, to, new Color(0.6f, 0.4f, 1f), false);
        }
        if (Once(1, t, 0.9f))
        {
            sleepAt = t;
            if (tomasekHead != null) Fx.Sparks(tomasekHead.position + Vector3.down * 0.35f, new Color(0.6f, 0.4f, 1f));
            if (beer != null)
            {
                beer.SetParent(root.transform, true);
                beerVelocity = new Vector3(0.2f, 0.6f, -0.3f);
                beerFlying = true;
            }
        }
        // Tomasek sedi; po zasahu se svezne na stranu a usne
        if (tomasek != null && tomasek.go != null)
        {
            tomasek.Pose(clips.sitIdle, t);
            float slump = sleepAt >= 0f ? Smooth(Mathf.Clamp01((t - sleepAt - 0.15f) / 0.6f)) : 0f;
            tomasek.go.transform.rotation = Quaternion.AngleAxis(62f * slump, seatRotation * Vector3.forward) * seatRotation;
            tomasek.go.transform.position = seat + Vector3.down * 0.1f * slump + (seatRotation * Vector3.right) * 0.15f * slump;
        }
        if (beerFlying && beer != null)
        {
            beerVelocity += Vector3.down * 9f * Time.unscaledDeltaTime;
            beer.position += beerVelocity * Time.unscaledDeltaTime;
            beer.Rotate(new Vector3(300f, 0f, 200f) * Time.unscaledDeltaTime, Space.World);
            if (beer.position.y < Stage.y + 0.05f) { beerFlying = false; Fx.Sparks(beer.position, new Color(1f, 0.75f, 0.2f)); }
        }
        // David se otoci ke kamere a sfoukne hlaven
        float turn = Smooth(Mathf.Clamp01((t - 1.6f) / 0.4f));
        hero.rotation = Quaternion.Slerp(Facing(Stage + new Vector3(0.9f, 0f, 1.1f), seat), ToCamera, turn);
        if (Once(2, t, 2.15f))
        {
            var hand = visual.GetHand(true);
            Fx.Sparks((hand != null ? hand.position : Chest) + Vector3.up * 0.15f, new Color(0.7f, 0.7f, 0.72f));
            ProceduralSfx.Play(ProceduralSfx.Dash, Chest, 0.25f);
        }

        Vector3 look = Vector3.Lerp(seat + Vector3.up * 0.9f, Chest, Smooth(Mathf.Clamp01((t - 1.2f) / 1f)));
        pos = Vector3.Lerp(Stage + new Vector3(0.4f, 1.2f, -1.9f), Stage + new Vector3(0.8f, 1.15f, -1.5f), Smooth(Mathf.Clamp01(t / 2.4f)));
        rot = Quaternion.LookRotation(look - pos);
        fov = 50f;
    }

    void Kondulic(float t, out Vector3 pos, out Quaternion rot, out float fov)
    {
        // hak: lano k zemi pred kamerou, prelet obloukem
        const float pullStart = 0.85f, pullEnd = 1.5f;
        if (Once(0, t, pullStart))
        {
            rope.enabled = true;
            ProceduralSfx.Play(ProceduralSfx.Dash, heroFrom, 1f);
        }
        float k = Mathf.Clamp01((t - pullStart) / (pullEnd - pullStart));
        float eased = k * k;
        hero.position = Vector3.Lerp(heroFrom, heroTo, eased) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.6f;
        hero.rotation = Facing(hero.position, Stage + new Vector3(0.6f, 0f, -2f));
        if (rope != null && rope.enabled)
        {
            var hand = visual.GetHand(true);
            rope.SetPosition(0, hand != null ? hand.position : Chest);
            rope.SetPosition(1, heroTo - Vector3.up * RootY + Vector3.up * 0.05f + Vector3.back * 0.2f);
            if (k >= 1f) rope.enabled = false;
        }
        if (Once(1, t, pullEnd))
        {
            Fx.Sparks(heroTo - Vector3.up * RootY, new Color(0.6f, 0.6f, 0.6f));
            ProceduralSfx.Play(ProceduralSfx.Footstep, heroTo, 1f);
        }
        // kopanec do kamery
        if (Once(2, t, 1.6f)) visual.PlayKick(0.5f);
        if (Once(3, t, 1.82f))
        {
            shake = new Vector3(0.16f, 0.12f, 0f);
            ProceduralSfx.Play(ProceduralSfx.Hit, Chest, 1f);
            Fx.PlayGlobal(ProceduralSfx.Explosion, 0.25f);
            // kopanec rozbije sklo objektivu: velka prasklina vpravo dole (zustane i v detailu)
            if (view != null)
            {
                Cracks(new Vector3(0.045f, -0.03f, 0.16f), 11, 1.7f, 11);
                Cracks(new Vector3(0.062f, -0.012f, 0.161f), 5, 0.8f, 5);
                ProceduralSfx.Play(ProceduralSfx.BulletImpact, view.transform.position, 1f);
            }
        }
        float hit = t >= 1.82f ? Mathf.Exp(-(t - 1.82f) * 3f) : 0f;
        cameraRoll = 18f * hit;

        // z nizka nahoru na sloup, pak sleduje prelet
        Vector3 look = Vector3.Lerp(heroFrom + Vector3.up * 1.1f, Chest, Smooth(k));
        pos = Stage + new Vector3(0.7f, 0.9f, -1.6f) + new Vector3(0f, 0.25f, -0.6f) * hit;
        rot = Quaternion.LookRotation(look - pos) * Quaternion.Euler(-8f * hit, 0f, cameraRoll);
        fov = Mathf.Lerp(40f, 55f, Smooth(k));
    }

    // Kondulic se pri kopu a v detailu mraci: stazene oboci, privrene oci, ohrnuty nos, sevrene rty.
    void KondulicFrown()
    {
        float k = Smooth(Mathf.Clamp01((time - 1.45f) / 0.25f));
        if (k <= 0f) return;
        heroFace.Set("browDownLeft", 100f * k);
        heroFace.Set("browDownRight", 100f * k);
        heroFace.Set("eyeSquintLeft", 65f * k);
        heroFace.Set("eyeSquintRight", 65f * k);
        heroFace.Set("noseSneerLeft", 50f * k);
        heroFace.Set("noseSneerRight", 45f * k);
        heroFace.Set("mouthFrownLeft", 55f * k);
        heroFace.Set("mouthFrownRight", 55f * k);
        heroFace.Set("mouthPressLeft", 50f * k);
        heroFace.Set("mouthPressRight", 50f * k);
        heroFace.Set("jawForward", 20f * k);
        heroFace.Set("mouthSmileLeft", 0f);
        heroFace.Set("mouthSmileRight", 0f);
        heroFace.Set("browOuterUpLeft", 0f);
    }

    // ---------------- Max: ji lasagne, nekdo mu sahne na talir, stinovy krok k kamere, rez kosou pres obraz ----------------

    Vector3 MaxSeat => Stage + new Vector3(-0.45f, 0f, 2.35f) + Vector3.up * RootY;
    Vector3 MaxCamera => Stage + new Vector3(0.35f, 1.3f, -1.4f);
    const float ThiefStart = 0.55f, ThiefReach = 1.0f, MaxVanish = 1.15f, MaxAppear = 1.45f, MaxSlash = 1.55f, MaxCut = 1.72f;
    bool maxChair;
    UnityEngine.UI.Image cutLine, cutGlow;

    void Max(float t, out Vector3 pos, out Quaternion rot, out float fov)
    {
        // sedi a ji
        if (Once(0, t, 0f) && clips != null && clips.sitIdle != null)
            visual.PlayFullBody(clips.sitIdle, MaxVanish, true);

        // Tomasek sedi vedle a nakloni se pro lasagne; po rezu se lekne a ucukne dozadu
        if (tomasek != null && tomasek.go != null)
        {
            tomasek.Pose(clips.sitIdle, t);
            float lean = Smooth(Mathf.Clamp01((t - ThiefStart) / (ThiefReach - ThiefStart))) * 16f;
            float recoil = Smooth(Mathf.Clamp01((t - MaxCut) / 0.2f));
            lean = Mathf.Lerp(lean, -14f, recoil);
            tomasek.go.transform.rotation = Quaternion.AngleAxis(lean, seatRotation * Vector3.right) * seatRotation;
        }
        if (Once(1, t, ThiefReach)) ProceduralSfx.Play(ProceduralSfx.StunConfirm, Chest, 0.5f);

        // stinovy krok: zmizi od stolu, vynori se tesne pred kamerou
        if (Once(2, t, MaxVanish))
        {
            Fx.Sparks(Chest, new Color(0.08f, 0.05f, 0.12f));
            Fx.Explosion(hero.position - Vector3.up * RootY + Vector3.up * 0.4f, 0.6f);
            ProceduralSfx.Play(ProceduralSfx.LeapStart, Chest, 0.7f);
            hero.gameObject.SetActive(false);
        }
        if (Once(3, t, MaxAppear))
        {
            hero.SetPositionAndRotation(Ground(0.25f, -0.35f), Facing(Ground(0.25f, -0.35f), MaxCamera));
            hero.gameObject.SetActive(true);
            Fx.Sparks(Chest, new Color(0.08f, 0.05f, 0.12f));
            Fx.Explosion(hero.position - Vector3.up * RootY + Vector3.up * 0.4f, 0.6f);
            shake = new Vector3(0.05f, 0.04f, 0f);
        }
        // rez kosou pres kameru
        if (Once(4, t, MaxSlash))
        {
            if (visual.HasAbilityAnimation(CharacterVisual.AbilityAnim.RightMouse))
                visual.PlayAbility(CharacterVisual.AbilityAnim.RightMouse, 0f, 1f, 0.6f);
            else visual.PlayAttack(true, 0.5f);
            held.Swing(2.2f);
            ProceduralSfx.Play(ProceduralSfx.SurfaceSlash, Chest, 1f);
        }
        if (Once(5, t, MaxCut))
        {
            shake = new Vector3(0.14f, 0.1f, 0f);
            ProceduralSfx.Play(ProceduralSfx.BulletImpact, MaxCamera, 1f);
            Fx.PlayGlobal(ProceduralSfx.Explosion, 0.2f);
            if (prop != null && prop2 != null)
            {
                Fx.Sparks(prop2.position + Vector3.up * 0.05f, Color.white);   // oblacek prasku
                Fx.Sparks(prop2.position + Vector3.up * 0.1f, new Color(0.95f, 0.95f, 1f));
                flying.Add((prop, new Vector3(-1.6f, 2.6f, 0.4f), new Vector3(200f, 0f, 380f)));
                flying.Add((prop2, new Vector3(1.4f, 2.9f, 0.2f), new Vector3(-150f, 0f, -420f)));
            }
        }
        // obraz rozpuleny: jasny rez, pak tenka trhlina pres celou obrazovku (drzi i v detailu)
        if (cutLine != null)
        {
            float since = t - MaxCut;
            float flashA = since < 0f ? 0f : Mathf.Exp(-since * 5f);
            cutGlow.color = new Color(1f, 0.92f, 1f, flashA * 0.9f);
            cutGlow.rectTransform.sizeDelta = new Vector2(4000f, Mathf.Lerp(4f, 40f, flashA));
            cutLine.color = since < 0f ? Color.clear : new Color(0.05f, 0.03f, 0.07f, 0.9f);
        }
        // po rezu zbran pryc (v detailu si snupne pres bankovku - Drink)
        if (Once(6, t, 2.1f)) held.HideWeapons();

        // kamera: na stul, po vynoreni na Maxe tesne pred ni
        Vector3 look = t < MaxAppear ? Stage + new Vector3(-0.3f, 1.0f, 2.0f) : Chest + Vector3.up * 0.2f;
        pos = MaxCamera;
        rot = Quaternion.LookRotation(look - pos);
        fov = t < MaxAppear ? 48f : 58f;
    }

    // Po animaci: zidle pod Maxem, zvykani, pri sahnuti na talir otoceni hlavy a zamraceni, v detailu jazyk na vidlicce.
    void MaxLate()
    {
        if (!maxChair && time > 0.02f && visual.BodyAnimator != null)
        {
            maxChair = true;
            var hips = visual.BodyAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 at = hips != null ? hips.position : MaxSeat + Vector3.up * 0.5f;
            float top = Mathf.Max(0.3f, at.y - Stage.y - 0.1f);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(at.x, Stage.y + top * 0.5f, at.z + 0.05f), new Vector3(0.5f, top, 0.5f), new Color(0.3f, 0.2f, 0.12f));
        }
        TomasekReach();
        if (head == null || !hero.gameObject.activeInHierarchy) return;
        // zvykani a hlava sklonena nad talirem
        float eating = 1f - Mathf.Clamp01((time - ThiefReach) / 0.1f);
        if (time < MaxVanish)
        {
            // (sklopena hlava nad lajnou)
            float glance = Smooth(Mathf.Clamp01((time - ThiefReach) / 0.1f));
            head.rotation = Quaternion.AngleAxis(-50f * glance, hero.up) * Quaternion.AngleAxis(14f * eating, hero.right) * head.rotation;
            heroFace.Set("browDownLeft", 100f * glance);
            heroFace.Set("browDownRight", 100f * glance);
            heroFace.Set("eyeSquintLeft", 50f * glance);
            heroFace.Set("eyeSquintRight", 50f * glance);
        }
        else if (time < 2.2f)
        {
            heroFace.Set("browDownLeft", 100f);
            heroFace.Set("browDownRight", 100f);
            heroFace.Set("noseSneerLeft", 50f);
            heroFace.Set("mouthPressLeft", 50f);
            heroFace.Set("mouthPressRight", 50f);
        }
        // snupnuti pres bankovku (Drink ji drzi u nosu): nosni dirky, pak vykulene oci a hlava cukne dozadu
        float sniff = Mathf.Clamp01((time - cupAtMouth) / 0.12f) * (1f - Mathf.Clamp01((time - cupDrinkEnd) / 0.15f));
        float kick = Smooth(Mathf.Clamp01((time - cupDrinkEnd) / 0.15f)) * (1f - Smooth(Mathf.Clamp01((time - cupDrinkEnd - 0.9f) / 0.4f)));
        heroFace.Set("mouthFunnel", 0f);
        heroFace.Set("noseSneerLeft", 90f * sniff);
        heroFace.Set("noseSneerRight", 90f * sniff);
        heroFace.Set("eyeBlinkLeft", 70f * sniff);
        heroFace.Set("eyeBlinkRight", 70f * sniff);
        heroFace.Set("eyeWideLeft", 100f * kick);
        heroFace.Set("eyeWideRight", 100f * kick);
        heroFace.Set("browInnerUp", 70f * kick);
        heroFace.Set("browOuterUpLeft", 80f * kick);
        heroFace.Set("browOuterUpRight", 80f * kick);
        heroFace.Set("jawOpen", 20f * kick);
        head.rotation = Quaternion.AngleAxis(-12f * kick + Mathf.Sin(time * 40f) * 2f * kick, hero.right) * head.rotation;
        if (Once(11, time, cupAtMouth + 0.05f)) ProceduralSfx.Play(ProceduralSfx.Splash, head.position, 0.25f);   // "sssnf"
    }

    // Tomasek natahuje pravou ruku k taliri (IK po animaci sezeni), po rezu ji stahne; oblicej: chtivy, pak vydeseny.
    FirstPersonArms.Arm tomasekArm;
    Transform tomasekGrip;

    void TomasekReach()
    {
        if (tomasek == null || tomasek.go == null || tomasek.animator == null) return;
        if (tomasekArm == null) tomasekArm = FirstPersonArms.BuildArm(tomasek.animator, true);
        if (tomasekGrip == null)
        {
            tomasekGrip = new GameObject("UchopTomaska").transform;
            tomasekGrip.SetParent(root.transform, false);
        }
        float reach = Smooth(Mathf.Clamp01((time - ThiefStart) / (ThiefReach - ThiefStart))) * (1f - Smooth(Mathf.Clamp01((time - MaxCut) / 0.15f)));
        if (tomasekArm != null && reach > 0f)
        {
            Vector3 plate = Stage + new Vector3(-0.35f, 0.82f, 1.8f);   // k lajne
            Vector3 grip = Vector3.Lerp(tomasekArm.hand.position, plate, reach);
            Vector3 toward = Vector3.ProjectOnPlane(plate - tomasekArm.upper.position, Vector3.up).normalized;
            tomasekGrip.rotation = Quaternion.LookRotation(toward, Vector3.Cross(toward, Vector3.up));   // dlan dolu, prsty k taliri
            FirstPersonArms.SolveArm(tomasekArm, grip, tomasekGrip, Vector3.down + seatRotation * Vector3.forward * -0.3f, false, true);
        }
        // hlava k taliri, chtivy usmev; kdyz Max zmizi a vynori se, vydeseny
        float scared = Smooth(Mathf.Clamp01((time - MaxVanish) / 0.15f));
        if (tomasekHead != null)
            tomasekHead.rotation = Quaternion.AngleAxis(28f * reach * (1f - scared), seatRotation * Vector3.right) * tomasekHead.rotation;
        tomasekFace.Set("mouthSmileLeft", 60f * reach * (1f - scared));
        tomasekFace.Set("mouthSmileRight", 40f * reach * (1f - scared));
        tomasekFace.Set("eyeWideLeft", 90f * scared);
        tomasekFace.Set("eyeWideRight", 90f * scared);
        tomasekFace.Set("browInnerUp", 80f * scared);
        tomasekFace.Set("jawOpen", 45f * scared);
    }

    // Srolovana bankovka (napoj pro Drink - snupnuti): zelena trubicka, osa y.
    Transform BuildBanknote()
    {
        var note = new GameObject("Bankovka").transform;
        note.SetParent(root.transform, false);
        Prim(PrimitiveType.Cylinder, note, Vector3.zero, new Vector3(0.014f, 0.045f, 0.014f), new Color(0.55f, 0.7f, 0.5f)).transform.localPosition = Vector3.zero;
        Prim(PrimitiveType.Cylinder, note, Vector3.zero, new Vector3(0.0145f, 0.012f, 0.0145f), new Color(0.75f, 0.6f, 0.75f)).transform.localPosition = new Vector3(0f, -0.02f, 0f);
        return note;
    }

    // Rez pres obraz: sikma cara pres celou obrazovku (zablesk, pak tmava trhlina).
    void BuildCutOverlay()
    {
        var canvasObject = new GameObject("Rez");
        canvasObject.transform.SetParent(root.transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        cutGlow = CutBar(canvasObject.transform, "Zablesk", 40f);
        cutLine = CutBar(canvasObject.transform, "Trhlina", 3f);
    }

    static UnityEngine.UI.Image CutBar(Transform parent, string name, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(4000f, height);
        rect.anchoredPosition = new Vector2(0f, -40f);
        rect.localRotation = Quaternion.Euler(0f, 0f, 24f);
        var image = go.GetComponent<UnityEngine.UI.Image>();
        image.color = Color.clear;
        image.raycastTarget = false;
        return image;
    }

    // ---------------- Mosino: breakdance v krouzku, kamera prileze moc blizko - odstrel, kamera rozbita na zemi ----------------

    Vector3 MosinoCenter => Stage + new Vector3(0f, 0f, 2.5f);
    Vector3 MosinoCamStart => MosinoCenter + new Vector3(0.25f, 1.55f, -4.8f);
    Vector3 MosinoCamNear => MosinoCenter + new Vector3(0.1f, 1.25f, -1.05f);
    const float MosinoStop = 1.7f, MosinoShot = 1.88f, MosinoHit = 1.98f;
    const float CamLaunchBack = 8.5f, CamLaunchUp = 4.5f, CamGravity = 14f;
    readonly List<(Puppet puppet, float phase)> crowd = new List<(Puppet, float)>();
    bool cameraLanded;

    void Mosino(float t, out Vector3 pos, out Quaternion rot, out float fov)
    {
        // breakdance (Flair z ultimatky), dokud kamera neprijede moc blizko
        if (Once(0, t, 0f) && visual.HasUltimateAnimation) visual.PlayUltimate(MosinoStop);
        if (speaker != null)
            speaker.localScale = new Vector3(1.1f, 2.2f, 0.8f) * (1f + Mathf.Abs(Mathf.Sin(t * 9f)) * 0.03f);
        for (int i = 0; i < stageLights.Count; i++)
            stageLights[i].color = Color.HSVToRGB(Mathf.Repeat(t * 0.9f + i * 0.27f, 1f), 0.7f, 1f);

        // krouzek: postavy stoji, pohupuji se a fandi; po vystrelu vic
        for (int i = 0; i < crowd.Count; i++)
        {
            var (puppet, phase) = crowd[i];
            if (puppet.go == null) continue;
            puppet.Pose(clips.idle, t + phase);
            float bounce = Mathf.Abs(Mathf.Sin(t * (t > MosinoHit ? 9f : 6f) + phase * 3f)) * (t > MosinoHit ? 0.12f : 0.05f);
            Vector3 p = puppet.go.transform.position;
            puppet.go.transform.position = new Vector3(p.x, Stage.y + bounce, p.z);
        }

        // kamera se blizi, Mosino se zastavi a vytasi zbran
        float dolly = Mathf.Pow(Smooth(Mathf.Clamp01(t / MosinoStop)), 1.4f);
        Vector3 camPos = Vector3.Lerp(MosinoCamStart, MosinoCamNear, dolly);
        if (t >= MosinoStop)
            hero.rotation = Quaternion.Slerp(hero.rotation, Facing(hero.position, camPos), 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
        if (Once(1, t, MosinoShot))
        {
            visual.PlayAttack(false, 0.35f);
            held.Swing(2.4f);
            ProceduralSfx.Play(ProceduralSfx.Gunshot, Chest, 1f);
            Fx.PlayGlobal(ProceduralSfx.Explosion, 0.6f);
            var hand = visual.GetHand(true);
            ring = Glow(root.transform, PrimitiveType.Cylinder, hand != null ? hand.position : Chest, new Vector3(0.2f, 0.01f, 0.2f), new Color(0.55f, 0.3f, 1f), 3f);
        }
        if (ring != null)
        {
            float r = Mathf.Clamp01((t - MosinoShot) / (MosinoHit - MosinoShot));
            ring.position = Vector3.Lerp(Chest + hero.forward * 0.4f, MosinoCamNear, r);
            ring.rotation = Quaternion.FromToRotation(Vector3.up, (MosinoCamNear - Chest).normalized);
            ring.localScale = new Vector3(0.2f + r * 1.2f, 0.01f, 0.2f + r * 1.2f);
            ring.gameObject.SetActive(r < 1f);
        }

        // zasah: kamera odleti dozadu obloukem, prevraci se, dopadne na zem a zustane lezet (sklo popraskane)
        float since = t - MosinoHit;
        Vector3 back = Flat(MosinoCamNear - MosinoCenter).normalized;
        Vector3 lookAtMosino = Quaternion.LookRotation(Chest + Vector3.up * 0.2f - camPos) * Vector3.forward;
        if (since < 0f)
        {
            pos = camPos;
            rot = Quaternion.LookRotation(t < MosinoStop - 0.3f ? MosinoCenter + Vector3.up * 0.6f - camPos : lookAtMosino);
        }
        else
        {
            if (Once(2, t, MosinoHit))
            {
                shake = new Vector3(0.3f, 0.25f, 0f);
                ProceduralSfx.Play(ProceduralSfx.BulletImpact, MosinoCamNear, 1f);
                if (view != null) Cracks(new Vector3(-0.02f, 0.01f, 0.16f), 21, 1.4f, 9);
            }
            float groundY = Stage.y + 0.12f;
            float y0 = MosinoCamNear.y;
            float land = (CamLaunchUp + Mathf.Sqrt(CamLaunchUp * CamLaunchUp + 2f * CamGravity * (y0 - groundY))) / CamGravity;
            Vector3 landPos = MosinoCamNear + back * CamLaunchBack * land;
            landPos.y = groundY;
            Quaternion rest = Quaternion.LookRotation(MosinoCenter + Vector3.up * 0.9f - landPos) * Quaternion.Euler(-4f, 0f, 72f);
            if (since < land)
            {
                pos = MosinoCamNear + back * CamLaunchBack * since + Vector3.up * (CamLaunchUp * since - 0.5f * CamGravity * since * since);
                rot = Quaternion.LookRotation(lookAtMosino) * Quaternion.Euler(-since * 900f, since * 120f, since * 520f);
            }
            else
            {
                float after = since - land;
                if (!cameraLanded)
                {
                    cameraLanded = true;
                    shake = new Vector3(0.12f, 0.1f, 0f);
                    ProceduralSfx.Play(ProceduralSfx.BulletImpact, landPos, 1f);
                    ProceduralSfx.Play(ProceduralSfx.Hit, landPos, 0.8f);
                    SpiderCracks();
                }
                float slide = Smooth(Mathf.Clamp01(after / 0.35f));
                float bounce = Mathf.Abs(Mathf.Sin(after * 14f)) * Mathf.Exp(-after * 9f) * 0.12f;
                pos = landPos + back * 0.45f * slide + Vector3.up * bounce;
                rot = Quaternion.Slerp(Quaternion.LookRotation(lookAtMosino) * Quaternion.Euler(-land * 900f, land * 120f, land * 520f), rest, Smooth(Mathf.Clamp01(after / 0.18f)));
            }
        }
        if (flash != null)
        {
            float a = since > 0f ? Mathf.Exp(-since * 4f) * 0.6f : 0f;
            flash.color = new Color(0.75f, 0.55f, 1f, a);
        }
        // Mosino nad rozbitou kamerou neco prohodi
        if (Once(3, t, 3.1f) && HeroVoice.HasLines(definition, VoiceKind.Kill, 0))
            voice.Play(definition, VoiceKind.Kill, 0, Random.Range(0, 1000));
        fov = 55f;
    }

    // Krouzek ostatnich hrdinu (modely hrdinu bez Mosina) kolem stredu, celem k Mosinovi; mezera pro kameru.
    void BuildCrowd()
    {
        crowd.Clear();
        cameraLanded = false;
        if (clips == null || clips.idle == null) return;
        var heroes = new List<HeroDefinition>();
        foreach (var h in HeroRegistry.All)
            if (h != null && h != definition && h.characterPrefab != null) heroes.Add(h);
        for (int i = 0; i < heroes.Count; i++)
        {
            float angle = Mathf.Lerp(38f, 322f, heroes.Count > 1 ? i / (heroes.Count - 1f) : 0.5f) * Mathf.Deg2Rad;
            Vector3 at = MosinoCenter + new Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle)) * 2.0f;
            var go = Instantiate(heroes[i].characterPrefab, root.transform);
            go.name = "Divak_" + heroes[i].name;
            foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            go.transform.SetPositionAndRotation(at, Facing(at, MosinoCenter));
            var animator = go.GetComponentInChildren<Animator>();
            if (animator == null) { Destroy(go); continue; }
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var puppet = new Puppet { go = go, animator = animator };
            puppet.Pose(clips.idle, i * 0.37f);
            crowd.Add((puppet, i * 0.37f));
        }
    }

    // Pavucina prasklin pres cely objektiv: paprsky od mista dopadu a soustredne kruhy z kratkych useku.
    void SpiderCracks()
    {
        if (view == null) return;
        var group = new GameObject("Pavucina").transform;
        group.SetParent(view.transform, false);
        Vector3 center = new Vector3(0.025f, -0.015f, 0.155f);
        group.localPosition = center;
        sceneObjects.Add(group.gameObject);
        var random = new System.Random(42);
        const int spokes = 15;
        var angles = new float[spokes];
        for (int i = 0; i < spokes; i++)
        {
            angles[i] = i * 360f / spokes + (float)random.NextDouble() * 14f;
            float length = 0.12f + (float)random.NextDouble() * 0.08f;
            Crack(group, Vector3.zero, angles[i], length, 0.0009f);
        }
        foreach (float radius in new[] { 0.012f, 0.028f, 0.05f, 0.08f })
            for (int i = 0; i < spokes; i++)
            {
                float a0 = angles[i] * Mathf.Deg2Rad, a1 = angles[(i + 1) % spokes] * Mathf.Deg2Rad + (i == spokes - 1 ? Mathf.PI * 2f : 0f);
                float r0 = radius * (0.85f + (float)random.NextDouble() * 0.3f), r1 = radius * (0.85f + (float)random.NextDouble() * 0.3f);
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * r0, Mathf.Sin(a0) * r0, 0f), p1 = new Vector3(Mathf.Cos(a1) * r1, Mathf.Sin(a1) * r1, 0f);
                Vector3 d = p1 - p0;
                Crack(group, (p0 + p1) * 0.5f - d * 0.5f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, d.magnitude, 0.0007f);
            }
        // tmavsi rozbity stred
        var dot = Glow(group, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.012f, 0.012f, 0.001f), new Color(0.85f, 0.88f, 0.95f), 0.4f);
        dot.SetParent(group, false);
        dot.localPosition = Vector3.zero;
    }

    void Crack(Transform group, Vector3 from, float angle, float length, float width)
    {
        var crack = Glow(group, PrimitiveType.Cube, Vector3.zero, new Vector3(length, width, 0.0004f), new Color(0.92f, 0.95f, 1f), 0.55f);
        crack.SetParent(group, false);
        crack.localRotation = Quaternion.Euler(0f, 0f, angle);
        crack.localPosition = from + crack.localRotation * new Vector3(length * 0.5f, 0f, 0f);
        crack.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // Mosino pri vystrelu zarve, pak se samolibe usmiva.
    void MosinoLate()
    {
        float yell = Mathf.Clamp01((time - (MosinoShot - 0.1f)) / 0.1f) * (1f - Mathf.Clamp01((time - (MosinoHit + 0.35f)) / 0.2f));
        float smug = Smooth(Mathf.Clamp01((time - (MosinoHit + 0.4f)) / 0.3f));
        heroFace.Set("jawOpen", 70f * yell);
        heroFace.Set("browDownLeft", 80f * yell);
        heroFace.Set("browDownRight", 80f * yell);
        heroFace.Set("mouthStretchLeft", 50f * yell);
        heroFace.Set("mouthStretchRight", 50f * yell);
        heroFace.Set("mouthSmileLeft", 85f * smug);
        heroFace.Set("mouthSmileRight", 30f * smug);
        heroFace.Set("browOuterUpLeft", 50f * smug);
    }

    // ---------------- Sindel: slivovice s Viktorem, vsimne si kamery, granat, oslepeni, panak ----------------

    Vector3 SindelSeat => Stage + new Vector3(-0.85f, 0f, 2.2f) + Vector3.up * RootY;
    Vector3 SindelCamera => Stage + new Vector3(0.35f, 1.3f, -0.7f);
    const float SindelNotice = 0.75f, SindelStand = 1.15f, SindelThrow = 1.4f, SindelGrenadeFlight = 0.35f;
    const float SindelBlindEnd = 2.15f, SindelFadeEnd = 2.75f;
    bool sindelChair;
    Transform holyGrenade;
    Vector3 holyFrom;

    void Sindel(float t, out Vector3 pos, out Quaternion rot, out float fov)
    {
        if (tomasek != null && tomasek.go != null) tomasek.Pose(clips.sitIdle, t);   // Viktor sedi

        // sedi u stolu (poza sezeni pres cele telo), pak vstane a otoci se ke kamere
        if (Once(0, t, 0f) && clips != null && clips.sitIdle != null)
            visual.PlayFullBody(clips.sitIdle, SindelStand, true);
        float stand = Smooth(Mathf.Clamp01((t - SindelStand) / 0.3f));
        Vector3 toCamera = Facing(SindelSeat, SindelCamera) * Vector3.forward;
        hero.position = SindelSeat + toCamera * 0.45f * stand;
        hero.rotation = Quaternion.Slerp(Quaternion.Euler(0f, 90f, 0f), Facing(SindelSeat, SindelCamera), stand);
        if (Once(1, t, SindelNotice)) ProceduralSfx.Play(ProceduralSfx.StunConfirm, Chest, 0.6f);   // "hm?!"

        // svaty granat do kamery
        if (Once(2, t, SindelThrow))
        {
            held.PlayThrow();
            ProceduralSfx.Play(ProceduralSfx.Dash, Chest, 0.7f);
            var hand = visual.GetHand(true);
            holyFrom = (hand != null ? hand.position : Chest) + Vector3.up * 0.1f;
            holyGrenade = BuildHolyGrenade();
        }
        if (holyGrenade != null && holyGrenade.gameObject.activeSelf)
        {
            float k = Mathf.Clamp01((t - SindelThrow) / SindelGrenadeFlight);
            holyGrenade.position = Vector3.Lerp(holyFrom, SindelCamera + Vector3.forward * 0.35f, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.3f;
            holyGrenade.Rotate(500f * Time.unscaledDeltaTime, 250f * Time.unscaledDeltaTime, 0f);
            if (k >= 1f) holyGrenade.gameObject.SetActive(false);
        }
        // vybuch: obraz zbeli (do zlata), chvili nic, pak se pomalu vraci
        float blindAt = SindelThrow + SindelGrenadeFlight;
        if (Once(3, t, blindAt))
        {
            Fx.PlayGlobal(ProceduralSfx.Explosion, 0.5f);
            ProceduralSfx.Play(ProceduralSfx.Stun, SindelCamera, 1f);
            shake = new Vector3(0.18f, 0.14f, 0f);
        }
        if (flash != null)
        {
            float a = t < blindAt ? 0f : t < SindelBlindEnd ? 1f : 1f - Smooth(Mathf.Clamp01((t - SindelBlindEnd) / (SindelFadeEnd - SindelBlindEnd)));
            float gold = Mathf.Clamp01((t - blindAt) / 0.5f);
            flash.color = new Color(1f, Mathf.Lerp(0.82f, 1f, gold), Mathf.Lerp(0.45f, 1f, gold), a);
        }

        // kamera: zboku na stul, po vstani na Sindela
        Vector3 look = Vector3.Lerp(Stage + new Vector3(-0.2f, 0.95f, 2.2f), Chest + Vector3.up * 0.3f, Smooth(Mathf.Clamp01((t - SindelStand) / 0.4f)));
        pos = SindelCamera;
        rot = Quaternion.LookRotation(look - pos);
        fov = 50f;
    }

    // Po animaci: zidle pod Sindelem (podle boku v sedu), pri vsimnuti hlava ke kamere, prekvapeni a pak zamraceni.
    void SindelLate()
    {
        if (!sindelChair && time > 0.02f && visual.BodyAnimator != null)
        {
            sindelChair = true;
            var hips = visual.BodyAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 at = hips != null ? hips.position : SindelSeat + Vector3.up * 0.5f;
            float top = Mathf.Max(0.3f, at.y - Stage.y - 0.1f);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(at.x - 0.05f, Stage.y + top * 0.5f, at.z), new Vector3(0.5f, top, 0.55f), new Color(0.45f, 0.3f, 0.16f));
        }
        if (head == null) return;
        float notice = Smooth(Mathf.Clamp01((time - SindelNotice) / 0.12f)) * (1f - Smooth(Mathf.Clamp01((time - SindelStand) / 0.3f)));
        head.rotation = Quaternion.AngleAxis(62f * notice, Vector3.up) * head.rotation;

        float surprise = Mathf.Clamp01((time - SindelNotice) / 0.1f) * (1f - Mathf.Clamp01((time - SindelStand) / 0.2f));
        float anger = Smooth(Mathf.Clamp01((time - SindelStand) / 0.2f)) * (1f - Mathf.Clamp01((time - 1.85f) / 0.1f));
        heroFace.Set("eyeWideLeft", 80f * surprise);
        heroFace.Set("eyeWideRight", 80f * surprise);
        heroFace.Set("browOuterUpLeft", 70f * surprise);
        heroFace.Set("browInnerUp", 60f * surprise);
        heroFace.Set("browDownLeft", 100f * anger);
        heroFace.Set("browDownRight", 100f * anger);
        heroFace.Set("noseSneerLeft", 40f * anger);
        heroFace.Set("mouthPressLeft", 45f * anger);
        heroFace.Set("mouthPressRight", 45f * anger);
    }

    // Stul se slivovici: deska, nohy, lahev s etiketou a dva panaky.
    void BuildSlivovitzTable()
    {
        Vector3 c = Stage + new Vector3(0f, 0f, 2.2f);
        var wood = new Color(0.42f, 0.27f, 0.15f);
        Prim(PrimitiveType.Cube, root.transform, c + Vector3.up * 0.74f, new Vector3(1.2f, 0.05f, 0.8f), wood);
        foreach (float x in new[] { -0.55f, 0.55f })
            foreach (float z in new[] { -0.33f, 0.33f })
                Prim(PrimitiveType.Cube, root.transform, c + new Vector3(x, 0.36f, z), new Vector3(0.06f, 0.72f, 0.06f), wood);
        var glass = new Color(0.82f, 0.9f, 0.86f);
        Prim(PrimitiveType.Cylinder, root.transform, c + new Vector3(0.05f, 0.9f, 0.08f), new Vector3(0.085f, 0.14f, 0.085f), glass);
        Prim(PrimitiveType.Cylinder, root.transform, c + new Vector3(0.05f, 1.1f, 0.08f), new Vector3(0.03f, 0.06f, 0.03f), glass);
        Prim(PrimitiveType.Cylinder, root.transform, c + new Vector3(0.05f, 1.165f, 0.08f), new Vector3(0.034f, 0.012f, 0.034f), new Color(0.75f, 0.1f, 0.1f));
        Prim(PrimitiveType.Cylinder, root.transform, c + new Vector3(0.05f, 0.9f, 0.08f), new Vector3(0.087f, 0.05f, 0.087f), new Color(0.95f, 0.88f, 0.6f));   // etiketa
        foreach (float x in new[] { -0.32f, 0.36f })
            Prim(PrimitiveType.Cylinder, root.transform, c + new Vector3(x, 0.79f, -0.05f), new Vector3(0.045f, 0.03f, 0.045f), glass);
    }

    // Panak slivovice (napoj pro Drink): sklicko s trochou zlutave palenky.
    Transform BuildShotGlass()
    {
        var shot = new GameObject("Panak").transform;
        shot.SetParent(root.transform, false);
        Prim(PrimitiveType.Cylinder, shot, Vector3.zero, new Vector3(0.048f, 0.03f, 0.048f), new Color(0.85f, 0.92f, 0.9f)).transform.localPosition = Vector3.zero;
        Prim(PrimitiveType.Cylinder, shot, Vector3.zero, new Vector3(0.04f, 0.02f, 0.04f), new Color(0.98f, 0.9f, 0.6f)).transform.localPosition = new Vector3(0f, 0.008f, 0f);
        return shot;
    }

    // Svaty granat: zlata koule s bilym krizem.
    Transform BuildHolyGrenade()
    {
        var ball = Prim(PrimitiveType.Sphere, root.transform, holyFrom, Vector3.one * 0.14f, SindelAbility.GrenadeColor).transform;
        foreach (var size in new[] { new Vector3(0.12f, 0.7f, 0.12f), new Vector3(0.5f, 0.12f, 0.12f) })
        {
            var bar = Prim(PrimitiveType.Cube, ball, Vector3.zero, size, new Color(0.95f, 0.9f, 0.8f)).transform;
            bar.localScale = size;
            bar.localPosition = new Vector3(0f, size.y > 0.5f ? 0.75f : 0.85f, 0f);
        }
        return ball;
    }

    // Odletujici veci (bedny, pulky lasagni).
    void UpdateFlying()
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = flying.Count - 1; i >= 0; i--)
        {
            var (t, velocity, spin) = flying[i];
            if (t == null) { flying.RemoveAt(i); continue; }
            velocity += Vector3.down * 9f * dt;
            t.position += velocity * dt;
            t.Rotate(spin * dt, Space.World);
            flying[i] = (t, velocity, spin);
            if (t.position.y < Stage.y - 3f) { t.gameObject.SetActive(false); flying.RemoveAt(i); }
        }
    }

    // Po animaci postavy: zamerovac na ocich (Viktor), spici Tomasek (David).
    void LateScene()
    {
        if (scene == "Viktor" && visor != null && visor.gameObject.activeSelf && head != null)
        {
            // osy obliceje v prostoru hlavy (zachyceno pri nasazeni) - zamerovac pak sleduje kyvani hlavou
            if (!visorAxes)
            {
                var inverse = Quaternion.Inverse(head.rotation);
                visorForward = inverse * hero.forward;
                visorUp = inverse * hero.up;
                visorAxes = true;
            }
            Vector3 forward = head.rotation * visorForward, up = head.rotation * visorUp;
            float down = Smooth(Mathf.Clamp01((time - 0.45f) / 0.2f));
            visor.SetPositionAndRotation(head.position + forward * 0.105f + up * Mathf.Lerp(0.15f, 0.065f, down), Quaternion.LookRotation(forward, up));
        }
        if (cup != null && head != null)
            Drink();
        if (scene == "Honza")
            HonzaCheer();
        if (scene == "Sniper")
            KondulicFrown();
        if (scene == "Sindel")
            SindelLate();
        if (scene == "Max")
            MaxLate();
        if (scene == "Bard")
            MosinoLate();
        if (scene == "Anna" && sleepAt >= 0f && tomasek != null && tomasek.go != null)
        {
            float k = Smooth(Mathf.Clamp01((time - sleepAt) / 0.4f));
            tomasekFace.Set("eyeBlinkLeft", 100f * k);
            tomasekFace.Set("eyeBlinkRight", 100f * k);
            tomasekFace.Set("jawOpen", 30f * k);
            tomasekFace.Set("mouthFunnel", 25f * k);
            if (tomasekHead != null)
                tomasekHead.rotation = Quaternion.AngleAxis(35f * k, tomasek.go.transform.right) * tomasekHead.rotation;
        }
    }
}
