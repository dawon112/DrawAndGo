using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Opens the settings popup with Esc in every stage. The microphone's state is shown by
// the mic button on the left of the screen (CoopCommsHud).
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
        if (!StageSequence.Load().IsStage(scene.name) || FindFirstObjectByType<InGameSettingsMenu>() != null)
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
}
