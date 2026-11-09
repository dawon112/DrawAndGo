using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// How clearing a stage looks: a short full-screen sequence on each player's own screen. The door
// lights up, the camera goes to it, and the view is drawn inside it. This class only shows
// things: the game decides a stage is clear, calls Play, and carries on from the finished callback
// (StageFlow moves on from there). Each PC plays its own copy; nothing here is sent over the network.
[DisallowMultipleComponent]
public sealed class ClearSequenceController : MonoBehaviour
{
    private static bool fadeInNextStage;

    [Header("Haru (first person)")]
    [Tooltip("Keeps the player's own view while the hand sinks out of the way.")]
    [SerializeField, Min(0f)] private float handLowerSeconds = 0.3f;
    [SerializeField, Min(0.1f)] private float doorStandOff = 2.4f;
    [Tooltip("Further from the door than this, the camera does not fly there; it dips to paper and starts near it.")]
    [SerializeField, Min(1f)] private float maxFlyDistance = 9f;
    [SerializeField, Min(0.05f)] private float dipSeconds = 0.22f;
    [SerializeField, Min(0.1f)] private float minMoveSeconds = 1.2f;
    [SerializeField, Min(0.1f)] private float maxMoveSeconds = 1.9f;
    [Header("Dudu (2D)")]
    [SerializeField, Min(0f)] private float duduBeatSeconds = 0.5f;
    [SerializeField, Min(0.1f)] private float duduWalkSeconds = 1.1f;
    [SerializeField, Min(0.5f)] private float duduDoorDistance = 3.2f;
    [SerializeField, Min(0.5f)] private float duduDoorHeight = 2.3f;
    [Header("Into the door (both)")]
    [SerializeField, Min(0.1f)] private float enterSeconds = 0.85f;
    [Tooltip("Share of the way in when the screen starts fading to paper.")]
    [SerializeField, Range(0f, 1f)] private float fadeStart = 0.35f;
    [Header("Colours")]
    [SerializeField] private Color glowColor = new Color(1f, 0.82f, 0.42f);
    [SerializeField] private Color insideColor = new Color(0.1f, 0.07f, 0.1f);

    private static Sprite SmoothGlow => Resources.Load<Sprite>("UI/Hud/glow");

    private static readonly Color[] BitColors =
    {
        new Color(1f, 0.92f, 0.72f), new Color(1f, 0.78f, 0.35f), new Color(0.95f, 0.55f, 0.3f), new Color(0.55f, 0.75f, 1f)
    };

    private GameViewManager view;
    private Camera haruCamera, duduCamera, cinematicCamera;
    private CanvasGroup fade;
    private DuduSurfaceMovement dudu;
    private Player3DMovement haru;
    private HaruDrawingController drawing;
    private Transform props;
    private bool playing;

    private struct DoorShape
    {
        public Vector3 center, front, side;
        public float width, height;
    }

    public void Configure(GameViewManager owner, Camera haruView, Camera duduView, Camera cinematic, CanvasGroup screenFade,
        DuduSurfaceMovement duduMovement, Player3DMovement haruMovement, HaruDrawingController haruDrawing)
    {
        view = owner;
        haruCamera = haruView;
        duduCamera = duduView;
        cinematicCamera = cinematic;
        fade = screenFade;
        dudu = duduMovement;
        haru = haruMovement;
        drawing = haruDrawing;
    }

    // True once, right after a clear: the next stage comes up out of the paper-coloured fade.
    public static bool ConsumeFadeIn()
    {
        bool wanted = fadeInNextStage;
        fadeInNextStage = false;
        return wanted;
    }

    public void Play(GameObject door, Vector3 doorFacing, Action openDoor, Action finished)
    {
        if (playing) return;
        playing = true;
        bool last = StageSequence.Load().NextAfter(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name) == null;
        bool duduView = view.IsDuduMode;
        view.EnterCinematic(GameViewManager.CameraState.ClearCinematic);
        props = new GameObject("Clear Sequence").transform;
        props.SetParent(transform, false);
        StartCoroutine(duduView ? DuduSequence(door, openDoor, finished, last) : HaruSequence(door, doorFacing, openDoor, finished, last));
    }

    // ---- Haru: from where the player is looking, to the door, through it ----------------

    private IEnumerator HaruSequence(GameObject door, Vector3 doorFacing, Action openDoor, Action finished, bool last)
    {
        DoorShape shape = Measure(door, doorFacing);
        HaruDrawingArm arm = drawing != null ? drawing.GetComponent<HaruDrawingArm>() : null;
        if (arm != null) arm.Lowered = true;

        PlaySuccessSounds(shape.center, last);
        openDoor?.Invoke();
        DoorLook look = BuildDoorLook(shape, last);

        // The player's own view for a moment, while the hand goes down. Nothing cuts yet.
        for (float t = 0f; t < handLowerSeconds; t += Time.unscaledDeltaTime)
        {
            look.Animate(Mathf.Clamp01(t / 0.5f), 0f);
            yield return null;
        }
        SwitchToCinematicCamera();
        Transform shot = cinematicCamera.transform;

        // Far from the door or out of sight of it: no flying through the room at speed or
        // through its walls. A quick dip to paper, and the shot starts in front of the door.
        Vector3 approach = ApproachPoint(shape, shot.position);
        if (Vector3.Distance(shot.position, approach) > maxFlyDistance || !CanSee(shot.position, approach, door))
        {
            for (float t = 0f; t < dipSeconds; t += Time.unscaledDeltaTime)
            {
                fade.alpha = Mathf.SmoothStep(0f, 1f, t / dipSeconds);
                yield return null;
            }
            Vector3 opening = shape.center + shape.front * (Vector3.Distance(approach, shape.center) + 2.4f);
            opening.y = approach.y;
            shot.SetPositionAndRotation(opening, Quaternion.LookRotation(shape.center - opening, Vector3.up));
            look.Animate(1f, 0f);
            for (float t = 0f; t < dipSeconds; t += Time.unscaledDeltaTime)
            {
                fade.alpha = 1f - Mathf.SmoothStep(0f, 1f, t / dipSeconds);
                yield return null;
            }
            fade.alpha = 0f;
        }

        // Turn to the door first, then glide to it: the eyes lead and the body follows.
        Vector3 startPosition = shot.position;
        Quaternion startRotation = shot.rotation;
        float startFov = cinematicCamera.fieldOfView;
        float distance = Vector3.Distance(startPosition, approach);
        Quaternion approachRotation = Quaternion.LookRotation(shape.center - approach, Vector3.up);
        float turn = Quaternion.Angle(startRotation, approachRotation);
        float moveSeconds = Mathf.Clamp(0.8f + distance / 9f + turn / 300f, minMoveSeconds, maxMoveSeconds);
        float bits = 0f;
        for (float t = 0f; t < moveSeconds; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / moveSeconds);
            shot.rotation = Quaternion.Slerp(startRotation, approachRotation, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.7f, u)));
            shot.position = Vector3.Lerp(startPosition, approach, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 1f, u)));
            cinematicCamera.fieldOfView = Mathf.Lerp(startFov, 50f, Mathf.SmoothStep(0f, 1f, u));
            look.Animate(1f, 0f);
            bits = EmitInflow(shape, bits, last ? 26f : 16f);
            yield return null;
        }

        // Drawn in: the doorway fills the screen, speeding up as it goes.
        FxSound.Play(FxClip.Absorb, shape.center, 1f, 0f);
        Vector3 inside = shape.center - shape.front * 0.6f;
        for (float t = 0f; t < enterSeconds; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / enterSeconds);
            float eased = u * u;
            shot.position = Vector3.Lerp(approach, inside, eased);
            cinematicCamera.fieldOfView = Mathf.Lerp(50f, 34f, u);
            look.Animate(1f, u);
            bits = EmitInflow(shape, bits, last ? 44f : 30f);
            fade.alpha = Mathf.InverseLerp(fadeStart, 1f, u);
            yield return null;
        }
        Finish(finished);
    }

    // Far enough back to see the door whole, or as far as Haru already is if that is further.
    private Vector3 ApproachPoint(DoorShape shape, Vector3 from)
    {
        float wanted = Mathf.Max(doorStandOff, shape.height * 1.15f);
        float along = Vector3.Dot(from - shape.center, shape.front);
        float back = Mathf.Clamp(along, wanted, Mathf.Max(wanted, maxFlyDistance));
        Vector3 point = shape.center + shape.front * back;
        point.y = shape.center.y - shape.height * 0.08f;
        return point;
    }

    private bool CanSee(Vector3 from, Vector3 to, GameObject door)
    {
        Vector3 path = to - from;
        Transform self = haru != null ? haru.transform.root : null;
        foreach (RaycastHit hit in Physics.RaycastAll(from, path.normalized, path.magnitude, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            Transform other = hit.collider.transform;
            if (self != null && other.IsChildOf(self)) continue;
            if (door != null && other.IsChildOf(door.transform)) continue;
            if (hit.collider.GetComponentInParent<DrawingStroke>() != null || hit.collider.GetComponentInParent<DuduSurfaceMovement>() != null) continue;
            return false;
        }
        return true;
    }

    private void SwitchToCinematicCamera()
    {
        cinematicCamera.CopyFrom(haruCamera);
        // CopyFrom leaves URP's own settings; without these the picture changes at the cut.
        UniversalAdditionalCameraData from = haruCamera.GetUniversalAdditionalCameraData();
        UniversalAdditionalCameraData to = cinematicCamera.GetUniversalAdditionalCameraData();
        to.renderPostProcessing = from.renderPostProcessing;
        to.antialiasing = from.antialiasing;
        to.renderShadows = from.renderShadows;
        to.volumeLayerMask = from.volumeLayerMask;
        to.volumeTrigger = from.volumeTrigger;
        cinematicCamera.transform.SetPositionAndRotation(haruCamera.transform.position, haruCamera.transform.rotation);
        cinematicCamera.gameObject.SetActive(true);
        cinematicCamera.enabled = true;
        haruCamera.gameObject.SetActive(false);
    }

    private Vector3 HaruPosition => haru != null ? haru.transform.position : haruCamera != null ? haruCamera.transform.position : Vector3.zero;

    private DoorShape Measure(GameObject door, Vector3 doorFacing)
    {
        Bounds bounds = new Bounds(door != null ? door.transform.position : HaruPosition, Vector3.zero);
        bool found = false;
        if (door != null)
            foreach (Renderer part in door.GetComponentsInChildren<Renderer>(true))
            {
                if (!found) bounds = part.bounds; else bounds.Encapsulate(part.bounds);
                found = true;
            }
        Vector3 front = Vector3.ProjectOnPlane(doorFacing, Vector3.up).normalized;
        if (front.sqrMagnitude < 0.1f) front = Vector3.forward;
        if (Vector3.Dot(HaruPosition - bounds.center, front) < 0f) front = -front;
        Vector3 side = Vector3.Cross(Vector3.up, front).normalized;
        Vector3 extents = bounds.extents;
        float width = 2f * (Mathf.Abs(side.x) * extents.x + Mathf.Abs(side.y) * extents.y + Mathf.Abs(side.z) * extents.z);
        return new DoorShape
        {
            center = bounds.center, front = front, side = side,
            width = found ? Mathf.Clamp(width, 1f, 4f) : 2f,
            height = found ? Mathf.Clamp(bounds.size.y, 1.5f, 6f) : 3f
        };
    }

    private float EmitInflow(DoorShape shape, float pending, float perSecond)
    {
        pending += perSecond * Time.unscaledDeltaTime;
        Quaternion facing = FxWall.FacingAway(shape.front);
        while (pending >= 1f)
        {
            pending -= 1f;
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float radius = UnityEngine.Random.Range(0.8f, 1.5f);
            Vector3 start = shape.center + shape.front * UnityEngine.Random.Range(0.2f, 1.1f) +
                shape.side * Mathf.Cos(angle) * shape.width * radius + Vector3.up * Mathf.Sin(angle) * shape.height * 0.6f * radius;
            float life = UnityEngine.Random.Range(0.45f, 0.8f);
            Vector3 target = shape.center - shape.front * 0.15f;
            FxRunner.Spawn(FxArt.Get(UnityEngine.Random.value < 0.75f ? FxShape.Crumb : FxShape.Spark), start,
                facing * Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f)), (target - start) / life, life,
                UnityEngine.Random.Range(0.06f, 0.14f), 0.02f, BitColors[UnityEngine.Random.Range(0, BitColors.Length)], FxLayer.Top,
                spin: UnityEngine.Random.Range(-300f, 300f), fadeIn: true);
        }
        return pending;
    }

    // The doorway, made to read as a way through: dark depth, warm light, a glowing edge.
    private DoorLook BuildDoorLook(DoorShape shape, bool last)
    {
        Quaternion facing = FxWall.FacingAway(shape.front);
        Sprite soft = SmoothGlow;
        SpriteRenderer glow = NewSprite("Door Glow", soft, shape.center + shape.front * 0.08f, facing, 60);
        glow.transform.localScale = new Vector3(shape.width * 4f, shape.height * 2f, 1f);
        // Solid and square-edged: this is the dark of the doorway itself, inside the frame.
        SpriteRenderer inside = NewSprite("Door Inside", FxArt.Get(FxShape.Box), shape.center + shape.front * 0.12f, facing, 61);
        inside.transform.localScale = new Vector3(shape.width * 0.86f, shape.height * 0.97f, 1f);
        SpriteRenderer core = NewSprite("Door Core", soft, shape.center + shape.front * 0.18f, facing, 63);

        LineRenderer edge = FxArt.NewLine("Door Edge", 0.07f, 64, props);
        edge.enabled = true;
        edge.loop = true;
        edge.positionCount = 4;
        Vector3 x = shape.side * shape.width * 0.5f, y = Vector3.up * shape.height * 0.5f, z = shape.front * 0.22f;
        edge.SetPosition(0, shape.center - x - y + z);
        edge.SetPosition(1, shape.center + x - y + z);
        edge.SetPosition(2, shape.center + x + y + z);
        edge.SetPosition(3, shape.center - x + y + z);

        Light light = new GameObject("Door Light").AddComponent<Light>();
        light.transform.SetParent(props, false);
        light.transform.position = shape.center + shape.front * 0.8f;
        light.type = LightType.Point;
        light.color = glowColor;
        light.range = 7f;
        light.shadows = LightShadows.None;
        light.intensity = 0f;

        return new DoorLook
        {
            inside = inside, glow = glow, core = core, edge = edge, light = light,
            coreSize = new Vector2(shape.width, shape.height),
            glowColor = glowColor, insideColor = insideColor, strength = last ? 1.25f : 1f
        };
    }

    private SpriteRenderer NewSprite(string name, Sprite sprite, Vector3 position, Quaternion rotation, int order)
    {
        GameObject holder = new GameObject(name);
        holder.transform.SetParent(props, false);
        holder.transform.SetPositionAndRotation(position, rotation);
        SpriteRenderer renderer = holder.AddComponent<SpriteRenderer>();
        renderer.sharedMaterial = FxArt.SolidMaterial;
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        renderer.color = Color.clear;
        return renderer;
    }

    private sealed class DoorLook
    {
        public SpriteRenderer inside, glow, core;
        public LineRenderer edge;
        public Light light;
        public Color glowColor, insideColor;
        public Vector2 coreSize;
        public float strength;

        // appear: 0-1 as the door lights up. enter: 0-1 as the view goes in, when the light takes over.
        public void Animate(float appear, float enter)
        {
            float breathe = 0.9f + Mathf.Sin(Time.unscaledTime * 5f) * 0.1f;
            Color deep = insideColor;
            deep.a = appear;
            inside.color = deep;
            Color wide = glowColor;
            wide.a = appear * (0.6f * strength + enter * 0.4f) * breathe;
            glow.color = wide;
            // A small light deep inside, swelling until it takes the whole door.
            float swell = Mathf.Lerp(0.8f, 3f, enter * enter);
            core.transform.localScale = new Vector3(coreSize.x * swell, coreSize.y * swell * 0.85f, 1f);
            Color center = Color.Lerp(glowColor, Color.white, 0.55f);
            center.a = appear * (0.75f + enter * 0.25f) * breathe;
            core.color = center;
            Color rim = Color.Lerp(glowColor, Color.white, 0.3f);
            rim.a = appear * 0.9f * breathe * (1f - enter * 0.6f);
            edge.startColor = edge.endColor = rim;
            light.intensity = appear * (5f + enter * 8f) * strength * breathe;
        }
    }

    // ---- Dudu: the 2D view keeps Dudu and a crayon door in frame, and he walks in ---------

    private IEnumerator DuduSequence(GameObject door, Action openDoor, Action finished, bool last)
    {
        DuduCameraController controller = duduCamera != null ? duduCamera.GetComponent<DuduCameraController>() : null;
        DuduSurface surface = dudu != null ? dudu.CurrentSurface : null;
        openDoor?.Invoke();
        if (controller == null || surface == null || controller.ViewRoot == null)
        {
            // No 2D view to work with: still end the same way.
            for (float t = 0f; t < enterSeconds; t += Time.unscaledDeltaTime)
            {
                fade.alpha = Mathf.Clamp01(t / enterSeconds);
                yield return null;
            }
            Finish(finished);
            yield break;
        }

        dudu.BeginCinematicWalk();
        Vector2 start = surface.WorldToSurface(dudu.transform.position);
        float doorX = ChooseDoorX(door, surface, start);
        float facing = Mathf.Sign(doorX - start.x);
        float height = duduDoorHeight;
        Vector3 startView = controller.MapToView(dudu.transform.position, surface);
        Vector3 doorBase = controller.MapToView(surface.SurfaceToWorld(new Vector2(doorX, start.y)), surface);
        float feetOffset = dudu.GetComponent<DuduMoveFeedback>() != null
            ? start.y - surface.WorldToSurface(dudu.GetComponent<DuduMoveFeedback>().FeetPoint()).y : 0.7f;
        Vector3 doorCenter = doorBase + Vector3.up * (height * 0.5f - feetOffset);

        CrayonDoor crayonDoor = BuildCrayonDoor(controller.ViewRoot, doorCenter, height, last);
        PlaySuccessSounds(duduCamera.transform.position, last);
        SpriteRenderer duduPicture = dudu.Visual;
        Color pictureColor = duduPicture != null ? duduPicture.color : Color.white;
        Vector2 cameraStart = duduCamera.transform.position;
        float sizeStart = duduCamera.orthographicSize;
        Vector2 frameBoth = new Vector2((startView.x + doorCenter.x) * 0.5f, Mathf.Lerp(cameraStart.y, doorCenter.y, 0.5f));

        // The door draws itself; Dudu turns to it; the view settles to hold them both.
        dudu.SetCinematicWalking(false, facing < 0f);
        for (float t = 0f; t < duduBeatSeconds; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / duduBeatSeconds);
            crayonDoor.Animate(Mathf.SmoothStep(0f, 1f, u), 0f);
            controller.SetCinematicFraming(Vector2.Lerp(cameraStart, frameBoth, Mathf.SmoothStep(0f, 1f, u)), sizeStart);
            yield return null;
        }

        // He walks in, and the door comes to the middle of the screen.
        dudu.SetCinematicWalking(true, facing < 0f);
        float bits = 0f;
        for (float t = 0f; t < duduWalkSeconds; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / duduWalkSeconds);
            float x = Mathf.Lerp(start.x, doorX, Mathf.SmoothStep(0f, 1f, u));
            Vector3 world = surface.SurfaceToWorld(new Vector2(x, start.y));
            dudu.ApplyRemotePose(surface, world, surface.transform.rotation, facing < 0f);
            float eased = Mathf.SmoothStep(0f, 1f, u);
            controller.SetCinematicFraming(Vector2.Lerp(frameBoth, doorCenter, eased), Mathf.Lerp(sizeStart, sizeStart * 0.72f, eased));
            crayonDoor.Animate(1f, 0f);
            bits = EmitViewBits(controller, doorCenter, height, bits, last ? 22f : 14f);
            yield return null;
        }

        // Inside: Dudu fades into the light as the doorway fills the screen.
        dudu.SetCinematicWalking(false, facing < 0f);
        FxSound.Play(FxClip.Absorb, duduCamera.transform.position, 1f, 0f);
        float zoomStart = duduCamera.orthographicSize;
        for (float t = 0f; t < enterSeconds; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / enterSeconds);
            float eased = u * u;
            controller.SetCinematicFraming(doorCenter, Mathf.Lerp(zoomStart, height * 0.22f, eased));
            crayonDoor.Animate(1f, u);
            if (duduPicture != null)
            {
                Color color = pictureColor;
                color.a = pictureColor.a * (1f - Mathf.InverseLerp(0.05f, 0.5f, u));
                duduPicture.color = color;
            }
            bits = EmitViewBits(controller, doorCenter, height, bits, last ? 34f : 24f);
            fade.alpha = Mathf.InverseLerp(fadeStart, 1f, u);
            yield return null;
        }
        Finish(finished);
    }

    // Toward the real door if it is along this wall, otherwise simply ahead of Dudu.
    private float ChooseDoorX(GameObject door, DuduSurface surface, Vector2 start)
    {
        float limit = Mathf.Max(0.5f, surface.Width * 0.5f - 1.2f);
        float direction = dudu.FacingLeft ? -1f : 1f;
        if (door != null)
        {
            Vector3 real = door.transform.position;
            Vector2 local = surface.WorldToSurface(real);
            float plane = Mathf.Abs(Vector3.Dot(real - surface.transform.position, surface.Normal.normalized));
            if (plane < 3f && Mathf.Abs(local.x) < surface.Width * 0.5f + 1f && Mathf.Abs(local.x - start.x) > 0.5f)
                direction = Mathf.Sign(local.x - start.x);
        }
        float x = start.x + direction * duduDoorDistance;
        if (Mathf.Abs(x) > limit) x = Mathf.Clamp(x, -limit, limit);
        // Too close to an edge in that direction: the door goes on the other side.
        if (Mathf.Abs(x - start.x) < 1.2f) x = Mathf.Clamp(start.x - direction * duduDoorDistance, -limit, limit);
        return x;
    }

    private float EmitViewBits(DuduCameraController controller, Vector3 center, float height, float pending, float perSecond)
    {
        pending += perSecond * Time.unscaledDeltaTime;
        while (pending >= 1f)
        {
            pending -= 1f;
            Vector2 away = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(height * 0.65f, height * 1.25f);
            float life = UnityEngine.Random.Range(0.4f, 0.75f);
            StartCoroutine(FlyViewBit(controller.ViewRoot, center + (Vector3)away, center, life));
        }
        return pending;
    }

    // Dudu's view shows its own layer, so its bits are drawn straight in view coordinates.
    private IEnumerator FlyViewBit(Transform root, Vector3 from, Vector3 to, float life)
    {
        GameObject holder = new GameObject("Door Bit");
        holder.layer = root.gameObject.layer;
        holder.transform.SetParent(root, false);
        SpriteRenderer bit = holder.AddComponent<SpriteRenderer>();
        bit.sharedMaterial = FxArt.SolidMaterial;
        bit.sprite = FxArt.Get(UnityEngine.Random.value < 0.75f ? FxShape.Crumb : FxShape.Spark);
        bit.sortingOrder = 14;
        Color color = BitColors[UnityEngine.Random.Range(0, BitColors.Length)];
        float size = UnityEngine.Random.Range(0.1f, 0.2f);
        from.z = to.z = -0.3f;
        for (float t = 0f; t < life; t += Time.unscaledDeltaTime)
        {
            float u = t / life;
            holder.transform.position = Vector3.Lerp(from, to, u * u);
            holder.transform.rotation = Quaternion.Euler(0f, 0f, t * 300f);
            holder.transform.localScale = Vector3.one * Mathf.Lerp(size, size * 0.3f, u);
            color.a = Mathf.Min(u * 5f, 1f);
            bit.color = color;
            yield return null;
        }
        Destroy(holder);
    }

    private CrayonDoor BuildCrayonDoor(Transform root, Vector3 center, float height, bool last)
    {
        return new CrayonDoor
        {
            inside = NewViewSprite(root, "Crayon Door Inside", FxArt.Get(FxShape.Door), center, 8),
            frame = NewViewSprite(root, "Crayon Door Frame", FxArt.Get(FxShape.DoorFrame), center, 9),
            glow = NewViewSprite(root, "Crayon Door Light", SmoothGlow, center, 7),
            core = NewViewSprite(root, "Crayon Door Core", SmoothGlow, center, 12),
            height = height, glowColor = glowColor, insideColor = insideColor, strength = last ? 1.25f : 1f
        };
    }

    private static SpriteRenderer NewViewSprite(Transform root, string name, Sprite sprite, Vector3 center, int order)
    {
        GameObject holder = new GameObject(name);
        holder.layer = root.gameObject.layer;
        holder.transform.SetParent(root, false);
        holder.transform.position = new Vector3(center.x, center.y, -0.15f);
        SpriteRenderer renderer = holder.AddComponent<SpriteRenderer>();
        renderer.sharedMaterial = FxArt.SolidMaterial;
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        renderer.color = Color.clear;
        return renderer;
    }

    private sealed class CrayonDoor
    {
        public SpriteRenderer inside, frame, glow, core;
        public float height, strength;
        public Color glowColor, insideColor;

        public void Animate(float appear, float enter)
        {
            float breathe = 0.92f + Mathf.Sin(Time.unscaledTime * 5f) * 0.08f;
            // It rises out of the ground line a little as it appears.
            float grow = Mathf.Lerp(0.8f, 1f, appear) * height;
            inside.transform.localScale = frame.transform.localScale = Vector3.one * grow;
            glow.transform.localScale = Vector3.one * grow * (2.6f + enter);
            core.transform.localScale = Vector3.one * grow * (0.75f + enter * 1.8f);
            Color deep = insideColor;
            deep.a = appear;
            inside.color = deep;
            Color rim = Color.Lerp(glowColor, new Color(0.45f, 0.28f, 0.12f), 0.35f);
            rim.a = appear;
            frame.color = rim;
            Color wide = glowColor;
            wide.a = appear * 0.55f * strength * breathe;
            glow.color = wide;
            Color middle = Color.Lerp(glowColor, Color.white, 0.5f);
            middle.a = appear * (0.5f + enter * 0.5f) * breathe;
            core.color = middle;
        }
    }

    // ---- Shared ---------------------------------------------------------------------------

    private static void PlaySuccessSounds(Vector3 at, bool last)
    {
        FxSound.Play(FxClip.DoorHum, FxView.Listener, 0.9f, 0f);
        if (last) FxSound.Play(FxClip.Chime, FxView.Listener, 0.8f, 0f);
    }

    private void Finish(Action finished)
    {
        fade.alpha = 1f;
        fade.blocksRaycasts = true;
        FxSound.Play(FxClip.Pop, FxView.Listener, 0.35f, 0f);
        fadeInNextStage = true;
        finished?.Invoke();
    }
}
