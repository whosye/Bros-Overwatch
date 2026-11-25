using UnityEngine;

// Jednoduche uzivatelske nastaveni ulozene v PlayerPrefs (per pocitac).
public static class GameSettings
{
    // Pouze pro automaticke testy (v headless rezimu nejde zamknout kurzor).
    public static bool ForceCursorLocked;

    public static bool CursorLocked => !TextChatUI.BlocksGameplayInput && (ForceCursorLocked || Cursor.lockState == CursorLockMode.Locked);

    const string SensitivityKey = "settings.sensitivity";
    const string VolumeKey = "settings.volume";
    const string AmbientVolumeKey = "settings.ambientVolume";
    const string VoiceVolumeKey = "settings.voiceVolume";

    // Hlasitost hlasek hrdinu (a Gluma) zvlast od ostatnich zvuku; celkova Volume plati i pro ne.
    public static float VoiceVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(VoiceVolumeKey, 1f));
        set => PlayerPrefs.SetFloat(VoiceVolumeKey, Mathf.Clamp01(value));
    }
    // Zvuky zbrani (vystrely, prebijeni, tetiva, sipy) a ostatni efekty (schopnosti, zasahy, vybuchy, stroje...)
    // zvlast od hlasek, hudby a prostredi; celkova Volume plati i pro ne.
    public static float WeaponVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat("settings.weaponVolume", 1f));
        set => PlayerPrefs.SetFloat("settings.weaponVolume", Mathf.Clamp01(value));
    }
    public static float EffectsVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat("settings.effectsVolume", 1f));
        set => PlayerPrefs.SetFloat("settings.effectsVolume", Mathf.Clamp01(value));
    }
    public static float MusicVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat("settings.musicVolume", 1f));
        set => PlayerPrefs.SetFloat("settings.musicVolume", Mathf.Clamp01(value));
    }
    public static bool MusicEnabled
    {
        get => PlayerPrefs.GetInt("settings.musicEnabled", 1) != 0;
        set
        {
            PlayerPrefs.SetInt("settings.musicEnabled", value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static float AmbientVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(AmbientVolumeKey, 0.12f));
        set => PlayerPrefs.SetFloat(AmbientVolumeKey, Mathf.Clamp01(value));
    }
    // Logovani vykonu do souboru (PerfLogger) - zapina se v Nastaveni > HRA.
    // Neuklada se: po kazdem spusteni hry je vypnute.
    static bool perfLogEnabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPerfLog() => perfLogEnabled = false;

    public static bool PerfLogEnabled
    {
        get => perfLogEnabled;
        set
        {
            perfLogEnabled = value;
            PerfLogger.Apply();
        }
    }

    const string HeadBobEnabledKey = "settings.headBobEnabled";
    const string HeadBobIntensityKey = "settings.headBobIntensity";

    public static bool HeadBobEnabled
    {
        get => PlayerPrefs.GetInt(HeadBobEnabledKey, 1) != 0;
        set => PlayerPrefs.SetInt(HeadBobEnabledKey, value ? 1 : 0);
    }

    public static float HeadBobIntensity
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(HeadBobIntensityKey, 1f), 0f, 2f);
        set => PlayerPrefs.SetFloat(HeadBobIntensityKey, Mathf.Clamp(value, 0f, 2f));
    }

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

    // Docasne ztlumeni vseho (1 = bez ztlumeni): behem hlasky k ultimatce, viz HeroVoice.
    public static float Duck = 1f;

    public static void ApplyVolume()
    {
        AudioListener.volume = Volume * Duck;
    }
}
