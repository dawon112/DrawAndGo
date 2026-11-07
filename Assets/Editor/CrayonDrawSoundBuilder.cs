#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Synthesises the crayon scratch and eraser rub variants HaruDrawFeedback plays, as plain
// WAV files in Resources/Audio/CrayonDraw and Resources/Audio/EraserRub. They are
// placeholders: drop recorded files into those folders (any name, any count) instead.
// Every run makes a new random set, so it can be run again until one sounds right.
public static class CrayonDrawSoundBuilder
{
    private const string CrayonFolder = "Assets/Resources/Audio/CrayonDraw";
    private const string EraserFolder = "Assets/Resources/Audio/EraserRub";
    private const int SampleRate = 44100;

    // How one family of sounds is made: a band of soft noise that swells and fades the
    // way a hand presses, with little irregular grains of grit on top. No regular pulse
    // anywhere: a steady repeat is what makes a scratch sound like a machine.
    private struct Recipe
    {
        public float centre;      // Hz, where the noise band sits
        public float width;       // band width, as a fraction of the centre
        public float grainsPerSecond;
        public float grainLevel;
        public float swellSeconds; // how quickly the pressure drifts
        public float top;         // Hz, everything above is softened
    }

    private static readonly Recipe Crayon = new Recipe
    {
        centre = 1700f, width = 1.3f, grainsPerSecond = 170f, grainLevel = 0.55f, swellSeconds = 0.07f, top = 5200f
    };

    private static readonly Recipe Eraser = new Recipe
    {
        centre = 650f, width = 1.1f, grainsPerSecond = 45f, grainLevel = 0.3f, swellSeconds = 0.12f, top = 2600f
    };

    [MenuItem("Tools/Draw And Go/크레파스·지우개 효과음 다시 만들기")]
    private static void BuildFromMenu()
    {
        Build(System.Environment.TickCount);
        EditorUtility.DisplayDialog("크레파스·지우개 효과음",
            "새 소리를 만들었어요 (크레파스 5개, 지우개 4개).\n플레이해서 그려 보고, 마음에 안 들면 다시 누르세요.\n" +
            "녹음한 파일을 Resources/Audio/CrayonDraw, EraserRub 폴더에 넣어 바꿀 수도 있어요.", "확인");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(CrayonFolder));
    }

    public static void Build(int seed)
    {
        System.Random random = new System.Random(seed);
        Directory.CreateDirectory(CrayonFolder);
        Directory.CreateDirectory(EraserFolder);
        for (int i = 0; i < 5; i++)
            WriteWav($"{CrayonFolder}/crayon_scratch_{i + 1}.wav", Synthesize(Crayon, 0.5f + (float)random.NextDouble() * 0.3f, random));
        for (int i = 0; i < 4; i++)
            WriteWav($"{EraserFolder}/eraser_rub_{i + 1}.wav", Synthesize(Eraser, 0.55f + (float)random.NextDouble() * 0.3f, random));
        AssetDatabase.Refresh();
        Debug.Log("Crayon and eraser sounds rebuilt.");
    }

    private static float[] Synthesize(Recipe recipe, float seconds, System.Random random)
    {
        int count = Mathf.RoundToInt(seconds * SampleRate);
        float[] samples = new float[count];
        // Each variant sits a little higher or lower.
        float centre = recipe.centre * (0.85f + (float)random.NextDouble() * 0.3f);
        Biquad band = Biquad.BandPass(centre, centre / (centre * recipe.width), SampleRate);
        Biquad grainBand = Biquad.BandPass(centre * 1.6f, 1.2f, SampleRate);
        Biquad soften = Biquad.LowPass(recipe.top, 0.7f, SampleRate);

        float pressure = 0.7f, pressureTarget = 0.7f, nextPressure = 0f;
        float grain = 0f;
        for (int i = 0; i < count; i++)
        {
            float time = i / (float)SampleRate;
            float white = (float)(random.NextDouble() * 2.0 - 1.0);

            // The hand's pressure drifts to a new level every few tens of milliseconds.
            if (time >= nextPressure)
            {
                pressureTarget = 0.45f + (float)random.NextDouble() * 0.55f;
                nextPressure = time + recipe.swellSeconds * (0.5f + (float)random.NextDouble());
            }
            pressure += (pressureTarget - pressure) * (1f / (recipe.swellSeconds * SampleRate));

            // Grit: rare, random, very short ticks (Poisson spaced, never a steady rate).
            if (random.NextDouble() < recipe.grainsPerSecond / SampleRate)
                grain = recipe.grainLevel * (0.3f + (float)random.NextDouble() * 0.7f);
            grain *= 0.992f;

            float body = band.Process(white) * pressure;
            float tick = grainBand.Process(white) * grain;
            float sample = soften.Process(body + tick);

            // Soft ends so variants follow each other without clicks.
            float edge = Mathf.Clamp01(Mathf.Min(time, seconds - time) / 0.05f);
            samples[i] = sample * edge * edge;
        }

        float peak = 0.0001f;
        foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
        for (int i = 0; i < count; i++) samples[i] = samples[i] / peak * 0.7f;
        return samples;
    }

    // RBJ cookbook two-pole filter.
    private sealed class Biquad
    {
        private float b0, b1, b2, a1, a2, x1, x2, y1, y2;

        public static Biquad BandPass(float frequency, float q, float rate)
        {
            float w = 2f * Mathf.PI * frequency / rate, alpha = Mathf.Sin(w) / (2f * q), a0 = 1f + alpha;
            return new Biquad { b0 = alpha / a0, b1 = 0f, b2 = -alpha / a0, a1 = -2f * Mathf.Cos(w) / a0, a2 = (1f - alpha) / a0 };
        }

        public static Biquad LowPass(float frequency, float q, float rate)
        {
            float w = 2f * Mathf.PI * frequency / rate, alpha = Mathf.Sin(w) / (2f * q), cos = Mathf.Cos(w), a0 = 1f + alpha;
            return new Biquad { b0 = (1f - cos) / 2f / a0, b1 = (1f - cos) / a0, b2 = (1f - cos) / 2f / a0, a1 = -2f * cos / a0, a2 = (1f - alpha) / a0 };
        }

        public float Process(float x)
        {
            float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
    }

    private static void WriteWav(string path, float[] samples)
    {
        using (BinaryWriter writer = new BinaryWriter(File.Create(path)))
        {
            int bytes = samples.Length * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + bytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(bytes);
            foreach (float sample in samples)
                writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
        }
    }
}
#endif
