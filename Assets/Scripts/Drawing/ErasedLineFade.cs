using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The part of a crayon line an eraser just rubbed out, kept for a moment as a fading
// smear: it spreads a little wider and lighter, then is gone. Only a look; the real
// line is already split. Spawned by DrawingStroke.Erase (Haru's eraser and the
// Moving Eraser hazard alike).
public sealed class ErasedLineFade : MonoBehaviour
{
    private const float Duration = 0.45f;
    private const float Spread = 0.8f;
    private const float StartAlpha = 0.75f;

    private LineRenderer line;
    private float width;
    private Color color;
    private float age;

    public static void Spawn(LineRenderer source, Vector3 center, float radius)
    {
        foreach (List<Vector3> piece in PiecesInside(source, center, radius))
        {
            if (piece.Count < 2) continue;
            GameObject smear = new GameObject("Erased Smear");
            smear.layer = source.gameObject.layer;
            LineRenderer copy = smear.AddComponent<LineRenderer>();
            copy.useWorldSpace = true;
            copy.alignment = source.alignment;
            copy.textureMode = source.textureMode;
            copy.numCapVertices = source.numCapVertices;
            copy.numCornerVertices = source.numCornerVertices;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            copy.sharedMaterial = source.sharedMaterial;
            copy.positionCount = piece.Count;
            copy.SetPositions(piece.ToArray());
            ErasedLineFade fade = smear.AddComponent<ErasedLineFade>();
            fade.line = copy;
            fade.width = source.widthMultiplier * source.widthCurve.Evaluate(0.5f);
            fade.color = source.startColor;
            fade.Apply(0f);
            DuduCameraController.RegisterRuntimeRenderer(copy);
        }
    }

    // Walks the line in small steps and keeps the runs that lie inside the eraser.
    private static List<List<Vector3>> PiecesInside(LineRenderer source, Vector3 center, float radius)
    {
        List<List<Vector3>> pieces = new List<List<Vector3>>();
        List<Vector3> current = null;
        float step = Mathf.Max(0.004f, radius * 0.1f);
        float radiusSqr = radius * radius;
        for (int i = 1; i < source.positionCount; i++)
        {
            Vector3 a = source.GetPosition(i - 1);
            Vector3 b = source.GetPosition(i);
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / step));
            for (int s = i == 1 ? 0 : 1; s <= samples; s++)
            {
                Vector3 point = Vector3.Lerp(a, b, s / (float)samples);
                if ((point - center).sqrMagnitude <= radiusSqr)
                {
                    if (current == null) pieces.Add(current = new List<Vector3>());
                    current.Add(point);
                }
                else current = null;
            }
        }
        return pieces;
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= Duration)
        {
            Destroy(gameObject);
            return;
        }
        Apply(age / Duration);
    }

    private void Apply(float t)
    {
        // Spreads fast at first, fades slower: rubbed wax smudging into the wall.
        float spread = 1f - (1f - t) * (1f - t);
        line.widthMultiplier = width * (1f + Spread * spread);
        Color now = color;
        now.a *= StartAlpha * (1f - t) * (1f - t * 0.5f);
        line.startColor = now;
        line.endColor = now;
    }
}
