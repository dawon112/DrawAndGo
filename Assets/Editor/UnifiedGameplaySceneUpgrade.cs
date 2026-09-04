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
    private const string SlowStainMaterialPath = "Assets/Materials/DuduSlowStain.mat";
    private const string ReverseStainMaterialPath = "Assets/Materials/DuduReverseStain.mat";

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
                EnsureVerticalObstacle(scene, existingDuduSurface);
                EnsureStainObstacles(scene, existingDuduSurface);
                GameObject existingDudu = scene.GetRootGameObjects().First(root => root.name == "Dudu");
                Animator animator = existingDudu.GetComponentInChildren<Animator>(true);
                SpriteRenderer spriteRenderer = existingDudu.GetComponentInChildren<SpriteRenderer>(true);
                DuduSurfaceMovement existingDuduMovement = existingDudu.GetComponent<DuduSurfaceMovement>();
                existingDuduMovement.Configure(existingDuduSurface, animator, spriteRenderer);
                EnsureGroundLine(scene, existingDuduSurface, existingDuduMovement);
                EnsureHomingEnemy(scene, existingDuduSurface, existingDuduMovement);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                return;
            }

            GameObject haru = scene.GetRootGameObjects().First(root => root.name == "Player3D");
            Player3DMovement haruMovement = haru.GetComponent<Player3DMovement>();
            Player3DLook haruLook = haru.GetComponentInChildren<Player3DLook>(true);
            Camera haruCamera = haruLook.GetComponent<Camera>();

            DuduSurface surface = CreateSurface(scene);
            EnsureVerticalObstacle(scene, surface);
            EnsureStainObstacles(scene, surface);
            DuduSurfaceMovement duduMovement = CreateDudu(scene, surface);
            EnsureGroundLine(scene, surface, duduMovement);
            EnsureHomingEnemy(scene, surface, duduMovement);
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

    private static void EnsureGroundLine(Scene scene, DuduSurface surface, DuduSurfaceMovement dudu)
    {
        GameObject groundLine = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Dudu Ground Line");
        if (groundLine == null)
        {
            groundLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundLine.name = "Dudu Ground Line";
            SceneManager.MoveGameObjectToScene(groundLine, scene);
        }

        const float groundY = -2.4f;
        // Measure the character in world units along the paper's horizontal axis.
        // Transform the box's axes so this also works for a rotated paper surface.
        BoxCollider characterCollider = dudu.GetComponent<BoxCollider>();
        Vector3 right = surface.Right.normalized;
        Transform characterTransform = characterCollider.transform;
        Vector3 size = characterCollider.size;
        float characterWidth =
            Mathf.Abs(Vector3.Dot(right, characterTransform.TransformVector(Vector3.right * size.x))) +
            Mathf.Abs(Vector3.Dot(right, characterTransform.TransformVector(Vector3.up * size.y))) +
            Mathf.Abs(Vector3.Dot(right, characterTransform.TransformVector(Vector3.forward * size.z)));
        float gap = Mathf.Min(characterWidth * 3f, surface.Width - 0.01f);
        groundLine.transform.position = surface.SurfaceToWorld(new Vector2(-gap * 0.5f, groundY));
        groundLine.transform.rotation = surface.transform.rotation;
        groundLine.transform.localScale = new Vector3(surface.Width - gap, 0.14f, 0.06f);
        groundLine.GetComponent<Renderer>().sharedMaterial = EnsureGroundLineMaterial();
    }

    private static void EnsureVerticalObstacle(Scene scene, DuduSurface surface)
    {
        const string obstacleName = "Vertical Obstacle";
        GameObject obstacle = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == obstacleName);
        if (obstacle != null)
            return;

        obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.name = obstacleName;
        SceneManager.MoveGameObjectToScene(obstacle, scene);
        obstacle.transform.localScale = new Vector3(0.9f, 0.9f, 0.12f);
        obstacle.GetComponent<Renderer>().sharedMaterial = EnsureGroundLineMaterial();

        Rigidbody body = obstacle.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.FreezeRotation;

        DuduMovingObstacle movement = obstacle.AddComponent<DuduMovingObstacle>();
        movement.Configure(
            surface,
            DuduMovingObstacle.MovementAxis.Vertical,
            new Vector2(1.5f, -0.3f),
            2.8f,
            3f);
    }

    private static void EnsureHomingEnemy(
        Scene scene,
        DuduSurface surface,
        DuduSurfaceMovement duduMovement)
    {
        const string enemyName = "Homing Enemy";
        GameObject enemy = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == enemyName);
        if (enemy != null)
            return;

        enemy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        enemy.name = enemyName;
        SceneManager.MoveGameObjectToScene(enemy, scene);
        enemy.transform.localScale = new Vector3(0.55f, 1.4f, 0.12f);
        enemy.GetComponent<Renderer>().sharedMaterial = EnsureGroundLineMaterial();
        enemy.GetComponent<Collider>().isTrigger = true;

        DuduHomingShooter shooter = enemy.AddComponent<DuduHomingShooter>();
        shooter.Configure(
            surface,
            duduMovement,
            new Vector2(-1f, 1.5f),
            EnsureGroundLineMaterial());
    }

    private static void EnsureStainObstacles(Scene scene, DuduSurface surface)
    {
        const string slowStainName = "Slow Stain Obstacle";
        GameObject slowStain = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == slowStainName);
        if (slowStain == null)
        {
            slowStain = scene.GetRootGameObjects()
                .FirstOrDefault(root => root.name == "Stain Obstacle");
            if (slowStain == null)
            {
                slowStain = CreateStainObstacle(
                    scene,
                    surface,
                    slowStainName,
                    new Vector2(-3f, -2.2f),
                    DuduStainObstacle.EffectType.Slow,
                    EnsureSlowStainMaterial());
            }
            else
            {
                slowStain.name = slowStainName;
                slowStain.GetComponent<Renderer>().sharedMaterial = EnsureSlowStainMaterial();
                slowStain.GetComponent<DuduStainObstacle>().Configure(
                    surface,
                    new Vector2(-3f, -2.2f),
                    DuduStainObstacle.EffectType.Slow);
            }
        }

        const string reverseStainName = "Reverse Stain Obstacle";
        GameObject reverseStain = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == reverseStainName);
        if (reverseStain == null)
        {
            CreateStainObstacle(
                scene,
                surface,
                reverseStainName,
                new Vector2(0f, -2.2f),
                DuduStainObstacle.EffectType.ReverseControls,
                EnsureReverseStainMaterial());
        }
    }

    private static GameObject CreateStainObstacle(
        Scene scene,
        DuduSurface surface,
        string stainName,
        Vector2 position,
        DuduStainObstacle.EffectType effectType,
        Material material)
    {
        GameObject stain = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        stain.name = stainName;
        SceneManager.MoveGameObjectToScene(stain, scene);
        stain.transform.localScale = new Vector3(0.7f, 0.7f, 0.08f);
        stain.GetComponent<Renderer>().sharedMaterial = material;
        stain.GetComponent<Collider>().isTrigger = true;

        DuduStainObstacle stainObstacle = stain.AddComponent<DuduStainObstacle>();
        stainObstacle.Configure(surface, position, effectType);
        return stain;
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

    private static Material EnsureSlowStainMaterial()
    {
        return EnsureStainMaterial(
            SlowStainMaterialPath,
            "DuduSlowStain",
            Color.red);
    }

    private static Material EnsureReverseStainMaterial()
    {
        return EnsureStainMaterial(
            ReverseStainMaterialPath,
            "DuduReverseStain",
            Color.blue);
    }

    private static Material EnsureStainMaterial(string path, string materialName, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        material = new Material(shader) { name = materialName };
        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
#endif
