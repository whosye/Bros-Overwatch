using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class SelectedAudioIntegrationChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    static AudioSource[] Playing(AudioClip clip) => Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
        .Where(source => source.clip == clip).ToArray();
    static AudioSource[] SourcesForCue(string cue)
    {
        var clips = Resources.Load<CollectedAudioBank>("CollectedAudioBank").cues.First(row => row.id == cue).clips;
        return Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Where(source => clips.Contains(source.clip)).ToArray();
    }

    [UnityTest]
    public IEnumerator WeaponHitResponseCanOverlapVictimHurt()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        var mirek = AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Mirek.asset");
        var viktor = AssetDatabase.LoadAssetAtPath<HeroDefinition>("Assets/Resources/Heroes/Viktor.asset");
        Assert.IsNotEmpty(mirek.attackLines);
        var ear = Object.FindAnyObjectByType<AudioListener>();
        var objects = new[] { new GameObject("HitResponseAttacker"), new GameObject("HitResponseVictim"), new GameObject("HitResponseBystander") };
        foreach (var go in objects) go.transform.position = ear != null ? ear.transform.position : Vector3.zero;
        var attacker = objects[0].AddComponent<HeroVoice>();
        var victim = objects[1].AddComponent<HeroVoice>();
        var bystander = objects[2].AddComponent<HeroVoice>();
        try
        {
            HeroVoice.SilenceAll();
            // Damage publishes the victim's Hurt RPC before the attacker's Attack RPC.
            victim.Play(viktor, VoiceKind.Hurt, 0, 0);
            var hurt = Field<AudioSource>(victim, "source");
            Assert.IsNotNull(hurt);
            Assert.IsTrue(hurt.isPlaying);
            attacker.Play(mirek, VoiceKind.Attack, 0, 0);
            var response = Field<AudioSource>(attacker, "source");
            Assert.IsNotNull(response, "Victim pain must not discard Mirek's hit response.");
            Assert.IsTrue(response.isPlaying);
            Assert.AreEqual(mirek.attackLines[0], response.clip);
            Assert.IsTrue(hurt.isPlaying, "The response must not stop the victim's pain.");

            response.Stop();
            bystander.PlayLine(viktor.hurtLines[0], 4);
            Assert.IsTrue(Field<AudioSource>(bystander, "source").isPlaying);
            attacker.Play(mirek, VoiceKind.Attack, 0, 0);
            Assert.IsFalse(response.isPlaying, "Two other speakers must still suppress another hit response.");
            attacker.Play(mirek, VoiceKind.Idle, 0, 0);
            Assert.IsFalse(response.isPlaying, "Idle chatter must retain its existing speech limit.");
            yield return null;
        }
        finally
        {
            foreach (var go in objects) Object.DestroyImmediate(go);
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SelectedSoundsReachTheirGameplayEvents()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        var network = NetworkManager.Singleton;
        network.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", 17894);
        Assert.IsTrue(network.StartHost());
        for (int i=0; i<120 && (MatchManager.Instance == null || !MatchManager.Instance.IsSpawned || network.LocalClient.PlayerObject == null); i++)
            yield return null;
        var match = MatchManager.Instance;
        Assert.IsTrue(match.IsSpawned);
        match.enabled = false;
        match.phase.Value = MatchManager.PhasePlaying;
        var actor = network.LocalClient.PlayerObject;
        actor.GetComponent<FirstPersonController>().enabled = false;
        var shooting = actor.GetComponent<WeaponShooting>();
        shooting.enabled = false;
        actor.GetComponent<HeldWeapons>().enabled = false;

        foreach (var pair in new[] { ("Rocket_Data", "grenade_launcher_shoot"), ("SindelPistol_Data", "ambassador_shoot"), ("BardGun_Data", "capper_shoot") })
        {
            shooting.SetWeapon(AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Data/"+pair.Item1+".asset"));
            var clip = (AudioClip)Call(shooting, "ShotClip", false);
            Assert.IsNotNull(clip);
            StringAssert.StartsWith(pair.Item2+"_", clip.name, "Explicitly chosen recording must replace the previous weapon sound.");
        }

        shooting.SetWeapon(AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Data/AnnaRifle_Data.asset"));
        Call(shooting, "PlayShot", actor.transform.position, false, .5f);
        Assert.IsNotEmpty(SourcesForCue("david_impact.fire"), "The chosen crossbow release is a biotic firing layer, never a random contact sound.");

        shooting.SetWeapon(AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Data/Bow_Data.asset"));
        Call(shooting, "PlayBowDrawAudio", false);
        var draw = Field<AudioSource>(shooting, "bowDrawAudio");
        Assert.IsNotNull(draw);
        StringAssert.StartsWith("bow_shoot_pull_", draw.clip.name);
        Assert.AreEqual(actor.transform, draw.transform.parent);
        yield return null;
        Assert.IsTrue(draw.isPlaying);
        Call(shooting, "StopBowDrawAudio");
        Assert.IsFalse(draw.isPlaying, "Releasing or cancelling a bow must stop the draw recording.");
        Call(shooting, "PlayBowDrawAudio", true);
        var shortDraw = Field<AudioSource>(shooting, "bowDrawAudio");
        StringAssert.StartsWith("bow_shoot_pull_short_", shortDraw.clip.name);
        shooting.SetWeapon(AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/Data/Pistol_Data.asset"));
        Assert.IsNull(Field<AudioSource>(shooting, "bowDrawAudio"), "Hero/weapon switch must clean up drawing.");
        StringAssert.StartsWith("bow_shoot_", ProceduralSfx.BowRelease.name);
        Assert.IsFalse(ProceduralSfx.BowRelease.name.Contains("pull"));
        foreach (string material in new[] { "wood", "metal", "stone", "body" })
            Assert.IsNotNull(CollectedAudioBank.Pick("mirek_weapon.impact."+material));
        var arrow = new GameObject("SelectedArrowAudioCheck");
        var flight = ProceduralSfx.PlayArrowFlight(arrow.transform);
        StringAssert.StartsWith("arrow_nearmiss", flight.clip.name);
        Assert.IsFalse(flight.loop);
        Object.Destroy(arrow);

        var trap = actor.GetComponent<TrapAbility>();
        var snap = CollectedAudioBank.Pick("honza_trap");
        Call(trap, "SnappedClientRpc", actor.transform.position, .5f);
        Assert.AreEqual(1, Playing(snap).Length, "A snapped trap must play the selected steel-jaw recording once.");

        var boulder = actor.GetComponent<BoulderAbility>();
        Call(boulder, "ShowBoulder", actor.transform.position+Vector3.forward*5);
        var roll = Field<AudioSource>(boulder, "rollAudio");
        Assert.IsNotNull(roll);
        Assert.IsTrue(roll.loop);
        StringAssert.StartsWith("honza_boulder_roll_", roll.clip.name);
        var blast = CollectedAudioBank.Pick("honza_ult.explode");
        Call(boulder, "ExplodeFxClientRpc", actor.transform.position+Vector3.forward*5);
        Assert.AreEqual(1, Playing(blast).Length);
        Object.Destroy(Field<GameObject>(boulder, "boulder"));

        var hook = HookVisual.Spawn(actor.transform, null, actor.transform.position, Vector3.forward, 10f);
        Call(hook, "Launch");
        var setState = typeof(HookVisual).GetMethod("SetState", Private);
        setState.Invoke(hook, new[] { Enum.Parse(setState.GetParameters()[0].ParameterType, "Retract") });
        var reel = Field<AudioSource>(hook, "reelSource");
        Assert.IsNotNull(reel);
        StringAssert.StartsWith("grappling_hook_reel_start_", reel.clip.name);
        Object.Destroy(hook.gameObject);
        yield return null;
        Assert.IsTrue(reel == null || !reel.isPlaying, "Returning/cancelling the hook must stop reeling.");
        Assert.IsNotEmpty(Playing(CollectedAudioBank.Pick("pova_hook.stop")));

        var grapple = actor.GetComponent<GrappleAbility>();
        Call(grapple, "RopeClientRpc", actor.transform.position+Vector3.forward*5, .15f);
        Assert.IsNotEmpty(Playing(CollectedAudioBank.Pick("kondulic_hook.throw")));
        yield return new WaitForSeconds(.1f);
        Assert.IsNotEmpty(Playing(CollectedAudioBank.Pick("kondulic_hook.reel")));
        yield return new WaitForSeconds(.3f);
        Assert.IsNotEmpty(Playing(CollectedAudioBank.Pick("kondulic_hook.stop")));

        var shadow = actor.GetComponent<MaxAbility>();
        Call(shadow, "ShadowBurst");
        Assert.IsNotEmpty(Playing(CollectedAudioBank.Pick("max_shadow")));
        Call(shadow, "SpinClientRpc");
        Assert.IsNotEmpty(SourcesForCue("max_slash"));
        Call(actor.GetComponent<TrespassAbility>(), "BeginClientRpc", ulong.MaxValue);
        Assert.IsNotEmpty(Playing(CollectedAudioBank.Pick("max_ult")));
        Assert.IsNotNull(CollectedAudioBank.Pick("david_sleep.fire"));
        Assert.IsNotNull(CollectedAudioBank.Pick("david_impact.fire"));
        Assert.IsNotNull(CollectedAudioBank.Pick("viktor_ult.lock"));
        Assert.That(CollectedAudioBank.Pick("ui_capture_tick").length, Is.LessThan(.2f), "Capture tick must be a single tick, not a multi-second clock loop.");

        int slivovice = PickupSpot.Sorted.FindIndex(spot => spot.kind == PickupKind.Invulnerable);
        Assert.GreaterOrEqual(slivovice, 0);
        var burp = CollectedAudioBank.Pick("pickup_invulnerable.burp");
        Assert.IsEmpty(Playing(burp));
        Call(match, "PickupTakenClientRpc", slivovice, actor.transform.position, actor.NetworkObjectId);
        Assert.IsNotEmpty(Playing(CollectedAudioBank.Pick("pickup_invulnerable.drink")));
        Assert.IsEmpty(Playing(burp), "Burp must come after drinking, not replace pickup confirmation.");
        float burpDeadline=Time.time+CollectedAudioBank.Pick("pickup_invulnerable.drink").length+2f;
        while (Playing(burp).Length==0 && Time.time<burpDeadline)
        {
            Assert.IsFalse(match.IsLobby, "Match returned to lobby before the drink finished.");
            Assert.IsFalse(match.IsOver, "Match ended before the drink finished.");
            Assert.IsFalse(ReplayPlayer.Active, "Replay interrupted the live drink sequence.");
            yield return null;
        }
        Assert.IsNotEmpty(Playing(burp), "Drinking must be followed by the selected burp.");
        network.Shutdown();
        yield return null;
        yield return new ExitPlayMode();
    }
}
