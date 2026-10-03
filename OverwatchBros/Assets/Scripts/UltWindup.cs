using UnityEngine;

// Pripravna faze ultimatky (Viktor, Honza, Mirek): po stisku Q chvili trva, nez se ultimatka spusti.
// Hrac se behem ni muze hybat, ale nestrili a nepouziva jine schopnosti; ruce se zbrani se lehce zvednou.
// Kdyz ho behem pripravy nekdo zabije nebo omraci, ultimatka se nespusti a nabiti mu zustane.
public class UltWindup
{
    float start;
    float duration;

    public bool Active { get; private set; }
    public float Progress => Active ? Mathf.Clamp01((Time.time - start) / Mathf.Max(0.01f, duration)) : 0f;

    public void Begin(FirstPersonController fpc, float seconds)
    {
        Active = true;
        start = Time.time;
        duration = seconds;
        fpc.UltCasting = true;
        fpc.UltWindup = 0f;
    }

    // Vola se kazdy snimek. Vraci true v okamziku, kdy priprava dobehla (ultimatka se ma spustit).
    public bool Tick(FirstPersonController fpc)
    {
        if (!Active) return false;

        if (fpc.CannotAct)
        {
            Cancel(fpc);
            return false;
        }

        fpc.UltWindup = Progress;
        if (Progress < 1f) return false;

        Cancel(fpc);
        return true;
    }

    public void Cancel(FirstPersonController fpc)
    {
        Active = false;
        fpc.UltCasting = false;
        fpc.UltWindup = 0f;
    }
}
