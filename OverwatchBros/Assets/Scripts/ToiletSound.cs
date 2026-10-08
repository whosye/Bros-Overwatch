using Unity.Netcode;
using UnityEngine;

// Zachod ve velke chate: kdyz kdokoli vkroci do mistnosti se zachodem (mezi jejimi zdmi, ve stejnem patre),
// prehraje se nahodne jeden ze zvuku Resources/Audio/Toilet (s ozvenou) - slyset je po cele chate.
// Kazdy klient hlida polohy hracu sam, takze se nic neposila po siti.
// Komponentu ma objekt "Zachod" (vlozeny pres BrosOverwatch > Mapa > Vlozit zachod); kdyz chybi, prida se pri startu.
public class ToiletSound : MonoBehaviour
{
    public float roomRadius = 6f;        // nejvetsi rozmer mistnosti od zachodu (kdyz zed nenajde)
    public float cooldown = 4f;          // nejkratsi pauza mezi zvuky

    const float FloorTolerance = 1.6f;   // vyskovy rozdil chodidel a zachodu (jine patro se nepocita)
    const float HearDistance = 38f;      // slyset po cele chate

    static AudioClip[] clips;

    readonly System.Collections.Generic.HashSet<ulong> inside = new System.Collections.Generic.HashSet<ulong>();
    AudioSource source;
    float nextAllowed;

    // mistnost: obdelnik mezi nejblizsimi zdmi kolem zachodu (svet, osy x/z), zmereny pri startu
    float minX, maxX, minZ, maxZ;
    bool roomMeasured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AttachToToilets()
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            if (t.name == "Zachod" && t.GetComponent<ToiletSound>() == null)
                t.gameObject.AddComponent<ToiletSound>();
    }

    void Awake()
    {
        if (clips == null) clips = Resources.LoadAll<AudioClip>("Audio/Toilet");

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 10f;
        source.maxDistance = HearDistance;
        source.dopplerLevel = 0f;
        source.volume = 1f;
    }

    void Update()
    {
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsListening || clips == null || clips.Length == 0) return;

        if (!roomMeasured) MeasureRoom();
        Vector3 seat = transform.position;
        foreach (var player in FindObjectsByType<PlayerHero>())
        {
            if (player == null || !player.IsSpawned) continue;
            ulong id = player.NetworkObjectId;
            Vector3 feet = player.transform.position;
            bool isInside = feet.x > minX && feet.x < maxX && feet.z > minZ && feet.z < maxZ
                && Mathf.Abs(feet.y - seat.y) <= FloorTolerance;

            if (isInside && inside.Add(id))
            {
                if (Time.time >= nextAllowed)
                {
                    nextAllowed = Time.time + cooldown;
                    source.clip = clips[Random.Range(0, clips.Length)];
                    source.Play();
                }
            }
            else if (!isInside)
            {
                inside.Remove(id);
            }
        }
    }

    // Paprsky ze zachodu (ve vysce 1,3 m, nad nabytkem) do ctyr svetovych stran; do kazdeho smeru tri vedle sebe
    // a plati nejkratsi - jinak by mistnost "vytekla" dvermi. Hraci a spoustece se ignoruji.
    void MeasureRoom()
    {
        roomMeasured = true;
        Vector3 origin = transform.position + Vector3.up * 1.3f;
        minX = origin.x - Reach(origin, Vector3.left, Vector3.forward);
        maxX = origin.x + Reach(origin, Vector3.right, Vector3.forward);
        minZ = origin.z - Reach(origin, Vector3.back, Vector3.right);
        maxZ = origin.z + Reach(origin, Vector3.forward, Vector3.right);
    }

    float Reach(Vector3 origin, Vector3 direction, Vector3 side)
    {
        float best = roomRadius;
        foreach (float wanted in new[] { -0.6f, 0f, 0.6f })
        {
            // posun do strany jen po nejblizsi zed (jinak by paprsek zacinal za ni v sousedni mistnosti)
            float offset = wanted;
            if (wanted != 0f && Physics.Raycast(origin, side * Mathf.Sign(wanted), out var wall, Mathf.Abs(wanted), ~0, QueryTriggerInteraction.Ignore)
                && !wall.collider.transform.IsChildOf(transform))
                offset = Mathf.Sign(wanted) * Mathf.Max(0f, wall.distance - 0.05f);
            var hits = Physics.RaycastAll(origin + side * offset, direction, roomRadius, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.collider.GetComponentInParent<PlayerHero>() != null) continue;
                best = Mathf.Min(best, hit.distance);
            }
        }
        return best;
    }
}
