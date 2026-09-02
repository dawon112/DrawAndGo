#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SideScrollerPrototypeBuilder
{
    private const string SpritePath = "Assets/Art/Characters/Knight/Knight.png";
    private const string FloorSpritePath = "Assets/Art/Environment/PrototypeSquare.png";
    private const string AnimationFolder = "Assets/Animations/Player";
    private const string ControllerPath = AnimationFolder + "/Knight.controller";
    private const string ScenePath = "Assets/Scenes/SideScrollerPrototype.unity";
    private const string PaperMaterialPath = "Assets/Materials/PaperBackground.mat";

    static SideScrollerPrototypeBuilder()
    {
        EditorApplication.delayCall += BuildIfNeeded;
        EditorApplication.delayCall += AddPaperBackgroundIfNeeded;
    }

    [MenuItem("Tools/Draw And Go/Rebuild 2D Prototype")]
    public static void Rebuild()
    {
        BuildPrototype(true);
    }

    private static void BuildIfNeeded()
    {
        if (!Application.isBatchMode && !File.Exists(ScenePath))
            BuildPrototype(false);
    }

    private static void BuildPrototype(bool force)
    {
        if (!force && File.Exists(ScenePath))
            return;

        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        bool restorePreviousSetup = previousSetup.Length > 0;

        try
        {
            ConfigureKnightImporter();
            EnsureFloorSprite();

            Sprite[] frames = AssetDatabase.LoadAllAssetsAtPath(SpritePath)
                .OfType<Sprite>()
                .OrderBy(sprite => ParseFrameIndex(sprite.name))
                .ToArray();

            if (frames.Length != 13)
                throw new InvalidOperationException($"Expected 13 Knight frames, but imported {frames.Length}.");

            AnimationClip idle = CreateSpriteClip("Knight_Idle", new[] { frames[0] }, 1f, true);
            AnimationClip run = CreateSpriteClip("Knight_Run", frames.Skip(1).Take(3).ToArray(), 10f, true);
            AnimationClip jump = CreateSpriteClip("Knight_Jump", frames.Skip(4).Take(9).ToArray(), 12f, false);
            AnimatorController controller = CreateAnimatorController(idle, run, jump);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "SideScrollerPrototype";

            GameObject player = CreatePlayer(frames[0], controller);
            CreateGround();
            CreatePaperBackground(scene);
            CreateCamera(player.transform);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings();
            AssetDatabase.SaveAssets();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (restorePreviousSetup)
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
        }
    }

    [MenuItem("Tools/Draw And Go/Add or Update Paper Background")]
    public static void AddPaperBackgroundIfNeeded()
    {
        if (Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isPlaying ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(ScenePath))
            return;

        Scene prototypeScene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForUpdate = !prototypeScene.IsValid() || !prototypeScene.isLoaded;

        try
        {
            if (openedForUpdate)
                prototypeScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            GameObject paper = prototypeScene.GetRootGameObjects()
                .FirstOrDefault(root => root.name == "Paper Background");

            if (paper == null)
                paper = CreatePaperBackground(prototypeScene);

            paper.transform.SetPositionAndRotation(new Vector3(0f, 2f, 1f), Quaternion.identity);
            paper.transform.localScale = new Vector3(40f, 7f, 1f);

            MeshRenderer meshRenderer = paper.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
                meshRenderer.sharedMaterial = EnsurePaperMaterial();

            SpriteRenderer spriteRenderer = paper.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.color = Color.white;
                spriteRenderer.sortingOrder = -10;
            }

            Collider collider = paper.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);

            Camera sceneCamera = prototypeScene.GetRootGameObjects()
                .Select(root => root.GetComponent<Camera>())
                .FirstOrDefault(camera => camera != null);
            if (sceneCamera != null)
                sceneCamera.clearFlags = CameraClearFlags.Skybox;

            EditorSceneManager.MarkSceneDirty(prototypeScene);
            EditorSceneManager.SaveScene(prototypeScene);
            AssetDatabase.SaveAssets();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (openedForUpdate && prototypeScene.IsValid() && prototypeScene.isLoaded)
                EditorSceneManager.CloseScene(prototypeScene, true);
        }
    }

    private static void ConfigureKnightImporter()
    {
        AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException("Knight.png could not be imported.");

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 32f;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;

        SpriteMetaData[] slices = new SpriteMetaData[13];
        for (int index = 0; index < slices.Length; index++)
        {
            int rowFromTop = index / 4;
            int column = index % 4;
            slices[index] = new SpriteMetaData
            {
                name = "Knight_" + index,
                rect = new Rect(column * 32, 128 - ((rowFromTop + 1) * 32), 32, 32),
                alignment = (int)SpriteAlignment.Custom,
                pivot = new Vector2(0.5f, 0f)
            };
        }

#pragma warning disable 0618
        importer.spritesheet = slices;
#pragma warning restore 0618
        importer.SaveAndReimport();
    }

    private static void EnsureFloorSprite()
    {
        if (File.Exists(FloorSpritePath))
            return;

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        File.WriteAllBytes(FloorSpritePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(FloorSpritePath, ImportAssetOptions.ForceSynchronousImport);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(FloorSpritePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 1f;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
    }

    private static AnimationClip CreateSpriteClip(string name, Sprite[] sprites, float frameRate, bool loop)
    {
        string path = $"{AnimationFolder}/{name}.anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = name };
            AssetDatabase.CreateAsset(clip, path);
        }

        clip.frameRate = frameRate;
        EditorCurveBinding binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = string.Empty,
            propertyName = "m_Sprite"
        };
        ObjectReferenceKeyframe[] keys = sprites.Select((sprite, index) => new ObjectReferenceKeyframe
        {
            time = index / frameRate,
            value = sprite
        }).ToArray();
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static AnimatorController CreateAnimatorController(AnimationClip idle, AnimationClip run, AnimationClip jump)
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.parameters.First(parameter => parameter.name == "Grounded").defaultBool = true;

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idleState = machine.AddState("Idle");
        AnimatorState runState = machine.AddState("Run");
        AnimatorState jumpState = machine.AddState("Jump");
        idleState.motion = idle;
        runState.motion = run;
        jumpState.motion = jump;
        machine.defaultState = idleState;

        AddTransition(idleState, runState, "Speed", AnimatorConditionMode.Greater, 0.01f);
        AddTransition(runState, idleState, "Speed", AnimatorConditionMode.Less, 0.01f);
        AddTransition(idleState, jumpState, "Grounded", AnimatorConditionMode.IfNot, 0f);
        AddTransition(runState, jumpState, "Grounded", AnimatorConditionMode.IfNot, 0f);
        AddLandingTransition(jumpState, idleState, false);
        AddLandingTransition(jumpState, runState, true);

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void AddTransition(AnimatorState from, AnimatorState to, string parameter, AnimatorConditionMode mode, float threshold)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0.04f;
        transition.AddCondition(mode, threshold, parameter);
    }

    private static void AddLandingTransition(AnimatorState from, AnimatorState to, bool moving)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0.04f;
        transition.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
        transition.AddCondition(moving ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, 0.01f, "Speed");
    }

    private static GameObject CreatePlayer(Sprite idleSprite, AnimatorController controller)
    {
        GameObject player = new GameObject("Player");
        player.transform.position = new Vector3(0f, -1.5f, 0f);
        player.transform.localScale = Vector3.one * 1.5f;

        SpriteRenderer renderer = player.AddComponent<SpriteRenderer>();
        renderer.sprite = idleSprite;
        renderer.sortingOrder = 10;

        Rigidbody2D body = player.AddComponent<Rigidbody2D>();
        body.gravityScale = 2.5f;
        body.freezeRotation = true;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;

        BoxCollider2D collider = player.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(0.48f, 0.82f);
        collider.offset = new Vector2(0f, 0.41f);

        Animator animator = player.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        player.AddComponent<PlayerController2D>();
        return player;
    }

    private static void CreateGround()
    {
        GameObject ground = new GameObject("Ground");
        ground.transform.position = new Vector3(0f, -2f, 0f);
        ground.transform.localScale = new Vector3(40f, 1f, 1f);

        SpriteRenderer renderer = ground.AddComponent<SpriteRenderer>();
        renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(FloorSpritePath);
        renderer.color = new Color(0.16f, 0.17f, 0.2f, 1f);

        BoxCollider2D collider = ground.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one;
    }

    private static GameObject CreatePaperBackground(Scene scene)
    {
        GameObject paper = GameObject.CreatePrimitive(PrimitiveType.Quad);
        paper.name = "Paper Background";
        SceneManager.MoveGameObjectToScene(paper, scene);
        paper.transform.SetPositionAndRotation(new Vector3(0f, 2f, 1f), Quaternion.identity);
        paper.transform.localScale = new Vector3(40f, 7f, 1f);
        paper.GetComponent<MeshRenderer>().sharedMaterial = EnsurePaperMaterial();

        Collider collider = paper.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.DestroyImmediate(collider);

        return paper;
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

        material = new Material(shader) { name = "Paper Background" };
        material.color = Color.white;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(material, PaperMaterialPath);
        return material;
    }

    private static void CreateCamera(Transform target)
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 4.5f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 1000f;

        CameraFollow2D follow = cameraObject.AddComponent<CameraFollow2D>();
        follow.SetTarget(target);
    }

    private static void AddSceneToBuildSettings()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        if (scenes.Any(scene => scene.path == ScenePath))
            return;

        EditorBuildSettings.scenes = scenes
            .Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) })
            .ToArray();
    }

    private static int ParseFrameIndex(string spriteName)
    {
        string suffix = spriteName.Substring(spriteName.LastIndexOf('_') + 1);
        return int.TryParse(suffix, out int value) ? value : int.MaxValue;
    }
}
#endif
