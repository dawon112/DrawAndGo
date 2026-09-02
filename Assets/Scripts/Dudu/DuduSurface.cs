using UnityEngine;

public sealed class DuduSurface : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float width = 10f;
    [SerializeField, Min(0.1f)] private float height = 6f;
    [SerializeField, Min(0.001f)] private float surfaceOffset = 0.03f;

    public float Width => width;
    public float Height => height;
    public float SurfaceOffset => surfaceOffset;
    public Vector3 Right => transform.right;
    public Vector3 Up => transform.up;
    public Vector3 Normal => -transform.forward;

    public Vector3 SurfaceToWorld(Vector2 surfacePosition)
    {
        return transform.position + Right * surfacePosition.x + Up * surfacePosition.y + Normal * surfaceOffset;
    }

    public Vector2 ClampPosition(Vector2 position, Vector2 characterHalfSize)
    {
        float horizontalLimit = Mathf.Max(0f, width * 0.5f - characterHalfSize.x);
        float verticalLimit = Mathf.Max(0f, height * 0.5f - characterHalfSize.y);
        return new Vector2(
            Mathf.Clamp(position.x, -horizontalLimit, horizontalLimit),
            Mathf.Clamp(position.y, -verticalLimit, verticalLimit));
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 center = transform.position + Normal * surfaceOffset;
        Vector3 right = Right * width * 0.5f;
        Vector3 up = Up * height * 0.5f;
        Gizmos.DrawLine(center - right - up, center + right - up);
        Gizmos.DrawLine(center + right - up, center + right + up);
        Gizmos.DrawLine(center + right + up, center - right + up);
        Gizmos.DrawLine(center - right + up, center - right - up);
    }
}
