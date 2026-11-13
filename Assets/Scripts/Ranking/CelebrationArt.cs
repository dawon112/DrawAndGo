using UnityEngine;

// What the final clear's dance needs, kept in Resources/Celebration/CelebrationArt so the
// stage can be built anywhere at run time. Filled in by Tools > Draw And Go > 전시 랭킹 UI 다시 만들기.
[CreateAssetMenu(menuName = "Follow My Line/Celebration Art", fileName = "CelebrationArt")]
public sealed class CelebrationArt : ScriptableObject
{
    [Tooltip("Plays Haru_ani_dance on a loop on Haru's humanoid rig.")]
    public RuntimeAnimatorController haruDance;
    [Tooltip("dudu_dance's frames, in order.")]
    public Sprite[] duduDance;
    [Min(1f)] public float duduFramesPerSecond = 8f;

    private static CelebrationArt cached;

    public static CelebrationArt Load()
    {
        if (cached == null) cached = Resources.Load<CelebrationArt>("Celebration/CelebrationArt");
        return cached;
    }
}
