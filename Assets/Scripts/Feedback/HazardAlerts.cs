using UnityEngine;
using UnityEngine.UI;

public enum HazardAlert { Reversed, Slowed, Thunder, Eraser, Magnet, Wind, Count }

// Haru can be looking anywhere, so what is happening to Dudu is said on Haru's screen too: a
// short line at the top of the screen for as long as it lasts. Each hazard just holds its
// line every frame; it goes away by itself. On Dudu's PC this is where the screen flash lives.
[DefaultExecutionOrder(900)]
public sealed class HazardAlerts : MonoBehaviour
{
    private const float RowHeight = 42f;
    private const float RowGap = 6f;
    private const float RowWidth = 330f;

    private static HazardAlerts instance;

    private static readonly string[] Lines =
    {
        "두두 조작 반전!", "두두 이동속도 감소", "번개가 두두를 쫓는 중!", "지우개가 두두를 노려요!",
        "자석이 두두를 끌어당겨요", "바람에 두두가 밀려요"
    };
    private static readonly FxShape[] Icons =
    {
        FxShape.Swirl, FxShape.SlowFoot, FxShape.Bolt, FxShape.Exclaim, FxShape.Magnet, FxShape.Arrow
    };
    private static readonly Color[] Colors =
    {
        new Color(0.72f, 0.35f, 1f), new Color(0.72f, 0.74f, 0.78f), new Color(1f, 0.85f, 0.2f),
        new Color(1f, 0.45f, 0.72f), new Color(1f, 0.32f, 0.42f), new Color(0.45f, 0.82f, 1f)
    };

    private sealed class Row
    {
        public RectTransform rect;
        public CanvasGroup group;
        public RectTransform bar;
        public Text text;
        public Image icon;
        public float visibility;
        public float heldUntil;
        public float time = -1f;
        public float pop;
    }

    private readonly Row[] rows = new Row[(int)HazardAlert.Count];
    private Row ping;
    private Canvas canvas;
    private RectTransform canvasRect;
    private Image flash;
    private float flashAlpha;
    private float flashFade = 1f;

    private static HazardAlerts Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject holder = new GameObject("Hazard Alerts");
                holder.transform.SetParent(FxRunner.Instance.transform, false);
                instance = holder.AddComponent<HazardAlerts>();
                instance.Build();
            }
            return instance;
        }
    }

    // Keep this line up for now. timeLeft (0-1) shows a shrinking bar; below 0 there is none.
    public static void Hold(HazardAlert alert, float timeLeft = -1f)
    {
        Row row = Instance.rows[(int)alert];
        row.heldUntil = Time.unscaledTime + 0.15f;
        row.time = timeLeft;
    }

    // A one-off line for an event, like a hit.
    public static void Ping(string line, Color color)
    {
        Row row = Instance.ping;
        row.text.text = line;
        row.icon.color = color;
        row.heldUntil = Time.unscaledTime + 1.3f;
        row.pop = 1f;
    }

    // A flash of the whole screen, on the PC playing Dudu.
    public static void Flash(Color color, float alpha, float seconds)
    {
        if (!FxView.LocalIsDudu) return;
        HazardAlerts alerts = Instance;
        if (alpha < alerts.flashAlpha) return;
        alerts.flash.color = new Color(color.r, color.g, color.b, alpha);
        alerts.flashAlpha = alpha;
        alerts.flashFade = alpha / Mathf.Max(0.01f, seconds);
    }

    private void Build()
    {
        GameObject canvasObject = new GameObject("Hazard Alert Canvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 19;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = canvasObject.GetComponent<RectTransform>();

        GameObject flashObject = new GameObject("Screen Flash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        flashObject.transform.SetParent(canvasObject.transform, false);
        RectTransform flashRect = flashObject.GetComponent<RectTransform>();
        flashRect.anchorMin = Vector2.zero;
        flashRect.anchorMax = Vector2.one;
        flashRect.offsetMin = flashRect.offsetMax = Vector2.zero;
        flash = flashObject.GetComponent<Image>();
        flash.raycastTarget = false;
        flash.enabled = false;

        for (int i = 0; i < rows.Length; i++) rows[i] = CreateRow(Lines[i], Colors[i], Icons[i]);
        ping = CreateRow(string.Empty, Color.white, FxShape.Exclaim);
    }

    private Row CreateRow(string line, Color color, FxShape iconShape)
    {
        GameObject panel = new GameObject("Alert", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        panel.transform.SetParent(canvas.transform, false);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(RowWidth, RowHeight);
        Image background = panel.GetComponent<Image>();
        background.color = new Color(0.1f, 0.09f, 0.13f, 0.78f);
        background.raycastTarget = false;
        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0f;

        Image icon = NewImage(panel.transform, "Icon", new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(32f, 32f));
        icon.sprite = FxArt.Get(iconShape);
        icon.color = color;

        GameObject label = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        label.transform.SetParent(panel.transform, false);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(50f, 2f);
        labelRect.offsetMax = new Vector2(-8f, -2f);
        Text text = label.GetComponent<Text>();
        text.font = GameFont.Bold;
        text.fontSize = 22;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = line;

        Image bar = NewImage(panel.transform, "Time Left", new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
        bar.color = color;
        RectTransform barRect = bar.rectTransform;
        barRect.anchorMin = Vector2.zero;
        barRect.anchorMax = new Vector2(1f, 0f);
        barRect.pivot = new Vector2(0f, 0f);
        barRect.anchoredPosition = Vector2.zero;
        barRect.sizeDelta = new Vector2(0f, 4f);

        return new Row { rect = rect, group = group, bar = barRect, text = text, icon = icon };
    }

    private static Image NewImage(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        GameObject holder = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        holder.transform.SetParent(parent, false);
        RectTransform rect = holder.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image image = holder.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private void LateUpdate()
    {
        if (flashAlpha > 0f)
        {
            flashAlpha = Mathf.Max(0f, flashAlpha - flashFade * Time.unscaledDeltaTime);
            Color color = flash.color;
            color.a = flashAlpha;
            flash.color = color;
            flash.enabled = flashAlpha > 0.002f;
        }

        bool show = FxView.HaruGameplay && !SettingsPopupController.IsAnyOpen;
        float y = -14f;
        y = Layout(ping, show, y);
        foreach (Row row in rows) y = Layout(row, show, y);
    }

    private static float Layout(Row row, bool show, float y)
    {
        bool held = show && Time.unscaledTime < row.heldUntil;
        if (held && row.visibility <= 0f) row.pop = 1f;
        row.visibility = Mathf.MoveTowards(row.visibility, held ? 1f : 0f, Time.unscaledDeltaTime * (held ? 9f : 4f));
        row.group.alpha = row.visibility;
        row.group.gameObject.SetActive(row.visibility > 0f);
        if (row.visibility <= 0f) return y;

        row.pop = Mathf.MoveTowards(row.pop, 0f, Time.unscaledDeltaTime * 4f);
        row.rect.localScale = Vector3.one * (1f + row.pop * 0.14f);
        row.rect.anchoredPosition = new Vector2(0f, y);
        row.bar.gameObject.SetActive(row.time >= 0f);
        if (row.time >= 0f) row.bar.anchorMax = new Vector2(Mathf.Clamp01(row.time), 0f);
        return y - (RowHeight + RowGap) * row.visibility;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
