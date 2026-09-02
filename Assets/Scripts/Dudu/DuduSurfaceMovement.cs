using UnityEngine;
using UnityEngine.InputSystem;

public sealed class DuduSurfaceMovement : MonoBehaviour
{
    [SerializeField] private DuduSurface currentSurface;
    [SerializeField, Min(0f)] private float moveSpeed = 4f;
    [SerializeField, Min(0f)] private float gravity = 18f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.4f;
    [SerializeField] private float groundSurfaceY = -2.4f;
    [SerializeField] private Vector2 characterHalfSize = new Vector2(0.4f, 0.75f);
    [SerializeField] private Vector2 surfacePosition = new Vector2(0f, -1.65f);
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    private float verticalVelocity;
    private bool grounded;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int GroundedId = Animator.StringToHash("Grounded");

    public void SetSurface(DuduSurface surface)
    {
        currentSurface = surface;
        ApplySurfaceTransform();
    }

    public void Configure(DuduSurface surface, Animator newAnimator, SpriteRenderer newSpriteRenderer)
    {
        currentSurface = surface;
        animator = newAnimator;
        spriteRenderer = newSpriteRenderer;
        SnapToGround();
        ApplySurfaceTransform();
    }

    private void Start()
    {
        ApplySurfaceTransform();
    }

    private void Update()
    {
        if (currentSurface == null)
            return;

        Keyboard keyboard = Keyboard.current;
        float horizontalInput = 0f;
        if (keyboard != null)
        {
            bool right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
            bool left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
            horizontalInput = (right ? 1f : 0f) - (left ? 1f : 0f);
        }

        surfacePosition.x += horizontalInput * moveSpeed * Time.deltaTime;
        float horizontalLimit = Mathf.Max(0f, currentSurface.Width * 0.5f - characterHalfSize.x);
        surfacePosition.x = Mathf.Clamp(surfacePosition.x, -horizontalLimit, horizontalLimit);

        if (grounded && keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * 2f * gravity);
            grounded = false;
        }

        verticalVelocity -= gravity * Time.deltaTime;
        surfacePosition.y += verticalVelocity * Time.deltaTime;

        float standingY = groundSurfaceY + characterHalfSize.y;
        if (surfacePosition.y <= standingY)
        {
            surfacePosition.y = standingY;
            verticalVelocity = 0f;
            grounded = true;
        }

        float topLimit = currentSurface.Height * 0.5f - characterHalfSize.y;
        if (surfacePosition.y > topLimit)
        {
            surfacePosition.y = topLimit;
            verticalVelocity = Mathf.Min(verticalVelocity, 0f);
        }

        if (spriteRenderer != null && horizontalInput != 0f)
            spriteRenderer.flipX = horizontalInput < 0f;
        if (animator != null)
        {
            animator.SetFloat(SpeedId, Mathf.Abs(horizontalInput));
            animator.SetBool(GroundedId, grounded);
        }

        ApplySurfaceTransform();
    }

    private void SnapToGround()
    {
        surfacePosition.y = groundSurfaceY + characterHalfSize.y;
        verticalVelocity = 0f;
        grounded = true;
    }

    private void ApplySurfaceTransform()
    {
        if (currentSurface == null)
            return;

        transform.position = currentSurface.SurfaceToWorld(surfacePosition);
        transform.rotation = currentSurface.transform.rotation;
    }
}
