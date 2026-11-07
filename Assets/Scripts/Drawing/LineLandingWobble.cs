using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// A crayon line Dudu has just landed on gives a little: it dips under his feet and springs
// back a couple of times. Only a look. The real line (its points, colliders, network copy)
// is never touched: it is hidden for the moment while a bent copy is drawn, rebuilt every
// frame from the real line, so drawing, erasing or wind during the bounce still shows.
// Started by DuduMoveFeedback.
public sealed class LineLandingWobble : MonoBehaviour
{
    private const float Duration = 0.5f;
    private const float Frequency = 6.5f;
    private const float Damping = 7f;
    private const float Reach = 0.55f;
    private const float MaxSpacing = 0.04f;

    private static readonly Dictionary<LineRenderer, LineLandingWobble> Active =
        new Dictionary<LineRenderer, LineLandingWobble>();

    private readonly List<Vector3> points = new List<Vector3>();
    private MaterialPropertyBlock block;
    private LineRenderer source;
    private LineRenderer copy;
    private Vector3 contact;
    private Vector3 up;
    private float amplitude;
    private float startTime;

    public static void Trigger(LineRenderer line, Vector3 contactPoint, Vector3 surfaceUp, float strength)
    {
        if (line == null || line.positionCount < 2) return;
        if (!Active.TryGetValue(line, out LineLandingWobble wobble) || wobble == null)
        {
            // A line hidden for its own reasons stays hidden.
            if (!line.enabled || !line.gameObject.activeInHierarchy) return;
            GameObject holder = new GameObject(line.gameObject.name + " (bounce)");
            holder.layer = line.gameObject.layer;
            holder.transform.SetPositionAndRotation(line.transform.position, line.transform.rotation);
            wobble = holder.AddComponent<LineLandingWobble>();
            wobble.Begin(line);
            Active[line] = wobble;
        }
        wobble.contact = contactPoint;
        wobble.up = surfaceUp.normalized;
        wobble.amplitude = strength;
        wobble.startTime = Time.time;
        wobble.Rebuild();
        DuduCameraController.RegisterRuntimeRenderer(wobble.copy);
    }

    private void Begin(LineRenderer line)
    {
        source = line;
        block = new MaterialPropertyBlock();
        copy = gameObject.AddComponent<LineRenderer>();
        copy.useWorldSpace = true;
        copy.alignment = source.alignment;
        copy.textureMode = source.textureMode;
        copy.textureScale = source.textureScale;
        copy.numCapVertices = source.numCapVertices;
        copy.numCornerVertices = source.numCornerVertices;
        copy.sharedMaterials = source.sharedMaterials;
        copy.sortingLayerID = source.sortingLayerID;
        copy.sortingOrder = source.sortingOrder;
        copy.shadowCastingMode = ShadowCastingMode.Off;
        copy.receiveShadows = false;
        copy.widthCurve = source.widthCurve;
        copy.widthMultiplier = source.widthMultiplier;
        source.enabled = false;
    }

    private void LateUpdate() => Rebuild();

    private void Rebuild()
    {
        if (source == null)
        {
            Destroy(gameObject);
            return;
        }
        float t = Time.time - startTime;
        int count = source.positionCount;
        if (t >= Duration || count < 2)
        {
            Destroy(gameObject);
            return;
        }

        // Starts pressed down under his feet, then springs back and settles.
        float settle = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Duration * 0.7f, Duration, t));
        float bend = -amplitude * Mathf.Exp(-Damping * t) * Mathf.Cos(2f * Mathf.PI * Frequency * t) * settle;

        // Short lines are split finer so the bend is a curve, not a kink; every segment
        // the same way, so the crayon shape along the line stays where it was.
        float length = 0f;
        Vector3 previous = WorldPoint(0);
        for (int i = 1; i < count; i++)
        {
            Vector3 point = WorldPoint(i);
            length += Vector3.Distance(previous, point);
            previous = point;
        }
        int split = Mathf.Clamp(Mathf.CeilToInt(length / (count - 1) / MaxSpacing), 1, 8);

        points.Clear();
        previous = WorldPoint(0);
        points.Add(Bent(previous, bend));
        for (int i = 1; i < count; i++)
        {
            Vector3 point = WorldPoint(i);
            for (int s = 1; s < split; s++) points.Add(Bent(Vector3.Lerp(previous, point, s / (float)split), bend));
            points.Add(Bent(point, bend));
            previous = point;
        }

        copy.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) copy.SetPosition(i, points[i]);
        copy.colorGradient = source.colorGradient;
        copy.widthMultiplier = source.widthMultiplier;
        source.GetPropertyBlock(block);
        copy.SetPropertyBlock(block);
    }

    private Vector3 WorldPoint(int index) => source.useWorldSpace
        ? source.GetPosition(index)
        : source.transform.TransformPoint(source.GetPosition(index));

    private Vector3 Bent(Vector3 point, float bend)
    {
        Vector3 offset = point - contact;
        float along = (offset - up * Vector3.Dot(offset, up)).magnitude / Reach;
        return point + up * (bend * Mathf.Exp(-along * along));
    }

    private void OnDestroy()
    {
        if (source == null) return;
        if (Active.TryGetValue(source, out LineLandingWobble wobble) && wobble == this)
        {
            Active.Remove(source);
            source.enabled = true;
        }
    }
}
