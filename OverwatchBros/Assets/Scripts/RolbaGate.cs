using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Vrata garaze snezne rolby: zavrena - neprojde se jimi a nestrili se skrz. Prorazit je jde jen rolbou: kdyz se jich
// rolba (obrys s radlici a frezou) za jizdy dotkne, server je oznaci za proborena (MatchManager.rolbaGateBroken)
// a na vsech pocitacich kridla vrat odletí. Na zacatku zapasu jsou zase cela.
// Ridic rolby vraty projede vzdy (kolize s jeho postavou je vypnuta) - jinak by se o ne zasekl driv, nez je server prorazi.
public class RolbaGate : MonoBehaviour
{
    public static RolbaGate Instance { get; private set; }

    readonly List<Transform> leaves = new List<Transform>();
    readonly List<Collider> colliders = new List<Collider>();
    bool shownBroken;
    bool initialized;
    CharacterController ignored;

    void Awake()
    {
        Instance = this;
        foreach (Transform t in transform)
            if (t.name.StartsWith("Kridlo")) leaves.Add(t);
        foreach (var leaf in leaves)
            leaf.GetComponentsInChildren(true, colliders);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Dotyka se rolba vrat? (jen dokud jsou cela)
    public bool TouchedBy(Tractor tractor)
    {
        if (tractor == null || shownBroken) return false;
        foreach (var c in colliders)
        {
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            Bounds b = c.bounds;
            b.Expand(0.4f);
            if (tractor.HullTouches(b)) return true;
        }
        return false;
    }

    void Update()
    {
        var match = MatchManager.Instance;
        bool live = match != null && match.IsSpawned;
        bool broken = live && match.rolbaGateBroken.Value;
        if (!initialized || broken != shownBroken)
        {
            SetBroken(broken, initialized);
            shownBroken = broken;
            initialized = true;
        }

        // ridic rolby vraty projede
        CharacterController driver = null;
        if (live && !broken && match.tractorDriver.Value != ulong.MaxValue)
        {
            var network = NetworkManager.Singleton;
            if (network != null && network.SpawnManager != null
                && network.SpawnManager.SpawnedObjects.TryGetValue(match.tractorDriver.Value, out var obj))
                driver = obj.GetComponent<CharacterController>();
        }
        if (driver != ignored)
        {
            foreach (var c in colliders)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (ignored != null) Physics.IgnoreCollision(c, ignored, false);
                if (driver != null) Physics.IgnoreCollision(c, driver, true);
            }
            ignored = driver;
        }
    }

    void SetBroken(bool broken, bool effects)
    {
        if (broken && effects)
        {
            Vector3 center = transform.position;
            if (leaves.Count > 0)
            {
                center = Vector3.zero;
                foreach (var leaf in leaves) center += leaf.position;
                center /= leaves.Count;
            }
            ProceduralSfx.Play(ProceduralSfx.Explosion, center, 0.8f, 45f, spatial: true);
            ProceduralSfx.Play(ProceduralSfx.Hit, center, 1f, 30f, spatial: true);
            Fx.Sparks(center + Vector3.up * 0.8f, new Color(0.55f, 0.38f, 0.22f));
            var tractor = Tractor.Instance;
            Vector3 push = tractor != null ? tractor.transform.right : -transform.forward;
            push.y = 0f;
            push = push.sqrMagnitude > 0.01f ? push.normalized : Vector3.back;
            foreach (var leaf in leaves)
                if (leaf.gameObject.activeSelf) Debris(leaf, push);
        }
        foreach (var leaf in leaves)
            leaf.gameObject.SetActive(!broken);
        ignored = null;
    }

    // Kridlo odletí (jen vizual na tomto pocitaci, bez kolize) a po chvili zmizi.
    static void Debris(Transform leaf, Vector3 push)
    {
        var copy = Instantiate(leaf.gameObject, leaf.position, leaf.rotation);
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Destroy(c);
        copy.AddComponent<FlyingPlank>().velocity = push * Random.Range(6f, 9f) + Vector3.up * Random.Range(3f, 5f)
            + new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));
        Destroy(copy, 3.5f);
    }

    class FlyingPlank : MonoBehaviour
    {
        public Vector3 velocity;
        Vector3 spin;
        float groundY;

        void Start()
        {
            spin = new Vector3(Random.Range(-400f, 400f), Random.Range(-200f, 200f), Random.Range(-400f, 400f));
            groundY = transform.position.y - 1f;
        }

        void Update()
        {
            velocity += Physics.gravity * Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            if (transform.position.y < groundY)
            {
                transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
                velocity = new Vector3(velocity.x * 0.4f, 0f, velocity.z * 0.4f);
                spin *= 0.3f;
            }
            transform.Rotate(spin * Time.deltaTime, Space.Self);
        }
    }
}
