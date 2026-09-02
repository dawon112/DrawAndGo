#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class UnifiedGameplaySceneUpgrade
{
    private const string MainScenePath = "Assets/Scenes/Player3DScene.unity";
    private const string Legacy2DScenePath = "Assets/Scenes/SideScrollerPrototype.unity";
    private const string KnightPath = "Assets/Art/Characters/Knight/Knight.png";
    private const string KnightControllerPath = "Assets/Animations/Player/Knight.controller";
    private const string PaperMaterialPath = "Assets/Materials/PaperBackground.mat";
    private const string GroundLineMaterialPath = "Assets/Materials/DuduGroundLine.mat";

    static UnifiedGameplaySceneUpgrade()
    {
        EditorApplication.delayCall += ApplyNow;
    }

    public static void ApplyNow()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode ||
            !File.Exists(MainScenePath))
            return;

        UpgradeMainScene();
        RemoveLegacySceneSwitcher();
        ConfigureBuildSettings();
        AssetDatabase.SaveAssets();
    }

    private static void UpgradeMainScene()
    {
        Scene scene = OpenSceneForUpdate(MainScenePath, out bool closeAfter);
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            foreach (GameObject root in roots.Where(root => root.name == "View Scene Switcher"))
                Object.DestroyImmediate(root);

            GameObject existingSurface = scene.GetRootGameObjects()
                .FirstOrDefault(root => root.name == "DuduSurface");
            if (existingSurface != null)
            {
                existingSurface.GetComponent<Renderer>().sharedMaterial = EnsurePaperMaterial();
                DuduSurface existingDuduSurface = existingSurface.GetComponent<DuduSurface>();
                EnsureGroundLine(scene, existingDuduSurface);
                GameObject existingDudu = scene.GetRootGameObjects().First(root => root.name == "Dudu");
                Animator animator = existingDudu.GetComponentInChildren<Animator>(true);
                SpriteRenderer spriteRenderer = existingDudu.GetComponentInChildren<SpriteRenderer>(true);
                existingDudu.GetComponent<DuduSurfaceMovement>().Configure(existingDuduSurface, animator, spriteRenderer);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                return;
            }

            GameObject haru = scene.GetRootGameObjects().First(root => root.name == "Player3D");
            Player3DMovement haruMovement = haru.GetComponent<Player3DMovement>();
            Player3DLook haruLook = haru.GetComponentInChildren<Player3DLook>(true);
            Camera haruCamera = haruLook.GetComponent<Camera>();

            DuduSurface surface = CreateSurface(scene);
            EnsureGroundLine(scene, surface);
            DuduSurfaceMovement duduMovement = CreateDudu(scene, surface);
            Camera duduCamera = CreateDuduCamera(scene, surface);

            GameObject managerObject = new GameObject("GameViewManager");
            SceneManager.MoveGameObjectToScene(managerObject, scene);
            GameViewManager manager = managerObject.AddComponent<GameViewManager>();
            manager.Configure(haruCamera, haruMovement, haruLook, duduCamera, duduMovement);

            duduCamera.gameObject.SetActive(false);
            duduMovement.enabled = false;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (closeAfter && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static DuduSurface CreateSurface(Scene scene)
    {
        GameObject surfaceObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        surfaceObject.name = "DuduSurface";
        SceneManager.MoveGameObjectToScene(surfaceObject, scene);
        surfaceObject.transform.position = new Vector3(0f, 3f, 8f);
        surfaceObject.transform.rotation = Quaternion.identity;
        surfaceObject.transform.localScale = new Vector3(10f, 6f, 1f);

        Renderer renderer = surfaceObject.GetComponent<Renderer>();
        renderer.sharedMaterial = EnsurePaperMaterial();

        DuduSurface surface = surfaceObject.AddComponent<DuduSurface>();
        return surface;
    }

    private static DuduSurfaceMovement CreateDudu(Scene scene, DuduSurface surface)
    {
        GameObject dudu = new GameObject("Dudu");
        SceneManager.MoveGameObjectToScene(dudu, scene);

        GameObject visual = new GameObject("Dudu Sprite");
        visual.transform.SetParent(dudu.transform, false);
        visual.transform.localPosition = new Vector3(0f, -0.75f, 0f);
        visual.transform.localScale = Vector3.one * 1.5f;
        SpriteRenderer spriteRenderer = visual.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = AssetDatabase.LoadAllAssetsAtPath(KnightPath).OfType<Sprite>()
            .First(sprite => sprite.name == "Knight_0");
        spriteRenderer.sortingOrder = 10;
        Animator animator = visual.AddComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(KnightControllerPath);

        BoxCollider collider = dudu.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.8f, 1.5f, 0.08f);

        DuduSurfaceMovement movement = dudu.AddComponent<DuduSurfaceMovement>();
        movement.Configure(surface, animator, spriteRenderer);
        return movement;
    }

    private static void EnsureGroundLine(Scene scene, DuduSurface surface)
    {
        GameObject groundLine = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Dudu Ground Line");
        if (groundLine == null)
        {
            groundLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundLine.name = "Dudu Ground Line";
            SceneManager.MoveGameObjectToScene(groundLine, scene);
        }

        const float groundY = -2.4f;
        groundLine.transform.position = surface.SurfaceToWorld(new Vector2(0f, groundY));
        groundLine.transform.rotation = surface.transform.rotation;
        groundLine.transform.localScale = new Vector3(surface.Width, 0.14f, 0.06f);
        groundLine.GetComponent<Renderer>().sharedMaterial = EnsureGroundLineMaterial();
    }

    private static Camera CreateDuduCamera(Scene scene, DuduSurface surface)
    {
        GameObject cameraObject = new GameObject("DuduCamera");
        cameraObject.tag = "Untagged";
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 3.5f;
        camera.depth = 1f;
        DuduCameraController controller = cameraObject.AddComponent<DuduCameraController>();
        controller.SetSurface(surface);
        return camera;
    }

    private static void RemoveLegacySceneSwitcher()
    {
        if (!File.Exists(Legacy2DScenePath))
            return;

        Scene scene = OpenSceneForUpdate(Legacy2DScenePath, out bool closeAfter);
        try
        {
            GameObject switcher = scene.GetRootGameObjects()
                .FirstOrDefault(root => root.name == "View Scene Switcher");
            if (switcher == null)
                return;
            Object.DestroyImmediate(switcher);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (closeAfter && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void ConfigureBuildSettings()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(MainScenePath, true),
            new EditorBuildSettingsScene(Legacy2DScenePath, false)
        };
    }

    private static Scene OpenSceneForUpdate(string path, out bool closeAfter)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        closeAfter = !scene.IsValid() || !scene.isLoaded;
        return closeAfter ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : scene;
    }

    private static Material EnsurePaperMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(PaperMaterialPath);
        if (material != null)
            return material;

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        material = new Material(shader) { name = "PaperBackground" };
        material.color = Color.white;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(material, PaperMaterialPath);
        return material;
    }

    private static Material EnsureGroundLineMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(GroundLineMaterialPath);
        if (material != null)
            return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        material = new Material(shader) { name = "DuduGroundLine" };
        material.color = Color.black;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.black);
        AssetDatabase.CreateAsset(material, GroundLineMaterialPath);
        return material;
    }
}
#endif
