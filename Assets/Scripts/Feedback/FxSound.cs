using System;
using System.Collections.Generic;
using UnityEngine;

public enum FxClip
{
    Charge, BoltLoop, Crack, Pop, Tok, Thud, Pulse, Knock, Whoosh, WindLoop, Gust, SizzleLoop,
    Crumble, Fail, Clack, Reverse, DizzyLoop, ReleasePuff, Boom, SlowLoop, Disperse,
    MagnetLoop, Snap, Rumble, Swish, Rub, StarPick, Chime
}

// Hazard sounds. They are synthesised placeholders: to replace one, put a recorded clip named
// after it in Resources/Audio/Hazards (e.g. Audio/Hazards/Crack) and it is used instead.
public static class FxSound
{
    private const int Rate = 44100;
    private static readonly Dictionary<FxClip, AudioClip> Clips = new Dictionary<FxClip, AudioClip>();
    private static readonly Dictionary<FxClip, float> LastPlayed = new Dictionary<FxClip, float>();

    public static AudioClip Get(FxClip id)
    {
        if (Clips.TryGetValue(id, out AudioClip clip) && clip != null) return clip;
        clip = Resources.Load<AudioClip>("Audio/Hazards/" + id);
        if (clip == null) clip = Synthesize(id);
        Clips[id] = clip;
        return clip;
    }

    // Plays once, quieter the further it is from whoever this PC is watching.
    public static void Play(FxClip id, Vector3 position, float volume = 1f, float pitchJitter = 0.06f)
    {
        float scaled = volume * FxView.Attenuation(position);
        if (scaled <= 0.01f) return;
        if (LastPlayed.TryGetValue(id, out float last) && Time.unscaledTime - last < 0.05f && last <= Time.unscaledTime) return;
        LastPlayed[id] = Time.unscaledTime;
        FxRunner.Instance.PlayOneShot(Get(id), scaled * CoopAudioSettings.SfxVolume,
            1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter));
    }

    // A looping voice on its owner: it stops with the owner. Starts silent; drive with SetLoop.
    public static AudioSource AddLoop(GameObject owner, FxClip id)
    {
        AudioSource source = owner.AddComponent<AudioSource>();
        source.clip = Get(id);
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = 0f;
        return source;
    }

    public static void SetLoop(AudioSource source, float volume, float pitch = 1f)
    {
        if (source == null) return;
        float target = volume * CoopAudioSettings.SfxVolume;
        source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * 1.5f);
        source.pitch = pitch;
        if (source.volume > 0.003f)
        {
            if (!source.isPlaying) source.Play();
        }
        else if (source.isPlaying) source.Stop();
    }

    private static AudioClip Synthesize(FxClip id)
    {
        System.Random random = new System.Random((int)id * 97 + 11);
        float White() => (float)(random.NextDouble() * 2.0 - 1.0);
        float low = 0f, low2 = 0f, phase = 0f, phase2 = 0f, tick = 0f;

        // One-pole low-pass of white noise; the coefficient sets how dull it is.
        float Dull(float amount) { low += (White() - low) * amount; return low; }
        float Bright(float amount) { float n = White(); low2 += (n - low2) * amount; return n - low2; }
        float Tone(float hz) { phase += 2f * Mathf.PI * hz / Rate; return Mathf.Sin(phase); }
        float Tone2(float hz) { phase2 += 2f * Mathf.PI * hz / Rate; return Mathf.Sin(phase2); }
        // Irregular little ticks, never a steady rate.
        float Grit(float perSecond, float decay)
        {
            if (random.NextDouble() < perSecond / Rate) tick = 0.4f + (float)random.NextDouble() * 0.6f;
            tick *= decay;
            return tick;
        }

        switch (id)
        {
            case FxClip.Charge:
                return Bake(id, 0.48f, false, t => (Tone(Mathf.Lerp(160f, 820f, t / 0.48f)) * 0.5f + Bright(0.2f) * Grit(140f, 0.993f) * 0.8f) * Mathf.Pow(t / 0.48f, 1.4f));
            case FxClip.BoltLoop:
                return Bake(id, 1.2f, true, t => Bright(0.35f) * (0.14f + Grit(55f, 0.996f) * 0.75f));
            case FxClip.Crack:
                return Bake(id, 0.26f, false, t => (Bright(0.08f) * (Mathf.Exp(-30f * t) + Grit(180f, 0.99f) * 0.5f) + Tone(Mathf.Lerp(1500f, 200f, t / 0.26f)) * 0.35f) * Mathf.Exp(-9f * t));
            case FxClip.Pop:
                return Bake(id, 0.2f, false, t => (Dull(0.4f) * 1.3f + Tone(Mathf.Lerp(600f, 90f, t / 0.2f)) * 0.5f) * Mathf.Exp(-22f * t));
            case FxClip.Tok:
                return Bake(id, 0.09f, false, t => (Tone(Mathf.Lerp(340f, 170f, t / 0.09f)) + Dull(0.5f) * 0.4f) * Mathf.Exp(-55f * t));
            case FxClip.Thud:
                return Bake(id, 0.2f, false, t => (Tone(Mathf.Lerp(130f, 58f, t / 0.2f)) + Dull(0.15f) * 0.6f) * Mathf.Exp(-20f * t));
            case FxClip.Pulse:
                return Bake(id, 0.55f, false, t => (Tone(Mathf.Lerp(70f, 135f, t / 0.55f)) + Tone2(Mathf.Lerp(141f, 271f, t / 0.55f)) * 0.3f) * Mathf.Sin(Mathf.PI * t / 0.55f));
            case FxClip.Knock:
                return Bake(id, 0.24f, false, t => (Tone(Mathf.Lerp(220f, 66f, t / 0.24f)) * 0.9f + Dull(0.25f) * Mathf.Exp(-40f * t) * 1.3f) * Mathf.Exp(-14f * t));
            case FxClip.Whoosh:
                return Bake(id, 0.34f, false, t => Bright(Mathf.Lerp(0.03f, 0.35f, t / 0.34f)) * Mathf.Pow(Mathf.Sin(Mathf.PI * t / 0.34f), 2f));
            case FxClip.Swish:
                return Bake(id, 0.2f, false, t => Bright(Mathf.Lerp(0.5f, 0.05f, t / 0.2f)) * Mathf.Pow(Mathf.Sin(Mathf.PI * t / 0.2f), 2f));
            case FxClip.WindLoop:
                return Bake(id, 3f, true, t => Dull(0.035f + 0.02f * Mathf.Sin(t * 1.9f)) * (0.7f + 0.3f * Mathf.Sin(t * 2.7f + 1f)));
            case FxClip.Gust:
                return Bake(id, 0.75f, false, t => Dull(Mathf.Lerp(0.02f, 0.14f, Mathf.Sin(Mathf.PI * t / 0.75f))) * Mathf.Sin(Mathf.PI * t / 0.75f));
            case FxClip.SizzleLoop:
                return Bake(id, 1.2f, true, t => Bright(0.5f) * (0.1f + Grit(120f, 0.985f)));
            case FxClip.Crumble:
                return Bake(id, 0.4f, false, t => Bright(0.2f) * Grit(Mathf.Lerp(260f, 40f, t / 0.4f), 0.985f) * (1f - t / 0.4f));
            case FxClip.Fail:
                return Bake(id, 0.1f, false, t => Mathf.Sign(Tone(Mathf.Lerp(880f, 520f, t / 0.1f))) * 0.5f * Mathf.Exp(-34f * t));
            case FxClip.Clack:
                return Bake(id, 0.07f, false, t => (Tone(240f) * 0.8f + Bright(0.2f) * 0.5f) * Mathf.Exp(-70f * t));
            case FxClip.Reverse:
                return Bake(id, 0.38f, false, t => Tone(Mathf.Lerp(300f, 880f, Mathf.Sqrt(t / 0.38f)) + Mathf.Sin(t * 70f) * 40f) * Mathf.Sin(Mathf.PI * Mathf.Pow(t / 0.38f, 0.6f)));
            case FxClip.DizzyLoop:
                return Bake(id, 3f, true, t => (Tone(220f + Mathf.Sin(t * 2.1f) * 3f) + Tone2(331f + Mathf.Sin(t * 1.3f) * 4f) * 0.6f) * (0.6f + 0.4f * Mathf.Sin(t * 2.094f)));
            case FxClip.ReleasePuff:
                return Bake(id, 0.32f, false, t => Bright(Mathf.Lerp(0.4f, 0.06f, t / 0.32f)) * Mathf.Exp(-9f * t));
            case FxClip.Boom:
                return Bake(id, 0.5f, false, t => (Tone(Mathf.Lerp(92f, 44f, t / 0.5f)) + Dull(0.06f) * Mathf.Exp(-30f * t)) * Mathf.Exp(-7f * t));
            case FxClip.SlowLoop:
                return Bake(id, 3f, true, t => (Tone(55f) + Tone2(82.5f) * 0.5f) * (0.7f + 0.3f * Mathf.Sin(t * 2.094f)));
            case FxClip.Disperse:
                return Bake(id, 0.55f, false, t => Dull(0.3f) * Mathf.Sin(Mathf.PI * Mathf.Pow(t / 0.55f, 0.5f)));
            case FxClip.MagnetLoop:
                return Bake(id, 2f, true, t => Tone(60f) + Tone2(121f + Mathf.Sin(t * 6.283f) * 2f) * 0.45f + Bright(0.6f) * 0.02f * (1f + Mathf.Sin(t * 37.7f)));
            case FxClip.Snap:
                return Bake(id, 0.11f, false, t => (Bright(0.1f) + Tone(1900f) * 0.4f) * Mathf.Exp(-45f * t));
            case FxClip.StarPick:
                return Bake(id, 0.32f, false, t => (Tone(t < 0.08f ? 1320f : 1760f) + Tone2(t < 0.08f ? 2640f : 3520f) * 0.25f) * Mathf.Exp(-11f * t));
            case FxClip.Chime:
                return Bake(id, 0.85f, false, t =>
                {
                    float note = t < 0.13f ? 784f : t < 0.26f ? 988f : t < 0.39f ? 1175f : 1568f;
                    return (Tone(note) + Tone2(note * 2f) * 0.2f) * Mathf.Exp(-3.6f * Mathf.Max(0f, t - 0.39f));
                });
            case FxClip.Rumble:
                return Bake(id, 0.7f, false, t => Dull(0.12f) * (0.55f + 0.45f * Mathf.Sin(t * 170f + Mathf.Sin(t * 41f) * 3f)) * Mathf.Sin(Mathf.PI * t / 0.7f));
            default:
                return Bake(id, 0.22f, false, t => Bright(0.25f) * (0.3f + Grit(90f, 0.99f)) * Mathf.Sin(Mathf.PI * t / 0.22f));
        }
    }

    private static AudioClip Bake(FxClip id, float seconds, bool loop, Func<float, float> voice)
    {
        const float Overlap = 0.25f;
        int count = Mathf.CeilToInt(seconds * Rate);
        int extra = loop ? Mathf.CeilToInt(Overlap * Rate) : 0;
        float[] raw = new float[count + extra];
        for (int i = 0; i < raw.Length; i++) raw[i] = voice(i / (float)Rate);

        float[] samples = new float[count];
        Array.Copy(raw, samples, count);
        if (loop)
        {
            // Fade the extra tail over the start so the loop has no seam.
            for (int i = 0; i < extra; i++)
            {
                float mix = i / (float)extra;
                samples[i] = raw[i] * mix + raw[count + i] * (1f - mix);
            }
        }
        else
        {
            int edge = Mathf.Min(count / 4, Mathf.CeilToInt(0.003f * Rate));
            for (int i = 0; i < edge; i++)
            {
                float fade = i / (float)edge;
                samples[i] *= fade;
                samples[count - 1 - i] *= fade;
            }
        }
        float peak = 0.0001f;
        foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
        for (int i = 0; i < count; i++) samples[i] = samples[i] / peak * 0.8f;

        AudioClip clip = AudioClip.Create("Fx " + id, count, 1, Rate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
