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
            bool wanted = testDown || ShouldBeDown(unlockAfter);
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
        transform.localRotation = closedRotation * Quaternion.AngleAxis(openAngle * eased, axis);
        bool show = progress <= 0.001f;
        foreach (var go in hideWhenOpen)
            if (go != null && go.activeSelf != show) go.SetActive(show);
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
