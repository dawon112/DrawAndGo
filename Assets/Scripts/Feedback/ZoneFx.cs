using UnityEngine;

// Wind Zone: the air itself is moving. Streaks and bits of paper blow across the zone, drawn
// lines caught in it shed crayon dust and show which way they are being pushed.
[DisallowMultipleComponent]
public sealed class WindZoneFx : MonoBehaviour
{
    private static readonly Color Air = new Color(0.62f, 0.8f, 0.98f, 0.9f);
    private static readonly Color Marker = new Color(0.45f, 0.82f, 1f, 0.95f);
    private static readonly Color[] Bits =
    {
        new Color(0.95f, 0.93f, 0.85f, 0.95f), new Color(0.78f, 0.86f, 1f, 0.9f), new Color(0.92f, 0.72f, 0.55f, 0.9f)
    };

    private WindZone zone;
    private BoxCollider box;
    private AudioSource loop;
    private float nextStreak, nextBit, nextArrow, nextStrokeCheck;

    private void Awake()
    {
        zone = GetComponent<WindZone>();
        box = GetComponent<BoxCollider>();
        loop = FxSound.AddLoop(gameObject, FxClip.WindLoop);
    }

    private void Update()
    {
        DuduSurface surface = zone.Surface;
        if (surface == null || box == null) return;
        Rect rect = FxWall.ZoneRect(box, surface);
        Vector2 flow = zone.Direction;
        float force = zone.Force;
        float angle = FxWall.Angle(flow);
        Vector3 direction = FxWall.Direction(surface, flow);
        float now = Time.time;

        FxSound.SetLoop(loop, Mathf.Clamp(0.09f + force * 0.05f, 0f, 0.28f) * FxView.Attenuation(FxWall.Point(surface, rect.center)));

        // More air the stronger it blows and the bigger the zone. Everything stays inside the
        // zone: the air outside it is still.
        float sizeFactor = Mathf.Clamp(rect.width * rect.height / 6f, 0.5f, 3f);
        if (now >= nextStreak)
        {
            nextStreak = now + 0.1f / (Mathf.Clamp(force, 0.5f, 2.5f) * sizeFactor);
            float speed = 1.2f + force * 1.6f;
            Vector2 start = FxWall.RandomIn(rect);
            float room = FxWall.DistanceToExit(rect, start, flow);
            float size = Mathf.Min(Random.Range(0.5f, 1f), room);
            float life = Mathf.Clamp((room - size * 0.5f) / speed, 0.18f, 0.9f);
            FxRunner.Spawn(FxArt.Get(FxShape.Streak), FxWall.Point(surface, start),
                FxWall.Rotation(surface, angle), direction * speed, life, size, size, Air, FxLayer.Behind,
                stretch: new Vector2(1f, 1.5f), fadeIn: true);
        }
        if (now >= nextBit)
        {
            nextBit = now + 0.22f / sizeFactor;
            float speed = 0.9f + force * 1.1f;
            Vector2 start = FxWall.RandomIn(rect);
            float cross = Vector2.Dot(start - rect.center, new Vector2(-flow.y, flow.x));
            FxRunner.Spawn(FxArt.Get(FxShape.Crumb), FxWall.Point(surface, start), FxWall.Rotation(surface, Random.Range(0f, 360f)),
                direction * speed + surface.Up.normalized * Mathf.Sin(cross * 3f) * 0.2f,
                Mathf.Clamp(FxWall.DistanceToExit(rect, start, flow) / speed, 0.2f, 1.2f),
                0.15f, 0.1f, Bits[Random.Range(0, Bits.Length)], FxLayer.Behind, spin: Random.Range(-400f, 400f), fadeIn: true);
        }
        if (now >= nextArrow)
        {
            nextArrow = now + 0.6f / sizeFactor;
            float speed = 0.5f + force * 0.5f;
            Vector2 start = FxWall.RandomIn(rect);
            FxRunner.Spawn(FxArt.Get(FxShape.Chevron), FxWall.Point(surface, start), FxWall.Rotation(surface, angle),
                direction * speed, Mathf.Clamp(FxWall.DistanceToExit(rect, start, flow) / speed, 0.3f, 1.2f), 0.34f, 0.38f,
                new Color(Marker.r, Marker.g, Marker.b, 0.85f), FxLayer.Behind, fadeIn: true);
        }
        if (now >= nextStrokeCheck)
        {
            nextStrokeCheck = now + 0.18f;
            ShowPushedStrokes(surface, direction, angle);
        }
    }

    private void ShowPushedStrokes(DuduSurface surface, Vector3 direction, float angle)
    {
        foreach (DrawingStroke stroke in DrawingStroke.Live)
        {
            if (stroke == null || stroke.Line == null || !stroke.IsFinished || stroke.PointCount < 2) continue;
            if (!zone.TouchesStroke(stroke, out Vector3 inside)) continue;

            LineRenderer line = stroke.Line;
            Vector3 onWall = FxWall.OnWall(surface, inside, FxWall.Depth + 0.02f);
            // Crayon dust blows off the line, and a marker says which way it is going.
            FxRunner.Spawn(FxArt.Get(FxShape.Crumb), onWall, FxWall.Rotation(surface, Random.Range(0f, 360f)),
                direction * Random.Range(1.2f, 2.4f) + surface.Up.normalized * Random.Range(-0.2f, 0.35f),
                0.55f, 0.11f, 0.05f, line.startColor, FxLayer.Front, 1.2f, 0f, 300f);
            FxRunner.Spawn(FxArt.Get(FxShape.Arrow), onWall + surface.Up.normalized * 0.26f, FxWall.Rotation(surface, angle),
                direction * 0.5f, 0.42f, 0.3f, 0.34f, Marker, FxLayer.Front, fadeIn: true);
            // A wisp streaming from the line's trailing end.
            Vector3 first = line.GetPosition(0), last = line.GetPosition(line.positionCount - 1);
            Vector3 tail = Vector3.Dot(first - last, direction) < 0f ? first : last;
            FxRunner.Spawn(FxArt.Get(FxShape.Streak), FxWall.OnWall(surface, tail), FxWall.Rotation(surface, angle),
                direction * 0.9f, 0.4f, 0.5f, 0.75f, new Color(line.startColor.r, line.startColor.g, line.startColor.b, 0.5f), FxLayer.Behind, fadeIn: true);
        }
    }
}

// Hot Zone: shimmering heat over the zone. Lines in it go through their stages inside
// DrawingStroke (pale, red and blinking, crumbled away); here they smoke and sizzle.
[DisallowMultipleComponent]
public sealed class HotZoneFx : MonoBehaviour
{
    private static readonly Color Heat = new Color(1f, 0.5f, 0.12f, 0.55f);
    private static readonly Color Ember = new Color(1f, 0.42f, 0.1f, 0.95f);

    private HotZone zone;
    private BoxCollider box;
    private AudioSource sizzle;
    private float nextHeat, nextEmber, nextStrokeCheck;
    private float heatOnStrokes;

    private void Awake()
    {
        zone = GetComponent<HotZone>();
        box = GetComponent<BoxCollider>();
        sizzle = FxSound.AddLoop(gameObject, FxClip.SizzleLoop);
    }

    private void Update()
    {
        DuduSurface surface = zone.Surface;
        if (surface == null || box == null) return;
        Rect rect = FxWall.ZoneRect(box, surface);
        float now = Time.time;
        Vector3 up = surface.Up.normalized;

        if (now >= nextHeat)
        {
            nextHeat = now + 0.14f;
            Vector2 start = new Vector2(Random.Range(rect.xMin, rect.xMax), Random.Range(rect.yMin, rect.center.y));
            FxRunner.Spawn(FxArt.Get(FxShape.Heat), FxWall.Point(surface, start), FxWall.Rotation(surface), up * Random.Range(0.5f, 0.9f),
                Random.Range(0.9f, 1.3f), 0.6f, 0.95f, Heat, FxLayer.Behind, flipX: Random.value < 0.5f, fadeIn: true);
        }
        if (now >= nextEmber)
        {
            nextEmber = now + 0.3f;
            FxRunner.Spawn(FxArt.Get(FxShape.Crumb), FxWall.Point(surface, new Vector2(Random.Range(rect.xMin, rect.xMax), rect.yMin + 0.1f)),
                FxWall.Rotation(surface, Random.Range(0f, 360f)), up * Random.Range(0.5f, 1f) + surface.Right.normalized * Random.Range(-0.2f, 0.2f),
                Random.Range(0.7f, 1.1f), 0.07f, 0.03f, Ember, FxLayer.Behind, -0.1f, spin: 200f);
        }
        if (now >= nextStrokeCheck)
        {
            nextStrokeCheck = now + 0.1f;
            heatOnStrokes = 0f;
            foreach (DrawingStroke stroke in DrawingStroke.Live)
            {
                if (stroke == null || !stroke.IsFading || stroke.Line == null || stroke.PointCount == 0) continue;
                Vector3 point = stroke.Line.GetPosition(Random.Range(0, stroke.PointCount));
                if (!rect.Contains(surface.WorldToSurface(point))) continue;
                heatOnStrokes = Mathf.Max(heatOnStrokes, stroke.FadeProgress);
                Vector3 onWall = FxWall.OnWall(surface, point, FxWall.Depth + 0.02f);
                // The wax smokes and melts as it goes.
                FxRunner.Spawn(FxArt.Get(FxShape.Heat), onWall, FxWall.Rotation(surface), up * 0.7f, 0.5f, 0.34f, 0.55f,
                    new Color(1f, 0.5f, 0.15f, 0.6f), FxLayer.Front, fadeIn: true);
                FxRunner.Spawn(FxArt.Get(FxShape.Soft), onWall, FxWall.Rotation(surface, Random.Range(0f, 360f)), up * 0.25f,
                    0.45f, 0.1f, 0.26f, new Color(stroke.Line.startColor.r, stroke.Line.startColor.g, stroke.Line.startColor.b, 0.4f), FxLayer.Behind);
            }
        }
        FxSound.SetLoop(sizzle, heatOnStrokes > 0f ? Mathf.Lerp(0.1f, 0.3f, heatOnStrokes) * FxView.Attenuation(FxWall.Point(surface, rect.center)) : 0f);
    }
}

// No Draw Zone: pencil hatching over the zone, which flares when Haru tries to draw in it.
[DefaultExecutionOrder(210)]
[DisallowMultipleComponent]
public sealed class NoDrawZoneFx : MonoBehaviour
{
    private const float RestAlpha = 0.26f;

    private NoDrawZone zone;
    private BoxCollider box;
    private SpriteRenderer hatch;
    private float flare;

    private void Awake()
    {
        zone = GetComponent<NoDrawZone>();
        box = GetComponent<BoxCollider>();
    }

    private void Start()
    {
        DuduSurface surface = zone.Surface;
        if (surface == null || box == null) return;
        Rect rect = FxWall.ZoneRect(box, surface);
        GameObject holder = new GameObject("No Draw Hatch");
        holder.transform.SetParent(FxRunner.Instance.transform, false);
        holder.transform.SetPositionAndRotation(FxWall.Point(surface, rect.center, FxWall.Depth - 0.01f), FxWall.Rotation(surface));
        hatch = holder.AddComponent<SpriteRenderer>();
        hatch.sharedMaterial = FxArt.SolidMaterial;
        hatch.sprite = FxArt.HatchFor(rect.size);
        hatch.color = new Color(1f, 1f, 1f, RestAlpha);
        hatch.sortingOrder = 6;
    }

    private void OnDisable()
    {
        if (hatch != null) hatch.enabled = false;
    }

    private void OnEnable()
    {
        if (hatch != null) hatch.enabled = true;
    }

    private void OnDestroy()
    {
        if (hatch == null) return;
        if (hatch.sprite != null)
        {
            Destroy(hatch.sprite.texture);
            Destroy(hatch.sprite);
        }
        Destroy(hatch.gameObject);
    }

    // Haru tried to draw here.
    public void Refused(Vector3 point)
    {
        flare = 1f;
        DuduSurface surface = zone.Surface;
        if (surface == null) return;
        Vector3 onWall = FxWall.OnWall(surface, point, FxWall.Depth + 0.03f);
        FxRunner.Spawn(FxArt.Get(FxShape.Cross), onWall, FxWall.Rotation(surface), Vector3.zero, 0.3f, 0.26f, 0.42f,
            new Color(1f, 0.25f, 0.2f, 0.95f), FxLayer.Top);
        FxRunner.Spawn(FxArt.Get(FxShape.Lock), onWall + surface.Up.normalized * 0.3f, FxWall.Rotation(surface),
            surface.Up.normalized * 0.45f, 0.5f, 0.2f, 0.3f, new Color(0.92f, 0.92f, 0.95f, 0.95f), FxLayer.Top);
    }

    private void Update()
    {
        if (hatch == null) return;
        flare = Mathf.MoveTowards(flare, 0f, Time.deltaTime * 3f);
        hatch.color = Color.Lerp(new Color(1f, 1f, 1f, RestAlpha), new Color(1f, 0.32f, 0.26f, 0.85f), flare);
    }
}

// Purple and Grey Fog: the patch itself gives off its kind of smoke, so the two read
// differently: purple curls about, grey sinks and sits heavily.
[DisallowMultipleComponent]
public sealed class StainFx : MonoBehaviour
{
    private DuduStainObstacle stain;
    private SpriteRenderer art;
    private float next;

    private void Awake()
    {
        stain = GetComponent<DuduStainObstacle>();
        art = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        DuduSurface surface = stain.Surface;
        if (surface == null || Time.time < next) return;
        next = Time.time + 0.16f;
        bool reverse = stain.Effect == DuduStainObstacle.EffectType.ReverseControls;
        Vector3 center = art != null ? art.bounds.center : transform.position;
        float reach = art != null ? Mathf.Max(art.bounds.extents.x, 0.2f) * 0.75f : 0.5f;
        Vector2 offset = Random.insideUnitCircle * reach;
        Vector3 position = FxWall.OnWall(surface, center, FxWall.Depth - 0.01f) + FxWall.Direction(surface, offset);
        if (reverse)
            FxRunner.Spawn(FxArt.Get(FxShape.Swirl), position, FxWall.Rotation(surface, Random.Range(0f, 360f)),
                surface.Up.normalized * Random.Range(0.1f, 0.35f), Random.Range(0.7f, 1.1f), 0.12f, 0.32f,
                new Color(0.7f, 0.32f, 1f, 0.5f), FxLayer.Behind, spin: Random.Range(120f, 260f), fadeIn: true);
        else
            FxRunner.Spawn(FxArt.Get(FxShape.Soft), position, FxWall.Rotation(surface, Random.Range(0f, 360f)),
                -surface.Up.normalized * Random.Range(0.05f, 0.2f), Random.Range(0.9f, 1.3f), 0.22f, 0.5f,
                new Color(0.48f, 0.5f, 0.55f, 0.42f), FxLayer.Behind, fadeIn: true);
    }
}
