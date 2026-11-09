using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The tutorial's lines, in a speech bubble by whoever is speaking. It is screen UI that follows
// the speaker: Dudu in either player's view (his 2D view maps him through the unwrapped walls),
// Haru by the hand in Haru's own view and toward Haru in Dudu's view. The bubble stays on the
// screen and off Dudu; its tail always points at the speaker. The tutorial keeps deciding what
// is said (Show) and when (its state object being active).
// Layout and colours are on Resources/UI/TutorialSpeechBubble.prefab.
[DisallowMultipleComponent]
public sealed class TutorialSpeechBubble : MonoBehaviour
{
    public const string DuduName = "두두";

    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform content;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text lineText;
    [SerializeField] private Image accentLine;
    [SerializeField] private RectTransform tail;
    [SerializeField] private Image tailAccent;
    [Header("Speakers")]
    [SerializeField] private Color haruColor = new Color(0.9f, 0.46f, 0.12f);
    [SerializeField] private Color duduColor = new Color(0.16f, 0.5f, 0.86f);
    [Header("Size")]
    [SerializeField, Min(100f)] private float maxLineWidth = 540f;
    [SerializeField, Min(50f)] private float minWidth = 300f;
    [SerializeField] private Vector2 padding = new Vector2(30f, 22f);
    [SerializeField, Min(0f)] private float nameHeight = 40f;
    [SerializeField, Min(0f)] private float guideHeight = 42f;
    [Header("Placement")]
    [Tooltip("Gap between the speaker's head and the bubble, in 1920x1080 UI units.")]
    [SerializeField, Min(0f)] private float gapToSpeaker = 74f;
    [Tooltip("How far the bubble keeps from the screen's edges (left/right, bottom).")]
    [SerializeField] private Vector2 screenMargin = new Vector2(24f, 24f);
    [Tooltip("Space kept free at the top of the screen for the star, the timer and the hint.")]
    [SerializeField, Min(0f)] private float topReserved = 170f;
    [Tooltip("Where Haru's bubble sits in Haru's own view, as a fraction of the screen.")]
    [SerializeField] private Vector2 haruBubbleViewport = new Vector2(0.62f, 0.46f);
    [Tooltip("Where its tail points: Haru's hand on the screen.")]
    [SerializeField] private Vector2 haruHandViewport = new Vector2(0.82f, 0.24f);
    [SerializeField, Min(0.01f)] private float followSmoothTime = 0.07f;
    [SerializeField, Min(0f)] private float tailLength = 44f;
    [SerializeField, Min(0f)] private float tailInset = 34f;
    [Header("Animation")]
    [SerializeField, Min(0.01f)] private float popSeconds = 0.18f;
    [SerializeField, Min(0.01f)] private float textFadeSeconds = 0.09f;

    private GameObject state;
    private DuduSurfaceMovement dudu;
    private DuduMoveFeedback duduBody;
    private SpriteRenderer duduSprite;
    private Transform haru;
    private RectTransform canvasRect;
    private RectTransform bubble;
    private TMP_Text[] allText;

    private string speaker = string.Empty;
    private string line = string.Empty;
    private string pendingSpeaker, pendingLine;
    private float visibility;
    private float textFade = 1f;
    private bool fadingOut;
    private float bounce;
    private bool placed;
    private Vector2 velocity;
    private Vector2 size = new Vector2(400f, 160f);

    public string Speaker => pendingSpeaker ?? speaker;
    public string Line => pendingLine ?? line;

    private void Awake()
    {
        bubble = (RectTransform)transform;
        group.alpha = 0f;
        allText = new[] { nameText, lineText };
    }

    public void Bind(GameObject stateObject, DuduSurfaceMovement duduMovement, Transform haruBody)
    {
        state = stateObject;
        dudu = duduMovement;
        haru = haruBody;
        duduBody = dudu != null ? dudu.GetComponent<DuduMoveFeedback>() : null;
        duduSprite = dudu != null ? dudu.GetComponentInChildren<SpriteRenderer>(true) : null;
        canvasRect = (RectTransform)bubble.parent;
    }

    // Same call as the old box: what is being said. Repeating a line changes nothing
    // (Dudu's PC receives it many times a second).
    public void Show(string who, string what)
    {
        if (who == Speaker && what == Line) return;
        if (visibility <= 0.01f)
        {
            Apply(who, what);
            placed = false;
            return;
        }
        pendingSpeaker = who;
        pendingLine = what;
        fadingOut = true;
    }

    private void Apply(string who, string what)
    {
        bool speakerChanged = who != speaker;
        speaker = who ?? string.Empty;
        line = what ?? string.Empty;
        pendingSpeaker = pendingLine = null;
        bool isDudu = speaker == DuduName;
        Color color = isDudu ? duduColor : haruColor;
        nameText.text = speaker;
        nameText.color = color;
        lineText.text = line;
        accentLine.color = color;
        tailAccent.color = color;

        Vector2 preferred = lineText.GetPreferredValues(line, maxLineWidth, 0f);
        float textWidth = Mathf.Min(preferred.x, maxLineWidth);
        size = new Vector2(
            Mathf.Max(minWidth, textWidth + padding.x * 2f),
            padding.y * 2f + nameHeight + preferred.y + guideHeight);
        bubble.sizeDelta = size;
        if (speakerChanged) placed = false;
    }

    private void LateUpdate()
    {
        if (canvasRect == null) return;
        float dt = Time.unscaledDeltaTime;
        bool want = state != null && state.activeSelf && line.Length + (pendingLine?.Length ?? 0) > 0;
        visibility = Mathf.MoveTowards(visibility, want ? 1f : 0f, dt / popSeconds);

        if (fadingOut)
        {
            textFade = Mathf.MoveTowards(textFade, 0f, dt / textFadeSeconds);
            if (textFade <= 0f)
            {
                Apply(pendingSpeaker, pendingLine);
                fadingOut = false;
                bounce = 1f;
            }
        }
        else textFade = Mathf.MoveTowards(textFade, 1f, dt / textFadeSeconds);
        foreach (TMP_Text text in allText) text.alpha = textFade;

        group.alpha = visibility;
        if (visibility <= 0f)
        {
            placed = false;
            return;
        }

        bounce = Mathf.MoveTowards(bounce, 0f, dt * 5f);
        float eased = 1f - (1f - visibility) * (1f - visibility);
        content.localScale = Vector3.one * (Mathf.Lerp(0.85f, 1f, eased) + Mathf.Sin(bounce * Mathf.PI) * 0.03f);

        if (!TryGetSpeakerPlace(out Vector2 target, out Rect speakerRect, out Vector2 fixedPlace, out bool useFixed))
            return;
        Vector2 goal = useFixed ? Clamp(fixedPlace) : PlaceAround(speakerRect);
        goal = AvoidDudu(goal, speakerRect);
        if (!placed)
        {
            bubble.anchoredPosition = goal;
            velocity = Vector2.zero;
            placed = true;
        }
        else
            bubble.anchoredPosition = Vector2.SmoothDamp(bubble.anchoredPosition, goal, ref velocity, followSmoothTime, Mathf.Infinity, dt);
        AimTail(target);
    }

    // Where the speaker is, in canvas units. target is what the tail points at.
    private bool TryGetSpeakerPlace(out Vector2 target, out Rect speakerRect, out Vector2 fixedPlace, out bool useFixed)
    {
        target = fixedPlace = Vector2.zero;
        speakerRect = default;
        useFixed = false;
        GameViewManager view = FxView.Current;
        if (view == null) return false;
        bool duduView = view.IsDuduMode;
        Camera camera = duduView ? view.DuduCamera : view.HaruCamera;
        if (camera == null) return false;

        Vector2 half = canvasRect.rect.size * 0.5f;
        if (speaker == DuduName)
        {
            GetDuduRect(camera, duduView, out speakerRect, out target);
            return true;
        }

        if (!duduView)
        {
            // Haru has no body in Haru's own view: the bubble sits above the hand, clear of the arm.
            useFixed = true;
            fixedPlace = new Vector2((haruBubbleViewport.x - 0.5f) * half.x * 2f, (haruBubbleViewport.y - 0.5f) * half.y * 2f);
            target = new Vector2((haruHandViewport.x - 0.5f) * half.x * 2f, (haruHandViewport.y - 0.5f) * half.y * 2f);
            return true;
        }

        // In Dudu's view: toward Haru's cursor if it is on this wall, else where Haru stands.
        DuduCameraController duduCamera = view.DuduCamera != null ? view.DuduCamera.GetComponent<DuduCameraController>() : null;
        if (duduCamera == null) return false;
        Vector3 place;
        if (!duduCamera.TryGetHaruCursorView(out place))
        {
            Vector3 haruPosition = haru != null ? haru.position : Vector3.zero;
            place = duduCamera.MapToView(haruPosition, dudu != null ? dudu.CurrentSurface : null);
            place.y = camera.transform.position.y + camera.orthographicSize * 0.45f;
        }
        target = ToCanvas(camera, place);
        speakerRect = new Rect(target - new Vector2(20f, 20f), new Vector2(40f, 40f));
        return true;
    }

    private void GetDuduRect(Camera camera, bool duduView, out Rect rect, out Vector2 head)
    {
        DuduSurface surface = dudu != null ? dudu.CurrentSurface : null;
        Vector3 up = surface != null ? surface.Up.normalized : Vector3.up;
        Vector3 feet = duduBody != null ? duduBody.FeetPoint() : (dudu != null ? dudu.transform.position : Vector3.zero);
        float height = duduSprite != null ? duduSprite.bounds.size.y * 0.8f : 1.2f;
        Vector3 top = feet + up * height;
        if (duduView)
        {
            DuduCameraController controller = camera.GetComponent<DuduCameraController>();
            if (controller != null)
            {
                feet = controller.MapToView(feet, surface);
                top = controller.MapToView(top, surface);
            }
        }
        head = ToCanvas(camera, top);
        Vector2 low = ToCanvas(camera, feet);
        float width = Mathf.Max(30f, Mathf.Abs(head.y - low.y) * 0.5f);
        rect = Rect.MinMaxRect(Mathf.Min(head.x, low.x) - width * 0.5f, Mathf.Min(head.y, low.y),
            Mathf.Max(head.x, low.x) + width * 0.5f, Mathf.Max(head.y, low.y));
    }

    // World to canvas, including points behind the camera (they stay off-screen, on the right side).
    private Vector2 ToCanvas(Camera camera, Vector3 world)
    {
        Vector3 local = camera.transform.InverseTransformPoint(world);
        if (!camera.orthographic && local.z <= camera.nearClipPlane)
        {
            Vector2 away = new Vector2(local.x, local.y);
            if (away.sqrMagnitude < 0.0001f) away = Vector2.down;
            return away.normalized * 4000f;
        }
        Vector3 screen = camera.WorldToScreenPoint(world);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 point);
        return point;
    }

    // Above the head if there is room, otherwise below the feet, otherwise beside the speaker.
    private Vector2 PlaceAround(Rect speakerRect)
    {
        float centerX = speakerRect.center.x;
        Vector2 above = new Vector2(centerX, speakerRect.yMax + gapToSpeaker + size.y * 0.5f);
        Vector2 clamped = Clamp(above);
        if (!Overlaps(clamped, speakerRect)) return clamped;

        Vector2 below = Clamp(new Vector2(centerX, speakerRect.yMin - gapToSpeaker - size.y * 0.5f));
        if (!Overlaps(below, speakerRect)) return below;

        float side = centerX < 0f ? 1f : -1f;
        return Clamp(new Vector2(centerX + side * (speakerRect.width * 0.5f + gapToSpeaker + size.x * 0.5f), speakerRect.center.y));
    }

    // When Haru talks, the bubble must not end up on Dudu either.
    private Vector2 AvoidDudu(Vector2 goal, Rect speakerRect)
    {
        if (speaker == DuduName) return goal;
        GameViewManager view = FxView.Current;
        Camera camera = view.IsDuduMode ? view.DuduCamera : view.HaruCamera;
        GetDuduRect(camera, view.IsDuduMode, out Rect duduRect, out _);
        if (!Overlaps(goal, duduRect)) return goal;
        Vector2 flipped = Clamp(new Vector2(-goal.x, goal.y));
        if (!Overlaps(flipped, duduRect)) return flipped;
        return Clamp(new Vector2(goal.x, duduRect.yMax + gapToSpeaker + size.y * 0.5f));
    }

    private bool Overlaps(Vector2 center, Rect other)
    {
        Rect mine = new Rect(center - size * 0.5f, size);
        return mine.Overlaps(other);
    }

    private Vector2 Clamp(Vector2 center)
    {
        Vector2 half = canvasRect.rect.size * 0.5f;
        float maxX = Mathf.Max(0f, half.x - size.x * 0.5f - screenMargin.x);
        float minY = -half.y + size.y * 0.5f + screenMargin.y;
        float maxY = Mathf.Max(minY, half.y - size.y * 0.5f - topReserved);
        return new Vector2(Mathf.Clamp(center.x, -maxX, maxX), Mathf.Clamp(center.y, minY, maxY));
    }

    // The tail sits on the edge nearest the speaker and points right at them, even when the
    // bubble has been pushed away from them by the edge of the screen.
    private void AimTail(Vector2 target)
    {
        Vector2 half = size * 0.5f;
        Vector2 toTarget = target - bubble.anchoredPosition;
        bool inside = Mathf.Abs(toTarget.x) < half.x && Mathf.Abs(toTarget.y) < half.y;
        tail.gameObject.SetActive(!inside);
        if (inside) return;

        Vector2 edge;
        float rangeX = Mathf.Max(0f, half.x - tailInset), rangeY = Mathf.Max(0f, half.y - tailInset);
        // Which side faces the target, compared in proportion to the bubble's shape.
        if (Mathf.Abs(toTarget.y) * half.x >= Mathf.Abs(toTarget.x) * half.y)
            edge = new Vector2(Mathf.Clamp(toTarget.x, -rangeX, rangeX), Mathf.Sign(toTarget.y) * (half.y - 4f));
        else
            edge = new Vector2(Mathf.Sign(toTarget.x) * (half.x - 4f), Mathf.Clamp(toTarget.y, -rangeY, rangeY));

        tail.anchoredPosition = edge;
        Vector2 direction = toTarget - edge;
        if (direction.sqrMagnitude < 1f) direction = Vector2.down;
        // The tail picture points down.
        tail.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f);
        tail.sizeDelta = new Vector2(tail.sizeDelta.x, Mathf.Clamp(direction.magnitude, tailLength * 0.5f, tailLength));
    }
}
