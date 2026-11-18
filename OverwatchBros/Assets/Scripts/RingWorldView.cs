using System.Collections.Generic;
using UnityEngine;

// Co kresli kazdy hrac podle stavu prstenu z MatchManageru (MatchManagerRing):
//  - spadly prsten (po smrti nositele lezi 5 s na zemi) - zlata obrucka, ktera se toci a svitivi,
//  - Glum (honi nositele) - postava z jednoduchych tvaru podle fotky (kamarad v kostymu: hola latexova cepice se
//    spicatyma usima, par tmavych pramenu vlasu, svetla kuze, sede trenky, v ruce "poklad"). Cim nastvanejsi, tim
//    vic se hrbi, rychleji cupita a oci mu zcervenaji.
// Behem POTG / killcamu se nekresli (neni v zaznamu).
public class RingWorldView : MonoBehaviour
{
    static RingWorldView instance;

    Transform droppedRing;
    Light droppedGlow;

    Transform gollum, gollumBody, armL, armR, legL, legR;
    Renderer eyeL, eyeR;
    Material eyeMaterial;
    Vector3 gollumShown;
    bool gollumPlaced;
    float walkPhase, airBlend;

    readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    public static void Sync(MatchManager match)
    {
        if (match == null || !match.IsSpawned) return;
        if (instance == null)
        {
            bool needed = match.ringDropped.Value || match.gollumActive.Value;
            if (!needed) return;
            instance = new GameObject("PrstenAGlum").AddComponent<RingWorldView>();
            instance.gameObject.AddComponent<ReplayIgnore>();   // do zaznamu POTG nepatri
            instance.Build();
        }
        instance.Tick(match);
    }

    // ---------------- stavba ----------------

    Material Mat(Color c, float emission = 0f)
    {
        var key = new Color(c.r, c.g, c.b, emission);
        if (materials.TryGetValue(key, out var m)) return m;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        m = new Material(shader) { color = c };
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.25f);
        if (emission > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * emission);
        }
        materials[key] = m;
        return m;
    }

    Transform Part(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color, float emission = 0f, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = Mat(color, emission);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go.transform;
    }

    Transform RingModel(Transform parent, float radius, float thickness)
    {
        var ring = new GameObject("Prsten").transform;
        ring.SetParent(parent, false);
        var gold = new Color(1f, 0.76f, 0.2f);
        const int links = 16;
        for (int i = 0; i < links; i++)
        {
            float a = i * Mathf.PI * 2f / links;
            Part(PrimitiveType.Cube, ring, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f),
                new Vector3(thickness, radius * 0.42f, thickness * 1.3f), gold, 1.4f, new Vector3(0f, 0f, a * Mathf.Rad2Deg));
        }
        return ring;
    }

    void Build()
    {
        // spadly prsten
        droppedRing = new GameObject("SpadlyPrsten").transform;
        droppedRing.SetParent(transform, false);
        RingModel(droppedRing, 0.13f, 0.026f);
        droppedGlow = droppedRing.gameObject.AddComponent<Light>();
        droppedGlow.type = LightType.Point;
        droppedGlow.color = new Color(1f, 0.8f, 0.35f);
        droppedGlow.range = 3.5f;
        droppedGlow.intensity = 1.6f;
        droppedRing.gameObject.SetActive(false);

        // Glum (vyska ~1,7 m, nahrbeny; pivot u chodidel)
        var skin = new Color(0.93f, 0.74f, 0.62f);
        var skinDark = new Color(0.85f, 0.64f, 0.52f);
        var cap = new Color(0.86f, 0.74f, 0.58f);
        var shorts = new Color(0.55f, 0.56f, 0.58f);
        var hair = new Color(0.12f, 0.1f, 0.09f);

        gollum = new GameObject("Glum").transform;
        gollum.SetParent(transform, false);
        // zasahova kolize (zbrane, projektily, vybuchy) - poskozeni ho zahani (MatchManagerRing)
        var hitbox = gollum.gameObject.AddComponent<CapsuleCollider>();   // po ctyrech: lezi podel tela
        hitbox.direction = 2;
        hitbox.center = new Vector3(0f, 0.6f, 0.05f);
        hitbox.height = 1.5f;
        hitbox.radius = 0.38f;
        gollum.gameObject.AddComponent<GollumHitbox>();
        // hlas (hlasky z Resources/Audio/Glum - prida se jich libovolne mnoho), jde s nim
        voice = gollum.gameObject.AddComponent<AudioSource>();
        voice.spatialBlend = 1f;
        voice.rolloffMode = AudioRolloffMode.Linear;
        voice.minDistance = 3f;
        voice.maxDistance = 40f;
        voice.playOnAwake = false;
        lines = Resources.LoadAll<AudioClip>("Audio/Glum");
        // Po ctyrech: telo od kycli (vzadu, 0,55 m) sikmo dopredu k ramenum (0,75 m), ruce jako predni nohy,
        // nohy pokrcene na kolenou dozadu, hlava zvednuta dopredu.
        gollumBody = new GameObject("Telo").transform;
        gollumBody.SetParent(gollum, false);
        gollumBody.localPosition = new Vector3(0f, HipHeight, -0.3f);

        // nohy (od kycli dolu, pokrcene dozadu)
        legL = Limb(gollum, new Vector3(-0.15f, HipHeight, -0.3f), 0.85f, 0.17f, skin);
        legR = Limb(gollum, new Vector3(0.15f, HipHeight, -0.3f), 0.85f, 0.17f, skin);

        // trenky, bricho, hrud
        Part(PrimitiveType.Cylinder, gollumBody, new Vector3(0f, 0.02f, 0f), new Vector3(0.52f, 0.13f, 0.4f), shorts);
        Part(PrimitiveType.Capsule, gollumBody, new Vector3(0f, 0.38f, 0f), new Vector3(0.56f, 0.36f, 0.42f), skin);
        Part(PrimitiveType.Sphere, gollumBody, new Vector3(0f, 0.25f, 0.1f), new Vector3(0.5f, 0.42f, 0.36f), skin);       // bricho
        Part(PrimitiveType.Sphere, gollumBody, new Vector3(0f, 0.55f, 0.17f), new Vector3(0.22f, 0.2f, 0.06f), new Color(0.35f, 0.27f, 0.22f));   // chlupy na hrudi

        // hlava: oblicej, latexova cepice, spicate usi, prameny vlasu, oci
        head = new GameObject("Hlava").transform;
        head.SetParent(gollumBody, false);
        head.localPosition = new Vector3(0f, 0.86f, 0.06f);
        Part(PrimitiveType.Sphere, head, Vector3.zero, new Vector3(0.27f, 0.31f, 0.28f), skin);
        Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.06f, -0.01f), new Vector3(0.285f, 0.24f, 0.29f), cap);
        foreach (float s in new[] { -1f, 1f })
        {
            Part(PrimitiveType.Capsule, head, new Vector3(0.15f * s, 0.04f, -0.02f), new Vector3(0.05f, 0.11f, 0.03f), cap, 0f, new Vector3(0f, 0f, -35f * s));
            eyeTemp = Part(PrimitiveType.Sphere, head, new Vector3(0.055f * s, 0.0f, 0.125f), new Vector3(0.045f, 0.035f, 0.02f), new Color(0.12f, 0.1f, 0.08f));
            if (s < 0f) eyeL = eyeTemp.GetComponent<Renderer>(); else eyeR = eyeTemp.GetComponent<Renderer>();
        }
        Part(PrimitiveType.Sphere, head, new Vector3(0f, -0.04f, 0.14f), new Vector3(0.05f, 0.06f, 0.05f), skinDark);   // nos
        for (int i = 0; i < 6; i++)
        {
            float x = Mathf.Lerp(-0.1f, 0.1f, i / 5f);
            float len = 0.25f + (i % 3) * 0.12f;
            Part(PrimitiveType.Cylinder, head, new Vector3(x, 0.02f - len * 0.5f, 0.13f - Mathf.Abs(x) * 0.4f), new Vector3(0.008f, len * 0.5f, 0.008f), hair);
        }
        eyeMaterial = new Material(eyeL.sharedMaterial);
        eyeL.sharedMaterial = eyeMaterial;
        eyeR.sharedMaterial = eyeMaterial;

        // ruce (od ramen dolu); v prave "poklad"
        armL = Limb(gollumBody, new Vector3(-0.32f, 0.6f, 0.02f), 0.7f, 0.12f, skin);
        armR = Limb(gollumBody, new Vector3(0.32f, 0.6f, 0.02f), 0.7f, 0.12f, skin);
        var treasure = Part(PrimitiveType.Sphere, armL, new Vector3(0f, -0.72f, 0.05f), new Vector3(0.07f, 0.07f, 0.07f), new Color(1f, 0.8f, 0.25f), 1.2f);
        treasure.name = "Poklad";

        gollum.gameObject.SetActive(false);
    }

    Transform eyeTemp, head;
    const float HipHeight = 0.55f;
    const float BodyPitch = 73f;   // predklon tela pri chuzi po ctyrech
    AudioSource voice;
    AudioClip[] lines;
    int lastLine = -1;

    // Glum promluvi (server posila MatchManagerRing - vsichni slysi stejnou hlasku ve stejnou chvili).
    public static void Speak(int seed)
    {
        if (instance == null || instance.voice == null || instance.lines == null || instance.lines.Length == 0) return;
        if (!instance.gollum.gameObject.activeInHierarchy || ReplayPlayer.Active) return;
        int pick = Mathf.Abs(seed) % instance.lines.Length;
        if (instance.lines.Length > 1 && pick == instance.lastLine) pick = (pick + 1) % instance.lines.Length;
        instance.lastLine = pick;
        instance.voice.clip = instance.lines[pick];
        instance.voice.volume = GameSettings.VoiceVolume;
        instance.voice.Play();
    }

    // Koncetina: kloub (pivot) nahore, valec visi dolu.
    Transform Limb(Transform parent, Vector3 joint, float length, float width, Color color)
    {
        var pivot = new GameObject("Kloub").transform;
        pivot.SetParent(parent, false);
        pivot.localPosition = joint;
        Part(PrimitiveType.Capsule, pivot, new Vector3(0f, -length * 0.5f, 0f), new Vector3(width, length * 0.5f, width), color);
        return pivot;
    }

    // ---------------- kazdy snimek ----------------

    void Tick(MatchManager match)
    {
        bool replay = ReplayPlayer.Active;

        bool dropped = match.ringDropped.Value && !replay;
        if (droppedRing.gameObject.activeSelf != dropped) droppedRing.gameObject.SetActive(dropped);
        if (dropped)
        {
            droppedRing.position = match.ringDropPos.Value + Vector3.up * (0.35f + Mathf.Sin(Time.time * 2.5f) * 0.05f);
            droppedRing.rotation = Quaternion.Euler(0f, Time.time * 90f, 0f);
            droppedGlow.intensity = 1.3f + Mathf.Sin(Time.time * 5f) * 0.4f;
        }

        bool active = match.gollumActive.Value && !replay;
        if (gollum.gameObject.activeSelf != active)
        {
            gollum.gameObject.SetActive(active);
            gollumPlaced = false;
        }
        if (!active) return;

        Vector3 target = match.gollumPos.Value;
        if (!gollumPlaced || (target - gollumShown).sqrMagnitude > 64f) { gollumShown = target; gollumPlaced = true; }
        Vector3 before = gollumShown;
        gollumShown = Vector3.Lerp(gollumShown, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
        float moving = Mathf.Clamp01((gollumShown - before).magnitude / Mathf.Max(0.0001f, Time.deltaTime) / 6f);
        gollum.SetPositionAndRotation(gollumShown, Quaternion.Slerp(gollum.rotation, Quaternion.Euler(0f, match.gollumYaw.Value, 0f), 1f - Mathf.Exp(-10f * Time.deltaTime)));

        float anger = match.gollumAnger.Value;
        bool air = match.gollumAir.Value;
        airBlend = Mathf.MoveTowards(airBlend, air ? 1f : 0f, Time.deltaTime * 6f);
        walkPhase += Time.deltaTime * Mathf.Lerp(8f, 17f, anger) * Mathf.Max(0.2f, moving);
        // klus po ctyrech: diagonalni pary (leva ruka + prava noha, prava ruka + leva noha)
        float swing = Mathf.Sin(walkPhase) * 28f * moving * (1f - airBlend);
        float pitch = Mathf.Lerp(BodyPitch, 45f, airBlend);
        // nastvany se plazi niz u zeme
        float hip = HipHeight - anger * 0.06f + Mathf.Abs(Mathf.Sin(walkPhase)) * 0.04f * moving;
        gollumBody.localPosition = new Vector3(0f, hip, -0.3f);
        gollumBody.localRotation = Quaternion.Euler(pitch + Mathf.Sin(walkPhase * 2f) * 2f * moving, 0f, 0f);
        legL.localPosition = new Vector3(-0.15f, hip, -0.3f);
        legR.localPosition = new Vector3(0.15f, hip, -0.3f);
        // nohy: pokrcene dozadu (ve skoku natazene dozadu)
        float legBase = Mathf.Lerp(50f, 85f, airBlend);
        legL.localRotation = Quaternion.Euler(legBase - swing, 0f, 0f);
        legR.localRotation = Quaternion.Euler(legBase + swing, 0f, 0f);
        // ruce: svisle k zemi (vyrovnat predklon tela); ve skoku natazene dopredu
        float armBase = Mathf.Lerp(-pitch, -pitch - 70f, airBlend);
        armL.localRotation = Quaternion.Euler(armBase + swing, 0f, 6f);
        armR.localRotation = Quaternion.Euler(armBase - swing, 0f, -6f);
        // hlava zvednuta, diva se dopredu (k obeti)
        head.localRotation = Quaternion.Euler(-pitch + 8f + Mathf.Sin(walkPhase * 0.5f) * 4f, Mathf.Sin(Time.time * 1.7f) * 12f * (1f - moving), 0f);

        // oci: tmave -> svitive cervene
        var eyeColor = Color.Lerp(new Color(0.12f, 0.1f, 0.08f), new Color(1f, 0.15f, 0.05f), anger);
        eyeMaterial.SetColor("_BaseColor", eyeColor);
        if (anger > 0.2f)
        {
            eyeMaterial.EnableKeyword("_EMISSION");
            eyeMaterial.SetColor("_EmissionColor", eyeColor * (anger * 3f));
        }
    }
}
