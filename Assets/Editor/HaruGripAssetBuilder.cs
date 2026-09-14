#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class HaruGripAssetBuilder
{
    private const string HaruPath = "Assets/Art/Characters/Haru/haru.fbx";
    private const string CrayonPath = "Assets/Art/Drawing/Crayon.fbx";
    private const string PrefabPath = "Assets/Art/Characters/Haru/HaruWithCrayon.prefab";
    private const string ScenePath = "Assets/Scenes/Player3DScene.unity";

    static HaruGripAssetBuilder()
    {
        EditorApplication.delayCall += BuildIfNeeded;
    }

    private static void BuildIfNeeded()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(PrefabPath))
            return;

        GameObject haruAsset = AssetDatabase.LoadAssetAtPath<GameObject>(HaruPath);
        GameObject crayonAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CrayonPath);
        if (haruAsset == null || crayonAsset == null) return;

        GameObject root = Object.Instantiate(haruAsset);
        root.name = "HaruWithCrayon";
        Transform[] bones = root.GetComponentsInChildren<Transform>(true);
        Transform hand = bones.First(item => item.name == "R_Hand");

        Curl(bones, "R_Index01", 48f);
        Curl(bones, "R_Index02", 58f);
        Curl(bones, "R_Middle01", 52f);
        Curl(bones, "R_Middle02", 62f);
        Curl(bones, "R_Pinky01", 55f);
        Curl(bones, "R_Pinky02", 65f);
        Curl(bones, "R_Thumb01", -28f);
        Curl(bones, "R_Thumb02", -38f);

        GameObject crayon = (GameObject)PrefabUtility.InstantiatePrefab(crayonAsset);
        crayon.name = "Crayon Grip";
        crayon.transform.SetParent(hand, false);
        crayon.transform.localPosition = new Vector3(0.075f, 0.01f, 0.015f);
        crayon.transform.localRotation = Quaternion.Euler(0f, 12f, -58f);
        FitLength(crayon.transform, 0.34f);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        ApplyPrefabToScene();
        AssetDatabase.SaveAssets();
        Debug.Log("Haru finger grip and Crayon asset prefab applied.");
    }

    private static void Curl(Transform[] bones, string name, float degrees)
    {
        Transform bone = bones.FirstOrDefault(item => item.name == name);
        if (bone != null) bone.localRotation *= Quaternion.Euler(0f, 0f, degrees);
    }

    private static void FitLength(Transform item, float desiredLength)
    {
        Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float length = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (length > 0.0001f) item.localScale *= desiredLength / length;
    }

    private static void ApplyPrefabToScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Player3DMovement player = Object.FindAnyObjectByType<Player3DMovement>(FindObjectsInactive.Include);
        Transform oldVisual = player != null ? player.transform.Find("Haru Visual") : null;
        if (player == null || oldVisual == null) return;

        Vector3 position = oldVisual.localPosition;
        Quaternion rotation = oldVisual.localRotation;
        Vector3 scale = oldVisual.localScale;
        Object.DestroyImmediate(oldVisual.gameObject);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        visual.name = "Haru Visual";
        visual.transform.SetParent(player.transform, false);
        visual.transform.localPosition = position;
        visual.transform.localRotation = rotation;
        visual.transform.localScale = scale;
        SetLayer(visual.transform, player.gameObject.layer);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void SetLayer(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root) SetLayer(child, layer);
    }
}
#endif
