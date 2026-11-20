using UnityEngine;

// Values chosen before hosting are applied once, when the server spawns MatchManager.
public static class HostOptions
{
    public static int Map { get=>Mathf.Clamp(PlayerPrefs.GetInt("host.map",0),0,MatchManager.MapNames.Length-1); set=>PlayerPrefs.SetInt("host.map",value); }
    public static int Mode { get=>PlayerPrefs.GetInt("host.mode",0)==MatchManager.ModeAttack?MatchManager.ModeAttack:MatchManager.ModeDeathmatch; set=>PlayerPrefs.SetInt("host.mode",value); }
    public static int Kills { get=>Mathf.Clamp(PlayerPrefs.GetInt("host.kills",30),1,100); set=>PlayerPrefs.SetInt("host.kills",Mathf.Clamp(value,1,100)); }
    public static int CaptureSeconds { get=>Mathf.Clamp(PlayerPrefs.GetInt("host.capture",20),5,120); set=>PlayerPrefs.SetInt("host.capture",Mathf.Clamp(value,5,120)); }
    public static bool ApplyOnSpawn;
}
