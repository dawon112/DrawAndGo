using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class NoDrawZone : MonoBehaviour
{
    [SerializeField] private DuduSurface surface;
    private BoxCollider zoneCollider;
    private static readonly List<NoDrawZone> ActiveZones = new List<NoDrawZone>();

    public void Configure(DuduSurface targetSurface)
    {
        surface = targetSurface;
    }

    private void Awake()
    {
        zoneCollider = GetComponent<BoxCollider>();
        ResolveSurface();
    }

    private void OnEnable()
    {
        ResolveSurface();
        if (!ActiveZones.Contains(this)) ActiveZones.Add(this);
    }

    private void OnDisable()
    {
        ActiveZones.Remove(this);
    }

    public static bool Blocks(DrawingSurface drawingSurface, Vector3 worldPoint)
    {
        if (drawingSurface == null) return false;
        DuduSurface targetSurface = drawingSurface.GetComponent<DuduSurface>();
        foreach (NoDrawZone zone in ActiveZones)
            if (zone != null && zone.surface == targetSurface && zone.Contains(worldPoint))
                return true;
        return false;
    }

    private bool Contains(Vector3 worldPoint)
    {
        if (zoneCollider == null) zoneCollider = GetComponent<BoxCollider>();
        Vector3 local = transform.InverseTransformPoint(worldPoint) - zoneCollider.center;
        Vector3 half = zoneCollider.size * 0.5f;
        // The zone is bound to one surface already, so depth must not matter: the
        // drawing ray hits the paper plane while the visual sits slightly in front.
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y;
    }

    [ContextMenu("Auto Assign Nearest Surface")]
    private void ResolveSurface()
    {
        Vector3 placedPosition = transform.position;
        surface = null;
        float bestScore = float.PositiveInfinity;
        foreach (DuduSurface candidate in FindObjectsByType<DuduSurface>(FindObjectsInactive.Exclude))
        {
            Vector2 position = candidate.WorldToSurface(transform.position);
            float outsideX = Mathf.Max(0f, Mathf.Abs(position.x) - candidate.Width * 0.5f);
            float outsideY = Mathf.Max(0f, Mathf.Abs(position.y) - candidate.Height * 0.5f);
            float planeDistance = Mathf.Abs(Vector3.Dot(
                transform.position - candidate.transform.position,
                candidate.Normal.normalized));
            float alignmentPenalty = 1f - Mathf.Abs(Vector3.Dot(
                transform.forward.normalized,
                candidate.transform.forward.normalized));
            float score = planeDistance + (outsideX + outsideY) * 4f + alignmentPenalty;
            if (score >= bestScore) continue;
            bestScore = score;
            surface = candidate;
        }

        if (surface != null)
        {
            Vector2 position = surface.WorldToSurface(placedPosition);
            transform.SetPositionAndRotation(
                surface.SurfaceToWorld(position) + surface.Normal.normalized * 0.012f,
                surface.transform.rotation);
        }
    }
}
