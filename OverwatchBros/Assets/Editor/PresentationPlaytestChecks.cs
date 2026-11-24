using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

public sealed class PresentationPlaytestChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly string Output=Path.GetFullPath("../tools/sound-collector/.qa/presentation");
    static void Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Private).Invoke(target,args);
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Private).GetValue(target);
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
    static void Screenshot(string name)
    {
        Directory.CreateDirectory(Output);
        if(name.StartsWith("chat"))
        {
            PlaytestFeedbackCheckBatch.CaptureUI(name);
            File.Copy(Path.Combine(PlaytestFeedbackCheckBatch.Output,name+".png"),Path.Combine(Output,name+".png"),true);
            return;
        }
        var camera=NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<FirstPersonController>().playerCamera;
        var target=new RenderTexture(1920,1080,24);
        var previous=RenderTexture.active;
        try
        {
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=target });
            RenderTexture.active=target;
            var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1920,1080),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG()); Object.DestroyImmediate(image);
        }
        finally { RenderTexture.active=previous; target.Release(); Object.DestroyImmediate(target); }
    }

    [UnityTest]
    public IEnumerator CompactChatAndTexturedCriticalRevolver()
    {
        foreach(string part in new[]{"Material.001","Material.002"})
        {
            var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Weapons/HolyRevolver/"+part+".mat");
            Assert.IsTrue(mat.IsKeywordEnabled("_EMISSION"),"The player build must retain an emissive shader variant.");
            Assert.IsNotNull(mat.GetTexture("_BumpMap"));
            Assert.IsNotNull(mat.GetTexture("_MetallicGlossMap"));
            if(part=="Material.001") Assert.IsNotNull(mat.GetTexture("_BaseMap"));
            else Assert.IsNotNull(mat.GetTexture("_OcclusionMap"));
            Assert.Less(mat.GetColor("_EmissionColor").maxColorComponent,.001f);
        }
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        GameSettings.ForceCursorLocked=true;
        var network=NetworkManager.Singleton;
        network.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1",17895);
        Assert.IsTrue(network.StartHost());
        for(int i=0;i<120 && (MatchManager.Instance==null || !MatchManager.Instance.IsSpawned || network.LocalClient.PlayerObject==null);i++) yield return null;
        var match=MatchManager.Instance;
        var player=network.LocalClient.PlayerObject;
        var hero=player.GetComponent<PlayerHero>();
        int sindel=Array.FindIndex(HeroRegistry.All,h=>h.name=="Sindel");
        Assert.GreaterOrEqual(sindel,0);
        player.GetComponent<PlayerTeam>().SelectTeam(0);
        hero.SelectHero(sindel);
        hero.ConfirmJoin();
        yield return null;
        match.SetGameMode(MatchManager.ModeDeathmatch);
        match.StartMatch();
        float waitUntil=Time.time+.5f;
        while(Time.time<waitUntil) yield return null;
        Assert.AreEqual("Sindel",hero.Hero.name);
        match.enabled=false;
        var movement=player.GetComponent<FirstPersonController>();
        movement.enabled=false;
        var shooting=player.GetComponent<WeaponShooting>();
        shooting.enabled=false;
        var held=player.GetComponent<HeldWeapons>();
        Call(held,"Rebuild",true);

        var chat=TextChatUI.Instance;
        Assert.IsNotNull(chat);
        for(int i=0;i<6;i++) TextChatUI.Receive("Hráč "+i,i%2==0?"Kryju vám záda, jdeme na bod.":"Pozor na pravou stranu!",i%2,i%2==0);
        yield return null;
        var panel=Field<GameObject>(chat,"panel");
        var rect=(RectTransform)panel.transform;
        Assert.AreEqual(420,rect.rect.width);
        Assert.LessOrEqual(rect.rect.height,TextChatUI.MaxPassiveHeight);
        Assert.LessOrEqual(panel.GetComponent<Image>().color.a,.25f);
        var corners=new Vector3[4]; rect.GetWorldCorners(corners);
        Assert.Less(corners[2].x,Screen.width*.5f,"Chat must remain left of the crosshair.");
        Assert.Less(corners[2].y,Screen.height*.5f,"Chat must not cover the aiming area.");
        Assert.LessOrEqual(panel.transform.Cast<Transform>().Count(t=>t.name.StartsWith("Message") && t.gameObject.activeSelf),2);
        Screenshot("chat-passive");
        yield return null;
        Assert.IsTrue(chat.Open());
        Assert.LessOrEqual(rect.rect.height,TextChatUI.MaxTypingHeight);
        Assert.Less(rect.rect.width*rect.rect.height,620*350*.45f,"Open chat must occupy under 45% of its previous area.");
        chat.ToggleChannel(); Assert.IsTrue(chat.TeamOnly);
        chat.Draft.text="Ahoj týme — už to nezakrývá výhled.";
        Screenshot("chat-typing");
        yield return null;
        Assert.IsTrue(chat.Submit());
        yield return null;
        Assert.IsFalse(TextChatUI.IsTyping);
        Assert.IsTrue(chat.Messages.Any(line=>line.text.Contains("nezakrývá")),"The compact composer must still send messages.");
        TextChatUI.Receive("Velmi dlouhé jméno hráče pro kontrolu zkrácení",new string('W',300),0,false);
        yield return null;
        Assert.LessOrEqual(rect.rect.height,TextChatUI.MaxPassiveHeight);
        Assert.IsTrue(chat.Open());
        Assert.LessOrEqual(rect.rect.height,TextChatUI.MaxTypingHeight);
        chat.Close();
        panel.SetActive(false);

        Set(held,"powerFlashUntil",0f);
        for(int fired=0;fired<9;fired++)
        {
            Set(shooting,"currentAmmo",9-fired);
            Call(held,"UpdatePowerGlow");
            bool expected=(fired+1)%3==0;
            Assert.AreEqual(expected,shooting.PowerShotReady,"Shots 3, 6 and 9 must be empowered.");
            var materials=Field<List<Material>>(held,"powerMaterials");
            if(materials!=null)
                foreach(var mat in materials.Where(m=>m.name.StartsWith("Material.00")))
                {
                    Assert.IsTrue(mat.IsKeywordEnabled("_EMISSION"));
                    float red=mat.GetColor("_EmissionColor").r;
                    if(expected) Assert.Greater(red,.5f); else Assert.Less(red,.001f);
                }
        }
        Set(shooting,"currentAmmo",9); Call(held,"UpdatePowerGlow");
        Screenshot("revolver-normal");
        yield return null;
        Set(shooting,"currentAmmo",7); Call(held,"UpdatePowerGlow");
        Assert.IsNotEmpty(Field<List<Material>>(held,"powerMaterials"));
        Screenshot("revolver-critical-ready");
        yield return null;
        Set(shooting,"reloading",true); Call(held,"UpdatePowerGlow");
        foreach(var mat in Field<List<Material>>(held,"powerMaterials")) Assert.Less(mat.GetColor("_EmissionColor").r,.001f);
        Set(shooting,"reloading",false); Set(shooting,"currentAmmo",6);
        held.FlashPower(); Call(held,"UpdatePowerGlow");
        Assert.Greater(Field<List<Material>>(held,"powerMaterials")[0].GetColor("_EmissionColor").r,.5f);
        // This is an EditMode UnityTest crossing into play mode: advance real
        // player frames explicitly rather than yielding WaitForSeconds.
        waitUntil=Time.time+.3f;
        while(Time.time<waitUntil) yield return null;
        Call(held,"UpdatePowerGlow");
        foreach(var mat in Field<List<Material>>(held,"powerMaterials")) Assert.Less(mat.GetColor("_EmissionColor").r,.001f,
            $"ammo={shooting.CurrentAmmo}/{shooting.MaxAmmo}, ready={shooting.PowerShotReady}, time={Time.time}, flash={Field<float>(held,"powerFlashUntil")}, shown={Field<bool>(held,"powerShown")}");
        foreach(string part in new[]{"Material.001","Material.002"})
            Assert.Less(AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Weapons/HolyRevolver/"+part+".mat").GetColor("_EmissionColor").maxColorComponent,.001f,"Runtime glow must not modify shared material assets.");
        GameSettings.ForceCursorLocked=false;
        network.Shutdown();
        yield return null;
        yield return new ExitPlayMode();
    }
}
