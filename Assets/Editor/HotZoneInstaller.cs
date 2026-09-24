using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class HotZoneInstaller
{
    private const string ScenePath = "Assets/Scenes/Player3DScene.unity";
    private const string PrefabPath = "Assets/Prefabs/Obstacles/Zones/HotZone.prefab";
    private const string MaterialPath = "Assets/Materials/Obstacles/Zones/HotZone.mat";
    private const string InstallKey = "DrawAndGo.HotZoneInstaller.v1";

    [InitializeOnLoadMethod]
    private static void InstallAfterCompile()
    {
        if (EditorPrefs.GetBool(InstallKey, false)) return;
        EditorApplication.update += TryInstall;
    }

    private static void TryInstall()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= TryInstall;
        Install();
    }

    [MenuItem("Tools/Draw And Go/Install Hot Zone")]
    public static void Install()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
        AssetDatabase.Refresh();
        GameObject prefab = CreatePrefab();

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CornerRoomLevel level = Object.FindAnyObjectByType<CornerRoomLevel>();
        if (level == null || level.surfaces == null || level.surfaces.Length < 3)
            throw new System.InvalidOperationException("Player3DScene requires at least three Dudu surfaces.");

        foreach (HotZone oldZone in Object.FindObjectsByType<HotZone>())
            Object.DestroyImmediate(oldZone.gameObject);

        DuduSurface target = level.surfaces[2];
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = "Hot Zone - Section 3";
        Vector2 local = new Vector2(-1.4f, -0.5f);
        instance.transform.SetPositionAndRotation(
            target.SurfaceToWorld(local) + target.Normal.normalized * 0.015f,
            target.transform.rotation);
        instance.GetComponent<HotZone>().Configure(target, 2f);
        EditorUtility.SetDirty(instance.GetComponent<HotZone>());
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(InstallKey, true);
        Debug.Log("Installed HotZone on Section 3 at (-1.4, -0.5).");
    }

    private static GameObject CreatePrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null) return existing;
        Material material = CreateMaterial();
        GameObject zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
        zone.name = "HotZone";
        zone.transform.localScale = new Vector3(3.2f, 1.6f, 0.025f);
        BoxCollider collider = zone.GetComponent<BoxCollider>();
        collider.isTrigger = true;
        MeshRenderer renderer = zone.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        zone.AddComponent<HotZone>();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(zone, PrefabPath);
        Object.DestroyImmediate(zone);
        return prefab;
    }

    private static Material CreateMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null) return existing;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        Color color = new Color(1f, 0.18f, 0.12f, 0.28f);
        Material material = new Material(shader) { name = "HotZone", color = color };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", 5f);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", 10f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }
}
