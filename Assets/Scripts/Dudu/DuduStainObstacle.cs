using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class DuduStainObstacle : MonoBehaviour
{
    private readonly HashSet<DuduSurfaceMovement> occupants = new HashSet<DuduSurfaceMovement>();

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
        ResolveSurface();
    }

    private void OnTriggerEnter(Collider other)
    {
        TrackOccupant(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TrackOccupant(other);
    }

    private void OnTriggerExit(Collider other)
    {
        DuduSurfaceMovement dudu = other.GetComponentInParent<DuduSurfaceMovement>();
        if (dudu != null)
            occupants.Remove(dudu);
    }

    private void Update()
    {
        occupants.RemoveWhere(dudu => dudu == null);
        foreach (DuduSurfaceMovement dudu in occupants)
            ApplyEffect(dudu);
    }

    private void TrackOccupant(Collider other)
    {
        DuduSurfaceMovement dudu = other.GetComponentInParent<DuduSurfaceMovement>();
        if (dudu == null) return;
        occupants.Add(dudu);
        ApplyEffect(dudu);
    }

    private void ApplyEffect(DuduSurfaceMovement dudu)
    {
        dudu.ApplyStainEffect(effectType, speedMultiplier, effectDuration);
    }

    private void OnDisable() => occupants.Clear();

    private void ApplySurfaceTransform()
    {
        if (surface == null)
            return;

        transform.SetPositionAndRotation(
            surface.SurfaceToWorld(surfacePosition),
            surface.transform.rotation);
    }

    [ContextMenu("Auto Assign Nearest Surface")]
    private void ResolveSurface()
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

        if (surface == null) return;
        surfacePosition = surface.WorldToSurface(placedPosition);
        ApplySurfaceTransform();
    }

    private void OnValidate()
    {
        speedMultiplier = Mathf.Clamp(speedMultiplier, 0.1f, 1f);
        effectDuration = Mathf.Max(0f, effectDuration);

        if (!Application.isPlaying)
            ApplySurfaceTransform();
    }
}
