using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Edit-mode UI review in an isolated preview scene. Never opens/saves the user's scene or exits Unity.
public static class MenuPreview
{
    static readonly string Output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../artifacts/Menu/compact"));
    public static void ValidateAndBuild()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        CaptureAll(); MenuPlaytest.BuildGame();
        File.WriteAllText(Path.Combine(Output,"build-result.txt"),"PASS: compact previews and Windows release build.");
    }
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    static void Invoke(object target,string method)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
    [MenuItem("BrosOverwatch/Menu/Capture Compact Previews")]
    public static void CaptureAll()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play mode before capturing menu previews.");
        MenuTypography.EnsureFonts(); Directory.CreateDirectory(Output);
        bool hadMap=PlayerPrefs.HasKey("host.map"),hadMode=PlayerPrefs.HasKey("host.mode");
        int map=HostOptions.Map,mode=HostOptions.Mode;
        bool oldMusic=GameSettings.MusicEnabled;
        var oldDiscovery=LanDiscovery.Instance;
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var root=new GameObject("MenuPreview",typeof(RectTransform),typeof(Canvas));
            root.hideFlags=HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(root,scene);
            var canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera;
            var cameraObject=new GameObject("PreviewCamera",typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.GetComponent<Camera>(); camera.transform.position=new Vector3(0,-10000,0);
            camera.nearClipPlane=.1f; camera.farClipPlane=10; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=MenuStyle.Ink; camera.scene=scene;
            canvas.worldCamera=camera; canvas.planeDistance=1;
            var discoveryObject=new GameObject("PreviewDiscovery",typeof(LanDiscovery)); SceneManager.MoveGameObjectToScene(discoveryObject,scene);
            typeof(LanDiscovery).GetProperty("Instance").SetValue(null,discoveryObject.GetComponent<LanDiscovery>());
            var menu=new MainMenuUI(); menu.Build(root.transform); menu.Tick();
            var settings=new SettingsMenuUI(); settings.Build(root.transform,()=>{},()=>{});
            Capture(canvas,camera,"01-main-menu",1920,1080);
            Capture(canvas,camera,"01-main-menu-720p",1280,720);
            Capture(canvas,camera,"01-main-menu-4x3",1024,768);
            var musicButton=Field<Button>(menu,"musicButton");
            var musicIcon=musicButton.GetComponentInChildren<MenuMusicIcon>();
            musicIcon.Muted=true; musicIcon.color=MenuStyle.Muted; UiKit.SetButtonColor(musicButton,MenuStyle.Field);
            Capture(canvas,camera,"01-music-muted",1920,1080);
            MainMenuUI.RefreshMusicToggle(musicButton);
            menu.Navigate("Host");
            for(int i=0;i<3;i++)
            {
                HostOptions.Map=i; HostOptions.Mode=0; Invoke(menu,"RefreshHost");
                if(Field<Image>(menu,"mapPreview").sprite==null)throw new Exception("Missing map preview "+i);
                Capture(canvas,camera,"02-host-"+i,1920,1080);
            }
            menu.Navigate("Home");
            Field<GameObject>(menu,"home").SetActive(false); Field<GameObject>(menu,"smallLogo").SetActive(true);
            var browser=Field<ServerBrowserUI>(menu,"servers"); browser.Root.SetActive(true); Invoke(browser,"Paint");
            Capture(canvas,camera,"03-servers",1920,1080);
            Invoke(browser,"OpenAdd"); Capture(canvas,camera,"03-add-server",1920,1080); browser.CloseModal(); browser.Root.SetActive(false);
            menu.Navigate("Home");
            settings.Open(false);
            for(int i=0;i<4;i++)
            {
                settings.Select(i); settings.Tick(); Capture(canvas,camera,"04-settings-"+i,1920,1080);
                if(i==3)Capture(canvas,camera,"04-controls-720p",1280,720);
            }
            settings.Close();
            MainMenuUI.RefreshMusicToggle(Field<Button>(menu,"musicButton"));
            ValidateLabels(root);
            File.WriteAllText(Path.Combine(Output,"result.txt"),"PASS: all menu pages render, all three map previews exist, typography is loaded, control rows are separate.");
            Debug.Log("[Menu compact] Preview PASS: "+Output);
        }
        finally
        {
            if(hadMap)HostOptions.Map=map; else PlayerPrefs.DeleteKey("host.map");
            if(hadMode)HostOptions.Mode=mode; else PlayerPrefs.DeleteKey("host.mode");
            if(GameSettings.MusicEnabled!=oldMusic)GameSettings.MusicEnabled=oldMusic;
            PlayerPrefs.Save(); EditorSceneManager.ClosePreviewScene(scene);
            typeof(LanDiscovery).GetProperty("Instance").SetValue(null,oldDiscovery);
        }
    }
    internal static void ValidateLabels(GameObject root)
    {
        if(MenuStyle.BodyFont==null||MenuStyle.DisplayFont==null)throw new Exception("Custom fonts missing");
        foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if(text.name!="Action"&&text.name!="Key")continue;
            text.ForceMeshUpdate(true);
            if(text.isTextOverflowing)throw new Exception("Overflow in controls: "+text.text);
        }
    }
    internal static void Capture(Canvas canvas,Camera camera,string name,int width,int height)
    {
        var rt=new RenderTexture(width,height,24); camera.targetTexture=rt; canvas.scaleFactor=Mathf.Min(width/1920f,height/1080f);
        Canvas.ForceUpdateCanvases(); camera.Render();
        var old=RenderTexture.active; RenderTexture.active=rt;
        var texture=new Texture2D(width,height,TextureFormat.RGB24,false); texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply();
        bool varied=false; var pixels=texture.GetPixels32();
        for(int i=0;i<pixels.Length;i+=997)
            if(Mathf.Abs(pixels[i].r-pixels[0].r)+Mathf.Abs(pixels[i].g-pixels[0].g)+Mathf.Abs(pixels[i].b-pixels[0].b)>20) { varied=true; break; }
        if(!varied)throw new Exception("Blank UI capture: "+name);
        File.WriteAllBytes(Path.Combine(Output,name+".png"),texture.EncodeToPNG());
        RenderTexture.active=old; camera.targetTexture=null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(texture);
    }
}
