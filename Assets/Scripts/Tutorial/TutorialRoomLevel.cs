using System.Collections;
using UnityEngine;

// Scene-local tutorial progression. It deliberately does not touch the prototype coin flow.
public sealed class TutorialRoomLevel : MonoBehaviour
{
    private const int RequiredStars = 3;
    // The authored 3D floor can catch Dudu just above the surface bottom.
    private const float DeathLineSurfaceY = -2.1f;
    [SerializeField] private DuduSurface[] sections;
    [SerializeField] private TutorialStarCollectible[] stars;
    [SerializeField] private GameObject lockedDoor;
    [SerializeField] private DuduSurfaceMovement dudu;

    private bool starCounterVisible;
    private int collectedStars;
    // Which stars are taken, one bit per entry of stars.
    private int collectedMask;
    private bool doorOpened;
    private bool doorSequenceStarted;
    public event System.Action<int> StarCollected;

    public int CollectedStars => collectedStars;
    public int CollectedStarMask => collectedMask;
    public bool DoorOpened => doorOpened;
    // GameHud shows the count while this is on.
    public bool IsStarCounterVisible => starCounterVisible;
    public TutorialStarCollectible[] Stars => stars;
    public int RequiredStarCount => RequiredStars;
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
        collectedMask = 0;
        doorOpened = false;
        doorSequenceStarted = false;
    }

    private void Awake()
    {
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
        int index = stars != null ? System.Array.IndexOf(stars, star) : -1;
        if (star == null || doorOpened || doorSequenceStarted || stars == null ||
            stars.Length != RequiredStars || index < 0 || (collectedMask & (1 << index)) != 0)
            return false;

        collectedMask |= 1 << index;
        collectedStars = Mathf.Min(RequiredStars, collectedStars + 1);
        Debug.Log($"Tutorial: {star.name} collected ({collectedStars}/{RequiredStars}).", star);
        if (collectedStars >= RequiredStars)
            BeginDoorOpeningSequence();
        StarCollected?.Invoke(collectedStars);
        return true;
    }

    // Online client: the host decides pickups. Stars can be taken in any order, so the
    // host sends which ones (one bit per star) and the same stars are hidden here.
    public void ApplyNetworkStars(int mask, bool doorIsOpen)
    {
        if (stars == null) return;
        mask &= (1 << stars.Length) - 1;
        if (mask != collectedMask)
        {
            int count = 0;
            for (int i = 0; i < stars.Length; i++) if ((mask & (1 << i)) != 0) count++;
            bool gained = count > collectedStars;
            collectedMask = mask;
            collectedStars = count;
            for (int i = 0; i < stars.Length; i++)
                if (stars[i] != null) stars[i].gameObject.SetActive((mask & (1 << i)) == 0);
            if (gained) StarCollected?.Invoke(collectedStars);
        }
        if (doorIsOpen && !doorOpened)
        {
            doorSequenceStarted = true;
            OpenDoorForCinematic();
        }
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
            Transform visual = lockedDoor.transform.Find("Free Wood Door - Door 1 Brown");
            Transform leaf = visual != null ? visual.Find("Door") : null;
            if (leaf != null) leaf.gameObject.SetActive(false);
            else Debug.LogWarning("Tutorial: door leaf was not found under the authored door prefab.", lockedDoor);
        }
        Debug.Log("Tutorial: all 3 stars collected; door opened once.", this);
    }

    public void ShowStarCounter(bool visible) => starCounterVisible = visible;
}
