using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class TutorialStarCollectible : MonoBehaviour
{
    private const float VisualScale = 0.07f;
    // Deep across the wall (local z): Dudu walks on a thin plane right at the wall, and a
    // star placed by hand a little off its wall (or left behind when a wall is moved)
    // must still be within reach.
    private static readonly Vector3 PickupWorldSize = new Vector3(0.7f, 0.9f, 1.5f);
    private bool collected;

    private void Awake()
    {
        transform.localScale = Vector3.one * VisualScale;
        gameObject.AddComponent<StarShineFx>();
        Collider trigger = GetComponent<Collider>();
        trigger.isTrigger = true;
        Vector3 scale = transform.lossyScale;
        if (trigger is BoxCollider box)
        {
            box.size = new Vector3(
                PickupWorldSize.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                PickupWorldSize.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                PickupWorldSize.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryCollect(other);
    }

    private void TryCollect(Collider other)
    {
        if (collected || other.GetComponentInParent<DuduSurfaceMovement>() == null)
            return;

        TutorialRoomLevel level = GetComponentInParent<TutorialRoomLevel>();
        if (level == null)
            level = FindAnyObjectByType<TutorialRoomLevel>();
        if (level == null)
            return;

        TutorialSequenceController sequence = FindAnyObjectByType<TutorialSequenceController>();
        if (sequence != null && !sequence.CanCollectStar(this))
            return;
        if (!level.CollectStar(this)) return;
        collected = true;
        gameObject.SetActive(false);
    }
}
