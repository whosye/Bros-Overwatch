using UnityEngine;
using UnityEngine.Video;

// Easter egg "Respin Joker": kdo ma televizi s automatem (VideoPlayer v obyvaku) 5 s v kuse na obrazovce,
// dostane na 20 s cooldowny schopnosti nejvys 1 s (ultimatka se nabiji normalne). Jen mistni hrac (vlastnik).
public class JokerTvBuff : MonoBehaviour
{
    const float WatchSeconds = 5f;
    const float BuffSeconds = 20f;
    const float MaxDistance = 15f;
    static readonly Color Gold = new Color(1f, 0.82f, 0.2f);

    FirstPersonController fpc;
    Renderer[] screens;
    float watched;
    float nextScreenSearch;

    // Novy start hry (i v editoru bez reloadu domeny): buff z minula neplati.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetBuff() => AbilityDefinition.FastCooldownUntil = 0f;

    void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
    }

    void Update()
    {
        if (fpc == null || fpc.playerCamera == null) return;

        if (screens == null || Time.time >= nextScreenSearch)
        {
            nextScreenSearch = Time.time + 5f;
            var players = FindObjectsByType<VideoPlayer>();
            screens = new Renderer[players.Length];
            for (int i = 0; i < players.Length; i++)
                screens[i] = players[i].targetMaterialRenderer != null ? players[i].targetMaterialRenderer : players[i].GetComponent<Renderer>();
        }

        // Behem buffu se nepocita (novy buff az po skonceni toho stareho).
        bool buffed = Time.time < AbilityDefinition.FastCooldownUntil;
        if (buffed || fpc.IsDead || !Watching())
        {
            watched = 0f;
            return;
        }

        watched += Time.deltaTime;
        if (watched < WatchSeconds) return;

        watched = 0f;
        AbilityDefinition.FastCooldownUntil = Time.time + BuffSeconds;
        CaptureUI.Announce("Je z tebe RESPIN JOKER", Gold, 3f);
        PlayMusic();
    }

    // Hudba buffu: vlastni nahravka Assets/Resources/Audio/buff_respin_joker (mp3/wav/ogg), jinak zastupna znelka.
    // Slysi ji jen tenhle hrac; delsi hudba na konci buffu plynule ztichne.
    AudioSource music;

    void PlayMusic()
    {
        if (music == null)
        {
            music = gameObject.AddComponent<AudioSource>();
            music.spatialBlend = 0f;
            music.playOnAwake = false;
        }
        var custom = Resources.Load<AudioClip>("Audio/buff_respin_joker");
        music.clip = custom != null ? custom : ProceduralSfx.JokerJingle;
        music.volume = 0.8f;
        music.Play();
    }

    void LateUpdate()
    {
        if (music == null || !music.isPlaying) return;
        float left = AbilityDefinition.FastCooldownUntil - Time.time;
        if (left < 1.5f)
            music.volume = Mathf.Max(0f, left / 1.5f) * 0.8f;
        if (left <= 0f) music.Stop();
    }

    // Obrazovka je v zaberu (spis uprostred), blizko a nic ji nezakryva.
    bool Watching()
    {
        var cam = fpc.playerCamera;
        Vector3 eye = cam.transform.position;
        foreach (var screen in screens)
        {
            if (screen == null || !screen.enabled || !screen.gameObject.activeInHierarchy) continue;
            Vector3 center = screen.bounds.center;
            float distance = Vector3.Distance(eye, center);
            if (distance > MaxDistance) continue;

            Vector3 view = cam.WorldToViewportPoint(center);
            if (view.z <= 0f || view.x < 0.15f || view.x > 0.85f || view.y < 0.15f || view.y > 0.85f) continue;

            // Zakryti: prvni prekazka na ceste k obrazovce nesmi byt bliz nez obrazovka (vlastni telo se preskoci).
            bool blocked = false;
            foreach (var hit in Physics.RaycastAll(eye, (center - eye) / distance, distance - 0.3f, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(transform)) { blocked = true; break; }
            if (!blocked) return true;
        }
        return false;
    }
}
