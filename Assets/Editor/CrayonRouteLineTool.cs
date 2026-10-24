#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Level-editing helpers for Dudu's crayon ground lines (CrayonRouteLine).
//  - New: places a line on the Dudu wall under the Scene view's centre, or right
//    after the selected line so a route can be extended step by step.
//  - Convert: turns the old box ground lines of the open scene into crayon lines.
// After that, move a line with the Move tool and set its length with Scale X.
public static class CrayonRouteLineTool
{
    public const string MaterialPath = "Assets/Materials/CrayonRouteLine.mat";
    private const string CrayonTexturePath = "Assets/Art/Drawing/Caryon_Texture.png";
    private const string OldInkName = "DuduGroundLine";
    private static readonly Vector3 DefaultSize = new Vector3(4f, 0.14f, 0.16f);
    private const float GroundHeightOnWall = -2.4f;

    [MenuItem("Tools/Draw And Go/Crayon Route Lines/New Line", priority = 0)]
    [MenuItem("GameObject/Draw And Go/Crayon Route Line", priority = 10)]
    private static void CreateLine()
    {
        Material material = LoadOrCreateMaterial();
        CrayonRouteLine after = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<CrayonRouteLine>()
            : null;

        var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.name = "Dudu Route Line";
        Undo.RegisterCreatedObjectUndo(line, "New crayon route line");
        if (after != null)
        {
            // Continue the selected line, leaving a small gap to jump or draw across.
            Transform source = after.transform;
            line.transform.SetParent(source.parent, false);
            line.transform.SetSiblingIndex(source.GetSiblingIndex() + 1);
            float step = Mathf.Abs(source.lossyScale.x) * 0.5f + DefaultSize.x * 0.5f + 1.5f;
            line.transform.localScale = new Vector3(DefaultSize.x / ParentScaleX(source), source.localScale.y, source.localScale.z);
            line.transform.SetPositionAndRotation(source.position + source.right * step, source.rotation);
        }
        else
        {
            DuduSurface wall = WallInView(out Vector3 point);
            if (wall != null)
            {
                Vector2 local = wall.WorldToSurface(point);
                line.transform.SetPositionAndRotation(
                    wall.SurfaceToWorld(new Vector2(local.x, GroundHeightOnWall)), wall.transform.rotation);
                Transform levelRoot = wall.transform.root;
                line.transform.SetParent(FindLinesParent(levelRoot), true);
            }
            else if (SceneView.lastActiveSceneView != null)
                line.transform.position = SceneView.lastActiveSceneView.pivot;
            line.transform.localScale = DefaultSize;
        }

        Convert(line, material);
        Selection.activeGameObject = line;
        EditorSceneManager.MarkSceneDirty(line.scene);
    }

    [MenuItem("Tools/Draw And Go/Crayon Route Lines/Convert Old Lines In Open Scene", priority = 1)]
    private static void ConvertOpenScene()
    {
        Material material = LoadOrCreateMaterial();
        int converted = 0;
        foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            if (renderer.sharedMaterial == null || renderer.sharedMaterial.name != OldInkName) continue;
            if (!(renderer.GetComponent<Collider>() is BoxCollider) || renderer.GetComponent<CrayonRouteLine>() != null) continue;
            if (!IsGroundLine(renderer.transform)) continue;
            Convert(renderer.gameObject, material);
            converted++;
        }
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"Crayon route lines: converted {converted} ground line(s).");
    }

    // Long, thin boxes lying on a wall (doors and frames also use the old ink material).
    private static bool IsGroundLine(Transform box)
    {
        Vector3 s = box.lossyScale;
        return Mathf.Abs(s.y) <= 0.3f && Mathf.Abs(s.z) <= 0.3f && Mathf.Abs(s.x) >= 1f;
    }

    // Used by the scene builders too, so rebuilt rooms get crayon lines.
    public static void Convert(GameObject box, Material material = null)
    {
        if (box.GetComponent<CrayonRouteLine>() != null) return;
        if (box.TryGetComponent(out MeshRenderer renderer))
            Undo.DestroyObjectImmediate(renderer);
        if (box.TryGetComponent(out MeshFilter filter))
            Undo.DestroyObjectImmediate(filter);
        CrayonRouteLine line = Undo.AddComponent<CrayonRouteLine>(box);
        line.Material = material != null ? material : LoadOrCreateMaterial();
        line.Rebuild();
    }

    public static Material LoadOrCreateMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null) return material;
        material = new Material(Shader.Find("DrawAndGo/CrayonRouteLine")) { name = "CrayonRouteLine" };
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CrayonTexturePath));
        material.SetTextureScale("_BaseMap", new Vector2(0.9f, 1f));
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    private static DuduSurface WallInView(out Vector3 point)
    {
        point = default;
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null) return null;
        Ray ray = new Ray(view.camera.transform.position, view.camera.transform.forward);
        DuduSurface best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (DuduSurface surface in Object.FindObjectsByType<DuduSurface>())
        {
            var plane = new Plane(surface.Normal, surface.transform.position);
            if (!plane.Raycast(ray, out float distance) || distance >= bestDistance) continue;
            Vector2 local = surface.WorldToSurface(ray.GetPoint(distance));
            if (Mathf.Abs(local.x) > surface.Width * 0.5f || Mathf.Abs(local.y) > surface.Height * 0.5f) continue;
            best = surface;
            bestDistance = distance;
            point = ray.GetPoint(distance);
        }
        return best;
    }

    private static Transform FindLinesParent(Transform levelRoot)
    {
        CrayonRouteLine existing = levelRoot.GetComponentInChildren<CrayonRouteLine>(true);
        return existing != null ? existing.transform.parent : levelRoot;
    }

    private static float ParentScaleX(Transform source) =>
        source.parent != null ? Mathf.Max(0.0001f, Mathf.Abs(source.parent.lossyScale.x)) : 1f;
}
#endif
