using UnityEngine;

public sealed class CameraFollow2D : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.18f;
    [SerializeField] private float fixedY = 0f;

    private float horizontalVelocity;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        float x = Mathf.SmoothDamp(transform.position.x, target.position.x, ref horizontalVelocity, smoothTime);
        transform.position = new Vector3(x, fixedY, transform.position.z);
    }
}
