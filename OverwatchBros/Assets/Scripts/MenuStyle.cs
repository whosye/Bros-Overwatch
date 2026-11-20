using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Menu styling is independent of combat HUD and team colours.
public static class MenuStyle
{
    public static readonly Color Ink = new Color(.035f,.12f,.075f);
    public static readonly Color Panel = new Color(.035f,.15f,.09f,1f);
    public static readonly Color Field = new Color(.055f,.20f,.12f);
    public static readonly Color Mint = new Color(.57f,.92f,.64f);
    public static readonly Color Cream = new Color(1,.97f,.87f);
    public static readonly Color Muted = new Color(.69f,.77f,.68f);
    public static readonly Color Peach = new Color(1,.72f,.53f);
    public static readonly Vector2 Center = new Vector2(.5f,.5f);
    static Sprite rounded;
    static TMP_FontAsset bodyFont, displayFont;
    public static TMP_FontAsset BodyFont => bodyFont != null ? bodyFont : bodyFont=Resources.Load<TMP_FontAsset>("UI/Fonts/Barlow Medium SDF");
    public static TMP_FontAsset DisplayFont => displayFont != null ? displayFont : displayFont=Resources.Load<TMP_FontAsset>("UI/Fonts/Barlow Condensed SemiBold SDF");
    public static void Compact(Transform panel,float scale) => panel.localScale=Vector3.one*scale;
    static Sprite Rounded
    {
        get
        {
            if (rounded != null) return rounded;
            var t = new Texture2D(64,64,TextureFormat.RGBA32,false);
            t.name = "Menu rounded corners"; t.hideFlags = HideFlags.HideAndDontSave; t.wrapMode = TextureWrapMode.Clamp;
            for (int y=0;y<64;y++) for (int x=0;x<64;x++)
            {
                float dx=Mathf.Max(18-x,x-45,0), dy=Mathf.Max(18-y,y-45,0);
                t.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(18-Mathf.Sqrt(dx*dx+dy*dy))));
            }
            t.Apply(); rounded=Sprite.Create(t,new Rect(0,0,64,64),Center,100,0,SpriteMeshType.FullRect,new Vector4(20,20,20,20));
            rounded.hideFlags=HideFlags.HideAndDontSave; return rounded;
        }
    }
    public static void Round(Image image) { image.sprite=Rounded; image.type=Image.Type.Sliced; image.pixelsPerUnitMultiplier=1.5f; }
    public static Image Box(Transform parent,string name,Vector2 pos,Vector2 size,Color? color=null)
    { var image=UiKit.MakeImage(parent,name,color??Panel,Center,pos,size); Round(image); return image; }
    public static TextMeshProUGUI Text(Transform parent,string name,string value,Vector2 pos,Vector2 size,float font=28,Color? color=null,TextAlignmentOptions align=TextAlignmentOptions.Left)
    {
        var t=UiKit.MakeText(parent,name,value,font,align,Center,pos,size,color??Cream);
        bool heading=name=="Title"||name=="GameTitle"||name=="Wordmark";
        var fontAsset=heading?DisplayFont:BodyFont;
        if(fontAsset!=null)t.font=fontAsset;
        t.richText=false; t.textWrappingMode=TextWrappingModes.NoWrap; t.overflowMode=TextOverflowModes.Ellipsis; return t;
    }
    public static Button Button(Transform parent,string label,Vector2 pos,Vector2 size,UnityAction action,bool primary=false,float font=28)
    {
        var b=UiKit.MakeButton(parent,label,Center,pos,size,action,primary?Mint:Field,font);
        Round((Image)b.targetGraphic);
        var t=b.GetComponentInChildren<TextMeshProUGUI>(); t.color=primary?Ink:Cream; t.fontStyle=FontStyles.Normal;
        if(DisplayFont!=null)t.font=DisplayFont;
        t.richText=false; t.textWrappingMode=TextWrappingModes.NoWrap; t.overflowMode=TextOverflowModes.Ellipsis;
        var c=b.colors; c.highlightedColor=new Color(1.08f,1.08f,1.08f); c.pressedColor=new Color(.8f,.9f,.8f); c.fadeDuration=.12f; b.colors=c;
        b.gameObject.AddComponent<MenuHover>(); return b;
    }
    public static TMP_InputField Input(Transform parent,string name,string value,Vector2 pos,Vector2 size,int limit=30)
    {
        var f=UiKit.MakeInput(parent,name,Center,pos,size,value,30); f.gameObject.name=name;
        ((Image)f.targetGraphic).color=Field; Round((Image)f.targetGraphic);
        var o=f.GetComponent<Outline>(); o.effectColor=new Color(.57f,.92f,.64f,.35f); o.effectDistance=new Vector2(1,-1);
        if(BodyFont!=null)f.textComponent.font=BodyFont;
        if(BodyFont!=null)((TMP_Text)f.placeholder).font=BodyFont;
        f.textComponent.color=Cream; f.textComponent.richText=false; f.characterLimit=limit; f.lineType=TMP_InputField.LineType.SingleLine;
        f.selectionColor=new Color(.57f,.92f,.64f,.35f); return f;
    }
    public static void Rule(Transform parent,Vector2 pos,float width)
        =>UiKit.MakeImage(parent,"Divider",new Color(.69f,.77f,.68f,.24f),Center,pos,new Vector2(width,1));
    static readonly Sprite[] mapPreviews=new Sprite[3];
    public static Sprite MapPreview(int index)
    {
        if(index<0||index>=mapPreviews.Length)return null;
        if(mapPreviews[index]!=null)return mapPreviews[index];
        string[] names={"cottage-evening","hanamura-lan","prdust-lan"};
        var texture=Resources.Load<Texture2D>("UI/"+names[index]);
        if(texture==null)return null;
        return mapPreviews[index]=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),Center);
    }
    public static Image Background(Transform parent)
    {
        var image=UiKit.MakeImage(parent,"CottageBackground",Color.white,Center,Vector2.zero,Vector2.zero);
        UiKit.Stretch(image.rectTransform);
        var texture=Resources.Load<Texture2D>("UI/cottage-evening");
        if(texture!=null)
        {
            image.sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),Center);
            var fit=image.gameObject.AddComponent<AspectRatioFitter>(); fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent; fit.aspectRatio=(float)texture.width/texture.height;
        }
        else image.color=Ink;
        return image;
    }
}
public class MenuHover : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler
{
    bool hovering;
    public void OnPointerEnter(PointerEventData data) { hovering=true; }
    public void OnPointerExit(PointerEventData data) { hovering=false; }
    void OnDisable() { hovering=false; transform.localScale=Vector3.one; }
    void Update()=>transform.localScale=Vector3.Lerp(transform.localScale,Vector3.one*(hovering?1.015f:1),1-Mathf.Exp(-Time.unscaledDeltaTime*16));
}
[RequireComponent(typeof(CanvasRenderer))]
public class MenuScrim : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); Rect r=rectTransform.rect; var v=UIVertex.simpleVert;
        for(int i=0;i<4;i++)
        {
            v.position=new Vector3(i==0||i==3?r.xMin:r.xMax,i<2?r.yMin:r.yMax);
            v.color=new Color(.015f,.08f,.045f,i==0||i==3?.60f:.02f); vh.AddVert(v);
        }
        vh.AddTriangle(0,1,2); vh.AddTriangle(2,3,0);
    }
}

// A note with a diagonal mute stroke, rather than the window-close cross.
[RequireComponent(typeof(CanvasRenderer))]
public class MenuMusicIcon : MaskableGraphic
{
    bool muted;
    public bool Muted { get=>muted; set { if(muted==value)return; muted=value; SetVerticesDirty(); } }
    void Quad(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d)
    {
        int n=vh.currentVertCount; var v=UIVertex.simpleVert; v.color=color;
        foreach(var p in new[]{a,b,c,d}) { v.position=p; vh.AddVert(v); }
        vh.AddTriangle(n,n+1,n+2); vh.AddTriangle(n+2,n+3,n);
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Quad(vh,new Vector2(-3,-6),new Vector2(0,-6),new Vector2(0,12),new Vector2(-3,12));
        Quad(vh,new Vector2(0,12),new Vector2(9,7),new Vector2(9,3),new Vector2(0,8));
        int start=vh.currentVertCount; var v=UIVertex.simpleVert; v.color=color; v.position=new Vector2(-6,-7); vh.AddVert(v);
        for(int i=0;i<=16;i++)
        {
            float a=i*Mathf.PI*2/16; v.position=new Vector2(-6+Mathf.Cos(a)*5,-7+Mathf.Sin(a)*4); vh.AddVert(v);
            if(i>0)vh.AddTriangle(start,start+i,start+i+1);
        }
        if(muted)Quad(vh,new Vector2(-14,-12),new Vector2(-12,-14),new Vector2(14,12),new Vector2(12,14));
    }
}
