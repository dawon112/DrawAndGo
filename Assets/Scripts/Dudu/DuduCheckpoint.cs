using UnityEngine;

// Place on a Dudu surface with a trigger collider. When Dudu passes through, his
// next respawn happens at the point where he entered instead of the stage start.
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class DuduCheckpoint : MonoBehaviour
{
    [Tooltip("Optional object shown once this checkpoint is reached (a flag, a glow).")]
    [SerializeField] private GameObject reachedVisual;

    public bool Reached { get; private set; }

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
        if (reachedVisual != null) reachedVisual.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        DuduSurfaceMovement dudu = other.GetComponentInParent<DuduSurfaceMovement>();
        // Online, the host's Dudu is a disabled pose copy; only the controlling
        // client simulates death and respawn, so only it records the checkpoint.
        if (dudu == null || !dudu.isActiveAndEnabled || !dudu.SetCheckpoint()) return;
        Reached = true;
        if (reachedVisual != null) reachedVisual.SetActive(true);
    }
}
