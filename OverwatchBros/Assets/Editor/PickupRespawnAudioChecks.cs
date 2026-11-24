using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

public sealed class PickupRespawnAudioChecks
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BatchInput()
    {
        string test = SessionState.GetString("CollectedAudio.PlayCheck.Active.Test", "");
        if (!Application.isBatchMode || !(test.StartsWith("PickupRespawnAudioChecks.") || test.StartsWith("SelectedAudioIntegrationChecks."))) return;
        // A hidden batch editor has no physical input devices; gameplay expects both to exist.
        if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
        if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
    }

    static AudioSource[] Sounds(AudioClip clip) => Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
        .Where(source => source.clip == clip).ToArray();

    [UnityTest]
    public IEnumerator RespawnPlaysOnceAtPickupAndResetStaysSilent()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        var clip = CollectedAudioBank.Pick("pickup_respawn");
        Assert.IsNotNull(clip, "spawn_item must be bundled in Resources' audio bank.");
        Assert.That(clip.length, Is.InRange(0.9f, 1.1f));
        var network = NetworkManager.Singleton;
        Assert.IsNotNull(network);
        network.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", 17893);
        Assert.IsTrue(network.StartHost());
        for (int i = 0; i < 120 && (MatchManager.Instance == null || !MatchManager.Instance.IsSpawned); i++)
            yield return null;
        var match = MatchManager.Instance;
        Assert.IsNotNull(match);
        Assert.IsTrue(match.IsSpawned);
        Assert.IsTrue(match.IsLobby);
        Assert.IsEmpty(Sounds(clip), "Initial map/network spawn must stay silent.");
        var spots = PickupSpot.Sorted;
        Assert.IsNotEmpty(spots);
        int index = spots.FindIndex(spot => spot.kind == PickupKind.Health);
        Assert.That(index, Is.InRange(0, 63));
        var spot = spots[index];
        // Keep this test independent of other gameplay systems and players taking pickups.
        match.enabled = false;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var tick = typeof(MatchManager).GetMethod("ServerPickupTick", flags);
        var nextTick = typeof(MatchManager).GetField("nextPickupTick", flags);
        var deadlines = (float[])typeof(MatchManager).GetField("pickupRespawnAt", flags).GetValue(match);
        ulong bit = 1UL << index;
        match.pickupMask.Value &= ~bit;
        deadlines[index] = Time.time + 10f;
        match.phase.Value = MatchManager.PhasePlaying;
        nextTick.SetValue(match, 0f);
        tick.Invoke(match, null);
        Assert.IsFalse(match.PickupAvailable(index));
        Assert.IsEmpty(Sounds(clip), "Hidden pickup must stay silent before its deadline.");

        deadlines[index] = Time.time - 1f;
        nextTick.SetValue(match, 0f);
        tick.Invoke(match, null);
        Assert.IsTrue(match.PickupAvailable(index));
        var sources = Sounds(clip);
        Assert.AreEqual(1, sources.Length, "Host must receive exactly one respawn event.");
        Assert.That(Vector3.Distance(spot.transform.position, sources[0].transform.position), Is.LessThan(0.01f));
        Assert.AreEqual(1f, sources[0].spatialBlend);
        Assert.AreEqual(18f, sources[0].maxDistance);
        Assert.AreEqual(0.65f, sources[0].volume);
        yield return null;
        Assert.IsTrue(sources[0].isPlaying, "The imported clip must play in Unity's audio engine.");
        nextTick.SetValue(match, 0f);
        tick.Invoke(match, null);
        Assert.AreEqual(1, Sounds(clip).Length, "Later ticks must not replay an already available pickup.");
        foreach (var source in Sounds(clip)) Object.Destroy(source.gameObject);
        yield return null;

        match.pickupMask.Value &= ~bit;
        typeof(MatchManager).GetMethod("ServerResetPickups", flags).Invoke(match, null);
        Assert.IsTrue(match.PickupAvailable(index));
        Assert.IsEmpty(Sounds(clip), "Round reset must not emit a map-wide sound burst.");

        match.pickupMask.Value &= ~bit;
        deadlines[index] = Time.time - 1f;
        match.phase.Value = MatchManager.PhaseLobby;
        nextTick.SetValue(match, 0f);
        tick.Invoke(match, null);
        Assert.IsTrue(match.PickupAvailable(index));
        Assert.IsEmpty(Sounds(clip), "Lobby refresh must stay silent.");
        network.Shutdown();
        yield return null;
        yield return new ExitPlayMode();
    }
}
