using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Rozhovory hrdinu: kdyz stoji dva urceni hrdinove u sebe (a zrovna nebojuji), je sance, ze spolu prohodi par vet.
// Nahravky jsou v Resources/Banter/<rozhovor>/ a jdou po sobe podle jmena: "1_max", "2_honza", "3_max"...
// (cislo = poradi, za podtrzitkem jmeno hero assetu toho, kdo mluvi). Ridi server, hraji vsichni klienti.
public partial class MatchManager
{
    // Kdo s kym mluvi (jmena hero assetu) a slozka s nahravkami.
    static readonly (string first, string second, string folder)[] Banters =
    {
        ("Max", "Honza", "max_honza"),
    };

    const float BanterDistance = 7f;        // jak blizko u sebe musi stat
    const float BanterCheckInterval = 2f;
    const float BanterChance = 0.12f;       // sance pri kazde kontrole (kdyz stoji u sebe)
    const float BanterCooldown = 120f;      // stejny rozhovor nejdriv po tolika sekundach
    const float BanterCalmSeconds = 4f;     // ani jeden nesmi chvili predtim dostat zasah

    float nextBanterCheck;
    bool banterRunning;
    readonly Dictionary<string, float> banterReadyAt = new Dictionary<string, float>();

    void ServerBanterTick()
    {
        if (banterRunning || Time.time < nextBanterCheck || IsLobby || IsOver) return;
        nextBanterCheck = Time.time + BanterCheckInterval;

        var heroes = FindObjectsByType<PlayerHero>();
        foreach (var (first, second, folder) in Banters)
        {
            if (banterReadyAt.TryGetValue(folder, out float ready) && Time.time < ready) continue;
            foreach (var a in heroes)
            {
                if (!CanBanter(a, first)) continue;
                foreach (var b in heroes)
                {
                    if (b == a || !CanBanter(b, second)) continue;
                    if (Vector3.Distance(a.transform.position, b.transform.position) > BanterDistance) continue;
                    if (Random.value > BanterChance) continue;

                    banterReadyAt[folder] = Time.time + BanterCooldown;
                    StartCoroutine(RunBanter(folder, a, b, first, second));
                    return;
                }
            }
        }
    }

    static bool CanBanter(PlayerHero hero, string heroAsset)
    {
        if (hero == null || !hero.IsSpawned || hero.IsJoining || hero.Hero == null || hero.Hero.name != heroAsset) return false;
        var health = hero.GetComponent<Health>();
        return health != null && health.currentHealth.Value > 0f && Time.time - health.LastHurtTime > BanterCalmSeconds;
    }

    IEnumerator RunBanter(string folder, PlayerHero a, PlayerHero b, string first, string second)
    {
        banterRunning = true;
        var clips = new List<AudioClip>(Resources.LoadAll<AudioClip>("Banter/" + folder));
        clips.Sort((x, y) => string.CompareOrdinal(x.name, y.name));

        for (int i = 0; i < clips.Count; i++)
        {
            // kdo mluvi: jmeno za podtrzitkem
            string speaker = clips[i].name.Substring(clips[i].name.IndexOf('_') + 1);
            var who = string.Equals(speaker, second, System.StringComparison.OrdinalIgnoreCase) ? b : a;
            // kdyz nekdo z nich mezitim zemrel nebo odesel, rozhovor konci
            if (who == null || !who.IsSpawned) break;
            var health = who.GetComponent<Health>();
            if (health == null || health.currentHealth.Value <= 0f) break;

            who.SayBanterClientRpc(folder, i);
            yield return new WaitForSeconds(clips[i].length + 0.3f);
        }
        banterRunning = false;
    }
}
