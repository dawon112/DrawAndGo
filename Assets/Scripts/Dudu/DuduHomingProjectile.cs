using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public sealed class DuduHomingProjectile : MonoBehaviour
{
    private DuduSurface surface;
    private DuduSurfaceMovement target;
    private Rigidbody body;
    private Vector3 movementDirection;
    private float moveSpeed;
    private float turnSpeedRadians;
    private float remainingLifetime;

    public void Configure(
        DuduSurface targetSurface,
        DuduSurfaceMovement targetDudu,
        float speed,
        float turnSpeedDegrees,
        float lifetime)
    {
        surface = targetSurface;
        target = targetDudu;
        moveSpeed = Mathf.Max(0f, speed);
        turnSpeedRadians = Mathf.Max(0f, turnSpeedDegrees) * Mathf.Deg2Rad;
        remainingLifetime = Mathf.Max(0.1f, lifetime);
        movementDirection = GetDirectionToTarget();
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.FreezeRotation;

        Collider projectileCollider = GetComponent<Collider>();
        projectileCollider.isTrigger = true;
    }

    private void FixedUpdate()
    {
        if (surface == null || target == null || body == null || !target.InputEnabled)
        {
            Destroy(gameObject);
            return;
        }

        remainingLifetime -= Time.fixedDeltaTime;
        if (remainingLifetime <= 0f || IsOutsideSurface())
        {
            Destroy(gameObject);
            return;
        }

        Vector3 desiredDirection = GetDirectionToTarget();
        if (desiredDirection.sqrMagnitude > 0.0001f)
        {
            movementDirection = Vector3.RotateTowards(
                movementDirection,
                desiredDirection,
                turnSpeedRadians * Time.fixedDeltaTime,
                0f).normalized;
        }

        body.MovePosition(body.position + movementDirection * moveSpeed * Time.fixedDeltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        DuduSurfaceMovement dudu = other.GetComponentInParent<DuduSurfaceMovement>();
        if (dudu == null || dudu != target)
            return;

        dudu.ShowHitFeedback();
        Destroy(gameObject);
    }

    private Vector3 GetDirectionToTarget()
    {
        if (surface == null || target == null)
            return Vector3.right;

        Vector3 direction = target.transform.position - transform.position;
        Vector3 normal = surface.Normal.normalized;
        direction -= normal * Vector3.Dot(direction, normal);
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : surface.Right.normalized;
    }

    private bool IsOutsideSurface()
    {
        Vector2 position = surface.WorldToSurface(body.position);
        const float boundaryMargin = 1f;
        return Mathf.Abs(position.x) > surface.Width * 0.5f + boundaryMargin ||
               Mathf.Abs(position.y) > surface.Height * 0.5f + boundaryMargin;
    }
}
