using TMPro;
using Unity.Netcode;
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

        GameObject instance = Instantiate(prefab, transform, false);
        popup = instance.GetComponent<SettingsPopupController>();
        if (popup == null)
        {
            Debug.LogError("SettingsPopupController is missing from the prefab.", this);
            enabled = false;
            return;
        }

        foreach (string pageName in new[]
        {
            "SoundPage", "DisplayPage", "ControlSelectPage", "HaruControlsPage", "DuduControlsPage"
        })
        {
            Transform page = instance.transform.Find(pageName);
            if (page != null) AddLeaveButton(page);
        }
    }

    private void LateUpdate()
    {
        Keyboard keyboard = Keyboard.current;
        if (popup == null || popup.gameObject.activeSelf || keyboard == null ||
            !keyboard.escapeKey.wasPressedThisFrame ||
            SettingsPopupController.LastClosedFrame == Time.frameCount) return;

        popup.Open();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void AddLeaveButton(Transform page)
    {
        GameObject holder = new GameObject("LeaveRoomButton", typeof(RectTransform),
            typeof(Image), typeof(Button));
        holder.transform.SetParent(page, false);
        RectTransform rect = holder.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -275f);
        rect.sizeDelta = new Vector2(240f, 58f);

        Image image = holder.GetComponent<Image>();
        image.color = Color.clear;
        Button button = holder.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(LeaveRoom);

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(holder.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        TMP_Text reference = page.GetComponentInChildren<TMP_Text>(true);
        if (reference != null) label.font = reference.font;
        label.text = "방 나가기";
        label.fontSize = 30f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.76f, 0.40f, 0.18f);
        label.raycastTarget = false;
    }

    private void LeaveRoom()
    {
        if (GameSession.Current != null)
        {
            GameSession.Current.LeaveToTitle();
            return;
        }

        if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene("TitleScene");
    }
}
