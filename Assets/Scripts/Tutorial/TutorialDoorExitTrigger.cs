using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public sealed class TutorialDoorExitTrigger : MonoBehaviour
{
    private TutorialSequenceController owner;
    private Player3DMovement trackedPlayer;
    private bool enteredFromInside;

    public void Configure(TutorialSequenceController tutorial) => owner = tutorial;

    private void OnTriggerEnter(Collider other)
    {
        Player3DMovement player = other.GetComponentInParent<Player3DMovement>();
        if (player == null) return;
        trackedPlayer = player;
        enteredFromInside = Vector3.Dot(player.transform.position - transform.position, transform.forward) < -0.4f;
    }

    // The opened doorway is backed by the "Outside" panel, so Haru cannot walk past the
    // door plane: his centre stops about -0.95 from this trigger's centre. Stepping into
    // the doorway is the exit.
    private const float DoorwayDepth = -1.1f;

    private void OnTriggerStay(Collider other)
    {
        Player3DMovement player = other.GetComponentInParent<Player3DMovement>();
        // Keep asking while Haru stands in the doorway: arriving before the last star
        // (or before the closing dialogue) must not use up the exit.
        if (player == trackedPlayer && enteredFromInside && owner != null &&
            Vector3.Dot(player.transform.position - transform.position, transform.forward) > DoorwayDepth)
            owner.TryCompleteAtDoor(player);
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<Player3DMovement>() != trackedPlayer) return;
        trackedPlayer = null;
        enteredFromInside = false;
    }
}
