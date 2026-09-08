using UnityEngine;

// Owns only the prototype's three coins, door and finish state.
public sealed class CornerRoomLevel : MonoBehaviour
{
    public DuduSurface[] surfaces;
    public GameObject[] coins;
    public GameObject door;
    public DuduSurfaceMovement player;
    public Player3DMovement player3D;
    public ThreeDimensionalGoal goal3D;
    public TextMesh goalLabel;
    public bool IsOpen { get; private set; }
    public bool IsClear { get; private set; }
    public int CollectedCount { get; private set; }

    private void Awake()
    {
        ApplyRequestedLayout();
        RemoveGuideLabels();
    }

    private void RemoveGuideLabels()
    {
        foreach (TextMesh label in GetComponentsInChildren<TextMesh>(true))
        {
            string text = label.text.TrimStart();
            if (text.StartsWith("START") || text.StartsWith("SECTION"))
            {
                label.gameObject.SetActive(false);
                Destroy(label.gameObject);
            }
        }
    }

    private void Start()
    {
        // Paper and doorway wall pieces stay solid for Haru, while Dudu moves on their plane.
        Collider playerCollider = player.GetComponent<Collider>();
        foreach (DrawingSurface drawingSurface in FindObjectsByType<DrawingSurface>())
        {
            Collider paperCollider = drawingSurface.GetComponent<Collider>();
            if (paperCollider != null)
                Physics.IgnoreCollision(playerCollider, paperCollider);
        }
    }

    private void ApplyRequestedLayout()
    {
        if (coins != null)
            foreach (GameObject coin in coins)
                if (coin != null)
                    coin.transform.localScale = Vector3.one * 0.96f;

        if (surfaces == null || surfaces.Length < 4 || surfaces[3] == null)
            return;
        Transform original = transform.Find("Ground - Section 4");
        if (original == null || transform.Find("Ground - Section 4 Left") != null)
            return;

        const float gapWidth = 8f;
        DuduSurface surface = surfaces[3];
        float sectionWidth = (surface.Width - gapWidth) * 0.5f;
        float centerOffset = (gapWidth + sectionWidth) * 0.5f;
        CreateSegment("Ground - Section 4 Left", -centerOffset);
        CreateSegment("Ground - Section 4 Right", centerOffset);
        original.gameObject.SetActive(false);
        Destroy(original.gameObject);

        void CreateSegment(string name, float x)
        {
            GameObject segment = Instantiate(original.gameObject, transform);
            segment.name = name;
            segment.transform.SetPositionAndRotation(
                surface.SurfaceToWorld(new Vector2(x, -2.4f)), surface.transform.rotation);
            segment.transform.localScale = new Vector3(sectionWidth, 0.14f, 0.16f);
        }
    }

    private void Update()
    {
        if (coins == null || coins.Length != 3 || surfaces == null || surfaces.Length != 5)
            return;
        CollectedCount = 0;
        foreach (GameObject coin in coins)
            if (coin == null || !coin.activeSelf) CollectedCount++;
        if (!IsOpen && goalLabel != null)
            goalLabel.text = $"COINS {CollectedCount} / 3";
        if (!IsOpen && CollectedCount == 3)
        {
            IsOpen = true;
            door.SetActive(false);
            if (goalLabel != null)
                goalLabel.text = "ALL COINS!\nRETURN TO 3D";
            GameViewManager viewManager = FindAnyObjectByType<GameViewManager>();
            if (viewManager != null) viewManager.PlayAllCoinsCollectedSequence();
            Debug.Log("All 3 coins collected: door opened.", this);
        }
    }

    public void TryCompleteThreeDimensionalGoal(Player3DMovement candidate)
    {
        if (!IsClear && IsOpen && candidate != null && candidate == player3D)
        {
            IsClear = true;
            GameViewManager viewManager = FindAnyObjectByType<GameViewManager>();
            if (viewManager != null) viewManager.HideExitInstruction();
            Debug.Log("Clear: the 3D player passed through the opened wall.", this);
        }
    }
}

