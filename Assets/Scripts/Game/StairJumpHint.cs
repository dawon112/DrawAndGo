using UnityEngine;
using UnityEngine.UI;

// While Haru climbs the stairs, the hint at the top of the screen (the tutorial's) tells him he
// can jump. It is ticked off when he reaches the top and does not come back after that; if he
// turns back down first, it just goes away and returns next time he climbs. Haru's screen only:
// on Dudu's PC, and in Dudu's view, it never shows.
[DisallowMultipleComponent]
public sealed class StairJumpHint : MonoBehaviour
{
    [Tooltip("The stairs, in world space: the hint shows while Haru stands inside.")]
    public Bounds stairs = new Bounds(new Vector3(6.65f, 3f, 2.05f), new Vector3(12.5f, 7f, 4.1f));
    [Tooltip("Height of the floor at the top of the stairs: reaching it ticks the hint off.")]
    public float topHeight = 6.2f;
    [Tooltip("Shown with the jump key as a key cap in front.")]
    public string message = "를 눌러 점프할 수 있어요!";

    private Player3DMovement haru;
    private GameViewManager views;
    private TutorialHintUI hint;
    private GameObject state;
    private bool climbedOnce;

    private static bool IsHarusPc => GameSession.Current == null || GameSession.Current.IsHost;

    private void Update()
    {
        if (!IsHarusPc) return;
        if (haru == null) haru = FindAnyObjectByType<Player3DMovement>();
        if (views == null) views = FindAnyObjectByType<GameViewManager>();
        if (haru == null) return;

        Vector3 feet = haru.transform.position;
        // On the steps, not just walking past their foot.
        bool onStairs = !climbedOnce && stairs.Contains(feet) && feet.y > 0.3f;
        if (onStairs && feet.y >= topHeight - 0.3f) climbedOnce = true;
        bool show = onStairs && !climbedOnce && (views == null || views.IsHaruGameplayView);

        if (show && hint == null) Build();
        if (state == null) return;
        if (state.activeSelf && !show && !climbedOnce) hint.QuietNextHide();
        state.SetActive(show);
    }

    private void Build()
    {
        TutorialHintUI prefab = Resources.Load<TutorialHintUI>("UI/TutorialHint");
        if (prefab == null) return;
        GameObject canvasObject = new GameObject("Stair Jump Hint", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        state = new GameObject("Hint State");
        state.transform.SetParent(canvasObject.transform, false);
        state.SetActive(false);
        hint = Instantiate(prefab, canvasObject.transform, false);
        hint.Bind(state);
        string key = CoopInputSettings.GetKeyLabel(CoopInputAction.HaruJump);
        hint.text = $"[ {key} ] {message}";
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(stairs.center, stairs.size);
    }
#endif
}
