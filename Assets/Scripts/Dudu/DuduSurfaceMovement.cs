using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public sealed class DuduSurfaceMovement : MonoBehaviour
{
    [SerializeField] private DuduSurface currentSurface;
    [SerializeField, Min(0f)] private float moveSpeed = 4f;
    [SerializeField, Min(0f)] private float gravity = 18f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.4f;
    [SerializeField, Min(0f)] private float maximumFallSpeed = 20f;
    [SerializeField] private Vector2 characterHalfSize = new Vector2(0.4f, 0.75f);
    [SerializeField] private Vector2 surfacePosition = new Vector2(-5f, -1.65f);
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Rigidbody body;
    private Collider duduCollider;
    private float horizontalInput;
    private float surfaceDepth;
    private bool inputEnabled;
    private bool jumpRequested;
    private float lastGroundedTime = float.NegativeInfinity;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int GroundedId = Animator.StringToHash("Grounded");

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

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (body == null)
            body = gameObject.AddComponent<Rigidbody>();
        duduCollider = GetComponent<Collider>();

        body.useGravity = false;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        InitializeSurfacePhysics();
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

        float horizontalSpeed = horizontalInput * moveSpeed;
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
}
