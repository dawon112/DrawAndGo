#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// The chevron floor has no colour map, only a normal map, so every plank is the same tone. This
// finds the planks in the normal map (the seams between them are its strongest edges), gives each
// its own slight tone and writes that as the material's colour map. Run once; re-run to reroll.
public static class FloorPlankToneBaker
{
    private const string MaterialPath = "Assets/Stylized Wood Textures/Materials/Chevron/Chevron.mat";
    private const string OutputPath = "Assets/Stylized Wood Textures/Materials/Chevron/Chevron_PlankTone.png";

    [MenuItem("Tools/Draw And Go/바닥 판자 색 변화 굽기")]
    public static void Bake()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Texture normal = material != null ? material.GetTexture("_BumpMap") : null;
        if (normal == null)
        {
            Debug.LogError("Chevron material or its normal map was not found.");
            return;
        }

        const int Size = 1024;
        RenderTexture temporary = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(normal, temporary);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = temporary;
        Texture2D read = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
        read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        read.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(temporary);
        Color32[] source = read.GetPixels32();
        Object.DestroyImmediate(read);

        // How steep the surface is at each pixel. Normal maps may hold x in red or alpha.
        // A channel that never changes (solid alpha, or solid red in the other layout) holds nothing.
        byte redLow = 255, redHigh = 0, alphaLow = 255, alphaHigh = 0;
        foreach (Color32 pixel in source)
        {
            redLow = (byte)Mathf.Min(redLow, pixel.r); redHigh = (byte)Mathf.Max(redHigh, pixel.r);
            alphaLow = (byte)Mathf.Min(alphaLow, pixel.a); alphaHigh = (byte)Mathf.Max(alphaHigh, pixel.a);
        }
        bool useAlpha = alphaHigh - alphaLow > redHigh - redLow;
        float[] steep = new float[Size * Size];
        for (int i = 0; i < source.Length; i++)
        {
            float x = Mathf.Abs((useAlpha ? source[i].a : source[i].r) / 255f - 0.5f), y = Mathf.Abs(source[i].g / 255f - 0.5f);
            steep[i] = Mathf.Max(x, y);
        }

        // Seams: the steepest part of the map, closed up a little so planks don't leak together.
        List<float> sorted = new List<float>(steep);
        sorted.Sort();
        float cut = sorted[(int)(sorted.Count * 0.88f)];
        bool[] seam = new bool[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            if (steep[y * Size + x] < cut) continue;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                seam[Wrap(y + dy, Size) * Size + Wrap(x + dx, Size)] = true;
        }

        // Each connected area between seams is a plank; tiny specks of grain are not.
        int[] label = new int[Size * Size];
        List<float> tones = new List<float> { 1f };
        Stack<int> stack = new Stack<int>();
        System.Random random = new System.Random(20261109);
        for (int start = 0; start < label.Length; start++)
        {
            if (seam[start] || label[start] != 0) continue;
            int id = tones.Count;
            int pixels = 0;
            stack.Push(start);
            label[start] = id;
            while (stack.Count > 0)
            {
                int at = stack.Pop();
                pixels++;
                int px = at % Size, py = at / Size;
                Visit(Wrap(px + 1, Size) + py * Size, id, label, seam, stack);
                Visit(Wrap(px - 1, Size) + py * Size, id, label, seam, stack);
                Visit(px + Wrap(py + 1, Size) * Size, id, label, seam, stack);
                Visit(px + Wrap(py - 1, Size) * Size, id, label, seam, stack);
            }
            tones.Add(pixels < 400 ? 1f : 0.82f + (float)random.NextDouble() * 0.2f);
        }

        Color32[] pixelsOut = new Color32[Size * Size];
        for (int i = 0; i < pixelsOut.Length; i++)
        {
            float tone = label[i] == 0 ? NearestTone(i, Size, label, tones) : tones[label[i]];
            // A little warmer where darker, so it never turns grey.
            byte r = (byte)(255f * Mathf.Clamp01(tone));
            byte g = (byte)(255f * Mathf.Clamp01(tone * tone * 0.98f + 0.02f));
            byte b = (byte)(255f * Mathf.Clamp01(tone * tone * tone * 0.96f + 0.04f));
            pixelsOut[i] = new Color32(r, g, b, 255);
        }
        Texture2D output = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        output.SetPixels32(pixelsOut);
        output.Apply();
        File.WriteAllBytes(OutputPath, output.EncodeToPNG());
        Object.DestroyImmediate(output);

        AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(OutputPath));
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        Debug.Log($"Floor plank tones baked: {tones.Count - 1} areas.");
    }

    private static int Wrap(int value, int size) => (value % size + size) % size;

    private static void Visit(int index, int id, int[] label, bool[] seam, Stack<int> stack)
    {
        if (seam[index] || label[index] != 0) return;
        label[index] = id;
        stack.Push(index);
    }

    // Seam pixels take a neighbouring plank's tone; the normal map already draws the seam itself.
    private static float NearestTone(int index, int size, int[] label, List<float> tones)
    {
        int x = index % size, y = index / size;
        for (int reach = 1; reach < 6; reach++)
        {
            int right = label[y * size + Wrap(x + reach, size)];
            if (right != 0) return tones[right];
            int up = label[Wrap(y + reach, size) * size + x];
            if (up != 0) return tones[up];
        }
        return 1f;
    }
}
#endif
