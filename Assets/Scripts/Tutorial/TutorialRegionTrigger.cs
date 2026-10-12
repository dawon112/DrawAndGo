using UnityEngine;

public sealed class TutorialRegionTrigger : MonoBehaviour
{
    private TutorialSequenceController owner;
    private bool isExit;
    private bool isErase;
    public void Configure(TutorialSequenceController controller, bool exit, bool erase = false)
    { owner = controller; isExit = exit; isErase = erase; }
    private void OnTriggerEnter(Collider other) => Check(other);
    private void OnTriggerStay(Collider other) => Check(other);
    private void Check(Collider other)
    {
        DuduSurfaceMovement player = other.GetComponentInParent<DuduSurfaceMovement>();
        if (owner == null || player == null) return;
        if (isErase) owner.EnterEraseRegion(isExit, player);
        else owner.EnterRegion(isExit, player);
    }
}
