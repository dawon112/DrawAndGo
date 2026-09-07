using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class ThreeDimensionalGoal : MonoBehaviour
{
    [SerializeField] private CornerRoomLevel level;

    public void Configure(CornerRoomLevel targetLevel)
    {
        level = targetLevel;
        GetComponent<Collider>().isTrigger = true;
    }

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        Player3DMovement player = other.GetComponentInParent<Player3DMovement>();
        if (player != null && level != null)
            level.TryCompleteThreeDimensionalGoal(player);
    }
}
