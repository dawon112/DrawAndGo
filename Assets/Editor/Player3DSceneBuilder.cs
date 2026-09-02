#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class Player3DSceneBuilder
{
    private const string TwoDScenePath = "Assets/Scenes/SideScrollerPrototype.unity";
    private const string ThreeDScenePath = "Assets/Scenes/Player3DScene.unity";

    static Player3DSceneBuilder()
    {
        EditorApplication.delayCall += BuildIfNeeded;
    }

    [MenuItem("Tools/Draw And Go/Rebuild 3D Prototype Scene")]
    public static void Rebuild()
    {
        BuildScene(true);
    }

    private static void BuildIfNeeded()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (!File.Exists(ThreeDScenePath))
            BuildScene(false);
        else
            EnsureTwoDSceneSwitcher();
    }

    private static void BuildScene(bool force)
    {
        if (!force && File.Exists(ThreeDScenePath))
            return;

        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateGround();
            CreatePlayer();
            CreateLighting();
            CreateSceneSwitcher(scene);
            EditorSceneManager.SaveScene(scene, ThreeDScenePath);
            EnsureBuildSettings();
        }
        finally
        {
            if (previousSetup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
        }

        EnsureTwoDSceneSwitcher();
        AssetDatabase.SaveAssets();
    }

    private static void CreateGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground3D";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(5f, 1f, 5f);
    }

    private static void CreatePlayer()
    {
        GameObject player = new GameObject("Player3D");
        player.transform.position = new Vector3(0f, 0.05f, 0f);

        CharacterController controller = player.AddComponent<CharacterController>();
        controller.center = new Vector3(0f, 1f, 0f);
        controller.height = 2f;
        controller.radius = 0.5f;
        controller.skinWidth = 0.08f;
        controller.stepOffset = 0.3f;
        player.AddComponent<Player3DMovement>();

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Capsule Visual";
        visual.transform.SetParent(player.transform, false);
        visual.transform.localPosition = new Vector3(0f, 1f, 0f);
        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
            Object.DestroyImmediate(visualCollider);

        GameObject cameraObject = new GameObject("First Person Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(player.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        cameraObject.AddComponent<Camera>();
        Player3DLook look = cameraObject.AddComponent<Player3DLook>();
        look.SetPlayerBody(player.transform);
    }

    private static void CreateLighting()
    {
        GameObject lightObject = new GameObject("Directional Light");
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
    }

    private static void CreateSceneSwitcher(Scene scene)
    {
        GameObject switcher = new GameObject("View Scene Switcher");
        SceneManager.MoveGameObjectToScene(switcher, scene);
        switcher.AddComponent<ViewSceneSwitcher>();
    }

    private static void EnsureTwoDSceneSwitcher()
    {
        if (!File.Exists(TwoDScenePath))
            return;

        Scene scene = SceneManager.GetSceneByPath(TwoDScenePath);
        bool openedForUpdate = !scene.IsValid() || !scene.isLoaded;
        if (openedForUpdate)
            scene = EditorSceneManager.OpenScene(TwoDScenePath, OpenSceneMode.Additive);

        try
        {
            if (scene.GetRootGameObjects().Any(root => root.GetComponent<ViewSceneSwitcher>() != null))
                return;

            CreateSceneSwitcher(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (openedForUpdate && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void EnsureBuildSettings()
    {
        string[] requiredPaths = { TwoDScenePath, ThreeDScenePath };
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        foreach (string path in requiredPaths)
        {
            if (!scenes.Any(scene => scene.path == path))
                scenes = scenes.Concat(new[] { new EditorBuildSettingsScene(path, true) }).ToArray();
        }
        EditorBuildSettings.scenes = scenes;
    }
}
#endif
