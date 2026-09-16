using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public sealed class MovingEraserObstacle : MonoBehaviour
{
    [Header("Auto-bound References")]
    [SerializeField] private DuduSurface surface;
    [SerializeField] private DuduSurfaceMovement target;

    [Header("Aim / Launch")]
    [SerializeField, Min(0.1f)] private float aimDuration = 3f;
    [SerializeField, Min(0f)] private float attackRange = 7f;
    [SerializeField, Min(0.1f)] private float moveSpeed = 5f;
    [SerializeField, Min(0.01f)] private float eraseRadius = 0.22f;
    [SerializeField, Min(0.01f)] private float visualOffset = 0.025f;

    private Rigidbody body;
    private Vector2 surfacePosition;
    private Vector2 launchDirection = Vector2.right;
    private float aimElapsed;
    private bool launched;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
        GetComponent<BoxCollider>().isTrigger = true;
        ResolveReferencesAndPlacement();
    }

    private void FixedUpdate()
    {
        if (surface == null || target == null || body == null)
            return;

        if (!launched)
        {
            if (!CanAimAtTarget())
            {
                aimElapsed = 0f;
                return;
            }

            Vector2 direction = surface.WorldToSurface(target.transform.position) - surfacePosition;
            if (direction.sqrMagnitude > 0.0001f)
                launchDirection = direction.normalized;
            body.MoveRotation(GetDirectionRotation(launchDirection));
            aimElapsed += Time.fixedDeltaTime;
            if (aimElapsed >= aimDuration)
                launched = true;
            return;
        }

        Vector2 nextSurfacePosition = surfacePosition + launchDirection * moveSpeed * Time.fixedDeltaTime;
        Vector3 nextWorldPosition = SurfaceToWorld(nextSurfacePosition);
        Vector3 displacement = nextWorldPosition - body.position;
        EraseAlongMove(body.position, nextWorldPosition);
        if (TouchesDuduAlongMove(displacement))
            target.Die();

        body.MoveRotation(GetDirectionRotation(launchDirection));
        body.MovePosition(nextWorldPosition);
        surfacePosition = nextSurfacePosition;
        if (IsOutsideSurface()) Destroy(gameObject);
    }

    private bool CanAimAtTarget()
    {
        if (!target.InputEnabled || target.CurrentSurface != surface)
            return false;
        Vector2 targetPosition = surface.WorldToSurface(target.transform.position);
        return Vector2.Distance(surfacePosition, targetPosition) <= attackRange;
    }

    private void EraseAlongMove(Vector3 start, Vector3 end)
    {
        float distance = Vector3.Distance(start, end);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(0.02f, eraseRadius * 0.5f)));
        for (int step = 0; step <= steps; step++)
        {
            Vector3 point = Vector3.Lerp(start, end, step / (float)steps);
            foreach (DrawingStroke stroke in FindObjectsByType<DrawingStroke>())
                if (stroke != null) stroke.Erase(point, eraseRadius);
        }
    }

    private bool TouchesDuduAlongMove(Vector3 displacement)
    {
        Vector2 start = surface.WorldToSurface(body.position);
        Vector2 end = surface.WorldToSurface(body.position + displacement);
        Vector2 dudu = surface.WorldToSurface(target.transform.position);
        Vector2 segment = end - start;
        float t = segment.sqrMagnitude > Mathf.Epsilon
            ? Mathf.Clamp01(Vector2.Dot(dudu - start, segment) / segment.sqrMagnitude)
            : 0f;
        return Vector2.Distance(start + segment * t, dudu) <= 0.7f;
    }

    private void ResolveReferencesAndPlacement()
    {
        Vector3 placedPosition = transform.position;
        surface = null;
        float nearest = float.PositiveInfinity;
        foreach (DuduSurface candidate in FindObjectsByType<DuduSurface>(FindObjectsInactive.Exclude))
        {
            Vector2 position = candidate.WorldToSurface(placedPosition);
            Vector2 clamped = new Vector2(
                Mathf.Clamp(position.x, -candidate.Width * 0.5f, candidate.Width * 0.5f),
                Mathf.Clamp(position.y, -candidate.Height * 0.5f, candidate.Height * 0.5f));
            float distance = (candidate.SurfaceToWorld(clamped) - placedPosition).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            surface = candidate;
        }

        target = FindAnyObjectByType<DuduSurfaceMovement>(FindObjectsInactive.Include);
        if (surface == null) return;
        surfacePosition = surface.WorldToSurface(placedPosition);
        transform.SetPositionAndRotation(SurfaceToWorld(surfacePosition), surface.transform.rotation);
    }

    private Vector3 SurfaceToWorld(Vector2 position)
    {
        return surface.transform.position + surface.Right.normalized * position.x +
            surface.Up.normalized * position.y + surface.Normal.normalized * visualOffset;
    }

    private Quaternion GetDirectionRotation(Vector2 direction)
    {
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        return surface.transform.rotation * Quaternion.Euler(0f, 0f, angle);
    }

    private bool IsOutsideSurface()
    {
        const float margin = 2f;
        return Mathf.Abs(surfacePosition.x) > surface.Width * 0.5f + margin ||
            Mathf.Abs(surfacePosition.y) > surface.Height * 0.5f + margin;
    }

    private void OnValidate()
    {
        aimDuration = Mathf.Max(0.1f, aimDuration);
        attackRange = Mathf.Max(0f, attackRange);
        moveSpeed = Mathf.Max(0.1f, moveSpeed);
        eraseRadius = Mathf.Max(0.01f, eraseRadius);
        visualOffset = Mathf.Max(0.01f, visualOffset);
    }
}
