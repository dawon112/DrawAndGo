using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The same HUD for both players: stars top left, play time top right, and a short toast when
// Haru changes tool. It only watches the game (stars taken, StageFlow's play time), so every
// PC plays its own feedback from the shared state, and the game logic stays where it is.
// Layout and colours are on Resources/UI/GameHud.prefab.
[DisallowMultipleComponent]
public sealed class GameHud : MonoBehaviour
{
    [Header("Stars")]
    [SerializeField] private RectTransform starBadge;
    [SerializeField] private TMP_Text starCount;
    [SerializeField] private TMP_Text plusOne;
    [SerializeField] private Image starGlow;
    [SerializeField] private RectTransform starIcon;
    [SerializeField] private Image flyingStar;
    [SerializeField, Min(0.05f)] private float flySeconds = 0.5f;
    [Header("Play Time")]
    [SerializeField] private RectTransform timerBadge;
    [SerializeField] private TMP_Text timerText;
    [Header("Tool Toast")]
    [SerializeField] private CanvasGroup toolToast;
    [SerializeField] private Image toolIcon;
    [SerializeField] private TMP_Text toolLabel;
    [SerializeField] private Sprite crayonIcon;
    [SerializeField] private Sprite eraserIcon;
    [SerializeField, Min(0.1f)] private float toastSeconds = 0.9f;

    private static readonly Color StarYellow = new Color(1f, 0.82f, 0.2f);

    private Canvas canvas;
    private CanvasGroup hudGroup;
    private float clearAge;
    private RectTransform canvasRect;
    private GameViewManager view;
    private TutorialRoomLevel room;
    private CornerRoomLevel corner;
    private HaruDrawingController drawing;

    private readonly List<GameObject> pickups = new List<GameObject>();
    private readonly List<bool> wasActive = new List<bool>();
    private object boundSource;
    private int shownCount;
    private int total;
    private bool finished;

    private sealed class Flyer
    {
        public RectTransform rect;
        public Image image;
        public Vector2 from, arc;
        public Vector2 to;
        public float age, life, startSize, endSize;
        public bool countsWhenLanded;
        public bool spark;
    }

    private readonly List<Flyer> flyers = new List<Flyer>();
    private readonly List<Flyer> pool = new List<Flyer>();

    private float bounceTime = 1f, bounceLength = 0.3f, bounceSize = 0.25f;
    private float plusTime = 1f;
    private float glowTime = 1f;
    private float doorMomentAt = -1f;
    private Vector2 plusRest;

    private bool haveTool;
    private DrawingTool lastTool;
    private bool lastLock;
    private float toastUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Setup(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Setup(scene);

    private static void Setup(Scene scene)
    {
        if (!StageSequence.Load().IsStage(scene.name) || FindAnyObjectByType<GameHud>() != null) return;
        GameObject prefab = Resources.Load<GameObject>("UI/GameHud");
        if (prefab == null)
        {
            Debug.LogError("Resources/UI/GameHud prefab is missing.");
            return;
        }
        Instantiate(prefab).name = prefab.name;
    }

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        canvasRect = (RectTransform)transform;
        hudGroup = gameObject.AddComponent<CanvasGroup>();
        hudGroup.blocksRaycasts = false;
        hudGroup.interactable = false;
        flyingStar.gameObject.SetActive(false);
        plusOne.gameObject.SetActive(false);
        plusRest = plusOne.rectTransform.anchoredPosition;
        starGlow.enabled = false;
        toolToast.alpha = 0f;
    }

    private void Update()
    {
        if (view == null) view = FindAnyObjectByType<GameViewManager>();
        bool inPlay = view == null || view.CurrentCameraState == GameViewManager.CameraState.Gameplay;
        // A clear: stars and time stay for a moment, then step aside for the sequence.
        bool clearing = view != null && view.CurrentCameraState == GameViewManager.CameraState.ClearCinematic;
        clearAge = clearing ? clearAge + Time.unscaledDeltaTime : 0f;
        hudGroup.alpha = inPlay ? 1f : clearing ? 1f - Mathf.InverseLerp(0.3f, 0.55f, clearAge) : 0f;
        bool showing = hudGroup.alpha > 0f;
        if (canvas.enabled != showing) canvas.enabled = showing;

        UpdateTimer();
        UpdateStars(inPlay);
        UpdateFlyers();
        UpdateStarBadgeAnimation();
        UpdateToolToast();
    }

    private void UpdateTimer()
    {
        int seconds = StageFlow.Instance != null ? Mathf.FloorToInt(StageFlow.Instance.PlaySeconds) : 0;
        string value = $"{seconds / 60:00}:{seconds % 60:00}";
        if (timerText.text != value) timerText.text = value;
    }

    // --- Stars -------------------------------------------------------------------------

    private void BindSource()
    {
        if (room == null) room = FindAnyObjectByType<TutorialRoomLevel>();
        if (room == null && corner == null) corner = FindAnyObjectByType<CornerRoomLevel>();
        object source = room != null ? room.Stars : corner != null ? corner.coins : null;
        if (ReferenceEquals(source, boundSource)) return;
        boundSource = source;

        pickups.Clear();
        wasActive.Clear();
        if (room != null && room.Stars != null)
        {
            foreach (TutorialStarCollectible star in room.Stars)
                if (star != null) AddPickup(star.gameObject);
            total = room.RequiredStarCount;
        }
        else if (corner != null && corner.coins != null)
        {
            foreach (GameObject coin in corner.coins)
                if (coin != null) AddPickup(coin);
            total = corner.RequiredCoins;
            // The HUD says this now; a label painted on the wall would only repeat it.
            if (corner.goalLabel != null) corner.goalLabel.gameObject.SetActive(false);
        }
        shownCount = CurrentCount();
        finished = total > 0 && shownCount >= total;
        RefreshCount();
    }

    private void AddPickup(GameObject pickup)
    {
        pickups.Add(pickup);
        wasActive.Add(pickup.activeSelf);
    }

    private int CurrentCount() => room != null ? room.CollectedStars : corner != null ? corner.CollectedCount : 0;

    private void UpdateStars(bool inPlay)
    {
        BindSource();
        bool hasGoal = total > 0;
        bool visible = hasGoal && (room == null || room.IsStarCounterVisible);
        if (starBadge.gameObject.activeSelf != visible) starBadge.gameObject.SetActive(visible);

        for (int i = 0; i < pickups.Count; i++)
        {
            GameObject pickup = pickups[i];
            bool active = pickup != null && pickup.activeSelf;
            if (wasActive[i] && !active && pickup != null) StarTaken(pickup.transform.position, inPlay && visible);
            wasActive[i] = active;
        }

        // Nothing flying, but the count differs: a restart, or a state packet that jumped.
        int actual = CurrentCount();
        if (flyers.Count == 0 && shownCount != actual)
        {
            shownCount = actual;
            RefreshCount();
        }
        if (doorMomentAt >= 0f && Time.unscaledTime >= doorMomentAt)
        {
            doorMomentAt = -1f;
            DoorMoment();
        }
    }

    private void RefreshCount()
    {
        string value = $"{shownCount} / {total}";
        if (starCount.text != value) starCount.text = value;
    }

    private void StarTaken(Vector3 world, bool animate)
    {
        DuduSurfaceMovement dudu = FxView.Dudu;
        DuduSurface surface = dudu != null ? dudu.CurrentSurface : null;
        if (surface != null)
        {
            Vector3 onWall = FxWall.OnWall(surface, world, FxWall.Depth + 0.03f);
            FxRunner.Spawn(FxArt.Get(FxShape.Star), onWall, FxWall.Rotation(surface), Vector3.zero, 0.3f, 0.55f, 1.3f, StarYellow, FxLayer.Top);
            FxRunner.Spawn(FxArt.Get(FxShape.Ring), onWall, FxWall.Rotation(surface), Vector3.zero, 0.35f, 0.3f, 1.5f, StarYellow, FxLayer.Front);
            FxRunner.Burst(FxShape.Star, onWall, surface, 9, 3f, 0.5f, 0.2f, StarYellow, FxLayer.Front, 1.5f);
            FxRunner.Burst(FxShape.Spark, onWall, surface, 6, 3.6f, 0.35f, 0.24f, Color.white, FxLayer.Front);
        }
        FxSound.Play(FxClip.StarPick, FxView.Listener, 0.9f, 0.03f);

        if (!animate)
        {
            shownCount = CurrentCount();
            RefreshCount();
            return;
        }
        // A star taken out of this player's sight still flies in, from the nearest screen edge.
        Vector2 half = canvasRect.rect.size * 0.5f;
        Vector2 from = WorldToCanvas(world);
        from = new Vector2(Mathf.Clamp(from.x, -half.x, half.x), Mathf.Clamp(from.y, -half.y, half.y));
        Launch(from, BadgeIconPosition(), flySeconds, 82f, 46f, true, false);
    }

    private Vector2 WorldToCanvas(Vector3 world)
    {
        Camera camera = view == null ? null : view.IsDuduMode ? view.DuduCamera : view.HaruCamera;
        if (camera == null) return Vector2.zero;
        if (view.IsDuduMode)
        {
            DuduCameraController controller = camera.GetComponent<DuduCameraController>();
            if (controller != null) world = controller.MapToView(world);
        }
        else if (camera.transform.InverseTransformPoint(world).z <= camera.nearClipPlane)
            return new Vector2(0f, -canvasRect.rect.height * 0.5f);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, camera.WorldToScreenPoint(world), null, out Vector2 point);
        return point;
    }

    private Vector2 BadgeIconPosition()
    {
        Vector3 world = starIcon.TransformPoint(starIcon.rect.center);
        return canvasRect.InverseTransformPoint(world);
    }

    private void Launch(Vector2 from, Vector2 to, float life, float startSize, float endSize, bool counts, bool spark)
    {
        Flyer flyer;
        if (pool.Count > 0)
        {
            flyer = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
        }
        else
        {
            Image image = Instantiate(flyingStar, canvasRect, false);
            flyer = new Flyer { image = image, rect = image.rectTransform };
        }
        flyer.rect.gameObject.SetActive(true);
        flyer.rect.SetAsLastSibling();
        flyer.from = from;
        flyer.to = to;
        // A curve rather than a straight line, to the side of the way it travels.
        Vector2 across = new Vector2(-(to - from).y, (to - from).x).normalized;
        flyer.arc = across * Mathf.Min((to - from).magnitude * 0.22f, 220f) * (from.x < to.x ? 1f : -1f);
        flyer.age = 0f;
        flyer.life = life;
        flyer.startSize = startSize;
        flyer.endSize = endSize;
        flyer.countsWhenLanded = counts;
        flyer.spark = spark;
        flyers.Add(flyer);
        PlaceFlyer(flyer, 0f);
    }

    private void UpdateFlyers()
    {
        for (int i = flyers.Count - 1; i >= 0; i--)
        {
            Flyer flyer = flyers[i];
            flyer.age += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(flyer.age / flyer.life);
            PlaceFlyer(flyer, t);
            if (t < 1f) continue;
            flyer.rect.gameObject.SetActive(false);
            flyers.RemoveAt(i);
            pool.Add(flyer);
            if (flyer.countsWhenLanded) StarLanded();
        }
    }

    private static void PlaceFlyer(Flyer flyer, float t)
    {
        float eased = flyer.spark ? 1f - (1f - t) * (1f - t) : t * t * (3f - 2f * t);
        Vector2 position = Vector2.Lerp(flyer.from, flyer.to, eased) + flyer.arc * Mathf.Sin(t * Mathf.PI);
        flyer.rect.anchoredPosition = position;
        float size = Mathf.Lerp(flyer.startSize, flyer.endSize, eased);
        flyer.rect.sizeDelta = new Vector2(size, size);
        flyer.rect.localRotation = Quaternion.Euler(0f, 0f, t * (flyer.spark ? 200f : 360f));
        Color color = flyer.image.color;
        color.a = flyer.spark ? 1f - t : 1f;
        flyer.image.color = color;
    }

    private void StarLanded()
    {
        shownCount = Mathf.Min(shownCount + 1, Mathf.Max(CurrentCount(), shownCount + 1));
        if (total > 0) shownCount = Mathf.Min(shownCount, total);
        RefreshCount();
        plusTime = 0f;
        plusOne.gameObject.SetActive(true);

        bool last = total > 0 && shownCount >= total && !finished;
        if (!last)
        {
            bounceTime = 0f;
            bounceLength = 0.3f;
            bounceSize = 0.25f;
            return;
        }

        // The last one counts for more, and leads into the door opening.
        finished = true;
        bounceTime = 0f;
        bounceLength = 0.55f;
        bounceSize = 0.5f;
        glowTime = 0f;
        Vector2 center = BadgeIconPosition();
        for (int i = 0; i < 10; i++)
        {
            float angle = i * Mathf.PI * 2f / 10f;
            Launch(center, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(90f, 170f),
                Random.Range(0.35f, 0.55f), 26f, 8f, false, true);
        }
        FxSound.Play(FxClip.Chime, FxView.Listener, 0.9f, 0f);
        doorMomentAt = Time.unscaledTime + 0.45f;
    }

    private void DoorMoment()
    {
        GameObject door = room != null ? room.Door : corner != null ? corner.door : null;
        if (door == null) return;
        Vector3 center = door.transform.position;
        Renderer[] renderers = door.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer part in renderers) bounds.Encapsulate(part.bounds);
            center = bounds.center;
        }
        FxSound.Play(FxClip.Whoosh, center, 0.7f);
        Vector3 listener = FxView.Listener;
        Vector3 toward = (listener - center).sqrMagnitude > 0.01f ? (listener - center).normalized : Vector3.forward;
        Quaternion facing = Quaternion.LookRotation(-toward, Vector3.up);
        Color glow = new Color(1f, 0.85f, 0.35f, 0.75f);
        for (int i = 0; i < 3; i++)
            FxRunner.Spawn(FxArt.Get(FxShape.Soft), center + toward * (0.3f + i * 0.05f), facing, Vector3.zero, 1f + i * 0.4f, 1.6f + i, 3.4f + i, glow, FxLayer.Top, fadeIn: true);
    }

    private void UpdateStarBadgeAnimation()
    {
        float dt = Time.unscaledDeltaTime;
        if (bounceTime < 1f)
        {
            bounceTime = Mathf.Min(1f, bounceTime + dt / bounceLength);
            // 1 -> 1.25 -> 0.95 -> 1.
            float t = bounceTime;
            float scale = t < 0.4f ? Mathf.Lerp(1f, 1f + bounceSize, t / 0.4f)
                : t < 0.75f ? Mathf.Lerp(1f + bounceSize, 0.95f, (t - 0.4f) / 0.35f)
                : Mathf.Lerp(0.95f, 1f, (t - 0.75f) / 0.25f);
            starBadge.localScale = Vector3.one * scale;
        }
        if (plusTime < 1f)
        {
            plusTime = Mathf.Min(1f, plusTime + dt / 0.75f);
            plusOne.rectTransform.anchoredPosition = plusRest + Vector2.up * (plusTime * 34f);
            plusOne.alpha = plusTime < 0.5f ? 1f : 1f - (plusTime - 0.5f) / 0.5f;
            if (plusTime >= 1f) plusOne.gameObject.SetActive(false);
        }
        if (glowTime < 1f)
        {
            glowTime = Mathf.Min(1f, glowTime + dt / 1.2f);
            starGlow.enabled = glowTime < 1f;
            Color color = starGlow.color;
            color.a = Mathf.Sin(glowTime * Mathf.PI) * 0.95f;
            starGlow.color = color;
        }
    }

    // --- Tool toast (replaces the permanent "PEN | DRAW MODE" text) --------------------

    private void UpdateToolToast()
    {
        if (drawing == null) drawing = FindAnyObjectByType<HaruDrawingController>(FindObjectsInactive.Include);
        bool haruView = view != null && view.IsHaruGameplayView;
        if (drawing != null && haruView)
        {
            if (!haveTool)
            {
                haveTool = true;
                lastTool = drawing.CurrentTool;
                lastLock = drawing.CameraLock;
            }
            else if (drawing.CurrentTool != lastTool)
            {
                lastTool = drawing.CurrentTool;
                bool eraser = lastTool == DrawingTool.Eraser;
                ShowToast(eraser ? eraserIcon : crayonIcon, eraser ? "지우개" : "크레파스");
            }
            else if (drawing.CameraLock != lastLock)
            {
                lastLock = drawing.CameraLock;
                ShowToast(lastTool == DrawingTool.Eraser ? eraserIcon : crayonIcon,
                    lastLock ? "그리기 모드 켜짐" : "그리기 모드 꺼짐");
            }
        }
        float remaining = toastUntil - Time.unscaledTime;
        toolToast.alpha = haruView ? Mathf.Clamp01(remaining / 0.2f) : 0f;
    }

    private void ShowToast(Sprite icon, string label)
    {
        toolIcon.sprite = icon;
        toolIcon.preserveAspect = true;
        toolLabel.text = label;
        toastUntil = Time.unscaledTime + toastSeconds;
    }
}
