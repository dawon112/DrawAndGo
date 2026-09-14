#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CharacterAssetIntegration
{
    private const string ScenePath = "Assets/Scenes/Player3DScene.unity";
    private const string HaruModelPath = "Assets/Art/Characters/Haru/haru.fbx";
    private const string HaruMaterialPath = "Assets/Art/Characters/Haru/tripo_mat_cc23d9d0.mat";
    private const string HaruTexturePath = "Assets/Art/Characters/Haru/Haru_Texture.jpg";
    private const string DuduSourceFolder = "Assets/Art/Characters/Dudu";
    private const string DuduAnimationFolder = "Assets/Animations/Dudu";
    private const string DuduControllerPath = DuduAnimationFolder + "/Dudu.controller";
    private const float DuduHeight = 1.5f;
    private const float DuduFrameRate = 10f;

    static CharacterAssetIntegration()
    {
        EditorApplication.delayCall += ApplyIfNeeded;
    }

    private static void ApplyIfNeeded()
    {
        if (!EditorApplication.isCompiling &&
            !EditorApplication.isPlayingOrWillChangePlaymode &&
            AssetDatabase.LoadAssetAtPath<AnimatorController>(DuduControllerPath) == null)
            Apply();
    }

    [MenuItem("Tools/Draw And Go/Apply Haru and Dudu Assets")]
    public static void Apply()
    {
        EnsureDuduAnimationFolder();
        AnimationClip idle = CreateOrUpdateClip("dudu_idle", true);
        AnimationClip walk = CreateOrUpdateClip("dudu_walk", true);
        AnimationClip jump = CreateOrUpdateClip("dudu_jump", false);
        AnimationClip die = CreateOrUpdateClip("dudu_die", false);
        AnimationClip rebirth = CreateOrUpdateClip("dudu_rebirth", false);
        AnimationClip knockback = CreateOrUpdateClip("dudu_knockback", false);
        AnimatorController controller = CreateOrUpdateController(idle, walk, jump, die, rebirth, knockback);

        ConfigureHaruMaterial();
        AssetDatabase.SaveAssets();
        UpdateMainScene(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("Haru and Dudu character assets applied successfully.");
    }

    private static void EnsureDuduAnimationFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Animations"))
            AssetDatabase.CreateFolder("Assets", "Animations");
        if (!AssetDatabase.IsValidFolder(DuduAnimationFolder))
            AssetDatabase.CreateFolder("Assets/Animations", "Dudu");
    }

    private static AnimationClip CreateOrUpdateClip(string sourceName, bool loop)
    {
        string texturePath = DuduSourceFolder + "/" + sourceName + ".png";
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(texturePath)
            .OfType<Sprite>()
            .OrderBy(sprite => ParseFrameIndex(sprite.name))
            .ToArray();
        if (sprites.Length == 0)
            throw new InvalidOperationException("No sprite frames found at " + texturePath);

        string clipPath = DuduAnimationFolder + "/" + ToPascalCase(sourceName) + ".anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = ToPascalCase(sourceName) };
            AssetDatabase.CreateAsset(clip, clipPath);
        }

        clip.frameRate = DuduFrameRate;
        EditorCurveBinding binding = new EditorCurveBinding
        {
            path = string.Empty,
            type = typeof(SpriteRenderer),
            propertyName = "m_Sprite"
        };
        ObjectReferenceKeyframe[] keys = sprites.Select((sprite, index) => new ObjectReferenceKeyframe
        {
            time = index / DuduFrameRate,
            value = sprite
        }).ToArray();
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static AnimatorController CreateOrUpdateController(
        AnimationClip idle, AnimationClip walk, AnimationClip jump,
        AnimationClip die, AnimationClip rebirth, AnimationClip knockback)
    {
        AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(DuduControllerPath);
        if (existing != null)
            AssetDatabase.DeleteAsset(DuduControllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(DuduControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idleState = AddState(machine, "Idle", idle, new Vector3(220f, 20f));
        AnimatorState walkState = AddState(machine, "Walk", walk, new Vector3(450f, 20f));
        AnimatorState jumpState = AddState(machine, "Jump", jump, new Vector3(340f, 130f));
        AddState(machine, "Die", die, new Vector3(220f, 260f));
        AnimatorState rebirthState = AddState(machine, "Rebirth", rebirth, new Vector3(450f, 260f));
        AddState(machine, "Knockback", knockback, new Vector3(650f, 150f));
        machine.defaultState = idleState;

        AddTransition(idleState, walkState, "Speed", AnimatorConditionMode.Greater, 0.01f);
        AddTransition(walkState, idleState, "Speed", AnimatorConditionMode.Less, 0.01f);
        AddTransition(idleState, jumpState, "Grounded", AnimatorConditionMode.IfNot, 0f);
        AddTransition(walkState, jumpState, "Grounded", AnimatorConditionMode.IfNot, 0f);
        AnimatorStateTransition landIdle = AddTransition(jumpState, idleState, "Grounded", AnimatorConditionMode.If, 0f);
        landIdle.AddCondition(AnimatorConditionMode.Less, 0.01f, "Speed");
        AnimatorStateTransition landWalk = AddTransition(jumpState, walkState, "Grounded", AnimatorConditionMode.If, 0f);
        landWalk.AddCondition(AnimatorConditionMode.Greater, 0.01f, "Speed");

        AnimatorStateTransition rebirthToIdle = rebirthState.AddTransition(idleState);
        rebirthToIdle.hasExitTime = true;
        rebirthToIdle.exitTime = 1f;
        rebirthToIdle.hasFixedDuration = true;
        rebirthToIdle.duration = 0f;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static AnimatorState AddState(AnimatorStateMachine machine, string name, Motion motion, Vector3 position)
    {
        AnimatorState state = machine.AddState(name, position);
        state.motion = motion;
        state.speed = 0.8f;
        return state;
    }

    private static AnimatorStateTransition AddTransition(
        AnimatorState from, AnimatorState to, string parameter, AnimatorConditionMode mode, float threshold)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0f;
        transition.AddCondition(mode, threshold, parameter);
        return transition;
    }

    private static void ConfigureHaruMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(HaruMaterialPath);
        Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(HaruTexturePath);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (material == null || texture == null)
            throw new InvalidOperationException("Haru material or texture is missing.");
        if (shader != null && material.shader != shader)
            material.shader = shader;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        material.color = Color.white;
        EditorUtility.SetDirty(material);
    }

    private static void UpdateMainScene(AnimatorController controller)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Player3DMovement player3D = UnityEngine.Object.FindAnyObjectByType<Player3DMovement>(FindObjectsInactive.Include);
        DuduSurfaceMovement dudu = UnityEngine.Object.FindAnyObjectByType<DuduSurfaceMovement>(FindObjectsInactive.Include);
        if (player3D == null || dudu == null)
            throw new InvalidOperationException("Player3D or Dudu was not found in the main scene.");

        ConfigureHaruVisual(player3D);
        ConfigureFirstPersonCamera(player3D);
        ConfigureDuduVisual(dudu, controller);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void ConfigureHaruVisual(Player3DMovement player)
    {
        Transform oldVisual = player.transform.Find("Haru Visual");
        if (oldVisual != null)
            UnityEngine.Object.DestroyImmediate(oldVisual.gameObject);

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(HaruModelPath);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(HaruMaterialPath);
        if (model == null || material == null)
            throw new InvalidOperationException("Haru model or material is missing.");

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model, player.gameObject.scene);
        visual.name = "Haru Visual";
        visual.transform.SetParent(player.transform, false);
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        visual.transform.localScale = Vector3.one;
        SetLayerRecursively(visual.transform, player.gameObject.layer);

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
        }
        FitVisualToHeightAndFloor(visual.transform, player.transform, 1.8f);
    }

    private static void ConfigureFirstPersonCamera(Player3DMovement player)
    {
        Camera camera = player.GetComponentInChildren<Player3DLook>(true)?.GetComponent<Camera>();
        if (camera != null)
            camera.cullingMask &= ~(1 << player.gameObject.layer);
    }

    private static void ConfigureDuduVisual(DuduSurfaceMovement dudu, AnimatorController controller)
    {
        SpriteRenderer renderer = dudu.GetComponentInChildren<SpriteRenderer>(true);
        Animator animator = dudu.GetComponentInChildren<Animator>(true);
        if (renderer == null || animator == null)
            throw new InvalidOperationException("Dudu visual components are missing.");

        Transform visual = renderer.transform;
        visual.name = "DuduVisual";
        Sprite firstFrame = AssetDatabase.LoadAllAssetsAtPath(DuduSourceFolder + "/dudu_idle.png")
            .OfType<Sprite>().First(sprite => sprite.name == "dudu_idle_0");
        renderer.sprite = firstFrame;
        renderer.sortingOrder = 10;
        animator.runtimeAnimatorController = controller;
        visual.localPosition = new Vector3(0f, -0.1f, 0f);
        float sourceHeight = Mathf.Max(firstFrame.bounds.size.y, 0.001f);
        visual.localScale = Vector3.one * (DuduHeight / sourceHeight);
        dudu.Configure(dudu.CurrentSurface, animator, renderer);
        EditorUtility.SetDirty(dudu);
    }

    private static void FitVisualToHeightAndFloor(Transform visual, Transform player, float targetHeight)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new InvalidOperationException("Haru model contains no renderer.");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        visual.localScale *= targetHeight / Mathf.Max(bounds.size.y, 0.001f);
        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        visual.position += Vector3.up * (player.position.y - bounds.min.y);
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root) SetLayerRecursively(child, layer);
    }

    private static int ParseFrameIndex(string name)
    {
        int separator = name.LastIndexOf('_');
        return separator >= 0 && int.TryParse(name.Substring(separator + 1), out int index) ? index : 0;
    }

    private static string ToPascalCase(string value)
    {
        return string.Concat(value.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
    }
}
#endif
