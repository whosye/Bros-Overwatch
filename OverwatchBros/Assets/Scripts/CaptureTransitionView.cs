using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// UI-only fade. The server schedules the actual teleport while this is fully opaque.
public sealed class CaptureTransitionView : MonoBehaviour
{
    Canvas canvas;
    Image background, line;
    TextMeshProUGUI title, subtitle;

    public static CaptureTransitionView Build(Transform parent)
    {
        var go=new GameObject("CaptureTransition",typeof(RectTransform),typeof(Canvas));
        go.transform.SetParent(parent,false);
        UiKit.Stretch((RectTransform)go.transform);
        var view=go.AddComponent<CaptureTransitionView>();
        view.canvas=go.GetComponent<Canvas>();
        view.canvas.overrideSorting=true;
        view.canvas.sortingOrder=900;
        var center=new Vector2(.5f,.5f);
        view.background=UiKit.MakeImage(go.transform,"Fade",Color.black,center,Vector2.zero,Vector2.one);
        UiKit.Stretch(view.background.rectTransform);
        view.title=UiKit.MakeText(go.transform,"Result","",64,TextAlignmentOptions.Center,center,
            new Vector2(0,25),new Vector2(1500,100));
        view.title.fontStyle=FontStyles.Bold|FontStyles.Italic;
        view.subtitle=UiKit.MakeText(go.transform,"Next","",27,TextAlignmentOptions.Center,center,
            new Vector2(0,-55),new Vector2(1400,80));
        view.line=UiKit.MakeImage(go.transform,"Accent",Color.white,center,new Vector2(0,-105),new Vector2(600,4));
        view.canvas.enabled=false;
        return view;
    }

    public static float Opacity(float age,float duration) =>
        Mathf.SmoothStep(0,1,Mathf.Clamp01(age/.5f))*Mathf.SmoothStep(0,1,Mathf.Clamp01((duration-age)/.55f));

    public static string ResultTitle(CapturePresentationState state,int localTeam)
    {
        string point=MatchManager.PointNames[Mathf.Clamp(state.point,0,MatchManager.PointNames.Length-1)];
        bool own=localTeam==state.team;
        switch(state.kind)
        {
            case CapturePresentationState.Taken: return $"BOD {point} "+(own || localTeam<0?"DOBYT":"ZTRACEN");
            case CapturePresentationState.Defended: return $"BOD {point} "+(own || localTeam<0?"UBRÁNĚN":"NEDOBYT");
            case CapturePresentationState.GoalReached: return own?"CÍL SPLNĚN":"SOUPEŘ SPLNIL CÍL";
            case CapturePresentationState.SwitchSides: return state.point<0?"NOVÉ KOLO":"VÝMĚNA STRAN";
            default: return "DALŠÍ BOD";
        }
    }

    void Update()
    {
        if (canvas==null) return;
        var match=MatchManager.Instance;
        var state=match!=null?match.capturePresentation.Value:default;
        bool visible=match!=null && match.IsSpawned && match.IsAttackMode && !match.IsLobby
            && state.Active(match.CaptureClock);
        canvas.enabled=visible;
        if (!visible) return;
        float age=(float)(match.CaptureClock-state.started);
        float alpha=Opacity(age,state.duration);
        background.color=new Color(0,0,0,alpha);
        var local=NetworkManager.Singleton?.LocalClient?.PlayerObject;
        var team=local!=null?local.GetComponent<PlayerTeam>():null;
        int localTeam=team!=null?team.teamId.Value:-1;
        bool neutral=state.kind==CapturePresentationState.SwitchSides || state.kind==CapturePresentationState.NextPoint || localTeam<0;
        Color tint=neutral?Color.white:localTeam==state.team?new Color(.35f,1f,.65f):new Color(1f,.35f,.30f);
        tint.a=alpha;
        title.text=ResultTitle(state,localTeam);
        title.color=tint;
        line.color=tint;
        subtitle.color=new Color(.85f,.88f,.94f,alpha);
        if (state.kind==CapturePresentationState.SwitchSides)
            subtitle.text=localTeam<0?"Začíná nové kolo":localTeam==state.team?"Teď útočíte":"Teď bráníte";
        else if (state.kind==CapturePresentationState.Defended) subtitle.text="Čas útoku vypršel";
        else if (state.next>=0) subtitle.text=$"Přesun k bodu {MatchManager.PointNames[Mathf.Clamp(state.next,0,MatchManager.PointNames.Length-1)]}";
        else subtitle.text="Konec útoku · vyhodnocení kola";
    }
}
