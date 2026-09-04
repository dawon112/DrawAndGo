using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public sealed class DrawingStroke : MonoBehaviour
{
    private LineRenderer lineRenderer;
    private Material strokeMaterial;
    private float strokeWidth;
    private Color strokeColor;
    private float colliderThickness;
    private float colliderDepth;
    private Vector3 surfaceNormal;
    private int strokeLayer;
    private static int splitNumber;
    private readonly List<BoxCollider> colliderSegments = new List<BoxCollider>();

    public int PointCount => lineRenderer != null ? lineRenderer.positionCount : 0;

    public void Initialize(
        Material material,
        float width,
        Color color,
        Vector3 firstPoint,
        float newColliderThickness,
        float newColliderDepth,
        Vector3 newSurfaceNormal,
        int newStrokeLayer)
    {
        Initialize(
            material,
            width,
            color,
            new[] { firstPoint },
            newColliderThickness,
            newColliderDepth,
            newSurfaceNormal,
            newStrokeLayer);
    }

    private void Initialize(
        Material material,
        float width,
        Color color,
        IReadOnlyList<Vector3> points,
        float newColliderThickness,
        float newColliderDepth,
        Vector3 newSurfaceNormal,
        int newStrokeLayer)
    {
        lineRenderer = GetComponent<LineRenderer>();
        strokeMaterial = material;
        strokeWidth = width;
        strokeColor = color;
        colliderThickness = newColliderThickness;
        colliderDepth = newColliderDepth;
        surfaceNormal = newSurfaceNormal.normalized;
        strokeLayer = newStrokeLayer;
        gameObject.layer = strokeLayer;
        lineRenderer.useWorldSpace = true;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.numCapVertices = 4;
        lineRenderer.numCornerVertices = 4;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.sharedMaterial = material;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++)
            lineRenderer.SetPosition(i, points[i]);
        for (int i = 1; i < points.Count; i++)
            CreateColliderSegment(GetPhysicsPoint(i - 1), GetPhysicsPoint(i), i - 1);
    }

    public void AddPoint(Vector3 point)
    {
        int index = lineRenderer.positionCount;
        lineRenderer.positionCount = index + 1;
        lineRenderer.SetPosition(index, point);
        // Only the former endpoint gains a new neighbour. Update its two segments
        // in place, keeping the rendered points and the rest of the stroke intact.
        if (index >= 2)
            CreateColliderSegment(GetPhysicsPoint(index - 2), GetPhysicsPoint(index - 1), index - 2);
        CreateColliderSegment(GetPhysicsPoint(index - 1), point, index - 1);
    }

    private Vector3 GetPhysicsPoint(int index)
    {
        Vector3 point = lineRenderer.GetPosition(index);
        if (index == 0 || index == lineRenderer.positionCount - 1)
            return point;

        Vector3 previous = lineRenderer.GetPosition(index - 1);
        Vector3 next = lineRenderer.GetPosition(index + 1);
        // Preserve deliberate corners, reversals, and stroke/eraser endpoints.
        if (Vector3.Dot((point - previous).normalized, (next - point).normalized) < 0.5f)
            return point;

        Vector3 offset = ClosestPointOnSegment(point, previous, next) - point;
        float tolerance = Mathf.Min(colliderThickness * 0.25f, 0.015f);
        if (offset.magnitude > tolerance)
            return point;
        return point + offset * 0.5f;
    }

    public bool TryGetSqrDistance(Vector3 point, float radius, out float sqrDistance)
    {
        sqrDistance = float.PositiveInfinity;
        if (lineRenderer == null || lineRenderer.positionCount == 0)
            return false;

        if (lineRenderer.positionCount == 1)
        {
            sqrDistance = (point - lineRenderer.GetPosition(0)).sqrMagnitude;
            return sqrDistance <= radius * radius;
        }

        for (int i = 1; i < lineRenderer.positionCount; i++)
        {
            Vector3 closestPoint = ClosestPointOnSegment(
                point,
                lineRenderer.GetPosition(i - 1),
                lineRenderer.GetPosition(i));
            sqrDistance = Mathf.Min(sqrDistance, (point - closestPoint).sqrMagnitude);
        }

        return sqrDistance <= radius * radius;
    }

    public bool Erase(Vector3 center, float radius)
    {
        if (lineRenderer == null || lineRenderer.positionCount == 0)
            return false;
        if (lineRenderer.positionCount == 1)
        {
            if ((lineRenderer.GetPosition(0) - center).sqrMagnitude > radius * radius)
                return false;
            Destroy(gameObject);
            return true;
        }

        List<List<Vector3>> sections = BuildOutsideSections(center, radius);
        if (sections == null)
            return false;

        Transform parent = transform.parent;
        foreach (List<Vector3> section in sections)
        {
            if (section.Count < 2)
                continue;

            splitNumber++;
            GameObject partObject = new GameObject($"{gameObject.name}_Part_{splitNumber:000}");
            partObject.transform.SetParent(parent, true);
            partObject.AddComponent<LineRenderer>();
            DrawingStroke part = partObject.AddComponent<DrawingStroke>();
            part.Initialize(
                strokeMaterial,
                strokeWidth,
                strokeColor,
                section,
                colliderThickness,
                colliderDepth,
                surfaceNormal,
                strokeLayer);
        }

        Destroy(gameObject);
        return true;
    }

    private List<List<Vector3>> BuildOutsideSections(Vector3 center, float radius)
    {
        float radiusSqr = radius * radius;
        var sections = new List<List<Vector3>>();
        List<Vector3> current = null;
        bool erasedAnything = false;

        for (int i = 1; i < lineRenderer.positionCount; i++)
        {
            Vector3 start = lineRenderer.GetPosition(i - 1);
            Vector3 end = lineRenderer.GetPosition(i);
            if (!TryGetInsideInterval(start, end, center, radiusSqr, out float insideStart, out float insideEnd))
            {
                current ??= new List<Vector3> { start };
                AddDistinct(current, end);
                continue;
            }

            erasedAnything = true;
            if (insideStart > 0f)
            {
                current ??= new List<Vector3> { start };
                AddDistinct(current, Vector3.Lerp(start, end, insideStart));
            }

            FinishSection(sections, ref current);

            if (insideEnd < 1f)
            {
                current = new List<Vector3> { Vector3.Lerp(start, end, insideEnd) };
                AddDistinct(current, end);
            }
        }

        FinishSection(sections, ref current);
        return erasedAnything ? sections : null;
    }

    private static bool TryGetInsideInterval(
        Vector3 start,
        Vector3 end,
        Vector3 center,
        float radiusSqr,
        out float insideStart,
        out float insideEnd)
    {
        Vector3 direction = end - start;
        Vector3 offset = start - center;
        float a = Vector3.Dot(direction, direction);
        float c = Vector3.Dot(offset, offset) - radiusSqr;

        if (a <= Mathf.Epsilon)
        {
            insideStart = 0f;
            insideEnd = 1f;
            return c < 0f;
        }

        float b = 2f * Vector3.Dot(offset, direction);
        float discriminant = b * b - 4f * a * c;
        if (discriminant <= 0f)
        {
            insideStart = 0f;
            insideEnd = 0f;
            return false;
        }

        float sqrt = Mathf.Sqrt(discriminant);
        float first = (-b - sqrt) / (2f * a);
        float second = (-b + sqrt) / (2f * a);
        insideStart = Mathf.Clamp01(first);
        insideEnd = Mathf.Clamp01(second);
        return second > 0f && first < 1f && insideEnd > insideStart;
    }

    private static void AddDistinct(List<Vector3> points, Vector3 point)
    {
        if (points.Count == 0 || (points[^1] - point).sqrMagnitude > 0.00000001f)
            points.Add(point);
    }

    private static void FinishSection(List<List<Vector3>> sections, ref List<Vector3> current)
    {
        if (current != null && current.Count >= 2)
            sections.Add(current);
        current = null;
    }

    private void CreateColliderSegment(Vector3 start, Vector3 end, int index)
    {
        while (colliderSegments.Count <= index)
            colliderSegments.Add(null);
        Vector3 segment = end - start;
        float length = segment.magnitude;
        if (length <= Mathf.Epsilon)
        {
            if (colliderSegments[index] != null)
                colliderSegments[index].enabled = false;
            return;
        }

        Vector3 direction = segment / length;
        Vector3 up = Vector3.Cross(surfaceNormal, direction).normalized;
        if (up.sqrMagnitude <= Mathf.Epsilon)
            return;

        BoxCollider collider = colliderSegments[index];
        if (collider == null)
        {
            GameObject newSegment = new GameObject($"ColliderSegment_{index:000}");
            newSegment.layer = strokeLayer;
            newSegment.transform.SetParent(transform, true);
            collider = newSegment.AddComponent<BoxCollider>();
            colliderSegments[index] = collider;
        }
        GameObject segmentObject = collider.gameObject;
        segmentObject.transform.position = (start + end) * 0.5f;
        segmentObject.transform.rotation = Quaternion.LookRotation(surfaceNormal, up);

        collider.enabled = true;
        collider.size = new Vector3(
            length + colliderThickness * 0.5f,
            colliderThickness,
            colliderDepth);
    }

    private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        float sqrLength = segment.sqrMagnitude;
        if (sqrLength <= Mathf.Epsilon)
            return start;

        float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / sqrLength);
        return start + segment * t;
    }
}
