using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class DuduCameraController : MonoBehaviour
{
    [SerializeField] private DuduSurface targetSurface;
    [SerializeField] private Transform targetDudu;
    [SerializeField, Min(0.1f)] private float cameraDistance = 10f;
    [SerializeField, Min(0.1f)] private float orthographicSize = 3.5f;
    [SerializeField, Min(0.01f)] private float followSmoothTime = 0.15f;
    [SerializeField] private float horizontalOffset = 1f;

    private Camera cameraComponent;
    private float horizontalVelocity;

    public void SetSurface(DuduSurface surface)
    {
        targetSurface = surface;
        AlignToSurface(true);
    }

    public void SetTarget(Transform target)
    {
        targetDudu = target;
        AlignToSurface(true);
    }

    private void Awake()
    {
        cameraComponent = GetComponent<Camera>();
        cameraComponent.orthographic = true;
        cameraComponent.orthographicSize = orthographicSize;
    }

    private void Start()
    {
        AlignToSurface(true);
    }

    private void LateUpdate()
    {
        AlignToSurface(false);
    }

    private void AlignToSurface(bool snap)
    {
        if (targetSurface == null)
            return;

        Vector3 surfaceRight = targetSurface.Right.normalized;
        float targetHorizontal = 0f;
        if (targetDudu != null)
        {
            targetHorizontal = Vector3.Dot(
                targetDudu.position - targetSurface.transform.position,
                surfaceRight) + horizontalOffset;
        }

        float aspect = cameraComponent != null ? cameraComponent.aspect : 1f;
        float viewHalfWidth = orthographicSize * aspect;
        float cameraLimit = Mathf.Max(0f, targetSurface.Width * 0.5f - viewHalfWidth);
        targetHorizontal = Mathf.Clamp(targetHorizontal, -cameraLimit, cameraLimit);

        float currentHorizontal = Vector3.Dot(
            transform.position - targetSurface.transform.position,
            surfaceRight);
        float followedHorizontal = snap
            ? targetHorizontal
            : Mathf.SmoothDamp(
                currentHorizontal,
                targetHorizontal,
                ref horizontalVelocity,
                followSmoothTime);

        transform.position =
            targetSurface.transform.position +
            surfaceRight * followedHorizontal +
            targetSurface.Normal * cameraDistance;
        transform.rotation = Quaternion.LookRotation(-targetSurface.Normal, targetSurface.Up);
    }
}
