using UnityEngine;

// A wall zone (Hot Zone, Wind Zone) read once for a batch of point queries: the wall's
// plane and the zone box in its own space. Contains() answers exactly like the zones'
// own per-point checks (plane distance, then the box's surface-local X/Y), without
// touching any transform per point.
public readonly struct ZoneShape
{
    private readonly Vector3 planePoint;
    private readonly Vector3 planeNormal;
    private readonly Matrix4x4 worldToLocal;
    private readonly Vector3 center;
    private readonly Vector3 half;
    public readonly float Value;
    public readonly Vector3 Velocity;

    public ZoneShape(Vector3 planePoint, Vector3 planeNormal, Matrix4x4 worldToLocal, Vector3 center, Vector3 half,
        float value = 0f, Vector3 velocity = default)
    {
        this.planePoint = planePoint;
        this.planeNormal = planeNormal;
        this.worldToLocal = worldToLocal;
        this.center = center;
        this.half = half;
        Value = value;
        Velocity = velocity;
    }

    // False only when no point inside the box can be Contains(): the box lies too far
    // from the wall, or wholly beside the zone. Lets a line skip zones on other walls.
    public bool MayContain(Vector3 boxCenter, Vector3 boxExtents, float maxWallDistance)
    {
        float reach = Vector3.Dot(boxExtents, Abs(planeNormal));
        if (Mathf.Abs(Vector3.Dot(boxCenter - planePoint, planeNormal)) - reach > maxWallDistance) return false;
        Vector3 local = worldToLocal.MultiplyPoint3x4(boxCenter) - center;
        float reachX = Vector3.Dot(boxExtents, Abs(new Vector3(worldToLocal.m00, worldToLocal.m01, worldToLocal.m02)));
        float reachY = Vector3.Dot(boxExtents, Abs(new Vector3(worldToLocal.m10, worldToLocal.m11, worldToLocal.m12)));
        return Mathf.Abs(local.x) - reachX <= half.x && Mathf.Abs(local.y) - reachY <= half.y;
    }

    // The box around a line's points (its segment midpoints lie inside it too).
    public static void LineBox(LineRenderer line, int count, out Vector3 boxCenter, out Vector3 boxExtents)
    {
        Vector3 min = line.GetPosition(0), max = min;
        for (int i = 1; i < count; i++)
        {
            Vector3 p = line.GetPosition(i);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        boxCenter = (min + max) * 0.5f;
        boxExtents = (max - min) * 0.5f;
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    public bool Contains(Vector3 worldPoint, float maxWallDistance)
    {
        if (Mathf.Abs(Vector3.Dot(worldPoint - planePoint, planeNormal)) > maxWallDistance) return false;
        Vector3 local = worldToLocal.MultiplyPoint3x4(worldPoint) - center;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y;
    }
}
