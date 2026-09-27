using UnityEngine;

public static class CoopAudioSettings
{
    private const string MasterKey = "DrawAndGo.Audio.Master";
    private const string BgmKey = "DrawAndGo.Audio.Bgm";
    private const string SfxKey = "DrawAndGo.Audio.Sfx";
    private const string VoiceKey = "DrawAndGo.Audio.Voice";
    private const string MicKey = "DrawAndGo.Audio.MicEnabled";

    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(MasterKey, 1f);
        set
        {
            float volume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MasterKey, volume);
            AudioListener.volume = volume;
        }
    }

    public static float BgmVolume
    {
        get => PlayerPrefs.GetFloat(BgmKey, 1f);
        set => PlayerPrefs.SetFloat(BgmKey, Mathf.Clamp01(value));
    }

    public static float SfxVolume
    {
        get => PlayerPrefs.GetFloat(SfxKey, 1f);
        set => PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value));
    }

    public static float VoiceVolume
    {
        get => PlayerPrefs.GetFloat(VoiceKey, 1f);
        set => PlayerPrefs.SetFloat(VoiceKey, Mathf.Clamp01(value));
    }

    public static bool MicEnabled
    {
        get => PlayerPrefs.GetInt(MicKey, 0) != 0;
        set => PlayerPrefs.SetInt(MicKey, value ? 1 : 0);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        AudioListener.volume = MasterVolume;
    }

    public static void Save()
    {
        PlayerPrefs.Save();
    }
}
