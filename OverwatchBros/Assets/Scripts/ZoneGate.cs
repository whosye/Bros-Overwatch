using UnityEngine;

// Brana zony v rezimu utok a obrana (plot, branka, mriz v tunelu, neviditelna stena): zavrena, dokud utocnici
// nedobyji 'unlockAfter' bodu (1 = po bodu A, 2 = po bodu B...). V team deathmatchi, v lobby a mimo zapas je otevrena.
// Stav si pocita kazdy pocitac sam z MatchManageru - nic se neposila po siti.
// Zavrena: kolize zapnute, objekt "Zavreno" viditelny (a "Otevreno" skryty); otevrena naopak.
// Kdyz nema podobjekty Zavreno/Otevreno, schovaji se pri otevreni vsechny jeho renderery (plot zmizi).
public class ZoneGate : MonoBehaviour
{
    [Tooltip("Po kolika dobytych bodech se otevre (1 = po A, 2 = po B).")]
    public int unlockAfter = 1;
    [Tooltip("Po kolika dobytych bodech zmizi uplne (0 = nikdy) - napr. otevrena branka po bodu B.")]
    public int removeAfter = 0;
    [Tooltip("Zvuk pri otevreni (vrzajici branka / mriz).")]
    public bool playSound = true;
    [Tooltip("Zavrena brana pusti obrance (jako dvere spawnu v Overwatchi) - zastavi jen utocniky.")]
    public bool defendersPass;

    bool initialized;
    bool open;
    bool removed;
    float nextCheck;
    Collider[] colliders;
    Renderer[] renderers;
    Transform closedPart, openPart;

    static int lastChangeFrame = -1;

    void Awake()
    {
        colliders = GetComponentsInChildren<Collider>(true);
        closedPart = transform.Find("Zavreno");
        openPart = transform.Find("Otevreno");
        if (closedPart == null && openPart == null)
            renderers = GetComponentsInChildren<Renderer>(true);
    }

    void OnEnable()
    {
        initialized = false;
        nextCheck = 0f;
    }

    public static bool ShouldBeOpen(int unlockAfter)
    {
        var match = MatchManager.Instance;
        if (match == null || !match.IsSpawned || match.IsLobby || !match.IsAttackMode) return true;
        return match.pointIndex.Value >= unlockAfter;
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.2f;
        if (defendersPass) UpdateDefenderPass();

        bool wanted = ShouldBeOpen(unlockAfter);
        bool gone = removeAfter > 0 && ShouldBeOpen(removeAfter);
        if (initialized && wanted == open && gone == removed) return;
        removed = gone;
        bool wasInitialized = initialized;
        initialized = true;
        open = wanted;
        Apply();

        if (!wasInitialized) return;
        if (open && playSound)
            ProceduralSfx.Play(ProceduralSfx.CaptureUnlock, transform.position + Vector3.up, 0.5f);

        // boti: cesty se zmenily - sit se postavi znovu (jednou za snimek, at se branami nerusi)
        if (lastChangeFrame != Time.frameCount)
        {
            lastChangeFrame = Time.frameCount;
            BotNavigation.Invalidate();
        }
    }

    // Obranci zavrenou branou projdou: kolize mezi branou a jejich postavou se vypne (u utocniku zustane).
    void UpdateDefenderPass()
    {
        var match = MatchManager.Instance;
        bool attackMode = match != null && match.IsSpawned && match.IsAttackMode && !match.IsLobby;
        foreach (var fpc in FirstPersonController.All)
        {
            if (fpc == null) continue;
            var body = fpc.GetComponent<CharacterController>();
            var team = fpc.GetComponent<PlayerTeam>();
            if (body == null) continue;
            bool pass = attackMode && team != null && team.teamId.Value != match.attackTeam.Value;
            foreach (var c in colliders)
                if (c != null) Physics.IgnoreCollision(c, body, pass);
        }
    }

    void Apply()
    {
        foreach (var c in colliders)
            if (c != null) c.enabled = !open;
        if (closedPart != null) closedPart.gameObject.SetActive(!open);
        if (openPart != null) openPart.gameObject.SetActive(open);
        if (renderers != null)
            foreach (var r in renderers)
                if (r != null) r.enabled = !open;
        if (removed)
        {
            if (closedPart != null) closedPart.gameObject.SetActive(false);
            if (openPart != null) openPart.gameObject.SetActive(false);
        }
    }
}
