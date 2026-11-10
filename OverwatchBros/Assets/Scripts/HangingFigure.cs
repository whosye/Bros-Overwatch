using System.Collections.Generic;
using UnityEngine;

// Postavicka visici za pravou ruku na liste pod stropem (DecorTools: VisiciPostavicka). Necinne se mirne houpe.
// Play(): chytne se i levou rukou a udela shyb (jen pri nem je videt fotka obliceje Hlava/Oblicej) - v horni poloze se zhrouti strop (FoldingFloor ceka na konec).
// Animace jen presouva dily postavicky (paze se pocitaji ze dvou kloubu), nic se neposila po siti.
// Lokalni osy: pocatek = uchop prave ruky na liste, +x podel listy (prava ruka), +y nahoru, +z dopredu.
public class HangingFigure : MonoBehaviour
{
    const float GrabTime = 0.7f, PauseTime = 0.35f, PullTime = 1.3f, TopTime = 0.15f;
    public static float Duration => GrabTime + PauseTime + PullTime + TopTime;
    const float PullHeight = 0.55f;

    class Limb { public Transform t; public float length; }

    readonly Dictionary<string, Limb> limbs = new Dictionary<string, Limb>();
    readonly List<(Transform t, Vector3 position)> body = new List<(Transform, Vector3)>();
    Transform leftHand;
    Renderer face;   // fotka obliceje (Hlava/Oblicej) - jen behem shybu
    Quaternion baseRotation;
    float playStart = -1f;
    bool ready, setup;

    static readonly string[] Arms = { "PravePredlokti", "PravaPaze", "LevaPaze", "LevePredlokti" };
    static readonly HashSet<string> Fixed = new HashSet<string> { "PravaDlan", "Prsty" };

    void Awake() => Setup(true);

    // Najde dily postavicky. 'allowCreate' = smi vytvorit fotku obliceje (ne v nahledu v editoru).
    public void Setup(bool allowCreate)
    {
        if (setup) return;
        setup = true;
        baseRotation = transform.localRotation;
        foreach (Transform child in transform)
        {
            if (System.Array.IndexOf(Arms, child.name) >= 0)
            {
                var mesh = child.GetComponent<MeshFilter>();
                float length = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.bounds.size.z * child.localScale.z : 0.35f;
                limbs[child.name] = new Limb { t = child, length = Mathf.Max(0.05f, length) };
            }
            else if (child.name == "LevaDlan") leftHand = child;
            else if (!Fixed.Contains(child.name)) body.Add((child, child.localPosition));
        }
        ready = limbs.Count == Arms.Length;
        var faceObject = transform.Find("Hlava/Oblicej");
        face = faceObject != null ? faceObject.GetComponent<Renderer>() : (allowCreate ? CreateFace() : null);
        if (face != null) PlaceFace(face.transform);
        if (face != null) face.enabled = false;
    }

    // Fotka obliceje (Resources/Dekorace/VisiciOblicej.png) na hlavu, kdyz ve scene neni: ctverec tesne pred celem
    // hlavy (dite "Hlava" - hybe se presne s ni). Material = kopie materialu hlavy s fotkou a alfa orezem.
    Renderer CreateFace()
    {
        var head = transform.Find("Hlava");
        var texture = Resources.Load<Texture2D>("Dekorace/VisiciOblicej");
        var headRenderer = head != null ? head.GetComponent<Renderer>() : null;
        if (head == null || texture == null || headRenderer == null) return null;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(quad.GetComponent<Collider>());
        quad.name = "Oblicej";
        quad.transform.SetParent(head, false);
        PlaceFace(quad.transform);
        var material = new Material(headRenderer.sharedMaterial);
        material.SetTexture("_BaseMap", texture);
        material.mainTexture = texture;
        material.SetColor("_BaseColor", Color.white);
        material.color = Color.white;
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", 0.5f);
        material.EnableKeyword("_ALPHATEST_ON");
        var renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return renderer;
    }

    // Fotka na ZATYLKU (kamera videa je za postavickou): tesne za hlavou, videt zezadu. Quad je videt ze strany -z,
    // coz je u postavicky smer dozadu - neotaci se.
    public const float FaceWidth = 0.30f, FaceHeight = 0.40f;

    public static void PlaceFace(Transform quad)
    {
        Vector3 s = quad.parent != null ? quad.parent.localScale : Vector3.one;
        // 0,30 x 0,40 m (vetsi nez hlava), 4 cm za zatylkem - pred vlasy
        quad.localPosition = new Vector3(0f, 0.04f / Mathf.Max(0.01f, s.y), -(0.5f + 0.04f / Mathf.Max(0.01f, s.z)));
        quad.localRotation = Quaternion.identity;
        quad.localScale = new Vector3(FaceWidth / Mathf.Max(0.01f, s.x), FaceHeight / Mathf.Max(0.01f, s.y), 1f);
    }

    // Spusti shyb; vraci, za jak dlouho se ma zhroutit strop.
    public float Play()
    {
        playStart = Time.time;
        if (face != null) face.enabled = true;
        Debug.Log($"[Postavicka] Shyb: dily {(ready ? "ok" : "CHYBI (" + limbs.Count + "/4 paze)")}, oblicej {(face != null ? "ano (" + face.name + ")" : "NENI")}.");
        return Duration;
    }

    public void ResetPose()
    {
        playStart = -1f;
        if (face != null) face.enabled = false;
        Pose(0f, 0f);
    }

    void Update() => Evaluate(playStart >= 0f ? Time.time - playStart : -1f, Time.time);

    // Poza v case 'sinceStart' od zacatku shybu (zaporne = jen visi), 'clock' = cas pro houpani.
    // Pouziva ji i nahled filmu v editoru (bez hry).
    public void Evaluate(float sinceStart, float clock)
    {
        if (!ready) return;
        if (face != null) face.enabled = sinceStart >= 0f;
        float grab = 0f, pull = 0f;
        if (sinceStart >= 0f)
        {
            float t = sinceStart;
            grab = Mathf.SmoothStep(0f, 1f, t / GrabTime);
            float p = Mathf.Clamp01((t - GrabTime - PauseTime) / PullTime);
            // shyb: pomalu se rozjede, nahore se zastavi (s malym zakmitnutim na zacatku)
            pull = Mathf.SmoothStep(0f, 1f, p) - 0.05f * Mathf.Sin(Mathf.Clamp01(p * 3f) * Mathf.PI);
            pull = Mathf.Max(0f, pull);
        }
        // necinne houpani (pri shybu ustane)
        float calm = 1f - Mathf.Max(grab, pull);
        transform.localRotation = baseRotation * Quaternion.Euler(Mathf.Sin(clock * 1.4f) * 3f * calm, 0f, Mathf.Sin(clock * 0.9f) * 1.5f * calm);
        Pose(grab, pull);
    }

    void Pose(float grab, float pull)
    {
        if (!ready) return;
        float up = pull * PullHeight;
        Vector3 lift = new Vector3(0f, up, 0f);
        foreach (var (t, position) in body)
            if (t != null) t.localPosition = position + lift;

        // prava ruka: dlan na liste, rameno jde se shybem nahoru, loket ven
        Vector3 rightHand = new Vector3(0.07f, -0.07f, 0f);
        Vector3 rightShoulder = new Vector3(0.18f, -0.78f, 0f) + lift;
        Arm("PravaPaze", "PravePredlokti", rightShoulder, rightHand, new Vector3(1f, -0.4f, 0.35f), out _);

        // leva ruka: z visu obloukem nahoru na listu
        Vector3 hangHand = new Vector3(-0.31f, -1.58f, 0.08f), barHand = new Vector3(-0.12f, -0.12f, 0f);
        Vector3 leftTarget = Vector3.Lerp(hangHand, barHand, grab) + new Vector3(-0.18f, 0f, 0.22f) * Mathf.Sin(grab * Mathf.PI);
        Vector3 leftShoulder = Vector3.Lerp(new Vector3(-0.22f, -0.95f, 0f), new Vector3(-0.2f, -0.76f, 0f), grab) + lift;
        Arm("LevaPaze", "LevePredlokti", leftShoulder, leftTarget, Vector3.Lerp(new Vector3(-0.3f, 0f, 1f), new Vector3(-1f, -0.4f, 0.35f), grab), out Vector3 wrist);
        if (leftHand != null)
        {
            Vector3 dir = (wrist - limbs["LevePredlokti"].t.localPosition).normalized;
            leftHand.localPosition = wrist + dir * 0.06f;
            leftHand.localRotation = Quaternion.FromToRotation(Vector3.down, dir);
        }
    }

    // Paze ze dvou dilu (rameno -> loket -> zapesti), loket do smeru 'bend'.
    void Arm(string upperName, string foreName, Vector3 shoulder, Vector3 hand, Vector3 bend, out Vector3 wrist)
    {
        var upper = limbs[upperName];
        var fore = limbs[foreName];
        float l1 = upper.length, l2 = fore.length;
        Vector3 toHand = hand - shoulder;
        float d = Mathf.Clamp(toHand.magnitude, 0.05f, l1 + l2 - 0.001f);
        Vector3 dir = toHand.normalized;
        wrist = shoulder + dir * d;
        float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
        float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
        Vector3 perp = bend - dir * Vector3.Dot(bend, dir);
        perp = perp.sqrMagnitude > 1e-6f ? perp.normalized : Vector3.forward;
        Vector3 elbow = shoulder + dir * a + perp * h;
        Place(upper, shoulder, elbow);
        Place(fore, elbow, wrist);
    }

    static void Place(Limb limb, Vector3 from, Vector3 to)
    {
        Vector3 along = to - from;
        float length = along.magnitude;
        if (length < 1e-4f) return;
        Vector3 upHint = Mathf.Abs(Vector3.Dot(along / length, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
        limb.t.localPosition = (from + to) * 0.5f;
        limb.t.localRotation = Quaternion.LookRotation(along, upHint);
        var s = limb.t.localScale;
        limb.t.localScale = new Vector3(s.x, s.y, length / Mathf.Max(1e-4f, limb.length) * 1f);
    }
}
