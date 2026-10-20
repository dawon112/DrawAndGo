using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Moves both players through StageSequence. Only the host changes scenes and tells the
// client which one to load. Restarting a stage or skipping the tutorial needs both
// players to agree; offline (no session) a single key press is enough.
[DisallowMultipleComponent]
public sealed class StageFlow : MonoBehaviour
{
    public enum VoteKind : byte { SkipTutorial, RestartStage }

    private const string LoadMessage = "FlowLoadV1";
    private const string VoteMessage = "FlowVoteV1";
    private const float VoteTimeout = 20f;
    private const Key SkipTutorialKey = Key.K;
    private const Key RestartStageKey = Key.R;
    private const int VoteCount = 2;

    public static StageFlow Instance { get; private set; }

    private StageSequence sequence;
    private NetworkManager manager;
    private readonly bool[] localVotes = new bool[VoteCount];
    private readonly bool[] remoteVotes = new bool[VoteCount];
    private readonly float[] localVoteTime = new float[VoteCount];
    private bool loading;
    private GameObject panel;
    private Text panelText;

    private bool IsNetworked => GameSession.Current != null && manager != null && manager.IsConnectedClient;
    private bool IsAuthority => GameSession.Current == null || GameSession.Current.IsHost;
    private string ActiveScene => SceneManager.GetActiveScene().name;
    private bool InStage => sequence.IsStage(ActiveScene);
    private bool InTutorial => ActiveScene == sequence.tutorialScene;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (Instance != null) return;
        GameObject holder = new GameObject("Stage Flow", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        DontDestroyOnLoad(holder);
        holder.AddComponent<StageFlow>();
    }

    private void Awake()
    {
        Instance = this;
        sequence = StageSequence.Load();
        BuildUi();
        SceneManager.sceneLoaded += OnSceneLoaded;
        RefreshUi();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnregisterNetwork();
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        loading = false;
        ClearVotes();
    }

    // Called when a stage's clear cinematic has finished on this machine.
    public void CompleteCurrentStage()
    {
        if (!IsAuthority || loading) return;
        string next = sequence.NextAfter(ActiveScene);
        StartCoroutine(AfterDelay(() =>
        {
            if (next != null) LoadStage(next);
            else FinishGame();
        }));
    }

    private IEnumerator AfterDelay(System.Action action)
    {
        loading = true;
        yield return new WaitForSecondsRealtime(sequence.nextStageDelay);
        loading = false;
        action();
    }

    private void FinishGame()
    {
        if (GameSession.Current != null) GameSession.Current.ReturnToTitle();
        else SceneManager.LoadSceneAsync(sequence.titleScene);
    }

    private void LoadStage(string sceneName)
    {
        if (loading || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            if (!loading) Debug.LogError($"Stage '{sceneName}' is not in Build Settings.", this);
            return;
        }
        loading = true;
        ClearVotes();
        if (IsNetworked)
        {
            using var writer = new FastBufferWriter(sizeof(int) + sceneName.Length * 2 + 8, Allocator.Temp);
            writer.WriteValueSafe(sceneName);
            foreach (ulong clientId in manager.ConnectedClientsIds)
                if (clientId != manager.LocalClientId)
                    manager.CustomMessagingManager.SendNamedMessage(
                        LoadMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
        }
        SceneManager.LoadSceneAsync(sceneName);
    }

    private void Update()
    {
        RegisterNetworkIfNeeded();
        for (int i = 0; i < VoteCount; i++)
            if (localVotes[i] && Time.unscaledTime - localVoteTime[i] > VoteTimeout)
                SetLocalVote((VoteKind)i, false);

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && InStage && !loading && !SettingsPopupController.IsAnyOpen)
        {
            if (InTutorial && keyboard[SkipTutorialKey].wasPressedThisFrame)
                SetLocalVote(VoteKind.SkipTutorial, !localVotes[(int)VoteKind.SkipTutorial]);
            if (keyboard[RestartStageKey].wasPressedThisFrame)
                SetLocalVote(VoteKind.RestartStage, !localVotes[(int)VoteKind.RestartStage]);
        }
        RefreshUi();
    }

    private void SetLocalVote(VoteKind kind, bool agree)
    {
        int index = (int)kind;
        if (localVotes[index] == agree) return;
        localVotes[index] = agree;
        localVoteTime[index] = Time.unscaledTime;
        if (IsNetworked) SendVote(kind, agree);
        EvaluateVotes();
    }

    private void EvaluateVotes()
    {
        if (!IsAuthority || loading) return;
        for (int i = 0; i < VoteCount; i++)
        {
            if (!localVotes[i] || (IsNetworked && !remoteVotes[i])) continue;
            if ((VoteKind)i == VoteKind.SkipTutorial)
            {
                if (!InTutorial) continue;
                string next = sequence.NextAfter(ActiveScene);
                if (next != null) LoadStage(next);
                else FinishGame();
            }
            else LoadStage(ActiveScene);
            return;
        }
    }

    private void ClearVotes()
    {
        for (int i = 0; i < VoteCount; i++) localVotes[i] = remoteVotes[i] = false;
    }

    private void SendVote(VoteKind kind, bool agree)
    {
        using var writer = new FastBufferWriter(2, Allocator.Temp);
        writer.WriteValueSafe((byte)kind);
        writer.WriteValueSafe(agree);
        if (!manager.IsHost)
        {
            manager.CustomMessagingManager.SendNamedMessage(
                VoteMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            return;
        }
        foreach (ulong clientId in manager.ConnectedClientsIds)
            if (clientId != manager.LocalClientId)
                manager.CustomMessagingManager.SendNamedMessage(
                    VoteMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
    }

    private bool ValidSender(ulong sender) => manager.IsHost
        ? sender != NetworkManager.ServerClientId && manager.ConnectedClients.ContainsKey(sender)
        : sender == NetworkManager.ServerClientId;

    private void OnVote(ulong sender, FastBufferReader reader)
    {
        if (!ValidSender(sender) || !reader.TryBeginRead(2)) return;
        reader.ReadValueSafe(out byte kind);
        reader.ReadValueSafe(out bool agree);
        if (kind >= VoteCount) return;
        remoteVotes[kind] = agree;
        EvaluateVotes();
    }

    private void OnLoad(ulong sender, FastBufferReader reader)
    {
        if (manager.IsHost || sender != NetworkManager.ServerClientId) return;
        reader.ReadValueSafe(out string sceneName);
        // Accept only scenes the game itself plays; never an arbitrary name.
        if (!sequence.IsStage(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName)) return;
        loading = true;
        ClearVotes();
        SceneManager.LoadSceneAsync(sceneName);
    }

    private void RegisterNetworkIfNeeded()
    {
        NetworkManager active = NetworkManager.Singleton;
        if (active == null || !active.IsConnectedClient || active.CustomMessagingManager == null) active = null;
        if (manager == active) return;
        UnregisterNetwork();
        manager = active;
        if (manager == null) return;
        manager.CustomMessagingManager.RegisterNamedMessageHandler(LoadMessage, OnLoad);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(VoteMessage, OnVote);
    }

    private void UnregisterNetwork()
    {
        if (manager != null && manager.CustomMessagingManager != null)
        {
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(LoadMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(VoteMessage);
        }
        manager = null;
    }

    private void BuildUi()
    {
        Canvas canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        panel = new GameObject("Vote Panel", typeof(RectTransform), typeof(Image));
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-24f, 24f);
        rect.sizeDelta = new Vector2(470f, 86f);
        Image background = panel.GetComponent<Image>();
        background.color = new Color(0.055f, 0.08f, 0.12f, 0.6f);
        background.raycastTarget = false;

        GameObject label = new GameObject("Vote Text", typeof(RectTransform), typeof(Text), typeof(Outline));
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(16f, 6f);
        labelRect.offsetMax = new Vector2(-16f, -6f);
        panelText = label.GetComponent<Text>();
        panelText.font = GameFont.Bold;
        panelText.fontSize = 22;
        panelText.alignment = TextAnchor.MiddleLeft;
        panelText.color = Color.white;
        panelText.supportRichText = true;
        panelText.raycastTarget = false;
    }

    private void RefreshUi()
    {
        bool visible = InStage && !loading;
        if (panel.activeSelf != visible) panel.SetActive(visible);
        if (!visible) return;
        string text = InTutorial ? VoteLine("K", "튜토리얼 건너뛰기", VoteKind.SkipTutorial) + "\n" : string.Empty;
        text += VoteLine("R", "처음부터 다시", VoteKind.RestartStage);
        panelText.text = text;
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(470f, InTutorial ? 86f : 50f);
    }

    private string VoteLine(string key, string label, VoteKind kind)
    {
        bool mine = localVotes[(int)kind];
        if (!IsNetworked) return $"[{key}] {label}";
        bool theirs = remoteVotes[(int)kind];
        if (theirs && !mine) return $"<color=#FFD54A>[{key}] {label} - 친구가 원해요!</color>";
        string state = mine ? (theirs ? "둘 다 동의" : "친구 동의 기다리는 중") : "둘 다 동의하면 진행";
        return $"[{key}] {label}  <size=18>({state})</size>";
    }
}
