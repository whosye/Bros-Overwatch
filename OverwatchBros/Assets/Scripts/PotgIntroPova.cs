using UnityEngine;

// Povova scenka - hak (zbytek scenky v PotgIntro.cs): Pova stoji opodal, hodi levou rukou hak na retezu, zasekne ho
// Tomaskovi (Honzovi) do hrudi a strhne si ho z bedny k sobe - Tomasek leti vzduchem primo do rany sekerou (odpal jako
// v baseballu, zpomaleni). Pak detail Povova obliceje.
public partial class PotgIntro
{
    const float HookThrow = 1.55f, HookHit = 1.78f, YankStart = 1.88f, YankEnd = 2.38f;
    const float PovaImpactReach = 0.9f;          // Tomasek pri uderu tak daleko pred Povou (m)

    Transform povaHook;
    LineRenderer povaChain;
    Vector3 launchFrom;                          // odkud Tomasek po uderu odleti

    // kde Tomasek dostane ranu: pred Povou, kousek nad zemi
    Vector3 PovaImpact => Stage + PovaTo + Flat(TomasekSeat - PovaTo).normalized * PovaImpactReach + Vector3.up * 0.25f;

    // Pri stavbe: hak s retezem; Tomasek bez piva.
    void BuildPovaExtras()
    {
        if (beer != null) beer.gameObject.SetActive(false);
        povaHook = new GameObject("Hak").transform;
        povaHook.SetParent(root.transform, false);
        var iron = new Color(0.22f, 0.22f, 0.25f);
        Prim(PrimitiveType.Sphere, povaHook, Vector3.zero, Vector3.one * 0.07f, iron);
        var shank = Prim(PrimitiveType.Cube, povaHook, Vector3.zero, new Vector3(0.025f, 0.025f, 0.16f), iron).transform;
        shank.localPosition = new Vector3(0f, 0f, 0.06f);
        foreach (float side in new[] { -1f, 1f })   // dva zahnute hroty
        {
            var barb = Prim(PrimitiveType.Cube, povaHook, Vector3.zero, new Vector3(0.02f, 0.02f, 0.09f), new Color(0.6f, 0.62f, 0.66f)).transform;
            barb.localPosition = new Vector3(side * 0.035f, 0f, 0.1f);
            barb.localRotation = Quaternion.Euler(0f, -side * 140f, 0f);
        }
        povaHook.gameObject.SetActive(false);

        var chain = new GameObject("Retez");
        chain.transform.SetParent(root.transform, false);
        povaChain = chain.AddComponent<LineRenderer>();
        povaChain.material = Fx.NewLit(new Color(0.3f, 0.3f, 0.33f));
        povaChain.widthMultiplier = 0.022f;
        povaChain.positionCount = 2;
        povaChain.enabled = false;
    }

    // Hak: hod levou rukou, zaseknuti do hrudi, pritazeni; pri uderu sekerou zmizi.
    void UpdatePovaHook(float t)
    {
        if (Once(20, t, HookThrow - 0.07f))
        {
            held.PlayThrow();
            ProceduralSfx.Play(ProceduralSfx.Dash, hero.position + Vector3.up, 0.8f);
        }
        if (povaHook != null && povaChain != null)
        {
            bool on = t >= HookThrow && t < ImpactAt;
            if (povaHook.gameObject.activeSelf != on) povaHook.gameObject.SetActive(on);
            povaChain.enabled = on;
            if (on)
            {
                var hand = visual.GetHand(false);
                Vector3 from = hand != null ? hand.position : hero.position + Vector3.up * 1.3f;
                Vector3 chest = tomasekHead != null ? tomasekHead.position + Vector3.down * 0.42f : seat + Vector3.up * 0.8f;
                float fly = Mathf.Clamp01((t - HookThrow) / (HookHit - HookThrow));
                Vector3 at = t < HookHit ? Vector3.Lerp(from, chest, EaseOutCubic(fly)) + Vector3.up * Mathf.Sin(fly * Mathf.PI) * 0.25f : chest;
                if ((at - from).sqrMagnitude > 1e-4f) povaHook.rotation = Quaternion.LookRotation(at - from);
                povaHook.position = at;
                povaChain.SetPosition(0, from);
                povaChain.SetPosition(1, at);
            }
        }
        if (Once(21, t, HookHit))
        {
            Vector3 chest = tomasekHead != null ? tomasekHead.position + Vector3.down * 0.42f : seat + Vector3.up * 0.8f;
            Fx.Sparks(chest, new Color(0.8f, 0.8f, 0.85f));
            ProceduralSfx.Play(ProceduralSfx.Hit, chest, 0.9f);
            ProceduralSfx.Play(ProceduralSfx.BulletImpact, chest, 0.7f);
        }
        if (Once(22, t, YankStart)) ProceduralSfx.Play(ProceduralSfx.Dash, hero.position + Vector3.up, 1f);   // trhnuti retezem
    }

    // Tomasek pritazeny hakem: z bedny obloukem pred Povu, celem k nemu (pred uderem).
    bool PoseYankedTomasek(float t)
    {
        if (!pova || t < YankStart || tomasek == null || tomasek.go == null) return false;
        float y = Mathf.Clamp01((t - YankStart) / (YankEnd - YankStart));
        y *= y;   // trhnuti zrychluje
        tomasek.Pose(clips.hitChest != null ? clips.hitChest : clips.sitIdle, (t - YankStart) * 1.2f);
        Vector3 p = Vector3.Lerp(seat, PovaImpact, y) + Vector3.up * Mathf.Sin(y * Mathf.PI) * 0.5f;
        tomasek.go.transform.position = p;
        Quaternion toPova = Quaternion.LookRotation(Flat(Stage + PovaTo - p)) * Quaternion.Euler(-25f, 0f, 0f);
        tomasek.go.transform.rotation = Quaternion.Slerp(seatRotation, toPova, Mathf.Clamp01(y * 1.5f));
        return true;
    }
}
