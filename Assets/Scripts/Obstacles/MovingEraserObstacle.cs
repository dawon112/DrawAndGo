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
    [Tooltip("Slightly smaller than the visual collider for fair near-misses.")]
    [SerializeField, Range(0.5f, 1f)] private float hitboxScale = 0.8f;

    private Rigidbody body;
    private BoxCollider eraserCollider;
    private Collider targetCollider;
    private Vector2 surfacePosition;
    private Vector2 launchDirection = Vector2.right;
    private float aimElapsed;
    private bool launched;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        eraserCollider = GetComponent<BoxCollider>();
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
        eraserCollider.isTrigger = true;
        ResolveReferencesAndPlacement();
    }

    private void FixedUpdate()
    {
        // Dudu's client owns hazards; Haru's host displays their synced poses.
        if (GameSession.Current != null && GameSession.Current.IsHost)
            return;
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

    public void ApplyRemotePose(bool active, Vector3 position, Quaternion rotation)
    {
        if (gameObject.activeSelf != active)
            gameObject.SetActive(active);
        if (!active) return;
        transform.SetPositionAndRotation(position, rotation);
        if (body != null)
        {
            body.position = position;
            body.rotation = rotation;
        }
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
        if (target.CurrentSurface != surface || eraserCollider == null)
            return false;
        if (targetCollider == null)
            targetCollider = target.GetComponent<Collider>();
        if (targetCollider == null)
            return false;

        Vector3 scale = transform.lossyScale;
        Vector3 absoluteScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Vector3 halfExtents = Vector3.Scale(eraserCollider.size * 0.5f, absoluteScale) * hitboxScale;
        Vector3 center = body.position + body.rotation * Vector3.Scale(eraserCollider.center, scale);

        foreach (Collider overlap in Physics.OverlapBox(
            center, halfExtents, body.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide))
            if (IsTargetCollider(overlap)) return true;

        float distance = displacement.magnitude;
        if (distance <= Mathf.Epsilon)
            return false;
        foreach (RaycastHit hit in Physics.BoxCastAll(
            center, halfExtents, displacement / distance, body.rotation,
            distance, Physics.AllLayers, QueryTriggerInteraction.Collide))
            if (IsTargetCollider(hit.collider)) return true;
        return false;
    }

    private bool IsTargetCollider(Collider candidate)
    {
        return candidate == targetCollider || candidate.GetComponentInParent<DuduSurfaceMovement>() == target;
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
        targetCollider = target != null ? target.GetComponent<Collider>() : null;
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
        hitboxScale = Mathf.Clamp(hitboxScale, 0.5f, 1f);
    }
}
