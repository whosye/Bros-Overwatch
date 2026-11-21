using UnityEngine;
using UnityEngine.Rendering.Universal;

// Vybrany hrdina v pozadi lobby (jako vyber hrdiny v Overwatchi): postava se zbrani na podstavci se svetly, daleko pod
// mapou, s vlastni kamerou. Lobby (UI) se kresli pres ni. Pri zmene hrdiny se postava vymeni.
public class LobbyShowcase : MonoBehaviour
{
    static readonly Vector3 Stage = new Vector3(0f, -3000f, 0f);
    static LobbyShowcase instance;

    Camera view;
    RenderTexture preview;
    public static Texture PreviewTexture => instance!=null?instance.preview:null;
    Transform hero, modelAnchor;
    bool modelAligned;
    float shownAt;
    CharacterVisual visual;
    HeldWeapons held;
    Renderer ring;
    int shownHero = -1;
    int shownTeam = -1;
    int frameAfterUpdates;

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
        floor.transform.localScale = new Vector3(1.2f, 0.03f, 1.2f);
        Fx.Paint(floor, new Color(0.09f, 0.085f, 0.07f));

        var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        if (Application.isPlaying) Destroy(rim.GetComponent<Collider>());
        else DestroyImmediate(rim.GetComponent<Collider>());
        rim.transform.SetParent(transform, false);
        rim.transform.localPosition = new Vector3(0f, -0.035f, 0f);
        rim.transform.localScale = new Vector3(1.25f, 0.02f, 1.25f);
        Fx.Paint(rim,MenuStyle.Mint);
        ring = rim.GetComponent<Renderer>();
        ring.material.EnableKeyword("_EMISSION");

        // svetla: hlavni zepredu ze strany, modre doplnkove, zadni obrys
        AddLight(new Vector3(1.6f, 2.6f, 2.2f), new Color(1f, 0.93f, 0.85f), 5f, 9f);
        AddLight(new Vector3(-2.2f, 1.4f, 1.4f), new Color(0.45f, 0.6f, 1f), 2.5f, 7f);
        AddLight(new Vector3(0f, 2.4f, -2f), new Color(1f, 0.8f, 0.55f), 4f, 6f);

        // kamera: cela postava, posazena nad stredem obrazovky (mezi kartami tymu)
        var camObject = new GameObject("LobbyCamera");
        camObject.transform.SetParent(transform, false);
        camObject.transform.localPosition = new Vector3(0f, 1.0f, 4.7f);
        camObject.transform.LookAt(transform.position + new Vector3(0f, 1.0f, 0f));
        view = camObject.AddComponent<Camera>();
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = Color.clear;
        view.allowHDR = false;
        preview=new RenderTexture(960,1020,24,RenderTextureFormat.ARGB32) { name="LobbyHeroPreview" };
        view.targetTexture=preview;
        view.aspect = 640f / 680f;
        view.fieldOfView = 30f;
        view.nearClipPlane = 0.1f;
        view.farClipPlane = 30f;
        view.depth = 5f;
        var data = view.GetUniversalAdditionalCameraData();
        // Preserve alpha so the character stands directly in the menu backdrop.
        data.renderPostProcessing = false;
    }

    void OnDestroy()
    {
        if(view!=null)view.targetTexture=null;
        if(preview!=null) { preview.Release(); Destroy(preview); }
        if(instance==this)instance=null;
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
            ring.material.SetColor("_EmissionColor", c * .15f);
        }
        if (heroIndex == shownHero) return;
        shownHero = heroIndex;
        if (hero != null)
        {
            if (Application.isPlaying) Destroy(hero.gameObject);
            else DestroyImmediate(hero.gameObject);
        }
        hero = null;

        var definition = HeroRegistry.Get(heroIndex);
        if (definition == null || definition.characterPrefab == null) return;

        var go = new GameObject("ShowcaseHero");
        go.transform.SetParent(transform, false);
        go.transform.localRotation = Quaternion.identity;   // celem ke kamere (kamera stoji na +Z)
        hero = go.transform;
        var content=new GameObject("ModelAnchor");
        content.transform.SetParent(hero,false);
        modelAnchor=content.transform;
        modelAligned=false;
        shownAt=Time.unscaledTime;
        visual = content.AddComponent<CharacterVisual>();
        visual.IsGhost = true;
        visual.SetFeetOffset(0f);
        visual.SetModel(definition.characterPrefab, definition.tintCharacter ? definition.color : Color.white, definition.animations);
        held = content.AddComponent<HeldWeapons>();
        held.SetGhost(definition, false, null);
        content.AddComponent<ReplayIgnore>();
        // Wait for the idle pose and held weapons, then align the whole preview once.
        // The camera and rotation pivot stay identical for every hero.
        frameAfterUpdates = 2;
    }

    void LateUpdate()
    {
        if (frameAfterUpdates > 0 && --frameAfterUpdates == 0) FrameHero();
    }

    void FrameHero()
    {
        if (hero == null || modelAligned || visual.ModelRoot == null) return;
        hero.localRotation=Quaternion.identity;
        if (!TryBodyBounds(out var body)) return;
        var left=visual.GetBone(HumanBodyBones.LeftFoot);
        var right=visual.GetBone(HumanBodyBones.RightFoot);
        // Feet provide a stable horizontal pivot even when a pose stretches out an arm.
        // The mesh gives the soles' ground height; bone pivots sit inside the shoes.
        var center=left!=null&&right!=null?(left.position+right.position)*.5f:body.center;
        var offset=new Vector3(transform.position.x-center.x,transform.position.y-body.min.y,transform.position.z-center.z);
        modelAnchor.localPosition+=hero.InverseTransformVector(offset);
        modelAligned=true;
    }

    bool TryBodyBounds(out Bounds bounds)
    {
        bool found=false;
        bounds=new Bounds();
        var baked=new Mesh();
        // Only the body participates in alignment. Weapons and effects must not shift
        // the origin or move the camera when the player picks another character.
        foreach (var skin in visual.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if(!skin.enabled||skin.sharedMesh==null)continue;
            // Account for imported renderer scale (Pova uses 100x) exactly once.
            skin.BakeMesh(baked,true); baked.RecalculateBounds();
            for(int corner=0;corner<8;corner++)
            {
                var sign=new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1);
                var point=skin.transform.TransformPoint(baked.bounds.center+Vector3.Scale(baked.bounds.extents,sign));
                if(!found){ bounds=new Bounds(point,Vector3.zero); found=true; }
                else bounds.Encapsulate(point);
            }
        }
        if(Application.isPlaying)Destroy(baked);else DestroyImmediate(baked);
        return found;
    }

    void Update()
    {
        // pomale natoceni a pohupovani (postava "zije")
        if (hero != null)
            hero.localRotation = Quaternion.Euler(0f, Mathf.Sin((Time.unscaledTime-shownAt) * 0.5f) * 18f, 0f);
    }
}
