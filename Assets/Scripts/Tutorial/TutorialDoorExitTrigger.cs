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

    private void OnTriggerStay(Collider other)
    {
        Player3DMovement player = other.GetComponentInParent<Player3DMovement>();
        if (player == trackedPlayer && enteredFromInside &&
            Vector3.Dot(player.transform.position - transform.position, transform.forward) > -0.25f)
        {
            enteredFromInside = false;
            if (owner != null) owner.TryCompleteAtDoor(player);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<Player3DMovement>() != trackedPlayer) return;
        trackedPlayer = null;
        enteredFromInside = false;
    }
}
