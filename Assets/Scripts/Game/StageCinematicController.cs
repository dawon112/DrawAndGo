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
    [Header("Clear")]
    [SerializeField, Min(0f)] private float doorFocusTime = 0.7f;
    [SerializeField, Min(0.1f)] private float doorZoomDuration = 1.8f;
    [SerializeField, Min(0f)] private float doorOpenTiming = 1.1f;
    [SerializeField, Min(0f)] private float openedDoorHoldTime = 0.8f;
    [SerializeField, Min(0.1f)] private float fadeDuration = 1.5f;

    private GameViewManager manager;
    private Camera haruCamera, duduCamera, cinematicCamera;
    private Player3DMovement haruMovement;
    private Player3DLook haruLook;
    private DuduSurfaceMovement duduMovement;
    private HaruDrawingController haruDrawing;
    private CrosshairController crosshair;
    private CanvasGroup fadeGroup;
    private Text skipText;
    private bool introPlayed, clearPlaying;
    private bool skipRequested;

    private void Update()
    {
        if (skipText != null && skipText.gameObject.activeSelf && Keyboard.current != null &&
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
    }

    public void PlayIntro(CornerRoomLevel level)
    {
        if (!introPlayed && level != null) StartCoroutine(IntroRoutine(level));
    }

    public void PlayClear(CornerRoomLevel level)
    {
        if (!clearPlaying && level != null) StartCoroutine(ClearRoutine(level));
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
        panel.GetComponent<Image>().color = Color.white;
        fadeGroup = panel.GetComponent<CanvasGroup>(); fadeGroup.alpha = 0f; fadeGroup.blocksRaycasts = false;

        GameObject skipObject = new GameObject("Cinematic Skip Text", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Text), typeof(Outline));
        skipObject.transform.SetParent(canvasObject.transform, false);
        RectTransform skipRect = skipObject.GetComponent<RectTransform>();
        skipRect.anchorMin = skipRect.anchorMax = skipRect.pivot = Vector2.zero;
        skipRect.anchoredPosition = new Vector2(24f, 22f);
        skipRect.sizeDelta = new Vector2(360f, 48f);
        skipText = skipObject.GetComponent<Text>();
        skipText.text = "Q  스킵하고 싶어요";
        skipText.font = GameFont.Bold;
        skipText.fontSize = 24;
        skipText.fontStyle = FontStyle.Normal;
        skipText.alignment = TextAnchor.MiddleLeft;
        skipText.color = Color.white;
        skipText.raycastTarget = false;
        Outline skipOutline = skipObject.GetComponent<Outline>();
        skipOutline.effectColor = Color.black;
        skipOutline.effectDistance = new Vector2(2f, -2f);
        skipObject.SetActive(false);
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

    private IEnumerator ClearRoutine(CornerRoomLevel level)
    {
        clearPlaying = true;
        BeginSkippableCinematic();
        manager.EnterCinematic(GameViewManager.CameraState.ClearCinematic);
        manager.ShowHaruCameraWithoutInput();
        ActivateCinematic();
        cinematicCamera.transform.SetPositionAndRotation(haruCamera.transform.position, haruCamera.transform.rotation);
        Vector3 center = DoorCenter(level);
        Transform focus = CreateLookPoint(transform, "ClearDoorCameraPoint", center, DoorFrontPosition(level, 6f));
        GameObject whiteBeyond = BuildWhiteBeyondDoor(center, focus.position);
        yield return MoveCamera(focus.position, focus.rotation, 58f, introBlendDuration);
        if (skipRequested) { FinishClearImmediately(level, whiteBeyond); yield break; }
        yield return WaitOrSkip(doorFocusTime);
        if (skipRequested) { FinishClearImmediately(level, whiteBeyond); yield break; }
        Vector3 zoomPosition = Vector3.Lerp(focus.position, center, 0.38f);
        float elapsed = 0f;
        bool opened = false;
        Vector3 startPosition = cinematicCamera.transform.position;
        Quaternion startRotation = cinematicCamera.transform.rotation;
        while (elapsed < doorZoomDuration)
        {
            if (skipRequested) { FinishClearImmediately(level, whiteBeyond); yield break; }
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / doorZoomDuration));
            cinematicCamera.transform.position = Vector3.Lerp(startPosition, zoomPosition, t);
            cinematicCamera.transform.rotation = Quaternion.Slerp(startRotation, focus.rotation, t);
            cinematicCamera.fieldOfView = Mathf.Lerp(58f, 42f, t);
            if (!opened && elapsed >= doorOpenTiming)
            {
                opened = true;
                whiteBeyond.SetActive(true);
                level.OpenDoorForCinematic();
            }
            yield return null;
        }
        if (!opened)
        {
            whiteBeyond.SetActive(true);
            level.OpenDoorForCinematic();
        }
        yield return WaitOrSkip(openedDoorHoldTime);
        if (skipRequested) { FinishClearImmediately(level, whiteBeyond); yield break; }
        yield return MoveIntoDoorAndFade(center, focus.rotation);
        level.TryCompleteThreeDimensionalGoal(haruMovement);
        EndSkippableCinematic();
        Debug.Log("Stage Clear Complete", level);
    }

    private IEnumerator MoveIntoDoorAndFade(Vector3 doorCenter, Quaternion doorRotation)
    {
        Vector3 start = cinematicCamera.transform.position;
        Vector3 front = (start - doorCenter).normalized;
        Vector3 target = doorCenter + front * 0.35f;
        float startFov = cinematicCamera.fieldOfView;
        float elapsed = 0f;
        fadeGroup.blocksRaycasts = true;
        while (elapsed < fadeDuration)
        {
            if (skipRequested) break;
            elapsed += Time.unscaledDeltaTime;
            float linear = Mathf.Clamp01(elapsed / fadeDuration);
            float t = Mathf.SmoothStep(0f, 1f, linear);
            cinematicCamera.transform.position = Vector3.Lerp(start, target, t);
            cinematicCamera.transform.rotation = doorRotation;
            cinematicCamera.fieldOfView = Mathf.Lerp(startFov, 32f, t);
            fadeGroup.alpha = linear;
            yield return null;
        }
        cinematicCamera.transform.position = target;
        cinematicCamera.fieldOfView = 32f;
        fadeGroup.alpha = 1f;
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

    private void FinishClearImmediately(CornerRoomLevel level, GameObject whiteBeyond)
    {
        whiteBeyond.SetActive(true);
        level.OpenDoorForCinematic();
        fadeGroup.alpha = 1f;
        fadeGroup.blocksRaycasts = true;
        level.TryCompleteThreeDimensionalGoal(haruMovement);
        EndSkippableCinematic();
        Debug.Log("Stage Clear Complete (cinematic skipped)", level);
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

    private Vector3 DoorFrontPosition(CornerRoomLevel level, float distance)
    {
        Vector3 center = DoorCenter(level);
        Vector3 normal = level.surfaces[4].Normal.normalized;
        if (Vector3.Dot(haruMovement.transform.position - center, normal) < 0f) normal = -normal;
        return center + normal * distance;
    }

    private GameObject BuildWhiteBeyondDoor(Vector3 doorCenter, Vector3 cameraPosition)
    {
        Vector3 front = (cameraPosition - doorCenter).normalized;
        GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backdrop.name = "DoorBlackBeyond";
        backdrop.transform.SetParent(transform, false);
        backdrop.transform.SetPositionAndRotation(doorCenter - front * 2.5f, Quaternion.LookRotation(front));
        backdrop.transform.localScale = new Vector3(20f, 20f, 0.2f);
        Collider backdropCollider = backdrop.GetComponent<Collider>();
        if (backdropCollider != null) Destroy(backdropCollider);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        Material blackMaterial = new Material(shader) { name = "Door Beyond Black", color = Color.black };
        if (blackMaterial.HasProperty("_BaseColor")) blackMaterial.SetColor("_BaseColor", Color.black);
        backdrop.GetComponent<Renderer>().material = blackMaterial;

        GameObject centralLight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        centralLight.name = "Central White Light - 90 Percent";
        centralLight.transform.SetParent(backdrop.transform, true);
        centralLight.transform.SetPositionAndRotation(doorCenter - front * 2.2f, Quaternion.LookRotation(front));
        centralLight.transform.localScale = new Vector3(0.9f, 0.9f, 0.25f);
        Collider lightCollider = centralLight.GetComponent<Collider>();
        if (lightCollider != null) lightCollider.enabled = false;

        const int textureSize = 256;
        Texture2D glowTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGB24, false);
        glowTexture.name = "Door Central White Light";
        glowTexture.filterMode = FilterMode.Bilinear;
        glowTexture.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[textureSize * textureSize];
        for (int y = 0; y < textureSize; y++)
        for (int x = 0; x < textureSize; x++)
        {
            float dx = (x + 0.5f) / textureSize * 2f - 1f;
            float dy = (y + 0.5f) / textureSize * 2f - 1f;
            float radius = Mathf.Sqrt(dx * dx + dy * dy);
            float brightness = 1f - Mathf.SmoothStep(0.78f, 0.98f, radius);
            pixels[y * textureSize + x] = Color.white * brightness;
        }
        glowTexture.SetPixels(pixels);
        glowTexture.Apply();

        shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        Material material = new Material(shader) { name = "Black Door With Central White Light", color = Color.white };
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", glowTexture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", glowTexture);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        centralLight.GetComponent<Renderer>().material = material;

        GameObject doorLightObject = new GameObject("Doorway White Light", typeof(Light));
        doorLightObject.transform.SetParent(backdrop.transform, true);
        doorLightObject.transform.position = doorCenter + front * 0.25f;
        Light doorLight = doorLightObject.GetComponent<Light>();
        doorLight.type = LightType.Point;
        doorLight.color = Color.white;
        doorLight.intensity = 16f;
        doorLight.range = 9f;
        doorLight.shadows = LightShadows.None;
        backdrop.SetActive(false);
        return backdrop;
    }
}
