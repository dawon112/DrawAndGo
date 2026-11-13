using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// After the last stage is cleared: a party screen (play time on the left, Haru and Dudu
// dancing on the right). Either player pressing "랭킹 등록" opens the name entry on both
// screens; each writes their own name, the host (Haru's PC) files the team's record once, and
// both show the same ranking. Nobody is sent to the title until they choose to go.
// Offline (one PC) both names are written on that PC. Lives on StageFlow's object.
[DisallowMultipleComponent]
public sealed class ExhibitionRankingFlow : MonoBehaviour
{
    private const string NameMessage = "RankNameV1";
    private const string StateMessage = "RankStateV2";
    private const string OpenMessage = "RankOpenV1";
    private const string LeaveMessage = "RankLeaveV1";
    private const float StateInterval = 0.3f;
    private const float NameResendInterval = 0.6f;

    private enum Phase { Idle, Celebrate, Entering, Waiting, Result, Lost }

    [Serializable]
    private sealed class State
    {
        public bool open, haru, dudu, done;
        public string id;
        public int ourRank;
        public ExhibitionRanking.Record ours;
        public List<ExhibitionRanking.Record> top = new List<ExhibitionRanking.Record>();
    }

    public static ExhibitionRankingFlow Instance { get; private set; }

    // Hotkeys elsewhere (quick chat) stay quiet while a name is being typed.
    public static bool IsTyping => Instance != null && Instance.ui != null && Instance.ui.IsTyping;
    // The clear and ranking screens are up: no in-game settings popup behind them.
    public static bool IsShowing => Instance != null && Instance.phase != Phase.Idle;

    private NetworkManager manager;
    private ExhibitionRankingUI ui;
    private CelebrationStage stage;
    private Phase phase;
    private double clearTime;
    private bool isHost;
    private bool networked;
    private bool leaving;
    private State shownResult;

    // Host side: what has been received and filed.
    private string recordId;
    private string haruName;
    private string duduName;
    private bool registered;
    private State hostState;
    private float nextStateSend;

    // Client side.
    private string myName;
    private float nextNameSend;
    private State latest;

    private void Awake()
    {
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Unregister();
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Back at the title (or a new run): a clean slate for the next team.
        if (scene.name != StageSequence.Load().titleScene && phase == Phase.Idle) return;
        ResetRun();
    }

    private void ResetRun()
    {
        StopAllCoroutines();
        phase = Phase.Idle;
        haruName = duduName = myName = recordId = null;
        registered = leaving = false;
        hostState = latest = shownResult = null;
        if (ui != null) Destroy(ui.gameObject);
        ui = null;
        stage = null;
    }

    // ---- The party --------------------------------------------------------------------

    // Called by StageFlow on both PCs once the last stage is cleared. seconds is the host's
    // shared play time, the same on both screens and in the record.
    public void Begin(double seconds, Action hideClearScreen)
    {
        if (phase != Phase.Idle) return;
        clearTime = seconds;
        networked = GameSession.Current != null && manager != null;
        isHost = !networked || GameSession.Current.IsHost;
        if (isHost)
        {
            recordId = Guid.NewGuid().ToString("N");
            hostState = new State { id = recordId };
        }
        phase = Phase.Celebrate;
        EnsureUi();
        hideClearScreen?.Invoke();
        stage = CelebrationStage.Build(ui.CelebrationWidth);
        ui.ShowCelebration(seconds, () => OpenRanking(true), LeaveFromParty);
        ui.SetRankingButton("랭킹 등록", true);
        // The other player may already have opened it.
        if (!isHost && latest != null) ApplyState(latest);
    }

    private void EnsureUi()
    {
        if (ui != null) return;
        ExhibitionRankingUI prefab = Resources.Load<ExhibitionRankingUI>("UI/ExhibitionRanking");
        // A canvas of its own (StageFlow's object is a canvas too), kept across scene loads.
        ui = Instantiate(prefab);
        DontDestroyOnLoad(ui.gameObject);
    }

    // Either player's button opens the name entry on both screens.
    private void OpenRanking(bool pressedHere)
    {
        if (phase == Phase.Result || (phase != Phase.Celebrate && shownResult != null))
        {
            // Already filed: the button shows the result again.
            ShowResult(shownResult ?? latest ?? hostState);
            return;
        }
        if (phase != Phase.Celebrate) return;
        phase = Phase.Entering;
        ui.SetRankingButton("랭킹 등록 중...", false);
        ui.ShowNameEntry(isHost ? ExhibitionRankingUI.Role.Haru : ExhibitionRankingUI.Role.Dudu, OnLocalName);
        if (isHost && hostState != null)
        {
            hostState.open = true;
            if (networked) SendState();
        }
        else if (pressedHere && networked) SendSignal(OpenMessage);
    }

    private void LeaveFromParty()
    {
        if (leaving) return;
        leaving = true;
        StartCoroutine(Leave());
    }

    private IEnumerator Leave()
    {
        // Tell the friend first, so their screen says why the room is closing.
        if (networked && phase != Phase.Result)
        {
            SendSignal(LeaveMessage);
            yield return new WaitForSecondsRealtime(0.3f);
        }
        if (GameSession.Current != null) GameSession.Current.ExpectEnd();
        LeaveToTitle();
    }

    private void LeaveToTitle()
    {
        if (GameSession.Current != null) GameSession.Current.ReturnToTitle();
        else SceneManager.LoadSceneAsync(StageSequence.Load().titleScene);
    }

    // ---- Names ------------------------------------------------------------------------

    private void OnLocalName(string name)
    {
        if (isHost && !networked)
        {
            // One PC: Haru's name first, then the page turns for Dudu's.
            if (haruName == null)
            {
                haruName = name;
                ui.ShowNameEntry(ExhibitionRankingUI.Role.Dudu, OnLocalName);
                return;
            }
            duduName = name;
            Evaluate();
            return;
        }
        if (isHost)
        {
            if (haruName != null) return;
            haruName = name;
            Evaluate();
            return;
        }
        if (myName != null) return;
        myName = name;
        phase = Phase.Waiting;
        ui.ShowWaiting(latest != null && latest.haru, true);
        SendName();
    }

    // Host only: when both names are in, the team's record is filed exactly once.
    private void Evaluate()
    {
        hostState.haru = haruName != null;
        hostState.dudu = duduName != null;
        if (hostState.haru && hostState.dudu && !registered)
        {
            registered = true;
            ExhibitionRanking.Record record = new ExhibitionRanking.Record
            {
                id = recordId,
                haruName = haruName,
                duduName = duduName,
                clearTime = clearTime,
                recordedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            hostState.ourRank = ExhibitionRanking.Add(record);
            hostState.ours = record;
            hostState.top = ExhibitionRanking.Top();
            hostState.done = true;
            Debug.Log($"Exhibition ranking: {haruName} & {duduName} {ExhibitionRanking.FormatTime(clearTime)} filed at #{hostState.ourRank}.");
        }
        if (networked) SendState();
        if (hostState.done) ShowResult(hostState);
        else if (hostState.haru && phase == Phase.Entering)
        {
            phase = Phase.Waiting;
            ui.ShowWaiting(true, hostState.dudu);
        }
        else if (phase == Phase.Waiting) ui.ShowWaiting(hostState.haru, hostState.dudu);
    }

    private void ShowResult(State state)
    {
        if (state == null || ui == null) return;
        shownResult = state;
        phase = Phase.Result;
        // The ranking is complete: the room may close now, by either player's choice.
        if (GameSession.Current != null)
        {
            GameSession.Current.StayOnEnd = true;
            GameSession.Current.ExpectEnd();
        }
        ui.SetRankingButton("랭킹 보기", true);
        ui.ShowResult(new ExhibitionRankingUI.Result
        {
            top = state.top,
            ourId = state.id,
            ours = state.ours,
            ourRank = state.ourRank
        }, "처음 화면으로", LeaveToTitle, ui.ClosePopups);
    }

    // ---- Lost friend ------------------------------------------------------------------

    // GameSession asks this before its usual notice: before the record is filed, the ranking
    // screen says it itself and waits for a click.
    public static bool TakeOverLostConnection()
    {
        if (Instance == null || Instance.phase == Phase.Idle || Instance.phase == Phase.Result) return false;
        if (Instance.phase != Phase.Lost)
            Instance.ShowLost("상대 플레이어와 연결이 끊어졌어요.\n이번 기록은 등록되지 않았어요.");
        return true;
    }

    private void ShowLost(string message)
    {
        phase = Phase.Lost;
        EnsureUi();
        StageCinematicController cinematic = FindAnyObjectByType<StageCinematicController>();
        if (cinematic != null) cinematic.ClearCenterMessage();
        ui.SetRankingButton("랭킹 등록", false);
        ui.ShowNotice(message, "처음 화면으로", LeaveToTitle);
    }

    private void Update()
    {
        RegisterNetworkIfNeeded();
        if (manager == null || !networked) return;
        float now = Time.unscaledTime;
        if (isHost && phase != Phase.Idle && phase != Phase.Lost && now >= nextStateSend) SendState();
        if (!isHost && myName != null && (latest == null || !latest.dudu) && now >= nextNameSend) SendName();
        if (phase == Phase.Result && GameSession.Current != null && GameSession.Current.PeerLeft)
            ui.SetResultNote("친구가 처음 화면으로 돌아갔어요.");
    }

    // ---- Network ----------------------------------------------------------------------

    private void RegisterNetworkIfNeeded()
    {
        NetworkManager active = NetworkManager.Singleton;
        if (active == null || !active.IsConnectedClient || active.CustomMessagingManager == null) active = null;
        if (manager == active) return;
        Unregister();
        manager = active;
        if (manager == null) return;
        manager.CustomMessagingManager.RegisterNamedMessageHandler(NameMessage, OnName);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, OnState);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(OpenMessage, OnOpen);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(LeaveMessage, OnLeave);
    }

    private void Unregister()
    {
        if (manager != null && manager.CustomMessagingManager != null)
        {
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(NameMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(StateMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(OpenMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(LeaveMessage);
        }
        manager = null;
    }

    private bool FromPeer(ulong sender) => manager != null && (manager.IsHost
        ? sender != NetworkManager.ServerClientId && manager.ConnectedClients.ContainsKey(sender)
        : sender == NetworkManager.ServerClientId);

    private void SendSignal(string message)
    {
        if (manager == null) return;
        using var writer = new FastBufferWriter(1, Allocator.Temp);
        writer.WriteValueSafe((byte)1);
        if (!manager.IsHost)
        {
            manager.CustomMessagingManager.SendNamedMessage(message, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            return;
        }
        foreach (ulong clientId in manager.ConnectedClientsIds)
            if (clientId != manager.LocalClientId)
                manager.CustomMessagingManager.SendNamedMessage(message, clientId, writer, NetworkDelivery.ReliableSequenced);
    }

    // Host: Dudu's player pressed "랭킹 등록". The state then opens it on Dudu's screen too.
    private void OnOpen(ulong sender, FastBufferReader reader)
    {
        if (!FromPeer(sender) || !manager.IsHost) return;
        OpenRanking(false);
    }

    private void OnLeave(ulong sender, FastBufferReader reader)
    {
        if (!FromPeer(sender) || phase == Phase.Idle || phase == Phase.Result || phase == Phase.Lost) return;
        ShowLost("친구가 처음 화면으로 돌아갔어요.\n이번 기록은 등록되지 않았어요.");
    }

    private void SendName()
    {
        if (manager == null || manager.IsHost) return;
        nextNameSend = Time.unscaledTime + NameResendInterval;
        using var writer = new FastBufferWriter(8 + myName.Length * 2 + 8, Allocator.Temp);
        writer.WriteValueSafe(myName);
        manager.CustomMessagingManager.SendNamedMessage(NameMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
    }

    private void OnName(ulong sender, FastBufferReader reader)
    {
        if (manager == null || !manager.IsHost || !FromPeer(sender)) return;
        reader.ReadValueSafe(out string received);
        // Repeats (the client resends until it hears back) and late copies change nothing.
        if (phase == Phase.Idle || phase == Phase.Lost || duduName != null || hostState == null) return;
        string name = ExhibitionRanking.CleanName(received);
        if (name.Length == 0) return;
        duduName = name;
        Evaluate();
    }

    private void SendState()
    {
        if (manager == null || !manager.IsHost || hostState == null) return;
        nextStateSend = Time.unscaledTime + StateInterval;
        string json = JsonUtility.ToJson(hostState);
        using var writer = new FastBufferWriter(8 + json.Length * 2 + 8, Allocator.Temp);
        writer.WriteValueSafe(json);
        foreach (ulong clientId in manager.ConnectedClientsIds)
            if (clientId != manager.LocalClientId)
                manager.CustomMessagingManager.SendNamedMessage(StateMessage, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void OnState(ulong sender, FastBufferReader reader)
    {
        if (manager == null || manager.IsHost || !FromPeer(sender)) return;
        reader.ReadValueSafe(out string json);
        State state = JsonUtility.FromJson<State>(json);
        if (state == null) return;
        latest = state;
        ApplyState(state);
    }

    private void ApplyState(State state)
    {
        if (phase == Phase.Idle || phase == Phase.Lost || ui == null) return;
        if (state.done)
        {
            if (phase != Phase.Result) ShowResult(state);
            return;
        }
        if (state.open && phase == Phase.Celebrate) OpenRanking(false);
        if (phase == Phase.Waiting) ui.ShowWaiting(state.haru, true);
    }
}
