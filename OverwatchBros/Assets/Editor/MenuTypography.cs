using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// Keep the menu typography local; the combat HUD retains its existing fonts.
[InitializeOnLoad]
public static class MenuTypography
{
    static MenuTypography() { EditorApplication.delayCall += EnsureFonts; }
    public static void EnsureFonts()
    {
        Create("Assets/Fonts/Barlow/Barlow-Medium.ttf", "Barlow Medium");
        Create("Assets/Fonts/BarlowCondensed/BarlowCondensed-SemiBold.ttf", "Barlow Condensed SemiBold");
    }
    static void Create(string source, string name)
    {
        string path="Assets/Resources/UI/Fonts/"+name+" SDF.asset";
        if(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path)!=null)return;
        var sourceFont=AssetDatabase.LoadAssetAtPath<Font>(source);
        if(sourceFont==null)return;
        var asset=TMP_FontAsset.CreateFontAsset(sourceFont,90,9,GlyphRenderMode.SDFAA,1024,1024,AtlasPopulationMode.Dynamic,true);
        asset.name=name+" SDF";
        asset.fallbackFontAssetTable=new System.Collections.Generic.List<TMP_FontAsset>();
        if(TMP_Settings.defaultFontAsset!=null)asset.fallbackFontAssetTable.Add(TMP_Settings.defaultFontAsset);
        string chars="";
        for(int i=32;i<127;i++)chars+=(char)i;
        chars+="áčďéěíňóřšťúůýžÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ–—…‹›−→·";
        asset.TryAddCharacters(chars,out _);
        AssetDatabase.CreateAsset(asset,path);
        asset.material.name=name+" Material";
        AssetDatabase.AddObjectToAsset(asset.material,asset);
        foreach(var atlas in asset.atlasTextures)
        {
            atlas.name=name+" Atlas";
            AssetDatabase.AddObjectToAsset(atlas,asset);
        }
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
    }
}
