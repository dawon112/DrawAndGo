using UnityEngine;

[DisallowMultipleComponent]
public sealed class HaruSurfaceAimMarker : MonoBehaviour
{
    [SerializeField, Min(0.001f)] private float markerSurfaceOffset = 0.05f;
    [SerializeField, Min(0.01f)] private float markerSize = 0.24f;
    [SerializeField, Min(0.001f)] private float markerLineWidth = 0.035f;
    [SerializeField] private Color penMarkerColor = new Color(1f, 0.25f, 0.05f, 1f);
    [SerializeField] private Color eraserMarkerColor = new Color(1f, 0.1f, 0.65f, 1f);
    [SerializeField] private string markerLayerName = "RemoteAim";

    private LineRenderer horizontalLine;
    private LineRenderer verticalLine;
    private Material penMaterial;
    private Material eraserMaterial;

    public bool HasValidAim { get; private set; }
    public Vector3 SurfaceAimPosition { get; private set; }

    private void Awake()
    {
        int markerLayer = LayerMask.NameToLayer(markerLayerName);
        if (markerLayer >= 0)
            gameObject.layer = markerLayer;

        penMaterial = CreateMaterial("Remote Aim Pen", penMarkerColor);
        eraserMaterial = CreateMaterial("Remote Aim Eraser", eraserMarkerColor);
        horizontalLine = CreateLine("Horizontal", markerLayer);
        verticalLine = CreateLine("Vertical", markerLayer);
        SetVisible(false);
    }

    public void SetAim(Vector3 hitPoint, Vector3 hitNormal, Vector3 surfaceRight, Vector3 surfaceUp, DrawingTool tool)
    {
        HasValidAim = true;
        SurfaceAimPosition = hitPoint;
        Vector3 markerPosition = hitPoint + hitNormal.normalized * markerSurfaceOffset;
        float halfSize = markerSize * 0.5f;
        SetLine(horizontalLine, markerPosition - surfaceRight.normalized * halfSize, markerPosition + surfaceRight.normalized * halfSize);
        SetLine(verticalLine, markerPosition - surfaceUp.normalized * halfSize, markerPosition + surfaceUp.normalized * halfSize);

        Material material = tool == DrawingTool.Eraser ? eraserMaterial : penMaterial;
        horizontalLine.sharedMaterial = material;
        verticalLine.sharedMaterial = material;
        SetVisible(true);
    }

    public void ClearAim()
    {
        HasValidAim = false;
        SetVisible(false);
    }

    private LineRenderer CreateLine(string objectName, int markerLayer)
    {
        GameObject lineObject = new GameObject(objectName);
        lineObject.transform.SetParent(transform, false);
        if (markerLayer >= 0)
            lineObject.layer = markerLayer;

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.positionCount = 2;
        line.startWidth = markerLineWidth;
        line.endWidth = markerLineWidth;
        line.numCapVertices = 4;
        line.sortingLayerName = "Default";
        line.sortingOrder = 1000;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private static void SetLine(LineRenderer line, Vector3 start, Vector3 end)
    {
        line.SetPosition(0, start);
        line.SetPosition(1, end);
    }

    private void SetVisible(bool visible)
    {
        if (horizontalLine != null)
            horizontalLine.enabled = visible;
        if (verticalLine != null)
            verticalLine.enabled = visible;
    }

    private static Material CreateMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        Material material = new Material(shader) { name = materialName, color = color };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        return material;
    }

    private void OnDestroy()
    {
        if (penMaterial != null)
            Destroy(penMaterial);
        if (eraserMaterial != null)
            Destroy(eraserMaterial);
    }
}
