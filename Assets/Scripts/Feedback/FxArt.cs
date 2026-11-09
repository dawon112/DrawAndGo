using System.Collections.Generic;
using UnityEngine;

public enum FxShape
{
    Soft, Crumb, Streak, Spark, Ring, Chevron, Arrow, Bolt, Target, Swirl,
    SlowFoot, Cross, Lock, Exclaim, Heat, Magnet, Disc, Star, Door, DoorFrame, Box, ScribbleA, ScribbleB, ScribbleC
}

// Small crayon-looking pictures for hazard feedback, drawn in code so no scene or prefab
// needs wiring. All are white (tint with a colour) and one world unit across. To use real
// art for one of them, put a sprite named after the shape in Resources/Fx (e.g. Fx/Bolt).
public static class FxArt
{
    private const int Size = 96;
    private static readonly Dictionary<FxShape, Sprite> Cache = new Dictionary<FxShape, Sprite>();
    private static Material solidMaterial;
    private static Material lineMaterial;

    public static Sprite Get(FxShape shape)
    {
        if (Cache.TryGetValue(shape, out Sprite sprite) && sprite != null) return sprite;
        sprite = Resources.Load<Sprite>("Fx/" + shape);
        if (sprite == null) sprite = Draw(shape);
        Cache[shape] = sprite;
        return sprite;
    }

    // Draws a sprite as one flat colour: flashes, auras and outlines.
    public static Material SolidMaterial
    {
        get
        {
            if (solidMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("FxSolidSprite");
                solidMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default"))
                { name = "Fx Solid Sprite" };
            }
            return solidMaterial;
        }
    }

    public static Material LineMaterial
    {
        get
        {
            if (lineMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("FxLine");
                lineMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default"))
                { name = "Fx Line" };
            }
            return lineMaterial;
        }
    }

    public static LineRenderer NewLine(string name, float width, int sortingOrder, Transform parent = null)
    {
        GameObject holder = new GameObject(name);
        if (parent != null) holder.transform.SetParent(parent, false);
        LineRenderer line = holder.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.sharedMaterial = LineMaterial;
        line.startWidth = width;
        line.endWidth = width;
        line.numCapVertices = 3;
        line.numCornerVertices = 2;
        line.sortingOrder = sortingOrder;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    // A one-colour copy of a sprite, on or behind it (sortingOffset), following its frames.
    public static SpriteRenderer NewOverlay(SpriteRenderer source, string name, int sortingOffset, float scale)
    {
        GameObject holder = new GameObject(name);
        holder.layer = source.gameObject.layer;
        holder.transform.SetParent(source.transform, false);
        holder.transform.localScale = Vector3.one * scale;
        SpriteRenderer overlay = holder.AddComponent<SpriteRenderer>();
        overlay.sharedMaterial = SolidMaterial;
        overlay.sprite = source.sprite;
        overlay.sortingLayerID = source.sortingLayerID;
        overlay.sortingOrder = source.sortingOrder + sortingOffset;
        overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        overlay.receiveShadows = false;
        overlay.color = Color.clear;
        overlay.enabled = false;
        return overlay;
    }

    public static void Sync(SpriteRenderer overlay, SpriteRenderer source, Color color)
    {
        bool show = color.a > 0.004f && source.enabled;
        overlay.enabled = show;
        if (!show) return;
        overlay.sprite = source.sprite;
        overlay.flipX = source.flipX;
        overlay.flipY = source.flipY;
        overlay.color = color;
    }

    private static Sprite Draw(FxShape shape)
    {
        Canvas c = new Canvas(Size, (int)shape * 7 + 3);
        switch (shape)
        {
            case FxShape.Soft: c.SoftBlob(0.48f); break;
            case FxShape.Box: c.Box(0f, 0f, 1f, 1f); break;
            case FxShape.ScribbleA:
                // A quick zigzag, back and forth.
                for (int i = 0; i < 9; i++)
                {
                    float a = 0.12f + i * 0.085f;
                    c.Line(a, i % 2 == 0 ? 0.18f : 0.8f, a + 0.085f, i % 2 == 0 ? 0.8f : 0.18f, 0.018f);
                }
                break;
            case FxShape.ScribbleB:
                c.Spiral(0.5f, 0.5f, 0.06f, 0.4f, 3.2f, 0.016f);
                break;
            case FxShape.ScribbleC:
                // A wobbly loop and a tail, as a child tries out a crayon.
                c.Arc(0.42f, 0.55f, 0.26f, 20f, 340f, 0.018f);
                c.Line(0.66f, 0.47f, 0.92f, 0.2f, 0.018f);
                c.Line(0.92f, 0.2f, 0.74f, 0.14f, 0.018f);
                break;
            case FxShape.Door:
                c.Box(0.24f, 0.02f, 0.76f, 0.7f);
                c.Disc(0.5f, 0.7f, 0.26f);
                break;
            case FxShape.DoorFrame:
                c.Line(0.23f, 0.02f, 0.23f, 0.7f, 0.022f);
                c.Line(0.77f, 0.02f, 0.77f, 0.7f, 0.022f);
                c.Arc(0.5f, 0.7f, 0.27f, 0f, 180f, 0.022f);
                c.Line(0.17f, 0.02f, 0.83f, 0.02f, 0.016f);
                break;
            case FxShape.Star:
                for (int i = 0; i < 5; i++)
                {
                    float a = Mathf.PI * 0.5f + i * Mathf.PI * 2f / 5f;
                    c.Tapered(0.5f, 0.5f, 0.5f + Mathf.Cos(a) * 0.46f, 0.5f + Mathf.Sin(a) * 0.46f, 0.001f);
                    c.Wedge(0.5f, 0.5f, a, 0.46f, 0.17f);
                }
                c.Disc(0.5f, 0.5f, 0.17f);
                break;
            case FxShape.Crumb: c.Crumb(); break;
            case FxShape.Disc: c.Disc(0.5f, 0.5f, 0.46f); break;
            case FxShape.Streak: c.Tapered(0.04f, 0.5f, 0.96f, 0.5f, 0.05f); break;
            case FxShape.Spark:
                c.Tapered(0.5f, 0.04f, 0.5f, 0.96f, 0.055f);
                c.Tapered(0.04f, 0.5f, 0.96f, 0.5f, 0.055f);
                c.Tapered(0.24f, 0.24f, 0.76f, 0.76f, 0.03f);
                c.Tapered(0.24f, 0.76f, 0.76f, 0.24f, 0.03f);
                break;
            case FxShape.Ring: c.Arc(0.5f, 0.5f, 0.42f, 0f, 360f, 0.035f); break;
            case FxShape.Chevron:
                c.Line(0.3f, 0.12f, 0.72f, 0.5f, 0.07f);
                c.Line(0.72f, 0.5f, 0.3f, 0.88f, 0.07f);
                break;
            case FxShape.Arrow:
                c.Line(0.1f, 0.5f, 0.84f, 0.5f, 0.07f);
                c.Line(0.54f, 0.2f, 0.86f, 0.5f, 0.07f);
                c.Line(0.54f, 0.8f, 0.86f, 0.5f, 0.07f);
                break;
            case FxShape.Bolt:
                c.Line(0.64f, 0.92f, 0.34f, 0.5f, 0.075f);
                c.Line(0.34f, 0.5f, 0.64f, 0.5f, 0.075f);
                c.Line(0.64f, 0.5f, 0.38f, 0.08f, 0.075f);
                break;
            case FxShape.Target:
                c.Arc(0.5f, 0.5f, 0.4f, 0f, 360f, 0.016f);
                c.Line(0.5f, 0.98f, 0.5f, 0.82f, 0.024f);
                c.Line(0.5f, 0.02f, 0.5f, 0.18f, 0.024f);
                c.Line(0.02f, 0.5f, 0.18f, 0.5f, 0.024f);
                c.Line(0.98f, 0.5f, 0.82f, 0.5f, 0.024f);
                break;
            case FxShape.Swirl:
                c.Spiral(0.5f, 0.5f, 0.05f, 0.42f, 2.3f, 0.055f);
                break;
            case FxShape.SlowFoot:
                c.Disc(0.34f, 0.42f, 0.17f);
                c.Disc(0.34f, 0.62f, 0.13f);
                c.Disc(0.2f, 0.8f, 0.045f);
                c.Disc(0.31f, 0.84f, 0.05f);
                c.Disc(0.42f, 0.83f, 0.045f);
                c.Line(0.74f, 0.86f, 0.74f, 0.2f, 0.06f);
                c.Line(0.56f, 0.38f, 0.74f, 0.14f, 0.06f);
                c.Line(0.92f, 0.38f, 0.74f, 0.14f, 0.06f);
                break;
            case FxShape.Cross:
                c.Line(0.16f, 0.16f, 0.84f, 0.84f, 0.1f);
                c.Line(0.16f, 0.84f, 0.84f, 0.16f, 0.1f);
                break;
            case FxShape.Lock:
                c.Arc(0.5f, 0.6f, 0.2f, 0f, 180f, 0.06f);
                c.Line(0.3f, 0.6f, 0.3f, 0.48f, 0.06f);
                c.Line(0.7f, 0.6f, 0.7f, 0.48f, 0.06f);
                c.Box(0.18f, 0.1f, 0.82f, 0.52f);
                break;
            case FxShape.Exclaim:
                c.Tapered(0.5f, 0.32f, 0.5f, 0.94f, 0.1f);
                c.Disc(0.5f, 0.13f, 0.085f);
                break;
            case FxShape.Heat:
                for (int i = 0; i < 40; i++)
                {
                    float a = i / 40f, b = (i + 1) / 40f;
                    c.Line(0.5f + Mathf.Sin(a * 9f) * 0.22f, a, 0.5f + Mathf.Sin(b * 9f) * 0.22f, b,
                        0.05f * Mathf.Sin(a * Mathf.PI));
                }
                break;
            case FxShape.Magnet:
                c.Arc(0.5f, 0.56f, 0.27f, 180f, 360f, 0.1f);
                c.Line(0.23f, 0.56f, 0.23f, 0.88f, 0.1f);
                c.Line(0.77f, 0.56f, 0.77f, 0.88f, 0.1f);
                break;
        }
        // Solid areas stay solid: wax grain on them would show through as speckles.
        return c.Bake(shape.ToString(), shape != FxShape.Box && shape != FxShape.Door);
    }

    // Procedural pattern for one No Draw zone, sized to it: pencil hatching and a dashed edge.
    public static Sprite HatchFor(Vector2 worldSize)
    {
        const float pixelsPerUnit = 48f;
        int w = Mathf.Clamp(Mathf.CeilToInt(worldSize.x * pixelsPerUnit), 8, 1024);
        int h = Mathf.Clamp(Mathf.CeilToInt(worldSize.y * pixelsPerUnit), 8, 1024);
        Color32[] pixels = new Color32[w * h];
        const float spacing = 18f, thick = 2.4f;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float wobble = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 3f;
            float band = Mathf.Repeat(x + y + wobble, spacing);
            float hatch = Mathf.Clamp01(thick - Mathf.Abs(band - spacing * 0.5f)) *
                (0.65f + 0.35f * Mathf.PerlinNoise(x * 0.3f, y * 0.3f));
            float edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
            bool dash = Mathf.Repeat(x + y * 1.01f, 26f) < 15f;
            float border = edge < 3f && dash ? 1f : 0f;
            byte alpha = (byte)(Mathf.Max(hatch, border) * 255f);
            pixels[y * w + x] = new Color32(255, 255, 255, alpha);
        }
        Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
        { name = "No Draw Hatch", wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
    }

    private sealed class Canvas
    {
        private readonly int n;
        private readonly float[] alpha;
        private readonly float seed;

        public Canvas(int size, int seedValue)
        {
            n = size;
            alpha = new float[n * n];
            seed = seedValue * 1.37f;
        }

        public void Disc(float cx, float cy, float r)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt((cx - r) * n) - 2), maxX = Mathf.Min(n - 1, Mathf.CeilToInt((cx + r) * n) + 2);
            int minY = Mathf.Max(0, Mathf.FloorToInt((cy - r) * n) - 2), maxY = Mathf.Min(n - 1, Mathf.CeilToInt((cy + r) * n) + 2);
            float rPixels = r * n;
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - cx * n) * (x + 0.5f - cx * n) + (y + 0.5f - cy * n) * (y + 0.5f - cy * n));
                float a = Mathf.Clamp01(rPixels - d + 0.5f);
                if (a > alpha[y * n + x]) alpha[y * n + x] = a;
            }
        }

        public void Line(float x0, float y0, float x1, float y1, float r)
        {
            float length = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            int steps = Mathf.Max(2, Mathf.CeilToInt(length * n));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float wobble = Mathf.PerlinNoise(seed + t * 4f, seed) * 1.4f - 0.2f;
                Disc(Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), r * Mathf.Lerp(0.8f, 1.1f, Mathf.Clamp01(wobble)));
            }
        }

        public void Tapered(float x0, float y0, float x1, float y1, float r)
        {
            const int steps = 80;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Disc(Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), r * Mathf.Sin(t * Mathf.PI));
            }
        }

        public void Arc(float cx, float cy, float radius, float from, float to, float r)
        {
            int steps = Mathf.CeilToInt(Mathf.Abs(to - from) * Mathf.Deg2Rad * radius * n * 1.5f);
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(from, to, i / (float)steps) * Mathf.Deg2Rad;
                float wobble = 1f + (Mathf.PerlinNoise(seed + a, seed) - 0.5f) * 0.05f;
                Disc(cx + Mathf.Cos(a) * radius * wobble, cy + Mathf.Sin(a) * radius * wobble, r);
            }
        }

        public void Spiral(float cx, float cy, float from, float to, float turns, float r)
        {
            int steps = Mathf.CeilToInt(turns * 200f);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float a = t * turns * Mathf.PI * 2f, radius = Mathf.Lerp(from, to, t);
                Disc(cx + Mathf.Cos(a) * radius, cy + Mathf.Sin(a) * radius, r);
            }
        }

        // A point of a star: wide at the centre, sharp at its tip.
        public void Wedge(float cx, float cy, float angle, float length, float width)
        {
            const int Steps = 90;
            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                Disc(cx + Mathf.Cos(angle) * length * t, cy + Mathf.Sin(angle) * length * t, width * (1f - t) * 0.72f + 0.005f);
            }
        }

        public void Box(float x0, float y0, float x1, float y1)
        {
            for (int y = Mathf.FloorToInt(y0 * n); y < Mathf.CeilToInt(y1 * n); y++)
            for (int x = Mathf.FloorToInt(x0 * n); x < Mathf.CeilToInt(x1 * n); x++)
                alpha[Mathf.Clamp(y, 0, n - 1) * n + Mathf.Clamp(x, 0, n - 1)] = 1f;
        }

        public void SoftBlob(float radius)
        {
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                d += (Mathf.PerlinNoise(x * 0.09f + seed, y * 0.09f) - 0.5f) * 0.35f;
                float a = Mathf.Clamp01(1f - d);
                alpha[y * n + x] = a * a * (3f - 2f * a);
            }
        }

        public void Crumb()
        {
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                float angle = Mathf.Atan2(dy, dx);
                float edge = 0.28f + Mathf.PerlinNoise(angle * 1.4f + 3f, seed) * 0.2f;
                alpha[y * n + x] = Mathf.Clamp01((edge - Mathf.Sqrt(dx * dx + dy * dy)) * n);
            }
        }

        public Sprite Bake(string name, bool waxGrain)
        {
            Color32[] pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Waxy grain: every shape looks rubbed on with a crayon.
                float grain = waxGrain ? 0.8f + 0.2f * Mathf.PerlinNoise(x * 0.45f + seed, y * 0.45f) : 1f;
                pixels[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha[y * n + x] * grain) * 255f));
            }
            Texture2D texture = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { name = "Fx " + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), n, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }
    }
}
