using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public sealed class DuduMovingObstacle : MonoBehaviour
{
    public enum MovementAxis
    {
        Horizontal,
        Vertical
    }

    [Header("References")]
    [SerializeField] private DuduSurface surface;

    [Header("Movement")]
    [SerializeField] private MovementAxis movementAxis = MovementAxis.Vertical;
    [Tooltip("Center of the obstacle movement path on the Dudu surface.")]
    [SerializeField] private Vector2 centerPosition;
    [Tooltip("Total distance between the two ends of the movement path.")]
    [SerializeField, Min(0f)] private float travelDistance = 4f;
    [Tooltip("Seconds required to travel to the other end and return. Lower values move faster.")]
    [SerializeField, Min(0.1f)] private float roundTripDuration = 3f;
    [Tooltip("Delay before the obstacle starts moving.")]
    [SerializeField, Min(0f)] private float startDelay;

    [Header("Dudu Bounce")]
    [Tooltip("Horizontal speed applied to Dudu when hit.")]
    [SerializeField, Min(0f)] private float horizontalBounceSpeed = 6f;
    [Tooltip("Upward speed applied to Dudu when hit.")]
    [SerializeField, Min(0f)] private float upwardBounceSpeed = 5f;
    [Tooltip("How long the bounce overrides horizontal player input.")]
    [SerializeField, Min(0f)] private float bounceControlDuration = 0.35f;

    private Rigidbody body;
    private float elapsedTime;

    public void Configure(
        DuduSurface targetSurface,
        MovementAxis axis,
        Vector2 movementCenter,
        float distance,
        float duration,
        float delay = 0f)
    {
        surface = targetSurface;
        movementAxis = axis;
        centerPosition = movementCenter;
        travelDistance = Mathf.Max(0f, distance);
        roundTripDuration = Mathf.Max(0.1f, duration);
        startDelay = Mathf.Max(0f, delay);
        ApplyPosition(GetPositionAtProgress(0f));
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void FixedUpdate()
    {
        if (surface == null || body == null)
            return;

        elapsedTime += Time.fixedDeltaTime;
        float movementTime = Mathf.Max(0f, elapsedTime - startDelay);
        float halfTripDuration = roundTripDuration * 0.5f;
        float progress = Mathf.PingPong(movementTime / halfTripDuration, 1f);
        Vector3 targetPosition = surface.SurfaceToWorld(GetPositionAtProgress(progress));

        body.MovePosition(targetPosition);
        body.MoveRotation(surface.transform.rotation);
    }

    private void OnCollisionEnter(Collision collision)
    {
        DuduSurfaceMovement dudu = collision.gameObject.GetComponentInParent<DuduSurfaceMovement>();
        if (dudu == null)
            return;

        dudu.BounceAwayFrom(
            transform.position,
            horizontalBounceSpeed,
            upwardBounceSpeed,
            bounceControlDuration);
    }

    private Vector2 GetPositionAtProgress(float progress)
    {
        float offset = Mathf.Lerp(-travelDistance * 0.5f, travelDistance * 0.5f, progress);
        Vector2 direction = movementAxis == MovementAxis.Horizontal ? Vector2.right : Vector2.up;
        return centerPosition + direction * offset;
    }

    private void ApplyPosition(Vector2 position)
    {
        if (surface == null)
            return;

        transform.SetPositionAndRotation(
            surface.SurfaceToWorld(position),
            surface.transform.rotation);
    }

    private void OnValidate()
    {
        travelDistance = Mathf.Max(0f, travelDistance);
        roundTripDuration = Mathf.Max(0.1f, roundTripDuration);
        startDelay = Mathf.Max(0f, startDelay);
        horizontalBounceSpeed = Mathf.Max(0f, horizontalBounceSpeed);
        upwardBounceSpeed = Mathf.Max(0f, upwardBounceSpeed);
        bounceControlDuration = Mathf.Max(0f, bounceControlDuration);

        if (!Application.isPlaying)
            ApplyPosition(GetPositionAtProgress(0f));
    }
}
