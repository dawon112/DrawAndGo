using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CoinPrefabMigration
{
    private const string ScenePath = "Assets/Scenes/Player3DScene.unity";
    private const string PrefabDirectory = "Assets/Prefabs/Collectibles";
    private const string PrefabPath = PrefabDirectory + "/Coin.prefab";
    private const string MigrationKey = "DrawAndGo.CoinPrefabMigration.v1";

    [InitializeOnLoadMethod]
    private static void RunOnceAfterImport()
    {
        if (!EditorPrefs.GetBool(MigrationKey, false))
        {
            EditorApplication.delayCall += () =>
            {
                if (SceneManager.GetActiveScene().path == ScenePath)
                    Run();
                else
                    Debug.Log("Coin prefab migration is ready: open Player3DScene and use Tools/Draw And Go/Create Coin Prefab.");
            };
        }
    }

    [MenuItem("Tools/Draw And Go/Create Coin Prefab")]
    public static void Run()
    {
        Directory.CreateDirectory(PrefabDirectory);
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CornerRoomLevel level = Object.FindFirstObjectByType<CornerRoomLevel>();
        if (level == null || level.coins == null || level.coins.Length == 0)
            throw new System.InvalidOperationException("Player3DScene has no configured coins.");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            GameObject source = level.coins.Length > 1 && level.coins[1] != null
                ? level.coins[1]
                : level.coins[0];
            GameObject template = Object.Instantiate(source);
            template.name = "Coin";
            template.transform.SetParent(null);
            template.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            template.transform.localScale = Vector3.one * 0.07f;

            BoxCollider trigger = template.GetComponent<BoxCollider>();
            if (trigger != null)
            {
                trigger.isTrigger = true;
                trigger.center = Vector3.zero;
                trigger.size = new Vector3(10f, 12.857143f, 4.285714f);
            }

            prefab = PrefabUtility.SaveAsPrefabAsset(template, PrefabPath);
            Object.DestroyImmediate(template);
        }

        for (int i = 0; i < level.coins.Length; i++)
        {
            GameObject oldCoin = level.coins[i];
            if (oldCoin == null || PrefabUtility.IsPartOfPrefabInstance(oldCoin))
                continue;

            Transform oldTransform = oldCoin.transform;
            Transform parent = oldTransform.parent;
            int siblingIndex = oldTransform.GetSiblingIndex();
            Vector3 position = oldTransform.position;
            Quaternion rotation = oldTransform.rotation;
            Vector3 scale = oldTransform.localScale;
            string objectName = oldCoin.name;
            bool active = oldCoin.activeSelf;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = objectName;
            instance.transform.SetParent(parent, true);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = scale;
            instance.transform.SetSiblingIndex(siblingIndex);
            instance.SetActive(active);
            level.coins[i] = instance;
            Object.DestroyImmediate(oldCoin);
        }

        EditorUtility.SetDirty(level);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(MigrationKey, true);
        Debug.Log($"Coin prefab created and connected: {PrefabPath}");
    }
}
