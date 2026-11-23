using System.Collections.Generic;
using UnityEngine;

// One stop or pass-through point of a stage's opening camera tour (CornerRoomLevel.introPath).
// The camera passes through every point in order along one smooth curve, facing the way the
// point is turned; at a point with a hold it eases to a stop and waits there.
[DisallowMultipleComponent]
public sealed class IntroWaypoint : MonoBehaviour
{
    [Tooltip("Seconds the camera rests here. 0 sweeps straight through.")]
    [Min(0f)] public float hold;

    public static float HoldOf(Transform point)
    {
        IntroWaypoint waypoint = point.GetComponent<IntroWaypoint>();
        return waypoint != null ? waypoint.hold : 0f;
    }

    // The curve between points[segment] and points[segment + 1], at u from 0 to 1
    // (Catmull-Rom, using the neighbours on either side).
    public static Vector3 PathPoint(IReadOnlyList<Transform> points, int segment, float u)
    {
        Vector3 p0 = points[Mathf.Max(0, segment - 1)].position;
        Vector3 p1 = points[segment].position;
        Vector3 p2 = points[Mathf.Min(points.Count - 1, segment + 1)].position;
        Vector3 p3 = points[Mathf.Min(points.Count - 1, segment + 2)].position;
        float u2 = u * u, u3 = u2 * u;
        return 0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (3f * p1 - p0 - 3f * p2 + p3) * u3);
    }

#if UNITY_EDITOR
    // The whole tour in the Scene view, with the way each point faces.
    private void OnDrawGizmosSelected()
    {
        Transform path = transform.parent;
        if (path == null) return;
        List<Transform> points = new List<Transform>();
        foreach (Transform child in path) points.Add(child);
        Gizmos.color = new Color(1f, 0.6f, 0.1f);
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector3 previous = points[i].position;
            for (int step = 1; step <= 16; step++)
            {
                Vector3 next = PathPoint(points, i, step / 16f);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
        foreach (Transform point in points)
        {
            Gizmos.DrawWireSphere(point.position, 0.3f);
            Gizmos.DrawRay(point.position, point.forward * 1.5f);
        }
    }
#endif
}
