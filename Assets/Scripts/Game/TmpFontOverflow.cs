using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// Korean needs far more glyphs than the TextMeshPro font assets' single 1024 atlas pages can
// hold (at 90pt they fill up after about 150 characters), and a character that no longer fits
// shows as a box. So every dynamic font gets a runtime-only overflow font from the same .ttf:
// lower sampling size, larger pages, and more pages as needed. It never touches the asset files.
public static class TmpFontOverflow
{
    private const int SamplingSize = 64;
    private const int Padding = 6;
    private const int PageSize = 2048;

    private static readonly List<KeyValuePair<TMP_FontAsset, TMP_FontAsset>> Installed =
        new List<KeyValuePair<TMP_FontAsset, TMP_FontAsset>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        Installed.Clear();
        foreach (TMP_FontAsset font in Resources.LoadAll<TMP_FontAsset>("Fonts"))
        {
            if (font.atlasPopulationMode != AtlasPopulationMode.Dynamic || font.sourceFontFile == null) continue;
            TMP_FontAsset overflow = TMP_FontAsset.CreateFontAsset(font.sourceFontFile, SamplingSize, Padding,
                GlyphRenderMode.SDFAA, PageSize, PageSize, AtlasPopulationMode.Dynamic, true);
            if (overflow == null)
            {
                Debug.LogError($"Could not make an overflow font for {font.name}.");
                continue;
            }
            overflow.name = font.name + " (overflow)";
            if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            font.fallbackFontAssetTable.Add(overflow);
            Installed.Add(new KeyValuePair<TMP_FontAsset, TMP_FontAsset>(font, overflow));
        }
        Application.quitting -= Uninstall;
        Application.quitting += Uninstall;
    }

    // In the editor the font assets outlive play mode: leave no reference to the runtime font.
    private static void Uninstall()
    {
        foreach (KeyValuePair<TMP_FontAsset, TMP_FontAsset> pair in Installed)
            if (pair.Key != null && pair.Key.fallbackFontAssetTable != null)
                pair.Key.fallbackFontAssetTable.Remove(pair.Value);
        Installed.Clear();
    }
}
