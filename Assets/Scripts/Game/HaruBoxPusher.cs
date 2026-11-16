using UnityEngine;

// Lives on Haru: a CharacterController only reports what it bumps into on its own object.
[DisallowMultipleComponent]
public sealed class HaruBoxPusher : MonoBehaviour
{
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // Standing on a crate is not pushing it.
        if (hit.moveDirection.y < -0.5f) return;
        PushableBox box = hit.collider != null ? hit.collider.GetComponentInParent<PushableBox>() : null;
        if (box != null) box.Push(hit.moveDirection);
    }
}
