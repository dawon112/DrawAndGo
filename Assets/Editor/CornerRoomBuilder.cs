#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CornerRoomBuilder
{
    [InitializeOnLoadMethod]
    private static void ScheduleBuild()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Build();
        };
    }

    [MenuItem("Tools/Draw And Go/Build Corner Room")]
    public static void Build()
    {
        const string path = "Assets/Scenes/Player3DScene.unity";
        Scene scene = SceneManager.GetSceneByPath(path);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(path);
        GameObject[] roots = scene.GetRootGameObjects();
        GameObject Find(string name) => roots.FirstOrDefault(x => x.name == name);
        Player3DMovement FindPlayer3D() => roots
            .SelectMany(x => x.GetComponentsInChildren<Player3DMovement>(true))
            .FirstOrDefault();
        if (scene.GetRootGameObjects().Any(x => x.GetComponent<CornerRoomLevel>() != null))
        {
            var existingLevel = scene.GetRootGameObjects()
                .Select(x => x.GetComponent<CornerRoomLevel>())
                .First(x => x != null);
            Transform closureWall = existingLevel.transform.Find("Room Wall 6 - Closure");
            if (closureWall != null) Object.DestroyImmediate(closureWall.gameObject);
            existingLevel.player3D = FindPlayer3D();
            if (existingLevel.player3D == null)
            {
                Debug.LogError("Corner room build skipped: Player3DMovement was not found in Player3DScene.");
                return;
            }
            EnsureHaruModel(existingLevel.player3D.gameObject, scene);
            var existingGround = roots.FirstOrDefault(x => x.name == "Floor3D" || x.name == "Ground3D");
            if (existingGround == null)
            {
                Debug.LogError("Corner room build skipped: Floor3D was not found in Player3DScene.");
                return;
            }
            existingGround.name = "Floor3D";
            existingGround.transform.position = new Vector3(5f, -0.1f, 8f);
            existingGround.transform.localScale = new Vector3(22f, 1f, 22f);
            existingGround.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/YughuesFreeFlooringMaterials/Materials/M_YFFlM_01.mat");
            foreach (string frameName in new[] { "Door Frame Left", "Door Frame Right", "Door Frame Top" })
            {
                var frame = GameObject.Find(frameName);
                if (frame != null && frame.TryGetComponent(out Collider frameCollider))
                    Object.DestroyImmediate(frameCollider);
            }
            EnsureThreeDimensionalDoorway(existingLevel);
            EnsureCoinVisibility(existingLevel);
            EnsureLongGap(existingLevel);
            RemoveGuideLabels(existingLevel);
            EnsureThreeDimensionalGoal(existingLevel);
            UpdateObjectiveLabels(existingLevel);
            ConfigurePassableMovingObstacle(existingLevel);
            ValidateThreeDimensionalDoorway(existingLevel);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return;
        }
        var player = Find("Dudu").GetComponent<DuduSurfaceMovement>();
        var original = Find("DuduSurface");
        var root = new GameObject("Corner Room Level");
        SceneManager.MoveGameObjectToScene(root, scene);
        var level = root.AddComponent<CornerRoomLevel>();
        level.player = player;
        level.player3D = FindPlayer3D();
        if (level.player3D == null)
        {
            Object.DestroyImmediate(root);
            Debug.LogError("Corner room build skipped: Player3DMovement was not found in Player3DScene.");
            return;
        }
        EnsureHaruModel(level.player3D.gameObject, scene);
        level.surfaces = new DuduSurface[5];
        Vector3[] corners = { new Vector3(-5,3,8), new Vector3(5,3,8), new Vector3(5,3,18),
            new Vector3(15,3,18), new Vector3(15,3,-2), new Vector3(-5,3,-2) };
        Material paper = original.GetComponent<Renderer>().sharedMaterial;
        Material ink = Find("Dudu Ground Line").GetComponent<Renderer>().sharedMaterial;
        GameObject Cube(string name, Vector3 position, Quaternion rotation, Vector3 size, Material material)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.SetParent(root.transform);
            obj.transform.SetPositionAndRotation(position, rotation);
            obj.transform.localScale = size;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            if (name.StartsWith("Door Frame")) Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj;
        }
        TextMesh Label(string text, DuduSurface surface, float x, float y)
        {
            var obj = new GameObject(text);
            obj.transform.SetParent(root.transform);
            obj.transform.SetPositionAndRotation(surface.SurfaceToWorld(new Vector2(x,y)) + surface.Normal * .04f, surface.transform.rotation);
            var label = obj.AddComponent<TextMesh>();
            label.text = text; label.fontSize = 64; label.characterSize = .08f;
            label.anchor = TextAnchor.MiddleCenter; label.color = new Color(.1f,.25f,.35f);
            return label;
        }
        for (int i = 0; i < 6; i++)
        {
            Vector3 a = corners[i], b = corners[(i+1)%6];
            Vector3 right = (b-a).normalized;
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(right,Vector3.up),Vector3.up);
            float width = Vector3.Distance(a,b);
            if (i == 5)
            {
                continue;
            }
            GameObject wall = i == 0 ? original : Object.Instantiate(original);
            wall.name = i == 0 ? "DuduSurface" : "DuduSurface " + (i+1);
            wall.transform.SetParent(root.transform);
            wall.transform.SetPositionAndRotation((a+b)*.5f,rotation);
            wall.transform.localScale = new Vector3(width,6,1);
            var surface = wall.GetComponent<DuduSurface>();
            var data = new SerializedObject(surface);
            data.FindProperty("width").floatValue = width;
            data.FindProperty("height").floatValue = 6f;
            data.ApplyModifiedPropertiesWithoutUndo();
            level.surfaces[i] = surface;
            Cube("Ground - Section " + (i+1),surface.SurfaceToWorld(new Vector2(0,-2.4f)),rotation,new Vector3(width,.14f,.16f),ink);
        }
        for (int i = 0; i < 5; i++)
        {
            level.surfaces[i].previousSurface = i > 0 ? level.surfaces[i-1] : null;
            level.surfaces[i].nextSurface = i < 4 ? level.surfaces[i+1] : null;
        }
        Object.DestroyImmediate(Find("Dudu Ground Line"));
        player.Configure(level.surfaces[0],player.GetComponentInChildren<Animator>(),player.GetComponentInChildren<SpriteRenderer>());
        var playerData = new SerializedObject(player);
        playerData.FindProperty("surfacePosition").vector2Value = new Vector2(-3.8f,-1.5f);
        playerData.ApplyModifiedPropertiesWithoutUndo();
        player.SetSurface(level.surfaces[0]);
        var camera = Find("DuduCamera").GetComponent<DuduCameraController>();
        camera.SetSurface(level.surfaces[0]); camera.SetTarget(player.transform);
        level.goalLabel = Label("COINS 0 / 3",level.surfaces[4],3f,1.5f);
        var last = level.surfaces[4];
        Cube("Door Frame Left", last.SurfaceToWorld(new Vector2(6.5f,-.3f)),last.transform.rotation,new Vector3(.15f,4,.6f),ink);
        Cube("Door Frame Right", last.SurfaceToWorld(new Vector2(8.5f,-.3f)),last.transform.rotation,new Vector3(.15f,4,.6f),ink);
        Cube("Door Frame Top", last.SurfaceToWorld(new Vector2(7.5f,1.7f)),last.transform.rotation,new Vector3(2.15f,.15f,.6f),ink);
        level.door = Cube("Door - Opens After Three Coins",last.SurfaceToWorld(new Vector2(7.5f,-.3f)),last.transform.rotation,new Vector3(1.8f,4,.5f),ink);
        EnsureThreeDimensionalDoorway(level);
        ValidateThreeDimensionalDoorway(level);
        Label("3D EXIT",last,7.5f,2.35f);
        level.coins = new GameObject[3];
        var sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Collectibles/Coin/Money-Sheet.png").OfType<Sprite>().OrderBy(x=>x.name).First();
        for (int i=0;i<3;i++)
        {
            var surface = level.surfaces[i*2];
            var coin = new GameObject("Coin " + (i+1));
            coin.transform.SetParent(root.transform);
            coin.transform.SetPositionAndRotation(
                surface.SurfaceToWorld(new Vector2(1,-1.4f)) + surface.Normal * .16f,
                surface.transform.rotation);
            coin.AddComponent<SpriteRenderer>().sprite = sprite;
            coin.GetComponent<SpriteRenderer>().sortingOrder = 5;
            coin.AddComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Coin/Coin.controller");
            var trigger = coin.AddComponent<BoxCollider>(); trigger.size = new Vector3(.7f,.9f,.3f); trigger.isTrigger = true;
            coin.AddComponent<SurfaceCoinPickup>();
            level.coins[i] = coin;
        }
        EnsureCoinVisibility(level);
        EnsureLongGap(level);
        RemoveGuideLabels(level);
        EnsureThreeDimensionalGoal(level);
        UpdateObjectiveLabels(level);
        // Reuse the two existing stain effects and moving obstacle; shooter stays untouched.
        Find("Slow Stain Obstacle").GetComponent<DuduStainObstacle>().Configure(level.surfaces[1],new Vector2(0,-2.1f),DuduStainObstacle.EffectType.Slow);
        ConfigurePassableMovingObstacle(level);
        Find("Reverse Stain Obstacle").GetComponent<DuduStainObstacle>().Configure(level.surfaces[3],new Vector2(-6.5f,-2.1f),DuduStainObstacle.EffectType.ReverseControls);
        var ground = scene.GetRootGameObjects().First(x => x.name == "Floor3D" || x.name == "Ground3D");
        ground.name = "Floor3D";
        ground.transform.position = new Vector3(5,-.1f,8);
        ground.transform.localScale = new Vector3(22,1,22);
        ground.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/YughuesFreeFlooringMaterials/Materials/M_YFFlM_01.mat");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Corner room built: 6 walls, 5 connected surfaces, 3 coins, 3 obstacles.");
    }

    private static void EnsureThreeDimensionalDoorway(CornerRoomLevel level)
    {
        DuduSurface finalSurface = level.surfaces[4];
        DrawingSurface originalDrawingSurface = finalSurface.GetComponent<DrawingSurface>();
        Transform strokeRoot = originalDrawingSurface != null
            ? originalDrawingSurface.StrokeRoot
            : GameObject.Find("DrawingRoot")?.transform;

        Renderer fullWallRenderer = finalSurface.GetComponent<Renderer>();
        Material paperMaterial = fullWallRenderer != null ? fullWallRenderer.sharedMaterial : null;
        if (fullWallRenderer != null)
            fullWallRenderer.enabled = false;

        if (originalDrawingSurface != null)
            Object.DestroyImmediate(originalDrawingSurface);
        Collider fullWallCollider = finalSurface.GetComponent<Collider>();
        if (fullWallCollider != null)
            Object.DestroyImmediate(fullWallCollider);

        Transform doorwayRoot = level.transform.Find("3D Doorway Wall - Permanent Opening");
        if (doorwayRoot == null)
        {
            doorwayRoot = new GameObject("3D Doorway Wall - Permanent Opening").transform;
            doorwayRoot.SetParent(level.transform);
        }

        CreateDoorwaySegment("Doorway Wall Left", new Vector2(-1.75f, 0f), new Vector2(16.5f, 6f));
        CreateDoorwaySegment("Doorway Wall Right", new Vector2(9.25f, 0f), new Vector2(1.5f, 6f));
        CreateDoorwaySegment("Doorway Wall Top", new Vector2(7.5f, 2.35f), new Vector2(2f, 1.3f));

        level.door.name = "3D Door - Blocks Wall Opening Until Three Coins";

        void CreateDoorwaySegment(string name, Vector2 position, Vector2 size)
        {
            Transform existing = doorwayRoot.Find(name);
            GameObject segment = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
            segment.name = name;
            segment.layer = finalSurface.gameObject.layer;
            segment.transform.SetParent(doorwayRoot);
            segment.transform.SetPositionAndRotation(finalSurface.SurfaceToWorld(position), finalSurface.transform.rotation);
            segment.transform.localScale = new Vector3(size.x, size.y, 0.12f);
            segment.GetComponent<Renderer>().sharedMaterial = paperMaterial;
            DrawingSurface drawingSurface = segment.GetComponent<DrawingSurface>();
            if (drawingSurface == null)
                drawingSurface = segment.AddComponent<DrawingSurface>();
            drawingSurface.SetStrokeRoot(strokeRoot);
        }
    }

    private static void EnsureHaruModel(GameObject player, Scene scene)
    {
        Transform capsule = player.transform.Find("Capsule Visual");
        Transform existing = player.transform.Find("Haru Visual");
        if (existing == null)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Art/Characters/Haru/haru.fbx");
            if (asset == null) return;
            GameObject visual = PrefabUtility.InstantiatePrefab(asset, scene) as GameObject;
            if (visual == null) return;
            visual.name = "Haru Visual";
            visual.transform.SetParent(player.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                if (bounds.size.y > 0.001f)
                {
                    visual.transform.localScale *= 1.8f / bounds.size.y;
                    bounds = visual.GetComponentsInChildren<Renderer>(true)[0].bounds;
                    foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true).Skip(1))
                        bounds.Encapsulate(renderer.bounds);
                    visual.transform.position += Vector3.up * (player.transform.position.y - bounds.min.y);
                }
            }
        }
        if (capsule != null) Object.DestroyImmediate(capsule.gameObject);
    }

    private static void ValidateThreeDimensionalDoorway(CornerRoomLevel level)
    {
        DuduSurface finalSurface = level.surfaces[4];
        Vector3 rayDirection = -finalSurface.Normal.normalized;
        Vector3 doorRayOrigin = finalSurface.SurfaceToWorld(new Vector2(7.5f, -0.3f)) +
            finalSurface.Normal * 2f;
        Vector3 wallRayOrigin = finalSurface.SurfaceToWorld(new Vector2(0f, -0.3f)) +
            finalSurface.Normal * 2f;

        bool initialDoorState = level.door.activeSelf;
        level.door.SetActive(true);
        Physics.SyncTransforms();
        bool closedDoorBlocks = Physics.Raycast(
                doorRayOrigin, rayDirection, out RaycastHit closedHit, 4f,
                Physics.AllLayers, QueryTriggerInteraction.Ignore) &&
            closedHit.collider.gameObject == level.door;
        bool permanentWallBlocks = Physics.Raycast(
            wallRayOrigin, rayDirection, 4f, Physics.AllLayers, QueryTriggerInteraction.Ignore);

        level.door.SetActive(false);
        Physics.SyncTransforms();
        bool openDoorHasHole = !Physics.Raycast(
            doorRayOrigin, rayDirection, 4f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        level.door.SetActive(initialDoorState);
        Physics.SyncTransforms();

        if (!closedDoorBlocks || !openDoorHasHole || !permanentWallBlocks)
        {
            Debug.LogError("3D doorway validation failed: the closed door must block the opening, " +
                "the opened door must expose a real hole, and the surrounding wall must remain solid.", level);
            return;
        }

        Debug.Log("3D doorway verified: closed door blocks the wall opening and opened door exposes a passable hole.", level);
    }

    private static void EnsureCoinVisibility(CornerRoomLevel level)
    {
        // Dudu's job ends after the last coin; the wall opening belongs to the 3D player.
        level.surfaces[4].SetRightBoundary(5.8f);
        if (level.coins == null || level.coins.Length != 3)
            return;

        float[] horizontalPositions = { 1f, 0f, 3f };
        for (int i = 0; i < level.coins.Length; i++)
        {
            GameObject coin = level.coins[i];
            DuduSurface surface = level.surfaces[i * 2];
            coin.transform.SetPositionAndRotation(
                surface.SurfaceToWorld(new Vector2(horizontalPositions[i], -1.25f)) +
                    surface.Normal * .16f,
                surface.transform.rotation);
            coin.transform.localScale = Vector3.one * 0.96f;
            SpriteRenderer renderer = coin.GetComponent<SpriteRenderer>();
            if (renderer != null)
                renderer.sortingOrder = 20;
        }
    }

    private static void EnsureThreeDimensionalGoal(CornerRoomLevel level)
    {
        Transform existing = level.transform.Find("3D Goal Trigger - Outside Door");
        GameObject goalObject = existing != null
            ? existing.gameObject
            : GameObject.CreatePrimitive(PrimitiveType.Cube);
        goalObject.name = "3D Goal Trigger - Outside Door";
        goalObject.transform.SetParent(level.transform);

        DuduSurface finalSurface = level.surfaces[4];
        goalObject.transform.SetPositionAndRotation(
            finalSurface.SurfaceToWorld(new Vector2(7.5f, -0.3f)) - finalSurface.Normal * 1.5f,
            finalSurface.transform.rotation);
        goalObject.transform.localScale = new Vector3(1.5f, 3f, 1.5f);
        goalObject.GetComponent<Renderer>().enabled = false;

        ThreeDimensionalGoal goal = goalObject.GetComponent<ThreeDimensionalGoal>();
        if (goal == null)
            goal = goalObject.AddComponent<ThreeDimensionalGoal>();
        goal.Configure(level);
        level.goal3D = goal;
    }

    private static void RemoveGuideLabels(CornerRoomLevel level)
    {
        foreach (TextMesh label in level.GetComponentsInChildren<TextMesh>(true))
        {
            string text = label.text.TrimStart();
            if (text.StartsWith("START") || text.StartsWith("SECTION"))
                Object.DestroyImmediate(label.gameObject);
        }
    }

    private static void EnsureLongGap(CornerRoomLevel level)
    {
        const float gapWidth = 8f;
        DuduSurface surface = level.surfaces[3];
        Transform root = level.transform;
        Transform original = root.Find("Ground - Section 4");
        Material material = original != null && original.TryGetComponent(out Renderer originalRenderer)
            ? originalRenderer.sharedMaterial
            : null;
        if (original != null)
            Object.DestroyImmediate(original.gameObject);

        float sectionWidth = (surface.Width - gapWidth) * 0.5f;
        float centerOffset = (gapWidth + sectionWidth) * 0.5f;
        CreateGroundSegment("Ground - Section 4 Left", -centerOffset);
        CreateGroundSegment("Ground - Section 4 Right", centerOffset);

        GameObject reverseStain = GameObject.Find("Reverse Stain Obstacle");
        if (reverseStain != null)
            reverseStain.GetComponent<DuduStainObstacle>().Configure(
                surface, new Vector2(-6.5f, -2.1f), DuduStainObstacle.EffectType.ReverseControls);

        void CreateGroundSegment(string name, float x)
        {
            Transform existing = root.Find(name);
            GameObject segment = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
            segment.name = name;
            segment.transform.SetParent(root);
            segment.transform.SetPositionAndRotation(
                surface.SurfaceToWorld(new Vector2(x, -2.4f)), surface.transform.rotation);
            segment.transform.localScale = new Vector3(sectionWidth, 0.14f, 0.16f);
            if (material != null)
                segment.GetComponent<Renderer>().sharedMaterial = material;
        }
    }

    private static void UpdateObjectiveLabels(CornerRoomLevel level)
    {
        DuduSurface finalSurface = level.surfaces[4];
        if (level.goalLabel != null)
        {
            level.goalLabel.gameObject.name = "2D Coin Objective";
            level.goalLabel.text = "COINS 0 / 3";
            level.goalLabel.transform.SetPositionAndRotation(
                finalSurface.SurfaceToWorld(new Vector2(3f, 1.5f)) + finalSurface.Normal * .13f,
                finalSurface.transform.rotation);
        }

        Transform signTransform = level.transform.Find("3D EXIT");
        if (signTransform == null)
            signTransform = level.transform.Find("EXIT >");
        if (signTransform == null)
            signTransform = level.transform.Find("3D EXIT SIGN");
        if (signTransform == null)
            return;

        signTransform.name = "3D EXIT SIGN";
        signTransform.SetPositionAndRotation(
            finalSurface.SurfaceToWorld(new Vector2(7.5f, 2.35f)) + finalSurface.Normal * .13f,
            finalSurface.transform.rotation);
        TextMesh sign = signTransform.GetComponent<TextMesh>();
        if (sign != null)
            sign.text = "3D EXIT";
    }

    private static void ConfigurePassableMovingObstacle(CornerRoomLevel level)
    {
        GameObject obstacle = GameObject.Find("Vertical Obstacle");
        if (obstacle == null)
            return;

        obstacle.GetComponent<DuduMovingObstacle>().Configure(
            level.surfaces[2],
            DuduMovingObstacle.MovementAxis.Vertical,
            new Vector2(-2f, -0.5f),
            1.6f,
            4.5f);
    }
}
#endif


