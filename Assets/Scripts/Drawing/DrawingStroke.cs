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
    private bool isFinished;
    private bool isFading;
    private float fadeStartedAt;
    private float fadeDuration;
    private Color fadeStartColor;
    private Color fadeEndColor;
    private static int splitNumber;
    private static int nextSourceStrokeId;
    private static int nextNetworkId;
    public int NetworkStrokeId { get; private set; }
    private readonly List<BoxCollider> colliderSegments = new List<BoxCollider>();
    private const float BlobLength = 0.045f;
    private const float BlobWidth = 1.3f;
    private const float BlobShade = 0.72f;
    private const float WobbleSpacing = 0.05f;
    private const float WidthWobble = 0.12f;
    private float length;
    private float crayonSeed;
    // Erased pieces are cut, not lifted: no wax pile at their ends.
    private bool crayonEnds = true;

    private bool isErased;

    public int PointCount => lineRenderer != null ? lineRenderer.positionCount : 0;
    public bool IsFinished => isFinished;
    // Destroy is deferred to the end of the frame. Once erased, a stroke must be
    // ignored by every later query in the same frame or it would be split again.
    public bool IsErased => isErased;
    public float ColliderThickness => colliderThickness;
    public float ColliderDepth => colliderDepth;
    public Vector3 SurfaceNormal => surfaceNormal;
    // Sum of every rigid move (wind) since creation, so peers can replay it.
    public Vector3 TotalTranslation { get; private set; }
    // A copy of a stroke owned by the host. It never simulates or destroys itself;
    // the host's stroke messages are its only source of change.
    public bool IsNetworkMirror { get; private set; }
    // Erased fragments retain their source identity for scene-local objectives.
    public int SourceStrokeId { get; private set; }

    public void MarkAsNetworkMirror() => IsNetworkMirror = true;

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
        SourceStrokeId = ++nextSourceStrokeId;
        if (NetworkStrokeId == 0) NetworkStrokeId = ++nextNetworkId;
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
        lineRenderer.textureMode = LineTextureMode.Tile;
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
        length = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            lineRenderer.SetPosition(i, points[i]);
            if (i > 0) length += Vector3.Distance(points[i - 1], points[i]);
        }
        // Same on every peer: the wobble follows where the line was started.
        crayonSeed = (points[0].x * 12.9898f + points[0].y * 78.233f + points[0].z * 37.719f) % 97f;
        ApplyCrayonShape();
        for (int i = 1; i < points.Count; i++)
            CreateColliderSegment(GetPhysicsPoint(i - 1), GetPhysicsPoint(i), i - 1);
        TryStartHotZoneFade(firstPoint: points[0]);
    }

    public void AddPoint(Vector3 point)
    {
        int index = lineRenderer.positionCount;
        if (index > 0) length += Vector3.Distance(lineRenderer.GetPosition(index - 1), point);
        lineRenderer.positionCount = index + 1;
        lineRenderer.SetPosition(index, point);
        ApplyCrayonShape();
        // Only the former endpoint gains a new neighbour. Update its two segments
        // in place, keeping the rendered points and the rest of the stroke intact.
        if (index >= 2)
            CreateColliderSegment(GetPhysicsPoint(index - 2), GetPhysicsPoint(index - 1), index - 2);
        CreateColliderSegment(GetPhysicsPoint(index - 1), point, index - 1);
        TryStartHotZoneFade(point);
    }

    public void FinishDrawing()
    {
        isFinished = true;
    }

    // A real crayon line is not perfectly even: its width drifts a little along the line,
    // and where the crayon was pressed down and lifted the wax piles up thicker and darker.
    // The very first and last points keep the plain width and colour, because network sync
    // and Dudu's 2D view read the line's start width and colour as its base values.
    private void ApplyCrayonShape()
    {
        if (lineRenderer == null || isFading) return;
        lineRenderer.widthMultiplier = strokeWidth;
        if (length < 0.005f)
        {
            lineRenderer.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            lineRenderer.startColor = strokeColor;
            lineRenderer.endColor = strokeColor;
            return;
        }

        float blob = crayonEnds ? Mathf.Min(BlobLength, length * 0.3f) / length : 0f;
        var keys = new List<Keyframe> { new Keyframe(0f, 1f) };
        if (blob > 0f) keys.Add(new Keyframe(blob * 0.5f, BlobWidth));
        for (float along = WobbleSpacing; along < length - WobbleSpacing * 0.5f; along += WobbleSpacing)
        {
            float t = along / length;
            if (t <= blob || t >= 1f - blob) continue;
            keys.Add(new Keyframe(t, 1f + (Mathf.PerlinNoise(crayonSeed + along * 9f, crayonSeed) - 0.5f) * 2f * WidthWobble));
        }
        if (blob > 0f) keys.Add(new Keyframe(1f - blob * 0.5f, BlobWidth));
        keys.Add(new Keyframe(1f, 1f));
        AnimationCurve curve = new AnimationCurve(keys.ToArray());
        for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
        lineRenderer.widthCurve = curve;

        Color dark = new Color(strokeColor.r * BlobShade, strokeColor.g * BlobShade, strokeColor.b * BlobShade, strokeColor.a);
        Gradient gradient = new Gradient();
        gradient.SetKeys(blob > 0f
            ? new[]
            {
                new GradientColorKey(strokeColor, 0f), new GradientColorKey(dark, blob * 0.5f),
                new GradientColorKey(strokeColor, blob), new GradientColorKey(strokeColor, 1f - blob),
                new GradientColorKey(dark, 1f - blob * 0.5f), new GradientColorKey(strokeColor, 1f)
            }
            : new[] { new GradientColorKey(strokeColor, 0f), new GradientColorKey(strokeColor, 1f) },
            new[] { new GradientAlphaKey(strokeColor.a, 0f), new GradientAlphaKey(strokeColor.a, 1f) });
        lineRenderer.colorGradient = gradient;
    }

    private void Update()
    {
        if (!isFading || lineRenderer == null) return;
        float progress = Mathf.Clamp01((Time.time - fadeStartedAt) / fadeDuration);
        Color start = fadeStartColor;
        Color end = fadeEndColor;
        start.a *= 1f - progress;
        end.a *= 1f - progress;
        lineRenderer.startColor = start;
        lineRenderer.endColor = end;
        // A mirror only shows the fade; the host's delete message removes its colliders.
        if (progress >= 1f && !IsNetworkMirror) Destroy(gameObject);
    }

    private void FixedUpdate()
    {
        if (lineRenderer == null || lineRenderer.positionCount == 0)
            return;

        if (!isFading)
        {
            for (int i = 0; i < lineRenderer.positionCount; i++)
            {
                if (TryStartHotZoneFade(lineRenderer.GetPosition(i))) break;
                if (i + 1 >= lineRenderer.positionCount) continue;
                Vector3 midpoint = (lineRenderer.GetPosition(i) + lineRenderer.GetPosition(i + 1)) * 0.5f;
                if (TryStartHotZoneFade(midpoint)) break;
            }
        }

        if (!isFinished || IsNetworkMirror)
            return;

        Vector3 windVelocity = Vector3.zero;
        for (int i = 0; i < lineRenderer.positionCount; i++)
        {
            windVelocity = WindZone.GetWorldVelocity(lineRenderer.GetPosition(i), surfaceNormal);
            if (windVelocity.sqrMagnitude > 0.000001f) break;

            if (i + 1 < lineRenderer.positionCount)
            {
                Vector3 midpoint = (lineRenderer.GetPosition(i) + lineRenderer.GetPosition(i + 1)) * 0.5f;
                windVelocity = WindZone.GetWorldVelocity(midpoint, surfaceNormal);
                if (windVelocity.sqrMagnitude > 0.000001f) break;
            }
        }

        if (windVelocity.sqrMagnitude <= 0.000001f)
            return;

        // Translate the entire stroke rigidly. Moving only the points inside the
        // zone pins them at its boundary and destroys the shape of the drawing.
        Translate(windVelocity * Time.fixedDeltaTime);
    }

    public void Translate(Vector3 displacement)
    {
        if (lineRenderer == null || displacement.sqrMagnitude <= Mathf.Epsilon)
            return;

        for (int i = 0; i < lineRenderer.positionCount; i++)
            lineRenderer.SetPosition(i, lineRenderer.GetPosition(i) + displacement);
        // Physics points are translation invariant, so the segments can move as-is.
        foreach (BoxCollider segment in colliderSegments)
            if (segment != null)
                segment.transform.position += displacement;
        TotalTranslation += displacement;
    }

    private bool TryStartHotZoneFade(Vector3 firstPoint)
    {
        if (isFading || !HotZone.TryGetFadeDuration(firstPoint, surfaceNormal, out float duration))
            return false;
        isFading = true;
        fadeStartedAt = Time.time;
        fadeDuration = duration;
        fadeStartColor = lineRenderer.startColor;
        fadeEndColor = lineRenderer.endColor;
        return true;
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
        if (isErased || lineRenderer == null || lineRenderer.positionCount == 0)
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

    // Erases every stroke touched by a circle swept from start to end. Strokes and
    // fragments are tracked in one list so each piece is split at most once per point.
    public static bool EraseAlongSegment(Vector3 start, Vector3 end, float radius)
    {
        var strokes = new List<DrawingStroke>(FindObjectsByType<DrawingStroke>());
        var created = new List<DrawingStroke>();
        float distance = Vector3.Distance(start, end);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(0.02f, radius * 0.5f)));
        bool erasedAny = false;
        for (int step = 0; step <= steps; step++)
        {
            Vector3 point = Vector3.Lerp(start, end, step / (float)steps);
            created.Clear();
            for (int i = strokes.Count - 1; i >= 0; i--)
            {
                DrawingStroke stroke = strokes[i];
                if (stroke == null || stroke.isErased || stroke.IsNetworkMirror)
                    strokes.RemoveAt(i);
                else if (stroke.Erase(point, radius, created))
                {
                    erasedAny = true;
                    strokes.RemoveAt(i);
                }
            }
            strokes.AddRange(created);
        }
        return erasedAny;
    }

    public bool Erase(Vector3 center, float radius, List<DrawingStroke> createdParts = null)
    {
        if (isErased || lineRenderer == null || lineRenderer.positionCount == 0)
            return false;
        if (lineRenderer.positionCount == 1)
        {
            if ((lineRenderer.GetPosition(0) - center).sqrMagnitude > radius * radius)
                return false;
            isErased = true;
            Destroy(gameObject);
            return true;
        }

        List<List<Vector3>> sections = BuildOutsideSections(center, radius);
        if (sections == null)
            return false;
        // The rubbed-out bit smears and fades instead of vanishing at once.
        ErasedLineFade.Spawn(lineRenderer, center, radius);

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
            part.crayonEnds = false;
            part.Initialize(
                strokeMaterial,
                strokeWidth,
                strokeColor,
                section,
                colliderThickness,
                colliderDepth,
                surfaceNormal,
                strokeLayer);
            part.FinishDrawing();
            part.SourceStrokeId = SourceStrokeId;
            createdParts?.Add(part);
        }

        isErased = true;
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
