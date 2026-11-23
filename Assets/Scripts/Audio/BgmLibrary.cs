using UnityEngine;

// The game's music, kept in Resources/Audio/BgmLibrary so the player can find it from any scene.
[CreateAssetMenu(menuName = "Follow My Line/BGM Library", fileName = "BgmLibrary")]
public sealed class BgmLibrary : ScriptableObject
{
    [Tooltip("Title screen.")]
    public AudioClip title;
    [Tooltip("The tutorial and every stage.")]
    public AudioClip inGame;
    [Tooltip("The final clear screen, with the dance and the ranking.")]
    public AudioClip clear;
    [Tooltip("Music level before the player's BGM volume setting, so it sits under the effects.")]
    [Range(0f, 1f)] public float volume = 0.55f;
    [Tooltip("Seconds one track takes to fade into the next.")]
    [Min(0f)] public float fadeSeconds = 1.2f;

    private static BgmLibrary cached;

    public static BgmLibrary Load()
    {
        if (cached == null) cached = Resources.Load<BgmLibrary>("Audio/BgmLibrary");
        return cached;
    }

    public AudioClip Clip(BgmTrack track) => track switch
    {
        BgmTrack.Title => title,
        BgmTrack.InGame => inGame,
        BgmTrack.Clear => clear,
        _ => null
    };
}

public enum BgmTrack
{
    None,
    Title,
    InGame,
    Clear
}
