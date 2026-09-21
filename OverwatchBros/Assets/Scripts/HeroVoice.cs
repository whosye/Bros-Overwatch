using UnityEngine;

// Prehrava hlasove hlasky hrdiny (spawn / kill / death). Klipy se berou z HeroDefinition.
// Kdyz zadne klipy nejsou prirazene, nedela nic.
public class HeroVoice : MonoBehaviour
{
    public float volume = 1f;

    public void PlaySpawn(HeroDefinition hero) => PlayRandom(hero != null ? hero.spawnLines : null);

    public void PlayKill(HeroDefinition hero) => PlayRandom(hero != null ? hero.killLines : null);

    public void PlayDeath(HeroDefinition hero) => PlayRandom(hero != null ? hero.deathLines : null);

    void PlayRandom(AudioClip[] lines)
    {
        if (lines == null || lines.Length == 0) return;

        var clip = lines[Random.Range(0, lines.Length)];
        if (clip != null)
            AudioSource.PlayClipAtPoint(clip, transform.position, volume);
    }
}
