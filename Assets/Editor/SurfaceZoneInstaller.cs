using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class SurfaceZoneInstaller
{
    private const string ScenePath = "Assets/Scenes/Player3DScene.unity";
    private const string PrefabDirectory = "Assets/Prefabs/Obstacles/Zones";
    private const string MaterialDirectory = "Assets/Materials/Obstacles/Zones";
    private const string NoDrawPrefabPath = PrefabDirectory + "/NoDrawZone.prefab";
    private const string WindPrefabPath = PrefabDirectory + "/WindZone.prefab";
    private const string InstallKey = "DrawAndGo.SurfaceZoneInstaller.v2";

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

    [MenuItem("Tools/Draw And Go/Install Surface Zones")]
    public static void Install()
    {
        Directory.CreateDirectory(PrefabDirectory);
        Directory.CreateDirectory(MaterialDirectory);
        AssetDatabase.Refresh();

        GameObject noDrawPrefab = CreateZonePrefab<NoDrawZone>(
            NoDrawPrefabPath,
            CreateMaterial(MaterialDirectory + "/NoDrawZone.mat", new Color(0.35f, 0.35f, 0.35f, 0.38f)));
        GameObject windPrefab = CreateZonePrefab<WindZone>(
            WindPrefabPath,
            CreateMaterial(MaterialDirectory + "/WindZone.mat", new Color(0.25f, 0.7f, 1f, 0.35f)));

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CornerRoomLevel level = Object.FindFirstObjectByType<CornerRoomLevel>();
        if (level == null || level.surfaces == null || level.surfaces.Length < 4)
            throw new System.InvalidOperationException("Player3DScene requires at least four configured surfaces.");

        RemoveExistingZone<NoDrawZone>();
        RemoveExistingZone<WindZone>();

        GameObject noDraw = (GameObject)PrefabUtility.InstantiatePrefab(noDrawPrefab, scene);
        noDraw.name = "No Draw Zone - Section 2";
        Place(noDraw.transform, level.surfaces[1], new Vector2(0.8f, -0.45f));
        noDraw.GetComponent<NoDrawZone>().Configure(level.surfaces[1]);

        GameObject wind = (GameObject)PrefabUtility.InstantiatePrefab(windPrefab, scene);
        wind.name = "Wind Zone - Section 4";
        Place(wind.transform, level.surfaces[3], new Vector2(1.4f, -0.55f));
        wind.GetComponent<WindZone>().Configure(level.surfaces[3], Vector2.right, 1f);

        EditorUtility.SetDirty(noDraw.GetComponent<NoDrawZone>());
        EditorUtility.SetDirty(wind.GetComponent<WindZone>());
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(InstallKey, true);
        Debug.Log("Installed NoDrawZone on Section 2 and WindZone on Section 4.");
    }

    private static GameObject CreateZonePrefab<T>(string path, Material material) where T : Component
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        GameObject zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
        zone.name = typeof(T).Name;
        zone.transform.localScale = new Vector3(2.8f, 1.45f, 0.025f);
        BoxCollider collider = zone.GetComponent<BoxCollider>();
        collider.isTrigger = true;
        MeshRenderer renderer = zone.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        zone.AddComponent<T>();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(zone, path);
        Object.DestroyImmediate(zone);
        return prefab;
    }

    private static Material CreateMaterial(string path, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path), color = color };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void Place(Transform zone, DuduSurface surface, Vector2 surfacePosition)
    {
        zone.SetPositionAndRotation(
            surface.SurfaceToWorld(surfacePosition) + surface.Normal.normalized * 0.012f,
            surface.transform.rotation);
    }

    private static void RemoveExistingZone<T>() where T : Component
    {
        T existing = Object.FindFirstObjectByType<T>();
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
    }
}
