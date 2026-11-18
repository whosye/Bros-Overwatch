using Unity.Netcode;
using UnityEngine;

// Nahodni hrdinove (voli host v lobby; testeri: "balancovani bych neresil, kdyz bude random postava"):
//  0 = kazdy si vybere sam (jako dosud),
//  1 = na zacatku zapasu dostane kazdy nahodneho hrdinu,
//  2 = nahodny hrdina na zacatku a po kazde smrti jiny.
// V tymu je kazdy hrdina dal jen jednou. V nahodnych rezimech nejde menit hrdinu (F1).
public partial class MatchManager
{
    public const int HeroesChosen = 0, HeroesRandomMatch = 1, HeroesRandomDeath = 2;
    public static readonly string[] HeroModeNames = { "VLASTNÍ VÝBĚR", "NÁHODNÍ NA ZÁPAS", "NOVÝ PO KAŽDÉ SMRTI" };

    public NetworkVariable<int> heroMode = new NetworkVariable<int>(HeroesChosen);
    public bool RandomHeroes => heroMode.Value != HeroesChosen;

    public void SetHeroMode(int mode)
    {
        if (IsServer && IsLobby) heroMode.Value = Mathf.Clamp(mode, 0, HeroModeNames.Length - 1);
    }

    // Start zapasu: vsem nahodne hrdiny (po tymech bez opakovani).
    void ServerAssignRandomHeroes()
    {
        if (!RandomHeroes) return;
        // po tymech zamichat hrdiny a rozdat po rade (bez docasne "zadneho" hrdiny)
        int count = HeroRegistry.All.Length;
        if (count == 0) return;
        var decks = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
        foreach (var client in PlayerSlots())
        {
            var hero = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerHero>() : null;
            if (hero == null) continue;
            var team = hero.GetComponent<PlayerTeam>();
            int teamId = team != null ? team.teamId.Value : 0;
            if (!decks.TryGetValue(teamId, out var deck) || deck.Count == 0)
            {
                deck = new System.Collections.Generic.List<int>();
                for (int i = 0; i < count; i++) deck.Insert(Random.Range(0, deck.Count + 1), i);
                decks[teamId] = deck;
            }
            hero.heroId.Value = deck[0];
            deck.RemoveAt(0);
            hero.ultCharge.Value = 0f;
        }
    }

    // Smrt v rezimu "novy po kazde smrti": ozivi se uz jako jiny hrdina.
    public void ServerHeroDied(PlayerHero hero)
    {
        if (!IsServer || heroMode.Value != HeroesRandomDeath || IsLobby || IsOver || hero == null) return;
        StartCoroutine(SwapBeforeRespawn(hero));
    }

    // Az tesne pred oziveninim (mrtvola zatim zustava puvodni hrdina, killcam taky).
    System.Collections.IEnumerator SwapBeforeRespawn(PlayerHero hero)
    {
        yield return new WaitForSeconds(Mathf.Max(PlayerRespawn.RespawnSeconds, Killcam.RespawnDelay) - 0.4f);
        if (hero == null || !hero.IsSpawned || IsLobby || IsOver || heroMode.Value != HeroesRandomDeath) yield break;
        var health = hero.GetComponent<Health>();
        if (health != null && health.currentHealth.Value <= 0f) ServerRandomHero(hero, true);
    }

    void ServerRandomHero(PlayerHero hero, bool different)
    {
        int count = HeroRegistry.All.Length;
        if (count == 0) return;
        var team = hero.GetComponent<PlayerTeam>();
        int teamId = team != null ? team.teamId.Value : 0;
        var free = new System.Collections.Generic.List<int>();
        for (int i = 0; i < count; i++)
            if (!PlayerHero.TakenInTeam(i, teamId, hero) && !(different && i == hero.heroId.Value)) free.Add(i);
        if (free.Count == 0)
        {
            if (hero.heroId.Value < 0) hero.heroId.Value = Random.Range(0, count);   // vic hracu nez hrdinu
            return;
        }
        hero.heroId.Value = free[Random.Range(0, free.Count)];
        hero.ultCharge.Value = 0f;
    }
}
