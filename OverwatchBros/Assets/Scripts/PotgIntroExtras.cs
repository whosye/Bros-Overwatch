using UnityEngine;

// Doplnky POTG scenek:
//  - Zpomaleni ve vrcholu scenky (uder, vybuch, zasah - SceneClimax): cas scenky i hry (animace postav, castice)
//    zpomali asi na ctvrtinu, zazni hluboky uder a znelka se ztlumi (PotgUI). O ztraceny cas se uvod prodlouzi
//    (PotgUI jede podle hodin scenky), takze se konec scenky neutne.
public partial class PotgIntro
{
    const float SlowMin = 0.28f;                  // rychlost casu ve zpomaleni
    const float SlowLead = 0.06f;                 // nejpomaleji uz kousek pred vrcholem
    const float SlowHold = 0.15f;                 // jak dlouho (cas scenky) drzi nejpomaleji
    const float SlowIn = 0.04f, SlowOut = 0.1f;   // nabeh a dojezd

    float stepDt;          // krok casu scenky v tomto snimku (ve zpomaleni mensi)
    float climax = -1f;    // vrchol scenky (cas scenky), -1 = bez zpomaleni
    float slowAmount;      // 0..1 jak moc je zrovna zpomaleno
    bool slowHit, timeScaled;

    // Cas scenky (PotgUI podle nej casuje kartu a konec uvodu), -1 = scenka nebezi.
    public static float Clock => instance != null && instance.running ? instance.time : -1f;
    // 0..1 jak moc je scenka zrovna zpomalena (PotgUI ztlumi znelku).
    public static float SlowMo => instance != null && instance.running ? instance.slowAmount : 0f;
    // Delka uvodu (s): vetsinou 5, nektere scenky delsi (Sindeluv souboj).
    public static float Length => instance != null && instance.running ? instance.sceneLength : 5f;

    float SlowRate(float t)
    {
        if (climax < 0f) return 1f;
        float start = climax - SlowLead, end = start + SlowHold;
        if (t < start - SlowIn || t > end + SlowOut) return 1f;
        float k = t < start ? Smooth(Mathf.Clamp01((t - (start - SlowIn)) / SlowIn))
            : t <= end ? 1f : 1f - Smooth(Mathf.Clamp01((t - end) / SlowOut));
        return Mathf.Lerp(1f, SlowMin, k);
    }

    // Krok casu scenky; ve zpomaleni pomaleji bezi i cas hry (animace postav, castice), pak zase normalne.
    void StepClock()
    {
        float rate = SlowRate(time);
        slowAmount = (1f - rate) / (1f - SlowMin);
        if (rate < 0.999f)
        {
            Time.timeScale = rate;
            timeScaled = true;
        }
        else RestoreTimeScale();

        if (!slowHit && climax >= 0f && time >= climax - SlowLead - SlowIn)
        {
            slowHit = true;
            Fx.PlayGlobal(ProceduralSfx.PotgSlowHit, 0.75f);
        }
        stepDt = Time.unscaledDeltaTime * rate;
        time += stepDt;
    }

    void RestoreTimeScale()
    {
        if (!timeScaled) return;
        timeScaled = false;
        Time.timeScale = 1f;
    }
}
