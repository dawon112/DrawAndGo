using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class DuduCameraController : MonoBehaviour
{
    [SerializeField] private DuduSurface targetSurface;
    [SerializeField, Min(0.1f)] private float cameraDistance = 10f;
    [SerializeField, Min(0.1f)] private float orthographicSize = 3.5f;

    public void SetSurface(DuduSurface surface)
    {
        targetSurface = surface;
        AlignToSurface();
    }

    private void Awake()
    {
        Camera cameraComponent = GetComponent<Camera>();
        cameraComponent.orthographic = true;
        cameraComponent.orthographicSize = orthographicSize;
    }

    private void LateUpdate()
    {
        AlignToSurface();
    }

    private void AlignToSurface()
    {
        if (targetSurface == null)
            return;

        transform.position = targetSurface.transform.position + targetSurface.Normal * cameraDistance;
        transform.rotation = Quaternion.LookRotation(-targetSurface.Normal, targetSurface.Up);
    }
}
