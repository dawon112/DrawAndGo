using UnityEngine;

public static class GameFont
{
    private static Font bold;
    private static Font light;

    public static Font Bold => bold != null
        ? bold
        : bold = Resources.Load<Font>("Fonts/HakgyoansimNadeuri-Bold");

    public static Font Light => light != null
        ? light
        : light = Resources.Load<Font>("Fonts/HakgyoansimNadeuri-Light");

    public static void Apply(TextMesh text, bool useBold = true)
    {
        if (text == null) return;
        Font font = useBold ? Bold : Light;
        if (font == null) return;
        text.font = font;
        text.fontStyle = FontStyle.Normal;
        MeshRenderer renderer = text.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.sharedMaterial = font.material;
    }
}
