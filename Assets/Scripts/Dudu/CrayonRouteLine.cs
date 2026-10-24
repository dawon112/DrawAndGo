using UnityEngine;
using UnityEngine.Rendering;

// A ground line for Dudu drawn as a black crayon stroke on the wall.
// The object's BoxCollider is the floor Dudu walks on; the stroke is only its look.
// To place one, move or stretch the object (scale X = length) like any box: the
// stroke follows, and in the editor the line snaps flat onto the nearest Dudu wall.
// Tools > Draw And Go > Crayon Route Lines creates and converts them.
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
public sealed class CrayonRouteLine : MonoBehaviour
{
    private const float SurfaceGap = 0.012f;
    private const float PointSpacing = 0.2f;
    private static readonly int SeedId = Shader.PropertyToID("_Seed");

    [SerializeField] private Material material;
    [Tooltip("Drawn width as a multiple of the box height, so Dudu's feet sit on the visible line.")]
    [SerializeField, Min(0.1f)] private float widthScale = 1.25f;
    [Tooltip("How far the hand-drawn line drifts up and down, in metres.")]
    [SerializeField, Range(0f, 0.05f)] private float wobble = 0.012f;
    [SerializeField] private int seed;
    [Tooltip("In the editor, keep the line on the nearest Dudu wall while it is moved.")]
    [SerializeField] private bool snapToWall = true;
    [SerializeField, HideInInspector] private LineRenderer stroke;

    private bool dirty = true;

    public Material Material
    {
        get => material;
        set { material = value; dirty = true; }
    }

    // In play mode: turns a box made with CreatePrimitive into a crayon line.
    public static CrayonRouteLine AddTo(GameObject box, Material material)
    {
        if (box.TryGetComponent(out MeshRenderer mesh)) { mesh.enabled = false; Destroy(mesh); }
        if (box.TryGetComponent(out MeshFilter filter)) Destroy(filter);
        CrayonRouteLine line = box.AddComponent<CrayonRouteLine>();
        line.Material = material;
        line.Rebuild();
        return line;
    }

    private void OnEnable()
    {
        dirty = true;
        // From the position, so both players see the same wobble on lines built in play.
        if (seed == 0) seed = (transform.position.GetHashCode() & int.MaxValue) | 1;
        Rebuild();
    }

    private void OnValidate() => dirty = true;

    private void Update()
    {
        if (dirty || transform.hasChanged) Rebuild();
    }

    public void Rebuild()
    {
        dirty = false;
        transform.hasChanged = false;
        DuduSurface surface = FindWall();
        if (!Application.isPlaying && snapToWall && surface != null) SnapTo(surface);

        // Keep the old box mesh out of sight; the collider stays as Dudu's floor.
        if (TryGetComponent(out MeshRenderer box)) box.enabled = false;

        EnsureStroke();
        stroke.sharedMaterial = material;
        // Each line gets its own ragged edge (Dudu's 2D view copies this block too).
        var block = new MaterialPropertyBlock();
        stroke.GetPropertyBlock(block);
        block.SetFloat(SeedId, seed % 997 * 0.731f);
        stroke.SetPropertyBlock(block);
        Vector3 scale = transform.lossyScale;
        float length = Mathf.Abs(scale.x);
        float width = Mathf.Abs(scale.y) * widthScale;
        stroke.startWidth = width;
        stroke.endWidth = width;

        Vector3 right = transform.right;
        Vector3 up = transform.up;
        Vector3 centre = transform.position;
        Vector3 normal = -transform.forward;
        if (surface != null)
        {
            // Lie on the wall's visible face; Dudu's plane can sit inside a thick wall.
            normal = surface.Normal;
            centre -= normal * (Vector3.Dot(centre - surface.transform.position, normal) - VisibleFace(surface));
        }
        centre += normal * SurfaceGap;
        stroke.transform.rotation = Quaternion.LookRotation(-normal, up);
        // The stroke's own transform scales its width, so undo the box's size.
        stroke.transform.localScale = new Vector3(1f / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(scale.y)), 1f / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));

        // A few slow drifts, the same every time for this line.
        int count = Mathf.Max(2, Mathf.CeilToInt(length / PointSpacing) + 1);
        float phase = (seed % 1000) * 0.137f;
        stroke.positionCount = count;
        for (int i = 0; i < count; i++)
        {
            float t = i / (count - 1f);
            float along = (t - 0.5f) * length;
            float drift = (Mathf.PerlinNoise(phase + along * 0.45f, phase * 0.5f) - 0.5f) * 2f * wobble;
            stroke.SetPosition(i, centre + right * along + up * drift);
        }
    }

    private void EnsureStroke()
    {
        if (stroke != null) return;
        var child = new GameObject("Crayon Stroke");
        child.transform.SetParent(transform, false);
        child.layer = gameObject.layer;
        stroke = child.AddComponent<LineRenderer>();
        stroke.useWorldSpace = true;
        stroke.alignment = LineAlignment.TransformZ;
        stroke.textureMode = LineTextureMode.Tile;
        stroke.numCapVertices = 4;
        stroke.numCornerVertices = 2;
        stroke.shadowCastingMode = ShadowCastingMode.Off;
        stroke.receiveShadows = false;
        stroke.startColor = Color.white;
        stroke.endColor = Color.white;
    }

    private void SnapTo(DuduSurface surface)
    {
        Vector3 position = transform.position;
        Vector3 normal = surface.Normal;
        position -= normal * (Vector3.Dot(position - surface.transform.position, normal) - surface.SurfaceOffset);
        transform.SetPositionAndRotation(position, surface.transform.rotation);
        transform.hasChanged = false;
    }

    // How far in front of the surface's plane its wall face is.
    private static float VisibleFace(DuduSurface surface)
    {
        Bounds bounds;
        if (surface.TryGetComponent(out Collider wall)) bounds = wall.bounds;
        else if (surface.TryGetComponent(out Renderer look)) bounds = look.bounds;
        else return 0f;
        Vector3 normal = surface.Normal;
        Vector3 reach = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
        return Vector3.Dot(bounds.center - surface.transform.position, normal) + Vector3.Dot(reach, bounds.extents);
    }

    // The Dudu wall this line lies on: the nearest wall plane it overlaps.
    private DuduSurface FindWall()
    {
        DuduSurface best = null;
        float bestDistance = 1.5f;
        foreach (DuduSurface surface in FindObjectsByType<DuduSurface>())
        {
            // The wall itself can be longer than Dudu's walkable width.
            Vector2 local = surface.WorldToSurface(transform.position);
            bool onWall = surface.TryGetComponent(out Collider wall) &&
                wall.bounds.SqrDistance(transform.position) < 0.25f;
            if (!onWall && (Mathf.Abs(local.x) > surface.Width * 0.5f + 0.5f || Mathf.Abs(local.y) > surface.Height * 0.5f + 0.5f))
                continue;
            float distance = Mathf.Abs(Vector3.Dot(transform.position - surface.transform.position, surface.Normal));
            if (distance < bestDistance) { best = surface; bestDistance = distance; }
        }
        return best;
    }
}
