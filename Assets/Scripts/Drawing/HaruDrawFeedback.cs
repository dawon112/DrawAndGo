using UnityEngine;

// What makes Haru's crayon and eraser feel real, on Haru's screen. Drawing: wax crumbs
// falling from the tip (more and further when the stroke is fast), a tiny dust puff
// where the crayon meets the wall, and a scratching sound. Erasing: rubber crumbs and
// rubbed-off crayon dust, and a softer rubbing sound. Each sound picks one of a few
// variants with a random pitch so it never loops audibly. HaruDrawingController drives
// this. Sounds: every clip in Resources/Audio/CrayonDraw and Resources/Audio/EraserRub.
[DisallowMultipleComponent]
public sealed class HaruDrawFeedback : MonoBehaviour
{
    private static readonly Color Rubber = new Color(0.93f, 0.84f, 0.84f);

    [Header("Crumbs")]
    [Tooltip("Crumbs per metre of line at a slow, steady stroke.")]
    [SerializeField, Min(0f)] private float crumbsPerMetre = 55f;
    [Tooltip("Stroke speed (m/s) at which crumbs reach their most and fly furthest.")]
    [SerializeField, Min(0.1f)] private float fastSpeed = 2.5f;
    [SerializeField, Min(1f)] private float fastCrumbMultiplier = 3f;
    [Tooltip("Crumbs per metre the eraser travels while it is rubbing a line out.")]
    [SerializeField, Min(0f)] private float eraserCrumbsPerMetre = 70f;

    [Header("Sound")]
    [SerializeField, Range(0f, 1f)] private float drawVolume = 0.35f;
    [SerializeField, Range(0f, 1f)] private float eraseVolume = 0.3f;
    [SerializeField] private Vector2 pitchRange = new Vector2(0.9f, 1.12f);
    [Tooltip("Speed (m/s) that plays at full volume; slower strokes are quieter.")]
    [SerializeField, Min(0.05f)] private float loudSpeed = 1.2f;

    // One looping-by-variants sound (the crayon's scratch or the eraser's rub).
    private sealed class Voice
    {
        public AudioSource source;
        public AudioClip[] clips;
        public int lastClip = -1;
        public float speed;
        public float lastMoveTime = float.NegativeInfinity;
        public bool active;
        public Vector3 lastPoint;

        public void Moved(float distance)
        {
            float elapsed = Mathf.Max(Time.time - lastMoveTime, Time.deltaTime, 0.001f);
            lastMoveTime = Time.time;
            speed = Mathf.Lerp(speed, Mathf.Min(distance / elapsed, 20f), 0.5f);
        }
    }

    private ParticleSystem crumbs;
    private ParticleSystem puff;
    private Voice draw;
    private Voice erase;
    private Vector3 wallNormal = Vector3.forward;
    private Color crayon = Color.white;
    private float pendingCrumbs;
    private float pendingEraserCrumbs;

    private void Awake()
    {
        draw = CreateVoice("Audio/CrayonDraw");
        erase = CreateVoice("Audio/EraserRub");
        Material material = CreateParticleMaterial();
        crumbs = CreateSystem("Crayon Crumbs", material, true);
        puff = CreateSystem("Crayon Dust Puff", material, false);
    }

    private Voice CreateVoice(string folder)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.volume = 0f;
        return new Voice { source = source, clips = Resources.LoadAll<AudioClip>(folder) };
    }

    public void StrokeStarted(Vector3 point, Vector3 normal, Color color)
    {
        draw.active = true;
        wallNormal = normal.sqrMagnitude > 0f ? normal.normalized : Vector3.forward;
        crayon = color;
        draw.speed = 0f;
        draw.lastMoveTime = Time.time;
        pendingCrumbs = 0f;
        Puff(point, Color.Lerp(crayon, Color.white, 0.55f), 7);
        for (int i = 0; i < 3; i++) EmitCrumb(point, Vector3.zero, crayon, 1f);
    }

    public void StrokeMoved(Vector3 from, Vector3 to)
    {
        if (!draw.active) return;
        float distance = Vector3.Distance(from, to);
        draw.Moved(distance);
        float fast = Mathf.Clamp01(draw.speed / fastSpeed);
        pendingCrumbs += distance * crumbsPerMetre * Mathf.Lerp(1f, fastCrumbMultiplier, fast);
        int count = Mathf.FloorToInt(pendingCrumbs);
        pendingCrumbs -= count;
        Vector3 along = distance > 0f ? (to - from) / distance : Vector3.zero;
        for (int i = 0; i < count; i++)
            EmitCrumb(Vector3.Lerp(from, to, Random.value), along * fast, crayon, 1f);
    }

    public void StrokeEnded() => draw.active = false;

    // Called every frame the eraser is held on a wall; removed says it rubbed a line away.
    public void Erasing(Vector3 point, Vector3 normal, bool removed, Color color)
    {
        wallNormal = normal.sqrMagnitude > 0f ? normal.normalized : Vector3.forward;
        bool fresh = !erase.active || Time.time - erase.lastMoveTime > 0.25f;
        float distance = fresh ? 0f : Vector3.Distance(erase.lastPoint, point);
        erase.active = true;
        erase.lastPoint = point;
        if (distance > 0.0005f || fresh) erase.Moved(distance);
        if (!removed) return;

        // Rubbed-off crayon turns to dust, and the eraser sheds little rubber crumbs.
        if (fresh) Puff(point, Color.Lerp(color, Color.white, 0.4f), 5);
        pendingEraserCrumbs += Mathf.Max(distance, 0.004f) * eraserCrumbsPerMetre;
        int count = Mathf.FloorToInt(pendingEraserCrumbs);
        pendingEraserCrumbs -= count;
        for (int i = 0; i < count; i++)
        {
            bool rubber = Random.value < 0.45f;
            EmitCrumb(point + Random.insideUnitSphere * 0.03f, Vector3.zero, rubber ? Rubber : color, rubber ? 1.5f : 0.9f);
        }
        if (Random.value < 0.25f) Puff(point, Color.Lerp(color, Color.white, 0.5f), 1);
    }

    private void Update()
    {
        UpdateVoice(draw, drawVolume);
        UpdateVoice(erase, eraseVolume);
    }

    // Quiet when resting on the wall, silent off it; louder the faster it moves.
    private void UpdateVoice(Voice voice, float volume)
    {
        if (voice.active && Time.time - voice.lastMoveTime > 0.3f && voice == erase) voice.active = false;
        bool moving = voice.active && Time.time - voice.lastMoveTime < 0.12f;
        float target = moving ? volume * Mathf.Clamp01(0.3f + voice.speed / loudSpeed) : 0f;
        if (!moving) voice.speed = Mathf.MoveTowards(voice.speed, 0f, Time.deltaTime * 6f);
        float current = voice.source.volume;
        voice.source.volume = Mathf.MoveTowards(current, target * CoopAudioSettings.SfxVolume,
            Time.deltaTime * (target > current ? 6f : 4f));
        if (moving && !voice.source.isPlaying && voice.clips.Length > 0) PlayNext(voice);
        else if (!voice.active && voice.source.isPlaying && voice.source.volume <= 0.001f) voice.source.Stop();
    }

    // A different variant each time (never the same twice), each at its own pitch.
    private void PlayNext(Voice voice)
    {
        int count = voice.clips.Length;
        int pick = Random.Range(0, count);
        if (count > 1 && pick == voice.lastClip) pick = (pick + 1 + Random.Range(0, count - 1)) % count;
        voice.lastClip = pick;
        voice.source.clip = voice.clips[pick];
        voice.source.pitch = Random.Range(pitchRange.x, pitchRange.y);
        voice.source.Play();
    }

    // A flake knocked off the tip: it springs off the wall, drifts along a fast stroke,
    // then drops and fades.
    private void EmitCrumb(Vector3 point, Vector3 fling, Color color, float scale)
    {
        crumbs.Emit(new ParticleSystem.EmitParams
        {
            position = point + wallNormal * 0.01f,
            velocity = wallNormal * Random.Range(0.08f, 0.35f) + Random.insideUnitSphere * 0.12f +
                fling * Random.Range(0.3f, 0.9f) + Vector3.down * Random.Range(0f, 0.15f),
            startSize = Random.Range(0.006f, 0.016f) * scale,
            startLifetime = Random.Range(0.7f, 1.4f),
            startColor = Shade(color, Random.Range(0.6f, 1f), 1f),
            rotation = Random.Range(0f, 360f)
        }, 1);
    }

    // A tiny soft cloud of chalky dust on the wall.
    private void Puff(Vector3 point, Color color, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 drift = Vector3.ProjectOnPlane(Random.insideUnitSphere, wallNormal) * 0.08f;
            puff.Emit(new ParticleSystem.EmitParams
            {
                position = point + wallNormal * 0.015f,
                velocity = drift + wallNormal * Random.Range(0.02f, 0.07f),
                startSize = Random.Range(0.025f, 0.05f),
                startLifetime = Random.Range(0.35f, 0.6f),
                startColor = Shade(color, 1f, Random.Range(0.25f, 0.4f)),
                rotation = Random.Range(0f, 360f)
            }, 1);
        }
    }

    private static Color Shade(Color color, float brightness, float alpha) =>
        new Color(color.r * brightness, color.g * brightness, color.b * brightness, alpha);

    private ParticleSystem CreateSystem(string name, Material material, bool falls)
    {
        // World space and not under the camera: crumbs must not follow Haru's view.
        GameObject holder = new GameObject(name);
        ParticleSystem system = holder.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.loop = false;
        main.maxParticles = falls ? 400 : 60;
        main.gravityModifier = falls ? 0.55f : -0.02f;
        main.startSpeed = 0f;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
        fade.enabled = true;
        Gradient alpha = new Gradient();
        alpha.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            falls
                ? new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) }
                : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = alpha;

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = !falls;
        if (!falls) size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));

        if (falls)
        {
            // Air slows the light flakes as they fall.
            ParticleSystem.LimitVelocityOverLifetimeModule drag = system.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.2f;
            drag.multiplyDragByParticleSize = false;
            ParticleSystem.CollisionModule collision = system.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Low;
            collision.dampen = 0.85f;
            collision.bounce = 0.15f;
            collision.lifetimeLoss = 0f;
            collision.radiusScale = 0.5f;
            collision.collidesWith = LayerMask.GetMask("Default", "DrawingSurface");
        }

        ParticleSystemRenderer look = holder.GetComponent<ParticleSystemRenderer>();
        look.sharedMaterial = material;
        look.renderMode = ParticleSystemRenderMode.Billboard;
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        look.receiveShadows = false;
        look.sortingFudge = falls ? 0f : 1f;
        system.Play();
        return system;
    }

    // A soft, slightly ragged dot: chalky dust when big and faint, a flake when small.
    private static Material CreateParticleMaterial()
    {
        const int size = 32;
        Texture2D dot = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Crayon Crumb", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) / size * 2f - Vector2.one;
                float edge = 0.75f + (Mathf.PerlinNoise(x * 0.35f, y * 0.35f) - 0.5f) * 0.35f;
                float a = Mathf.Clamp01((edge - p.magnitude) / 0.25f);
                dot.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        dot.Apply();
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material material = new Material(shader) { name = "Crayon Crumb" };
        material.mainTexture = dot;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", dot);
        // Transparent, alpha blended (URP particle shader settings).
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return material;
    }

    private void OnDestroy()
    {
        if (crumbs != null) Destroy(crumbs.gameObject);
        if (puff != null) Destroy(puff.gameObject);
    }
}
