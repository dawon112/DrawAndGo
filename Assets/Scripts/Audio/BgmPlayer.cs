using UnityEngine;
using UnityEngine.SceneManagement;

// Plays the music for wherever the players are: the title track on the title screen, the
// in-game track through the tutorial and the stages (carrying on, not restarting, from one to
// the next), and the clear track on the final clear screen. Tracks cross-fade, and the
// BGM volume in the settings applies at once. Each PC plays its own; nothing is networked.
public sealed class BgmPlayer : MonoBehaviour
{
    private static BgmPlayer instance;

    private BgmLibrary library;
    private AudioSource current, previous;
    private BgmTrack playing = BgmTrack.None;
    private float fade = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject holder = new GameObject("BGM Player");
        DontDestroyOnLoad(holder);
        instance = holder.AddComponent<BgmPlayer>();
    }

    // For moments inside a scene, such as the final clear screen.
    public static void Play(BgmTrack track)
    {
        if (instance == null) Boot();
        instance.Switch(track);
    }

    private void Awake()
    {
        library = BgmLibrary.Load();
        current = NewSource();
        previous = NewSource();
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
    }

    private AudioSource NewSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.priority = 0;
        source.ignoreListenerPause = true;
        return source;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        StageSequence sequence = StageSequence.Load();
        if (scene.name == sequence.titleScene) Switch(BgmTrack.Title);
        else if (sequence.IsStage(scene.name)) Switch(BgmTrack.InGame);
    }

    private void Switch(BgmTrack track)
    {
        if (track == playing) return;
        playing = track;
        AudioClip clip = library != null ? library.Clip(track) : null;

        // The old track fades out on the spare source while the new one fades in.
        (previous, current) = (current, previous);
        current.Stop();
        current.clip = clip;
        if (clip != null) current.Play();
        fade = library != null && library.fadeSeconds > 0f && previous.isPlaying ? 0f : 1f;
        if (fade >= 1f) previous.Stop();
        ApplyVolume();
    }

    private void Update()
    {
        if (fade < 1f)
        {
            fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / Mathf.Max(0.01f, library.fadeSeconds));
            if (fade >= 1f) previous.Stop();
        }
        ApplyVolume();
    }

    private void ApplyVolume()
    {
        float level = (library != null ? library.volume : 0.55f) * CoopAudioSettings.BgmVolume;
        current.volume = level * fade;
        previous.volume = level * (1f - fade);
    }
}
