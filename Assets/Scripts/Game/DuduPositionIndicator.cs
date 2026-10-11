using UnityEngine;
using UnityEngine.UI;

// Runs after camera look and remote pose updates so the marker follows the current view.
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class DuduPositionIndicator : MonoBehaviour
{
    [SerializeField, Min(16f)] private float iconWidth = 104f;
    [SerializeField, Min(0f)] private float edgePadding = 24f;

    private GameViewManager view;
    private Camera haruCamera;
    private Transform dudu;
    private SpriteRenderer duduRenderer;
    private Canvas canvas;
    private RectTransform canvasRect;
    private RectTransform marker;

    public void Configure(GameViewManager owner, Camera camera, DuduSurfaceMovement target)
    {
        view = owner;
        haruCamera = camera;
        dudu = target != null ? target.transform : null;
        duduRenderer = target != null ? target.GetComponentInChildren<SpriteRenderer>(true) : null;
        if (canvas == null) BuildUi();
    }

    private void BuildUi()
    {
        Sprite sprite = Resources.Load<Sprite>("UI/DuduPosition");
        if (sprite == null)
        {
            Debug.LogError("Dudu position indicator needs Resources/UI/DuduPosition.png.", this);
            return;
        }

        GameObject canvasObject = new GameObject("Dudu Position Canvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        canvas.enabled = false;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = canvasObject.GetComponent<RectTransform>();

        GameObject markerObject = new GameObject("Dudu Offscreen Marker",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        markerObject.transform.SetParent(canvasObject.transform, false);
        marker = markerObject.GetComponent<RectTransform>();
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        marker.pivot = new Vector2(0.5f, 0.5f);
        marker.sizeDelta = new Vector2(iconWidth, iconWidth * sprite.rect.height / sprite.rect.width);
        Image image = markerObject.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private void LateUpdate()
    {
        if (canvas == null) return;
        if (view == null || !view.IsHaruGameplayView || SettingsPopupController.IsAnyOpen ||
            haruCamera == null || !haruCamera.isActiveAndEnabled ||
            dudu == null || !dudu.gameObject.activeInHierarchy)
        {
            canvas.enabled = false;
            return;
        }

        Vector3 targetPosition = duduRenderer != null ? duduRenderer.bounds.center : dudu.position;
        Vector3 viewport = haruCamera.WorldToViewportPoint(targetPosition);
        bool onScreen = viewport.z >= haruCamera.nearClipPlane &&
            viewport.z <= haruCamera.farClipPlane &&
            viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
        if (onScreen)
        {
            canvas.enabled = false;
            return;
        }

        // Camera-space x/y retain the correct bearing even for targets behind the camera.
        // Avoid projecting through z=0, where perspective coordinates can become infinite.
        Vector3 localTarget = haruCamera.transform.InverseTransformPoint(targetPosition);
        Vector2 direction = new Vector2(localTarget.x, localTarget.y);
        if (direction.sqrMagnitude < 0.000001f) direction = Vector2.right;
        direction.Normalize();

        Vector2 halfCanvas = canvasRect.rect.size * 0.5f;
        Vector2 halfMarker = marker.sizeDelta * 0.5f;
        Vector2 limits = new Vector2(
            Mathf.Max(0f, halfCanvas.x - halfMarker.x - edgePadding),
            Mathf.Max(0f, halfCanvas.y - halfMarker.y - edgePadding));
        float horizontalDistance = Mathf.Abs(direction.x) > 0.00001f
            ? limits.x / Mathf.Abs(direction.x) : float.PositiveInfinity;
        float verticalDistance = Mathf.Abs(direction.y) > 0.00001f
            ? limits.y / Mathf.Abs(direction.y) : float.PositiveInfinity;
        marker.anchoredPosition = direction * Mathf.Min(horizontalDistance, verticalDistance);

        // Keep the face upright; point the supplied bubble's tail toward the nearest side.
        marker.localScale = new Vector3(direction.x > 0f ? -1f : 1f, 1f, 1f);
        canvas.enabled = true;
    }

    private void OnDisable()
    {
        if (canvas != null) canvas.enabled = false;
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }
}
