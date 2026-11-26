#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Lays out the stage after the tutorial (Player3DScene) from the two-floor blueprint sketch.
// Every position below is a pixel of the 1F sketch (the 2F sketch is lined up on it through
// the shared staircase, 100 px lower), turned into metres with Scale. Rebuilding replaces the
// whole "Stage 1 Level" object; anything placed with the wall placer ("Placed Level") is kept.
public static class Stage1MapBuilder
{
    public const string ScenePath = "Assets/Scenes/Player3DScene.unity";
    private const string RootName = "Stage 1 Level";

    private const float Scale = 0.05f;          // metres per sketch pixel
    private const float OriginX = 132f, OriginY = 530f;
    private const float WallHeight = 6f;
    private const float WallThickness = 0.12f;
    private const float FloorTwo = 6.2f;         // 2F floor surface
    private const float SlabBottom = 5.88f;      // 1F ceiling underside
    private const float PartitionHeight = 3f;
    private const float Cell = 5f;               // floor raster, in sketch pixels
    private const float CrateSize = 1.3f;

    private const string PaperPath = "Assets/Materials/PaperBackground.mat";
    private const string InkPath = "Assets/Materials/DuduGroundLine.mat";
    private const string WoodFloorPath = "Assets/Stylized Wood Textures/Materials/Chevron/Chevron.mat";
    private const string PlasterPath = "Assets/Materials/CeilingPlaster.mat";
    private const string LampPath = "Assets/Materials/CeilingLamp.mat";
    private const string BoxPrefab = "Assets/VNB - Home Set/Prefabs/Other/Box.prefab";
    private const string WallPrefab = "Assets/VNB - Home Set/Prefabs/Building/Wall Standard.prefab";
    private const string StepsPrefab = "Assets/VNB - Home Set/Prefabs/Building/Stairs/Stairs  Steps.prefab";
    private const string GeneratedFolder = "Assets/Art/Stage1";

    // ---- The blueprint ----------------------------------------------------------------------
    // Walls go from a to b with the room on the right-hand side (clockwise seen from above), the
    // order Dudu walks them in, as the blue arrows show.

    private static readonly Vector2[][] FirstFloorRoute =
    {
        new[] { P(132, 448), P(132, 60) },   // Dudu starts here
        new[] { P(132, 60), P(295, 60) },
        new[] { P(295, 60), P(295, 200) },   // around the empty space outside
        new[] { P(295, 200), P(365, 200) },
        new[] { P(365, 200), P(365, 60) },
        new[] { P(365, 60), P(558, 60) },
        new[] { P(558, 60), P(558, 195) },
        new[] { P(558, 195), P(445, 195) },  // around the pillar
        new[] { P(445, 195), P(445, 315) },
        new[] { P(445, 315), P(558, 315) },
        new[] { P(558, 315), P(558, 60) },
        new[] { P(558, 60), P(745, 60) },
        new[] { P(745, 60), P(745, 530) },
        new[] { P(745, 530), P(370, 530) },
    };

    // Beside the staircase one wall runs from 1F's floor to 2F's ceiling, so Dudu can climb
    // along the stairs from the last 1F wall to the first 2F wall.
    private static readonly Vector2[] StairWall = { P(370, 530), P(45, 530) };

    private static readonly Vector2[][] FirstFloorPlain =
    {
        new[] { P(300, 448), P(132, 448) },  // closes off the space under the stairs
        new[] { P(132, 530), P(132, 448) },
    };

    // 2F sketch y + 100.
    private static readonly Vector2[][] SecondFloorRoute =
    {
        new[] { P(45, 530), P(45, 362) },
        new[] { P(45, 362), P(245, 362) },
        new[] { P(245, 362), P(245, 255) },
        new[] { P(245, 255), P(120, 255) },
        new[] { P(120, 255), P(120, 130) },
        new[] { P(120, 130), P(375, 130) },
        new[] { P(375, 130), P(375, 240) },
        new[] { P(375, 240), P(540, 240) },
        new[] { P(540, 240), P(540, 125) },
        new[] { P(540, 125), P(790, 125) },
        new[] { P(790, 125), P(790, 235) },
        new[] { P(790, 235), P(905, 235) },
        new[] { P(905, 235), P(905, 400) },
        new[] { P(905, 400), P(800, 400) },
        new[] { P(800, 400), P(800, 528) },
        new[] { P(800, 528), P(660, 528) },
        new[] { P(660, 528), P(660, 610) },
        new[] { P(660, 610), P(415, 610) },  // the clear door
    };

    private static readonly Vector2[][] SecondFloorPlain =
    {
        new[] { P(415, 610), P(415, 530) },
        new[] { P(415, 530), P(370, 530) },
    };

    private const float DoorPixelX = 478f;   // on the last 2F wall

    // Purple lines: brick partitions in Haru's way.
    private static readonly Vector2[][] FirstFloorPartitions =
    {
        new[] { P(558, 315), P(620, 315) },
        new[] { P(670, 315), P(745, 315) },
        new[] { P(518, 315), P(518, 390) },
        new[] { P(520, 445), P(520, 530) },
    };

    private static readonly Vector2[][] SecondFloorPartitions =
    {
        new[] { P(680, 233), P(790, 233) },
        new[] { P(662, 405), P(662, 528) },
    };

    // Red hatching on 2F: holes down to 1F.
    private static readonly Rect[] Holes =
    {
        R(245, 295, 305, 355),
        R(315, 240, 380, 295),
        // Sketched out to x 800, but 1F ends at 745: past it the hole would drop into nothing.
        R(665, 350, 745, 405),
    };

    // The staircase climbs west, from its foot on 1F to 2F.
    private const float StairFootX = 370f, StairTopX = 160f, StairNorthY = 448f, StairSouthY = 530f;
    private const int StairUnits = 6;

    private static readonly Vector2 HaruStart = P(210, 410);
    private static readonly Vector2 FirstFloorCrate = P(170, 222);
    private static readonly Vector2 SecondFloorCrate = P(538, 278);

    private static readonly Vector2[] FirstFloorLamps =
        { P(215, 130), P(215, 320), P(400, 330), P(460, 125), P(650, 160), P(650, 420), P(430, 485) };
    private static readonly Vector2[] SecondFloorLamps =
        { P(180, 190), P(110, 420), P(450, 320), P(620, 300), P(660, 180), P(540, 470), P(850, 320), P(730, 470) };

    private static Vector2 P(float x, float y) => new Vector2(x, y);
    private static Rect R(float x0, float y0, float x1, float y1) => Rect.MinMaxRect(x0, y0, x1, y1);
    private static Vector3 World(Vector2 pixel, float height) =>
        new Vector3((pixel.x - OriginX) * Scale, height, (OriginY - pixel.y) * Scale);

    // ---- Build ------------------------------------------------------------------------------

    [MenuItem("Tools/Draw And Go/1스테이지 맵 다시 만들기 (설계도)")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        RemoveOldLevel(scene);

        Transform strokeRoot = FindNamed(scene, "DrawingRoot")?.transform;
        DuduSurfaceMovement dudu = FindInScene<DuduSurfaceMovement>(scene);
        Player3DMovement haru = FindInScene<Player3DMovement>(scene);
        DuduCameraController duduCamera = FindInScene<DuduCameraController>(scene);
        if (strokeRoot == null || dudu == null || haru == null || duduCamera == null)
            throw new System.InvalidOperationException("Player3DScene is missing DrawingRoot, Dudu, Haru or the Dudu camera.");

        Materials art = Materials.Load();
        GameObject root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        Transform first = Child(root.transform, "1F");
        Transform second = Child(root.transform, "2F");

        List<DuduSurface> route = new List<DuduSurface>();
        route.AddRange(BuildWalls(FirstFloorRoute, 0f, "1F Wall", Child(first, "Walls"), strokeRoot, art, true));
        DuduSurface stairWall = BuildWalls(new[] { StairWall }, 0f, "Stair Wall 1F-2F", Child(root.transform, "Stairs 1F-2F"),
            strokeRoot, art, true, FloorTwo + WallHeight)[0];
        route.Add(stairWall);
        route.AddRange(BuildWalls(SecondFloorRoute, FloorTwo, "2F Wall", Child(second, "Walls"), strokeRoot, art, true));
        BuildWalls(FirstFloorPlain, 0f, "1F Stair Wall", Child(first, "Walls"), strokeRoot, art, false);
        BuildWalls(SecondFloorPlain, FloorTwo, "2F Stair Wall", Child(second, "Walls"), strokeRoot, art, false);
        for (int i = 0; i < route.Count; i++)
        {
            route[i].previousSurface = i > 0 ? route[i - 1] : null;
            route[i].nextSurface = i < route.Count - 1 ? route[i + 1] : null;
        }
        Transform lines = Child(root.transform, "Dudu Route Lines");
        DuduSurface lastFirstFloor = route[FirstFloorRoute.Length - 1];
        foreach (DuduSurface surface in route)
            if (surface != stairWall && surface != lastFirstFloor) CreateGround(surface.name + " Line", surface, lines, art.Ink);
        // The last 1F wall's floor line stops short of the stairs: the climb starts on it.
        float climbStart = lastFirstFloor.Width * 0.5f - ClimbLead;
        CreateGroundSpan(lastFirstFloor.name + " Line", lastFirstFloor, -lastFirstFloor.Width * 0.5f, climbStart, lines, art.Ink);
        // Up along the stairs, then level on 2F to the corner.
        const float LineHeight = 0.6f;
        Vector3 top = World(P(StairTopX, StairSouthY), FloorTwo + LineHeight);
        Vector3 end = World(StairWall[1], FloorTwo + LineHeight);
        Vector3 climbFrom = World(P(StairFootX + ClimbLead / Scale, StairSouthY), LineHeight);
        StairClimbLine("Stair Wall 1F-2F Line Up", stairWall, climbFrom, top, lines, art.Ink);
        CreateLine("Stair Wall 1F-2F Line 2F", stairWall, top, end, lines, art.Ink);

        BuildPartitions(FirstFloorPartitions, 0f, Child(first, "Partitions"), art);
        BuildPartitions(SecondFloorPartitions, FloorTwo, Child(second, "Partitions"), art);
        BuildFloors(first, second, art);
        BuildStairs(root.transform, art);
        BuildHoleCatcher(root.transform);
        BuildLamps(Child(first, "Lamps"), FirstFloorLamps, SlabBottom, art);
        BuildLamps(Child(second, "Lamps"), SecondFloorLamps, FloorTwo + WallHeight, art);
        Transform props = Child(root.transform, "Props");
        CreateCrate("Pushable Crate 1F", World(FirstFloorCrate, 0f), props, art);
        CreateCrate("Pushable Crate 2F", World(SecondFloorCrate, FloorTwo), props, art);

        DuduSurface doorWall = route[route.Count - 1];
        float doorX = doorWall.WorldToSurface(World(new Vector2(DoorPixelX, 610f), 0f)).x;
        GameObject door = BuildDoor(root.transform, doorWall, strokeRoot, art, doorX);

        CornerRoomLevel level = root.AddComponent<CornerRoomLevel>();
        level.surfaces = route.ToArray();
        level.coins = new GameObject[0];
        level.requiredCoins = 0;
        level.door = door;
        level.player = dudu;
        level.player3D = haru;
        level.goal3D = BuildGoal(root.transform, doorWall, doorX, level);
        level.introPath = BuildIntroPath(root.transform);

        PlacePlayers(haru, dudu, duduCamera, route[0], scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"Stage 1 rebuilt in {ScenePath}: {route.Count} Dudu walls on two floors, stairs, 3 holes, 2 crates.");
    }

    private static void RemoveOldLevel(Scene scene)
    {
        string[] temporaryRoots =
        {
            RootName, "Corner Room Level", "Floors", "Vertical Obstacle", "Homing Enemy", "Slow Stain Obstacle",
            "Reverse Stain Obstacle", "Line Magnet - Section 5", "Hot Zone - Section 3", "No Draw Zone - Section 2",
            "Wind Zone - Section 4"
        };
        foreach (GameObject root in scene.GetRootGameObjects())
            if (temporaryRoots.Contains(root.name)) Object.DestroyImmediate(root);
    }

    // ---- Walls --------------------------------------------------------------------------------

    private static List<DuduSurface> BuildWalls(Vector2[][] segments, float baseY, string prefix,
        Transform parent, Transform strokeRoot, Materials art, bool route, float height = WallHeight)
    {
        List<DuduSurface> result = new List<DuduSurface>();
        int drawingLayer = LayerMask.NameToLayer("DrawingSurface");
        for (int i = 0; i < segments.Length; i++)
        {
            Vector3 a = World(segments[i][0], baseY), b = World(segments[i][1], baseY);
            Vector3 right = (b - a).normalized;
            Vector3 normal = Vector3.Cross(Vector3.up, right);
            float length = Vector3.Distance(a, b);
            // The face lies on the sketched line and the wall's body behind it, so at every corner
            // the two faces meet exactly and no end of a wall hides inside the next one. A wall drawn
            // back to back with another on one line (the pillar's) is a thin sheet on its own side.
            bool backToBack = IsBackToBack(segments[i][0], segments[i][1]);
            float thickness = backToBack ? TwinThickness : WallThickness;
            Vector3 center = (a + b) * 0.5f + Vector3.up * (height * 0.5f) +
                normal * (backToBack ? thickness * 0.5f : -thickness * 0.5f);
            string name = segments.Length == 1 ? prefix : $"{prefix} {i + 1}";
            GameObject wall = Cube(name, center, Quaternion.LookRotation(-normal, Vector3.up),
                new Vector3(length, height, thickness), art.Paper, parent);
            wall.layer = drawingLayer;
            wall.AddComponent<DrawingSurface>().SetStrokeRoot(strokeRoot);
            if (!route) continue;
            DuduSurface surface = wall.AddComponent<DuduSurface>();
            SerializedObject data = new SerializedObject(surface);
            data.FindProperty("width").floatValue = length;
            data.FindProperty("height").floatValue = height;
            data.ApplyModifiedPropertiesWithoutUndo();
            result.Add(surface);
        }
        return result;
    }

    // Dudu's feet are a box and he steps up only a few centimetres, so a line that turns
    // sharply upward is a wall to him, and above about 30 degrees he barely crawls. The climb
    // starts ClimbLead metres before the stairs, bends up a little at a time (10, 20, 27
    // degrees), then runs straight to 2F at about 26 degrees; each piece tucks under its
    // neighbours so the joins are smooth.
    private const float ClimbLead = 3f;


    public static void StairClimbLine(string name, DuduSurface wall, Vector3 floorEnd, Vector3 top, Transform parent, Material ink)
    {
        float[] easeIn = { 10f, 20f, 27f };
        const float EaseRun = 1f, Tuck = 0.15f;
        Vector2 start = wall.WorldToSurface(floorEnd), end = wall.WorldToSurface(top);
        float forward = Mathf.Sign(end.x - start.x);
        List<Vector2> points = new List<Vector2> { start };
        foreach (float angle in easeIn)
            points.Add(points[points.Count - 1] + new Vector2(forward * EaseRun, EaseRun * Mathf.Tan(angle * Mathf.Deg2Rad)));
        points.Add(end);
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector2 a = points[i], b = points[i + 1];
            Vector2 along = (b - a).normalized;
            // Back under the piece before (twice as far under the flat floor line), and forward
            // under the next one; the last piece stops where the 2F line starts.
            a -= along * (i == 0 ? Tuck * 2f : Tuck);
            if (i + 2 < points.Count) b += along * Tuck;
            CreateLine($"{name} {i + 1}", wall, wall.SurfaceToWorld(a), wall.SurfaceToWorld(b), parent, ink);
        }
    }

    // A crayon line on a wall between two world points (a slope when their heights differ).
    private static void CreateLine(string name, DuduSurface surface, Vector3 from, Vector3 to, Transform parent, Material ink)
    {
        Vector2 a = surface.WorldToSurface(from), b = surface.WorldToSurface(to);
        Vector2 along = b - a;
        Quaternion rotation = surface.transform.rotation * Quaternion.Euler(0f, 0f, Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg);
        GameObject line = Cube(name, surface.SurfaceToWorld((a + b) * 0.5f), rotation,
            new Vector3(along.magnitude, 0.14f, 0.18f), ink, parent);
        CrayonRouteLineTool.Convert(line);
    }

    private static void CreateGroundSpan(string name, DuduSurface surface, float fromX, float toX, Transform parent, Material ink)
    {
        GameObject line = Cube(name, surface.SurfaceToWorld(new Vector2((fromX + toX) * 0.5f, -2.4f)), surface.transform.rotation,
            new Vector3(toX - fromX, 0.14f, 0.18f), ink, parent);
        CrayonRouteLineTool.Convert(line);
    }

    private const float TwinThickness = 0.03f;

    // True when another wall of the blueprint runs along the same line, facing the other way, over
    // part of this one's length.
    private static bool IsBackToBack(Vector2 a, Vector2 b)
    {
        IEnumerable<Vector2[]> all = FirstFloorRoute.Concat(FirstFloorPlain).Concat(SecondFloorRoute).Concat(SecondFloorPlain);
        Vector2 dir = (b - a).normalized;
        foreach (Vector2[] other in all)
        {
            Vector2 od = (other[1] - other[0]).normalized;
            if (Vector2.Dot(dir, od) > -0.99f) continue;
            Vector2 offset = other[0] - a;
            if (Mathf.Abs(dir.x * offset.y - dir.y * offset.x) > 0.5f) continue;   // not on the same line
            float lo = Mathf.Min(Vector2.Dot(other[0] - a, dir), Vector2.Dot(other[1] - a, dir));
            float hi = Mathf.Max(Vector2.Dot(other[0] - a, dir), Vector2.Dot(other[1] - a, dir));
            if (hi > 0.5f && lo < (b - a).magnitude - 0.5f) return true;
        }
        return false;
    }

    private static void CreateGround(string name, DuduSurface surface, Transform parent, Material ink)
    {
        GameObject line = Cube(name, surface.SurfaceToWorld(new Vector2(0f, -2.4f)), surface.transform.rotation,
            new Vector3(surface.Width, 0.14f, 0.18f), ink, parent);
        CrayonRouteLineTool.Convert(line);
    }

    private static void BuildPartitions(Vector2[][] segments, float baseY, Transform parent, Materials art)
    {
        // Partitions are in Haru's way only: Dudu's page neither shows them nor bumps into them.
        if (parent.GetComponent<RoomOnlyDecor>() == null) parent.gameObject.AddComponent<RoomOnlyDecor>();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefab);
        for (int i = 0; i < segments.Length; i++)
        {
            Vector3 a = World(segments[i][0], baseY), b = World(segments[i][1], baseY);
            float length = Vector3.Distance(a, b);
            GameObject wall = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            wall.name = $"Partition {i + 1}";
            wall.transform.SetParent(parent, false);
            // The prefab is 2 m wide, 3 m tall and stands on its pivot.
            wall.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(Vector3.Cross(b - a, Vector3.up), Vector3.up));
            wall.transform.localScale = new Vector3(length / 2f, PartitionHeight / 3f, 1f);
            foreach (Renderer renderer in wall.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(art.Urp).ToArray();
        }
    }

    // ---- Floors -------------------------------------------------------------------------------

    private static void BuildFloors(Transform first, Transform second, Materials art)
    {
        Rect stairwell = R(StairTopX, StairNorthY, StairFootX, StairSouthY);
        Vector2[] firstOutline =
        {
            P(132, 60), P(295, 60), P(295, 200), P(365, 200), P(365, 60), P(745, 60), P(745, 530), P(132, 530)
        };
        Vector2[] secondOutline = SecondFloorRoute.Select(s => s[0]).Concat(SecondFloorPlain.Select(s => s[0])).ToArray();

        bool InFirst(Vector2 p) => Inside(firstOutline, p);
        bool InSecond(Vector2 p) => Inside(secondOutline, p);
        bool Open(Vector2 p) => stairwell.Contains(p) || Holes.Any(h => h.Contains(p));

        BuildSlab("1F Floor", Child(first, "Floor"), InFirst, -0.2f, 0f, art.WoodFloor);
        BuildSlab("1F Ceiling", Child(first, "Ceiling"), p => InFirst(p) && !Open(p), SlabBottom, 6f, art.Plaster);
        BuildSlab("2F Floor", Child(second, "Floor"), p => InSecond(p) && !Open(p), 6f, FloorTwo, art.WoodFloor);
        BuildSlab("2F Ceiling", Child(second, "Ceiling"), InSecond, FloorTwo + WallHeight, FloorTwo + WallHeight + 0.12f, art.Plaster);

        // The holes look like more floor from above (1F's floor shows through), so each gets a red
        // edge, as in the sketch.
        Transform edges = Child(second, "Hole Edges");
        Material edge = art.HoleEdge;
        for (int i = 0; i < Holes.Length; i++)
        {
            Rect hole = Holes[i];
            Vector3 min = World(P(hole.xMin, hole.yMax), FloorTwo), max = World(P(hole.xMax, hole.yMin), FloorTwo);
            const float Band = 0.22f, Lift = 0.02f;
            float midX = (min.x + max.x) * 0.5f, midZ = (min.z + max.z) * 0.5f;
            float spanX = max.x - min.x + Band * 2f, spanZ = max.z - min.z;
            Cube($"Hole {i + 1} Edge South", new Vector3(midX, FloorTwo + Lift * 0.5f, min.z - Band * 0.5f), Quaternion.identity, new Vector3(spanX, Lift, Band), edge, edges);
            Cube($"Hole {i + 1} Edge North", new Vector3(midX, FloorTwo + Lift * 0.5f, max.z + Band * 0.5f), Quaternion.identity, new Vector3(spanX, Lift, Band), edge, edges);
            Cube($"Hole {i + 1} Edge West", new Vector3(min.x - Band * 0.5f, FloorTwo + Lift * 0.5f, midZ), Quaternion.identity, new Vector3(Band, Lift, spanZ), edge, edges);
            Cube($"Hole {i + 1} Edge East", new Vector3(max.x + Band * 0.5f, FloorTwo + Lift * 0.5f, midZ), Quaternion.identity, new Vector3(Band, Lift, spanZ), edge, edges);
        }
        foreach (Collider strip in edges.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(strip);

        // Outside the clear door: a step to stand on if Haru walks through it.
        Vector3 landing = World(P(DoorPixelX, 650), FloorTwo);
        Cube("Door Landing", landing + Vector3.down * 0.1f, Quaternion.identity, new Vector3(4f, 0.2f, 4f), art.WoodFloor, second);
    }

    // Covers every raster cell the test accepts with as few boxes as possible: runs along x,
    // stacked down y while the run stays the same.
    private static void BuildSlab(string name, Transform parent, System.Func<Vector2, bool> solid,
        float bottom, float top, Material material)
    {
        const float MinX = 0f, MaxX = 960f, MinY = 0f, MaxY = 700f;
        int columns = Mathf.CeilToInt((MaxX - MinX) / Cell), rows = Mathf.CeilToInt((MaxY - MinY) / Cell);
        bool[,] cells = new bool[columns, rows];
        for (int x = 0; x < columns; x++)
            for (int y = 0; y < rows; y++)
                cells[x, y] = solid(new Vector2(MinX + (x + 0.5f) * Cell, MinY + (y + 0.5f) * Cell));

        bool[,] used = new bool[columns, rows];
        int count = 0;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                if (!cells[x, y] || used[x, y]) continue;
                int x1 = x;
                while (x1 + 1 < columns && cells[x1 + 1, y] && !used[x1 + 1, y]) x1++;
                int y1 = y;
                while (y1 + 1 < rows && Enumerable.Range(x, x1 - x + 1).All(c => cells[c, y1 + 1] && !used[c, y1 + 1])) y1++;
                for (int c = x; c <= x1; c++)
                    for (int r = y; r <= y1; r++) used[c, r] = true;

                Vector2 min = new Vector2(MinX + x * Cell, MinY + y * Cell);
                Vector2 max = new Vector2(MinX + (x1 + 1) * Cell, MinY + (y1 + 1) * Cell);
                Vector3 a = World(min, bottom), b = World(max, bottom);
                Vector3 center = (a + b) * 0.5f + Vector3.up * ((top - bottom) * 0.5f);
                Vector3 size = new Vector3(Mathf.Abs(b.x - a.x), top - bottom, Mathf.Abs(b.z - a.z));
                Slab($"{name} {++count}", center, size, material, parent);
            }
        }
    }

    // A box whose texture is laid in world space, so neighbouring pieces of one floor read as
    // one floor: same grain size, no seam where they meet.
    private static void Slab(string name, Vector3 center, Vector3 size, Material material, Transform parent)
    {
        const float Tile = 6f;
        GameObject slab = new GameObject(name);
        slab.transform.SetParent(parent, false);
        slab.transform.position = center;
        Vector3 h = size * 0.5f;
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> triangles = new List<int>();
        void Face(Vector3 normal, Vector3 u, Vector3 v)
        {
            // u x v points along the normal's opposite for Unity's clockwise front faces.
            Vector3 c = Vector3.Scale(normal, h);
            Vector3 du = Vector3.Scale(u, h), dv = Vector3.Scale(v, h);
            int start = vertices.Count;
            Vector3[] corners = { c - du - dv, c - du + dv, c + du + dv, c + du - dv };
            foreach (Vector3 corner in corners)
            {
                vertices.Add(corner);
                Vector3 world = center + corner;
                Vector2 coords = Mathf.Abs(normal.y) > 0.5f ? new Vector2(world.x, world.z)
                    : Mathf.Abs(normal.x) > 0.5f ? new Vector2(world.z, world.y) : new Vector2(world.x, world.y);
                uv.Add(coords / Tile);
            }
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        Face(Vector3.up, Vector3.right, Vector3.forward);
        Face(Vector3.down, Vector3.forward, Vector3.right);
        Face(Vector3.right, Vector3.forward, Vector3.up);
        Face(Vector3.left, Vector3.up, Vector3.forward);
        Face(Vector3.forward, Vector3.up, Vector3.right);
        Face(Vector3.back, Vector3.right, Vector3.up);
        Mesh mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        slab.AddComponent<MeshFilter>().sharedMesh = mesh;
        slab.AddComponent<MeshRenderer>().sharedMaterial = material;
        slab.AddComponent<BoxCollider>().size = size;
    }

    private static bool Inside(Vector2[] polygon, Vector2 point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Vector2 a = polygon[i], b = polygon[j];
            if ((a.y > point.y) != (b.y > point.y) &&
                point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    // ---- Stairs -------------------------------------------------------------------------------

    private static void BuildStairs(Transform root, Materials art)
    {
        Transform stairs = Child(root, "Stairs 1F-2F");
        Vector3 foot = World(P(StairFootX, StairNorthY), 0f);
        Vector3 top = World(P(StairTopX, StairNorthY), FloorTwo);
        float north = World(P(0, StairNorthY), 0f).z, south = World(P(0, StairSouthY), 0f).z;
        float width = north - south;
        float run = foot.x - top.x;

        // What Haru walks on: one smooth solid ramp, foot to landing. It keeps clear of the
        // stair wall's face, where Dudu walks: touching it, the ramp would be a wall in his way.
        const float DuduClearance = 0.25f;
        Mesh ramp = SaveMesh(Wedge(top.x, foot.x, south + DuduClearance, north, FloorTwo, 0f), "StairRamp");
        GameObject walk = new GameObject("Stair Ramp (Collider)");
        walk.transform.SetParent(stairs, false);
        MeshCollider collider = walk.AddComponent<MeshCollider>();
        collider.sharedMesh = ramp;
        collider.convex = true;

        // The steps people see; the solid wedge under them closes the side seen from the room.
        // Both stop a few centimetres short of the walls either side: flush with a wall's face,
        // the two would flicker through each other.
        const float WallGap = 0.03f;
        Mesh support = SaveMesh(Wedge(top.x, foot.x, south + WallGap, north - WallGap, FloorTwo - 0.25f, -0.25f), "StairSupport");
        GameObject under = new GameObject("Stair Support");
        under.transform.SetParent(stairs, false);
        under.AddComponent<MeshFilter>().sharedMesh = support;
        under.AddComponent<MeshRenderer>().sharedMaterial = art.WoodFloor;
        // The stairs are Haru's: Dudu's page shows the wall beside them, not the steps.
        under.AddComponent<RoomOnlyDecor>();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StepsPrefab);
        float unitRun = run / StairUnits, unitRise = FloorTwo / StairUnits;
        for (int i = 0; i < StairUnits; i++)
        {
            GameObject unit = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            unit.name = $"Stairs Steps {i + 1}";
            unit.transform.SetParent(stairs, false);
            // The prefab's steps climb toward its -z; turned so they climb west.
            unit.transform.SetPositionAndRotation(
                new Vector3(foot.x - unitRun * (i + 0.5f), unitRise * i, (north + south) * 0.5f),
                Quaternion.Euler(0f, 90f, 0f));
            unit.transform.localScale = new Vector3((width - WallGap * 2f) / 1.05f, unitRise, unitRun / 1.01f);
            foreach (Collider part in unit.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(part);
            unit.AddComponent<RoomOnlyDecor>();
        }

        // While Haru climbs, the hint at the top of his screen says he can jump.
        StairJumpHint jumpHint = stairs.gameObject.AddComponent<StairJumpHint>();
        Vector3 hintMin = World(P(StairTopX - 10f, StairSouthY), -0.5f), hintMax = World(P(StairFootX + 10f, StairNorthY), FloorTwo + 0.4f);
        jumpHint.stairs = new Bounds((hintMin + hintMax) * 0.5f, new Vector3(
            Mathf.Abs(hintMax.x - hintMin.x), Mathf.Abs(hintMax.y - hintMin.y), Mathf.Abs(hintMax.z - hintMin.z)));
        jumpHint.topHeight = FloorTwo;

        // A railing on 2F around the opening, open at the top step where Haru walks off.
        Transform rails = Child(stairs, "2F Railing");
        rails.gameObject.AddComponent<RoomOnlyDecor>();
        Vector3 railNorthA = World(P(StairTopX + 35f, StairNorthY), FloorTwo);
        Vector3 railNorthB = World(P(StairFootX, StairNorthY), FloorTwo);
        Vector3 railEastB = World(P(StairFootX, StairSouthY), FloorTwo);
        Rail("Railing North", railNorthA, railNorthB, rails, art);
        Rail("Railing East", railNorthB, railEastB, rails, art);
    }

    private static void Rail(string name, Vector3 a, Vector3 b, Transform parent, Materials art)
    {
        Vector3 center = (a + b) * 0.5f + Vector3.up * 0.5f;
        Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(b - a, Vector3.up), Vector3.up);
        Cube(name, center, rotation, new Vector3(Vector3.Distance(a, b), 1f, 0.12f), art.Wood, parent);
    }

    // A wedge between x0 (high end, at height high) and x1 (low end, at height low), z from z0 to z1,
    // solid down to y = low.
    private static Mesh Wedge(float x0, float x1, float z0, float z1, float high, float low)
    {
        Vector3[] v =
        {
            new Vector3(x0, low, z0), new Vector3(x1, low, z0), new Vector3(x0, high, z0),
            new Vector3(x0, low, z1), new Vector3(x1, low, z1), new Vector3(x0, high, z1),
        };
        Vector3[] vertices =
        {
            v[0], v[2], v[1],            // south side
            v[3], v[4], v[5],            // north side
            v[2], v[5], v[4], v[2], v[4], v[1],   // slope
            v[0], v[1], v[4], v[0], v[4], v[3],   // bottom
            v[0], v[3], v[5], v[0], v[5], v[2],   // high end
        };
        int[] triangles = Enumerable.Range(0, vertices.Length).ToArray();
        Vector2[] uv = vertices.Select(p => new Vector2(p.x + p.z, p.y) * 0.5f).ToArray();
        Mesh mesh = new Mesh { name = "Wedge", vertices = vertices, triangles = triangles, uv = uv };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static Mesh SaveMesh(Mesh mesh, string name)
    {
        EnsureFolder(GeneratedFolder);
        string path = $"{GeneratedFolder}/{name}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        mesh.name = name;
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        existing.name = name;
        EditorUtility.SetDirty(existing);
        return existing;
    }

    // ---- Holes, crates, lamps ---------------------------------------------------------------

    // The third hole reaches past 1F's east wall; anything falling there is moved into the room.
    private static void BuildHoleCatcher(Transform root)
    {
        Rect hole = Holes[2];
        const float FirstFloorEast = 745f;
        if (hole.xMax <= FirstFloorEast) return;
        Vector3 a = World(P(FirstFloorEast, hole.yMin), 0.4f), b = World(P(hole.xMax + 10f, hole.yMax), 0.4f);
        GameObject catcher = new GameObject("Hole 3 Fall Redirect");
        catcher.transform.SetParent(root, false);
        catcher.transform.position = new Vector3((a.x + b.x) * 0.5f, 3f, (a.z + b.z) * 0.5f);
        BoxCollider box = catcher.AddComponent<BoxCollider>();
        box.size = new Vector3(Mathf.Abs(b.x - a.x), 5f, Mathf.Abs(b.z - a.z));
        box.isTrigger = true;
        catcher.AddComponent<FallRedirectZone>().shift = new Vector3(-((hole.xMax - FirstFloorEast) * Scale + 1.2f), 0f, 0f);
    }

    private static void CreateCrate(string name, Vector3 position, Transform parent, Materials art)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoxPrefab);
        GameObject crate = new GameObject(name);
        crate.transform.SetParent(parent, false);
        crate.transform.position = position + Vector3.up * (CrateSize * 0.5f);
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        visual.name = "Box Visual";
        visual.transform.SetParent(crate.transform, false);
        // The prefab is a 0.65 m crate standing on its pivot.
        float scale = CrateSize / 0.65f;
        visual.transform.localScale = Vector3.one * scale;
        visual.transform.localPosition = Vector3.down * (CrateSize * 0.5f);
        foreach (Collider part in visual.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(part);
        // The home set's palette material has no texture here and draws plain white; cardboard
        // brown reads as a box to push, as in the sketch.
        foreach (Renderer part in visual.GetComponentsInChildren<Renderer>(true))
            part.sharedMaterials = part.sharedMaterials.Select(_ => art.Crate).ToArray();

        BoxCollider box = crate.AddComponent<BoxCollider>();
        box.size = Vector3.one * CrateSize;
        Rigidbody body = crate.AddComponent<Rigidbody>();
        body.mass = 25f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        crate.AddComponent<PushableBox>();
    }

    private static void BuildLamps(Transform parent, Vector2[] spots, float ceiling, Materials art)
    {
        parent.gameObject.AddComponent<RoomOnlyDecor>();
        for (int i = 0; i < spots.Length; i++)
        {
            GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lamp.name = $"Lamp {i + 1}";
            lamp.transform.SetParent(parent, false);
            lamp.transform.position = World(spots[i], ceiling - 0.02f);
            lamp.transform.localScale = new Vector3(0.7f, 0.03f, 0.7f);
            lamp.GetComponent<Renderer>().sharedMaterial = art.Lamp;
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            GameObject bulb = new GameObject("Light");
            bulb.transform.SetParent(lamp.transform, false);
            bulb.transform.localPosition = new Vector3(0f, -12.33f, 0f);
            Light light = bulb.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 9f;
            light.intensity = 8f;
            light.color = new Color(1f, 0.8f, 0.58f);
        }
    }

    // ---- Door and goal ------------------------------------------------------------------------

    private static GameObject BuildDoor(Transform root, DuduSurface surface, Transform strokeRoot, Materials art, float doorX)
    {
        Transform parent = Child(root, "Clear Door");
        const float DoorWidth = 1.8f, DoorHeight = 4f, DoorY = -0.3f;
        float wallMin = -surface.Width * 0.5f, wallMax = surface.Width * 0.5f;
        float doorMin = doorX - DoorWidth * 0.5f, doorMax = doorX + DoorWidth * 0.5f;

        // The full wall stays as Dudu's surface but stops drawing and blocking; pieces around the
        // opening take over.
        surface.GetComponent<Renderer>().enabled = false;
        Object.DestroyImmediate(surface.GetComponent<DrawingSurface>());
        Object.DestroyImmediate(surface.GetComponent<Collider>());
        Segment("Door Wall Left", (wallMin + doorMin) * 0.5f, 0f, doorMin - wallMin, WallHeight, true);
        Segment("Door Wall Right", (doorMax + wallMax) * 0.5f, 0f, wallMax - doorMax, WallHeight, true);
        float topHeight = WallHeight * 0.5f - (DoorY + DoorHeight * 0.5f);
        Segment("Door Wall Top", doorX, WallHeight * 0.5f - topHeight * 0.5f, DoorWidth, topHeight, true);
        Segment("Door Wall Bottom Filler", doorX, -2.65f, DoorWidth, 0.7f, false);

        GameObject door = Cube("Clear Door - Opens When Ready", surface.SurfaceToWorld(new Vector2(doorX, DoorY)) + surface.Normal * 0.12f,
            surface.transform.rotation, new Vector3(DoorWidth, DoorHeight, 0.5f), art.Ink, parent);
        Frame("Door Frame Left", new Vector2(doorX - 1f, DoorY), new Vector3(0.15f, DoorHeight, 0.6f));
        Frame("Door Frame Right", new Vector2(doorX + 1f, DoorY), new Vector3(0.15f, DoorHeight, 0.6f));
        Frame("Door Frame Top", new Vector2(doorX, DoorY + DoorHeight * 0.5f), new Vector3(2.15f, 0.15f, 0.6f));
        return door;

        void Segment(string name, float x, float y, float width, float height, bool solid)
        {
            // Flush with the wall itself: the surface point sits a little in front of its centre.
            GameObject piece = Cube(name, surface.transform.position + surface.Right * x + surface.Up * y, surface.transform.rotation,
                new Vector3(width, height, WallThickness), art.Paper, parent);
            piece.layer = surface.gameObject.layer;
            if (solid) piece.AddComponent<DrawingSurface>().SetStrokeRoot(strokeRoot);
            else Object.DestroyImmediate(piece.GetComponent<Collider>());
        }

        void Frame(string name, Vector2 position, Vector3 size)
        {
            GameObject frame = Cube(name, surface.SurfaceToWorld(position) + surface.Normal * 0.15f,
                surface.transform.rotation, size, art.Ink, parent);
            Object.DestroyImmediate(frame.GetComponent<Collider>());
        }
    }

    private static ThreeDimensionalGoal BuildGoal(Transform root, DuduSurface surface, float doorX, CornerRoomLevel level)
    {
        // In front of the door, on the room side: reaching it is reaching the door.
        GameObject goal = new GameObject("3D Goal Trigger - In Front Of Door");
        goal.transform.SetParent(root, false);
        goal.transform.SetPositionAndRotation(
            surface.SurfaceToWorld(new Vector2(doorX, -1.5f)) + surface.Normal.normalized * 1.1f, surface.transform.rotation);
        BoxCollider box = goal.AddComponent<BoxCollider>();
        box.size = new Vector3(2.6f, 3f, 1.6f);
        box.isTrigger = true;
        ThreeDimensionalGoal component = goal.AddComponent<ThreeDimensionalGoal>();
        component.Configure(level);
        return component;
    }

    // ---- Opening camera tour ------------------------------------------------------------------

    // 2F first, then down along the stairs, around 1F, and on to Haru. Each point stays at
    // least a metre or so from every wall, floor and ceiling, and the straight line to the next
    // one crosses no wall, so the camera never looks through one.
    private struct TourPoint
    {
        public Vector2 at, look;
        public float height, lookHeight, hold;
        public TourPoint(float x, float y, float h, float lx, float ly, float lh, float hold)
        {
            at = P(x, y); height = h; look = P(lx, ly); lookHeight = lh; this.hold = hold;
        }
    }

    private static readonly TourPoint[] Tour =
    {
        new TourPoint(560, 480, 8.6f, 330, 300, 6.2f, 1.2f),   // 2F: over the hall toward the two holes
        new TourPoint(440, 330, 8.8f, 700, 330, 6.6f, 0.8f),   // 2F: turning east, the third hole and the rooms
        new TourPoint(330, 415, 8.6f, 150, 450, 7.0f, 0f),     // past the corner of the west room's wall
        new TourPoint(150, 420, 8.4f, 260, 489, 4.0f, 0f),     // round to the top of the stairs, clear of the railing
        new TourPoint(175, 489, 7.6f, 370, 489, 1.2f, 0f),     // down the stairs
        new TourPoint(270, 489, 5.0f, 420, 489, 1.5f, 0f),
        new TourPoint(395, 489, 2.5f, 395, 380, 2.0f, 0f),     // the foot of the stairs, turning into 1F
        new TourPoint(390, 390, 3.4f, 250, 200, 1.2f, 0.8f),   // 1F: one look over the west room and its crate
        new TourPoint(300, 412, 2.2f, 210, 410, 1.6f, 0f),     // and on to Haru
    };

    public static Transform BuildIntroPath(Transform root)
    {
        Transform existing = root.Find("Intro Camera Path");
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        Transform path = Child(root, "Intro Camera Path");
        for (int i = 0; i < Tour.Length; i++)
        {
            TourPoint point = Tour[i];
            GameObject waypoint = new GameObject($"Waypoint {i + 1:00}");
            waypoint.transform.SetParent(path, false);
            Vector3 position = World(point.at, point.height);
            waypoint.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation(World(point.look, point.lookHeight) - position, Vector3.up));
            waypoint.AddComponent<IntroWaypoint>().hold = point.hold;
        }
        return path;
    }

    // ---- Players ------------------------------------------------------------------------------

    private static void PlacePlayers(Player3DMovement haru, DuduSurfaceMovement dudu, DuduCameraController duduCamera,
        DuduSurface startWall, Scene scene)
    {
        Transform haruRoot = haru.transform.root;
        haruRoot.SetPositionAndRotation(World(HaruStart, 0.05f), Quaternion.Euler(0f, 0f, 0f));
        HaruDrawingController drawing = haru.GetComponentInChildren<HaruDrawingController>(true);
        if (drawing != null)
        {
            // The rooms are wide; Haru draws on walls across them, as in the tutorial.
            SerializedObject drawingData = new SerializedObject(drawing);
            drawingData.FindProperty("maxDrawDistance").floatValue = 20f;
            drawingData.ApplyModifiedPropertiesWithoutUndo();
        }

        float startX = startWall.WorldToSurface(World(P(132, 380), 0f)).x;
        dudu.Configure(startWall, dudu.GetComponentInChildren<Animator>(), dudu.GetComponentInChildren<SpriteRenderer>());
        SerializedObject duduData = new SerializedObject(dudu);
        duduData.FindProperty("surfacePosition").vector2Value = new Vector2(startX, -1.65f);
        duduData.ApplyModifiedPropertiesWithoutUndo();
        dudu.SetSurface(startWall);
        duduCamera.SetSurface(startWall);
        duduCamera.SetTarget(dudu.transform);

        GameViewManager viewManager = FindInScene<GameViewManager>(scene);
        if (viewManager != null)
            viewManager.Configure(haru.GetComponentInChildren<Camera>(true), haru,
                haru.GetComponentInChildren<Player3DLook>(true), duduCamera.GetComponent<Camera>(), dudu);

        GameObject sun = FindNamed(scene, "Sun Patch");
        if (sun != null) sun.transform.position = World(HaruStart, 5.4f) + Vector3.forward * 1.5f;
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private sealed class Materials
    {
        public Material Paper, Ink, WoodFloor, Plaster, Lamp, Wood;
        private readonly Dictionary<Material, Material> urp = new Dictionary<Material, Material>();

        public static Materials Load()
        {
            Materials art = new Materials
            {
                Paper = AssetDatabase.LoadAssetAtPath<Material>(PaperPath),
                Ink = AssetDatabase.LoadAssetAtPath<Material>(InkPath),
                WoodFloor = AssetDatabase.LoadAssetAtPath<Material>(WoodFloorPath),
                Plaster = AssetDatabase.LoadAssetAtPath<Material>(PlasterPath),
                Lamp = AssetDatabase.LoadAssetAtPath<Material>(LampPath),
            };
            art.Wood = art.Urp(AssetDatabase.LoadAssetAtPath<Material>("Assets/VNB - Home Set/Materials/Wood Light.mat"));
            art.HoleEdge = Solid("Hole Edge", new Color(0.86f, 0.27f, 0.22f));
            art.Crate = Solid("Crate Cardboard", new Color(0.8f, 0.6f, 0.38f));
            return art;
        }

        public Material HoleEdge, Crate;

        private static Material Solid(string name, Color color)
        {
            EnsureFolder(GeneratedFolder);
            string path = $"{GeneratedFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // The home set's walls use the built-in Standard shader, which URP draws pink; a URP Lit
        // copy keeps their texture and colour.
        public Material Urp(Material source)
        {
            if (source == null || source.shader.name.StartsWith("Universal Render Pipeline")) return source;
            if (urp.TryGetValue(source, out Material made)) return made;
            EnsureFolder(GeneratedFolder);
            string path = $"{GeneratedFolder}/{source.name} URP.mat";
            Material copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null)
            {
                copy = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(copy, path);
            }
            if (source.HasProperty("_MainTex")) copy.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
            if (source.HasProperty("_MainTex")) copy.SetTextureScale("_BaseMap", source.GetTextureScale("_MainTex"));
            if (source.HasProperty("_Color")) copy.SetColor("_BaseColor", source.GetColor("_Color"));
            if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null)
            {
                copy.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                copy.EnableKeyword("_NORMALMAP");
            }
            copy.SetFloat("_Smoothness", source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") * 0.5f : 0.2f);
            EditorUtility.SetDirty(copy);
            urp[source] = copy;
            return copy;
        }
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }

    private static GameObject Cube(string name, Vector3 position, Quaternion rotation, Vector3 scale, Material material, Transform parent)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.SetPositionAndRotation(position, rotation);
        cube.transform.localScale = scale;
        if (material != null) cube.GetComponent<Renderer>().sharedMaterial = material;
        return cube;
    }

    private static Transform Child(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing;
        Transform child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    private static GameObject FindNamed(Scene scene, string name) => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
        .Select(item => item.gameObject).FirstOrDefault(item => item.name == name);

    private static T FindInScene<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).FirstOrDefault();
}
#endif
