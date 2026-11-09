using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class StageCinematicController : MonoBehaviour
{
    [Header("Intro")]
    [SerializeField, Min(0.1f)] private float waypointMoveSpeed = 7f;
    [SerializeField, Min(0f)] private float waypointWaitTime = 0.7f;
    [SerializeField, Min(0f)] private float introBlendDuration = 0.8f;

    private GameViewManager manager;
    private Camera haruCamera, duduCamera, cinematicCamera;
    private Player3DMovement haruMovement;
    private Player3DLook haruLook;
    private DuduSurfaceMovement duduMovement;
    private HaruDrawingController haruDrawing;
    private CrosshairController crosshair;
    private CanvasGroup fadeGroup;
    private Text skipText;
    private ClearSequenceController clearSequence;
    private bool introPlayed;
    private bool skipRequested;
    // What the clear fades to and the final result is written on, and the next stage comes out of.
    private static readonly Color PaperColor = new Color(0.98f, 0.94f, 0.85f);

    private void Update()
    {
        if (!SettingsPopupController.IsAnyOpen && skipText != null &&
            skipText.gameObject.activeSelf && Keyboard.current != null &&
            Keyboard.current.qKey.wasPressedThisFrame)
            skipRequested = true;
    }

    public void Configure(GameViewManager owner, Camera playerCamera, Player3DMovement movement,
        Player3DLook look, Camera twoDimensionalCamera, DuduSurfaceMovement twoDimensionalMovement,
        HaruDrawingController drawing, CrosshairController targetCrosshair)
    {
        manager = owner; haruCamera = playerCamera; haruMovement = movement; haruLook = look;
        duduCamera = twoDimensionalCamera; duduMovement = twoDimensionalMovement;
        haruDrawing = drawing; crosshair = targetCrosshair;
        BuildCameraAndFade();
        clearSequence = GetComponent<ClearSequenceController>();
        if (clearSequence == null) clearSequence = gameObject.AddComponent<ClearSequenceController>();
        clearSequence.Configure(owner, playerCamera, twoDimensionalCamera, cinematicCamera, fadeGroup,
            twoDimensionalMovement, movement, drawing);
        if (ClearSequenceController.ConsumeFadeIn()) StartCoroutine(FadeInFromClear());
    }

    private IEnumerator FadeInFromClear()
    {
        const float Seconds = 0.5f;
        fadeGroup.alpha = 1f;
        for (float t = 0f; t < Seconds; t += Time.unscaledDeltaTime)
        {
            fadeGroup.alpha = 1f - Mathf.SmoothStep(0f, 1f, t / Seconds);
            yield return null;
        }
        fadeGroup.alpha = 0f;
    }

    public void PlayIntro(CornerRoomLevel level)
    {
        if (!introPlayed && level != null) StartCoroutine(IntroRoutine(level));
    }

    public void PlayClear(CornerRoomLevel level)
    {
        if (level == null) return;
        Vector3 facing = level.surfaces != null && level.surfaces.Length > 4 && level.surfaces[4] != null
            ? level.surfaces[4].Normal : (level.door != null ? level.door.transform.forward : Vector3.forward);
        clearSequence.Play(level.door, facing, level.OpenDoorForCinematic, () =>
        {
            level.TryCompleteThreeDimensionalGoal(haruMovement);
            StageFlow.Instance?.CompleteCurrentStage();
            Debug.Log("Stage Clear Complete", level);
        });
    }

    public void PlayTutorialDoorOpen(TutorialRoomLevel level)
    {
        if (level == null) return;
        Vector3 facing = level.Door != null ? level.Door.transform.forward : Vector3.forward;
        clearSequence.Play(level.Door, facing, level.OpenDoorForCinematic, () =>
        {
            StageFlow.Instance?.CompleteCurrentStage();
            Debug.Log("Tutorial Clear Complete", level);
        });
    }

    private void BuildCameraAndFade()
    {
        Transform existing = transform.Find("StageCinematicCamera");
        if (existing == null)
        {
            GameObject cameraObject = new GameObject("StageCinematicCamera");
            cameraObject.SetActive(false);
            cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            existing = cameraObject.transform; existing.SetParent(transform, false);
        }
        cinematicCamera = existing.GetComponent<Camera>();
        cinematicCamera.gameObject.SetActive(false);

        GameObject canvasObject = new GameObject("GlobalCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = short.MaxValue;
        GameObject panel = new GameObject("FadePanel", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(CanvasGroup));
        panel.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = PaperColor;
        fadeGroup = panel.GetComponent<CanvasGroup>(); fadeGroup.alpha = 0f; fadeGroup.blocksRaycasts = false;

        GameObject skipObject = new GameObject("Cinematic Skip Text", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Text), typeof(Outline));
        skipObject.transform.SetParent(canvasObject.transform, false);
        RectTransform skipRect = skipObject.GetComponent<RectTransform>();
        skipRect.anchorMin = skipRect.anchorMax = skipRect.pivot = Vector2.zero;
        skipRect.anchoredPosition = new Vector2(24f, 22f);
        skipRect.sizeDelta = new Vector2(1080f, 144f);
        skipText = skipObject.GetComponent<Text>();
        skipText.text = "Q  스킵하고 싶어요";
        skipText.font = GameFont.Bold;
        skipText.fontSize = 72;
        skipText.fontStyle = FontStyle.Normal;
        skipText.alignment = TextAnchor.MiddleLeft;
        skipText.color = Color.white;
        skipText.raycastTarget = false;
        Outline skipOutline = skipObject.GetComponent<Outline>();
        skipOutline.effectColor = Color.black;
        skipOutline.effectDistance = new Vector2(6f, -6f);
        skipObject.SetActive(false);
    }

    // Writes on the top-most canvas, over the paper-coloured fade a clear ends on.
    public bool ShowCenterMessage(string message)
    {
        if (fadeGroup == null) return false;
        fadeGroup.alpha = 1f;
        GameObject label = new GameObject("Center Message", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform rect = label.GetComponent<RectTransform>();
        rect.SetParent(fadeGroup.transform.parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Text text = label.GetComponent<Text>();
        text.font = GameFont.Bold;
        text.fontSize = 64;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.12f, 0.16f, 0.22f);
        text.raycastTarget = false;
        text.text = message;
        return true;
    }

    private IEnumerator IntroRoutine(CornerRoomLevel level)
    {
        introPlayed = true;
        BeginSkippableCinematic();
        manager.EnterCinematic(GameViewManager.CameraState.IntroCinematic);
        ActivateCinematic();
        List<Transform> points = BuildIntroPoints(level);
        cinematicCamera.transform.SetPositionAndRotation(points[0].position, points[0].rotation);
        yield return WaitOrSkip(1f);
        if (skipRequested) { FinishIntroImmediately(); yield break; }
        for (int i = 1; i < points.Count; i++)
        {
            float duration = Mathf.Max(0.15f,
                Vector3.Distance(cinematicCamera.transform.position, points[i].position) / waypointMoveSpeed);
            yield return MoveCamera(points[i].position, points[i].rotation, 60f, duration);
            if (skipRequested) { FinishIntroImmediately(); yield break; }
            yield return WaitOrSkip(i == points.Count - 1 ? 1f : waypointWaitTime);
            if (skipRequested) { FinishIntroImmediately(); yield break; }
        }
        yield return MoveCamera(haruCamera.transform.position, haruCamera.transform.rotation,
            haruCamera.fieldOfView, introBlendDuration);
        cinematicCamera.gameObject.SetActive(false);
        EndSkippableCinematic();
        manager.FinishIntro();
    }

    private List<Transform> BuildIntroPoints(CornerRoomLevel level)
    {
        Transform root = new GameObject("StageIntroPoints").transform;
        root.SetParent(transform, false);
        List<Transform> result = new List<Transform>();
        result.Add(CreateLookPoint(root, "Intro_00_Door", DoorCenter(level), DoorViewPosition(level, 6f)));
        int[] surfaceOrder = { 2, 3, 1, 4 };
        string[] names = { "Intro_01_Obstacle", "Intro_02_Gap", "Intro_03_Middle", "Intro_04_LateSection" };
        for (int i = 0; i < surfaceOrder.Length; i++)
        {
            DuduSurface surface = level.surfaces[surfaceOrder[i]];
            Vector3 target = surface.transform.position;
            result.Add(CreateLookPoint(root, names[i], target, target + surface.Normal.normalized * 7f + Vector3.up * 1.5f));
        }
        Transform start = new GameObject("Intro_05_Start").transform;
        start.SetParent(root, false);
        start.SetPositionAndRotation(haruCamera.transform.position, haruCamera.transform.rotation);
        result.Add(start);
        return result;
    }

    private IEnumerator WaitOrSkip(float duration)
    {
        float endTime = Time.unscaledTime + duration;
        while (!skipRequested && Time.unscaledTime < endTime) yield return null;
    }

    private void BeginSkippableCinematic()
    {
        skipRequested = false;
        if (skipText != null) skipText.gameObject.SetActive(true);
    }

    private void EndSkippableCinematic()
    {
        if (skipText != null) skipText.gameObject.SetActive(false);
        skipRequested = false;
    }

    private void FinishIntroImmediately()
    {
        cinematicCamera.gameObject.SetActive(false);
        EndSkippableCinematic();
        manager.FinishIntro();
    }

    private void ActivateCinematic()
    {
        if (haruCamera != null) haruCamera.gameObject.SetActive(false);
        if (duduCamera != null) duduCamera.gameObject.SetActive(false);
        cinematicCamera.gameObject.SetActive(true); cinematicCamera.enabled = true;
    }

    private IEnumerator MoveCamera(Vector3 position, Quaternion rotation, float fov, float duration)
    {
        Vector3 startPosition = cinematicCamera.transform.position;
        Quaternion startRotation = cinematicCamera.transform.rotation;
        float startFov = cinematicCamera.fieldOfView;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            if (skipRequested) yield break;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration)));
            cinematicCamera.transform.position = Vector3.Lerp(startPosition, position, t);
            cinematicCamera.transform.rotation = Quaternion.Slerp(startRotation, rotation, t);
            cinematicCamera.fieldOfView = Mathf.Lerp(startFov, fov, t);
            yield return null;
        }
        cinematicCamera.transform.SetPositionAndRotation(position, rotation); cinematicCamera.fieldOfView = fov;
    }

    private static Transform CreateLookPoint(Transform parent, string pointName, Vector3 target, Vector3 position)
    {
        Transform point = new GameObject(pointName).transform; point.SetParent(parent, false);
        point.position = position; point.rotation = Quaternion.LookRotation(target - position, Vector3.up);
        return point;
    }

    private static Vector3 DoorCenter(CornerRoomLevel level)
    {
        Renderer renderer = level.door != null ? level.door.GetComponent<Renderer>() : null;
        return renderer != null ? renderer.bounds.center : level.door.transform.position;
    }

    private Vector3 DoorViewPosition(CornerRoomLevel level, float distance)
    {
        Vector3 center = DoorCenter(level);
        Vector3 roomSide = Vector3.ProjectOnPlane(haruMovement.transform.position - center, Vector3.up).normalized;
        if (roomSide.sqrMagnitude < 0.1f) roomSide = -level.surfaces[4].Normal.normalized;
        return center + roomSide * distance + Vector3.up * 1.2f;
    }
}
