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

    private static readonly List<HotZone> ActiveZones = new List<HotZone>();
    private BoxCollider zoneCollider;

    public void Configure(DuduSurface targetSurface, float duration = 2f)
    {
        surface = targetSurface;
        fadeDuration = Mathf.Max(0.05f, duration);
    }

    private void Awake()
    {
        zoneCollider = GetComponent<BoxCollider>();
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
            if (!zone.Contains(worldPoint)) continue;
            duration = zone.fadeDuration;
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
