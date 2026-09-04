using UnityEngine;

[DisallowMultipleComponent]
public sealed class DuduHomingShooter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DuduSurface surface;
    [SerializeField] private DuduSurfaceMovement target;

    [Header("Enemy Placement")]
    [Tooltip("Enemy position on the Dudu surface.")]
    [SerializeField] private Vector2 surfacePosition = new Vector2(-1f, 1.5f);

    [Header("Firing")]
    [Tooltip("Seconds between projectile shots.")]
    [SerializeField, Min(0.1f)] private float fireInterval = 2.5f;
    [Tooltip("Delay before the first shot after entering 2D mode.")]
    [SerializeField, Min(0f)] private float firstShotDelay = 1f;

    [Header("Homing Projectile")]
    [Tooltip("Projectile movement speed.")]
    [SerializeField, Min(0f)] private float projectileSpeed = 2.45f;
    [Tooltip("Maximum homing turn speed in degrees per second.")]
    [SerializeField, Min(0f)] private float projectileTurnSpeed = 180f;
    [Tooltip("Seconds before a projectile is automatically removed.")]
    [SerializeField, Min(0.1f)] private float projectileLifetime = 8f;
    [Tooltip("Temporary projectile visual size.")]
    [SerializeField] private Vector3 projectileScale = new Vector3(0.35f, 0.35f, 0.12f);
    [SerializeField] private Material projectileMaterial;

    private bool targetWasActive;
    private float nextShotTime;

    public void Configure(
        DuduSurface targetSurface,
        DuduSurfaceMovement targetDudu,
        Vector2 position,
        Material bulletMaterial)
    {
        surface = targetSurface;
        target = targetDudu;
        surfacePosition = position;
        projectileMaterial = bulletMaterial;
        ApplySurfaceTransform();
    }

    private void Update()
    {
        // Temporary 3D testing gate: reuse the view manager's existing input state.
        bool targetIsActive = surface != null && target != null && target.InputEnabled;
        if (!targetIsActive)
        {
            targetWasActive = false;
            return;
        }

        if (!targetWasActive)
        {
            targetWasActive = true;
            nextShotTime = Time.time + firstShotDelay;
        }

        if (Time.time < nextShotTime)
            return;

        FireProjectile();
        nextShotTime = Time.time + fireInterval;
    }

    private void FireProjectile()
    {
        if (surface == null || target == null)
            return;

        Vector2 targetPosition = surface.WorldToSurface(target.transform.position);
        Vector2 fireDirection = (targetPosition - surfacePosition).normalized;
        Vector2 spawnPosition = surfacePosition + fireDirection * 0.9f;

        GameObject projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projectile.name = "Homing Projectile";
        projectile.transform.SetPositionAndRotation(
            surface.SurfaceToWorld(spawnPosition),
            surface.transform.rotation);
        projectile.transform.localScale = projectileScale;

        Renderer projectileRenderer = projectile.GetComponent<Renderer>();
        if (projectileMaterial != null)
            projectileRenderer.sharedMaterial = projectileMaterial;

        projectile.GetComponent<Collider>().isTrigger = true;
        projectile.AddComponent<Rigidbody>();
        DuduHomingProjectile homingProjectile = projectile.AddComponent<DuduHomingProjectile>();
        homingProjectile.Configure(
            surface,
            target,
            projectileSpeed,
            projectileTurnSpeed,
            projectileLifetime);
    }

    private void ApplySurfaceTransform()
    {
        if (surface == null)
            return;

        transform.SetPositionAndRotation(
            surface.SurfaceToWorld(surfacePosition),
            surface.transform.rotation);
    }

    private void OnValidate()
    {
        fireInterval = Mathf.Max(0.1f, fireInterval);
        firstShotDelay = Mathf.Max(0f, firstShotDelay);
        projectileSpeed = Mathf.Max(0f, projectileSpeed);
        projectileTurnSpeed = Mathf.Max(0f, projectileTurnSpeed);
        projectileLifetime = Mathf.Max(0.1f, projectileLifetime);

        if (!Application.isPlaying)
            ApplySurfaceTransform();
    }
}
