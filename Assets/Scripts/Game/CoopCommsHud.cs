using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The mic and quick-chat buttons on the left of every stage. V or a click turns the
// microphone on and off (an X marks it off); C or a click opens the quick chat.
// The layout lives in Resources/UI/CoopCommsHud.prefab, so it can be edited in the editor.
[DisallowMultipleComponent]
public sealed class CoopCommsHud : MonoBehaviour
{
    [SerializeField] private Button micButton;
    [SerializeField] private GameObject micOffMark;
    [SerializeField] private Button quickChatButton;
    [SerializeField] private GameObject quickChatLabel;

    private Canvas canvas;
    private GameViewManager view;
    private CoopQuickChat quickChat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Setup(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Setup(scene);

    private static void Setup(Scene scene)
    {
        if (!StageSequence.Load().IsStage(scene.name) || FindAnyObjectByType<CoopCommsHud>() != null)
            return;
        GameObject prefab = Resources.Load<GameObject>("UI/CoopCommsHud");
        if (prefab == null)
        {
            Debug.LogError("Resources/UI/CoopCommsHud prefab is missing.");
            return;
        }
        Instantiate(prefab).name = prefab.name;
    }

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        micButton.onClick.AddListener(() => { ToggleMicrophone(); Deselect(); });
        quickChatButton.onClick.AddListener(() => { if (quickChat != null) quickChat.ToggleMenu(); Deselect(); });
    }

    private void Update()
    {
        if (view == null) view = FindAnyObjectByType<GameViewManager>();
        // Quick chat exists only in online play.
        if (quickChat == null) quickChat = FindAnyObjectByType<CoopQuickChat>();
        SetActiveIfChanged(quickChatButton.gameObject, quickChat != null);
        SetActiveIfChanged(quickChatLabel, quickChat != null);

        // Out of the way during intro, clear and door cinematics.
        bool inPlay = view == null || view.CurrentCameraState == GameViewManager.CameraState.Gameplay;
        if (canvas != null && canvas.enabled != inPlay) canvas.enabled = inPlay;

        Keyboard keyboard = Keyboard.current;
        if (inPlay && keyboard != null && keyboard.vKey.wasPressedThisFrame && !SettingsPopupController.IsAnyOpen)
            ToggleMicrophone();
        SetActiveIfChanged(micOffMark, !CoopAudioSettings.MicEnabled);
    }

    private static void ToggleMicrophone()
    {
        CoopAudioSettings.MicEnabled = !CoopAudioSettings.MicEnabled;
        CoopAudioSettings.Save();
    }

    // A clicked button stays selected, and Space (Dudu's jump) or Enter would press it again.
    private static void Deselect()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private static void SetActiveIfChanged(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active) target.SetActive(active);
    }
}
