using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CoopQuickChat : MonoBehaviour
{
    private const string MessageName = "CoopQuickChatV1";
    private const float MessageDuration = 3.5f;
    private const float SendCooldown = 0.75f;

    private static readonly string[] Phrases =
    {
        "👋 이쪽이야!", "⚠️ 조심해!", "✏️ 여기에 그려줘!", "⏳ 잠깐만!",
        "▶️ 지금이야!", "👍 준비됐어!", "🙏 고마워!", "😵 미안!"
    };

    private static readonly Key[] ChoiceKeys =
    {
        Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
        Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8
    };

    private NetworkManager manager;
    private GameObject menu;
    private Text messageText;
    private float hideMessageAt;
    private float nextSendAt;

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
        if (scene.name != GameSession.MapSceneName || FindFirstObjectByType<CoopQuickChat>() != null)
            return;

        GameObject root = new GameObject("Quick Chat", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 101;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<CoopQuickChat>();
    }

    private void Awake()
    {
        BuildUi();
    }

    private void Update()
    {
        RegisterNetworkIfNeeded();

        if (messageText.gameObject.activeSelf && Time.unscaledTime >= hideMessageAt)
            messageText.gameObject.SetActive(false);

        if (SettingsPopupController.IsAnyOpen)
        {
            menu.SetActive(false);
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.cKey.wasPressedThisFrame)
        {
            menu.SetActive(!menu.activeSelf);
            return;
        }

        if (!menu.activeSelf) return;
        for (int i = 0; i < ChoiceKeys.Length; i++)
        {
            if (!keyboard[ChoiceKeys[i]].wasPressedThisFrame) continue;
            Send(i);
            menu.SetActive(false);
            break;
        }
    }

    private void RegisterNetworkIfNeeded()
    {
        NetworkManager active = NetworkManager.Singleton;
        if (active == null || !active.IsConnectedClient) active = null;
        if (manager == active) return;

        if (manager != null && manager.CustomMessagingManager != null)
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(MessageName);
        manager = active;
        if (manager != null && manager.CustomMessagingManager != null)
            manager.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, Receive);
    }

    private void Send(int index)
    {
        if (Time.unscaledTime < nextSendAt) return;
        nextSendAt = Time.unscaledTime + SendCooldown;
        Show(index, true);
        if (manager == null) return;

        using var writer = new FastBufferWriter(1, Allocator.Temp);
        writer.WriteValueSafe((byte)index);
        if (!manager.IsHost)
        {
            manager.CustomMessagingManager.SendNamedMessage(
                MessageName, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            return;
        }

        foreach (ulong clientId in manager.ConnectedClientsIds)
            if (clientId != manager.LocalClientId)
                manager.CustomMessagingManager.SendNamedMessage(
                    MessageName, clientId, writer, NetworkDelivery.ReliableSequenced);
    }

    private void Receive(ulong sender, FastBufferReader reader)
    {
        if (manager == null || !reader.TryBeginRead(1)) return;
        if (manager.IsHost)
        {
            if (sender == manager.LocalClientId || !manager.ConnectedClients.ContainsKey(sender)) return;
        }
        else if (sender != NetworkManager.ServerClientId) return;

        reader.ReadValueSafe(out byte index);
        if (index >= Phrases.Length) return;
        Show(index, false);
    }

    private void Show(int index, bool local)
    {
        bool fromHaru = manager != null && (local == manager.IsHost);
        string speaker = manager == null ? "나" : fromHaru ? "하루" : "두두";
        messageText.text = speaker + ": " + Phrases[index];
        messageText.gameObject.SetActive(true);
        hideMessageAt = Time.unscaledTime + MessageDuration;
    }

    private void BuildUi()
    {
        GameObject panel = new GameObject("Quick Chat Choices", typeof(RectTransform), typeof(RawImage));
        panel.transform.SetParent(transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot = new Vector2(1f, 0f);
        panelRect.anchoredPosition = new Vector2(-35f, 35f);
        panelRect.sizeDelta = new Vector2(510f, 460f);
        RawImage background = panel.GetComponent<RawImage>();
        background.texture = Texture2D.whiteTexture;
        background.color = new Color(1f, 0.97f, 0.88f, 0.94f);
        background.raycastTarget = false;

        Text choices = CreateText("Choices", panel.transform, 30, TextAnchor.UpperLeft);
        RectTransform choicesRect = choices.rectTransform;
        choicesRect.anchorMin = Vector2.zero;
        choicesRect.anchorMax = Vector2.one;
        choicesRect.offsetMin = new Vector2(25f, 15f);
        choicesRect.offsetMax = new Vector2(-15f, -15f);
        choices.color = new Color(0.69f, 0.34f, 0.14f);
        choices.text = "퀵채팅  C 닫기\n\n" +
            "1  " + Phrases[0] + "\n2  " + Phrases[1] + "\n" +
            "3  " + Phrases[2] + "\n4  " + Phrases[3] + "\n" +
            "5  " + Phrases[4] + "\n6  " + Phrases[5] + "\n" +
            "7  " + Phrases[6] + "\n8  " + Phrases[7];
        menu = panel;
        menu.SetActive(false);

        messageText = CreateText("Quick Chat Message", transform, 38, TextAnchor.MiddleCenter);
        RectTransform messageRect = messageText.rectTransform;
        messageRect.anchorMin = messageRect.anchorMax = new Vector2(0.5f, 1f);
        messageRect.pivot = new Vector2(0.5f, 1f);
        messageRect.anchoredPosition = new Vector2(0f, -140f);
        messageRect.sizeDelta = new Vector2(1000f, 70f);
        Outline outline = messageText.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.17f, 0.13f, 0.1f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);
        messageText.gameObject.SetActive(false);
    }

    private static Text CreateText(string name, Transform parent, int size, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = GameFont.Bold;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private void OnDestroy()
    {
        if (manager != null && manager.CustomMessagingManager != null)
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(MessageName);
    }
}
