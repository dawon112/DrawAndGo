using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public sealed class DuduHomingProjectile : MonoBehaviour
{
    private DuduSurface surface;
    private DuduSurfaceMovement target;
    private Rigidbody body;
    private SphereCollider projectileSphere;
    private Vector2 surfacePosition;
    private Vector2 movementDirection;
    private float moveSpeed;
    private float remainingLifetime;
    private float visualOffset;
    private BoxCollider targetBody;
    private Vector3 tipLocalPosition;
    // Share of Dudu's collider (width, height) a bolt tip must reach into to hit him.
    private static readonly Vector2 ForgivingBodyScale = new Vector2(0.6f, 0.75f);
    private float maxHorizontalReach;
    private float maxRange;
    private Vector2 travel;

    // Optional cap on how far the bolt may travel sideways from where it was fired (0 = none).
    public void SetMaxHorizontalReach(float reach) => maxHorizontalReach = Mathf.Max(0f, reach);

    // The bolt disappears once it is farther than this from where it was fired (0 = no limit).
    public void SetMaxRange(float range) => maxRange = Mathf.Max(0f, range);

    // Extra distance past the switch point before the bolt changes walls, in either direction,
    // so a bolt homing right at a corner cannot flip between the two walls every tick.
    private const float SeamHysteresis = 0.15f;

    // A position on a neighbouring wall, in this wall's unwrapped coordinates, laid out like
    // Dudu's 2D view (walls end to end), so homing and hits work across a room corner.
    public static bool TryMapToSurface(DuduSurface surface, DuduSurface other, Vector3 worldPosition, out Vector2 position)
    {
        position = Vector2.zero;
        if (surface == null || other == null) return false;
        Vector2 local = other.WorldToSurface(worldPosition);
        float seam = surface.Width * 0.5f + other.Width * 0.5f;
        if (other == surface) position = local;
        else if (other == surface.nextSurface) position = new Vector2(local.x + seam, local.y);
        else if (other == surface.previousSurface) position = new Vector2(local.x - seam, local.y);
        else return false;
        return true;
    }

    private bool TryGetTargetSurfacePosition(out Vector2 position)
        => TryMapToSurface(surface, target.CurrentSurface, target.transform.position, out position);

    // Past a wall's edge the bolt moves onto the neighbouring wall. Its spot in Dudu's 2D view
    // (walls end to end) stays exactly the same.
    private void CrossSeam(ref Vector2 position)
    {
        float half = surface.Width * 0.5f;
        float switchAt = half + SeamHysteresis;
        if (position.x > switchAt && surface.nextSurface != null)
        {
            position.x -= half + surface.nextSurface.Width * 0.5f;
            surface = surface.nextSurface;
        }
        else if (position.x < -switchAt && surface.previousSurface != null)
        {
            position.x += half + surface.previousSurface.Width * 0.5f;
            surface = surface.previousSurface;
        }
    }

    public void Configure(
        DuduSurface targetSurface,
        DuduSurfaceMovement targetDudu,
        float speed,
        float turnSpeedDegrees,
        float lifetime,
        Vector2 startSurfacePosition,
        float surfaceVisualOffset)
    {
        surface = targetSurface;
        target = targetDudu;
        moveSpeed = Mathf.Max(0f, speed);
        remainingLifetime = Mathf.Max(0.1f, lifetime);
        surfacePosition = startSurfacePosition;
        visualOffset = Mathf.Clamp(surfaceVisualOffset, 0.01f, 0.15f);
        targetBody = target != null ? target.GetComponent<BoxCollider>() : null;
        // The artwork's tip is at the bottom of the sprite (see GetMovementRotation); the
        // outer 15% is transparent padding.
        SpriteRenderer art = GetComponent<SpriteRenderer>();
        if (art != null && art.sprite != null)
        {
            Bounds spriteBounds = art.sprite.bounds;
            tipLocalPosition = spriteBounds.center - Vector3.up * spriteBounds.extents.y * 0.85f;
        }
        movementDirection = GetDirectionToTarget();
        transform.SetPositionAndRotation(SurfaceToWorld(surfacePosition), GetMovementRotation());
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;

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

        // Dudu may walk around a corner onto a neighbouring wall; the bolt follows him there.
        if (!TryGetTargetSurfacePosition(out _))
        {
            Destroy(gameObject);
            return;
        }

        Vector2 desiredDirection = GetDirectionToTarget();
        if (desiredDirection.sqrMagnitude > 0.0001f)
            movementDirection = desiredDirection.normalized;

        // Direction changes must never change the projectile's travel speed.
        Vector2 step = movementDirection.normalized * moveSpeed * Time.fixedDeltaTime;
        // Distance from the firing point, kept across corners (walls lie side by side unwrapped).
        travel += step;
        if ((maxHorizontalReach > 0f && Mathf.Abs(travel.x) > maxHorizontalReach) ||
            (maxRange > 0f && travel.magnitude > maxRange))
        {
            Destroy(gameObject);
            return;
        }
        Vector2 nextSurfacePosition = surfacePosition + step;
        CrossSeam(ref nextSurfacePosition);
        Vector3 nextWorldPosition = SurfaceToWorld(nextSurfacePosition);
        Vector3 displacement = nextWorldPosition - body.position;
        if (TouchesDrawnLineAlongMove(displacement))
        {
            Destroy(gameObject);
            return;
        }
        if (TouchesTargetAlongMove(displacement))
        {
            target.Die();
            Destroy(gameObject);
            return;
        }

        body.MoveRotation(GetMovementRotation());
        body.MovePosition(nextWorldPosition);
        surfacePosition = nextSurfacePosition;
    }

    private bool TouchesTargetAlongMove(Vector3 displacement)
    {
        if (target == null || targetBody == null)
            return false;

        // Hit only where the bolt is drawn: its tip against Dudu's body box, in the
        // unwrapped 2D view. The old sphere (and a fixed 0.85 circle) was about as wide
        // as the whole sprite, so the bolt vanished and Dudu was hit before they touched.
        Vector2 tip2D = surface.WorldToSurface(transform.TransformPoint(tipLocalPosition) + displacement);
        if (!TryMapToSurface(surface, target.CurrentSurface, targetBody.transform.TransformPoint(targetBody.center), out Vector2 body2D))
            return false;
        // Forgiving: the tip must reach well inside Dudu's box (shrunk to his drawn body),
        // so a near miss that only grazes the edge of his collider does not count.
        Vector3 bodyScale = targetBody.transform.lossyScale;
        Vector2 halfSize = Vector2.Scale(new Vector2(Mathf.Abs(targetBody.size.x * bodyScale.x),
            Mathf.Abs(targetBody.size.y * bodyScale.y)) * 0.5f, ForgivingBodyScale);
        return Mathf.Abs(tip2D.x - body2D.x) <= halfSize.x && Mathf.Abs(tip2D.y - body2D.y) <= halfSize.y;
    }

    private Quaternion GetMovementRotation()
    {
        if (surface == null || movementDirection.sqrMagnitude <= 0.0001f)
            return body != null ? body.rotation : transform.rotation;

        // The thunder artwork points along local down, so rotate that tip toward Dudu.
        float angle = Mathf.Atan2(
            movementDirection.y,
            movementDirection.x) * Mathf.Rad2Deg;
        return surface.transform.rotation * Quaternion.Euler(0f, 0f, angle + 90f);
    }

    // Dudu is hit only by the visible-tip test in FixedUpdate, never by this wide trigger.
    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<DrawingStroke>() != null)
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

    private Vector2 GetDirectionToTarget()
    {
        if (surface == null || target == null || !TryGetTargetSurfacePosition(out Vector2 targetPosition))
            return Vector2.right;

        Vector2 direction = targetPosition - surfacePosition;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
    }

    private Vector3 SurfaceToWorld(Vector2 position)
    {
        return surface.transform.position +
            surface.Right.normalized * position.x +
            surface.Up.normalized * position.y +
            surface.Normal.normalized * visualOffset;
    }

    private bool IsOutsideSurface()
    {
        const float boundaryMargin = 1f;
        return Mathf.Abs(surfacePosition.x) > surface.Width * 0.5f + boundaryMargin ||
               Mathf.Abs(surfacePosition.y) > surface.Height * 0.5f + boundaryMargin;
    }
}
