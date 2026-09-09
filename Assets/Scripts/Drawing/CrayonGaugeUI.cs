using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CrayonGaugeUI : MonoBehaviour
{
    private RectTransform fill;
    private RectTransform background;
    private Text label;

    private void Awake()
    {
        Build();
    }

    public void SetAmount(float normalizedAmount, bool recharging)
    {
        Build();
        float amount = Mathf.Clamp01(normalizedAmount);
        fill.sizeDelta = new Vector2(254f * amount, 22f);
        label.text = recharging && amount <= 0f
            ? "CRAYON 0%  EMPTY"
            : recharging && amount < 1f
                ? $"CRAYON {Mathf.RoundToInt(amount * 100f)}%  RECHARGING"
                : $"CRAYON {Mathf.RoundToInt(amount * 100f)}%";
    }

    public void SetSplitScreenLayout(bool split)
    {
        Build();
        float centerX = split ? 0.75f : 0.5f;
        background.anchorMin = new Vector2(centerX, 0f);
        background.anchorMax = new Vector2(centerX, 0f);
    }

    private void Build()
    {
        if (fill != null) return;

        GameObject canvasObject = new GameObject("Crayon Gauge Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue - 1;

        GameObject background = CreateImage(canvasObject.transform, "Crayon Gauge", new Color(0.04f, 0.05f, 0.07f, 0.88f));
        RectTransform backgroundRect = background.GetComponent<RectTransform>();
        this.background = backgroundRect;
        backgroundRect.anchorMin = new Vector2(0.5f, 0f);
        backgroundRect.anchorMax = new Vector2(0.5f, 0f);
        backgroundRect.pivot = new Vector2(0.5f, 0f);
        backgroundRect.anchoredPosition = new Vector2(0f, 22f);
        backgroundRect.sizeDelta = new Vector2(260f, 28f);
        Outline outline = background.AddComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(1f, -1f);

        GameObject fillObject = CreateImage(background.transform, "Fill", new Color(22f / 255f, 127f / 255f, 195f / 255f, 1f));
        fill = fillObject.GetComponent<RectTransform>();
        fill.anchorMin = new Vector2(0f, 0.5f);
        fill.anchorMax = new Vector2(0f, 0.5f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.anchoredPosition = new Vector2(3f, 0f);
        fill.sizeDelta = new Vector2(254f, 22f);

        GameObject textObject = new GameObject("Amount", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(background.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        label = textObject.GetComponent<Text>();
        label.font = GameFont.Bold;
        label.fontSize = 13;
        label.fontStyle = FontStyle.Normal;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    private static GameObject CreateImage(Transform parent, string objectName, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return imageObject;
    }
}
