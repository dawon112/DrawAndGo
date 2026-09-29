using UnityEngine;
using UnityEngine.UI;

// Scene-local tutorial progression. It deliberately does not touch the prototype coin flow.
public sealed class TutorialRoomLevel : MonoBehaviour
{
    private const float DeathLineSurfaceY = -2.4f;
    [SerializeField] private DuduSurface[] sections;
    [SerializeField] private TutorialStarCollectible[] stars;
    [SerializeField] private GameObject lockedDoor;
    [SerializeField] private DuduSurfaceMovement dudu;

    private Text starCounter;
    private int collectedStars;
    private bool doorOpened;
    private bool doorSequenceStarted;

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

    public void CollectStar(TutorialStarCollectible star)
    {
        if (star == null || doorOpened || doorSequenceStarted)
            return;

        collectedStars = Mathf.Min(stars != null ? stars.Length : 3, collectedStars + 1);
        UpdateStarCounter();
        if (collectedStars >= 3)
            BeginDoorOpeningSequence();
    }

    private void BeginDoorOpeningSequence()
    {
        if (doorSequenceStarted)
            return;

        doorSequenceStarted = true;
        GameViewManager viewManager = FindAnyObjectByType<GameViewManager>();
        if (viewManager != null)
            viewManager.PlayAllStarsCollectedSequence(this);
        else
            OpenDoorForCinematic();
    }

    public void OpenDoorForCinematic()
    {
        if (doorOpened)
            return;

        doorOpened = true;
        if (lockedDoor != null)
            lockedDoor.SetActive(false);
        Debug.Log("Tutorial: all 3 stars collected; door opened once.", this);
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
}
