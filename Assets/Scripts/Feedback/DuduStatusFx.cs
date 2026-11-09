using System.Collections.Generic;
using UnityEngine;

// What is happening to Dudu, shown on Dudu so both players read it at a glance: status icons
// over his head, a coloured aura, debuff trails, hit flashes and the way his body reacts.
// Dudu's PC knows his state; Haru's PC gets it through the network visual state.
// Added and set up by DuduSurfaceMovement.
[DisallowMultipleComponent]
public sealed class DuduStatusFx : MonoBehaviour
{
    public enum Hit { None, Thunder, Eraser }

    private static readonly Color Purple = new Color(0.68f, 0.28f, 1f);
    private static readonly Color Grey = new Color(0.55f, 0.57f, 0.62f);
    private static readonly Color Yellow = new Color(1f, 0.85f, 0.15f);
    private static readonly Color Pink = new Color(1f, 0.42f, 0.7f);
    private static readonly Color Sky = new Color(0.45f, 0.82f, 1f);
    private static readonly Color MagnetRed = new Color(1f, 0.32f, 0.42f);

    public static DuduStatusFx Instance { get; private set; }

    // Hazards aimed at Dudu say so here every frame.
    public static float ThunderThreatUntil;
    public static float EraserThreatUntil;

    private DuduSurfaceMovement movement;
    private DuduMoveFeedback body;
    private SpriteRenderer sprite;
    private Animator animator;
    private SpriteRenderer aura;
    private SpriteRenderer roomRim, roomDeepen;
    private SpriteRenderer flash;
    private SpriteRenderer threatRing;
    private readonly List<Icon> icons = new List<Icon>();
    private DuduPositionIndicator indicator;

    private bool wasReversed, wasSlowed, inWind, wasPulled, wasAnimatorSlowed;
    private bool remoteReversed, remoteSlowed;
    private float remoteReverseLeft, remoteSlowLeft;
    private float reverseFull = 1f, slowFull = 1f;
    private int thunderHits, eraserHits, knocks;
    private float flashAmount;
    private Color flashColor = Color.white;
    private float shudderUntil;
    private float ghostUntil;
    private Color ghostColor;
    private float nextGhost, nextTrail, nextFootprint, nextSlide, nextPushJitter;
    private float smoothedLean;
    private float nextBlockAttach;
    private int blockScans;
    private Vector3 lastPosition;
    private AudioSource dizzyLoop, slowLoop;

    private sealed class Icon
    {
        public Transform root;
        public SpriteRenderer backing, glyph;
        public FxShape shape;
        public float pop;
    }

    public void Bind(SpriteRenderer newSprite, Animator newAnimator)
    {
        sprite = newSprite;
        animator = newAnimator;
        body = GetComponent<DuduMoveFeedback>();
        movement = GetComponent<DuduSurfaceMovement>();
        if (aura != null) Destroy(aura.gameObject);
        if (roomRim != null) Destroy(roomRim.gameObject);
        if (roomDeepen != null) Destroy(roomDeepen.gameObject);
        if (flash != null) Destroy(flash.gameObject);
        if (sprite == null) return;
        aura = FxArt.NewOverlay(sprite, "Dudu Aura", -1, 1.14f);
        flash = FxArt.NewOverlay(sprite, "Dudu Flash", 1, 1f);
        // Haru's view only (Dudu's page is plain, and he reads fine on it): a stronger blue
        // outline around him and a deeper blue over his line.
        roomRim = FxArt.NewOverlay(sprite, "Dudu Room Rim", -2, 1.045f);
        roomRim.gameObject.AddComponent<RoomOnlyDecor>();
        roomDeepen = FxArt.NewOverlay(sprite, "Dudu Room Deepen", 1, 1f);
        roomDeepen.gameObject.AddComponent<RoomOnlyDecor>();
    }

    private void Awake()
    {
        Instance = this;
        ThunderThreatUntil = EraserThreatUntil = 0f;
        lastPosition = transform.position;
        dizzyLoop = FxSound.AddLoop(gameObject, FxClip.DizzyLoop);
        slowLoop = FxSound.AddLoop(gameObject, FxClip.SlowLoop);
        threatRing = NewChildSprite("Dudu Threat Ring", 9);
        threatRing.sprite = FxArt.Get(FxShape.Target);
        threatRing.enabled = false;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private SpriteRenderer NewChildSprite(string name, int order)
    {
        GameObject holder = new GameObject(name);
        holder.transform.SetParent(transform, false);
        SpriteRenderer renderer = holder.AddComponent<SpriteRenderer>();
        renderer.sharedMaterial = FxArt.SolidMaterial;
        renderer.sortingOrder = order;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    // --- Network -----------------------------------------------------------------------

    public void WriteNetwork(ref DuduSurfaceMovement.NetworkVisualState state)
    {
        state.reverseLeft = movement.ReverseTimeLeft;
        state.slowLeft = movement.SlowTimeLeft;
        state.thunderHits = thunderHits;
        state.eraserHits = eraserHits;
        state.knocks = knocks;
    }

    public void ReadNetwork(DuduSurfaceMovement.NetworkVisualState state)
    {
        remoteReverseLeft = state.reverseLeft;
        remoteSlowLeft = state.slowLeft;
        remoteReversed = state.reverseLeft > 0f;
        remoteSlowed = state.slowLeft > 0f;
        if (state.thunderHits > thunderHits) PlayHit(Hit.Thunder);
        if (state.eraserHits > eraserHits) PlayHit(Hit.Eraser);
        if (state.knocks > knocks) PlayKnock(transform.position);
        thunderHits = state.thunderHits;
        eraserHits = state.eraserHits;
        knocks = state.knocks;
    }

    // --- Events ------------------------------------------------------------------------

    public void PlayHit(Hit kind)
    {
        if (kind == Hit.None || sprite == null) return;
        bool local = movement != null && movement.enabled;
        Color color = kind == Hit.Thunder ? Yellow : Pink;
        Vector3 center = BodyCenter();
        DuduSurface surface = movement != null ? movement.CurrentSurface : null;
        flashAmount = 1f;
        flashColor = kind == Hit.Thunder ? Color.white : new Color(1f, 0.85f, 0.92f);
        shudderUntil = Time.unscaledTime + 0.3f;
        FxRunner.Burst(FxShape.Spark, center, surface, 12, 3.4f, 0.4f, 0.26f, color);
        FxRunner.Burst(FxShape.Streak, center, surface, 8, 4.4f, 0.26f, 0.5f, Color.Lerp(color, Color.white, 0.5f));
        if (kind == Hit.Thunder)
        {
            FxSound.Play(FxClip.Crack, center);
            HazardAlerts.Flash(Color.white, 0.32f, 0.22f);
            DuduCameraController.Shake(0.09f, 0.22f);
        }
        else
        {
            FxRunner.Burst(FxShape.Crumb, center, surface, 16, 2.6f, 0.6f, 0.13f, Pink, FxLayer.Front, 5f);
            FxSound.Play(FxClip.Knock, center);
            HazardAlerts.Flash(Pink, 0.3f, 0.24f);
            DuduCameraController.Shake(0.15f, 0.28f);
        }
        HazardAlerts.Ping(kind == Hit.Thunder ? "두두가 번개에 맞았어요!" : "두두가 지우개에 맞았어요!", color);
        AlarmHaru(color, 1.2f);
        if (local)
        {
            if (kind == Hit.Thunder) thunderHits++; else eraserHits++;
            if (FxView.LocalIsDudu) FxRunner.Instance.HitStop(kind == Hit.Thunder ? 0.07f : 0.09f);
        }
    }

    public void PlayKnock(Vector3 from)
    {
        if (sprite == null) return;
        bool local = movement != null && movement.enabled;
        Vector3 center = BodyCenter();
        DuduSurface surface = movement != null ? movement.CurrentSurface : null;
        flashAmount = 0.9f;
        flashColor = Color.white;
        ghostUntil = Time.unscaledTime + 0.32f;
        ghostColor = new Color(Sky.r, Sky.g, Sky.b, 0.5f);
        FxRunner.Burst(FxShape.Spark, center, surface, 8, 3f, 0.3f, 0.2f, Sky);
        FxRunner.Burst(FxShape.Soft, FeetPoint(), surface, 5, 1.4f, 0.4f, 0.28f, new Color(0.62f, 0.55f, 0.47f, 0.7f), FxLayer.Behind);
        FxSound.Play(FxClip.Knock, center);
        FxSound.Play(FxClip.Swish, center, 0.7f);
        KnockBlockFx.BumpNearest(center);
        DuduCameraController.Shake(0.07f, 0.16f);
        HazardAlerts.Ping("두두가 튕겨났어요!", Sky);
        AlarmHaru(Sky, 0.8f);
        if (local) knocks++;
    }

    // Walked into something solid.
    public void PlayBump(Vector3 point, Collider other)
    {
        DuduSurface surface = movement != null ? movement.CurrentSurface : null;
        FxRunner.Burst(FxShape.Soft, point, surface, 3, 0.8f, 0.3f, 0.2f, new Color(0.62f, 0.55f, 0.47f, 0.6f), FxLayer.Front);
        FxRunner.Burst(FxShape.Crumb, point, surface, 4, 1.1f, 0.35f, 0.07f, new Color(0.45f, 0.38f, 0.3f), FxLayer.Front, 4f);
        FxSound.Play(FxClip.Tok, point, 0.8f);
        if (other != null && other.TryGetComponent(out BlockFx block)) block.Bumped();
    }

    private void AlarmHaru(Color color, float seconds)
    {
        if (indicator == null) indicator = FindAnyObjectByType<DuduPositionIndicator>(FindObjectsInactive.Include);
        if (indicator != null) indicator.Alarm(color, seconds);
    }

    // --- Every frame -------------------------------------------------------------------

    private void LateUpdate()
    {
        // Blocks have no script of their own, and the tutorial places some after the scene
        // starts. Here rather than in movement: that is switched off on Haru's PC.
        if (Time.time >= nextBlockAttach)
        {
            nextBlockAttach = Time.time + (++blockScans < 8 ? 0.5f : 5f);
            BlockFx.AttachToBlocks();
        }
        if (sprite == null || movement == null) return;
        if (body == null) body = GetComponent<DuduMoveFeedback>();
        DuduSurface surface = movement.CurrentSurface;
        float dt = Time.unscaledDeltaTime;
        float now = Time.unscaledTime;
        bool local = movement.enabled;

        bool reversed = local ? movement.IsControlsReversed : remoteReversed;
        bool slowed = local ? movement.IsSlowed : remoteSlowed;
        float reverseLeft = local ? movement.ReverseTimeLeft : remoteReverseLeft;
        float slowLeft = local ? movement.SlowTimeLeft : remoteSlowLeft;
        Vector3 position = transform.position;
        Vector3 moved = position - lastPosition;
        lastPosition = position;
        if (moved.sqrMagnitude > 1f) moved = Vector3.zero;
        bool walking = dt > 0f && moved.magnitude / Mathf.Max(dt, 0.001f) > 0.6f;
        bool grounded = body == null || body.Grounded;

        Vector2 wind = surface != null ? WindZone.GetSurfaceVelocity(surface, position) : Vector2.zero;
        Vector2 pull = surface != null ? LineMagnet.GetSurfaceVelocity(surface, position) : Vector2.zero;
        bool thunderThreat = Time.time < ThunderThreatUntil;
        bool eraserThreat = Time.time < EraserThreatUntil;

        UpdateDebuffChanges(reversed, slowed, surface);
        if (reversed)
        {
            if (reverseLeft > reverseFull || !wasReversed) reverseFull = Mathf.Max(0.1f, reverseLeft);
            HazardAlerts.Hold(HazardAlert.Reversed, reverseLeft / reverseFull);
        }
        if (slowed)
        {
            if (slowLeft > slowFull || !wasSlowed) slowFull = Mathf.Max(0.1f, slowLeft);
            HazardAlerts.Hold(HazardAlert.Slowed, slowLeft / slowFull);
        }
        wasReversed = reversed;
        wasSlowed = slowed;
        if (thunderThreat) HazardAlerts.Hold(HazardAlert.Thunder);
        if (eraserThreat) HazardAlerts.Hold(HazardAlert.Eraser);
        if (wind.sqrMagnitude > 0.01f) HazardAlerts.Hold(HazardAlert.Wind);
        bool pulled = pull.sqrMagnitude > 0.04f;
        if (pulled) HazardAlerts.Hold(HazardAlert.Magnet);
        wasPulled = pulled;

        UpdateWindEntry(wind, surface);
        UpdateTrails(reversed, slowed, pulled, walking, grounded, pull, surface);
        UpdateBody(reversed, slowed, wind, pull, walking, grounded, now, dt);
        UpdateOverlays(reversed, slowed, dt, now);
        UpdateThreatRing(thunderThreat, eraserThreat, now);
        UpdateIcons(reversed, slowed, thunderThreat, eraserThreat, wind, pull, surface, dt, now);

        FxSound.SetLoop(dizzyLoop, reversed ? 0.1f : 0f);
        FxSound.SetLoop(slowLoop, slowed ? 0.14f : 0f);
    }

    private void UpdateDebuffChanges(bool reversed, bool slowed, DuduSurface surface)
    {
        Vector3 center = BodyCenter();
        if (reversed && !wasReversed)
        {
            FxSound.Play(FxClip.Reverse, center);
            FxRunner.Burst(FxShape.Swirl, center, surface, 6, 1.5f, 0.5f, 0.3f, Purple);
            HazardAlerts.Flash(Purple, 0.22f, 0.3f);
            // Reversed: the view itself lurches for a moment.
            DuduCameraController.Roll(3.5f, 0.45f);
            flashAmount = 0.7f;
            flashColor = Purple;
            AlarmHaru(Purple, 1.2f);
        }
        else if (!reversed && wasReversed)
        {
            FxSound.Play(FxClip.ReleasePuff, center);
            FxRunner.Burst(FxShape.Soft, center, surface, 9, 1.5f, 0.55f, 0.5f, new Color(Purple.r, Purple.g, Purple.b, 0.55f), FxLayer.Behind);
        }

        if (slowed && !wasSlowed)
        {
            FxSound.Play(FxClip.Boom, center);
            FxRunner.Burst(FxShape.Soft, FeetPoint(), surface, 8, 1f, 0.6f, 0.5f, new Color(Grey.r, Grey.g, Grey.b, 0.75f), FxLayer.Behind);
            HazardAlerts.Flash(Grey, 0.2f, 0.3f);
            DuduCameraController.Shake(0.05f, 0.14f);
            flashAmount = 0.6f;
            flashColor = Grey;
            AlarmHaru(Grey, 1.2f);
        }
        else if (!slowed && wasSlowed)
        {
            FxSound.Play(FxClip.Disperse, center);
            FxRunner.Burst(FxShape.Soft, FeetPoint(), surface, 9, 1.8f, 0.6f, 0.45f, new Color(Grey.r, Grey.g, Grey.b, 0.5f), FxLayer.Behind);
        }
    }

    private void UpdateWindEntry(Vector2 wind, DuduSurface surface)
    {
        bool windy = wind.sqrMagnitude > 0.01f;
        if (windy && !inWind && surface != null)
        {
            Vector3 center = BodyCenter();
            FxSound.Play(FxClip.Gust, center, 0.8f);
            Sprite streak = FxArt.Get(FxShape.Streak);
            Vector3 direction = FxWall.Direction(surface, wind.normalized);
            float angle = FxWall.Angle(wind);
            for (int i = 0; i < 6; i++)
                FxRunner.Spawn(streak, center + FxWall.Direction(surface, Random.insideUnitCircle * 0.7f) - direction * 0.5f,
                    FxWall.Rotation(surface, angle), direction * Random.Range(3f, 5f), 0.3f,
                    Random.Range(0.6f, 1.1f), Random.Range(0.6f, 1.1f), new Color(1f, 1f, 1f, 0.8f));
        }
        inWind = windy;
    }

    private void UpdateTrails(bool reversed, bool slowed, bool pulled, bool walking, bool grounded, Vector2 pull, DuduSurface surface)
    {
        float now = Time.unscaledTime;
        if (surface == null) return;

        if ((reversed || slowed) && now >= nextTrail)
        {
            nextTrail = now + (walking ? 0.07f : 0.14f);
            if (reversed)
                FxRunner.Spawn(FxArt.Get(FxShape.Soft), BodyCenter() + FxWall.Direction(surface, Random.insideUnitCircle * 0.35f),
                    FxWall.Rotation(surface, Random.Range(0f, 360f)), surface.Up.normalized * Random.Range(0.1f, 0.5f),
                    Random.Range(0.5f, 0.8f), 0.14f, 0.34f, new Color(Purple.r, Purple.g, Purple.b, 0.55f), FxLayer.Behind, 0f, 1f, 120f);
            if (slowed)
                FxRunner.Spawn(FxArt.Get(FxShape.Soft), FeetPoint() + surface.Right.normalized * Random.Range(-0.32f, 0.32f),
                    FxWall.Rotation(surface, Random.Range(0f, 360f)), -surface.Up.normalized * 0.05f + surface.Right.normalized * Random.Range(-0.15f, 0.15f),
                    Random.Range(0.6f, 0.9f), 0.26f, 0.5f, new Color(Grey.r, Grey.g, Grey.b, 0.6f), FxLayer.Front, 0f, 1f);
        }

        // Heavy, sticky footprints that take a moment to go.
        if (slowed && walking && grounded && now >= nextFootprint)
        {
            nextFootprint = now + 0.22f;
            FxRunner.Spawn(FxArt.Get(FxShape.Soft), FeetPoint(), FxWall.Rotation(surface), Vector3.zero,
                0.9f, 0.3f, 0.34f, new Color(0.33f, 0.34f, 0.38f, 0.65f), FxLayer.Behind, 0f, 0f, 0f, new Vector2(1.5f, 0.45f));
        }

        // Dragged along by the magnet: the feet slide.
        if (pulled && grounded && walking && now >= nextSlide)
        {
            nextSlide = now + 0.08f;
            float side = Mathf.Sign(pull.x);
            FxRunner.Spawn(FxArt.Get(FxShape.Soft), FeetPoint() - surface.Right.normalized * side * 0.15f,
                FxWall.Rotation(surface, Random.Range(0f, 360f)),
                -surface.Right.normalized * side * Random.Range(0.3f, 0.7f) + surface.Up.normalized * 0.2f,
                0.35f, 0.14f, 0.3f, new Color(0.62f, 0.55f, 0.47f, 0.7f), FxLayer.Behind);
        }

        if (now < ghostUntil && now >= nextGhost)
        {
            nextGhost = now + 0.04f;
            Vector3 ghostAt = OnWallBehindDudu();
            FxRunner.Spawn(sprite.sprite, ghostAt, sprite.transform.rotation, Vector3.zero, 0.22f,
                sprite.transform.lossyScale.x, sprite.transform.lossyScale.x, ghostColor, FxLayer.Behind, 0f, 0f, 0f,
                default, sprite.flipX);
        }
    }

    private void UpdateBody(bool reversed, bool slowed, Vector2 wind, Vector2 pull, bool walking, bool grounded, float now, float dt)
    {
        if (body == null) return;
        float lean = Mathf.Clamp((wind.x + pull.x) * 5f, -16f, 16f);
        if (reversed) lean += Mathf.Sin(now * 7.5f) * (walking ? 7f : 4f);
        smoothedLean = Mathf.Lerp(smoothedLean, lean, 1f - Mathf.Exp(-10f * dt));
        body.ExternalLean = smoothedLean;
        body.ExternalSquash = slowed ? -0.05f : 0f;

        Vector2 offset = Vector2.zero;
        float parentScale = sprite.transform.parent != null ? Mathf.Max(0.0001f, sprite.transform.parent.lossyScale.x) : 1f;
        if (now < shudderUntil)
            offset += Random.insideUnitCircle * (0.05f * Mathf.InverseLerp(0f, 0.3f, shudderUntil - now));

        // Pushing against something that won't move: he strains and stutters a little.
        if (movement.enabled && movement.HorizontalInput != 0f && grounded && !walking && now >= nextPushJitter)
        {
            offset.x += Random.Range(-0.018f, 0.018f);
            nextPushJitter = now + 0.03f;
        }
        body.ExternalOffset = offset / parentScale;

        // Heavier animation while slowed; the host's network update resets this every packet.
        if (animator != null)
        {
            if (slowed) animator.speed = 0.65f;
            else if (wasAnimatorSlowed) animator.speed = 1f;
            wasAnimatorSlowed = slowed;
        }
    }

    private void UpdateOverlays(bool reversed, bool slowed, float dt, float now)
    {
        if (roomRim != null)
        {
            FxArt.Sync(roomRim, sprite, new Color(0.08f, 0.3f, 0.82f, 0.9f * sprite.color.a));
            FxArt.Sync(roomDeepen, sprite, new Color(0.08f, 0.3f, 0.82f, 0.22f * sprite.color.a));
        }
        flashAmount = Mathf.MoveTowards(flashAmount, 0f, dt * 5f);
        Color flashNow = flashColor;
        flashNow.a = flashAmount * (Mathf.Sin(now * 60f) > -0.3f ? 1f : 0.35f);
        if (flash != null) FxArt.Sync(flash, sprite, flashNow);

        if (aura == null) return;
        Color auraColor = Color.clear;
        if (reversed || slowed)
        {
            bool showPurple = reversed && (!slowed || Mathf.Repeat(now, 1.2f) < 0.6f);
            auraColor = showPurple ? Purple : Grey;
            auraColor.a = 0.62f + Mathf.Sin(now * 6f) * 0.2f;
            aura.transform.localScale = Vector3.one * (1.13f + Mathf.Sin(now * 4f) * 0.04f);
        }
        FxArt.Sync(aura, sprite, auraColor);
    }

    private void UpdateThreatRing(bool thunder, bool eraser, float now)
    {
        bool show = (thunder || eraser) && sprite.enabled;
        threatRing.enabled = show;
        if (!show) return;
        Transform ring = threatRing.transform;
        ring.position = BodyCenter() + movement.transform.forward * 0.02f;
        ring.rotation = transform.rotation * Quaternion.Euler(0f, 0f, now * 90f);
        float size = (2.3f + Mathf.Sin(now * 9f) * 0.1f) / Mathf.Max(0.0001f, transform.lossyScale.x);
        ring.localScale = Vector3.one * size;
        Color color = thunder ? Yellow : Pink;
        color.a = 0.75f + Mathf.Sin(now * 14f) * 0.2f;
        threatRing.color = color;
    }

    private readonly List<(FxShape shape, Color color, float angle, float spin)> wanted =
        new List<(FxShape, Color, float, float)>();

    private void UpdateIcons(bool reversed, bool slowed, bool thunder, bool eraser, Vector2 wind, Vector2 pull,
        DuduSurface surface, float dt, float now)
    {
        wanted.Clear();
        if (thunder) wanted.Add((FxShape.Bolt, Yellow, 0f, 0f));
        if (eraser) wanted.Add((FxShape.Exclaim, Pink, 0f, 0f));
        if (reversed) wanted.Add((FxShape.Swirl, Purple, 0f, 240f));
        if (slowed) wanted.Add((FxShape.SlowFoot, Color.Lerp(Grey, Color.white, 0.45f), 0f, 0f));
        if (wind.sqrMagnitude > 0.01f) wanted.Add((FxShape.Arrow, Sky, FxWall.Angle(wind), 0f));
        if (pull.sqrMagnitude > 0.04f) wanted.Add((FxShape.Magnet, MagnetRed, 0f, 0f));
        if (surface != null && NoDrawZone.IsInside(surface, transform.position))
            wanted.Add((FxShape.Lock, new Color(0.85f, 0.85f, 0.88f), 0f, 0f));

        while (icons.Count < wanted.Count) icons.Add(NewIcon());
        const float Spacing = 0.46f, Size = 0.42f;
        Vector3 up = surface != null ? surface.Up.normalized : transform.up;
        Vector3 right = surface != null ? surface.Right.normalized : transform.right;
        Vector3 forward = -transform.forward;
        Vector3 top = FeetPoint() + up * (sprite.bounds.size.y * 0.8f + 0.14f + Mathf.Sin(now * 3.4f) * 0.03f) + forward * 0.03f;
        float scaleBack = Mathf.Max(0.0001f, transform.lossyScale.x);
        for (int i = 0; i < icons.Count; i++)
        {
            Icon icon = icons[i];
            bool used = i < wanted.Count && sprite.enabled;
            if (icon.root.gameObject.activeSelf != used) icon.root.gameObject.SetActive(used);
            if (!used) { icon.pop = 0f; icon.shape = FxShape.Soft; continue; }
            var want = wanted[i];
            if (icon.shape != want.shape)
            {
                icon.shape = want.shape;
                icon.glyph.sprite = FxArt.Get(want.shape);
                icon.pop = 1f;
                DuduCameraController.RegisterRuntimeRenderer(icon.backing);
                DuduCameraController.RegisterRuntimeRenderer(icon.glyph);
            }
            icon.pop = Mathf.MoveTowards(icon.pop, 0f, dt * 4f);
            float size = Size * (1f + icon.pop * 0.6f) / scaleBack;
            icon.root.position = top + right * ((i - (wanted.Count - 1) * 0.5f) * Spacing);
            icon.root.rotation = transform.rotation;
            icon.root.localScale = Vector3.one * size;
            icon.glyph.transform.localRotation = Quaternion.Euler(0f, 0f, want.spin != 0f ? -now * want.spin : want.angle);
            icon.glyph.color = want.color;
        }
    }

    private Icon NewIcon()
    {
        GameObject root = new GameObject("Dudu Status Icon");
        root.transform.SetParent(transform, false);
        SpriteRenderer backing = root.AddComponent<SpriteRenderer>();
        backing.sharedMaterial = FxArt.SolidMaterial;
        backing.sprite = FxArt.Get(FxShape.Disc);
        backing.color = new Color(0.1f, 0.09f, 0.14f, 0.82f);
        backing.sortingOrder = 58;
        GameObject glyphObject = new GameObject("Glyph");
        glyphObject.transform.SetParent(root.transform, false);
        glyphObject.transform.localScale = Vector3.one * 0.74f;
        SpriteRenderer glyph = glyphObject.AddComponent<SpriteRenderer>();
        glyph.sharedMaterial = FxArt.SolidMaterial;
        glyph.sortingOrder = 59;
        root.SetActive(false);
        return new Icon { root = root.transform, backing = backing, glyph = glyph, shape = FxShape.Soft };
    }

    private Vector3 FeetPoint() => body != null ? body.FeetPoint() : transform.position;

    private Vector3 BodyCenter()
    {
        Vector3 up = movement != null && movement.CurrentSurface != null ? movement.CurrentSurface.Up.normalized : transform.up;
        return FeetPoint() + up * (sprite != null ? sprite.bounds.size.y * 0.5f : 0.6f);
    }

    private Vector3 OnWallBehindDudu()
    {
        Vector3 position = sprite.transform.position;
        return position + transform.forward * 0.02f;
    }
}
