using UnityEditor;
using UnityEngine;

// Vytvori prefab sekyry v ruce z importovaneho modelu (Assets/Models/StylizedAxe) a priradi ho Ayranovym sekyram.
// Model se sam otoci (topurko nahoru, ostri dopredu), zmeni velikost a vycentruje, takze nezalezi na osach exportu.
public static class HeldAxeSetup
{
    const string ModelPath = "Assets/Models/StylizedAxe/Stylizedaxe.fbx";
    const string TexturePath = "Assets/Models/StylizedAxe/AxeTexture.jpeg";
    const string MaterialPath = "Assets/Models/StylizedAxe/Axe.mat";
    const string PrefabPath = "Assets/Prefab/HeldAxe.prefab";
    const string WeaponPath = "Assets/Data/Ayran_Weapon.asset";
    const float AxeLength = 0.62f;

    public static bool IsReady()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
            return !System.IO.File.Exists(ModelPath);

        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        return weapon != null && weapon.heldPrefab != null;
    }

    public static void Setup()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (model == null || texture == null) return;

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;

            material = new Material(shader);
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            prefab = BuildPrefab(model, material);
            if (prefab == null) return;
        }

        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
        if (weapon != null && weapon.heldPrefab == null)
        {
            weapon.heldPrefab = prefab;
            EditorUtility.SetDirty(weapon);
        }
    }

    static GameObject BuildPrefab(GameObject model, Material material)
    {
        var root = new GameObject("HeldAxe");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        try
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }

            var points = new System.Collections.Generic.List<Vector3>();
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                foreach (var vertex in filter.sharedMesh.vertices)
                    points.Add(root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex)));
            }

            if (points.Count < 10)
            {
                Debug.LogWarning("[HeldAxeSetup] Model sekyry nema mesh.");
                return null;
            }

            Orient(points, out Vector3 up, out Vector3 blade, out float length, out Vector3 bottomCenter);

            // Model se otoci tak, aby up -> +Y a blade -> +Z, zmensi na AxeLength a spodek topurka bude v pocatku.
            var rotation = Quaternion.Inverse(Quaternion.LookRotation(blade, up));
            float scale = AxeLength / length;
            instance.transform.localScale = Vector3.one * scale;
            instance.transform.localRotation = rotation;
            instance.transform.localPosition = -(rotation * bottomCenter) * scale + Vector3.down * 0.02f;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[HeldAxeSetup] Prefab sekyry vytvoren (delka {length:0.###} -> {AxeLength} m, up={up}, blade={blade}).");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // Najde osu topurka (nejdelsi rozmer), konec s hlavou (sirsi konec) a smer ostri (ta strana hlavy, ktera vic vyci od topurka).
    static void Orient(System.Collections.Generic.List<Vector3> points, out Vector3 up, out Vector3 blade, out float length, out Vector3 bottomCenter)
    {
        var min = points[0];
        var max = points[0];
        foreach (var p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        Vector3 size = max - min;
        int upAxis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
        int otherA = (upAxis + 1) % 3;
        int otherB = (upAxis + 2) % 3;
        length = size[upAxis];

        float low = min[upAxis] + length * 0.3f;
        float high = max[upAxis] - length * 0.3f;

        Vector3 lowMin = max, lowMax = min, highMin = max, highMax = min;
        foreach (var p in points)
        {
            if (p[upAxis] <= low) { lowMin = Vector3.Min(lowMin, p); lowMax = Vector3.Max(lowMax, p); }
            if (p[upAxis] >= high) { highMin = Vector3.Min(highMin, p); highMax = Vector3.Max(highMax, p); }
        }

        float lowWidth = (lowMax[otherA] - lowMin[otherA]) + (lowMax[otherB] - lowMin[otherB]);
        float highWidth = (highMax[otherA] - highMin[otherA]) + (highMax[otherB] - highMin[otherB]);
        bool headAtMax = highWidth >= lowWidth;

        Vector3 handleMin = headAtMax ? lowMin : highMin;
        Vector3 handleMax = headAtMax ? lowMax : highMax;
        Vector3 headMin = headAtMax ? highMin : lowMin;
        Vector3 headMax = headAtMax ? highMax : lowMax;

        // Ostri je v tom z obou zbylych rozmeru, kde je hlava sirsi; strana je ta, ktera vic vyci od stredu topurka.
        float extentA = headMax[otherA] - headMin[otherA];
        float extentB = headMax[otherB] - headMin[otherB];
        int bladeAxis = extentA >= extentB ? otherA : otherB;
        int thicknessAxis = bladeAxis == otherA ? otherB : otherA;

        float handleCenter = (handleMin[bladeAxis] + handleMax[bladeAxis]) * 0.5f;
        float positive = headMax[bladeAxis] - handleCenter;
        float negative = handleCenter - headMin[bladeAxis];

        up = Vector3.zero;
        up[upAxis] = headAtMax ? 1f : -1f;
        blade = Vector3.zero;
        blade[bladeAxis] = positive >= negative ? 1f : -1f;

        bottomCenter = Vector3.zero;
        bottomCenter[upAxis] = headAtMax ? min[upAxis] : max[upAxis];
        bottomCenter[bladeAxis] = handleCenter;
        bottomCenter[thicknessAxis] = (handleMin[thicknessAxis] + handleMax[thicknessAxis]) * 0.5f;
    }
}
