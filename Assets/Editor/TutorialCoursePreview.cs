#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Shows the tutorial course that TutorialSequenceController builds from code in the Scene
// view, by running that same code, so it always matches play and updates after each compile.
// The objects are read-only and never saved. They exist only outside play mode.
[InitializeOnLoad]
public static class TutorialCoursePreview
{
    public const string RootName = "튜토리얼 코스 미리보기 (코드에서 생성, 저장 안 됨)";
    private const string EnabledKey = "DrawAndGo.TutorialCoursePreview";
    private const string MenuPath = "Tools/Draw And Go/튜토리얼 코스 미리보기";
    private const HideFlags PreviewFlags = HideFlags.DontSave | HideFlags.NotEditable;

    static TutorialCoursePreview()
    {
        EditorSceneManager.sceneOpened += (scene, mode) => Schedule();
        EditorSceneManager.sceneClosing += (scene, removing) => Remove();
        EditorApplication.playModeStateChanged += change =>
        {
            if (change == PlayModeStateChange.ExitingEditMode) Remove();
            else if (change == PlayModeStateChange.EnteredEditMode) Schedule();
        };
        Schedule();
    }

    private static bool Enabled => EditorPrefs.GetBool(EnabledKey, true);

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        EditorPrefs.SetBool(EnabledKey, !Enabled);
        Rebuild();
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleState()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    public static void Schedule()
    {
        EditorApplication.delayCall -= Rebuild;
        EditorApplication.delayCall += Rebuild;
    }

    // Scene searches skip objects that are never saved, so this looks through everything.
    public static GameObject FindRoot()
    {
        foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            if (candidate != null && candidate.name == RootName && candidate.scene.IsValid()) return candidate;
        return null;
    }

    public static void Remove()
    {
        for (GameObject root = FindRoot(); root != null; root = FindRoot())
        {
            foreach (Renderer look in root.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in look.sharedMaterials)
                    if (material != null && !EditorUtility.IsPersistent(material)) Object.DestroyImmediate(material);
            Object.DestroyImmediate(root);
        }
        // The authored lines the course replaces were hidden while it was shown.
        foreach (CrayonRouteLine line in Object.FindObjectsByType<CrayonRouteLine>(FindObjectsInactive.Include))
            if (SceneVisibilityManager.instance.IsHidden(line.gameObject))
                SceneVisibilityManager.instance.Show(line.gameObject, true);
    }

    private static void Rebuild() => RebuildNow(false);

    // force builds it even with the preview turned off (the course bake reads from it).
    public static void RebuildNow(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Remove();
        if (!Enabled && !force) return;
        TutorialSequenceController course =
            Object.FindAnyObjectByType<TutorialSequenceController>(FindObjectsInactive.Include);
        if (course == null) return;

        HashSet<Transform> before = new HashSet<Transform>(Object.FindObjectsByType<Transform>(FindObjectsInactive.Include));
        Dictionary<GameObject, bool> active = new Dictionary<GameObject, bool>();
        foreach (Transform existing in before) active[existing.gameObject] = existing.gameObject.activeSelf;
        // The code changes walls and the room (widths, links, section lists): keep copies to
        // put back. On an inactive object, where the code's own searches cannot find them.
        GameObject saved = new GameObject("Tutorial Preview Saved State") { hideFlags = HideFlags.HideAndDontSave };
        saved.SetActive(false);
        Dictionary<MonoBehaviour, MonoBehaviour> state = new Dictionary<MonoBehaviour, MonoBehaviour>();
        Remember<DuduSurface>(state, saved);
        Remember<TutorialRoomLevel>(state, saved);
        Remember<TutorialSequenceController>(state, saved);

        try
        {
            course.BuildCourseForPreview();
        }
        catch (System.Exception problem)
        {
            Debug.LogWarning("튜토리얼 코스 미리보기를 만들지 못했어요: " + problem.Message);
        }

        GameObject root = new GameObject(RootName);
        foreach (Transform built in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (built == null || before.Contains(built) || built == root.transform || built == saved.transform) continue;
            // The tutorial's extra room-side wall is only a logical surface; it must not
            // show up as a wall to edit.
            if (built.GetComponent<DuduSurface>() != null) Object.DestroyImmediate(built.gameObject);
            else if (built.parent == null || before.Contains(built.parent)) built.SetParent(root.transform, true);
        }
        foreach (KeyValuePair<GameObject, bool> entry in active)
        {
            if (entry.Key == null || entry.Key.activeSelf == entry.Value) continue;
            entry.Key.SetActive(entry.Value);
            // Lines the course replaces in play stay out of the picture here too.
            if (entry.Key.GetComponent<CrayonRouteLine>() != null)
                SceneVisibilityManager.instance.Hide(entry.Key, true);
        }
        foreach (KeyValuePair<MonoBehaviour, MonoBehaviour> entry in state)
            if (entry.Key != null) EditorUtility.CopySerialized(entry.Value, entry.Key);
        Object.DestroyImmediate(saved);

        foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
        {
            part.gameObject.hideFlags = PreviewFlags;
            foreach (Component component in part.GetComponents<Component>())
                if (component != null) component.hideFlags = PreviewFlags;
        }
        SceneView.RepaintAll();
    }

    private static void Remember<T>(Dictionary<MonoBehaviour, MonoBehaviour> state, GameObject holder) where T : MonoBehaviour
    {
        foreach (T item in Object.FindObjectsByType<T>(FindObjectsInactive.Include))
        {
            if (item.gameObject == holder) continue;
            T copy = holder.AddComponent<T>();
            EditorUtility.CopySerialized(item, copy);
            state[item] = copy;
        }
    }

    public static bool IsPreview(GameObject target) => (target.hideFlags & HideFlags.NotEditable) != 0 && target.transform.root.name == RootName;

    // A player build opens the scene from disk, but keep it out of the way regardless.
    private sealed class BuildHook : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => Remove();
        public void OnPostprocessBuild(BuildReport report) => Schedule();
    }
}
#endif
