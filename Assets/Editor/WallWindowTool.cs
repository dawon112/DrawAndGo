#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Cuts evenly spaced windows into a box wall so the sun (StageLook, shadows on) falls
// into the room through them. The wall is rebuilt from pieces around the openings;
// each opening gets a wooden frame with a cross bar (its shadow lands on the floor)
// and a glowing frosted pane that lets light through but hides the outside.
// Only use it on walls Dudu does not walk on.
public static class WallWindowTool
{
    private const float WindowWidth = 2.2f;
    private const float WindowHeight = 2.3f;
    private const float SillHeight = 1.5f;
    private const float FrameBar = 0.1f;
    private const float FrameDepthExtra = 0.08f;
    private const float WindowSpacing = 5f;
    private const string FrameMaterialPath = "Assets/Materials/WindowFrame.mat";
    private const string GlassMaterialPath = "Assets/Materials/WindowGlass.mat";
    private const string LampMaterialPath = "Assets/Materials/CeilingLamp.mat";
    private const float LampSpacing = 6f;

    [MenuItem("Tools/Draw And Go/Cut Windows In Selected Wall")]
    private static void CutSelected()
    {
        foreach (GameObject wall in Selection.gameObjects)
            CutWindows(wall);
    }

    [MenuItem("Tools/Draw And Go/Cut Windows In Selected Wall", true)]
    private static bool CanCutSelected()
    {
        foreach (GameObject wall in Selection.gameObjects)
            if (CanCut(wall)) return true;
        return false;
    }

    // Warm lamps along a ceiling, so the shade away from the windows stays readable.
    [MenuItem("Tools/Draw And Go/Hang Lamps Under Selected Ceiling")]
    private static void HangLampsSelected()
    {
        foreach (GameObject ceiling in Selection.gameObjects)
            HangLamps(ceiling);
    }

    public static GameObject HangLamps(GameObject ceiling, float spacing = LampSpacing)
    {
        Renderer renderer = ceiling != null ? ceiling.GetComponent<Renderer>() : null;
        if (renderer == null) return null;
        Bounds bounds = renderer.bounds;
        bool alongX = bounds.size.x >= bounds.size.z;
        float length = alongX ? bounds.size.x : bounds.size.z;
        int count = Mathf.Max(1, Mathf.RoundToInt(length / spacing));

        var root = new GameObject(ceiling.name + " (Lamps)");
        Undo.RegisterCreatedObjectUndo(root, "Hang lamps");
        root.transform.SetParent(ceiling.transform.parent, false);
        root.AddComponent<RoomOnlyDecor>();
        Material shadeMaterial = LoadOrCreateLampMaterial();
        for (int i = 0; i < count; i++)
        {
            float along = -length * 0.5f + (i + 0.5f) * length / count;
            Vector3 position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) +
                (alongX ? Vector3.right : Vector3.forward) * along;

            GameObject shade = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shade.name = $"Lamp {i + 1}";
            shade.transform.SetParent(root.transform, false);
            shade.transform.position = position + Vector3.down * 0.03f;
            shade.transform.localScale = new Vector3(0.7f, 0.03f, 0.7f);
            Object.DestroyImmediate(shade.GetComponent<Collider>());
            var shadeRenderer = shade.GetComponent<MeshRenderer>();
            shadeRenderer.sharedMaterial = shadeMaterial;
            shadeRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var glow = new GameObject("Light");
            glow.transform.SetParent(shade.transform, false);
            glow.transform.position = position + Vector3.down * 0.4f;
            Light light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.8f, 0.58f);
            light.intensity = 8f;
            light.range = 9f;
            light.shadows = LightShadows.None;
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        return root;
    }

    private static Material LoadOrCreateLampMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(LampMaterialPath);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "CeilingLamp" };
        material.SetColor("_BaseColor", new Color(1.5f, 1.25f, 0.95f));
        AssetDatabase.CreateAsset(material, LampMaterialPath);
        return material;
    }

    private static bool CanCut(GameObject wall) =>
        wall != null && wall.GetComponent<MeshFilter>() != null && wall.GetComponent<DuduSurface>() == null &&
        wall.GetComponentInParent<RoomOnlyDecor>() == null;

    // Returns the new root, or null when the wall cannot take a window.
    public static GameObject CutWindows(GameObject wall, int count = 0)
    {
        if (!CanCut(wall))
        {
            Debug.LogWarning($"{(wall != null ? wall.name : "null")}: not a plain wall (Dudu walls and cut walls are skipped).");
            return null;
        }

        Transform t = wall.transform;
        Vector3 s = t.localScale;
        // The long side is local X or Z; the thin side is the other one.
        bool alongX = Mathf.Abs(s.x) >= Mathf.Abs(s.z);
        float length = alongX ? s.x : s.z;
        float height = s.y;
        if (count <= 0) count = Mathf.Max(1, Mathf.FloorToInt(length / WindowSpacing));
        if (length < count * (WindowWidth + 0.6f) || height < SillHeight + WindowHeight + 0.3f)
        {
            Debug.LogWarning($"{wall.name}: too small for {count} window(s).");
            return null;
        }

        var root = new GameObject(wall.name + " (Windows)");
        Undo.RegisterCreatedObjectUndo(root, "Cut windows");
        root.transform.SetParent(t.parent, false);
        root.transform.SetSiblingIndex(t.GetSiblingIndex());
        root.AddComponent<RoomOnlyDecor>().letsSunIn = true;

        // Unit-cube coordinates along the wall (u), up (v) and through it (w).
        float sill = -0.5f + SillHeight / height;
        float top = sill + WindowHeight / height;
        var centres = new List<float>();
        for (int i = 0; i < count; i++) centres.Add(-0.5f + (i + 0.5f) / count);
        float half = WindowWidth * 0.5f / length;

        Piece(wall, root, "Sill", -0.5f, 0.5f, -0.5f, sill, alongX);
        Piece(wall, root, "Lintel", -0.5f, 0.5f, top, 0.5f, alongX);
        float from = -0.5f;
        for (int i = 0; i < count; i++)
        {
            Piece(wall, root, $"Pier {i + 1}", from, centres[i] - half, sill, top, alongX);
            from = centres[i] + half;
        }
        Piece(wall, root, $"Pier {count + 1}", from, 0.5f, sill, top, alongX);

        Material frame = LoadOrCreateFrameMaterial();
        Material glass = LoadOrCreateGlassMaterial();
        float thick = alongX ? s.z : s.x;
        for (int i = 0; i < count; i++)
        {
            float u = centres[i] * length;
            float v = SillHeight + WindowHeight * 0.5f;
            float depth = thick + FrameDepthExtra;
            // Frame: four sides and a cross bar.
            Box(root, t, $"Window {i + 1} Frame Left", alongX, u - WindowWidth * 0.5f + FrameBar * 0.5f, v, FrameBar, WindowHeight, depth, frame, true);
            Box(root, t, $"Window {i + 1} Frame Right", alongX, u + WindowWidth * 0.5f - FrameBar * 0.5f, v, FrameBar, WindowHeight, depth, frame, true);
            Box(root, t, $"Window {i + 1} Frame Top", alongX, u, v + WindowHeight * 0.5f - FrameBar * 0.5f, WindowWidth, FrameBar, depth, frame, true);
            Box(root, t, $"Window {i + 1} Frame Bottom", alongX, u, v - WindowHeight * 0.5f + FrameBar * 0.5f, WindowWidth, FrameBar, depth, frame, true);
            Box(root, t, $"Window {i + 1} Bar Vertical", alongX, u, v, FrameBar * 0.6f, WindowHeight, depth * 0.8f, frame, true);
            Box(root, t, $"Window {i + 1} Bar Horizontal", alongX, u, v, WindowWidth, FrameBar * 0.6f, depth * 0.8f, frame, true);
            // Pane: lets the sun through (no shadow) but keeps the room closed.
            GameObject pane = Box(root, t, $"Window {i + 1} Glass", alongX, u, v, WindowWidth, WindowHeight, thick * 0.3f, glass, true);
            pane.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            pane.GetComponent<MeshRenderer>().receiveShadows = false;
        }

        Undo.DestroyObjectImmediate(wall);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"{root.name}: cut {count} window(s).", root);
        return root;
    }

    // A copy of the original wall (same material, collider, layer and components)
    // resized to cover [u0,u1] x [v0,v1] of the unit cube.
    private static void Piece(GameObject wall, GameObject root, string label, float u0, float u1, float v0, float v1, bool alongX)
    {
        if (u1 - u0 <= 0.0001f || v1 - v0 <= 0.0001f) return;
        Transform t = wall.transform;
        GameObject piece = Object.Instantiate(wall, root.transform);
        piece.name = $"{wall.name} - {label}";
        Vector3 unitCentre = alongX
            ? new Vector3((u0 + u1) * 0.5f, (v0 + v1) * 0.5f, 0f)
            : new Vector3(0f, (v0 + v1) * 0.5f, (u0 + u1) * 0.5f);
        Vector3 unitSize = alongX ? new Vector3(u1 - u0, v1 - v0, 1f) : new Vector3(1f, v1 - v0, u1 - u0);
        piece.transform.SetLocalPositionAndRotation(
            t.localPosition + t.localRotation * Vector3.Scale(unitCentre, t.localScale), t.localRotation);
        piece.transform.localScale = Vector3.Scale(unitSize, t.localScale);
    }

    // A box in the wall's frame: u along the wall, v up, sizes in world units.
    private static GameObject Box(GameObject root, Transform wall, string name, bool alongX,
        float u, float v, float width, float height, float depth, Material material, bool collider)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(root.transform, false);
        Vector3 offset = alongX ? new Vector3(u, v, 0f) : new Vector3(0f, v, u);
        Vector3 bottomCentre = wall.localPosition + wall.localRotation * new Vector3(0f, -wall.localScale.y * 0.5f, 0f);
        box.transform.SetLocalPositionAndRotation(bottomCentre + wall.localRotation * offset, wall.localRotation);
        box.transform.localScale = alongX ? new Vector3(width, height, depth) : new Vector3(depth, height, width);
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        if (!collider) Object.DestroyImmediate(box.GetComponent<Collider>());
        box.layer = wall.gameObject.layer == LayerMask.NameToLayer("DrawingSurface") ? 0 : wall.gameObject.layer;
        return box;
    }

    private static Material LoadOrCreateFrameMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(FrameMaterialPath);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "WindowFrame" };
        material.SetColor("_BaseColor", new Color(0.62f, 0.42f, 0.28f));
        material.SetFloat("_Smoothness", 0.3f);
        AssetDatabase.CreateAsset(material, FrameMaterialPath);
        return material;
    }

    private static Material LoadOrCreateGlassMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(GlassMaterialPath);
        if (material != null) return material;
        // Frosted, sunlit pane: bright enough to bloom softly, no view of the outside.
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "WindowGlass" };
        material.SetColor("_BaseColor", new Color(1.6f, 1.38f, 1.08f));
        AssetDatabase.CreateAsset(material, GlassMaterialPath);
        return material;
    }
}
#endif
