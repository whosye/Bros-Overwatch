using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Landing -> host options / address book. Joining and hosting never share a form.
public partial class MainMenuUI
{
    public GameObject Root { get; private set; }
    public Action OnSettings;
    public string Page { get; private set; }="Home";
    GameObject home,host,profile,smallLogo;
    readonly ServerBrowserUI servers=new ServerBrowserUI();
    Button nameButton,musicButton;
    TMP_InputField profileName,hostName,serverName;
    TextMeshProUGUI statusText;
    public void Build(Transform canvas)
    {
        var root=UiKit.MakeImage(canvas,"MainMenu",Color.clear,MenuStyle.Center,Vector2.zero,Vector2.zero);
        UiKit.Stretch(root.rectTransform); root.raycastTarget=true; Root=root.gameObject;
        MenuStyle.Background(Root.transform);
        var scrim=new GameObject("Readability",typeof(RectTransform),typeof(MenuScrim)); scrim.transform.SetParent(Root.transform,false);
        UiKit.Stretch((RectTransform)scrim.transform); scrim.GetComponent<MenuScrim>().raycastTarget=false;
        home=MenuStyle.Box(Root.transform,"Home",Vector2.zero,new Vector2(1920,1080),Color.clear).gameObject;
        var title=MenuStyle.Text(home.transform,"GameTitle","BROS\nOVERWATCH",new Vector2(-560,225),new Vector2(600,220),84);
        title.textWrappingMode=TextWrappingModes.Normal; title.fontStyle=FontStyles.Bold|FontStyles.Italic;
        MenuStyle.Box(home.transform,"TitleAccent",new Vector2(-640,105),new Vector2(440,3),MenuStyle.Peach);
        MenuStyle.Button(home.transform,"+   ZALOŽIT HRU",new Vector2(-625,35),new Vector2(470,70),()=>Navigate("Host"),true,36);
        HomeAction("SERVERY",-55,()=>Navigate("Servers"));
        HomeAction("NASTAVENÍ",-135,()=>OnSettings?.Invoke());
        HomeAction("UKONČIT",-215,QuitGame);
        smallLogo=MenuStyle.Text(Root.transform,"Wordmark","BROS OVERWATCH",new Vector2(-580,470),new Vector2(640,60),32).gameObject;
        smallLogo.GetComponent<TextMeshProUGUI>().fontStyle=FontStyles.Bold|FontStyles.Italic;
        BuildHost();
        servers.Build(Root.transform,()=>Navigate("Home"),SaveName,ShowStatus);
        nameButton=MenuStyle.Button(Root.transform,"",new Vector2(640,470),new Vector2(260,56),OpenProfile,false,25);
        UiKit.SetButtonColor(nameButton,Color.clear);
        musicButton=BuildMusicToggle(Root.transform);
        statusText=MenuStyle.Text(Root.transform,"MenuStatus","",new Vector2(0,-495),new Vector2(1650,65),25,MenuStyle.Peach,TextAlignmentOptions.Center);
        BuildProfile(); Navigate("Home");
    }
    void HomeAction(string label,float y,UnityEngine.Events.UnityAction action)
    {
        var b=MenuStyle.Button(home.transform,label,new Vector2(-625,y),new Vector2(470,56),action,false,34);
        UiKit.SetButtonColor(b,Color.clear);
        b.GetComponentInChildren<TextMeshProUGUI>().alignment=TextAlignmentOptions.Left;
        b.GetComponentInChildren<TextMeshProUGUI>().margin=new Vector4(20,0,0,0);
    }
    public void Navigate(string page)
    {
        Page=page; home.SetActive(page=="Home"); host.SetActive(page=="Host"); smallLogo.SetActive(page!="Home");
        servers.SetVisible(page=="Servers"); profile.SetActive(false); ShowStatus("");
        if(page=="Host") { hostName.SetTextWithoutNotify(PlayerName); RefreshHost(); }
        PlayerPrefs.Save();
    }
    public static string PlayerName=>PlayerPrefs.GetString("nick","Hráč");
    public static void SaveName(string value)
    {
        value=(value??"").Trim().Replace("<", "").Replace(">", ""); if(value.Length==0)value="Hráč"; if(value.Length>20)value=value.Substring(0,20);
        PlayerHero.PreferredName=value; PlayerPrefs.SetString("nick",value); PlayerPrefs.Save();
    }
    void BuildProfile()
    {
        var veil=UiKit.MakeImage(Root.transform,"RenameOverlay",new Color(0,0,0,.65f),MenuStyle.Center,Vector2.zero,Vector2.zero);
        UiKit.Stretch(veil.rectTransform); veil.raycastTarget=true; profile=veil.gameObject;
        var box=MenuStyle.Box(profile.transform,"Rename",Vector2.zero,new Vector2(760,360));
        MenuStyle.Text(box.transform,"Title","JMÉNO HRÁČE",new Vector2(0,100),new Vector2(640,60),38).fontStyle=FontStyles.Bold;
        MenuStyle.Compact(box.transform,.88f);
        profileName=MenuStyle.Input(box.transform,"Jméno hráče",PlayerName,new Vector2(0,10),new Vector2(640,65),20);
        MenuStyle.Button(box.transform,"ZRUŠIT",new Vector2(-170,-100),new Vector2(280,65),()=>profile.SetActive(false));
        MenuStyle.Button(box.transform,"ULOŽIT",new Vector2(170,-100),new Vector2(280,65),()=>{ SaveName(profileName.text); profile.SetActive(false); hostName.SetTextWithoutNotify(PlayerName); servers.SyncName(); },true);
        profile.SetActive(false);
    }
    void OpenProfile() { profileName.SetTextWithoutNotify(PlayerName); profile.SetActive(true); profile.transform.SetAsLastSibling(); profileName.ActivateInputField(); }
    public void SetVisible(bool visible)
    {
        if(Root==null||Root.activeSelf==visible)return;
        Root.SetActive(visible);
        if(!visible) { LanDiscovery.Instance?.StopListening(); profile.SetActive(false); }
        else { Cursor.lockState=CursorLockMode.None; servers.SetVisible(Page=="Servers",true); }
    }
    public void ShowStatus(string value) { if(statusText!=null)statusText.text=value??""; }
    public void Tick(bool settingsOpen=false)
    {
        if(Root==null||!Root.activeSelf)return;
        RefreshMusicToggle(musicButton); UiKit.SetButtonLabel(nameButton,PlayerName+"   / upravit");
        servers.Tick();
        if(!settingsOpen&&Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if(servers.CloseModal())return;
            if(profile.activeSelf)profile.SetActive(false);
            else if(Page!="Home")Navigate("Home");
        }
    }
    public static Button BuildMusicToggle(Transform parent)
    {
        var b=MenuStyle.Button(parent,"",new Vector2(865,470),new Vector2(64,46),()=>GameSettings.MusicEnabled=!GameSettings.MusicEnabled,false,28);
        var icon=new GameObject("MusicIcon",typeof(RectTransform),typeof(MenuMusicIcon));
        icon.transform.SetParent(b.transform,false);
        var rect=(RectTransform)icon.transform; rect.anchorMin=rect.anchorMax=rect.pivot=MenuStyle.Center; rect.sizeDelta=new Vector2(32,32);
        icon.GetComponent<MenuMusicIcon>().raycastTarget=false;
        RefreshMusicToggle(b); return b;
    }
    public static void RefreshMusicToggle(Button button)
    {
        if(button==null)return;
        UiKit.SetButtonLabel(button,"");
        var icon=button.GetComponentInChildren<MenuMusicIcon>();
        if(icon!=null) { icon.Muted=!GameSettings.MusicEnabled; icon.color=GameSettings.MusicEnabled?MenuStyle.Ink:MenuStyle.Muted; }
        UiKit.SetButtonColor(button,GameSettings.MusicEnabled?MenuStyle.Mint:MenuStyle.Field);
        button.GetComponentInChildren<TextMeshProUGUI>().color=GameSettings.MusicEnabled?MenuStyle.Ink:MenuStyle.Muted;
    }
    void QuitGame()
    {
        PlayerPrefs.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying=false;
#else
        Application.Quit();
#endif
    }
}
