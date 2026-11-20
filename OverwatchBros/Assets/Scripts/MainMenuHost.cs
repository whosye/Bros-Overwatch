using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class MainMenuUI
{
    readonly Button[] mapButtons=new Button[MatchManager.MapNames.Length];
    Button deathmatch,attack,push,create,test;
    TextMeshProUGUI ruleLabel,ruleValue,mapSummary;
    GameObject advanced;
    Image mapPreview;
    void BuildHost()
    {
        host=MenuStyle.Box(Root.transform,"HostScreen",new Vector2(0,-35),new Vector2(1660,830)).gameObject;
        MenuStyle.Compact(host.transform,.80f);
        MenuStyle.Text(host.transform,"Title","ZALOŽIT HRU",new Vector2(110,325),new Vector2(1100,85),54).fontStyle=FontStyles.Bold|FontStyles.Italic;
        MenuStyle.Button(host.transform,"‹ Zpět",new Vector2(-670,325),new Vector2(180,55),()=>Navigate("Home"),false,24);
        HostLabel("Jméno hráče",205);
        hostName=MenuStyle.Input(host.transform,"Jméno hráče",PlayerName,new Vector2(-290,205),new Vector2(420,64),20);
        hostName.onEndEdit.AddListener(SaveName);
        HostLabel("Název serveru",110);
        serverName=MenuStyle.Input(host.transform,"Název serveru",PlayerPrefs.GetString("gameName","Víkend na chatě"),new Vector2(-290,110),new Vector2(420,64),30);
        HostLabel("Herní režim",15);
        deathmatch=MenuStyle.Button(host.transform,"Deathmatch",new Vector2(-428,15),new Vector2(140,62),()=>SetMode(MatchManager.ModeDeathmatch),false,21);
        attack=MenuStyle.Button(host.transform,"Dobývání",new Vector2(-285,15),new Vector2(140,62),()=>SetMode(MatchManager.ModeAttack),false,21);
        push=MenuStyle.Button(host.transform,"Tlačenice",new Vector2(-142,15),new Vector2(140,62),()=>SetMode(MatchManager.ModePush),false,21);
        ruleLabel=MenuStyle.Text(host.transform,"RuleLabel","",new Vector2(-640,-80),new Vector2(280,60),25);
        MenuStyle.Button(host.transform,"−",new Vector2(-455,-80),new Vector2(80,60),()=>ChangeRule(-1),false,35);
        ruleValue=MenuStyle.Text(host.transform,"RuleValue","",new Vector2(-300,-80),new Vector2(210,60),30,null,TextAlignmentOptions.Center);
        MenuStyle.Button(host.transform,"+",new Vector2(-145,-80),new Vector2(80,60),()=>ChangeRule(1),false,35);
        MenuStyle.Rule(host.transform,new Vector2(-430,-140),700);
        MenuStyle.Button(host.transform,"Pokročilé   ›",new Vector2(-430,-190),new Vector2(700,54),()=>advanced.SetActive(!advanced.activeSelf),false,25);
        advanced=MenuStyle.Box(host.transform,"Advanced",new Vector2(-430,-260),new Vector2(700,66),Color.clear).gameObject;
        test=MenuStyle.Button(advanced.transform,"",Vector2.zero,new Vector2(700,58),()=>{ MatchManager.PendingTestCooldowns=!MatchManager.PendingTestCooldowns; RefreshHost(); },false,23);
        advanced.SetActive(false);
        MenuStyle.Text(host.transform,"MapLabel","MAPA",new Vector2(420,230),new Vector2(680,50),27,MenuStyle.Muted);
        mapPreview=MenuStyle.Box(host.transform,"MapPreview",new Vector2(420,60),new Vector2(680,265));
        mapPreview.type=Image.Type.Simple; mapPreview.color=Color.white; mapPreview.preserveAspect=true;
        for(int i=0;i<mapButtons.Length;i++)
        {
            // (mapa Tlacenice ma na tlacitku kratsi jmeno; cele jmeno je pod nahledem)
            int map=i; float step=680f/mapButtons.Length;
            mapButtons[i]=MenuStyle.Button(host.transform,map==MatchManager.MapDomasovPush?"Tlačenice":MatchManager.MapNames[i],new Vector2(80+step*(i+.5f),-130),new Vector2(step-12,60),()=>{ HostOptions.Map=map; RefreshHost(); },false,mapButtons.Length>3?21:24);
        }
        mapSummary=MenuStyle.Text(host.transform,"MapSummary","",new Vector2(420,-220),new Vector2(680,70),26,MenuStyle.Cream,TextAlignmentOptions.Center);
        MenuStyle.Rule(host.transform,new Vector2(0,-310),1520);
        MenuStyle.Text(host.transform,"LobbyHint","V lobby vyberete týmy, hrdiny a boty.",new Vector2(-340,-365),new Vector2(820,50),24,MenuStyle.Muted);
        create=MenuStyle.Button(host.transform,"VYTVOŘIT LOBBY",new Vector2(500,-365),new Vector2(510,75),CreateLobby,true,32);
        RefreshHost();
    }
    void HostLabel(string text,float y)=>MenuStyle.Text(host.transform,text,text,new Vector2(-640,y),new Vector2(280,60),25);
    void SetMode(int mode)
    {
        HostOptions.Mode=mode;
        // (tlacenice ma vlastni mapu - pri prepnuti na ni nebo z ni se vybere mapa, ktera rezim podporuje)
        if(!MatchManager.MapSupportsMode(HostOptions.Map,mode))HostOptions.Map=mode==MatchManager.ModePush?MatchManager.MapDomasovPush:MatchManager.MapDomasov;
        RefreshHost();
    }
    void ChangeRule(int delta)
    {
        if(HostOptions.Mode==MatchManager.ModePush)return;   // delka tlacenice je pevna
        if(HostOptions.Mode==MatchManager.ModeAttack)HostOptions.CaptureSeconds+=delta*5;
        else HostOptions.Kills+=delta;
        RefreshHost();
    }
    void RefreshHost()
    {
        bool capture=HostOptions.Mode==MatchManager.ModeAttack;
        bool pushing=HostOptions.Mode==MatchManager.ModePush;
        SelectButton(deathmatch,!capture&&!pushing); SelectButton(attack,capture); SelectButton(push,pushing);
        ruleLabel.text=pushing?"Délka zápasu":capture?"Zabrání bodu":"Zabití na výhru";
        ruleValue.text=pushing?$"{MatchManager.PushMatchSeconds/60f:0} min":capture?HostOptions.CaptureSeconds+" s":HostOptions.Kills.ToString();
        for(int i=0;i<mapButtons.Length;i++)
        {
            mapButtons[i].interactable=MatchManager.MapReady(i)&&MatchManager.MapSupportsMode(i,HostOptions.Mode);
            SelectButton(mapButtons[i],HostOptions.Map==i);
        }
        bool ready=MatchManager.MapReady(HostOptions.Map)&&MatchManager.MapSupportsMode(HostOptions.Map,HostOptions.Mode);
        create.interactable=ready;
        mapPreview.sprite=MenuStyle.MapPreview(HostOptions.Map);
        mapPreview.gameObject.SetActive(mapPreview.sprite!=null);
        mapSummary.text=MatchManager.MapNames[HostOptions.Map]+" · "+(pushing?"Tlačenice":capture?"Dobývání":"Team deathmatch");
        mapSummary.textWrappingMode=TextWrappingModes.Normal;
        UiKit.SetButtonLabel(test,"Testovací cooldowny 1 s: "+(MatchManager.PendingTestCooldowns?"ZAPNUTO":"VYPNUTO"));
    }
    static void SelectButton(Button button,bool selected)
    {
        UiKit.SetButtonColor(button,selected?MenuStyle.Mint:MenuStyle.Field);
        button.GetComponentInChildren<TextMeshProUGUI>().color=selected?MenuStyle.Ink:MenuStyle.Cream;
    }
    void CreateLobby()
    {
        SaveName(hostName.text);
        string name=serverName.text.Trim().Replace("<", "").Replace(">", ""); if(name.Length==0)name="Hra hráče "+PlayerName;
        PlayerPrefs.SetString("gameName",name); PlayerPrefs.Save();
        HostOptions.ApplyOnSpawn=true;
        if(!GameConnection.Host(name)) { HostOptions.ApplyOnSpawn=false; ShowStatus(GameConnection.LastError); }
    }
}
