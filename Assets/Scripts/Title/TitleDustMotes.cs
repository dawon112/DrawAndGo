using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Specks of dust drifting slowly in the afternoon sun on the title screen. Many gather in the
// sunny corridor, where they show against the wood; they rise and sway a little,
// glint now and then, and fade in and out so none of them pops. A few large, very faint
// out-of-focus motes float nearer the eye. Sits over the background, under the menu.
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class TitleDustMotes : MonoBehaviour
{
    [Min(0)] public int specks = 46;
    [Min(0)] public int blurredMotes = 7;
    public Color tint = new Color(1f, 0.86f, 0.58f, 1f);
    [Range(0f, 1f)] public float brightness = 0.8f;
    [Tooltip("The sunny corridor and floor, in parts of this rect (0-1): half the motes gather there, " +
        "where they glint against the darker wood; the rest drift anywhere.")]
    public Rect sunlit = new Rect(0.6f, 0.05f, 0.4f, 0.85f);

    private sealed class Mote
    {
        public RectTransform rect;
        public Image image;
        public Vector2 position, velocity;
        public float size, alpha, age, life, swayPhase, swaySpeed, swayAmount, twinklePhase;
        public bool blurred;
    }

    private readonly List<Mote> motes = new List<Mote>();
    private RectTransform area;
    private Sprite glow;
    private bool scattered;

    private void Awake()
    {
        area = (RectTransform)transform;
        glow = Resources.Load<Sprite>("UI/Hud/glow");
        for (int i = 0; i < specks + blurredMotes; i++)
        {
            Mote mote = Create(i >= specks);
            mote.image.color = Color.clear;
            motes.Add(mote);
        }
    }

    private Mote Create(bool blurred)
    {
        GameObject holder = new GameObject(blurred ? "Blurred Mote" : "Dust Speck", typeof(RectTransform), typeof(Image));
        holder.transform.SetParent(transform, false);
        Image image = holder.GetComponent<Image>();
        image.sprite = glow;
        image.raycastTarget = false;
        RectTransform rect = (RectTransform)holder.transform;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        return new Mote { rect = rect, image = image, blurred = blurred };
    }

    private void Respawn(Mote mote, bool anywhereInLife)
    {
        Rect bounds = area.rect;
        // Half gather in the sunny corridor; the rest drift anywhere, so the wall has some too.
        Rect zone = Random.value < 0.5f ? sunlit : new Rect(0f, 0f, 1f, 1f);
        mote.position = new Vector2(
            bounds.width * Random.Range(zone.xMin, zone.xMax),
            bounds.height * Random.Range(zone.yMin, zone.yMax));
        mote.size = mote.blurred ? Random.Range(40f, 95f) : Random.Range(7f, 18f);
        mote.alpha = mote.blurred ? Random.Range(0.08f, 0.16f) : Random.Range(0.45f, 1f);
        // Warm air rises, and drifts a touch toward the open corridor.
        float speed = mote.blurred ? Random.Range(4f, 9f) : Random.Range(6f, 18f);
        mote.velocity = new Vector2(Random.Range(-0.6f, 0.35f), Random.Range(0.25f, 1f)).normalized * speed;
        mote.swayPhase = Random.Range(0f, Mathf.PI * 2f);
        mote.swaySpeed = Random.Range(0.25f, 0.7f);
        mote.swayAmount = Random.Range(6f, 22f);
        mote.twinklePhase = Random.Range(0f, Mathf.PI * 2f);
        mote.life = Random.Range(9f, 18f);
        mote.age = anywhereInLife ? Random.Range(0f, mote.life) : 0f;
        mote.rect.sizeDelta = Vector2.one * mote.size;
    }

    private void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        float now = Time.unscaledTime;
        Rect bounds = area.rect;
        // The background sizes itself to the screen after Awake: scatter once it has a size.
        if (!scattered)
        {
            if (bounds.width < 1f) return;
            foreach (Mote mote in motes) Respawn(mote, true);
            scattered = true;
        }
        foreach (Mote mote in motes)
        {
            mote.age += dt;
            mote.position += mote.velocity * dt;
            if (mote.age >= mote.life || mote.position.y > bounds.height + 40f || mote.position.x < -40f || mote.position.x > bounds.width + 40f)
                Respawn(mote, false);

            float sway = Mathf.Sin(now * mote.swaySpeed + mote.swayPhase) * mote.swayAmount;
            mote.rect.anchoredPosition = mote.position + new Vector2(sway, sway * 0.35f);

            // Fade in, hold, fade out; specks glint as they turn in the light.
            float t = mote.age / mote.life;
            float envelope = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.2f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - t) / 0.25f));
            float glint = mote.blurred ? 1f : 0.65f + 0.35f * Mathf.Sin(now * 1.7f + mote.twinklePhase);
            Color color = tint;
            color.a = mote.alpha * envelope * glint * brightness * 2f;
            mote.image.color = color;
        }
    }
}
