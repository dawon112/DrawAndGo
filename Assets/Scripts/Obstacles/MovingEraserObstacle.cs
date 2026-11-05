using UnityEngine;

// After TutorialSequenceController (100) has set up its walls, so this binds to the final ones.
[DefaultExecutionOrder(200)]
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
    [SerializeField] private float visualOffset = 0.025f;
    [Tooltip("Share of the drawn eraser that hits Dudu; slightly under 1 for fair near-misses.")]
    [SerializeField, Range(0.5f, 1f)] private float hitboxScale = 0.8f;
    [Tooltip("Rotation offset that aligns the sprite's long axis with its launch direction.")]
    [SerializeField] private float visualForwardAngleOffset = -90f;

    private Rigidbody body;
    private BoxCollider eraserCollider;
    private Collider targetCollider;
    private Vector2 surfacePosition;
    private Vector2 launchDirection = Vector2.right;
    private float aimElapsed;
    private bool launched;
    private Vector2 eraserArtHalf, eraserArtCenter;

    public void SetSurfaceNormalOffset(float offset)
    {
        visualOffset = offset;
        if (surface != null)
            transform.position = SurfaceToWorld(surfacePosition);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        eraserCollider = GetComponent<BoxCollider>();
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        // Rotation is driven explicitly around each surface's normal while aiming and flying.
        body.constraints = RigidbodyConstraints.None;
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
        EraseAlongMove(body.position, nextWorldPosition);
        if (TouchesDuduAlongMove(surfacePosition, nextSurfacePosition))
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
        // Only the host edits strokes. Dudu's client owns this hazard's motion, so it
        // asks the host to erase; the result returns through the normal stroke sync.
        if (GameSession.Current != null && !GameSession.Current.IsHost)
        {
            if (StageStateSync.Active != null)
                StageStateSync.Active.RequestErase(start, end, eraseRadius);
            return;
        }
        DrawingStroke.EraseAlongSegment(start, end, eraseRadius);
    }

    private bool TouchesDuduAlongMove(Vector2 from, Vector2 to)
    {
        if (target.CurrentSurface != surface)
            return false;
        if (targetCollider == null)
            targetCollider = target.GetComponent<Collider>();
        if (!(targetCollider is BoxCollider duduBox))
            return false;

        // In the 2D view, as the player sees it: the eraser's drawn shape (its sprite's tight
        // outline, much larger than the prefab's small collider) against Dudu's body box.
        // Touching on screen hits; the 3D boxes alone missed visible contact.
        Vector3 eraserScale = transform.lossyScale;
        Vector2 half = Vector2.Scale(GetEraserLocalHalfSize(), new Vector2(Mathf.Abs(eraserScale.x), Mathf.Abs(eraserScale.y))) * hitboxScale;
        float angle = (Mathf.Atan2(launchDirection.y, launchDirection.x) * Mathf.Rad2Deg + visualForwardAngleOffset) * Mathf.Deg2Rad;
        Vector2 axisX = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), axisY = new Vector2(-axisX.y, axisX.x);

        Vector3 duduScale = duduBox.transform.lossyScale;
        Vector2 duduCenter = surface.WorldToSurface(duduBox.transform.TransformPoint(duduBox.center));
        Vector2 duduHalf = new Vector2(Mathf.Abs(duduBox.size.x * duduScale.x), Mathf.Abs(duduBox.size.y * duduScale.y)) * 0.5f;

        // Sample the whole move so a fast eraser cannot skip over Dudu in one tick.
        float travel = Vector2.Distance(from, to);
        int samples = Mathf.Max(1, Mathf.CeilToInt(travel / Mathf.Max(0.05f, Mathf.Min(half.x, half.y))));
        for (int i = 0; i <= samples; i++)
        {
            Vector2 center = Vector2.Lerp(from, to, i / (float)samples) + axisX * eraserArtCenter.x * Mathf.Abs(eraserScale.x) +
                axisY * eraserArtCenter.y * Mathf.Abs(eraserScale.y);
            if (RectsOverlap(center, half, axisX, axisY, duduCenter, duduHalf)) return true;
        }
        return false;
    }

    // Separating-axis test: an eraser rectangle turned to axisX/axisY against Dudu's upright box.
    private static bool RectsOverlap(Vector2 center, Vector2 half, Vector2 axisX, Vector2 axisY, Vector2 boxCenter, Vector2 boxHalf)
    {
        Vector2 offset = boxCenter - center;
        foreach (Vector2 axis in new[] { Vector2.right, Vector2.up, axisX, axisY })
        {
            float eraserReach = Mathf.Abs(Vector2.Dot(axisX, axis)) * half.x + Mathf.Abs(Vector2.Dot(axisY, axis)) * half.y;
            float boxReach = Mathf.Abs(axis.x) * boxHalf.x + Mathf.Abs(axis.y) * boxHalf.y;
            if (Mathf.Abs(Vector2.Dot(offset, axis)) > eraserReach + boxReach) return false;
        }
        return true;
    }

    // Half size of the drawn eraser in its own space (the sprite without its transparent margin).
    private Vector2 GetEraserLocalHalfSize()
    {
        if (eraserArtHalf == Vector2.zero)
        {
            SpriteRenderer art = GetComponentInChildren<SpriteRenderer>();
            if (art != null && art.sprite != null)
            {
                Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
                foreach (Vector2 vertex in art.sprite.vertices) { min = Vector2.Min(min, vertex); max = Vector2.Max(max, vertex); }
                eraserArtHalf = (max - min) * 0.5f;
                eraserArtCenter = (max + min) * 0.5f;
            }
            else if (eraserCollider != null)
            {
                eraserArtHalf = eraserCollider.size * 0.5f;
                eraserArtCenter = eraserCollider.center;
            }
        }
        return eraserArtHalf;
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
        return surface.transform.rotation * Quaternion.Euler(0f, 0f, angle + visualForwardAngleOffset);
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
        hitboxScale = Mathf.Clamp(hitboxScale, 0.5f, 1f);
    }
}
