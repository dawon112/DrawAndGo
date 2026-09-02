using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(Animator))]
public sealed class PlayerController2D : MonoBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(0f)] private float jumpVelocity = 8f;

    private readonly RaycastHit2D[] groundHits = new RaycastHit2D[4];
    private Rigidbody2D body;
    private Collider2D bodyCollider;
    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private float moveInput;
    private bool jumpRequested;
    private bool grounded;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int GroundedId = Animator.StringToHash("Grounded");

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            moveInput = 0f;
            return;
        }

        bool left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
        bool right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
        moveInput = (right ? 1f : 0f) - (left ? 1f : 0f);

        if (keyboard.spaceKey.wasPressedThisFrame)
            jumpRequested = true;

        if (moveInput != 0f)
            spriteRenderer.flipX = moveInput < 0f;

        animator.SetFloat(SpeedId, Mathf.Abs(moveInput));
    }

    private void FixedUpdate()
    {
        grounded = CheckGrounded();

        Vector2 velocity = body.linearVelocity;
        velocity.x = moveInput * moveSpeed;

        if (jumpRequested && grounded)
        {
            velocity.y = jumpVelocity;
            grounded = false;
        }

        jumpRequested = false;
        body.linearVelocity = velocity;
        animator.SetBool(GroundedId, grounded);
    }

    private bool CheckGrounded()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(Physics2D.AllLayers);
        filter.useTriggers = false;

        int hitCount = bodyCollider.Cast(Vector2.down, filter, groundHits, 0.08f);
        for (int i = 0; i < hitCount; i++)
        {
            if (groundHits[i].normal.y > 0.6f)
                return true;
        }

        return false;
    }
}
