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
        bool layoutOnly=File.ReadAllText(Request).Trim()=="layout-only";
        File.Delete(Request);
        try
        {
            Capture();
            if(!layoutOnly)MenuPlaytest.BuildGame();
            File.WriteAllText(Request+".result",layoutOnly?"PASS: lobby layout and hero details.":"PASS: lobby layout, hero details and Windows build.");
        }
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
            var stage=new GameObject("ReviewShowcase"); SceneManager.MoveGameObjectToScene(stage,scene);
            var showcase=stage.AddComponent<LobbyShowcase>();
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(LobbyShowcase).GetMethod("Build",hidden).Invoke(showcase,null);
            var heroCamera=(Camera)typeof(LobbyShowcase).GetField("view",hidden).GetValue(showcase);
            heroCamera.scene=scene;
            var cameraPosition=heroCamera.transform.localPosition;
            var cameraRotation=heroCamera.transform.localRotation;
            // Keep only the isolated stage in this camera; the UI review camera is far away.
            for(int i=0;i<HeroRegistry.All.Length;i++)
            {
                typeof(LobbyShowcase).GetMethod("SetHero",hidden).Invoke(showcase,new object[]{i,0});
                stage.GetComponentInChildren<CharacterVisual>().EvaluateIdlePose();
                var held=stage.GetComponentInChildren<HeldWeapons>();
                typeof(HeldWeapons).GetMethod("Update",hidden).Invoke(held,null);
                typeof(HeldWeapons).GetMethod("LateUpdate",hidden).Invoke(held,null);
                typeof(LobbyShowcase).GetMethod("FrameHero",hidden).Invoke(showcase,null);
                var bodyVisual=stage.GetComponentInChildren<CharacterVisual>();
                var footCenter=(bodyVisual.GetBone(HumanBodyBones.LeftFoot).position+bodyVisual.GetBone(HumanBodyBones.RightFoot).position)*.5f-stage.transform.position;
                if(new Vector2(footCenter.x,footCenter.z).magnitude>.001f)throw new Exception("Off-center hero feet: "+HeroRegistry.All[i].heroName);
                object[] bodyArgs={new Bounds()};
                typeof(LobbyShowcase).GetMethod("TryBodyBounds",hidden).Invoke(showcase,bodyArgs);
                var bounds=(Bounds)bodyArgs[0];
                if(Mathf.Abs(bounds.min.y-stage.transform.position.y)>.001f)throw new Exception("Hero is not grounded: "+HeroRegistry.All[i].heroName);
                if(Vector3.Distance(cameraPosition,heroCamera.transform.localPosition)>.0001f||Quaternion.Angle(cameraRotation,heroCamera.transform.localRotation)>.001f)
                    throw new Exception("Hero moved the preview camera: "+HeroRegistry.All[i].heroName);
                heroCamera.Render();
                VerifyHeroPreview(heroCamera.targetTexture,HeroRegistry.All[i].heroName);
                typeof(LobbyUI).GetMethod("ReviewForEditor",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(lobby,new object[]{i,1});
                var image=(RawImage)typeof(LobbyUI).GetField("characterPreview",hidden).GetValue(lobby);
                image.texture=heroCamera.targetTexture; image.enabled=true;
                Canvas.ForceUpdateCanvases();
                foreach(var text in lobby.Root.GetComponentsInChildren<TMP_Text>())
                {
                    if(text.name!="Description"&&text.name!="Stats"&&text.name!="Key")continue;
                    text.ForceMeshUpdate();
                    if(text.isTextOverflowing)throw new Exception("Hero details overflow: "+HeroRegistry.All[i].heroName+" / "+text.name);
                }
                MenuPreview.Capture(canvas,camera,"lobby-hero-"+i,1920,1080);
            }
            typeof(LobbyShowcase).GetMethod("SetHero",hidden).Invoke(showcase,new object[]{4,0});
            stage.GetComponentInChildren<CharacterVisual>().EvaluateIdlePose();
            var selectedWeapon=stage.GetComponentInChildren<HeldWeapons>();
            typeof(HeldWeapons).GetMethod("Update",hidden).Invoke(selectedWeapon,null);
            typeof(HeldWeapons).GetMethod("LateUpdate",hidden).Invoke(selectedWeapon,null);
            typeof(LobbyShowcase).GetMethod("FrameHero",hidden).Invoke(showcase,null);
            heroCamera.Render();
            for(int page=0;page<3;page++)
            {
                typeof(LobbyUI).GetMethod("ReviewForEditor",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(lobby,new object[]{4,page});
                var image=(RawImage)typeof(LobbyUI).GetField("characterPreview",hidden).GetValue(lobby);
                image.texture=heroCamera.targetTexture; image.enabled=true;
                Canvas.ForceUpdateCanvases();
                foreach(var scroll in lobby.Root.GetComponentsInChildren<ScrollRect>())
                    if(scroll.verticalScrollbar!=null)scroll.verticalScrollbar.gameObject.SetActive(scroll.content.rect.height>scroll.viewport.rect.height+1);
                MenuPreview.Capture(canvas,camera,"lobby-"+page,1920,1080);
                if(page==0)
                {
                    MenuPreview.Capture(canvas,camera,"lobby-720p",1280,720);
                    typeof(LobbyUI).GetMethod("ReviewChoicesForEditor",hidden).Invoke(lobby,new object[]{(Action)(()=>MenuPreview.Capture(canvas,camera,"lobby-choices",1920,1080))});
                    Canvas.ForceUpdateCanvases();
                    foreach(var text in lobby.Root.GetComponentsInChildren<TMP_Text>())
                    {
                        text.ForceMeshUpdate();
                        if(text.name=="FieldLabel"&&text.isTextOverflowing)throw new Exception("Lobby field label overflow: "+text.text);
                    }
                }
            }
            Debug.Log("[Lobby review] PASS: all heroes, 3 panels, 1080p and 720p.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    static void VerifyHeroPreview(RenderTexture render,string hero)
    {
        var previous=RenderTexture.active;
        var pixels=new Texture2D(render.width,render.height,TextureFormat.RGBA32,false);
        try
        {
            RenderTexture.active=render;
            pixels.ReadPixels(new Rect(0,0,render.width,render.height),0,0); pixels.Apply();
            var colors=pixels.GetPixels32(); int visible=0,total=0;
            // Exclude the platform at the bottom: it must be the character that is visible.
            for(int y=render.height/3;y<render.height*19/20;y+=4)
                for(int x=render.width/5;x<render.width*4/5;x+=4)
                {
                    total++;
                    var color=colors[y*render.width+x];
                    if(color.a>8&&color.r+color.g+color.b>24)visible++;
                }
            if(visible<total/100)throw new Exception("Empty character preview: "+hero);
        }
        finally { RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(pixels); }
    }

}
