#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class HomingProjectilePrefabBuilder
{
    private const string Folder = "Assets/Prefabs/Enemies";
    private const string PrefabPath = Folder + "/HomingProjectile.prefab";
    private const string ScenePath = "Assets/Scenes/Player3DScene.unity";

    static HomingProjectilePrefabBuilder() => EditorApplication.delayCall += BuildIfNeeded;

    private static void BuildIfNeeded()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Enemies");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            GameObject projectile = new GameObject("Homing Projectile");
            SpriteRenderer renderer = projectile.AddComponent<SpriteRenderer>();
            renderer.color = Color.black;
            renderer.sortingOrder = 25;
            SphereCollider collider = projectile.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            Rigidbody body = projectile.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.None;
            projectile.AddComponent<DuduHomingProjectile>();
            prefab = PrefabUtility.SaveAsPrefabAsset(projectile, PrefabPath);
            Object.DestroyImmediate(projectile);
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (DuduHomingShooter shooter in Object.FindObjectsByType<DuduHomingShooter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedObject serialized = new SerializedObject(shooter);
            serialized.FindProperty("projectilePrefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Homing projectile prefab created and assigned.");
    }
}
#endif
