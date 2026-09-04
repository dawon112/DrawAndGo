using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class DuduStainObstacle : MonoBehaviour
{
    public enum EffectType
    {
        Slow,
        ReverseControls
    }

    [Header("Placement")]
    [SerializeField] private DuduSurface surface;
    [SerializeField] private Vector2 surfacePosition = new Vector2(-3f, -2.2f);

    [Header("Player Effect")]
    [SerializeField] private EffectType effectType;
    [Tooltip("Dudu movement speed while the Slow debuff is active.")]
    [SerializeField, Range(0.1f, 1f)] private float speedMultiplier = 0.5f;
    [Tooltip("Seconds the debuff remains active after Dudu first steps on the stain.")]
    [SerializeField, Min(0f)] private float effectDuration = 3f;

    public void Configure(DuduSurface targetSurface, Vector2 position, EffectType type)
    {
        surface = targetSurface;
        surfacePosition = position;
        effectType = type;
        ApplySurfaceTransform();
    }

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        DuduSurfaceMovement dudu = other.GetComponentInParent<DuduSurfaceMovement>();
        if (dudu == null)
            return;

        dudu.ApplyStainEffect(effectType, speedMultiplier, effectDuration);
    }

    private void ApplySurfaceTransform()
    {
        if (surface == null)
            return;

        transform.SetPositionAndRotation(
            surface.SurfaceToWorld(surfacePosition),
            surface.transform.rotation);
    }

    private void OnValidate()
    {
        speedMultiplier = Mathf.Clamp(speedMultiplier, 0.1f, 1f);
        effectDuration = Mathf.Max(0f, effectDuration);

        if (!Application.isPlaying)
            ApplySurfaceTransform();
    }
}
