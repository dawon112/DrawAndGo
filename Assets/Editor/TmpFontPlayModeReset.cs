#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;

// Dynamic TextMeshPro fonts add every character they meet to the font asset itself. In the
// editor that survives play mode, and over many sessions the atlas ended up with leftovers and
// damaged text. So after each play session they go back to empty, exactly as a build starts:
// the atlas fills again with what is on screen. This also keeps the font files out of commits.
[InitializeOnLoad]
public static class TmpFontPlayModeReset
{
    static TmpFontPlayModeReset()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode) return;
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/Resources/Fonts" }))
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (font == null || font.atlasPopulationMode != AtlasPopulationMode.Dynamic) continue;
            font.ClearFontAssetData(true);
            AssetDatabase.SaveAssetIfDirty(font);
        }
    }
}
#endif
