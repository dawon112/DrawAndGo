using UnityEngine;

// How Dudu's moves feel: squash and stretch on jumps and landings, a short lean when he
// turns round, doodle dust where his feet touch the ground, and a little give in the line
// he lands on. Only looks: physics and the network pose are untouched. It reads what
// Dudu's pose and sprite do, so it runs the same on both PCs (Haru sees it in 3D too).
// Added and set up by DuduSurfaceMovement.
[DefaultExecutionOrder(10)]
[DisallowMultipleComponent]
public sealed class DuduMoveFeedback : MonoBehaviour
{
    private static readonly int GroundedId = Animator.StringToHash("Grounded");

    // Squash and stretch: the sprite's height as a spring around its normal size.
    private const float JumpStretch = 0.2f;
    private const float LandSquash = 0.24f;
    private const float SpringStiffness = 260f;
    private const float SpringDamping = 13f;

    // Turning round: a lean of a couple of frames, then straight again.
    private const float TurnLean = 11f;
    private const float TurnLeanHold = 0.05f;
    private const float TurnLeanRelease = 0.08f;

    // Dust.
    private const int StepPoolSize = 14;
    private const int LandPoolSize = 10;
    private const float StepDustInterval = 0.11f;
    private const float StepDustMinSpeed = 0.8f;
    private const float LandMinFallSpeed = 2f;
    private const float HardLandFallSpeed = 12f;
    // The dust picture is paper-coloured; darkened it reads as smudged crayon on the wall.
    private static readonly Color DustTint = new Color(0.64f, 0.56f, 0.48f);

    private DuduSurfaceMovement movement;
    private SpriteRenderer sprite;
    private Transform spriteTransform;
    private Animator animator;
    private bool animatorHasGrounded;
    private Sprite dustSprite;
    private Vector3 baseLocalPosition;
    private Vector3 baseLocalScale = Vector3.one;
    private Quaternion baseLocalRotation = Quaternion.identity;

    private float stretch;
    private float stretchVelocity;
    private float leanSign;
    private float leanStartedAt = -1f;
    private bool wasGrounded = true;
    private bool wasFacingLeft;
    private bool hasLastPose;
    private Vector3 lastPosition;
    private DuduSurface lastSurface;
    private float airTime;
    private bool jumpHandled;
    private float fallSpeed;
    private float nextStepDust;

    private Transform dustRoot;
    private Dust[] stepDust;
    private Dust[] landDust;
    private int nextStep;
    private int nextLand;

    // Set every frame by DuduStatusFx: how hazards are pushing his body about.
    public float ExternalLean { get; set; }
    public float ExternalSquash { get; set; }
    public Vector2 ExternalOffset { get; set; }
    public bool Grounded => wasGrounded;

    private sealed class Dust
    {
        public SpriteRenderer renderer;
        public Vector3 velocity;
        public float age;
        public float life;
        public float startSize;
        public float endSize;
        public float alpha;
        public float spin;
    }

    public void Bind(SpriteRenderer newSprite, Animator newAnimator, Sprite newDust)
    {
        RestoreSprite();
        sprite = newSprite;
        animator = newAnimator;
        dustSprite = newDust;
        spriteTransform = sprite != null ? sprite.transform : null;
        // Never scale or tilt the physics body itself.
        if (spriteTransform == transform) spriteTransform = null;
        if (spriteTransform != null)
        {
            baseLocalPosition = spriteTransform.localPosition;
            baseLocalScale = spriteTransform.localScale;
            baseLocalRotation = spriteTransform.localRotation;
        }
        animatorHasGrounded = false;
        if (animator != null && animator.runtimeAnimatorController != null)
            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.nameHash == GroundedId) animatorHasGrounded = true;
        if (sprite != null) wasFacingLeft = sprite.flipX;
        stretch = stretchVelocity = 0f;
        leanStartedAt = -1f;
        hasLastPose = false;
        CreateDust();
    }

    private void Awake()
    {
        movement = GetComponent<DuduSurfaceMovement>();
    }

    private void OnDisable() => RestoreSprite();

    private void OnDestroy()
    {
        if (dustRoot != null) Destroy(dustRoot.gameObject);
    }

    private void RestoreSprite()
    {
        if (spriteTransform == null) return;
        spriteTransform.localScale = baseLocalScale;
        spriteTransform.localRotation = baseLocalRotation;
        spriteTransform.localPosition = new Vector3(
            baseLocalPosition.x, baseLocalPosition.y, spriteTransform.localPosition.z);
    }

    private void LateUpdate()
    {
        if (movement == null || sprite == null || spriteTransform == null) return;
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        UpdateDust(dt);
        if (dt <= 0f) return;

        DuduSurface surface = movement.CurrentSurface;
        Vector3 right = surface != null ? surface.Right.normalized : transform.right;
        Vector3 up = surface != null ? surface.Up.normalized : transform.up;

        // Speed along the wall, from how the pose moved (the same on both PCs). A respawn
        // or a corner is a jump in position, not a movement.
        Vector3 position = transform.position;
        Vector3 delta = position - lastPosition;
        bool teleported = !hasLastPose || surface != lastSurface || delta.sqrMagnitude > 1f;
        lastPosition = position;
        lastSurface = surface;
        hasLastPose = true;
        float speedX = teleported ? 0f : Vector3.Dot(delta, right) / Time.deltaTime;
        float speedY = teleported ? 0f : Vector3.Dot(delta, up) / Time.deltaTime;

        bool grounded = IsGrounded();
        bool shown = sprite.enabled && sprite.gameObject.activeInHierarchy && sprite.color.a > 0.05f;
        if (teleported) fallSpeed = 0f;

        if (!grounded)
        {
            airTime += dt;
            fallSpeed = Mathf.Max(fallSpeed, -speedY);
            // The body only starts rising a physics step after the jump key.
            if (!jumpHandled && airTime < 0.15f && speedY > 1.5f)
            {
                jumpHandled = true;
                if (shown) Jumped();
            }
        }
        else
        {
            if (!wasGrounded && fallSpeed >= LandMinFallSpeed && shown)
                Landed(Mathf.InverseLerp(LandMinFallSpeed, HardLandFallSpeed, fallSpeed), right, up);
            airTime = 0f;
            jumpHandled = false;
            fallSpeed = 0f;
            if (shown && Mathf.Abs(speedX) >= StepDustMinSpeed && Time.time >= nextStepDust)
            {
                SpawnStepDust(Mathf.Sign(speedX), right, up);
                nextStepDust = Time.time + StepDustInterval * Random.Range(0.8f, 1.25f);
            }
        }
        wasGrounded = grounded;

        if (sprite.flipX != wasFacingLeft)
        {
            wasFacingLeft = sprite.flipX;
            leanSign = wasFacingLeft ? -1f : 1f;
            leanStartedAt = Time.time;
        }

        float accel = -SpringStiffness * stretch - SpringDamping * stretchVelocity;
        stretchVelocity += accel * dt;
        stretch += stretchVelocity * dt;
        ApplySpriteShape();
    }

    private bool IsGrounded()
    {
        if (movement.enabled) return movement.IsGrounded;
        // A remote Dudu: his PC sends the animator's Grounded flag.
        return !animatorHasGrounded || animator.GetBool(GroundedId);
    }

    private void Jumped()
    {
        stretch = JumpStretch;
        stretchVelocity = 0f;
    }

    private void Landed(float impact, Vector3 right, Vector3 up)
    {
        stretch = -LandSquash * Mathf.Lerp(0.45f, 1f, impact);
        stretchVelocity = 0f;
        SpawnLandDust(impact, right, up);
        BounceLineUnderFeet(impact, up);
    }

    // Scales and tilts the picture about its feet, so he squashes onto the ground rather
    // than shrinking into the air.
    private void ApplySpriteShape()
    {
        float scaleY = 1f + stretch + ExternalSquash;
        float scaleX = 1f - (stretch + ExternalSquash) * 0.7f;
        float lean = 0f;
        if (leanStartedAt >= 0f)
        {
            float t = Time.time - leanStartedAt;
            if (t < TurnLeanHold) lean = 1f;
            else if (t < TurnLeanHold + TurnLeanRelease) lean = 1f - (t - TurnLeanHold) / TurnLeanRelease;
            else leanStartedAt = -1f;
        }
        // Leans into the way he now faces.
        Quaternion tilt = Quaternion.Euler(0f, 0f, -leanSign * TurnLean * lean - ExternalLean);

        Vector3 scale = new Vector3(baseLocalScale.x * scaleX, baseLocalScale.y * scaleY, baseLocalScale.z);
        float feet = sprite.sprite != null ? -sprite.sprite.bounds.min.y : 0f;
        Vector3 baseFeet = baseLocalRotation * new Vector3(0f, -feet * baseLocalScale.y, 0f);
        Vector3 nowFeet = baseLocalRotation * tilt * new Vector3(0f, -feet * scale.y, 0f);
        Vector3 local = baseLocalPosition + baseFeet - nowFeet + (Vector3)ExternalOffset;
        spriteTransform.localScale = scale;
        spriteTransform.localRotation = baseLocalRotation * tilt;
        spriteTransform.localPosition = new Vector3(local.x, local.y, spriteTransform.localPosition.z);
    }

    // The bottom of Dudu's picture at rest, just behind the picture so his feet cover it.
    public Vector3 FeetPoint()
    {
        float feet = sprite.sprite != null ? -sprite.sprite.bounds.min.y : 0f;
        Vector3 local = baseLocalPosition + baseLocalRotation * new Vector3(0f, -feet * baseLocalScale.y, 0f);
        local.z = spriteTransform.localPosition.z + 0.02f;
        Transform parent = spriteTransform.parent;
        return parent != null ? parent.TransformPoint(local) : local;
    }

    private void SpawnStepDust(float direction, Vector3 right, Vector3 up)
    {
        Vector3 feet = FeetPoint();
        // Kicked up behind his heel, drifting back and up.
        Spawn(stepDust, ref nextStep,
            feet - right * direction * Random.Range(0.08f, 0.18f) + up * Random.Range(0f, 0.04f),
            -right * direction * Random.Range(0.3f, 0.75f) + up * Random.Range(0.25f, 0.55f),
            Random.Range(0.32f, 0.45f), Random.Range(0.16f, 0.21f), Random.Range(0.3f, 0.38f), 0.7f);
    }

    private void SpawnLandDust(float impact, Vector3 right, Vector3 up)
    {
        Vector3 feet = FeetPoint();
        int count = Mathf.RoundToInt(Mathf.Lerp(4f, 8f, impact));
        float push = Mathf.Lerp(0.7f, 1.5f, impact);
        float size = Mathf.Lerp(0.85f, 1.25f, impact);
        for (int i = 0; i < count; i++)
        {
            float side = i % 2 == 0 ? 1f : -1f;
            Spawn(landDust, ref nextLand,
                feet + right * side * Random.Range(0.05f, 0.22f) + up * Random.Range(0f, 0.05f),
                right * side * push * Random.Range(0.6f, 1.3f) + up * Random.Range(0.1f, 0.45f),
                Random.Range(0.38f, 0.55f), 0.22f * size, Random.Range(0.4f, 0.5f) * size, 0.75f);
        }
    }

    private void BounceLineUnderFeet(float impact, Vector3 up)
    {
        Vector3 feet = FeetPoint();
        LineRenderer best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (Collider hit in Physics.OverlapBox(feet, new Vector3(0.3f, 0.12f, 0.5f),
            transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            LineRenderer line = null;
            DrawingStroke stroke = hit.GetComponentInParent<DrawingStroke>();
            if (stroke != null) line = stroke.GetComponentInChildren<LineRenderer>();
            else if (hit.TryGetComponent(out CrayonRouteLine route)) line = route.GetComponentInChildren<LineRenderer>();
            if (line == null) continue;
            float distance = (hit.ClosestPoint(feet) - feet).sqrMagnitude;
            if (distance < bestDistance) { bestDistance = distance; best = line; }
        }
        if (best != null) LineLandingWobble.Trigger(best, feet, up, Mathf.Lerp(0.04f, 0.09f, impact));
    }

    private void CreateDust()
    {
        if (dustRoot != null) Destroy(dustRoot.gameObject);
        dustRoot = null;
        stepDust = landDust = null;
        if (sprite == null || dustSprite == null) return;

        dustRoot = new GameObject("Dudu Move Dust").transform;
        // Behind Dudu while he walks; the landing puff is thrown out in front of his feet.
        stepDust = CreatePool(StepPoolSize, sprite.sortingOrder - 1);
        landDust = CreatePool(LandPoolSize, sprite.sortingOrder + 1);
    }

    private Dust[] CreatePool(int size, int sortingOrder)
    {
        Dust[] pool = new Dust[size];
        Vector3 start = FeetPoint();
        for (int i = 0; i < size; i++)
        {
            GameObject puff = new GameObject("Dudu Dust");
            puff.layer = sprite.gameObject.layer;
            puff.transform.SetParent(dustRoot, false);
            // Kept on Dudu's wall even while unused, so Dudu's 2D view maps it to that wall.
            puff.transform.SetPositionAndRotation(start, transform.rotation);
            SpriteRenderer renderer = puff.AddComponent<SpriteRenderer>();
            renderer.sprite = dustSprite;
            renderer.sharedMaterial = sprite.sharedMaterial;
            renderer.sortingLayerID = sprite.sortingLayerID;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            puff.SetActive(false);
            pool[i] = new Dust { renderer = renderer };
        }
        return pool;
    }

    private void Spawn(Dust[] pool, ref int next, Vector3 position, Vector3 velocity,
        float life, float startSize, float endSize, float alpha)
    {
        if (pool == null) return;
        Dust dust = pool[next];
        next = (next + 1) % pool.Length;
        dust.velocity = velocity;
        dust.age = 0f;
        dust.life = life;
        dust.startSize = startSize;
        dust.endSize = endSize;
        dust.alpha = alpha;
        dust.spin = Random.Range(-90f, 90f);
        Transform puff = dust.renderer.transform;
        puff.SetPositionAndRotation(position, transform.rotation * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        puff.localScale = Vector3.one * startSize;
        dust.renderer.flipX = Random.value < 0.5f;
        dust.renderer.color = new Color(DustTint.r, DustTint.g, DustTint.b, alpha);
        dust.renderer.gameObject.SetActive(true);
        DuduCameraController.RegisterRuntimeRenderer(dust.renderer);
    }

    private void UpdateDust(float dt)
    {
        UpdatePool(stepDust, dt);
        UpdatePool(landDust, dt);
    }

    private void UpdatePool(Dust[] pool, float dt)
    {
        if (pool == null) return;
        float drag = Mathf.Exp(-5f * dt);
        foreach (Dust dust in pool)
        {
            GameObject puff = dust.renderer.gameObject;
            if (!puff.activeSelf) continue;
            dust.age += dt;
            float t = dust.age / dust.life;
            if (t >= 1f)
            {
                puff.SetActive(false);
                continue;
            }
            Transform tr = puff.transform;
            tr.position += dust.velocity * dt;
            dust.velocity *= drag;
            tr.rotation = tr.rotation * Quaternion.Euler(0f, 0f, dust.spin * dt);
            float grow = 1f - (1f - t) * (1f - t);
            tr.localScale = Vector3.one * Mathf.Lerp(dust.startSize, dust.endSize, grow);
            dust.renderer.color = new Color(DustTint.r, DustTint.g, DustTint.b, dust.alpha * (1f - t) * (1f - t * 0.4f));
        }
    }
}
