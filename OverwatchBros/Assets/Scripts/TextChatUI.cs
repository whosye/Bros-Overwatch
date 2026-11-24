using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Read chat keys before gameplay scripts, so the opening/closing key cannot fire a game action.
[DefaultExecutionOrder(-300)]
public sealed class TextChatUI:MonoBehaviour
{
    public readonly struct Line
    {
        public readonly string author,text;
        public readonly int team;
        public readonly bool teamOnly;
        public Line(string author,string text,int team,bool teamOnly)
        { this.author=author; this.text=text; this.team=team; this.teamOnly=teamOnly; }
    }
    sealed class Row { public RectTransform rect; public TMP_Text channel,name,body; }
    public const float DisplaySeconds=8f;
    public const float PanelWidth=420f, MaxTypingHeight=210f, MaxPassiveHeight=126f;
    const float BodyWidth=392f;
    static TextChatUI instance;
    static int consumedFrame=-1;
    public static bool IsTyping { get; private set; }
    public static bool BlocksGameplayInput => IsTyping || consumedFrame==Time.frameCount;
    public static event Action<Line> MessageReceived;
    public IReadOnlyList<Line> Messages => history;
    public TMP_InputField Draft => input;
    public bool TeamOnly => teamOnly;
    public bool Visible => panel!=null && panel.activeSelf;
    public static TextChatUI Instance => instance;
    readonly List<Line> history=new List<Line>();
    readonly Row[] rows=new Row[4];
    MatchUI owner;
    GameObject panel,inputRow;
    TMP_InputField input;
    TMP_Text channelLabel,hint;
    Image accent;
    bool teamOnly,wasReady,wasLocked;
    float visibleUntil,nextSend;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance=null; IsTyping=false; consumedFrame=-1; MessageReceived=null; }
    void OnDestroy() { if(instance==this) { instance=null; IsTyping=false; consumedFrame=-1; } }

    public static TextChatUI Build(Transform canvas,MatchUI owner)
    {
        var view=owner.gameObject.AddComponent<TextChatUI>();
        view.owner=owner; instance=view;
        var left=new Vector2(0,0);
        var backdrop=UiKit.MakeImage(canvas,"TextChat",new Color(.025f,.035f,.045f,.76f),left,
            new Vector2(18,184),new Vector2(PanelWidth,96));
        view.panel=backdrop.gameObject; MenuStyle.Round(backdrop);
        view.accent=UiKit.MakeImage(backdrop.transform,"Accent",MenuStyle.Mint,left,Vector2.zero,new Vector2(2,96));
        var title=UiKit.MakeText(backdrop.transform,"Title","CHAT",14,TextAlignmentOptions.TopLeft,
            new Vector2(0,1),new Vector2(14,-6),new Vector2(64,20));
        title.fontStyle=FontStyles.Bold;
        view.hint=UiKit.MakeText(backdrop.transform,"Hint","ENTER · napsat zprávu",12,TextAlignmentOptions.TopRight,
            new Vector2(1,1),new Vector2(-14,-7),new Vector2(326,18),UiKit.Muted);
        for(int i=0;i<view.rows.Length;i++)
        {
            var go=new GameObject("Message"+i,typeof(RectTransform)); go.transform.SetParent(backdrop.transform,false);
            var rect=(RectTransform)go.transform; rect.anchorMin=rect.anchorMax=rect.pivot=left;
            var row=new Row { rect=rect };
            row.channel=UiKit.MakeText(rect,"Channel","",12,TextAlignmentOptions.TopLeft,new Vector2(0,1),Vector2.zero,new Vector2(62,17));
            row.name=UiKit.MakeText(rect,"Author","",15,TextAlignmentOptions.TopLeft,new Vector2(0,1),new Vector2(68,0),new Vector2(324,18));
            row.body=UiKit.MakeText(rect,"Message","",18,TextAlignmentOptions.TopLeft,new Vector2(0,1),new Vector2(0,-18),new Vector2(BodyWidth,42));
            row.name.textWrappingMode=TextWrappingModes.NoWrap;
            row.name.overflowMode=TextOverflowModes.Ellipsis;
            row.body.maxVisibleLines=2;
            row.body.overflowMode=TextOverflowModes.Ellipsis;
            foreach(var label in new[]{row.channel,row.name,row.body})
            {
                label.richText=false;
                if(MenuStyle.BodyFont!=null) label.font=MenuStyle.BodyFont;
            }
            row.channel.fontStyle=row.name.fontStyle=FontStyles.Bold;
            view.rows[i]=row; go.SetActive(false);
        }
        var entry=UiKit.MakeImage(backdrop.transform,"Composer",Color.clear,left,new Vector2(14,10),new Vector2(BodyWidth,34));
        view.inputRow=entry.gameObject;
        view.channelLabel=UiKit.MakeText(entry.transform,"Channel","VŠICHNI",13,TextAlignmentOptions.Center,
            new Vector2(0,.5f),new Vector2(0,0),new Vector2(68,32),UiKit.Accent);
        view.channelLabel.fontStyle=FontStyles.Bold;
        view.input=UiKit.MakeInput(entry.transform,"Napiš zprávu…",new Vector2(0,0),new Vector2(76,0),new Vector2(316,34),"",18);
        view.input.characterLimit=MatchManager.ChatCharacterLimit;
        view.input.lineType=TMP_InputField.LineType.SingleLine;
        view.input.richText=false; view.input.textComponent.richText=false;
        view.input.navigation=new Navigation { mode=Navigation.Mode.None };
        ((Image)view.input.targetGraphic).color=new Color(.08f,.11f,.14f,1);
        MenuStyle.Round((Image)view.input.targetGraphic);
        if(MenuStyle.BodyFont!=null) { view.input.textComponent.font=MenuStyle.BodyFont; ((TMP_Text)view.input.placeholder).font=MenuStyle.BodyFont; }
        view.inputRow.SetActive(false); view.panel.SetActive(false);
        return view;
    }

    public static void Receive(string author,string text,int team,bool teamOnly)
    {
        var line=new Line(author,text,team,teamOnly);
        if(instance!=null)
        {
            instance.history.Add(line);
            if(instance.history.Count>40) instance.history.RemoveAt(0);
            instance.visibleUntil=Time.unscaledTime+DisplaySeconds;
            instance.RefreshRows();
        }
        MessageReceived?.Invoke(line);
    }

    bool Ready => NetworkManager.Singleton!=null && NetworkManager.Singleton.IsListening
        && NetworkManager.Singleton.LocalClient?.PlayerObject!=null && MatchManager.Instance!=null && MatchManager.Instance.IsSpawned;

    public bool Open()
    {
        if(!Ready || owner==null || !owner.ChatAvailable) return false;
        wasLocked=Cursor.lockState==CursorLockMode.Locked;
        IsTyping=true; consumedFrame=Time.frameCount;
        panel.SetActive(true); inputRow.SetActive(true); input.SetTextWithoutNotify("");
        Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
        EventSystem.current?.SetSelectedGameObject(input.gameObject); input.ActivateInputField();
        RefreshRows(); return true;
    }
    public void ToggleChannel()
    {
        if(!IsTyping) return;
        teamOnly=!teamOnly; consumedFrame=Time.frameCount;
        input.ActivateInputField(); RefreshRows();
    }
    public bool Submit()
    {
        if(!IsTyping) return false;
        consumedFrame=Time.frameCount;
        string text=MatchManager.CleanChatText(input.text);
        if(text.Length>0 && Time.unscaledTime<nextSend) { hint.text="Chvilku počkej…"; return false; }
        if(text.Length>0 && Ready)
        {
            MatchManager.Instance.SendChatRpc(text,teamOnly);
            nextSend=Time.unscaledTime+.65f;
        }
        Close(); return text.Length>0;
    }
    public void Close(bool restoreCursor=true)
    {
        if(!IsTyping) return;
        IsTyping=false; consumedFrame=Time.frameCount;
        input.DeactivateInputField(); EventSystem.current?.SetSelectedGameObject(null);
        inputRow.SetActive(false); visibleUntil=Time.unscaledTime+DisplaySeconds;
        // Locked mode already hides the cursor. Do not leave its visibility flag
        // disabled when a later menu unlocks it.
        if(restoreCursor && wasLocked) Cursor.lockState=CursorLockMode.Locked;
        RefreshRows();
    }

    void Update()
    {
        if(panel==null) return;
        bool ready=Ready;
        if(!ready)
        {
            if(IsTyping) Close(false);
            if(wasReady) { history.Clear(); nextSend=0; RefreshRows(); }
            panel.SetActive(false); wasReady=false; return;
        }
        wasReady=true;
        bool allowed=owner!=null && owner.ChatAvailable;
        if(!allowed && IsTyping) Close(false);
        var keyboard=Keyboard.current;
        if(allowed && keyboard!=null)
        {
            bool enter=keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            if(IsTyping)
            {
                if(keyboard.escapeKey.wasPressedThisFrame) Close();
                else if(keyboard.tabKey.wasPressedThisFrame) ToggleChannel();
                else if(enter) Submit();
                else if(!input.isFocused) { EventSystem.current?.SetSelectedGameObject(input.gameObject); input.ActivateInputField(); }
            }
            else if(enter)
            {
                var selected=EventSystem.current?.currentSelectedGameObject;
                var field=selected!=null?selected.GetComponent<TMP_InputField>():null;
                if(field==null || !field.isFocused) Open();
            }
        }
        bool show=IsTyping || (history.Count>0 && Time.unscaledTime<visibleUntil);
        if(panel.activeSelf!=show) panel.SetActive(show);
    }

    void RefreshRows()
    {
        if(panel==null) return;
        Color channelColor=teamOnly?MenuStyle.Mint:UiKit.Accent;
        accent.color=channelColor;
        channelLabel.text=teamOnly?"TÝM":"VŠICHNI"; channelLabel.color=channelColor;
        hint.text=IsTyping?"TAB · kanál    ENTER · odeslat    ESC · zavřít":"ENTER · napsat zprávu";
        float bottom=IsTyping?54:10;
        float limit=IsTyping?MaxTypingHeight:MaxPassiveHeight;
        panel.GetComponent<Image>().color=new Color(.025f,.035f,.045f,IsTyping ? .76f : .24f);
        bool room=true;
        for(int slot=0;slot<rows.Length;slot++)
        {
            var row=rows[slot]; int index=history.Count-1-slot;
            if(index<0 || (!IsTyping && slot>=2)) { row.rect.gameObject.SetActive(false); continue; }
            var line=history[index];
            row.body.text=line.text;
            float bodyHeight=Mathf.Clamp(row.body.GetPreferredValues(line.text,BodyWidth,0).y,22,42);
            float height=18+bodyHeight;
            bool fits=room && bottom+height<=limit-26;
            row.rect.gameObject.SetActive(fits);
            if(!fits) { room=false; continue; }
            row.rect.anchoredPosition=new Vector2(14,bottom); row.rect.sizeDelta=new Vector2(BodyWidth,height);
            row.body.rectTransform.sizeDelta=new Vector2(BodyWidth,bodyHeight+2);
            row.channel.text=line.teamOnly?"TÝM":"VŠICHNI"; row.channel.color=line.teamOnly?MenuStyle.Mint:UiKit.Accent;
            row.name.text=line.author; row.name.color=UiKit.TeamColor(line.team);
            bottom+=height+4;
        }
        float panelHeight=Mathf.Clamp(bottom+26,IsTyping?96:70,limit);
        ((RectTransform)panel.transform).sizeDelta=new Vector2(PanelWidth,panelHeight);
        accent.rectTransform.sizeDelta=new Vector2(2,panelHeight);
    }
}
