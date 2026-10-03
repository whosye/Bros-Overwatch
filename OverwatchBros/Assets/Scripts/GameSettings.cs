using UnityEngine;

// Jednoduche uzivatelske nastaveni ulozene v PlayerPrefs (per pocitac).
public static class GameSettings
{
    // Pouze pro automaticke testy (v headless rezimu nejde zamknout kurzor).
    public static bool ForceCursorLocked;

    public static bool CursorLocked => ForceCursorLocked || Cursor.lockState == CursorLockMode.Locked;

    const string SensitivityKey = "settings.sensitivity";
    const string VolumeKey = "settings.volume";

    public const float MinSensitivity = 0.2f;
    public const float MaxSensitivity = 20f;

    // Krok tlacitek +/-: jemny u nizkych hodnot, hrubsi u vysokych (at se k 10 nemusi klikat stokrat).
    public static float SensitivityStep(float value, bool up)
    {
        float edge = up ? value + 0.001f : value - 0.001f;
        return edge < 2f ? 0.1f : edge < 5f ? 0.25f : edge < 10f ? 0.5f : 1f;
    }

    public static float Sensitivity
    {
        get => PlayerPrefs.GetFloat(SensitivityKey, 1f);
        set => PlayerPrefs.SetFloat(SensitivityKey, Mathf.Clamp(value, MinSensitivity, MaxSensitivity));
    }

    public static float Volume
    {
        get => PlayerPrefs.GetFloat(VolumeKey, 1f);
        set
        {
            PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
            ApplyVolume();
        }
    }

    public static void ApplyVolume()
    {
        AudioListener.volume = Volume;
    }
}
