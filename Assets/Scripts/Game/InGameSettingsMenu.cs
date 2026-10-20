using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class InGameSettingsMenu : MonoBehaviour
{
    private SettingsPopupController popup;
    private RawImage microphoneIndicator;
    private TMP_Text microphoneStatusText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Setup(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Setup(scene);
    }

    private static void Setup(Scene scene)
    {
        if (scene.name != GameSession.MapSceneName || FindFirstObjectByType<InGameSettingsMenu>() != null)
            return;

        GameObject root = new GameObject("In-Game Settings", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        root.GetComponent<Canvas>().sortingOrder = 100;
        root.AddComponent<InGameSettingsMenu>();
    }

    private void Awake()
    {
        if (EventSystem.current == null)
        {
            GameObject events = new GameObject("Settings Event System", typeof(EventSystem));
            events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        GameObject prefab = Resources.Load<GameObject>("SettingsPopup");
        if (prefab == null)
        {
            Debug.LogError("SettingsPopup prefab is missing from Resources.", this);
            enabled = false;
            return;
        }

        popup = Instantiate(prefab, transform, false).GetComponent<SettingsPopupController>();
        if (popup == null)
        {
            Debug.LogError("SettingsPopupController is missing from the prefab.", this);
            enabled = false;
            return;
        }

        CreateMicrophoneIndicator();
    }

    private void LateUpdate()
    {
        UpdateMicrophoneIndicator();

        Keyboard keyboard = Keyboard.current;
        if (popup == null || popup.gameObject.activeSelf || keyboard == null ||
            !keyboard.escapeKey.wasPressedThisFrame ||
            SettingsPopupController.LastClosedFrame == Time.frameCount) return;

        popup.Open();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void CreateMicrophoneIndicator()
    {
        GameObject holder = new GameObject("Microphone Status", typeof(RectTransform), typeof(RawImage));
        holder.transform.SetParent(transform, false);
        RectTransform rect = holder.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-20f, -20f);
        rect.sizeDelta = new Vector2(140f, 38f);
        RawImage background = holder.GetComponent<RawImage>();
        background.texture = Texture2D.whiteTexture;
        background.color = new Color(0.12f, 0.12f, 0.12f, 0.75f);
        background.raycastTarget = false;

        GameObject dot = new GameObject("Recording Light", typeof(RectTransform), typeof(RawImage));
        dot.transform.SetParent(holder.transform, false);
        RectTransform dotRect = dot.GetComponent<RectTransform>();
        dotRect.anchorMin = dotRect.anchorMax = new Vector2(0f, 0.5f);
        dotRect.pivot = new Vector2(0f, 0.5f);
        dotRect.anchoredPosition = new Vector2(11f, 0f);
        dotRect.sizeDelta = new Vector2(15f, 15f);
        microphoneIndicator = dot.GetComponent<RawImage>();
        microphoneIndicator.texture = Texture2D.whiteTexture;
        microphoneIndicator.raycastTarget = false;

        GameObject label = new GameObject("MIC Status", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(holder.transform, false);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(34f, 0f);
        labelRect.offsetMax = Vector2.zero;
        microphoneStatusText = label.GetComponent<TMP_Text>();
        Transform micButton = popup.transform.Find("SoundPage/MicRow/MicOnButton");
        TMP_Text existingLabel = micButton != null ? micButton.GetComponentInChildren<TMP_Text>(true) : null;
        if (existingLabel != null) microphoneStatusText.font = existingLabel.font;
        microphoneStatusText.fontSize = 19f;
        microphoneStatusText.alignment = TextAlignmentOptions.MidlineLeft;
        microphoneStatusText.color = Color.white;
        microphoneStatusText.raycastTarget = false;
    }

    private void UpdateMicrophoneIndicator()
    {
        if (microphoneIndicator == null) return;
        if (CoopVoiceChat.IsMicrophoneRecording)
        {
            microphoneIndicator.color = new Color(0.24f, 0.83f, 0.35f);
            microphoneStatusText.text = "MIC ON";
        }
        else if (CoopAudioSettings.MicEnabled)
        {
            microphoneIndicator.color = new Color(1f, 0.72f, 0.25f);
            microphoneStatusText.text = "MIC WAIT";
        }
        else
        {
            microphoneIndicator.color = new Color(0.55f, 0.55f, 0.55f);
            microphoneStatusText.text = "MIC OFF";
        }
    }

}
