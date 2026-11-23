using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

[RequireComponent(typeof(Camera))]
public sealed class DuduCameraController : MonoBehaviour
{
    private const int UnwrappedLayer = 30;
    private static DuduCameraController activeInstance;
    [SerializeField] private DuduSurface targetSurface;
    [SerializeField] private Transform targetDudu;
    [SerializeField, Min(0.1f)] private float cameraDistance = 10f;
    [SerializeField, Min(0.1f)] private float orthographicSize = 3.5f;
    [SerializeField, Min(0.01f)] private float followSmoothTime = 0.15f;
    [SerializeField] private float horizontalOffset = 1f;
    [Header("Haru Cursor")]
    [Tooltip("Shown where Haru's crayon touches the wall while Haru can draw there.")]
    [SerializeField] private Sprite haruCrayonCursor;
    [Tooltip("Shown where Haru's eraser touches the wall while Haru can erase there.")]
    [SerializeField] private Sprite haruEraserCursor;
    [SerializeField, Min(0.01f)] private float haruCursorScale = 0.28f;
    [Header("Dudu Debuff Overlay")]
    [SerializeField] private Sprite[] slowOverlayFrames;
    [SerializeField] private Sprite[] reverseOverlayFrames;
    [SerializeField, Min(0.02f)] private float overlayFrameDuration = 0.12f;

    private readonly List<VisualProxy> visualProxies = new List<VisualProxy>();
    private readonly HashSet<Renderer> proxiedSources = new HashSet<Renderer>();
    private bool cinematicFraming;
    private Vector2 cinematicCenter;
    private float cinematicSize;
    private Vector3 shakeApplied;
    private float shakeAmount, shakeUntil, shakeLength, rollAmount, rollUntil, rollLength;

    public static void Shake(float amount, float seconds)
    {
        if (activeInstance == null) return;
        activeInstance.shakeAmount = amount;
        activeInstance.shakeLength = seconds;
        activeInstance.shakeUntil = Time.unscaledTime + seconds;
    }

    // The whole view lurches over and back.
    public static void Roll(float degrees, float seconds)
    {
        if (activeInstance == null) return;
        activeInstance.rollAmount = degrees;
        activeInstance.rollLength = seconds;
        activeInstance.rollUntil = Time.unscaledTime + seconds;
    }
    private Camera cameraComponent;
    private Transform proxyRoot;
    private DuduSurface[] surfaces;
    private float[] surfaceCenters;
    // How far each wall's middle sits above the first wall's: walls are laid out by real
    // height too, so a wall reaching up two floors meets the floors on either side of it.
    private float[] surfaceLifts;
    private float totalWidth;
    private float verticalVelocity;
    private readonly List<Vector3> clippedLinePoints = new List<Vector3>();
    private float horizontalVelocity;
    private float nextProxyRefresh;
    private HaruDrawingController localHaruDrawing;
    private Transform haruCursorRoot;
    private SpriteRenderer haruCursor;
    private bool haruCursorShown;
    private MaterialPropertyBlock proxyPropertyBlock;
    private DuduSurfaceMovement duduMovement;
    private Image debuffOverlay;
    // The walls stand still (their places in this view are laid out once, in Start), so
    // their axes are read once too: finding a thing's wall is then plain arithmetic.
    private Vector3[] surfacePositions, surfaceRights, surfaceUps, surfaceNormals, surfaceFacings;
    // Renderers that can never get a proxy (by kind, name or place), so the periodic
    // look for new renderers does not weigh them again and again.
    private readonly HashSet<Renderer> neverProxied = new HashSet<Renderer>();
    private readonly Dictionary<Renderer, VisualProxy> offTheWalls = new Dictionary<Renderer, VisualProxy>();
    private readonly List<Renderer> goneRenderers = new List<Renderer>();
    private int refreshCount;
    private Vector3[] linePointBuffer = new Vector3[64];

    // A world point as this camera sees it: walls laid end to end. UI that follows something
    // in this view projects the result with this camera.
    public Vector3 MapToView(Vector3 world, DuduSurface surface = null)
    {
        if (surfaces == null) return world;
        if (surface == null) surface = FindSurface(world);
        int index = surface != null ? System.Array.IndexOf(surfaces, surface) : -1;
        if (index < 0) return world;
        return ToView(index, surface.WorldToSurface(world), 0f);
    }

    private Vector3 ToView(int index, Vector2 local, float depth) =>
        new Vector3(surfaceCenters[index] + local.x, local.y + surfaceLifts[index], depth);

    // Where to put things that only this view shows (they use its layer and view coordinates).
    public Transform ViewRoot => proxyRoot;

    // Takes the camera from following Dudu, for a cinematic. Stage scenes end after those,
    // so there is nothing to hand back.
    public void SetCinematicFraming(Vector2 center, float size)
    {
        cinematicFraming = true;
        cinematicCenter = center;
        cinematicSize = size;
    }

    public bool TryGetHaruCursorView(out Vector3 position)
    {
        bool shown = haruCursorRoot != null && haruCursorRoot.gameObject.activeSelf;
        position = shown ? haruCursorRoot.position : Vector3.zero;
        return shown;
    }

    public void SetSurface(DuduSurface surface) => targetSurface = surface;
    public void SetTarget(Transform target) => targetDudu = target;

    private void Awake()
    {
        cameraComponent = GetComponent<Camera>();
        proxyPropertyBlock = new MaterialPropertyBlock();
        cameraComponent.orthographic = true;
        cameraComponent.orthographicSize = orthographicSize;
        CreateDebuffOverlay();
        SetWhiteBackground();
    }

    private void OnEnable()
    {
        activeInstance = this;
        if (cameraComponent == null) cameraComponent = GetComponent<Camera>();
        EnsureDebuffOverlay();
        SetWhiteBackground();
        ConfigureCameraMasks();
    }

    private void SetWhiteBackground()
    {
        if (cameraComponent == null) return;
        cameraComponent.clearFlags = CameraClearFlags.SolidColor;
        // Around the page the view shows this colour; match the stage's sunlit paper.
        StageLook look = StageLook.Load();
        cameraComponent.backgroundColor = look != null ? look.duduBackdrop : Color.white;
    }

    public static void RegisterRuntimeRenderer(Renderer source)
    {
        if (activeInstance == null || source == null)
            return;

        activeInstance.AddVisualProxy(source);
    }

    private void Start()
    {
        CornerRoomLevel level = FindAnyObjectByType<CornerRoomLevel>();
        surfaces = level != null ? level.surfaces : null;
        if (surfaces == null || surfaces.Length == 0)
        {
            TutorialRoomLevel tutorialLevel = FindAnyObjectByType<TutorialRoomLevel>();
            surfaces = tutorialLevel != null ? tutorialLevel.Sections : null;
        }
        if (surfaces == null || surfaces.Length == 0) return;

        surfaceCenters = new float[surfaces.Length];
        // Walls are laid end to end at their real lengths, so what is drawn on either side of
        // a corner keeps its own place, and Dudu walks straight on across the seam.
        for (int i = 0; i < surfaces.Length; i++)
        {
            surfaceCenters[i] = totalWidth + surfaces[i].Width * 0.5f;
            totalWidth += surfaces[i].Width;
        }
        for (int i = 0; i < surfaceCenters.Length; i++)
            surfaceCenters[i] -= totalWidth * 0.5f;
        surfaceLifts = new float[surfaces.Length];
        for (int i = 0; i < surfaces.Length; i++)
            surfaceLifts[i] = surfaces[i].transform.position.y - surfaces[0].transform.position.y;
        surfacePositions = new Vector3[surfaces.Length];
        surfaceRights = new Vector3[surfaces.Length];
        surfaceUps = new Vector3[surfaces.Length];
        surfaceNormals = new Vector3[surfaces.Length];
        surfaceFacings = new Vector3[surfaces.Length];
        for (int i = 0; i < surfaces.Length; i++)
        {
            if (surfaces[i] == null) continue;
            surfacePositions[i] = surfaces[i].transform.position;
            surfaceRights[i] = surfaces[i].Right.normalized;
            surfaceUps[i] = surfaces[i].Up.normalized;
            surfaceNormals[i] = surfaces[i].Normal;
            surfaceFacings[i] = surfaces[i].Normal.normalized;
        }

        proxyRoot = new GameObject("Dudu Unwrapped View").transform;
        proxyRoot.gameObject.layer = UnwrappedLayer;
        localHaruDrawing = FindAnyObjectByType<HaruDrawingController>(FindObjectsInactive.Include);
        CreateHaruCursor();
        ConfigureCameraMasks();
        RefreshVisualProxies();
        SnapToTarget();
    }

    private void ConfigureCameraMasks()
    {
        if (cameraComponent == null) return;
        cameraComponent.cullingMask = 1 << UnwrappedLayer;
        foreach (Camera otherCamera in FindObjectsByType<Camera>(FindObjectsInactive.Include))
            if (otherCamera != cameraComponent) otherCamera.cullingMask &= ~(1 << UnwrappedLayer);
    }

    private void LateUpdate()
    {
        UpdateDebuffOverlay();
        if (surfaces == null || targetDudu == null) return;
        if (targetDudu.TryGetComponent(out DuduSurfaceMovement movement) && movement.CurrentSurface != null)
            targetSurface = movement.CurrentSurface;
        if (Time.unscaledTime >= nextProxyRefresh) RefreshVisualProxies();
        UpdateVisualProxies();
        UpdateHaruCursor();

        if (cinematicFraming)
        {
            cameraComponent.orthographicSize = cinematicSize;
            transform.SetPositionAndRotation(new Vector3(cinematicCenter.x, cinematicCenter.y, -cameraDistance), Quaternion.identity);
            return;
        }

        float targetX = GetUnwrappedX(targetDudu.position, targetSurface) + horizontalOffset;
        float viewHalfWidth = orthographicSize * cameraComponent.aspect;
        float limit = Mathf.Max(0f, totalWidth * 0.5f - viewHalfWidth);
        targetX = Mathf.Clamp(targetX, -limit, limit);
        float x = Mathf.SmoothDamp(transform.position.x - shakeApplied.x, targetX, ref horizontalVelocity, followSmoothTime);
        float now = Time.unscaledTime;
        shakeApplied = now < shakeUntil
            ? (Vector3)(Random.insideUnitCircle * (shakeAmount * (shakeUntil - now) / Mathf.Max(0.01f, shakeLength)))
            : Vector3.zero;
        Quaternion roll = Quaternion.identity;
        if (now < rollUntil)
        {
            float t = 1f - (rollUntil - now) / Mathf.Max(0.01f, rollLength);
            roll = Quaternion.Euler(0f, 0f, rollAmount * Mathf.Sin(t * Mathf.PI * 2.5f) * (1f - t));
        }
        float y = Mathf.SmoothDamp(transform.position.y - shakeApplied.y, TargetViewY(), ref verticalVelocity, followSmoothTime);
        transform.SetPositionAndRotation(new Vector3(x, y, -cameraDistance) + shakeApplied, roll);
    }

    // A one-floor wall stays framed as before, centred; on a taller wall (one climbing to the
    // next floor) the view follows Dudu up and down within it.
    private float TargetViewY()
    {
        int index = targetSurface != null ? System.Array.IndexOf(surfaces, targetSurface) : -1;
        if (index < 0) return 0f;
        float lift = surfaceLifts[index];
        float band = targetSurface.Height * 0.5f - orthographicSize;
        if (band <= 0f) return lift;
        float dudu = targetSurface.WorldToSurface(targetDudu.position).y + lift;
        return Mathf.Clamp(dudu, lift - band, lift + band);
    }

    private void CreateDebuffOverlay()
    {
        GameObject canvasObject = new GameObject("DuduStatusCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler));
        canvasObject.layer = UnwrappedLayer;
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cameraComponent;
        canvas.planeDistance = 0.5f;
        canvas.sortingOrder = 100;

        GameObject imageObject = new GameObject("DebuffFrameOverlay", typeof(RectTransform), typeof(Image));
        imageObject.layer = UnwrappedLayer;
        imageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        debuffOverlay = imageObject.GetComponent<Image>();
        debuffOverlay.preserveAspect = false;
        debuffOverlay.raycastTarget = false;
        debuffOverlay.enabled = false;
    }

    private void EnsureDebuffOverlay()
    {
        if (debuffOverlay == null)
        {
            Transform existing = transform.Find("DuduStatusCanvas/DebuffFrameOverlay");
            if (existing != null)
                debuffOverlay = existing.GetComponent<Image>();
            else
            {
                CreateDebuffOverlay();
                return;
            }
        }

        Canvas canvas = debuffOverlay.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas.gameObject.layer = UnwrappedLayer;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cameraComponent;
            canvas.planeDistance = 0.5f;
            canvas.sortingOrder = 100;
        }

        debuffOverlay.gameObject.layer = UnwrappedLayer;
        AspectRatioFitter fitter = debuffOverlay.GetComponent<AspectRatioFitter>();
        if (fitter != null) fitter.enabled = false;
        RectTransform rect = debuffOverlay.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one;
        debuffOverlay.type = Image.Type.Simple;
        debuffOverlay.preserveAspect = false;
        debuffOverlay.useSpriteMesh = false;
        debuffOverlay.raycastTarget = false;
    }

    private void UpdateDebuffOverlay()
    {
        EnsureDebuffOverlay();
        if (debuffOverlay == null) return;
        if (duduMovement == null && targetDudu != null)
            duduMovement = targetDudu.GetComponent<DuduSurfaceMovement>();

        Sprite[] frames = null;
        if (duduMovement != null)
            frames = duduMovement.IsControlsReversed ? reverseOverlayFrames :
                duduMovement.IsSlowed ? slowOverlayFrames : null;

        if (frames == null || frames.Length == 0)
        {
            debuffOverlay.enabled = false;
            return;
        }

        int frame = Mathf.FloorToInt(Time.unscaledTime / overlayFrameDuration) % frames.Length;
        debuffOverlay.sprite = frames[frame];
        debuffOverlay.enabled = debuffOverlay.sprite != null;
    }

    // The crayon's tip and the middle of the eraser are where the tool touches the wall,
    // as fractions of each picture.
    private static readonly Vector2 CrayonCursorTip = new Vector2(0.02f, 0.4f);
    private static readonly Vector2 EraserCursorTip = new Vector2(0.5f, 0.5f);

    private void CreateHaruCursor()
    {
        haruCursorRoot = new GameObject("Haru Cursor [2D View]").transform;
        haruCursorRoot.SetParent(proxyRoot, false);
        haruCursorRoot.gameObject.layer = UnwrappedLayer;
        haruCursorRoot.localScale = Vector3.one * haruCursorScale;
        GameObject picture = new GameObject("Picture");
        picture.layer = UnwrappedLayer;
        picture.transform.SetParent(haruCursorRoot, false);
        haruCursor = picture.AddComponent<SpriteRenderer>();
        haruCursor.sortingOrder = 200;
        haruCursor.shadowCastingMode = ShadowCastingMode.Off;
        haruCursor.receiveShadows = false;
        haruCursorRoot.gameObject.SetActive(false);
    }

    // Haru's live aim, mapped onto the unwrapped walls. Hidden (not left where it was)
    // whenever Haru can't draw: too far from the wall, aiming off it, or blocked.
    private void UpdateHaruCursor()
    {
        if (haruCursorRoot == null) return;

        bool remote = PlayerPoseSync.TryGetRemoteHaruCursor(
            out bool visible, out Vector3 point, out Vector3 normal, out DrawingTool tool);
        if (!remote)
        {
            if (localHaruDrawing == null)
                localHaruDrawing = FindAnyObjectByType<HaruDrawingController>(FindObjectsInactive.Include);
            visible = localHaruDrawing != null && localHaruDrawing.ShowsDrawCursor;
            if (visible)
            {
                point = localHaruDrawing.CurrentSurfaceAimPosition;
                normal = localHaruDrawing.CurrentSurfaceAimNormal;
                tool = localHaruDrawing.CurrentTool;
            }
        }

        Sprite sprite = tool == DrawingTool.Eraser ? haruEraserCursor : haruCrayonCursor;
        DuduSurface surface = visible && sprite != null
            ? FindSurface(point, normal) ?? FindSurface(point) : null;
        int surfaceIndex = surface != null ? System.Array.IndexOf(surfaces, surface) : -1;
        if (surfaceIndex < 0)
        {
            haruCursorShown = false;
            haruCursorRoot.gameObject.SetActive(false);
            return;
        }

        Vector3 target = ToView(surfaceIndex, surface.WorldToSurface(point), -0.3f);
        // The host sends the aim 20 times a second; ease between those so it doesn't step.
        haruCursorRoot.position = haruCursorShown && remote
            ? Vector3.Lerp(haruCursorRoot.position, target, 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime))
            : target;
        haruCursorRoot.localScale = Vector3.one * haruCursorScale;
        if (haruCursor.sprite != sprite)
        {
            haruCursor.sprite = sprite;
            Vector2 pivot = sprite.pivot / sprite.rect.size;
            Vector2 tip = tool == DrawingTool.Eraser ? EraserCursorTip : CrayonCursorTip;
            haruCursor.transform.localPosition = Vector2.Scale(pivot - tip, sprite.bounds.size);
        }
        haruCursorShown = true;
        haruCursorRoot.gameObject.SetActive(true);
    }

    public void SnapToTarget()
    {
        if (targetDudu == null) return;
        if (targetDudu.TryGetComponent(out DuduSurfaceMovement movement) && movement.CurrentSurface != null)
            targetSurface = movement.CurrentSurface;
        if (targetSurface == null) return;
        transform.SetPositionAndRotation(new Vector3(
            GetUnwrappedX(targetDudu.position, targetSurface) + horizontalOffset, TargetViewY(), -cameraDistance), Quaternion.identity);
        horizontalVelocity = 0f;
        verticalVelocity = 0f;
    }

    private float GetUnwrappedX(Vector3 worldPosition, DuduSurface surface)
    {
        int index = System.Array.IndexOf(surfaces, surface);
        return index < 0 ? 0f : surfaceCenters[index] + surface.WorldToSurface(worldPosition).x;
    }

    private void RefreshVisualProxies()
    {
        nextProxyRefresh = Time.unscaledTime + 0.25f;
        foreach (Renderer source in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            if (source == null || proxiedSources.Contains(source) || neverProxied.Contains(source)) continue;
            AddVisualProxy(source);
        }
        // Every ten seconds or so, forget renderers that have been destroyed.
        if (++refreshCount % 40 != 0) return;
        neverProxied.RemoveWhere(source => source == null);
        goneRenderers.Clear();
        foreach (Renderer source in offTheWalls.Keys)
            if (source == null) goneRenderers.Add(source);
        foreach (Renderer source in goneRenderers) offTheWalls.Remove(source);
    }

    private void AddVisualProxy(Renderer source)
    {
        if (source == null || proxyRoot == null || proxiedSources.Contains(source))
            return;

        string sourceName = source.gameObject.name;
        if (source is SkinnedMeshRenderer || source.transform.IsChildOf(proxyRoot) ||
            sourceName == "Room Wall 6 - Closure" || sourceName.StartsWith("START") ||
            sourceName.StartsWith("SECTION") || source.GetComponentInParent<RoomOnlyDecor>() != null)
        {
            neverProxied.Add(source);
            return;
        }
        // Haru's aim cross on the wall: Dudu sees Haru's crayon/eraser cursor there instead.
        // (Strokes are drawn under the same root, so only its two cross lines are left out.)
        if ((sourceName == "Horizontal" || sourceName == "Vertical") && source.transform.parent != null &&
            source.transform.parent.GetComponent<HaruSurfaceAimMarker>() != null)
        {
            neverProxied.Add(source);
            return;
        }

        // Off every wall for now (room furniture, effects waiting in their pool): kept, so the
        // next look only checks whether it has moved.
        if (!offTheWalls.TryGetValue(source, out VisualProxy item))
            item = new VisualProxy(source);
        DuduSurface sourceSurface = item.magnet != null && item.magnet.Surface != null
            ? item.magnet.Surface
            : FindSurface(item);
        if (sourceSurface == null)
        {
            offTheWalls[source] = item;
            return;
        }
        offTheWalls.Remove(source);
        Renderer proxy = CreateProxy(source);
        if (proxy == null)
        {
            neverProxied.Add(source);
            return;
        }
        item.proxy = proxy;
        item.surface = sourceSurface;
        visualProxies.Add(item);
        proxiedSources.Add(source);
    }

    // A drawn stroke knows which way its wall faces. Near a corner its points are close to both
    // walls, so only walls facing the same way are candidates for it.
    // Things on the walls face their wall too. Right at a corner a point is about as close to
    // the neighbouring wall, and drawing it there turns it edge-on to the camera (it vanishes).
    // Dudu himself is always on his current wall.
    // The parents asked about here are found once per proxy (VisualProxy), and a thing that
    // has not moved since the last look keeps the wall it had: this ran for every proxy every
    // frame and was most of Dudu's frame time once the page filled up (2026-11-23).
    private DuduSurface FindSurface(VisualProxy item)
    {
        DuduSurfaceMovement walker = item.walker;
        if (walker != null && walker.CurrentSurface != null && System.Array.IndexOf(surfaces, walker.CurrentSurface) >= 0)
            return walker.CurrentSurface;
        Vector3 position = GetReferencePosition(item.source, item.routeLine);
        Vector3 facing = item.stroke != null ? item.stroke.SurfaceNormal : item.source.transform.forward;
        if (item.placeKnown && position == item.placePosition && facing == item.placeFacing)
            return item.placeSurface;
        item.placeKnown = true;
        item.placePosition = position;
        item.placeFacing = facing;
        item.placeSurface = FindSurface(position, facing) ?? FindSurface(position, Vector3.zero);
        return item.placeSurface;
    }

    private DuduSurface FindSurface(Vector3 position) => FindSurface(position, Vector3.zero);

    private DuduSurface FindSurface(Vector3 position, Vector3 facing)
    {
        DuduSurface best = null;
        float bestScore = float.PositiveInfinity;
        Vector3 facingDirection = facing.normalized;
        for (int i = 0; i < surfaces.Length; i++)
        {
            DuduSurface surface = surfaces[i];
            if (surface == null) continue;
            if (facing != Vector3.zero && Mathf.Abs(Vector3.Dot(facingDirection, surfaceFacings[i])) < 0.9f) continue;
            Vector3 relative = position - surfacePositions[i];
            Vector2 local = new Vector2(Vector3.Dot(relative, surfaceRights[i]), Vector3.Dot(relative, surfaceUps[i]));
            float planeDistance = Mathf.Abs(Vector3.Dot(relative, surfaceNormals[i]));
            float outsideX = Mathf.Max(0f, Mathf.Abs(local.x) - surface.Width * 0.5f - 1f);
            float outsideY = Mathf.Max(0f, Mathf.Abs(local.y) - surface.Height * 0.5f - 1f);
            float score = planeDistance + outsideX + outsideY;
            if (score < bestScore && score < 1.5f) { best = surface; bestScore = score; }
        }
        return best;
    }

    // surface.WorldToSurface with the wall's axes read once.
    private Vector2 ToSurface(int index, Vector3 world)
    {
        Vector3 relative = world - surfacePositions[index];
        return new Vector2(Vector3.Dot(relative, surfaceRights[index]), Vector3.Dot(relative, surfaceUps[index]));
    }

    private Renderer CreateProxy(Renderer source)
    {
        GameObject copy = new GameObject(source.gameObject.name + " [2D View]");
        copy.layer = UnwrappedLayer;
        copy.transform.SetParent(proxyRoot);
        if (source is LineRenderer sourceLine)
        {
            LineRenderer line = copy.AddComponent<LineRenderer>();
            line.sharedMaterials = sourceLine.sharedMaterials;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.textureMode = sourceLine.textureMode;
            line.numCapVertices = sourceLine.numCapVertices;
            line.numCornerVertices = sourceLine.numCornerVertices;
            line.sortingLayerID = sourceLine.sortingLayerID;
            line.sortingOrder = Mathf.Max(sourceLine.sortingOrder, 10);
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }
        if (source is SpriteRenderer sourceSprite)
        {
            SpriteRenderer sprite = copy.AddComponent<SpriteRenderer>();
            sprite.sharedMaterials = sourceSprite.sharedMaterials;
            sprite.sortingLayerID = sourceSprite.sortingLayerID;
            sprite.sortingOrder = sourceSprite.sortingOrder;
            return sprite;
        }
        if (source.TryGetComponent(out TextMesh sourceText))
        {
            TextMesh text = copy.AddComponent<TextMesh>();
            int highResolutionSize = Mathf.Max(sourceText.fontSize, 128);
            text.font = sourceText.font; text.fontSize = highResolutionSize;
            text.characterSize = sourceText.characterSize * sourceText.fontSize / highResolutionSize;
            text.anchor = sourceText.anchor;
            text.alignment = sourceText.alignment; text.fontStyle = sourceText.fontStyle;
            text.richText = sourceText.richText;
            Renderer textRenderer = text.GetComponent<Renderer>();
            if (text.font != null) textRenderer.sharedMaterial = text.font.material;
            return textRenderer;
        }
        if (source is MeshRenderer && source.TryGetComponent(out MeshFilter sourceFilter))
        {
            copy.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            MeshRenderer mesh = copy.AddComponent<MeshRenderer>();
            mesh.sharedMaterials = source.sharedMaterials;
            mesh.shadowCastingMode = ShadowCastingMode.Off;
            mesh.receiveShadows = false;
            return mesh;
        }
        Destroy(copy);
        return null;
    }

    private void UpdateVisualProxies()
    {
        for (int i = visualProxies.Count - 1; i >= 0; i--)
        {
            VisualProxy item = visualProxies[i];
            if (item.source == null)
            {
                if (item.proxy != null) Destroy(item.proxy.gameObject);
                visualProxies.RemoveAt(i);
                proxiedSources.RemoveWhere(source => source == null);
                continue;
            }
            // Unused effect sprites cost nothing.
            if (!item.source.gameObject.activeInHierarchy)
            {
                item.proxy.enabled = false;
                continue;
            }
            LineMagnet magnet = item.magnet;
            DuduSurface liveSurface = magnet != null && magnet.Surface != null
                ? magnet.Surface
                : FindSurface(item);
            if (liveSurface != null) item.surface = liveSurface;
            int surfaceIndex = System.Array.IndexOf(surfaces, item.surface);
            if (surfaceIndex < 0) continue;
            Vector2 local = ToSurface(surfaceIndex, item.source.transform.position);
            // Keep magnets in front of wall proxies, but behind Dudu's sprite.
            float displayDepth = magnet != null ? -0.1f : item.proxy is SpriteRenderer ? -0.2f : 0f;
            item.proxy.transform.position = ToView(surfaceIndex, local, displayDepth);
            item.proxy.transform.rotation = MapRotation(item.source.transform, item.surface);
            item.proxy.transform.localScale = item.source.transform.lossyScale;
            item.proxy.enabled = item.source.enabled && item.source.gameObject.activeInHierarchy;
            item.source.GetPropertyBlock(proxyPropertyBlock);
            item.proxy.SetPropertyBlock(proxyPropertyBlock);
            if (item.source is SpriteRenderer sourceSprite && item.proxy is SpriteRenderer sprite)
            {
                sprite.sprite = sourceSprite.sprite; sprite.color = sourceSprite.color;
                sprite.flipX = sourceSprite.flipX; sprite.flipY = sourceSprite.flipY; sprite.size = sourceSprite.size;
            }
            else if (item.source is LineRenderer sourceLine && item.proxy is LineRenderer line)
            {
                line.startWidth = sourceLine.startWidth; line.endWidth = sourceLine.endWidth;
                line.startColor = sourceLine.startColor; line.endColor = sourceLine.endColor;
                // Re-laid only when the line or its wall changed: most drawn lines sit still.
                if (!LineChanged(item, sourceLine, surfaceIndex)) continue;
                // A line ends at its wall's edge: anything poking past a corner would lie over
                // the neighbouring wall's own drawing here.
                float half = item.surface.Width * 0.5f;
                float minX = surfaceCenters[surfaceIndex] + (surfaceIndex > 0 ? -half : float.NegativeInfinity);
                float maxX = surfaceCenters[surfaceIndex] + (surfaceIndex < surfaces.Length - 1 ? half : float.PositiveInfinity);
                clippedLinePoints.Clear();
                Vector3 previous = Vector3.zero;
                int pointCount = item.lineCount;
                for (int point = 0; point < pointCount; point++)
                {
                    Vector3 worldPoint = sourceLine.useWorldSpace ? item.linePoints[point] :
                        sourceLine.transform.TransformPoint(item.linePoints[point]);
                    Vector2 pointLocal = ToSurface(surfaceIndex, worldPoint);
                    Vector3 current = ToView(surfaceIndex, pointLocal, -0.1f);
                    if (point > 0) AddClippedSegment(previous, current, minX, maxX);
                    else if (current.x >= minX && current.x <= maxX) clippedLinePoints.Add(current);
                    previous = current;
                }
                if (linePointBuffer.Length < clippedLinePoints.Count)
                    linePointBuffer = new Vector3[Mathf.NextPowerOfTwo(clippedLinePoints.Count)];
                clippedLinePoints.CopyTo(linePointBuffer);
                line.positionCount = clippedLinePoints.Count;
                line.SetPositions(linePointBuffer);
            }
            else if (item.source.TryGetComponent(out TextMesh sourceText) && item.proxy.TryGetComponent(out TextMesh text))
            {
                text.text = sourceText.text; text.color = sourceText.color;
                if (text.font != sourceText.font)
                {
                    text.font = sourceText.font;
                    if (text.font != null) item.proxy.sharedMaterial = text.font.material;
                }
            }
        }
    }

    // Appends the part of segment a-b with minX <= x <= maxX (a itself is already added
    // when it lies inside).
    private void AddClippedSegment(Vector3 a, Vector3 b, float minX, float maxX)
    {
        float dx = b.x - a.x;
        float enter = 0f, exit = 1f;
        if (Mathf.Abs(dx) < 0.00001f)
        {
            if (a.x < minX || a.x > maxX) return;
        }
        else
        {
            float tMin = (minX - a.x) / dx, tMax = (maxX - a.x) / dx;
            enter = Mathf.Max(0f, Mathf.Min(tMin, tMax));
            exit = Mathf.Min(1f, Mathf.Max(tMin, tMax));
            if (enter > exit) return;
        }
        Vector3 start = Vector3.Lerp(a, b, enter);
        if (clippedLinePoints.Count == 0 || (clippedLinePoints[clippedLinePoints.Count - 1] - start).sqrMagnitude > 0.000001f)
            clippedLinePoints.Add(start);
        clippedLinePoints.Add(Vector3.Lerp(a, b, exit));
    }

    private static Vector3 GetReferencePosition(Renderer renderer, CrayonRouteLine routeLine)
    {
        // A route line's stroke can end right at a room corner; its box centre is on its own wall.
        if (routeLine != null)
            return routeLine.transform.position;
        if (renderer is LineRenderer line && line.positionCount > 0)
            return line.useWorldSpace ? line.GetPosition(0) : line.transform.TransformPoint(line.GetPosition(0));
        return renderer.transform.position;
    }

    private static Quaternion MapRotation(Transform source, DuduSurface surface)
    {
        Vector3 forward = MapDirection(source.forward, surface);
        Vector3 up = MapDirection(source.up, surface);
        return forward.sqrMagnitude < 0.001f || up.sqrMagnitude < 0.001f ? Quaternion.identity :
            Quaternion.LookRotation(forward, up);
    }

    private static Vector3 MapDirection(Vector3 direction, DuduSurface surface) => new Vector3(
        Vector3.Dot(direction, surface.Right.normalized), Vector3.Dot(direction, surface.Up.normalized),
        Vector3.Dot(direction, surface.transform.forward.normalized));

    private void OnDestroy()
    {
        if (activeInstance == this) activeInstance = null;
        if (proxyRoot != null) Destroy(proxyRoot.gameObject);
    }

    // Reads the source line's points into the proxy's copy. True when they, the wall or (for
    // a line in its own space) the line's transform differ from what was laid out last.
    private static bool LineChanged(VisualProxy item, LineRenderer sourceLine, int surfaceIndex)
    {
        int count = sourceLine.positionCount;
        if (item.scratchPoints == null || item.scratchPoints.Length < count)
            item.scratchPoints = new Vector3[Mathf.NextPowerOfTwo(Mathf.Max(count, 16))];
        if (count > 0) sourceLine.GetPositions(item.scratchPoints);
        Matrix4x4 space = sourceLine.useWorldSpace ? Matrix4x4.identity : sourceLine.transform.localToWorldMatrix;
        bool changed = item.linePoints == null || count != item.lineCount || surfaceIndex != item.lineSurfaceIndex ||
            space != item.lineSpace;
        for (int i = 0; !changed && i < count; i++)
            changed = item.scratchPoints[i] != item.linePoints[i];
        if (!changed) return false;
        Vector3[] previous = item.linePoints;
        item.linePoints = item.scratchPoints;
        item.scratchPoints = previous;
        item.lineCount = count;
        item.lineSurfaceIndex = surfaceIndex;
        item.lineSpace = space;
        return true;
    }

    private sealed class VisualProxy
    {
        public readonly Renderer source;
        public Renderer proxy;
        public DuduSurface surface;
        // Its parents, which do not change: asked once instead of every frame.
        public readonly LineMagnet magnet;
        public readonly DuduSurfaceMovement walker;
        public readonly DrawingStroke stroke;
        public readonly CrayonRouteLine routeLine;
        // The wall last found for it, and from where.
        public bool placeKnown;
        public Vector3 placePosition, placeFacing;
        public DuduSurface placeSurface;
        // A line's points as last laid out in this view.
        public Vector3[] linePoints, scratchPoints;
        public int lineCount, lineSurfaceIndex = -1;
        public Matrix4x4 lineSpace;

        public VisualProxy(Renderer source)
        {
            this.source = source;
            magnet = source.GetComponentInParent<LineMagnet>();
            walker = source.GetComponentInParent<DuduSurfaceMovement>();
            stroke = source.GetComponentInParent<DrawingStroke>();
            routeLine = source.GetComponentInParent<CrayonRouteLine>();
        }
    }
}
