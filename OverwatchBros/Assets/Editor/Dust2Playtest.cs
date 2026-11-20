using System;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Davkovy smoke test skutecneho hosta. Posloucha jen na loopbacku a nemení ulozenou scenu.
[InitializeOnLoad]
public static class Dust2Playtest
{
    const string Key = "Dust2Playtest.";
    static Dust2Playtest()
    {
        if (SessionState.GetBool(Key + "running", false) || SessionState.GetBool(Key + "exiting", false))
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += RecordError;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) PrepareInput();
            };
        }
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(Key + "running", true);
        SessionState.SetBool(Key + "exiting", false);
        SessionState.SetInt(Key + "stage", 0);
        SessionState.SetInt(Key + "errors", 0);
        SessionState.SetFloat(Key + "started", (float)EditorApplication.timeSinceStartup);
        EditorApplication.isPaused = false;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= RecordError;
        Application.logMessageReceived += RecordError;
        EditorApplication.isPlaying = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void PrepareInput()
    {
        if (!SessionState.GetBool(Key + "running", false)) return;
        if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
        if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
        Application.runInBackground = true;
        GameSettings.ForceCursorLocked = true;
    }

    static void RecordError(string message, string trace, LogType type)
    {
        if (SessionState.GetBool(Key + "running", false)
            && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
        {
            SessionState.SetInt(Key + "errors", SessionState.GetInt(Key + "errors", 0) + 1);
            SessionState.SetString(Key + "lastError", message);
        }
    }

    static void Tick()
    {
        if (SessionState.GetBool(Key + "exiting", false))
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool(Key + "exiting", false);
                EditorApplication.Exit(SessionState.GetInt(Key + "exitCode", 1));
            }
            return;
        }
        if (!SessionState.GetBool(Key + "running", false)) return;
        try
        {
            Require(EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "started", 0) < 120,
                "Test prekrocil casovy limit.");
            if (!EditorApplication.isPlaying || Time.realtimeSinceStartup < 2f) return;
            PrepareInput();
            var network = NetworkManager.Singleton;
            if (network == null) return;
            int stage = SessionState.GetInt(Key + "stage", 0);
            if (stage == 3)
            {
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(UnityEngine.InputSystem.Key.W));
                InputSystem.Update();
            }
            if (stage == 0)
            {
                Require(!network.IsListening, "Pred testem uz bezi jina sitova relace.");
                var transport = network.GetComponent<UnityTransport>();
                transport.SetConnectionData("127.0.0.1", 17787, "127.0.0.1");
                Require(network.StartHost(), "Host se nespustil.");
                SessionState.SetInt(Key + "stage", 1);
                return;
            }
            var match = MatchManager.Instance;
            if (match == null || !match.IsSpawned || network.LocalClient?.PlayerObject == null) return;
            if (stage == 1)
            {
                // Scope runtime error checks to the match, after unrelated menu/audio startup.
                SessionState.SetInt(Key + "errors", 0);
                match.SetGameMode(MatchManager.ModeAttack);
                Require(match.IsAttackMode, "Na chatach nelze zvolit dobyvani.");
                match.SetMap(MatchManager.MapDust2);
                Require(match.MapIndex == MatchManager.MapChaty, "Server povolil Dust II pro dobyvani.");
                match.SetGameMode(MatchManager.ModeDeathmatch);
                match.SetMap(MatchManager.MapDust2);
                Require(match.MapIndex == MatchManager.MapDust2, "Server nepovolil Dust II pro deathmatch.");
                match.SetGameMode(MatchManager.ModeAttack);
                Require(!match.IsAttackMode, "Na Dust II lze zapnout dobyvani.");
                // I kdyby nekdo zmenil primo sitovou promennou, server nesmi zapas spustit.
                match.gameMode.Value = MatchManager.ModeAttack;
                match.StartMatch();
                Require(match.IsLobby, "Server spustil nepodporovanou kombinaci mapy a rezimu.");
                match.gameMode.Value = MatchManager.ModeDeathmatch;
                match.StartMatch();
                Require(!match.IsLobby, "Platny deathmatch se nespustil.");
                match.ServerAddBot(0, 1);
                Require(BotBrain.All.Count == 1, "Bot se nepridal.");
                SessionState.SetFloat(Key + "matchStarted", (float)EditorApplication.timeSinceStartup);
                SessionState.SetInt(Key + "stage", 2);
                return;
            }
            if (stage < 2 || EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "matchStarted", 0) < (stage == 2 ? 4 : 0.75))
                return;
            var player = network.LocalClient.PlayerObject.transform;
            var bot = BotBrain.All[0].transform;
            var bounds = MatchManager.ActiveMapBounds;
            Require(bounds.Contains(player.position), "Hrac neni na Dust II.");
            Require(bounds.Contains(bot.position), "Bot neni na Dust II.");
            Require(Physics.Raycast(player.position + Vector3.up * 0.1f, Vector3.down, 0.65f, ~0,
                QueryTriggerInteraction.Ignore), "Hrac po startu nestoji na podlaze.");
            Require(BotNavigation.Ready, "Boti nemaji navigaci.");
            Require(match.MapIndex == MatchManager.MapDust2 && !match.IsAttackMode && !match.IsLobby,
                "Rezim nebo mapa se po startu zmenily.");
            if (stage == 2)
            {
                SessionState.SetFloat(Key + "walkX", player.position.x);
                SessionState.SetFloat(Key + "walkZ", player.position.z);
                SpawnZone.TryGetCenter(1, out var enemyBase);
                var direction = enemyBase - player.position;
                float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                player.GetComponent<FirstPersonController>().OwnerTeleport(player.position, yaw);
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(UnityEngine.InputSystem.Key.W));
                InputSystem.Update();
                SessionState.SetFloat(Key + "matchStarted", (float)EditorApplication.timeSinceStartup);
                SessionState.SetInt(Key + "stage", 3);
                return;
            }
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            var walked = new Vector2(player.position.x - SessionState.GetFloat(Key + "walkX", 0),
                player.position.z - SessionState.GetFloat(Key + "walkZ", 0));
            Debug.Log($"[Dust II test] Pohyb {walked.magnitude:0.000} m; W={Keyboard.current.wKey.isPressed}, "
                + $"device={Keyboard.current.enabled}, frozen={player.GetComponent<FirstPersonController>().CannotAct}, "
                + $"dt={Time.deltaTime}, paused={EditorApplication.isPaused}, timeScale={Time.timeScale}.");
            Require(walked.magnitude > 0.2f, "Hrac se po spawnu nemuze pohnout.");
            Require(SessionState.GetInt(Key + "errors", 0) == 0,
                "Behem zapasu vznikly runtime chyby: " + SessionState.GetString(Key + "lastError", ""));
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/Dust2"));
            CaptureView(player.GetComponent<FirstPersonController>().playerCamera, Path.Combine(folder, "unity-player-view.png"));
            File.WriteAllText(Path.Combine(folder, "unity-playtest.json"),
                "{\"hostStarted\":true,\"deathmatchStarted\":true,\"captureBlockedOnServer\":true,"
                + "\"playerOnDust2\":true,\"botOnDust2\":true,\"playerGrounded\":true,\"playerWalked\":true,\"matchRuntimeErrors\":0}");
            Debug.Log("[Dust II test] PASS: host, omezeni rezimu, spawn hrace a bota, podlaha a navigace.");
            Finish(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    static void Finish(int exitCode)
    {
        SessionState.SetBool(Key + "running", false);
        SessionState.SetBool(Key + "exiting", true);
        SessionState.SetInt(Key + "exitCode", exitCode);
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
        GameSettings.ForceCursorLocked = false;
        EditorApplication.isPlaying = false;
    }

    static void CaptureView(Camera camera, string path)
    {
        var texture = new RenderTexture(1280, 800, 24);
        texture.Create();
        var previous = RenderTexture.active;
        try
        {
            RenderPipeline.SubmitRenderRequest(camera,
                new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
            RenderTexture.active = texture;
            var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
        finally
        {
            RenderTexture.active = previous;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[Dust II test] " + message);
    }
}
