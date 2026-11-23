using Unity.Netcode;
using UnityEngine;

// One clock and two spatial voices: clean nearby, filtered wet-only reflections far away.
[DisallowMultipleComponent]
public sealed class DistanceLayeredLoop : MonoBehaviour
{
    public AudioClip clip;
    public Vector3 emitterOffset;
    [Range(0f,1f)] public float volume=.85f;
    [Range(0f,4f)] public float ambientBoost=3f;
    [Min(.1f)] public float clearDistance=4f;
    [Min(1f)] public float distantDistance=18f;
    [Min(1f)] public float audibleDistance=55f;
    [Min(.01f)] public float transitionSeconds=.18f;
    AudioSource clean,distant;
    AudioListener listener;
    bool running;
    float nextListenerSearch;
    public bool Running => running;
    public AudioSource CleanSource => clean;
    public AudioSource DistantSource => distant;

    // Both gains include the same bounded attenuation, avoiding a second Unity rolloff.
    // Equal-power weights keep the middle of the crossfade from developing a volume hole.
    public Vector2 MixAt(float metres)
    {
        float near=Mathf.Max(.1f,clearDistance);
        float far=Mathf.Max(near+.1f,distantDistance);
        float end=Mathf.Max(far+.1f,audibleDistance);
        float distance=Mathf.Max(0f,metres);
        float t=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(near,far,distance));
        float attenuation=1f-Mathf.InverseLerp(near,end,distance);
        attenuation*=attenuation;
        // Keep a quiet direct cue even at full wet blend so the emitter stays locatable.
        float angle=t*Mathf.Acos(.15f);
        return new Vector2(Mathf.Cos(angle)*attenuation,
            t<=0f?0f:Mathf.Sin(angle)*attenuation);
    }

    bool CanPlay
    {
        get
        {
            var network=NetworkManager.Singleton;
            var match=MatchManager.Instance;
            return clip!=null && network!=null && network.IsListening && network.IsClient
                && !network.ShutdownInProgress && match!=null && match.IsSpawned
                && !match.IsLobby && !match.IsOver
                && (match.MapIndex==MatchManager.MapDomasov || match.MapIndex==MatchManager.MapDomasovPush);
        }
    }

    void Update()
    {
        if(!CanPlay) { StopLoop(); return; }
        if(listener==null || !listener.isActiveAndEnabled)
        {
            listener=null;
            if(Time.unscaledTime>=nextListenerSearch)
            {
                nextListenerSearch=Time.unscaledTime+.25f;
                foreach(var candidate in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                    if(candidate.isActiveAndEnabled) { listener=candidate; break; }
            }
        }
        if(listener==null) { StopLoop(); return; }
        if(!running)
        {
            if(clip.loadState==AudioDataLoadState.Unloaded) clip.LoadAudioData();
            if(clip.loadState!=AudioDataLoadState.Loaded) return;
            StartLoop();
        }
        clean.transform.localPosition=distant.transform.localPosition=emitterOffset;
        float distance=Vector3.Distance(listener.transform.position,clean.transform.position);
        Vector2 mix=MixAt(distance);
        float gain=Mathf.Clamp01(volume*ambientBoost*GameSettings.AmbientVolume);
        float smoothing=1f-Mathf.Exp(-Time.unscaledDeltaTime/Mathf.Max(.01f,transitionSeconds));
        clean.volume=Mathf.Lerp(clean.volume,gain*mix.x,smoothing);
        distant.volume=Mathf.Lerp(distant.volume,gain*mix.y,smoothing);
        if(mix==Vector2.zero || gain==0f) clean.volume=distant.volume=0f;
    }

    void StartLoop()
    {
        if(clean==null) clean=MakeSource("BigLez_Clean",false);
        if(distant==null) distant=MakeSource("BigLez_Distant",true);
        clean.gameObject.SetActive(true); distant.gameObject.SetActive(true);
        clean.clip=distant.clip=clip;
        clean.volume=distant.volume=0f;
        double start=AudioSettings.dspTime+.15;
        double phase=(NetworkManager.Singleton.ServerTime.Time+.15)%clip.length;
        int sample=Mathf.Clamp((int)(phase*clip.frequency),0,clip.samples-1);
        clean.timeSamples=distant.timeSamples=sample;
        clean.PlayScheduled(start); distant.PlayScheduled(start);
        running=true;
    }

    AudioSource MakeSource(string name,bool wet)
    {
        var child=new GameObject(name);
        child.transform.SetParent(transform,false);
        child.transform.localPosition=emitterOffset;
        var source=child.AddComponent<AudioSource>();
        source.playOnAwake=false; source.loop=true;
        source.spatialBlend=1f; source.spread=0f;
        source.dopplerLevel=0f; source.pitch=1f; source.priority=190;
        source.reverbZoneMix=0f;
        source.minDistance=1f; source.maxDistance=Mathf.Max(1f,audibleDistance);
        source.rolloffMode=AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff,AnimationCurve.Linear(0,1,1,1));
        source.volume=0f;
        if(wet)
        {
            var lowpass=child.AddComponent<AudioLowPassFilter>();
            lowpass.cutoffFrequency=16000f; lowpass.lowpassResonanceQ=1f;
            var reverb=child.AddComponent<AudioReverbFilter>();
            reverb.reverbPreset=AudioReverbPreset.User;
            reverb.dryLevel=-10000f; // Only reflections here; no second direct voice.
            reverb.room=-600f; reverb.roomHF=-300f;
            reverb.decayTime=1.8f; reverb.decayHFRatio=.9f;
            reverb.reflectionsLevel=-500f; reverb.reflectionsDelay=.055f;
            reverb.reverbLevel=-450f; reverb.reverbDelay=.035f;
            reverb.diffusion=95f; reverb.density=85f;
        }
        return source;
    }

    void StopLoop()
    {
        if(!running) return;
        foreach(var source in new[]{clean,distant})
            if(source!=null) { source.volume=0f; source.Stop(); source.gameObject.SetActive(false); }
        running=false;
    }
    void OnDisable() { StopLoop(); listener=null; nextListenerSearch=0f; }
}
