using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ServerBrowserUI
{
    public GameObject Root { get; private set; }
    const int PageSize=4;
    readonly List<ServerHistory.Entry> visible=new List<ServerHistory.Entry>();
    readonly Button[] rows=new Button[PageSize];
    readonly TextMeshProUGUI[] rowNames=new TextMeshProUGUI[PageSize],rowAddresses=new TextMeshProUGUI[PageSize],rowMaps=new TextMeshProUGUI[PageSize],rowCounts=new TextMeshProUGUI[PageSize],rowStates=new TextMeshProUGUI[PageSize];
    TextMeshProUGUI lastTitle,lastDetail,lastState,refreshState,selection,pageLabel,empty,addError;
    Button lastJoin,join,refresh,savedTab,lanTab,remove,previous,next;
    TMP_InputField nick,address,alias;
    GameObject addModal;
    Action<string> saveName,status;
    bool lan;
    int page;
    string selected;
    float nextPaint;
    public void Build(Transform parent,Action back,Action<string> onName,Action<string> onStatus)
    {
        saveName=onName; status=onStatus;
        Root=MenuStyle.Box(parent,"ServerScreen",Vector2.zero,new Vector2(1920,1080),Color.clear).gameObject;
        var p=MenuStyle.Box(Root.transform,"BrowserPanel",new Vector2(0,-20),new Vector2(1660,880)).transform;
        MenuStyle.Compact(p,.82f);
        MenuStyle.Text(p,"Title","SERVERY",new Vector2(110,325),new Vector2(1100,80),54).fontStyle=FontStyles.Bold|FontStyles.Italic;
        MenuStyle.Button(p,"‹ Zpět",new Vector2(-670,325),new Vector2(180,55),()=>back(),false,24);
        var last=MenuStyle.Box(p,"LastVisited",new Vector2(0,220),new Vector2(1520,94),MenuStyle.Field).transform;
        MenuStyle.Text(last,"Label","Poslední navštívený",new Vector2(-590,0),new Vector2(290,65),24,MenuStyle.Peach);
        lastTitle=MenuStyle.Text(last,"Server","",new Vector2(-185,20),new Vector2(480,38),28);
        lastDetail=MenuStyle.Text(last,"Detail","",new Vector2(-185,-20),new Vector2(480,38),21,MenuStyle.Muted);
        lastState=MenuStyle.Text(last,"State","",new Vector2(225,0),new Vector2(250,50),23,MenuStyle.Mint,TextAlignmentOptions.Center);
        lastJoin=MenuStyle.Button(last,"PŘIPOJIT",new Vector2(555,0),new Vector2(300,60),()=>{ var e=ServerHistory.LastJoined; if(e!=null)Join(e); },true,25);
        savedTab=MenuStyle.Button(p,"Uložené",new Vector2(-630,120),new Vector2(250,58),()=>SetTab(false),true,25);
        lanTab=MenuStyle.Button(p,"LAN",new Vector2(-370,120),new Vector2(250,58),()=>SetTab(true),false,25);
        MenuStyle.Button(p,"+ Přidat server",new Vector2(260,120),new Vector2(300,58),OpenAdd,false,25);
        refresh=MenuStyle.Button(p,"Obnovit",new Vector2(610,120),new Vector2(300,58),Refresh,false,25);
        MenuStyle.Rule(p,new Vector2(0,73),1520);
        MenuStyle.Text(p,"ServerColumn","SERVER",new Vector2(-510,43),new Vector2(470,40),22,MenuStyle.Muted);
        MenuStyle.Text(p,"MapColumn","MAPA / REŽIM",new Vector2(0,43),new Vector2(470,40),22,MenuStyle.Muted);
        MenuStyle.Text(p,"CountColumn","HRÁČI",new Vector2(350,43),new Vector2(140,40),22,MenuStyle.Muted,TextAlignmentOptions.Center);
        MenuStyle.Text(p,"StateColumn","STAV",new Vector2(610,43),new Vector2(280,40),22,MenuStyle.Muted,TextAlignmentOptions.Center);
        for(int i=0;i<PageSize;i++)
        {
            int n=i;
            rows[i]=MenuStyle.Button(p,"",new Vector2(0,-15-i*76),new Vector2(1520,70),()=>SelectRow(n));
            var t=rows[i].transform;
            rowNames[i]=MenuStyle.Text(t,"ServerName","",new Vector2(-510,14),new Vector2(470,34),27);
            rowAddresses[i]=MenuStyle.Text(t,"Address","",new Vector2(-510,-17),new Vector2(470,30),20,MenuStyle.Muted);
            rowMaps[i]=MenuStyle.Text(t,"MapMode","",new Vector2(0,0),new Vector2(470,55),23);
            rowCounts[i]=MenuStyle.Text(t,"Players","",new Vector2(350,0),new Vector2(140,50),27,null,TextAlignmentOptions.Center);
            rowStates[i]=MenuStyle.Text(t,"State","",new Vector2(610,0),new Vector2(280,50),24,MenuStyle.Mint,TextAlignmentOptions.Center);
        }
        empty=MenuStyle.Text(p,"EmptyServers","",new Vector2(0,-100),new Vector2(1400,160),29,MenuStyle.Muted,TextAlignmentOptions.Center);
        empty.textWrappingMode=TextWrappingModes.Normal;
        refreshState=MenuStyle.Text(p,"RefreshStatus","",new Vector2(-400,-295),new Vector2(700,40),21,MenuStyle.Muted);
        previous=MenuStyle.Button(p,"‹",new Vector2(280,-295),new Vector2(65,42),()=>{ page--; selected=null; Paint(); },false,25);
        pageLabel=MenuStyle.Text(p,"Pagination","",new Vector2(400,-295),new Vector2(160,42),22,null,TextAlignmentOptions.Center);
        next=MenuStyle.Button(p,"›",new Vector2(520,-295),new Vector2(65,42),()=>{ page++; selected=null; Paint(); },false,25);
        remove=MenuStyle.Button(p,"Odebrat",new Vector2(665,-295),new Vector2(170,42),Remove,false,22);
        MenuStyle.Rule(p,new Vector2(0,-325),1520);
        MenuStyle.Text(p,"NickLabel","Jméno hráče",new Vector2(-640,-370),new Vector2(245,60),25);
        nick=MenuStyle.Input(p,"Jméno hráče",MainMenuUI.PlayerName,new Vector2(-315,-370),new Vector2(385,64),20);
        nick.onEndEdit.AddListener(v=>saveName(v));
        selection=MenuStyle.Text(p,"SelectedServer","",new Vector2(430,-342),new Vector2(640,30),20,MenuStyle.Muted,TextAlignmentOptions.Center);
        join=MenuStyle.Button(p,"PŘIPOJIT SE",new Vector2(430,-390),new Vector2(640,58),JoinSelected,true,30);
        BuildAdd(); Root.SetActive(false);
    }
    void BuildAdd()
    {
        var v=UiKit.MakeImage(Root.transform,"AddServerOverlay",new Color(0,0,0,.72f),MenuStyle.Center,Vector2.zero,Vector2.zero);
        UiKit.Stretch(v.rectTransform); v.raycastTarget=true; addModal=v.gameObject;
        var p=MenuStyle.Box(addModal.transform,"AddServer",Vector2.zero,new Vector2(870,570)).transform;
        MenuStyle.Compact(p,.88f);
        MenuStyle.Text(p,"Title","PŘIDAT SERVER",new Vector2(0,205),new Vector2(730,70),42).fontStyle=FontStyles.Bold;
        MenuStyle.Text(p,"AddressLabel","IP adresa a port",new Vector2(0,125),new Vector2(730,42),25);
        address=MenuStyle.Input(p,"192.168.1.12:7777","",new Vector2(0,62),new Vector2(730,66),64);
        MenuStyle.Text(p,"AliasLabel","Vlastní název (nepovinné)",new Vector2(0,-10),new Vector2(730,42),25);
        alias=MenuStyle.Input(p,"Například U Honzy","",new Vector2(0,-70),new Vector2(730,66),40);
        addError=MenuStyle.Text(p,"Error","",new Vector2(0,-140),new Vector2(730,58),22,MenuStyle.Peach,TextAlignmentOptions.Center);
        addError.textWrappingMode=TextWrappingModes.Normal;
        MenuStyle.Button(p,"ZRUŠIT",new Vector2(-205,-220),new Vector2(320,64),()=>CloseModal());
        MenuStyle.Button(p,"ULOŽIT SERVER",new Vector2(185,-220),new Vector2(360,64),()=>
        {
            var entry=ServerHistory.Add(address.text,alias.text,out string error);
            if(entry==null) { addError.text=error; return; }
            selected=entry.Endpoint; lan=false; page=0; addModal.SetActive(false); Refresh(); Paint();
            page=Mathf.Max(0,visible.FindIndex(e=>e.Endpoint==selected)/PageSize); Paint();
        },true,25);
        addModal.SetActive(false);
    }
    void OpenAdd() { address.text=""; alias.text=""; addError.text=""; addModal.SetActive(true); address.ActivateInputField(); }
    public bool CloseModal() { if(!addModal.activeSelf)return false; addModal.SetActive(false); return true; }
    public void SyncName()=>nick.SetTextWithoutNotify(MainMenuUI.PlayerName);
    public void SetVisible(bool value,bool force=false)
    {
        if(!force&&Root.activeSelf==value)return;
        Root.SetActive(value);
        if(value) { SyncName(); Refresh(); Paint(); }
        else { LanDiscovery.Instance?.StopListening(); addModal.SetActive(false); }
    }
    void SetTab(bool value) { lan=value; page=0; selected=null; Paint(); }
    void Refresh() { LanDiscovery.GetOrCreate().Refresh(ServerHistory.All); Paint(); }
    void SelectRow(int index)
    {
        int n=page*PageSize+index; if(n<visible.Count) { selected=visible[n].Endpoint; Paint(); }
    }
    void JoinSelected() { var e=visible.Find(s=>s.Endpoint==selected); if(e!=null)Join(e); }
    void Join(ServerHistory.Entry entry)
    {
        saveName(nick.text); SyncName();
        status(GameConnection.Join(entry.address,entry.port)?"Připojuji se k „"+entry.Name+"“…":GameConnection.LastError);
    }
    void Remove() { if(selected==null||lan)return; ServerHistory.Remove(selected); selected=null; Paint(); }
    public void Tick()
    {
        if(!Root.activeSelf||Time.unscaledTime<nextPaint)return;
        nextPaint=Time.unscaledTime+.15f; Paint();
    }
    void Paint()
    {
        var discovery=LanDiscovery.GetOrCreate();
        visible.Clear();
        if(lan)
        {
            foreach(var item in discovery.Games)
                if(discovery.IsCurrent(item.Value))visible.Add(new ServerHistory.Entry { address=item.Value.address,port=item.Value.port,label=item.Value.name });
        }
        else foreach(var entry in ServerHistory.All)visible.Add(entry);
        visible.Sort((a,b)=>string.Compare(a.Name,b.Name,StringComparison.CurrentCultureIgnoreCase));
        int count=Mathf.Max(1,(visible.Count+PageSize-1)/PageSize); page=Mathf.Clamp(page,0,count-1);
        if(selected==null&&visible.Count>0)selected=visible[page*PageSize].Endpoint;
        for(int i=0;i<PageSize;i++)
        {
            int n=page*PageSize+i; bool has=n<visible.Count; rows[i].gameObject.SetActive(has); if(!has)continue;
            var e=visible[n]; discovery.Games.TryGetValue(e.Endpoint,out var game); bool online=discovery.IsCurrent(game);
            UiKit.SetButtonColor(rows[i],e.Endpoint==selected?new Color(.18f,.37f,.23f):MenuStyle.Field);
            rowNames[i].text=string.IsNullOrWhiteSpace(e.label)&&online?game.name:e.Name;
            rowAddresses[i].text=e.Endpoint;
            rowMaps[i].text=online?game.map+" · "+game.mode:"—";
            rowCounts[i].text=online?game.players.ToString():"—";
            rowStates[i].text=State(discovery,game,e.Endpoint);
            rowStates[i].color=online?MenuStyle.Mint:MenuStyle.Muted;
        }
        var last=ServerHistory.LastJoined;
        lastJoin.interactable=last!=null;
        if(last!=null)
        {
            discovery.Games.TryGetValue(last.Endpoint,out var g); lastTitle.text=last.Name;
            lastDetail.text=discovery.IsCurrent(g)?g.map+" · "+g.mode+" · "+g.players+" hráčů":last.Endpoint;
            lastState.text=State(discovery,g,last.Endpoint);
        }
        else { lastTitle.text="Zatím žádný server"; lastDetail.text="Tady najdeš poslední úspěšné připojení."; lastState.text=""; }
        empty.gameObject.SetActive(visible.Count==0);
        empty.text=lan?(discovery.IsQuerying?"Hledám hry v místní síti…":"Žádná hra v LAN neodpověděla.\nMůžeš přidat IP adresu hostitele ručně."):"Zatím nemáš uložené servery.\nPřidej adresu, nebo najdi hru v záložce LAN.";
        var selectedEntry=visible.Find(e=>e.Endpoint==selected);
        join.interactable=selectedEntry!=null; selection.text=selectedEntry!=null?selectedEntry.Name:"Vyber server ze seznamu";
        remove.interactable=!lan&&selectedEntry!=null;
        previous.interactable=page>0; next.interactable=page+1<count; pageLabel.text=(page+1)+" / "+count;
        UiKit.SetButtonColor(savedTab,!lan?MenuStyle.Mint:MenuStyle.Field); savedTab.GetComponentInChildren<TextMeshProUGUI>().color=!lan?MenuStyle.Ink:MenuStyle.Cream;
        UiKit.SetButtonColor(lanTab,lan?MenuStyle.Mint:MenuStyle.Field); lanTab.GetComponentInChildren<TextMeshProUGUI>().color=lan?MenuStyle.Ink:MenuStyle.Cream;
        UiKit.SetButtonLabel(refresh,discovery.Cooldown>0?"Obnovit za "+Mathf.CeilToInt(discovery.Cooldown)+" s":"Obnovit");
        refreshState.text=discovery.Error!=""?discovery.Error:discovery.IsQuerying?"Ověřuji stav serverů…":"Poslední ověření dokončeno. Obnovení je ruční.";
    }
    static string State(LanDiscovery d,LanDiscovery.Game g,string endpoint)=>d.IsCurrent(g)?g.phase:!d.WasQueried(endpoint)?"Neověřeno":d.IsQuerying?"Ověřuji…":"Nedostupný";
}
