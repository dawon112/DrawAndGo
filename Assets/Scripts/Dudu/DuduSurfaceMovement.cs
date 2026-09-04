using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public sealed class DuduSurfaceMovement : MonoBehaviour
{
    [Header("Surface")]
    [SerializeField] private DuduSurface currentSurface;
    [SerializeField] private Vector2 characterHalfSize = new Vector2(0.4f, 0.75f);
    [SerializeField] private Vector2 surfacePosition = new Vector2(-5f, -1.65f);

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 4f;
    [SerializeField, Min(0f)] private float gravity = 18f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.4f;
    [SerializeField, Min(0f)] private float maximumFallSpeed = 20f;

    [Header("Drawn Line Walking")]
    [SerializeField, Range(0f, 60f)] private float maxWalkableSlopeAngle = 45f;
    [Tooltip("Maximum small seam height to step over on a drawn line, in world units.")]
    [SerializeField, Range(0f, 0.1f)] private float drawnLineStepHeight = 0.04f;

    [Header("Visuals")]
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("Hit Feedback")]
    [Tooltip("Dudu opacity while showing a projectile hit.")]
    [SerializeField, Range(0f, 1f)] private float hitOpacity = 0.5f;
    [Tooltip("Seconds before Dudu returns to normal opacity.")]
    [SerializeField, Min(0f)] private float hitFeedbackDuration = 0.5f;

    private Rigidbody body;
    private Collider duduCollider;
    private float horizontalInput;
    private float surfaceDepth;
    private bool inputEnabled;
    private bool jumpRequested;
    private float lastGroundedTime = float.NegativeInfinity;
    private float bounceHorizontalSpeed;
    private float bounceControlUntil = float.NegativeInfinity;
    private Coroutine hitFeedbackRoutine;
    private Color normalSpriteColor = Color.white;
    private bool slowEffectActive;
    private float slowEffectUntil = float.NegativeInfinity;
    private bool reverseEffectActive;
    private float reverseEffectUntil = float.NegativeInfinity;
    private float stainSpeedMultiplier = 1f;
    private bool wasFollowingDrawnLine;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int GroundedId = Animator.StringToHash("Grounded");

    public bool InputEnabled => inputEnabled;

    public void SetSurface(DuduSurface surface)
    {
        currentSurface = surface;
        if (Application.isPlaying && body != null)
            InitializeSurfacePhysics();
        else
            ApplySurfaceTransform();
    }

    public void Configure(DuduSurface surface, Animator newAnimator, SpriteRenderer newSpriteRenderer)
    {
        currentSurface = surface;
        animator = newAnimator;
        spriteRenderer = newSpriteRenderer;
        CacheNormalSpriteColor();
        surfacePosition.y = -1.65f;
        ApplySurfaceTransform();
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        if (!enabled)
        {
            horizontalInput = 0f;
            jumpRequested = false;
        }
    }

    public void BounceAwayFrom(
        Vector3 sourcePosition,
        float horizontalSpeed,
        float upwardSpeed,
        float controlDuration)
    {
        if (currentSurface == null || body == null)
            return;

        Vector3 right = currentSurface.Right.normalized;
        Vector3 up = currentSurface.Up.normalized;
        float side = Vector3.Dot(body.position - sourcePosition, right);
        if (Mathf.Abs(side) < 0.01f)
            side = horizontalInput == 0f ? 1f : -horizontalInput;

        bounceHorizontalSpeed = Mathf.Sign(side) * Mathf.Abs(horizontalSpeed);
        bounceControlUntil = Time.time + Mathf.Max(0f, controlDuration);
        float verticalSpeed = Mathf.Max(
            Vector3.Dot(body.linearVelocity, up),
            Mathf.Abs(upwardSpeed));

        body.linearVelocity = right * bounceHorizontalSpeed + up * verticalSpeed;
        jumpRequested = false;
        lastGroundedTime = float.NegativeInfinity;
    }

    public void ShowHitFeedback()
    {
        if (spriteRenderer == null)
            return;

        if (hitFeedbackRoutine != null)
            StopCoroutine(hitFeedbackRoutine);
        hitFeedbackRoutine = StartCoroutine(HitFeedbackRoutine());
    }

    public void ApplyStainEffect(
        DuduStainObstacle.EffectType effectType,
        float speedMultiplier,
        float duration)
    {
        float effectEndTime = Time.time + Mathf.Max(0f, duration);
        switch (effectType)
        {
            case DuduStainObstacle.EffectType.Slow:
                if (slowEffectActive && Time.time < slowEffectUntil)
                    return;
                slowEffectActive = true;
                slowEffectUntil = effectEndTime;
                stainSpeedMultiplier = Mathf.Clamp(speedMultiplier, 0.1f, 1f);
                break;

            case DuduStainObstacle.EffectType.ReverseControls:
                if (reverseEffectActive && Time.time < reverseEffectUntil)
                    return;
                reverseEffectActive = true;
                reverseEffectUntil = effectEndTime;
                break;
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (body == null)
            body = gameObject.AddComponent<Rigidbody>();
        duduCollider = GetComponent<Collider>();
        CacheNormalSpriteColor();

        body.useGravity = false;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        InitializeSurfacePhysics();
    }

    private void OnDisable()
    {
        RestoreSpriteColor();
        ResetStainEffects();
    }

    private void Update()
    {
        if (slowEffectActive && Time.time >= slowEffectUntil)
            ResetSlowEffect();
        if (reverseEffectActive && Time.time >= reverseEffectUntil)
            ResetReverseEffect();

        horizontalInput = 0f;
        Keyboard keyboard = Keyboard.current;
        if (inputEnabled && keyboard != null)
        {
            bool right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
            bool left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
            horizontalInput = (right ? 1f : 0f) - (left ? 1f : 0f);
            if (reverseEffectActive)
                horizontalInput = -horizontalInput;
            if (keyboard.spaceKey.wasPressedThisFrame && Time.time - lastGroundedTime < 0.15f)
                jumpRequested = true;
        }

        if (spriteRenderer != null && horizontalInput != 0f)
            spriteRenderer.flipX = horizontalInput < 0f;
        if (animator != null)
        {
            animator.SetFloat(SpeedId, Mathf.Abs(horizontalInput));
            animator.SetBool(GroundedId, Time.time - lastGroundedTime < 0.1f);
        }
    }

    private void FixedUpdate()
    {
        if (currentSurface == null || body == null)
            return;

        if (currentSurface.IsAtOrBelowBottom(body.position, characterHalfSize.y))
        {
            Respawn();
            return;
        }

        Vector3 right = currentSurface.Right.normalized;
        Vector3 up = currentSurface.Up.normalized;
        Vector3 normal = currentSurface.Normal.normalized;
        Vector3 relativePosition = body.position - currentSurface.transform.position;

        float horizontalPosition = Vector3.Dot(relativePosition, right);
        float horizontalLimit = Mathf.Max(0f, currentSurface.Width * 0.5f - characterHalfSize.x);
        float clampedHorizontal = Mathf.Clamp(horizontalPosition, -horizontalLimit, horizontalLimit);
        float normalPosition = Vector3.Dot(relativePosition, normal);
        Vector3 constraintCorrection = right * (clampedHorizontal - horizontalPosition) +
            normal * (surfaceDepth - normalPosition);
        if (constraintCorrection.sqrMagnitude > 0.00000001f)
            body.position += constraintCorrection;

        float horizontalSpeed = Time.time < bounceControlUntil
            ? bounceHorizontalSpeed
            : horizontalInput * moveSpeed * stainSpeedMultiplier;
        if ((clampedHorizontal <= -horizontalLimit && horizontalSpeed < 0f) ||
            (clampedHorizontal >= horizontalLimit && horizontalSpeed > 0f))
            horizontalSpeed = 0f;

        float verticalSpeed = Mathf.Max(Vector3.Dot(body.linearVelocity, up), -maximumFallSpeed);
        bool followingDrawnLine = !jumpRequested && Time.time >= bounceControlUntil &&
            (verticalSpeed <= 0.01f || wasFollowingDrawnLine) &&
            TryFollowDrawnLine(right, up, horizontalSpeed, ref verticalSpeed);
        wasFollowingDrawnLine = followingDrawnLine;
        if (jumpRequested)
        {
            verticalSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);
            jumpRequested = false;
            lastGroundedTime = float.NegativeInfinity;
        }
        body.linearVelocity = right * horizontalSpeed + up * verticalSpeed;
        if (!followingDrawnLine)
            body.AddForce(-up * gravity, ForceMode.Acceleration);
    }

    private bool TryFollowDrawnLine(Vector3 right, Vector3 up, float speed, ref float verticalSpeed)
    {
        if (!(duduCollider is BoxCollider box))
            return false;

        const float skin = 0.005f;
        Vector3 scale = transform.lossyScale;
        Vector3 half = Vector3.Scale(box.size * 0.5f,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        Vector3 center = body.position + body.rotation * Vector3.Scale(box.center, scale);
        // Keep the full foot width: a center ray misses the leading box corner.
        // A small vertical inset lets the cast start above existing contacts.
        half.y = Mathf.Max(skin, half.y - skin);
        float probeLift = drawnLineStepHeight + skin;
        if (!TryFindSupport(center, half, up, probeLift, probeLift + skin * 4f, out RaycastHit current))
            return false;

        float minUp = Mathf.Cos(maxWalkableSlopeAngle * Mathf.Deg2Rad);
        if (Vector3.Dot(current.normal, up) < minUp)
            return false;

        if (Mathf.Abs(speed) <= 0.0001f && current.collider.GetComponentInParent<DrawingStroke>() != null)
        {
            // Stay on the verified support without repeated height corrections
            // or gravity-induced sliding while the player is not walking.
            verticalSpeed = 0f;
            lastGroundedTime = Time.time;
            return true;
        }

        Vector3 horizontalMove = right * speed * Time.fixedDeltaTime;
        float reach = horizontalMove.magnitude * Mathf.Tan(maxWalkableSlopeAngle * Mathf.Deg2Rad) + probeLift;
        if (!TryFindSupport(center + horizontalMove, half, up, reach, reach * 2f, out RaycastHit next) ||
            next.collider.GetComponentInParent<DrawingStroke>() == null ||
            Vector3.Dot(next.normal, up) < minUp)
            return false;

        // A steep segment's end cap can face upward; it must still remain a wall.
        float segmentRise = Mathf.Abs(Vector3.Dot(next.collider.transform.right, up));
        if (segmentRise > Mathf.Sin(maxWalkableSlopeAngle * Mathf.Deg2Rad) + 0.001f)
            return false;

        // Restore the inset and leave a skin-width gap for horizontal clearance.
        float rise = reach - next.distance + skin * 2f;
        if (rise > reach || rise < -reach)
            return false;

        // Travel along the slope with velocity, rather than teleporting upward
        // every tick. Only the residual above the tangent is a possible seam.
        float tangentRise = -Vector3.Dot(next.normal, horizontalMove) / Vector3.Dot(next.normal, up);
        float step = Mathf.Max(0f, rise - Mathf.Max(0f, tangentRise));
        if (step > drawnLineStepHeight)
            return false;

        Vector3 lift = up * step;
        Vector3 travel = horizontalMove + up * (rise - step);
        if (IsWalkingPathBlocked(center, half, lift))
            return false;
        if (IsWalkingPathBlocked(center + lift, half, travel))
        {
            // A box end cap can still obstruct the diagonal at a join.
            // Allow only a tiny, clearance-tested step, never a whole steep slope.
            if (rise <= 0f || rise > drawnLineStepHeight)
                return false;
            step = rise;
            lift = up * step;
            if (IsWalkingPathBlocked(center, half, lift) ||
                IsWalkingPathBlocked(center + lift, half, horizontalMove))
                return false;
        }

        if (step > 0.0001f)
            body.position += lift;
        verticalSpeed = (rise - step) / Time.fixedDeltaTime;
        lastGroundedTime = Time.time;
        return true;
    }

    private bool TryFindSupport(Vector3 center, Vector3 half, Vector3 up,
        float lift, float distance, out RaycastHit support)
    {
        support = default;
        float nearest = float.PositiveInfinity;
        foreach (RaycastHit hit in Physics.BoxCastAll(center + up * lift, half, -up,
            body.rotation, distance, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (IsIgnoredWalkingCollider(hit.collider) || hit.distance <= 0f || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            support = hit;
        }
        return nearest < float.PositiveInfinity;
    }

    private bool IsWalkingPathBlocked(Vector3 center, Vector3 half, Vector3 displacement)
    {
        float distance = displacement.magnitude;
        if (distance <= Mathf.Epsilon)
            return false;
        foreach (RaycastHit hit in Physics.BoxCastAll(center, half, displacement / distance,
            body.rotation, distance, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (!IsIgnoredWalkingCollider(hit.collider))
                return true;
        }
        return false;
    }

    private bool IsIgnoredWalkingCollider(Collider other)
    {
        return other.attachedRigidbody == body || other == currentSurface.GetComponent<Collider>() ||
            Physics.GetIgnoreLayerCollision(gameObject.layer, other.gameObject.layer) ||
            Physics.GetIgnoreCollision(duduCollider, other);
    }

    public void Respawn()
    {
        if (currentSurface == null || body == null)
            return;

        body.position = currentSurface.SurfaceToWorld(surfacePosition);
        body.rotation = currentSurface.transform.rotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        jumpRequested = false;
        lastGroundedTime = float.NegativeInfinity;
        bounceHorizontalSpeed = 0f;
        wasFollowingDrawnLine = false;
        bounceControlUntil = float.NegativeInfinity;
        ResetStainEffects();
    }

    private void OnCollisionStay(Collision collision)
    {
        if (currentSurface == null)
            return;

        Vector3 up = currentSurface.Up.normalized;
        float minimumGroundDot = collision.collider.GetComponentInParent<DrawingStroke>() != null
            ? Mathf.Cos(maxWalkableSlopeAngle * Mathf.Deg2Rad)
            : 0.35f;
        foreach (ContactPoint contact in collision.contacts)
        {
            if (Vector3.Dot(contact.normal, up) >= minimumGroundDot)
            {
                lastGroundedTime = Time.time;
                break;
            }
        }
    }

    private void InitializeSurfacePhysics()
    {
        if (currentSurface == null || body == null)
            return;

        surfaceDepth = Vector3.Dot(
            body.position - currentSurface.transform.position,
            currentSurface.Normal.normalized);

        Collider surfaceCollider = currentSurface.GetComponent<Collider>();
        if (surfaceCollider != null && duduCollider != null)
            Physics.IgnoreCollision(duduCollider, surfaceCollider, true);
    }

    private void ApplySurfaceTransform()
    {
        if (currentSurface == null)
            return;

        transform.position = currentSurface.SurfaceToWorld(surfacePosition);
        transform.rotation = currentSurface.transform.rotation;
    }

    private IEnumerator HitFeedbackRoutine()
    {
        Color hitColor = normalSpriteColor;
        hitColor.a = hitOpacity;
        spriteRenderer.color = hitColor;

        yield return new WaitForSeconds(hitFeedbackDuration);

        RestoreSpriteColor();
    }

    private void CacheNormalSpriteColor()
    {
        if (spriteRenderer != null)
            normalSpriteColor = spriteRenderer.color;
    }

    private void RestoreSpriteColor()
    {
        if (spriteRenderer != null)
            spriteRenderer.color = normalSpriteColor;
        hitFeedbackRoutine = null;
    }

    private void ResetStainEffects()
    {
        ResetSlowEffect();
        ResetReverseEffect();
    }

    private void ResetSlowEffect()
    {
        slowEffectActive = false;
        slowEffectUntil = float.NegativeInfinity;
        stainSpeedMultiplier = 1f;
    }

    private void ResetReverseEffect()
    {
        reverseEffectActive = false;
        reverseEffectUntil = float.NegativeInfinity;
    }
}
