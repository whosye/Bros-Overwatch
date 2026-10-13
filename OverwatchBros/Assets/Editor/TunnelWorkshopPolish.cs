using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class TunnelWorkshopPolish
{
    const string FontPath = "Assets/Fonts/Workshop/Rajdhani-SemiBold SDF.asset";

    public static TMP_FontAsset LoadFont()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (asset) return asset;
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Workshop/Rajdhani-SemiBold.ttf");
        if (!source) throw new FileNotFoundException("Workshop Rajdhani font has not been imported.");
        asset = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
        asset.name = "Rajdhani-SemiBold SDF";
        var characters = new StringBuilder();
        for (int i = 32; i < 127; i++) characters.Append((char)i);
        if (!asset.TryAddCharacters(characters.ToString(), out string missing))
            throw new System.InvalidOperationException("Missing workshop font characters: " + missing);
        asset.atlasPopulationMode = AtlasPopulationMode.Static;
        AssetDatabase.CreateAsset(asset, FontPath);
        asset.material.name = asset.name + " Material";
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        foreach (var atlas in asset.atlasTextures)
        {
            atlas.name = asset.name + " Atlas";
            AssetDatabase.AddObjectToAsset(atlas, asset);
            EditorUtility.SetDirty(atlas);
        }
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        return asset;
    }

    public static void StyleLabel(TMP_Text label, TMP_FontAsset font)
    {
        Vector2 bounds;
        float maximum;
        switch (label.name)
        {
            case "PAVOUK": bounds = new Vector2(0.74f, 0.13f); maximum = 1.05f; break;
            case "Nerusit": bounds = new Vector2(0.95f, 0.09f); maximum = 0.75f; break;
            case "Modeluji":
                label.text = "msi";
                label.fontStyle = FontStyles.Italic;
                label.characterSpacing = 10;
                label.color = new Color(0.65f, 0.69f, 0.7f);
                bounds = new Vector2(0.20f, 0.036f); maximum = 0.43f; break;
            case "Tempo": bounds = new Vector2(1.15f, 0.06f); maximum = 0.64f; break;
            case "PC_nalepka": bounds = new Vector2(0.36f, 0.05f); maximum = 0.60f; break;
            case "PVK_na_zadech": bounds = new Vector2(0.26f, 0.26f); maximum = 1.15f; break;
            case "Nazev_na_zdi":
                label.characterSpacing = 16;
                bounds = new Vector2(1.85f, 0.30f); maximum = 2.5f; break;
            case "Flow_state":
                bounds = new Vector2(1.85f, 0.11f); maximum = 0.92f;
                label.color = new Color(0.62f, 0.83f, 0.71f); break;
            case "Plan_titulek": bounds = new Vector2(1.10f, 0.10f); maximum = 0.94f; break;
            case "Rack_popis": bounds = new Vector2(0.57f, 0.09f); maximum = 0.68f; break;
            default: return;
        }
        label.font = font;
        label.fontSharedMaterial = font.material;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = bounds;
        label.margin = Vector4.zero;
        label.enableAutoSizing = true;
        label.fontSizeMin = 0.1f;
        label.fontSizeMax = maximum;
        label.fontSize = maximum;
        label.overflowMode = TextOverflowModes.Truncate;
        label.ForceMeshUpdate();
    }

    public static Vector3 RackPosition(float width, float depth) => new Vector3(width / 2 - 0.93f, 0.85f, depth - 0.73f);

    /// <summary>Updates just the requested details, preserving manually edited lighting and geometry.</summary>
    [MenuItem("BrosOverwatch/Easter egg/Upravit stitky a servery")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var room = Object.FindAnyObjectByType<TunnelWorkshop>();
        if (!room) return;
        Directory.CreateDirectory(TunnelWorkshopVerification.Output);
        string backup = TunnelWorkshopVerification.Output + "/BeforePolish.unity";
        if (!File.Exists(backup)) EditorSceneManager.SaveScene(room.gameObject.scene, backup, true);
        var font = LoadFont();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Upravit stitky a servery");
        Undo.RegisterFullObjectHierarchyUndo(room.gameObject, "Upravit stitky a servery");
        foreach (var label in room.GetComponentsInChildren<TMP_Text>(true)) StyleLabel(label, font);
        var details = room.transform.Find("Detaily");
        var spider = details.Find("Pavouci_podpis");
        spider.localPosition = new Vector3(0, 2.68f, room.roomSize.z - 0.20f);
        spider.localScale = Vector3.one * 0.70f;
        details.Find("Nazev_na_zdi").localPosition = new Vector3(0, 2.28f, room.roomSize.z - 0.22f);
        details.Find("Flow_state").localPosition = new Vector3(0, 2.06f, room.roomSize.z - 0.22f);
        var rack = details.Find("Rack");
        rack.localPosition = RackPosition(room.roomSize.x, room.roomSize.z);
        rack.localRotation = Quaternion.Euler(0, 22, 0);
        if (!rack.GetComponent<WorkshopServerActivity>()) Undo.AddComponent<WorkshopServerActivity>(rack.gameObject);
        // Reconnect the existing power lead to the relocated rack.
        Vector3 start = rack.localPosition + new Vector3(0.1f, -0.76f, 0.18f);
        Vector3 bend = new Vector3(room.roomSize.x / 2 - 0.5f, 0.09f, 3.0f);
        var cable = details.Find("Napajeni_1");
        if (cable)
        {
            cable.localPosition = (start + bend) * 0.5f;
            cable.localRotation = Quaternion.FromToRotation(Vector3.up, bend - start);
            cable.localScale = new Vector3(0.034f, (bend - start).magnitude * 0.5f, 0.034f);
        }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(room.gameObject.scene);
        SceneView.RepaintAll();
        Debug.Log("[Pavouk] Typografie, vyhled a serverove kontrolky upraveny.");
    }
}
