using UnityEngine;

// Zarive efekty Povova Trojseku a Drtiveho skoku (aditivni shader BrosOverwatch/Additive Glow):
//   - stopa cepele (srpek / oblouk), ktera se behem par setin "vykresli" od zacatku ke konci jako sek a pak dohasne,
//   - zhava puklina v zemi (kopiruje teren),
//   - razova vlna (kruh, ktery se rozbehne do stran a ztenci).
// Vse bez textur - barvy jsou ve vrcholech (zhavy stred, okraje do ztracena).
public class SlashGlow : MonoBehaviour
{
    public static readonly Color Hot = new Color(1f, 0.82f, 0.45f, 1f);
    public static readonly Color Ember = new Color(1f, 0.32f, 0.06f, 0.8f);
    static readonly Color Clear = new Color(1f, 0.2f, 0.02f, 0f);

    static Material material;
    static Material GlowMaterial
    {
        get
        {
            if (material == null)
            {
                var shader = Shader.Find("BrosOverwatch/Additive Glow");
                material = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            }
            return material;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => material = null;

    Mesh mesh;
    Color[] baseColors, colors;
    float[] along;
    float reveal, life, age;

    // razova vlna
    bool ring;
    Vector3[] ringVertices;
    int ringSegments;
    float ringFrom, ringTo, ringWidth;

    // ---------------- zem ----------------

    // Bod na zemi pod 'p' (hraci a boti se preskoci), kousek nad povrchem. Kdyz zem neni, vrati 'p'.
    public static Vector3 Ground(Vector3 p, float lift = 0.06f)
    {
        Vector3 from = p + Vector3.up * 2f;
        float length = 5f;
        for (int i = 0; i < 3; i++)
        {
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, length, ~0, QueryTriggerInteraction.Ignore)) return p + Vector3.up * lift;
            if (hit.collider is CharacterController || hit.collider.attachedRigidbody != null)
            {
                length -= hit.distance + 0.05f;
                from = hit.point + Vector3.down * 0.05f;
                if (length <= 0f) break;
                continue;
            }
            return hit.point + Vector3.up * lift;
        }
        return p + Vector3.up * lift;
    }

    // ---------------- stavebni kameny ----------------

    // Pas podel bodu: kazdy radek = levy okraj (pruhledny), stred (zhavy), pravy okraj (pruhledny).
    public static void Ribbon(Vector3[] points, Vector3[] sides, float[] halfWidths, Color core, Color rim, float reveal, float life, float delay = 0f)
    {
        int n = points.Length;
        if (n < 2) return;
        Vector3 origin = points[0];
        var vertices = new Vector3[n * 3];
        var cols = new Color[n * 3];
        var along = new float[n * 3];
        var triangles = new int[(n - 1) * 12];
        for (int i = 0; i < n; i++)
        {
            float s = i / (float)(n - 1);
            Vector3 p = points[i] - origin;
            vertices[i * 3] = p - sides[i] * halfWidths[i];
            vertices[i * 3 + 1] = p;
            vertices[i * 3 + 2] = p + sides[i] * halfWidths[i];
            cols[i * 3] = rim;
            cols[i * 3 + 1] = core;
            cols[i * 3 + 2] = rim;
            along[i * 3] = along[i * 3 + 1] = along[i * 3 + 2] = s;
        }
        for (int i = 0; i < n - 1; i++)
        {
            int a = i * 3, b = (i + 1) * 3, t = i * 12;
            triangles[t] = a; triangles[t + 1] = b; triangles[t + 2] = a + 1;
            triangles[t + 3] = a + 1; triangles[t + 4] = b; triangles[t + 5] = b + 1;
            triangles[t + 6] = a + 1; triangles[t + 7] = b + 1; triangles[t + 8] = a + 2;
            triangles[t + 9] = a + 2; triangles[t + 10] = b + 1; triangles[t + 11] = b + 2;
        }
        var glow = Create("FX_Slash", origin, vertices, cols, triangles);
        glow.along = along;
        glow.reveal = reveal;
        glow.life = life;
        glow.age = -Mathf.Max(0f, delay);   // zpozdeni: do te doby neviditelne
        glow.Apply(glow.age);
    }

    // Oblouk cepele: stred 'pivot', rovina (axisA, axisB), uhly od-do (stupne, 0 = axisA). Pas je siroky do stran
    // od stredu (srpek), uprostred nejsirsi; 'lift' zvedne/snizi zacatek a konec (sikmy sek).
    public static void Arc(Vector3 pivot, Vector3 axisA, Vector3 axisB, float radius, float fromDeg, float toDeg,
        float halfWidth, Color core, Color rim, float reveal, float life, float liftStart = 0f, float liftEnd = 0f, float delay = 0f)
    {
        const int n = 18;
        var points = new Vector3[n];
        var sides = new Vector3[n];
        var widths = new float[n];
        for (int i = 0; i < n; i++)
        {
            float s = i / (float)(n - 1);
            float a = Mathf.Lerp(fromDeg, toDeg, s) * Mathf.Deg2Rad;
            Vector3 radial = axisA * Mathf.Cos(a) + axisB * Mathf.Sin(a);
            points[i] = pivot + radial * radius + Vector3.up * Mathf.Lerp(liftStart, liftEnd, s);
            sides[i] = radial;
            // srpek: tenky zacatek, nejsirsi v druhe tretine, ostra spicka
            // (sin(PI) vyjde nepatrne zaporny a Pow ze zaporneho cisla = NaN -> Max 0)
            widths[i] = halfWidth * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(s, 0.8f))), 0.75f) + 0.01f;
        }
        Ribbon(points, sides, widths, core, rim, reveal, life, delay);
    }

    // Zhava puklina v zemi: kratke klikate useky, kopiruje teren, od spicky k ustim se rozsiruje.
    public static void Crack(Vector3 start, Vector3 direction, float length, float halfWidth, float reveal, float life, int seed)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;
        direction.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        var random = new System.Random(seed);
        const int n = 12;
        var points = new Vector3[n];
        var sides = new Vector3[n];
        var widths = new float[n];
        float drift = 0f;
        for (int i = 0; i < n; i++)
        {
            float s = i / (float)(n - 1);
            if (i > 0) drift += ((float)random.NextDouble() - 0.5f) * 0.28f;
            points[i] = Ground(start + direction * (length * s) + side * drift, 0.05f);
            sides[i] = side;
            widths[i] = halfWidth * Mathf.Lerp(1f, 0.25f, s) * Mathf.Clamp01(s * 5f + 0.4f);
        }
        Ribbon(points, sides, widths, Hot, Clear, reveal, life);
        Ribbon(points, sides, System.Array.ConvertAll(widths, w => w * 3f), new Color(1f, 0.3f, 0.05f, 0.35f), Clear, reveal, life * 0.8f);
    }

    // Razova vlna po zemi: kruh se rozbehne z 'startRadius' do 'radius', ztenci a zhasne.
    public static void Shockwave(Vector3 center, float radius, float life, float startRadius = 0.3f, float width = 0.55f)
    {
        const int segments = 56;
        var vertices = new Vector3[(segments + 1) * 3];
        var cols = new Color[vertices.Length];
        var triangles = new int[segments * 12];
        for (int i = 0; i <= segments; i++)
        {
            cols[i * 3] = Clear;
            cols[i * 3 + 1] = Hot;
            cols[i * 3 + 2] = new Color(1f, 0.45f, 0.1f, 0f);
        }
        for (int i = 0; i < segments; i++)
        {
            int a = i * 3, b = (i + 1) * 3, t = i * 12;
            triangles[t] = a; triangles[t + 1] = b; triangles[t + 2] = a + 1;
            triangles[t + 3] = a + 1; triangles[t + 4] = b; triangles[t + 5] = b + 1;
            triangles[t + 6] = a + 1; triangles[t + 7] = b + 1; triangles[t + 8] = a + 2;
            triangles[t + 9] = a + 2; triangles[t + 10] = b + 1; triangles[t + 11] = b + 2;
        }
        var glow = Create("FX_Shockwave", Ground(center, 0.08f), vertices, cols, triangles);
        glow.ring = true;
        glow.ringVertices = vertices;
        glow.ringSegments = segments;
        glow.ringFrom = startRadius;
        glow.ringTo = radius;
        glow.ringWidth = width;
        glow.life = life;
        glow.UpdateRing(0f);
    }

    static SlashGlow Create(string name, Vector3 position, Vector3[] vertices, Color[] cols, int[] triangles)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        var mesh = new Mesh { name = name };
        mesh.MarkDynamic();
        mesh.vertices = vertices;
        mesh.colors = cols;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = GlowMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var glow = go.AddComponent<SlashGlow>();
        glow.mesh = mesh;
        glow.baseColors = cols;
        glow.colors = new Color[cols.Length];
        glow.along = new float[cols.Length];
        glow.life = 0.3f;
        glow.Apply(0f);
        return glow;
    }

    // ---------------- animace ----------------

    void Update()
    {
        age += Time.deltaTime;
        if (age >= life)
        {
            Destroy(mesh);
            Destroy(gameObject);
            return;
        }
        if (ring && age >= 0f) UpdateRing(age / life);
        Apply(age);
    }

    void Apply(float time)
    {
        if (time < 0f)
        {
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.clear;
            mesh.colors = colors;
            return;
        }
        float head = reveal > 0f ? Mathf.Clamp01(time / reveal) : 1f;
        float fade;
        if (ring) fade = Mathf.Pow(Mathf.Max(0f, 1f - time / life), 1.4f);
        else if (time <= reveal) fade = 1f;
        else { fade = 1f - (time - reveal) / Mathf.Max(0.01f, life - reveal); fade *= fade; }
        for (int i = 0; i < colors.Length; i++)
        {
            // mekka hlava stopy: co je za ni, sviti; kousek pred ni se teprve rozsveci
            float shown = reveal > 0f ? Mathf.Clamp01((head - along[i]) * 10f + 1f) : 1f;
            var c = baseColors[i];
            c.a *= shown * fade;
            colors[i] = c;
        }
        mesh.colors = colors;
    }

    void UpdateRing(float k)
    {
        float ease = 1f - Mathf.Pow(1f - Mathf.Clamp01(k), 3f);
        float r = Mathf.Lerp(ringFrom, ringTo, ease);
        float w = ringWidth * Mathf.Lerp(1f, 0.3f, k);
        for (int i = 0; i <= ringSegments; i++)
        {
            float a = i / (float)ringSegments * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            ringVertices[i * 3] = dir * Mathf.Max(0f, r - w);
            ringVertices[i * 3 + 1] = dir * r;
            ringVertices[i * 3 + 2] = dir * (r + w * 0.35f);   // ostre celo vlny, za ni dlouhy dozvuk
        }
        mesh.vertices = ringVertices;
        mesh.RecalculateBounds();
    }
}
