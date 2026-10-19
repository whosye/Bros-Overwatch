using UnityEngine;
using UnityEngine.Rendering.Universal;

// Vybrany hrdina v pozadi lobby (jako vyber hrdiny v Overwatchi): postava se zbrani na podstavci se svetly, daleko pod
// mapou, s vlastni kamerou. Lobby (UI) se kresli pres ni. Pri zmene hrdiny se postava vymeni.
public class LobbyShowcase : MonoBehaviour
{
    static readonly Vector3 Stage = new Vector3(0f, -3000f, 0f);
    static LobbyShowcase instance;

    Camera view;
    Transform hero;
    CharacterVisual visual;
    HeldWeapons held;
    Renderer ring;
    int shownHero = -1;
    int shownTeam = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    // Vola LobbyUI: zobrazit (s danym hrdinou a barvou tymu) / schovat.
    public static void Show(int heroIndex, int team)
    {
        if (instance == null)
        {
            var go = new GameObject("LobbyShowcase");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<LobbyShowcase>();
            instance.Build();
        }
        if (!instance.gameObject.activeSelf) instance.gameObject.SetActive(true);
        instance.SetHero(heroIndex, team);
    }

    public static void Hide()
    {
        if (instance != null && instance.gameObject.activeSelf)
            instance.gameObject.SetActive(false);
    }

    void Build()
    {
        transform.position = Stage;

        // podstavec: tmavy kotouc se svitivym okrajem v barve tymu
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        // kolize: animace postavy podle ni pozna, ze stoji na zemi (jinak by "padala"). Plochy kvadr -
        // kapsle valce by se u zplosteneho podstavce zmenila v kouli trcici nad podlahu.
        DestroyImmediate(floor.GetComponent<Collider>());
        floor.AddComponent<BoxCollider>();
        floor.transform.SetParent(transform, false);
        floor.transform.localPosition = new Vector3(0f, -0.03f, 0f);
        floor.transform.localScale = new Vector3(2.6f, 0.03f, 2.6f);
        Fx.Paint(floor, new Color(0.08f, 0.09f, 0.12f));

        var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(rim.GetComponent<Collider>());
        rim.transform.SetParent(transform, false);
        rim.transform.localPosition = new Vector3(0f, -0.035f, 0f);
        rim.transform.localScale = new Vector3(2.8f, 0.02f, 2.8f);
        ring = rim.GetComponent<Renderer>();
        ring.material.EnableKeyword("_EMISSION");

        // svetla: hlavni zepredu ze strany, modre doplnkove, zadni obrys
        AddLight(new Vector3(1.6f, 2.6f, 2.2f), new Color(1f, 0.93f, 0.85f), 5f, 9f);
        AddLight(new Vector3(-2.2f, 1.4f, 1.4f), new Color(0.45f, 0.6f, 1f), 2.5f, 7f);
        AddLight(new Vector3(0f, 2.4f, -2f), new Color(1f, 0.8f, 0.55f), 4f, 6f);

        // kamera: cela postava, posazena nad stredem obrazovky (mezi kartami tymu)
        var camObject = new GameObject("LobbyCamera");
        camObject.transform.SetParent(transform, false);
        camObject.transform.localPosition = new Vector3(0f, 1.0f, 6.4f);
        camObject.transform.LookAt(transform.position + new Vector3(0f, 0.47f, 0f));
        view = camObject.AddComponent<Camera>();
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color(0.035f, 0.045f, 0.08f);
        view.fieldOfView = 30f;
        view.nearClipPlane = 0.1f;
        view.farClipPlane = 30f;
        view.depth = 5f;
        var data = view.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
    }

    void AddLight(Vector3 local, Color color, float intensity, float range)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
    }

    void SetHero(int heroIndex, int team)
    {
        if (team != shownTeam && ring != null)
        {
            shownTeam = team;
            Color c = team >= 0 ? UiKit.TeamColor(team) : UiKit.Accent;
            ring.material.color = c;
            ring.material.SetColor("_EmissionColor", c * 1.6f);
        }
        if (heroIndex == shownHero) return;
        shownHero = heroIndex;
        if (hero != null) Destroy(hero.gameObject);
        hero = null;

        var definition = HeroRegistry.Get(heroIndex);
        if (definition == null || definition.characterPrefab == null) return;

        var go = new GameObject("ShowcaseHero");
        go.transform.SetParent(transform, false);
        go.transform.localRotation = Quaternion.identity;   // celem ke kamere (kamera stoji na +Z)
        hero = go.transform;
        visual = go.AddComponent<CharacterVisual>();
        visual.IsGhost = true;
        visual.SetFeetOffset(0f);
        visual.SetModel(definition.characterPrefab, definition.tintCharacter ? definition.color : Color.white);
        held = go.AddComponent<HeldWeapons>();
        held.SetGhost(definition, false, null);
        go.AddComponent<ReplayIgnore>();
    }

    void Update()
    {
        // pomale natoceni a pohupovani (postava "zije")
        if (hero != null)
            hero.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.unscaledTime * 0.5f) * 18f, 0f);
    }
}
