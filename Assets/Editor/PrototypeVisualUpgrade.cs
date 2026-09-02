#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PrototypeVisualUpgrade
{
    private const string ScenePath = "Assets/Scenes/SideScrollerPrototype.unity";
    private const string CoinTexturePath = "Assets/Art/Collectibles/Coin/Money-Sheet.png";
    private const string CoinClipPath = "Assets/Animations/Coin/Coin_Spin.anim";
    private const string CoinControllerPath = "Assets/Animations/Coin/Coin.controller";

    [MenuItem("Tools/Draw And Go/Apply Player And Coin")]
    public static void Apply()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode ||
            !File.Exists(ScenePath))
            return;

        try
        {
            ConfigureCoinImporter();

            Sprite[] coinFrames = AssetDatabase.LoadAllAssetsAtPath(CoinTexturePath)
                .OfType<Sprite>()
                .OrderBy(sprite => ParseFrameIndex(sprite.name))
                .ToArray();
            if (coinFrames.Length != 6)
                throw new InvalidOperationException("Coin sprites were not imported as expected.");

            AnimatorController coinController = CreateCoinAnimation(coinFrames);
            Scene prototypeScene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForUpdate = !prototypeScene.IsValid() || !prototypeScene.isLoaded;
            if (openedForUpdate)
                prototypeScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                GameObject[] roots = prototypeScene.GetRootGameObjects();
                GameObject player = roots.First(root => root.name == "Player");

                player.transform.localScale = Vector3.one * 1.5f;
                CreateOrUpdateCoin(prototypeScene, roots, coinFrames[0], coinController);

                EditorSceneManager.MarkSceneDirty(prototypeScene);
                EditorSceneManager.SaveScene(prototypeScene);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (openedForUpdate && prototypeScene.IsValid() && prototypeScene.isLoaded)
                    EditorSceneManager.CloseScene(prototypeScene, true);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void ConfigureCoinImporter()
    {
        AssetDatabase.ImportAsset(CoinTexturePath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(CoinTexturePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 16f;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;

        SpriteMetaData[] slices = new SpriteMetaData[6];
        for (int index = 0; index < slices.Length; index++)
        {
            slices[index] = new SpriteMetaData
            {
                name = "Coin_" + index,
                rect = new Rect(index * 16, 0, 16, 16),
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f)
            };
        }

#pragma warning disable 0618
        importer.spritesheet = slices;
#pragma warning restore 0618
        importer.SaveAndReimport();
    }

    private static AnimatorController CreateCoinAnimation(Sprite[] frames)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CoinClipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = "Coin_Spin", frameRate = 10f };
            AssetDatabase.CreateAsset(clip, CoinClipPath);
        }

        EditorCurveBinding binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = string.Empty,
            propertyName = "m_Sprite"
        };
        ObjectReferenceKeyframe[] keys = frames.Select((frame, index) => new ObjectReferenceKeyframe
        {
            time = index / 10f,
            value = frame
        }).ToArray();
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
        clipSettings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
        EditorUtility.SetDirty(clip);

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CoinControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(CoinControllerPath);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState state = machine.states.Select(child => child.state)
            .FirstOrDefault(item => item.name == "Coin Spin") ?? machine.AddState("Coin Spin");
        state.motion = clip;
        machine.defaultState = state;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void CreateOrUpdateCoin(Scene scene, GameObject[] roots, Sprite firstFrame, AnimatorController controller)
    {
        GameObject coin = roots.FirstOrDefault(root => root.name == "Coin");
        if (coin == null)
        {
            coin = new GameObject("Coin");
            SceneManager.MoveGameObjectToScene(coin, scene);
        }

        coin.transform.position = new Vector3(3f, -0.25f, 0f);
        coin.transform.rotation = Quaternion.identity;
        coin.transform.localScale = Vector3.one * 1.05f;

        SpriteRenderer renderer = coin.GetComponent<SpriteRenderer>();
        if (renderer == null)
            renderer = coin.AddComponent<SpriteRenderer>();
        renderer.sprite = firstFrame;
        renderer.color = Color.white;
        renderer.sortingOrder = 5;

        Animator animator = coin.GetComponent<Animator>();
        if (animator == null)
            animator = coin.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        CircleCollider2D trigger = coin.GetComponent<CircleCollider2D>();
        if (trigger == null)
            trigger = coin.AddComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = 0.42f;

        if (coin.GetComponent<CoinPickup2D>() == null)
            coin.AddComponent<CoinPickup2D>();
    }

    private static int ParseFrameIndex(string spriteName)
    {
        string suffix = spriteName.Substring(spriteName.LastIndexOf('_') + 1);
        return int.TryParse(suffix, out int value) ? value : int.MaxValue;
    }
}
#endif
