using UnityEngine;

// The little hand-drawn icons that drift out of the zones (zone_hot_icon, zone_locked_icon),
// kept in Resources/Fx/ZoneIconArt so the zone effects can find them at run time.
[CreateAssetMenu(menuName = "Follow My Line/Zone Icon Art", fileName = "ZoneIconArt")]
public sealed class ZoneIconArt : ScriptableObject
{
    [Tooltip("zone_hot_icon's frames: the flame that rises out of a hot zone.")]
    public Sprite[] hot;
    [Tooltip("zone_locked_icon's frames: the padlock that spreads out of a no-draw zone.")]
    public Sprite[] locked;

    private static ZoneIconArt cached;
    private static bool looked;

    public static ZoneIconArt Load()
    {
        if (!looked)
        {
            cached = Resources.Load<ZoneIconArt>("Fx/ZoneIconArt");
            looked = true;
        }
        return cached;
    }

    // Spreads the zone's icon out across it, softly: now and then one drifts out from near the
    // middle and fades, and every few seconds a large faint one swells over the whole zone, like a
    // ripple. Each zone keeps its own few sprites (the shared effect pool is busy with heat and
    // embers in a stage full of zones). Called every frame by the zone's effect.
    public sealed class Pulse
    {
        private const int Sprites = 4;
        private const int SortingOrder = 7;

        private sealed class Icon
        {
            public SpriteRenderer renderer;
            public Vector3 velocity;
            public float age, life, sizeFrom, sizeTo, alpha;
        }

        private readonly float driftEvery, rippleEvery;
        private readonly Icon[] icons = new Icon[Sprites];
        private float nextDrift = -1f, nextRipple;
        private Transform owner;

        // Made as a field of a MonoBehaviour, so no clock or random numbers here.
        public Pulse(float driftEvery, float rippleEvery)
        {
            this.driftEvery = driftEvery;
            this.rippleEvery = rippleEvery;
        }

        public void Update(Sprite[] frames, DuduSurface surface, Rect rect, Color color, float rise, Transform zone)
        {
            if (frames == null || frames.Length == 0 || surface == null) return;
            float now = Time.time;
            if (nextDrift < 0f)
            {
                owner = zone;
                // Zones side by side do not pulse in step.
                nextDrift = now + Random.Range(0f, driftEvery);
                nextRipple = now + Random.Range(0.3f, rippleEvery);
            }
            Animate(color);

            Sprite sprite = frames[Random.Range(0, frames.Length)];
            if (sprite == null) return;
            // Sizes are in the icon's own height: one icon fills this much of the zone.
            float iconHeight = Mathf.Max(0.01f, sprite.bounds.size.y);
            float zoneSize = Mathf.Min(rect.width, rect.height);
            Vector3 right = surface.Right.normalized, up = surface.Up.normalized;

            if (now >= nextDrift)
            {
                nextDrift = now + driftEvery * Random.Range(0.75f, 1.25f);
                Vector2 from = rect.center + Random.insideUnitCircle * zoneSize * 0.15f;
                Vector2 away = Random.insideUnitCircle.normalized;
                float size = zoneSize * Random.Range(0.18f, 0.26f) / iconHeight;
                Launch(sprite, FxWall.Point(surface, from), FxWall.Rotation(surface, Random.Range(-10f, 10f)),
                    (right * away.x + up * away.y) * Random.Range(0.35f, 0.6f) + up * rise,
                    Random.Range(2f, 2.6f), size * 0.7f, size * 1.25f, 1f);
            }
            if (now >= nextRipple)
            {
                nextRipple = now + rippleEvery * Random.Range(0.85f, 1.15f);
                float size = zoneSize * 0.55f / iconHeight;
                Launch(sprite, FxWall.Point(surface, rect.center, FxWall.Depth - 0.005f), FxWall.Rotation(surface),
                    up * rise * 0.3f, 2.8f, size * 0.6f, size * 1.5f, 0.4f);
            }
        }

        public void Hide()
        {
            foreach (Icon icon in icons)
                if (icon != null && icon.renderer != null) icon.renderer.enabled = false;
        }

        public void Release()
        {
            foreach (Icon icon in icons)
                if (icon != null && icon.renderer != null) Object.Destroy(icon.renderer.gameObject);
        }

        private void Launch(Sprite sprite, Vector3 position, Quaternion rotation, Vector3 velocity,
            float life, float sizeFrom, float sizeTo, float alpha)
        {
            // A free sprite, or else the one nearest its end.
            Icon pick = null;
            for (int i = 0; i < icons.Length; i++)
            {
                if (icons[i] == null) icons[i] = Create(i);
                if (!icons[i].renderer.enabled) { pick = icons[i]; break; }
                if (pick == null || icons[i].age / icons[i].life > pick.age / pick.life) pick = icons[i];
            }
            pick.renderer.sprite = sprite;
            pick.renderer.transform.SetPositionAndRotation(position, rotation);
            pick.velocity = velocity;
            pick.age = 0f;
            pick.life = life;
            pick.sizeFrom = sizeFrom;
            pick.sizeTo = sizeTo;
            pick.alpha = alpha;
            pick.renderer.color = Color.clear;
            pick.renderer.enabled = true;
        }

        private Icon Create(int index)
        {
            // Not under the zone: zones are scaled to their size, and the icons keep their own.
            GameObject holder = new GameObject("Zone Icon " + index);
            if (owner != null) holder.transform.SetParent(FxRunner.Instance.transform, false);
            SpriteRenderer renderer = holder.AddComponent<SpriteRenderer>();
            // The icons keep their own colours (the flame is orange): the zone's own sprite
            // material, not the effects' one-colour one.
            SpriteRenderer zoneArt = owner != null ? owner.GetComponent<SpriteRenderer>() : null;
            if (zoneArt != null && zoneArt.sharedMaterial != null) renderer.sharedMaterial = zoneArt.sharedMaterial;
            renderer.sortingOrder = SortingOrder;
            renderer.enabled = false;
            // Dudu sees it on his page too.
            DuduCameraController.RegisterRuntimeRenderer(renderer);
            return new Icon { renderer = renderer };
        }

        // Grow out, drift, fade in quickly and out slowly.
        private void Animate(Color color)
        {
            float dt = Time.deltaTime;
            foreach (Icon icon in icons)
            {
                if (icon == null || icon.renderer == null || !icon.renderer.enabled) continue;
                icon.age += dt;
                float t = icon.age / icon.life;
                if (t >= 1f) { icon.renderer.enabled = false; continue; }
                Transform tr = icon.renderer.transform;
                tr.position += icon.velocity * dt;
                float grow = 1f - (1f - t) * (1f - t);
                float size = Mathf.Lerp(icon.sizeFrom, icon.sizeTo, grow);
                tr.localScale = new Vector3(size, size, 1f);
                Color c = color;
                c.a = color.a * icon.alpha * Mathf.Min(t * 5f, 1f) * (1f - t);
                icon.renderer.color = c;
            }
        }
    }
}
