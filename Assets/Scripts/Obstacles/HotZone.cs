using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class HotZone : MonoBehaviour
{
    [SerializeField] private DuduSurface surface;
    [SerializeField, Min(0.05f)] private float fadeDuration = 2f;
    [SerializeField] private Renderer visualRenderer;
    [SerializeField] private Color zoneColor = new Color(1f, 0.18f, 0.12f, 0.28f);

    // Strokes sit just in front of their wall; anything further away is on another wall.
    private const float MaxWallDistance = 0.5f;
    private static readonly List<HotZone> ActiveZones = new List<HotZone>();
    private BoxCollider zoneCollider;

    public DuduSurface Surface => surface;

    public void Configure(DuduSurface targetSurface, float duration = 2f)
    {
        surface = targetSurface;
        fadeDuration = Mathf.Max(0.05f, duration);
    }

    private void Awake()
    {
        zoneCollider = GetComponent<BoxCollider>();
        gameObject.AddComponent<HotZoneFx>();
        if (visualRenderer == null) visualRenderer = GetComponent<Renderer>();
        // Preserve authored sprite colors. The tint is only for the legacy mesh visual.
        if (visualRenderer != null && visualRenderer is not SpriteRenderer)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            visualRenderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", zoneColor);
            block.SetColor("_Color", zoneColor);
            visualRenderer.SetPropertyBlock(block);
        }
    }

    private void OnEnable()
    {
        if (!ActiveZones.Contains(this)) ActiveZones.Add(this);
    }

    private void OnDisable() => ActiveZones.Remove(this);

    public static bool TryGetFadeDuration(Vector3 worldPoint, Vector3 surfaceNormal, out float duration)
    {
        foreach (HotZone zone in ActiveZones)
        {
            if (zone == null || zone.surface == null) continue;
            if (Mathf.Abs(Vector3.Dot(surfaceNormal.normalized, zone.surface.Normal.normalized)) < 0.9f) continue;
            // A parallel wall elsewhere in the room (same facing) is not this zone's wall.
            if (Mathf.Abs(Vector3.Dot(worldPoint - zone.surface.transform.position, zone.surface.Normal.normalized)) > MaxWallDistance) continue;
            if (!zone.Contains(worldPoint)) continue;
            duration = zone.fadeDuration;
            return true;
        }
        duration = 0f;
        return false;
    }

    // A whole line at once: its points and segment midpoints, in order, answer as the
    // point-by-point query above would. Each zone's wall and transform are read once per
    // call, not once per point, so a long line costs next to nothing.
    public static bool TryGetFadeDuration(LineRenderer line, Vector3 surfaceNormal, out float duration)
    {
        duration = 0f;
        int count = line != null ? line.positionCount : 0;
        if (count == 0) return false;
        Vector3 strokeNormal = surfaceNormal.normalized;
        ZoneShape.LineBox(line, count, out Vector3 boxCenter, out Vector3 boxExtents);
        int zones = 0;
        foreach (HotZone zone in ActiveZones)
        {
            if (zone == null || zone.surface == null) continue;
            Vector3 normal = zone.surface.Normal.normalized;
            if (Mathf.Abs(Vector3.Dot(strokeNormal, normal)) < 0.9f) continue;
            if (zone.zoneCollider == null) zone.zoneCollider = zone.GetComponent<BoxCollider>();
            var shape = new ZoneShape(zone.surface.transform.position, normal, zone.transform.worldToLocalMatrix,
                zone.zoneCollider.center, zone.zoneCollider.size * 0.5f, zone.fadeDuration);
            if (!shape.MayContain(boxCenter, boxExtents, MaxWallDistance)) continue;
            if (zones == Scratch.Length) System.Array.Resize(ref Scratch, zones * 2);
            Scratch[zones++] = shape;
        }
        if (zones == 0) return false;
        for (int i = 0; i < count; i++)
        {
            Vector3 point = line.GetPosition(i);
            if (FirstHit(point, zones, out duration)) return true;
            if (i + 1 < count && FirstHit((point + line.GetPosition(i + 1)) * 0.5f, zones, out duration)) return true;
        }
        return false;
    }

    private static ZoneShape[] Scratch = new ZoneShape[16];

    private static bool FirstHit(Vector3 point, int zones, out float duration)
    {
        for (int z = 0; z < zones; z++)
        {
            if (!Scratch[z].Contains(point, MaxWallDistance)) continue;
            duration = Scratch[z].Value;
            return true;
        }
        duration = 0f;
        return false;
    }

    private bool Contains(Vector3 worldPoint)
    {
        if (zoneCollider == null) zoneCollider = GetComponent<BoxCollider>();
        Vector3 local = transform.InverseTransformPoint(worldPoint) - zoneCollider.center;
        Vector3 half = zoneCollider.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y;
    }

    private void OnValidate() => fadeDuration = Mathf.Max(0.05f, fadeDuration);
}
