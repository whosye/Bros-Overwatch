using UnityEngine;

// Jednoduche uzivatelske nastaveni ulozene v PlayerPrefs (per pocitac).
public static class GameSettings
{
    // Pouze pro automaticke testy (v headless rezimu nejde zamknout kurzor).
    public static bool ForceCursorLocked;

    public static bool CursorLocked => ForceCursorLocked || Cursor.lockState == CursorLockMode.Locked;

    const string SensitivityKey = "settings.sensitivity";
    const string VolumeKey = "settings.volume";

    public static float Sensitivity
    {
        get => PlayerPrefs.GetFloat(SensitivityKey, 1f);
        set => PlayerPrefs.SetFloat(SensitivityKey, Mathf.Clamp(value, 0.2f, 3f));
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
