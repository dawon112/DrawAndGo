#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Makes the paper-and-crayon HUD art (Resources/UI/Hud/*.png) and the prefabs that use it:
// GameHud, TutorialHint, TutorialSpeechBubble and KeyPrompt, and lays out the comms buttons.
// After a run they are ordinary assets: edit the prefabs or draw over the PNGs freely. Run the
// menu again only to start over from these defaults: it replaces the prefabs.
public static class GameHudBuilder
{
    private const string ArtFolder = "Assets/Resources/UI/Hud";
    private const string PrefabFolder = "Assets/Resources/UI";
    private const string FontPath = "Assets/Resources/Fonts/HakgyoansimNadeuri-Bold SDF.asset";

    private static readonly Color Paper = new Color(0.99f, 0.95f, 0.86f);
    private static readonly Color Brown = new Color(0.36f, 0.23f, 0.14f);
    private static readonly Color DarkBrown = new Color(0.24f, 0.16f, 0.11f);
    private static readonly Color Orange = new Color(0.9f, 0.46f, 0.12f);
    private static readonly Color Yellow = new Color(1f, 0.82f, 0.2f);

    [MenuItem("Tools/Draw And Go/HUD·말풍선 프리팹 다시 만들기")]
    private static void BuildFromMenu()
    {
        Build();
        EditorUtility.DisplayDialog("HUD", "HUD 그림과 프리팹을 새로 만들었어요.\nResources/UI 폴더의 GameHud, TutorialHint, TutorialSpeechBubble, KeyPrompt 를 편집할 수 있어요.", "확인");
    }

    public static void Build()
    {
        Directory.CreateDirectory(ArtFolder);
        WriteArt();
        AssetDatabase.Refresh();
        ImportArt();

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        KeyPromptUI key = BuildKeyPrompt(font);
        BuildHint(font, key);
        BuildBubble(font, key);
        BuildGameHud(font);
        LayOutComms();
        AssetDatabase.SaveAssets();
        Debug.Log("HUD art and prefabs rebuilt.");
    }

    // --- Art ---------------------------------------------------------------------------

    private static void WriteArt()
    {
        Save("panel_paper", Panel(128, 44f, Paper, Brown, 5f, 1f));
        Save("panel_dark", Panel(128, 44f, new Color(DarkBrown.r, DarkBrown.g, DarkBrown.b, 0.88f), new Color(0.72f, 0.54f, 0.34f), 4f, 0.9f));
        Save("key_cap", KeyCap(96));
        Save("star", Star(128));
        Save("stopwatch", Stopwatch(128));
        Save("check", Check(128));
        Save("bubble_tail", Tail(64, false));
        Save("bubble_tail_accent", Tail(64, true));
        Save("line_crayon", CrayonLine(128, 16));
        Save("glow", Glow(128));
    }

    private static float Noise(float x, float y, float scale) => Mathf.PerlinNoise(x * scale + 11.3f, y * scale + 5.7f);

    private static Color Blend(Color under, Color over)
    {
        float a = over.a + under.a * (1f - over.a);
        if (a <= 0f) return Color.clear;
        Color result = (over * over.a + under * under.a * (1f - over.a)) / a;
        result.a = a;
        return result;
    }

    private static float RoundedBox(float x, float y, float halfW, float halfH, float radius)
    {
        float qx = Mathf.Abs(x) - halfW + radius, qy = Mathf.Abs(y) - halfH + radius;
        return Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) +
            Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    private static Color[] Panel(int size, float radius, Color fill, Color edge, float edgeWidth, float edgeAlpha)
    {
        Color[] pixels = new Color[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float wobble = (Noise(x, y, 0.13f) - 0.5f) * 2.4f;
            float d = RoundedBox(x + 0.5f - half, y + 0.5f - half, half - 3f, half - 3f, radius) + wobble;
            float inside = Mathf.Clamp01(0.5f - d);
            float grain = 0.97f + 0.05f * Noise(x, y, 0.5f);
            Color color = new Color(fill.r * grain, fill.g * grain, fill.b * grain, fill.a * inside);
            float band = Mathf.Clamp01(edgeWidth - Mathf.Abs(d + edgeWidth * 0.5f));
            float wax = Mathf.Lerp(0.55f, 1f, Noise(x, y, 0.55f));
            pixels[y * size + x] = Blend(color, new Color(edge.r, edge.g, edge.b, band * edgeAlpha * wax));
        }
        return pixels;
    }

    private static Color[] KeyCap(int size)
    {
        Color[] pixels = new Color[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float wobble = (Noise(x, y, 0.16f) - 0.5f) * 1.8f;
            float d = RoundedBox(x + 0.5f - half, y + 0.5f - half, half - 2f, half - 2f, 18f) + wobble;
            float inside = Mathf.Clamp01(0.5f - d);
            // The lower lip of a key.
            float lip = y < 12 ? 0.8f : 1f;
            Color color = new Color(0.99f * lip, 0.93f * lip, 0.78f * lip, inside);
            float band = Mathf.Clamp01(3.2f - Mathf.Abs(d + 1.6f));
            pixels[y * size + x] = Blend(color, new Color(Brown.r, Brown.g, Brown.b, band));
        }
        return pixels;
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }

    private static bool InPolygon(Vector2 p, IList<Vector2> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                inside = !inside;
        return inside;
    }

    private static Color[] Star(int size)
    {
        List<Vector2> points = new List<Vector2>();
        Vector2 center = new Vector2(size * 0.5f, size * 0.47f);
        for (int i = 0; i < 10; i++)
        {
            float angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
            float radius = i % 2 == 0 ? size * 0.47f : size * 0.22f;
            points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
            float edge = float.PositiveInfinity;
            for (int i = 0; i < points.Count; i++) edge = Mathf.Min(edge, SegmentDistance(p, points[i], points[(i + 1) % points.Count]));
            edge += (Noise(x, y, 0.2f) - 0.5f) * 1.5f;
            bool inside = InPolygon(p, points);
            float d = inside ? -edge : edge;
            Color color = new Color(Yellow.r, Yellow.g * (0.93f + 0.1f * Noise(x, y, 0.35f)), Yellow.b, Mathf.Clamp01(0.5f - d));
            float band = Mathf.Clamp01(4.5f - Mathf.Abs(d + 2f));
            pixels[y * size + x] = Blend(color, new Color(0.74f, 0.42f, 0.06f, band));
        }
        return pixels;
    }

    private static Color[] Stopwatch(int size)
    {
        Color[] pixels = new Color[size * size];
        Vector2 c = new Vector2(size * 0.5f, size * 0.44f);
        float r = size * 0.39f;
        Vector2 knobA = new Vector2(size * 0.5f, size * 0.86f), knobB = new Vector2(size * 0.5f, size * 0.96f);
        Vector2 knobL = new Vector2(size * 0.38f, size * 0.97f), knobR = new Vector2(size * 0.62f, size * 0.97f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
            float d = Vector2.Distance(p, c) - r + (Noise(x, y, 0.2f) - 0.5f) * 1.6f;
            Color color = Color.clear;
            color = Blend(color, new Color(Paper.r, Paper.g, Paper.b, Mathf.Clamp01(0.5f - d)));
            color = Blend(color, new Color(Orange.r, Orange.g, Orange.b, Mathf.Clamp01(7f - Mathf.Abs(d + 3f))));
            float knob = Mathf.Min(SegmentDistance(p, knobA, knobB), SegmentDistance(p, knobL, knobR)) - size * 0.05f;
            color = Blend(color, new Color(Orange.r, Orange.g, Orange.b, Mathf.Clamp01(0.5f - knob)));
            float hand = Mathf.Min(
                SegmentDistance(p, c, c + new Vector2(0f, r * 0.68f)),
                SegmentDistance(p, c, c + new Vector2(r * 0.42f, -r * 0.1f))) - size * 0.028f;
            color = Blend(color, new Color(Brown.r, Brown.g, Brown.b, Mathf.Clamp01(0.5f - hand)));
            pixels[y * size + x] = color;
        }
        return pixels;
    }

    private static Color[] Check(int size)
    {
        Vector2 a = new Vector2(size * 0.16f, size * 0.5f), b = new Vector2(size * 0.41f, size * 0.22f), c = new Vector2(size * 0.86f, size * 0.8f);
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
            float d = Mathf.Min(SegmentDistance(p, a, b), SegmentDistance(p, b, c)) + (Noise(x, y, 0.2f) - 0.5f) * 1.5f;
            Color color = new Color(0.98f, 0.95f, 0.85f, Mathf.Clamp01(size * 0.11f - d));
            pixels[y * size + x] = Blend(color, new Color(0.3f, 0.7f, 0.28f, Mathf.Clamp01(size * 0.075f - d)));
        }
        return pixels;
    }

    // Pointing down, open at the top so it runs on into the bubble.
    private static Color[] Tail(int size, bool accentOnly)
    {
        Vector2 leftTop = new Vector2(size * 0.1f, size), rightTop = new Vector2(size * 0.9f, size), tip = new Vector2(size * 0.5f, size * 0.04f);
        List<Vector2> shape = new List<Vector2> { leftTop, rightTop, tip };
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
            float edge = Mathf.Min(SegmentDistance(p, leftTop, tip), SegmentDistance(p, rightTop, tip)) + (Noise(x, y, 0.2f) - 0.5f) * 1.1f;
            bool inside = InPolygon(p, shape);
            float d = inside ? -edge : edge;
            if (accentOnly)
            {
                // The colour line down the tail's sides; the prefab tints it per speaker.
                float band = Mathf.Clamp01(4f - Mathf.Abs(d + 2f));
                pixels[y * size + x] = new Color(1f, 1f, 1f, band);
                continue;
            }
            pixels[y * size + x] = new Color(Paper.r, Paper.g, Paper.b, Mathf.Clamp01(0.5f - d));
        }
        return pixels;
    }

    private static Color[] CrayonLine(int width, int height)
    {
        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float center = height * 0.5f + (Noise(x, 3f, 0.09f) - 0.5f) * 3f;
            float t = Mathf.Clamp01(1f - Mathf.Abs(x - width * 0.5f) / (width * 0.5f));
            float a = Mathf.Clamp01(4f * Mathf.Min(1f, t * 8f) - Mathf.Abs(y + 0.5f - center));
            pixels[y * width + x] = new Color(1f, 1f, 1f, a * Mathf.Lerp(0.65f, 1f, Noise(x, y, 0.55f)));
        }
        return pixels;
    }

    private static Color[] Glow(int size)
    {
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
            float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
        }
        return pixels;
    }

    private static void Save(string name, Color[] pixels)
    {
        int width = name == "line_crayon" ? 128 : Mathf.RoundToInt(Mathf.Sqrt(pixels.Length));
        int height = pixels.Length / width;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();
        File.WriteAllBytes($"{ArtFolder}/{name}.png", texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }

    private static void ImportArt()
    {
        foreach (string name in new[] { "panel_paper", "panel_dark", "key_cap", "star", "stopwatch", "check", "bubble_tail", "bubble_tail_accent", "line_crayon", "glow" })
        {
            string path = $"{ArtFolder}/{name}.png";
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = name == "panel_paper" || name == "panel_dark" ? new Vector4(48f, 48f, 48f, 48f)
                : name == "key_cap" ? new Vector4(26f, 26f, 26f, 26f)
                : name == "line_crayon" ? new Vector4(24f, 0f, 24f, 0f)
                : Vector4.zero;
            importer.SaveAndReimport();
        }
    }

    private static Sprite Art(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtFolder}/{name}.png");

    // --- Small building blocks ---------------------------------------------------------

    private static RectTransform Node(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        GameObject node = new GameObject(name, typeof(RectTransform));
        RectTransform rect = node.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static Image AddImage(RectTransform rect, Sprite sprite, Color color, bool sliced = false, bool preserve = false)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        image.preserveAspect = preserve;
        return image;
    }

    private static TMP_Text AddText(RectTransform rect, TMP_FontAsset font, string value, float size, Color color, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private static void Set(Object target, string field, Object value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty property = so.FindProperty(field);
        if (property == null) { Debug.LogError($"{target.GetType().Name} has no field {field}."); return; }
        property.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static T SavePrefab<T>(GameObject root, string name) where T : Component
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{name}.prefab");
        Object.DestroyImmediate(root);
        return prefab.GetComponent<T>();
    }

    // --- Prefabs -----------------------------------------------------------------------

    private static KeyPromptUI BuildKeyPrompt(TMP_FontAsset font)
    {
        GameObject root = new GameObject("KeyPrompt", typeof(RectTransform));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(44f, 44f);
        AddImage(rect, Art("key_cap"), Color.white, true);
        LayoutElement layout = root.AddComponent<LayoutElement>();
        layout.preferredWidth = layout.preferredHeight = 44f;

        RectTransform labelRect = Node("Label", rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), Vector2.zero);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = new Vector2(0f, 2f);
        TMP_Text label = AddText(labelRect, font, "E", 24f, DarkBrown, TextAlignmentOptions.Center);

        KeyPromptUI key = root.AddComponent<KeyPromptUI>();
        Set(key, "label", label);
        Set(key, "layout", layout);
        return SavePrefab<KeyPromptUI>(root, "KeyPrompt");
    }

    private static void BuildHint(TMP_FontAsset font, KeyPromptUI key)
    {
        GameObject root = new GameObject("TutorialHint", typeof(RectTransform));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        RectTransform panel = Node("Panel", rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(480f, 96f));
        AddImage(panel, Art("panel_dark"), Color.white, true);
        VerticalLayoutGroup vertical = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(34, 34, 16, 18);
        vertical.spacing = 8f;
        vertical.childAlignment = TextAnchor.MiddleCenter;
        vertical.childControlWidth = vertical.childControlHeight = true;
        vertical.childForceExpandWidth = vertical.childForceExpandHeight = false;
        ContentSizeFitter fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform textRect = Node("Text Template", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 40f));
        TMP_Text template = AddText(textRect, font, "힌트", 30f, new Color(1f, 0.96f, 0.86f), TextAlignmentOptions.Center);

        KeyPromptUI keyTemplate = (KeyPromptUI)PrefabUtility.InstantiatePrefab(key, panel);
        keyTemplate.gameObject.name = "Key Template";

        RectTransform check = Node("Check", panel, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-6f, -6f), new Vector2(64f, 64f));
        AddImage(check, Art("check"), Color.white, false, true);
        check.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        TutorialHintUI hint = root.AddComponent<TutorialHintUI>();
        Set(hint, "group", group);
        Set(hint, "panel", panel);
        Set(hint, "textTemplate", template);
        Set(hint, "keyTemplate", keyTemplate);
        Set(hint, "starIcon", Art("star"));
        Set(hint, "checkMark", check);
        SavePrefab<TutorialHintUI>(root, "TutorialHint");
    }

    private static void BuildBubble(TMP_FontAsset font, KeyPromptUI key)
    {
        GameObject root = new GameObject("TutorialSpeechBubble", typeof(RectTransform));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(420f, 170f);
        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        // Everything hangs on Content, so popping its scale leaves the bubble's place alone.
        RectTransform content = Node("Content", rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = content.offsetMax = Vector2.zero;

        RectTransform background = Node("BubbleBackground", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        background.anchorMin = Vector2.zero;
        background.anchorMax = Vector2.one;
        background.offsetMin = background.offsetMax = Vector2.zero;
        AddImage(background, Art("panel_paper"), Color.white, true);

        // Over the paper's edge, so the tail runs on out of the bubble without a line across it.
        RectTransform tail = Node("BubbleTail", content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(52f, 44f));
        AddImage(tail, Art("bubble_tail"), Color.white);

        RectTransform accentRect = Node("TailAccent", tail, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        accentRect.anchorMin = Vector2.zero;
        accentRect.anchorMax = Vector2.one;
        accentRect.offsetMin = accentRect.offsetMax = Vector2.zero;
        Image tailAccent = AddImage(accentRect, Art("bubble_tail_accent"), Orange);

        RectTransform nameRect = Node("SpeakerName", content, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -16f), new Vector2(300f, 36f));
        TMP_Text name = AddText(nameRect, font, "두두", 28f, Orange, TextAlignmentOptions.Left);

        RectTransform accent = Node("AccentLine", content, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -52f), new Vector2(92f, 10f));
        Image accentLine = AddImage(accent, Art("line_crayon"), Orange, true);

        RectTransform lineRect = Node("DialogueText", content, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
        lineRect.anchorMin = Vector2.zero;
        lineRect.anchorMax = Vector2.one;
        lineRect.offsetMin = new Vector2(30f, 54f);
        lineRect.offsetMax = new Vector2(-30f, -58f);
        TMP_Text line = AddText(lineRect, font, "대사", 31f, DarkBrown, TextAlignmentOptions.TopLeft);
        line.textWrappingMode = TextWrappingModes.Normal;

        RectTransform guide = Node("ContinueGuide", content, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-22f, 12f), new Vector2(150f, 38f));
        HorizontalLayoutGroup row = guide.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 8f;
        row.childAlignment = TextAnchor.MiddleRight;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        KeyPromptUI enter = (KeyPromptUI)PrefabUtility.InstantiatePrefab(key, guide);
        enter.gameObject.name = "KeyIcon_Enter";
        enter.SetSize(34f, 19f);
        SerializedObject labelSo = new SerializedObject(enter);
        ((TMP_Text)labelSo.FindProperty("label").objectReferenceValue).text = "Enter";
        LayoutElement enterLayout = enter.GetComponent<LayoutElement>();
        enterLayout.preferredWidth = 72f;
        RectTransform nextRect = Node("Next", guide, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(50f, 34f));
        AddText(nextRect, font, "다음", 22f, new Color(Brown.r, Brown.g, Brown.b, 0.85f), TextAlignmentOptions.MidlineRight);

        TutorialSpeechBubble bubble = root.AddComponent<TutorialSpeechBubble>();
        Set(bubble, "group", group);
        Set(bubble, "content", content);
        Set(bubble, "nameText", name);
        Set(bubble, "lineText", line);
        Set(bubble, "accentLine", accentLine);
        Set(bubble, "tail", tail);
        Set(bubble, "tailAccent", tailAccent);
        SavePrefab<TutorialSpeechBubble>(root, "TutorialSpeechBubble");
    }

    private static void BuildGameHud(TMP_FontAsset font)
    {
        GameObject root = new GameObject("GameHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        RectTransform rect = root.GetComponent<RectTransform>();

        RectTransform glow = Node("Star Glow", rect, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(128f, -64f), new Vector2(360f, 240f));
        Image starGlow = AddImage(glow, Art("glow"), new Color(1f, 0.85f, 0.25f, 0f));

        RectTransform star = Node("Star Badge", rect, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(128f, -64f), new Vector2(196f, 72f));
        AddImage(star, Art("panel_paper"), Color.white, true);
        RectTransform icon = Node("Icon", star, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(44f, 2f), new Vector2(50f, 50f));
        AddImage(icon, Art("star"), Color.white, false, true);
        RectTransform countRect = Node("Count", star, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(76f, 0f), new Vector2(108f, 50f));
        TMP_Text count = AddText(countRect, font, "0 / 3", 34f, DarkBrown, TextAlignmentOptions.Left);
        RectTransform plusRect = Node("Plus One", star, new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(70f, 50f));
        TMP_Text plus = AddText(plusRect, font, "+1", 36f, new Color(0.9f, 0.55f, 0.05f), TextAlignmentOptions.Left);

        RectTransform timer = Node("Timer Badge", rect, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-128f, -64f), new Vector2(196f, 72f));
        AddImage(timer, Art("panel_paper"), Color.white, true);
        RectTransform watch = Node("Icon", timer, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(44f, 2f), new Vector2(46f, 46f));
        AddImage(watch, Art("stopwatch"), Color.white, false, true);
        RectTransform timeRect = Node("Time", timer, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(76f, 0f), new Vector2(108f, 50f));
        TMP_Text time = AddText(timeRect, font, "00:00", 34f, DarkBrown, TextAlignmentOptions.Left);

        RectTransform toast = Node("Tool Toast", rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(300f, 64f));
        AddImage(toast, Art("panel_dark"), Color.white, true);
        CanvasGroup toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
        toastGroup.blocksRaycasts = false;
        toastGroup.interactable = false;
        RectTransform toolRect = Node("Icon", toast, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(52f, 0f), new Vector2(68f, 44f));
        Image toolIcon = AddImage(toolRect, AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Ingame/Curser_Crayon.png"), Color.white, false, true);
        RectTransform toolLabelRect = Node("Label", toast, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(96f, 0f), new Vector2(190f, 44f));
        TMP_Text toolLabel = AddText(toolLabelRect, font, "크레파스", 26f, new Color(1f, 0.96f, 0.86f), TextAlignmentOptions.Left);

        RectTransform fly = Node("Flying Star", rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
        Image flying = AddImage(fly, Art("star"), Color.white, false, true);

        GameHud hud = root.AddComponent<GameHud>();
        Set(hud, "starBadge", star);
        Set(hud, "starCount", count);
        Set(hud, "plusOne", plus);
        Set(hud, "starGlow", starGlow);
        Set(hud, "starIcon", icon);
        Set(hud, "flyingStar", flying);
        Set(hud, "timerBadge", timer);
        Set(hud, "timerText", time);
        Set(hud, "toolToast", toastGroup);
        Set(hud, "toolIcon", toolIcon);
        Set(hud, "toolLabel", toolLabel);
        Set(hud, "crayonIcon", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Ingame/Curser_Crayon.png"));
        Set(hud, "eraserIcon", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Ingame/Curser_Eraser.png"));
        SavePrefab<GameHud>(root, "GameHud");
    }

    // Mic and quick chat move to the bottom left, icons only; their labels show on hover.
    private static void LayOutComms()
    {
        string path = $"{PrefabFolder}/CoopCommsHud.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        void Place(string name, Vector2 position)
        {
            Transform found = root.transform.Find(name);
            if (found == null) { Debug.LogError($"CoopCommsHud has no {name}."); return; }
            RectTransform rect = (RectTransform)found;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.anchoredPosition = position;
        }
        Place("MicButton", new Vector2(70f, 136f));
        Place("QuickChatButton", new Vector2(70f, 50f));
        Place("MicLabel", new Vector2(202f, 136f));
        Place("QuickChatLabel", new Vector2(202f, 50f));
        CoopCommsHud hud = root.GetComponent<CoopCommsHud>();
        Set(hud, "micLabel", root.transform.Find("MicLabel").gameObject);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }
}
#endif
