using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum FxLayer
{
    Behind = 0, // under Dudu and the crayon lines
    Front = 1,  // over Dudu and the lines
    Top = 2     // over everything on the wall
}

// Which of the two players this PC is showing, so effects can be aimed at the right one.
public static class FxView
{
    private static GameViewManager view;
    private static DuduSurfaceMovement dudu;
    private static Camera haruCamera;

    private static GameViewManager View
    {
        get
        {
            if (view == null) view = Object.FindAnyObjectByType<GameViewManager>(FindObjectsInactive.Include);
            return view;
        }
    }

    public static DuduSurfaceMovement Dudu
    {
        get
        {
            if (dudu == null) dudu = Object.FindAnyObjectByType<DuduSurfaceMovement>(FindObjectsInactive.Include);
            return dudu;
        }
    }

    public static bool LocalIsDudu => View != null && View.IsDuduMode;
    public static bool HaruGameplay => View != null && View.IsHaruGameplayView;

    public static Vector3 Listener
    {
        get
        {
            if (LocalIsDudu && Dudu != null) return Dudu.transform.position;
            if (haruCamera == null || !haruCamera.isActiveAndEnabled)
            {
                HaruDrawingController drawing = Object.FindAnyObjectByType<HaruDrawingController>(FindObjectsInactive.Include);
                if (drawing != null) haruCamera = drawing.GetComponent<Camera>();
            }
            return haruCamera != null ? haruCamera.transform.position : Vector3.zero;
        }
    }

    public static float Attenuation(Vector3 position)
    {
        float distance = Vector3.Distance(Listener, position);
        return Mathf.Lerp(1f, 0.08f, Mathf.InverseLerp(5f, 22f, distance));
    }
}

// Lives in the scene while effects are in use (goes with the scene): pooled sprites on the
// walls, one-shot audio, the hit stop. Plain sprites rather than particle systems because
// Dudu's 2D view shows sprites, so both players see the same effect.
[DefaultExecutionOrder(-50)]
public sealed class FxRunner : MonoBehaviour
{
    private static readonly int[] Limits = { 70, 110, 24 };
    private static readonly int[] Orders = { 8, 22, 60 };
    private static FxRunner instance;

    private readonly List<Puff>[] pools = { new List<Puff>(), new List<Puff>(), new List<Puff>() };
    private readonly int[] oldest = new int[3];
    private readonly List<AudioSource> voices = new List<AudioSource>();
    private bool hitStopping;

    private sealed class Puff
    {
        public SpriteRenderer renderer;
        public Vector3 velocity;
        public Vector2 stretch;
        public float age, life, from, to, gravity, drag, spin, alpha;
        public Color color;
        public bool fadeIn;
    }

    public static FxRunner Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject holder = new GameObject("Hazard Feedback");
                instance = holder.AddComponent<FxRunner>();
            }
            return instance;
        }
    }

    private void OnDestroy()
    {
        if (hitStopping) Time.timeScale = 1f;
        if (instance == this) instance = null;
    }

    public void PlayOneShot(AudioClip clip, float volume, float pitch)
    {
        AudioSource voice = null;
        foreach (AudioSource candidate in voices)
            if (!candidate.isPlaying) { voice = candidate; break; }
        if (voice == null)
        {
            if (voices.Count >= 14) return;
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            voices.Add(voice);
        }
        voice.pitch = pitch;
        voice.clip = clip;
        voice.volume = volume;
        voice.Play();
    }

    // A tiny freeze when a hit lands. Only on the PC playing Dudu: Haru keeps drawing.
    public void HitStop(float seconds)
    {
        if (!hitStopping && Mathf.Approximately(Time.timeScale, 1f)) StartCoroutine(HitStopRoutine(seconds));
    }

    private IEnumerator HitStopRoutine(float seconds)
    {
        hitStopping = true;
        Time.timeScale = 0.04f;
        yield return new WaitForSecondsRealtime(seconds);
        Time.timeScale = 1f;
        hitStopping = false;
    }

    public static void Spawn(Sprite sprite, Vector3 position, Quaternion rotation, Vector3 velocity,
        float life, float sizeFrom, float sizeTo, Color color, FxLayer layer = FxLayer.Front,
        float gravity = 0f, float drag = 0f, float spin = 0f, Vector2 stretch = default,
        bool flipX = false, bool fadeIn = false)
    {
        if (sprite == null || life <= 0f) return;
        FxRunner runner = Instance;
        Puff puff = runner.Take(layer);
        Transform t = puff.renderer.transform;
        t.SetPositionAndRotation(position, rotation);
        puff.velocity = velocity;
        puff.age = 0f;
        puff.life = life;
        puff.from = sizeFrom;
        puff.to = sizeTo;
        puff.color = color;
        puff.alpha = color.a;
        puff.gravity = gravity;
        puff.drag = drag;
        puff.spin = spin;
        puff.fadeIn = fadeIn;
        puff.stretch = stretch == default ? Vector2.one : stretch;
        puff.renderer.sprite = sprite;
        puff.renderer.flipX = flipX;
        Apply(puff, 0f);
        puff.renderer.gameObject.SetActive(true);
        DuduCameraController.RegisterRuntimeRenderer(puff.renderer);
    }

    // A handful of bits thrown outward across a wall.
    public static void Burst(FxShape shape, Vector3 position, DuduSurface surface, int count, float speed,
        float life, float size, Color color, FxLayer layer = FxLayer.Front, float gravity = 0f, float spread = 0.05f)
    {
        if (surface == null) return;
        Sprite sprite = FxArt.Get(shape);
        Vector3 right = surface.Right.normalized, up = surface.Up.normalized;
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = Random.insideUnitCircle.normalized;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            float pace = speed * Random.Range(0.45f, 1.2f);
            Spawn(sprite, position + (right * direction.x + up * direction.y) * spread,
                FxWall.Rotation(surface, shape == FxShape.Streak ? angle : Random.Range(0f, 360f)),
                (right * direction.x + up * direction.y) * pace,
                life * Random.Range(0.7f, 1.15f), size * Random.Range(0.7f, 1.2f), size * Random.Range(0.25f, 0.9f),
                color, layer, gravity, 4f, Random.Range(-180f, 180f));
        }
    }

    private Puff Take(FxLayer layer)
    {
        int index = (int)layer;
        List<Puff> pool = pools[index];
        foreach (Puff candidate in pool)
            if (!candidate.renderer.gameObject.activeSelf) return candidate;
        if (pool.Count >= Limits[index])
        {
            // Out of sprites: the oldest effect gives way.
            oldest[index] = (oldest[index] + 1) % pool.Count;
            return pool[oldest[index]];
        }

        GameObject holder = new GameObject("Fx");
        holder.transform.SetParent(transform, false);
        SpriteRenderer renderer = holder.AddComponent<SpriteRenderer>();
        renderer.sharedMaterial = FxArt.SolidMaterial;
        renderer.sortingOrder = Orders[index];
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        holder.SetActive(false);
        Puff puff = new Puff { renderer = renderer };
        pool.Add(puff);
        return puff;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        foreach (List<Puff> pool in pools)
        foreach (Puff puff in pool)
        {
            GameObject holder = puff.renderer.gameObject;
            if (!holder.activeSelf) continue;
            puff.age += dt;
            float t = puff.age / puff.life;
            if (t >= 1f)
            {
                holder.SetActive(false);
                continue;
            }
            Transform tr = puff.renderer.transform;
            puff.velocity += Vector3.down * (puff.gravity * dt);
            if (puff.drag > 0f) puff.velocity *= Mathf.Exp(-puff.drag * dt);
            tr.position += puff.velocity * dt;
            if (puff.spin != 0f) tr.rotation *= Quaternion.Euler(0f, 0f, puff.spin * dt);
            Apply(puff, t);
        }
    }

    private static void Apply(Puff puff, float t)
    {
        float grow = 1f - (1f - t) * (1f - t);
        float size = Mathf.Lerp(puff.from, puff.to, grow);
        puff.renderer.transform.localScale = new Vector3(size * puff.stretch.x, size * puff.stretch.y, 1f);
        float fade = puff.fadeIn ? Mathf.Min(t * 5f, 1f) * (1f - t) : (1f - t * t);
        Color color = puff.color;
        color.a = puff.alpha * fade;
        puff.renderer.color = color;
    }
}

// Where things go on a wall: in front of its face and just behind Dudu's sprite.
public static class FxWall
{
    public const float Depth = 0.125f;

    public static Vector3 Point(DuduSurface surface, Vector2 local, float depth = Depth) =>
        surface.transform.position + surface.Right.normalized * local.x + surface.Up.normalized * local.y +
        surface.Normal.normalized * depth;

    // The same spot on the wall, whatever depth it came from.
    public static Vector3 OnWall(DuduSurface surface, Vector3 world, float depth = Depth) =>
        Point(surface, surface.WorldToSurface(world), depth);

    public static Quaternion Rotation(DuduSurface surface, float degrees = 0f) =>
        surface.transform.rotation * Quaternion.Euler(0f, 0f, degrees);

    public static Vector3 Direction(DuduSurface surface, Vector2 direction) =>
        surface.Right.normalized * direction.x + surface.Up.normalized * direction.y;

    public static float Angle(Vector2 direction) => Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

    public static Quaternion FacingAway(Vector3 normal) => Quaternion.LookRotation(-normal, Vector3.up);

    // A zone's box as a rectangle in its wall's coordinates.
    public static Rect ZoneRect(BoxCollider box, DuduSurface surface)
    {
        Vector3 half = box.size * 0.5f;
        Vector2 a = surface.WorldToSurface(box.transform.TransformPoint(box.center + new Vector3(-half.x, -half.y, 0f)));
        Vector2 b = surface.WorldToSurface(box.transform.TransformPoint(box.center + new Vector3(half.x, half.y, 0f)));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // How far from a point inside a rectangle until this direction leaves it.
    public static float DistanceToExit(Rect rect, Vector2 start, Vector2 direction)
    {
        float distance = float.PositiveInfinity;
        if (direction.x > 0.001f) distance = Mathf.Min(distance, (rect.xMax - start.x) / direction.x);
        else if (direction.x < -0.001f) distance = Mathf.Min(distance, (rect.xMin - start.x) / direction.x);
        if (direction.y > 0.001f) distance = Mathf.Min(distance, (rect.yMax - start.y) / direction.y);
        else if (direction.y < -0.001f) distance = Mathf.Min(distance, (rect.yMin - start.y) / direction.y);
        return distance;
    }

    public static Vector2 RandomIn(Rect rect) => new Vector2(
        Random.Range(rect.xMin, rect.xMax), Random.Range(rect.yMin, rect.yMax));
}
