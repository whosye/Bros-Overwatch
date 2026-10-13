using System;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Opt-in integration check in a disposable Play Mode scene; never saves test objects.
[InitializeOnLoad]
public static class SniperTrailVerification
{
    const string Key = "SniperTrailVerification.Run";
    const string Output = "Temp/SniperTrailQA";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static StringBuilder report;
    static double started, stageStarted;
    static int stage, sniper;
    static NetworkManager network;
    static SniperShotTrail own, enemy;
    static LineRenderer ownSmoke, enemySmoke;
    static float initialAlpha, initialWidth;
    static Vector3 initialFrom;
    static GameObject bot;

    static SniperTrailVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false)) Begin();
            if (state == PlayModeStateChange.EnteredEditMode) SessionState.EraseBool(Key);
        };
    }

    [MenuItem("BrosOverwatch/Overit stopy odstrelovacky")]
    public static void Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void Begin()
    {
        report = new StringBuilder(); stage = 0;
        started = stageStarted = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        try
        {
            network = NetworkManager.Singleton;
            if (!network || network.IsListening) throw new InvalidOperationException("Test needs an idle scene NetworkManager.");
            // Local-only test host; the original configuration is restored when leaving Play Mode.
            network.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", 17983, "127.0.0.1");
            if (!network.StartHost()) throw new InvalidOperationException("Test host could not start.");
        }
        catch (Exception e) { Check(false, e.ToString()); Finish(); }
    }

    static void Check(bool ok, string message) => report.AppendLine((ok ? "PASS " : "FAIL ") + message);
    static void Next() { stage++; stageStarted = EditorApplication.timeSinceStartup; }
    static LineRenderer Smoke(SniperShotTrail trail) => trail.transform.Find("Smoke").GetComponent<LineRenderer>();

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - started > 45) throw new TimeoutException("Sniper trail verification timed out.");
            if (stage == 0 && network.LocalClient?.PlayerObject != null && now - stageStarted > 0.5)
            {
                sniper = Array.FindIndex(HeroRegistry.All, h => h.name == "Sniper");
                if (sniper < 0) throw new InvalidOperationException("Sniper hero missing.");
                var local = network.LocalClient.PlayerObject.gameObject;
                local.GetComponent<PlayerTeam>().teamId.Value = 0;
                bot = Object.Instantiate(network.NetworkConfig.PlayerPrefab, new Vector3(503, 500, 500), Quaternion.identity);
                bot.AddComponent<BotBrain>().enabled = false;
                bot.GetComponent<NetworkObject>().Spawn();
                bot.GetComponent<PlayerHero>().isBot.Value = true;
                bot.GetComponent<PlayerHero>().heroId.Value = sniper;
                bot.GetComponent<PlayerTeam>().teamId.Value = 1;
                var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
                target.name = "Sniper verification backstop";
                target.transform.position = new Vector3(500, 501, 530);
                target.transform.localScale = new Vector3(12, 5, 1);
                Fx.Paint(target, new Color(0.25f, 0.3f, 0.34f));
                Physics.SyncTransforms();
                Fire(local, new Vector3(497, 501, 500), 1f);
                Fire(bot, new Vector3(503, 501, 500), 0.4f);
                var trails = Object.FindObjectsByType<SniperShotTrail>();
                Check(trails.Length == 2, "Actual scoped shots create one trail each; host RPC does not duplicate prediction");
                foreach (var trail in trails)
                    if (Smoke(trail).GetPosition(0).x < 500) own = trail; else enemy = trail;
                ownSmoke = Smoke(own); enemySmoke = Smoke(enemy);
                Check(ownSmoke.startColor.g > 0.8f && ownSmoke.startColor.b > 0.8f, "Local player's shot is smoke white");
                Check(enemySmoke.startColor.r > 0.95f && enemySmoke.startColor.g < 0.2f, "Enemy host-owned bot's shot is red");
                Check(Mathf.Abs(ownSmoke.GetPosition(1).z - 529.5f) < 0.01f
                    && Mathf.Abs(enemySmoke.GetPosition(1).z - 529.5f) < 0.01f, "Full and partial-charge trails stop at the actual wall hit");
                initialAlpha = enemySmoke.startColor.a; initialWidth = enemySmoke.widthMultiplier;
                initialFrom = enemySmoke.GetPosition(0);
                bot.transform.position += Vector3.right * 10;
                Check(Render("fresh") > 100, "Enemy trail is visibly red in the rendered frame");
                Check(Render("occluded", true) < 5, "Opaque cover hides the trail; it cannot reveal shooters through walls");
                Next();
            }
            else if (stage == 1 && now - stageStarted > 0.85)
            {
                Check(own && enemy && enemySmoke.startColor.a > 0.1f && enemySmoke.startColor.a < initialAlpha,
                    "Afterimage remains visible and fades well after the former 0.35-second cutoff");
                Check(enemySmoke.widthMultiplier > initialWidth, "Smoke softly disperses as it fades");
                Check(enemySmoke.GetPosition(0) == initialFrom, "Afterimage stays at the shot origin when the shooter moves");
                Render("fading");
                Next();
            }
            else if (stage == 2 && now - stageStarted > 1.6)
            {
                Check(!own && !enemy, "Both charge strengths disappear completely after their lifetime");
                var localTeam = network.LocalClient.PlayerObject.GetComponent<PlayerTeam>();
                localTeam.teamId.Value = 1;
                Fx.SniperTracer(new Vector3(497, 501, 500), new Vector3(497, 501, 529.5f), 0, true);
                var opposite = Object.FindAnyObjectByType<SniperShotTrail>();
                Check(Smoke(opposite).startColor.g < 0.2f, "Enemy color reverses correctly for a team-1 viewer");
                Object.DestroyImmediate(opposite.gameObject);
                Fx.SniperTracer(new Vector3(497, 501, 500), new Vector3(497, 501, 529.5f), 1, false);
                var ally = Object.FindAnyObjectByType<SniperShotTrail>();
                Check(Smoke(ally).startColor.g > 0.8f, "Teammate trail uses the neutral smoke color");
                Object.DestroyImmediate(ally.gameObject);
                localTeam.teamId.Value = 0;
                TestReplay();
                Next();
            }
            else if (stage == 3 && now - stageStarted > 0.15)
            {
                Check(ReplayPlayer.PovTeam == 1, "Killcam uses the killer's team instead of the recording victim's team");
                var trail = Object.FindAnyObjectByType<SniperShotTrail>();
                Check(trail && Smoke(trail).startColor.g > 0.8f && Smoke(trail).startColor.a < 0.7f,
                    "Killcam recolors its shooter's trail white and restores the age of a pre-clip shot");
                ReplayPlayer.Stop();
                Fx.Tracer(Vector3.zero, Vector3.forward, Color.cyan, true);
                Next();
            }
            else if (stage == 4 && now - stageStarted > 0.6)
            {
                Check(Object.FindObjectsByType<SniperShotTrail>().Length == 0, "Replay trail is cleaned up on return to live play");
                Check(Object.FindObjectsByType<TracerFade>().Length == 0, "Generic Blink streak keeps its short lifetime");
                Finish();
            }
        }
        catch (Exception e) { Check(false, e.ToString()); Finish(); }
    }

    static void Fire(GameObject actor, Vector3 origin, float charge)
    {
        var brain = actor.GetComponent<BotBrain>() ?? actor.AddComponent<BotBrain>();
        brain.enabled = false;
        actor.GetComponent<FirstPersonController>().enabled = false;
        var shooting = actor.GetComponent<WeaponShooting>();
        shooting.enabled = false;
        shooting.Bot = brain;
        shooting.SetWeapon(HeroRegistry.Get(sniper).weapon);
        var camera = new GameObject("Sniper verification aim").AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.SetPositionAndRotation(origin, Quaternion.identity);
        shooting.playerCamera = camera;
        typeof(BotBrain).GetMethod("SetFire", Private).Invoke(brain, new object[] { true });
        typeof(WeaponShooting).GetField("scopedCharge", Private).SetValue(shooting, charge);
        typeof(WeaponShooting).GetMethod("UpdateScoped", Private).Invoke(shooting, null);
        typeof(BotBrain).GetMethod("SetFire", Private).Invoke(brain, new object[] { false });
    }

    static void TestReplay()
    {
        var clip = new ReplayClip { povId = 10, duration = 3 };
        for (int i = 0; i < 2; i++)
            clip.frames.Add(new ReplayFrame
            {
                time = i * 3, hudTeam = 0, hudHero = (short)sniper, fov = 70,
                cameraPosition = new Vector3(487, 505, 505), cameraRotation = Quaternion.identity,
                slots = Array.Empty<ReplaySlot>(),
                players = new[] { new ReplayPlayerSnap { id = 10, hero = (short)sniper, team = 1,
                    position = new Vector3(503, 500, 500), health = 100, maxHealth = 100, flags = ReplayPlayerSnap.Alive } }
            });
        clip.events.Add(new ReplayEvent { type = ReplayEventType.SniperTracer, time = -0.7f,
            a = 1, flag = true, p0 = new Vector3(503, 501, 500), p1 = new Vector3(503, 501, 529.5f) });
        clip = ReplayClip.Deserialize(clip.Serialize());
        Check(clip.events[0].a == 1 && clip.events[0].type == ReplayEventType.SniperTracer,
            "Replay serialization preserves shooter team and new tracer event");
        ReplayPlayer.Play(clip, 11);
    }

    static int Render(string name, bool occluded = false)
    {
        Directory.CreateDirectory(Output);
        var camera = new GameObject("Sniper trail verification camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = new Vector3(487, 505, 503);
        camera.transform.LookAt(new Vector3(500, 501, 515));
        camera.fieldOfView = 65; camera.farClipPlane = 80;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.065f, 0.075f, 0.09f);
        GameObject cover = null;
        if (occluded)
        {
            cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cover.transform.SetPositionAndRotation(camera.transform.position + camera.transform.forward * 2, camera.transform.rotation);
            cover.transform.localScale = new Vector3(12, 12, 0.2f);
            Fx.Paint(cover, new Color(0.2f, 0.24f, 0.3f));
        }
        var texture = new RenderTexture(1280, 800, 24);
        var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
            int redPixels = 0;
            foreach (var pixel in image.GetPixels32())
                if (pixel.r > 70 && pixel.r > pixel.g * 2 && pixel.r > pixel.b * 2) redPixels++;
            return redPixels;
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.DestroyImmediate(image); texture.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(camera.gameObject);
            if (cover) Object.DestroyImmediate(cover);
        }
    }

    static void Finish()
    {
        EditorApplication.update -= Tick;
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/verification.txt", report.ToString());
        Debug.Log("[Sniper trail QA]\n" + report);
        ReplayPlayer.Stop();
        EditorApplication.ExitPlaymode();
    }
}
