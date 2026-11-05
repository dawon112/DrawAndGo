#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public enum WallShape
{
    // Click to place; only moves.
    Point,
    // Drag a rectangle; its corners resize it.
    Rect,
    // Drag from end to end; a height difference makes a slope.
    Line,
    // Drag along the movement path.
    Path
}

// What the palette can place.
public sealed class WallItem
{
    public readonly string Label;
    public readonly WallShape Shape;
    public readonly string PrefabPath;
    public readonly float Gap;
    public readonly string Tip;

    public WallItem(string label, WallShape shape, string prefabPath, float gap, string tip)
    {
        Label = label;
        Shape = shape;
        PrefabPath = prefabPath;
        Gap = gap;
        Tip = tip;
    }

    public GameObject Prefab => PrefabPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) : null;
    public bool IsCoin => PrefabPath == WallPlacerCore.CoinPath;
    // Magnets and storm clouds keep the wall they were given and never look for a room-side one.
    public bool NeedsRoomFacingWall =>
        Prefab != null && (Prefab.GetComponent<LineMagnet>() != null || Prefab.GetComponent<DuduHomingShooter>() != null);
}

// One Dudu wall seen flat from the side the room is on, in wall units:
// x to the viewer's right, y up, (0, 0) at the wall's centre.
public sealed class WallView
{
    public readonly DuduSurface Surface;
    public readonly bool Flipped;
    public readonly Vector3 Normal;
    public readonly Vector3 Right;
    public readonly Vector3 Up;
    public readonly float FaceOffset;
    public readonly Rect WalkRect;
    public readonly Rect WallRect;

    public WallView(DuduSurface surface)
    {
        Surface = surface;
        Flipped = FacesAwayFromRoom(surface);
        Normal = (Flipped ? -surface.Normal : surface.Normal).normalized;
        Up = surface.Up.normalized;
        Right = (Flipped ? -surface.Right : surface.Right).normalized;
        FaceOffset = FaceDistance(surface, Normal);
        WalkRect = new Rect(-surface.Width * 0.5f, -surface.Height * 0.5f, surface.Width, surface.Height);
        Rect wall = WalkRect;
        if (surface.TryGetComponent(out Collider collider))
            wall = Encapsulate(wall, ProjectBounds(collider.bounds));
        WallRect = wall;
    }

    public Quaternion Rotation => Quaternion.LookRotation(-Normal, Up);
    // Dudu's usual ground height: the line the builders use on a 6 m wall.
    public float GroundY => WalkRect.yMin + 0.6f;

    public Vector3 ToWorld(Vector2 point, float depth) =>
        Surface.transform.position + Right * point.x + Up * point.y + Normal * depth;

    public Vector2 FromWorld(Vector3 world)
    {
        Vector3 relative = world - Surface.transform.position;
        return new Vector2(Vector3.Dot(relative, Right), Vector3.Dot(relative, Up));
    }

    public float DepthOf(Vector3 world) => Vector3.Dot(world - Surface.transform.position, Normal);

    public bool TryFromRay(Ray ray, out Vector2 point, out float distance)
    {
        Plane plane = new Plane(Normal, Surface.transform.position + Normal * FaceOffset);
        point = default;
        if (!plane.Raycast(ray, out distance)) return false;
        point = FromWorld(ray.GetPoint(distance));
        return true;
    }

    // Lies on this wall: close to its plane, facing along it, within its span.
    public bool Holds(Transform target)
    {
        if (Mathf.Abs(DepthOf(target.position)) > 1f ||
            Mathf.Abs(Vector3.Dot(target.forward, Normal)) < 0.9f) return false;
        Rect span = WallRect;
        span.xMin -= 1f; span.xMax += 1f; span.yMin -= 1f; span.yMax += 1f;
        return span.Contains(FromWorld(target.position));
    }

    public Rect ProjectBounds(Bounds bounds)
    {
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector2 point = FromWorld(corner);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Rect Encapsulate(Rect a, Rect b) => Rect.MinMaxRect(
        Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    private static float FaceDistance(DuduSurface surface, Vector3 normal)
    {
        Bounds bounds;
        if (surface.TryGetComponent(out Collider collider)) bounds = collider.bounds;
        else if (surface.TryGetComponent(out Renderer look)) bounds = look.bounds;
        else return 0f;
        Vector3 reach = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
        return Vector3.Dot(bounds.center - surface.transform.position, normal) + Vector3.Dot(reach, bounds.extents);
    }

    // The tutorial's last wall points out of the building: open space behind its normal,
    // another wall of the room in front of the opposite side.
    private static bool FacesAwayFromRoom(DuduSurface surface)
    {
        Physics.SyncTransforms();
        Vector3 normal = surface.Normal.normalized;
        Vector3 origin = surface.transform.position;
        return !Physics.Raycast(origin + normal * (FaceDistance(surface, normal) + 0.05f), normal, 80f, ~0, QueryTriggerInteraction.Ignore) &&
            Physics.Raycast(origin - normal * (FaceDistance(surface, -normal) + 0.05f), -normal, 80f, ~0, QueryTriggerInteraction.Ignore);
    }
}

// Creates and edits wall objects. Each kind of obstacle keeps its wall, depth and path in
// its own fields; everything here goes through one Bind so they cannot drift apart.
public static class WallPlacerCore
{
    public const string RootName = CrayonRouteLine.PlacedRootName;
    public const string CoinPath = "Assets/Prefabs/Collectibles/Coin.prefab";
    private const string BlockPath = "Assets/Prefabs/Obstacles/Block.prefab";
    private const string CloudPath = "Assets/Prefabs/Enemies/Storm Cloud.prefab";
    private const string MoverPath = "Assets/Prefabs/Obstacles/Vertical Obstacle.prefab";
    private const string CloudSpritePath = "Assets/Art/Obstacle/darkcloud.png";
    private const string CloudAnimatorPath = "Assets/Animations/Homing Enemy.controller";
    private const string ProjectilePath = "Assets/Prefabs/Enemies/HomingProjectile.prefab";
    private const string LightningMaterialPath = "Assets/Materials/DuduGroundLine.mat";
    private const string LineItem = "바닥 선";
    private const string CheckpointItem = "체크포인트";
    private const float MinSize = 0.2f;
    // Deep enough to reach Dudu's plane behind the art, which sits in front of the wall.
    private const float SolidDepth = 0.8f;
    private static readonly Vector2 CoinPickupSize = new Vector2(0.7f, 0.9f);

    public static readonly WallItem[] Items =
    {
        new WallItem(LineItem, WallShape.Line, null, 0f, "드래그: 양 끝을 잇는 선. 높이가 다르면 경사가 돼요."),
        new WallItem("블록", WallShape.Rect, BlockPath, 0.06f, "드래그: 크기 지정 / 클릭: 기본 크기. 두두가 밟을 수 있어요."),
        new WallItem("움직이는 장애물", WallShape.Path, MoverPath, 0.06f, "드래그: 왕복 경로(가로·세로 중 긴 쪽) / 클릭: 기본 경로."),
        new WallItem("느림 안개", WallShape.Point, "Assets/Prefabs/Obstacles/Slow Stain Obstacle.prefab", 0.06f, "클릭: 놓기."),
        new WallItem("반전 안개", WallShape.Point, "Assets/Prefabs/Obstacles/Reverse Stain Obstacle.prefab", 0.06f, "클릭: 놓기."),
        new WallItem("바람 구역", WallShape.Rect, "Assets/Prefabs/Obstacles/Zones/WindZone.prefab", 0.03f, "드래그: 구역 지정. 바람 방향·세기는 아래 설정에서."),
        new WallItem("핫존", WallShape.Rect, "Assets/Prefabs/Obstacles/Zones/HotZone.prefab", 0.03f, "드래그: 구역 지정."),
        new WallItem("그리기 금지", WallShape.Rect, "Assets/Prefabs/Obstacles/Zones/NoDrawZone.prefab", 0.03f, "드래그: 구역 지정."),
        new WallItem("자석", WallShape.Point, "Assets/Prefabs/Obstacles/LineMagnet.prefab", 0.06f, "클릭: 놓기."),
        new WallItem("지우개", WallShape.Point, "Assets/Prefabs/Obstacles/Moving Eraser.prefab", 0.06f, "클릭: 놓기."),
        new WallItem("먹구름", WallShape.Point, CloudPath, 0.06f, "클릭: 놓기."),
        new WallItem("코인", WallShape.Point, CoinPath, 0.1f, "클릭: 놓기. 스테이지의 코인 목록에 자동으로 추가돼요."),
        new WallItem(CheckpointItem, WallShape.Rect, null, 0f, "드래그: 범위 지정. 게임에선 보이지 않아요."),
    };

    public static void EnsurePrefabs()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(BlockPath) == null) CreateBlockPrefab();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(CloudPath) == null) CreateCloudPrefab();
    }

    // The obstacle block's art and collider, standing still and safe to stand on.
    private static void CreateBlockPrefab()
    {
        GameObject block = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MoverPath));
        PrefabUtility.UnpackPrefabInstance(block, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        Object.DestroyImmediate(block.GetComponent<DuduMovingObstacle>());
        Object.DestroyImmediate(block.GetComponent<Rigidbody>());
        BoxCollider box = block.GetComponent<BoxCollider>();
        box.size = new Vector3(box.size.x, box.size.y, SolidDepth / Mathf.Abs(block.transform.localScale.z));
        block.name = "Block";
        PrefabUtility.SaveAsPrefabAsset(block, BlockPath);
        Object.DestroyImmediate(block);
    }

    private static void CreateCloudPrefab()
    {
        GameObject cloud = new GameObject("StormCloud", typeof(SpriteRenderer), typeof(Animator));
        cloud.transform.localScale = Vector3.one * 0.13f;
        cloud.GetComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CloudSpritePath);
        cloud.GetComponent<Animator>().runtimeAnimatorController =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CloudAnimatorPath);
        DuduHomingShooter shooter = cloud.AddComponent<DuduHomingShooter>();
        SerializedObject data = new SerializedObject(shooter);
        data.FindProperty("projectilePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath);
        data.FindProperty("projectileMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(LightningMaterialPath);
        data.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(cloud, CloudPath);
        Object.DestroyImmediate(cloud);
    }

    // ---- Placing ----

    // a and b are the drag's start and end in wall units; the same point means a click.
    public static GameObject Place(WallItem item, WallView view, Vector2 a, Vector2 b)
    {
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        GameObject placed = Create(item);
        placed.transform.SetParent(WallGroup(view), false);
        // Online sync tells hazards apart by name; a storm cloud keeps its "StormCloud" prefix.
        placed.name = GameObjectUtility.GetUniqueNameForSibling(placed.transform.parent,
            placed.GetComponent<DuduHomingShooter>() != null ? "StormCloud" : item.Label);
        placed.transform.SetPositionAndRotation(view.ToWorld(a, view.FaceOffset + item.Gap), view.Rotation);

        bool dragged = (b - a).sqrMagnitude > 0.01f;
        switch (item.Shape)
        {
            case WallShape.Line:
                SetLine(placed, view, a, dragged ? b : a + Vector2.right * 4f);
                break;
            case WallShape.Rect:
                FitColliderToArt(placed);
                Rect start = GetRect(placed, view);
                SetRect(placed, view, dragged ? Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y))
                    : new Rect(a - start.size * 0.5f, start.size));
                break;
            case WallShape.Path:
                DeepenCollider(placed);
                if (dragged) SetPath(placed, view, a, b);
                break;
            case WallShape.Point:
                TightenStain(placed);
                break;
        }
        Bind(placed, view);
        Undo.RegisterCreatedObjectUndo(placed, "Place " + item.Label);
        Undo.CollapseUndoOperations(group);
        if (item.IsCoin) SyncCoins();
        EditorSceneManager.MarkSceneDirty(placed.scene);
        return placed;
    }

    private static GameObject Create(WallItem item)
    {
        if (item.Label == LineItem)
        {
            GameObject line = new GameObject("Line", typeof(BoxCollider));
            line.transform.localScale = new Vector3(4f, 0.14f, 0.16f);
            line.AddComponent<CrayonRouteLine>().Material = CrayonRouteLineTool.LoadOrCreateMaterial();
            return line;
        }
        if (item.Label == CheckpointItem)
        {
            GameObject checkpoint = new GameObject("Checkpoint", typeof(BoxCollider));
            BoxCollider box = checkpoint.GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1f, 1f, 3f);
            checkpoint.transform.localScale = new Vector3(0.6f, 2f, 1f);
            checkpoint.AddComponent<DuduCheckpoint>();
            return checkpoint;
        }
        return (GameObject)PrefabUtility.InstantiatePrefab(item.Prefab, EditorSceneManager.GetActiveScene());
    }

    private static Transform WallGroup(WallView view)
    {
        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
        Transform root = null;
        foreach (GameObject candidate in scene.GetRootGameObjects())
            if (candidate.name == RootName) root = candidate.transform;
        if (root == null)
        {
            root = new GameObject(RootName).transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Create level root");
        }
        // Not the wall's own name: code elsewhere finds scene objects by name.
        string groupName = view.Surface.name + " Objects";
        Transform group = root.Find(groupName);
        if (group == null)
        {
            group = new GameObject(groupName).transform;
            Undo.RegisterCreatedObjectUndo(group.gameObject, "Create wall group");
            group.SetParent(root, false);
        }
        return group;
    }

    // Zone art has transparent margins; what Haru sees should be exactly where the zone works.
    private static void FitColliderToArt(GameObject zone)
    {
        if (!zone.TryGetComponent(out BoxCollider box) || !zone.TryGetComponent(out SpriteRenderer art) ||
            art.sprite == null || zone.GetComponent<WindZone>() == null && zone.GetComponent<HotZone>() == null &&
            zone.GetComponent<NoDrawZone>() == null) return;
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        foreach (Vector2 vertex in art.sprite.vertices)
        {
            min = Vector2.Min(min, vertex);
            max = Vector2.Max(max, vertex);
        }
        box.center = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, box.center.z);
        box.size = new Vector3(max.x - min.x, max.y - min.y, box.size.z);
    }

    private static void DeepenCollider(GameObject mover)
    {
        if (!mover.TryGetComponent(out BoxCollider box)) return;
        float depth = SolidDepth / Mathf.Max(0.0001f, Mathf.Abs(mover.transform.lossyScale.z));
        if (box.size.z < depth) box.size = new Vector3(box.size.x, box.size.y, depth);
    }

    // The fog art is far smaller than the prefab's trigger, which felt like an invisible wall.
    private static void TightenStain(GameObject stain)
    {
        if (stain.GetComponent<DuduStainObstacle>() == null || !stain.TryGetComponent(out SphereCollider trigger)) return;
        trigger.center = Vector3.zero;
        trigger.radius = 2.2f;
    }

    // ---- Reading and changing shapes (wall units) ----

    public static WallShape ShapeOf(GameObject target)
    {
        if (target.GetComponent<CrayonRouteLine>() != null) return WallShape.Line;
        if (target.GetComponent<DuduMovingObstacle>() != null) return WallShape.Path;
        if (target.GetComponent<WindZone>() != null || target.GetComponent<HotZone>() != null ||
            target.GetComponent<NoDrawZone>() != null || target.GetComponent<DuduCheckpoint>() != null || IsBlock(target))
            return WallShape.Rect;
        return WallShape.Point;
    }

    public static Rect GetRect(GameObject target, WallView view)
    {
        Transform t = target.transform;
        // Coins are resized to this pickup when play starts, whatever their scale in the scene.
        if (target.GetComponent<SurfaceCoinPickup>() != null)
            return new Rect(view.FromWorld(t.position) - CoinPickupSize * 0.5f, CoinPickupSize);
        if (target.TryGetComponent(out BoxCollider box))
        {
            Vector2 size = Vector2.Scale(box.size, new Vector2(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y)));
            return new Rect(view.FromWorld(t.TransformPoint(box.center)) - size * 0.5f, size);
        }
        if (target.TryGetComponent(out SphereCollider sphere))
        {
            float radius = sphere.radius * Mathf.Abs(t.lossyScale.x);
            return new Rect(view.FromWorld(t.TransformPoint(sphere.center)) - Vector2.one * radius, Vector2.one * radius * 2f);
        }
        bool any = false;
        Bounds bounds = default;
        foreach (Renderer look in target.GetComponentsInChildren<Renderer>(true))
        {
            if (!any) bounds = look.bounds; else bounds.Encapsulate(look.bounds);
            any = true;
        }
        Rect result = any ? view.ProjectBounds(bounds) : new Rect(view.FromWorld(t.position), Vector2.zero);
        Vector2 centre = result.center;
        Vector2 shown = Vector2.Max(result.size, Vector2.one * 0.4f);
        return new Rect(centre - shown * 0.5f, shown);
    }

    public static void SetRect(GameObject target, WallView view, Rect rect)
    {
        Transform t = target.transform;
        BoxCollider box = target.GetComponent<BoxCollider>();
        if (box == null) return;
        Vector2 size = Vector2.Max(rect.size, Vector2.one * MinSize);
        Vector3 parent = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(size.x / box.size.x / parent.x, size.y / box.size.y / parent.y, t.localScale.z);
        float depth = view.DepthOf(t.position);
        t.SetPositionAndRotation(view.ToWorld(rect.position + size * 0.5f, depth), view.Rotation);
        t.position -= t.TransformVector(new Vector3(box.center.x, box.center.y, 0f));
    }

    public static void GetLine(GameObject line, WallView view, out Vector2 a, out Vector2 b)
    {
        Transform t = line.transform;
        Vector2 half = (view.FromWorld(t.position + t.right) - view.FromWorld(t.position)) * Mathf.Abs(t.lossyScale.x) * 0.5f;
        Vector2 centre = view.FromWorld(t.position);
        a = centre - half;
        b = centre + half;
    }

    public static void SetLine(GameObject line, WallView view, Vector2 a, Vector2 b)
    {
        Vector2 along = b - a;
        if (along.magnitude < MinSize) along = along.sqrMagnitude > 0f ? along.normalized * MinSize : Vector2.right * MinSize;
        Transform t = line.transform;
        Vector3 parent = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(along.magnitude / parent.x, t.localScale.y, t.localScale.z);
        t.SetPositionAndRotation(view.ToWorld(a + along * 0.5f, view.Surface.SurfaceOffset),
            view.Rotation * Quaternion.Euler(0f, 0f, Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg));
        line.GetComponent<CrayonRouteLine>().Rebuild();
    }

    // Movers go from their placed position to the right or up, and back.
    public static void GetPath(GameObject mover, WallView view, out Vector2 start, out Vector2 end)
    {
        SerializedObject data = new SerializedObject(mover.GetComponent<DuduMovingObstacle>());
        bool horizontal = data.FindProperty("movementAxis").enumValueIndex == (int)DuduMovingObstacle.MovementAxis.Horizontal;
        float travel = data.FindProperty("travelDistance").floatValue;
        start = view.FromWorld(mover.transform.position);
        end = start + (horizontal ? new Vector2(travel, 0f) : new Vector2(0f, travel));
    }

    public static void SetPath(GameObject mover, WallView view, Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        bool horizontal = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y);
        // Movers start at the low end. On a wall facing out of the room this is still the
        // viewer's left: in play they run on the room-side surface, whose x matches the view.
        bool swap = horizontal ? delta.x < 0f : delta.y < 0f;
        Vector2 start = swap ? (horizontal ? new Vector2(to.x, from.y) : new Vector2(from.x, to.y)) : from;
        Transform t = mover.transform;
        t.position = view.ToWorld(start, view.DepthOf(t.position));
        SerializedObject data = new SerializedObject(mover.GetComponent<DuduMovingObstacle>());
        data.FindProperty("movementAxis").enumValueIndex =
            (int)(horizontal ? DuduMovingObstacle.MovementAxis.Horizontal : DuduMovingObstacle.MovementAxis.Vertical);
        data.FindProperty("travelDistance").floatValue = horizontal ? Mathf.Abs(delta.x) : Mathf.Abs(delta.y);
        data.ApplyModifiedProperties();
        Bind(mover, view);
    }

    // Same place on another wall (kept inside it), at the same distance from its face.
    public static void MoveToWall(GameObject target, WallView from, WallView to)
    {
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Transform t = target.transform;
        Undo.SetTransformParent(t, WallGroup(to), "Move to wall");
        Undo.RecordObject(t, "Move to wall");
        // Blank first, so it does not count as its own namesake.
        string wanted = target.name;
        target.name = "";
        target.name = GameObjectUtility.GetUniqueNameForSibling(t.parent, wanted);
        Vector2 at = from.FromWorld(t.position);
        at = new Vector2(Mathf.Clamp(at.x, to.WalkRect.xMin, to.WalkRect.xMax), Mathf.Clamp(at.y, to.WalkRect.yMin, to.WalkRect.yMax));
        if (ShapeOf(target) == WallShape.Line)
        {
            GetLine(target, from, out Vector2 a, out Vector2 b);
            Vector2 half = (b - a) * 0.5f;
            SetLine(target, to, at - half, at + half);
        }
        else
        {
            float gap = from.DepthOf(t.position) - from.FaceOffset;
            t.SetPositionAndRotation(to.ToWorld(at, to.FaceOffset + gap), to.Rotation);
            Bind(target, to);
        }
        Undo.CollapseUndoOperations(group);
    }

    public static void MoveBy(GameObject target, WallView view, Vector3 startPosition, Vector2 offset)
    {
        target.transform.position = startPosition + view.Right * offset.x + view.Up * offset.y;
        Bind(target, view);
    }

    // Writes the wall, depth and position into whichever fields this object's scripts keep.
    public static void Bind(GameObject target, WallView view)
    {
        Vector3 world = target.transform.position;
        float depth = Mathf.Abs(view.DepthOf(world));
        Vector2 onSurface = view.Surface.WorldToSurface(world);
        DuduSurfaceMovement dudu = Object.FindAnyObjectByType<DuduSurfaceMovement>(FindObjectsInactive.Include);
        foreach (MonoBehaviour script in target.GetComponents<MonoBehaviour>())
        {
            if (script == null || script is CrayonRouteLine) continue;
            SerializedObject data = new SerializedObject(script);
            // Scripts snap to their surface's normal side in the editor. On a wall that faces
            // out of the room, leave it empty: they find the room-side surface in play.
            bool resolvesItself = script is DuduStainObstacle || script is DuduMovingObstacle;
            SetObject(data, "surface", view.Flipped && resolvesItself ? null : view.Surface);
            SetVector(data, "surfacePosition", onSurface);
            SetFloat(data, "surfaceNormalOffset", depth - view.Surface.SurfaceOffset);
            SetFloat(data, "visualOffset", depth);
            if (script is DuduHomingShooter) SetObject(data, "target", dudu);
            if (script is DuduMovingObstacle)
            {
                bool horizontal = data.FindProperty("movementAxis").enumValueIndex == (int)DuduMovingObstacle.MovementAxis.Horizontal;
                SetVector(data, "centerPosition", onSurface +
                    (horizontal ? Vector2.right : Vector2.up) * data.FindProperty("travelDistance").floatValue * 0.5f);
            }
            data.ApplyModifiedProperties();
        }
        target.transform.position = world;
    }

    private static void SetObject(SerializedObject data, string name, Object value)
    {
        SerializedProperty property = data.FindProperty(name);
        if (property != null) property.objectReferenceValue = value;
    }

    private static void SetVector(SerializedObject data, string name, Vector2 value)
    {
        SerializedProperty property = data.FindProperty(name);
        if (property != null) property.vector2Value = value;
    }

    private static void SetFloat(SerializedObject data, string name, float value)
    {
        SerializedProperty property = data.FindProperty(name);
        if (property != null) property.floatValue = value;
    }

    // ---- Finding what is on a wall ----

    public static bool IsBlock(GameObject target) =>
        PrefabUtility.GetCorrespondingObjectFromSource(target) is GameObject source &&
        AssetDatabase.GetAssetPath(source) == BlockPath;

    public static List<GameObject> FindPlaced()
    {
        HashSet<GameObject> found = new HashSet<GameObject>();
        // Scene searches skip the never-saved preview objects, so look through its root too.
        GameObject preview = TutorialCoursePreview.FindRoot();
        void Add<T>() where T : Component
        {
            foreach (T item in Object.FindObjectsByType<T>(FindObjectsInactive.Include)) found.Add(item.gameObject);
            if (preview == null) return;
            foreach (T item in preview.GetComponentsInChildren<T>(true)) found.Add(item.gameObject);
        }
        Add<CrayonRouteLine>();
        Add<DuduMovingObstacle>();
        Add<DuduStainObstacle>();
        Add<WindZone>();
        Add<HotZone>();
        Add<NoDrawZone>();
        Add<LineMagnet>();
        Add<MovingEraserObstacle>();
        Add<DuduHomingShooter>();
        Add<DuduCheckpoint>();
        Add<SurfaceCoinPickup>();
        Add<TutorialStarCollectible>();
        foreach (BoxCollider box in Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include))
            if (IsBlock(box.gameObject)) found.Add(box.gameObject);
        // Lines the code-built course replaces in play are hidden and not part of the level.
        found.RemoveWhere(item => SceneVisibilityManager.instance.IsHidden(item));
        return new List<GameObject>(found);
    }

    public static bool IsCodeBuilt(GameObject target) => TutorialCoursePreview.IsPreview(target);

    public static List<DuduSurface> FindWalls()
    {
        List<DuduSurface> walls = new List<DuduSurface>();
        // The stage's route order first, so "next wall" walks the course.
        CornerRoomLevel corner = Object.FindAnyObjectByType<CornerRoomLevel>(FindObjectsInactive.Include);
        if (corner != null && corner.surfaces != null) walls.AddRange(corner.surfaces);
        TutorialRoomLevel tutorial = Object.FindAnyObjectByType<TutorialRoomLevel>(FindObjectsInactive.Include);
        if (tutorial != null && tutorial.Sections != null) walls.AddRange(tutorial.Sections);
        walls.RemoveAll(wall => wall == null);
        foreach (DuduSurface wall in Object.FindObjectsByType<DuduSurface>(FindObjectsInactive.Include))
            if (!walls.Contains(wall)) walls.Add(wall);
        return walls;
    }

    // The door counts the stage's coin list, and an empty entry counts as collected.
    public static void SyncCoins()
    {
        CornerRoomLevel level = Object.FindAnyObjectByType<CornerRoomLevel>(FindObjectsInactive.Include);
        if (level == null) return;
        List<GameObject> coins = new List<GameObject>();
        if (level.coins != null)
            foreach (GameObject coin in level.coins)
                if (coin != null) coins.Add(coin);
        foreach (SurfaceCoinPickup pickup in Object.FindObjectsByType<SurfaceCoinPickup>(FindObjectsInactive.Include))
            if (!coins.Contains(pickup.gameObject) && pickup.transform.root.name == RootName) coins.Add(pickup.gameObject);
        if (level.coins != null && level.coins.Length == coins.Count && !System.Array.Exists(level.coins, coin => coin == null)) return;
        Undo.RecordObject(level, "Update coin list");
        level.coins = coins.ToArray();
        EditorUtility.SetDirty(level);
    }
}
#endif
