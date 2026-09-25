using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class LineMagnet : MonoBehaviour
{
    [Header("Surface")]
    [SerializeField] private DuduSurface surface;

    [Header("Attraction")]
    [SerializeField, Min(0.1f)] private float attractionRange = 3.5f;
    [SerializeField, Min(1f)] private float verticalRangeMultiplier = 2f;
    [SerializeField, Min(0f)] private float attractionForce = 2.25f;

    [Header("Timing")]
    [SerializeField, Min(0.05f)] private float activeDuration = 3f;
    [SerializeField, Min(0.05f)] private float inactiveDuration = 3f;

    [Header("Visuals")]
    [SerializeField] private Renderer[] visualRenderers;
    [SerializeField, Range(0f, 1f)] private float inactiveAlpha = 0.28f;

    private GameObject activeVisual;
    private GameObject inactiveVisual;

    private static readonly List<LineMagnet> Instances = new List<LineMagnet>();
    private MaterialPropertyBlock propertyBlock;
    private bool isActive = true;
    private float stateEndsAt;

    public bool IsActive => isActive;
    public DuduSurface Surface => surface;

    public void Configure(DuduSurface targetSurface)
    {
        surface = targetSurface;
    }

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        Transform visualRoot = transform.Find("Visual");
        if (visualRoot != null)
        {
            Transform active = visualRoot.Find("activate");
            Transform inactive = visualRoot.Find("deactivate");
            activeVisual = active != null ? active.gameObject : null;
            inactiveVisual = inactive != null ? inactive.gameObject : null;
        }
        if (visualRenderers == null || visualRenderers.Length == 0)
            visualRenderers = GetComponentsInChildren<Renderer>(true);
        ResetCycle();
    }

    private void OnEnable()
    {
        if (!Instances.Contains(this)) Instances.Add(this);
        ResetCycle();
    }

    private void OnDisable()
    {
        Instances.Remove(this);
    }

    private void Update()
    {
        if (Time.time < stateEndsAt) return;
        isActive = !isActive;
        stateEndsAt = Time.time + (isActive ? activeDuration : inactiveDuration);
        UpdateVisuals();
    }

    public static Vector2 GetSurfaceVelocity(DuduSurface targetSurface, Vector3 worldPoint)
    {
        if (targetSurface == null) return Vector2.zero;
        Vector2 result = Vector2.zero;
        for (int i = Instances.Count - 1; i >= 0; i--)
        {
            LineMagnet magnet = Instances[i];
            if (magnet == null)
            {
                Instances.RemoveAt(i);
                continue;
            }
            if (!magnet.isActive || magnet.surface != targetSurface) continue;

            Vector2 playerPosition = targetSurface.WorldToSurface(worldPoint);
            Vector2 magnetPosition = targetSurface.WorldToSurface(magnet.transform.position);
            Vector2 offset = magnetPosition - playerPosition;
            Vector2 scaledOffset = new Vector2(offset.x, offset.y / magnet.verticalRangeMultiplier);
            float distance = scaledOffset.magnitude;
            if (distance <= 0.001f || distance >= magnet.attractionRange) continue;

            float proximity = 1f - distance / magnet.attractionRange;
            result += offset / distance * (magnet.attractionForce * proximity);
        }
        return result;
    }

    private void ResetCycle()
    {
        isActive = true;
        stateEndsAt = Time.time + activeDuration;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (activeVisual != null && inactiveVisual != null)
        {
            activeVisual.SetActive(isActive);
            inactiveVisual.SetActive(!isActive);
            return;
        }

        if (visualRenderers == null) return;
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
        float alphaMultiplier = isActive ? 1f : inactiveAlpha;
        foreach (Renderer target in visualRenderers)
        {
            if (target == null || target.sharedMaterial == null) continue;
            Color color = target.sharedMaterial.color;
            color *= alphaMultiplier;
            color.a = target.sharedMaterial.color.a * alphaMultiplier;
            target.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            target.SetPropertyBlock(propertyBlock);
        }
    }

    private void OnValidate()
    {
        attractionRange = Mathf.Max(0.1f, attractionRange);
        verticalRangeMultiplier = Mathf.Max(1f, verticalRangeMultiplier);
        attractionForce = Mathf.Max(0f, attractionForce);
        activeDuration = Mathf.Max(0.05f, activeDuration);
        inactiveDuration = Mathf.Max(0.05f, inactiveDuration);
    }

    private void OnDrawGizmosSelected()
    {
        if (surface == null) return;
        Gizmos.color = isActive ? new Color(1f, 0.2f, 0.55f, 0.9f) : new Color(0.5f, 0.5f, 0.5f, 0.55f);
        const int segments = 48;
        Vector3 center = surface.SurfaceToWorld(surface.WorldToSurface(transform.position));
        Vector3 previous = center + surface.Right.normalized * attractionRange;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 next = center + (surface.Right.normalized * Mathf.Cos(angle) +
                surface.Up.normalized * Mathf.Sin(angle) * verticalRangeMultiplier) * attractionRange;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
