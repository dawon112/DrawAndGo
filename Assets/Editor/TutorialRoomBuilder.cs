#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TutorialRoomBuilder
{
    public const string ScenePath = "Assets/Scenes/Tutorial.unity";
    private const float CorridorWidth = 5f;
    private const float WallHeight = 6f;
    private const float FirstSectionGapWidth = 4f;
    private const float SecondSectionGapWidth = 2.88f;
    private const float HaruVisibleSurfaceOffset = 0.08f;
    private const string VisualBuildMarker = "Serpentine Corridor v15";

    [InitializeOnLoadMethod]
    private static void BuildMissingTutorialScene()
    {
        EditorApplication.delayCall += TryBuildMissingScene;
    }

    private static void TryBuildMissingScene()
    {
        bool sceneMissing = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && sceneMissing)
            Build();
    }

    [MenuItem("Tools/Draw And Go/Build Tutorial Room")]
    public static void Build()
    {
        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
        {
            const string source = "Assets/Scenes/Player3DScene.unity";
            if (!AssetDatabase.CopyAsset(source, ScenePath))
                throw new System.InvalidOperationException("Could not create the tutorial scene copy.");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        PreserveCoreActors(scene);
        RemovePrototypeLevel(scene);

        DuduSurfaceMovement dudu = CloneOriginalDudu(scene);
        DuduCameraController duduCamera = FindInScene<DuduCameraController>(scene);
        Transform strokeRoot = FindNamed(scene, "DrawingRoot")?.transform;
        if (dudu == null || duduCamera == null || strokeRoot == null)
            throw new System.InvalidOperationException("Tutorial source scene is missing Dudu, DuduCamera, or DrawingRoot.");

        Material paper = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PaperBackground.mat");
        Material ink = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/DuduGroundLine.mat");
        Material wood = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Stylized Wood Textures/Materials/Chevron/Chevron.mat");
        GameObject levelRoot = new GameObject("Long Narrow Tutorial Corridor");
        SceneManager.MoveGameObjectToScene(levelRoot, scene);

        Transform environment = Child(levelRoot.transform, "Environment");
        Child(environment, VisualBuildMarker);
        Transform walls = Child(environment, "Walls");
        Transform ground = Child(environment, "Section Ground");
        Transform players = Child(levelRoot.transform, "Players");

        dudu.transform.SetParent(players, true);
        Player3DMovement haru = FindInScene<Player3DMovement>(scene);
        if (haru != null)
        {
            Transform haruRoot = haru.transform.root;
            haruRoot.SetParent(players, true);
            HaruDrawingController drawing = haru.GetComponentInChildren<HaruDrawingController>(true);
            if (drawing != null)
            {
                SerializedObject drawingData = new SerializedObject(drawing);
                drawingData.FindProperty("maxDrawDistance").floatValue = 20f;
                drawingData.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        DuduSurface[] route = CreateSerpentineCorridor(environment, walls, strokeRoot, paper, wood, ink);
        LinkSections(route);
        for (int i = 0; i < route.Length; i++)
            CreateGround($"Dudu Route Line {i + 1}", route[i], 0f, route[i].Width, ground, ink);
        DuduSurface corridorSurface = route[0];

        Vector2 startPosition = new Vector2(corridorSurface.GetMinimumX(0.4f) + 0.6f, -1.65f);
        dudu.Configure(corridorSurface, dudu.GetComponentInChildren<Animator>(), dudu.GetComponentInChildren<SpriteRenderer>());
        SerializedObject duduData = new SerializedObject(dudu);
        duduData.FindProperty("surfacePosition").vector2Value = startPosition;
        duduData.ApplyModifiedPropertiesWithoutUndo();
        dudu.SetSurface(corridorSurface);
        duduCamera.SetSurface(corridorSurface);
        duduCamera.SetTarget(dudu.transform);

        GameViewManager viewManager = FindInScene<GameViewManager>(scene);
        if (viewManager != null && haru != null)
            viewManager.Configure(haru.GetComponentInChildren<Camera>(true), haru,
                haru.GetComponentInChildren<Player3DLook>(true), duduCamera.GetComponent<Camera>(), dudu);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"Tutorial rebuilt at {ScenePath}: empty serpentine corridor with Haru, Dudu and exit door.");
    }

    public static void BuildAndExit()
    {
        try
        {
            Build();
            EditorApplication.Exit(0);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void RemovePrototypeLevel(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Corner Room Level" || root.name == "Follow My Line Tutorial" ||
                root.name == "Long Narrow Tutorial Corridor")
                Object.DestroyImmediate(root);
        }

        foreach (DuduStainObstacle obstacle in FindAllInScene<DuduStainObstacle>(scene))
            Object.DestroyImmediate(obstacle.gameObject);
        foreach (DuduMovingObstacle obstacle in FindAllInScene<DuduMovingObstacle>(scene))
            Object.DestroyImmediate(obstacle.gameObject);
        foreach (MovingEraserObstacle obstacle in FindAllInScene<MovingEraserObstacle>(scene))
            Object.DestroyImmediate(obstacle.gameObject);
        foreach (DuduHomingShooter obstacle in FindAllInScene<DuduHomingShooter>(scene))
            Object.DestroyImmediate(obstacle.gameObject);
        foreach (LineMagnet obstacle in FindAllInScene<LineMagnet>(scene))
            Object.DestroyImmediate(obstacle.gameObject);
        foreach (NoDrawZone obstacle in FindAllInScene<NoDrawZone>(scene))
            Object.DestroyImmediate(obstacle.gameObject);
        foreach (WindZone obstacle in FindAllInScene<WindZone>(scene))
            Object.DestroyImmediate(obstacle.gameObject);
        foreach (HotZone obstacle in FindAllInScene<HotZone>(scene))
            Object.DestroyImmediate(obstacle.gameObject);
        foreach (SurfaceCoinPickup coin in FindAllInScene<SurfaceCoinPickup>(scene))
            Object.DestroyImmediate(coin.gameObject);
        foreach (DuduSurfaceMovement existingDudu in FindAllInScene<DuduSurfaceMovement>(scene))
            Object.DestroyImmediate(existingDudu.gameObject);

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Floors" || root.name == "Floor3D" || root.name == "Ground3D")
                Object.DestroyImmediate(root);
        }
    }

    private static void PreserveCoreActors(Scene scene)
    {
        Player3DMovement existingHaru = FindInScene<Player3DMovement>(scene);
        if (existingHaru != null && (existingHaru.transform.root.name == "Follow My Line Tutorial" ||
            existingHaru.transform.root.name == "Long Narrow Tutorial Corridor"))
            existingHaru.transform.SetParent(null, true);
    }

    private static DuduSurfaceMovement CloneOriginalDudu(Scene targetScene)
    {
        const string sourcePath = "Assets/Scenes/Player3DScene.unity";
        Scene sourceScene = EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Additive);
        try
        {
            DuduSurfaceMovement sourceDudu = FindInScene<DuduSurfaceMovement>(sourceScene);
            if (sourceDudu == null)
                throw new System.InvalidOperationException("Player3DScene is missing the original Dudu player.");

            GameObject clone = Object.Instantiate(sourceDudu.gameObject);
            clone.name = sourceDudu.gameObject.name;
            SceneManager.MoveGameObjectToScene(clone, targetScene);
            return clone.GetComponent<DuduSurfaceMovement>();
        }
        finally
        {
            EditorSceneManager.CloseScene(sourceScene, true);
        }
    }

    private static DuduSurface[] CreateSections(Transform parent, Transform strokeRoot, Material paper)
    {
        Vector3[] corners =
        {
            new Vector3(-7f, 3f, 16f),
            new Vector3(17f, 3f, 16f),
            new Vector3(17f, 3f, 0f),
            new Vector3(-7f, 3f, 0f)
        };
        DuduSurface[] result = new DuduSurface[4];
        for (int i = 0; i < result.Length; i++)
        {
            Vector3 a = corners[i];
            Vector3 b = corners[(i + 1) % corners.Length];
            Vector3 right = (b - a).normalized;
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(right, Vector3.up), Vector3.up);
            float width = Vector3.Distance(a, b);
            GameObject wall = Cube($"Section{i + 1}", (a + b) * 0.5f, rotation,
                new Vector3(width, WallHeight, 0.12f), paper, parent);
            wall.layer = LayerMask.NameToLayer("DrawingSurface");
            DuduSurface surface = wall.AddComponent<DuduSurface>();
            SerializedObject data = new SerializedObject(surface);
            data.FindProperty("width").floatValue = width;
            data.FindProperty("height").floatValue = WallHeight;
            data.ApplyModifiedPropertiesWithoutUndo();
            DrawingSurface drawingSurface = wall.AddComponent<DrawingSurface>();
            drawingSurface.SetStrokeRoot(strokeRoot);
            result[i] = surface;
        }
        return result;
    }

    private static void LinkSections(DuduSurface[] sections)
    {
        for (int i = 0; i < sections.Length; i++)
        {
            sections[i].previousSurface = i > 0 ? sections[i - 1] : null;
            sections[i].nextSurface = i < sections.Length - 1 ? sections[i + 1] : null;
        }
    }

    private static void CreateSectionGround(Transform parent, DuduSurface[] sections, Material ink)
    {
        const float firstGapCenter = 6.5f;
        float firstMinimum = -sections[0].Width * 0.5f;
        float firstMaximum = sections[0].Width * 0.5f;
        float firstGapMinimum = firstGapCenter - FirstSectionGapWidth * 0.5f;
        float firstGapMaximum = firstGapCenter + FirstSectionGapWidth * 0.5f;
        CreateGroundBetween("Section1 - Ground Before Right Gap", sections[0],
            firstMinimum, firstGapMinimum, parent, ink);
        CreateGroundBetween("Section1 - Ground After Right Gap", sections[0],
            firstGapMaximum, firstMaximum, parent, ink);

        float segmentWidth = (sections[1].Width - SecondSectionGapWidth) * 0.5f;
        float centerOffset = (SecondSectionGapWidth + segmentWidth) * 0.5f;
        CreateGround("Section2 - Ground Before Gap", sections[1], -centerOffset, segmentWidth, parent, ink);
        CreateGround("Section2 - Ground After Gap", sections[1], centerOffset, segmentWidth, parent, ink);

        CreateGround("Section3 - Knockback Ground", sections[2], 0f, sections[2].Width, parent, ink);
        CreateGround("Section4 - Slow Ground", sections[3], 0f, sections[3].Width, parent, ink);
    }

    private static void CreateGroundBetween(string name, DuduSurface surface, float minimum, float maximum,
        Transform parent, Material ink)
    {
        float width = maximum - minimum;
        CreateGround(name, surface, (minimum + maximum) * 0.5f, width, parent, ink);
    }

    private static void CreateGround(string name, DuduSurface surface, float x, float width, Transform parent, Material ink)
    {
        Cube(name, surface.SurfaceToWorld(new Vector2(x, -2.4f)), surface.transform.rotation,
            new Vector3(width, 0.14f, 0.18f), ink, parent);
    }

    private static TutorialStarCollectible[] CreateStars(Transform parent, DuduSurface[] sections)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Collectibles/Coin.prefab");
        if (prefab == null)
            throw new System.InvalidOperationException("The original Player3DScene coin prefab is missing.");
        int[] sectionIndexes = { 1, 2, 3 };
        float[] xPositions = { 7.2f, 7f, 3.2f };
        TutorialStarCollectible[] stars = new TutorialStarCollectible[3];
        for (int i = 0; i < stars.Length; i++)
        {
            GameObject star = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            star.name = $"Star_0{i + 1}_Section{sectionIndexes[i] + 1}";
            star.transform.SetParent(parent);
            DuduSurface surface = sections[sectionIndexes[i]];
            star.transform.SetPositionAndRotation(
                surface.SurfaceToWorld(new Vector2(xPositions[i], -1.35f)) +
                    surface.Normal.normalized * HaruVisibleSurfaceOffset,
                surface.transform.rotation);
            SurfaceCoinPickup originalPickup = star.GetComponent<SurfaceCoinPickup>();
            if (originalPickup != null)
                Object.DestroyImmediate(originalPickup);
            stars[i] = star.AddComponent<TutorialStarCollectible>();
        }
        return stars;
    }

    private static void CreateThirdSectionObstacles(Transform parent, DuduSurface surface)
    {
        GameObject verticalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Obstacles/Vertical Obstacle.prefab");
        GameObject vertical = PrefabUtility.InstantiatePrefab(verticalPrefab) as GameObject;
        vertical.name = "Vertical Obstacle - Section 3";
        vertical.transform.SetParent(parent);
        // Its lowest path point is y=-1.4: exactly one surface unit above the y=-2.4 floor line.
        DuduMovingObstacle verticalMovement = vertical.GetComponent<DuduMovingObstacle>();
        verticalMovement.Configure(
            surface, DuduMovingObstacle.MovementAxis.Vertical, new Vector2(-2f, -0.6f), 1.6f, 4.5f);
        verticalMovement.SetSurfaceNormalOffset(HaruVisibleSurfaceOffset);

        GameObject reversePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Obstacles/Reverse Stain Obstacle.prefab");
        GameObject reverse = PrefabUtility.InstantiatePrefab(reversePrefab) as GameObject;
        reverse.name = "Reverse Obstacle - Section 3";
        reverse.transform.SetParent(parent);
        DuduStainObstacle reverseStain = reverse.GetComponent<DuduStainObstacle>();
        reverseStain.Configure(
            surface, new Vector2(3f, -2.1f), DuduStainObstacle.EffectType.ReverseControls);
        reverseStain.SetSurfaceNormalOffset(HaruVisibleSurfaceOffset);
    }

    private static void CreateFinalSectionObstacles(Transform parent, DuduSurface surface)
    {
        GameObject slowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Obstacles/Slow Stain Obstacle.prefab");
        GameObject slow = PrefabUtility.InstantiatePrefab(slowPrefab) as GameObject;
        slow.name = "Slow Obstacle - Section 4";
        slow.transform.SetParent(parent);
        DuduStainObstacle slowStain = slow.GetComponent<DuduStainObstacle>();
        slowStain.Configure(
            surface, new Vector2(-6.2f, -2.1f), DuduStainObstacle.EffectType.Slow);
        slowStain.SetSurfaceNormalOffset(HaruVisibleSurfaceOffset);

        GameObject eraserPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Obstacles/Moving Eraser.prefab");
        float[] xPositions = { -4.2f, -1.8f, 0.6f };
        for (int i = 0; i < xPositions.Length; i++)
        {
            GameObject eraser = PrefabUtility.InstantiatePrefab(eraserPrefab) as GameObject;
            eraser.name = $"Moving Eraser 0{i + 1} - Section 4";
            eraser.transform.SetParent(parent);
            eraser.transform.SetPositionAndRotation(
                surface.SurfaceToWorld(new Vector2(xPositions[i], 1.45f)) +
                    surface.Normal.normalized * HaruVisibleSurfaceOffset,
                surface.transform.rotation);
            eraser.GetComponent<MovingEraserObstacle>().SetSurfaceNormalOffset(HaruVisibleSurfaceOffset);
        }
    }

    private static void CreateSecondSectionNoDrawZone(Transform parent, DuduSurface surface)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Obstacles/Zones/NoDrawZone.prefab");
        GameObject zone = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        zone.name = "No Draw Zone - Section 2 Gap";
        zone.transform.SetParent(parent);
        zone.transform.localScale = new Vector3(0.223f, 0.27f, 0.003214286f);
        zone.transform.SetPositionAndRotation(
            surface.SurfaceToWorld(new Vector2(0f, -1.25f)) + surface.Normal.normalized * 0.08f,
            surface.transform.rotation);
        zone.GetComponent<NoDrawZone>().Configure(surface);
    }

    private static void CreateDoorwayOpening(Transform parent, DuduSurface surface,
        Transform strokeRoot, Material paper, float doorX)
    {
        const float doorWidth = 1.8f;
        const float doorHeight = 4f;
        const float doorY = -0.3f;
        float wallMinimum = -surface.Width * 0.5f;
        float wallMaximum = surface.Width * 0.5f;
        float doorMinimum = doorX - doorWidth * 0.5f;
        float doorMaximum = doorX + doorWidth * 0.5f;
        float leftWidth = doorMinimum - wallMinimum;
        float rightWidth = wallMaximum - doorMaximum;
        float leftCenter = (wallMinimum + doorMinimum) * 0.5f;
        float rightCenter = (doorMaximum + wallMaximum) * 0.5f;

        Renderer fullWallRenderer = surface.GetComponent<Renderer>();
        if (fullWallRenderer != null) fullWallRenderer.enabled = false;
        Collider fullWallCollider = surface.GetComponent<Collider>();
        if (fullWallCollider != null) Object.DestroyImmediate(fullWallCollider);
        DrawingSurface fullDrawingSurface = surface.GetComponent<DrawingSurface>();
        if (fullDrawingSurface != null) Object.DestroyImmediate(fullDrawingSurface);

        CreateWallSegment("Doorway Wall Left", leftCenter, 0f, leftWidth, WallHeight, true);
        CreateWallSegment("Doorway Wall Right", rightCenter, 0f, rightWidth, WallHeight, true);
        CreateWallSegment("Doorway Wall Top", doorX, 2.35f, doorWidth, 1.3f, true);
        CreateWallSegment("Doorway Wall Bottom Filler", doorX, -2.65f, doorWidth, 0.7f, false);

        void CreateWallSegment(string name, float x, float y, float width, float height, bool solid)
        {
            GameObject segment = Cube(name, surface.SurfaceToWorld(new Vector2(x, y)),
                surface.transform.rotation, new Vector3(width, height, 0.12f), paper, parent);
            segment.layer = surface.gameObject.layer;
            if (solid)
            {
                DrawingSurface drawingSurface = segment.AddComponent<DrawingSurface>();
                drawingSurface.SetStrokeRoot(strokeRoot);
            }
            else
            {
                Collider collider = segment.GetComponent<Collider>();
                if (collider != null) Object.DestroyImmediate(collider);
            }
        }
    }

    private static GameObject CreateDoor(Transform parent, DuduSurface surface, Material ink, float doorX)
    {
        GameObject door = Cube("Locked Door - Opens At 3 Stars",
            surface.SurfaceToWorld(new Vector2(doorX, -0.3f)) + surface.Normal * 0.12f,
            surface.transform.rotation, new Vector3(1.8f, 4f, 0.5f), ink, parent);

        CreateDoorFrame("Door Frame Left", new Vector2(doorX - 1f, -0.3f), new Vector3(0.15f, 4f, 0.6f));
        CreateDoorFrame("Door Frame Right", new Vector2(doorX + 1f, -0.3f), new Vector3(0.15f, 4f, 0.6f));
        CreateDoorFrame("Door Frame Top", new Vector2(doorX, 1.7f), new Vector3(2.15f, 0.15f, 0.6f));

        GameObject visualPrefab = Resources.Load<GameObject>("FreeWoodDoor");
        if (visualPrefab != null)
        {
            GameObject visual = PrefabUtility.InstantiatePrefab(visualPrefab) as GameObject;
            visual.name = "FreeWoodDoor Visual";
            visual.transform.SetParent(door.transform, false);
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            FitVisualToDoor(door, visual);
            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (MonoBehaviour behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            door.GetComponent<Renderer>().enabled = false;
        }
        return door;

        void CreateDoorFrame(string name, Vector2 position, Vector3 size)
        {
            GameObject frame = Cube(name, surface.SurfaceToWorld(position) + surface.Normal * 0.15f,
                surface.transform.rotation, size, ink, parent);
            Object.DestroyImmediate(frame.GetComponent<Collider>());
        }
    }

    private static void FitVisualToDoor(GameObject door, GameObject visual)
    {
        Renderer doorRenderer = door.GetComponent<Renderer>();
        Renderer[] visualRenderers = visual.GetComponentsInChildren<Renderer>(true);
        if (doorRenderer == null || visualRenderers.Length == 0) return;

        Bounds targetBounds = doorRenderer.bounds;
        Vector3 parentScale = door.transform.lossyScale;
        visual.transform.localScale = new Vector3(
            1f / Mathf.Max(0.001f, Mathf.Abs(parentScale.x)),
            1f / Mathf.Max(0.001f, Mathf.Abs(parentScale.y)),
            1f / Mathf.Max(0.001f, Mathf.Abs(parentScale.z)));

        Bounds visualBounds = visualRenderers[0].bounds;
        for (int i = 1; i < visualRenderers.Length; i++) visualBounds.Encapsulate(visualRenderers[i].bounds);
        Vector3 fit = new Vector3(
            targetBounds.size.x / Mathf.Max(0.001f, visualBounds.size.x),
            targetBounds.size.y / Mathf.Max(0.001f, visualBounds.size.y),
            targetBounds.size.z / Mathf.Max(0.001f, visualBounds.size.z));
        visual.transform.localScale = Vector3.Scale(visual.transform.localScale, fit);
        visual.transform.localScale = Vector3.Scale(
            visual.transform.localScale, new Vector3(0.55f, 1f, 0.35f));

        visualBounds = visualRenderers[0].bounds;
        for (int i = 1; i < visualRenderers.Length; i++) visualBounds.Encapsulate(visualRenderers[i].bounds);
        visual.transform.position += targetBounds.center - visualBounds.center;
    }

    private static DuduSurface[] CreateSerpentineCorridor(Transform environment, Transform walls,
        Transform strokeRoot, Material paper, Material wood, Material ink)
    {
        Vector3[] points =
        {
            new Vector3(-10f, 0f, 12f),
            new Vector3(10f, 0f, 12f),
            new Vector3(10f, 0f, 5f),
            new Vector3(-3f, 0f, 5f),
            new Vector3(-3f, 0f, -2f),
            new Vector3(10f, 0f, -2f),
            new Vector3(10f, 0f, -10f),
            new Vector3(-10f, 0f, -10f)
        };

        Transform floors = Child(environment, "Floor And Ceiling");
        DuduSurface[] route = new DuduSurface[points.Length - 1];
        for (int i = 0; i < route.Length; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[i + 1];
            Vector3 right = (b - a).normalized;
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(right, Vector3.up), Vector3.up);
            float length = Vector3.Distance(a, b);
            Vector3 mainCenter = (a + b) * 0.5f + Vector3.up * (WallHeight * 0.5f);

            GameObject mainWall = Cube($"Dudu Route Wall {i + 1}", mainCenter, rotation,
                new Vector3(length, WallHeight, 0.12f), paper, walls);
            mainWall.layer = LayerMask.NameToLayer("DrawingSurface");
            DuduSurface surface = mainWall.AddComponent<DuduSurface>();
            SerializedObject surfaceData = new SerializedObject(surface);
            surfaceData.FindProperty("width").floatValue = length;
            surfaceData.FindProperty("height").floatValue = WallHeight;
            surfaceData.ApplyModifiedPropertiesWithoutUndo();
            mainWall.AddComponent<DrawingSurface>().SetStrokeRoot(strokeRoot);
            route[i] = surface;

            Vector3 inward = surface.Normal.normalized;
            Vector3 oppositeCenter = mainCenter + inward * CorridorWidth;
            float oppositeLength = Mathf.Max(1f, length - CorridorWidth);
            GameObject opposite = Cube($"Opposite White Wall {i + 1}", oppositeCenter, rotation,
                new Vector3(oppositeLength, WallHeight, 0.12f), paper, walls);
            opposite.layer = LayerMask.NameToLayer("DrawingSurface");
            opposite.AddComponent<DrawingSurface>().SetStrokeRoot(strokeRoot);

            Vector3 shellCenter = (a + b) * 0.5f + inward * (CorridorWidth * 0.5f);
            Cube($"Wood Floor {i + 1}", shellCenter + Vector3.down * 0.1f, rotation,
                new Vector3(length + CorridorWidth, 0.2f, CorridorWidth), wood, floors);
            Cube($"Wood Ceiling {i + 1}", shellCenter + Vector3.up * (WallHeight + 0.06f), rotation,
                new Vector3(length + CorridorWidth, 0.12f, CorridorWidth), wood, floors);
        }

        CreateCorridorEnd("Start End Wall", points[0], route[0], paper, walls, strokeRoot, false, ink);
        CreateCorridorEnd("Exit Door", points[points.Length - 1], route[route.Length - 1],
            paper, walls, strokeRoot, true, ink);
        return route;
    }

    private static void CreateCorridorEnd(string name, Vector3 endpoint, DuduSurface adjacent,
        Material paper, Transform walls, Transform strokeRoot, bool addDoor, Material ink)
    {
        Vector3 right = adjacent.Right.normalized;
        Vector3 inward = adjacent.Normal.normalized;
        Vector3 center = endpoint + inward * (CorridorWidth * 0.5f) + Vector3.up * (WallHeight * 0.5f);
        Quaternion rotation = Quaternion.LookRotation(right, Vector3.up);
        GameObject endWall = Cube(name + " Frame", center, rotation,
            new Vector3(CorridorWidth, WallHeight, 0.12f), paper, walls);
        endWall.layer = LayerMask.NameToLayer("DrawingSurface");
        endWall.AddComponent<DrawingSurface>().SetStrokeRoot(strokeRoot);
        if (!addDoor)
            return;

        Vector3 roomSide = -right;
        Cube(name, center + roomSide * 0.08f + Vector3.down * 0.9f, rotation,
            new Vector3(1.8f, 4.2f, 0.18f), ink, walls);
    }

    private static GameObject Cube(string name, Vector3 position, Quaternion rotation,
        Vector3 scale, Material material, Transform parent)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent);
        cube.transform.SetPositionAndRotation(position, rotation);
        cube.transform.localScale = scale;
        if (material != null)
            cube.GetComponent<Renderer>().sharedMaterial = material;
        return cube;
    }

    private static Material FindPaperMaterial(Scene scene)
    {
        DuduSurface surface = FindInScene<DuduSurface>(scene);
        return surface != null ? surface.GetComponent<Renderer>()?.sharedMaterial : null;
    }

    private static Material FindInkMaterial(Scene scene)
    {
        GameObject ground = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(item => item.gameObject)
            .FirstOrDefault(item => item.name.StartsWith("Ground - Section"));
        return ground != null ? ground.GetComponent<Renderer>()?.sharedMaterial : FindPaperMaterial(scene);
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform child = new GameObject(name).transform;
        child.SetParent(parent);
        return child;
    }

    private static GameObject FindNamed(Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(item => item.gameObject)
            .FirstOrDefault(item => item.name == name);
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .FirstOrDefault();
    }

    private static T[] FindAllInScene<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .ToArray();
    }
}
#endif
