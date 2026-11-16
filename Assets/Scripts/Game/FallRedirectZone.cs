using UnityEngine;

// A stretch of air under a floor hole that has no room below it: whatever falls in here is
// slid sideways into the room underneath, mid-fall, so it lands inside instead of outside.
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class FallRedirectZone : MonoBehaviour
{
    [Tooltip("World offset applied to Haru or a crate that falls into the zone.")]
    public Vector3 shift = new Vector3(-3f, 0f, 0f);

    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        CharacterController controller = other.GetComponentInParent<CharacterController>();
        if (controller != null && controller.GetComponent<Player3DMovement>() != null)
        {
            controller.enabled = false;
            controller.transform.position += shift;
            controller.enabled = true;
            return;
        }
        PushableBox box = other.GetComponentInParent<PushableBox>();
        if (box != null && !box.Body.isKinematic) box.Body.position += shift;
    }
}
