using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public sealed class DuduHomingProjectile : MonoBehaviour
{
    private DuduSurface surface;
    private DuduSurfaceMovement target;
    private Rigidbody body;
    private SphereCollider projectileSphere;
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
        projectileSphere = projectileCollider as SphereCollider;
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

        Vector3 displacement = movementDirection * moveSpeed * Time.fixedDeltaTime;
        if (TouchesDrawnLineAlongMove(displacement))
        {
            Destroy(gameObject);
            return;
        }

        body.MovePosition(body.position + displacement);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<DrawingStroke>() != null)
        {
            Destroy(gameObject);
            return;
        }

        DuduSurfaceMovement dudu = other.GetComponentInParent<DuduSurfaceMovement>();
        if (dudu == null || dudu != target)
            return;

        dudu.ShowHitFeedback();
        Destroy(gameObject);
    }

    private bool TouchesDrawnLineAlongMove(Vector3 displacement)
    {
        if (projectileSphere == null)
            return false;

        // Match the actual sphere collider, including its non-uniform world scale.
        Vector3 scale = transform.lossyScale;
        float radius = projectileSphere.radius * Mathf.Max(
            Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Vector3 center = body.position + body.rotation * Vector3.Scale(projectileSphere.center, scale);

        // Trigger CCD alone cannot guarantee a hit across a thin stroke in one step.
        // Include initial overlaps (e.g. a line drawn over a stationary missile).
        foreach (Collider other in Physics.OverlapSphere(center, radius, Physics.AllLayers, QueryTriggerInteraction.Collide))
        {
            if (other.GetComponentInParent<DrawingStroke>() != null)
                return true;
        }

        float distance = displacement.magnitude;
        if (distance <= Mathf.Epsilon)
            return false;

        foreach (RaycastHit hit in Physics.SphereCastAll(
            center, radius, displacement / distance, distance, Physics.AllLayers, QueryTriggerInteraction.Collide))
        {
            if (hit.collider.GetComponentInParent<DrawingStroke>() != null)
                return true;
        }

        return false;
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
