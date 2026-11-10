using Unity.Netcode;
using UnityEngine;

// Sklopna podlaha (rampa do patra): kus podlahy na kloubu. Dokud utocnici nedobyji 'unlockAfter' bodu, je to
// normalni podlaha; pak se s rachotem sklopi dolu na zem a udela rampu - dalsi vstup do patra. Na zacatku
// kola je zase nahore. V lobby, mimo zapas a v team deathmatchi zustava nahore.
// Stav si pocita kazdy pocitac sam z MatchManageru - nic se neposila po siti.
// Objekt = kloub (otaci se kolem 'axis' v lokalnich osach), kus podlahy je jeho dite. Nastavuje DecorTools.
[RequireComponent(typeof(Rigidbody))]
public class FoldingFloor : MonoBehaviour
{
    [Tooltip("Po kolika dobytych bodech se sklopi (1 = po A, 2 = po B, 3 = po bodu 3...).")]
    public int unlockAfter = 3;
    [Tooltip("Osa kloubu (lokalne).")]
    public Vector3 axis = Vector3.forward;
    [Tooltip("O kolik stupnu se sklopi (az na zem).")]
    public float openAngle = 30f;
    [Tooltip("O kolik stupnu se sklopi navic - volny konec se lehce zabori do zeme, at se na rampu dobre vstupuje (bez schodu).")]
    public float sinkAngle = 3f;
    [Tooltip("Jak dlouho trva sklopeni (s).")]
    public float duration = 1.1f;
    [Tooltip("Co zmizi, jakmile se podlaha zacne sklapet (napr. lista pod hranou stropu); nahore se zase ukaze.")]
    public GameObject[] hideWhenOpen = new GameObject[0];
    [Tooltip("Zvuk, kdyz postavicka s listou sleti dolu (zhrouceni) - Povova ultimatka, cast 2. Slysi ho vsichni stejne nahlas.")]
    public AudioClip collapseSound;
    [HideInInspector] public Quaternion closedLocalRotation = Quaternion.identity;   // poloha nahore (nastavi DecorTools)

    // Ma podlaha filmovou scenu (visici postavicka je ve scene a videt)? A jak dlouho trva (shyb + pad + chvilka).
    public bool HasCutscene => figure != null && figure.gameObject.activeInHierarchy;
    public float CutsceneSeconds => HangingFigure.Duration + duration + 1.3f;

    Quaternion closedRotation;
    bool initialized, open, landed = true, cracked = true;
    bool wasReplay;
    float progress, nextCheck, startAt;
    HangingFigure figure;   // visici postavicka (z Hide When Open): pred zhroucenim udela shyb

    void Awake()
    {
        closedRotation = closedLocalRotation;
        foreach (var go in hideWhenOpen)
            if (go != null && go.transform.Find("PravaDlan") != null)
            {
                figure = go.GetComponent<HangingFigure>();
                if (figure == null) figure = go.AddComponent<HangingFigure>();
            }
        var body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    void OnEnable()
    {
        initialized = false;
        nextCheck = 0f;
    }

    void Update()
    {
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.2f;
            // zacatek / konec zaznamu: podlaha se jen nastavi (bez shybu, filmu a zvuku)
            if (ReplayPlayer.RecordedPoint.HasValue != wasReplay) { wasReplay = ReplayPlayer.RecordedPoint.HasValue; initialized = false; }
            bool wanted = testDown || ShouldBeDown(unlockAfter);
            if (wasReplay && wanted != open) initialized = false;   // v zaznamu bez filmu, jen nastavit
            if (!initialized)
            {
                initialized = true;
                open = wanted;
                progress = open ? 1f : 0f;
                Apply();
            }
            else if (wanted != open)
            {
                open = wanted;
                landed = false;
                startAt = Time.time;
                bool figureShown = figure != null && figure.gameObject.activeInHierarchy;
                if (open && figureShown)
                {
                    // postavicka se chytne i levou a udela shyb - v horni poloze se strop zhrouti
                    startAt = Time.time + figure.Play();
                    // filmovy zaber pro kazdeho hrace: shyb + pad podlahy + chvilka po dopadu
                    FloorCutscene.Play(figure.transform, CutsceneSeconds);
                    cracked = false;
                }
                else if (open)
                {
                    ProceduralSfx.Play(ProceduralSfx.Hit, transform.position, 0.9f, 40f, spatial: true);
                    cracked = true;
                }
                if (!open && figure != null) figure.ResetPose();
            }
        }

        if (open && Time.time < startAt) return;   // ceka se na shyb
        if (open && !cracked)
        {
            cracked = true;
            ProceduralSfx.Play(ProceduralSfx.Hit, transform.position, 1f, 40f, spatial: true);
            if (collapseSound != null)
                ProceduralSfx.Play(collapseSound, transform.position, 1f, 80f, spatial: false);   // soucast filmu - vsem stejne
        }
        float target = open ? 1f : 0f;
        if (Mathf.Approximately(progress, target)) return;
        // dolu zrychluje (pada), nahoru stejnomerne
        progress = Mathf.MoveTowards(progress, target, Time.deltaTime / Mathf.Max(0.05f, duration));
        Apply();
        // padajici strop zabije, koho zavali (jen behem padu a pri dopadu - pak uz ne; v zaznamu nikdy)
        if (open && !landed && !wasReplay)
            Crush(Mathf.Approximately(progress, target));
        if (Mathf.Approximately(progress, target) && !landed)
        {
            landed = true;
            if (open)
            {
                ProceduralSfx.Play(ProceduralSfx.Explosion, FreeEdge(), 0.55f, 45f, spatial: true);
                Fx.Sparks(FreeEdge(), new Color(0.55f, 0.42f, 0.3f));
            }
            BotNavigation.Invalidate();   // boti: nova cesta do patra
        }
    }

    // Dole jen v utoku a obrane po dobyti 'unlockAfter' bodu. V lobby, mimo zapas a v team deathmatchi je nahore.
    static bool ShouldBeDown(int unlockAfter)
    {
        // v zaznamu (POTG, killcam) podle bodu, o ktery se tehdy hralo
        var recorded = ReplayPlayer.RecordedPoint;
        if (recorded.HasValue) return recorded.Value >= 0 && recorded.Value >= unlockAfter;

        var match = MatchManager.Instance;
        if (match == null || !match.IsSpawned || match.IsLobby || !match.IsAttackMode) return false;
        return match.pointIndex.Value >= unlockAfter;
    }

    // Test v Play modu (Inspector -> tri tecky komponenty): shyb a zhrouceni stropu bez dobyvani bodu; podruhe zpet.
    bool testDown;
    [ContextMenu("Test - shyb a zhrouceni (Play mode)")]
    public void TestCollapse() => testDown = !testDown;
    public bool TestDown => testDown;

    void Apply()
    {
        float eased = open ? progress * progress : progress;   // pad zrychluje
        transform.localRotation = closedRotation * Quaternion.AngleAxis((openAngle + sinkAngle) * eased, axis);
        bool show = progress <= 0.001f;
        foreach (var go in hideWhenOpen)
            if (go != null && go.activeSelf != show) go.SetActive(show);
    }

    // Server: kdo je pod padajici deskou, zemre. Behem padu jen tesne pod ni (koho prave zasahne), pri dopadu
    // cely prostor pod deskou az k zemi. Kdo stal nahore na podlaze, ten se jen sveze.
    void Crush(bool final)
    {
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsServer) return;
        Physics.SyncTransforms();
        foreach (var box in GetComponentsInChildren<BoxCollider>())
        {
            if (!box.enabled || box.isTrigger) continue;
            Transform t = box.transform;
            Vector3 scale = t.lossyScale;
            Vector3 half = Vector3.Scale(box.size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            // tloustka desky = nejkratsi osa; 'up' = ta osa otocena od zeme
            int axis = half.x <= half.y && half.x <= half.z ? 0 : half.y <= half.z ? 1 : 2;
            Vector3 up = axis == 0 ? t.right : axis == 1 ? t.up : t.forward;
            if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;
            Vector3 slab = t.TransformPoint(box.center);
            float reach = final ? 2.4f : 0.6f;
            half[axis] += reach * 0.5f;
            foreach (var hit in Physics.OverlapBox(slab - up * reach * 0.5f, half, t.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!(hit is CharacterController)) continue;
                if (Vector3.Dot(hit.bounds.center - slab, up) > 0f) continue;   // stoji nahore na desce
                var health = hit.GetComponentInParent<Health>();
                if (health == null || !health.IsSpawned || health.currentHealth.Value <= 0f) continue;
                var hero = health.GetComponent<PlayerHero>();
                if (hero != null && hero.IsJoining) continue;
                if (hero != null) hero.ServerDeathCause = "env:strop";
                health.Kill();
            }
        }
    }

    // Stred volneho okraje (kde rampa dopadne) - pro zvuk a prach.
    Vector3 FreeEdge()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return transform.position;
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        Vector3 p = b.center;
        p.y = b.min.y + 0.2f;
        Vector3 away = p - transform.position;
        away.y = 0f;
        return p + away;
    }
}
