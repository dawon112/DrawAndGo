using System;
using UnityEngine;

// Designer-editable play order. Lives at Resources/StageSequence so both the title
// scene and every stage read the same list without scene references.
[CreateAssetMenu(menuName = "Follow My Line/Stage Sequence", fileName = "StageSequence")]
public sealed class StageSequence : ScriptableObject
{
    [Tooltip("Scenes played in order after matching. Each must be in Build Settings.")]
    public string[] stageScenes = { "Tutorial", "Player3DScene" };
    [Tooltip("The stage both players may agree to skip.")]
    public string tutorialScene = "Tutorial";
    public string titleScene = "TitleScene";
    [Tooltip("Pause on the faded clear screen before the next stage loads.")]
    [Min(0f)] public float nextStageDelay = 1f;

    private static StageSequence cached;

    public static StageSequence Load()
    {
        if (cached == null) cached = Resources.Load<StageSequence>("StageSequence");
        if (cached == null) cached = CreateInstance<StageSequence>();
        return cached;
    }

    public string FirstStage => stageScenes != null && stageScenes.Length > 0 ? stageScenes[0] : titleScene;

    public int IndexOf(string sceneName) =>
        stageScenes == null ? -1 : Array.IndexOf(stageScenes, sceneName);

    public bool IsStage(string sceneName) => IndexOf(sceneName) >= 0;

    // Null when the given stage is the last one.
    public string NextAfter(string sceneName)
    {
        int index = IndexOf(sceneName);
        return index >= 0 && index + 1 < stageScenes.Length ? stageScenes[index + 1] : null;
    }
}
