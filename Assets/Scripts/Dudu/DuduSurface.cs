using UnityEngine;

public sealed class DuduSurface : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float width = 10f;
    [SerializeField, Min(0.1f)] private float height = 6f;
    [SerializeField, Min(0.001f)] private float surfaceOffset = 0.03f;
    [SerializeField] private bool useRightBoundary;
    [SerializeField] private float rightBoundary = 5.8f;

    public DuduSurface previousSurface;
    public DuduSurface nextSurface;

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

    public Vector2 WorldToSurface(Vector3 worldPosition)
    {
        Vector3 relativePosition = worldPosition - transform.position;
        return new Vector2(
            Vector3.Dot(relativePosition, Right.normalized),
            Vector3.Dot(relativePosition, Up.normalized));
    }

    public bool IsAtOrBelowBottom(Vector3 worldPosition, float lowerExtent)
    {
        Vector2 position = WorldToSurface(worldPosition);
        float bottom = position.y - Mathf.Max(0f, lowerExtent);
        return bottom <= -height * 0.5f;
    }

    public Vector2 ClampPosition(Vector2 position, Vector2 characterHalfSize)
    {
        float verticalLimit = Mathf.Max(0f, height * 0.5f - characterHalfSize.y);
        return new Vector2(
            Mathf.Clamp(position.x, GetMinimumX(characterHalfSize.x), GetMaximumX(characterHalfSize.x)),
            Mathf.Clamp(position.y, -verticalLimit, verticalLimit));
    }

    public float GetMinimumX(float characterHalfWidth)
    {
        return -Mathf.Max(0f, width * 0.5f - characterHalfWidth);
    }

    public float GetMaximumX(float characterHalfWidth)
    {
        float surfaceMaximum = Mathf.Max(0f, width * 0.5f - characterHalfWidth);
        return useRightBoundary ? Mathf.Min(surfaceMaximum, rightBoundary) : surfaceMaximum;
    }

    public void SetRightBoundary(float maximumCenterX)
    {
        useRightBoundary = true;
        rightBoundary = maximumCenterX;
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

