using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Render a neutral Sapa surface with lighting independently toggled. This catches
// missing Forward+ variants even when a shader compiles and textures look correct.
public static class SapaLightingValidation
{
    public static void Check(Light sun)
    {
        var root = new GameObject("SapaLightingValidation");
        root.transform.position = new Vector3(-500,300,-500);
        var material = new Material(Shader.Find("BrosOverwatch/Sapa"));
        material.SetFloat("_Style",0);
        material.SetVector("_OW_ColorValue",new Vector4(.5f,.5f,.5f,1));
        material.SetVector("_OW_NormalValue",new Vector4(.5f,.5f,0,1));
        material.SetVector("_OW_PBRValue",Vector4.zero);
        material.SetVector("_OW_EmissionValue",Vector4.zero);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.SetParent(root.transform,false);
        ground.GetComponent<Renderer>().sharedMaterial = material;
        ground.layer = 31;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(root.transform,false);
        cube.transform.localPosition = Vector3.up;
        cube.transform.localScale = new Vector3(1.8f,2,1.8f);
        cube.GetComponent<Renderer>().sharedMaterial = material;
        cube.layer = 31;
        var camera = new GameObject("LightingCamera").AddComponent<Camera>();
        camera.transform.SetParent(root.transform,false);
        camera.transform.localPosition = new Vector3(0,7,-8);
        camera.transform.LookAt(root.transform.position);
        camera.orthographic = true;
        camera.orthographicSize = 5.5f;
        camera.farClipPlane = 40;
        camera.cullingMask = 1 << 31;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        bool enabled = sun.enabled;
        var shadows = sun.shadows;
        var previousSun = RenderSettings.sun;
        try
        {
            RenderSettings.sun = sun;
            sun.enabled = true;
            sun.shadows = LightShadows.Soft;
            var lit = Render(camera,"lighting-sun");
            sun.shadows = LightShadows.None;
            var unshadowed = Render(camera,"lighting-no-shadows");
            sun.enabled = false;
            var dark = Render(camera,"lighting-no-sun");
            var point = new GameObject("Point").AddComponent<Light>();
            point.transform.SetParent(root.transform,false);
            point.transform.localPosition = new Vector3(2,3,-2);
            point.type = LightType.Point;
            point.range = 10;
            point.intensity = 8;
            var pointLit = Render(camera,"lighting-point");
            int sunlight = Brighter(unshadowed,dark,.04f);
            int shadow = Brighter(unshadowed,lit,.025f);
            int localLight = Brighter(pointLit,dark,.025f);
            string report = $"{{\"pixels\":{lit.Length},\"sunlitPixels\":{sunlight},\"shadowPixels\":{shadow},\"pointLitPixels\":{localLight}}}";
            File.WriteAllText(Path.Combine(SapaSetup.WorkFolder,"lighting-validation.json"),report);
            Debug.Log("[Sapa lighting] "+report);
            SapaSetup.Require(sunlight>lit.Length*.1f,"Sapa material nereaguje na slunce.");
            SapaSetup.Require(shadow>lit.Length*.005f,"Sapa material neprijima vrzene stiny.");
            SapaSetup.Require(localLight>lit.Length*.02f,"Sapa material nereaguje na bodove svetlo.");
        }
        finally
        {
            sun.enabled = enabled;
            sun.shadows = shadows;
            RenderSettings.sun = previousSun;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(material);
        }
    }

    static Color[] Render(Camera camera,string name)
    {
        string path = Path.Combine(SapaSetup.WorkFolder,name+".png");
        SapaSetup.CaptureCamera(camera,path,384,384);
        var texture = new Texture2D(2,2);
        texture.LoadImage(File.ReadAllBytes(path));
        var pixels = texture.GetPixels();
        Object.DestroyImmediate(texture);
        return pixels;
    }

    static int Brighter(Color[] a,Color[] b,float threshold)
    {
        int count = 0;
        for(int i=0;i<a.Length;i++) if(a[i].grayscale-b[i].grayscale>threshold) count++;
        return count;
    }
}
