using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public sealed class PopupDropInAnimation : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float duration = 0.5f;
    [SerializeField, Min(0f)] private float topGap = 24f;

    private RectTransform panel;
    private Vector2 restPosition;
    private Vector2 startPosition;
    private float elapsed;
    private bool playing;

    private void Awake()
    {
        panel = GetComponent<RectTransform>();
        restPosition = panel.anchoredPosition;
    }

    private void OnEnable()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        float canvasHeight = canvasRect != null && canvasRect.rect.height > 0f
            ? canvasRect.rect.height
            : Screen.height;

        // The panel's lower edge begins just above the visible canvas.
        float startY = canvasHeight * 0.5f + panel.rect.height * panel.pivot.y + topGap;
        startPosition = new Vector2(restPosition.x, startY);
        panel.anchoredPosition = startPosition;
        elapsed = 0f;
        playing = true;
    }

    private void Update()
    {
        if (!playing)
            return;

        elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        panel.anchoredPosition = Vector2.LerpUnclamped(startPosition, restPosition, eased);
        if (t >= 1f)
            playing = false;
    }

    private void OnDisable()
    {
        if (panel != null)
            panel.anchoredPosition = restPosition;
        playing = false;
    }
}
