using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Continuation of the existing scene-local manager; shares its dialogue, UI and bridge validator.
public sealed partial class TutorialSequenceController
{
    public enum ErasePhase { WaitingForArea, SwitchToEraser, RemoveTarget, RemovedDialogue, SwitchToPen, Redraw, Completed }
    [Header("Erase tutorial (first wall surface coordinates)")]
    [SerializeField] private float eraseGapLeft = 4.2f;
    [SerializeField] private float eraseGapRight = 9f;
    public ErasePhase CurrentErasePhase { get; private set; }
    public bool TargetLineRemoved { get; private set; }
    public bool HasWalkedOnReplacement { get; private set; }

    private HaruDrawingController tutorialDrawing;
    private DrawingTool previousTool;
    private GameObject eraseStartTrigger, eraseSuccessTrigger, eraseHighlight, replacementHighlight;
    private DrawingStroke replacementLine;
    private int targetStrokeId;
    private bool targetWasErased;
    private float nextEraseCheck;
    private Material blockingLineMaterial;
    private readonly HashSet<int> oldStrokeIds = new HashSet<int>();

    private void InitializeErase()
    {
        tutorialDrawing = haruCamera.GetComponent<HaruDrawingController>();
        if (tutorialDrawing == null)
        {
            Debug.LogError("Tutorial: HaruDrawingController is missing.", this);
            return;
        }
        previousTool = tutorialDrawing.CurrentTool;
        tutorialDrawing.StrokeErased += OnTutorialStrokeErased;
    }

    private void OnTutorialStrokeErased(int sourceId)
    {
        if (sourceId == targetStrokeId) targetWasErased = true;
    }

    public void EnterEraseRegion(bool isExit, DuduSurfaceMovement player)
    {
        if (player != dudu || player.CurrentSurface != surface || CurrentStep != TutorialStep.Tutorial_03_Erase) return;
        if (isExit)
        {
            if (CurrentErasePhase != ErasePhase.Redraw || !TargetLineRemoved ||
                replacementLine == null || !HasWalkedOnReplacement) return;
            if (surface.WorldToSurface(player.transform.position).x < eraseGapRight + 0.15f) return;
            CurrentErasePhase = ErasePhase.Completed;
            CurrentStep = TutorialStep.Tutorial_03_EraseCompleted;
            instructionPanel.SetActive(false);
            eraseHighlight.SetActive(false);
            replacementHighlight.SetActive(false);
            eraseSuccessTrigger.SetActive(false);
            return;
        }
        if (CurrentErasePhase != ErasePhase.WaitingForArea) return;
        CurrentErasePhase = ErasePhase.SwitchToEraser;
        eraseStartTrigger.SetActive(false);
        eraseHighlight.SetActive(true);
        ShowDialogue(new[] { "두두", "하루" }, new[] {
            "어라? 이 선 때문에 앞으로 못 가겠어!", "그러면 지우고 다시 그리면 되겠다." });
    }

    private void UpdateErase()
    {
        if (tutorialDrawing == null) return;
        DrawingTool tool = tutorialDrawing.CurrentTool;
        bool toggled = tutorialDrawing.isActiveAndEnabled && Keyboard.current != null &&
            Keyboard.current.eKey.wasPressedThisFrame && previousTool != tool;
        previousTool = tool;
        if (CurrentStep != TutorialStep.Tutorial_03_Erase || dialoguePanel.activeSelf) return;

        if (CurrentErasePhase == ErasePhase.SwitchToEraser && toggled && tool == DrawingTool.Eraser)
        {
            CurrentErasePhase = ErasePhase.RemoveTarget;
            SetEraseInstruction("지우고 싶은 선을 지워보세요!\n<size=21>왼쪽 버튼을 누른 채 파란 선의 아래쪽을 지워 통로를 열어주세요.</size>");
        }
        else if (CurrentErasePhase == ErasePhase.SwitchToPen && toggled && tool == DrawingTool.Pen)
        {
            CurrentErasePhase = ErasePhase.Redraw;
            oldStrokeIds.Clear();
            foreach (DrawingStroke stroke in drawingRoot.GetComponentsInChildren<DrawingStroke>())
                oldStrokeIds.Add(stroke.SourceStrokeId);
            replacementHighlight.SetActive(true);
            eraseSuccessTrigger.SetActive(true);
            SetEraseInstruction("새로운 길을 그려 두두가 지나갈 수 있게 해보세요!");
        }
        if (Time.time < nextEraseCheck) return;
        nextEraseCheck = Time.time + 0.15f;
        if (CurrentErasePhase == ErasePhase.RemoveTarget && targetWasErased && IsTargetPassageClear())
        {
            TargetLineRemoved = true;
            CurrentErasePhase = ErasePhase.RemovedDialogue;
            eraseHighlight.SetActive(false);
            ShowDialogue(new[] { "두두" }, new[] { "됐다! 이제 다시 그려줘!" });
        }
        if (CurrentErasePhase == ErasePhase.Redraw)
        {
            Vector2 position = surface.WorldToSurface(dudu.transform.position);
            if (position.y < groundSurfaceY || position.x < eraseGapLeft - 0.5f || replacementLine == null)
                HasWalkedOnReplacement = false;
            DrawingStroke candidate = HasWalkedOnReplacement ? replacementLine : null;
            if (candidate == null)
                foreach (DrawingStroke stroke in drawingRoot.GetComponentsInChildren<DrawingStroke>())
                    if (!oldStrokeIds.Contains(stroke.SourceStrokeId) && IsBridge(stroke, eraseGapLeft, eraseGapRight))
                    { candidate = stroke; break; }
            if (candidate != replacementLine)
            {
                HasWalkedOnReplacement = false;
                SetEraseInstruction(candidate != null
                    ? "길이 이어졌어요! Tab으로 두두를 조작해 건너가세요.\n<size=21>선의 시작점이 높으면 Space로 올라가세요.</size>"
                    : "새로운 길을 그려 두두가 지나갈 수 있게 해보세요!");
            }
            replacementLine = candidate;
            replacementHighlight.SetActive(candidate == null);
            if (surface.WorldToSurface(dudu.transform.position).y < groundSurfaceY)
                HasWalkedOnReplacement = false;
        }
    }

    private bool IsTargetPassageClear()
    {
        // Erase splits a stroke and destroys the original. Inspect every descendant's real collider,
        // so a tiny notch or erasing an unrelated line cannot complete this objective.
        Vector3 center = surface.SurfaceToWorld(new Vector2(eraseGapLeft - 0.05f, groundSurfaceY + 0.78f));
        foreach (Collider collider in Physics.OverlapBox(center, new Vector3(0.48f, 0.76f, 0.25f),
            surface.transform.rotation, 1 << LayerMask.NameToLayer("DrawnStroke"), QueryTriggerInteraction.Ignore))
        {
            DrawingStroke source = collider.GetComponentInParent<DrawingStroke>();
            if (source != null && source.SourceStrokeId == targetStrokeId) return false;
        }
        return true;
    }

    private void FinishEraseDialogue()
    {
        if (CurrentErasePhase == ErasePhase.RemovedDialogue)
        {
            CurrentErasePhase = ErasePhase.SwitchToPen;
            SetEraseInstruction(tutorialDrawing != null && tutorialDrawing.CurrentTool == DrawingTool.Pen
                ? "이미 그리기 모드입니다. [ E ]로 지우개로 돌아간 뒤\n[ E ] 키를 눌러 다시 그리기 모드로 전환하세요!"
                : "[ E ] 키를 눌러 다시 그리기 모드로 전환하세요!");
        }
        else if (CurrentErasePhase == ErasePhase.SwitchToEraser)
        {
            SetEraseInstruction(tutorialDrawing != null && tutorialDrawing.CurrentTool == DrawingTool.Eraser
                ? "이미 지우개 모드입니다. [ E ]로 그리기로 돌아간 뒤\n[ E ] 키를 눌러 지우개로 전환하세요!"
                : "[ E ] 키를 눌러 지우개로 전환하세요!");
        }
    }

    private void SetEraseInstruction(string text)
    {
        instruction.text = text;
        instructionPanel.SetActive(true);
    }

    private void RecordReplacementSupport(DrawingStroke stroke, float x)
    {
        if (CurrentStep != TutorialStep.Tutorial_03_Erase || CurrentErasePhase != ErasePhase.Redraw ||
            oldStrokeIds.Contains(stroke.SourceStrokeId)) return;
        if (x <= eraseGapLeft || x >= eraseGapRight) return;
        replacementLine = stroke;
        HasWalkedOnReplacement = true;
        replacementHighlight.SetActive(false);
    }

    private void BuildEraseCourse(Transform root, Transform triggers)
    {
        eraseStartTrigger = BuildTrigger(triggers, "EraseStartTrigger", eraseGapLeft - 0.7f, false);
        eraseStartTrigger.GetComponent<TutorialRegionTrigger>().Configure(this, false, true);
        eraseSuccessTrigger = BuildTrigger(triggers, "EraseSuccessTrigger", eraseGapRight + 0.35f, true);
        eraseSuccessTrigger.GetComponent<BoxCollider>().size = new Vector3(0.25f, 2f, 0.6f);
        eraseSuccessTrigger.GetComponent<TutorialRegionTrigger>().Configure(this, true, true);
        eraseStartTrigger.SetActive(false); eraseSuccessTrigger.SetActive(false);

        // The player's first bridge remains in place. Its endpoints cannot block this later gap,
        // so use the permitted fallback: a real, erasable DrawingStroke, never a cube obstacle.
        GameObject blocker = new GameObject("Tutorial_BlockingDrawnLine", typeof(LineRenderer), typeof(DrawingStroke));
        blocker.transform.SetParent(drawingRoot, false);
        blockingLineMaterial = new Material(Shader.Find("Sprites/Default"));
        DrawingStroke stroke = blocker.GetComponent<DrawingStroke>();
        Vector3 low = surface.SurfaceToWorld(new Vector2(eraseGapLeft - 0.05f, groundSurfaceY - 0.15f)) + surface.Normal * 0.04f;
        Vector3 high = low + surface.Up * 3.65f;
        stroke.Initialize(blockingLineMaterial, 0.08f, new Color(0.086f, 0.498f, 0.765f), low,
            0.08f, 0.18f, surface.Normal, LayerMask.NameToLayer("DrawnStroke"));
        stroke.AddPoint(high); stroke.FinishDrawing();
        targetStrokeId = stroke.SourceStrokeId;
        eraseHighlight = MakeEraseOutline(root, "EraseTargetHighlight", eraseGapLeft - 0.3f, eraseGapLeft + 0.2f,
            groundSurfaceY - 0.2f, groundSurfaceY + 1.6f);
        replacementHighlight = MakeEraseOutline(root, "ReplacementDrawHighlight", eraseGapLeft - 0.2f, eraseGapRight + 0.2f,
            groundSurfaceY - 0.35f, groundSurfaceY + 0.35f);
    }

    private GameObject MakeEraseOutline(Transform root, string name, float left, float right, float bottom, float top)
    {
        GameObject obj = new GameObject(name, typeof(LineRenderer));
        obj.transform.SetParent(root, false);
        LineRenderer line = obj.GetComponent<LineRenderer>();
        line.sharedMaterial = highlightMaterial;
        line.startColor = line.endColor = new Color(0.3f, 0.65f, 0.95f, 0.3f);
        line.startWidth = line.endWidth = 0.025f;
        line.useWorldSpace = true; line.loop = true; line.positionCount = 4;
        Vector2[] points = { new Vector2(left, bottom), new Vector2(right, bottom), new Vector2(right, top), new Vector2(left, top) };
        for (int i = 0; i < points.Length; i++) line.SetPosition(i, surface.SurfaceToWorld(points[i]) + surface.Normal * 0.07f);
        obj.SetActive(false);
        return obj;
    }

    private void DisposeErase()
    {
        if (tutorialDrawing != null) tutorialDrawing.StrokeErased -= OnTutorialStrokeErased;
        if (blockingLineMaterial != null) Destroy(blockingLineMaterial);
    }
}
