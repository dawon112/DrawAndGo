using System.Collections.Generic;
using UnityEngine;

// After TutorialSequenceController (100) has set up its walls, so this binds to the final ones.
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class WindZone : MonoBehaviour
{
    [SerializeField] private DuduSurface surface;
    [SerializeField] private Vector2 windDirection = Vector2.right;
    [SerializeField, Min(0f)] private float windForce = 1f;
    private BoxCollider zoneCollider;
    private static readonly List<WindZone> ActiveZones = new List<WindZone>();

    public DuduSurface Surface => surface;
    public Vector2 Direction => windDirection.sqrMagnitude > 0f ? windDirection.normalized : Vector2.right;
    public float Force => windForce;

    // Is any part of this line being blown? Gives a point of it inside the zone.
    public bool TouchesStroke(DrawingStroke stroke, out Vector3 inside)
    {
        inside = default;
        if (surface == null || Mathf.Abs(Vector3.Dot(stroke.SurfaceNormal.normalized, surface.Normal.normalized)) < 0.9f)
            return false;
        LineRenderer line = stroke.Line;
        int step = Mathf.Max(1, line.positionCount / 12);
        for (int i = 0; i < line.positionCount; i += step)
        {
            Vector3 point = line.GetPosition(i);
            if (Mathf.Abs(Vector3.Dot(point - surface.transform.position, surface.Normal.normalized)) > 0.35f) continue;
            if (Contains(point)) { inside = point; return true; }
        }
        return false;
    }

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
        gameObject.AddComponent<WindZoneFx>();
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

    // A whole line at once: the wind on its first point or segment midpoint that has any,
    // as the point-by-point query above would give. Each zone is read once per call.
    public static Vector3 GetStrokeVelocity(LineRenderer line, Vector3 surfaceNormal)
    {
        int count = line != null ? line.positionCount : 0;
        if (count == 0) return Vector3.zero;
        Vector3 strokeNormal = surfaceNormal.normalized;
        ZoneShape.LineBox(line, count, out Vector3 boxCenter, out Vector3 boxExtents);
        int zones = 0;
        foreach (WindZone zone in ActiveZones)
        {
            if (zone == null || zone.surface == null) continue;
            Vector3 normal = zone.surface.Normal.normalized;
            if (Mathf.Abs(Vector3.Dot(strokeNormal, normal)) < 0.9f) continue;
            if (zone.zoneCollider == null) zone.zoneCollider = zone.GetComponent<BoxCollider>();
            Vector2 direction = zone.windDirection.sqrMagnitude > 0f ? zone.windDirection.normalized : Vector2.right;
            Vector3 velocity = (zone.surface.Right.normalized * direction.x + zone.surface.Up.normalized * direction.y) * zone.windForce;
            var shape = new ZoneShape(zone.surface.transform.position, normal, zone.transform.worldToLocalMatrix,
                zone.zoneCollider.center, zone.zoneCollider.size * 0.5f, velocity: velocity);
            if (!shape.MayContain(boxCenter, boxExtents, 0.35f)) continue;
            if (zones == Scratch.Length) System.Array.Resize(ref Scratch, zones * 2);
            Scratch[zones++] = shape;
        }
        if (zones == 0) return Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            Vector3 point = line.GetPosition(i);
            Vector3 velocity = VelocityAt(point, zones);
            if (velocity.sqrMagnitude > 0.000001f) return velocity;
            if (i + 1 >= count) continue;
            velocity = VelocityAt((point + line.GetPosition(i + 1)) * 0.5f, zones);
            if (velocity.sqrMagnitude > 0.000001f) return velocity;
        }
        return Vector3.zero;
    }

    private static ZoneShape[] Scratch = new ZoneShape[16];

    private static Vector3 VelocityAt(Vector3 point, int zones)
    {
        Vector3 result = Vector3.zero;
        for (int z = 0; z < zones; z++)
            if (Scratch[z].Contains(point, 0.35f)) result += Scratch[z].Velocity;
        return result;
    }

    // True while Dudu stands in this zone's air, on its wall: the wind is pushing him.
    public bool IsPushing(DuduSurfaceMovement dudu) =>
        dudu != null && surface != null && dudu.CurrentSurface == surface && Contains(dudu.transform.position);

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
