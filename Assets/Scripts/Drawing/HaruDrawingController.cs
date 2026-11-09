using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public enum DrawingTool
{
    Pen,
    Eraser
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class HaruDrawingController : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float maxDrawDistance = 6f;
    [SerializeField, Min(1f)] private float aimRayDistance = 100f;
    [SerializeField, Min(0.001f)] private float minPointDistance = 0.02f;
    [SerializeField, Min(0.001f)] private float lineWidth = 0.03f;
    [SerializeField, Min(0f)] private float surfaceOffset = 0.01f;
    [SerializeField, Min(0.001f)] private float eraserRadius = 0.08f;
    [SerializeField, Min(0.001f)] private float colliderThickness = 0.06f;
    [SerializeField, Min(0.001f)] private float colliderDepth = 0.12f;
    [SerializeField] private Color penColor = new Color(22f / 255f, 127f / 255f, 195f / 255f, 1f);
    [SerializeField] private Material lineMaterialTemplate;
    [SerializeField] private LayerMask drawingSurfaceMask;
    [SerializeField] private LayerMask drawingBlockerMask;
    [SerializeField] private CrosshairController crosshair;
    [SerializeField] private HaruSurfaceAimMarker surfaceAimMarker;
    [SerializeField] private Player3DMovement haruMovement;
    [SerializeField] private Player3DLook haruLook;
    [SerializeField] private string haruLayerName = "Haru";
    [SerializeField] private string duduLayerName = "Dudu";
    [SerializeField] private string strokeLayerName = "DrawnStroke";

    [Header("Crayon Usage")]
    [SerializeField, Min(1f)] private float maxCrayon = 100f;
    [SerializeField, Min(0.01f)] private float consumptionPerWorldUnit = 10f;

    private Camera drawingCamera;
    private DrawingStroke currentStroke;
    private DrawingSurface currentSurface;
    private Material lineMaterial;
    private Vector3 lastPoint;
    private int strokeNumber;
    private DrawingTool currentTool = DrawingTool.Pen;
    private bool cameraLock;
    private bool pressStartedOnUi;
    private int strokeLayer;
    private float currentCrayon;
    private HaruDrawFeedback feedback;
    private float nextRefusal;

    public DrawingTool CurrentTool => currentTool;
    public System.Func<DrawingSurface, Vector3, bool> PenPointAllowed { get; set; }
    public event System.Action<int> StrokeErased;
    public bool CameraLock => cameraLock;
    public bool HasValidSurfaceAim { get; private set; }
    public Vector3 CurrentSurfaceAimPosition { get; private set; }
    public bool CanDrawAtCurrentAim { get; private set; }
    public Vector3 CurrentSurfaceAimNormal { get; private set; }
    // Where Haru's crayon or eraser would touch the wall right now; false while it can't draw.
    // Dudu's 2D view shows Haru's cursor only while this is true.
    public bool ShowsDrawCursor => isActiveAndEnabled && HasValidSurfaceAim && CanDrawAtCurrentAim;
    public bool IsDrawing => isActiveAndEnabled && currentStroke != null && CanDrawAtCurrentAim;
    // Crayon is unlimited. Keep these public values full as well so the arm,
    // gauges and any other external consumers never display depletion.
    public float CrayonNormalized => 1f;
    public bool CanUseCrayon => true;

    private void Awake()
    {
        drawingCamera = GetComponent<Camera>();
        if (GetComponent<HaruDrawingArm>() == null) gameObject.AddComponent<HaruDrawingArm>();
        feedback = GetComponent<HaruDrawFeedback>();
        if (feedback == null) feedback = gameObject.AddComponent<HaruDrawFeedback>();
        currentCrayon = maxCrayon;
        ConfigurePhysicsLayers();
        lineMaterial = CreateLineMaterial(lineMaterialTemplate);
        ApplyPenColor();
        SetTool(DrawingTool.Pen);
    }

    private void Update()
    {
        if (SettingsPopupController.IsAnyOpen)
        {
            EndStroke();
            ClearSurfaceAim();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (CoopInputSettings.WasPressed(keyboard, CoopInputAction.HaruToolSwitch))
            SetTool(currentTool == DrawingTool.Pen ? DrawingTool.Eraser : DrawingTool.Pen);
        if (CoopInputSettings.WasPressed(keyboard, CoopInputAction.HaruCameraLock))
            SetCameraLock(!cameraLock);

        Mouse mouse = Mouse.current;
        bool expectedCursorState = cameraLock
            ? Cursor.lockState != CursorLockMode.Locked
            : Cursor.lockState == CursorLockMode.Locked;
        if (mouse == null || !expectedCursorState)
        {
            EndStroke();
            ClearSurfaceAim();
            return;
        }

        bool hasSurfaceAim = TryGetCurrentSurfaceAim(
            out DrawingSurface surface,
            out Vector3 hitPoint,
            out Vector3 hitNormal,
            out float aimDistance);
        CanDrawAtCurrentAim =
            hasSurfaceAim &&
            aimDistance <= maxDrawDistance &&
            !IsDrawingBlocked(aimDistance) &&
            !NoDrawZone.Blocks(surface, hitPoint) &&
            (currentTool != DrawingTool.Pen || PenPointAllowed == null || PenPointAllowed(surface, hitPoint));
        bool refusedHere = hasSurfaceAim && NoDrawZone.Blocks(surface, hitPoint);
        if (crosshair != null)
        {
            crosshair.SetDrawAvailability(CanDrawAtCurrentAim);
            crosshair.SetRefused(refusedHere);
        }
        if (hasSurfaceAim)
            SetSurfaceAim(surface, hitPoint, hitNormal, refusedHere);
        else
            ClearSurfaceAim();

        if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
        {
            pressStartedOnUi = false;
            EndStroke();
            return;
        }

        // With the cursor free (camera lock), a click on a screen button is not a stroke.
        if (mouse.leftButton.wasPressedThisFrame)
            pressStartedOnUi = cameraLock && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (pressStartedOnUi)
        {
            EndStroke();
            return;
        }

        if (!CanDrawAtCurrentAim)
        {
            EndStroke();
            if (refusedHere && Time.time >= nextRefusal)
            {
                // Held down it keeps trying, but not every frame.
                nextRefusal = Time.time + 0.28f;
                feedback.DrawRefused(hitPoint, hitNormal);
                NoDrawZone.Refused(surface, hitPoint);
                if (crosshair != null) crosshair.Punch();
            }
            return;
        }

        Vector3 point = hitPoint + hitNormal * surfaceOffset;

        if (currentTool == DrawingTool.Eraser)
        {
            EndStroke();
            EraseStrokeParts(surface, point);
        }
        else if (!CanUseCrayon)
            EndStroke();
        else if (mouse.leftButton.wasPressedThisFrame || currentStroke == null || currentSurface != surface)
            StartStroke(surface, point);
        else if (Vector3.Distance(lastPoint, point) >= minPointDistance)
        {
            float distance = Vector3.Distance(lastPoint, point);
            float drawableDistance = distance;
            if (drawableDistance > 0f)
            {
                Vector3 consumedPoint = Vector3.Lerp(lastPoint, point, drawableDistance / distance);
                currentStroke.AddPoint(consumedPoint);
                feedback.StrokeMoved(lastPoint, consumedPoint);
                lastPoint = consumedPoint;
                ConsumeCrayon(drawableDistance * consumptionPerWorldUnit);
            }
            if (!CanUseCrayon) EndStroke();
        }
    }

    private void ConsumeCrayon(float amount)
    {
        currentCrayon = maxCrayon;
    }

    public bool TryGetCurrentSurfaceAim(
        out DrawingSurface surface,
        out Vector3 hitPoint,
        out Vector3 hitNormal,
        out float aimDistance)
    {
        if (drawingCamera == null || (cameraLock && Mouse.current == null))
        {
            surface = null;
            hitPoint = default;
            hitNormal = default;
            aimDistance = 0f;
            return false;
        }

        Ray ray = cameraLock
            ? drawingCamera.ScreenPointToRay(Mouse.current.position.ReadValue())
            : drawingCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, aimRayDistance, drawingSurfaceMask, QueryTriggerInteraction.Ignore))
        {
            surface = hit.collider.GetComponentInParent<DrawingSurface>();
            if (surface != null)
            {
                hitPoint = hit.point;
                hitNormal = hit.normal;
                aimDistance = hit.distance;
                return true;
            }
        }

        surface = null;
        hitPoint = default;
        hitNormal = default;
        aimDistance = 0f;
        return false;
    }

    private void SetSurfaceAim(DrawingSurface surface, Vector3 hitPoint, Vector3 hitNormal, bool refused)
    {
        HasValidSurfaceAim = true;
        CurrentSurfaceAimPosition = hitPoint;
        CurrentSurfaceAimNormal = hitNormal;
        if (surfaceAimMarker != null)
            surfaceAimMarker.SetAim(
                hitPoint,
                hitNormal,
                surface.transform.right,
                surface.transform.up,
                currentTool,
                refused);
    }

    private bool IsDrawingBlocked(float surfaceDistance)
    {
        if (drawingBlockerMask.value == 0)
            return false;

        Ray ray = cameraLock
            ? drawingCamera.ScreenPointToRay(Mouse.current.position.ReadValue())
            : drawingCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        return Physics.Raycast(
            ray,
            surfaceDistance,
            drawingBlockerMask,
            QueryTriggerInteraction.Ignore);
    }

    private void ClearSurfaceAim()
    {
        HasValidSurfaceAim = false;
        CanDrawAtCurrentAim = false;
        if (crosshair != null)
        {
            crosshair.SetDrawAvailability(false);
            crosshair.SetRefused(false);
        }
        if (surfaceAimMarker != null)
            surfaceAimMarker.ClearAim();
    }

    private void StartStroke(DrawingSurface surface, Vector3 point)
    {
        EndStroke();
        currentSurface = surface;
        strokeNumber++;

        GameObject strokeObject = new GameObject($"Stroke_{strokeNumber:000}");
        if (surface.StrokeRoot != null)
            strokeObject.transform.SetParent(surface.StrokeRoot, true);

        strokeObject.AddComponent<LineRenderer>();
        currentStroke = strokeObject.AddComponent<DrawingStroke>();
        // Keep the LineRenderer vertex tint white so the material's exact pen color
        // is not multiplied by the same color a second time.
        currentStroke.Initialize(
            lineMaterial,
            lineWidth,
            Color.white,
            point,
            colliderThickness,
            colliderDepth,
            surface.Normal,
            strokeLayer);
        lastPoint = point;
        feedback.StrokeStarted(point, surface.Normal, penColor);
    }

    private void EraseStrokeParts(DrawingSurface surface, Vector3 point)
    {
        Transform root = surface.StrokeRoot;
        if (root == null)
            return;

        DrawingStroke[] strokes = root.GetComponentsInChildren<DrawingStroke>(false);
        bool removed = false;
        foreach (DrawingStroke stroke in strokes)
        {
            if (stroke != null && stroke.TryGetSqrDistance(point, eraserRadius, out _) &&
                stroke.Erase(point, eraserRadius))
            {
                removed = true;
                StrokeErased?.Invoke(stroke.SourceStrokeId);
            }
        }
        feedback.Erasing(point, surface.Normal, removed, penColor);
    }

    private void SetTool(DrawingTool tool)
    {
        currentTool = tool;
        EndStroke();
        if (crosshair != null)
            crosshair.SetEraserMode(tool == DrawingTool.Eraser);
        HaruHeldTool held = FindHeldTool();
        if (held != null) held.Tool = tool;
    }

    // The world Haru's hand (also seen by Dudu); the first-person arm follows CurrentTool itself.
    private HaruHeldTool FindHeldTool()
    {
        Player3DMovement player = haruMovement != null ? haruMovement : GetComponentInParent<Player3DMovement>();
        Transform visual = player != null ? player.transform.Find("Haru Visual") : null;
        return visual != null ? visual.GetComponent<HaruHeldTool>() : null;
    }

    public void ForceDisableCameraLock()
    {
        if (cameraLock)
            SetCameraLock(false);
    }

    private void SetCameraLock(bool locked)
    {
        cameraLock = locked;
        EndStroke();

        if (haruMovement != null)
            haruMovement.enabled = !locked;
        if (haruLook != null)
            haruLook.enabled = !locked;

        Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = locked;
        if (crosshair != null)
            crosshair.SetCameraLock(locked);
    }

    private void ApplyPenColor()
    {
        if (lineMaterial == null)
            return;

        lineMaterial.color = penColor;
        if (lineMaterial.HasProperty("_BaseColor"))
            lineMaterial.SetColor("_BaseColor", penColor);
        if (lineMaterial.HasProperty("_Color"))
            lineMaterial.SetColor("_Color", penColor);
    }

    private void ConfigurePhysicsLayers()
    {
        int haruLayer = LayerMask.NameToLayer(haruLayerName);
        int duduLayer = LayerMask.NameToLayer(duduLayerName);
        strokeLayer = LayerMask.NameToLayer(strokeLayerName);

        if (haruLayer < 0 || duduLayer < 0 || strokeLayer < 0)
        {
            Debug.LogError("Drawing physics layers are missing. Expected Haru, Dudu, and DrawnStroke.", this);
            strokeLayer = gameObject.layer;
            return;
        }

        Physics.IgnoreLayerCollision(haruLayer, strokeLayer, true);
        Physics.IgnoreLayerCollision(duduLayer, strokeLayer, false);
    }

    private void EndStroke()
    {
        if (currentStroke != null)
        {
            if (currentStroke.PointCount < 2)
                Destroy(currentStroke.gameObject);
            else
                currentStroke.FinishDrawing();
            if (feedback != null) feedback.StrokeEnded();
        }
        currentStroke = null;
        currentSurface = null;
    }

    private void OnDisable()
    {
        EndStroke();
        ClearSurfaceAim();
        ForceDisableCameraLock();
    }

    private void OnDestroy()
    {
        if (lineMaterial != null)
            Destroy(lineMaterial);
    }

    private void OnValidate()
    {
        maxCrayon = Mathf.Max(1f, maxCrayon);
        consumptionPerWorldUnit = Mathf.Max(0.01f, consumptionPerWorldUnit);
    }

    public Material CreateStrokeMaterialCopy()
    {
        Material copy = CreateLineMaterial(lineMaterialTemplate);
        copy.color = penColor;
        return copy;
    }

    private static Material CreateLineMaterial(Material template)
    {
        if (template != null)
        {
            Material instance = new Material(template)
            {
                name = "Runtime Crayon Line Material"
            };
            return instance;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader)
        {
            name = "Runtime Drawing Line Material",
            color = new Color(22f / 255f, 127f / 255f, 195f / 255f, 1f)
        };
        return material;
    }
}
