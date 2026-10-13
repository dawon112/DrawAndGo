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
    private RectTransform haruViewport;
    private Camera duduCamera;
    private Text instruction, speaker, dialogue;
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
        Transform starCounter = room != null ? room.transform.Find("Star Counter Canvas") : null;
        if (starCounter != null) starCounter.gameObject.SetActive(false);
        dudu.gameObject.AddComponent<TutorialBridgeContact>().Owner = this;
        built = true;
        CurrentStep = TutorialStep.Tutorial_01_Move;
        ShowDialogue(new[] { "두두", "하루" }, new[] { "하루! 나 여기 있어!", "두두! 잠깐만, 그쪽으로 갈게!" });
    }

    private void Update()
    {
        if (!built) return;
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
        if (drawStarted && crossed && HasWalkedOnBridge && HasValidBridge && !dialoguePanel.activeSelf && !successDialogue)
        {
            successDialogue = true;
            instructionPanel.SetActive(false);
            highlight.SetActive(false);
            successTrigger.SetActive(false);
            ShowDialogue(new[] { "두두", "하루" }, new[] { "우와! 하루가 그린 선을 밟을 수 있어!", "그럼 내가 길을 만들어줄게!" });
        }
    }

    private void LateUpdate()
    {
        if (!built) return;
        // Obstacle hints belong to Dudu's view; the earlier drawing hints stay with Haru.
        Camera instructionCamera = CurrentStep == TutorialStep.Tutorial_04_Obstacle ||
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
        if (!built || dudu.CurrentSurface != surface) return;
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

    public void EnterRegion(bool isExit, DuduSurfaceMovement player)
    {
        if (player != dudu || player.CurrentSurface != surface || CurrentStep != TutorialStep.Tutorial_02_Draw) return;
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
            instruction.text = "Tab으로 두두를 조작해 그린 선을 밟고 건너가세요!";
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
        instructionPanel.SetActive(false);
        dialoguePanel.SetActive(true);
        speaker.text = speakers[0]; dialogue.text = lines[0];
    }

    private void AdvanceDialogue()
    {
        dialogueIndex++;
        if (dialogueIndex < lines.Length)
        {
            speaker.text = speakers[dialogueIndex]; dialogue.text = lines[dialogueIndex];
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
            ? "마우스 왼쪽 버튼을 누른 채 드래그해서\n선을 그려보세요!\n<Size=21>Tab: 두두 / 하루 전환 · 두두: ← → 이동, Space 점프</Size>"
            : "[ W ][ A ][ S ][ D ] 키를 눌러 이동해보세요!\n마우스를 움직여 주변을 둘러보세요!";
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
        BuildEraseCourse(root, triggers);
        BuildLateCourse(root, triggers);
    }

    private void BuildGround(Transform root, string objectName, float from, float to)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = objectName;
        ground.transform.SetParent(root, false);
        ground.transform.SetPositionAndRotation(surface.SurfaceToWorld(new Vector2((from + to) * 0.5f, groundSurfaceY - 0.07f)) + surface.Normal * 0.055f, surface.transform.rotation);
        ground.transform.localScale = new Vector3(to - from, 0.14f, 0.18f);
        ground.GetComponent<Renderer>().sharedMaterial = originalStartLine.GetComponent<Renderer>().sharedMaterial;
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
        instructionPanel = Panel("InstructionPanel", haruViewport, new Vector2(0.08f, 0.74f), new Vector2(0.92f, 0.92f));
        instruction = Label("InstructionText", instructionPanel.transform, Vector2.zero, Vector2.one, 30, TextAnchor.MiddleCenter);
        dialoguePanel = Panel("DialoguePanel", canvasObject.transform, new Vector2(0.12f, 0.04f), new Vector2(0.88f, 0.23f));
        speaker = Label("SpeakerName", dialoguePanel.transform, new Vector2(0.035f, 0.63f), new Vector2(0.95f, 0.95f), 28, TextAnchor.MiddleLeft);
        speaker.color = new Color(1f, 0.8f, 0.35f);
        dialogue = Label("DialogueText", dialoguePanel.transform, new Vector2(0.035f, 0.22f), new Vector2(0.95f, 0.67f), 32, TextAnchor.MiddleLeft);
        Label("ContinueHint", dialoguePanel.transform, new Vector2(0.03f, 0.02f), new Vector2(0.96f, 0.24f), 20, TextAnchor.MiddleRight).text = "Enter  다음";
        // Persistent hint is available in both views while using the existing Tab switching system.
        Label("ViewSwitchHint", canvasObject.transform, new Vector2(0.02f, 0.94f), new Vector2(0.98f, 0.99f), 22, TextAnchor.MiddleCenter).text = "Tab: 하루 / 두두 전환    |    두두: ← → 이동 · Space 점프";
    }

    private static GameObject Panel(string name, Transform parent, Vector2 min, Vector2 max)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        Place(panel.GetComponent<RectTransform>(), parent, min, max);
        Image image = panel.GetComponent<Image>(); image.color = new Color(0.055f, 0.08f, 0.12f, 0.88f); image.raycastTarget = false;
        return panel;
    }

    private static Text Label(string name, Transform parent, Vector2 min, Vector2 max, int size, TextAnchor alignment)
    {
        GameObject label = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
        Place(label.GetComponent<RectTransform>(), parent, min, max);
        Text text = label.GetComponent<Text>(); text.font = GameFont.Bold; text.fontSize = size;
        text.resizeTextForBestFit = true; text.resizeTextMinSize = 16; text.resizeTextMaxSize = size;
        text.alignment = alignment; text.color = Color.white; text.raycastTarget = false;
        return text;
    }

    private static void Place(RectTransform rect, Transform parent, Vector2 min, Vector2 max)
    {
        rect.SetParent(parent, false); rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void OnDestroy()
    {
        DisposeErase();
        DisposeLateTutorial();
        if (highlightMaterial != null) Destroy(highlightMaterial);
        if (originalStartLine != null) originalStartLine.SetActive(true);
    }
}
