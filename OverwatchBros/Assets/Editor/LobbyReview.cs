using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class LobbyReview
{
    static readonly string Request=Path.GetFullPath(Path.Combine(Application.dataPath,"../../artifacts/Menu/lobby-review.request"));
    static LobbyReview() { EditorApplication.delayCall+=RunRequested; }
    static void RunRequested()
    {
        if(!File.Exists(Request)||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(Request);
        try { Capture(); MenuPlaytest.BuildGame(); File.WriteAllText(Request+".result","PASS: lobby layout, hero details and Windows build."); }
        catch(Exception e) { File.WriteAllText(Request+".result","FAIL: "+e); Debug.LogException(e); }
    }
    [MenuItem("BrosOverwatch/Menu/Review Lobby Layout")]
    public static void Capture()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Review requires edit mode.");
        MenuTypography.EnsureFonts();
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var root=new GameObject("LobbyReview",typeof(RectTransform),typeof(Canvas));
            SceneManager.MoveGameObjectToScene(root,scene); root.hideFlags=HideFlags.HideAndDontSave;
            var canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera;
            var go=new GameObject("ReviewCamera",typeof(Camera)); SceneManager.MoveGameObjectToScene(go,scene);
            var camera=go.GetComponent<Camera>(); camera.scene=scene; camera.transform.position=new Vector3(0,-10000,0);
            camera.nearClipPlane=.1f; camera.farClipPlane=10; camera.clearFlags=CameraClearFlags.SolidColor;
            canvas.worldCamera=camera; canvas.planeDistance=1;
            var lobby=new LobbyUI(); lobby.Build(root.transform,()=>{});
            for(int i=0;i<HeroRegistry.All.Length;i++)
            {
                typeof(LobbyUI).GetMethod("ReviewForEditor",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(lobby,new object[]{i,1});
                Canvas.ForceUpdateCanvases();
                foreach(var text in lobby.Root.GetComponentsInChildren<TMP_Text>())
                {
                    if(text.name!="Description"&&text.name!="Stats"&&text.name!="Key")continue;
                    text.ForceMeshUpdate();
                    if(text.isTextOverflowing)throw new Exception("Hero details overflow: "+HeroRegistry.All[i].heroName+" / "+text.name);
                }
            }
            for(int page=0;page<3;page++)
            {
                typeof(LobbyUI).GetMethod("ReviewForEditor",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(lobby,new object[]{0,page});
                Canvas.ForceUpdateCanvases();
                foreach(var scroll in lobby.Root.GetComponentsInChildren<ScrollRect>())
                    if(scroll.verticalScrollbar!=null)scroll.verticalScrollbar.gameObject.SetActive(scroll.content.rect.height>scroll.viewport.rect.height+1);
                MenuPreview.Capture(canvas,camera,"lobby-"+page,1920,1080);
                if(page==0)MenuPreview.Capture(canvas,camera,"lobby-720p",1280,720);
            }
            Debug.Log("[Lobby review] PASS: all heroes, 3 panels, 1080p and 720p.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
