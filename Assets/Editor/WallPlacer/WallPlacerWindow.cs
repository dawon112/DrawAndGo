#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Level editing for the 2D course on the 3D walls: click a wall and the Scene view turns
// into a flat, gridded page of it; pick something from the palette, then click or drag.
// The geometry and per-obstacle bookkeeping live in WallPlacerCore.
public sealed class WallPlacerWindow : EditorWindow
{
    private const int SelectTool = -1;
    private const float HandlePixels = 7f;
    private static readonly int ControlHash = "WallPlacerControl".GetHashCode();
    private static readonly float[] GridSizes = { 0f, 0.1f, 0.25f, 0.5f, 1f };
    private static readonly string[] GridLabels = { "끔", "0.1", "0.25", "0.5", "1" };
    private static readonly Color ItemColor = new Color(0.1f, 0.65f, 1f, 1f);
    private static readonly Color SelectedColor = new Color(1f, 0.55f, 0.1f, 1f);
    // The tutorial's code-built course: shown for reference, not editable here.
    private static readonly Color CodeBuiltColor = new Color(0.6f, 0.6f, 0.6f, 0.9f);

    private enum Drag { None, Draw, Move, Corner, LineEnd, PathEnd }

    [SerializeField] private DuduSurface wall;
    [SerializeField] private int tool = SelectTool;
    [SerializeField] private int gridIndex = 2;
    [SerializeField] private Vector2 scroll;
    // The Scene view as it was before going flat.
    [SerializeField] private bool savedView;
    [SerializeField] private Vector3 savedPivot;
    [SerializeField] private Quaternion savedRotation;
    [SerializeField] private float savedSize;
    [SerializeField] private bool savedOrthographic;
    [SerializeField] private bool savedRotationLocked;
    [SerializeField] private bool savedDynamicClip;
    [SerializeField] private float savedNear;
    [SerializeField] private float savedFar;

    private WallView view;
    private DuduSurface viewWall;
    private List<GameObject> placed;
    private Drag drag;
    private GameObject dragTarget;
    private Vector2 dragStart;
    private Vector2 dragCurrent;
    private Vector3 dragStartPosition;
    private Rect dragStartRect;
    private int dragIndex;
    private Vector2 lineOther;
    private Vector2 lineStartA;
    private Vector2 lineStartB;
    private Vector2 dragAnchor;
    private DuduSurface hoverWall;
    private Editor inspector;
    private readonly Dictionary<string, Texture> icons = new Dictionary<string, Texture>();

    [MenuItem("Tools/Draw And Go/벽 배치 도구")]
    public static void Open() => GetWindow<WallPlacerWindow>("벽 배치 도구");

    private WallView View
    {
        get
        {
            if (wall == null) return null;
            if (view == null || viewWall != wall)
            {
                view = new WallView(wall);
                viewWall = wall;
            }
            return view;
        }
    }

    private float Grid => GridSizes[Mathf.Clamp(gridIndex, 0, GridSizes.Length - 1)];
    private List<GameObject> Placed => placed ?? (placed = WallPlacerCore.FindPlaced());

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        Undo.undoRedoPerformed += OnHierarchyChanged;
        Selection.selectionChanged += Repaint;
        EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        WallPlacerCore.EnsurePrefabs();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        Undo.undoRedoPerformed -= OnHierarchyChanged;
        Selection.selectionChanged -= Repaint;
        EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        Tools.hidden = false;
        if (inspector != null) DestroyImmediate(inspector);
    }

    private void OnDestroy() => LeaveWall();

    private void OnHierarchyChanged()
    {
        placed = null;
        if (!EditorApplication.isPlayingOrWillChangePlaymode) WallPlacerCore.SyncCoins();
        Repaint();
        SceneView.RepaintAll();
    }

    private void OnSceneChanged(UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to)
    {
        LeaveWall();
        placed = null;
        Repaint();
    }

    private void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode) LeaveWall();
    }

    // ---- Moving between walls ----

    public void EnterWall(DuduSurface target)
    {
        SceneView scene = SceneView.lastActiveSceneView;
        if (target == null || scene == null) return;
        if (!savedView)
        {
            savedView = true;
            savedPivot = scene.pivot;
            savedRotation = scene.rotation;
            savedSize = scene.size;
            savedOrthographic = scene.orthographic;
            savedRotationLocked = scene.isRotationLocked;
            savedDynamicClip = scene.cameraSettings.dynamicClip;
            savedNear = scene.cameraSettings.nearClip;
            savedFar = scene.cameraSettings.farClip;
        }
        wall = target;
        drag = Drag.None;
        if (tool >= 0 && WallPlacerCore.Items[tool].NeedsRoomFacingWall && View.Flipped) tool = SelectTool;
        Tools.hidden = true;
        FrameWall(scene);
        Repaint();
    }

    private void FrameWall(SceneView scene)
    {
        WallView current = View;
        if (current == null) return;
        scene.in2DMode = false;
        scene.isRotationLocked = true;
        float aspect = Mathf.Max(0.5f, scene.camera.aspect);
        scene.LookAt(current.ToWorld(current.WalkRect.center, current.FaceOffset), current.Rotation,
            Mathf.Max(current.WalkRect.height * 0.62f, current.WalkRect.width * 0.56f / aspect), true, true);
        scene.Repaint();
    }

    public void LeaveWall()
    {
        wall = null;
        view = null;
        drag = Drag.None;
        tool = SelectTool;
        Tools.hidden = false;
        SceneView scene = SceneView.lastActiveSceneView;
        if (savedView && scene != null)
        {
            CameraSettings(scene, savedDynamicClip, savedNear, savedFar);
            scene.isRotationLocked = savedRotationLocked;
            scene.LookAt(savedPivot, savedRotation, savedSize, savedOrthographic, true);
        }
        savedView = false;
        SceneView.RepaintAll();
    }

    private static void CameraSettings(SceneView scene, bool dynamicClip, float near, float far)
    {
        SceneView.CameraSettings settings = scene.cameraSettings;
        settings.dynamicClip = dynamicClip;
        settings.nearClip = near;
        settings.farClip = far;
        scene.cameraSettings = settings;
    }

    private void StepWall(int direction)
    {
        List<DuduSurface> walls = WallPlacerCore.FindWalls();
        if (walls.Count == 0) return;
        int index = wall != null ? walls.IndexOf(wall) : -1;
        EnterWall(walls[(index + direction + walls.Count) % walls.Count]);
    }

    // ---- Window ----

    private void OnGUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("플레이 중에는 쓸 수 없어요.", MessageType.Info);
            return;
        }
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawWallPicker();
        if (wall != null)
        {
            EditorGUILayout.Space(6f);
            gridIndex = EditorGUILayout.Popup("격자 맞춤", gridIndex, GridLabels);
            EditorGUILayout.Space(6f);
            DrawPalette();
            EditorGUILayout.Space(6f);
            DrawSelection();
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawWallPicker()
    {
        List<DuduSurface> walls = WallPlacerCore.FindWalls();
        if (walls.Count == 0)
        {
            EditorGUILayout.HelpBox("이 씬에는 두두가 다니는 벽(DuduSurface)이 없어요.", MessageType.Warning);
            return;
        }
        if (wall == null) EditorGUILayout.HelpBox("Scene 뷰에서 벽을 클릭하거나 아래에서 벽을 고르세요.", MessageType.Info);

        string[] names = new string[walls.Count];
        for (int i = 0; i < walls.Count; i++) names[i] = (i + 1) + ". " + walls[i].name;
        int current = wall != null ? walls.IndexOf(wall) : -1;
        int chosen = EditorGUILayout.Popup("벽", current, names);
        if (chosen != current && chosen >= 0) EnterWall(walls[chosen]);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("◀ 이전 벽")) StepWall(-1);
            if (GUILayout.Button("다음 벽 ▶")) StepWall(1);
        }
        if (wall == null) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("벽 전체 보기")) FrameWall(SceneView.lastActiveSceneView);
            if (GUILayout.Button("3D로 돌아가기")) LeaveWall();
        }
    }

    private void DrawPalette()
    {
        EditorGUILayout.LabelField("놓을 것", EditorStyles.boldLabel);
        if (GUILayout.Toggle(tool == SelectTool, "선택 / 이동 (Esc)", "Button", GUILayout.Height(24f))) tool = SelectTool;

        WallItem[] items = WallPlacerCore.Items;
        bool hasCoinList = FindAnyObjectByType<CornerRoomLevel>(FindObjectsInactive.Include) != null;
        int columns = Mathf.Max(2, Mathf.FloorToInt((position.width - 24f) / 84f));
        for (int i = 0; i < items.Length; i += columns)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int k = i; k < Mathf.Min(i + columns, items.Length); k++)
                {
                    WallItem item = items[k];
                    using (new EditorGUI.DisabledScope(item.IsCoin && !hasCoinList || item.NeedsRoomFacingWall && View.Flipped))
                    {
                        GUIContent content = new GUIContent(item.Label, IconOf(item), item.Tip);
                        GUIStyle style = new GUIStyle(GUI.skin.button)
                        {
                            imagePosition = ImagePosition.ImageAbove,
                            fontSize = 11,
                            wordWrap = true
                        };
                        bool on = GUILayout.Toggle(tool == k, content, style, GUILayout.Width(80f), GUILayout.Height(72f));
                        if (on && tool != k) tool = k;
                        else if (!on && tool == k) tool = SelectTool;
                    }
                }
            }
        }
        if (tool >= 0 && tool < items.Length) EditorGUILayout.HelpBox(items[tool].Tip, MessageType.None);
    }

    private Texture IconOf(WallItem item)
    {
        if (icons.TryGetValue(item.Label, out Texture icon) && icon != null) return icon;
        Sprite sprite = null;
        if (item.Prefab != null)
        {
            SpriteRenderer art = item.Prefab.GetComponentInChildren<SpriteRenderer>(true);
            if (art != null) sprite = art.sprite;
        }
        // Previews load in the background; the next repaint picks them up.
        icon = sprite != null ? AssetPreview.GetAssetPreview(sprite) : null;
        if (icon == null) return null;
        icons[item.Label] = icon;
        return icon;
    }

    private void DrawSelection()
    {
        GameObject selected = SelectedPlaced();
        WallView current = View;
        if (selected == null || current == null || !current.Holds(selected.transform))
        {
            EditorGUILayout.LabelField("선택한 항목 없음", EditorStyles.miniLabel);
            return;
        }
        EditorGUILayout.LabelField(selected.name, EditorStyles.boldLabel);
        WallShape shape = WallPlacerCore.ShapeOf(selected);
        EditorGUI.BeginChangeCheck();
        if (shape == WallShape.Rect)
        {
            Rect rect = WallPlacerCore.GetRect(selected, current);
            Vector2 centre = EditorGUILayout.Vector2Field("가운데", rect.center);
            Vector2 size = EditorGUILayout.Vector2Field("크기", rect.size);
            if (EditorGUI.EndChangeCheck())
            {
                RecordAll(selected, "Resize");
                WallPlacerCore.SetRect(selected, current, new Rect(centre - size * 0.5f, size));
                WallPlacerCore.Bind(selected, current);
            }
        }
        else if (shape == WallShape.Line)
        {
            WallPlacerCore.GetLine(selected, current, out Vector2 a, out Vector2 b);
            a = EditorGUILayout.Vector2Field("시작", a);
            b = EditorGUILayout.Vector2Field("끝", b);
            if (EditorGUI.EndChangeCheck())
            {
                RecordAll(selected, "Change line");
                WallPlacerCore.SetLine(selected, current, a, b);
            }
        }
        else
        {
            Vector2 at = EditorGUILayout.Vector2Field("위치", current.FromWorld(selected.transform.position));
            if (EditorGUI.EndChangeCheck())
            {
                RecordAll(selected, "Move");
                selected.transform.position = current.ToWorld(at, current.DepthOf(selected.transform.position));
                WallPlacerCore.Bind(selected, current);
            }
        }

        foreach (MonoBehaviour script in selected.GetComponents<MonoBehaviour>())
        {
            if (script == null) continue;
            Editor.CreateCachedEditor(script, null, ref inspector);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("설정", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            inspector.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
            break;
        }
        EditorGUILayout.Space(4f);
        DrawMoveToWall(selected, current);
        if (GUILayout.Button("지우기")) Undo.DestroyObjectImmediate(selected);
    }

    private void DrawMoveToWall(GameObject selected, WallView current)
    {
        List<DuduSurface> walls = WallPlacerCore.FindWalls();
        walls.Remove(wall);
        string[] names = new string[walls.Count];
        for (int i = 0; i < walls.Count; i++) names[i] = walls[i].name;
        int chosen = EditorGUILayout.Popup("다른 벽으로 옮기기", -1, names);
        if (chosen < 0) return;
        DuduSurface target = walls[chosen];
        // Selection and the Scene view follow it to the new wall.
        WallPlacerCore.MoveToWall(selected, current, new WallView(target));
        placed = null;
        EnterWall(target);
    }

    private static void RecordAll(GameObject target, string name)
    {
        Undo.RecordObject(target.transform, name);
    }

    private GameObject SelectedPlaced()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null || WallPlacerCore.IsCodeBuilt(selected)) return null;
        CrayonRouteLine line = selected.GetComponentInParent<CrayonRouteLine>();
        if (line != null) return line.gameObject;
        return Placed.Contains(selected) ? selected : null;
    }

    // ---- Scene view ----

    private void OnSceneGUI(SceneView scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene != SceneView.lastActiveSceneView) return;
        if (wall == null)
        {
            PickWall(scene);
            return;
        }

        WallView current = View;
        Event e = Event.current;
        if (e.type == EventType.Layout) KeepFlat(scene, current);
        Tools.hidden = true;

        int id = GUIUtility.GetControlID(ControlHash, FocusType.Passive);
        if (e.type == EventType.Layout && !e.alt) HandleUtility.AddDefaultControl(id);
        bool onWall = current.TryFromRay(HandleUtility.GUIPointToWorldRay(e.mousePosition), out Vector2 mouse, out _);
        Vector2 snapped = Snap(mouse, current);

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && !e.alt && onWall && HandleUtility.nearestControl == id)
                {
                    GUIUtility.hotControl = id;
                    if (tool >= 0) BeginDraw(snapped);
                    else BeginSelect(mouse, e.mousePosition, current);
                    e.Use();
                }
                else if (e.button == 1 && drag != Drag.None)
                {
                    CancelDrag();
                    e.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id && drag != Drag.None)
                {
                    UpdateDrag(mouse, snapped, current);
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                    FinishDrag(snapped, current);
                    e.Use();
                }
                break;
            case EventType.KeyDown:
                if (e.keyCode == KeyCode.Escape)
                {
                    if (drag != Drag.None) CancelDrag();
                    else tool = SelectTool;
                    Repaint();
                    e.Use();
                }
                break;
            case EventType.MouseMove:
                scene.Repaint();
                break;
            case EventType.Repaint:
                DrawGrid(scene, current);
                DrawPlaced(current, e.mousePosition);
                if (onWall) DrawCursor(current, snapped);
                break;
        }
        DrawHint(current, snapped, onWall);
    }

    // Looks straight at the wall, with whatever stands in front of it or behind it clipped away.
    private static void KeepFlat(SceneView scene, WallView current)
    {
        scene.orthographic = true;
        scene.isRotationLocked = true;
        Transform camera = scene.camera.transform;
        float distance = Vector3.Dot(current.ToWorld(Vector2.zero, current.FaceOffset) - camera.position, camera.forward);
        float near = Mathf.Max(0.01f, distance - 1.5f);
        CameraSettings(scene, false, near, Mathf.Max(near + 0.5f, distance + 0.8f));
    }

    private void PickWall(SceneView scene)
    {
        Event e = Event.current;
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        DuduSurface best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (DuduSurface candidate in WallPlacerCore.FindWalls())
        {
            WallView look = new WallView(candidate);
            if (Vector3.Dot(ray.direction, look.Normal) >= 0f || !look.TryFromRay(ray, out Vector2 point, out float distance) ||
                distance >= bestDistance || !look.WallRect.Contains(point)) continue;
            best = candidate;
            bestDistance = distance;
        }
        if (best != hoverWall)
        {
            hoverWall = best;
            scene.Repaint();
        }
        if (best == null) return;

        int id = GUIUtility.GetControlID(ControlHash, FocusType.Passive);
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
        if (e.GetTypeForControl(id) == EventType.MouseDown && e.button == 0 && !e.alt && HandleUtility.nearestControl == id)
        {
            e.Use();
            EnterWall(best);
            return;
        }
        if (e.type == EventType.MouseMove) scene.Repaint();
        if (e.type == EventType.Repaint)
        {
            WallView look = new WallView(best);
            DrawRect(look, look.WallRect, look.FaceOffset, new Color(1f, 0.55f, 0.1f, 0.12f), SelectedColor);
            Handles.Label(look.ToWorld(look.WallRect.center, look.FaceOffset), best.name + "\n클릭해서 편집", EditorStyles.whiteLargeLabel);
        }
    }

    // Snaps to a grid that has Dudu's usual ground height on one of its lines.
    private Vector2 Snap(Vector2 point, WallView current)
    {
        float grid = Grid;
        if (grid <= 0f) return point;
        float ground = current.GroundY;
        return new Vector2(Mathf.Round(point.x / grid) * grid, ground + Mathf.Round((point.y - ground) / grid) * grid);
    }

    // ---- Dragging ----

    private void BeginDraw(Vector2 snapped)
    {
        drag = Drag.Draw;
        dragTarget = null;
        dragStart = dragCurrent = snapped;
    }

    private void BeginSelect(Vector2 mouse, Vector2 guiPoint, WallView current)
    {
        // The selected object's handles come first, then anything under the cursor.
        GameObject selected = SelectedPlaced();
        if (selected != null && current.Holds(selected.transform) && TryGrabHandle(selected, guiPoint, current)) return;

        GameObject hit = ObjectAt(mouse, current);
        Selection.activeGameObject = hit;
        if (hit == null) return;
        drag = Drag.Move;
        dragTarget = hit;
        dragStart = dragCurrent = mouse;
        dragStartPosition = hit.transform.position;
        dragStartRect = WallPlacerCore.GetRect(hit, current);
        dragAnchor = MoveAnchor(hit, current);
        if (WallPlacerCore.ShapeOf(hit) == WallShape.Line) WallPlacerCore.GetLine(hit, current, out lineStartA, out lineStartB);
        Undo.RecordObject(hit.transform, "Move");
    }

    // The point of an object that lands on the grid while it is dragged.
    private static Vector2 MoveAnchor(GameObject target, WallView current)
    {
        switch (WallPlacerCore.ShapeOf(target))
        {
            case WallShape.Rect:
                return WallPlacerCore.GetRect(target, current).position;
            case WallShape.Line:
                WallPlacerCore.GetLine(target, current, out Vector2 a, out _);
                return a;
            case WallShape.Path:
                return current.FromWorld(target.transform.position);
            default:
                return WallPlacerCore.GetRect(target, current).center;
        }
    }

    private bool TryGrabHandle(GameObject selected, Vector2 guiPoint, WallView current)
    {
        WallShape shape = WallPlacerCore.ShapeOf(selected);
        float depth = current.FaceOffset;
        if (shape == WallShape.Rect)
        {
            Rect rect = WallPlacerCore.GetRect(selected, current);
            Vector2[] corners = Corners(rect);
            for (int i = 0; i < corners.Length; i++)
            {
                if (!NearHandle(current.ToWorld(corners[i], depth), guiPoint)) continue;
                drag = Drag.Corner;
                dragTarget = selected;
                dragIndex = i;
                dragStartRect = rect;
                Undo.RecordObject(selected.transform, "Resize");
                return true;
            }
        }
        else if (shape == WallShape.Line)
        {
            WallPlacerCore.GetLine(selected, current, out Vector2 a, out Vector2 b);
            bool first = NearHandle(current.ToWorld(a, depth), guiPoint);
            if (!first && !NearHandle(current.ToWorld(b, depth), guiPoint)) return false;
            drag = Drag.LineEnd;
            dragTarget = selected;
            lineOther = first ? b : a;
            lineStartA = a;
            lineStartB = b;
            Undo.RecordObject(selected.transform, "Change line");
            return true;
        }
        else if (shape == WallShape.Path)
        {
            WallPlacerCore.GetPath(selected, current, out Vector2 start, out Vector2 end);
            if (!NearHandle(current.ToWorld(end, depth), guiPoint)) return false;
            drag = Drag.PathEnd;
            dragTarget = selected;
            dragStart = start;
            dragCurrent = end;
            return true;
        }
        return false;
    }

    private static bool NearHandle(Vector3 world, Vector2 guiPoint) =>
        Vector2.Distance(HandleUtility.WorldToGUIPoint(world), guiPoint) <= HandlePixels + 3f;

    private void UpdateDrag(Vector2 mouse, Vector2 snapped, WallView current)
    {
        switch (drag)
        {
            case Drag.Draw:
            case Drag.PathEnd:
                dragCurrent = snapped;
                break;
            case Drag.Move:
                WallPlacerCore.MoveBy(dragTarget, current, dragStartPosition,
                    Snap(dragAnchor + mouse - dragStart, current) - dragAnchor);
                if (WallPlacerCore.ShapeOf(dragTarget) == WallShape.Line)
                    dragTarget.GetComponent<CrayonRouteLine>().Rebuild();
                break;
            case Drag.Corner:
                Vector2 opposite = Corners(dragStartRect)[(dragIndex + 2) % 4];
                WallPlacerCore.SetRect(dragTarget, current, Rect.MinMaxRect(Mathf.Min(opposite.x, snapped.x), Mathf.Min(opposite.y, snapped.y),
                    Mathf.Max(opposite.x, snapped.x), Mathf.Max(opposite.y, snapped.y)));
                WallPlacerCore.Bind(dragTarget, current);
                break;
            case Drag.LineEnd:
                WallPlacerCore.SetLine(dragTarget, current, lineOther, snapped);
                break;
        }
    }

    private void FinishDrag(Vector2 snapped, WallView current)
    {
        if (drag == Drag.Draw && tool >= 0)
        {
            Selection.activeGameObject = WallPlacerCore.Place(WallPlacerCore.Items[tool], current, dragStart, snapped);
            placed = null;
        }
        else if (drag == Drag.PathEnd && dragTarget != null)
        {
            Undo.RecordObject(dragTarget.transform, "Change path");
            WallPlacerCore.SetPath(dragTarget, current, dragStart, snapped);
        }
        drag = Drag.None;
        dragTarget = null;
        Repaint();
    }

    private void CancelDrag()
    {
        if (GUIUtility.hotControl != 0) GUIUtility.hotControl = 0;
        // Puts the object back by hand: an Undo here could revert an earlier step
        // when the drag has not changed anything yet.
        WallView current = View;
        if (dragTarget != null && current != null)
        {
            if (drag == Drag.Move || drag == Drag.Corner)
            {
                if (drag == Drag.Corner) WallPlacerCore.SetRect(dragTarget, current, dragStartRect);
                else WallPlacerCore.MoveBy(dragTarget, current, dragStartPosition, Vector2.zero);
                WallPlacerCore.Bind(dragTarget, current);
            }
            if (WallPlacerCore.ShapeOf(dragTarget) == WallShape.Line) WallPlacerCore.SetLine(dragTarget, current, lineStartA, lineStartB);
        }
        drag = Drag.None;
        dragTarget = null;
    }

    private GameObject ObjectAt(Vector2 point, WallView current)
    {
        GameObject best = null;
        float bestArea = float.PositiveInfinity;
        foreach (GameObject candidate in Placed)
        {
            if (candidate == null || WallPlacerCore.IsCodeBuilt(candidate) || !current.Holds(candidate.transform)) continue;
            float area;
            if (WallPlacerCore.ShapeOf(candidate) == WallShape.Line)
            {
                WallPlacerCore.GetLine(candidate, current, out Vector2 a, out Vector2 b);
                if (DistanceToSegment(point, a, b) > 0.2f) continue;
                area = 0f;
            }
            else
            {
                Rect rect = WallPlacerCore.GetRect(candidate, current);
                if (!rect.Contains(point)) continue;
                area = rect.width * rect.height;
            }
            // The smallest thing under the cursor, so a block inside a wind zone can be picked.
            if (area >= bestArea) continue;
            best = candidate;
            bestArea = area;
        }
        return best;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 along = b - a;
        float t = along.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, along) / along.sqrMagnitude) : 0f;
        return Vector2.Distance(point, a + along * t);
    }

    private static Vector2[] Corners(Rect rect) => new[]
    {
        new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin),
        new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax)
    };

    // ---- Drawing ----

    private void DrawGrid(SceneView scene, WallView current)
    {
        float depth = current.FaceOffset + 0.01f;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        DrawRect(current, current.WalkRect, depth, new Color(1f, 1f, 1f, 0.02f), new Color(1f, 1f, 1f, 0.8f));

        float grid = Grid;
        if (grid > 0f)
        {
            // Skip lines that would blur together when zoomed out.
            float pixels = Vector2.Distance(HandleUtility.WorldToGUIPoint(current.ToWorld(Vector2.zero, depth)),
                HandleUtility.WorldToGUIPoint(current.ToWorld(Vector2.right * grid, depth)));
            float step = grid;
            while (pixels < 10f)
            {
                step *= 2f;
                pixels *= 2f;
            }
            Rect area = current.WalkRect;
            Handles.color = new Color(0f, 0f, 0f, 0.14f);
            for (float x = Mathf.Ceil(area.xMin / step) * step; x <= area.xMax; x += step)
                Handles.DrawLine(current.ToWorld(new Vector2(x, area.yMin), depth), current.ToWorld(new Vector2(x, area.yMax), depth));
            float ground = current.GroundY;
            for (float y = ground + Mathf.Ceil((area.yMin - ground) / step) * step; y <= area.yMax; y += step)
                Handles.DrawLine(current.ToWorld(new Vector2(area.xMin, y), depth), current.ToWorld(new Vector2(area.xMax, y), depth));
        }
        Handles.color = new Color(1f, 0.45f, 0.1f, 0.7f);
        Handles.DrawDottedLine(current.ToWorld(new Vector2(current.WalkRect.xMin, current.GroundY), depth),
            current.ToWorld(new Vector2(current.WalkRect.xMax, current.GroundY), depth), 4f);
    }

    private void DrawPlaced(WallView current, Vector2 guiPoint)
    {
        GameObject selected = SelectedPlaced();
        foreach (GameObject item in Placed)
        {
            if (item == null || !current.Holds(item.transform)) continue;
            bool isSelected = item == selected;
            Color color = isSelected ? SelectedColor : WallPlacerCore.IsCodeBuilt(item) ? CodeBuiltColor : ItemColor;
            WallShape shape = WallPlacerCore.ShapeOf(item);
            float depth = current.FaceOffset + 0.02f;
            if (shape == WallShape.Line)
            {
                WallPlacerCore.GetLine(item, current, out Vector2 a, out Vector2 b);
                Handles.color = color;
                Handles.DrawLine(current.ToWorld(a, depth), current.ToWorld(b, depth), isSelected ? 3f : 1f);
                if (isSelected)
                {
                    DrawHandle(current.ToWorld(a, depth), guiPoint);
                    DrawHandle(current.ToWorld(b, depth), guiPoint);
                }
                continue;
            }
            Rect rect = WallPlacerCore.GetRect(item, current);
            DrawRect(current, rect, depth, new Color(color.r, color.g, color.b, isSelected ? 0.12f : 0.05f), color);
            if (item.GetComponent<DuduCheckpoint>() != null)
                Handles.Label(current.ToWorld(rect.center, depth), item.name, EditorStyles.miniBoldLabel);
            if (shape == WallShape.Rect && isSelected)
                foreach (Vector2 corner in Corners(rect))
                    DrawHandle(current.ToWorld(corner, depth), guiPoint);
            if (shape == WallShape.Path)
            {
                WallPlacerCore.GetPath(item, current, out Vector2 start, out Vector2 end);
                if (drag == Drag.PathEnd && dragTarget == item) end = dragCurrent;
                Vector2 toEnd = end - start;
                DrawRect(current, new Rect(rect.position + toEnd, rect.size), depth,
                    new Color(color.r, color.g, color.b, 0.03f), new Color(color.r, color.g, color.b, 0.5f));
                Handles.color = color;
                Handles.DrawDottedLine(current.ToWorld(start, depth), current.ToWorld(end, depth), 3f);
                if (isSelected) DrawHandle(current.ToWorld(end, depth), guiPoint);
            }
        }
    }

    private static void DrawHandle(Vector3 world, Vector2 guiPoint)
    {
        float size = HandleUtility.GetHandleSize(world) * 0.07f;
        Handles.color = Vector2.Distance(HandleUtility.WorldToGUIPoint(world), guiPoint) <= HandlePixels + 3f ? Color.white : SelectedColor;
        Handles.DotHandleCap(0, world, Quaternion.identity, size, EventType.Repaint);
    }

    private void DrawCursor(WallView current, Vector2 snapped)
    {
        float depth = current.FaceOffset + 0.03f;
        if (tool < 0 || tool >= WallPlacerCore.Items.Length) return;
        WallItem item = WallPlacerCore.Items[tool];
        Color fill = new Color(1f, 0.55f, 0.1f, 0.18f);
        if (drag == Drag.Draw && item.Shape == WallShape.Rect)
        {
            DrawRect(current, Rect.MinMaxRect(Mathf.Min(dragStart.x, dragCurrent.x), Mathf.Min(dragStart.y, dragCurrent.y),
                Mathf.Max(dragStart.x, dragCurrent.x), Mathf.Max(dragStart.y, dragCurrent.y)), depth, fill, SelectedColor);
            return;
        }
        if (drag == Drag.Draw && item.Shape != WallShape.Point)
        {
            Vector2 end = dragCurrent;
            if (item.Shape == WallShape.Path)
                end = Mathf.Abs(end.x - dragStart.x) >= Mathf.Abs(end.y - dragStart.y)
                    ? new Vector2(end.x, dragStart.y) : new Vector2(dragStart.x, end.y);
            Handles.color = SelectedColor;
            Handles.DrawLine(current.ToWorld(dragStart, depth), current.ToWorld(end, depth), 3f);
            return;
        }
        Handles.color = SelectedColor;
        float cross = HandleUtility.GetHandleSize(current.ToWorld(snapped, depth)) * 0.12f;
        Handles.DrawLine(current.ToWorld(snapped + Vector2.left * cross, depth), current.ToWorld(snapped + Vector2.right * cross, depth));
        Handles.DrawLine(current.ToWorld(snapped + Vector2.down * cross, depth), current.ToWorld(snapped + Vector2.up * cross, depth));
    }

    private static void DrawRect(WallView current, Rect rect, float depth, Color fill, Color outline)
    {
        Handles.DrawSolidRectangleWithOutline(new[]
        {
            current.ToWorld(new Vector2(rect.xMin, rect.yMin), depth), current.ToWorld(new Vector2(rect.xMax, rect.yMin), depth),
            current.ToWorld(new Vector2(rect.xMax, rect.yMax), depth), current.ToWorld(new Vector2(rect.xMin, rect.yMax), depth)
        }, fill, outline);
    }

    private void DrawHint(WallView current, Vector2 snapped, bool onWall)
    {
        Handles.BeginGUI();
        string action = tool >= 0 && tool < WallPlacerCore.Items.Length
            ? WallPlacerCore.Items[tool].Label + " 놓기 · " + WallPlacerCore.Items[tool].Tip + " · 우클릭/Esc 취소"
            : "클릭: 선택 · 드래그: 이동 · 모서리 점: 크기 · Delete: 지우기 · Ctrl+D: 복제";
        string where = onWall ? $"x {snapped.x:0.##}, y {snapped.y:0.##}" : "";
        string warning = current.Flipped ? "\n이 벽은 방 바깥쪽을 향해 있어서 방 쪽 면에 놓여요. 자석·먹구름은 놓을 수 없어요." : "";
        GUIStyle style = new GUIStyle(EditorStyles.label) { fontSize = 12, wordWrap = true };
        style.normal.textColor = Color.white;
        string text = $"{current.Surface.name}   {where}\n{action}{warning}";
        // Right of the Scene view's tool bar, left of its orientation gizmo.
        float width = Mathf.Clamp(Screen.width / EditorGUIUtility.pixelsPerPoint - 210f, 200f, 640f);
        Rect area = new Rect(70f, 8f, width, style.CalcHeight(new GUIContent(text), width - 12f) + 8f);
        EditorGUI.DrawRect(area, new Color(0f, 0f, 0f, 0.7f));
        GUI.Label(new Rect(area.x + 6f, area.y + 4f, area.width - 12f, area.height - 8f), text, style);
        Handles.EndGUI();
    }
}
#endif
