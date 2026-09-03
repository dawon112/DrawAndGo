using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CrosshairController : MonoBehaviour
{
    [SerializeField] private Color color = Color.black;
    [SerializeField, Min(1f)] private float armLength = 12f;
    [SerializeField, Min(1f)] private float thickness = 2f;

    private Canvas canvas;
    private Image horizontalBar;
    private Image verticalBar;
    private Text statusText;
    private bool eraserMode;
    private bool cameraLock;

    private void Awake()
    {
        BuildCrosshair();
    }

    public void SetVisible(bool visible)
    {
        BuildCrosshair();
        canvas.enabled = visible;
    }

    public void SetEraserMode(bool eraserMode)
    {
        BuildCrosshair();
        this.eraserMode = eraserMode;
        Color toolColor = eraserMode ? new Color(1f, 0.2f, 0.2f, 1f) : color;
        horizontalBar.color = toolColor;
        verticalBar.color = toolColor;
        RefreshStatus();
    }

    public void SetCameraLock(bool locked)
    {
        BuildCrosshair();
        cameraLock = locked;
        horizontalBar.enabled = !locked;
        verticalBar.enabled = !locked;
        RefreshStatus();
    }

    private void BuildCrosshair()
    {
        if (canvas != null)
            return;

        GameObject canvasObject = new GameObject("Haru Crosshair Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        horizontalBar = CreateBar(canvasObject.transform, "Horizontal", new Vector2(armLength, thickness));
        verticalBar = CreateBar(canvasObject.transform, "Vertical", new Vector2(thickness, armLength));
        statusText = CreateStatusText(canvasObject.transform);
        RefreshStatus();
    }

    private Image CreateBar(Transform parent, string objectName, Vector2 size)
    {
        GameObject bar = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        bar.transform.SetParent(parent, false);
        RectTransform rect = bar.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        Image image = bar.GetComponent<Image>();
        image.color = color;
        Outline outline = bar.GetComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;
        return image;
    }

    private static Text CreateStatusText(Transform parent)
    {
        GameObject label = new GameObject("Drawing Status", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        label.transform.SetParent(parent, false);
        RectTransform rect = label.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(16f, -16f);
        rect.sizeDelta = new Vector2(260f, 32f);

        Text text = label.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 18;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.UpperLeft;
        text.color = Color.white;
        text.supportRichText = false;
        return text;
    }

    private void RefreshStatus()
    {
        if (statusText == null)
            return;

        string tool = eraserMode ? "ERASER" : "PEN";
        statusText.text = cameraLock ? $"{tool} | DRAW MODE" : $"{tool} | Y: DRAW MODE";
    }
}
