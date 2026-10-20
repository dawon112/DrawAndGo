using UnityEngine;

// Drives Haru's Idle / Walk / Jump from how the character actually moves. It reads the
// transform, not input, so the same code animates the remote Haru on the other player's
// screen, whose position arrives over the network.
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class HaruLocomotionAnimator : MonoBehaviour
{
    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int GroundedId = Animator.StringToHash("Grounded");
    private static readonly int WalkSpeedId = Animator.StringToHash("WalkSpeed");

    [Tooltip("Ground speed (m/s) at which the walk clip's feet do not slide.")]
    [SerializeField, Min(0.1f)] private float walkClipSpeed = 1.6f;
    [Tooltip("Fastest the walk clip may play, for sprinting.")]
    [SerializeField, Min(1f)] private float maxWalkPlayback = 2.2f;
    [Tooltip("How far below the feet still counts as standing.")]
    [SerializeField, Min(0.01f)] private float groundProbeDistance = 0.2f;
    [SerializeField, Min(0f)] private float speedSmoothing = 12f;

    private Animator animator;
    private Transform body;
    private Vector3 lastPosition;
    private float smoothedSpeed;
    private int groundMask;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;
        // The visual hangs under the moving player; measure the player itself.
        body = transform.parent != null ? transform.parent : transform;
        lastPosition = body.position;
        groundMask = ~(1 << gameObject.layer) & ~(1 << body.gameObject.layer);
        int strokes = LayerMask.NameToLayer("DrawnStroke");
        if (strokes >= 0) groundMask &= ~(1 << strokes);
    }

    private void Update()
    {
        // Copies of Haru (first-person arm, silhouette) disable their Animator.
        if (!animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f) return;

        Vector3 delta = body.position - lastPosition;
        lastPosition = body.position;
        delta.y = 0f;
        // Teleports (respawn, stage start) must not read as a sprint.
        float speed = delta.magnitude / deltaTime;
        if (speed > 20f) speed = 0f;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-speedSmoothing * deltaTime));

        animator.SetFloat(SpeedId, smoothedSpeed);
        animator.SetFloat(WalkSpeedId, Mathf.Clamp(smoothedSpeed / walkClipSpeed, 0.6f, maxWalkPlayback));
        animator.SetBool(GroundedId, IsGrounded());
    }

    private bool IsGrounded()
    {
        Vector3 origin = body.position + Vector3.up * 0.1f;
        return Physics.Raycast(origin, Vector3.down, 0.1f + groundProbeDistance,
            groundMask, QueryTriggerInteraction.Ignore);
    }
}
