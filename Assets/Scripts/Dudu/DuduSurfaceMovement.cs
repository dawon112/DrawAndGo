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
    }

    private void Update()
    {
        horizontalInput = 0f;
        Keyboard keyboard = Keyboard.current;
        if (inputEnabled && keyboard != null)
        {
            bool right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
            bool left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
            horizontalInput = (right ? 1f : 0f) - (left ? 1f : 0f);
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
        body.position += right * (clampedHorizontal - horizontalPosition);
        body.position += normal * (surfaceDepth - normalPosition);

        float horizontalSpeed = Time.time < bounceControlUntil
            ? bounceHorizontalSpeed
            : horizontalInput * moveSpeed;
        if ((clampedHorizontal <= -horizontalLimit && horizontalSpeed < 0f) ||
            (clampedHorizontal >= horizontalLimit && horizontalSpeed > 0f))
            horizontalSpeed = 0f;

        float verticalSpeed = Mathf.Max(Vector3.Dot(body.linearVelocity, up), -maximumFallSpeed);
        if (jumpRequested)
        {
            verticalSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);
            jumpRequested = false;
            lastGroundedTime = float.NegativeInfinity;
        }
        body.linearVelocity = right * horizontalSpeed + up * verticalSpeed;
        body.AddForce(-up * gravity, ForceMode.Acceleration);
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
        bounceControlUntil = float.NegativeInfinity;
    }

    private void OnCollisionStay(Collision collision)
    {
        if (currentSurface == null)
            return;

        Vector3 up = currentSurface.Up.normalized;
        foreach (ContactPoint contact in collision.contacts)
        {
            if (Vector3.Dot(contact.normal, up) > 0.35f)
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
}
