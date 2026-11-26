using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Vlastni uvodni scenky POTG pro jednotlive hrdiny (Pova je v PotgIntro.cs). Kazda trva asi 2,5 s akce, pak
// spolecny prudky najezd na oblicej s usklibnutim (PotgIntro.Animate). Nazev = asset hrdiny v Resources/Heroes.
//  Honza   - drzi karburator a fouka z nej prach; za nim vybuchne naloz a Pova (sedi na bedne) odleti. Honza ani nemrkne.
//  Viktor  - stahne si zamerovac na oci, vystreli granat do kamery (obraz zbeli); pak stoji tesne pred kamerou,
//            ukaze krabicku Cara, praskne s ni do objektivu a kamera odleti.
//  Mirek   - vystreli z luku mimo zaber; za nim se prozene jeho smrst s oblicejem. Neotoci se, pak pohled do kamery.
//  Anna    - (David) uspavaci pistolkou uspí Tomaska na gauci, pomalu k nemu dojde; pohled uspaneho Tomaska zespodu:
//            David se nad nim nakloni, podiva se do kamery a da si prst na pusu ("psst") - jako Anin POTG v Overwatchi.
//  Sniper  - (Kondulic) na vysokem sloupu, hakem se pritahne dolu ke kamere a kopne do ni.
//  Max     - u stolku s lasagnemi zmizi ve stinu, vynori se pred kamerou a otockou kosy rozpuli lasagne.
//  Bard    - (Mosino) u reproduktoru: basovy uder odhodi bedny, svetla se rozblikaji a tanci.
//  Sindel  - souboj za poledne u stolu se slivovici: chuchvalec travy, oci, Sindel tasi a prostreli Viktorovi panaka
//            u pusy, sfoukne kour, protoci pistoli a zastrci ji, v detailu dopije svuj panak.
public partial class PotgIntro
{
    string scene = "";
    float sceneWhip = 2.45f;
    float sceneLength = 5f;           // delka uvodu (s) - PotgUI podle ni casuje kartu a strih na zaznam
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
    // Max snupe: hlava se skloni k bankovce (ne zakloni jako pri piti) a ruka ji drzi konecky prstu dole u konce,
    // kousek stranou od obliceje (ne celou dlani jako plechovku - pest zajizdela do obliceje)
    float cupHeadTilt = -18f;   // naklon hlavy pri piti (zaporne = zaklonit, kladne = sklonit)
    float cupGripSide, cupGripDown;   // ruka od osy napoje do strany / dolu po nem (m)
    bool cupFollowsHead;              // osy obliceje z hlavy (bankovka u nosu), ne z tela
    Vector3 faceForwardLocal, faceUpLocal, faceRightLocal;
    bool faceAxes;
    FirstPersonArms.Arm drinkArm;
    bool silentCloseup;   // v detailu bez hlasky (pije)
    bool drinkSmile = true;   // po napiti usmev (Pova pije zamracene)
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
        sceneLength = 5f;
        cameraRoll = 0f;
        sceneEvents = 0;
        sleepAt = -1f;
        flying.Clear();
        prop = prop2 = visor = grenade = ring = speaker = null;
        visorAxes = false;
        cup = drinkFrame = null;
        cupNose = 0f;
        cupHeadTilt = -18f;
        cupGripSide = cupGripDown = 0f;
        cupFollowsHead = faceAxes = false;
        cheerLeft = cheerRight = null;
        cheerFrame = null;
        honzaDetonator = null;
        duelPistol = viktorGlass = tumbleweed = null;
        duelArm = viktorArm = null;
        caro = null;
        viktorLeft = null;
        davidPistol = null;
        davidLeft = davidRight = null;
        davidFaceAxes = false;
        konFaceAxes = false;
        konLight = null;
        konLeft = konRight = null;
        davidFaceLight = null;
        tomasekArm = null;
        tray = trayLine = bill = null;
        trayArm = billArm = null;
        maxFaceAxes = false;
        tomasekGrip = null;
        cutLine = cutGlow = null;
        drinkArm = null;
        silentCloseup = false;
        drinkSmile = true;
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
        if (cupFollowsHead)
        {
            // osy obliceje v prostoru hlavy (zachyceno na zacatku) - napoj sleduje natoceni hlavy z animace
            if (!faceAxes)
            {
                var inverse = Quaternion.Inverse(head.rotation);
                faceForwardLocal = inverse * hero.forward;
                faceUpLocal = inverse * hero.up;
                faceRightLocal = inverse * hero.right;
                faceAxes = true;
            }
            forward = head.rotation * faceForwardLocal;
            up = head.rotation * faceUpLocal;
            right = head.rotation * faceRightLocal;
        }
        // hlava se pri piti zakloni (snupnuti: lehce skloni)
        var back = Quaternion.AngleAxis(cupHeadTilt * sip, right);
        head.rotation = back * head.rotation;
        if (cupFollowsHead) { forward = back * forward; up = back * up; }
        Vector3 mouth = head.position + (cupFollowsHead ? Quaternion.identity : back) * (forward * (0.115f + cupNose * 0.3f) + up * (0.005f + cupNose));

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
            FirstPersonArms.SolveArm(drinkArm, center - right * (cupRadius + cupGripSide) - canUp * cupGripDown, drinkFrame, -up - right * 0.8f - forward * 0.2f, false);
        }

        // oci zavrene a rty na plechovce, po napiti spokojeny usmev
        heroFace.Set("eyeBlinkLeft", 85f * sip);
        heroFace.Set("eyeBlinkRight", 85f * sip);
        heroFace.Set("mouthFunnel", 55f * sip);
        if (drinkSmile)
        {
            heroFace.Set("mouthSmileLeft", Mathf.Lerp(65f * (1f - sip), 90f, lower));
            heroFace.Set("mouthSmileRight", Mathf.Lerp(20f * (1f - sip), 70f, lower));
        }
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
                caro = BuildCaro();
                caro.gameObject.SetActive(false);
                sceneWhip = 99f;   // bez spolecneho detailu - konci odletem kamery po uderu Carem
                break;

            case "Mirek":
                // nejdriv mimo zaber (priskoci az po smrsti)
                hero.SetPositionAndRotation(Ground(-12f, 4f), Facing(Ground(0f, 3f), MirekCamera));
                arrows.Clear();
                sceneWhip = 2.75f;
                break;

            case "Anna":
                // Tomasek na gauci, David pred nim (kus od nej) s uspavaci pistolkou v leve ruce
                seat = Stage + new Vector3(-1.3f, 0f, 2.7f);
                seatRotation = Quaternion.Euler(0f, 130f, 0f);
                BuildTomasek(new Vector3(-1.3f, 0f, 2.7f), seatRotation, true);
                hero.SetPositionAndRotation(DavidStart, Facing(DavidStart, seat));
                silentCloseup = true;   // misto hlasky "psst"
                sceneWhip = 99f;        // bez spolecneho detailu - konci pohledem uspaneho Tomaska
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
                sceneWhip = 99f;   // bez spolecneho detailu - konci pohledem rozbite kamery ze zeme (Kondulic se nad ni nakloni)
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
                BuildMaxSnort();   // v detailu: stribrny tacek s lajnou, snupnuti bankovkou, blazenost
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
                // souboj za poledne: Sindel a Viktor sedi proti sobe u stolu se slivovici
                BuildSlivovitzTable();
                hero.SetPositionAndRotation(SindelSeat, Quaternion.Euler(0f, 90f, 0f));
                BuildTomasek(new Vector3(0.85f, 0f, 2.2f), Quaternion.Euler(0f, -90f, 0f), false, "Viktor");
                if (beer != null) beer.gameObject.SetActive(false);   // Viktor drzi panaka (SindelLate)
                viktorGlass = BuildShotGlass();
                viktorGlass.position = Stage + DuelTable + new Vector3(0.36f, 0.8f, -0.05f);
                tumbleweed = BuildTumbleweed();
                tumbleweed.gameObject.SetActive(false);
                SetupDrink(BuildShotGlass(), DuelDrink, DuelDrink + 0.4f, DuelDrink + 0.95f, DuelDrink + 1.3f, 0.03f, 115f, 0.024f);
                sindelChair = false;
                sceneWhip = DuelCloseup;
                sceneLength = DuelLength;   // delsi nez ostatni - jinak se nestihne poznat, co se deje
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

    // Vrchol scenky (cas scenky) - kolem nej se zpomali (PotgIntroExtras); -1 = bez zpomaleni.
    float SceneClimax()
    {
        if (pova) return ImpactAt;                                         // Pova odpali Tomaska
        switch (scene)
        {
            case "Honza": return 1.7f;                                     // naloz vybuchne, Pova leti
            case "Viktor": return CaroSmash;                              // Caro do objektivu
            case "Mirek": return StormAt + (StormRun - 1.2f * StormAbility.LengthFactor) / StormSpeed;   // smrst u kamery
            case "Anna": return DavidHit;                                  // sipka zasahne Tomaska
            case "Sniper": return 1.82f;                                   // kopanec do kamery
            case "Max": return MaxCut;                                     // rez kosou
            case "Bard": return MosinoHit;                                 // basovy uder
            case "Sindel": return DuelShot;                                // vystrel do panaka
        }
        return -1f;
    }

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

    // ---------------- Honza: detonator ----------------

    // Honza zvedne levou ruku s detonatorem pred sebe (jako ve hre), palcem zmackne - naloz vybuchne (Launch 1,7 s) -
    // a detonator drzi v pesti i pri radosti.
    const float DetonatorRaise = 1.1f, DetonatorRaiseSeconds = 0.25f, DetonatorPress = 1.64f;
    Transform honzaDetonator;
    Quaternion detonatorFrame = Quaternion.identity;

    void HonzaDetonator()
    {
        if (time < DetonatorRaise || visual == null || visual.BodyAnimator == null) return;
        if (cheerLeft == null) cheerLeft = FirstPersonArms.BuildArm(visual.BodyAnimator, false);
        var arm = cheerLeft;
        if (arm == null || arm.upper == null) return;
        if (honzaDetonator == null)
        {
            honzaDetonator = HeldWeapons.BuildDetonator(root.transform);
            honzaDetonator.name = "HonzuvDetonator";
        }

        var body = visual.ModelRoot != null ? visual.ModelRoot : hero;
        Vector3 forward = body.forward, up = body.up, right = body.right;
        float k = Mathf.Clamp01((time - DetonatorRaise) / DetonatorRaiseSeconds);
        float raise = 1f - (1f - k) * (1f - k);
        // zmacknuti: kratke cuknuti pesti dopredu a dolu
        float d = time - DetonatorPress;
        float squeeze = d < 0f ? 0f : Mathf.Exp(-d * 14f) * Mathf.Min(1f, d * 50f);

        // ruka pred levym ramenem, kousek nad nim a ke stredu tela; pest: prsty dopredu, hrbet ruky vlevo
        Vector3 target = arm.upper.position + forward * 0.36f + up * 0.16f + right * 0.08f + (forward * 0.015f - up * 0.012f) * squeeze;
        detonatorFrame = Quaternion.LookRotation(forward, -right) * Quaternion.Euler(8f * squeeze, 0f, 0f);
        Quaternion upper = arm.upper.localRotation, lower = arm.lower.localRotation, wrist = arm.hand.localRotation;
        FirstPersonArms.SolveArmFrame(arm, target, detonatorFrame, body.TransformDirection(new Vector3(-0.6f, -1f, -0.2f)), false);
        if (raise < 0.999f)
        {
            arm.upper.localRotation = Quaternion.Slerp(upper, arm.upper.localRotation, raise);
            arm.lower.localRotation = Quaternion.Slerp(lower, arm.lower.localRotation, raise);
            arm.hand.localRotation = Quaternion.Slerp(wrist, arm.hand.localRotation, raise);
        }
        if (Once(3, time, DetonatorPress)) ProceduralSfx.Play(ProceduralSfx.Empty, Chest, 1f);   // cvak
    }

    // Detonator do leve dlane (po radosti, ktera ruku jeste zvedne).
    void HonzaDetonatorInHand()
    {
        if (honzaDetonator == null || cheerLeft == null) return;
        FirstPersonArms.PalmPose(cheerLeft, out var palm, out var handFrame);
        HeldWeapons.PlaceInPalm(honzaDetonator, palm, handFrame, Vector3.zero);
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
                if (arm == cheerLeft && honzaDetonator != null)   // (leva ruka plynule z drzeni detonatoru)
                    cheerFrame.rotation = Quaternion.Slerp(detonatorFrame, cheerFrame.rotation, cheer);
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
            grenade.Rotate(700f * stepDt, 400f * stepDt, 0f);
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
        // pod bilou: Viktor stoji tesne pred kamerou bez zbrane (Caro vytahne levou rukou - ViktorCaro)
        if (Once(3, t, ViktorStandAt))
        {
            hero.position = ViktorClose;
            hero.rotation = Facing(hero.position, ViktorCamera);
            held.HideWeapons();
        }
        if (Once(4, t, CaroOut)) ProceduralSfx.Play(ProceduralSfx.CaptureUnlock, Chest, 0.6f);   // "ta-da"
        if (Once(5, t, CaroWindup)) ProceduralSfx.Play(ProceduralSfx.Dash, Chest, 0.7f);
        if (Once(6, t, CaroSmash))
        {
            // uder Carem do objektivu: prasklina pres sklo, oblak kavy, kamera odleti
            shake = new Vector3(0.16f, 0.12f, 0f);
            ProceduralSfx.Play(ProceduralSfx.Hit, ViktorCamera, 1f);
            ProceduralSfx.Play(ProceduralSfx.BulletImpact, ViktorCamera, 1f);
            Fx.PlayGlobal(ProceduralSfx.Explosion, 0.3f);
            Fx.Sparks(ViktorCamera + Vector3.forward * 0.1f, new Color(0.45f, 0.28f, 0.12f));
            if (view != null)
            {
                Cracks(new Vector3(-0.01f, 0.005f, 0.16f), 23, 3.2f, 13);
                Cracks(new Vector3(0.03f, -0.02f, 0.161f), 24, 1.4f, 7);
            }
        }
        if (Once(7, t, CaroLand))
        {
            shake = new Vector3(0.08f, 0.06f, 0f);
            ProceduralSfx.Play(ProceduralSfx.Hit, ViktorCameraLand, 0.8f);
        }

        if (t < CaroSmash)
        {
            pos = ViktorCamera;
            Vector3 look = t < ViktorStandAt ? Chest + Vector3.up * 0.1f : Chest + Vector3.up * 0.35f;
            rot = Quaternion.LookRotation(look - pos);
            fov = t < ViktorStandAt ? 55f : 46f;
            return;
        }

        // kamera po uderu odleti obloukem dozadu, toci se a dopadne na bok - Viktor v dalce s Carem nad hlavou
        float f = Mathf.Clamp01((t - CaroSmash) / (CaroLand - CaroSmash));
        pos = Vector3.Lerp(ViktorCamera, ViktorCameraLand, f) + Vector3.up * Mathf.Sin(f * Mathf.PI) * 0.8f;
        Quaternion spin = Quaternion.LookRotation(Chest - ViktorCamera) * Quaternion.Euler(f * 520f, f * 160f, f * 700f);
        Quaternion lying = Quaternion.LookRotation(Chest + Vector3.up * 0.4f - ViktorCameraLand) * Quaternion.Euler(0f, 0f, ViktorLyingRoll);
        rot = Quaternion.Slerp(spin, lying, Smooth(Mathf.Clamp01((f - 0.55f) / 0.45f)));
        fov = 55f;
    }

    // ---------------- Viktor: Caro ----------------

    Vector3 ViktorClose => Ground(0.35f, -1.65f);                       // tesne pred kamerou (asi 75 cm)
    Vector3 ViktorCameraLand => Stage + new Vector3(0.9f, 0.08f, -6.3f);   // kam kamera dopadne
    const float ViktorLyingRoll = 75f;                                   // kamera lezi na boku
    const float CaroOut = 2.35f, CaroShown = 2.7f, CaroWindup = 3.3f, CaroSwing = 3.52f, CaroSmash = 3.6f, CaroRaised = 3.95f, CaroLand = 4.45f;
    Transform caro;
    FirstPersonArms.Arm viktorLeft;

    // Plechovka Caro (stejny model jako uprostred Viktorova leciveho pole - CaroHealCan), zmensena do ruky.
    // Mistne: z = nahoru (osa plechovky), -y = ceska strana etikety s logem Caro (ke kamere), x = do strany.
    const float CaroScale = 0.55f;                 // plechovka 22 x 28 cm -> asi 12 x 15 cm
    const float CaroRadius = 0.11f * CaroScale;
    const float CaroLabelYaw = 65f;                // natoceni, at je ke kamere ceska strana (logo na etikete u = 0,18)
    Transform BuildCaro()
    {
        var holder = new GameObject("Caro").transform;
        holder.SetParent(root.transform, false);
        var can = CaroHealCan.Create(holder).transform;
        can.localScale = Vector3.one * CaroScale;
        can.localRotation = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, CaroLabelYaw, 0f);
        can.localPosition = new Vector3(0f, 0f, -0.14f * CaroScale);
        foreach (var r in holder.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return holder;
    }

    // Po animaci: leva ruka s Carem - vytahne ji, ukaze u obliceje, naprahne se, uderi do objektivu, pak ji zvedne nad hlavu.
    void ViktorCaro()
    {
        if (caro == null || head == null || visual == null || visual.BodyAnimator == null || time < CaroOut) return;
        var anim = visual.BodyAnimator;
        if (viktorLeft == null) viktorLeft = FirstPersonArms.BuildArm(anim, false);
        var arm = viktorLeft;
        if (arm == null || arm.upper == null) return;
        if (!caro.gameObject.activeSelf) caro.gameObject.SetActive(true);

        Vector3 forward = hero.forward, right = hero.right, up = Vector3.up;
        Vector3 face = head.position + up * 0.03f;
        // zakladni drzeni: prsty nahoru, hrbet ruky vlevo, krabicka celem ke kamere
        Quaternion baseFrame = Quaternion.LookRotation(up, -right);

        Vector3 present = face + forward * 0.26f - right * 0.15f - up * 0.03f;
        Vector3 windup = face - right * 0.28f + up * 0.12f;
        Vector3 smash = ViktorCamera - forward * 0.06f;
        Vector3 raised = face + up * 0.45f - right * 0.12f + forward * 0.05f;

        Vector3 center;
        Quaternion frame;
        float lean = 0f;
        if (time < CaroWindup)
        {
            float wiggle = Mathf.Sin((time - CaroShown) * 6f) * 10f * Mathf.Clamp01((time - CaroShown) / 0.2f);
            center = present;
            frame = Quaternion.AngleAxis(wiggle, up) * baseFrame;
        }
        else if (time < CaroSwing)
        {
            float k = Smooth(Mathf.Clamp01((time - CaroWindup) / (CaroSwing - CaroWindup)));
            center = Vector3.Lerp(present, windup, k);
            frame = Quaternion.AngleAxis(-35f * k, right) * baseFrame;
            lean = -6f * k;
        }
        else if (time < CaroSmash)
        {
            float k = Mathf.Clamp01((time - CaroSwing) / (CaroSmash - CaroSwing));
            k *= k;
            center = Vector3.Lerp(windup, smash, k);
            frame = Quaternion.AngleAxis(Mathf.Lerp(-35f, 20f, k), right) * baseFrame;
            lean = Mathf.Lerp(-6f, 16f, k);
        }
        else
        {
            float k = Smooth(Mathf.Clamp01((time - CaroSmash) / (CaroRaised - CaroSmash)));
            center = Vector3.Lerp(smash, raised, k);
            frame = Quaternion.AngleAxis(20f * (1f - k), right) * baseFrame;
            lean = 16f * (1f - k);
        }
        if (Mathf.Abs(lean) > 0.01f)
        {
            BendBone(anim, HumanBodyBones.Spine, lean * 0.5f, right);
            BendBone(anim, HumanBodyBones.Chest, lean * 0.5f, right);
        }

        // dlan na boku krabicky (zleva), krabicka tedy vpravo od dlane
        var grip = new Vector3(-(CaroRadius + 0.012f), 0f, 0f);
        Vector3 fingers = frame * Vector3.forward, back = frame * Vector3.up;
        var boxRotation = Quaternion.LookRotation(fingers, Vector3.Cross(back, fingers));
        Vector3 palmAt = center + boxRotation * grip;
        float raise = Smooth(Mathf.Clamp01((time - CaroOut) / (CaroShown - CaroOut)));
        Quaternion upper0 = arm.upper.localRotation, lower0 = arm.lower.localRotation, hand0 = arm.hand.localRotation;
        FirstPersonArms.SolveArmFrame(arm, palmAt, frame, hero.TransformDirection(new Vector3(-0.6f, -1f, -0.3f)), time >= CaroSwing);
        if (raise < 0.999f)
        {
            arm.upper.localRotation = Quaternion.Slerp(upper0, arm.upper.localRotation, raise);
            arm.lower.localRotation = Quaternion.Slerp(lower0, arm.lower.localRotation, raise);
            arm.hand.localRotation = Quaternion.Slerp(hand0, arm.hand.localRotation, raise);
        }
        FirstPersonArms.PalmPose(arm, out var palm, out var handFrame);
        HeldWeapons.PlaceInPalm(caro, palm, handFrame, grip);

        // vyraz: samolibe usmev pri ukazovani, pri uderu zuby a stazene oboci, pak vitezny smich
        float show = Smooth(Mathf.Clamp01((time - CaroOut) / 0.3f)) * (1f - Smooth(Mathf.Clamp01((time - CaroWindup) / 0.15f)));
        float angry = Smooth(Mathf.Clamp01((time - CaroWindup) / 0.15f)) * (1f - Smooth(Mathf.Clamp01((time - CaroSmash - 0.15f) / 0.2f)));
        float win = Smooth(Mathf.Clamp01((time - CaroSmash - 0.1f) / 0.3f));
        heroFace.Set("mouthSmileLeft", 70f * show + 90f * win);
        heroFace.Set("mouthSmileRight", 40f * show + 90f * win);
        heroFace.Set("browOuterUpLeft", 60f * show);
        heroFace.Set("browDownLeft", 90f * angry);
        heroFace.Set("browDownRight", 90f * angry);
        heroFace.Set("mouthStretchLeft", 60f * angry);
        heroFace.Set("mouthStretchRight", 60f * angry);
        heroFace.Set("jawOpen", 25f * angry + 35f * win);
    }

    Vector3 grenadeFrom;

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

    // ---------------- David: uspavaci sipka, pomaly prichod a "psst" nad uspanym Tomaskem ----------------
    // (jako Anin POTG v Overwatchi) David levou rukou vystreli uspavaci pistolkou do Tomaska na gauci, pistolku spusti
    // k boku a pomalu k nemu dojde. Strih na pohled uspaneho Tomaska (zespodu, nakrivo): David se nad nim nakloni,
    // podiva se do kamery a da si prst na pusu.
    const float DavidAim = 0.12f, DavidShot = 0.55f, DavidHit = 0.65f, DavidLower = 1.0f;
    const float DavidWalkStart = 1.05f, DavidWalkEnd = 2.55f, DavidCut = 1.95f;
    const float DavidLeanStart = 2.45f, DavidLeanEnd = 3.0f, DavidShush = 3.05f, DavidShushIn = 0.3f;
    const float DavidFrom = 2.65f, DavidStop = 1.25f;   // jak daleko pred Tomaskem zacina a kde zastavi (m)
    const float DavidLeanAngle = 38f;                   // predklon trupu nad Tomaska (bez animace podrepu)
    const float DavidCrouchLean = 18f;                  // predklon navic k animaci podrepu
    const float DavidCrouchAt = 2.4f;                   // kdy zacne podrep (pri dojiti)
    // podrep z Mixama (Models/Animations/Mixamo/David/David_PotgCrouch.fbx), kdyz je
    AnimationClip DavidCrouchClip => definition != null && definition.animations != null ? definition.animations.potgCrouch : null;
    const float DavidDutch = 18f;                       // naklon kamery v pohledu uspaneho
    Transform davidPistol;
    FirstPersonArms.Arm davidLeft, davidRight;
    Vector3 davidFaceForwardLocal, davidFaceUpLocal;
    bool davidFaceAxes;
    UnityEngine.Light davidFaceLight;

    Vector3 TomasekForward => seatRotation * Vector3.forward;
    Vector3 DavidStart => seat + TomasekForward * DavidFrom + Vector3.up * RootY;
    Vector3 DavidEnd => seat + TomasekForward * DavidStop + Vector3.up * RootY;
    // oci uspaneho Tomaska (kamera): pred hrudi, nizko
    Vector3 SleeperView => seat + Vector3.up * (DavidCrouchClip != null ? 0.72f : 0.95f) + TomasekForward * 0.45f;
    Vector3 TomasekNeck => tomasekHead != null ? tomasekHead.position + Vector3.down * 0.3f : seat + Vector3.up * 1f;

    void David(float t, out Vector3 pos, out Quaternion rot, out float fov)
    {
        if (Once(0, t, 0f)) held.HideWeapons();   // puska pryc - jen uspavaci pistolka v leve ruce (DavidLate)
        if (Once(1, t, DavidShot))
        {
            ProceduralSfx.Play(CollectedAudioBank.Pick("david_sleep.fire", ProceduralSfx.Dash), Chest, 0.6f);
            Vector3 from = davidPistol != null ? davidPistol.position + davidPistol.forward * 0.18f : Chest;
            Fx.Tracer(from, TomasekNeck, new Color(0.6f, 0.4f, 1f), false);
        }
        if (Once(2, t, DavidHit))
        {
            sleepAt = t;
            Fx.Sparks(TomasekNeck, new Color(0.6f, 0.4f, 1f));
            ProceduralSfx.Play(ProceduralSfx.Hit, TomasekNeck, 0.6f);
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
            beerVelocity += Vector3.down * 9f * stepDt;
            beer.position += beerVelocity * stepDt;
            beer.Rotate(new Vector3(300f, 0f, 200f) * stepDt, Space.World);
            if (beer.position.y < Stage.y + 0.05f) { beerFlying = false; Fx.Sparks(beer.position, new Color(1f, 0.75f, 0.2f)); }
        }

        // pomaly prichod k Tomaskovi (chuze z animace podle rychlosti)
        float walk = Smooth(Mathf.Clamp01((t - DavidWalkStart) / (DavidWalkEnd - DavidWalkStart)));
        hero.SetPositionAndRotation(Vector3.Lerp(DavidStart, DavidEnd, walk), Facing(DavidStart, seat));

        if (DavidCrouchClip != null && Once(4, t, DavidCrouchAt))
            visual.PlayFullBody(DavidCrouchClip, 10f, false, true);   // podrep (jednou, drzi posledni snimek)
        if (Once(3, t, DavidShush + DavidShushIn * 0.6f))
            ProceduralSfx.Play(ProceduralSfx.Shush, head != null ? head.position : Chest, 0.7f);

        if (t < DavidCut)
        {
            // celek: David strili a vyrazi k Tomaskovi
            Vector3 look = Vector3.Lerp(seat + Vector3.up * 0.9f, Chest, 0.45f);
            pos = Vector3.Lerp(Stage + new Vector3(0.4f, 1.2f, -1.9f), Stage + new Vector3(0.6f, 1.1f, -1.4f), Smooth(Mathf.Clamp01(t / DavidCut)));
            rot = Quaternion.LookRotation(look - pos);
            fov = 50f;
            return;
        }

        // pohled uspaneho Tomaska: zespodu a nakrivo; David prichazi, nakloni se a diva se do kamery
        if (davidFaceLight == null)
            davidFaceLight = PointLight(SleeperView + Vector3.up * 0.25f, new Color(0.78f, 0.72f, 1f), 1.6f, 3f);
        Vector3 face = head != null ? head.position + Vector3.up * 0.03f : Chest + Vector3.up * 0.5f;
        Vector3 target = Vector3.Lerp(Chest + Vector3.up * 0.1f, face, Smooth(Mathf.Clamp01((t - (DavidWalkEnd - 0.6f)) / 0.9f)));
        Vector3 sway = new Vector3(Mathf.PerlinNoise(t * 0.7f, 0.1f) - 0.5f, Mathf.PerlinNoise(0.3f, t * 0.6f) - 0.5f, 0f) * 0.03f;
        pos = SleeperView + sway;
        rot = Quaternion.LookRotation(target - pos) * Quaternion.Euler(0f, 0f, DavidDutch);
        fov = Mathf.Lerp(54f, 44f, Smooth(Mathf.Clamp01((t - DavidCut) / 2.2f)));
    }

    // Po animaci: predklon, pistolka v leve ruce, pohled do kamery, prst na pusu a vyraz.
    void DavidLate()
    {
        if (visual == null || visual.BodyAnimator == null || head == null) return;
        var anim = visual.BodyAnimator;
        Vector3 forward = hero.forward, right = hero.right, up = Vector3.up;

        // osy obliceje v prostoru hlavy (ze zakladni pozy na zacatku - az po prvnich snimcich animace)
        if (!davidFaceAxes && time > 0.1f)
        {
            var inverse = Quaternion.Inverse(head.rotation);
            davidFaceForwardLocal = inverse * forward;
            davidFaceUpLocal = inverse * up;
            davidFaceAxes = true;
        }

        // predklon trupu nad Tomaska
        float lean = Smooth(Mathf.Clamp01((time - DavidLeanStart) / (DavidLeanEnd - DavidLeanStart)));
        if (lean > 0f)
        {
            float angle = DavidCrouchClip != null ? DavidCrouchLean : DavidLeanAngle;
            BendBone(anim, HumanBodyBones.Spine, angle * 0.38f * lean, right);
            BendBone(anim, HumanBodyBones.Chest, angle * 0.37f * lean, right);
            BendBone(anim, HumanBodyBones.UpperChest, angle * 0.25f * lean, right);
        }

        // hlava: v predklonu se diva do kamery (oci uspaneho) a trochu ji nakloni na stranu
        if (lean > 0f)
        {
            Vector3 faceNow = head.rotation * davidFaceForwardLocal;
            Vector3 toCamera = (SleeperView - head.position).normalized;
            Quaternion look = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(faceNow, toCamera), 0.85f * lean);
            head.rotation = Quaternion.AngleAxis(-10f * lean, toCamera) * look * head.rotation;
        }

        // leva ruka: uspavaci pistolka - miri na Tomaska, vystreli (cuknuti), pak ji drzi u boku hlavni dolu
        if (davidLeft == null) davidLeft = FirstPersonArms.BuildArm(anim, false);
        if (davidPistol == null)
        {
            davidPistol = HeldWeapons.BuildDartPistol(root.transform);
            davidPistol.name = "DavidovaPistolka";
        }
        if (davidLeft != null && davidLeft.upper != null)
        {
            float raise = Smooth(Mathf.Clamp01((time - DavidAim) / 0.25f));
            float d = time - DavidShot;
            float kick = d > 0f && d < 0.2f ? Mathf.Sin(d / 0.2f * Mathf.PI) : 0f;
            float lower = Smooth(Mathf.Clamp01((time - DavidLower) / 0.4f));
            Vector3 shoulder = davidLeft.upper.position;
            Vector3 aim = (TomasekNeck - shoulder).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, aim).normalized;
            Vector3 aimAt = shoulder + aim * 0.42f + up * 0.04f + side * 0.08f - aim * 0.05f * kick;
            Quaternion aimFrame = Quaternion.LookRotation(aim, -side) * Quaternion.Euler(-12f * kick, 0f, 0f);
            Vector3 hangAt = shoulder - up * 0.5f + forward * 0.1f - right * 0.03f;
            Quaternion hangFrame = Quaternion.LookRotation((forward * 0.35f - up).normalized, -right);   // hlaven sikmo dolu
            var frame = Quaternion.Slerp(aimFrame, hangFrame, lower);
            SolveDavidArm(davidLeft, Vector3.Lerp(aimAt, hangAt, lower), frame, hero.TransformDirection(new Vector3(-0.6f, -1f, -0.2f)), raise);
            FirstPersonArms.PalmPose(davidLeft, out var palm, out var handFrame);
            HeldWeapons.PlaceInPalm(davidPistol, palm, handFrame, HeldWeapons.DartGrip);
        }

        // prava ruka: ukazovacek na rty ("psst"), ostatni prsty v pest
        float shush = Smooth(Mathf.Clamp01((time - DavidShush) / DavidShushIn));
        if (shush > 0f)
        {
            if (davidRight == null) davidRight = FirstPersonArms.BuildArm(anim, true);
            if (davidRight != null && davidRight.upper != null)
                DavidShushHand(anim, shush);
        }

        // vyraz: privrene oci a lisacky usmev, pri "psst" spulene rty; mrknuti na konci
        float blink = Mathf.Clamp01(1f - Mathf.Abs(time - 4.35f) / 0.08f);
        heroFace.Set("eyeBlinkLeft", Mathf.Max(28f * lean, 100f * blink));
        heroFace.Set("eyeBlinkRight", Mathf.Max(28f * lean, 100f * blink));
        heroFace.Set("eyeSquintLeft", 35f * lean);
        heroFace.Set("eyeSquintRight", 35f * lean);
        heroFace.Set("browDownLeft", 25f * lean);
        heroFace.Set("browOuterUpRight", 35f * lean);
        heroFace.Set("mouthSmileLeft", 40f * lean * (1f - 0.6f * shush));
        heroFace.Set("mouthPucker", 70f * shush);
        heroFace.Set("mouthFunnel", 15f * shush);
    }

    void BendBone(Animator anim, HumanBodyBones bone, float angle, Vector3 axis)
    {
        var t = anim.GetBoneTransform(bone);
        if (t != null) t.rotation = Quaternion.AngleAxis(angle, axis) * t.rotation;
    }

    // Ruka do cile (dlan, osy ruky 'frame'); weight < 1 = jeste se zveda z pozy animace.
    static void SolveDavidArm(FirstPersonArms.Arm arm, Vector3 palmAt, Quaternion frame, Vector3 pole, float weight)
    {
        Quaternion upper = arm.upper.localRotation, lower = arm.lower.localRotation, wrist = arm.hand.localRotation;
        FirstPersonArms.SolveArmFrame(arm, palmAt, frame, pole, false);
        if (weight >= 0.999f) return;
        arm.upper.localRotation = Quaternion.Slerp(upper, arm.upper.localRotation, weight);
        arm.lower.localRotation = Quaternion.Slerp(lower, arm.lower.localRotation, weight);
        arm.hand.localRotation = Quaternion.Slerp(wrist, arm.hand.localRotation, weight);
    }

    // Rty Davida: podle kosti oci (stred oci, kousek dolu a dopredu), jinak odhad od kosti hlavy.
    bool DavidLips(Animator anim, Vector3 faceForward, Vector3 faceUp, out Vector3 lips)
    {
        var leftEye = anim.GetBoneTransform(HumanBodyBones.LeftEye);
        var rightEye = anim.GetBoneTransform(HumanBodyBones.RightEye);
        if (leftEye != null && rightEye != null)
            lips = (leftEye.position + rightEye.position) * 0.5f - faceUp * DavidLipsBelowEyes + faceForward * DavidLipsForward;
        else
            lips = head.position + faceForward * 0.12f + faceUp * 0.03f;
        return true;
    }

    static readonly HumanBodyBones[][] DavidRightFingers =
    {
        new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal },
        new[] { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
        new[] { HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal },
        new[] { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal },
    };
    // ohyb clanku k dlani (stupne): ukazovacek natazeny, ostatni v pest
    static readonly float[][] ShushCurl = { new[] { 0f, 5f, 5f }, new[] { 75f, 85f, 45f }, new[] { 80f, 85f, 45f }, new[] { 85f, 80f, 45f } };
    const float DavidLipsBelowEyes = 0.075f, DavidLipsForward = 0.035f;   // rty vuci stredu oci (m)

    // "Psst": ruka z profilu (dlan doleva, hrbet ruky doprava), prsty nahoru, ukazovacek natazeny pres rty, ostatni
    // v pest pred nim. Ruka se dorovna tak, aby kloub ukazovacku (zacatek posledniho clanku) byl na rtech.
    void DavidShushHand(Animator anim, float weight)
    {
        var arm = davidRight;
        Vector3 faceForward = head.rotation * davidFaceForwardLocal, faceUp = head.rotation * davidFaceUpLocal;
        Vector3 faceRight = Vector3.Cross(faceUp, faceForward).normalized;
        DavidLips(anim, faceForward, faceUp, out var lips);
        Vector3 lipTarget = lips + faceForward * 0.012f;   // prst tesne pred rty

        // puvodni poza z animace (pro nabeh)
        Quaternion upper0 = arm.upper.localRotation, lower0 = arm.lower.localRotation, hand0 = arm.hand.localRotation;
        var fingerBones = new Transform[DavidRightFingers.Length][];
        var fingerRest = new Quaternion[DavidRightFingers.Length][];
        for (int f = 0; f < DavidRightFingers.Length; f++)
        {
            fingerBones[f] = new Transform[3];
            fingerRest[f] = new Quaternion[3];
            for (int b = 0; b < 3; b++)
            {
                fingerBones[f][b] = anim.GetBoneTransform(DavidRightFingers[f][b]);
                if (fingerBones[f][b] != null) fingerRest[f][b] = fingerBones[f][b].localRotation;
            }
        }

        var frame = Quaternion.LookRotation(faceUp, (faceRight + faceForward * 0.35f).normalized);
        Vector3 pole = hero.TransformDirection(new Vector3(0.6f, -1f, -0.3f));
        Vector3 palmAt = lips - faceUp * 0.1f + faceForward * 0.04f + faceRight * 0.03f;
        var indexJoint = fingerBones[0][2];
        for (int i = 0; i < 3; i++)
        {
            FirstPersonArms.SolveArmFrame(arm, palmAt, frame, pole, false);
            FirstPersonArms.PalmPose(arm, out _, out var handFrame);
            PoseFingers(fingerBones, handFrame, ShushCurl);
            if (indexJoint == null) break;
            palmAt += lipTarget - indexJoint.position;
        }

        if (weight >= 0.999f) return;
        arm.upper.localRotation = Quaternion.Slerp(upper0, arm.upper.localRotation, weight);
        arm.lower.localRotation = Quaternion.Slerp(lower0, arm.lower.localRotation, weight);
        arm.hand.localRotation = Quaternion.Slerp(hand0, arm.hand.localRotation, weight);
        for (int f = 0; f < fingerBones.Length; f++)
            for (int b = 0; b < 3; b++)
                if (fingerBones[f][b] != null)
                    fingerBones[f][b].localRotation = Quaternion.Slerp(fingerRest[f][b], fingerBones[f][b].localRotation, weight);
    }

    // Prsty presne (ne pricitat k animaci): kazdy clanek natocit podel prstu ruky (z) a ohnout k dlani o dany uhel.
    // Posledni clanek nema kost za sebou - smer bere ze stejne osy jako predchozi clanek.
    static void PoseFingers(Transform[][] fingers, Quaternion handFrame, float[][] curl)
    {
        Vector3 along = handFrame * Vector3.forward;
        Vector3 axis = handFrame * Vector3.right;   // ohyb k dlani (hrbet ruky = +y)
        for (int f = 0; f < fingers.Length; f++)
        {
            var bones = fingers[f];
            float total = 0f;
            Vector3 axisLocal = Vector3.zero;
            for (int b = 0; b < 3; b++)
            {
                var bone = bones[b];
                if (bone == null) break;
                total += curl[f][b];
                Vector3 current;
                if (b < 2 && bones[b + 1] != null)
                {
                    current = bones[b + 1].position - bone.position;
                    if (current.sqrMagnitude < 1e-8f) continue;
                }
                else if (axisLocal != Vector3.zero) current = bone.rotation * axisLocal;
                else continue;
                Vector3 wanted = Quaternion.AngleAxis(total, axis) * along;
                bone.rotation = Quaternion.FromToRotation(current, wanted) * bone.rotation;
                if (b < 2 && bones[b + 1] != null)
                    axisLocal = Quaternion.Inverse(bone.rotation) * (bones[b + 1].position - bone.position).normalized;
            }
        }
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
        // po kopanci: kamera lezi na zemi, chvili nikdo; pak Kondulic prijde a nakloni se nad ni
        if (t >= KonWalkStart)
        {
            float walk = Smooth(Mathf.Clamp01((t - KonWalkStart) / (KonArrive - KonWalkStart)));
            Vector3 stand = KonStand;
            hero.position = Vector3.Lerp(heroTo, stand, walk);
            hero.rotation = walk < 0.9f ? Facing(heroTo, stand) : Quaternion.Slerp(Facing(heroTo, stand), Facing(stand, KonCamLand), (walk - 0.9f) / 0.1f);
        }

        Vector3 kickPos = Stage + new Vector3(0.7f, 0.9f, -1.6f);
        if (t < KonKick)
        {
            // z nizka nahoru na sloup, pak sleduje prelet
            Vector3 look = Vector3.Lerp(heroFrom + Vector3.up * 1.1f, Chest, Smooth(k));
            pos = kickPos;
            rot = Quaternion.LookRotation(look - pos);
            fov = Mathf.Lerp(40f, 55f, Smooth(k));
            return;
        }

        // kopanec: kamera odleti obloukem dozadu, toci se a spadne na zem objektivem nahoru (do stropu)
        float f = Mathf.Clamp01((t - KonKick) / (KonLand - KonKick));
        Vector3 fallPos = Vector3.Lerp(kickPos, KonCamLand, f * f) + Vector3.up * Mathf.Sin(f * Mathf.PI) * 0.35f;
        Quaternion kicked = Quaternion.LookRotation(Chest - kickPos);
        Quaternion lying = Quaternion.LookRotation(Vector3.up, Flat(heroTo - KonCamLand)) * Quaternion.Euler(0f, 0f, KonLyingRoll);
        pos = fallPos;
        rot = Quaternion.Slerp(kicked, lying, Smooth(f)) * Quaternion.Euler(0f, 0f, 200f * Mathf.Sin(f * Mathf.PI));
        fov = 62f;
        if (Once(4, t, KonLand))
        {
            shake = new Vector3(0.1f, 0.08f, 0f);
            ProceduralSfx.Play(ProceduralSfx.Hit, KonCamLand, 0.9f);
            ProceduralSfx.Play(ProceduralSfx.BulletImpact, KonCamLand, 1f);
            if (view != null) SpiderWebCrack();
            konLight = PointLight(KonCamLand + Vector3.up * 0.35f, new Color(0.7f, 0.75f, 1f), 1.4f, 3.5f);   // svetlo zespodu na oblicej
        }
        // puska pryc, dokud ho neni videt (kamera kouka do stropu) - kameru pak zveda obema rukama
        if (Once(5, t, KonLand + 0.05f)) held.HideWeapons();

        // zvedne kameru: ze zeme do rukou a pred oblicej - detail zamraceneho obliceje pres prasklé sklo
        if (t >= KonGrab && head != null)
        {
            if (Once(6, t, KonGrab)) ProceduralSfx.Play(ProceduralSfx.Hit, KonCamLand, 0.45f);
            float lift = Smooth(Mathf.Clamp01((t - KonGrab) / (KonLifted - KonGrab)));
            Vector3 forward = Flat(hero.forward).normalized;
            Vector3 feet = hero.position - Vector3.up * RootY;
            Vector3 grab = feet + forward * 0.45f + Vector3.up * 0.62f;   // kam az ruce v predklonu dosahnou
            Vector3 face = head.position + Vector3.up * 0.03f;
            Vector3 hold = face + forward * KonHoldDistance + Vector3.down * 0.05f;
            float u = 1f - lift;
            Vector3 carried = u * u * KonCamLand + 2f * u * lift * grab + lift * lift * hold;
            Vector3 handheld = new Vector3(Mathf.PerlinNoise(t * 3f, 0.2f) - 0.5f, Mathf.PerlinNoise(0.7f, t * 2.6f) - 0.5f, 0f) * 0.012f * lift;
            Quaternion held2 = Quaternion.LookRotation(face - hold) * Quaternion.Euler(0f, 0f, KonHoldRoll + 3f * Mathf.Sin(t * 1.7f));
            pos = carried + handheld;
            rot = Quaternion.Slerp(lying, held2, lift);
            fov = Mathf.Lerp(62f, 48f, lift);
            if (konLight != null) konLight.transform.position = Vector3.Lerp(KonCamLand + Vector3.up * 0.35f, pos + Vector3.down * 0.25f + forward * 0.05f, lift);
        }
    }

    // Kondulic: po kopanci kamera na zemi (KonCamLand), on dojde k ni (KonStand) a nakloni se nad ni.
    const float KonKick = 1.82f, KonLand = 2.3f, KonWalkStart = 2.4f, KonArrive = 3.0f, KonLeanStart = 2.9f, KonLeanEnd = 3.15f;
    const float KonReach = 3.1f, KonGrab = 3.32f, KonLifted = 3.75f;   // natahne ruce, kamera v rukou, pred obliceji
    const float KonReachLean = 55f, KonHoldLean = 6f;   // predklon pri sahani a pri drzeni kamery
    const float KonHoldDistance = 0.42f;                // kamera v rukou pred obliceji (m)
    const float KonHoldRoll = -6f;                      // naklon kamery v rukou
    UnityEngine.Light konLight;
    FirstPersonArms.Arm konLeft, konRight;
    const float KonLyingRoll = 12f;      // naklon lezici kamery
    const float KonStandOff = 0.5f;      // jak daleko od kamery stoji (m)
    const float KonLeanAngle = 28f;      // predklon nad kamerou
    Vector3 KonCamLand => Stage + new Vector3(0.75f, 0.06f, -2.0f);
    Vector3 KonStand
    {
        get
        {
            Vector3 land = new Vector3(KonCamLand.x, Stage.y, KonCamLand.z);
            Vector3 from = new Vector3(heroTo.x, Stage.y, heroTo.z);
            return land + (from - land).normalized * KonStandOff + Vector3.up * RootY;
        }
    }
    Vector3 konFaceForwardLocal;
    bool konFaceAxes;

    // Pavucina prasklin pres cele sklo: paprsky ze stredu a dva kruhy kratkych prasklin.
    void SpiderWebCrack()
    {
        var center = new Vector3(0.008f, 0.006f, 0.16f);
        Cracks(center, 41, 4.2f, 14);
        Cracks(center, 42, 2.2f, 9);
        var group = new GameObject("Pavucina").transform;
        group.SetParent(view.transform, false);
        group.localPosition = center + new Vector3(0f, 0f, -0.004f);
        sceneObjects.Add(group.gameObject);
        foreach (float radius in new[] { 0.022f, 0.045f, 0.075f })
        {
            int pieces = Mathf.RoundToInt(radius * 260f);
            for (int i = 0; i < pieces; i++)
            {
                float a0 = (i + 0.15f) * 360f / pieces, a1 = (i + 0.85f) * 360f / pieces;
                Vector3 p0 = Quaternion.Euler(0f, 0f, a0) * Vector3.right * radius * (0.9f + 0.2f * Mathf.Sin(i * 3.7f));
                Vector3 p1 = Quaternion.Euler(0f, 0f, a1) * Vector3.right * radius * (0.9f + 0.2f * Mathf.Cos(i * 2.3f));
                var seg = Glow(group, PrimitiveType.Cube, Vector3.zero, new Vector3((p1 - p0).magnitude, 0.0006f, 0.0004f), new Color(0.92f, 0.95f, 1f), 0.6f);
                seg.SetParent(group, false);
                seg.localPosition = (p0 + p1) * 0.5f;
                seg.localRotation = Quaternion.FromToRotation(Vector3.right, p1 - p0);
                seg.localScale = new Vector3((p1 - p0).magnitude, 0.0006f, 0.0004f);
                seg.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }

    // Kondulic se pri kopu a v detailu mraci: stazene oboci, privrene oci, ohrnuty nos, sevrene rty.
    void KondulicFrown()
    {
        // nad lezici kamerou: predklon a pohled dolu do objektivu
        var anim = visual != null ? visual.BodyAnimator : null;
        if (anim != null && head != null)
        {
            if (!konFaceAxes && time > 0.1f)
            {
                konFaceForwardLocal = Quaternion.Inverse(head.rotation) * hero.forward;
                konFaceAxes = true;
            }
            float lean = Smooth(Mathf.Clamp01((time - KonLeanStart) / (KonLeanEnd - KonLeanStart)));
            float reach = Smooth(Mathf.Clamp01((time - KonReach) / (KonGrab - KonReach)));
            float lift = Smooth(Mathf.Clamp01((time - KonGrab) / (KonLifted - KonGrab)));
            if (lean > 0f && konFaceAxes)
            {
                // predklon nad kamerou, pri sahani hloubeji, s kamerou v rukou zase skoro rovne
                float angle = Mathf.Lerp(Mathf.Lerp(KonLeanAngle * lean, KonReachLean, reach), KonHoldLean, lift);
                BendBone(anim, HumanBodyBones.Spine, angle * 0.4f, hero.right);
                BendBone(anim, HumanBodyBones.Chest, angle * 0.35f, hero.right);
                BendBone(anim, HumanBodyBones.UpperChest, angle * 0.25f, hero.right);

                // obe ruce ke kamere (na zemi natazene k ni), pak ji drzi z boku
                if (reach > 0f)
                {
                    if (konLeft == null) konLeft = FirstPersonArms.BuildArm(anim, false);
                    if (konRight == null) konRight = FirstPersonArms.BuildArm(anim, true);
                    Vector3 cam = view != null ? view.transform.position : KonCamLand;
                    Vector3 body = cam + hero.forward * 0.04f;   // telo kamery za objektivem
                    foreach (var arm in new[] { konLeft, konRight })
                    {
                        if (arm == null || arm.upper == null) continue;
                        float side = arm.side;
                        Vector3 grip = body + hero.right * side * 0.085f;
                        var frame = Quaternion.LookRotation(hero.forward, hero.right * side);   // prsty dopredu, dlane ke kamere
                        SolveDavidArm(arm, grip, frame, hero.TransformDirection(new Vector3(0.6f * side, -1f, -0.3f)), reach);
                    }
                }

                // pohled do kamery (na zemi, pak v rukou)
                Vector3 faceNow = head.rotation * konFaceForwardLocal;
                Vector3 target = view != null ? view.transform.position : KonCamLand;
                Vector3 toCamera = (target - head.position).normalized;
                head.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(faceNow, toCamera), 0.85f * lean) * head.rotation;
            }
        }

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
        MaxSnort();
    }

    // ---------------- Max v detailu: stribrny tacek s lajnou, snupnuti bankovkou, blazenost ----------------
    // Po rezu (zbran pryc) vezme pravou rukou stribrny tacek s bilou lajnou a zvedne ho pod oblicej, levou rukou drzi
    // srolovanou bankovku u nosu. Pri snupnuti se tacek posouva, spodni konec bankovky projede lajnu a lajna mizi.
    // Pak tacek a bankovku sklopi a blazene kyva hlavou nahoru a dolu (privrene oci, usmev).
    const float TrayUp = 2.2f, SnortStart = 2.65f, SnortEnd = 3.2f, TrayDown = 3.5f, BlissStart = 3.35f;
    const float LineHalf = 0.065f, BillLength = 0.09f;
    Transform tray, trayLine, bill;
    FirstPersonArms.Arm trayArm, billArm;
    Vector3 maxFaceF, maxFaceU, maxFaceR;
    bool maxFaceAxes;

    void BuildMaxSnort()
    {
        tray = new GameObject("StribrnyTacek").transform;
        tray.SetParent(root.transform, false);
        var dish = Prim(PrimitiveType.Cylinder, tray, Vector3.zero, new Vector3(0.24f, 0.004f, 0.24f), new Color(0.84f, 0.86f, 0.9f));
        dish.transform.localPosition = Vector3.zero;
        var silver = dish.GetComponent<Renderer>().sharedMaterial;
        if (silver.HasProperty("_Metallic")) silver.SetFloat("_Metallic", 0.95f);
        if (silver.HasProperty("_Smoothness")) silver.SetFloat("_Smoothness", 0.85f);
        var rim = Prim(PrimitiveType.Cylinder, tray, Vector3.zero, new Vector3(0.25f, 0.007f, 0.25f), new Color(0.7f, 0.72f, 0.76f));
        rim.transform.localPosition = new Vector3(0f, -0.004f, 0f);
        trayLine = Prim(PrimitiveType.Cube, tray, Vector3.zero, new Vector3(LineHalf * 2f, 0.004f, 0.012f), Color.white).transform;
        trayLine.localPosition = new Vector3(0f, 0.006f, 0f);
        bill = BuildBanknote();
        foreach (var r in tray.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (var r in bill.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tray.gameObject.SetActive(false);
        bill.gameObject.SetActive(false);
        silentCloseup = true;   // v detailu snupe - bez hlasky
    }

    void MaxSnort()
    {
        if (tray == null || bill == null || head == null) return;
        bool show = time >= TrayUp;
        if (tray.gameObject.activeSelf != show) { tray.gameObject.SetActive(show); bill.gameObject.SetActive(show); }
        if (!show) return;
        if (!maxFaceAxes)
        {
            // osy obliceje v prostoru hlavy (tacek i bankovka sleduji natoceni hlavy z animace)
            var inverse = Quaternion.Inverse(head.rotation);
            maxFaceF = inverse * hero.forward;
            maxFaceU = inverse * hero.up;
            maxFaceR = inverse * hero.right;
            maxFaceAxes = true;
        }

        float raise = Smooth(Mathf.Clamp01((time - TrayUp) / (SnortStart - TrayUp)));
        float snort = Mathf.Clamp01((time - SnortStart) / (SnortEnd - SnortStart));
        float lower = Smooth(Mathf.Clamp01((time - SnortEnd) / (TrayDown - SnortEnd)));
        float bliss = Smooth(Mathf.Clamp01((time - BlissStart) / 0.4f));
        float hold = Smooth(Mathf.Clamp01((time - TrayUp) / 0.25f));

        // hlava: k tacku se skloni, v blazenosti pomale kyvani nahoru a dolu (mirne zaklonena)
        float lean = 16f * Smooth(Mathf.Clamp01((time - (SnortStart - 0.2f)) / 0.2f)) * (1f - lower);
        float nod = bliss * (11f * Mathf.Sin((time - BlissStart) * Mathf.PI * 2f * 1.2f) - 7f);
        head.rotation = Quaternion.AngleAxis(lean + nod, head.rotation * maxFaceR) * head.rotation;
        Vector3 F = head.rotation * maxFaceF, U = head.rotation * maxFaceU, R = head.rotation * maxFaceR;
        Vector3 nose = head.position + F * 0.115f + U * 0.035f;

        // telo (tacek drzi vodorovne): lajna podel prave osy tela
        Vector3 bodyF = hero.forward, bodyU = Vector3.up, bodyR = hero.right;
        Vector3 billDir = (-U * 0.85f + F * 0.5f).normalized;   // od nosu sikmo dolu dopredu
        Vector3 billTipAtNose = nose + billDir * BillLength;      // spodni konec bankovky (na lajne)
        float sweep = time < SnortStart ? LineHalf - 0.01f : Mathf.Lerp(LineHalf - 0.01f, -LineHalf + 0.01f, snort);
        Vector3 trayAtFace = billTipAtNose - bodyU * 0.008f - bodyR * sweep;
        Vector3 trayLow = head.position - bodyU * 0.55f + bodyF * 0.3f + bodyR * 0.12f;
        Vector3 trayPos = Vector3.Lerp(Vector3.Lerp(trayLow, trayAtFace, raise), trayLow + bodyU * 0.05f, lower);
        tray.SetPositionAndRotation(trayPos, Quaternion.LookRotation(bodyF, bodyU));

        // lajna mizi od konce, kudy uz bankovka projela
        float end = time < SnortStart ? LineHalf : sweep;
        float remaining = Mathf.Max(0f, end + LineHalf);
        trayLine.gameObject.SetActive(remaining > 0.004f && time < SnortEnd + 0.05f);
        trayLine.localScale = new Vector3(Mathf.Max(0.001f, remaining), 0.004f, 0.012f);
        trayLine.localPosition = new Vector3((end - LineHalf) * 0.5f, 0.006f, 0f);

        // bankovka: pri snupnuti horni konec u nosu, predtim zvedana s tackem, pak dolu
        Vector3 billAtNose = nose + billDir * (BillLength * 0.5f);
        Vector3 billLow = trayLow - bodyR * 0.3f + bodyU * 0.08f;
        Vector3 billPos = Vector3.Lerp(Vector3.Lerp(billLow, billAtNose, raise), billLow + bodyU * 0.05f, lower);
        Vector3 billAxis = Vector3.Slerp(Vector3.Slerp(bodyU, -billDir, raise), bodyU, lower);
        bill.SetPositionAndRotation(billPos, Quaternion.FromToRotation(Vector3.up, billAxis));

        // ruce: prava drzi tacek zespodu (dlan nahoru), leva drzi bankovku konecky prstu z leve strany
        if (visual.BodyAnimator != null)
        {
            if (trayArm == null) trayArm = FirstPersonArms.BuildArm(visual.BodyAnimator, true);
            if (billArm == null) billArm = FirstPersonArms.BuildArm(visual.BodyAnimator, false);
            SolveHeld(trayArm, trayPos - bodyU * 0.006f + bodyR * 0.03f, Quaternion.LookRotation(bodyF, -bodyU), bodyR * 0.6f - bodyU, hold);
            SolveHeld(billArm, billPos - R * 0.028f, Quaternion.LookRotation(F, -R), -bodyR * 0.6f - bodyU, hold);
        }

        // oblicej: pri snupnuti nos a privrene oci, v blazenosti usmev a pomale zavirani oci
        float sniff = Mathf.Clamp01((time - SnortStart) / 0.12f) * (1f - Mathf.Clamp01((time - SnortEnd) / 0.15f));
        heroFace.Set("mouthFunnel", 0f);
        heroFace.Set("noseSneerLeft", 90f * sniff);
        heroFace.Set("noseSneerRight", 90f * sniff);
        heroFace.Set("eyeBlinkLeft", Mathf.Max(70f * sniff, 55f * bliss));
        heroFace.Set("eyeBlinkRight", Mathf.Max(70f * sniff, 55f * bliss));
        heroFace.Set("mouthSmileLeft", 85f * bliss);
        heroFace.Set("mouthSmileRight", 80f * bliss);
        heroFace.Set("cheekSquintLeft", 45f * bliss);
        heroFace.Set("cheekSquintRight", 45f * bliss);
        heroFace.Set("browInnerUp", 45f * bliss);
        heroFace.Set("browDownLeft", 0f);
        heroFace.Set("browDownRight", 0f);
        if (Once(11, time, SnortStart + 0.05f)) ProceduralSfx.Play(ProceduralSfx.Splash, head.position, 0.25f);   // "sssnf"
        if (Once(12, time, SnortStart + 0.35f)) ProceduralSfx.Play(ProceduralSfx.Splash, head.position, 0.2f);
    }

    // Paze k bodu s danym natocenim ruky (IK po animaci), plynule podle 'weight'.
    static void SolveHeld(FirstPersonArms.Arm arm, Vector3 grip, Quaternion frame, Vector3 pole, float weight)
    {
        if (arm == null || weight <= 0f) return;
        Quaternion upper = arm.upper.localRotation, lower = arm.lower.localRotation, wrist = arm.hand.localRotation;
        FirstPersonArms.SolveArmFrame(arm, grip, frame, pole, false);
        if (weight >= 0.999f) return;
        arm.upper.localRotation = Quaternion.Slerp(upper, arm.upper.localRotation, weight);
        arm.lower.localRotation = Quaternion.Slerp(lower, arm.lower.localRotation, weight);
        arm.hand.localRotation = Quaternion.Slerp(wrist, arm.hand.localRotation, weight);
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
            hero.rotation = Quaternion.Slerp(hero.rotation, Facing(hero.position, camPos), 1f - Mathf.Exp(-14f * stepDt));
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

    // ---------------- Sindel: souboj za poledne ----------------

    Vector3 SindelSeat => Stage + new Vector3(-0.85f, 0f, 2.2f) + Vector3.up * RootY;
    static readonly Vector3 DuelTable = new Vector3(0f, 0f, 2.2f);
    const float DuelLength = 7.2f;                                        // cely uvod (ostatni 5 s)
    const float WeedStart = 0.05f, WeedEnd = 1.7f;                         // chuchvalec travy pres zaber
    const float EyesSindel = 1.7f, EyesViktor = 2.4f, DuelWide = 3.1f;     // detaily oci
    const float GlassRaise = 3.1f, GlassAtMouth = 3.65f;                  // Viktor zvedne panaka k puse
    const float DuelDraw = 3.5f, DuelShot = 3.7f;                         // Sindel tasi a vystreli
    const float DuelMedium = 4.3f;                                        // kamera na Sindela
    const float SmokeUp = 4.35f, SmokeBlow = 4.75f, SpinStart = 4.95f, SpinEnd = 5.5f, Holstered = 5.75f;
    const float DuelCloseup = 5.75f, DuelDrink = 5.85f;                   // detail obliceje a panak
    bool sindelChair;
    Transform duelPistol, viktorGlass, tumbleweed;
    Vector3 duelPistolGrip, duelMuzzleLocal;
    FirstPersonArms.Arm duelArm, viktorArm;

    void Sindel(float t, out Vector3 pos, out Quaternion rot, out float fov)
    {
        if (tomasek != null && tomasek.go != null) tomasek.Pose(clips.sitIdle, t);   // Viktor sedi
        if (Once(0, t, 0f))
        {
            if (clips != null && clips.sitIdle != null) visual.PlayFullBody(clips.sitIdle, 10f, true);   // sedi cely souboj
            held.HideWeapons();   // pistoli tasi z pouzdra (SindelLate)
        }
        hero.SetPositionAndRotation(SindelSeat, Quaternion.Euler(0f, 90f, 0f));

        // chuchvalec travy se kutali pres zaber, hvizdnuti
        if (Once(1, t, WeedStart + 0.1f)) Fx.PlayGlobal(ProceduralSfx.WesternWhistle, 0.6f);
        if (tumbleweed != null)
        {
            float k = Mathf.Clamp01((t - WeedStart) / (WeedEnd - WeedStart));
            float x = Mathf.Lerp(-2.6f, 2.6f, k);
            tumbleweed.gameObject.SetActive(k > 0f && k < 1f);
            tumbleweed.position = Stage + new Vector3(x, 0.26f + Mathf.Abs(Mathf.Sin(k * Mathf.PI * 5f)) * 0.22f, 1.15f);
            tumbleweed.rotation = Quaternion.AngleAxis(-x / 0.26f * Mathf.Rad2Deg, Vector3.forward);
        }

        // vystrel: Sindel prostreli Viktorovi panaka tesne u pusy
        if (Once(2, t, DuelShot))
        {
            Vector3 muzzle = duelPistol != null ? duelPistol.TransformPoint(duelMuzzleLocal) : Chest;
            Vector3 glass = viktorGlass != null ? viktorGlass.position : TomasekNeck;
            ProceduralSfx.Play(ProceduralSfx.Gunshot, muzzle, 1f);
            ProceduralSfx.Play(ProceduralSfx.BulletImpact, glass, 1f);
            Fx.Tracer(muzzle, glass, new Color(1f, 0.85f, 0.4f), true);
            Fx.Sparks(muzzle, new Color(1f, 0.7f, 0.25f));
            Fx.Sparks(glass, new Color(0.9f, 0.95f, 1f));
            Fx.Sparks(glass + Vector3.up * 0.02f, new Color(1f, 0.85f, 0.35f));   // slivovice
            if (viktorGlass != null) viktorGlass.gameObject.SetActive(false);
            for (int i = 0; i < 7; i++)   // strepy
            {
                var shard = Prim(PrimitiveType.Cube, root.transform, glass, Vector3.one * 0.012f, new Color(0.85f, 0.92f, 0.9f)).transform;
                flying.Add((shard, new Vector3(Random.Range(0.6f, 2.6f), Random.Range(0.6f, 2.2f), Random.Range(-1.2f, 1.2f)), Random.insideUnitSphere * 720f));
            }
            shake = new Vector3(0.05f, 0.04f, 0f);
        }
        if (Once(3, t, SmokeBlow))
        {
            Vector3 muzzle = duelPistol != null ? duelPistol.TransformPoint(duelMuzzleLocal) : Chest;
            Fx.Sparks(muzzle + Vector3.up * 0.03f, new Color(0.7f, 0.7f, 0.72f));   // kour z hlavne
            ProceduralSfx.Play(ProceduralSfx.Shush, Chest, 0.25f);
        }
        if (Once(4, t, SpinStart)) ProceduralSfx.Play(ProceduralSfx.Dash, Chest, 0.35f);
        if (Once(5, t, Holstered)) ProceduralSfx.Play(ProceduralSfx.Empty, Chest, 0.9f);   // cvak do pouzdra

        // kamera: celek zboku, rychle detaily oci, celek, pak na Sindela (detail obliceje - spolecny najezd)
        if (t >= EyesSindel && t < EyesViktor && visual.BodyAnimator != null)
        {
            Vector3 eyes = Eyes(visual.BodyAnimator, head, hero.forward);
            pos = eyes + hero.forward * Mathf.Lerp(0.34f, 0.26f, (t - EyesSindel) / (EyesViktor - EyesSindel));
            rot = Quaternion.LookRotation(eyes - pos);
            fov = 18f;
            return;
        }
        if (t >= EyesViktor && t < DuelWide && tomasek != null && tomasek.animator != null)
        {
            Vector3 facing = seatRotation * Vector3.forward;
            Vector3 eyes = Eyes(tomasek.animator, tomasekHead, facing);
            pos = eyes + facing * Mathf.Lerp(0.34f, 0.26f, (t - EyesViktor) / (DuelWide - EyesViktor));
            rot = Quaternion.LookRotation(eyes - pos);
            fov = 18f;
            return;
        }
        if (t >= DuelMedium)
        {
            pos = Stage + new Vector3(0.45f, 1.22f, 1.55f);
            rot = Quaternion.LookRotation(Chest + Vector3.up * 0.25f - pos);
            fov = 42f;
            return;
        }
        pos = Stage + new Vector3(0f, 0.95f, 0.3f);
        rot = Quaternion.LookRotation(Stage + DuelTable + Vector3.up * 0.95f - pos);
        fov = 50f;
    }

    // Stred oci postavy (kosti oci; bez nich odhad od hlavy).
    static Vector3 Eyes(Animator anim, Transform headBone, Vector3 forward)
    {
        var left = anim.GetBoneTransform(HumanBodyBones.LeftEye);
        var right = anim.GetBoneTransform(HumanBodyBones.RightEye);
        if (left != null && right != null) return (left.position + right.position) * 0.5f;
        return headBone != null ? headBone.position + Vector3.up * 0.09f + forward * 0.08f : anim.transform.position + Vector3.up * 1.6f;
    }

    // Po animaci: zidle, vyrazy, pistole v prave ruce (tasi, miri, kour, protoceni, pouzdro), Viktor s panakem.
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
        if (head == null || visual.BodyAnimator == null) return;
        var anim = visual.BodyAnimator;
        Vector3 forward = hero.forward, right = hero.right, up = Vector3.up;

        // Sindel: mhouri oci (souboj), po vystrelu se usklibne (v detailu pije - Drink)
        float squint = Smooth(Mathf.Clamp01((time - (EyesSindel - 0.2f)) / 0.2f));
        float smirk = Smooth(Mathf.Clamp01((time - DuelShot - 0.1f) / 0.3f));
        heroFace.Set("eyeSquintLeft", 75f * squint * (1f - 0.5f * smirk));
        heroFace.Set("eyeSquintRight", 75f * squint * (1f - 0.5f * smirk));
        heroFace.Set("browDownLeft", 50f * squint * (1f - smirk));
        heroFace.Set("browDownRight", 50f * squint * (1f - smirk));
        heroFace.Set("mouthPressLeft", 40f * squint * (1f - smirk));
        heroFace.Set("mouthPressRight", 40f * squint * (1f - smirk));
        heroFace.Set("mouthSmileLeft", 60f * smirk);

        // pistole v prave ruce
        if (time >= DuelDraw && time < Holstered + 0.25f)
        {
            if (duelArm == null) duelArm = FirstPersonArms.BuildArm(anim, true);
            if (duelPistol == null) BuildDuelPistol();
            if (duelArm != null && duelArm.upper != null)
            {
                Vector3 shoulder = duelArm.upper.position;
                Vector3 glassAt = Stage + DuelTable + new Vector3(0.62f, 1.15f, -0.02f);
                if (tomasekHead != null) glassAt = tomasekHead.position + (seatRotation * Vector3.forward) * 0.12f - up * 0.04f;
                Vector3 aim = (glassAt - shoulder).normalized;
                float d = time - DuelShot;
                float kick = d > 0f && d < 0.15f ? Mathf.Sin(d / 0.15f * Mathf.PI) : 0f;
                Vector3 aimPalm = shoulder + aim * 0.5f - aim * 0.04f * kick;
                Quaternion aimFrame = Quaternion.LookRotation(aim, Vector3.Cross(up, aim)) * Quaternion.Euler(-20f * kick, 0f, 0f);
                Vector3 mouth = head.position + forward * 0.12f + up * 0.02f;
                Vector3 smokePalm = mouth + forward * 0.1f - up * 0.14f;
                Quaternion smokeFrame = Quaternion.LookRotation((up + forward * 0.25f).normalized, right);
                Vector3 spinPalm = shoulder + forward * 0.3f + right * 0.12f - up * 0.12f;
                Quaternion spinFrame = Quaternion.LookRotation(forward, right);
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                Vector3 hipPalm = (hips != null ? hips.position : shoulder - up * 0.5f) + right * 0.2f + up * 0.05f;
                Quaternion holsterFrame = Quaternion.LookRotation(-up, right);

                float draw = Smooth(Mathf.Clamp01((time - DuelDraw) / (DuelShot - 0.04f - DuelDraw)));
                float toSmoke = Smooth(Mathf.Clamp01((time - SmokeUp) / (SmokeBlow - 0.05f - SmokeUp)));
                float toSpin = Smooth(Mathf.Clamp01((time - SmokeBlow - 0.1f) / (SpinStart - SmokeBlow - 0.1f)));
                float toHolster = Smooth(Mathf.Clamp01((time - SpinEnd) / (Holstered - SpinEnd)));
                float release = 1f - Smooth(Mathf.Clamp01((time - Holstered) / 0.25f));
                Vector3 palm = Vector3.Lerp(Vector3.Lerp(Vector3.Lerp(aimPalm, smokePalm, toSmoke), spinPalm, toSpin), hipPalm, toHolster);
                Quaternion frame = Quaternion.Slerp(Quaternion.Slerp(Quaternion.Slerp(aimFrame, smokeFrame, toSmoke), spinFrame, toSpin), holsterFrame, toHolster);
                SolveDavidArm(duelArm, palm, frame, hero.TransformDirection(new Vector3(0.6f, -1f, -0.3f)), draw * release);

                // pistole v dlani (hlaven = prsty, prava strana zbrane = hrbet ruky); protoceni kolem lucika
                FirstPersonArms.PalmPose(duelArm, out var palmAt, out var handFrame);
                Vector3 fingers = handFrame * Vector3.forward, back = handFrame * Vector3.up;
                Quaternion gun = Quaternion.LookRotation(fingers, Vector3.Cross(fingers, back));
                float spin = Mathf.Clamp01((time - SpinStart) / (SpinEnd - SpinStart));
                if (spin > 0f && spin < 1f) gun *= Quaternion.AngleAxis(720f * (1f - (1f - spin) * (1f - spin)), Vector3.right);
                duelPistol.SetPositionAndRotation(palmAt - gun * duelPistolGrip, gun);
                duelPistol.gameObject.SetActive(time < Holstered);
            }
        }
        else if (duelPistol != null && duelPistol.gameObject.activeSelf) duelPistol.gameObject.SetActive(false);

        // Viktor: drzi panaka na stole, zvedne ho k puse; po vystrelu ztuhne zdesenim a ruku pomalu spusti
        if (tomasek != null && tomasek.animator != null && tomasekHead != null)
        {
            if (viktorArm == null) viktorArm = FirstPersonArms.BuildArm(tomasek.animator, true);
            Vector3 vf = seatRotation * Vector3.forward, vr = seatRotation * Vector3.right;
            Vector3 onTable = Stage + DuelTable + new Vector3(0.36f, 0.8f, -0.05f);
            Vector3 atMouth = tomasekHead.position + vf * 0.14f - up * 0.04f;
            float raise = Smooth(Mathf.Clamp01((time - GlassRaise) / (GlassAtMouth - GlassRaise)));
            float lower = Smooth(Mathf.Clamp01((time - (DuelShot + 0.8f)) / 0.5f));
            Vector3 glassAt = Vector3.Lerp(Vector3.Lerp(onTable, atMouth, raise), onTable + up * 0.15f, lower);
            if (viktorArm != null && viktorArm.upper != null)
                FirstPersonArms.SolveArmFrame(viktorArm, glassAt + vr * 0.035f, Quaternion.LookRotation(vf, vr), vr * 0.6f - up - vf * 0.3f, false);
            if (viktorGlass != null && viktorGlass.gameObject.activeSelf) viktorGlass.SetPositionAndRotation(glassAt, Quaternion.identity);

            float vSquint = Smooth(Mathf.Clamp01((time - (EyesSindel - 0.2f)) / 0.2f)) * (time < DuelShot ? 1f : 0f);
            float shock = Smooth(Mathf.Clamp01((time - DuelShot) / 0.08f));
            tomasekFace.Set("eyeSquintLeft", 70f * vSquint);
            tomasekFace.Set("eyeSquintRight", 70f * vSquint);
            tomasekFace.Set("browDownLeft", 50f * vSquint);
            tomasekFace.Set("browDownRight", 50f * vSquint);
            tomasekFace.Set("eyeWideLeft", 100f * shock);
            tomasekFace.Set("eyeWideRight", 100f * shock);
            tomasekFace.Set("browInnerUp", 80f * shock);
            tomasekFace.Set("jawOpen", 40f * shock);
            tomasekHead.rotation = Quaternion.AngleAxis(-12f * shock, vr) * tomasekHead.rotation;
        }
    }

    // Sindelova pistole (stejny model jako ve hre) pro souboj.
    void BuildDuelPistol()
    {
        duelPistol = new GameObject("SindelovaPistole").transform;
        duelPistol.SetParent(root.transform, false);
        duelPistolGrip = Vector3.zero;
        duelMuzzleLocal = new Vector3(0f, 0.05f, 0.2f);
        var weapon = definition.weapon;
        if (weapon == null) return;
        var model = weapon.heldModel == HeldModel.Auto ? (weapon.IsMelee ? HeldModel.Axe : HeldModel.Gun) : weapon.heldModel;
        HeldWeapons.BuildModel(duelPistol, weapon, model, 1f);
        duelPistolGrip = HeldWeapons.GripInWeapon(weapon, model);
        // usti: nejprednejsi bod modelu (mistne +z), kousek nad uchopem
        float front = 0.2f;
        bool any = false;
        foreach (var r in duelPistol.GetComponentsInChildren<Renderer>())
        {
            float z = r.bounds.max.z - duelPistol.position.z;
            front = any ? Mathf.Max(front, z) : z;
            any = true;
        }
        duelMuzzleLocal = new Vector3(0f, duelPistolGrip.y + 0.05f, front);
    }

    // Chuchvalec suche travy: koule ze zkrizenych stebel.
    Transform BuildTumbleweed()
    {
        var weed = new GameObject("ChuchvalecTravy").transform;
        weed.SetParent(root.transform, false);
        var dry = new Color(0.62f, 0.48f, 0.28f);
        for (int i = 0; i < 18; i++)
        {
            var stick = Prim(PrimitiveType.Cylinder, weed, Vector3.zero, new Vector3(0.012f, 0.22f, 0.012f), i % 3 == 0 ? dry * 0.75f : dry).transform;
            stick.localPosition = Random.insideUnitSphere * 0.1f;
            stick.localRotation = Random.rotationUniform;
            stick.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        return weed;
    }

    // Stul se slivovici: deska, nohy, lahev s etiketou.
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

    // Odletujici veci (bedny, pulky lasagni).
    void UpdateFlying()
    {
        float dt = stepDt;
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
        if (scene == "Viktor")
            ViktorCaro();
        if (scene == "Honza")
        {
            HonzaDetonator();
            HonzaCheer();
            HonzaDetonatorInHand();
        }
        if (scene == "Sniper")
            KondulicFrown();
        if (scene == "Sindel")
            SindelLate();
        if (scene == "Max")
            MaxLate();
        if (scene == "Bard")
            MosinoLate();
        if (scene == "Anna")
            DavidLate();
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
