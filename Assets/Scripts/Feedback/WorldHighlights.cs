using System;
using UnityEngine;

// A star or coin should be seen before the wall around it: a warm halo, a clear outline, and
// a little sparkle. Added by the pickup itself; goes away with it.
[DisallowMultipleComponent]
public sealed class StarShineFx : MonoBehaviour
{
    private static readonly Color Halo = new Color(1f, 0.72f, 0.2f);
    private static readonly Color Core = new Color(1f, 0.95f, 0.55f);
    private static readonly Color Rim = new Color(0.86f, 0.44f, 0.06f);

    private SpriteRenderer art;
    private SpriteRenderer halo, core, rim;
    private float nextSparkle;
    private float phase;

    private void Start()
    {
        art = GetComponentInChildren<SpriteRenderer>();
        Sprite glow = Resources.Load<Sprite>("UI/Hud/glow");
        if (art == null || glow == null) return;
        phase = UnityEngine.Random.value * 6f;
        float scaleBack = 1f / Mathf.Max(0.0001f, Mathf.Abs(art.transform.lossyScale.x));
        halo = NewGlow("Star Halo", glow, -3, 2.2f * scaleBack);
        core = NewGlow("Star Core", glow, -2, 1f * scaleBack);
        rim = FxArt.NewOverlay(art, "Star Rim", -1, 1.16f);
        nextSparkle = Time.time + UnityEngine.Random.value;
    }

    private SpriteRenderer NewGlow(string name, Sprite sprite, int orderOffset, float size)
    {
        GameObject holder = new GameObject(name);
        holder.layer = art.gameObject.layer;
        holder.transform.SetParent(art.transform, false);
        holder.transform.localScale = Vector3.one * size;
        SpriteRenderer renderer = holder.AddComponent<SpriteRenderer>();
        renderer.sharedMaterial = FxArt.SolidMaterial;
        renderer.sprite = sprite;
        renderer.sortingOrder = art.sortingOrder + orderOffset;
        return renderer;
    }

    private void Update()
    {
        if (art == null || halo == null) return;
        float wave = Mathf.Sin(Time.time * 2.4f + phase) * 0.5f + 0.5f;
        Color wide = Halo;
        wide.a = Mathf.Lerp(0.3f, 0.5f, wave);
        halo.color = wide;
        Color center = Core;
        center.a = Mathf.Lerp(0.55f, 0.85f, wave);
        core.color = center;
        FxArt.Sync(rim, art, new Color(Rim.r, Rim.g, Rim.b, 0.95f));

        if (Time.time < nextSparkle) return;
        nextSparkle = Time.time + UnityEngine.Random.Range(0.45f, 1f);
        Vector3 position = art.bounds.center + transform.TransformDirection(UnityEngine.Random.insideUnitCircle * 0.42f);
        FxRunner.Spawn(FxArt.Get(FxShape.Spark), position, art.transform.rotation, Vector3.up * 0.12f,
            UnityEngine.Random.Range(0.35f, 0.6f), 0.18f, 0.03f, new Color(1f, 0.97f, 0.7f), FxLayer.Front, fadeIn: true);
    }
}

// The goal: it always draws the eye a little, and clearly more once it can be used.
[DisallowMultipleComponent]
public sealed class GoalDoorFx : MonoBehaviour
{
    private static readonly Color Warm = new Color(1f, 0.8f, 0.48f);

    private StageDressing owner;
    private Func<bool> isOpen;
    private Light spot;
    private SpriteRenderer glow;
    private Vector3 center, front;
    private float height, openness, nextBit;

    public void Bind(StageDressing dressing, Func<bool> doorIsOpen)
    {
        owner = dressing;
        isOpen = doorIsOpen;
    }

    private void Start()
    {
        center = StageDressing.DoorCenter(gameObject);
        height = 3f;
        float width = 2f;
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer part in GetComponentsInChildren<Renderer>(true))
        {
            if (!found) bounds = part.bounds; else bounds.Encapsulate(part.bounds);
            found = true;
        }
        if (found) height = bounds.size.y;
        front = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (front.sqrMagnitude < 0.1f) front = Vector3.forward;
        Player3DMovement haru = FindAnyObjectByType<Player3DMovement>(FindObjectsInactive.Include);
        if (haru != null && Vector3.Dot(haru.transform.position - center, front) < 0f) front = -front;
        if (found)
        {
            Vector3 side = Vector3.Cross(Vector3.up, front).normalized;
            width = 2f * (Mathf.Abs(side.x) * bounds.extents.x + Mathf.Abs(side.z) * bounds.extents.z);
        }

        GameObject lightHolder = new GameObject("Goal Door Light");
        lightHolder.transform.SetParent(transform.root, true);
        Vector3 from = center + front * 3.2f + Vector3.up * Mathf.Max(1.6f, height * 0.5f);
        lightHolder.transform.SetPositionAndRotation(from, Quaternion.LookRotation(center - from, Vector3.up));
        spot = lightHolder.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = Warm;
        spot.range = 10f;
        spot.spotAngle = 70f;
        spot.innerSpotAngle = 30f;
        spot.shadows = LightShadows.None;
        if (owner != null) owner.RegisterRoomLight(spot);

        Sprite soft = Resources.Load<Sprite>("UI/Hud/glow");
        if (soft == null) return;
        GameObject glowHolder = new GameObject("Goal Door Glow");
        glowHolder.AddComponent<RoomOnlyDecor>();
        glowHolder.transform.SetParent(transform.root, true);
        glowHolder.transform.SetPositionAndRotation(center + front * 0.06f, FxWall.FacingAway(front));
        glowHolder.transform.localScale = new Vector3(Mathf.Max(1f, width) * 2.4f, height * 1.25f, 1f);
        glow = glowHolder.AddComponent<SpriteRenderer>();
        glow.sharedMaterial = FxArt.SolidMaterial;
        glow.sprite = soft;
        glow.sortingOrder = -4;
    }

    private void Update()
    {
        if (spot == null) return;
        bool open = isOpen != null && isOpen();
        openness = Mathf.MoveTowards(openness, open ? 1f : 0f, Time.deltaTime * 1.2f);
        float breathe = 0.92f + Mathf.Sin(Time.time * 3f) * 0.08f * (0.4f + openness);
        spot.intensity = Mathf.Lerp(16f, 42f, openness) * breathe;
        if (glow != null)
        {
            Color color = Warm;
            color.a = Mathf.Lerp(0.18f, 0.6f, openness) * breathe;
            glow.color = color;
        }
        if (openness > 0.5f && Time.time >= nextBit && spot.enabled)
        {
            nextBit = Time.time + 0.14f;
            Vector3 side = Vector3.Cross(Vector3.up, front).normalized;
            Vector3 at = center + front * 0.25f + side * UnityEngine.Random.Range(-1.2f, 1.2f) + Vector3.up * UnityEngine.Random.Range(-height * 0.45f, height * 0.4f);
            FxRunner.Spawn(FxArt.Get(FxShape.Spark), at, FxWall.FacingAway(front), Vector3.up * UnityEngine.Random.Range(0.1f, 0.35f),
                UnityEngine.Random.Range(0.6f, 1.1f), 0.14f, 0.04f, new Color(1f, 0.92f, 0.65f), FxLayer.Top, fadeIn: true);
        }
    }
}
