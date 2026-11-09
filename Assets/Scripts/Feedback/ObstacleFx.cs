using System.Collections.Generic;
using UnityEngine;

// Storm cloud: flickers with stored lightning, charges visibly before each bolt, and marks
// Dudu as the target from the first spark until the bolt is gone.
[DisallowMultipleComponent]
public sealed class StormCloudFx : MonoBehaviour
{
    private static readonly Color Yellow = new Color(1f, 0.9f, 0.25f);

    private DuduHomingShooter shooter;
    private SpriteRenderer art;
    private SpriteRenderer glow;
    private LineRenderer fork;
    private float flicker;
    private float nextFlicker;
    private float nextFork;
    private float nextSpark;
    private bool wasCharging;
    private bool hadBolt;

    private void Awake()
    {
        shooter = GetComponent<DuduHomingShooter>();
        art = GetComponent<SpriteRenderer>();
        if (art == null) art = GetComponentInChildren<SpriteRenderer>();
    }

    private void Start()
    {
        if (art != null) glow = FxArt.NewOverlay(art, "Cloud Flash", 1, 1f);
        fork = FxArt.NewLine("Lightning Warning", 0.05f, 24);
        fork.transform.SetPositionAndRotation(transform.position, transform.rotation);
        fork.startColor = fork.endColor = Yellow;
    }

    private void OnDestroy()
    {
        if (fork != null) Destroy(fork.gameObject);
    }

    private void Update()
    {
        DuduSurface surface = shooter.Surface;
        if (surface == null) return;
        float charge = shooter.ChargeProgress;
        bool charging = charge > 0f;
        bool hasBolt = shooter.ActiveProjectile != null;
        float now = Time.time;

        if (charging && !wasCharging)
            FxSound.Play(FxClip.Charge, shooter.EmitterPoint);
        if (hasBolt && !hadBolt) flicker = 1.2f;
        wasCharging = charging;
        hadBolt = hasBolt;

        // Hazards owned by Dudu's PC: the target mark and Haru's warning show on both.
        if (charging || hasBolt) DuduStatusFx.ThunderThreatUntil = now + 0.12f;

        // Now and then it flickers by itself; while charging it hardly stops.
        if (now >= nextFlicker)
        {
            nextFlicker = now + (charging ? Random.Range(0.04f, 0.09f) : Random.Range(1.2f, 3.5f));
            flicker = charging ? Random.Range(0.55f, 1f) : Random.Range(0.3f, 0.6f);
        }
        flicker = Mathf.MoveTowards(flicker, 0f, Time.deltaTime * 4.5f);
        if (glow != null && art != null)
        {
            Color color = Yellow;
            color.a = Mathf.Clamp01(flicker) * 0.85f;
            FxArt.Sync(glow, art, color);
        }

        Vector3 emitter = FxWall.OnWall(surface, shooter.EmitterPoint, FxWall.Depth + 0.01f);
        if (charging && now >= nextSpark)
        {
            nextSpark = now + 0.05f;
            FxRunner.Burst(FxShape.Spark, emitter + FxWall.Direction(surface, Random.insideUnitCircle * 0.3f),
                surface, 1, 0.8f, 0.2f, 0.16f + charge * 0.14f, Yellow, FxLayer.Front);
        }
        UpdateFork(surface, emitter, charging, charge, now);
    }

    // A short jagged line hinting where the bolt is about to go.
    private void UpdateFork(DuduSurface surface, Vector3 emitter, bool charging, float charge, float now)
    {
        if (fork == null) return;
        fork.enabled = charging;
        if (!charging || now < nextFork) return;
        nextFork = now + 0.05f;

        Vector2 from = surface.WorldToSurface(emitter);
        Vector2 toward = Vector2.down;
        DuduSurfaceMovement dudu = FxView.Dudu;
        if (dudu != null && DuduHomingProjectile.TryMapToSurface(surface, dudu.CurrentSurface, dudu.transform.position, out Vector2 target) &&
            (target - from).sqrMagnitude > 0.01f)
            toward = (target - from).normalized;
        float length = Mathf.Lerp(0.5f, 1.5f, charge);
        Vector2 side = new Vector2(-toward.y, toward.x);
        const int Points = 7;
        fork.positionCount = Points;
        for (int i = 0; i < Points; i++)
        {
            float t = i / (Points - 1f);
            Vector2 point = from + toward * (length * t) + side * (i == 0 ? 0f : Random.Range(-0.1f, 0.1f));
            fork.SetPosition(i, FxWall.Point(surface, point, FxWall.Depth + 0.015f));
        }
        Color color = Yellow;
        color.a = Mathf.Lerp(0.45f, 1f, charge);
        fork.startColor = color;
        color.a = 0.1f;
        fork.endColor = color;
        DuduCameraController.RegisterRuntimeRenderer(fork);
    }
}

// A bolt in flight: electric tail, afterimages, a crackle that grows as it nears Dudu and a
// little burst when it ends.
[DisallowMultipleComponent]
public sealed class BoltFx : MonoBehaviour
{
    private static readonly Color Yellow = new Color(1f, 0.9f, 0.25f);

    private SpriteRenderer art;
    private AudioSource crackle;
    private float nextSpark, nextGhost, nextScreenFlicker;

    private void Awake()
    {
        art = GetComponent<SpriteRenderer>();
        crackle = FxSound.AddLoop(gameObject, FxClip.BoltLoop);
    }

    private static DuduSurface SurfaceHere()
    {
        DuduSurfaceMovement dudu = FxView.Dudu;
        return dudu != null ? dudu.CurrentSurface : null;
    }

    private void Update()
    {
        DuduSurfaceMovement dudu = FxView.Dudu;
        DuduSurface surface = SurfaceHere();
        if (surface == null) return;
        Vector3 position = transform.position;
        float distance = dudu != null ? Vector3.Distance(dudu.transform.position, position) : 99f;
        float near = 1f - Mathf.InverseLerp(0.8f, 6f, distance);
        FxSound.SetLoop(crackle, Mathf.Lerp(0.04f, 0.42f, near) * FxView.Attenuation(position), 1f + near * 0.25f);

        float now = Time.time;
        if (now >= nextSpark)
        {
            nextSpark = now + 0.035f;
            FxRunner.Spawn(FxArt.Get(FxShape.Spark), position + FxWall.Direction(surface, Random.insideUnitCircle * 0.1f),
                FxWall.Rotation(surface, Random.Range(0f, 360f)), FxWall.Direction(surface, Random.insideUnitCircle * 0.5f),
                Random.Range(0.18f, 0.32f), 0.2f, 0.05f, Yellow, FxLayer.Behind);
        }
        if (art != null && art.sprite != null && now >= nextGhost)
        {
            nextGhost = now + 0.06f;
            FxRunner.Spawn(art.sprite, position - transform.forward * -0.005f, transform.rotation, Vector3.zero, 0.2f,
                transform.lossyScale.x, transform.lossyScale.x * 0.8f, new Color(1f, 0.95f, 0.55f, 0.4f), FxLayer.Behind);
        }
        // Close by, the whole screen twitches very faintly.
        if (near > 0.45f && now >= nextScreenFlicker)
        {
            nextScreenFlicker = now + Random.Range(0.12f, 0.35f);
            HazardAlerts.Flash(Color.white, 0.07f * near, 0.08f);
        }
    }

    public void Burst()
    {
        DuduSurface surface = SurfaceHere();
        if (surface == null) return;
        Vector3 position = transform.position;
        FxRunner.Burst(FxShape.Spark, position, surface, 8, 2.4f, 0.28f, 0.22f, Yellow);
        FxRunner.Burst(FxShape.Streak, position, surface, 5, 3f, 0.2f, 0.34f, Color.white);
        FxRunner.Spawn(FxArt.Get(FxShape.Ring), position, FxWall.Rotation(surface), Vector3.zero, 0.22f, 0.15f, 0.8f, Yellow);
        FxSound.Play(FxClip.Pop, position, 0.7f);
        FxSound.Play(FxClip.Snap, position, 0.5f);
    }
}

// Knockback block: pulses in blue with little chevrons pointing where it throws you, so it
// reads apart from a plain block from any distance. Flares and dims after a hit.
[DisallowMultipleComponent]
public sealed class KnockBlockFx : MonoBehaviour
{
    private static readonly Color Blue = new Color(0.32f, 0.68f, 1f);
    private static readonly List<KnockBlockFx> All = new List<KnockBlockFx>();

    private DuduMovingObstacle block;
    private SpriteRenderer art;
    private SpriteRenderer pulse, flash;
    private Color normalColor = Color.white;
    private float flashAmount, dim;
    private float nextChevrons, nextHum;

    private void Awake()
    {
        block = GetComponent<DuduMovingObstacle>();
        art = GetComponent<SpriteRenderer>();
    }

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    private void Start()
    {
        if (art == null) return;
        normalColor = art.color;
        pulse = FxArt.NewOverlay(art, "Knock Pulse", -1, 1.1f);
        flash = FxArt.NewOverlay(art, "Knock Flash", 1, 1f);
    }

    public static void BumpNearest(Vector3 point)
    {
        KnockBlockFx nearest = null;
        float best = float.PositiveInfinity;
        foreach (KnockBlockFx candidate in All)
        {
            float distance = (candidate.transform.position - point).sqrMagnitude;
            if (distance < best) { best = distance; nearest = candidate; }
        }
        if (nearest != null) nearest.Bumped(point);
    }

    private void Bumped(Vector3 point)
    {
        flashAmount = 1f;
        dim = 1f;
        DuduSurface surface = block.Surface;
        if (surface == null) return;
        Vector3 between = Vector3.Lerp(transform.position, point, 0.5f);
        Vector3 at = FxWall.OnWall(surface, between, FxWall.Depth + 0.02f);
        FxRunner.Spawn(FxArt.Get(FxShape.Ring), at, FxWall.Rotation(surface), Vector3.zero, 0.25f, 0.2f, 1.1f, Blue);
        FxRunner.Burst(FxShape.Crumb, at, surface, 8, 2.2f, 0.4f, 0.09f, new Color(0.55f, 0.72f, 0.95f), FxLayer.Front, 4f);
    }

    private void Update()
    {
        if (art == null) return;
        float now = Time.time;
        DuduSurface surface = block.Surface;

        float wave = Mathf.Sin(now * 3.4f) * 0.5f + 0.5f;
        Color pulseColor = Blue;
        pulseColor.a = Mathf.Lerp(0.12f, 0.62f, wave) * (1f - dim * 0.8f);
        if (pulse != null)
        {
            pulse.transform.localScale = Vector3.one * Mathf.Lerp(1.05f, 1.18f, wave);
            FxArt.Sync(pulse, art, pulseColor);
        }
        flashAmount = Mathf.MoveTowards(flashAmount, 0f, Time.deltaTime * 4.5f);
        if (flash != null) FxArt.Sync(flash, art, new Color(1f, 1f, 1f, flashAmount));

        // After throwing Dudu it goes dark for a moment, then comes back.
        dim = Mathf.MoveTowards(dim, 0f, Time.deltaTime * 1.6f);
        art.color = Color.Lerp(normalColor, new Color(0.45f, 0.48f, 0.58f, normalColor.a), dim);

        if (surface != null && now >= nextChevrons)
        {
            nextChevrons = now + 0.85f;
            Vector3 center = FxWall.OnWall(surface, art.bounds.center, FxWall.Depth);
            float reach = art.bounds.extents.magnitude * 0.72f;
            Vector3 right = surface.Right.normalized;
            Color color = new Color(Blue.r, Blue.g, Blue.b, 0.85f);
            FxRunner.Spawn(FxArt.Get(FxShape.Chevron), center + right * reach, FxWall.Rotation(surface, 0f), right * 0.7f, 0.5f, 0.34f, 0.4f, color, FxLayer.Front, fadeIn: true);
            FxRunner.Spawn(FxArt.Get(FxShape.Chevron), center - right * reach, FxWall.Rotation(surface, 180f), -right * 0.7f, 0.5f, 0.34f, 0.4f, color, FxLayer.Front, fadeIn: true);
        }
        if (now >= nextHum)
        {
            nextHum = now + 1.85f;
            DuduSurfaceMovement dudu = FxView.Dudu;
            if (dudu != null && Vector3.Distance(dudu.transform.position, transform.position) < 4f)
                FxSound.Play(FxClip.Pulse, transform.position, 0.22f);
        }
    }
}

// Plain block: a clear crayon outline so it never reads as drawable space, a little brighter
// as Dudu comes near, and a small reaction when he walks into it.
[DisallowMultipleComponent]
public sealed class BlockFx : MonoBehaviour
{
    private SpriteRenderer art;
    private SpriteRenderer outline, flash;
    private float flashAmount;
    private float nearness;

    public void Bind(SpriteRenderer renderer)
    {
        art = renderer;
        outline = FxArt.NewOverlay(art, "Block Outline", -1, 1.08f);
        flash = FxArt.NewOverlay(art, "Block Flash", 1, 1f);
    }

    public void Bumped() => flashAmount = 0.6f;

    private void Update()
    {
        if (art == null) return;
        DuduSurfaceMovement dudu = FxView.Dudu;
        float distance = dudu != null ? Vector3.Distance(dudu.transform.position, art.bounds.center) : 99f;
        nearness = Mathf.MoveTowards(nearness, 1f - Mathf.InverseLerp(art.bounds.extents.magnitude + 0.3f, art.bounds.extents.magnitude + 1.6f, distance), Time.deltaTime * 5f);
        FxArt.Sync(outline, art, Color.Lerp(new Color(0.16f, 0.13f, 0.1f, 0.62f), new Color(1f, 0.97f, 0.82f, 0.9f), nearness));
        flashAmount = Mathf.MoveTowards(flashAmount, 0f, Time.deltaTime * 4f);
        FxArt.Sync(flash, art, new Color(1f, 1f, 1f, flashAmount));
    }

    // Blocks have no script of their own, so Dudu finds them.
    public static void AttachToBlocks()
    {
        foreach (BoxCollider box in Object.FindObjectsByType<BoxCollider>())
        {
            if (box.isTrigger || box.GetComponent<BlockFx>() != null || box.GetComponent<DuduMovingObstacle>() != null) continue;
            if (!box.TryGetComponent(out SpriteRenderer renderer) || renderer.sprite == null) continue;
            if (!renderer.sprite.name.StartsWith("block")) continue;
            box.gameObject.AddComponent<BlockFx>().Bind(renderer);
        }
    }
}

// Magnet: a visible field, bits streaming in while it is on, a line tying it to Dudu while it
// has hold of him, and a snap when he breaks free.
[DefaultExecutionOrder(210)]
[DisallowMultipleComponent]
public sealed class MagnetFx : MonoBehaviour
{
    private static readonly Color Field = new Color(1f, 0.32f, 0.42f);

    private LineMagnet magnet;
    private LineRenderer range;
    private LineRenderer tether;
    private AudioSource hum;
    private bool wasActive;
    private bool wasPulling;
    private float nextFlow, nextTether, nextRing;
    private readonly List<Vector3> ranged = new List<Vector3>();

    private void Awake()
    {
        magnet = GetComponent<LineMagnet>();
        hum = FxSound.AddLoop(gameObject, FxClip.MagnetLoop);
    }

    private void Start()
    {
        range = FxArt.NewLine("Magnet Field", 0.025f, 7);
        tether = FxArt.NewLine("Magnet Tether", 0.035f, 21);
        range.transform.SetPositionAndRotation(transform.position, transform.rotation);
        tether.transform.SetPositionAndRotation(transform.position, transform.rotation);
        wasActive = magnet.IsActive;
        BuildRange();
    }

    private void OnDestroy()
    {
        if (range != null) Destroy(range.gameObject);
        if (tether != null) Destroy(tether.gameObject);
    }

    private void BuildRange()
    {
        DuduSurface surface = magnet.Surface;
        if (surface == null || range == null) return;
        const int Points = 72;
        Vector2 center = surface.WorldToSurface(transform.position);
        range.positionCount = Points + 1;
        for (int i = 0; i <= Points; i++)
        {
            float angle = i * Mathf.PI * 2f / Points;
            Vector2 local = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * magnet.VerticalRangeMultiplier) * magnet.AttractionRange;
            local.x = Mathf.Clamp(local.x, -surface.Width * 0.5f, surface.Width * 0.5f);
            local.y = Mathf.Clamp(local.y, -surface.Height * 0.5f, surface.Height * 0.5f);
            range.SetPosition(i, FxWall.Point(surface, local));
        }
    }

    private void Update()
    {
        DuduSurface surface = magnet.Surface;
        if (surface == null || range == null) return;
        bool active = magnet.IsActive;
        float now = Time.time;
        Vector3 center = FxWall.OnWall(surface, transform.position);

        if (active != wasActive)
        {
            wasActive = active;
            FxSound.Play(active ? FxClip.Pulse : FxClip.Clack, center, 0.6f);
            FxRunner.Spawn(FxArt.Get(FxShape.Ring), center, FxWall.Rotation(surface), Vector3.zero, 0.4f,
                active ? magnet.AttractionRange * 2f : 0.3f, active ? 0.3f : 1.2f, new Color(Field.r, Field.g, Field.b, 0.75f), FxLayer.Behind);
        }

        // The field's edge, faint and breathing, only while it pulls.
        range.enabled = active;
        if (active)
        {
            Color edge = Field;
            edge.a = 0.26f + Mathf.Sin(now * 3f) * 0.08f;
            range.startColor = range.endColor = edge;
        }

        DuduSurfaceMovement dudu = FxView.Dudu;
        bool pulling = active && dudu != null && magnet.IsPulling(dudu.CurrentSurface, dudu.transform.position, out _);
        float proximity = 0f;
        if (dudu != null) magnet.IsPulling(dudu.CurrentSurface, dudu.transform.position, out proximity);

        if (active && now >= nextFlow)
        {
            nextFlow = now + 0.06f;
            SpawnFlowBit(surface, center);
        }
        if (active && now >= nextRing)
        {
            nextRing = now + 1.1f;
            FxRunner.Spawn(FxArt.Get(FxShape.Ring), center, FxWall.Rotation(surface), Vector3.zero, 0.8f, 1.6f, 0.3f,
                new Color(Field.r, Field.g, Field.b, 0.4f), FxLayer.Behind);
        }

        UpdateTether(surface, center, dudu, pulling, proximity, now);
        if (wasPulling && !pulling && dudu != null && dudu.CurrentSurface == surface)
        {
            // Broken free: the line snaps.
            Vector3 at = FxWall.OnWall(surface, dudu.transform.position, FxWall.Depth + 0.02f);
            FxRunner.Burst(FxShape.Spark, at, surface, 5, 1.8f, 0.24f, 0.17f, Field);
            FxSound.Play(FxClip.Snap, at, 0.7f);
        }
        wasPulling = pulling;

        float listen = FxView.Attenuation(center);
        FxSound.SetLoop(hum, active ? Mathf.Lerp(0.04f, 0.3f, pulling ? 0.4f + proximity * 0.6f : 0.1f) * listen : 0f,
            pulling ? 1f + proximity * 0.18f : 1f);
    }

    private void SpawnFlowBit(DuduSurface surface, Vector3 center)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float radius = magnet.AttractionRange * Random.Range(0.4f, 1f);
        Vector2 local = surface.WorldToSurface(center) +
            new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * magnet.VerticalRangeMultiplier * 0.6f) * radius;
        if (Mathf.Abs(local.x) > surface.Width * 0.5f || Mathf.Abs(local.y) > surface.Height * 0.5f) return;
        Vector3 start = FxWall.Point(surface, local);
        float life = Random.Range(0.8f, 1.2f);
        Vector3 velocity = (center - start) / life;
        FxRunner.Spawn(FxArt.Get(FxShape.Crumb), start, FxWall.Rotation(surface, Random.Range(0f, 360f)), velocity, life,
            0.16f, 0.06f, new Color(Field.r, Field.g, Field.b, 0.9f), FxLayer.Behind, spin: 200f, fadeIn: true);
    }

    private void UpdateTether(DuduSurface surface, Vector3 center, DuduSurfaceMovement dudu, bool pulling, float proximity, float now)
    {
        if (tether == null) return;
        tether.enabled = pulling;
        if (!pulling || now < nextTether) return;
        nextTether = now + 0.03f;
        Vector2 from = surface.WorldToSurface(dudu.transform.position);
        Vector2 to = surface.WorldToSurface(center);
        Vector2 side = (to - from).sqrMagnitude > 0.001f ? new Vector2(-(to - from).y, (to - from).x).normalized : Vector2.up;
        const int Points = 14;
        tether.positionCount = Points;
        for (int i = 0; i < Points; i++)
        {
            float t = i / (Points - 1f);
            float wave = Mathf.Sin(t * Mathf.PI) * (Mathf.Sin(t * 22f - now * 28f) * 0.05f + Random.Range(-0.015f, 0.015f));
            tether.SetPosition(i, FxWall.Point(surface, Vector2.Lerp(from, to, t) + side * wave, FxWall.Depth + 0.015f));
        }
        Color color = Field;
        color.a = Mathf.Lerp(0.45f, 1f, proximity);
        tether.startColor = tether.endColor = color;
        tether.startWidth = tether.endWidth = Mathf.Lerp(0.025f, 0.06f, proximity);
        DuduCameraController.RegisterRuntimeRenderer(tether);
    }
}

// Moving Eraser: a warning before it goes, speed and rubber dust while it goes, and a clear
// show of which lines it is about to take and is taking.
[DisallowMultipleComponent]
public sealed class EraserFx : MonoBehaviour
{
    private static readonly Color Pink = new Color(1f, 0.42f, 0.7f);
    private static readonly Color Rubber = new Color(0.98f, 0.62f, 0.8f);

    private MovingEraserObstacle eraser;
    private SpriteRenderer art;
    private LineRenderer warning;
    private Color normalColor = Color.white;
    private bool wasLaunched;
    private bool rumbled;
    private float nextGhost, nextDust, nextChevron, nextPathCheck, nextRub, nextMark;
    private Vector3 lastPosition;
    private Vector3 dashDirection;

    private void Awake()
    {
        eraser = GetComponent<MovingEraserObstacle>();
        art = GetComponent<SpriteRenderer>();
        lastPosition = transform.position;
    }

    private void Start()
    {
        if (art != null) normalColor = art.color;
        warning = FxArt.NewLine("Eraser Dash Warning", 0.04f, 7);
        warning.transform.SetPositionAndRotation(transform.position, transform.rotation);
    }

    private void OnDisable()
    {
        if (warning != null) warning.enabled = false;
    }

    private void OnDestroy()
    {
        if (warning != null) Destroy(warning.gameObject);
    }

    private void Update()
    {
        DuduSurface surface = eraser.Surface;
        if (surface == null) return;
        float aim = eraser.FxAim;
        bool launched = aim > 1.5f;
        bool aiming = aim > 0f && !launched;
        Vector3 position = transform.position;
        Vector3 moved = position - lastPosition;
        lastPosition = position;
        if (moved.sqrMagnitude > 0.000001f) dashDirection = moved.normalized;
        float now = Time.time;

        if (aiming)
        {
            DuduStatusFx.EraserThreatUntil = now + 0.12f;
            UpdateWarning(surface, aim, now);
        }
        else if (warning != null) warning.enabled = false;

        if (art != null)
        {
            // Glares red as it is about to go.
            float heat = aiming ? Mathf.InverseLerp(0.45f, 1f, aim) * (Mathf.Sin(now * 22f) * 0.5f + 0.5f) : 0f;
            art.color = Color.Lerp(normalColor, new Color(1f, 0.25f, 0.3f, normalColor.a), heat);
        }

        if (aiming && aim > 0.55f && !rumbled)
        {
            rumbled = true;
            FxSound.Play(FxClip.Rumble, position);
        }
        if (!aiming && !launched) rumbled = false;

        if (launched)
        {
            if (!wasLaunched)
            {
                FxSound.Play(FxClip.Swish, position);
                FxRunner.Burst(FxShape.Crumb, position, surface, 8, 2.2f, 0.4f, 0.1f, Rubber, FxLayer.Front, 4f);
                DuduStatusFx.EraserThreatUntil = now + 0.8f;
            }
            UpdateDash(surface, position, now);
        }
        wasLaunched = launched;
    }

    private void UpdateWarning(DuduSurface surface, float aim, float now)
    {
        DuduSurfaceMovement dudu = FxView.Dudu;
        if (warning == null || dudu == null || dudu.CurrentSurface != surface) { if (warning != null) warning.enabled = false; return; }
        Vector2 from = surface.WorldToSurface(transform.position);
        Vector2 toDudu = surface.WorldToSurface(dudu.transform.position) - from;
        if (toDudu.sqrMagnitude < 0.0001f) return;
        Vector2 direction = toDudu.normalized;
        float length = toDudu.magnitude + 2.5f;
        Vector2 to = from + direction * length;
        warning.enabled = true;
        warning.positionCount = 2;
        warning.SetPosition(0, FxWall.Point(surface, from));
        warning.SetPosition(1, FxWall.Point(surface, to));
        Color color = Pink;
        color.a = Mathf.Lerp(0.18f, 0.85f, aim) * (aim > 0.7f ? 0.55f + Mathf.Sin(now * 24f) * 0.45f : 1f);
        warning.startColor = color;
        color.a *= 0.15f;
        warning.endColor = color;
        warning.startWidth = warning.endWidth = Mathf.Lerp(0.03f, 0.075f, aim);
        DuduCameraController.RegisterRuntimeRenderer(warning);

        float angle = FxWall.Angle(direction);
        if (now >= nextChevron)
        {
            nextChevron = now + Mathf.Lerp(0.34f, 0.1f, aim);
            FxRunner.Spawn(FxArt.Get(FxShape.Chevron), FxWall.Point(surface, from + direction * 0.4f), FxWall.Rotation(surface, angle),
                FxWall.Direction(surface, direction) * Mathf.Lerp(2f, 5f, aim), Mathf.Min(0.7f, length / 4f), 0.26f, 0.32f,
                new Color(Pink.r, Pink.g, Pink.b, 0.85f), FxLayer.Behind, fadeIn: true);
        }
        if (now >= nextMark)
        {
            nextMark = now + 0.4f;
            FxRunner.Spawn(FxArt.Get(FxShape.Exclaim), FxWall.Point(surface, from + Vector2.up * 0.55f, FxWall.Depth + 0.03f), FxWall.Rotation(surface),
                Vector3.zero, 0.42f, 0.42f, 0.5f, new Color(1f, 0.25f, 0.35f, 0.95f), FxLayer.Top);
        }
        // Lines in its path are marked before they are hit.
        if (now >= nextPathCheck)
        {
            nextPathCheck = now + 0.16f;
            MarkLinesInPath(surface, from, direction, length);
        }
    }

    private void MarkLinesInPath(DuduSurface surface, Vector2 from, Vector2 direction, float length)
    {
        float radius = eraser.EraseRadius * 1.5f;
        for (float along = 0.3f; along < length; along += radius * 2f)
        {
            Vector3 point = FxWall.Point(surface, from + direction * along, surface.SurfaceOffset);
            foreach (DrawingStroke stroke in DrawingStroke.Live)
            {
                if (stroke == null || stroke.IsErased || !stroke.TryGetSqrDistance(point, Mathf.Max(radius, 0.35f), out _)) continue;
                FxRunner.Spawn(FxArt.Get(FxShape.Ring), FxWall.OnWall(surface, point, FxWall.Depth + 0.02f), FxWall.Rotation(surface), Vector3.zero,
                    0.3f, 0.5f, 0.2f, new Color(1f, 0.25f, 0.6f, 0.95f), FxLayer.Front);
                break;
            }
        }
    }

    private void UpdateDash(DuduSurface surface, Vector3 position, float now)
    {
        if (dashDirection == Vector3.zero) return;
        Vector2 flat = new Vector2(Vector3.Dot(dashDirection, surface.Right.normalized), Vector3.Dot(dashDirection, surface.Up.normalized));
        float angle = FxWall.Angle(flat);
        Vector3 at = FxWall.OnWall(surface, position);
        if (now >= nextGhost)
        {
            nextGhost = now + 0.04f;
            if (art != null && art.sprite != null)
                FxRunner.Spawn(art.sprite, position, transform.rotation, Vector3.zero, 0.2f, transform.lossyScale.x, transform.lossyScale.x,
                    new Color(Pink.r, Pink.g, Pink.b, 0.42f), FxLayer.Behind);
            FxRunner.Spawn(FxArt.Get(FxShape.Streak), at - dashDirection * 0.5f + FxWall.Direction(surface, Random.insideUnitCircle * 0.3f),
                FxWall.Rotation(surface, angle), -dashDirection * 1.2f, 0.25f, 1.1f, 0.6f, new Color(1f, 1f, 1f, 0.75f), FxLayer.Behind);
        }
        if (now >= nextDust)
        {
            nextDust = now + 0.03f;
            FxRunner.Spawn(FxArt.Get(FxShape.Crumb), at + FxWall.Direction(surface, Random.insideUnitCircle * 0.2f), FxWall.Rotation(surface, Random.Range(0f, 360f)),
                -dashDirection * Random.Range(0.3f, 1f) + surface.Up.normalized * Random.Range(0f, 0.6f), Random.Range(0.4f, 0.8f), 0.1f, 0.05f, Rubber, FxLayer.Front, 3.5f, 0f, 300f);
        }
        RubLines(surface, position, now);
    }

    // Erasing shows as big as it can on both screens: rubbed, then powder, then gone.
    private void RubLines(DuduSurface surface, Vector3 position, float now)
    {
        float radius = Mathf.Max(eraser.EraseRadius * 1.4f, 0.28f);
        foreach (DrawingStroke stroke in DrawingStroke.Live)
        {
            if (stroke == null || stroke.IsErased || stroke.Line == null) continue;
            if (!stroke.TryGetSqrDistance(position, radius, out _)) continue;
            Color wax = stroke.Line.startColor;
            Vector3 at = FxWall.OnWall(surface, position, FxWall.Depth + 0.02f);
            FxRunner.Burst(FxShape.Crumb, at, surface, 6, 2f, 0.6f, 0.1f, wax, FxLayer.Front, 5f);
            FxRunner.Burst(FxShape.Crumb, at, surface, 4, 1.6f, 0.6f, 0.09f, Rubber, FxLayer.Front, 5f);
            FxRunner.Spawn(FxArt.Get(FxShape.Soft), at, FxWall.Rotation(surface, Random.Range(0f, 360f)), Vector3.zero, 0.4f, 0.3f, 0.7f,
                new Color(Rubber.r, Rubber.g, Rubber.b, 0.65f), FxLayer.Front);
            // The host's real erase leaves its own smear; a mirror line on Dudu's PC needs one here.
            if (stroke.IsNetworkMirror) ErasedLineFade.Spawn(stroke.Line, position, radius);
            if (now >= nextRub)
            {
                nextRub = now + 0.09f;
                FxSound.Play(FxClip.Rub, position, 0.9f);
            }
        }
    }
}
