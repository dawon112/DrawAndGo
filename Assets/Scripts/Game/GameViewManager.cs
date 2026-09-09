using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class GameViewManager : MonoBehaviour
{
    public enum CameraState { Gameplay, IntroCinematic, SplitScreen, ClearCinematic }
    [SerializeField] private bool developmentViewSwitch = true;
    [SerializeField] private Camera haruCamera;
    [SerializeField] private Player3DMovement haruMovement;
    [SerializeField] private Player3DLook haruLook;
    [SerializeField] private Camera duduCamera;
    [SerializeField] private DuduSurfaceMovement duduMovement;
    [SerializeField] private HaruDrawingController haruDrawing;
    [SerializeField] private CrosshairController crosshair;
    [SerializeField, Min(0.1f)] private float splitScreenDuration = 4f;

    private bool duduMode;
    private bool splitSequencePlayed;
    private bool splitSequenceActive;
    private Canvas exitInstructionCanvas;
    private StageCinematicController stageCinematic;
    public CameraState CurrentCameraState { get; private set; }

    private void Awake()
    {
        EnsureAudioListener(haruCamera);
        EnsureAudioListener(duduCamera);
        BuildExitInstruction();
        stageCinematic = GetComponent<StageCinematicController>();
        if (stageCinematic == null) stageCinematic = gameObject.AddComponent<StageCinematicController>();
        stageCinematic.Configure(this, haruCamera, haruMovement, haruLook, duduCamera,
            duduMovement, haruDrawing, crosshair);
    }

    private static void EnsureAudioListener(Camera targetCamera)
    {
        if (targetCamera != null && targetCamera.GetComponent<AudioListener>() == null)
            targetCamera.gameObject.AddComponent<AudioListener>();
    }

    public void Configure(
        Camera newHaruCamera,
        Player3DMovement newHaruMovement,
        Player3DLook newHaruLook,
        Camera newDuduCamera,
        DuduSurfaceMovement newDuduMovement)
    {
        haruCamera = newHaruCamera;
        haruMovement = newHaruMovement;
        haruLook = newHaruLook;
        duduCamera = newDuduCamera;
        duduMovement = newDuduMovement;
    }

    private void Start()
    {
        SetDuduMode(false);
        CornerRoomLevel level = FindAnyObjectByType<CornerRoomLevel>();
        if (level != null) stageCinematic.PlayIntro(level);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (!splitSequenceActive && developmentViewSwitch && keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            SetDuduMode(!duduMode);
    }

    public void PlayAllCoinsCollectedSequence()
    {
        if (splitSequencePlayed) return;
        splitSequencePlayed = true;
        StartCoroutine(PlaySplitScreenSequence());
    }

    public void HideExitInstruction()
    {
        if (exitInstructionCanvas != null) exitInstructionCanvas.enabled = false;
    }

    private IEnumerator PlaySplitScreenSequence()
    {
        while (CurrentCameraState == CameraState.IntroCinematic) yield return null;
        CurrentCameraState = CameraState.SplitScreen;
        splitSequenceActive = true;
        bool restoreDuduMode = duduMode;
        Rect haruRect = haruCamera.rect;
        Rect duduRect = duduCamera.rect;
        AudioListener duduListener = duduCamera.GetComponent<AudioListener>();
        bool duduListenerEnabled = duduListener != null && duduListener.enabled;

        haruCamera.gameObject.SetActive(true);
        duduCamera.gameObject.SetActive(true);
        haruCamera.rect = new Rect(0.5f, 0f, 0.5f, 1f);
        duduCamera.rect = new Rect(0f, 0f, 0.5f, 1f);
        if (duduListener != null) duduListener.enabled = false;
        if (haruDrawing != null) haruDrawing.enabled = false;
        if (haruMovement != null) haruMovement.enabled = false;
        if (haruLook != null) haruLook.enabled = false;
        if (duduMovement != null) duduMovement.SetInputEnabled(false);
        if (crosshair != null)
        {
            crosshair.SetVisible(true);
            crosshair.SetSplitScreenLayout(true);
        }
        CrayonGaugeUI gauge = haruCamera.GetComponent<CrayonGaugeUI>();
        if (gauge != null) gauge.SetSplitScreenLayout(true);

        yield return new WaitForSecondsRealtime(splitScreenDuration);

        haruCamera.rect = haruRect;
        duduCamera.rect = duduRect;
        if (duduListener != null) duduListener.enabled = duduListenerEnabled;
        if (crosshair != null) crosshair.SetSplitScreenLayout(false);
        if (gauge != null) gauge.SetSplitScreenLayout(false);
        splitSequenceActive = false;
        SetDuduMode(false);
        CornerRoomLevel level = FindAnyObjectByType<CornerRoomLevel>();
        if (level != null) stageCinematic.PlayClear(level);
        else
        {
            CurrentCameraState = CameraState.Gameplay;
            SetDuduMode(restoreDuduMode);
        }
    }

    public void EnterCinematic(CameraState state)
    {
        CurrentCameraState = state;
        HideExitInstruction();
        if (haruDrawing != null) haruDrawing.ForceDisableCameraLock();
        if (haruMovement != null) haruMovement.enabled = false;
        if (haruLook != null) haruLook.enabled = false;
        if (duduMovement != null) duduMovement.SetInputEnabled(false);
        if (haruDrawing != null) haruDrawing.enabled = false;
        if (crosshair != null) crosshair.SetVisible(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void FinishIntro()
    {
        CurrentCameraState = CameraState.Gameplay;
        SetDuduMode(false);
    }

    public void ShowHaruCameraWithoutInput()
    {
        if (duduCamera != null) duduCamera.gameObject.SetActive(false);
        if (haruCamera != null) haruCamera.gameObject.SetActive(true);
    }

    private void BuildExitInstruction()
    {
        if (haruCamera == null || exitInstructionCanvas != null) return;
        GameObject canvasObject = new GameObject("3D Exit Instruction Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(haruCamera.transform, false);
        exitInstructionCanvas = canvasObject.GetComponent<Canvas>();
        exitInstructionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        exitInstructionCanvas.sortingOrder = short.MaxValue - 2;

        GameObject textObject = new GameObject("Exit Instruction Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
        textObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -72f);
        rect.sizeDelta = new Vector2(480f, 55f);
        Text text = textObject.GetComponent<Text>();
        text.text = "문 밖으로 나가자!";
        text.font = GameFont.Bold;
        text.fontSize = 28;
        text.fontStyle = FontStyle.Normal;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        Outline outline = textObject.GetComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2f, -2f);
        exitInstructionCanvas.enabled = false;
    }

    private void SetDuduMode(bool enabled)
    {
        duduMode = enabled;
        GameObject player3D = haruMovement != null
            ? haruMovement.transform.root.gameObject
            : null;

        if (!enabled && player3D != null)
            player3D.SetActive(true);

        if (enabled && haruDrawing != null)
            haruDrawing.ForceDisableCameraLock();
        if (haruCamera != null)
            haruCamera.gameObject.SetActive(!enabled);
        if (haruMovement != null)
            haruMovement.enabled = !enabled;
        if (haruLook != null)
            haruLook.enabled = !enabled;
        if (duduCamera != null)
            duduCamera.gameObject.SetActive(enabled);
        if (duduMovement != null)
            duduMovement.SetInputEnabled(enabled);
        if (haruDrawing != null)
            haruDrawing.enabled = !enabled;
        if (crosshair != null)
            crosshair.SetVisible(!enabled);

        // Keep Haru alive while viewing Dudu so the 2D silhouette can follow the
        // real transform and rig. Its movement, camera and drawing are disabled above.

        if (enabled)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
