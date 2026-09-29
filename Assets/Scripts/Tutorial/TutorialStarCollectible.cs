using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class TutorialStarCollectible : MonoBehaviour
{
    private const float VisualScale = 0.07f;
    private static readonly Vector3 PickupWorldSize = new Vector3(0.7f, 0.9f, 0.3f);
    private bool collected;

    private void Awake()
    {
        transform.localScale = Vector3.one * VisualScale;
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

        collected = true;
        level.CollectStar(this);
        gameObject.SetActive(false);
    }
}
