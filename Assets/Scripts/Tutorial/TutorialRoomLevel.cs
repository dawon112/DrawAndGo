using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Scene-local tutorial progression. It deliberately does not touch the prototype coin flow.
public sealed class TutorialRoomLevel : MonoBehaviour
{
    private const int RequiredStars = 3;
    private const float DeathLineSurfaceY = -2.4f;
    [SerializeField] private DuduSurface[] sections;
    [SerializeField] private TutorialStarCollectible[] stars;
    [SerializeField] private GameObject lockedDoor;
    [SerializeField] private DuduSurfaceMovement dudu;

    private Text starCounter;
    private int collectedStars;
    private bool doorOpened;
    private bool doorSequenceStarted;
    public event System.Action<int> StarCollected;

    public int CollectedStars => collectedStars;
    public bool DoorOpened => doorOpened;
    public DuduSurface[] Sections => sections;
    public GameObject Door => lockedDoor;
    public DuduSurface FinalSurface => sections != null && sections.Length > 0 ? sections[sections.Length - 1] : null;

    public void Configure(
        DuduSurface[] tutorialSections,
        TutorialStarCollectible[] tutorialStars,
        GameObject door,
        DuduSurfaceMovement player)
    {
        sections = tutorialSections;
        stars = tutorialStars;
        lockedDoor = door;
        dudu = player;
        collectedStars = 0;
        doorOpened = false;
        doorSequenceStarted = false;
        UpdateStarCounter();
    }

    private void Awake()
    {
        BuildStarCounter();
        UpdateStarCounter();
        if (lockedDoor != null)
            lockedDoor.SetActive(true);
    }

    private void Start()
    {
        if (dudu == null)
            dudu = FindAnyObjectByType<DuduSurfaceMovement>();
        if (dudu == null)
            return;

        Collider playerCollider = dudu.GetComponent<Collider>();
        foreach (DrawingSurface drawingSurface in FindObjectsByType<DrawingSurface>())
        {
            Collider wallCollider = drawingSurface.GetComponent<Collider>();
            if (wallCollider != null)
                Physics.IgnoreCollision(playerCollider, wallCollider);
        }
    }

    private void FixedUpdate()
    {
        if (dudu == null || dudu.CurrentSurface == null)
            return;

        Vector2 position = dudu.CurrentSurface.WorldToSurface(dudu.transform.position);
        if (position.y < DeathLineSurfaceY)
            dudu.Die();
    }

    public bool CollectStar(TutorialStarCollectible star)
    {
        if (star == null || doorOpened || doorSequenceStarted || stars == null ||
            stars.Length != RequiredStars || System.Array.IndexOf(stars, star) < 0)
            return false;

        collectedStars = Mathf.Min(RequiredStars, collectedStars + 1);
        Debug.Log($"Tutorial: {star.name} collected ({collectedStars}/{RequiredStars}).", star);
        UpdateStarCounter();
        if (collectedStars >= RequiredStars)
            BeginDoorOpeningSequence();
        StarCollected?.Invoke(collectedStars);
        return true;
    }

    private void BeginDoorOpeningSequence()
    {
        if (doorSequenceStarted)
            return;

        doorSequenceStarted = true;
        OpenDoorForCinematic();
    }

    public void OpenDoorForCinematic()
    {
        if (doorOpened)
            return;

        doorOpened = true;
        if (lockedDoor != null)
        {
            Collider doorCollider = lockedDoor.GetComponent<Collider>();
            if (doorCollider != null) doorCollider.enabled = false;
            StartCoroutine(SwingDoorOpen(lockedDoor.transform));
        }
        Debug.Log("Tutorial: all 3 stars collected; door opened once.", this);
    }

    private IEnumerator SwingDoorOpen(Transform door)
    {
        Quaternion closed = door.rotation;
        Quaternion opened = Quaternion.AngleAxis(70f, Vector3.up) * closed;
        for (float elapsed = 0f; elapsed < 0.8f; elapsed += Time.deltaTime)
        {
            door.rotation = Quaternion.Slerp(closed, opened, Mathf.SmoothStep(0f, 1f, elapsed / 0.8f));
            yield return null;
        }
        door.rotation = opened;
    }

    private void BuildStarCounter()
    {
        GameObject canvasObject = new GameObject("Star Counter Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject textObject = new GameObject("Star Counter", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
        textObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(36f, -32f);
        rect.sizeDelta = new Vector2(360f, 72f);

        starCounter = textObject.GetComponent<Text>();
        starCounter.font = GameFont.Bold;
        starCounter.fontSize = 40;
        starCounter.alignment = TextAnchor.MiddleLeft;
        starCounter.color = new Color(1f, 0.85f, 0.15f);
        starCounter.raycastTarget = false;
        Outline outline = textObject.GetComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2f, -2f);
    }

    private void UpdateStarCounter()
    {
        if (starCounter != null)
            starCounter.text = $"★ {collectedStars} / 3";
    }

    public void ShowStarCounter(bool visible)
    {
        if (starCounter != null) starCounter.transform.parent.gameObject.SetActive(visible);
    }

    public void PulseStarCounter()
    {
        if (starCounter != null) StartCoroutine(PulseCounter());
    }

    private System.Collections.IEnumerator PulseCounter()
    {
        RectTransform rect = starCounter.GetComponent<RectTransform>();
        float elapsed = 0f;
        while (elapsed < 0.35f)
        {
            elapsed += Time.unscaledDeltaTime;
            rect.localScale = Vector3.one * (1f + 0.2f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(elapsed / 0.35f)));
            yield return null;
        }
        rect.localScale = Vector3.one;
    }
}
