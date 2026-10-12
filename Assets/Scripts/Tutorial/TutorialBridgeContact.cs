using UnityEngine;

public sealed class TutorialBridgeContact : MonoBehaviour
{
    public TutorialSequenceController Owner { get; set; }
    private void OnCollisionEnter(Collision collision)
    {
        if (Owner != null) Owner.RecordBridgeContact(collision);
    }
    private void OnCollisionStay(Collision collision)
    {
        if (Owner != null) Owner.RecordBridgeContact(collision);
    }
}
