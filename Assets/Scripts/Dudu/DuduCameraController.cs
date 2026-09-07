using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public sealed class DuduCameraController : MonoBehaviour
{
    private const int UnwrappedLayer = 30;
    [SerializeField] private DuduSurface targetSurface;
    [SerializeField] private Transform targetDudu;
    [SerializeField, Min(0.1f)] private float cameraDistance = 10f;
    [SerializeField, Min(0.1f)] private float orthographicSize = 3.5f;
    [SerializeField, Min(0.01f)] private float followSmoothTime = 0.15f;
    [SerializeField] private float horizontalOffset = 1f;

    private readonly List<VisualProxy> visualProxies = new List<VisualProxy>();
    private Camera cameraComponent;
    private Transform proxyRoot;
    private DuduSurface[] surfaces;
    private float[] surfaceCenters;
    private float totalWidth;
    private float horizontalVelocity;
    private float nextProxyRefresh;

    public void SetSurface(DuduSurface surface) => targetSurface = surface;
    public void SetTarget(Transform target) => targetDudu = target;

    private void Awake()
    {
        cameraComponent = GetComponent<Camera>();
        cameraComponent.orthographic = true;
        cameraComponent.orthographicSize = orthographicSize;
    }

    private void OnEnable()
    {
        if (cameraComponent == null) cameraComponent = GetComponent<Camera>();
        ConfigureCameraMasks();
    }

    private void Start()
    {
        CornerRoomLevel level = FindAnyObjectByType<CornerRoomLevel>();
        surfaces = level != null ? level.surfaces : null;
        if (surfaces == null || surfaces.Length == 0) return;

        surfaceCenters = new float[surfaces.Length];
        float seamOverlap = targetDudu != null && targetDudu.TryGetComponent(out DuduSurfaceMovement movement)
            ? movement.VisualSeamOverlap
            : 0f;
        for (int i = 0; i < surfaces.Length; i++)
        {
            surfaceCenters[i] = totalWidth + surfaces[i].Width * 0.5f;
            totalWidth += surfaces[i].Width;
            if (i < surfaces.Length - 1) totalWidth -= seamOverlap;
        }
        for (int i = 0; i < surfaceCenters.Length; i++)
            surfaceCenters[i] -= totalWidth * 0.5f;

        proxyRoot = new GameObject("Dudu Unwrapped View").transform;
        proxyRoot.gameObject.layer = UnwrappedLayer;
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
        if (surfaces == null || targetDudu == null) return;
        if (targetDudu.TryGetComponent(out DuduSurfaceMovement movement) && movement.CurrentSurface != null)
            targetSurface = movement.CurrentSurface;
        if (Time.unscaledTime >= nextProxyRefresh) RefreshVisualProxies();
        UpdateVisualProxies();

        float targetX = GetUnwrappedX(targetDudu.position, targetSurface) + horizontalOffset;
        float viewHalfWidth = orthographicSize * cameraComponent.aspect;
        float limit = Mathf.Max(0f, totalWidth * 0.5f - viewHalfWidth);
        targetX = Mathf.Clamp(targetX, -limit, limit);
        float x = Mathf.SmoothDamp(transform.position.x, targetX, ref horizontalVelocity, followSmoothTime);
        transform.SetPositionAndRotation(new Vector3(x, 0f, -cameraDistance), Quaternion.identity);
    }

    private void SnapToTarget()
    {
        if (targetDudu == null || targetSurface == null) return;
        transform.SetPositionAndRotation(new Vector3(
            GetUnwrappedX(targetDudu.position, targetSurface) + horizontalOffset, 0f, -cameraDistance), Quaternion.identity);
        horizontalVelocity = 0f;
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
            string sourceName = source.gameObject.name;
            if (source.transform.IsChildOf(proxyRoot) || visualProxies.Exists(item => item.source == source) ||
                sourceName == "Room Wall 6 - Closure" || sourceName.StartsWith("START") ||
                sourceName.StartsWith("SECTION")) continue;
            Vector3 referencePosition = GetReferencePosition(source);
            DuduSurface surface = FindSurface(referencePosition);
            if (surface == null) continue;
            Renderer proxy = CreateProxy(source);
            if (proxy != null) visualProxies.Add(new VisualProxy(source, proxy, surface));
        }
    }

    private DuduSurface FindSurface(Vector3 position)
    {
        DuduSurface best = null;
        float bestScore = float.PositiveInfinity;
        foreach (DuduSurface surface in surfaces)
        {
            if (surface == null) continue;
            Vector2 local = surface.WorldToSurface(position);
            float planeDistance = Mathf.Abs(Vector3.Dot(position - surface.transform.position, surface.Normal));
            float outsideX = Mathf.Max(0f, Mathf.Abs(local.x) - surface.Width * 0.5f - 1f);
            float outsideY = Mathf.Max(0f, Mathf.Abs(local.y) - surface.Height * 0.5f - 1f);
            float score = planeDistance + outsideX + outsideY;
            if (score < bestScore && score < 1.5f) { best = surface; bestScore = score; }
        }
        return best;
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
            text.font = sourceText.font; text.fontSize = sourceText.fontSize;
            text.characterSize = sourceText.characterSize; text.anchor = sourceText.anchor;
            text.alignment = sourceText.alignment; text.fontStyle = sourceText.fontStyle;
            text.richText = sourceText.richText;
            return text.GetComponent<Renderer>();
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
                continue;
            }
            Vector3 referencePosition = GetReferencePosition(item.source);
            DuduSurface liveSurface = FindSurface(referencePosition);
            if (liveSurface != null) item.surface = liveSurface;
            int surfaceIndex = System.Array.IndexOf(surfaces, item.surface);
            if (surfaceIndex < 0) continue;
            Vector2 local = item.surface.WorldToSurface(item.source.transform.position);
            float displayDepth = item.proxy is SpriteRenderer ? -0.2f : 0f;
            item.proxy.transform.position = new Vector3(surfaceCenters[surfaceIndex] + local.x, local.y, displayDepth);
            item.proxy.transform.rotation = MapRotation(item.source.transform, item.surface);
            item.proxy.transform.localScale = item.source.transform.lossyScale;
            item.proxy.enabled = item.source.enabled && item.source.gameObject.activeInHierarchy;
            if (item.source is SpriteRenderer sourceSprite && item.proxy is SpriteRenderer sprite)
            {
                sprite.sprite = sourceSprite.sprite; sprite.color = sourceSprite.color;
                sprite.flipX = sourceSprite.flipX; sprite.flipY = sourceSprite.flipY; sprite.size = sourceSprite.size;
            }
            else if (item.source is LineRenderer sourceLine && item.proxy is LineRenderer line)
            {
                line.positionCount = sourceLine.positionCount;
                line.startWidth = sourceLine.startWidth; line.endWidth = sourceLine.endWidth;
                line.startColor = sourceLine.startColor; line.endColor = sourceLine.endColor;
                for (int point = 0; point < sourceLine.positionCount; point++)
                {
                    Vector3 worldPoint = sourceLine.useWorldSpace ? sourceLine.GetPosition(point) :
                        sourceLine.transform.TransformPoint(sourceLine.GetPosition(point));
                    Vector2 pointLocal = item.surface.WorldToSurface(worldPoint);
                    line.SetPosition(point, new Vector3(surfaceCenters[surfaceIndex] + pointLocal.x, pointLocal.y, -0.1f));
                }
            }
            else if (item.source.TryGetComponent(out TextMesh sourceText) && item.proxy.TryGetComponent(out TextMesh text))
            { text.text = sourceText.text; text.color = sourceText.color; }
        }
    }

    private static Vector3 GetReferencePosition(Renderer renderer)
    {
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

    private void OnDestroy() { if (proxyRoot != null) Destroy(proxyRoot.gameObject); }

    private sealed class VisualProxy
    {
        public readonly Renderer source;
        public readonly Renderer proxy;
        public DuduSurface surface;
        public VisualProxy(Renderer source, Renderer proxy, DuduSurface surface)
        { this.source = source; this.proxy = proxy; this.surface = surface; }
    }
}
