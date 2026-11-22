using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class SettingsMenuUI
{
    public GameObject Root { get; private set; }
    public bool IsOpen=>Root!=null&&Root.activeSelf;
    public bool InGame { get; private set; }
    readonly GameObject[] pages=new GameObject[4];
    readonly Button[] tabs=new Button[4];
    TextMeshProUGUI hint;
    Button closeButton,leaveButton,headBob,music,graphics;
    Slider sensitivity,bob,master,musicVolume,voice,ambient;
    TextMeshProUGUI sensValue,bobValue,masterValue,musicValue,voiceValue,ambientValue;
    Action resume;
    int openedFrame;
    static int escapeHandledFrame = -1;
    public static bool EscapeHandledThisFrame => escapeHandledFrame == Time.frameCount;
    public void Build(Transform parent,Action onResume,Action onLeave)
    {
        resume=onResume;
        var veil=UiKit.MakeImage(parent,"SettingsOverlay",new Color(.015f,.035f,.025f,.72f),MenuStyle.Center,Vector2.zero,Vector2.zero);
        UiKit.Stretch(veil.rectTransform); veil.raycastTarget=true; Root=veil.gameObject;
        var panel=MenuStyle.Box(Root.transform,"SettingsPanel",Vector2.zero,new Vector2(1260,850));
        MenuStyle.Compact(panel.transform,.80f);
        MenuStyle.Text(panel.transform,"Title","NASTAVENÍ",new Vector2(0,325),new Vector2(1100,80),56).fontStyle=FontStyles.Bold|FontStyles.Italic;
        string[] names={"HRA","ZVUK","OBRAZ","OVLÁDÁNÍ"};
        for(int i=0;i<4;i++)
        {
            int n=i; tabs[i]=MenuStyle.Button(panel.transform,names[i],new Vector2(-420+i*280,225),new Vector2(250,56),()=>Select(n));
            pages[i]=MenuStyle.Box(panel.transform,"Settings"+names[i],new Vector2(0,-20),new Vector2(1100,400),Color.clear).gameObject;
        }
        MenuStyle.Rule(panel.transform,new Vector2(0,175),1100);
        sensitivity=Row(pages[0].transform,"Citlivost myši",120,0,1,SensitivityValue(),v=>GameSettings.Sensitivity=Mathf.Round(GameSettings.MinSensitivity*Mathf.Pow(GameSettings.MaxSensitivity/GameSettings.MinSensitivity,v)*100)/100,out sensValue);
        headBob=MenuStyle.Button(pages[0].transform,"",new Vector2(0,15),new Vector2(1000,60),()=>GameSettings.HeadBobEnabled=!GameSettings.HeadBobEnabled);
        bob=Row(pages[0].transform,"Intenzita pohupování",-95,0,200,GameSettings.HeadBobIntensity*100,v=>GameSettings.HeadBobIntensity=v/100,out bobValue);
        master=Row(pages[1].transform,"Celková hlasitost",145,0,100,GameSettings.Volume*100,v=>GameSettings.Volume=v/100,out masterValue);
        musicVolume=Row(pages[1].transform,"Hudba",65,0,100,GameSettings.MusicVolume*100,v=>GameSettings.MusicVolume=v/100,out musicValue);
        voice=Row(pages[1].transform,"Hlášky postav",-15,0,100,GameSettings.VoiceVolume*100,v=>GameSettings.VoiceVolume=v/100,out voiceValue);
        ambient=Row(pages[1].transform,"Zvuky prostředí",-95,0,100,GameSettings.AmbientVolume*100,v=>GameSettings.AmbientVolume=v/100,out ambientValue);
        music=MenuStyle.Button(pages[1].transform,"",new Vector2(0,-175),new Vector2(1000,50),()=>GameSettings.MusicEnabled=!GameSettings.MusicEnabled,false,23);
        graphics=MenuStyle.Button(pages[2].transform,"",new Vector2(0,100),new Vector2(1000,70),()=>GraphicsQuality.Level=(GraphicsQuality.Level+1)%GraphicsQuality.Names.Length);
        MenuStyle.Text(pages[2].transform,"GraphicsHint","Změny se projeví ihned.",new Vector2(0,15),new Vector2(1000,60),26,MenuStyle.Muted);
        ControlGroup(pages[3].transform,-275,"POHYB A BOJ",
            new[]{"WASD","Mezerník","Ctrl","Shift","Levé tlačítko","Pravé tlačítko","E","Q"},
            new[]{"Pohyb","Skok","Dřep","Pohyb / schopnost","Útok","Alt. útok / míření","Schopnost","Ultimátka"});
        ControlGroup(pages[3].transform,275,"DALŠÍ KLÁVESY",
            new[]{"R","Tab","F1","F2","Enter","Esc"},
            new[]{"Přebití","Tabulka hráčů","Změna hrdiny","Schopnosti hrdiny","Chat (Tab: kanál)","Nastavení / zpět"});
        MenuStyle.Rule(panel.transform,new Vector2(0,-290),1100);
        hint=MenuStyle.Text(panel.transform,"SettingsHint","",new Vector2(0,-255),new Vector2(1100,42),23,MenuStyle.Muted,TextAlignmentOptions.Center);
        closeButton=MenuStyle.Button(panel.transform,"POKRAČOVAT",new Vector2(-250,-355),new Vector2(490,74),CloseAndResume,true,30);
        leaveButton=MenuStyle.Button(panel.transform,"OPUSTIT HRU",new Vector2(285,-355),new Vector2(430,74),()=>{ Close(); onLeave(); },false,28);
        Root.SetActive(false); Select(1);
    }
    void ControlGroup(Transform parent,float x,string heading,string[] keys,string[] actions)
    {
        var group=MenuStyle.Box(parent,heading,new Vector2(x,0),new Vector2(520,400),Color.clear).transform;
        MenuStyle.Text(group,"GroupTitle",heading,new Vector2(0,160),new Vector2(520,40),24,MenuStyle.Muted);
        for(int i=0;i<keys.Length;i++)
        {
            float y=105-i*43;
            var key=MenuStyle.Box(group,"Keycap",new Vector2(-180,y),new Vector2(150,36),MenuStyle.Field);
            MenuStyle.Text(key.transform,"Key",keys[i],Vector2.zero,new Vector2(144,32),24,MenuStyle.Cream,TextAlignmentOptions.Center);
            MenuStyle.Text(group,"Action",actions[i],new Vector2(90,y),new Vector2(350,38),30);
        }
    }
    Slider Row(Transform parent,string title,float y,float min,float max,float value,UnityEngine.Events.UnityAction<float> changed,out TextMeshProUGUI display)
    {
        MenuStyle.Text(parent,title,title,new Vector2(-340,y),new Vector2(400,50),28);
        var slider=UiKit.MakeSlider(parent,title,MenuStyle.Center,new Vector2(160,y),new Vector2(440,44),min,max,value,changed,max>1);
        foreach(var image in slider.GetComponentsInChildren<Image>())
        {
            if(image.name=="Fill")image.color=MenuStyle.Mint;
            if(image.name=="Track")image.color=MenuStyle.Field;
            if(image.name=="Handle")
            {
                image.color=MenuStyle.Cream;
                // Slider stretches handles vertically; subtract the area height from the desired size.
                image.rectTransform.sizeDelta=new Vector2(24,-12);
            }
            MenuStyle.Round(image);
        }
        display=MenuStyle.Text(parent,title+"Value","",new Vector2(470,y),new Vector2(130,50),26,MenuStyle.Cream,TextAlignmentOptions.Right);
        return slider;
    }
    static float SensitivityValue()=>Mathf.Log(Mathf.Clamp(GameSettings.Sensitivity,GameSettings.MinSensitivity,GameSettings.MaxSensitivity)/GameSettings.MinSensitivity)/Mathf.Log(GameSettings.MaxSensitivity/GameSettings.MinSensitivity);
    public void Select(int index)
    {
        for(int i=0;i<pages.Length;i++)
        {
            pages[i].SetActive(i==index); UiKit.SetButtonColor(tabs[i],i==index?MenuStyle.Mint:MenuStyle.Field);
            tabs[i].GetComponentInChildren<TextMeshProUGUI>().color=i==index?MenuStyle.Ink:MenuStyle.Cream;
        }
    }
    public void Open(bool inGame)
    {
        openedFrame=Time.frameCount; InGame=inGame; Root.SetActive(true); Root.transform.SetAsLastSibling();
        leaveButton.gameObject.SetActive(inGame);
        ((RectTransform)closeButton.transform).anchoredPosition=new Vector2(inGame?-250:0,-355);
        UiKit.SetButtonLabel(closeButton,inGame?"POKRAČOVAT":"ZPĚT DO MENU");
        hint.text=inGame?"Zápas pokračuje online.":"Nastavení se ukládá automaticky.";
    }
    public void Close() { Root.SetActive(false); PlayerPrefs.Save(); }
    void CloseAndResume() { bool wasGame=InGame; Close(); if(wasGame)resume(); }
    public void Tick()
    {
        if(!IsOpen)return;
        if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame&&Time.frameCount>openedFrame)
        {
            escapeHandledFrame=Time.frameCount;
            if(InGame)CloseAndResume(); else Close();
            return;
        }
        sensitivity.SetValueWithoutNotify(SensitivityValue()); sensValue.text=GameSettings.Sensitivity.ToString("0.00");
        bob.SetValueWithoutNotify(GameSettings.HeadBobIntensity*100); bob.interactable=GameSettings.HeadBobEnabled; bobValue.text=Mathf.RoundToInt(GameSettings.HeadBobIntensity*100)+" %";
        master.SetValueWithoutNotify(GameSettings.Volume*100); masterValue.text=Mathf.RoundToInt(GameSettings.Volume*100)+" %";
        musicVolume.SetValueWithoutNotify(GameSettings.MusicVolume*100); musicValue.text=Mathf.RoundToInt(GameSettings.MusicVolume*100)+" %";
        voice.SetValueWithoutNotify(GameSettings.VoiceVolume*100); voiceValue.text=Mathf.RoundToInt(GameSettings.VoiceVolume*100)+" %";
        ambient.SetValueWithoutNotify(GameSettings.AmbientVolume*100); ambientValue.text=Mathf.RoundToInt(GameSettings.AmbientVolume*100)+" %";
        UiKit.SetButtonLabel(headBob,"Pohupování kamery: "+(GameSettings.HeadBobEnabled?"ZAPNUTO":"VYPNUTO"));
        UiKit.SetButtonLabel(music,"Hudba v menu: "+(GameSettings.MusicEnabled?"ZAPNUTO":"VYPNUTO"));
        UiKit.SetButtonLabel(graphics,"Kvalita grafiky: "+GraphicsQuality.Names[GraphicsQuality.Level]+"   ›");
    }
}
