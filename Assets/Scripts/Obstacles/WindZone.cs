using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class WindZone : MonoBehaviour
{
    [SerializeField] private DuduSurface surface;
    [SerializeField] private Vector2 windDirection = Vector2.right;
    [SerializeField, Min(0f)] private float windForce = 1f;
    private BoxCollider zoneCollider;
    private static readonly List<WindZone> ActiveZones = new List<WindZone>();

    public void Configure(DuduSurface targetSurface, Vector2 direction, float force)
    {
        surface = targetSurface;
        windDirection = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.right;
        windForce = Mathf.Max(0f, force);
    }

    private void Awake()
    {
        zoneCollider = GetComponent<BoxCollider>();
        ResolveSurface();
    }

    private void OnEnable()
    {
        ResolveSurface();
        if (!ActiveZones.Contains(this)) ActiveZones.Add(this);
    }

    private void OnDisable()
    {
        ActiveZones.Remove(this);
    }

    public static Vector2 GetSurfaceVelocity(DuduSurface targetSurface, Vector3 worldPoint)
    {
        if (targetSurface == null) return Vector2.zero;
        Vector2 result = Vector2.zero;
        foreach (WindZone zone in ActiveZones)
        {
            if (zone == null || zone.surface != targetSurface || !zone.Contains(worldPoint)) continue;
            Vector2 direction = zone.windDirection.sqrMagnitude > 0f
                ? zone.windDirection.normalized
                : Vector2.right;
            result += direction * zone.windForce;
        }
        return result;
    }

    public static Vector3 GetWorldVelocity(Vector3 worldPoint, Vector3 surfaceNormal)
    {
        Vector3 result = Vector3.zero;
        Vector3 normalizedStrokeNormal = surfaceNormal.normalized;
        foreach (WindZone zone in ActiveZones)
        {
            if (zone == null || zone.surface == null || !zone.Contains(worldPoint)) continue;
            Vector3 zoneNormal = zone.surface.Normal.normalized;
            if (Mathf.Abs(Vector3.Dot(normalizedStrokeNormal, zoneNormal)) < 0.9f) continue;
            float planeDistance = Mathf.Abs(Vector3.Dot(
                worldPoint - zone.surface.transform.position,
                zoneNormal));
            if (planeDistance > 0.35f) continue;

            Vector2 direction = zone.windDirection.sqrMagnitude > 0f
                ? zone.windDirection.normalized
                : Vector2.right;
            result += (zone.surface.Right.normalized * direction.x +
                zone.surface.Up.normalized * direction.y) * zone.windForce;
        }
        return result;
    }

    private bool Contains(Vector3 worldPoint)
    {
        if (zoneCollider == null) zoneCollider = GetComponent<BoxCollider>();
        Vector3 local = transform.InverseTransformPoint(worldPoint) - zoneCollider.center;
        Vector3 half = zoneCollider.size * 0.5f;
        // Compare only surface-local X/Y. Dudu, line points, and the translucent
        // visual intentionally use slightly different depth offsets.
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y;
    }

    [ContextMenu("Auto Assign Nearest Surface")]
    private void ResolveSurface()
    {
        Vector3 placedPosition = transform.position;
        surface = null;
        float bestScore = float.PositiveInfinity;
        foreach (DuduSurface candidate in FindObjectsByType<DuduSurface>(FindObjectsInactive.Exclude))
        {
            Vector2 position = candidate.WorldToSurface(transform.position);
            float outsideX = Mathf.Max(0f, Mathf.Abs(position.x) - candidate.Width * 0.5f);
            float outsideY = Mathf.Max(0f, Mathf.Abs(position.y) - candidate.Height * 0.5f);
            float planeDistance = Mathf.Abs(Vector3.Dot(
                transform.position - candidate.transform.position,
                candidate.Normal.normalized));
            float alignmentPenalty = 1f - Mathf.Abs(Vector3.Dot(
                transform.forward.normalized,
                candidate.transform.forward.normalized));
            float score = planeDistance + (outsideX + outsideY) * 4f + alignmentPenalty;
            if (score >= bestScore) continue;
            bestScore = score;
            surface = candidate;
        }

        if (surface != null)
        {
            Vector2 position = surface.WorldToSurface(placedPosition);
            transform.SetPositionAndRotation(
                surface.SurfaceToWorld(position) + surface.Normal.normalized * 0.012f,
                surface.transform.rotation);
        }
    }

    private void OnValidate()
    {
        windForce = Mathf.Max(0f, windForce);
        if (windDirection.sqrMagnitude <= 0.0001f) windDirection = Vector2.right;
    }
}
