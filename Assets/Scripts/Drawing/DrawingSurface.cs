using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class DrawingSurface : MonoBehaviour
{
    [SerializeField] private Transform strokeRoot;

    private DuduSurface duduSurface;

    public Transform StrokeRoot => strokeRoot;
    public Vector3 Normal => duduSurface != null ? duduSurface.Normal : -transform.forward;

    private void Awake()
    {
        duduSurface = GetComponent<DuduSurface>();
    }

    public void SetStrokeRoot(Transform newStrokeRoot)
    {
        strokeRoot = newStrokeRoot;
    }
}
