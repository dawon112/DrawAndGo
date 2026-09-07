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
    [Tooltip("Maximum surface-space distance at which new projectiles can be fired.")]
    [SerializeField, Min(0f)] private float attackRange = 9f;

    [Header("Audio")]
    [SerializeField] private AudioClip fireSound;
    [SerializeField, Range(0f, 1f)] private float fireSoundVolume = 0.85f;

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
    private AudioSource audioSource;
    private static AudioClip defaultFireSound;
    private static Sprite projectileSprite;

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

        nextShotTime = Time.time + fireInterval;
        if (IsTargetInRange())
            FireProjectile();
    }

    private bool IsTargetInRange()
    {
        if (surface == null || target == null || target.CurrentSurface != surface)
            return false;
        Vector2 targetPosition = surface.WorldToSurface(target.transform.position);
        return Vector2.Distance(surfacePosition, targetPosition) <= attackRange;
    }

    private void FireProjectile()
    {
        if (surface == null || target == null)
            return;

        Vector2 targetPosition = surface.WorldToSurface(target.transform.position);
        Vector2 fireDirection = (targetPosition - surfacePosition).normalized;
        Vector2 spawnPosition = surfacePosition + fireDirection * 0.9f;

        GameObject projectile = new GameObject("Homing Projectile");
        projectile.name = "Homing Projectile";
        projectile.transform.SetPositionAndRotation(
            surface.SurfaceToWorld(spawnPosition) + surface.Normal.normalized * 0.1f,
            surface.transform.rotation);
        projectile.transform.localScale = projectileScale;

        SpriteRenderer projectileRenderer = projectile.AddComponent<SpriteRenderer>();
        projectileRenderer.sprite = GetProjectileSprite();
        projectileRenderer.color = Color.black;
        projectileRenderer.sortingOrder = 25;

        SphereCollider projectileCollider = projectile.AddComponent<SphereCollider>();
        projectileCollider.isTrigger = true;
        projectile.AddComponent<Rigidbody>();
        DuduHomingProjectile homingProjectile = projectile.AddComponent<DuduHomingProjectile>();
        homingProjectile.Configure(
            surface,
            target,
            projectileSpeed,
            projectileTurnSpeed,
            projectileLifetime);
        PlayFireSound();
    }

    private static Sprite GetProjectileSprite()
    {
        if (projectileSprite != null) return projectileSprite;
        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "Generated Homing Projectile Circle";
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f;
            float dy = (y + 0.5f) / size * 2f - 1f;
            float alpha = dx * dx + dy * dy <= 0.9f * 0.9f ? 1f : 0f;
            pixels[y * size + x] = new Color(0f, 0f, 0f, alpha);
        }
        texture.SetPixels(pixels);
        texture.Apply();
        projectileSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        projectileSprite.name = "Generated Homing Projectile Circle";
        return projectileSprite;
    }

    private void PlayFireSound()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.priority = 64;
            audioSource.volume = 1f;
        }
        audioSource.PlayOneShot(fireSound != null ? fireSound : GetDefaultFireSound(), fireSoundVolume);
    }

    private static AudioClip GetDefaultFireSound()
    {
        if (defaultFireSound != null) return defaultFireSound;
        const int sampleRate = 44100;
        const float duration = 0.18f;
        float[] samples = new float[Mathf.CeilToInt(sampleRate * duration)];
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)sampleRate;
            float frequency = Mathf.Lerp(1100f, 240f, t / duration);
            phase += 2f * Mathf.PI * frequency / sampleRate;
            float metallic = Mathf.Sin(phase) * 0.78f + Mathf.Sign(Mathf.Sin(phase * 0.31f)) * 0.22f;
            samples[i] = Mathf.Clamp(metallic * Mathf.Exp(-14f * t), -1f, 1f);
        }
        defaultFireSound = AudioClip.Create("Generated Mechanical Fire", samples.Length, 1, sampleRate, false);
        defaultFireSound.SetData(samples, 0);
        return defaultFireSound;
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
        attackRange = Mathf.Max(0f, attackRange);
        projectileSpeed = Mathf.Max(0f, projectileSpeed);
        projectileTurnSpeed = Mathf.Max(0f, projectileTurnSpeed);
        projectileLifetime = Mathf.Max(0.1f, projectileLifetime);

        if (!Application.isPlaying)
            ApplySurfaceTransform();
    }

    private void OnDrawGizmosSelected()
    {
        if (surface == null || attackRange <= 0f) return;
        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.8f);
        const int segments = 32;
        Vector3 previous = surface.SurfaceToWorld(surfacePosition + Vector2.right * attackRange);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector2 point = surfacePosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * attackRange;
            Vector3 current = surface.SurfaceToWorld(point);
            Gizmos.DrawLine(previous, current);
            previous = current;
        }
    }
}
