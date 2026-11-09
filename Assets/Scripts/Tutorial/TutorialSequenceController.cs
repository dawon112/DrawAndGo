using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Only installed in Tutorial. Authored environment transforms are never changed.
[DefaultExecutionOrder(100)]
public sealed partial class TutorialSequenceController : MonoBehaviour
{
    public enum TutorialStep { Tutorial_01_Move, Tutorial_02_Draw, Tutorial_02_DrawCompleted, Tutorial_03_Erase, Tutorial_03_EraseCompleted, Tutorial_04_Obstacle, Tutorial_05_Star, Tutorial_06_FinalTest, Tutorial_07_Door, Tutorial_Clear }
    [SerializeField] private Player3DMovement haru;
    [SerializeField] private Player3DLook haruLook;
    [SerializeField] private Camera haruCamera;
    [SerializeField] private DuduSurfaceMovement dudu;
    [SerializeField] private DuduSurface surface;
    [SerializeField] private Transform drawingRoot;
    [SerializeField] private GameObject originalStartLine;
    [SerializeField, Min(0.1f)] private float requiredMoveDistance = 2f;
    [SerializeField, Min(1f)] private float requiredMouseDelta = 180f;
    [SerializeField] private float gapLeft = -3f;
    [SerializeField] private float gapRight = 2.2f;
    [SerializeField] private float groundSurfaceY = -2.33f;
    public TutorialStep CurrentStep { get; private set; }
    public bool HasMoved { get; private set; }
    public bool HasLookedAround => accumulatedLook >= requiredMouseDelta;
    public bool HasValidBridge => validBridge != null;
    public bool HasWalkedOnBridge { get; private set; }

    private Vector3 initialPosition;
    private float accumulatedLook, nextBridgeCheck;
    private bool drawStarted, crossed, successDialogue, built;
    private DrawingStroke validBridge;
    private GameObject highlight, startTrigger, successTrigger, instructionPanel, dialoguePanel;
    // Invisible walls that split the course into one zone per step: each opens only once
    // the step before it is done, so Dudu can never reach a step early, and closes again
    // behind him once he is through, so he cannot walk back into a finished step.
    private sealed class ZoneGate
    {
        public GameObject Wall;
        public DuduSurface Surface;
        public float X;
        public bool Locked;
    }
    private ZoneGate moveGate, drawGate, eraseGate;
    private RectTransform haruViewport;
    private Camera duduCamera;
    private TutorialHintUI instruction;
    private TutorialSpeechBubble dialogueBox;
    private string[] speakers, lines;
    private int dialogueIndex;
    private Material highlightMaterial;

    private void Start()
    {
        if (haru == null || dudu == null || surface == null || haruCamera == null || originalStartLine == null)
        {
            Debug.LogError("Tutorial: missing scene references.", this);
            enabled = false;
            return;
        }
        initialPosition = haru.transform.position;
        BuildCourse();
        BuildUI();
        DuduCameraController duduView = FindAnyObjectByType<DuduCameraController>(FindObjectsInactive.Include);
        duduCamera = duduView != null ? duduView.GetComponent<Camera>() : null;
        InitializeErase();
        InitializeLateTutorial();
        TutorialRoomLevel room = FindAnyObjectByType<TutorialRoomLevel>();
        if (room != null) room.ShowStarCounter(false);
        dudu.gameObject.AddComponent<TutorialBridgeContact>().Owner = this;
        InitializeNetwork();
        built = true;
        CurrentStep = TutorialStep.Tutorial_01_Move;
        ShowDialogue(new[] { "두두", "하루" }, new[] { "하루! 나 여기 있어!", "두두! 잠깐만, 그쪽으로 갈게!" });
    }

    private void Update()
    {
        if (!built) return;
        // Both host and client open the gates from the (synced) step, so Dudu is blocked on
        // whichever machine simulates him.
        UpdateGate(moveGate, CurrentStep >= TutorialStep.Tutorial_02_Draw);
        UpdateGate(drawGate, CurrentStep >= TutorialStep.Tutorial_03_Erase);
        UpdateGate(eraseGate, CurrentStep >= TutorialStep.Tutorial_04_Obstacle);
        if (!IsAuthority)
        {
            UpdateTutorialClient();
            return;
        }
        SendTutorialState();
        LogStepChange();
        UpdateErase();
        UpdateLateTutorial();
        if (CurrentStep == TutorialStep.Tutorial_01_Move)
        {
            Vector3 displacement = haru.transform.position - initialPosition;
            displacement.y = 0f;
            HasMoved |= displacement.magnitude >= requiredMoveDistance;
            if (haruLook != null && haruLook.isActiveAndEnabled && Cursor.lockState == CursorLockMode.Locked && Mouse.current != null)
                accumulatedLook += Mouse.current.delta.ReadValue().magnitude;
        }
        // Enter avoids consuming the same click as drawing or Space as Dudu's jump.
        if (dialoguePanel.activeSelf && Keyboard.current != null &&
            (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            AdvanceDialogue();

        if (CurrentStep == TutorialStep.Tutorial_01_Move && HasMoved && HasLookedAround && !dialoguePanel.activeSelf)
        {
            CurrentStep = TutorialStep.Tutorial_02_Draw;
            instructionPanel.SetActive(false);
            startTrigger.SetActive(true);
        }
        if (CurrentStep == TutorialStep.Tutorial_02_Draw && drawStarted && !successDialogue && Time.time >= nextBridgeCheck)
        {
            nextBridgeCheck = Time.time + 0.15f;
            ValidateBridge();
        }
        // The first gap cannot be crossed without a drawn line, so Dudu standing past it also
        // proves the bridge; otherwise a missed contact check would leave him stuck at the gate.
        if (drawStarted && (crossed && HasWalkedOnBridge && HasValidBridge || DuduPastFirstGap()) &&
            !dialoguePanel.activeSelf && !successDialogue)
        {
            successDialogue = true;
            instructionPanel.SetActive(false);
            highlight.SetActive(false);
            successTrigger.SetActive(false);
            ShowDialogue(new[] { "두두", "하루" }, new[] { "우와! 하루가 그린 선을 밟을 수 있어!", "그럼 내가 길을 만들어줄게!" });
        }
        // Last, so a step's normal clear in this frame always wins.
        CatchUpToDudu();
    }

    private void LateUpdate()
    {
        if (!built) return;
        UpdateSketchDanger();
        // Obstacle hints belong to Dudu's view; the earlier drawing hints stay with Haru.
        // Online, each player has one view, so every hint shows on the local camera.
        Camera instructionCamera = IsNetworked ? (IsAuthority ? haruCamera : duduCamera) :
            CurrentStep == TutorialStep.Tutorial_04_Obstacle ||
            CurrentStep == TutorialStep.Tutorial_05_Star ? duduCamera : haruCamera;
        if (instructionCamera == null)
        {
            haruViewport.gameObject.SetActive(false);
            return;
        }
        Rect view = instructionCamera.rect;
        haruViewport.anchorMin = view.min;
        haruViewport.anchorMax = view.max;
        haruViewport.offsetMin = haruViewport.offsetMax = Vector2.zero;
        haruViewport.gameObject.SetActive(instructionCamera.isActiveAndEnabled);
    }

    private void FixedUpdate()
    {
        if (!built || !IsAuthority || dudu.CurrentSurface != surface) return;
        // The player's drawn-line walker can maintain a small skin gap, without OnCollisionStay.
        // Probe the same physical foot area so that supported walking is still observed.
        BoxCollider body = dudu.GetComponent<BoxCollider>();
        if (body == null) return;
        Vector3 half = Vector3.Scale(body.size * 0.5f, body.transform.lossyScale);
        Vector3 center = body.transform.TransformPoint(body.center);
        foreach (RaycastHit hit in Physics.BoxCastAll(center, half * 0.9f, -surface.Up,
            body.transform.rotation, 0.15f, 1 << LayerMask.NameToLayer("DrawnStroke"), QueryTriggerInteraction.Ignore))
            if (Vector3.Dot(hit.normal, surface.Up) > 0.5f)
                RecordSupportedStroke(hit.collider.GetComponentInParent<DrawingStroke>());
    }

    // The course itself decides how far the tutorial is: Dudu can only get past a gap,
    // the blocking line or the obstacles once it is really solved. If a step's own check
    // missed that (or a pose packet was dropped online), drop the earlier step's
    // leftovers and start the step for where Dudu is now.
    private void CatchUpToDudu()
    {
        if (dialoguePanel.activeSelf || CurrentStep >= TutorialStep.Tutorial_05_Star) return;
        TutorialStep reached = StepAtDuduPosition();
        if (reached <= CurrentStep) return;
        Debug.Log($"Tutorial: Dudu is ahead of {CurrentStep}, catching up to {reached}.", this);

        successDialogue = true;
        instructionPanel.SetActive(false);
        highlight.SetActive(false);
        startTrigger.SetActive(false);
        successTrigger.SetActive(false);
        if (reached == TutorialStep.Tutorial_03_Erase)
        {
            CurrentStep = TutorialStep.Tutorial_03_Erase;
            eraseStartTrigger.SetActive(true);
            if (eraseStartTrigger.GetComponent<Collider>().bounds.Intersects(dudu.GetComponent<Collider>().bounds))
                EnterEraseRegion(false, dudu);
            return;
        }

        CurrentErasePhase = ErasePhase.Completed;
        eraseStartTrigger.SetActive(false);
        eraseSuccessTrigger.SetActive(false);
        eraseHighlight.SetActive(false);
        replacementHighlight.SetActive(false);
        if (reached == TutorialStep.Tutorial_04_Obstacle) BeginObstacleTutorial();
        else BeginStarTutorial();
    }

    // The step whose part of the course Dudu is standing on.
    private TutorialStep StepAtDuduPosition()
    {
        DuduSurface here = dudu.CurrentSurface;
        if (here == null) return TutorialStep.Tutorial_01_Move;
        float x = here.WorldToSurface(dudu.transform.position).x;
        if (lateSections != null && here != surface)
        {
            if (ReachedFirstStarArea(here, x)) return TutorialStep.Tutorial_05_Star;
            return TutorialStep.Tutorial_04_Obstacle;
        }
        if (here != surface) return TutorialStep.Tutorial_01_Move;
        // Past the erase gap, or at the blocking line after the first gap. The margins sit
        // beyond each step's own success triggers, so a normal clear always comes first.
        if (x > eraseGapRight + 1f) return TutorialStep.Tutorial_04_Obstacle;
        if (x > eraseGapLeft - 0.5f) return TutorialStep.Tutorial_03_Erase;
        return TutorialStep.Tutorial_01_Move;
    }

    public void EnterRegion(bool isExit, DuduSurfaceMovement player)
    {
        if (!IsAuthority || player != dudu || player.CurrentSurface != surface ||
            CurrentStep != TutorialStep.Tutorial_02_Draw) return;
        if (isExit)
        {
            if (HasValidBridge && HasWalkedOnBridge &&
                surface.WorldToSurface(player.transform.position).x >= gapRight + 0.1f) crossed = true;
            return;
        }
        if (drawStarted) return;
        drawStarted = true;
        startTrigger.SetActive(false);
        successTrigger.SetActive(true);
        highlight.SetActive(true);
        ShowDialogue(new[] { "두두", "하루" }, new[] { "어? 길이 끊어져 있어.", "잠깐, 내가 이어볼게!" });
    }

    public void RecordBridgeContact(Collision collision)
    {
        if (!IsAuthority) return;
        foreach (ContactPoint contact in collision.contacts)
            if (Vector3.Dot(contact.normal, surface.Up) > 0.5f)
                RecordSupportedStroke(collision.collider.GetComponentInParent<DrawingStroke>());
    }

    private void RecordSupportedStroke(DrawingStroke stroke)
    {
        if (stroke == null || !stroke.IsFinished || stroke.PointCount < 2 || dudu.CurrentSurface != surface) return;
        float x = surface.WorldToSurface(dudu.transform.position).x;
        if (CurrentStep == TutorialStep.Tutorial_02_Draw && drawStarted && x > gapLeft && x < gapRight)
        {
            // Crossing may use multiple strokes, curves or a jump between drawn footholds.
            // Actual support inside the gap is stronger evidence than a single-stroke shape heuristic.
            validBridge = stroke;
            HasWalkedOnBridge = true;
            highlight.SetActive(false);
        }
        RecordReplacementSupport(stroke, x);
    }

    private void ValidateBridge()
    {
        Vector2 position = surface.WorldToSurface(dudu.transform.position);
        if (position.y < groundSurfaceY || position.x < gapLeft - 0.5f || validBridge == null)
        {
            HasWalkedOnBridge = false;
            crossed = false;
        }
        DrawingStroke candidate = HasWalkedOnBridge ? validBridge : null;
        if (candidate == null && drawingRoot != null)
            foreach (DrawingStroke stroke in drawingRoot.GetComponentsInChildren<DrawingStroke>())
                if (IsBridge(stroke)) { candidate = stroke; break; }
        validBridge = candidate;
        highlight.SetActive(candidate == null);
        if (HasWalkedOnBridge && position.x >= gapRight + 0.1f && position.y > groundSurfaceY)
            crossed = true;
        if (!dialoguePanel.activeSelf && candidate != null)
        {
            instruction.text = "두두를 움직여 하루가 그린 선을 밟고 건너가세요!";
        }
    }

    private bool IsBridge(DrawingStroke stroke)
        => IsBridge(stroke, gapLeft, gapRight);

    private bool IsBridge(DrawingStroke stroke, float leftEdge, float rightEdge)
    {
        if (!stroke.IsFinished || stroke.PointCount < 2) return false;
        LineRenderer line = stroke.GetComponent<LineRenderer>();
        bool left = false, right = false;
        float length = 0f;
        Vector3 previous = line.GetPosition(0);
        for (int i = 0; i < line.positionCount; i++)
        {
            Vector3 point = line.GetPosition(i);
            Vector2 p = surface.WorldToSurface(point);
            if (Mathf.Abs(Vector3.Dot(point - surface.transform.position, surface.Normal)) > 0.3f ||
                Mathf.Abs(p.y - groundSurfaceY) > 0.45f) return false;
            left |= p.x <= leftEdge + 0.12f && p.x >= leftEdge - 1f;
            right |= p.x >= rightEdge - 0.12f && p.x <= rightEdge + 1f;
            length += Vector3.Distance(previous, point);
            previous = point;
        }
        if (!left || !right || length < rightEdge - leftEdge - 0.24f) return false;
        BoxCollider[] colliders = stroke.GetComponentsInChildren<BoxCollider>();
        if (colliders.Length < line.positionCount - 1) return false;
        foreach (BoxCollider segment in colliders)
            if (!segment.enabled || segment.isTrigger || !segment.gameObject.activeInHierarchy) return false;
        return true;
    }

    private void ShowDialogue(string[] names, string[] text)
    {
        speakers = names; lines = text; dialogueIndex = 0;
        dialogueSerial++;
        // Dudu keeps moving while a line is shown; only Enter advances it.
        instructionPanel.SetActive(false);
        dialoguePanel.SetActive(true);
        dialogueBox.Show(speakers[0], lines[0]);
    }

    private void AdvanceDialogue()
    {
        dialogueSerial++;
        dialogueIndex++;
        if (dialogueIndex < lines.Length)
        {
            dialogueBox.Show(speakers[dialogueIndex], lines[dialogueIndex]);
            return;
        }
        dialoguePanel.SetActive(false);
        if (CurrentStep == TutorialStep.Tutorial_03_Erase)
        {
            FinishEraseDialogue();
            return;
        }
        if (CurrentStep == TutorialStep.Tutorial_05_Star || CurrentStep == TutorialStep.Tutorial_07_Door)
        {
            FinishLateDialogue();
            return;
        }
        // Sketch course warnings (wind, danger) only close; the obstacle step continues.
        if (CurrentStep == TutorialStep.Tutorial_04_Obstacle) return;
        if (successDialogue)
        {
            instructionPanel.SetActive(false);
            highlight.SetActive(false);
            CurrentStep = TutorialStep.Tutorial_03_Erase;
            eraseStartTrigger.SetActive(true);
            // OnTriggerStay is not guaranteed while Dudu's rigidbody sleeps at the blocker.
            if (eraseStartTrigger.GetComponent<Collider>().bounds.Intersects(dudu.GetComponent<Collider>().bounds))
                EnterEraseRegion(false, dudu);
            return;
        }
        instruction.text = drawStarted
            ? "마우스 왼쪽 버튼을 누른 채 드래그해서 선을 그려보세요!\n<size=21>두두: [ ← ][ → ] 이동, [ Space ] 점프</size>"
            : "[ W ][ A ][ S ][ D ] 이동해보세요!\n<size=21>마우스로 주변 둘러보기</size>";
        instructionPanel.SetActive(true);
    }

    private void BuildCourse()
    {
        Transform root = new GameObject("Tutorial2D").transform;
        root.SetParent(transform, false);
        // Replace only this 2D route line during play. Leave the authored object and all 3D geometry intact.
        originalStartLine.SetActive(false);
        BuildGround(root, "Ground_Start", -surface.Width * 0.5f, gapLeft);
        BuildGround(root, "Gap_Right", gapRight, eraseGapLeft);
        BuildGround(root, "Erase_Gap_Right", eraseGapRight, surface.Width * 0.5f);
        Transform triggers = new GameObject("TutorialTriggers").transform;
        triggers.SetParent(transform, false);
        startTrigger = BuildTrigger(triggers, "DrawStartTrigger", gapLeft - 0.9f, false);
        successTrigger = BuildTrigger(triggers, "DrawSuccessTrigger", gapRight + 0.9f, true);
        startTrigger.SetActive(false); successTrigger.SetActive(false);
        highlight = new GameObject("DrawHighlight", typeof(LineRenderer));
        highlight.transform.SetParent(root, false);
        LineRenderer outline = highlight.GetComponent<LineRenderer>();
        highlightMaterial = new Material(Shader.Find("Sprites/Default"));
        outline.sharedMaterial = highlightMaterial;
        outline.startColor = outline.endColor = new Color(0.3f, 0.65f, 0.95f, 0.25f);
        outline.startWidth = outline.endWidth = 0.025f;
        outline.useWorldSpace = true; outline.loop = true; outline.positionCount = 4;
        outline.SetPositions(new[] {
            surface.SurfaceToWorld(new Vector2(gapLeft - 0.3f, groundSurfaceY - 0.35f)) + surface.Normal * 0.06f,
            surface.SurfaceToWorld(new Vector2(gapRight + 0.3f, groundSurfaceY - 0.35f)) + surface.Normal * 0.06f,
            surface.SurfaceToWorld(new Vector2(gapRight + 0.3f, groundSurfaceY + 0.35f)) + surface.Normal * 0.06f,
            surface.SurfaceToWorld(new Vector2(gapLeft - 0.3f, groundSurfaceY + 0.35f)) + surface.Normal * 0.06f });
        highlight.SetActive(false);
        // Zone 1 (move) ends just before the draw start trigger; zone 2 (draw) ends past the
        // first gap, before the erase start trigger. Later zones are already closed off by the
        // erasable blocking line, the gaps, and the star-gated door.
        moveGate = BuildGate(root, "Gate_MoveZone", surface, gapLeft - 1.8f);
        drawGate = BuildGate(root, "Gate_DrawZone", surface, gapRight + 2.2f);
        BuildEraseCourse(root, triggers);
        BuildLateCourse(root, triggers);
        // The erase gap ends right at the first wall's edge, so the gate that closes the erase
        // zone behind Dudu stands at the start of the next wall, on the floor before the stairs.
        if (lateSections != null && lateSections.Length > 1 && lateSections[1] != null)
            eraseGate = BuildGate(root, "Gate_EraseZone", lateSections[1], -lateSections[1].Width * 0.5f + 0.6f);
    }

    private void BuildGround(Transform root, string objectName, float from, float to)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = objectName;
        ground.transform.SetParent(root, false);
        ground.transform.SetPositionAndRotation(surface.SurfaceToWorld(new Vector2((from + to) * 0.5f, groundSurfaceY - 0.07f)) + surface.Normal * 0.055f, surface.transform.rotation);
        ground.transform.localScale = new Vector3(to - from, 0.14f, 0.18f);
        StyleLikeStartLine(ground);
    }

    // Course lines built during play look like the authored route line (a crayon line).
    private void StyleLikeStartLine(GameObject ground)
    {
        if (originalStartLine.TryGetComponent(out CrayonRouteLine crayon))
            CrayonRouteLine.AddTo(ground, crayon.Material);
        else if (originalStartLine.TryGetComponent(out Renderer look))
            ground.GetComponent<Renderer>().sharedMaterial = look.sharedMaterial;
    }

    private ZoneGate BuildGate(Transform root, string objectName, DuduSurface on, float x)
    {
        // A gate behind Dudu's spawn would shut him out of the course instead of in, and
        // one past the wall's end would stand where Dudu has already moved to the next wall.
        float duduX = on == surface ? surface.WorldToSurface(dudu.transform.position).x : float.NegativeInfinity;
        if (duduX > x - 0.5f || Mathf.Abs(x) > on.Width * 0.5f - 0.3f)
        {
            Debug.LogWarning($"Tutorial: {objectName} at {on.name} x={x:F2} does not fit (Dudu x={duduX:F2}, " +
                $"wall half width {on.Width * 0.5f:F2}), so it is skipped.", this);
            return null;
        }
        GameObject wall = new GameObject(objectName, typeof(BoxCollider));
        // Off the drawing raycasts, so Haru can still draw across where the gate stands.
        wall.layer = LayerMask.NameToLayer("Ignore Raycast");
        wall.transform.SetParent(root, false);
        wall.transform.SetPositionAndRotation(on.SurfaceToWorld(new Vector2(x, groundSurfaceY + 3f)), on.transform.rotation);
        wall.GetComponent<BoxCollider>().size = new Vector3(0.2f, 6f, 0.6f);
        IgnoreHaru(wall);
        return new ZoneGate { Wall = wall, Surface = on, X = x };
    }

    // Only Dudu is held back; Haru walks the 3D room in front of the wall. Deactivating a
    // collider drops its IgnoreCollision pairs, so this runs again each time a gate closes.
    private void IgnoreHaru(GameObject wall)
    {
        Collider box = wall.GetComponent<Collider>();
        foreach (Collider haruCollider in haru.GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(box, haruCollider, true);
    }

    // Closed until its step is done, open while Dudu walks through, then closed for good
    // behind him. Locking also moves his respawn point into the new zone, since dying
    // would otherwise send him back to the start, behind the locked gates.
    private void UpdateGate(ZoneGate gate, bool stepDone)
    {
        if (gate == null || gate.Locked || !stepDone) return;
        if (gate.Wall.activeSelf) gate.Wall.SetActive(false);
        if (!DuduPast(gate)) return;
        gate.Wall.SetActive(true);
        IgnoreHaru(gate.Wall);
        gate.Locked = true;
        dudu.SetCheckpoint();
    }

    // Dudu is a metre past the gate on its wall, or already on a later wall of the course.
    private bool DuduPast(ZoneGate gate)
    {
        DuduSurface here = dudu.CurrentSurface;
        if (here == null) return false;
        if (here == gate.Surface) return here.WorldToSurface(dudu.transform.position).x > gate.X + 1f;
        if (here == surface) return false;
        return CurrentStep >= TutorialStep.Tutorial_04_Obstacle;
    }

    private bool DuduPastFirstGap()
    {
        if (CurrentStep != TutorialStep.Tutorial_02_Draw || dudu.CurrentSurface != surface) return false;
        Vector2 p = surface.WorldToSurface(dudu.transform.position);
        return p.x >= gapRight + 0.3f && p.x < eraseGapLeft - 0.5f && p.y >= groundSurfaceY;
    }

    private GameObject BuildTrigger(Transform root, string objectName, float x, bool isExit)
    {
        GameObject trigger = new GameObject(objectName, typeof(BoxCollider), typeof(TutorialRegionTrigger));
        trigger.transform.SetParent(root, false);
        trigger.transform.SetPositionAndRotation(surface.SurfaceToWorld(new Vector2(x, groundSurfaceY + 1.1f)), surface.transform.rotation);
        BoxCollider box = trigger.GetComponent<BoxCollider>();
        box.isTrigger = true; box.size = new Vector3(1.2f, 3f, 0.6f);
        trigger.GetComponent<TutorialRegionTrigger>().Configure(this, isExit);
        return trigger;
    }

    private void BuildUI()
    {
        GameObject canvasObject = new GameObject("TutorialCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        haruViewport = new GameObject("HaruViewport", typeof(RectTransform)).GetComponent<RectTransform>();
        haruViewport.SetParent(canvasObject.transform, false);

        // Whether a hint or a line is up stays plain state on these two objects, as the steps
        // and the network expect. The prefabs watch them, so they can fade in and out.
        instructionPanel = new GameObject("Hint State");
        instructionPanel.transform.SetParent(canvasObject.transform, false);
        instructionPanel.SetActive(false);
        instruction = Instantiate(Resources.Load<TutorialHintUI>("UI/TutorialHint"), haruViewport, false);
        instruction.Bind(instructionPanel);

        dialoguePanel = new GameObject("Dialogue State");
        dialoguePanel.transform.SetParent(canvasObject.transform, false);
        dialoguePanel.SetActive(false);
        dialogueBox = Instantiate(Resources.Load<TutorialSpeechBubble>("UI/TutorialSpeechBubble"), canvasObject.transform, false);
        dialogueBox.Bind(dialoguePanel, dudu, haru.transform);
    }

    private void OnDestroy()
    {
        DisposeNetwork();
        DisposeErase();
        DisposeLateTutorial();
        if (highlightMaterial != null) Destroy(highlightMaterial);
        if (originalStartLine != null) originalStartLine.SetActive(true);
    }
}
