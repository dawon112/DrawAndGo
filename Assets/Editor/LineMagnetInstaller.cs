using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class LineMagnetInstaller
{
    private const string ScenePath = "Assets/Scenes/Player3DScene.unity";
    private const string PrefabPath = "Assets/Prefabs/Obstacles/LineMagnet.prefab";
    private const string MaterialDirectory = "Assets/Materials/Obstacles/LineMagnet";
    private const string InstallKey = "DrawAndGo.LineMagnetInstaller.v1";

    [InitializeOnLoadMethod]
    private static void InstallAfterCompile()
    {
        if (EditorPrefs.GetBool(InstallKey, false)) return;
        EditorApplication.update += TryInstallWhenReady;
    }

    private static void TryInstallWhenReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (SceneManager.GetActiveScene().path != ScenePath) return;
        EditorApplication.update -= TryInstallWhenReady;
        Install();
    }

    [MenuItem("Tools/Draw And Go/Install Line Magnet")]
    public static void Install()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        Directory.CreateDirectory(MaterialDirectory);
        AssetDatabase.Refresh();
        GameObject prefab = CreatePrefab();

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CornerRoomLevel level = Object.FindAnyObjectByType<CornerRoomLevel>();
        if (level == null || level.surfaces == null || level.surfaces.Length < 5)
            throw new System.InvalidOperationException("Player3DScene requires five configured Dudu surfaces.");

        foreach (LineMagnet oldMagnet in Object.FindObjectsByType<LineMagnet>())
            Object.DestroyImmediate(oldMagnet.gameObject);

        DuduSurface targetSurface = level.surfaces[4];
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = "Line Magnet - Section 5";
        Vector2 surfacePosition = new Vector2(-1.8f, -0.65f);
        instance.transform.SetPositionAndRotation(
            targetSurface.SurfaceToWorld(surfacePosition) + targetSurface.Normal.normalized * 0.25f,
            targetSurface.transform.rotation);
        instance.GetComponent<LineMagnet>().Configure(targetSurface);
        EditorUtility.SetDirty(instance.GetComponent<LineMagnet>());

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(InstallKey, true);
        Debug.Log("Installed LineMagnet prefab and placed one instance on Section 5 at (-1.8, -0.65).");
    }

    private static GameObject CreatePrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null) return existing;

        Material red = CreateMaterial(MaterialDirectory + "/MagnetRed.mat", new Color(0.95f, 0.12f, 0.22f, 1f));
        Material blue = CreateMaterial(MaterialDirectory + "/MagnetBlue.mat", new Color(0.12f, 0.5f, 1f, 1f));
        Material core = CreateMaterial(MaterialDirectory + "/MagnetCore.mat", new Color(0.95f, 0.95f, 1f, 1f));

        GameObject root = new GameObject("LineMagnet");
        root.AddComponent<LineMagnet>();
        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        CreateBar(visual.transform, "Red Pole", new Vector3(-0.34f, 0f, 0f), new Vector3(0.34f, 1.05f, 0.08f), red);
        CreateBar(visual.transform, "Blue Pole", new Vector3(0.34f, 0f, 0f), new Vector3(0.34f, 1.05f, 0.08f), blue);
        CreateBar(visual.transform, "Core", Vector3.zero, new Vector3(0.22f, 0.48f, 0.09f), core);
        GameObject range = new GameObject("Attraction Range (Gizmo Only)");
        range.transform.SetParent(root.transform, false);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void CreateBar(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = name;
        bar.transform.SetParent(parent, false);
        bar.transform.localPosition = position;
        bar.transform.localScale = scale;
        Object.DestroyImmediate(bar.GetComponent<Collider>());
        MeshRenderer renderer = bar.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static Material CreateMaterial(string path, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path), color = color };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
