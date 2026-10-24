using System;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public sealed class PopupDropInAnimation : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float duration = 0.5f;
    [SerializeField, Min(0.01f)] private float exitDuration = 0.3f;
    [SerializeField, Min(0f)] private float topGap = 24f;

    private RectTransform panel;
    private Vector2 restPosition;
    private Vector2 from;
    private Vector2 to;
    private float elapsed;
    private bool playing;
    private bool exiting;
    private bool skipNextEntrance;
    private Action exitDone;

    private void Awake()
    {
        panel = GetComponent<RectTransform>();
        restPosition = panel.anchoredPosition;
    }

    // The next time the panel turns on it simply appears in place (e.g. turning a page).
    public void SkipNextEntrance() => skipNextEntrance = true;

    // Slides the panel back up out of view, then calls done (e.g. to hide the popup).
    public void PlayExit(Action done)
    {
        if (!isActiveAndEnabled)
        {
            done?.Invoke();
            return;
        }
        from = panel.anchoredPosition;
        to = AboveCanvas();
        elapsed = 0f;
        playing = true;
        exiting = true;
        exitDone = done;
    }

    private void OnEnable()
    {
        exiting = false;
        exitDone = null;
        if (skipNextEntrance)
        {
            skipNextEntrance = false;
            panel.anchoredPosition = restPosition;
            playing = false;
            return;
        }

        from = AboveCanvas();
        to = restPosition;
        panel.anchoredPosition = from;
        elapsed = 0f;
        playing = true;
    }

    private Vector2 AboveCanvas()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        float canvasHeight = canvasRect != null && canvasRect.rect.height > 0f
            ? canvasRect.rect.height
            : Screen.height;

        // The panel's lower edge just above the visible canvas.
        float y = canvasHeight * 0.5f + panel.rect.height * panel.pivot.y + topGap;
        return new Vector2(restPosition.x, y);
    }

    private void Update()
    {
        if (!playing)
            return;

        elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, exiting ? exitDuration : duration));
        // Drops in fast and settles; leaves slowly at first, then quickly.
        float eased = exiting ? t * t * t : 1f - Mathf.Pow(1f - t, 3f);
        panel.anchoredPosition = Vector2.LerpUnclamped(from, to, eased);
        if (t < 1f)
            return;

        playing = false;
        if (!exiting)
            return;
        exiting = false;
        Action done = exitDone;
        exitDone = null;
        done?.Invoke();
    }

    private void OnDisable()
    {
        if (panel != null)
            panel.anchoredPosition = restPosition;
        playing = false;
        exiting = false;
        exitDone = null;
    }
}
