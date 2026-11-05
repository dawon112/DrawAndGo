#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Turns the course BuildSketchCourse makes in code into real scene objects under
// "Placed Level", so the wall placer can move them. It reads them from the running code's
// preview, so nothing is copied by hand. After this the tutorial stops building them.
// The first wall's drawing and erasing lesson stays in code: its steps check fixed gaps.
public static class TutorialCourseBake
{
    private const string MenuPath = "Tools/Draw And Go/튜토리얼 코스를 씬으로 옮기기";
    private const string SketchPrefix = "Sketch_";
    private const string StarPrefix = "Tutorial_Star_";
    private static readonly WallItem StarItem =
        new WallItem("별", WallShape.Point, "Assets/Prefabs/Collectibles/Tutorial Star.prefab", 0.13f, "");

    [MenuItem(MenuPath)]
    private static void BakeFromMenu()
    {
        if (!EditorUtility.DisplayDialog("튜토리얼 코스를 씬으로 옮기기",
            "코드가 만들던 코스(계단, 바닥, 구역, 먹구름, 별)를 지금 씬의 오브젝트로 옮기고 씬을 저장해요.\n" +
            "그 뒤로는 벽 배치 도구로 고칠 수 있어요. 첫 벽의 그리기/지우기 수업은 코드에 남아요.",
            "옮기기", "취소")) return;
        Bake(true);
    }

    [MenuItem(MenuPath, true)]
    private static bool CanBake()
    {
        TutorialSequenceController course = Object.FindAnyObjectByType<TutorialSequenceController>(FindObjectsInactive.Include);
        return !EditorApplication.isPlayingOrWillChangePlaymode && course != null && CanBake(course);
    }

    private static bool CanBake(TutorialSequenceController course)
    {
        SerializedObject data = new SerializedObject(course);
        return data.FindProperty("useSketchCourse").boolValue && !data.FindProperty("sketchCourseInScene").boolValue;
    }

    // Returns how many objects were moved into the scene, or -1 when there is nothing to move.
    public static int Bake(bool save)
    {
        TutorialSequenceController course = Object.FindAnyObjectByType<TutorialSequenceController>(FindObjectsInactive.Include);
        if (course == null || !CanBake(course)) return -1;
        WallPlacerCore.EnsurePrefabs();
        TutorialCoursePreview.RebuildNow(true);
        GameObject preview = TutorialCoursePreview.FindRoot();
        if (preview == null) return -1;

        List<WallView> views = new List<WallView>();
        foreach (DuduSurface wall in WallPlacerCore.FindWalls()) views.Add(new WallView(wall));

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        DuduHomingShooter cloud = null;
        TutorialStarCollectible[] stars = new TutorialStarCollectible[3];
        int moved = 0;
        foreach (Transform source in preview.GetComponentsInChildren<Transform>(true))
        {
            bool isStar = source.name.StartsWith(StarPrefix);
            if (!isStar && !source.name.StartsWith(SketchPrefix)) continue;
            WallView view = ViewFor(source, views);
            if (view == null)
            {
                Debug.LogWarning($"{source.name}: 어느 벽에 있는지 찾지 못해서 옮기지 못했어요.");
                continue;
            }
            GameObject placed = Move(source.gameObject, view);
            if (placed == null)
            {
                Debug.LogWarning($"{source.name}: 옮기는 방법을 모르는 종류예요.");
                continue;
            }
            moved++;
            if (placed.TryGetComponent(out DuduHomingShooter shooter)) cloud = shooter;
            if (isStar && int.TryParse(source.name.Substring(StarPrefix.Length), out int number) && number >= 1 && number <= stars.Length)
                stars[number - 1] = placed.GetComponent<TutorialStarCollectible>();
        }

        SerializedObject data = new SerializedObject(course);
        data.FindProperty("sketchCourseInScene").boolValue = true;
        data.FindProperty("placedDangerCloud").objectReferenceValue = cloud;
        SerializedProperty starList = data.FindProperty("placedStars");
        starList.arraySize = stars.Length;
        for (int i = 0; i < stars.Length; i++) starList.GetArrayElementAtIndex(i).objectReferenceValue = stars[i];
        data.ApplyModifiedProperties();
        Undo.CollapseUndoOperations(group);

        // The preview now shows only what is still built in code.
        TutorialCoursePreview.RebuildNow(false);
        EditorSceneManager.MarkSceneDirty(course.gameObject.scene);
        if (save) EditorSceneManager.SaveScene(course.gameObject.scene);
        Debug.Log($"튜토리얼 코스 {moved}개를 씬으로 옮겼어요.");
        return moved;
    }

    private static WallView ViewFor(Transform source, List<WallView> views)
    {
        WallView best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (WallView view in views)
        {
            if (!view.Holds(source)) continue;
            float distance = Mathf.Abs(view.DepthOf(source.position));
            if (distance >= bestDistance) continue;
            best = view;
            bestDistance = distance;
        }
        return best;
    }

    private static GameObject Move(GameObject source, WallView view)
    {
        GameObject placed;
        Vector2 at = view.FromWorld(source.transform.position);
        if (source.GetComponent<CrayonRouteLine>() != null)
        {
            WallPlacerCore.GetLine(source, view, out Vector2 a, out Vector2 b);
            placed = WallPlacerCore.Place(Item("바닥 선"), view, a, b);
        }
        else if (source.TryGetComponent(out DuduMovingObstacle sourceMover))
        {
            WallPlacerCore.GetPath(source, view, out Vector2 start, out Vector2 end);
            placed = WallPlacerCore.Place(Item("움직이는 장애물"), view, start, end);
            CopyShape(source, placed);
            CopyField(sourceMover, placed.GetComponent<DuduMovingObstacle>(), "roundTripDuration");
            placed.transform.position = view.ToWorld(start, view.DepthOf(placed.transform.position));
        }
        else if (source.TryGetComponent(out WindZone sourceWind))
        {
            placed = PlaceZone(Item("바람 구역"), source, view);
            WindZone wind = placed.GetComponent<WindZone>();
            CopyField(sourceWind, wind, "windDirection");
            CopyField(sourceWind, wind, "windForce");
        }
        else if (source.GetComponent<HotZone>() != null) placed = PlaceZone(Item("핫존"), source, view);
        else if (source.GetComponent<NoDrawZone>() != null) placed = PlaceZone(Item("그리기 금지"), source, view);
        else if (source.GetComponent<DuduCheckpoint>() != null) placed = PlaceRect(Item("체크포인트"), source, view);
        else if (source.TryGetComponent(out DuduHomingShooter sourceCloud))
        {
            placed = WallPlacerCore.Place(Item("먹구름"), view, at, at);
            placed.transform.localScale = source.transform.localScale;
            DuduHomingShooter cloud = placed.GetComponent<DuduHomingShooter>();
            CopyField(sourceCloud, cloud, "attackRange");
            CopyField(sourceCloud, cloud, "projectileMaxHorizontalReach");
            CopyField(sourceCloud, cloud, "projectileMaterial");
            CopyField(sourceCloud, cloud, "projectilePrefab");
            return Finish(placed, "StormCloud Danger", view);
        }
        else if (source.GetComponent<TutorialStarCollectible>() != null)
            placed = WallPlacerCore.Place(StarItem, view, at, at);
        // SketchBlock: the mover's art and collider, standing still.
        else if (source.GetComponent<BoxCollider>() != null && source.GetComponent<SpriteRenderer>() != null)
            placed = PlaceRect(Item("블록"), source, view);
        else return null;
        return Finish(placed, source.name, view);
    }

    private static GameObject Finish(GameObject placed, string name, WallView view)
    {
        placed.name = GameObjectUtility.GetUniqueNameForSibling(placed.transform.parent, name);
        WallPlacerCore.Bind(placed, view);
        return placed;
    }

    private static GameObject PlaceRect(WallItem item, GameObject source, WallView view)
    {
        Rect rect = WallPlacerCore.GetRect(source, view);
        return WallPlacerCore.Place(item, view, rect.min, rect.max);
    }

    // Exactly the code's scale and collider: the art is not always fitted to the zone.
    private static GameObject PlaceZone(WallItem item, GameObject source, WallView view)
    {
        GameObject placed = PlaceRect(item, source, view);
        float depth = view.DepthOf(placed.transform.position);
        CopyShape(source, placed);
        Rect rect = WallPlacerCore.GetRect(source, view);
        BoxCollider box = placed.GetComponent<BoxCollider>();
        placed.transform.position = view.ToWorld(rect.center, depth) -
            placed.transform.TransformVector(new Vector3(box.center.x, box.center.y, 0f));
        return placed;
    }

    private static void CopyShape(GameObject source, GameObject placed)
    {
        placed.transform.localScale = source.transform.localScale;
        if (source.TryGetComponent(out BoxCollider from) && placed.TryGetComponent(out BoxCollider to))
        {
            to.center = from.center;
            to.size = from.size;
        }
    }

    private static void CopyField(Object from, Object to, string field)
    {
        SerializedObject target = new SerializedObject(to);
        target.CopyFromSerializedProperty(new SerializedObject(from).FindProperty(field));
        target.ApplyModifiedProperties();
    }

    private static WallItem Item(string label) => System.Array.Find(WallPlacerCore.Items, item => item.Label == label);
}
#endif
