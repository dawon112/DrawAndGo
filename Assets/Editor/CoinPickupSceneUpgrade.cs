#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CoinPickupSceneUpgrade
{
    private const string ScenePath = "Assets/Scenes/SideScrollerPrototype.unity";

    static CoinPickupSceneUpgrade()
    {
        EditorApplication.delayCall += ApplyIfNeeded;
    }

    private static void ApplyIfNeeded()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode ||
            !File.Exists(ScenePath))
            return;

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForUpdate = !scene.IsValid() || !scene.isLoaded;
        if (openedForUpdate)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        try
        {
            GameObject coin = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Coin");
            if (coin == null || coin.GetComponent<CoinPickup2D>() != null)
                return;

            CircleCollider2D trigger = coin.GetComponent<CircleCollider2D>();
            if (trigger == null)
                trigger = coin.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = 0.42f;

            coin.AddComponent<CoinPickup2D>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (openedForUpdate && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }
}
#endif
